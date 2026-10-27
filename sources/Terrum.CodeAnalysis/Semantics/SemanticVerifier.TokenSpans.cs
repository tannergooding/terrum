// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private enum ProvenanceAnchorKind : byte
    {
        BackingSource,
        IncomingBacking,
        NativeStorage,
    }

    private enum StorageProjectionKind : byte
    {
        WholeObject,
        ElementRange,
        ArrayElement,
        UnknownElement,
        InlineFieldPath,
    }

    private readonly record struct FieldProjectionSegment(PrimitiveType OwnerType, int Field);

    private readonly struct FieldProjectionPath : IEquatable<FieldProjectionPath>
    {
        private readonly ImmutableArray<FieldProjectionSegment> _segments;

        private FieldProjectionPath(ImmutableArray<FieldProjectionSegment> segments)
        {
            _segments = segments;
        }

        internal ImmutableArray<FieldProjectionSegment> Segments => _segments.IsDefault
            ? [] : _segments;

        internal FieldProjectionPath Append(PrimitiveType ownerType, int field)
        {
            if (field < 0)
            {
                throw Invalid();
            }

            var segments = _segments.IsDefault ? [] : _segments;
            return new FieldProjectionPath(segments.Add(new FieldProjectionSegment(ownerType, field)));
        }

        internal FieldProjectionPath Append(FieldProjectionPath suffix)
        {
            var result = _segments.IsDefault ? [] : _segments;
            foreach (var segment in suffix.Segments)
            {
                result = result.Add(segment);
            }
            return new FieldProjectionPath(result);
        }

        internal bool IsPrefixOf(FieldProjectionPath other)
        {
            var segments = Segments;
            var otherSegments = other.Segments;
            return segments.Length <= otherSegments.Length
                && segments.AsSpan().SequenceEqual(otherSegments.AsSpan(0, segments.Length));
        }

        public bool Equals(FieldProjectionPath other)
            => Segments.AsSpan().SequenceEqual(other.Segments.AsSpan());

        public override bool Equals(object? obj)
            => obj is FieldProjectionPath other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var segment in Segments)
            {
                hash.Add(segment);
            }
            return hash.ToHashCode();
        }
    }

    private enum CopyOriginSourceKind : byte
    {
        ExplicitFormal = 1,
        Receiver = 2,
    }

    private enum CopyOriginSourceView : byte
    {
        EntryValue = 1,
        EntryReferentValue = 2,
    }

    private readonly record struct CopyValueOrigin(
        CopyOriginSourceKind SourceKind,
        CopyOriginSourceView SourceView,
        int SourceOrdinal,
        FieldProjectionPath SourcePath)
    {
        internal bool IsValid => (SourceKind is CopyOriginSourceKind.ExplicitFormal
                or CopyOriginSourceKind.Receiver)
            && (SourceView is CopyOriginSourceView.EntryValue
                or CopyOriginSourceView.EntryReferentValue)
            && SourceOrdinal >= 0
            && (SourceKind != CopyOriginSourceKind.Receiver
                || (SourceView == CopyOriginSourceView.EntryReferentValue && SourceOrdinal == 0));
    }

    private sealed class CopyOriginFact
    {
        private CopyOriginFact(bool isAvailable, HashSet<CopyValueOrigin> origins)
        {
            IsAvailable = isAvailable;
            Origins = origins;
        }

        internal bool IsAvailable { get; private set; }
        internal HashSet<CopyValueOrigin> Origins { get; }

        internal static CopyOriginFact Unavailable() => new(false, []);
        internal static CopyOriginFact KnownEmpty() => new(true, []);

        internal static CopyOriginFact Known(IEnumerable<CopyValueOrigin> origins)
        {
            var result = new HashSet<CopyValueOrigin>(origins);
            if (result.Any(static origin => !origin.IsValid))
            {
                throw Invalid();
            }
            return new CopyOriginFact(true, result);
        }

        internal CopyOriginFact Clone()
            => new(IsAvailable, [.. Origins]);

        internal bool Merge(CopyOriginFact other)
        {
            if (!IsAvailable || !other.IsAvailable)
            {
                var changed = IsAvailable || Origins.Count != 0;
                IsAvailable = false;
                Origins.Clear();
                return changed;
            }

            var priorCount = Origins.Count;
            Origins.UnionWith(other.Origins);
            return priorCount != Origins.Count;
        }

        internal bool IsEquivalentTo(CopyOriginFact other)
            => IsAvailable == other.IsAvailable && Origins.SetEquals(other.Origins);

        internal bool IsValid => !IsAvailable || Origins.All(static origin => origin.IsValid);

        internal CopyOriginFact Project(FieldProjectionPath suffix)
            => IsAvailable
                ? Known(Origins.Select(origin => origin with {
                    SourcePath = origin.SourcePath.Append(suffix),
                }))
                : Unavailable();
    }

    private enum StorageActivationKind : byte
    {
        Block,
        LoopFirstEntry,
        LoopReentry,
    }

    private readonly record struct StorageActivationKey(
        int Parent, int Site, int Length, StorageActivationKind Kind);

    private sealed class StorageActivationContext
    {
        private readonly Dictionary<StorageActivationKey, int> _activations = [];
        private int _nextActivation = 1;

        internal int Enter(int parent, TextSpan span, StorageActivationKind kind)
        {
            if (parent < 0)
            {
                throw Invalid();
            }

            var key = new StorageActivationKey(parent, span.Start, span.Length, kind);
            if (!_activations.TryGetValue(key, out var activation))
            {
                activation = _nextActivation++;
                _activations.Add(key, activation);
            }
            return activation;
        }
    }

    private readonly record struct StorageProjection(
        StorageProjectionKind Kind, long Start, long Length,
        FieldProjectionPath FieldPath = default)
    {
        internal static StorageProjection WholeObject =>
            new(StorageProjectionKind.WholeObject, 0, 0);

        internal StorageProjection AppendField(PrimitiveType ownerType, int field)
        {
            return Kind switch
            {
                StorageProjectionKind.WholeObject => new StorageProjection(
                    StorageProjectionKind.InlineFieldPath, 0, 0,
                    new FieldProjectionPath().Append(ownerType, field)),
                StorageProjectionKind.InlineFieldPath => this with
                {
                    FieldPath = FieldPath.Append(ownerType, field),
                },
                _ => new StorageProjection(StorageProjectionKind.UnknownElement, 0, 0),
            };
        }

        internal static StorageProjection Combine(
            StorageProjection prefix, StorageProjection suffix)
        {
            if (suffix.Kind == StorageProjectionKind.WholeObject)
            {
                return prefix;
            }
            if (prefix.Kind == StorageProjectionKind.WholeObject)
            {
                return suffix;
            }
            return prefix.Kind == StorageProjectionKind.InlineFieldPath
                && suffix.Kind == StorageProjectionKind.InlineFieldPath
                    ? new StorageProjection(StorageProjectionKind.InlineFieldPath,
                        0, 0, prefix.FieldPath.Append(suffix.FieldPath))
                    : new StorageProjection(StorageProjectionKind.UnknownElement, 0, 0);
        }

        internal static StorageProjection ForArrayElement(BoundExpression index)
        {
            if (index is BoundConstant constant && constant.TryGetLegacyValue(out var value))
            {
                return new StorageProjection(StorageProjectionKind.ArrayElement, value, 1);
            }
            return new StorageProjection(StorageProjectionKind.UnknownElement, 0, 0);
        }

        internal static StorageProjection ForStorageAddress(BoundNativeStorageAddress address) =>
            address.CarrierType == PrimitiveType.NativeFixedArray
                ? new StorageProjection(StorageProjectionKind.ElementRange, 0, address.ElementCount)
                : WholeObject;
    }

    private readonly record struct StorageInstance(int SupportingScopeActivation, long LifetimeGeneration)
    {
        internal bool IsKnown => SupportingScopeActivation >= 0
            && LifetimeGeneration != StorageKey.UnknownGeneration;
    }

    private readonly record struct StorageKey(
        int Activation, int Site, StorageInstance Instance, StorageProjection Projection)
    {
        internal const long UnknownGeneration = long.MinValue;
        internal bool IsKnown => Activation >= 0 && Instance.IsKnown;
    }

    private readonly record struct ProvenanceAnchor(
        ProvenanceAnchorKind Kind, int Identity, StorageKey? Storage)
    {
        internal static ProvenanceAnchor ForBackingSource(int source)
            => source >= 0
                ? new ProvenanceAnchor(ProvenanceAnchorKind.BackingSource, source, null)
                : new ProvenanceAnchor(ProvenanceAnchorKind.IncomingBacking, ~source, null);

        internal static ProvenanceAnchor ForNativeStorage(int activation, int variable,
            StorageInstance instance, StorageProjection projection)
            => new(ProvenanceAnchorKind.NativeStorage, variable,
                new StorageKey(activation, variable, instance, projection));
    }

    private static readonly HashSet<ProvenanceAnchor> EmptyBackingSources
        = new HashSet<ProvenanceAnchor>();

    private readonly record struct StagedDependencySource(
        BoundCall? Call,
        int ParameterIndex,
        HashSet<ProvenanceAnchor> BackingSources,
        HashSet<ProvenanceAnchor> NativePointerSources,
        bool NativePointerMayBeUnknown);

    private sealed class NativePointerFieldValue
    {
        internal HashSet<ProvenanceAnchor> Sources { get; }
        internal bool MayBeUnknown { get; set; }

        internal NativePointerFieldValue(IReadOnlySet<ProvenanceAnchor> sources,
            bool mayBeUnknown)
        {
            Sources = [.. sources];
            MayBeUnknown = mayBeUnknown;
        }

        internal NativePointerFieldValue Clone()
            => new(Sources, MayBeUnknown);
    }

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
                    || (role == TokenSpanTypeRole.Snapshot
                        && !TokenSpanContract.ValidSnapshot(structure))))
            {
                throw InvalidEffect(default, "Ownership");
            }
            if ((structure.Fields.Any(static field => field.Type == PrimitiveType.TokenSpan)
                && !TokenSpanContract.ValidCursor(structure))
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
            var resultDependency = function.ReturnType == PrimitiveType.TokenSpan
                ? ResultDependencyContract(function) : null;
            var resultSources = resultDependency?.Sources ?? [];
            var expectedResultParameters = resultSources
                .Where(static source => source.Kind == BoundDependencySourceKind.Formal)
                .Select(static source => source.FormalOrdinal!.Value)
                .ToImmutableArray();
            var expectedResultReceiver = resultSources.Any(static source =>
                source.Kind == BoundDependencySourceKind.Receiver);
            var forwardedSpanResult = function.ReturnType == PrimitiveType.TokenSpan
                && !resultSources.IsEmpty;
            if (function.ReturnsForwardedBacking != forwardedSpanResult
                || !function.ResultDependencyParameters.SequenceEqual(expectedResultParameters)
                || function.ResultDependencyIncludesReceiver != expectedResultReceiver)
            {
                throw InvalidEffect(function.Span, "Ownership");
            }
            var cursorConstructor = function.IsConstructor
                && TokenSpanContract.IsCursor(function.ReturnType, program.Structures);
            var capture = cursorConstructor ? ReceiverCaptureContract(function) : null;
            var expectedCapturedParameters = capture is null
                ? ImmutableArray<int>.Empty
                : [
                    .. capture.Sources
                    .Where(static source => source.Kind == BoundDependencySourceKind.Formal)
                    .Select(static source => source.FormalOrdinal!.Value + 1),
                ];
            if (!function.CapturedParameters.SequenceEqual(expectedCapturedParameters))
            {
                throw InvalidEffect(function.Span, "Ownership");
            }

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
                || (TokenSpanContract.IsDependent(function.ReturnType, program.Structures)
                    && !forwardedSpanResult))
            {
                throw InvalidEffect(function.Span, "Ownership");
            }
            if (forwardedSpanResult)
            {
                if (function.IsConstructor || function.IsExternal || function.Intrinsic != BoundIntrinsic.None
                    || !function.IsInternal || function.ParameterCount == 0)
                {
                    throw InvalidEffect(function.Span, "Ownership");
                }
                var receiverOffset = function.DeclaringStruct is not null
                    && !function.IsStatic ? 1 : 0;
                var explicitParameterCount = function.ParameterCount - receiverOffset;
                if (function.ResultDependencyIncludesReceiver
                    && (receiverOffset == 0 || function.Variables.Length == 0
                        || !IsSpanResultReceiver(function.Variables[0].Type, program)))
                {
                    throw InvalidEffect(function.Span, "Ownership");
                }
                var seen = new HashSet<int>();
                foreach (var parameter in function.ResultDependencyParameters)
                {
                    var slot = parameter + receiverOffset;
                    if ((uint)parameter >= (uint)explicitParameterCount || !seen.Add(parameter)
                        || (uint)slot >= (uint)function.Variables.Length
                        || function.Variables[slot] is not {
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
                        || (variable.Type == PrimitiveType.TokenSpan && variable.IsMutable)))
                {
                    throw InvalidEffect(variable.Span, "Ownership");
                }
            }
        }
        return hasSpans;
    }

    private static bool IsSpanResultReceiver(PrimitiveType type,
        BoundProgram program)
    {
        if (TokenSpanContract.IsDependent(type, program.Structures))
        {
            return true;
        }

        return IsTokenSnapshotType(type, program);
    }

    private static BoundDependencyContract? ResultDependencyContract(
        BoundFunction function)
        => function.DependencyContracts.FirstOrDefault(static contract =>
            contract.Relation == BoundDependencyRelation.DependsOn
            && contract.Destination == BoundDependencyDestination.Result);

    private static BoundDependencyContract? ReceiverCaptureContract(
        BoundFunction function)
        => function.DependencyContracts.FirstOrDefault(static contract =>
            contract.Relation == BoundDependencyRelation.Captures
            && contract.Destination == BoundDependencyDestination.Receiver);

    private static BoundExpression DependencyCallArgument(BoundCall call,
        BoundFunction callee, BoundDependencySource source)
    {
        var receiverOffset = callee.DeclaringStruct is not null
            && !callee.IsStatic && !callee.IsConstructor && !callee.IsClassConstructor ? 1 : 0;
        var parameter = source.Kind switch
        {
            BoundDependencySourceKind.Receiver when receiverOffset != 0 => 0,
            BoundDependencySourceKind.Formal when source.FormalOrdinal is int ordinal
                => ordinal + receiverOffset,
            _ => throw Invalid(call.Span),
        };
        var argument = call.ArgumentIndex(parameter);
        return (uint)argument < (uint)call.Arguments.Length
            ? call.Arguments[argument] : throw Invalid(call.Span);
    }

    private sealed class BackingDependencies
    {
        // A slot conservatively retains every source it can hold on any path. Liveness
        // remains path-sensitive in InitializationState, including unwind replay.
        internal HashSet<ProvenanceAnchor>?[] Sources { get; }
        internal HashSet<ProvenanceAnchor>?[] NativePointerSources { get; }
        internal bool[] NativePointerMayBeUnknown { get; }
        private Dictionary<StorageKey, NativePointerFieldValue> NativePointerFieldValues { get; } = [];
        private CopyOriginFact[] CopyOriginRoots { get; }
        private Dictionary<StorageKey, CopyOriginFact> CopyOriginFieldValues { get; } = [];
        private HashSet<StorageInstance>[] StorageInstances { get; }
        private StorageActivationContext StorageActivationContext { get; }
        internal int FunctionActivation { get; }
        internal BoundFunction Function { get; }
        internal const int RootScopeActivation = 0;
        internal List<StagedDependencySource> Staged { get; } = [];

        internal BackingDependencies(int functionActivation, BoundFunction function, int length)
            : this(functionActivation, function, length, new StorageActivationContext())
        {
        }

        private BackingDependencies(int functionActivation, BoundFunction function, int length,
            StorageActivationContext storageActivationContext)
        {
            FunctionActivation = functionActivation;
            Function = function;
            StorageActivationContext = storageActivationContext;
            Sources = new HashSet<ProvenanceAnchor>?[length];
            NativePointerSources = new HashSet<ProvenanceAnchor>?[length];
            NativePointerMayBeUnknown = new bool[length];
            CopyOriginRoots = new CopyOriginFact[length];
            StorageInstances = new HashSet<StorageInstance>[length];
            for (var slot = 0; slot < length; slot++)
            {
                CopyOriginRoots[slot] = CopyOriginFact.Unavailable();
                StorageInstances[slot] = [new StorageInstance(RootScopeActivation, 0)];
            }
        }

        internal BackingDependencies Clone()
        {
            var clone = new BackingDependencies(FunctionActivation, Function, Sources.Length,
                StorageActivationContext);
            CopyTo(clone);
            return clone;
        }

        internal void CopyTo(BackingDependencies destination)
        {
            if (destination.Sources.Length != Sources.Length
                || destination.FunctionActivation != FunctionActivation
                || !ReferenceEquals(destination.StorageActivationContext, StorageActivationContext))
            {
                throw Invalid();
            }

            for (var slot = 0; slot < Sources.Length; slot++)
            {
                destination.Sources[slot] = CloneSet(Sources[slot]);
                destination.NativePointerSources[slot] = CloneSet(NativePointerSources[slot]);
                destination.NativePointerMayBeUnknown[slot] = NativePointerMayBeUnknown[slot];
                destination.CopyOriginRoots[slot] = CopyOriginRoots[slot].Clone();
                destination.StorageInstances[slot] = [.. StorageInstances[slot]];
            }
            destination.Staged.Clear();
            foreach (var staged in Staged)
            {
                destination.Staged.Add(staged with {
                    BackingSources = [.. staged.BackingSources],
                    NativePointerSources = [.. staged.NativePointerSources],
                });
            }
            destination.NativePointerFieldValues.Clear();
            foreach (var (key, value) in NativePointerFieldValues)
            {
                destination.NativePointerFieldValues.Add(key, value.Clone());
            }
            destination.CopyOriginFieldValues.Clear();
            foreach (var (key, value) in CopyOriginFieldValues)
            {
                destination.CopyOriginFieldValues.Add(key, value.Clone());
            }
        }

        internal bool MergeSlot(int slot, BackingDependencies other)
        {
            if ((uint)slot >= (uint)Sources.Length || other.Sources.Length != Sources.Length)
            {
                throw Invalid();
            }

            var changed = MergeSet(ref Sources[slot], other.Sources[slot]);
            changed |= MergeSet(ref NativePointerSources[slot], other.NativePointerSources[slot]);
            changed |= CopyOriginRoots[slot].Merge(other.CopyOriginRoots[slot]);
            if (other.NativePointerMayBeUnknown[slot] && !NativePointerMayBeUnknown[slot])
            {
                NativePointerMayBeUnknown[slot] = true;
                changed = true;
            }
            if (other.FunctionActivation != FunctionActivation
                || !ReferenceEquals(other.StorageActivationContext, StorageActivationContext))
            {
                throw Invalid();
            }
            var currentInstances = new HashSet<StorageInstance>(StorageInstances[slot]);
            var priorInstanceCount = StorageInstances[slot].Count;
            StorageInstances[slot].UnionWith(other.StorageInstances[slot]);
            changed |= priorInstanceCount != StorageInstances[slot].Count;

            var fieldKeys = NativePointerFieldValues.Keys
                .Concat(other.NativePointerFieldValues.Keys)
                .Where(key => key.Site == slot)
                .Distinct()
                .ToArray();
            foreach (var key in fieldKeys)
            {
                var hasCurrent = NativePointerFieldValues.TryGetValue(key, out var currentValue);
                var hasOther = other.NativePointerFieldValues.TryGetValue(key, out var otherValue);
                if (hasCurrent && hasOther)
                {
                    var currentCount = currentValue!.Sources.Count;
                    currentValue.Sources.UnionWith(otherValue!.Sources);
                    changed |= currentCount != currentValue.Sources.Count;
                    if (otherValue.MayBeUnknown && !currentValue.MayBeUnknown)
                    {
                        currentValue.MayBeUnknown = true;
                        changed = true;
                    }
                }
                else if (hasCurrent)
                {
                    if (other.StorageInstances[slot].Contains(key.Instance)
                        && !currentValue!.MayBeUnknown)
                    {
                        currentValue.MayBeUnknown = true;
                        changed = true;
                    }
                }
                else
                {
                    currentValue = otherValue!.Clone();
                    if (currentInstances.Contains(key.Instance)
                        && !currentValue.MayBeUnknown)
                    {
                        currentValue.MayBeUnknown = true;
                    }
                    NativePointerFieldValues.Add(key, currentValue);
                    changed = true;
                }
            }
            var copyOriginKeys = CopyOriginFieldValues.Keys
                .Concat(other.CopyOriginFieldValues.Keys)
                .Where(key => key.Site == slot)
                .Distinct()
                .ToArray();
            foreach (var key in copyOriginKeys)
            {
                var hasCurrent = CopyOriginFieldValues.TryGetValue(key, out var currentValue);
                var hasOther = other.CopyOriginFieldValues.TryGetValue(key, out var otherValue);
                if (hasCurrent && hasOther)
                {
                    changed |= currentValue!.Merge(otherValue!);
                }
                else if (hasCurrent)
                {
                    if (other.StorageInstances[slot].Contains(key.Instance))
                    {
                        changed |= currentValue!.Merge(
                            other.CopyOriginRoots[slot].Project(key.Projection.FieldPath));
                    }
                }
                else
                {
                    currentValue = otherValue!.Clone();
                    if (currentInstances.Contains(key.Instance))
                    {
                        changed |= currentValue.Merge(
                            CopyOriginRoots[slot].Project(key.Projection.FieldPath));
                    }
                    CopyOriginFieldValues.Add(key, currentValue);
                    changed = true;
                }
            }
            return changed;
        }

        internal bool CopySlot(int slot, BackingDependencies other)
        {
            if ((uint)slot >= (uint)Sources.Length || other.Sources.Length != Sources.Length)
            {
                throw Invalid();
            }

            var changed = !SetEquals(Sources[slot], other.Sources[slot])
                || !SetEquals(NativePointerSources[slot], other.NativePointerSources[slot])
                || NativePointerMayBeUnknown[slot] != other.NativePointerMayBeUnknown[slot]
                || !CopyOriginRoots[slot].IsEquivalentTo(other.CopyOriginRoots[slot])
                || !StorageInstances[slot].SetEquals(other.StorageInstances[slot])
                || !NativePointerFieldValuesEqualForSlot(slot, other);
            changed |= !CopyOriginFieldValuesEqualForSlot(slot, other);
            Sources[slot] = CloneSet(other.Sources[slot]);
            NativePointerSources[slot] = CloneSet(other.NativePointerSources[slot]);
            NativePointerMayBeUnknown[slot] = other.NativePointerMayBeUnknown[slot];
            CopyOriginRoots[slot] = other.CopyOriginRoots[slot].Clone();
            StorageInstances[slot] = [.. other.StorageInstances[slot]];
            ClearNativePointerFieldValues(slot);
            foreach (var (key, value) in other.NativePointerFieldValues)
            {
                if (key.Site == slot)
                {
                    NativePointerFieldValues.Add(key, value.Clone());
                }
            }
            ClearCopyOriginFieldValues(slot);
            foreach (var (key, value) in other.CopyOriginFieldValues)
            {
                if (key.Site == slot)
                {
                    CopyOriginFieldValues.Add(key, value.Clone());
                }
            }
            return changed;
        }

        internal HashSet<StorageInstance> StorageInstanceSet(int slot)
        {
            if ((uint)slot >= (uint)StorageInstances.Length)
            {
                throw Invalid();
            }
            return StorageInstances[slot];
        }

        internal int EnterBlockActivation(int parent, TextSpan span) =>
            StorageActivationContext.Enter(parent, span, StorageActivationKind.Block);

        internal int EnterLoopActivation(int parent, TextSpan span, bool isReentry) =>
            StorageActivationContext.Enter(parent, span, isReentry
                ? StorageActivationKind.LoopReentry : StorageActivationKind.LoopFirstEntry);

        internal void ActivateStorage(int slot, long generation, int supportingScopeActivation)
        {
            if ((uint)slot >= (uint)StorageInstances.Length || generation == StorageKey.UnknownGeneration
                || supportingScopeActivation < 0)
            {
                throw Invalid();
            }
            ClearCopyOriginFacts(slot);
            StorageInstances[slot] = [new StorageInstance(supportingScopeActivation, generation)];
        }

        internal void RetireStorage(int slot, long generation)
        {
            if ((uint)slot >= (uint)StorageInstances.Length || generation == StorageKey.UnknownGeneration)
            {
                throw Invalid();
            }
            ClearCopyOriginFacts(slot);
            StorageInstances[slot] = [.. StorageInstances[slot]
                .Select(instance => instance with { LifetimeGeneration = generation })];
        }

        internal void SetCopyOriginRoot(int slot, CopyOriginFact fact)
        {
            if ((uint)slot >= (uint)CopyOriginRoots.Length || fact is null || !fact.IsValid)
            {
                throw Invalid();
            }
            CopyOriginRoots[slot] = fact.Clone();
        }

        internal CopyOriginFact CopyOriginRoot(int slot)
        {
            if ((uint)slot >= (uint)CopyOriginRoots.Length)
            {
                throw Invalid();
            }
            return CopyOriginRoots[slot].Clone();
        }

        internal void SetCopyOriginFieldValue(
            int slot, StorageProjection projection, CopyOriginFact fact)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty
                || fact is null || !fact.IsValid)
            {
                throw Invalid();
            }

            foreach (var instance in StorageInstances[slot])
            {
                var key = new StorageKey(FunctionActivation, slot, instance, projection);
                CopyOriginFieldValues[key] = fact.Clone();
            }
        }

        internal bool TryGetCopyOriginFieldValue(
            int slot, StorageProjection projection, out CopyOriginFact fact)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty)
            {
                throw Invalid();
            }

            var found = false;
            CopyOriginFact? result = null;
            foreach (var instance in StorageInstances[slot])
            {
                var key = new StorageKey(FunctionActivation, slot, instance, projection);
                CopyOriginFact value;
                if (CopyOriginFieldValues.TryGetValue(key, out var existing)
                    && existing is not null)
                {
                    value = existing;
                    found = true;
                }
                else
                {
                    value = CopyOriginRoots[slot].Project(projection.FieldPath);
                }
                if (result is null)
                {
                    result = value.Clone();
                }
                else
                {
                    _ = result.Merge(value);
                }
            }

            fact = result ?? CopyOriginFact.Unavailable();
            return found;
        }

        private void ClearCopyOriginFacts(int slot)
        {
            CopyOriginRoots[slot] = CopyOriginFact.Unavailable();
            ClearCopyOriginFieldValues(slot);
        }

        internal void ClearCopyOriginFieldValues(int slot)
        {
            foreach (var key in CopyOriginFieldValues.Keys
                .Where(key => key.Site == slot).ToArray())
            {
                _ = CopyOriginFieldValues.Remove(key);
            }
        }

        internal void ClearCopyOriginFieldValues(int slot, StorageProjection prefix)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || prefix.Kind is not (StorageProjectionKind.WholeObject
                    or StorageProjectionKind.InlineFieldPath))
            {
                throw Invalid();
            }
            if (prefix.Kind == StorageProjectionKind.WholeObject)
            {
                ClearCopyOriginFieldValues(slot);
                return;
            }

            foreach (var key in CopyOriginFieldValues.Keys.Where(key =>
                key.Site == slot
                && prefix.FieldPath.IsPrefixOf(key.Projection.FieldPath)).ToArray())
            {
                _ = CopyOriginFieldValues.Remove(key);
            }
        }

        private bool CopyOriginFieldValuesEqualForSlot(int slot, BackingDependencies other)
        {
            foreach (var (key, value) in CopyOriginFieldValues)
            {
                if (key.Site != slot)
                {
                    continue;
                }
                if (!other.CopyOriginFieldValues.TryGetValue(key, out var otherValue)
                    || !value.IsEquivalentTo(otherValue))
                {
                    return false;
                }
            }
            foreach (var key in other.CopyOriginFieldValues.Keys)
            {
                if (key.Site == slot && !CopyOriginFieldValues.ContainsKey(key))
                {
                    return false;
                }
            }
            return true;
        }

        internal void SetBackingSources(int slot, IReadOnlySet<ProvenanceAnchor> sources)
        {
            if ((uint)slot >= (uint)Sources.Length)
            {
                throw Invalid();
            }
            Sources[slot] = sources.Count == 0 ? null : [.. sources];
        }

        internal void AddBackingSources(int slot, IReadOnlySet<ProvenanceAnchor> sources)
        {
            if ((uint)slot >= (uint)Sources.Length)
            {
                throw Invalid();
            }
            _ = MergeSet(ref Sources[slot], sources);
        }

        internal void ClearBackingSources(int slot)
        {
            if ((uint)slot >= (uint)Sources.Length)
            {
                throw Invalid();
            }
            Sources[slot] = null;
        }

        internal void SetNativePointerProvenance(int slot,
            IReadOnlySet<ProvenanceAnchor> sources, bool mayBeUnknown)
        {
            if ((uint)slot >= (uint)NativePointerSources.Length)
            {
                throw Invalid();
            }
            NativePointerSources[slot] = sources.Count == 0 ? null : [.. sources];
            NativePointerMayBeUnknown[slot] = mayBeUnknown;
        }

        internal void SetNativePointerAggregateProvenance(int slot,
            IReadOnlySet<ProvenanceAnchor> sources, bool mayBeUnknown)
        {
            if ((uint)slot >= (uint)NativePointerSources.Length)
            {
                throw Invalid();
            }
            NativePointerSources[slot] = sources.Count == 0 ? null : [.. sources];
            NativePointerMayBeUnknown[slot] = mayBeUnknown;
        }

        internal void AddNativePointerProvenance(int slot,
            IReadOnlySet<ProvenanceAnchor> sources, bool mayBeUnknown)
        {
            if ((uint)slot >= (uint)NativePointerSources.Length)
            {
                throw Invalid();
            }
            _ = MergeSet(ref NativePointerSources[slot], sources);
            NativePointerMayBeUnknown[slot] |= mayBeUnknown;
        }

        internal void ClearNativePointerProvenance(int slot)
        {
            if ((uint)slot >= (uint)NativePointerSources.Length)
            {
                throw Invalid();
            }
            NativePointerSources[slot] = null;
            NativePointerMayBeUnknown[slot] = false;
            ClearNativePointerFieldValues(slot);
        }

        internal void MarkAllNativePointerProvenanceUnknown()
        {
            Array.Fill(NativePointerMayBeUnknown, true);
            foreach (var fieldValue in NativePointerFieldValues.Values)
            {
                fieldValue.MayBeUnknown = true;
            }
        }

        internal void SetNativePointerFieldValue(int slot, StorageProjection projection,
            IReadOnlySet<ProvenanceAnchor> sources, bool mayBeUnknown)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty)
            {
                throw Invalid();
            }

            foreach (var instance in StorageInstances[slot])
            {
                var key = new StorageKey(FunctionActivation, slot, instance, projection);
                NativePointerFieldValues[key] = new NativePointerFieldValue(
                    sources, mayBeUnknown);
            }
        }

        internal bool TryGetNativePointerFieldValue(int slot,
            StorageProjection projection, out HashSet<ProvenanceAnchor> sources,
            out bool mayBeUnknown)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty)
            {
                throw Invalid();
            }

            sources = [];
            mayBeUnknown = false;
            var found = false;
            foreach (var instance in StorageInstances[slot])
            {
                var key = new StorageKey(FunctionActivation, slot, instance, projection);
                if (NativePointerFieldValues.TryGetValue(key, out var value))
                {
                    sources.UnionWith(value.Sources);
                    mayBeUnknown |= value.MayBeUnknown;
                    found = true;
                }
                else
                {
                    mayBeUnknown = true;
                }
            }

            return found;
        }

        private void ClearNativePointerFieldValues(int slot)
        {
            foreach (var key in NativePointerFieldValues.Keys
                .Where(key => key.Site == slot).ToArray())
            {
                _ = NativePointerFieldValues.Remove(key);
            }
        }

        private bool NativePointerFieldValuesEqualForSlot(
            int slot, BackingDependencies other)
        {
            foreach (var (key, value) in NativePointerFieldValues)
            {
                if (key.Site != slot)
                {
                    continue;
                }
                if (!other.NativePointerFieldValues.TryGetValue(key, out var otherValue)
                    || value.MayBeUnknown != otherValue.MayBeUnknown
                    || !value.Sources.SetEquals(otherValue.Sources))
                {
                    return false;
                }
            }
            foreach (var key in other.NativePointerFieldValues.Keys)
            {
                if (key.Site == slot && !NativePointerFieldValues.ContainsKey(key))
                {
                    return false;
                }
            }
            return true;
        }

        internal void TransferBackingSource(int source, int destination)
        {
            if ((uint)source >= (uint)Sources.Length
                || (uint)destination >= (uint)Sources.Length)
            {
                throw Invalid();
            }
            if (source == destination)
            {
                return;
            }

            var sourceBacking = CloneSet(Sources[source]);
            var sourcePointers = CloneSet(NativePointerSources[source]);
            var sourcePointerMayBeUnknown = NativePointerMayBeUnknown[source];
            var sourceFieldValues = NativePointerFieldValues
                .Where(pair => pair.Key.Site == source)
                .Select(static pair => (pair.Key.Projection, Value: pair.Value.Clone()))
                .ToArray();
            var sourceCopyOrigins = CopyOriginRoots[source].Clone();
            var sourceCopyOriginFields = CopyOriginFieldValues
                .Where(pair => pair.Key.Site == source)
                .Select(static pair => (pair.Key.Projection, Value: pair.Value.Clone()))
                .ToArray();
            for (var slot = 0; slot < Sources.Length; slot++)
            {
                RemapSource(Sources[slot], source, destination);
            }
            for (var index = 0; index < Staged.Count; index++)
            {
                var sourceAnchor = ProvenanceAnchor.ForBackingSource(source);
                var staged = Staged[index];
                if (!staged.BackingSources.Contains(sourceAnchor))
                {
                    continue;
                }
                var sources = new HashSet<ProvenanceAnchor>(staged.BackingSources);
                RemapSource(sources, source, destination);
                Staged[index] = staged with { BackingSources = sources };
            }

            if (sourceBacking is not null)
            {
                RemapSource(sourceBacking, source, destination);
            }
            Sources[destination] = sourceBacking;
            Sources[source] = null;
            NativePointerSources[destination] = sourcePointers;
            NativePointerSources[source] = null;
            NativePointerMayBeUnknown[destination] = sourcePointerMayBeUnknown;
            NativePointerMayBeUnknown[source] = false;
            CopyOriginRoots[destination] = sourceCopyOrigins;
            CopyOriginRoots[source] = CopyOriginFact.Unavailable();
            ClearNativePointerFieldValues(destination);
            foreach (var (projection, value) in sourceFieldValues)
            {
                foreach (var instance in StorageInstances[destination])
                {
                    NativePointerFieldValues.Add(new StorageKey(
                        FunctionActivation, destination, instance, projection), value.Clone());
                }
            }
            ClearCopyOriginFieldValues(destination);
            ClearCopyOriginFieldValues(source);
            foreach (var (projection, value) in sourceCopyOriginFields)
            {
                foreach (var instance in StorageInstances[destination])
                {
                    CopyOriginFieldValues.Add(new StorageKey(
                        FunctionActivation, destination, instance, projection), value.Clone());
                }
            }
        }

        private static HashSet<ProvenanceAnchor>? CloneSet(
            HashSet<ProvenanceAnchor>? sources)
            => sources is null ? null : [.. sources];

        private static bool MergeSet(ref HashSet<ProvenanceAnchor>? destination,
            IReadOnlySet<ProvenanceAnchor>? additions)
        {
            if (additions is null || additions.Count == 0)
            {
                return false;
            }
            if (destination is null)
            {
                destination = [.. additions];
                return true;
            }
            var priorCount = destination.Count;
            destination.UnionWith(additions);
            return priorCount != destination.Count;
        }

        private static bool SetEquals(IReadOnlySet<ProvenanceAnchor>? left,
            IReadOnlySet<ProvenanceAnchor>? right)
            => left is null ? right is null || right.Count == 0
                : right is not null && left.SetEquals(right);

        private static void RemapSource(HashSet<ProvenanceAnchor>? sources,
            int source, int destination)
        {
            var sourceAnchor = ProvenanceAnchor.ForBackingSource(source);
            if (sources?.Remove(sourceAnchor) == true)
            {
                _ = sources.Add(ProvenanceAnchor.ForBackingSource(destination));
            }
        }
    }

    private readonly record struct NativePointerWriteEffect(int DestinationParameter, int ValueParameter);

    private readonly record struct NativePointerCallSite(int CallerFunction, BoundCall Call);

    private static HashSet<NativePointerWriteEffect>[] CreateNativePointerCaptureEffects(
        BoundProgram program)
    {
        var effects = new HashSet<NativePointerWriteEffect>[program.Functions.Length];
        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            if (function.DependencyContracts.IsDefault)
            {
                throw InvalidEffect(function.Span, "Ownership");
            }

            var functionEffects = effects[functionIndex] = [];
            var receiverOffset = function.DeclaringStruct is not null
                && !function.IsStatic && !function.IsConstructor && !function.IsClassConstructor
                    ? 1 : 0;
            var variableOffset = function.IsConstructor ? 1 : receiverOffset;
            foreach (var contract in function.DependencyContracts)
            {
                if (contract.Relation != BoundDependencyRelation.Captures
                    || contract.Destination != BoundDependencyDestination.RetainedFormal
                    || contract.DestinationOrdinal is not int destinationOrdinal)
                {
                    continue;
                }

                var destinationSlot = destinationOrdinal + variableOffset;
                if ((uint)destinationSlot >= (uint)function.Variables.Length
                    || !IsNativePointerHolder(function.Variables[destinationSlot].Type, program))
                {
                    continue;
                }

                foreach (var source in contract.Sources)
                {
                    var sourceParameter = source.Kind switch
                    {
                        BoundDependencySourceKind.Formal
                            when source.FormalOrdinal is int ordinal => ordinal + receiverOffset,
                        BoundDependencySourceKind.Receiver when receiverOffset != 0 => 0,
                        _ => -1,
                    };
                    var sourceSlot = source.Kind switch
                    {
                        BoundDependencySourceKind.Formal
                            when source.FormalOrdinal is int ordinal => ordinal + variableOffset,
                        BoundDependencySourceKind.Receiver when receiverOffset != 0 => 0,
                        _ => -1,
                    };
                    if ((uint)sourceSlot < (uint)function.Variables.Length
                        && sourceParameter >= 0
                        && CarriesNativePointer(function.Variables[sourceSlot].Type, program))
                    {
                        _ = functionEffects.Add(new NativePointerWriteEffect(
                            destinationOrdinal + receiverOffset, sourceParameter));
                    }
                }
            }
        }
        return effects;
    }

    private static bool IsNativePointerHolder(PrimitiveType type, BoundProgram program)
    {
        if (!NativePointerTypeContract.IsValidType(type, program.NativePointerTypes))
        {
            return false;
        }

        var pointer = program.NativePointerTypes[NativePointerTypes.Index(type)];
        return pointer.IsWritable && CarriesNativePointer(pointer.PointeeType, program);
    }

    private static void ValidateNativePointerWriteContracts(BoundProgram program,
        HashSet<NativePointerWriteEffect>[] actualEffects,
        HashSet<NativePointerWriteEffect>[] declaredEffects)
    {
        if (actualEffects.Length != program.Functions.Length
            || declaredEffects.Length != program.Functions.Length)
        {
            throw Invalid();
        }

        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            foreach (var effect in actualEffects[functionIndex])
            {
                if (!declaredEffects[functionIndex].Contains(effect))
                {
                    throw InvalidEffect(program.Functions[functionIndex].Span, "Ownership");
                }
            }
        }
    }

    // Body-derived effects validate each source declaration; callers consume only declared captures.
    private static HashSet<NativePointerWriteEffect>[] CreateNativePointerWriteEffects(
        BoundProgram program, HashSet<NativePointerWriteEffect>[] declaredEffects)
    {
        var summaries = new HashSet<NativePointerWriteEffect>[program.Functions.Length];
        var callersByCallee = new List<NativePointerCallSite>?[program.Functions.Length];
        var parameterSources = new HashSet<int>?[program.Functions.Length][];
        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            summaries[functionIndex] = [];
        }
        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            var hasBody = !function.IsExternal && function.Intrinsic == BoundIntrinsic.None
                && function.IndexedBody is null;
            parameterSources[functionIndex] = CreateNativePointerParameterSources(function, program, hasBody);
            if (!hasBody)
            {
                continue;
            }
            var functionParameterSources = parameterSources[functionIndex];
            if (!BoundTreeTraversal.VisitExpressions(function.Body, expression =>
                {
                    if (expression is BoundNativePointerWrite write)
                    {
                        var destinations = NativePointerParameterSources(write.Address, function, program,
                            functionParameterSources);
                        var values = NativePointerParameterSources(write.Value, function, program,
                            functionParameterSources);
                        foreach (var destination in destinations)
                        {
                            foreach (var value in values)
                            {
                                _ = summaries[functionIndex].Add(
                                    new NativePointerWriteEffect(destination, value));
                            }
                        }
                    }
                    if (expression is BoundCall call && (uint)call.Function < (uint)program.Functions.Length)
                    {
                        var callee = program.Functions[call.Function];
                        if (declaredEffects[call.Function].Count > 0)
                        {
                            var callers = callersByCallee[call.Function];
                            if (callers is null)
                            {
                                callers = [];
                                callersByCallee[call.Function] = callers;
                            }
                            callers.Add(new NativePointerCallSite(functionIndex, call));
                        }
                    }
                }))
            {
                throw Invalid(function.Span);
            }
        }

        var pending = new Queue<int>();
        var queued = new bool[program.Functions.Length];
        for (var functionIndex = 0; functionIndex < summaries.Length; functionIndex++)
        {
            if (declaredEffects[functionIndex].Count > 0)
            {
                pending.Enqueue(functionIndex);
                queued[functionIndex] = true;
            }
        }
        var remaining = MaximumAnalysisVisits;
        while (pending.TryDequeue(out var calleeIndex))
        {
            queued[calleeIndex] = false;
            if (callersByCallee[calleeIndex] is not { } callers)
            {
                continue;
            }
            var callee = program.Functions[calleeIndex];
            foreach (var callSite in callers)
            {
                Count(ref remaining, 0);
                var caller = program.Functions[callSite.CallerFunction];
                var changed = false;
                foreach (var effect in declaredEffects[calleeIndex])
                {
                    var destination = NativePointerCallArgument(callSite.Call, callee,
                        effect.DestinationParameter);
                    var value = NativePointerCallArgument(callSite.Call, callee, effect.ValueParameter);
                    var callerParameterSources = parameterSources[callSite.CallerFunction];
                    var destinations = NativePointerParameterSources(destination, caller, program,
                        callerParameterSources);
                    var values = NativePointerParameterSources(value, caller, program,
                        callerParameterSources);
                    foreach (var destinationParameter in destinations)
                    {
                        foreach (var valueParameter in values)
                        {
                            Count(ref remaining, 0);
                            changed |= summaries[callSite.CallerFunction].Add(
                                new NativePointerWriteEffect(destinationParameter, valueParameter));
                        }
                    }
                }
                if (changed && !queued[callSite.CallerFunction])
                {
                    pending.Enqueue(callSite.CallerFunction);
                    queued[callSite.CallerFunction] = true;
                }
            }
        }
        return summaries;
    }

    private static HashSet<int>?[] CreateNativePointerParameterSources(BoundFunction function,
        BoundProgram program, bool includeLocalAliases)
    {
        var sources = new HashSet<int>?[function.Variables.Length];
        var destinationsBySource = new HashSet<int>?[function.Variables.Length];
        var pending = new Queue<int>();
        for (var parameter = 0; parameter < function.ParameterCount; parameter++)
        {
            var variable = parameter + (function.IsConstructor ? 1 : 0);
            if ((uint)variable >= (uint)function.Variables.Length)
            {
                throw Invalid(function.Span);
            }
            if (CarriesNativePointer(function.Variables[variable].Type, program))
            {
                sources[variable] = [parameter];
                pending.Enqueue(variable);
            }
        }
        if (!includeLocalAliases)
        {
            return sources;
        }
        var remaining = MaximumAnalysisVisits;
        if (!BoundTreeTraversal.VisitStatements(function.Body, statement =>
            {
                switch (statement)
                {
                    case BoundLocal local:
                        AddAliases(local.Variable, local.Initializer);
                        break;
                    case BoundAssignment assignment:
                        AddAliases(assignment.Variable, assignment.Value);
                        break;
                }
            }))
        {
            throw Invalid(function.Span);
        }
        while (pending.TryDequeue(out var source))
        {
            Count(ref remaining, 0);
            if (destinationsBySource[source] is not { } destinations
                || sources[source] is not { Count: > 0 } sourceParameters)
            {
                continue;
            }
            foreach (var destination in destinations)
            {
                Count(ref remaining, 0);
                var destinationParameters = sources[destination] ??= [];
                var changed = false;
                foreach (var parameter in sourceParameters)
                {
                    Count(ref remaining, 0);
                    changed |= destinationParameters.Add(parameter);
                }
                if (changed)
                {
                    pending.Enqueue(destination);
                }
            }
        }
        return sources;

        void AddAliases(int destination, BoundExpression value)
        {
            Count(ref remaining, 0);
            if ((uint)destination >= (uint)function.Variables.Length)
            {
                throw Invalid(value.Span);
            }
            if (!CarriesNativePointer(function.Variables[destination].Type, program))
            {
                return;
            }
            var sourceVariables = new HashSet<int>();
            NativePointerAliasVariables(value, function, program, sourceVariables, ref remaining, 0);
            foreach (var sourceVariable in sourceVariables)
            {
                var destinations = destinationsBySource[sourceVariable] ??= [];
                _ = destinations.Add(destination);
            }
        }
    }

    private static void NativePointerAliasVariables(BoundExpression expression, BoundFunction function,
        BoundProgram program, HashSet<int> variables, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        switch (expression)
        {
            case BoundRead read when CarriesNativePointer(read.Type, program):
                ValidateNativePointerSourceVariable(read.Variable, read.Type, function);
                _ = variables.Add(read.Variable);
                break;
            case BoundMove move when CarriesNativePointer(move.Type, program):
                ValidateNativePointerSourceVariable(move.Variable, move.Type, function);
                _ = variables.Add(move.Variable);
                break;
            case BoundUnsafeExpression unsafeExpression:
                NativePointerAliasVariables(unsafeExpression.Expression, function, program, variables,
                    ref remaining, depth + 1);
                break;
            case BoundConditional conditional when CarriesNativePointer(conditional.Type, program):
                NativePointerAliasVariables(conditional.WhenTrue, function, program, variables,
                    ref remaining, depth + 1);
                NativePointerAliasVariables(conditional.WhenFalse, function, program, variables,
                    ref remaining, depth + 1);
                break;
        }
    }

    private static HashSet<int> NativePointerParameterSources(BoundExpression expression,
        BoundFunction function, BoundProgram program, HashSet<int>?[] parameterSources)
    {
        switch (expression)
        {
            case BoundRead read when CarriesNativePointer(read.Type, program):
                return NativePointerParameterSource(read.Variable, read.Type, function, parameterSources);
            case BoundMove move when CarriesNativePointer(move.Type, program):
                return NativePointerParameterSource(move.Variable, move.Type, function, parameterSources);
            case BoundUnsafeExpression unsafeExpression:
                return NativePointerParameterSources(unsafeExpression.Expression, function, program,
                    parameterSources);
            case BoundConditional conditional when CarriesNativePointer(conditional.Type, program):
                var sources = NativePointerParameterSources(conditional.WhenTrue, function, program,
                    parameterSources);
                sources.UnionWith(NativePointerParameterSources(conditional.WhenFalse, function, program,
                    parameterSources));
                return sources;
            default:
                return [];
        }
    }

    private static HashSet<int> NativePointerParameterSource(int variable, PrimitiveType type,
        BoundFunction function, HashSet<int>?[] parameterSources)
    {
        ValidateNativePointerSourceVariable(variable, type, function);
        if (parameterSources.Length != function.Variables.Length)
        {
            throw Invalid(function.Span);
        }
        return parameterSources[variable] is { Count: > 0 } sources ? [.. sources] : [];
    }

    private static void ValidateNativePointerSourceVariable(int variable, PrimitiveType type,
        BoundFunction function)
    {
        if ((uint)variable >= (uint)function.Variables.Length
            || function.Variables[variable].Type != type)
        {
            throw Invalid(function.Span);
        }
    }

    private static BoundExpression NativePointerCallArgument(BoundCall call, BoundFunction callee, int parameter)
    {
        if ((uint)parameter >= (uint)callee.ParameterCount || call.Arguments.IsDefault
            || (!call.ArgumentParameterIndices.IsDefault
                && call.ArgumentParameterIndices.Length != call.Arguments.Length))
        {
            throw Invalid(call.Span);
        }
        if (call.ArgumentParameterIndices.IsDefault)
        {
            return (uint)parameter < (uint)call.Arguments.Length
                ? call.Arguments[parameter] : throw Invalid(call.Span);
        }
        var argument = -1;
        for (var index = 0; index < call.ArgumentParameterIndices.Length; index++)
        {
            if (call.ArgumentParameterIndices[index] == parameter)
            {
                if (argument >= 0)
                {
                    throw Invalid(call.Span);
                }
                argument = index;
            }
        }
        return argument >= 0 && call.Arguments[argument] is { } expression
            ? expression : throw Invalid(call.Span);
    }

    private static void ApplyNativePointerCaptureEffects(BoundCall call, BoundFunction callee,
        InitializationState initialized, ref int remaining)
    {
        if (initialized.Dependencies is not BackingDependencies facts
            || initialized.NativePointerCaptureEffects is not { } captureEffects
            || (uint)call.Function >= (uint)captureEffects.Length)
        {
            return;
        }

        var effects = captureEffects[call.Function];
        for (var parameter = 0; parameter < callee.ParameterCount; parameter++)
        {
            Count(ref remaining, 0);
            var variable = parameter + (callee.IsConstructor ? 1 : 0);
            if ((uint)variable >= (uint)callee.Variables.Length
                || !IsNativePointerHolder(callee.Variables[variable].Type,
                    initialized.Program)
                || HasNativeOutputForParameter(callee, parameter))
            {
                continue;
            }

            var address = NativePointerCallArgument(call, callee, parameter);
            var targets = NativePointerBackingOf(address, facts,
                initialized.Program, ref remaining, 0);
            var addressMayBeUnknown = NativePointerMayBeUnknownOf(address, facts,
                initialized.Program, ref remaining, 0);
            var sources = new HashSet<ProvenanceAnchor>();
            foreach (var effect in effects)
            {
                Count(ref remaining, 0);
                if (effect.DestinationParameter != parameter)
                {
                    continue;
                }

                var stagedIndex = facts.Staged.FindLastIndex(staged =>
                    ReferenceEquals(staged.Call, call)
                    && staged.ParameterIndex == effect.ValueParameter);
                if (stagedIndex < 0)
                {
                    throw InvalidEffect(call.Span, "Ownership");
                }
                var staged = facts.Staged[stagedIndex];
                sources.UnionWith(staged.NativePointerSources);
            }

            if (targets.Count == 0)
            {
                facts.MarkAllNativePointerProvenanceUnknown();
                continue;
            }
            if (addressMayBeUnknown)
            {
                facts.MarkAllNativePointerProvenanceUnknown();
            }

            foreach (var target in targets)
            {
                Count(ref remaining, 0);
                if (target.Kind == ProvenanceAnchorKind.NativeStorage
                    && (uint)target.Identity < (uint)facts.NativePointerSources.Length)
                {
                    facts.AddNativePointerProvenance(target.Identity, sources,
                        mayBeUnknown: true);
                }
            }
        }
    }

    private static bool MayWriteNativePointerStorage(BoundIndirectCall call, BoundProgram program)
    {
        if (call.Target is null
            || !NativeFunctionPointerTypes.IsType(call.Target.Type))
        {
            return false;
        }

        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(call.Target.Type)];
        foreach (var parameterType in signature.ParameterTypes)
        {
            if (!NativePointerTypeContract.IsValidType(
                    parameterType, program.NativePointerTypes))
            {
                continue;
            }
            var pointer = program.NativePointerTypes[NativePointerTypes.Index(parameterType)];
            if (pointer.IsWritable && CarriesNativePointer(pointer.PointeeType, program))
            {
                return true;
            }
        }
        return false;
    }

    private static void ApplyNativePointerWrite(BoundExpression address,
        BoundExpression value, BackingDependencies facts, BoundProgram program,
        ref int remaining, int depth)
    {
        var targets = NativePointerBackingOf(address, facts, program, ref remaining, depth + 1);
        var valueSources = NativePointerBackingOf(value, facts, program, ref remaining, depth + 1);
        var valueMayBeUnknown = NativePointerMayBeUnknownOf(
            value, facts, program, ref remaining, depth + 1);
        if (valueSources.Count != 0 && (targets.Count == 0
            || NativePointerMayBeUnknownOf(address, facts, program, ref remaining, depth + 1)))
        {
            throw InvalidEffect(value.Span, "NativePointer");
        }

        foreach (var target in targets)
        {
            Count(ref remaining, depth);
            if (target.Kind == ProvenanceAnchorKind.NativeStorage
                && (uint)target.Identity < (uint)facts.NativePointerSources.Length)
            {
                if (target.Storage is not StorageKey storage || !storage.IsKnown
                    || storage.Activation != facts.FunctionActivation
                    || HasUnknownStorageInstances(facts.StorageInstanceSet(target.Identity))
                    || !facts.StorageInstanceSet(target.Identity).Contains(storage.Instance))
                {
                    throw InvalidEffect(value.Span, "NativePointer");
                }
                facts.AddNativePointerProvenance(
                    target.Identity, valueSources, valueMayBeUnknown);
            }
        }
    }

    private static BackingDependencies? CreateBackingDependencies(int functionActivation,
        BoundFunction function,
        BoundProgram program, bool hasSpans,
        HashSet<NativePointerWriteEffect>[] nativePointerCaptureEffects)
    {
        if (!hasSpans && program.NativePointerTypes.IsEmpty
            && !CarriesNativePointer(function.ReturnType, program)
            && !function.Variables.Any(variable => CarriesNativePointer(variable.Type, program))
            && !CarriesCopyOrigin(function.ReturnType, program)
            && !function.Variables.Any(variable => CarriesCopyOrigin(variable.Type, program)))
        {
            return null;
        }
        var facts = new BackingDependencies(
            functionActivation, function, function.Variables.Length);
        var edges = new List<int>?[function.Variables.Length];
        var nativePointerEdges = new List<int>?[function.Variables.Length];
        var resultDependency = ResultDependencyContract(function);
        var forwardedSpanReturns = function.ReturnType == PrimitiveType.TokenSpan
            && resultDependency is not null && !resultDependency.Sources.IsEmpty
            ? new List<BoundExpression>() : null;
        var nativePointerReturns = new List<BoundExpression>();
        var nativePointerReads = new List<(int Destination, BoundExpression Address)>();
        var nativePointerWrites = new List<(BoundExpression Address, BoundExpression Value)>();
        var hasNativePointerFieldFlow = false;
        if (!BoundTreeTraversal.VisitExpressions(function.Body, expression =>
            hasNativePointerFieldFlow |= expression is BoundFieldRead field
                && CarriesNativePointer(field.Type, program)))
        {
            throw Invalid(function.Span);
        }
        var remaining = MaximumAnalysisVisits;
        for (var slot = 0; slot < edges.Length; slot++)
        {
            if (TryGetCopyOriginInput(function, program, slot, out var copyOrigin))
            {
                facts.SetCopyOriginRoot(slot, CopyOriginFact.Known([copyOrigin]));
            }
            if (slot < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                && !(slot == 0 && (function.IsConstructor || function.IsClassConstructor)))
            {
                if (TokenSpanContract.IsDependent(function.Variables[slot].Type, program.Structures)
                    || IsTokenSnapshotType(function.Variables[slot].Type, program))
                {
                    // Negative roots denote incoming backing, not the callee's descriptor place.
                    AddBackingSource(facts, slot,
                        ProvenanceAnchor.ForBackingSource(~slot));
                }
                if (CarriesNativePointer(function.Variables[slot].Type, program))
                {
                    facts.NativePointerMayBeUnknown[slot] = true;
                }
            }
        }

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

        void AddSnapshotSources(int destination, int source, TextSpan span, int depth)
        {
            Count(ref remaining, depth);
            if ((uint)source >= (uint)edges.Length
                || !IsTokenSnapshotType(function.Variables[source].Type, program))
            {
                throw Invalid(span);
            }
            foreach (var backingSource in TokenSnapshotBackingOf(source, facts))
            {
                AddBackingSource(facts, destination, backingSource);
            }
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
                    foreach (var source in BackingOf(snapshot, facts, program,
                        ref remaining, depth + 1))
                    {
                        AddBackingSource(facts, destination, source);
                    }
                    break;
                case BoundReceiverLoan loan when IsTokenSnapshotType(loan.Type, program):
                    AddSnapshotSources(destination, loan.Variable, loan.Span, depth + 1);
                    break;
                case BoundMutableReceiverLoan loan when IsTokenSnapshotType(loan.Type, program):
                    AddSnapshotSources(destination, loan.Variable, loan.Span, depth + 1);
                    break;
                case BoundRead read when IsTokenSnapshotType(read.Type, program):
                    if ((uint)read.Variable >= (uint)edges.Length
                        || function.Variables[read.Variable].Type != read.Type)
                    {
                        throw Invalid(read.Span);
                    }
                    AddSnapshotSources(destination, read.Variable, read.Span, depth + 1);
                    break;
                case BoundMove move when IsTokenSnapshotType(move.Type, program):
                    if ((uint)move.Variable >= (uint)edges.Length
                        || function.Variables[move.Variable].Type != move.Type)
                    {
                        throw Invalid(move.Span);
                    }
                    AddSnapshotSources(destination, move.Variable, move.Span, depth + 1);
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
                    var capture = ReceiverCaptureContract(cursorConstructor);
                    if (capture is not null)
                    {
                        foreach (var source in capture.Sources)
                        {
                            Source(destination,
                                DependencyCallArgument(call, cursorConstructor, source),
                                depth + 1);
                        }
                    }
                    break;
                case BoundCall call when (uint)call.Function < (uint)program.Functions.Length
                    && call.Type == PrimitiveType.TokenSpan:
                    var callee = program.Functions[call.Function];
                    ValidateSpanCall(call, callee);
                    var dependency = ResultDependencyContract(callee);
                    if (dependency is not null)
                    {
                        foreach (var source in dependency.Sources)
                        {
                            Source(destination,
                                DependencyCallArgument(call, callee, source),
                                depth + 1);
                        }
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
                    AddNativePointerSource(facts, destination, address.Variable,
                        StorageProjection.WholeObject);
                    break;
                case BoundNativeFixedArrayPointerAddress address:
                    if ((uint)address.Variable >= (uint)function.Variables.Length
                        || !NativePointerTypeContract.IsValidType(address.Type, program.NativePointerTypes))
                    {
                        throw InvalidEffect(address.Span, "NativePointer");
                    }
                    AddNativePointerSource(facts, destination, address.Variable,
                        StorageProjection.ForArrayElement(address.Index));
                    break;
                case BoundNativeStorageAddress address:
                    if ((uint)address.Variable >= (uint)function.Variables.Length)
                    {
                        throw InvalidEffect(address.Span, "NativePointer");
                    }
                    AddNativePointerSource(facts, destination, address.Variable,
                        StorageProjection.ForStorageAddress(address));
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
                case BoundCall call when CarriesNativePointer(call.Type, program):
                    facts.NativePointerMayBeUnknown[destination] = true;
                    if (call.Arguments.IsDefault
                        || (uint)call.Function >= (uint)program.Functions.Length)
                    {
                        throw Invalid(call.Span);
                    }
                    var callee = program.Functions[call.Function];
                    if (!callee.IsExternal && callee.Body is not null)
                    {
                        ValidateSpanCall(call, callee);
                    }
                    foreach (var argument in call.Arguments)
                    {
                        if (CarriesNativePointer(argument, program))
                        {
                            NativePointerSource(destination, argument, depth + 1);
                        }
                    }
                    break;
                case BoundIndirectCall call when CarriesNativePointer(call.Type, program):
                    facts.NativePointerMayBeUnknown[destination] = true;
                    if (call.Arguments.IsDefault)
                    {
                        throw Invalid(call.Span);
                    }
                    foreach (var argument in call.Arguments)
                    {
                        if (CarriesNativePointer(argument, program))
                        {
                            NativePointerSource(destination, argument, depth + 1);
                        }
                    }
                    break;
                case BoundConditional conditional when CarriesNativePointer(conditional.Type, program):
                    NativePointerSource(destination, conditional.WhenTrue, depth + 1);
                    NativePointerSource(destination, conditional.WhenFalse, depth + 1);
                    break;
                case BoundConstant constant when CarriesNativePointer(constant.Type, program):
                    facts.NativePointerMayBeUnknown[destination] |= constant.Value != 0;
                    break;
                default:
                    facts.NativePointerMayBeUnknown[destination] = true;
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
                    hasNativePointerFieldFlow = true;
                    NativePointerSource(field.Variable, field.Value, depth + 1);
                    break;
                case BoundExpressionStatement expressionStatement
                    when UnwrapUnsafeExpression(expressionStatement.Expression) is BoundNativePointerWrite write
                        && CarriesNativePointer(write.Value.Type, program):
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
        if (!BoundTreeTraversal.VisitExpressions(function.Body, expression =>
            {
                if (expression is not BoundCall call)
                {
                    return;
                }
                if ((uint)call.Function >= (uint)program.Functions.Length)
                {
                    throw Invalid(call.Span);
                }
                var callee = program.Functions[call.Function];
                foreach (var effect in nativePointerCaptureEffects[call.Function])
                {
                    nativePointerWrites.Add((
                        NativePointerCallArgument(call, callee, effect.DestinationParameter),
                        NativePointerCallArgument(call, callee, effect.ValueParameter)));
                }
            }))
        {
            throw Invalid(function.Span);
        }
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
                    if (facts.NativePointerMayBeUnknown[source]
                        && !facts.NativePointerMayBeUnknown[destination])
                    {
                        facts.NativePointerMayBeUnknown[destination] = true;
                        changed = true;
                        if (!pointerQueued[destination])
                        {
                            pointerPending.Enqueue(destination);
                            pointerQueued[destination] = true;
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
                var targets = NativePointerBackingOf(address, facts, program, ref remaining, 0);
                if (targets.Count == 0 || NativePointerMayBeUnknownOf(address, facts,
                        program, ref remaining, 0))
                {
                    if (!facts.NativePointerMayBeUnknown[destination])
                    {
                        facts.NativePointerMayBeUnknown[destination] = true;
                        pointerFactsChanged = true;
                    }
                }
                foreach (var target in targets)
                {
                    if (target.Kind != ProvenanceAnchorKind.NativeStorage
                        || (uint)target.Identity >= (uint)facts.NativePointerSources.Length)
                    {
                        facts.NativePointerMayBeUnknown[destination] = true;
                        pointerFactsChanged = true;
                        continue;
                    }
                    if (facts.NativePointerMayBeUnknown[target.Identity])
                    {
                        if (!facts.NativePointerMayBeUnknown[destination])
                        {
                            facts.NativePointerMayBeUnknown[destination] = true;
                            pointerFactsChanged = true;
                        }
                    }
                    if (facts.NativePointerSources[target.Identity] is not { Count: > 0 } sourceFacts)
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
                var valueMayBeUnknown = NativePointerMayBeUnknownOf(value, facts, program,
                    ref remaining, 0);
                if (valueSources.Count != 0 && (targets.Count == 0
                    || NativePointerMayBeUnknownOf(address, facts, program, ref remaining, 0)))
                {
                    throw InvalidEffect(value.Span, "NativePointer");
                }
                foreach (var target in targets)
                {
                    if (target.Kind != ProvenanceAnchorKind.NativeStorage
                        || (uint)target.Identity >= (uint)facts.NativePointerSources.Length)
                    {
                        continue;
                    }
                    if (valueSources.Count != 0)
                    {
                        pointerFactsChanged |= AddNativePointerSources(
                            facts, target.Identity, valueSources);
                    }
                    if (valueMayBeUnknown
                        && !facts.NativePointerMayBeUnknown[target.Identity])
                    {
                        facts.NativePointerMayBeUnknown[target.Identity] = true;
                        pointerFactsChanged = true;
                    }
                }
            }
        }
        foreach (var result in nativePointerReturns)
        {
            if (!hasNativePointerFieldFlow
                && NativePointerBackingOf(result, facts, program, ref remaining, 0).Count != 0)
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
            var receiverOffset = function.DeclaringStruct is not null
                && !function.IsStatic ? 1 : 0;
            var permittedSources = function.ResultDependencyParameters
                .Select(parameter => ProvenanceAnchor.ForBackingSource(
                    ~(parameter + receiverOffset))).ToHashSet();
            if (function.ResultDependencyIncludesReceiver)
            {
                _ = permittedSources.Add(ProvenanceAnchor.ForBackingSource(~0));
            }
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

    private static void AddNativePointerSource(BackingDependencies facts, int slot, int source,
        StorageProjection projection)
    {
        var sources = facts.NativePointerSources[slot];
        if (sources is null)
        {
            sources = [];
            facts.NativePointerSources[slot] = sources;
        }
        sources.UnionWith(NativeStorageAnchors(source, facts, projection));
    }

    private static HashSet<ProvenanceAnchor> NativeStorageAnchors(
        int variable, BackingDependencies facts, StorageProjection projection)
    {
        var instances = facts.StorageInstanceSet(variable);
        var sources = new HashSet<ProvenanceAnchor>();
        foreach (var instance in instances)
        {
            _ = sources.Add(ProvenanceAnchor.ForNativeStorage(
                facts.FunctionActivation, variable, instance, projection));
        }
        return sources;
    }

    private static bool IsInlineStructStorage(PrimitiveType type, BoundProgram program)
    {
        var index = (int)type - 100;
        return !ManagedClassTypes.IsClass(type)
            && (uint)index < (uint)program.Structures.Length;
    }

    private static bool IsNativePointerValue(PrimitiveType type, BoundProgram program)
        => type == PrimitiveType.NativeRawPointer
            || NativePointerTypeContract.IsValidType(type, program.NativePointerTypes);

    private static List<(StorageProjection Projection, PrimitiveType Type)> NativePointerFieldLeaves(
        PrimitiveType type, BoundProgram program, ref int remaining, int depth)
    {
        var result = new List<(StorageProjection Projection, PrimitiveType Type)>();
        CollectNativePointerFieldLeaves(type, StorageProjection.WholeObject,
            program, result, [], ref remaining, depth);
        return result;
    }

    private static bool CarriesCopyOrigin(PrimitiveType type, BoundProgram program)
    {
        var pending = new Stack<PrimitiveType>();
        var visited = new HashSet<PrimitiveType>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (IsNativePointerValue(current, program)
                || NativeFunctionPointerTypes.IsType(current)
                || current == PrimitiveType.TokenSpan)
            {
                return true;
            }
            if (!IsInlineStructStorage(current, program) || !visited.Add(current))
            {
                continue;
            }
            foreach (var field in program.Structures[(int)current - 100].Fields)
            {
                pending.Push(field.Type);
            }
        }
        return false;
    }

    private static List<(StorageProjection Projection, PrimitiveType Type)> CopyOriginFieldLeaves(
        PrimitiveType type, BoundProgram program, ref int remaining, int depth)
    {
        var result = new List<(StorageProjection Projection, PrimitiveType Type)>();
        CollectCopyOriginFieldLeaves(type, StorageProjection.WholeObject,
            program, result, [], ref remaining, depth);
        return result;
    }

    private static void CollectCopyOriginFieldLeaves(PrimitiveType type,
        StorageProjection projection, BoundProgram program,
        List<(StorageProjection Projection, PrimitiveType Type)> result,
        HashSet<PrimitiveType> activeTypes, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (IsNativePointerValue(type, program)
            || NativeFunctionPointerTypes.IsType(type)
            || type == PrimitiveType.TokenSpan)
        {
            result.Add((projection, type));
            return;
        }
        if (!IsInlineStructStorage(type, program))
        {
            return;
        }
        if (!activeTypes.Add(type))
        {
            throw InvalidEffect(default, "Ownership");
        }

        var structure = program.Structures[(int)type - 100];
        for (var field = 0; field < structure.Fields.Length; field++)
        {
            var fieldType = structure.Fields[field].Type;
            if (!CarriesCopyOrigin(fieldType, program))
            {
                continue;
            }
            CollectCopyOriginFieldLeaves(fieldType,
                projection.AppendField(type, field), program, result,
                activeTypes, ref remaining, depth + 1);
        }

        _ = activeTypes.Remove(type);
    }

    private static void CollectNativePointerFieldLeaves(PrimitiveType type,
        StorageProjection projection, BoundProgram program,
        List<(StorageProjection Projection, PrimitiveType Type)> result,
        HashSet<PrimitiveType> activeTypes, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (IsNativePointerValue(type, program))
        {
            result.Add((projection, type));
            return;
        }
        if (!IsInlineStructStorage(type, program))
        {
            return;
        }
        if (!activeTypes.Add(type))
        {
            throw InvalidEffect(default, "NativePointer");
        }

        var structure = program.Structures[(int)type - 100];
        for (var field = 0; field < structure.Fields.Length; field++)
        {
            var fieldType = structure.Fields[field].Type;
            if (!CarriesNativePointer(fieldType, program))
            {
                continue;
            }
            CollectNativePointerFieldLeaves(fieldType,
                projection.AppendField(type, field), program, result,
                activeTypes, ref remaining, depth + 1);
        }

        _ = activeTypes.Remove(type);
    }

    private static bool TryGetNativePointerFieldPlace(BoundExpression expression,
        BoundFunction function, BoundProgram program, out int variable,
        out StorageProjection projection)
    {
        switch (expression)
        {
            case BoundRead read:
                if ((uint)read.Variable >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != read.Type)
                {
                    throw Invalid(read.Span);
                }
                variable = read.Variable;
                projection = StorageProjection.WholeObject;
                return IsInlineStructStorage(read.Type, program);

            case BoundMove move:
                if ((uint)move.Variable >= (uint)function.Variables.Length
                    || function.Variables[move.Variable].Type != move.Type)
                {
                    throw Invalid(move.Span);
                }
                variable = move.Variable;
                projection = StorageProjection.WholeObject;
                return IsInlineStructStorage(move.Type, program);

            case BoundFieldRead field:
                if (field.Receiver is null
                    || !TryGetNativePointerFieldPlace(field.Receiver, function,
                        program, out variable, out projection))
                {
                    variable = -1;
                    projection = default;
                    return false;
                }
                if (!IsInlineStructStorage(field.Receiver.Type, program))
                {
                    variable = -1;
                    projection = default;
                    return false;
                }
                var fields = program.Structures[(int)field.Receiver.Type - 100].Fields;
                if ((uint)field.Field >= (uint)fields.Length
                    || fields[field.Field].Type != field.Type)
                {
                    throw Invalid(field.Span);
                }
                projection = projection.AppendField(field.Receiver.Type, field.Field);
                return projection.Kind == StorageProjectionKind.InlineFieldPath;

            default:
                variable = -1;
                projection = default;
                return false;
        }
    }

    private static bool TryGetNativePointerFieldAssignmentPlace(
        BoundFieldAssignment assignment, BoundFunction function, BoundProgram program,
        out StorageProjection projection, out PrimitiveType valueType)
    {
        if ((uint)assignment.Variable >= (uint)function.Variables.Length
            || assignment.NestedFields.IsDefault)
        {
            throw Invalid(assignment.Span);
        }
        var ownerType = function.Variables[assignment.Variable].Type;
        if (!IsInlineStructStorage(ownerType, program))
        {
            projection = default;
            valueType = PrimitiveType.Void;
            return false;
        }

        projection = StorageProjection.WholeObject;
        var field = assignment.Field;
        for (var pathIndex = -1; pathIndex < assignment.NestedFields.Length; pathIndex++)
        {
            var fields = program.Structures[(int)ownerType - 100].Fields;
            if ((uint)field >= (uint)fields.Length)
            {
                throw Invalid(assignment.Span);
            }
            var fieldType = fields[field].Type;
            projection = projection.AppendField(ownerType, field);
            ownerType = fieldType;
            if (!IsInlineStructStorage(ownerType, program)
                && pathIndex + 1 < assignment.NestedFields.Length)
            {
                projection = default;
                valueType = PrimitiveType.Void;
                return false;
            }
            if (pathIndex + 1 < assignment.NestedFields.Length)
            {
                field = assignment.NestedFields[pathIndex + 1];
            }
        }

        valueType = ownerType;
        return projection.Kind == StorageProjectionKind.InlineFieldPath;
    }

    private static (HashSet<ProvenanceAnchor> Sources, bool MayBeUnknown)
        NativePointerFieldFactsOf(BoundFieldRead field, BackingDependencies facts,
            BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        if (!TryGetNativePointerFieldPlace(field, function, program,
            out var variable, out var projection))
        {
            return (new HashSet<ProvenanceAnchor>(NativePointerBackingOf(
                field.Receiver, facts, program, ref remaining, depth + 1)), true);
        }

        var leaves = NativePointerFieldLeaves(field.Type, program,
            ref remaining, depth + 1);
        var sources = new HashSet<ProvenanceAnchor>();
        var mayBeUnknown = false;
        foreach (var leaf in leaves)
        {
            Count(ref remaining, depth + 1);
            var leafProjection = StorageProjection.Combine(projection, leaf.Projection);
            if (leafProjection.Kind != StorageProjectionKind.InlineFieldPath
                || !facts.TryGetNativePointerFieldValue(variable, leafProjection,
                    out var fieldSources, out var fieldMayBeUnknown))
            {
                mayBeUnknown = true;
                if (facts.NativePointerSources[variable] is { } aggregateSources)
                {
                    sources.UnionWith(aggregateSources);
                }
                continue;
            }
            sources.UnionWith(fieldSources);
            mayBeUnknown |= fieldMayBeUnknown;
        }

        return (sources, mayBeUnknown || leaves.Count == 0);
    }

    private static (HashSet<ProvenanceAnchor> Sources, bool MayBeUnknown)
        NativePointerValueFactsOfExpression(BoundExpression expression,
            PrimitiveType valueType, StorageProjection relativeProjection,
            BackingDependencies facts, BoundFunction function, BoundProgram program,
            ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is null || expression.Type != valueType)
        {
            throw Invalid(expression?.Span ?? default);
        }

        switch (expression)
        {
            case BoundUnsafeExpression unsafeExpression:
                return NativePointerValueFactsOfExpression(unsafeExpression.Expression,
                    valueType, relativeProjection, facts, function, program,
                    ref remaining, depth + 1);
            case BoundConditional conditional when CarriesNativePointer(valueType, program):
                var (whenTrueSources, whenTrueMayBeUnknown) = NativePointerValueFactsOfExpression(conditional.WhenTrue,
                    valueType, relativeProjection, facts, function, program,
                    ref remaining, depth + 1);
                var (whenFalseSources, whenFalseMayBeUnknown) = NativePointerValueFactsOfExpression(conditional.WhenFalse,
                    valueType, relativeProjection, facts, function, program,
                    ref remaining, depth + 1);
                whenTrueSources.UnionWith(whenFalseSources);
                return (whenTrueSources, whenTrueMayBeUnknown || whenFalseMayBeUnknown);
        }

        if (IsNativePointerValue(valueType, program))
        {
            if (relativeProjection.Kind != StorageProjectionKind.WholeObject)
            {
                return (new HashSet<ProvenanceAnchor>(), true);
            }
            return (new HashSet<ProvenanceAnchor>(NativePointerBackingOf(
                    expression, facts, program, ref remaining, depth + 1)),
                NativePointerMayBeUnknownOf(expression, facts, program,
                    ref remaining, depth + 1));
        }

        if (TryGetNativePointerFieldPlace(expression, function, program,
            out var variable, out var sourcePrefix))
        {
            var sourceProjection = StorageProjection.Combine(
                sourcePrefix, relativeProjection);
            if (sourceProjection.Kind == StorageProjectionKind.InlineFieldPath
                && facts.TryGetNativePointerFieldValue(variable, sourceProjection,
                    out var sources, out var mayBeUnknown))
            {
                return (sources, mayBeUnknown);
            }
        }

        return (new HashSet<ProvenanceAnchor>(NativePointerBackingOf(
                expression, facts, program, ref remaining, depth + 1)), true);
    }

    private static CopyOriginFact CopyOriginOfExpression(
        BoundExpression expression, PrimitiveType valueType, StorageProjection relativeProjection,
        BackingDependencies facts, BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is null || expression.Type != valueType)
        {
            throw Invalid(expression?.Span ?? default);
        }

        switch (expression)
        {
            case BoundUnsafeExpression unsafeExpression:
                return CopyOriginOfExpression(unsafeExpression.Expression, valueType,
                    relativeProjection, facts, function, program, ref remaining, depth + 1);
            case BoundConditional conditional when conditional.WhenTrue is not null
                && conditional.WhenFalse is not null
                && conditional.WhenTrue.Type == valueType
                && conditional.WhenFalse.Type == valueType:
                var whenTrue = CopyOriginOfExpression(conditional.WhenTrue, valueType,
                    relativeProjection, facts, function, program, ref remaining, depth + 1);
                var whenFalse = CopyOriginOfExpression(conditional.WhenFalse, valueType,
                    relativeProjection, facts, function, program, ref remaining, depth + 1);
                _ = whenTrue.Merge(whenFalse);
                return whenTrue;
            case BoundRead read:
                ValidateCopyOriginVariable(read.Variable, read.Type, function);
                if (relativeProjection.Kind == StorageProjectionKind.InlineFieldPath
                    && facts.TryGetCopyOriginFieldValue(read.Variable, relativeProjection,
                        out var readFieldFact))
                {
                    return readFieldFact;
                }
                var readRoot = facts.CopyOriginRoot(read.Variable);
                return relativeProjection.Kind switch
                {
                    StorageProjectionKind.WholeObject => readRoot,
                    StorageProjectionKind.InlineFieldPath =>
                        readRoot.Project(relativeProjection.FieldPath),
                    _ => CopyOriginFact.Unavailable(),
                };
            case BoundMove move:
                ValidateCopyOriginVariable(move.Variable, move.Type, function);
                if (relativeProjection.Kind == StorageProjectionKind.InlineFieldPath
                    && facts.TryGetCopyOriginFieldValue(move.Variable, relativeProjection,
                        out var moveFieldFact))
                {
                    return moveFieldFact;
                }
                var moveRoot = facts.CopyOriginRoot(move.Variable);
                return relativeProjection.Kind switch
                {
                    StorageProjectionKind.WholeObject => moveRoot,
                    StorageProjectionKind.InlineFieldPath =>
                        moveRoot.Project(relativeProjection.FieldPath),
                    _ => CopyOriginFact.Unavailable(),
                };
            case BoundFieldRead field when TryGetNativePointerFieldPlace(field,
                function, program, out var variable, out var sourcePrefix):
                var sourceProjection = StorageProjection.Combine(sourcePrefix, relativeProjection);
                if (sourceProjection.Kind == StorageProjectionKind.InlineFieldPath
                    && facts.TryGetCopyOriginFieldValue(variable, sourceProjection,
                        out var fieldFact))
                {
                    return fieldFact;
                }
                var fieldRoot = facts.CopyOriginRoot(variable);
                return sourceProjection.Kind switch
                {
                    StorageProjectionKind.WholeObject => fieldRoot,
                    StorageProjectionKind.InlineFieldPath =>
                        fieldRoot.Project(sourceProjection.FieldPath),
                    _ => CopyOriginFact.Unavailable(),
                };
            case BoundConstant or BoundNull or BoundNativePointerAddress
                or BoundNativeFixedArrayPointerAddress or BoundNativeStorageAddress:
                return CopyOriginFact.KnownEmpty();
            default:
                return CopyOriginFact.Unavailable();
        }
    }

    private static void ValidateCopyOriginVariable(
        int variable, PrimitiveType type, BoundFunction function)
    {
        if ((uint)variable >= (uint)function.Variables.Length
            || function.Variables[variable].Type != type)
        {
            throw Invalid(function.Span);
        }
    }

    private static void SetCopyOriginValuesFromExpression(int destination,
        StorageProjection destinationPrefix, PrimitiveType destinationType,
        BoundExpression expression, BackingDependencies facts,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if ((uint)destination >= (uint)function.Variables.Length
            || expression is null || expression.Type != destinationType
            || destinationPrefix.Kind is not (StorageProjectionKind.WholeObject
                or StorageProjectionKind.InlineFieldPath)
            || (destinationPrefix.Kind == StorageProjectionKind.WholeObject
                && function.Variables[destination].Type != destinationType))
        {
            throw Invalid(expression?.Span ?? default);
        }

        if (destinationPrefix.Kind == StorageProjectionKind.WholeObject
            && !CarriesCopyOrigin(destinationType, program))
        {
            facts.SetCopyOriginRoot(destination, CopyOriginFact.Unavailable());
            facts.ClearCopyOriginFieldValues(destination);
            return;
        }

        if (destinationPrefix.Kind == StorageProjectionKind.WholeObject)
        {
            facts.ClearCopyOriginFieldValues(destination);
            facts.SetCopyOriginRoot(destination,
                CopyOriginOfExpression(expression, destinationType,
                    StorageProjection.WholeObject, facts, function, program,
                    ref remaining, depth + 1));
        }
        else
        {
            var ownerType = function.Variables[destination].Type;
            PreserveCopyOriginFieldValuesFromRoot(destination, ownerType,
                facts, program, ref remaining, depth + 1);
            facts.SetCopyOriginRoot(destination, CopyOriginFact.Unavailable());
            facts.ClearCopyOriginFieldValues(destination, destinationPrefix);
        }

        var leaves = CopyOriginFieldLeaves(destinationType, program,
            ref remaining, depth + 1);
        foreach (var leaf in leaves)
        {
            Count(ref remaining, depth + 1);
            var destinationProjection = StorageProjection.Combine(
                destinationPrefix, leaf.Projection);
            if (destinationProjection.Kind != StorageProjectionKind.InlineFieldPath)
            {
                continue;
            }
            facts.SetCopyOriginFieldValue(destination, destinationProjection,
                CopyOriginOfExpression(expression, destinationType, leaf.Projection,
                    facts, function, program, ref remaining, depth + 1));
        }
    }

    private static void PreserveCopyOriginFieldValuesFromRoot(int slot, PrimitiveType type,
        BackingDependencies facts, BoundProgram program, ref int remaining, int depth)
    {
        var root = facts.CopyOriginRoot(slot);
        if (!root.IsAvailable)
        {
            return;
        }

        var leaves = CopyOriginFieldLeaves(type, program, ref remaining, depth + 1);
        foreach (var leaf in leaves)
        {
            Count(ref remaining, depth + 1);
            if (leaf.Projection.Kind == StorageProjectionKind.InlineFieldPath
                && !facts.TryGetCopyOriginFieldValue(slot, leaf.Projection, out _))
            {
                facts.SetCopyOriginFieldValue(slot, leaf.Projection,
                    root.Project(leaf.Projection.FieldPath));
            }
        }
    }

    private static bool TryGetCopyOriginInput(BoundFunction function, BoundProgram program,
        int slot, out CopyValueOrigin origin)
    {
        origin = default;
        if ((uint)slot >= (uint)function.Variables.Length || function.IsClassConstructor)
        {
            return false;
        }

        var hasBorrowedReceiver = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor;
        if (hasBorrowedReceiver && slot == 0)
        {
            var receiverType = function.Variables[0].Type;
            if (!IsInlineStructStorage(receiverType, program)
                || !CarriesCopyOrigin(receiverType, program))
            {
                return false;
            }
            origin = new CopyValueOrigin(CopyOriginSourceKind.Receiver,
                CopyOriginSourceView.EntryReferentValue, 0, default);
            return true;
        }

        var formalOffset = function.IsConstructor || hasBorrowedReceiver ? 1 : 0;
        var formalOrdinal = slot - formalOffset;
        var formalCount = function.ParameterCount - (hasBorrowedReceiver ? 1 : 0);
        if ((uint)formalOrdinal >= (uint)formalCount)
        {
            return false;
        }

        var formal = function.Variables[slot];
        if (formal.IsOut || formal.IsInit || !CarriesCopyOrigin(formal.Type, program)
            || (!formal.IsImplicitRef && IsUnique(program, formal.Type)))
        {
            return false;
        }
        origin = new CopyValueOrigin(CopyOriginSourceKind.ExplicitFormal,
            formal.IsImplicitRef
                ? CopyOriginSourceView.EntryReferentValue : CopyOriginSourceView.EntryValue,
            formalOrdinal, default);
        return true;
    }

    private static void SetNativePointerFieldValuesFromExpression(int destination,
        StorageProjection destinationPrefix, PrimitiveType destinationType,
        BoundExpression expression, BackingDependencies facts,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        var leaves = NativePointerFieldLeaves(destinationType, program,
            ref remaining, depth + 1);
        foreach (var leaf in leaves)
        {
            Count(ref remaining, depth + 1);
            var destinationProjection = StorageProjection.Combine(
                destinationPrefix, leaf.Projection);
            if (destinationProjection.Kind != StorageProjectionKind.InlineFieldPath)
            {
                throw Invalid(expression.Span);
            }
            var (sources, mayBeUnknown) = NativePointerValueFactsOfExpression(expression,
                destinationType, leaf.Projection, facts, function, program,
                ref remaining, depth + 1);
            facts.SetNativePointerFieldValue(destination, destinationProjection,
                sources, mayBeUnknown);
        }
    }

    private static void RefreshNativePointerAggregateProvenance(int slot,
        PrimitiveType type, BackingDependencies facts, BoundProgram program,
        ref int remaining, int depth)
    {
        if (!IsInlineStructStorage(type, program)
            || !CarriesNativePointer(type, program))
        {
            return;
        }

        var leaves = NativePointerFieldLeaves(type, program,
            ref remaining, depth + 1);
        var sources = new HashSet<ProvenanceAnchor>();
        var mayBeUnknown = leaves.Count == 0;
        foreach (var leaf in leaves)
        {
            Count(ref remaining, depth + 1);
            if (!facts.TryGetNativePointerFieldValue(slot, leaf.Projection,
                out var fieldSources, out var fieldMayBeUnknown))
            {
                mayBeUnknown = true;
                continue;
            }
            sources.UnionWith(fieldSources);
            mayBeUnknown |= fieldMayBeUnknown;
        }
        facts.SetNativePointerAggregateProvenance(slot, sources, mayBeUnknown);
    }

    private static bool AddNativePointerSources(BackingDependencies facts, int slot,
        IReadOnlySet<ProvenanceAnchor> additions)
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
            if (current == PrimitiveType.NativeRawPointer
                || NativePointerTypeContract.IsValidType(current, program.NativePointerTypes))
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

    private static bool CarriesNativePointer(BoundExpression expression, BoundProgram program) =>
        expression is BoundNativeStorageAddress || CarriesNativePointer(expression.Type, program);

    private static IReadOnlySet<ProvenanceAnchor> NativePointerBackingOf(
        BoundExpression expression,
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
                return NativeStorageAnchors(address.Variable, facts, StorageProjection.WholeObject);
            case BoundNativeFixedArrayPointerAddress address
                when (uint)address.Variable < (uint)facts.NativePointerSources.Length:
                return NativeStorageAnchors(address.Variable, facts,
                    StorageProjection.ForArrayElement(address.Index));
            case BoundNativeStorageAddress address
                when (uint)address.Variable < (uint)facts.NativePointerSources.Length:
                return NativeStorageAnchors(address.Variable, facts,
                    StorageProjection.ForStorageAddress(address));
            case BoundRead read when (uint)read.Variable < (uint)facts.NativePointerSources.Length:
                return facts.NativePointerSources[read.Variable] ?? EmptyBackingSources;
            case BoundMove move when (uint)move.Variable < (uint)facts.NativePointerSources.Length:
                return facts.NativePointerSources[move.Variable] ?? EmptyBackingSources;
            case BoundNativePointerRead read when CarriesNativePointer(read.Type, program):
                var readSources = new HashSet<ProvenanceAnchor>();
                foreach (var target in NativePointerBackingOf(read.Address, facts, program,
                    ref remaining, depth + 1))
                {
                    if (target.Kind == ProvenanceAnchorKind.NativeStorage
                        && (uint)target.Identity < (uint)facts.NativePointerSources.Length
                        && facts.NativePointerSources[target.Identity] is { } targetSources)
                    {
                        if (target.Storage is not StorageKey targetStorage
                            || !targetStorage.IsKnown
                            || targetStorage.Activation != facts.FunctionActivation
                            || !facts.StorageInstanceSet(target.Identity).Contains(targetStorage.Instance))
                        {
                            throw InvalidEffect(read.Span, "NativePointer");
                        }
                        readSources.UnionWith(targetSources);
                    }
                }
                return readSources;
            case BoundFieldRead field:
                return NativePointerFieldFactsOf(field, facts, facts.Function, program,
                    ref remaining, depth + 1).Sources;
            case BoundUnsafeExpression unsafeExpression:
                return NativePointerBackingOf(unsafeExpression.Expression, facts, program, ref remaining, depth + 1);
            case BoundCall call when CarriesNativePointer(call.Type, program):
                if (call.Arguments.IsDefault
                    || (uint)call.Function >= (uint)program.Functions.Length)
                {
                    throw Invalid(call.Span);
                }
                var callee = program.Functions[call.Function];
                if (!callee.IsExternal && callee.Body is not null)
                {
                    ValidateSpanCall(call, callee);
                }
                var callBacking = new HashSet<ProvenanceAnchor>();
                foreach (var argument in call.Arguments)
                {
                    if (CarriesNativePointer(argument, program))
                    {
                        callBacking.UnionWith(NativePointerBackingOf(argument, facts, program,
                            ref remaining, depth + 1));
                    }
                }
                return callBacking;
            case BoundIndirectCall call when CarriesNativePointer(call.Type, program):
                if (call.Arguments.IsDefault)
                {
                    throw Invalid(call.Span);
                }
                var indirectCallBacking = new HashSet<ProvenanceAnchor>();
                foreach (var argument in call.Arguments)
                {
                    if (CarriesNativePointer(argument, program))
                    {
                        indirectCallBacking.UnionWith(NativePointerBackingOf(argument, facts,
                            program, ref remaining, depth + 1));
                    }
                }
                return indirectCallBacking;
            case BoundConditional conditional when CarriesNativePointer(conditional.Type, program):
                var branchBacking = new HashSet<ProvenanceAnchor>(
                    NativePointerBackingOf(conditional.WhenTrue,
                        facts, program, ref remaining, depth + 1));
                branchBacking.UnionWith(NativePointerBackingOf(conditional.WhenFalse,
                    facts, program, ref remaining, depth + 1));
                return branchBacking;
            default:
                return EmptyBackingSources;
        }
    }

    private static bool NativePointerMayBeUnknownOf(BoundExpression expression,
        BackingDependencies facts, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is null)
        {
            throw Invalid();
        }
        switch (expression)
        {
            case BoundNativePointerAddress address:
                return HasUnknownStorageInstances(facts.StorageInstanceSet(address.Variable));
            case BoundNativeFixedArrayPointerAddress address:
                return HasUnknownStorageInstances(facts.StorageInstanceSet(address.Variable));
            case BoundNativeStorageAddress address:
                return HasUnknownStorageInstances(facts.StorageInstanceSet(address.Variable));
            case BoundRead read when (uint)read.Variable < (uint)facts.NativePointerMayBeUnknown.Length:
                return facts.NativePointerMayBeUnknown[read.Variable]
                    || !NativePointerStorageInstancesMatch(read.Variable, facts);
            case BoundMove move when (uint)move.Variable < (uint)facts.NativePointerMayBeUnknown.Length:
                return facts.NativePointerMayBeUnknown[move.Variable]
                    || !NativePointerStorageInstancesMatch(move.Variable, facts);
            case BoundNativePointerRead read when CarriesNativePointer(read.Type, program):
                var targets = NativePointerBackingOf(read.Address, facts, program,
                    ref remaining, depth + 1);
                if (targets.Count == 0 || NativePointerMayBeUnknownOf(read.Address,
                    facts, program, ref remaining, depth + 1))
                {
                    return true;
                }
                foreach (var target in targets)
                {
                    if (target.Kind != ProvenanceAnchorKind.NativeStorage
                        || (uint)target.Identity >= (uint)facts.NativePointerMayBeUnknown.Length
                        || facts.NativePointerMayBeUnknown[target.Identity]
                        || target.Storage is not StorageKey targetStorage
                        || !targetStorage.IsKnown
                        || targetStorage.Activation != facts.FunctionActivation
                        || !facts.StorageInstanceSet(target.Identity).Contains(targetStorage.Instance)
                        || !NativePointerStorageInstancesMatch(target.Identity, facts))
                    {
                        return true;
                    }
                }
                return false;
            case BoundFieldRead field:
                return NativePointerFieldFactsOf(field, facts, facts.Function, program,
                    ref remaining, depth + 1).MayBeUnknown;
            case BoundUnsafeExpression unsafeExpression:
                return NativePointerMayBeUnknownOf(unsafeExpression.Expression, facts,
                    program, ref remaining, depth + 1);
            case BoundCall call when CarriesNativePointer(call.Type, program):
                return true;
            case BoundIndirectCall indirectCall when CarriesNativePointer(indirectCall.Type, program):
                return true;
            case BoundConditional conditional when CarriesNativePointer(conditional.Type, program):
                return NativePointerMayBeUnknownOf(conditional.WhenTrue, facts, program,
                    ref remaining, depth + 1)
                    || NativePointerMayBeUnknownOf(conditional.WhenFalse, facts, program,
                        ref remaining, depth + 1);
            case BoundConstant constant when CarriesNativePointer(constant.Type, program):
                return constant.Value != 0;
            default:
                return CarriesNativePointer(expression.Type, program);
        }
    }

    private static bool HasUnknownStorageInstances(HashSet<StorageInstance> instances)
        => instances.Count == 0 || instances.Any(static instance => !instance.IsKnown);

    private static bool NativePointerStorageInstancesMatch(int variable, BackingDependencies facts)
    {
        if ((uint)variable >= (uint)facts.NativePointerSources.Length
            || facts.NativePointerSources[variable] is not { } sources)
        {
            return true;
        }

        var sites = sources.Where(static source => source.Kind == ProvenanceAnchorKind.NativeStorage)
            .Select(static source => source.Identity).Distinct();
        foreach (var site in sites)
        {
            if ((uint)site >= (uint)facts.NativePointerSources.Length)
            {
                return false;
            }

            var expected = facts.StorageInstanceSet(site);
            if (HasUnknownStorageInstances(expected))
            {
                return false;
            }
            var actual = new HashSet<StorageInstance>();
            foreach (var source in sources)
            {
                if (source.Kind != ProvenanceAnchorKind.NativeStorage || source.Identity != site)
                {
                    continue;
                }
                if (source.Storage is not StorageKey key || !key.IsKnown
                    || key.Activation != facts.FunctionActivation || key.Site != site)
                {
                    return false;
                }
                _ = actual.Add(key.Instance);
            }
            if (!actual.SetEquals(expected))
            {
                return false;
            }
        }
        return true;
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

    private static bool IsTokenSnapshotType(PrimitiveType type,
        BoundProgram program)
    {
        var index = (int)type - 100;
        return (uint)index < (uint)program.Structures.Length
            && TokenSpanContract.RoleOf(program.Structures[index])
                == TokenSpanTypeRole.Snapshot
            && TokenSpanContract.ValidSnapshot(program.Structures[index]);
    }

    private static HashSet<ProvenanceAnchor> TokenSnapshotBackingOf(
        int variable, BackingDependencies facts)
    {
        var sources = facts.Sources[variable] is { } existing
            ? new HashSet<ProvenanceAnchor>(existing)
            : [];
        var incomingSource = ProvenanceAnchor.ForBackingSource(~variable);
        if (!sources.Contains(incomingSource))
        {
            _ = sources.Add(ProvenanceAnchor.ForBackingSource(variable));
        }
        return sources;
    }

    private static void AddBackingSource(BackingDependencies facts, int slot,
        ProvenanceAnchor source)
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
        var ownerInstances = facts.StorageInstanceSet(owner);
        if (facts.Staged.Any(staged => staged.NativePointerSources.Any(
                source => IsNativeStorageInstance(source, owner, ownerInstances,
                    facts.FunctionActivation))))
        {
            throw InvalidEffect(span, "NativePointer");
        }
        for (var slot = 0; slot < state.Length; slot++)
        {
            if ((state.Possibilities(slot) & 2) != 0
                && facts.NativePointerSources[slot]?.Any(
                    source => IsNativeStorageInstance(source, owner, ownerInstances,
                        facts.FunctionActivation)) == true)
            {
                throw InvalidEffect(span, "NativePointer");
            }
        }
    }

    private static bool IsNativeStorageInstance(ProvenanceAnchor source, int site,
        HashSet<StorageInstance> instances, int activation)
        => source.Kind == ProvenanceAnchorKind.NativeStorage
            && source.Identity == site
            && (HasUnknownStorageInstances(instances)
                || source.Storage is not StorageKey key || !key.IsKnown
                || key.Activation != activation
                || instances.Contains(key.Instance));

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
            if (scopedVariables.Contains(slot) || (state.Possibilities(slot) & 2) == 0)
            {
                continue;
            }

            if (facts.NativePointerSources[slot]?.Any(source =>
                    source.Kind == ProvenanceAnchorKind.NativeStorage
                    && scopedVariables.Contains(source.Identity)) == true)
            {
                throw InvalidEffect(block.Span, "NativePointer");
            }
        }
    }

    private static IReadOnlySet<ProvenanceAnchor> BackingOf(
        BoundExpression expression, BackingDependencies? facts,
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
                return facts.Sources[snapshot.Variable] ?? new HashSet<ProvenanceAnchor>
                {
                    ProvenanceAnchor.ForBackingSource(snapshot.Variable),
                };
            case BoundRead read when (uint)read.Variable < (uint)facts.Sources.Length:
                return IsTokenSnapshotType(read.Type, program)
                    ? TokenSnapshotBackingOf(read.Variable, facts)
                    : facts.Sources[read.Variable] ?? EmptyBackingSources;
            case BoundMove move when (uint)move.Variable < (uint)facts.Sources.Length:
                return IsTokenSnapshotType(move.Type, program)
                    ? TokenSnapshotBackingOf(move.Variable, facts)
                    : facts.Sources[move.Variable] ?? EmptyBackingSources;
            case BoundReceiverLoan loan when (uint)loan.Variable < (uint)facts.Sources.Length:
                if (IsTokenSnapshotType(loan.Type, program))
                {
                    return TokenSnapshotBackingOf(loan.Variable, facts);
                }
                return facts.Sources[loan.Variable] ?? EmptyBackingSources;
            case BoundMutableReceiverLoan loan when (uint)loan.Variable < (uint)facts.Sources.Length:
                if (IsTokenSnapshotType(loan.Type, program))
                {
                    return TokenSnapshotBackingOf(loan.Variable, facts);
                }
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
                var cursorBacking = new HashSet<ProvenanceAnchor>();
                var capture = ReceiverCaptureContract(constructor);
                if (capture is not null)
                {
                    foreach (var source in capture.Sources)
                    {
                        cursorBacking.UnionWith(BackingOf(
                            DependencyCallArgument(call, constructor, source),
                            facts, program, ref remaining, depth + 1));
                    }
                }
                if (cursorBacking.Count == 0)
                {
                    throw InvalidEffect(call.Span, "Ownership");
                }
                return cursorBacking;
            case BoundCall call when (uint)call.Function < (uint)program.Functions.Length
                && call.Type == PrimitiveType.TokenSpan:
                var result = new HashSet<ProvenanceAnchor>();
                var callee = program.Functions[call.Function];
                ValidateSpanCall(call, callee);
                var dependency = ResultDependencyContract(callee);
                if (dependency is not null)
                {
                    foreach (var source in dependency.Sources)
                    {
                        result.UnionWith(BackingOf(
                            DependencyCallArgument(call, callee, source), facts,
                            program, ref remaining, depth + 1));
                    }
                }
                return result;
            case BoundConditional conditional when conditional.Condition is not null
                && conditional.WhenTrue is not null && conditional.WhenFalse is not null
                && conditional.WhenTrue.Type == PrimitiveType.TokenSpan
                && conditional.WhenFalse.Type == PrimitiveType.TokenSpan:
                var union = new HashSet<ProvenanceAnchor>(
                    BackingOf(conditional.WhenTrue, facts, program, ref remaining, depth + 1));
                union.UnionWith(BackingOf(conditional.WhenFalse, facts, program, ref remaining, depth + 1));
                return union;
            default:
                throw InvalidEffect(expression.Span, "Ownership");
        }
    }

    private static void StageDependencySource(BoundCall call, int parameterIndex,
        BoundExpression argument, BoundFunction target, InitializationState initialized,
        BoundProgram program, ref int remaining, int depth)
    {
        if (initialized.Dependencies is not BackingDependencies dependencies)
        {
            return;
        }

        var hasBacking = TokenSpanContract.IsDependent(argument.Type, program.Structures);
        var hasNativePointer = CarriesNativePointer(argument, program);
        if (!hasBacking && !hasNativePointer && target.NativeOutputContracts.IsEmpty)
        {
            return;
        }

        var backingSources = hasBacking
            ? BackingOf(argument, dependencies, program, ref remaining, depth + 1)
            : EmptyBackingSources;
        var nativePointerSources = hasNativePointer
            ? NativePointerBackingOf(argument, dependencies, program, ref remaining, depth + 1)
            : EmptyBackingSources;
        var nativePointerMayBeUnknown = hasNativePointer
            && NativePointerMayBeUnknownOf(argument, dependencies, program,
                ref remaining, depth + 1);
        dependencies.Staged.Add(new StagedDependencySource(call, parameterIndex,
            [.. backingSources],
            [.. nativePointerSources],
            nativePointerMayBeUnknown));
    }

    private static void CheckBackingInvalidation(int owner, InitializationState state, TextSpan span)
    {
        if (state.Dependencies is not BackingDependencies facts)
        {
            return;
        }
        for (var slot = 0; slot < state.Length; slot++)
        {
            if ((state.Possibilities(slot) & 2) != 0
                && facts.Sources[slot]?.Contains(
                    ProvenanceAnchor.ForBackingSource(owner)) == true)
            {
                throw InvalidEffect(span, "Ownership");
            }
        }
        var ownerSource = ProvenanceAnchor.ForBackingSource(owner);
        if (facts.Staged.Any(staged => staged.BackingSources.Contains(ownerSource)))
        {
            throw InvalidEffect(span, "Ownership");
        }
    }

    private static bool VerifySpanOperand(BoundExpression view, InitializationState initialized, uint[] masks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures)
    {
        if (view is null || view.Type != PrimitiveType.TokenSpan)
        {
            throw InvalidEffect(function.Span, "Ownership");
        }
        if (!EvaluateExpression(view, initialized, masks, declarations, function, program,
            ref remaining, depth + 1, failures))
        {
            return false;
        }
        if (BackingOf(view, initialized.Dependencies, program, ref remaining, depth + 1).Count == 0)
        {
            throw InvalidEffect(view.Span, "Ownership");
        }
        return true;
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
                                || (index + 2 != block.Statements.Length
                                    && !(index + 3 == block.Statements.Length
                                        && block.Statements[^1] is BoundReturn terminal
                                        && ((function.ReturnType == PrimitiveType.Void && terminal.Value is null)
                                            || (function.IsConstructor
                                                && terminal.Value is BoundRead { Variable: 0 })))))
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
