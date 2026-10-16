// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static readonly IReadOnlySet<int> EmptyBackingSources = new HashSet<int>();

    private static bool VerifySpanContracts(BoundProgram program)
    {
        var hasSpans = TokenSpanContract.HasMetadata(program);
        if ((hasSpans || program.Structures.Any(static structure =>
                structure.TokenSpanRole is TokenSpanTypeRole.Token or TokenSpanTypeRole.Range))
            && !TokenSpanContract.ValidTokenLayout(program.Structures))
        {
            throw InvalidEffect(default, "Ownership");
        }
        var roles = new HashSet<TokenSpanTypeRole>();
        foreach (var structure in program.Structures)
        {
            var role = hasSpans ? TokenSpanContract.RoleOf(structure) : structure.TokenSpanRole;
            if (role != TokenSpanTypeRole.None
                && (role is < TokenSpanTypeRole.Token or > TokenSpanTypeRole.Snapshot
                    || !roles.Add(role)
                    || role == TokenSpanTypeRole.Snapshot
                        && !TokenSpanContract.ValidSnapshot(structure)))
            {
                throw InvalidEffect(default, "Ownership");
            }
            if (structure.Fields.Any(static field => field.Type == PrimitiveType.TokenSpan)
                && !TokenSpanContract.ValidCursor(structure)
                || structure.Fields.Any(field => TokenSpanContract.IsCursor(field.Type, program.Structures)))
            {
                throw InvalidEffect(default, "Ownership");
            }
        }
        foreach (var function in program.Functions)
        {
            if (function.CapturedParameters.IsDefault || function.ResultDependencyParameters.IsDefault)
            {
                throw Invalid(function.Span);
            }
            var forwardedSpanResult = function.ReturnType == PrimitiveType.TokenSpan
                && function.ReturnsForwardedBacking && !function.ResultDependencyParameters.IsEmpty;
            if (function.ReturnsForwardedBacking != forwardedSpanResult
                || !function.ReturnsForwardedBacking && !function.ResultDependencyParameters.IsEmpty)
            {
                throw InvalidEffect(function.Span, "Ownership");
            }
            var cursorConstructor = function.IsConstructor
                && TokenSpanContract.IsCursor(function.ReturnType, program.Structures);
            if (cursorConstructor)
            {
                if (function.CapturedParameters is not [1] || function.ParameterCount != 1
                    || function.Variables.Length < 2 || function.Variables[1].Type != PrimitiveType.TokenSpan
                    || function.Variables[1].IsOut || function.Variables[1].IsInit
                    || function.IsExternal || function.Intrinsic != BoundIntrinsic.None)
                {
                    throw InvalidEffect(function.Span, "Ownership");
                }
            }
            else if (!function.CapturedParameters.IsEmpty
                || TokenSpanContract.IsDependent(function.ReturnType, program.Structures) && !forwardedSpanResult)
            {
                throw InvalidEffect(function.Span, "Ownership");
            }
            if (forwardedSpanResult)
            {
                if (function.IsConstructor || function.IsExternal || function.Intrinsic != BoundIntrinsic.None
                    || function.DeclaringStruct is not null || !function.IsInternal || function.ParameterCount == 0)
                {
                    throw InvalidEffect(function.Span, "Ownership");
                }
                var seen = new HashSet<int>();
                foreach (var parameter in function.ResultDependencyParameters)
                {
                    if ((uint)parameter >= (uint)function.ParameterCount || !seen.Add(parameter)
                        || function.Variables[parameter] is not {
                            Type: PrimitiveType.TokenSpan,
                            IsOut: false,
                            IsInit: false,
                        })
                    {
                        throw InvalidEffect(function.Span, "Ownership");
                    }
                }
            }
            if (function.IndexedBody is not null && hasSpans)
            {
                throw InvalidEffect(function.Span, "Ownership");
            }
            for (var slot = 0; slot < function.Variables.Length; slot++)
            {
                var variable = function.Variables[slot];
                if (TokenSpanContract.IsDependent(variable.Type, program.Structures)
                    && (function.IsExternal || function.Intrinsic != BoundIntrinsic.None
                        || variable.IsOut || variable.IsInit || variable.IsInitializationLocal
                        || variable.Type == PrimitiveType.TokenSpan && variable.IsMutable))
                {
                    throw InvalidEffect(variable.Span, "Ownership");
                }
            }
        }
        return hasSpans;
    }

    private sealed class BackingDependencies
    {
        // A slot conservatively retains every source it can hold on any path. Liveness
        // remains path-sensitive in InitializationState, including unwind replay.
        internal HashSet<int>?[] Sources { get; }
        internal HashSet<int>?[] NativePointerSources { get; }
        internal List<IReadOnlySet<int>> Staged { get; } = [];

        internal BackingDependencies(int length)
        {
            Sources = new HashSet<int>?[length];
            NativePointerSources = new HashSet<int>?[length];
        }
    }

    private static BackingDependencies? CreateBackingDependencies(BoundFunction function, BoundProgram program, bool hasSpans)
    {
        if (!hasSpans && program.NativePointerTypes.IsEmpty)
        {
            return null;
        }
        var facts = new BackingDependencies(function.Variables.Length);
        var edges = new List<int>?[function.Variables.Length];
        var nativePointerEdges = new List<int>?[function.Variables.Length];
        var forwardedSpanReturns = function.ReturnsForwardedBacking ? new List<BoundExpression>() : null;
        var nativePointerReturns = new List<BoundExpression>();
        var nativePointerReads = new List<(int Destination, BoundExpression Address)>();
        var nativePointerWrites = new List<(BoundExpression Address, BoundExpression Value)>();
        for (var slot = 0; slot < edges.Length; slot++)
        {
            if (slot < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                && !(slot == 0 && function.IsConstructor)
                && TokenSpanContract.IsDependent(function.Variables[slot].Type, program.Structures))
            {
                // Negative roots denote incoming backing, not the callee's descriptor place.
                AddBackingSource(facts, slot, ~slot);
            }
        }
        var remaining = MaximumAnalysisVisits;

        void AddEdge(int source, int destination)
        {
            var outgoing = edges[source];
            if (outgoing is null)
            {
                outgoing = [];
                edges[source] = outgoing;
            }

            outgoing.Add(destination);
        }

        void AddNativePointerEdge(int source, int destination)
        {
            var outgoing = nativePointerEdges[source];
            if (outgoing is null)
            {
                outgoing = [];
                nativePointerEdges[source] = outgoing;
            }

            outgoing.Add(destination);
        }

        void Source(int destination, BoundExpression expression, int depth)
        {
            Count(ref remaining, depth);
            if (expression is null)
            {
                throw Invalid();
            }
            switch (expression)
            {
                case BoundSnapshotSpan snapshot:
                    if ((uint)snapshot.Variable >= (uint)edges.Length
                        || function.Variables[snapshot.Variable].Type
                            != (PrimitiveType)(100 + TokenSpanContract.Find(program.Structures, TokenSpanContract.SnapshotName)))
                    {
                        throw Invalid(snapshot.Span);
                    }
                    AddBackingSource(facts, destination, snapshot.Variable);
                    break;
                case BoundRead read:
                    if ((uint)read.Variable >= (uint)edges.Length
                        || function.Variables[read.Variable].Type != read.Type
                        || !TokenSpanContract.IsDependent(read.Type, program.Structures))
                    {
                        throw Invalid(read.Span);
                    }
                    AddEdge(read.Variable, destination);
                    break;
                case BoundMove move:
                    if ((uint)move.Variable >= (uint)edges.Length
                        || function.Variables[move.Variable].Type != move.Type
                        || !TokenSpanContract.IsDependent(move.Type, program.Structures))
                    {
                        throw Invalid(move.Span);
                    }
                    AddEdge(move.Variable, destination);
                    break;
                case BoundFieldRead field:
                    Source(destination, field.Receiver, depth + 1);
                    break;
                case BoundSpanAdvance advance:
                    Source(destination, advance.View, depth + 1);
                    break;
                case BoundUnsafeExpression unsafeExpression:
                    Source(destination, unsafeExpression.Expression, depth + 1);
                    break;
                case BoundCall call when (uint)call.Function < (uint)program.Functions.Length
                    && program.Functions[call.Function].IsConstructor
                    && TokenSpanContract.IsCursor(call.Type, program.Structures):
                    var cursorConstructor = program.Functions[call.Function];
                    ValidateSpanCall(call, cursorConstructor);
                    foreach (var capture in program.Functions[call.Function].CapturedParameters)
                    {
                        var parameter = capture - 1;
                        if ((uint)parameter >= (uint)call.Arguments.Length)
                        {
                            throw Invalid(call.Span);
                        }
                        Source(destination, call.Arguments[call.ArgumentIndex(parameter)], depth + 1);
                    }
                    break;
                case BoundCall call when (uint)call.Function < (uint)program.Functions.Length
                    && call.Type == PrimitiveType.TokenSpan
                    && program.Functions[call.Function].ReturnsForwardedBacking:
                    var callee = program.Functions[call.Function];
                    ValidateSpanCall(call, callee);
                    foreach (var parameter in callee.ResultDependencyParameters)
                    {
                        Source(destination, call.Arguments[call.ArgumentIndex(parameter)], depth + 1);
                    }
                    break;
                case BoundConditional conditional:
                    Source(destination, conditional.WhenTrue, depth + 1);
                    Source(destination, conditional.WhenFalse, depth + 1);
                    break;
                default:
                    throw InvalidEffect(expression.Span, "Ownership");
            }
        }

        void NativePointerSource(int destination, BoundExpression expression, int depth)
        {
            Count(ref remaining, depth);
            if (expression is null)
            {
                throw Invalid();
            }
            switch (expression)
            {
                case BoundNativePointerAddress address:
                    if ((uint)address.Variable >= (uint)function.Variables.Length
                        || !NativePointerTypeContract.IsValidType(address.Type, program.NativePointerTypes))
                    {
                        throw InvalidEffect(address.Span, "NativePointer");
                    }
                    AddNativePointerSource(facts, destination, address.Variable);
                    break;
                case BoundNativeFixedArrayPointerAddress address:
                    if ((uint)address.Variable >= (uint)function.Variables.Length
                        || !NativePointerTypeContract.IsValidType(address.Type, program.NativePointerTypes))
                    {
                        throw InvalidEffect(address.Span, "NativePointer");
                    }
                    AddNativePointerSource(facts, destination, address.Variable);
                    break;
                case BoundRead read when CarriesNativePointer(read.Type, program):
                    if ((uint)read.Variable >= (uint)function.Variables.Length
                        || function.Variables[read.Variable].Type != read.Type)
                    {
                        throw Invalid(read.Span);
                    }
                    AddNativePointerEdge(read.Variable, destination);
                    break;
                case BoundMove move when CarriesNativePointer(move.Type, program):
                    if ((uint)move.Variable >= (uint)function.Variables.Length
                        || function.Variables[move.Variable].Type != move.Type)
                    {
                        throw Invalid(move.Span);
                    }
                    AddNativePointerEdge(move.Variable, destination);
                    break;
                case BoundFieldRead field:
                    NativePointerSource(destination, field.Receiver, depth + 1);
                    break;
                case BoundNativePointerRead read when CarriesNativePointer(read.Type, program):
                    nativePointerReads.Add((destination, read.Address));
                    break;
                case BoundUnsafeExpression unsafeExpression:
                    NativePointerSource(destination, unsafeExpression.Expression, depth + 1);
                    break;
                case BoundCall call when CarriesNativePointer(call.Type, program)
                    && (uint)call.Function < (uint)program.Functions.Length
                    && !program.Functions[call.Function].IsExternal
                    && program.Functions[call.Function].Body is not null:
                    var callee = program.Functions[call.Function];
                    ValidateSpanCall(call, callee);
                    foreach (var argument in call.Arguments)
                    {
                        if (CarriesNativePointer(argument.Type, program))
                        {
                            NativePointerSource(destination, argument, depth + 1);
                        }
                    }
                    break;
                case BoundConditional conditional when CarriesNativePointer(conditional.Type, program):
                    NativePointerSource(destination, conditional.WhenTrue, depth + 1);
                    NativePointerSource(destination, conditional.WhenFalse, depth + 1);
                    break;
            }
        }

        void Collect(BoundStatement statement, int depth)
        {
            Count(ref remaining, depth);
            switch (statement)
            {
                case BoundLocal { Initializer: BoundExpression initializer } local:
                    if ((uint)local.Variable >= (uint)facts.Sources.Length)
                    {
                        throw Invalid(local.Span);
                    }
                    if (TokenSpanContract.IsDependent(initializer.Type, program.Structures))
                    {
                        Source(local.Variable, initializer, depth + 1);
                    }
                    if (CarriesNativePointer(initializer.Type, program))
                    {
                        NativePointerSource(local.Variable, initializer, depth + 1);
                    }
                    break;
                case BoundReturn { Value: BoundExpression result } when result.Type == PrimitiveType.TokenSpan:
                    forwardedSpanReturns?.Add(result);
                    break;
                case BoundReturn { Value: BoundExpression result } when CarriesNativePointer(result.Type, program):
                    nativePointerReturns.Add(result);
                    break;
                case BoundAssignment { Value: BoundExpression value } assignment:
                    if ((uint)assignment.Variable >= (uint)facts.Sources.Length)
                    {
                        throw Invalid(assignment.Span);
                    }
                    if (TokenSpanContract.IsDependent(value.Type, program.Structures))
                    {
                        Source(assignment.Variable, value, depth + 1);
                    }
                    if (CarriesNativePointer(value.Type, program))
                    {
                        NativePointerSource(assignment.Variable, value, depth + 1);
                    }
                    break;
                case BoundFieldAssignment { Value.Type: PrimitiveType.TokenSpan } field:
                    if ((uint)field.Variable >= (uint)facts.Sources.Length)
                    {
                        throw Invalid(field.Span);
                    }
                    Source(field.Variable, field.Value, depth + 1);
                    break;
                case BoundFieldAssignment field when CarriesNativePointer(field.Value.Type, program):
                    if ((uint)field.Variable >= (uint)facts.NativePointerSources.Length)
                    {
                        throw Invalid(field.Span);
                    }
                    NativePointerSource(field.Variable, field.Value, depth + 1);
                    break;
                case BoundExpressionStatement { Expression: BoundNativePointerWrite write }
                    when CarriesNativePointer(write.Value.Type, program):
                    nativePointerWrites.Add((write.Address, write.Value));
                    break;
                case BoundBlock block:
                    if (block.Statements.IsDefault)
                    {
                        throw Invalid(block.Span);
                    }
                    foreach (var child in block.Statements)
                    {
                        Collect(child, depth + 1);
                    }
                    break;
                case BoundTryFinally region:
                    Collect(region.Body, depth + 1);
                    Collect(region.Finally, depth + 1);
                    break;
                case BoundTryCatch region:
                    Collect(region.Body, depth + 1);
                    Collect(region.Catch, depth + 1);
                    if (region.Finally is not null)
                    {
                        Collect(region.Finally, depth + 1);
                    }
                    break;
                case BoundUnsafe region:
                    Collect(region.Body, depth + 1);
                    break;
                case BoundViewScope region:
                    Collect(region.Body, depth + 1);
                    break;
                case BoundIf conditional:
                    Collect(conditional.Then, depth + 1);
                    if (conditional.Else is not null)
                    {
                        Collect(conditional.Else, depth + 1);
                    }
                    break;
                case BoundWhile loop:
                    Collect(loop.Body, depth + 1);
                    if (loop.Iterator is not null)
                    {
                        Collect(loop.Iterator, depth + 1);
                    }
                    break;
            }
        }

        Collect(function.Body, 0);
        var pending = new Queue<int>(edges.Length);
        var queued = new bool[edges.Length];
        for (var source = 0; source < edges.Length; source++)
        {
            pending.Enqueue(source);
            queued[source] = true;
        }

        while (pending.TryDequeue(out var source))
        {
            queued[source] = false;
            if (edges[source] is { } outgoing)
            {
                var sourceFacts = facts.Sources[source];
                foreach (var destination in outgoing)
                {
                    Count(ref remaining, 0);
                    if (sourceFacts is { Count: > 0 })
                    {
                        var destinationFacts = facts.Sources[destination];
                        if (destinationFacts is null)
                        {
                            destinationFacts = [];
                            facts.Sources[destination] = destinationFacts;
                        }
                        var before = destinationFacts.Count;
                        destinationFacts.UnionWith(sourceFacts);
                        if (before != destinationFacts.Count && !queued[destination])
                        {
                            pending.Enqueue(destination);
                            queued[destination] = true;
                        }
                    }
                }
            }
        }

        bool PropagateNativePointerEdges()
        {
            var changed = false;
            var pointerPending = new Queue<int>(nativePointerEdges.Length);
            var pointerQueued = new bool[nativePointerEdges.Length];
            for (var source = 0; source < nativePointerEdges.Length; source++)
            {
                pointerPending.Enqueue(source);
                pointerQueued[source] = true;
            }
            while (pointerPending.TryDequeue(out var source))
            {
                pointerQueued[source] = false;
                if (nativePointerEdges[source] is not { } outgoing)
                {
                    continue;
                }
                var sourceFacts = facts.NativePointerSources[source];
                foreach (var destination in outgoing)
                {
                    Count(ref remaining, 0);
                    if (sourceFacts is { Count: > 0 })
                    {
                        var destinationFacts = facts.NativePointerSources[destination];
                        if (destinationFacts is null)
                        {
                            destinationFacts = [];
                            facts.NativePointerSources[destination] = destinationFacts;
                        }
                        var before = destinationFacts.Count;
                        destinationFacts.UnionWith(sourceFacts);
                        if (before != destinationFacts.Count)
                        {
                            changed = true;
                            if (!pointerQueued[destination])
                            {
                                pointerPending.Enqueue(destination);
                                pointerQueued[destination] = true;
                            }
                        }
                    }
                }
            }
            return changed;
        }

        var pointerFactsChanged = true;
        while (pointerFactsChanged)
        {
            pointerFactsChanged = PropagateNativePointerEdges();
            foreach (var (destination, address) in nativePointerReads)
            {
                Count(ref remaining, 0);
                foreach (var target in NativePointerBackingOf(address, facts, program, ref remaining, 0))
                {
                    if ((uint)target >= (uint)facts.NativePointerSources.Length
                        || facts.NativePointerSources[target] is not { Count: > 0 } sourceFacts)
                    {
                        continue;
                    }
                    var destinationFacts = facts.NativePointerSources[destination];
                    if (destinationFacts is null)
                    {
                        destinationFacts = [];
                        facts.NativePointerSources[destination] = destinationFacts;
                    }
                    var before = destinationFacts.Count;
                    destinationFacts.UnionWith(sourceFacts);
                    pointerFactsChanged |= before != destinationFacts.Count;
                }
            }
            foreach (var (address, value) in nativePointerWrites)
            {
                Count(ref remaining, 0);
                var targets = NativePointerBackingOf(address, facts, program, ref remaining, 0);
                var valueSources = NativePointerBackingOf(value, facts, program, ref remaining, 0);
                foreach (var target in targets)
                {
                    if ((uint)target >= (uint)facts.NativePointerSources.Length || valueSources.Count == 0)
                    {
                        continue;
                    }
                    pointerFactsChanged |= AddNativePointerSources(facts, target, valueSources);
                }
            }
        }
        foreach (var result in nativePointerReturns)
        {
            if (NativePointerBackingOf(result, facts, program, ref remaining, 0).Count != 0)
            {
                throw InvalidEffect(result.Span, "NativePointer");
            }
        }
        if (forwardedSpanReturns is not null)
        {
            if (forwardedSpanReturns.Count == 0)
            {
                throw InvalidEffect(function.Span, "Ownership");
            }
            var permittedSources = function.ResultDependencyParameters
                .Select(static parameter => ~parameter).ToHashSet();
            foreach (var result in forwardedSpanReturns)
            {
                var actualSources = BackingOf(result, facts, program, ref remaining, 0);
                if (actualSources.Count == 0 || actualSources.Any(source => !permittedSources.Contains(source)))
                {
                    throw InvalidEffect(result.Span, "Ownership");
                }
            }
        }
        return facts;
    }

    private static void AddNativePointerSource(BackingDependencies facts, int slot, int source)
    {
        var sources = facts.NativePointerSources[slot];
        if (sources is null)
        {
            sources = [];
            facts.NativePointerSources[slot] = sources;
        }
        _ = sources.Add(source);
    }

    private static bool AddNativePointerSources(BackingDependencies facts, int slot, IReadOnlySet<int> additions)
    {
        if (additions.Count == 0)
        {
            return false;
        }
        var sources = facts.NativePointerSources[slot];
        if (sources is null)
        {
            facts.NativePointerSources[slot] = [.. additions];
            return true;
        }
        var before = sources.Count;
        sources.UnionWith(additions);
        return before != sources.Count;
    }

    private static bool CarriesNativePointer(PrimitiveType type, BoundProgram program)
    {
        var pending = new Stack<PrimitiveType>();
        var visited = new HashSet<PrimitiveType>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (NativePointerTypeContract.IsValidType(current, program.NativePointerTypes))
            {
                return true;
            }
            var structureType = (int)current;
            if (structureType < 100 || structureType - 100 >= program.Structures.Length
                || !visited.Add(current))
            {
                continue;
            }
            var structureIndex = structureType - 100;
            foreach (var field in program.Structures[structureIndex].Fields)
            {
                pending.Push(field.Type);
            }
        }
        return false;
    }

    private static IReadOnlySet<int> NativePointerBackingOf(BoundExpression expression,
        BackingDependencies facts, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is null)
        {
            throw Invalid();
        }
        switch (expression)
        {
            case BoundNativePointerAddress address when (uint)address.Variable < (uint)facts.NativePointerSources.Length:
                return new HashSet<int> { address.Variable };
            case BoundNativeFixedArrayPointerAddress address
                when (uint)address.Variable < (uint)facts.NativePointerSources.Length:
                return new HashSet<int> { address.Variable };
            case BoundRead read when (uint)read.Variable < (uint)facts.NativePointerSources.Length:
                return facts.NativePointerSources[read.Variable] ?? EmptyBackingSources;
            case BoundMove move when (uint)move.Variable < (uint)facts.NativePointerSources.Length:
                return facts.NativePointerSources[move.Variable] ?? EmptyBackingSources;
            case BoundNativePointerRead read when CarriesNativePointer(read.Type, program):
                var readSources = new HashSet<int>();
                foreach (var target in NativePointerBackingOf(read.Address, facts, program,
                    ref remaining, depth + 1))
                {
                    if ((uint)target < (uint)facts.NativePointerSources.Length
                        && facts.NativePointerSources[target] is { } targetSources)
                    {
                        readSources.UnionWith(targetSources);
                    }
                }
                return readSources;
            case BoundFieldRead field:
                return NativePointerBackingOf(field.Receiver, facts, program, ref remaining, depth + 1);
            case BoundUnsafeExpression unsafeExpression:
                return NativePointerBackingOf(unsafeExpression.Expression, facts, program, ref remaining, depth + 1);
            case BoundCall call when CarriesNativePointer(call.Type, program)
                && (uint)call.Function < (uint)program.Functions.Length
                && !program.Functions[call.Function].IsExternal
                && program.Functions[call.Function].Body is not null:
                var callee = program.Functions[call.Function];
                ValidateSpanCall(call, callee);
                var callBacking = new HashSet<int>();
                foreach (var argument in call.Arguments)
                {
                    if (CarriesNativePointer(argument.Type, program))
                    {
                        callBacking.UnionWith(NativePointerBackingOf(argument, facts, program,
                            ref remaining, depth + 1));
                    }
                }
                return callBacking;
            case BoundConditional conditional when CarriesNativePointer(conditional.Type, program):
                var branchBacking = new HashSet<int>(NativePointerBackingOf(conditional.WhenTrue,
                    facts, program, ref remaining, depth + 1));
                branchBacking.UnionWith(NativePointerBackingOf(conditional.WhenFalse,
                    facts, program, ref remaining, depth + 1));
                return branchBacking;
            default:
                return EmptyBackingSources;
        }
    }

    private static void ValidateSpanCall(BoundCall call, BoundFunction callee)
    {
        var arguments = call.Arguments;
        if (arguments.IsDefault || arguments.Length != callee.ParameterCount || call.Type != callee.ReturnType
            || (!call.ArgumentParameterIndices.IsDefault && call.ArgumentParameterIndices.Length != arguments.Length))
        {
            throw Invalid(call.Span);
        }
        var seen = new bool[callee.ParameterCount];
        for (var argument = 0; argument < arguments.Length; argument++)
        {
            var parameter = call.ParameterIndex(argument);
            var variable = parameter + (callee.IsConstructor ? 1 : 0);
            if ((uint)parameter >= (uint)seen.Length || seen[parameter]
                || arguments[argument] is not BoundExpression expression
                || (uint)variable >= (uint)callee.Variables.Length
                || expression.Type != callee.Variables[variable].Type)
            {
                throw Invalid(call.Span);
            }
            seen[parameter] = true;
        }
    }

    private static void AddBackingSource(BackingDependencies facts, int slot, int source)
    {
        var sources = facts.Sources[slot];
        if (sources is null)
        {
            sources = [];
            facts.Sources[slot] = sources;
        }
        _ = sources.Add(source);
    }

    private static void CheckNativePointerLifetimeEnd(int owner, InitializationState state, TextSpan span)
    {
        if (state.Dependencies is not BackingDependencies facts)
        {
            return;
        }
        for (var slot = 0; slot < state.Length; slot++)
        {
            if ((state.Possibilities(slot) & 2) != 0 && facts.NativePointerSources[slot]?.Contains(owner) == true)
            {
                throw InvalidEffect(span, "NativePointer");
            }
        }
    }

    private static void CheckNativePointerReturn(BoundFunction function, InitializationState state, TextSpan span)
    {
        if (state.Dependencies is not BackingDependencies facts)
        {
            return;
        }
        var parameterCount = function.ParameterCount + (function.IsConstructor ? 1 : 0);
        for (var slot = 0; slot < parameterCount; slot++)
        {
            if (slot < function.Variables.Length
                && (function.Variables[slot].IsOut || function.Variables[slot].IsInit
                    || (function.IsConstructor && slot == 0) || (function.IsClassConstructor && slot == 0))
                && (state.Possibilities(slot) & 2) != 0
                && facts.NativePointerSources[slot] is { Count: > 0 })
            {
                throw InvalidEffect(span, "NativePointer");
            }
        }
    }

    private static void CheckNativePointerScopeExit(BoundBlock block, InitializationState state)
    {
        if (state.Dependencies is not BackingDependencies facts)
        {
            return;
        }
        var scopedVariables = new HashSet<int>();
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case BoundLocal local:
                    _ = scopedVariables.Add(local.Variable);
                    break;
                case BoundVacantLocal vacant:
                    _ = scopedVariables.Add(vacant.Variable);
                    break;
                case BoundOwnerDeclaration owner:
                    _ = scopedVariables.Add(owner.Variable);
                    break;
            }
        }
        if (scopedVariables.Count == 0)
        {
            return;
        }
        for (var slot = 0; slot < state.Length; slot++)
        {
            if (!scopedVariables.Contains(slot) && (state.Possibilities(slot) & 2) != 0
                && facts.NativePointerSources[slot]?.Any(scopedVariables.Contains) == true)
            {
                throw InvalidEffect(block.Span, "NativePointer");
            }
        }
    }

    private static IReadOnlySet<int> BackingOf(BoundExpression expression, BackingDependencies? facts,
        BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is null)
        {
            throw Invalid();
        }
        if (facts is null)
        {
            throw InvalidEffect(expression.Span, "Ownership");
        }
        switch (expression)
        {
            case BoundSnapshotSpan snapshot when (uint)snapshot.Variable < (uint)facts.Sources.Length:
                return new HashSet<int> { snapshot.Variable };
            case BoundRead read when (uint)read.Variable < (uint)facts.Sources.Length:
                return facts.Sources[read.Variable] ?? EmptyBackingSources;
            case BoundMove move when (uint)move.Variable < (uint)facts.Sources.Length:
                return facts.Sources[move.Variable] ?? EmptyBackingSources;
            case BoundReceiverLoan loan when (uint)loan.Variable < (uint)facts.Sources.Length:
                return facts.Sources[loan.Variable] ?? EmptyBackingSources;
            case BoundMutableReceiverLoan loan when (uint)loan.Variable < (uint)facts.Sources.Length:
                return facts.Sources[loan.Variable] ?? EmptyBackingSources;
            case BoundFieldRead field:
                return BackingOf(field.Receiver, facts, program, ref remaining, depth + 1);
            case BoundUnsafeExpression unsafeExpression:
                return BackingOf(unsafeExpression.Expression, facts, program, ref remaining, depth + 1);
            case BoundSpanAdvance advance:
                return BackingOf(advance.View, facts, program, ref remaining, depth + 1);
            case BoundCall call when (uint)call.Function < (uint)program.Functions.Length
                && program.Functions[call.Function].IsConstructor
                && TokenSpanContract.IsCursor(call.Type, program.Structures):
                var constructor = program.Functions[call.Function];
                ValidateSpanCall(call, constructor);
                var cursorBacking = new HashSet<int>();
                foreach (var capture in constructor.CapturedParameters)
                {
                    var parameter = capture - 1;
                    if ((uint)parameter >= (uint)call.Arguments.Length)
                    {
                        throw Invalid(call.Span);
                    }
                    cursorBacking.UnionWith(BackingOf(call.Arguments[call.ArgumentIndex(parameter)],
                        facts, program, ref remaining, depth + 1));
                }
                if (cursorBacking.Count == 0)
                {
                    throw InvalidEffect(call.Span, "Ownership");
                }
                return cursorBacking;
            case BoundCall call when (uint)call.Function < (uint)program.Functions.Length
                && call.Type == PrimitiveType.TokenSpan:
                var result = new HashSet<int>();
                var callee = program.Functions[call.Function];
                ValidateSpanCall(call, callee);
                if (callee.ReturnsForwardedBacking && callee.ReturnType == PrimitiveType.TokenSpan)
                {
                    foreach (var parameter in callee.ResultDependencyParameters)
                    {
                        result.UnionWith(BackingOf(call.Arguments[call.ArgumentIndex(parameter)], facts,
                            program, ref remaining, depth + 1));
                    }
                }
                return result;
            case BoundConditional conditional when conditional.Condition is not null
                && conditional.WhenTrue is not null && conditional.WhenFalse is not null
                && conditional.WhenTrue.Type == PrimitiveType.TokenSpan
                && conditional.WhenFalse.Type == PrimitiveType.TokenSpan:
                var union = new HashSet<int>(BackingOf(conditional.WhenTrue, facts, program, ref remaining, depth + 1));
                union.UnionWith(BackingOf(conditional.WhenFalse, facts, program, ref remaining, depth + 1));
                return union;
            default:
                throw InvalidEffect(expression.Span, "Ownership");
        }
    }

    private static void CheckBackingInvalidation(int owner, InitializationState state, TextSpan span)
    {
        if (state.Dependencies is not BackingDependencies facts)
        {
            return;
        }
        for (var slot = 0; slot < state.Length; slot++)
        {
            if ((state.Possibilities(slot) & 2) != 0 && facts.Sources[slot]?.Contains(owner) == true)
            {
                throw InvalidEffect(span, "Ownership");
            }
        }
        if (facts.Staged.Any(sources => sources.Contains(owner)))
        {
            throw InvalidEffect(span, "Ownership");
        }
    }

    private static void VerifySpanOperand(BoundExpression view, InitializationState initialized, uint[] masks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures)
    {
        if (view is null || view.Type != PrimitiveType.TokenSpan)
        {
            throw InvalidEffect(function.Span, "Ownership");
        }
        Expression(view, initialized, masks, declarations, function, program, ref remaining, depth + 1, failures);
        if (BackingOf(view, initialized.Dependencies, program, ref remaining, depth + 1).Count == 0)
        {
            throw InvalidEffect(view.Span, "Ownership");
        }
    }

    private static void VerifySpanFieldWrite(BoundFieldAssignment write, BoundFunction function,
        InitializationState initialized)
    {
        if (write.Variable != 0 || write.Field != 0 || !write.NestedFields.IsEmpty
            || function.DeclaringStruct != TokenSpanContract.CursorName)
        {
            throw InvalidEffect(write.Span, "Ownership");
        }
        if (function.IsConstructor)
        {
            if (write.Value is not BoundRead { Variable: 1, Type: PrimitiveType.TokenSpan }
                || function.CapturedParameters is not [1])
            {
                throw InvalidEffect(write.Span, "Ownership");
            }
        }
        else if (write.Value is not BoundSpanAdvance {
            View: BoundFieldRead { Receiver: BoundRead { Variable: 0 }, Field: 0 } })
        {
            // Replacing backing would require caller-visible capture effect metadata.
            throw InvalidEffect(write.Span, "Ownership");
        }
        if (initialized.Dependencies is not BackingDependencies facts
            || facts.Sources[0] is not { Count: > 0 })
        {
            throw InvalidEffect(write.Span, "Ownership");
        }
    }

    private static void VerifySpanScopes(BoundFunction function)
    {
        var allowed = new HashSet<BoundDropSpan>(ReferenceEqualityComparer.Instance);
        var actual = new HashSet<BoundDropSpan>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(BoundStatement Statement, int Depth)>();
        pending.Push((function.Body, 0));
        var remaining = MaximumAnalysisVisits;
        while (pending.TryPop(out var item))
        {
            Count(ref remaining, item.Depth);
            switch (item.Statement)
            {
                case BoundBlock block:
                    for (var index = 0; index < block.Statements.Length; index++)
                    {
                        var child = block.Statements[index];
                        if (child is BoundLocal { Initializer.Type: PrimitiveType.TokenSpan } local)
                        {
                            if (index + 1 >= block.Statements.Length
                                || block.Statements[index + 1] is not BoundTryFinally region
                                || region.Finally.Statements is not [BoundDropSpan drop]
                                || drop.Variable != local.Variable || !allowed.Add(drop)
                                || index + 2 != block.Statements.Length
                                    && !(index + 3 == block.Statements.Length
                                        && block.Statements[^1] is BoundReturn terminal
                                        && (function.ReturnType == PrimitiveType.Void && terminal.Value is null
                                            || function.IsConstructor && terminal.Value is BoundRead { Variable: 0 })))
                            {
                                throw InvalidEffect(local.Span, "Ownership");
                            }
                        }
                        pending.Push((child, item.Depth + 1));
                    }
                    break;
                case BoundDropSpan drop:
                    if (!actual.Add(drop))
                    {
                        throw InvalidEffect(drop.Span, "Ownership");
                    }
                    break;
                case BoundTryFinally region:
                    pending.Push((region.Body, item.Depth + 1));
                    pending.Push((region.Finally, item.Depth + 1));
                    break;
                case BoundTryCatch region:
                    pending.Push((region.Body, item.Depth + 1));
                    pending.Push((region.Catch, item.Depth + 1));
                    if (region.Finally is not null)
                    {
                        pending.Push((region.Finally, item.Depth + 1));
                    }
                    break;
                case BoundUnsafe region:
                    pending.Push((region.Body, item.Depth + 1));
                    break;
                case BoundViewScope region:
                    pending.Push((region.Body, item.Depth + 1));
                    break;
                case BoundIf conditional:
                    pending.Push((conditional.Then, item.Depth + 1));
                    if (conditional.Else is not null)
                    {
                        pending.Push((conditional.Else, item.Depth + 1));
                    }
                    break;
                case BoundWhile loop:
                    pending.Push((loop.Body, item.Depth + 1));
                    if (loop.Iterator is not null)
                    {
                        pending.Push((loop.Iterator, item.Depth + 1));
                    }
                    break;
            }
        }
        if (!allowed.SetEquals(actual))
        {
            throw InvalidEffect(function.Span, "Ownership");
        }
    }
}
