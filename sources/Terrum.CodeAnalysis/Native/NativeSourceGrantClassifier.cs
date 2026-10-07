// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal static class NativeSourceGrantClassifier
{
    internal static bool[] Classify(BoundProgram program)
    {
        if (!program.RequiresSourceGrant || program.IsInvocationSourceBound
            || program.InvocationProfileId is not null || program.EntryPoint != -1
            || program.Buffers.Length != 1 || program.Buffers[0] is not HostBuffer {
                Name: "Source", ElementType: PrimitiveType.Char16, IsWritable: false,
                IsInvocationScoped: true, InvocationProfileId: null, Values.IsEmpty: true,
            } || !program.Classes.IsEmpty || NativeEmitter.UsesManagedNodes(program))
        {
            throw Unsupported();
        }

        var functionIndices = new Dictionary<string, int>(program.Functions.Length, StringComparer.Ordinal);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            functionIndices.Add(program.Functions[index].Name, index);
        }

        var dependent = new bool[program.Functions.Length];
        var calls = new List<int>[program.Functions.Length];
        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            calls[functionIndex] = [];
            var statements = new Stack<BoundStatement>();
            var expressions = new Stack<BoundExpression>();
            statements.Push(function.Body);

            while (statements.Count != 0 || expressions.Count != 0)
            {
                while (expressions.Count != 0)
                {
                    var expression = expressions.Pop();
                    switch (expression)
                    {
                        case BoundUnsafeExpression unsafeExpression:
                            expressions.Push(unsafeExpression.Expression);
                            break;
                        case BoundCall call:
                            AddCall(call.Function);
                            foreach (var argument in call.Arguments)
                            {
                                if (argument is BoundOutArgument output)
                                {
                                    AddCleanupCall(output.Type);
                                }
                            }
                            PushExpressions(call.Arguments);
                            break;
                        case BoundNominalUpdate update:
                            AddCall(update.Function);
                            expressions.Push(update.Place);
                            break;
                        case BoundBufferLength length when length.Buffer == 0:
                            dependent[functionIndex] = true;
                            break;
                        case BoundBufferRead read when read.Buffer == 0:
                            dependent[functionIndex] = true;
                            expressions.Push(read.Index);
                            break;
                        case BoundSnapshotSpan snapshot when snapshot.SourceBuffer == 0:
                            dependent[functionIndex] = true;
                            break;
                        case BoundOptionalLift lift:
                            expressions.Push(lift.Value);
                            break;
                        case BoundCoalesce coalesce:
                            expressions.Push(coalesce.Optional);
                            expressions.Push(coalesce.Fallback);
                            break;
                        case BoundReadonlyProjection projection:
                            expressions.Push(projection.Value);
                            break;
                        case BoundIsNull test:
                            expressions.Push(test.Value);
                            break;
                        case BoundIsSome test:
                            expressions.Push(test.Value);
                            break;
                        case BoundIsVariablePattern test:
                            expressions.Push(test.Value);
                            break;
                        case BoundSpanLength length:
                            expressions.Push(length.View);
                            break;
                        case BoundSpanRead read:
                            expressions.Push(read.View);
                            expressions.Push(read.Index);
                            break;
                        case BoundSpanAdvance advance:
                            expressions.Push(advance.View);
                            break;
                        case BoundFieldLoan loan:
                            expressions.Push(loan.Place);
                            break;
                        case BoundMutableFieldLoan loan:
                            expressions.Push(loan.Place);
                            break;
                        case BoundUnary unary:
                            expressions.Push(unary.Operand);
                            break;
                        case BoundUpdate update:
                            expressions.Push(update.Place);
                            break;
                        case BoundCompoundAssignment assignment:
                            expressions.Push(assignment.Place);
                            expressions.Push(assignment.Right);
                            break;
                        case BoundBinary binary:
                            expressions.Push(binary.Left);
                            expressions.Push(binary.Right);
                            break;
                        case BoundConditional conditional:
                            expressions.Push(conditional.Condition);
                            expressions.Push(conditional.WhenTrue);
                            expressions.Push(conditional.WhenFalse);
                            break;
                        case BoundCast cast:
                            expressions.Push(cast.Operand);
                            break;
                        case BoundFieldRead field:
                            expressions.Push(field.Receiver);
                            break;
                        case BoundViewRead view:
                            expressions.Push(view.Index);
                            break;
                        case BoundBufferRead read:
                            expressions.Push(read.Index);
                            break;
                        case BoundConstant or BoundNull or BoundRead or BoundMove or BoundReceiverLoan
                            or BoundMutableReceiverLoan or BoundViewLength or BoundOutArgument
                            or BoundIsInitialized:
                            break;
                        default:
                            throw Unsupported();
                    }
                }

                if (statements.Count == 0)
                {
                    continue;
                }

                switch (statements.Pop())
                {
                    case BoundBlock block:
                        PushStatements(block.Statements);
                        break;
                    case BoundLocal local:
                        expressions.Push(local.Initializer);
                        break;
                    case BoundAssignment assignment:
                        expressions.Push(assignment.Value);
                        AddCleanupCall(assignment.Value.Type);
                        break;
                    case BoundOptionalAssignment assignment:
                        expressions.Push(assignment.Place);
                        expressions.Push(assignment.Value);
                        break;
                    case BoundFieldAssignment assignment:
                        expressions.Push(assignment.Value);
                        break;
                    case BoundBufferAssignment assignment:
                        expressions.Push(assignment.Index);
                        expressions.Push(assignment.Value);
                        if (assignment.Buffer == 0)
                        {
                            dependent[functionIndex] = true;
                        }
                        break;
                    case BoundExpressionStatement statement:
                        expressions.Push(statement.Expression);
                        break;
                    case BoundIf conditional:
                        expressions.Push(conditional.Condition);
                        statements.Push(conditional.Then);
                        if (conditional.Else is not null)
                        {
                            statements.Push(conditional.Else);
                        }
                        break;
                    case BoundWhile loop:
                        expressions.Push(loop.Condition);
                        statements.Push(loop.Body);
                        if (loop.Iterator is not null)
                        {
                            statements.Push(loop.Iterator);
                        }
                        break;
                    case BoundTryFinally cleanup:
                        statements.Push(cleanup.Body);
                        statements.Push(cleanup.Finally);
                        break;
                    case BoundViewScope view:
                        expressions.Push(view.Owner);
                        expressions.Push(view.Start);
                        expressions.Push(view.RangeEnd ?? view.Length);
                        statements.Push(view.Body);
                        break;
                    case BoundViewSet write:
                        expressions.Push(write.Index);
                        expressions.Push(write.Value);
                        break;
                    case BoundLifetimeEnd end:
                        AddCallIfPresent(end.CleanupFunction);
                        break;
                    case BoundDrop drop:
                        AddCallIfPresent(drop.CleanupFunction);
                        break;
                    case BoundDropConstructedFields partial:
                        AddConstructedFieldCleanupCalls(partial);
                        break;
                    case BoundUnsafe unsafeBlock:
                        statements.Push(unsafeBlock.Body);
                        break;
                    case BoundTryCatch recovery:
                        statements.Push(recovery.Body);
                        statements.Push(recovery.Catch);
                        if (recovery.Finally is not null)
                        {
                            statements.Push(recovery.Finally);
                        }
                        break;
                    case BoundRequire requirement:
                        expressions.Push(requirement.Condition);
                        break;
                    case BoundReturn { Value: BoundExpression value }:
                        expressions.Push(value);
                        break;
                    case BoundVacantLocal or BoundOwnerDeclaration or BoundDropSpan or BoundReturn
                        or BoundBreak or BoundContinue:
                        break;
                    default:
                        throw Unsupported();
                }
            }

            void AddCall(int callee)
            {
                if ((uint)callee >= (uint)program.Functions.Length)
                {
                    throw Unsupported();
                }
                calls[functionIndex].Add(callee);
            }

            void AddCallIfPresent(int callee)
            {
                if (callee is >= 0)
                {
                    AddCall(callee);
                }
            }

            void AddCleanupCall(PrimitiveType type)
            {
                if ((int)type >= 100 && (int)type < ManagedClassTypes.First
                    && program.Structures[(int)type - 100].CleanupMethod is string cleanup
                    && functionIndices.TryGetValue(cleanup, out var cleanupFunction))
                {
                    AddCall(cleanupFunction);
                }
            }

            void AddConstructedFieldCleanupCalls(BoundDropConstructedFields partial)
            {
                var type = function.ReturnType;
                if ((int)type is < 100 or >= ManagedClassTypes.First)
                {
                    return;
                }
                var structure = program.Structures[(int)type - 100];
                foreach (var fieldIndex in partial.AcquisitionOrder)
                {
                    AddCleanupCall(structure.Fields[fieldIndex].Type);
                }
            }

            void PushExpressions(ImmutableArray<BoundExpression> items)
            {
                foreach (var item in items)
                {
                    expressions.Push(item);
                }
            }

            void PushStatements(ImmutableArray<BoundStatement> items)
            {
                foreach (var item in items)
                {
                    statements.Push(item);
                }
            }
        }

        bool changed;
        do
        {
            changed = false;
            for (var function = 0; function < dependent.Length; function++)
            {
                if (dependent[function])
                {
                    continue;
                }
                foreach (var callee in calls[function])
                {
                    if (dependent[callee])
                    {
                        dependent[function] = true;
                        changed = true;
                        break;
                    }
                }
            }
        } while (changed);

        var reachable = (bool[])dependent.Clone();
        var pending = new Stack<int>();
        for (var functionIndex = 0; functionIndex < reachable.Length; functionIndex++)
        {
            if (reachable[functionIndex])
            {
                pending.Push(functionIndex);
            }
        }
        while (pending.Count != 0)
        {
            foreach (var callee in calls[pending.Pop()])
            {
                if (!reachable[callee])
                {
                    reachable[callee] = true;
                    pending.Push(callee);
                }
            }
        }
        for (var functionIndex = 0; functionIndex < reachable.Length; functionIndex++)
        {
            if (reachable[functionIndex])
            {
                ValidateDependentFunction(program, program.Functions[functionIndex]);
            }
        }

        return dependent;

        static void ValidateDependentFunction(BoundProgram program, BoundFunction function)
        {
            if (function.Intrinsic != BoundIntrinsic.None)
            {
                if (!IsSupportedWorkspaceIntrinsic(function))
                {
                    throw Unsupported();
                }

                return;
            }
            if (function.ParameterCount < 0 || function.ParameterCount > function.Variables.Length
                || function.IsExternal || function.IndexedBody is not null || function.DeclaringStruct is not null
                || function.IsConstructor || (!IsScalar(function.ReturnType)
                    && (function.ReturnType != PrimitiveType.OwnedInt32Buffer || !function.IsInternal)))
            {
                throw Unsupported();
            }
            for (var variableIndex = 0; variableIndex < function.Variables.Length; variableIndex++)
            {
                var variable = function.Variables[variableIndex];
                var isParameter = variableIndex < function.ParameterCount;
                var isOwner = variable.Type == PrimitiveType.OwnedInt32Buffer;
                var isView = IsBufferView(variable.Type);
                if ((!IsScalar(variable.Type) && !isOwner && !isView) || variable.IsOut
                    || (isOwner && ((!function.IsInternal && isParameter) || variable.IsInit))
                    || (isView && (isParameter || variable.IsInit)))
                {
                    throw Unsupported();
                }
            }

            var statements = new Stack<BoundStatement>();
            var expressions = new Stack<BoundExpression>();
            statements.Push(function.Body);
            while (statements.Count != 0 || expressions.Count != 0)
            {
                while (expressions.Count != 0)
                {
                    var expression = expressions.Pop();
                    if (!IsScalar(expression.Type) && !IsWorkspaceType(expression.Type))
                    {
                        throw Unsupported();
                    }
                    switch (expression)
                    {
                        case BoundUnsafeExpression unsafeExpression:
                            expressions.Push(unsafeExpression.Expression);
                            break;
                        case BoundConstant:
                            break;
                        case BoundRead read when IsScalar(read.Type)
                            || (IsBufferView(read.Type) && IsBufferViewLocal(function, read.Variable)):
                            break;
                        case BoundMove move when move.Type == PrimitiveType.OwnedInt32Buffer:
                            break;
                        case BoundReceiverLoan loan when loan.Type == PrimitiveType.OwnedInt32Buffer:
                            break;
                        case BoundMutableReceiverLoan loan when loan.Type == PrimitiveType.OwnedInt32Buffer:
                            break;
                        case BoundOutArgument output when output.Type == PrimitiveType.OwnedInt32Buffer:
                            break;
                        case BoundBufferLength length when length.Buffer == 0:
                            break;
                        case BoundBufferRead read when read.Buffer == 0:
                            expressions.Push(read.Index);
                            break;
                        case BoundUnary unary:
                            expressions.Push(unary.Operand);
                            break;
                        case BoundCast cast:
                            expressions.Push(cast.Operand);
                            break;
                        case BoundBinary binary:
                            expressions.Push(binary.Left);
                            expressions.Push(binary.Right);
                            break;
                        case BoundConditional conditional:
                            expressions.Push(conditional.Condition);
                            expressions.Push(conditional.WhenTrue);
                            expressions.Push(conditional.WhenFalse);
                            break;
                        case BoundCall call:
                            ValidateWorkspaceCall(call);
                            PushExpressions(call.Arguments);
                            break;
                        case BoundSpanLength length when IsBufferView(length.View.Type):
                            expressions.Push(length.View);
                            break;
                        case BoundSpanRead read when IsBufferView(read.View.Type):
                            expressions.Push(read.View);
                            expressions.Push(read.Index);
                            break;
                        case BoundViewRead read when IsBufferViewLocal(function, read.Variable):
                            expressions.Push(read.Index);
                            break;
                        default:
                            throw Unsupported();
                    }
                }

                if (statements.Count == 0)
                {
                    continue;
                }
                switch (statements.Pop())
                {
                    case BoundBlock block:
                        PushStatements(block.Statements);
                        break;
                    case BoundAssignment assignment:
                        if (!IsValidLocal(function, assignment.Variable)
                            || (!IsScalar(function.Variables[assignment.Variable].Type)
                                && function.Variables[assignment.Variable].Type
                                    != PrimitiveType.OwnedInt32Buffer))
                        {
                            throw Unsupported();
                        }
                        expressions.Push(assignment.Value);
                        break;
                    case BoundLocal local:
                        expressions.Push(local.Initializer);
                        break;
                    case BoundExpressionStatement statement:
                        expressions.Push(statement.Expression);
                        break;
                    case BoundIf conditional:
                        expressions.Push(conditional.Condition);
                        statements.Push(conditional.Then);
                        if (conditional.Else is not null)
                        {
                            statements.Push(conditional.Else);
                        }
                        break;
                    case BoundWhile loop when loop.Iterator is null && !loop.IsPostTested:
                        expressions.Push(loop.Condition);
                        statements.Push(loop.Body);
                        break;
                    case BoundTryFinally cleanup when IsWorkspaceCleanup(cleanup.Finally):
                        statements.Push(cleanup.Body);
                        statements.Push(cleanup.Finally);
                        break;
                    case BoundViewScope view when IsBufferViewLocal(function, view.Variable)
                        && function.Variables[view.Variable].Type
                            == (view.IsMutable ? PrimitiveType.MutableInt32BufferView
                                : PrimitiveType.ReadonlyInt32BufferView):
                        expressions.Push(view.Owner);
                        expressions.Push(view.Start);
                        expressions.Push(view.RangeEnd ?? view.Length);
                        statements.Push(view.Body);
                        break;
                    case BoundViewSet write when IsMutableBufferViewLocal(function, write.Variable):
                        expressions.Push(write.Index);
                        expressions.Push(write.Value);
                        break;
                    case BoundOwnerDeclaration owner when IsOwnerLocal(function, owner.Variable):
                        break;
                    case BoundDrop drop when IsOwnerLocal(function, drop.Variable)
                        && drop.CleanupFunction < 0:
                        break;
                    case BoundDropSpan drop when IsBufferViewLocal(function, drop.Variable):
                        break;
                    case BoundLifetimeEnd end when IsWorkspaceLocal(function, end.Variable)
                        && end.CleanupFunction < 0:
                        break;
                    case BoundReturn { Value: BoundExpression value }:
                        expressions.Push(value);
                        break;
                    case BoundRequire requirement:
                        expressions.Push(requirement.Condition);
                        break;
                    case BoundReturn or BoundBreak or BoundContinue:
                        break;
                    default:
                        throw Unsupported();
                }
            }

            void ValidateWorkspaceCall(BoundCall call)
            {
                var callee = program.Functions[call.Function];
                if (callee.Intrinsic != BoundIntrinsic.None)
                {
                    if (!IsSupportedWorkspaceIntrinsic(callee))
                    {
                        throw Unsupported();
                    }
                    if (callee.Intrinsic == BoundIntrinsic.BufferTryCreate)
                    {
                        if (call.Arguments.Length != 2
                            || call.Arguments[1] is not BoundOutArgument {
                                Type: PrimitiveType.OwnedInt32Buffer,
                            })
                        {
                            throw Unsupported();
                        }
                    }
                    else if (call.Arguments.IsEmpty
                        || call.Arguments[0] is not (BoundReceiverLoan {
                                Type: PrimitiveType.OwnedInt32Buffer,
                            } or BoundMutableReceiverLoan {
                                Type: PrimitiveType.OwnedInt32Buffer,
                            }))
                    {
                        throw Unsupported();
                    }

                    return;
                }

                var hasOwnerSignature = callee.ReturnType == PrimitiveType.OwnedInt32Buffer;
                for (var parameter = 0; parameter < callee.ParameterCount; parameter++)
                {
                    hasOwnerSignature |= callee.Variables[parameter].Type == PrimitiveType.OwnedInt32Buffer;
                }
                if (!hasOwnerSignature)
                {
                    return;
                }
                if (!callee.IsInternal || callee.IsExternal || callee.DeclaringStruct is not null
                    || callee.IsConstructor || callee.ParameterCount < 0
                    || callee.ParameterCount > callee.Variables.Length
                    || call.Arguments.Length != callee.ParameterCount)
                {
                    throw Unsupported();
                }
                for (var parameter = 0; parameter < callee.ParameterCount; parameter++)
                {
                    if (callee.Variables[parameter].IsOut || callee.Variables[parameter].IsInit
                        || IsBufferView(callee.Variables[parameter].Type))
                    {
                        throw Unsupported();
                    }
                    if (callee.Variables[parameter].Type == PrimitiveType.OwnedInt32Buffer
                        && call.Arguments[parameter] is not BoundMove {
                            Type: PrimitiveType.OwnedInt32Buffer,
                        })
                    {
                        throw Unsupported();
                    }
                }
            }

            bool IsWorkspaceCleanup(BoundBlock cleanup)
            {
                if (cleanup.Statements.IsEmpty)
                {
                    return false;
                }
                foreach (var statement in cleanup.Statements)
                {
                    if (statement is BoundBlock nested)
                    {
                        if (!IsWorkspaceCleanup(nested))
                        {
                            return false;
                        }
                    }
                    else if (statement is BoundDrop cleanupDrop)
                    {
                        if (!IsOwnerLocal(function, cleanupDrop.Variable) || cleanupDrop.CleanupFunction >= 0)
                        {
                            return false;
                        }
                    }
                    else if (statement is BoundDropSpan cleanupDropSpan)
                    {
                        if (!IsBufferViewLocal(function, cleanupDropSpan.Variable))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                return true;
            }

            void PushExpressions(ImmutableArray<BoundExpression> items)
            {
                foreach (var item in items)
                {
                    expressions.Push(item);
                }
            }

            void PushStatements(ImmutableArray<BoundStatement> items)
            {
                foreach (var item in items)
                {
                    statements.Push(item);
                }
            }
        }

        static bool IsSupportedWorkspaceIntrinsic(BoundFunction function) =>
            function.Intrinsic is BoundIntrinsic.BufferTryCreate or BoundIntrinsic.BufferCount
                or BoundIntrinsic.BufferRead or BoundIntrinsic.BufferSet or BoundIntrinsic.BufferTryAppend
            && NativeBufferIntrinsics.Matches(function);

        static bool IsWorkspaceType(PrimitiveType type) =>
            type == PrimitiveType.OwnedInt32Buffer || IsBufferView(type);

        static bool IsBufferView(PrimitiveType type) =>
            type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView;

        static bool IsWorkspaceLocal(BoundFunction function, int variable) =>
            IsValidLocal(function, variable) && IsWorkspaceType(function.Variables[variable].Type);

        static bool IsOwnerLocal(BoundFunction function, int variable) =>
            IsValidLocal(function, variable)
            && function.Variables[variable].Type == PrimitiveType.OwnedInt32Buffer;

        static bool IsBufferViewLocal(BoundFunction function, int variable) =>
            IsValidLocal(function, variable)
            && variable >= function.ParameterCount && IsBufferView(function.Variables[variable].Type);

        static bool IsMutableBufferViewLocal(BoundFunction function, int variable) =>
            IsBufferViewLocal(function, variable)
            && function.Variables[variable].Type == PrimitiveType.MutableInt32BufferView;

        static bool IsValidLocal(BoundFunction function, int variable) =>
            (uint)variable < (uint)function.Variables.Length;
    }

    internal static bool IsScalar(PrimitiveType type) =>
        type is PrimitiveType.Void or PrimitiveType.Bool or PrimitiveType.Char16
            or PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64;

    private static NotSupportedException Unsupported() =>
        new("The provisional source-grant DLL supports only self-contained structured scalar source-dependent functions.");
}
