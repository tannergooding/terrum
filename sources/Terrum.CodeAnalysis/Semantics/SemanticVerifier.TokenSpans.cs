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
        ReferencePlace,
        NormalBackingPlace,
        NormalBackingObject,
        NormalBackingObjectPlace,
        IncomingReference,
        IncomingReferencePlace,
        IncomingReferenceContents,
        IncomingReferenceField,
        ReferenceDescriptor,
        IncomingReferenceDescriptor,
    }

    private enum StorageProjectionKind : byte
    {
        WholeObject,
        ElementRange,
        ArrayElement,
        UnknownArrayElement,
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

    private readonly record struct NormalBackingObjectFieldKey(
        ProvenanceAnchor Object, StorageProjection Projection);

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

    private sealed class CopyOriginValue
    {
        private readonly Dictionary<BoundCopyOriginFieldPath, CopyOriginFact> _fields = [];

        internal CopyOriginValue Clone()
        {
            var clone = new CopyOriginValue();
            foreach (var (path, fact) in _fields)
            {
                clone._fields.Add(path, fact.Clone());
            }
            return clone;
        }

        internal CopyOriginFact Get(BoundCopyOriginFieldPath path)
            => _fields.TryGetValue(path, out var fact)
                ? fact.Clone() : CopyOriginFact.Unavailable();

        internal void Set(BoundCopyOriginFieldPath path, CopyOriginFact fact)
        {
            if (fact is null || !fact.IsValid)
            {
                throw Invalid();
            }
            _fields[path] = fact.Clone();
        }

        internal bool Merge(CopyOriginValue other)
        {
            var changed = false;
            foreach (var (path, fact) in other._fields)
            {
                if (_fields.TryGetValue(path, out var current))
                {
                    changed |= current.Merge(fact);
                }
                else
                {
                    _fields.Add(path, fact.Clone());
                    changed = true;
                }
            }
            return changed;
        }
    }

    private sealed record StagedCopyOriginInput(
        BoundCopyOriginSourceView SourceView,
        BoundExpression Argument,
        CopyOriginValue? Snapshot,
        Dictionary<StorageProjection, BackingFieldValue>? BackingSnapshot,
        Dictionary<StorageProjection, NativePointerFieldValue>? NativePointerSnapshot,
        Dictionary<StorageProjection, ReferenceFieldValue>? ReferenceSnapshot);

    private sealed class CopyOriginSummaryBuilder
    {
        private readonly BoundCopyOriginSummaryKind _kind;
        private readonly Dictionary<BoundReturn,
            Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>>> _returns
            = new(ReferenceEqualityComparer.Instance);

        private CopyOriginSummaryBuilder(BoundCopyOriginSummaryKind kind)
        {
            _kind = kind;
        }

        internal static CopyOriginSummaryBuilder? Create(
            BoundFunction function, BoundProgram program)
        {
            if (function.IsExternal || function.Intrinsic != BoundIntrinsic.None
                || function.IsClassConstructor || function.IsCleanup || function.DoesNotReturn)
            {
                return null;
            }
            if (function.DirectNativeImport is not null || function.NativeVTableMethod is not null
                || function.NativeFunctionPointerType is not null
                || function.NativeCallableEntryName is not null)
            {
                return null;
            }

            if (function.IsConstructor)
            {
                return IsInlineStructStorage(function.ReturnType, program)
                    && (IsCopyStruct(program, function.ReturnType)
                        || IsUnique(program, function.ReturnType))
                    && CarriesCopyOrigin(function.ReturnType, program)
                        ? new CopyOriginSummaryBuilder(
                            BoundCopyOriginSummaryKind.FreshInlineConstruction)
                        : null;
            }

            return IsCopyOriginValueType(function.ReturnType, program)
                && !IsUnique(program, function.ReturnType)
                && CarriesCopyOrigin(function.ReturnType, program)
                    ? new CopyOriginSummaryBuilder(BoundCopyOriginSummaryKind.NormalResult)
                    : null;
        }

        internal BoundCopyOriginSummaryKind Kind => _kind;

        internal void Observe(BoundReturn site,
            Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>> facts)
        {
            if (!_returns.TryGetValue(site, out var existing))
            {
                existing = new Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>>();
                foreach (var (path, origins) in facts)
                {
                    if (origins.Count > 0)
                    {
                        existing.Add(path, [.. origins]);
                    }
                }
                _returns.Add(site, existing);
                return;
            }

            foreach (var path in existing.Keys.ToArray())
            {
                if (facts.TryGetValue(path, out var origins) && origins.Count > 0)
                {
                    existing[path].UnionWith(origins);
                }
                else
                {
                    _ = existing.Remove(path);
                }
            }
        }

        internal BoundCopyOriginSummary? Build()
        {
            if (_returns.Count == 0)
            {
                return null;
            }

            var common = new Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>>();
            var first = true;
            foreach (var facts in _returns.Values)
            {
                if (first)
                {
                    foreach (var (path, origins) in facts)
                    {
                        common.Add(path, [.. origins]);
                    }
                    first = false;
                    continue;
                }

                foreach (var path in common.Keys.ToArray())
                {
                    if (facts.TryGetValue(path, out var origins) && origins.Count > 0)
                    {
                        common[path].UnionWith(origins);
                    }
                    else
                    {
                        _ = common.Remove(path);
                    }
                }
            }

            if (common.Count == 0)
            {
                return null;
            }

            var copyFacts = common
                .OrderBy(static pair => pair.Key, BoundCopyOriginPathComparer.Instance)
                .Select(static pair => new BoundCopyFact(pair.Key,
                    [.. pair.Value.OrderBy(static origin => origin, BoundCopyOriginComparer.Instance)]))
                .ToImmutableArray();
            return new BoundCopyOriginSummary(_kind, copyFacts);
        }
    }

    private sealed class BoundCopyOriginPathComparer : IComparer<BoundCopyOriginFieldPath>
    {
        internal static BoundCopyOriginPathComparer Instance { get; } = new();

        public int Compare(BoundCopyOriginFieldPath left, BoundCopyOriginFieldPath right)
        {
            var leftSteps = left.Steps;
            var rightSteps = right.Steps;
            var count = Math.Min(leftSteps.Length, rightSteps.Length);
            for (var index = 0; index < count; index++)
            {
                var owner = ((int)leftSteps[index].OwnerType).CompareTo(
                    (int)rightSteps[index].OwnerType);
                if (owner != 0)
                {
                    return owner;
                }
                var field = leftSteps[index].FieldOrdinal.CompareTo(
                    rightSteps[index].FieldOrdinal);
                if (field != 0)
                {
                    return field;
                }
            }
            return leftSteps.Length.CompareTo(rightSteps.Length);
        }
    }

    private sealed class BoundCopyOriginComparer : IComparer<BoundCopyOrigin>
    {
        internal static BoundCopyOriginComparer Instance { get; } = new();

        public int Compare(BoundCopyOrigin left, BoundCopyOrigin right)
        {
            var kind = ((byte)left.SourceKind).CompareTo((byte)right.SourceKind);
            if (kind != 0)
            {
                return kind;
            }
            var ordinal = left.SourceOrdinal.CompareTo(right.SourceOrdinal);
            if (ordinal != 0)
            {
                return ordinal;
            }
            var view = ((byte)left.SourceView).CompareTo((byte)right.SourceView);
            return view != 0 ? view
                : BoundCopyOriginPathComparer.Instance.Compare(left.SourcePath, right.SourcePath);
        }
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
        StorageProjectionKind Kind, ulong Start, ulong Length,
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
                StorageProjectionKind.ArrayElement or StorageProjectionKind.UnknownArrayElement
                    => this with { FieldPath = FieldPath.Append(ownerType, field) },
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
            if (suffix.Kind != StorageProjectionKind.InlineFieldPath)
            {
                return new StorageProjection(StorageProjectionKind.UnknownElement, 0, 0);
            }

            return prefix.Kind switch
            {
                StorageProjectionKind.InlineFieldPath => new StorageProjection(
                    StorageProjectionKind.InlineFieldPath, 0, 0,
                    prefix.FieldPath.Append(suffix.FieldPath)),
                StorageProjectionKind.ArrayElement or StorageProjectionKind.UnknownArrayElement
                    => prefix with {
                        FieldPath = prefix.FieldPath.Append(suffix.FieldPath),
                    },
                _ => new StorageProjection(StorageProjectionKind.UnknownElement, 0, 0),
            };
        }

        internal static StorageProjection ForArrayElement(BoundExpression index)
        {
            if (NativeFixedArrayContract.TryGetUnsignedIndex(index,
                    out var value))
            {
                return new StorageProjection(StorageProjectionKind.ArrayElement, value, 1);
            }
            return new StorageProjection(StorageProjectionKind.UnknownArrayElement, 0, 0);
        }

        internal static StorageProjection ForArrayIndex(ulong index) =>
            new(StorageProjectionKind.ArrayElement, index, 1);

        internal static StorageProjection ForStorageAddress(BoundNativeStorageAddress address) =>
            NativeFixedArrayContract.IsCarrier(address.CarrierType)
                ? new StorageProjection(StorageProjectionKind.ElementRange, 0, address.ElementCount)
                : WholeObject;
    }

    private static bool IsNativePointerFieldProjection(StorageProjection projection) =>
        projection.Kind is StorageProjectionKind.InlineFieldPath
            or StorageProjectionKind.ArrayElement
            or StorageProjectionKind.UnknownArrayElement
            or StorageProjectionKind.ElementRange;

    private readonly record struct StorageInstance(int SupportingScopeActivation, long LifetimeGeneration)
    {
        internal bool IsKnown => SupportingScopeActivation >= 0
            && LifetimeGeneration != StorageKey.UnknownGeneration;
    }

    private readonly record struct StorageKey(
        int Activation, int Site, StorageInstance Instance,
        StorageProjection Projection, long PlaceGeneration)
    {
        internal const long UnknownGeneration = long.MinValue;
        internal bool IsKnown => Activation >= 0 && Instance.IsKnown;

        internal StorageKey(int activation, int site, StorageInstance instance,
            StorageProjection projection)
            : this(activation, site, instance, projection, 0)
        {
        }
    }

    private enum CapabilityKind : byte
    {
        Owner,
        Lease,
        Guard,
        Root,
        Pin,
    }

    private readonly record struct CapabilityContract(
        CapabilityKind Kind, PrimitiveType Type, int SupportingPlace, StorageProjection Projection)
    {
        internal bool IsKnown => (byte)Kind <= (byte)CapabilityKind.Pin
            && SupportingPlace >= 0
            && Projection.Kind is StorageProjectionKind.WholeObject
                or StorageProjectionKind.InlineFieldPath
                or StorageProjectionKind.ElementRange
                or StorageProjectionKind.ArrayElement
                or StorageProjectionKind.UnknownArrayElement
                or StorageProjectionKind.UnknownElement;
    }

    // Acquisition identity is separate from the storage generation that supports it.
    private readonly record struct CapabilityKey(
        int FunctionActivation, int AcquisitionSite, int ExecutionActivation,
        CapabilityContract Contract)
    {
        internal bool IsKnown => FunctionActivation >= 0 && AcquisitionSite >= 0
            && ExecutionActivation >= 0 && Contract.IsKnown;
    }

    private sealed class CapabilityAcquisitionContext
    {
        private readonly Dictionary<BoundViewScope, int> _acquisitionSites = new(
            ReferenceEqualityComparer.Instance);
        private int _nextAcquisitionSite;

        internal CapabilityKey Acquire(int functionActivation, BoundViewScope operation,
            int executionActivation, CapabilityContract contract)
        {
            if (functionActivation < 0 || operation is null || executionActivation < 0
                || !contract.IsKnown)
            {
                throw Invalid();
            }
            if (!_acquisitionSites.TryGetValue(operation, out var acquisitionSite))
            {
                acquisitionSite = _nextAcquisitionSite++;
                _acquisitionSites.Add(operation, acquisitionSite);
            }

            var key = new CapabilityKey(functionActivation, acquisitionSite,
                executionActivation, contract);
            if (!key.IsKnown)
            {
                throw Invalid(operation.Span);
            }
            return key;
        }
    }

    private readonly record struct ProvenanceAnchor(
        ProvenanceAnchorKind Kind, int Identity, StorageKey? Storage,
        StorageProjection? IncomingProjection = null,
        int IncomingReferenceDepth = 0,
        PrimitiveType ObjectType = PrimitiveType.Void,
        string? ObjectPath = null)
    {
        internal static ProvenanceAnchor ForBackingSource(int source)
            => source >= 0
                ? new ProvenanceAnchor(ProvenanceAnchorKind.BackingSource, source, null)
                : new ProvenanceAnchor(ProvenanceAnchorKind.IncomingBacking, ~source, null);

        internal static ProvenanceAnchor ForNativeStorage(int activation, int variable,
            StorageInstance instance, StorageProjection projection)
            => new(ProvenanceAnchorKind.NativeStorage, variable,
                new StorageKey(activation, variable, instance, projection));

        internal static ProvenanceAnchor ForReferencePlace(int activation, int variable,
            StorageInstance instance)
            => ForReferencePlace(activation, variable, instance,
                StorageProjection.WholeObject);

        internal static ProvenanceAnchor ForReferencePlace(int activation, int variable,
            StorageInstance instance, StorageProjection projection)
            => new(ProvenanceAnchorKind.ReferencePlace, variable,
                new StorageKey(activation, variable, instance, projection));

        internal static ProvenanceAnchor ForReferencePlace(int activation, int variable,
            StorageInstance instance, StorageProjection projection, long placeGeneration)
            => new(ProvenanceAnchorKind.ReferencePlace, variable,
                new StorageKey(activation, variable, instance, projection, placeGeneration));

        internal static ProvenanceAnchor ForNormalBackingPlace(int activation, int variable,
            StorageInstance instance, StorageProjection projection, long placeGeneration)
            => new(ProvenanceAnchorKind.NormalBackingPlace, variable,
                new StorageKey(activation, variable, instance, projection, placeGeneration));

        internal static ProvenanceAnchor ForNormalBackingObject(int activation,
            int allocationSite, StorageInstance instance, PrimitiveType objectType)
            => new ProvenanceAnchor(ProvenanceAnchorKind.NormalBackingObject, allocationSite,
                new StorageKey(activation, allocationSite, instance,
                    StorageProjection.WholeObject), ObjectType: objectType,
                ObjectPath: $"{activation}:{allocationSite}:{instance.SupportingScopeActivation}:"
                    + $"{instance.LifetimeGeneration}:{(int)objectType}");

        internal static ProvenanceAnchor ForNestedNormalBackingObject(
            ProvenanceAnchor parent, ProvenanceAnchor child)
        {
            if (parent.Kind != ProvenanceAnchorKind.NormalBackingObject
                || parent.Storage is not StorageKey parentKey || !parentKey.IsKnown
                || child.Kind != ProvenanceAnchorKind.NormalBackingObject
                || child.Storage is not StorageKey childKey || !childKey.IsKnown)
            {
                throw Invalid();
            }
            return new ProvenanceAnchor(
                ProvenanceAnchorKind.NormalBackingObject, child.Identity,
                new StorageKey(parentKey.Activation, childKey.Site,
                    parentKey.Instance, StorageProjection.WholeObject),
                ObjectType: child.ObjectType,
                ObjectPath: $"{parent.ObjectPath}/{child.ObjectPath}");
        }

        internal static ProvenanceAnchor ForNormalBackingObjectPlace(
            ProvenanceAnchor objectIdentity, StorageProjection projection,
            long placeGeneration)
        {
            if (objectIdentity.Kind != ProvenanceAnchorKind.NormalBackingObject
                || objectIdentity.Storage is not StorageKey objectKey
                || !objectKey.IsKnown || projection.Kind
                    != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty
                || placeGeneration == StorageKey.UnknownGeneration)
            {
                throw Invalid();
            }
            return new ProvenanceAnchor(
                ProvenanceAnchorKind.NormalBackingObjectPlace,
                objectIdentity.Identity,
                new StorageKey(objectKey.Activation, objectKey.Site,
                    objectKey.Instance, projection, placeGeneration),
                ObjectType: objectIdentity.ObjectType,
                ObjectPath: objectIdentity.ObjectPath);
        }

        internal static ProvenanceAnchor ForIncomingReference(int formal)
            => new(ProvenanceAnchorKind.IncomingReference, formal, null);

        internal static ProvenanceAnchor ForIncomingReferencePlace(
            int formal, StorageProjection projection, int referenceDepth)
            => new(ProvenanceAnchorKind.IncomingReferencePlace, formal, null,
                projection, referenceDepth);

        internal static ProvenanceAnchor ForIncomingReferenceContents(
            int formal, int depth)
            => new(ProvenanceAnchorKind.IncomingReferenceContents, formal,
                null, IncomingReferenceDepth: depth);

        internal static ProvenanceAnchor ForIncomingReferenceField(
            int formal, StorageProjection projection, int referenceDepth = 0)
            => new(ProvenanceAnchorKind.IncomingReferenceField, formal, null,
                projection, referenceDepth);

        internal static ProvenanceAnchor ForReferenceDescriptor(int activation,
            int variable, StorageInstance instance)
            => new(ProvenanceAnchorKind.ReferenceDescriptor, variable,
                new StorageKey(activation, variable, instance,
                    StorageProjection.WholeObject));

        internal static ProvenanceAnchor ForReferenceDescriptor(int activation,
            int variable, StorageInstance instance, StorageProjection projection,
            long placeGeneration)
            => new(ProvenanceAnchorKind.ReferenceDescriptor, variable,
                new StorageKey(activation, variable, instance, projection,
                    placeGeneration));

        internal static ProvenanceAnchor ForIncomingReferenceDescriptor(int formal)
            => new(ProvenanceAnchorKind.IncomingReferenceDescriptor, formal, null);
    }

    private static readonly HashSet<ProvenanceAnchor> EmptyBackingSources
        = new HashSet<ProvenanceAnchor>();
    private static readonly Dictionary<StorageProjection, ReferenceFieldValue>
        EmptyReferenceFieldValues = [];

    private readonly record struct StagedDependencySource(
        BoundExpression? Call,
        int ParameterIndex,
        HashSet<ProvenanceAnchor> BackingSources,
        HashSet<ProvenanceAnchor> ReferenceTargets,
        HashSet<ProvenanceAnchor> ReferenceObligations,
        Dictionary<StorageProjection, ReferenceFieldValue> ReferenceFieldValues,
        HashSet<ProvenanceAnchor> NativePointerSources,
        bool NativePointerMayBeUnknown,
        NativePointerExtentFact NativePointerExtent);

    private readonly record struct NativePointerExtentFact(
        HashSet<ProvenanceAnchor> Sources,
        bool MayBeOpaque,
        bool IsUnavailable);

    private sealed class NativePointerFieldValue
    {
        internal HashSet<ProvenanceAnchor> Sources { get; }
        internal bool MayBeUnknown { get; set; }
        internal HashSet<ProvenanceAnchor> ExtentSources { get; }
        internal bool ExtentMayBeOpaque { get; set; }
        internal bool ExtentUnavailable { get; set; }

        internal NativePointerFieldValue(IReadOnlySet<ProvenanceAnchor> sources,
            bool mayBeUnknown, IReadOnlySet<ProvenanceAnchor>? extentSources = null,
            bool extentMayBeOpaque = false, bool extentUnavailable = true)
        {
            Sources = [.. sources];
            MayBeUnknown = mayBeUnknown;
            ExtentSources = extentSources is null ? [] : [.. extentSources];
            ExtentMayBeOpaque = extentMayBeOpaque;
            ExtentUnavailable = extentUnavailable;
        }

        internal NativePointerFieldValue Clone()
            => new(Sources, MayBeUnknown, ExtentSources,
                ExtentMayBeOpaque, ExtentUnavailable);
    }

    private sealed class BackingFieldValue
    {
        internal HashSet<ProvenanceAnchor> Sources { get; }
        internal bool IsAvailable { get; private set; }

        internal BackingFieldValue(IReadOnlySet<ProvenanceAnchor> sources, bool isAvailable)
        {
            Sources = [.. sources];
            IsAvailable = isAvailable;
        }

        internal static BackingFieldValue Unavailable()
            => new(new HashSet<ProvenanceAnchor>(), isAvailable: false);

        internal BackingFieldValue Clone() => new(Sources, IsAvailable);

        internal bool Merge(BackingFieldValue other)
        {
            var priorCount = Sources.Count;
            Sources.UnionWith(other.Sources);
            var isAvailable = IsAvailable && other.IsAvailable;
            var changed = priorCount != Sources.Count || isAvailable != IsAvailable;
            IsAvailable = isAvailable;
            return changed;
        }

        internal bool IsEquivalentTo(BackingFieldValue other)
            => IsAvailable == other.IsAvailable && Sources.SetEquals(other.Sources);
    }

    private sealed class ReferenceFieldValue
    {
        internal HashSet<ProvenanceAnchor> Targets { get; }
        internal HashSet<ProvenanceAnchor> Obligations { get; }
        internal HashSet<ProvenanceAnchor> BackingSources { get; }
        internal bool IsAvailable { get; private set; }

        internal ReferenceFieldValue(
            IReadOnlySet<ProvenanceAnchor> targets,
            IReadOnlySet<ProvenanceAnchor> obligations,
            IReadOnlySet<ProvenanceAnchor> backingSources,
            bool isAvailable)
        {
            Targets = [.. targets];
            Obligations = [.. obligations];
            BackingSources = [.. backingSources];
            IsAvailable = isAvailable;
        }

        internal static ReferenceFieldValue Unavailable()
            => new(new HashSet<ProvenanceAnchor>(),
                new HashSet<ProvenanceAnchor>(),
                new HashSet<ProvenanceAnchor>(), isAvailable: false);

        internal ReferenceFieldValue Clone()
            => new(Targets, Obligations, BackingSources, IsAvailable);

        internal bool Merge(ReferenceFieldValue other)
        {
            var priorTargetCount = Targets.Count;
            Targets.UnionWith(other.Targets);
            var priorObligationCount = Obligations.Count;
            Obligations.UnionWith(other.Obligations);
            var priorBackingCount = BackingSources.Count;
            BackingSources.UnionWith(other.BackingSources);
            var isAvailable = IsAvailable && other.IsAvailable;
            var changed = priorTargetCount != Targets.Count
                || priorObligationCount != Obligations.Count
                || priorBackingCount != BackingSources.Count
                || isAvailable != IsAvailable;
            IsAvailable = isAvailable;
            return changed;
        }

        internal bool IsEquivalentTo(ReferenceFieldValue other)
            => IsAvailable == other.IsAvailable
                && Targets.SetEquals(other.Targets)
                && Obligations.SetEquals(other.Obligations)
                && BackingSources.SetEquals(other.BackingSources);
    }

    private sealed class NormalBackingClassFieldValue
    {
        internal HashSet<ProvenanceAnchor> Objects { get; }
        internal bool MayBeUnknown { get; private set; }

        internal NormalBackingClassFieldValue(
            IReadOnlySet<ProvenanceAnchor> objects, bool mayBeUnknown)
        {
            Objects = [.. objects];
            MayBeUnknown = mayBeUnknown;
        }

        internal NormalBackingClassFieldValue Clone()
            => new(Objects, MayBeUnknown);

        internal bool Merge(NormalBackingClassFieldValue other)
        {
            var priorCount = Objects.Count;
            Objects.UnionWith(other.Objects);
            var changed = priorCount != Objects.Count
                || (other.MayBeUnknown && !MayBeUnknown);
            MayBeUnknown |= other.MayBeUnknown;
            return changed;
        }
    }

    private sealed class NormalBackingClassConstructorSummary(
        ProvenanceAnchor receiverIdentity,
        Dictionary<StorageProjection, ReferenceFieldValue> referenceFields,
        Dictionary<StorageProjection, NormalBackingClassFieldValue> classFields,
        Dictionary<int, HashSet<ProvenanceAnchor>> localObjects,
        HashSet<int> unknownObjectSlots)
    {
        internal ProvenanceAnchor ReceiverIdentity { get; } = receiverIdentity;
        internal Dictionary<StorageProjection, ReferenceFieldValue> ReferenceFields { get; }
            = referenceFields.ToDictionary(
                static pair => pair.Key, static pair => pair.Value.Clone());
        internal Dictionary<StorageProjection, NormalBackingClassFieldValue> ClassFields { get; }
            = classFields.ToDictionary(
                static pair => pair.Key, static pair => pair.Value.Clone());
        internal Dictionary<int, HashSet<ProvenanceAnchor>> LocalObjects { get; }
            = localObjects.ToDictionary(
                static pair => pair.Key,
                static pair => new HashSet<ProvenanceAnchor>(pair.Value));
        internal HashSet<int> UnknownObjectSlots { get; } = [.. unknownObjectSlots];
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
                        || function.Variables[slot].IsOut || function.Variables[slot].IsInit
                        || !IsSpanDependencySource(function.Variables[slot].Type, program))
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
                        || variable.IsOut || variable.IsInit || variable.IsInitializationLocal))
                {
                    throw InvalidEffect(variable.Span, "Ownership");
                }
            }
        }
        return hasSpans;
    }

    private static bool IsSpanResultReceiver(PrimitiveType type,
        BoundProgram program)
        => TokenSpanContract.IsSpanResultReceiver(type, program.Structures);

    private static bool IsSpanDependencySource(PrimitiveType type, BoundProgram program)
    {
        if (TokenSpanContract.IsDependent(type, program.Structures))
        {
            return true;
        }
        if (!CheckedReferenceTypeContract.IsValidType(type, program.CheckedReferenceTypes))
        {
            return false;
        }

        var referenceType = program.CheckedReferenceTypes[CheckedReferenceTypes.Index(type)];
        return TokenSpanContract.IsDependent(referenceType.ReferentType, program.Structures)
            && !referenceType.IsGarbageCollected
            && !referenceType.IsInitialization
            && !referenceType.IsOptional;
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

    private static BoundDependencyContract? RetainedReferenceCaptureContract(
        BoundFunction function, int parameterSlot)
    {
        var receiverOffset = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor && !function.IsClassConstructor
                ? 1 : 0;
        if (receiverOffset != 0 && parameterSlot == 0)
        {
            return function.DependencyContracts.FirstOrDefault(contract =>
                contract.Relation == BoundDependencyRelation.Captures
                && contract.Destination == BoundDependencyDestination.Receiver);
        }
        var ordinal = parameterSlot - receiverOffset;
        if (ordinal < 0 || parameterSlot >= function.ParameterCount)
        {
            return null;
        }
        return function.DependencyContracts.FirstOrDefault(contract =>
            contract.Relation == BoundDependencyRelation.Captures
            && contract.Destination == BoundDependencyDestination.RetainedFormal
            && contract.DestinationOrdinal == ordinal);
    }

    private static void VerifyReferenceWriteCapture(
        BoundFunction function, int parameterSlot,
        IReadOnlySet<ProvenanceAnchor> sources, TextSpan span)
    {
        var capture = RetainedReferenceCaptureContract(function, parameterSlot)
            ?? throw InvalidEffect(span, "Ownership");
        var receiverOffset = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor && !function.IsClassConstructor
                ? 1 : 0;
        var permittedSources = new HashSet<ProvenanceAnchor>();
        foreach (var source in capture.Sources)
        {
            var sourceSlot = source.Kind switch
            {
                BoundDependencySourceKind.Receiver when receiverOffset != 0 => 0,
                BoundDependencySourceKind.Formal when source.FormalOrdinal is int ordinal
                    => ordinal + receiverOffset,
                _ => -1,
            };
            if ((uint)sourceSlot >= (uint)function.ParameterCount)
            {
                throw InvalidEffect(span, "Ownership");
            }
            _ = permittedSources.Add(ProvenanceAnchor.ForBackingSource(~sourceSlot));
        }
        if (sources.Count == 0 || sources.Any(source => !permittedSources.Contains(source)))
        {
            throw InvalidEffect(span, "Ownership");
        }
    }

    private static bool IsForwardedCaptureSource(
        BoundFunction function, BoundDependencyContract capture,
        BoundDependencySource source)
        => function.DependencyContracts.Any(contract =>
            contract.Relation == BoundDependencyRelation.ForwardsBacking
            && contract.Destination == capture.Destination
            && contract.DestinationOrdinal == capture.DestinationOrdinal
            && contract.Sources.Contains(source));

    private static bool IsForwardedCaptureSource(
        BoundFunction function, int destinationSlot, int sourceSlot)
    {
        var receiverOffset = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor
            && !function.IsClassConstructor ? 1 : 0;
        var destination = receiverOffset != 0 && destinationSlot == 0
            ? BoundDependencyDestination.Receiver
            : BoundDependencyDestination.RetainedFormal;
        var destinationOrdinal = destinationSlot - receiverOffset;
        if (destination == BoundDependencyDestination.RetainedFormal
            && destinationOrdinal < 0)
        {
            return false;
        }
        var source = sourceSlot == 0 && receiverOffset != 0
            ? new BoundDependencySource(BoundDependencySourceKind.Receiver, null)
            : new BoundDependencySource(BoundDependencySourceKind.Formal,
                sourceSlot - receiverOffset);
        return function.DependencyContracts.Any(contract =>
            contract.Relation == BoundDependencyRelation.ForwardsBacking
            && contract.Destination == destination
            && (destination == BoundDependencyDestination.Receiver
                || contract.DestinationOrdinal == destinationOrdinal)
            && contract.Sources.Contains(source));
    }

    private static HashSet<ProvenanceAnchor> ReferenceCaptureSourcesOfValue(
        ReferenceFieldValue value, TextSpan span)
    {
        var sources = new HashSet<ProvenanceAnchor>();
        foreach (var target in value.Targets)
        {
            if (target.Kind is not (ProvenanceAnchorKind.IncomingReference
                or ProvenanceAnchorKind.IncomingReferenceContents
                or ProvenanceAnchorKind.IncomingReferenceField
                or ProvenanceAnchorKind.IncomingReferencePlace))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            _ = sources.Add(ProvenanceAnchor.ForBackingSource(~target.Identity));
        }
        foreach (var obligation in value.Obligations)
        {
            if (obligation.Kind == ProvenanceAnchorKind.IncomingReferenceDescriptor)
            {
                _ = sources.Add(ProvenanceAnchor.ForBackingSource(~obligation.Identity));
            }
            else if (obligation.Kind == ProvenanceAnchorKind.BackingSource
                && obligation.Identity < 0)
            {
                _ = sources.Add(obligation);
            }
            else
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
        foreach (var backingSource in value.BackingSources)
        {
            if (backingSource.Kind == ProvenanceAnchorKind.BackingSource
                && backingSource.Identity < 0)
            {
                _ = sources.Add(backingSource);
            }
            else
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
        return sources;
    }

    private static BoundExpression DependencyCallArgument(BoundCall call,
        BoundFunction callee, BoundDependencySource source)
    {
        var parameter = DependencyCallParameterIndex(callee, source);
        var argument = call.ArgumentIndex(parameter);
        return (uint)argument < (uint)call.Arguments.Length
            ? call.Arguments[argument] : throw Invalid(call.Span);
    }

    private static int DependencyCallParameterIndex(
        BoundFunction callee, BoundDependencySource source)
    {
        var receiverOffset = callee.DeclaringStruct is not null
            && !callee.IsStatic && !callee.IsConstructor && !callee.IsClassConstructor ? 1 : 0;
        return source.Kind switch
        {
            BoundDependencySourceKind.Receiver when receiverOffset != 0 => 0,
            BoundDependencySourceKind.Formal when source.FormalOrdinal is int ordinal
                => ordinal + receiverOffset,
            _ => throw Invalid(callee.Span),
        };
    }

    private sealed class BackingDependencies
    {
        // A slot conservatively retains every source it can hold on any path. Liveness
        // remains path-sensitive in InitializationState, including unwind replay.
        internal HashSet<ProvenanceAnchor>?[] Sources { get; }
        internal HashSet<ProvenanceAnchor>?[] BackingBounds { get; }
        internal HashSet<ProvenanceAnchor>?[] ReferenceTargets { get; }
        internal HashSet<ProvenanceAnchor>?[] ReferenceObligations { get; }
        internal HashSet<ProvenanceAnchor>?[] ReferenceBackingSources { get; }
        internal HashSet<ProvenanceAnchor>?[] NormalBackingObjects { get; }
        internal bool[] NormalBackingObjectsMayBeUnknown { get; }
        internal bool[] BackingMayBeUnknown { get; }
        internal HashSet<ProvenanceAnchor>?[] NativePointerSources { get; }
        internal bool[] NativePointerMayBeUnknown { get; }
        internal HashSet<ProvenanceAnchor>?[] NativePointerExtentSources { get; }
        internal bool[] NativePointerExtentMayBeOpaque { get; }
        internal bool[] NativePointerExtentUnavailable { get; }
        private Dictionary<StorageKey, BackingFieldValue> BackingFieldValues { get; } = [];
        internal Dictionary<StorageKey, ReferenceFieldValue> ReferenceFieldValues { get; } = [];
        private Dictionary<NormalBackingObjectFieldKey, ReferenceFieldValue>
            NormalBackingReferenceFieldValues { get; } = [];
        private Dictionary<NormalBackingObjectFieldKey, NormalBackingClassFieldValue>
            NormalBackingClassFieldValues { get; } = [];
        internal Dictionary<ProvenanceAnchor, ReferenceFieldValue>
            IncomingReferenceFieldValues { get; } = [];
        private Dictionary<StorageKey, NativePointerFieldValue> NativePointerFieldValues { get; } = [];
        private Dictionary<BoundExpression,
            Dictionary<StorageProjection, NativePointerFieldValue>> NativePointerCallResults { get; }
            = new(ReferenceEqualityComparer.Instance);
        private CopyOriginFact[] CopyOriginRoots { get; }
        private Dictionary<StorageKey, CopyOriginFact> CopyOriginFieldValues { get; } = [];
        private Dictionary<BoundExpression, CopyOriginValue> CopyOriginCallResults { get; }
            = new(ReferenceEqualityComparer.Instance);
        private Dictionary<BoundExpression,
            Dictionary<StorageProjection, BackingFieldValue>> BackingCallResults { get; }
            = new(ReferenceEqualityComparer.Instance);
        private Dictionary<BoundExpression,
            Dictionary<StorageProjection, ReferenceFieldValue>> ReferenceFieldCallResults { get; }
            = new(ReferenceEqualityComparer.Instance);
        internal BoundCopyOriginSummary?[] CopyOriginSummaries { get; }
        internal NormalBackingClassConstructorSummary?[] NormalBackingClassConstructorSummaries { get; }
        internal CopyOriginSummaryBuilder? CopyOriginSummaryBuilder { get; }
        private HashSet<StorageInstance>[] StorageInstances { get; }
        private StorageActivationContext StorageActivationContext { get; }
        internal int FunctionActivation { get; }
        internal BoundFunction Function { get; }
        internal const int RootScopeActivation = 0;
        internal List<StagedDependencySource> Staged { get; } = [];

        internal BackingDependencies(int functionActivation, BoundFunction function, int length,
            BoundCopyOriginSummary?[] copyOriginSummaries,
            NormalBackingClassConstructorSummary?[] normalBackingClassConstructorSummaries,
            CopyOriginSummaryBuilder? copyOriginSummaryBuilder)
            : this(functionActivation, function, length, copyOriginSummaries,
                normalBackingClassConstructorSummaries,
                new StorageActivationContext(), copyOriginSummaryBuilder)
        {
        }

        private BackingDependencies(int functionActivation, BoundFunction function, int length,
            BoundCopyOriginSummary?[] copyOriginSummaries,
            NormalBackingClassConstructorSummary?[] normalBackingClassConstructorSummaries,
            StorageActivationContext storageActivationContext,
            CopyOriginSummaryBuilder? copyOriginSummaryBuilder)
        {
            FunctionActivation = functionActivation;
            Function = function;
            CopyOriginSummaries = copyOriginSummaries;
            NormalBackingClassConstructorSummaries =
                normalBackingClassConstructorSummaries;
            StorageActivationContext = storageActivationContext;
            CopyOriginSummaryBuilder = copyOriginSummaryBuilder;
            Sources = new HashSet<ProvenanceAnchor>?[length];
            BackingBounds = new HashSet<ProvenanceAnchor>?[length];
            ReferenceTargets = new HashSet<ProvenanceAnchor>?[length];
            ReferenceObligations = new HashSet<ProvenanceAnchor>?[length];
            ReferenceBackingSources = new HashSet<ProvenanceAnchor>?[length];
            NormalBackingObjects = new HashSet<ProvenanceAnchor>?[length];
            NormalBackingObjectsMayBeUnknown = new bool[length];
            BackingMayBeUnknown = new bool[length];
            NativePointerSources = new HashSet<ProvenanceAnchor>?[length];
            NativePointerMayBeUnknown = new bool[length];
            NativePointerExtentSources = new HashSet<ProvenanceAnchor>?[length];
            NativePointerExtentMayBeOpaque = new bool[length];
            NativePointerExtentUnavailable = new bool[length];
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
                CopyOriginSummaries, NormalBackingClassConstructorSummaries,
                StorageActivationContext, CopyOriginSummaryBuilder);
            CopyTo(clone);
            return clone;
        }

        internal void CopyTo(BackingDependencies destination)
        {
            if (destination.Sources.Length != Sources.Length
                || destination.FunctionActivation != FunctionActivation
                || !ReferenceEquals(destination.CopyOriginSummaries, CopyOriginSummaries)
                || !ReferenceEquals(destination.NormalBackingClassConstructorSummaries,
                    NormalBackingClassConstructorSummaries)
                || !ReferenceEquals(destination.StorageActivationContext, StorageActivationContext)
                || !ReferenceEquals(destination.CopyOriginSummaryBuilder, CopyOriginSummaryBuilder))
            {
                throw Invalid();
            }

            for (var slot = 0; slot < Sources.Length; slot++)
            {
                destination.Sources[slot] = CloneSet(Sources[slot]);
                destination.BackingBounds[slot] = CloneSet(BackingBounds[slot]);
                destination.ReferenceTargets[slot] = CloneSet(ReferenceTargets[slot]);
                destination.ReferenceObligations[slot] =
                    CloneSet(ReferenceObligations[slot]);
                destination.ReferenceBackingSources[slot] =
                    CloneSet(ReferenceBackingSources[slot]);
                destination.NormalBackingObjects[slot] =
                    CloneSet(NormalBackingObjects[slot]);
                destination.NormalBackingObjectsMayBeUnknown[slot] =
                    NormalBackingObjectsMayBeUnknown[slot];
                destination.BackingMayBeUnknown[slot] = BackingMayBeUnknown[slot];
                destination.NativePointerSources[slot] = CloneSet(NativePointerSources[slot]);
                destination.NativePointerMayBeUnknown[slot] = NativePointerMayBeUnknown[slot];
                destination.NativePointerExtentSources[slot] =
                    CloneSet(NativePointerExtentSources[slot]);
                destination.NativePointerExtentMayBeOpaque[slot] =
                    NativePointerExtentMayBeOpaque[slot];
                destination.NativePointerExtentUnavailable[slot] =
                    NativePointerExtentUnavailable[slot];
                destination.CopyOriginRoots[slot] = CopyOriginRoots[slot].Clone();
                destination.StorageInstances[slot] = [.. StorageInstances[slot]];
            }
            destination.Staged.Clear();
            foreach (var staged in Staged)
            {
                destination.Staged.Add(staged with {
                    BackingSources = [.. staged.BackingSources],
                    ReferenceTargets = [.. staged.ReferenceTargets],
                    ReferenceObligations = [.. staged.ReferenceObligations],
                    ReferenceFieldValues = staged.ReferenceFieldValues.ToDictionary(
                        static pair => pair.Key, static pair => pair.Value.Clone()),
                    NativePointerSources = [.. staged.NativePointerSources],
                    NativePointerExtent = staged.NativePointerExtent with {
                        Sources = [.. staged.NativePointerExtent.Sources],
                    },
                });
            }
            destination.NativePointerFieldValues.Clear();
            foreach (var (key, value) in NativePointerFieldValues)
            {
                destination.NativePointerFieldValues.Add(key, value.Clone());
            }
            destination.BackingFieldValues.Clear();
            foreach (var (key, value) in BackingFieldValues)
            {
                destination.BackingFieldValues.Add(key, value.Clone());
            }
            destination.ReferenceFieldValues.Clear();
            foreach (var (key, value) in ReferenceFieldValues)
            {
                destination.ReferenceFieldValues.Add(key, value.Clone());
            }
            destination.NormalBackingReferenceFieldValues.Clear();
            foreach (var (key, value) in NormalBackingReferenceFieldValues)
            {
                destination.NormalBackingReferenceFieldValues.Add(key, value.Clone());
            }
            destination.NormalBackingClassFieldValues.Clear();
            foreach (var (key, value) in NormalBackingClassFieldValues)
            {
                destination.NormalBackingClassFieldValues.Add(key, value.Clone());
            }
            destination.IncomingReferenceFieldValues.Clear();
            foreach (var (key, value) in IncomingReferenceFieldValues)
            {
                destination.IncomingReferenceFieldValues.Add(key, value.Clone());
            }
            destination.NativePointerCallResults.Clear();
            foreach (var (expression, values) in NativePointerCallResults)
            {
                destination.NativePointerCallResults.Add(expression,
                    values.ToDictionary(static pair => pair.Key,
                        static pair => pair.Value.Clone()));
            }
            destination.CopyOriginFieldValues.Clear();
            foreach (var (key, value) in CopyOriginFieldValues)
            {
                destination.CopyOriginFieldValues.Add(key, value.Clone());
            }
            destination.CopyOriginCallResults.Clear();
            foreach (var (expression, value) in CopyOriginCallResults)
            {
                destination.CopyOriginCallResults.Add(expression, value.Clone());
            }
            destination.BackingCallResults.Clear();
            foreach (var (expression, values) in BackingCallResults)
            {
                destination.BackingCallResults.Add(expression,
                    values.ToDictionary(static pair => pair.Key,
                        static pair => pair.Value.Clone()));
            }
            destination.ReferenceFieldCallResults.Clear();
            foreach (var (expression, values) in ReferenceFieldCallResults)
            {
                destination.ReferenceFieldCallResults.Add(expression,
                    values.ToDictionary(static pair => pair.Key,
                        static pair => pair.Value.Clone()));
            }
        }

        internal bool MergeCopyOriginCallResults(BackingDependencies other)
        {
            if (other.Sources.Length != Sources.Length
                || other.FunctionActivation != FunctionActivation
                || !ReferenceEquals(other.CopyOriginSummaries, CopyOriginSummaries))
            {
                throw Invalid();
            }

            var changed = false;
            foreach (var (call, value) in other.CopyOriginCallResults)
            {
                if (CopyOriginCallResults.TryGetValue(call, out var current))
                {
                    changed |= current.Merge(value);
                }
                else
                {
                    CopyOriginCallResults.Add(call, value.Clone());
                    changed = true;
                }
            }
            return changed;
        }

        internal bool MergeNativePointerCallResults(BackingDependencies other)
        {
            if (other.Sources.Length != Sources.Length
                || other.FunctionActivation != FunctionActivation
                || !ReferenceEquals(other.CopyOriginSummaries, CopyOriginSummaries))
            {
                throw Invalid();
            }

            var changed = false;
            foreach (var (call, values) in other.NativePointerCallResults)
            {
                if (!NativePointerCallResults.TryGetValue(call, out var currentValues))
                {
                    NativePointerCallResults.Add(call,
                        values.ToDictionary(static pair => pair.Key,
                            static pair => pair.Value.Clone()));
                    changed = true;
                    continue;
                }

                foreach (var (projection, value) in values)
                {
                    if (currentValues.TryGetValue(projection, out var current))
                    {
                        var priorCount = current.Sources.Count;
                        current.Sources.UnionWith(value.Sources);
                        changed |= priorCount != current.Sources.Count;
                        var priorExtentCount = current.ExtentSources.Count;
                        current.ExtentSources.UnionWith(value.ExtentSources);
                        changed |= priorExtentCount != current.ExtentSources.Count;
                        if (value.MayBeUnknown && !current.MayBeUnknown)
                        {
                            current.MayBeUnknown = true;
                            changed = true;
                        }
                        if (value.ExtentMayBeOpaque && !current.ExtentMayBeOpaque)
                        {
                            current.ExtentMayBeOpaque = true;
                            changed = true;
                        }
                        if (value.ExtentUnavailable && !current.ExtentUnavailable)
                        {
                            current.ExtentUnavailable = true;
                            changed = true;
                        }
                    }
                    else
                    {
                        currentValues.Add(projection, value.Clone());
                        changed = true;
                    }
                }
            }
            return changed;
        }

        internal bool MergeBackingCallResults(BackingDependencies other)
        {
            if (other.Sources.Length != Sources.Length
                || other.FunctionActivation != FunctionActivation
                || !ReferenceEquals(other.CopyOriginSummaries, CopyOriginSummaries))
            {
                throw Invalid();
            }

            var changed = false;
            foreach (var (call, values) in other.BackingCallResults)
            {
                if (!BackingCallResults.TryGetValue(call, out var currentValues))
                {
                    BackingCallResults.Add(call,
                        values.ToDictionary(static pair => pair.Key,
                            static pair => pair.Value.Clone()));
                    changed = true;
                    continue;
                }

                foreach (var (projection, value) in values)
                {
                    if (currentValues.TryGetValue(projection, out var current))
                    {
                        changed |= current.Merge(value);
                    }
                    else
                    {
                        currentValues.Add(projection, value.Clone());
                        changed = true;
                    }
                }
            }
            return changed;
        }

        internal bool MergeReferenceFieldCallResults(BackingDependencies other)
        {
            if (other.Sources.Length != Sources.Length
                || other.FunctionActivation != FunctionActivation
                || !ReferenceEquals(other.CopyOriginSummaries, CopyOriginSummaries))
            {
                throw Invalid();
            }

            var changed = false;
            foreach (var (call, values) in other.ReferenceFieldCallResults)
            {
                if (!ReferenceFieldCallResults.TryGetValue(call, out var currentValues))
                {
                    ReferenceFieldCallResults.Add(call,
                        values.ToDictionary(static pair => pair.Key,
                            static pair => pair.Value.Clone()));
                    changed = true;
                    continue;
                }

                foreach (var (projection, value) in values)
                {
                    if (currentValues.TryGetValue(projection, out var current))
                    {
                        changed |= current.Merge(value);
                    }
                    else
                    {
                        currentValues.Add(projection, value.Clone());
                        changed = true;
                    }
                }
            }
            return changed;
        }

        private bool MergeNormalBackingReferenceFieldValues(
            BackingDependencies other)
        {
            var changed = false;
            var keys = NormalBackingReferenceFieldValues.Keys
                .Concat(other.NormalBackingReferenceFieldValues.Keys)
                .Distinct()
                .ToArray();
            foreach (var key in keys)
            {
                var hasCurrent = NormalBackingReferenceFieldValues.TryGetValue(
                    key, out var currentValue);
                var hasOther = other.NormalBackingReferenceFieldValues.TryGetValue(
                    key, out var otherValue);
                if (hasCurrent && hasOther)
                {
                    changed |= currentValue!.Merge(otherValue!);
                }
                else if (hasCurrent)
                {
                    changed |= currentValue!.Merge(
                        ReferenceFieldValue.Unavailable());
                }
                else
                {
                    currentValue = otherValue!.Clone();
                    changed |= currentValue.Merge(
                        ReferenceFieldValue.Unavailable());
                    NormalBackingReferenceFieldValues.Add(key, currentValue);
                    changed = true;
                }
            }
            return changed;
        }

        private bool MergeNormalBackingClassFieldValues(
            BackingDependencies other)
        {
            var changed = false;
            var keys = NormalBackingClassFieldValues.Keys
                .Concat(other.NormalBackingClassFieldValues.Keys)
                .Distinct()
                .ToArray();
            foreach (var key in keys)
            {
                var hasCurrent = NormalBackingClassFieldValues.TryGetValue(
                    key, out var currentValue);
                var hasOther = other.NormalBackingClassFieldValues.TryGetValue(
                    key, out var otherValue);
                if (hasCurrent && hasOther)
                {
                    changed |= currentValue!.Merge(otherValue!);
                }
                else if (hasCurrent)
                {
                    if (!currentValue!.MayBeUnknown)
                    {
                        currentValue = new NormalBackingClassFieldValue(
                            currentValue.Objects, mayBeUnknown: true);
                        NormalBackingClassFieldValues[key] = currentValue;
                        changed = true;
                    }
                }
                else
                {
                    currentValue = otherValue!.Clone();
                    _ = currentValue.Merge(new NormalBackingClassFieldValue(
                        EmptyBackingSources, mayBeUnknown: true));
                    NormalBackingClassFieldValues.Add(key, currentValue);
                    changed = true;
                }
            }
            return changed;
        }

        internal void ClearCopyOriginCallResult(BoundExpression expression)
        {
            if (expression is null)
            {
                throw Invalid();
            }
            _ = CopyOriginCallResults.Remove(expression);
        }

        internal void ClearBackingCallResult(BoundExpression expression)
        {
            if (expression is null)
            {
                throw Invalid();
            }
            _ = BackingCallResults.Remove(expression);
        }

        internal void ClearReferenceFieldCallResult(BoundExpression expression)
        {
            if (expression is null)
            {
                throw Invalid();
            }
            _ = ReferenceFieldCallResults.Remove(expression);
        }

        internal void SetReferenceFieldCallResult(BoundExpression expression,
            IReadOnlyDictionary<StorageProjection, ReferenceFieldValue> values)
        {
            if (expression is null || values is null || values.Count == 0)
            {
                throw Invalid();
            }
            ReferenceFieldCallResults[expression] = values.ToDictionary(
                static pair => pair.Key, static pair => pair.Value.Clone());
        }

        internal bool TryGetReferenceFieldCallResult(BoundExpression expression,
            StorageProjection projection, out ReferenceFieldValue value)
        {
            if (expression is null)
            {
                throw Invalid();
            }
            if (ReferenceFieldCallResults.TryGetValue(expression, out var values)
                && values.TryGetValue(projection, out var existing))
            {
                value = existing.Clone();
                return true;
            }
            value = ReferenceFieldValue.Unavailable();
            return false;
        }

        internal void ClearNativePointerCallResult(BoundExpression expression)
        {
            if (expression is null)
            {
                throw Invalid();
            }
            _ = NativePointerCallResults.Remove(expression);
        }

        internal void SetNativePointerCallResult(BoundExpression expression,
            IReadOnlyDictionary<StorageProjection, NativePointerFieldValue> values)
        {
            if (expression is null || values is null || values.Count == 0)
            {
                throw Invalid();
            }

            NativePointerCallResults[expression] = values.ToDictionary(
                static pair => pair.Key, static pair => pair.Value.Clone());
        }

        internal bool TryGetNativePointerCallResult(BoundExpression expression,
            StorageProjection projection, out HashSet<ProvenanceAnchor> sources,
            out bool mayBeUnknown)
        {
            if (expression is null)
            {
                throw Invalid();
            }

            if (NativePointerCallResults.TryGetValue(expression, out var values)
                && values.TryGetValue(projection, out var value))
            {
                sources = [.. value.Sources];
                mayBeUnknown = value.MayBeUnknown;
                return true;
            }
            sources = [];
            mayBeUnknown = true;
            return false;
        }

        internal bool TryGetNativePointerCallExtentResult(BoundExpression expression,
            StorageProjection projection, out NativePointerExtentFact extentFact)
        {
            if (expression is null)
            {
                throw Invalid();
            }

            if (NativePointerCallResults.TryGetValue(expression, out var values)
                && values.TryGetValue(projection, out var value))
            {
                extentFact = new NativePointerExtentFact(
                    [.. value.ExtentSources], value.ExtentMayBeOpaque,
                    value.ExtentUnavailable);
                return true;
            }
            extentFact = new NativePointerExtentFact([], MayBeOpaque: false,
                IsUnavailable: true);
            return false;
        }

        internal void SetCopyOriginCallResult(BoundExpression expression, CopyOriginValue value)
        {
            if (expression is null || value is null)
            {
                throw Invalid();
            }
            CopyOriginCallResults[expression] = value.Clone();
        }

        internal bool TryGetCopyOriginCallResult(BoundExpression expression,
            out CopyOriginValue value)
        {
            if (expression is null)
            {
                throw Invalid();
            }
            return CopyOriginCallResults.TryGetValue(expression, out value!);
        }

        internal void SetBackingCallResult(BoundExpression expression,
            IReadOnlyDictionary<StorageProjection, BackingFieldValue> values)
        {
            if (expression is null || values is null || values.Count == 0)
            {
                throw Invalid();
            }
            BackingCallResults[expression] = values.ToDictionary(
                static pair => pair.Key, static pair => pair.Value.Clone());
        }

        internal bool TryGetBackingCallResult(BoundExpression expression,
            StorageProjection projection, out BackingFieldValue value)
        {
            if (expression is null)
            {
                throw Invalid();
            }
            if (BackingCallResults.TryGetValue(expression, out var values)
                && values.TryGetValue(projection, out var existing))
            {
                value = existing.Clone();
                return true;
            }
            value = BackingFieldValue.Unavailable();
            return false;
        }

        internal bool MergeSlot(int slot, BackingDependencies other)
        {
            if ((uint)slot >= (uint)Sources.Length || other.Sources.Length != Sources.Length)
            {
                throw Invalid();
            }

            var changed = MergeSet(ref Sources[slot], other.Sources[slot]);
            changed |= MergeSet(ref BackingBounds[slot], other.BackingBounds[slot]);
            changed |= MergeSet(ref ReferenceTargets[slot], other.ReferenceTargets[slot]);
            changed |= MergeSet(ref ReferenceObligations[slot],
                other.ReferenceObligations[slot]);
            changed |= MergeSet(ref ReferenceBackingSources[slot],
                other.ReferenceBackingSources[slot]);
            changed |= MergeSet(ref NormalBackingObjects[slot],
                other.NormalBackingObjects[slot]);
            if (other.NormalBackingObjectsMayBeUnknown[slot]
                && !NormalBackingObjectsMayBeUnknown[slot])
            {
                NormalBackingObjectsMayBeUnknown[slot] = true;
                changed = true;
            }
            if (other.BackingMayBeUnknown[slot] && !BackingMayBeUnknown[slot])
            {
                BackingMayBeUnknown[slot] = true;
                changed = true;
            }
            changed |= MergeSet(ref NativePointerSources[slot], other.NativePointerSources[slot]);
            changed |= MergeSet(ref NativePointerExtentSources[slot],
                other.NativePointerExtentSources[slot]);
            changed |= CopyOriginRoots[slot].Merge(other.CopyOriginRoots[slot]);
            if (other.NativePointerMayBeUnknown[slot] && !NativePointerMayBeUnknown[slot])
            {
                NativePointerMayBeUnknown[slot] = true;
                changed = true;
            }
            if (other.NativePointerExtentMayBeOpaque[slot]
                && !NativePointerExtentMayBeOpaque[slot])
            {
                NativePointerExtentMayBeOpaque[slot] = true;
                changed = true;
            }
            if (other.NativePointerExtentUnavailable[slot]
                && !NativePointerExtentUnavailable[slot])
            {
                NativePointerExtentUnavailable[slot] = true;
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
                    var currentExtentCount = currentValue.ExtentSources.Count;
                    currentValue.ExtentSources.UnionWith(otherValue.ExtentSources);
                    changed |= currentExtentCount != currentValue.ExtentSources.Count;
                    if (otherValue.ExtentMayBeOpaque && !currentValue.ExtentMayBeOpaque)
                    {
                        currentValue.ExtentMayBeOpaque = true;
                        changed = true;
                    }
                    if (otherValue.ExtentUnavailable && !currentValue.ExtentUnavailable)
                    {
                        currentValue.ExtentUnavailable = true;
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
                    if (other.StorageInstances[slot].Contains(key.Instance)
                        && !currentValue!.ExtentUnavailable)
                    {
                        currentValue.ExtentUnavailable = true;
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
                    if (currentInstances.Contains(key.Instance))
                    {
                        currentValue.ExtentUnavailable = true;
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
            var backingFieldKeys = BackingFieldValues.Keys
                .Concat(other.BackingFieldValues.Keys)
                .Where(key => key.Site == slot)
                .Distinct()
                .ToArray();
            foreach (var key in backingFieldKeys)
            {
                var hasCurrent = BackingFieldValues.TryGetValue(key, out var currentValue);
                var hasOther = other.BackingFieldValues.TryGetValue(key, out var otherValue);
                if (hasCurrent && hasOther)
                {
                    changed |= currentValue!.Merge(otherValue!);
                }
                else if (hasCurrent)
                {
                    if (other.StorageInstances[slot].Contains(key.Instance))
                    {
                        changed |= currentValue!.Merge(BackingFieldValue.Unavailable());
                    }
                }
                else
                {
                    currentValue = otherValue!.Clone();
                    if (currentInstances.Contains(key.Instance))
                    {
                        changed |= currentValue.Merge(BackingFieldValue.Unavailable());
                    }
                    BackingFieldValues.Add(key, currentValue);
                    changed = true;
                }
            }
            var referenceFieldKeys = ReferenceFieldValues.Keys
                .Concat(other.ReferenceFieldValues.Keys)
                .Where(key => key.Site == slot)
                .Distinct()
                .ToArray();
            foreach (var key in referenceFieldKeys)
            {
                var hasCurrent = ReferenceFieldValues.TryGetValue(key, out var currentValue);
                var hasOther = other.ReferenceFieldValues.TryGetValue(key, out var otherValue);
                if (hasCurrent && hasOther)
                {
                    changed |= currentValue!.Merge(otherValue!);
                }
                else if (hasCurrent)
                {
                    if (other.StorageInstances[slot].Contains(key.Instance))
                    {
                        changed |= currentValue!.Merge(ReferenceFieldValue.Unavailable());
                    }
                }
                else
                {
                    currentValue = otherValue!.Clone();
                    if (currentInstances.Contains(key.Instance))
                    {
                        changed |= currentValue.Merge(ReferenceFieldValue.Unavailable());
                    }
                    ReferenceFieldValues.Add(key, currentValue);
                    changed = true;
                }
            }
            var incomingReferenceFieldKeys = IncomingReferenceFieldValues.Keys
                .Concat(other.IncomingReferenceFieldValues.Keys)
                .Where(key => key.Identity == slot)
                .Distinct()
                .ToArray();
            foreach (var key in incomingReferenceFieldKeys)
            {
                var hasCurrent = IncomingReferenceFieldValues.TryGetValue(
                    key, out var currentValue);
                var hasOther = other.IncomingReferenceFieldValues.TryGetValue(
                    key, out var otherValue);
                if (hasCurrent && hasOther)
                {
                    changed |= currentValue!.Merge(otherValue!);
                }
                else if (hasCurrent)
                {
                    changed |= currentValue!.Merge(
                        IncomingReferenceFieldValueForUnstoredPlace(
                            key, other.ReferenceBackingSources[slot]));
                }
                else
                {
                    currentValue = otherValue!.Clone();
                    changed |= currentValue.Merge(
                        IncomingReferenceFieldValueForUnstoredPlace(
                            key, ReferenceBackingSources[slot]));
                    IncomingReferenceFieldValues.Add(key, currentValue);
                    changed = true;
                }
            }
            if (!BackingMayBeUnknown[slot]
                && BackingFieldValues.Any(pair => pair.Key.Site == slot
                    && !pair.Value.IsAvailable))
            {
                BackingMayBeUnknown[slot] = true;
                changed = true;
            }
            changed |= MergeCopyOriginCallResults(other);
            changed |= MergeNativePointerCallResults(other);
            changed |= MergeBackingCallResults(other);
            changed |= MergeReferenceFieldCallResults(other);
            changed |= MergeNormalBackingReferenceFieldValues(other);
            changed |= MergeNormalBackingClassFieldValues(other);
            return changed;
        }

        internal bool CopySlot(int slot, BackingDependencies other)
        {
            if ((uint)slot >= (uint)Sources.Length || other.Sources.Length != Sources.Length)
            {
                throw Invalid();
            }

            var changed = !SetEquals(Sources[slot], other.Sources[slot])
                || !SetEquals(BackingBounds[slot], other.BackingBounds[slot])
                || !SetEquals(ReferenceTargets[slot], other.ReferenceTargets[slot])
                || !SetEquals(ReferenceObligations[slot],
                    other.ReferenceObligations[slot])
                || !SetEquals(ReferenceBackingSources[slot],
                    other.ReferenceBackingSources[slot])
                || !SetEquals(NormalBackingObjects[slot],
                    other.NormalBackingObjects[slot])
                || NormalBackingObjectsMayBeUnknown[slot]
                    != other.NormalBackingObjectsMayBeUnknown[slot]
                || BackingMayBeUnknown[slot] != other.BackingMayBeUnknown[slot]
                || !SetEquals(NativePointerSources[slot], other.NativePointerSources[slot])
                || NativePointerMayBeUnknown[slot] != other.NativePointerMayBeUnknown[slot]
                || !SetEquals(NativePointerExtentSources[slot],
                    other.NativePointerExtentSources[slot])
                || NativePointerExtentMayBeOpaque[slot]
                    != other.NativePointerExtentMayBeOpaque[slot]
                || NativePointerExtentUnavailable[slot]
                    != other.NativePointerExtentUnavailable[slot]
                || !CopyOriginRoots[slot].IsEquivalentTo(other.CopyOriginRoots[slot])
                || !StorageInstances[slot].SetEquals(other.StorageInstances[slot])
                || !BackingFieldValuesEqualForSlot(slot, other)
                || !ReferenceFieldValuesEqualForSlot(slot, other)
                || !IncomingReferenceFieldValuesEqualForSlot(slot, other)
                || !NativePointerFieldValuesEqualForSlot(slot, other);
            changed |= !CopyOriginFieldValuesEqualForSlot(slot, other);
            Sources[slot] = CloneSet(other.Sources[slot]);
            BackingBounds[slot] = CloneSet(other.BackingBounds[slot]);
            ReferenceTargets[slot] = CloneSet(other.ReferenceTargets[slot]);
            ReferenceObligations[slot] =
                CloneSet(other.ReferenceObligations[slot]);
            ReferenceBackingSources[slot] =
                CloneSet(other.ReferenceBackingSources[slot]);
            NormalBackingObjects[slot] =
                CloneSet(other.NormalBackingObjects[slot]);
            NormalBackingObjectsMayBeUnknown[slot] =
                other.NormalBackingObjectsMayBeUnknown[slot];
            BackingMayBeUnknown[slot] = other.BackingMayBeUnknown[slot];
            NativePointerSources[slot] = CloneSet(other.NativePointerSources[slot]);
            NativePointerMayBeUnknown[slot] = other.NativePointerMayBeUnknown[slot];
            NativePointerExtentSources[slot] =
                CloneSet(other.NativePointerExtentSources[slot]);
            NativePointerExtentMayBeOpaque[slot] =
                other.NativePointerExtentMayBeOpaque[slot];
            NativePointerExtentUnavailable[slot] =
                other.NativePointerExtentUnavailable[slot];
            CopyOriginRoots[slot] = other.CopyOriginRoots[slot].Clone();
            StorageInstances[slot] = [.. other.StorageInstances[slot]];
            ClearBackingFieldValues(slot);
            foreach (var (key, value) in other.BackingFieldValues)
            {
                if (key.Site == slot)
                {
                    BackingFieldValues.Add(key, value.Clone());
                }
            }
            ClearReferenceFieldValues(slot);
            foreach (var (key, value) in other.ReferenceFieldValues)
            {
                if (key.Site == slot)
                {
                    ReferenceFieldValues.Add(key, value.Clone());
                }
            }
            ClearIncomingReferenceFieldValues(slot);
            foreach (var (key, value) in other.IncomingReferenceFieldValues)
            {
                if (key.Identity == slot)
                {
                    IncomingReferenceFieldValues.Add(key, value.Clone());
                }
            }
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
            BackingBounds[slot] = null;
            ReferenceTargets[slot] = null;
            ReferenceObligations[slot] = null;
            ReferenceBackingSources[slot] = null;
            NormalBackingObjects[slot] = null;
            NormalBackingObjectsMayBeUnknown[slot] = false;
            ClearBackingFieldValues(slot);
            ClearReferenceFieldValues(slot);
            ClearIncomingReferenceFieldValues(slot);
            BackingMayBeUnknown[slot] = false;
            StorageInstances[slot] = [new StorageInstance(supportingScopeActivation, generation)];
        }

        internal void RetireStorage(int slot, long generation)
        {
            if ((uint)slot >= (uint)StorageInstances.Length || generation == StorageKey.UnknownGeneration)
            {
                throw Invalid();
            }
            ClearCopyOriginFacts(slot);
            BackingBounds[slot] = null;
            ReferenceTargets[slot] = null;
            ReferenceObligations[slot] = null;
            ReferenceBackingSources[slot] = null;
            NormalBackingObjects[slot] = null;
            NormalBackingObjectsMayBeUnknown[slot] = false;
            ClearBackingFieldValues(slot);
            ClearReferenceFieldValues(slot);
            ClearIncomingReferenceFieldValues(slot);
            BackingMayBeUnknown[slot] = false;
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

        internal void InvalidateCopyOriginValue(int slot, StorageProjection projection,
            BoundProgram program, ref int remaining, int depth)
        {
            if ((uint)slot >= (uint)CopyOriginRoots.Length
                || projection.Kind is not (StorageProjectionKind.WholeObject
                    or StorageProjectionKind.InlineFieldPath))
            {
                throw Invalid();
            }
            if (projection.Kind == StorageProjectionKind.WholeObject)
            {
                SetCopyOriginRoot(slot, CopyOriginFact.Unavailable());
                ClearCopyOriginFieldValues(slot);
                return;
            }

            PreserveCopyOriginFieldValuesFromRoot(slot, Function.Variables[slot].Type,
                this, program, ref remaining, depth + 1);
            SetCopyOriginRoot(slot, CopyOriginFact.Unavailable());
            ClearCopyOriginFieldValues(slot, projection);
        }

        internal void InvalidateAllCopyOriginValues(
            BoundProgram program, ref int remaining, int depth)
        {
            for (var slot = 0; slot < Function.Variables.Length; slot++)
            {
                Count(ref remaining, depth + 1);
                if (CarriesCopyOrigin(Function.Variables[slot].Type, program))
                {
                    InvalidateCopyOriginValue(slot, StorageProjection.WholeObject,
                        program, ref remaining, depth + 1);
                }
            }
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
            BackingBounds[slot] = sources.Count == 0 ? null : [.. sources];
            BackingMayBeUnknown[slot] = false;
        }

        internal void SetReferenceTargets(int slot, IReadOnlySet<ProvenanceAnchor> targets)
        {
            if ((uint)slot >= (uint)ReferenceTargets.Length || targets is null)
            {
                throw Invalid();
            }
            ReferenceTargets[slot] = targets.Count == 0 ? null : [.. targets];
        }

        internal void SetReferenceObligations(
            int slot, IReadOnlySet<ProvenanceAnchor> obligations)
        {
            if ((uint)slot >= (uint)ReferenceObligations.Length || obligations is null)
            {
                throw Invalid();
            }
            ReferenceObligations[slot] =
                obligations.Count == 0 ? null : [.. obligations];
        }

        internal void ClearReferenceTargets(int slot)
        {
            if ((uint)slot >= (uint)ReferenceTargets.Length)
            {
                throw Invalid();
            }
            ReferenceTargets[slot] = null;
        }

        internal void SetReferenceBackingSources(
            int slot, IReadOnlySet<ProvenanceAnchor> sources)
        {
            if ((uint)slot >= (uint)ReferenceBackingSources.Length || sources is null)
            {
                throw Invalid();
            }
            ReferenceBackingSources[slot] = sources.Count == 0 ? null : [.. sources];
        }

        internal void AddReferenceBackingSources(
            int slot, IReadOnlySet<ProvenanceAnchor> sources)
        {
            if ((uint)slot >= (uint)ReferenceBackingSources.Length || sources is null)
            {
                throw Invalid();
            }
            _ = MergeSet(ref ReferenceBackingSources[slot], sources);
        }

        internal void AddReferenceTargets(int slot, IReadOnlySet<ProvenanceAnchor> targets)
        {
            if ((uint)slot >= (uint)ReferenceTargets.Length || targets is null)
            {
                throw Invalid();
            }
            _ = MergeSet(ref ReferenceTargets[slot], targets);
        }

        internal void SetBackingAggregateProvenance(int slot,
            IReadOnlySet<ProvenanceAnchor> sources, bool mayBeUnknown)
        {
            if ((uint)slot >= (uint)Sources.Length)
            {
                throw Invalid();
            }
            HashSet<ProvenanceAnchor> aggregate = BackingBounds[slot] is { } bounds
                ? [.. bounds]
                : [];
            aggregate.UnionWith(sources);
            Sources[slot] = aggregate.Count == 0 ? null : aggregate;
            BackingMayBeUnknown[slot] = mayBeUnknown;
        }

        internal void AddBackingSources(int slot, IReadOnlySet<ProvenanceAnchor> sources)
        {
            if ((uint)slot >= (uint)Sources.Length)
            {
                throw Invalid();
            }
            _ = MergeSet(ref Sources[slot], sources);
            _ = MergeSet(ref BackingBounds[slot], sources);
        }

        internal void AddBackingBoundSource(int slot, ProvenanceAnchor source)
        {
            if ((uint)slot >= (uint)BackingBounds.Length)
            {
                throw Invalid();
            }
            _ = MergeSet(ref BackingBounds[slot], new HashSet<ProvenanceAnchor> { source });
        }

        internal void ClearBackingSources(int slot)
        {
            if ((uint)slot >= (uint)Sources.Length)
            {
                throw Invalid();
            }
            Sources[slot] = null;
            BackingBounds[slot] = null;
            BackingMayBeUnknown[slot] = false;
        }

        internal void SetBackingFieldValue(int slot, StorageProjection projection,
            BackingFieldValue value)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty || value is null)
            {
                throw Invalid();
            }

            foreach (var instance in StorageInstances[slot])
            {
                var key = new StorageKey(FunctionActivation, slot, instance, projection);
                BackingFieldValues[key] = value.Clone();
            }
        }

        internal bool TryGetBackingFieldValue(int slot, StorageProjection projection,
            out BackingFieldValue value)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty)
            {
                throw Invalid();
            }

            var sources = new HashSet<ProvenanceAnchor>();
            var found = false;
            var available = true;
            foreach (var instance in StorageInstances[slot])
            {
                var key = new StorageKey(FunctionActivation, slot, instance, projection);
                if (BackingFieldValues.TryGetValue(key, out var fieldValue))
                {
                    found = true;
                    available &= fieldValue.IsAvailable;
                    sources.UnionWith(fieldValue.Sources);
                }
                else
                {
                    available = false;
                }
            }

            value = new BackingFieldValue(sources, found && available);
            return found;
        }

        internal void ClearBackingFieldValues(int slot)
        {
            foreach (var key in BackingFieldValues.Keys
                .Where(key => key.Site == slot).ToArray())
            {
                _ = BackingFieldValues.Remove(key);
            }
        }

        internal void ClearBackingFieldValues(int slot, StorageProjection prefix)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || prefix.Kind is not (StorageProjectionKind.WholeObject
                    or StorageProjectionKind.InlineFieldPath))
            {
                throw Invalid();
            }
            if (prefix.Kind == StorageProjectionKind.WholeObject)
            {
                ClearBackingFieldValues(slot);
                return;
            }

            foreach (var key in BackingFieldValues.Keys.Where(key =>
                key.Site == slot
                && prefix.FieldPath.IsPrefixOf(key.Projection.FieldPath)).ToArray())
            {
                _ = BackingFieldValues.Remove(key);
            }
        }

        internal void SetReferenceFieldValue(int slot, StorageProjection projection,
            ReferenceFieldValue value)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty || value is null)
            {
                throw Invalid();
            }

            foreach (var instance in StorageInstances[slot])
            {
                var key = new StorageKey(FunctionActivation, slot, instance, projection);
                ReferenceFieldValues[key] = value.Clone();
            }
        }

        internal bool TryGetReferenceFieldValue(int slot, StorageProjection projection,
            out ReferenceFieldValue value)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty)
            {
                throw Invalid();
            }

            var found = false;
            ReferenceFieldValue? result = null;
            foreach (var instance in StorageInstances[slot])
            {
                var key = new StorageKey(FunctionActivation, slot, instance, projection);
                if (ReferenceFieldValues.TryGetValue(key, out var fieldValue))
                {
                    found = true;
                    if (result is null)
                    {
                        result = fieldValue.Clone();
                    }
                    else
                    {
                        _ = result.Merge(fieldValue);
                    }
                }
                else if (result is null)
                {
                    result = ReferenceFieldValue.Unavailable();
                }
                else
                {
                    _ = result.Merge(ReferenceFieldValue.Unavailable());
                }
            }

            value = result ?? ReferenceFieldValue.Unavailable();
            return found;
        }

        internal void SetNormalBackingObjects(int slot,
            IReadOnlySet<ProvenanceAnchor> objects, bool mayBeUnknown)
        {
            if ((uint)slot >= (uint)NormalBackingObjects.Length || objects is null
                || objects.Any(static value =>
                    value.Kind != ProvenanceAnchorKind.NormalBackingObject
                    || value.Storage is not StorageKey key || !key.IsKnown))
            {
                throw Invalid();
            }
            NormalBackingObjects[slot] = objects.Count == 0 ? null : [.. objects];
            NormalBackingObjectsMayBeUnknown[slot] = mayBeUnknown;
        }

        internal HashSet<ProvenanceAnchor> NormalBackingObjectsForSlot(int slot)
        {
            if ((uint)slot >= (uint)NormalBackingObjects.Length)
            {
                throw Invalid();
            }
            return NormalBackingObjects[slot] ?? EmptyBackingSources;
        }

        internal bool NormalBackingObjectsMayBeUnknownForSlot(int slot)
        {
            if ((uint)slot >= (uint)NormalBackingObjects.Length)
            {
                throw Invalid();
            }
            return NormalBackingObjectsMayBeUnknown[slot];
        }

        internal void SetNormalBackingReferenceFieldValue(
            ProvenanceAnchor objectIdentity, StorageProjection projection,
            ReferenceFieldValue value)
        {
            if (objectIdentity.Kind != ProvenanceAnchorKind.NormalBackingObject
                || objectIdentity.Storage is not StorageKey key || !key.IsKnown
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty || value is null)
            {
                throw Invalid();
            }
            var fieldKey = new NormalBackingObjectFieldKey(objectIdentity, projection);
            if (NormalBackingReferenceFieldValues.TryGetValue(fieldKey, out var current))
            {
                _ = current.Merge(value);
            }
            else
            {
                NormalBackingReferenceFieldValues.Add(fieldKey, value.Clone());
            }
        }

        internal bool TryGetNormalBackingReferenceFieldValue(
            IReadOnlySet<ProvenanceAnchor> objectIdentities,
            StorageProjection projection, out ReferenceFieldValue value)
        {
            if (objectIdentities is null
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty)
            {
                throw Invalid();
            }
            if (objectIdentities.Count != 1)
            {
                value = ReferenceFieldValue.Unavailable();
                return false;
            }
            var identity = objectIdentities.Single();
            var key = new NormalBackingObjectFieldKey(identity, projection);
            if (NormalBackingReferenceFieldValues.TryGetValue(key, out var fieldValue))
            {
                value = fieldValue.Clone();
                return true;
            }
            value = ReferenceFieldValue.Unavailable();
            return false;
        }

        internal void SetNormalBackingClassFieldValue(
            ProvenanceAnchor objectIdentity, StorageProjection projection,
            IReadOnlySet<ProvenanceAnchor> objects, bool mayBeUnknown)
        {
            if (objectIdentity.Kind != ProvenanceAnchorKind.NormalBackingObject
                || objectIdentity.Storage is not StorageKey key || !key.IsKnown
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty || objects is null
                || objects.Any(static value =>
                    value.Kind != ProvenanceAnchorKind.NormalBackingObject
                    || value.Storage is not StorageKey storage || !storage.IsKnown))
            {
                throw Invalid();
            }
            NormalBackingClassFieldValues[
                new NormalBackingObjectFieldKey(objectIdentity, projection)] =
                new NormalBackingClassFieldValue(objects, mayBeUnknown);
        }

        internal bool TryGetNormalBackingClassFieldValue(
            IReadOnlySet<ProvenanceAnchor> objectIdentities,
            StorageProjection projection, out HashSet<ProvenanceAnchor> objects,
            out bool mayBeUnknown)
        {
            if (objectIdentities is null
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty)
            {
                throw Invalid();
            }
            if (objectIdentities.Count != 1
                || !NormalBackingClassFieldValues.TryGetValue(
                    new NormalBackingObjectFieldKey(objectIdentities.Single(),
                        projection), out var value))
            {
                objects = [];
                mayBeUnknown = true;
                return false;
            }
            objects = [.. value.Objects];
            mayBeUnknown = value.MayBeUnknown;
            return true;
        }

        internal Dictionary<NormalBackingObjectFieldKey, ReferenceFieldValue>.ValueCollection
            NormalBackingReferenceFieldValuesSnapshot() =>
                NormalBackingReferenceFieldValues.Values;

        internal void ClearNormalBackingReferenceFieldValues(
            IReadOnlySet<ProvenanceAnchor> objectIdentities,
            StorageProjection prefix)
        {
            if (objectIdentities is null
                || prefix.Kind is not (StorageProjectionKind.WholeObject
                    or StorageProjectionKind.InlineFieldPath))
            {
                throw Invalid();
            }
            foreach (var key in NormalBackingReferenceFieldValues.Keys
                .Where(key => objectIdentities.Contains(key.Object)
                    && (prefix.Kind == StorageProjectionKind.WholeObject
                        || prefix.FieldPath.IsPrefixOf(key.Projection.FieldPath)))
                .ToArray())
            {
                _ = NormalBackingReferenceFieldValues.Remove(key);
            }
        }

        internal void CaptureNormalBackingClassConstructorSummary(
            int constructorIndex, ProvenanceAnchor receiverIdentity)
        {
            if ((uint)constructorIndex
                    >= (uint)NormalBackingClassConstructorSummaries.Length
                || receiverIdentity.Kind
                    != ProvenanceAnchorKind.NormalBackingObject)
            {
                throw Invalid();
            }
            var referenceFields = NormalBackingReferenceFieldValues
                .Where(pair => pair.Key.Object == receiverIdentity)
                .ToDictionary(static pair => pair.Key.Projection,
                    static pair => pair.Value.Clone());
            var classFields = NormalBackingClassFieldValues
                .Where(pair => pair.Key.Object == receiverIdentity)
                .ToDictionary(static pair => pair.Key.Projection,
                    static pair => pair.Value.Clone());
            var localObjects = new Dictionary<int, HashSet<ProvenanceAnchor>>();
            var unknownObjectSlots = new HashSet<int>();
            for (var slot = 0; slot < NormalBackingObjects.Length; slot++)
            {
                if (NormalBackingObjects[slot] is { Count: > 0 } objects)
                {
                    localObjects.Add(slot, [.. objects]);
                }
                if (NormalBackingObjectsMayBeUnknown[slot])
                {
                    _ = unknownObjectSlots.Add(slot);
                }
            }
            NormalBackingClassConstructorSummaries[constructorIndex] =
                new NormalBackingClassConstructorSummary(receiverIdentity,
                    referenceFields, classFields, localObjects,
                    unknownObjectSlots);
        }

        internal void ApplyNormalBackingClassConstructorSummary(
            int constructorIndex, ProvenanceAnchor objectIdentity,
            BoundProgram program)
        {
            if ((uint)constructorIndex
                    >= (uint)NormalBackingClassConstructorSummaries.Length
                || objectIdentity.Kind
                    != ProvenanceAnchorKind.NormalBackingObject
                || objectIdentity.Storage is not StorageKey objectKey
                || !objectKey.IsKnown)
            {
                throw Invalid();
            }
            var pendingConstructors =
                new Stack<(int Constructor, ProvenanceAnchor Object, int Depth)>();
            var visited = new HashSet<ProvenanceAnchor>();
            pendingConstructors.Push((constructorIndex, objectIdentity, 0));
            while (pendingConstructors.TryPop(out var pending))
            {
                if (pending.Depth >= MaximumDepth || !visited.Add(pending.Object)
                    || NormalBackingClassConstructorSummaries[pending.Constructor]
                        is not NormalBackingClassConstructorSummary summary)
                {
                    continue;
                }

                var remappedObjects = new Dictionary<ProvenanceAnchor, ProvenanceAnchor> {
                    [summary.ReceiverIdentity] = pending.Object,
                };
                foreach (var child in summary.LocalObjects.Values.SelectMany(
                    static objects => objects))
                {
                    if (child == summary.ReceiverIdentity
                        || child.Storage is not StorageKey childKey
                        || childKey.Activation != pending.Constructor)
                    {
                        continue;
                    }
                    _ = remappedObjects.TryAdd(child,
                        ProvenanceAnchor.ForNestedNormalBackingObject(
                            pending.Object, child));
                }

                foreach (var (projection, value) in summary.ReferenceFields)
                {
                    if (!TryRebaseConstructorReferenceFieldValue(
                        summary, remappedObjects,
                        value, program, out var rebased))
                    {
                        rebased = ReferenceFieldValue.Unavailable();
                    }
                    SetNormalBackingReferenceFieldValue(
                        pending.Object, projection, rebased);
                }
                foreach (var (projection, value) in summary.ClassFields)
                {
                    var objects = new HashSet<ProvenanceAnchor>();
                    var mayBeUnknown = value.MayBeUnknown;
                    foreach (var child in value.Objects)
                    {
                        if (remappedObjects.TryGetValue(child, out var remapped))
                        {
                            _ = objects.Add(remapped);
                        }
                        else
                        {
                            mayBeUnknown = true;
                        }
                    }
                    SetNormalBackingClassFieldValue(pending.Object, projection,
                        objects, mayBeUnknown);
                    foreach (var child in objects)
                    {
                        var childConstructor = NormalBackingConstructorIndex(
                            child.ObjectType, program);
                        if (childConstructor >= 0)
                        {
                            pendingConstructors.Push((
                                childConstructor, child, pending.Depth + 1));
                        }
                    }
                }
            }
        }

        private static bool TryRebaseConstructorReferenceFieldValue(
            NormalBackingClassConstructorSummary summary,
            IReadOnlyDictionary<ProvenanceAnchor, ProvenanceAnchor> remappedObjects,
            ReferenceFieldValue value, BoundProgram program,
            out ReferenceFieldValue rebased)
        {
            var targets = new HashSet<ProvenanceAnchor>();
            foreach (var target in value.Targets)
            {
                if (target.Kind == ProvenanceAnchorKind.NormalBackingPlace
                    && target.Storage is StorageKey key
                    && summary.ReceiverIdentity.Storage is StorageKey receiverKey
                    && key.Activation == receiverKey.Activation
                    && key.Site == target.Identity)
                {
                    if (summary.UnknownObjectSlots.Contains(target.Identity)
                        || !summary.LocalObjects.TryGetValue(
                        target.Identity, out var targetObjects)
                        || targetObjects.Count != 1
                        || !remappedObjects.TryGetValue(
                            targetObjects.Single(), out var targetObject))
                    {
                        rebased = ReferenceFieldValue.Unavailable();
                        return false;
                    }
                    if (!TryGetNormalBackingObjectPlaceType(
                        targetObject.ObjectType, key.Projection,
                        program, out _))
                    {
                        rebased = ReferenceFieldValue.Unavailable();
                        return false;
                    }
                    _ = targets.Add(ProvenanceAnchor.ForNormalBackingObjectPlace(
                        targetObject, key.Projection, key.PlaceGeneration));
                }
                else
                {
                    _ = targets.Add(target);
                }
            }
            rebased = new ReferenceFieldValue(
                targets, value.Obligations, value.BackingSources,
                value.IsAvailable);
            return true;
        }

        private static int NormalBackingConstructorIndex(
            PrimitiveType objectType, BoundProgram program)
        {
            if (!ManagedClassTypes.IsClass(objectType))
            {
                return -1;
            }
            var classIndex = ManagedClassTypes.ClassIndex(objectType);
            if ((uint)classIndex >= (uint)program.Classes.Length)
            {
                throw Invalid();
            }
            var className = program.Classes[classIndex].Name;
            for (var index = 0; index < program.Functions.Length; index++)
            {
                var function = program.Functions[index];
                if (function.IsClassConstructor
                    && function.DeclaringStruct == className)
                {
                    return index;
                }
            }
            return -1;
        }

        internal void ClearReferenceFieldValues(int slot)
        {
            foreach (var key in ReferenceFieldValues.Keys
                .Where(key => key.Site == slot).ToArray())
            {
                _ = ReferenceFieldValues.Remove(key);
            }
        }

        internal void ClearReferenceFieldValues(int slot, StorageProjection prefix)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || prefix.Kind is not (StorageProjectionKind.WholeObject
                    or StorageProjectionKind.InlineFieldPath))
            {
                throw Invalid();
            }
            if (prefix.Kind == StorageProjectionKind.WholeObject)
            {
                ClearReferenceFieldValues(slot);
                return;
            }

            foreach (var key in ReferenceFieldValues.Keys.Where(key =>
                key.Site == slot
                && prefix.FieldPath.IsPrefixOf(key.Projection.FieldPath)).ToArray())
            {
                _ = ReferenceFieldValues.Remove(key);
            }
        }

        internal void SetIncomingReferenceFieldValue(
            ProvenanceAnchor place, ReferenceFieldValue value)
        {
            if (place.Kind != ProvenanceAnchorKind.IncomingReferencePlace
                || (uint)place.Identity >= (uint)ReferenceTargets.Length
                || place.IncomingProjection is not StorageProjection projection
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty
                || place.IncomingReferenceDepth < 1
                || place.IncomingReferenceDepth > MaximumDepth
                || value is null)
            {
                throw Invalid();
            }
            IncomingReferenceFieldValues[place] = value.Clone();
        }

        internal bool TryGetIncomingReferenceFieldValue(
            ProvenanceAnchor place, out ReferenceFieldValue value)
        {
            if (place.Kind != ProvenanceAnchorKind.IncomingReferencePlace
                || (uint)place.Identity >= (uint)ReferenceTargets.Length
                || place.IncomingProjection is not StorageProjection projection
                || projection.Kind != StorageProjectionKind.InlineFieldPath
                || projection.FieldPath.Segments.IsEmpty
                || place.IncomingReferenceDepth < 1
                || place.IncomingReferenceDepth > MaximumDepth)
            {
                throw Invalid();
            }

            ReferenceFieldValue? result = null;
            var found = false;
            foreach (var (candidate, candidateValue) in IncomingReferenceFieldValues)
            {
                if (candidate.IncomingProjection != projection)
                {
                    continue;
                }
                found = true;
                if (result is null)
                {
                    result = candidateValue.Clone();
                }
                else
                {
                    _ = result.Merge(candidateValue);
                }
            }

            if (!IncomingReferenceFieldValues.ContainsKey(place))
            {
                var unknown = new ReferenceFieldValue(
                    new HashSet<ProvenanceAnchor> {
                        ProvenanceAnchor.ForIncomingReferenceField(
                            place.Identity, projection,
                            place.IncomingReferenceDepth),
                    },
                    EmptyBackingSources, ReferenceBackingSources[place.Identity]
                        ?? EmptyBackingSources, isAvailable: true);
                if (result is null)
                {
                    result = unknown;
                }
                else
                {
                    _ = result.Merge(unknown);
                }
            }

            value = result ?? new ReferenceFieldValue(
                new HashSet<ProvenanceAnchor> {
                    ProvenanceAnchor.ForIncomingReferenceField(
                        place.Identity, projection,
                        place.IncomingReferenceDepth),
                },
                EmptyBackingSources, ReferenceBackingSources[place.Identity]
                    ?? EmptyBackingSources, isAvailable: true);
            return found;
        }

        private static ReferenceFieldValue IncomingReferenceFieldValueForUnstoredPlace(
            ProvenanceAnchor place,
            IReadOnlySet<ProvenanceAnchor>? backingSources)
        {
            if (place.Kind != ProvenanceAnchorKind.IncomingReferencePlace
                || place.IncomingProjection is not StorageProjection projection
                || projection.Kind != StorageProjectionKind.InlineFieldPath)
            {
                throw Invalid();
            }
            return new ReferenceFieldValue(
                new HashSet<ProvenanceAnchor> {
                    ProvenanceAnchor.ForIncomingReferenceField(
                        place.Identity, projection,
                        place.IncomingReferenceDepth),
                },
                EmptyBackingSources, backingSources ?? EmptyBackingSources,
                isAvailable: true);
        }

        internal void ClearIncomingReferenceFieldValues(int slot)
        {
            foreach (var key in IncomingReferenceFieldValues.Keys
                .Where(key => key.Identity == slot).ToArray())
            {
                _ = IncomingReferenceFieldValues.Remove(key);
            }
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
            ClearNativePointerExtentProvenance(slot);
            ClearNativePointerFieldValues(slot);
        }

        internal void MarkAllNativePointerProvenanceUnknown()
        {
            Array.Fill(NativePointerMayBeUnknown, true);
            Array.Fill(NativePointerExtentUnavailable, true);
            foreach (var fieldValue in NativePointerFieldValues.Values)
            {
                fieldValue.MayBeUnknown = true;
                fieldValue.ExtentUnavailable = true;
            }
        }

        internal void SetNativePointerExtentProvenance(int slot,
            IReadOnlySet<ProvenanceAnchor> sources, bool mayBeOpaque, bool unavailable)
        {
            if ((uint)slot >= (uint)NativePointerExtentSources.Length)
            {
                throw Invalid();
            }
            NativePointerExtentSources[slot] = sources.Count == 0 ? null : [.. sources];
            NativePointerExtentMayBeOpaque[slot] = mayBeOpaque;
            NativePointerExtentUnavailable[slot] = unavailable;
        }

        internal bool AddNativePointerExtentProvenance(int slot,
            IReadOnlySet<ProvenanceAnchor> sources, bool mayBeOpaque, bool unavailable)
        {
            if ((uint)slot >= (uint)NativePointerExtentSources.Length)
            {
                throw Invalid();
            }

            var changed = MergeSet(ref NativePointerExtentSources[slot], sources);
            if (mayBeOpaque && !NativePointerExtentMayBeOpaque[slot])
            {
                NativePointerExtentMayBeOpaque[slot] = true;
                changed = true;
            }
            if (unavailable && !NativePointerExtentUnavailable[slot])
            {
                NativePointerExtentUnavailable[slot] = true;
                changed = true;
            }
            return changed;
        }

        internal void MarkNativePointerExtentUnavailable(int slot)
        {
            if ((uint)slot >= (uint)NativePointerExtentSources.Length)
            {
                throw Invalid();
            }
            NativePointerExtentUnavailable[slot] = true;
        }

        private void ClearNativePointerExtentProvenance(int slot)
        {
            NativePointerExtentSources[slot] = null;
            NativePointerExtentMayBeOpaque[slot] = false;
            NativePointerExtentUnavailable[slot] = false;
        }

        internal void SetNativePointerFieldValue(int slot, StorageProjection projection,
            IReadOnlySet<ProvenanceAnchor> sources, bool mayBeUnknown,
            NativePointerExtentFact extentFact)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || !IsNativePointerFieldValueProjection(slot, projection))
            {
                throw Invalid();
            }

            foreach (var instance in StorageInstances[slot])
            {
                var key = new StorageKey(FunctionActivation, slot, instance, projection);
                NativePointerFieldValues[key] = new NativePointerFieldValue(
                    sources, mayBeUnknown, extentFact.Sources,
                    extentFact.MayBeOpaque, extentFact.IsUnavailable);
            }
        }

        internal bool TryGetNativePointerFieldValue(int slot,
            StorageProjection projection, out HashSet<ProvenanceAnchor> sources,
            out bool mayBeUnknown)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || (!IsNativePointerFieldValueProjection(slot, projection)
                    && !TryGetNativePointerArrayElementRange(
                        slot, projection, out _, out _)))
            {
                throw Invalid();
            }

            sources = [];
            mayBeUnknown = false;
            var found = false;
            foreach (var instance in StorageInstances[slot])
            {
                if (TryGetNativePointerArrayElementRange(
                    slot, projection, out var start, out var count))
                {
                    for (var index = start; index < start + count; index++)
                    {
                        var elementProjection = projection with {
                            Kind = StorageProjectionKind.ArrayElement,
                            Start = index,
                            Length = 1,
                        };
                        var key = new StorageKey(FunctionActivation, slot, instance,
                            elementProjection);
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
                }
                else
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
            }

            return found;
        }

        internal bool TryGetNativePointerFieldExtentValue(int slot,
            StorageProjection projection, out NativePointerExtentFact extentFact)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || (!IsNativePointerFieldValueProjection(slot, projection)
                    && !TryGetNativePointerArrayElementRange(
                        slot, projection, out _, out _)))
            {
                throw Invalid();
            }

            var sources = new HashSet<ProvenanceAnchor>();
            var mayBeOpaque = false;
            var unavailable = false;
            var found = false;
            foreach (var instance in StorageInstances[slot])
            {
                if (TryGetNativePointerArrayElementRange(
                    slot, projection, out var start, out var count))
                {
                    for (var index = start; index < start + count; index++)
                    {
                        var elementProjection = projection with {
                            Kind = StorageProjectionKind.ArrayElement,
                            Start = index,
                            Length = 1,
                        };
                        var key = new StorageKey(FunctionActivation, slot, instance,
                            elementProjection);
                        if (NativePointerFieldValues.TryGetValue(key, out var value))
                        {
                            sources.UnionWith(value.ExtentSources);
                            mayBeOpaque |= value.ExtentMayBeOpaque;
                            unavailable |= value.ExtentUnavailable;
                            found = true;
                        }
                        else
                        {
                            unavailable = true;
                        }
                    }
                }
                else
                {
                    var key = new StorageKey(FunctionActivation, slot, instance, projection);
                    if (NativePointerFieldValues.TryGetValue(key, out var value))
                    {
                        sources.UnionWith(value.ExtentSources);
                        mayBeOpaque |= value.ExtentMayBeOpaque;
                        unavailable |= value.ExtentUnavailable;
                        found = true;
                    }
                    else
                    {
                        unavailable = true;
                    }
                }
            }

            extentFact = new NativePointerExtentFact(sources, mayBeOpaque,
                unavailable || !found);
            return found;
        }

        internal void MarkNativePointerFieldExtentUnavailable(
            int slot, StorageProjection prefix)
        {
            if ((uint)slot >= (uint)StorageInstances.Length)
            {
                throw Invalid();
            }

            foreach (var (key, value) in NativePointerFieldValues)
            {
                if (key.Site != slot
                    || !NativePointerFieldProjectionOverlaps(
                        slot, prefix, key.Projection))
                {
                    continue;
                }
                value.ExtentUnavailable = true;
            }
        }

        internal void MarkNativePointerFieldValueUnknown(
            int slot, StorageProjection prefix)
        {
            if ((uint)slot >= (uint)StorageInstances.Length)
            {
                throw Invalid();
            }

            foreach (var (key, value) in NativePointerFieldValues)
            {
                if (key.Site == slot
                    && NativePointerFieldProjectionOverlaps(
                        slot, prefix, key.Projection))
                {
                    value.MayBeUnknown = true;
                    value.ExtentUnavailable = true;
                }
            }
        }

        private bool IsNativePointerFieldValueProjection(
            int slot, StorageProjection projection) =>
            projection.Kind == StorageProjectionKind.InlineFieldPath
                ? !projection.FieldPath.Segments.IsEmpty
                : projection.Kind == StorageProjectionKind.ArrayElement
                    && projection.Length == 1
                    && TryGetNativePointerArrayElementRange(
                        slot, projection, out _, out var count)
                    && count == 1;

        private bool TryGetNativePointerArrayElementRange(
            int slot, StorageProjection projection, out ulong start,
            out ulong count)
        {
            start = 0;
            count = 0;
            if ((uint)slot >= (uint)Function.Variables.Length
                || Function.Variables[slot].NativeFixedArrayLayout
                    is not BoundNativeFixedArrayLayout layout)
            {
                return false;
            }

            switch (projection.Kind)
            {
                case StorageProjectionKind.ArrayElement:
                    if (projection.Length != 1
                        || projection.Start >= layout.ElementCount)
                    {
                        return false;
                    }
                    start = projection.Start;
                    count = 1;
                    return true;
                case StorageProjectionKind.UnknownArrayElement:
                    count = layout.ElementCount;
                    return count > 0;
                case StorageProjectionKind.ElementRange:
                    if (projection.Length == 0
                        || projection.Start > layout.ElementCount
                        || projection.Length
                            > layout.ElementCount - projection.Start)
                    {
                        return false;
                    }
                    start = projection.Start;
                    count = projection.Length;
                    return true;
                default:
                    return false;
            }
        }

        private bool NativePointerFieldProjectionOverlaps(
            int slot, StorageProjection prefix, StorageProjection projection)
        {
            if (prefix.Kind == StorageProjectionKind.WholeObject)
            {
                return true;
            }
            if (prefix.Kind == StorageProjectionKind.InlineFieldPath)
            {
                return projection.Kind == StorageProjectionKind.InlineFieldPath
                    && prefix.FieldPath.IsPrefixOf(projection.FieldPath);
            }

            if (!TryGetNativePointerArrayElementRange(
                slot, prefix, out var start, out var count)
                || projection.Kind != StorageProjectionKind.ArrayElement
                || projection.Start < start
                || projection.Start >= start + count)
            {
                return prefix.Kind == StorageProjectionKind.UnknownElement;
            }

            return prefix.FieldPath.IsPrefixOf(projection.FieldPath);
        }

        private void ClearNativePointerFieldValues(int slot)
        {
            foreach (var key in NativePointerFieldValues.Keys
                .Where(key => key.Site == slot).ToArray())
            {
                _ = NativePointerFieldValues.Remove(key);
            }
        }

        internal void ClearNativePointerFieldValues(int slot, StorageProjection prefix)
        {
            if ((uint)slot >= (uint)StorageInstances.Length
                || prefix.Kind is not (StorageProjectionKind.WholeObject
                    or StorageProjectionKind.InlineFieldPath))
            {
                throw Invalid();
            }
            if (prefix.Kind == StorageProjectionKind.WholeObject)
            {
                ClearNativePointerFieldValues(slot);
                return;
            }

            foreach (var key in NativePointerFieldValues.Keys.Where(key =>
                key.Site == slot
                && prefix.FieldPath.IsPrefixOf(key.Projection.FieldPath)).ToArray())
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
                    || !value.Sources.SetEquals(otherValue.Sources)
                    || value.ExtentMayBeOpaque != otherValue.ExtentMayBeOpaque
                    || value.ExtentUnavailable != otherValue.ExtentUnavailable
                    || !value.ExtentSources.SetEquals(otherValue.ExtentSources))
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

        private bool BackingFieldValuesEqualForSlot(int slot, BackingDependencies other)
        {
            foreach (var (key, value) in BackingFieldValues)
            {
                if (key.Site != slot)
                {
                    continue;
                }
                if (!other.BackingFieldValues.TryGetValue(key, out var otherValue)
                    || !value.IsEquivalentTo(otherValue))
                {
                    return false;
                }
            }
            foreach (var key in other.BackingFieldValues.Keys)
            {
                if (key.Site == slot && !BackingFieldValues.ContainsKey(key))
                {
                    return false;
                }
            }
            return true;
        }

        private bool ReferenceFieldValuesEqualForSlot(int slot, BackingDependencies other)
        {
            foreach (var (key, value) in ReferenceFieldValues)
            {
                if (key.Site != slot)
                {
                    continue;
                }
                if (!other.ReferenceFieldValues.TryGetValue(key, out var otherValue)
                    || !value.IsEquivalentTo(otherValue))
                {
                    return false;
                }
            }
            foreach (var key in other.ReferenceFieldValues.Keys)
            {
                if (key.Site == slot && !ReferenceFieldValues.ContainsKey(key))
                {
                    return false;
                }
            }
            return true;
        }

        private bool IncomingReferenceFieldValuesEqualForSlot(
            int slot, BackingDependencies other)
        {
            foreach (var (key, value) in IncomingReferenceFieldValues)
            {
                if (key.Identity != slot)
                {
                    continue;
                }
                if (!other.IncomingReferenceFieldValues.TryGetValue(
                        key, out var otherValue)
                    || !value.IsEquivalentTo(otherValue))
                {
                    return false;
                }
            }
            foreach (var key in other.IncomingReferenceFieldValues.Keys)
            {
                if (key.Identity == slot
                    && !IncomingReferenceFieldValues.ContainsKey(key))
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
            var sourceBackingBounds = CloneSet(BackingBounds[source]);
            var sourceBackingMayBeUnknown = BackingMayBeUnknown[source];
            var sourcePointers = CloneSet(NativePointerSources[source]);
            var sourcePointerMayBeUnknown = NativePointerMayBeUnknown[source];
            var sourcePointerExtentSources = CloneSet(NativePointerExtentSources[source]);
            var sourcePointerExtentMayBeOpaque =
                NativePointerExtentMayBeOpaque[source];
            var sourcePointerExtentUnavailable =
                NativePointerExtentUnavailable[source];
            var sourceFieldValues = NativePointerFieldValues
                .Where(pair => pair.Key.Site == source)
                .Select(static pair => (pair.Key.Projection, Value: pair.Value.Clone()))
                .ToArray();
            var sourceBackingFieldValues = BackingFieldValues
                .Where(pair => pair.Key.Site == source)
                .Select(static pair => (pair.Key.Projection, Value: pair.Value.Clone()))
                .ToArray();
            var sourceReferenceFieldValues = ReferenceFieldValues
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
                RemapSource(BackingBounds[slot], source, destination);
            }
            foreach (var value in BackingFieldValues.Values)
            {
                RemapSource(value.Sources, source, destination);
            }
            foreach (var value in ReferenceFieldValues.Values)
            {
                RemapSource(value.BackingSources, source, destination);
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
            if (sourceBackingBounds is not null)
            {
                RemapSource(sourceBackingBounds, source, destination);
            }
            foreach (var (_, value) in sourceBackingFieldValues)
            {
                RemapSource(value.Sources, source, destination);
            }
            foreach (var (_, value) in sourceReferenceFieldValues)
            {
                RemapSource(value.BackingSources, source, destination);
            }
            Sources[destination] = sourceBacking;
            Sources[source] = null;
            BackingBounds[destination] = sourceBackingBounds;
            BackingBounds[source] = null;
            BackingMayBeUnknown[destination] = sourceBackingMayBeUnknown;
            BackingMayBeUnknown[source] = false;
            NativePointerSources[destination] = sourcePointers;
            NativePointerSources[source] = null;
            NativePointerMayBeUnknown[destination] = sourcePointerMayBeUnknown;
            NativePointerMayBeUnknown[source] = false;
            NativePointerExtentSources[destination] = sourcePointerExtentSources;
            NativePointerExtentSources[source] = null;
            NativePointerExtentMayBeOpaque[destination] =
                sourcePointerExtentMayBeOpaque;
            NativePointerExtentMayBeOpaque[source] = false;
            NativePointerExtentUnavailable[destination] =
                sourcePointerExtentUnavailable;
            NativePointerExtentUnavailable[source] = false;
            CopyOriginRoots[destination] = sourceCopyOrigins;
            CopyOriginRoots[source] = CopyOriginFact.Unavailable();
            ClearBackingFieldValues(destination);
            ClearBackingFieldValues(source);
            foreach (var (projection, value) in sourceBackingFieldValues)
            {
                foreach (var instance in StorageInstances[destination])
                {
                    BackingFieldValues.Add(new StorageKey(
                        FunctionActivation, destination, instance, projection), value.Clone());
                }
            }
            ClearReferenceFieldValues(destination);
            ClearReferenceFieldValues(source);
            foreach (var (projection, value) in sourceReferenceFieldValues)
            {
                foreach (var instance in StorageInstances[destination])
                {
                    ReferenceFieldValues.Add(new StorageKey(
                        FunctionActivation, destination, instance, projection), value.Clone());
                }
            }
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
            case BoundFieldMove move when CarriesNativePointer(move.Type, program):
                NativePointerAliasVariables(move.Place, function, program,
                    variables, ref remaining, depth + 1);
                break;
            case BoundFieldRead field when CarriesNativePointer(field.Type, program):
                NativePointerAliasVariables(field.Receiver, function, program,
                    variables, ref remaining, depth + 1);
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
            case BoundFieldMove move when CarriesNativePointer(move.Type, program):
                return NativePointerParameterSources(move.Place, function, program, parameterSources);
            case BoundFieldRead field when CarriesNativePointer(field.Type, program):
                return NativePointerParameterSources(field.Receiver, function, program, parameterSources);
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
                    facts.MarkNativePointerExtentUnavailable(target.Identity);
                    if (target.Storage is StorageKey targetStorage)
                    {
                        facts.MarkNativePointerFieldExtentUnavailable(
                            target.Identity, targetStorage.Projection);
                    }
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
            if (pointer.IsWritable
                && (pointer.PointeeType == PrimitiveType.Void
                    || CarriesNativePointer(pointer.PointeeType, program)))
            {
                return true;
            }
        }
        return signature.NativeParameters.Any(parameter =>
            (parameter.Access is NativeParameterAccess.Write or NativeParameterAccess.ReadWrite)
            && (parameter.PointeeType == PrimitiveType.Void
                || CarriesNativePointer(parameter.PointeeType, program)));
    }

    private static void ApplyNativePointerWrite(BoundExpression address,
        BoundExpression value, BackingDependencies facts, BoundProgram program,
        ref int remaining, int depth)
    {
        var targets = NativePointerBackingOf(address, facts, program, ref remaining, depth + 1);
        var valueSources = NativePointerBackingOf(value, facts, program, ref remaining, depth + 1);
        var valueMayBeUnknown = NativePointerMayBeUnknownOf(
            value, facts, program, ref remaining, depth + 1);
        var valueExtent = CarriesNativePointer(value.Type, program)
            ? NativePointerExtentFactsOfExpression(value, facts, facts.Function,
                program, ref remaining, depth + 1)
            : new NativePointerExtentFact([], MayBeOpaque: false, IsUnavailable: false);
        var addressMayBeUnknown = NativePointerMayBeUnknownOf(
            address, facts, program, ref remaining, depth + 1);
        if (valueSources.Count != 0 && (targets.Count == 0
            || addressMayBeUnknown))
        {
            throw InvalidEffect(value.Span, "NativePointer");
        }
        if (targets.Count == 0 || addressMayBeUnknown)
        {
            facts.InvalidateAllCopyOriginValues(program, ref remaining, depth + 1);
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
                if (CarriesNativePointer(value.Type, program))
                {
                    if (targets.Count == 1 && !addressMayBeUnknown
                        && storage.Projection.Kind == StorageProjectionKind.WholeObject
                        && facts.Function.Variables[target.Identity].Type == value.Type)
                    {
                        facts.SetNativePointerExtentProvenance(target.Identity,
                            valueExtent.Sources, valueExtent.MayBeOpaque,
                            valueExtent.IsUnavailable);
                        if (IsInlineStructStorage(value.Type, program))
                        {
                            SetNativePointerFieldValuesFromExpression(
                                target.Identity, StorageProjection.WholeObject,
                                value.Type, value, facts, facts.Function, program,
                                ref remaining, depth + 1);
                        }
                    }
                    else if (targets.Count == 1 && !addressMayBeUnknown
                        && storage.Projection.Kind == StorageProjectionKind.InlineFieldPath
                        && IsNativePointerValue(value.Type, program))
                    {
                        facts.SetNativePointerFieldValue(target.Identity,
                            storage.Projection, valueSources, valueMayBeUnknown,
                            valueExtent);
                    }
                    else if (targets.Count == 1 && !addressMayBeUnknown
                        && storage.Projection.Kind == StorageProjectionKind.InlineFieldPath
                        && IsInlineStructStorage(value.Type, program))
                    {
                        SetNativePointerFieldValuesFromExpression(
                            target.Identity, storage.Projection, value.Type,
                            value, facts, facts.Function, program,
                            ref remaining, depth + 1);
                    }
                    else
                    {
                        facts.MarkNativePointerExtentUnavailable(target.Identity);
                        facts.MarkNativePointerFieldExtentUnavailable(
                            target.Identity, storage.Projection);
                    }
                }
                if (CarriesCopyOrigin(facts.Function.Variables[target.Identity].Type, program))
                {
                    var projection = storage.Projection.Kind is
                        StorageProjectionKind.WholeObject or StorageProjectionKind.InlineFieldPath
                        ? storage.Projection : StorageProjection.WholeObject;
                    facts.InvalidateCopyOriginValue(target.Identity, projection,
                        program, ref remaining, depth + 1);
                }
            }
        }
    }

    private static void InvalidateNativePointerExtentForWrite(BoundExpression argument,
        DirectNativeParameter parameter, BackingDependencies facts, BoundProgram program)
    {
        if (parameter.Access == NativeParameterAccess.Read
            || !CarriesNativePointer(parameter.PointeeType, program))
        {
            return;
        }

        argument = UnwrapUnsafeExpression(argument);
        if (argument is BoundNativeStorageAddress address
            && (uint)address.Variable < (uint)facts.NativePointerExtentSources.Length)
        {
            facts.MarkNativePointerExtentUnavailable(address.Variable);
            var projection = StorageProjection.ForStorageAddress(address);
            facts.MarkNativePointerFieldValueUnknown(address.Variable, projection);
            facts.MarkNativePointerFieldExtentUnavailable(address.Variable, projection);
            return;
        }

        facts.MarkAllNativePointerProvenanceUnknown();
    }

    private static void InvalidateNativePointerExtentsForCall(BoundCall call,
        BoundFunction target, InitializationState initialized, BoundProgram program)
    {
        if (initialized.Dependencies is not BackingDependencies facts)
        {
            return;
        }

        for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var parameterIndex = call.ParameterIndex(argumentIndex);
            var parameter = DirectNativeImportContract.ParameterForParameter(
                target, parameterIndex)
                ?? NativeVTableCallContract.ParameterForParameter(target, parameterIndex);
            if (parameter is not null)
            {
                InvalidateNativePointerExtentForWrite(
                    call.Arguments[argumentIndex], parameter, facts, program);
            }
        }
    }

    private static void InvalidateNativePointerExtentsForCall(BoundIndirectCall call,
        NativeFunctionPointerSignatureDefinition signature,
        InitializationState initialized, BoundProgram program)
    {
        if (initialized.Dependencies is not BackingDependencies facts)
        {
            return;
        }

        foreach (var parameter in signature.NativeParameters)
        {
            if ((uint)parameter.ParameterIndex >= (uint)call.Arguments.Length)
            {
                throw Invalid(call.Span);
            }
            InvalidateNativePointerExtentForWrite(
                call.Arguments[parameter.ParameterIndex], parameter, facts, program);
        }
    }

    private static BackingDependencies? CreateBackingDependencies(int functionActivation,
        BoundFunction function,
        BoundProgram program, bool hasSpans,
        HashSet<NativePointerWriteEffect>[] nativePointerCaptureEffects,
        BoundCopyOriginSummary?[] copyOriginSummaries,
        NormalBackingClassConstructorSummary?[] normalBackingClassConstructorSummaries,
        bool deriveCopyOriginSummaries, bool hasCopyOriginValues)
    {
        if (!hasSpans && program.NativePointerTypes.IsEmpty
            && program.NativeFunctionPointerSignatures.IsEmpty
            && program.CheckedReferenceTypes.IsEmpty
            && !CarriesNativePointer(function.ReturnType, program)
            && !function.Variables.Any(variable => CarriesNativePointer(variable.Type, program))
            && !CarriesCopyOrigin(function.ReturnType, program)
            && !function.Variables.Any(variable => CarriesCopyOrigin(variable.Type, program))
            && !hasCopyOriginValues)
        {
            return null;
        }
        var facts = new BackingDependencies(functionActivation, function,
            function.Variables.Length, copyOriginSummaries,
            normalBackingClassConstructorSummaries,
            deriveCopyOriginSummaries
                ? CopyOriginSummaryBuilder.Create(function, program) : null);
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
                && !(slot == 0 && (function.IsConstructor || function.IsClassConstructor))
                && !function.Variables[slot].IsOut && !function.Variables[slot].IsInit
                && IsInlineStructStorage(function.Variables[slot].Type, program))
            {
                foreach (var (projection, _) in ReferenceFieldLeaves(
                    function.Variables[slot].Type, program,
                    ref remaining, depth: 0))
                {
                    var target = ProvenanceAnchor.ForIncomingReferenceField(
                        slot, projection);
                    facts.SetReferenceFieldValue(slot, projection,
                        new ReferenceFieldValue(
                            new HashSet<ProvenanceAnchor> { target },
                            EmptyBackingSources, EmptyBackingSources,
                            isAvailable: true));
                }
            }
            if (slot < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                && !(slot == 0 && (function.IsConstructor || function.IsClassConstructor))
                && !function.Variables[slot].IsOut && !function.Variables[slot].IsInit
                && CheckedReferenceTypeContract.IsValidType(
                    function.Variables[slot].Type, program.CheckedReferenceTypes))
            {
                var reference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(function.Variables[slot].Type)];
                facts.SetReferenceTargets(slot, new HashSet<ProvenanceAnchor>
                {
                    ProvenanceAnchor.ForIncomingReference(slot),
                });
                if (TokenSpanContract.IsDependent(reference.ReferentType, program.Structures))
                {
                    facts.SetReferenceBackingSources(slot, new HashSet<ProvenanceAnchor>
                    {
                        ProvenanceAnchor.ForBackingSource(~slot),
                    });
                }
            }
            if (slot < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                && !(slot == 0 && (function.IsConstructor || function.IsClassConstructor)))
            {
                if (TokenSpanContract.IsDependent(function.Variables[slot].Type, program.Structures)
                    || IsTokenSnapshotType(function.Variables[slot].Type, program))
                {
                    // Negative roots denote incoming backing, not the callee's descriptor place.
                    var incomingBacking = ProvenanceAnchor.ForBackingSource(~slot);
                    AddBackingSource(facts, slot, incomingBacking);
                    facts.AddBackingBoundSource(slot, incomingBacking);
                    foreach (var leaf in BackingFieldLeaves(function.Variables[slot].Type,
                        program, ref remaining, 0))
                    {
                        if (leaf.Projection.Kind != StorageProjectionKind.InlineFieldPath)
                        {
                            continue;
                        }
                        facts.SetBackingFieldValue(slot, leaf.Projection,
                            new BackingFieldValue(
                                new HashSet<ProvenanceAnchor> { incomingBacking },
                                isAvailable: true));
                    }
                }
                if (CarriesNativePointer(function.Variables[slot].Type, program))
                {
                    facts.NativePointerMayBeUnknown[slot] = true;
                    facts.NativePointerExtentMayBeOpaque[slot] = true;
                    if (IsInlineStructStorage(function.Variables[slot].Type, program))
                    {
                        foreach (var leaf in NativePointerFieldLeaves(
                            function.Variables[slot].Type, program, ref remaining, 0))
                        {
                            if (leaf.Projection.Kind == StorageProjectionKind.InlineFieldPath)
                            {
                                facts.SetNativePointerFieldValue(slot,
                                    leaf.Projection, EmptyBackingSources,
                                    true,
                                    new NativePointerExtentFact([],
                                        MayBeOpaque: true, IsUnavailable: false));
                            }
                        }
                    }
                    if (slot < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                        && !(slot == 0 && (function.IsConstructor || function.IsClassConstructor))
                        && !function.Variables[slot].IsOut && !function.Variables[slot].IsInit
                        && IsInlineStructStorage(function.Variables[slot].Type, program))
                    {
                        foreach (var (projection, _) in ReferenceFieldLeaves(
                            function.Variables[slot].Type, program,
                            ref remaining, depth: 0))
                        {
                            var target = ProvenanceAnchor.ForIncomingReferenceField(
                                slot, projection);
                            facts.SetReferenceFieldValue(slot, projection,
                                new ReferenceFieldValue(
                                    new HashSet<ProvenanceAnchor> { target },
                                    new HashSet<ProvenanceAnchor>(),
                                    new HashSet<ProvenanceAnchor>(), isAvailable: true));
                        }
                    }
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
                case BoundFieldMove move when IsTokenSnapshotType(move.Type, program):
                    Source(destination, move.Place, depth + 1);
                    break;
                case BoundReferenceFormation formation:
                    if (!CheckedReferenceTypeContract.IsValidType(
                            formation.Type, program.CheckedReferenceTypes))
                    {
                        throw Invalid(formation.Span);
                    }
                    var formedReference = program.CheckedReferenceTypes[
                        CheckedReferenceTypes.Index(formation.Type)];
                    if (formedReference.IsGarbageCollected || formedReference.IsInitialization
                        || formedReference.IsOptional || formedReference.ReferentType != formation.Place.Type
                        || !TokenSpanContract.IsDependent(formedReference.ReferentType, program.Structures))
                    {
                        throw Invalid(formation.Span);
                    }
                    Source(destination, formation.Place, depth + 1);
                    break;
                case BoundReferenceRead referenceRead:
                    if (!CheckedReferenceTypeContract.IsValidType(
                            referenceRead.Descriptor.Type, program.CheckedReferenceTypes))
                    {
                        throw Invalid(referenceRead.Span);
                    }
                    var readReference = program.CheckedReferenceTypes[
                        CheckedReferenceTypes.Index(referenceRead.Descriptor.Type)];
                    if (readReference.IsGarbageCollected || readReference.IsInitialization
                        || readReference.IsOptional || readReference.ReferentType != referenceRead.Type
                        || !TokenSpanContract.IsDependent(referenceRead.Type, program.Structures))
                    {
                        throw Invalid(referenceRead.Span);
                    }
                    foreach (var target in ReferenceTargetsSummary(
                        referenceRead.Descriptor, facts, function, program,
                        ref remaining, depth + 1))
                    {
                        if (target.Kind == ProvenanceAnchorKind.ReferencePlace
                            && (uint)target.Identity < (uint)function.Variables.Length
                            && function.Variables[target.Identity].Type == referenceRead.Type)
                        {
                            if (IsTokenSnapshotType(referenceRead.Type, program))
                            {
                                AddSnapshotSources(destination, target.Identity,
                                    referenceRead.Span, depth + 1);
                            }
                            else
                            {
                                AddEdge(target.Identity, destination);
                            }
                        }
                        else if (target.Kind is ProvenanceAnchorKind.IncomingReference
                                or ProvenanceAnchorKind.IncomingReferenceContents
                            && (uint)target.Identity < (uint)function.ParameterCount
                            + (function.IsConstructor ? 1u : 0u))
                        {
                            if (facts.ReferenceBackingSources[target.Identity] is
                            { Count: > 0 } capturedSources)
                            {
                            foreach (var source in capturedSources)
                            {
                                AddBackingSource(facts, destination, source);
                            }
                            }
                            else
                            {
                            AddBackingSource(facts, destination,
                                ProvenanceAnchor.ForBackingSource(~target.Identity));
                            }
                        }
                        else
                        {
                            throw InvalidEffect(referenceRead.Span, "Ownership");
                        }
                    }
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
                case BoundFieldMove move:
                    Source(destination, move.Place, depth + 1);
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
                        address.Projections.IsDefaultOrEmpty
                            ? StorageProjection.ForArrayElement(address.Index)
                            : StorageProjection.WholeObject);
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
                case BoundFieldMove move when CarriesNativePointer(move.Type, program):
                    NativePointerSource(destination, move.Place, depth + 1);
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
                    if (!callee.IsExternal
                        && callee.Intrinsic == BoundIntrinsic.None
                        && callee.Body is not null)
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
                    if (CheckedReferenceTypeContract.IsValidType(
                        initializer.Type, program.CheckedReferenceTypes))
                    {
                        facts.SetReferenceTargets(local.Variable,
                            ReferenceTargetsSummary(initializer, facts, function, program,
                                ref remaining, depth + 1));
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
                    if (CheckedReferenceTypeContract.IsValidType(
                        value.Type, program.CheckedReferenceTypes))
                    {
                        facts.AddReferenceTargets(assignment.Variable,
                            ReferenceTargetsSummary(value, facts, function, program,
                                ref remaining, depth + 1));
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
                case BoundOptionalAssignment {
                    Place: BoundRead place,
                    Value: BoundExpression value,
                }:
                    if ((uint)place.Variable >= (uint)facts.ReferenceTargets.Length)
                    {
                        throw Invalid(place.Span);
                    }
                    if (CheckedReferenceTypeContract.IsValidType(
                        place.Type, program.CheckedReferenceTypes)
                        && program.CheckedReferenceTypes[
                            CheckedReferenceTypes.Index(place.Type)].IsOptional)
                    {
                        facts.AddReferenceTargets(place.Variable,
                            ReferenceTargetsSummary(value, facts, function, program,
                                ref remaining, depth + 1));
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
            var hasPublishedCopyOriginSummary = result is BoundCall call
                && (uint)call.Function < (uint)facts.CopyOriginSummaries.Length
                && facts.CopyOriginSummaries[call.Function] is not null;
            if (!hasNativePointerFieldFlow && !hasPublishedCopyOriginSummary
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
        return index >= 0 && index < program.Structures.Length
            && !ManagedClassTypes.IsClass(type);
    }

    private static bool IsNativePointerValue(PrimitiveType type, BoundProgram program)
        => type == PrimitiveType.NativeRawPointer
            || NativePointerTypeContract.IsValidType(type, program.NativePointerTypes);

    private static bool IsOrdinaryStoredReferenceType(
        PrimitiveType type, BoundProgram program)
        => CheckedReferenceTypeContract.IsValidType(type, program.CheckedReferenceTypes)
            && CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                type, program.Structures, program.Classes,
                program.EnumDefinitions.Length, program.CheckedReferenceTypes);

    private static List<(StorageProjection Projection, PrimitiveType Type)> NativePointerFieldLeaves(
        PrimitiveType type, BoundProgram program, ref int remaining, int depth)
    {
        var result = new List<(StorageProjection Projection, PrimitiveType Type)>();
        CollectNativePointerFieldLeaves(type, StorageProjection.WholeObject,
            program, result, [], ref remaining, depth);
        return result;
    }

    private static List<(StorageProjection Projection, PrimitiveType Type)> BackingFieldLeaves(
        PrimitiveType type, BoundProgram program, ref int remaining, int depth)
    {
        var result = new List<(StorageProjection Projection, PrimitiveType Type)>();
        CollectBackingFieldLeaves(type, StorageProjection.WholeObject,
            program, result, [], ref remaining, depth);
        return result;
    }

    private static void CollectBackingFieldLeaves(PrimitiveType type,
        StorageProjection projection, BoundProgram program,
        List<(StorageProjection Projection, PrimitiveType Type)> result,
        HashSet<PrimitiveType> activeTypes, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (type == PrimitiveType.TokenSpan)
        {
            result.Add((projection, type));
            return;
        }
        if (!IsInlineStructStorage(type, program)
            || !TokenSpanContract.IsDependent(type, program.Structures))
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
            if (!TokenSpanContract.IsDependent(fieldType, program.Structures))
            {
                continue;
            }
            CollectBackingFieldLeaves(fieldType,
                projection.AppendField(type, field), program, result,
                activeTypes, ref remaining, depth + 1);
        }

        _ = activeTypes.Remove(type);
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
                || current == PrimitiveType.TokenSpan
                || IsOrdinaryStoredReferenceType(current, program))
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

    private static bool IsCopyOriginValueType(PrimitiveType type, BoundProgram program)
        => IsNativePointerValue(type, program)
            || NativeFunctionPointerTypes.IsType(type)
            || type == PrimitiveType.TokenSpan
            || IsOrdinaryStoredReferenceType(type, program)
            || IsCopyStruct(program, type);

    private static bool IsCopyOriginSummarySourceFunction(BoundFunction function)
        => !function.IsExternal && !function.IsModuleReference
            && function.Intrinsic == BoundIntrinsic.None && function.IndexedBody is null;

    private static int[] CopyOriginSummaryVerificationOrder(
        BoundProgram program, bool[] recursiveFunctions)
    {
        if (recursiveFunctions.Length != program.Functions.Length)
        {
            throw Invalid();
        }

        var sourceFunctions = new bool[program.Functions.Length];
        var dependencies = new List<int>?[program.Functions.Length];
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            sourceFunctions[index] = IsCopyOriginSummarySourceFunction(function);
            if (!sourceFunctions[index])
            {
                continue;
            }

            var callees = new HashSet<int>();
            if (!BoundTreeTraversal.VisitExpressions(function.Body, expression =>
                {
                    if (expression is BoundCall call
                        && (uint)call.Function < (uint)program.Functions.Length
                        && IsCopyOriginSummarySourceFunction(program.Functions[call.Function]))
                    {
                        _ = callees.Add(call.Function);
                    }
                }))
            {
                throw Invalid(function.Span);
            }
            dependencies[index] = [.. callees.Order()];
        }

        var states = new byte[program.Functions.Length];
        var active = new List<int>();
        var order = new List<int>(program.Functions.Length);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            Visit(index);
        }
        return [.. order];

        void Visit(int index)
        {
            if (states[index] == 2)
            {
                return;
            }
            if (!sourceFunctions[index])
            {
                states[index] = 2;
                order.Add(index);
                return;
            }

            states[index] = 1;
            active.Add(index);
            if (dependencies[index] is { } callees)
            {
                foreach (var callee in callees)
                {
                    if (states[callee] == 0)
                    {
                        Visit(callee);
                    }
                    else if (states[callee] == 1)
                    {
                        var cycleStart = active.IndexOf(callee);
                        if (cycleStart < 0)
                        {
                            throw Invalid(program.Functions[index].Span);
                        }
                        for (var cycleIndex = cycleStart; cycleIndex < active.Count; cycleIndex++)
                        {
                            recursiveFunctions[active[cycleIndex]] = true;
                        }
                    }
                }
            }
            active.RemoveAt(active.Count - 1);
            states[index] = 2;
            order.Add(index);
        }
    }

    private static List<(StorageProjection Projection, PrimitiveType Type)> ReferenceFieldLeaves(
        PrimitiveType type, BoundProgram program, ref int remaining, int depth)
    {
        var result = new List<(StorageProjection Projection, PrimitiveType Type)>();
        CollectReferenceFieldLeaves(type, StorageProjection.WholeObject,
            program, result, [], ref remaining, depth);
        return result;
    }

    private static void CollectReferenceFieldLeaves(PrimitiveType type,
        StorageProjection projection, BoundProgram program,
        List<(StorageProjection Projection, PrimitiveType Type)> result,
        HashSet<PrimitiveType> activeTypes, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (IsOrdinaryStoredReferenceType(type, program))
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
            throw InvalidEffect(default, "CheckedReference");
        }

        var structure = program.Structures[(int)type - 100];
        for (var field = 0; field < structure.Fields.Length; field++)
        {
            CollectReferenceFieldLeaves(structure.Fields[field].Type,
                projection.AppendField(type, field), program, result,
                activeTypes, ref remaining, depth + 1);
        }
        _ = activeTypes.Remove(type);
    }

    private static List<(StorageProjection Projection, PrimitiveType Type)>
        ReferencePlaceFieldLeaves(
            PrimitiveType type, BoundProgram program, ref int remaining, int depth)
    {
        var result = new List<(StorageProjection Projection, PrimitiveType Type)>();
        CollectReferencePlaceFieldLeaves(type, StorageProjection.WholeObject,
            program, result, [], ref remaining, depth);
        return result;
    }

    private static void CollectReferencePlaceFieldLeaves(
        PrimitiveType type, StorageProjection projection, BoundProgram program,
        List<(StorageProjection Projection, PrimitiveType Type)> result,
        HashSet<PrimitiveType> activeTypes, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (!IsInlineStructStorage(type, program))
        {
            return;
        }

        var structureIndex = (int)type - 100;
        if ((uint)structureIndex >= (uint)program.Structures.Length)
        {
            throw InvalidEffect(default, "CheckedReference");
        }
        var structure = program.Structures[structureIndex];
        if (structure.CleanupMethod is not null && !structure.IsUnique)
        {
            return;
        }
        if (!activeTypes.Add(type))
        {
            throw InvalidEffect(default, "CheckedReference");
        }

        for (var field = 0; field < structure.Fields.Length; field++)
        {
            Count(ref remaining, depth + 1);
            var fieldType = structure.Fields[field].Type;
            var fieldProjection = projection.AppendField(type, field);
            if (CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                fieldType, program.Structures, program.Classes,
                program.EnumDefinitions.Length, program.CheckedReferenceTypes))
            {
                result.Add((fieldProjection, fieldType));
            }
            if (IsInlineStructStorage(fieldType, program))
            {
                CollectReferencePlaceFieldLeaves(fieldType, fieldProjection,
                    program, result, activeTypes, ref remaining, depth + 1);
            }
        }

        _ = activeTypes.Remove(type);
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
            || type == PrimitiveType.TokenSpan
            || IsOrdinaryStoredReferenceType(type, program))
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

            case BoundFieldMove move:
                return TryGetNativePointerFieldPlace(
                    move.Place, function, program, out variable, out projection);

            case BoundReceiverLoan loan:
                ValidateCopyOriginVariable(loan.Variable, loan.Type, function);
                variable = loan.Variable;
                projection = StorageProjection.WholeObject;
                return IsInlineStructStorage(loan.Type, program);

            case BoundMutableReceiverLoan loan:
                ValidateCopyOriginVariable(loan.Variable, loan.Type, function);
                variable = loan.Variable;
                projection = StorageProjection.WholeObject;
                return IsInlineStructStorage(loan.Type, program);

            case BoundFieldLoan loan when loan.Place is not null
                && loan.Place.Type == loan.Type:
                return TryGetNativePointerFieldPlace(loan.Place, function,
                    program, out variable, out projection);

            case BoundMutableFieldLoan loan when loan.Place is not null
                && loan.Place.Type == loan.Type:
                return TryGetNativePointerFieldPlace(loan.Place, function,
                    program, out variable, out projection);

            case BoundNativeFixedArrayElement element:
                if ((uint)element.Variable >= (uint)function.Variables.Length
                    || function.Variables[element.Variable].Type != element.Layout.CarrierType
                    || function.Variables[element.Variable].NativeFixedArrayLayout != element.Layout
                    || !NativeFixedArrayContract.MatchesLayout(element.Layout, program)
                    || element.Type != element.Layout.ElementType)
                {
                    throw Invalid(element.Span);
                }
                variable = element.Variable;
                projection = StorageProjection.ForArrayElement(element.Index);
                return IsInlineStructStorage(element.Type, program);

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
                return IsNativePointerFieldProjection(projection);

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
            if (!IsNativePointerFieldProjection(leafProjection)
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
            case BoundCall call when facts.TryGetNativePointerCallResult(call,
                relativeProjection, out var callSources, out var callMayBeUnknown):
                return (callSources, callMayBeUnknown);
            case BoundFieldMove move when facts.TryGetNativePointerCallResult(move,
                relativeProjection, out var movedSources, out var movedMayBeUnknown):
                return (movedSources, movedMayBeUnknown);
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
            if (IsNativePointerFieldProjection(sourceProjection)
                && facts.TryGetNativePointerFieldValue(variable, sourceProjection,
                    out var sources, out var mayBeUnknown))
            {
                return (sources, mayBeUnknown);
            }
        }

        return (new HashSet<ProvenanceAnchor>(NativePointerBackingOf(
                expression, facts, program, ref remaining, depth + 1)), true);
    }

    private static NativePointerExtentFact NativePointerExtentValueFactsOfExpression(
        BoundExpression expression, PrimitiveType valueType,
        StorageProjection relativeProjection, BackingDependencies facts,
        BoundFunction function, BoundProgram program,
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
                return NativePointerExtentValueFactsOfExpression(
                    unsafeExpression.Expression, valueType, relativeProjection,
                    facts, function, program, ref remaining, depth + 1);
            case BoundNativePointerRead pointerRead
                when CarriesNativePointer(pointerRead.Type, program):
                return NativePointerExtentFactsOfPointerRead(pointerRead,
                    relativeProjection, facts, function, program,
                    ref remaining, depth + 1);
            case BoundCall call when facts.TryGetNativePointerCallExtentResult(
                call, relativeProjection, out var callExtent):
                return callExtent;
            case BoundConditional conditional when CarriesNativePointer(valueType, program):
                var whenTrue = NativePointerExtentValueFactsOfExpression(
                    conditional.WhenTrue, valueType, relativeProjection,
                    facts, function, program, ref remaining, depth + 1);
                var whenFalse = NativePointerExtentValueFactsOfExpression(
                    conditional.WhenFalse, valueType, relativeProjection,
                    facts, function, program, ref remaining, depth + 1);
                whenTrue.Sources.UnionWith(whenFalse.Sources);
                return new NativePointerExtentFact(whenTrue.Sources,
                    whenTrue.MayBeOpaque || whenFalse.MayBeOpaque,
                    whenTrue.IsUnavailable || whenFalse.IsUnavailable);
        }

        if (TryGetNativePointerFieldValuePath(expression, program,
            out var root, out var prefix))
        {
            var projection = StorageProjection.Combine(prefix, relativeProjection);
            if (!IsNativePointerFieldProjection(projection))
            {
                return new NativePointerExtentFact([], MayBeOpaque: false,
                    IsUnavailable: true);
            }
            return NativePointerExtentValueFactsOfExpression(root, root.Type,
                projection, facts, function, program, ref remaining, depth + 1);
        }

        if (TryGetNativePointerFieldPlace(expression, function, program,
            out var variable, out var sourcePrefix)
            && (sourcePrefix.Kind is StorageProjectionKind.InlineFieldPath
                    or StorageProjectionKind.ArrayElement
                    or StorageProjectionKind.UnknownArrayElement
                || relativeProjection.Kind == StorageProjectionKind.InlineFieldPath))
        {
            var sourceProjection = StorageProjection.Combine(
                sourcePrefix, relativeProjection);
            if (IsNativePointerFieldProjection(sourceProjection)
                && facts.TryGetNativePointerFieldExtentValue(
                    variable, sourceProjection, out var fieldExtent))
            {
                return fieldExtent;
            }
            return new NativePointerExtentFact([], MayBeOpaque: false,
                IsUnavailable: true);
        }

        if (IsNativePointerValue(valueType, program))
        {
            if (relativeProjection.Kind != StorageProjectionKind.WholeObject)
            {
                return new NativePointerExtentFact([], MayBeOpaque: false,
                    IsUnavailable: true);
            }
            return NativePointerExtentFactsOfExpression(expression, facts,
                function, program, ref remaining, depth + 1);
        }

        if (expression is BoundRead read
            && relativeProjection.Kind == StorageProjectionKind.InlineFieldPath)
        {
            ValidateCopyOriginVariable(read.Variable, read.Type, function);
            return facts.TryGetNativePointerFieldExtentValue(
                read.Variable, relativeProjection, out var readExtent)
                    ? readExtent
                    : new NativePointerExtentFact([], MayBeOpaque: false,
                        IsUnavailable: true);
        }
        if (expression is BoundMove move
            && relativeProjection.Kind == StorageProjectionKind.InlineFieldPath)
        {
            ValidateCopyOriginVariable(move.Variable, move.Type, function);
            return facts.TryGetNativePointerFieldExtentValue(
                move.Variable, relativeProjection, out var moveExtent)
                    ? moveExtent
                    : new NativePointerExtentFact([], MayBeOpaque: false,
                        IsUnavailable: true);
        }

        return new NativePointerExtentFact([], MayBeOpaque: false,
            IsUnavailable: true);
    }

    private static bool TryGetNativePointerFieldValuePath(BoundExpression expression,
        BoundProgram program, out BoundExpression root, out StorageProjection projection)
    {
        if (expression is not BoundFieldRead field)
        {
            root = expression;
            projection = default;
            return false;
        }
        if (field.Receiver is null)
        {
            root = expression;
            projection = default;
            return false;
        }

        if (field.Receiver is BoundFieldRead parent)
        {
            if (!TryGetNativePointerFieldValuePath(parent, program,
                out root, out projection))
            {
                projection = default;
                return false;
            }
        }
        else
        {
            root = field.Receiver;
            projection = StorageProjection.WholeObject;
        }

        if (!IsInlineStructStorage(field.Receiver.Type, program))
        {
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
        return IsNativePointerFieldProjection(projection);
    }

    private static Dictionary<StorageProjection, NativePointerFieldValue>
        NativePointerFieldValueSnapshotOfExpression(BoundExpression expression,
            PrimitiveType valueType, BackingDependencies facts, BoundFunction function,
            BoundProgram program, ref int remaining, int depth)
    {
        var result = new Dictionary<StorageProjection, NativePointerFieldValue>();
        foreach (var leaf in NativePointerFieldLeaves(valueType, program,
            ref remaining, depth + 1))
        {
            Count(ref remaining, depth + 1);
            var (sources, mayBeUnknown) = NativePointerValueFactsOfExpression(
                expression, valueType, leaf.Projection, facts, function, program,
                ref remaining, depth + 1);
            var extentFact = NativePointerExtentValueFactsOfExpression(
                expression, valueType, leaf.Projection, facts, function, program,
                ref remaining, depth + 1);
            if (!result.TryAdd(leaf.Projection,
                new NativePointerFieldValue(sources, mayBeUnknown,
                    extentFact.Sources, extentFact.MayBeOpaque,
                    extentFact.IsUnavailable)))
            {
                throw Invalid(expression.Span);
            }
        }
        return result;
    }

    private static Dictionary<StorageProjection, BackingFieldValue>?
        BackingFieldValueSnapshotOfExpression(BoundExpression expression,
            PrimitiveType valueType, BackingDependencies facts, BoundFunction function,
            BoundProgram program, ref int remaining, int depth)
    {
        var leaves = BackingFieldLeaves(valueType, program, ref remaining, depth + 1);
        if (leaves.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<StorageProjection, BackingFieldValue>();
        foreach (var leaf in leaves)
        {
            Count(ref remaining, depth + 1);
            var value = BackingFieldValueOfExpression(expression, valueType,
                leaf.Projection, facts, function, program, ref remaining, depth + 1);
            if (!result.TryAdd(leaf.Projection, value))
            {
                throw Invalid(expression.Span);
            }
        }
        return result;
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
            case BoundReferenceFormation when expression.Type == valueType:
                return CopyOriginFact.KnownEmpty();
            case BoundReferenceReborrow reborrow
                when reborrow.Type == valueType
                    && reborrow.Descriptor.Type == valueType:
                return CopyOriginOfExpression(reborrow.Descriptor, valueType,
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
            case BoundFieldMove move when facts.TryGetCopyOriginCallResult(move, out var movedResult):
                return movedResult.Get(BoundCopyOriginPath(relativeProjection.FieldPath));
            case BoundMove move:
                ValidateCopyOriginVariable(move.Variable, move.Type, function);
                if (facts.TryGetCopyOriginCallResult(move, out var moveResult))
                {
                    return moveResult.Get(BoundCopyOriginPath(relativeProjection.FieldPath));
                }
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
            case BoundReceiverLoan loan:
                ValidateCopyOriginVariable(loan.Variable, loan.Type, function);
                return CopyOriginFactAtProjection(loan.Variable, relativeProjection, facts);
            case BoundMutableReceiverLoan loan:
                ValidateCopyOriginVariable(loan.Variable, loan.Type, function);
                return CopyOriginFactAtProjection(loan.Variable, relativeProjection, facts);
            case BoundFieldLoan loan:
                return CopyOriginOfExpression(loan.Place, loan.Type, relativeProjection,
                    facts, function, program, ref remaining, depth + 1);
            case BoundMutableFieldLoan loan:
                return CopyOriginOfExpression(loan.Place, loan.Type, relativeProjection,
                    facts, function, program, ref remaining, depth + 1);
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
            case BoundFieldRead field when field.Receiver is not null
                && IsInlineStructStorage(field.Receiver.Type, program):
                var receiverFields = program.Structures[(int)field.Receiver.Type - 100].Fields;
                if ((uint)field.Field >= (uint)receiverFields.Length
                    || receiverFields[field.Field].Type != field.Type)
                {
                    throw Invalid(field.Span);
                }
                var receiverProjection = StorageProjection.Combine(
                    StorageProjection.WholeObject.AppendField(field.Receiver.Type, field.Field),
                    relativeProjection);
                return CopyOriginOfExpression(field.Receiver, field.Receiver.Type,
                    receiverProjection, facts, function, program, ref remaining, depth + 1);
            case BoundCall call:
                return facts.TryGetCopyOriginCallResult(call, out var callResult)
                    ? callResult.Get(BoundCopyOriginPath(relativeProjection.FieldPath))
                    : CopyOriginFact.Unavailable();
            case BoundConstant or BoundNull or BoundNativePointerAddress
                or BoundNativeFixedArrayPointerAddress or BoundNativeStorageAddress:
                return CopyOriginFact.KnownEmpty();
            default:
                return CopyOriginFact.Unavailable();
        }
    }

    private static CopyOriginFact CopyOriginFactAtProjection(int slot,
        StorageProjection projection, BackingDependencies facts)
        => projection.Kind switch
        {
            StorageProjectionKind.WholeObject => facts.CopyOriginRoot(slot),
            StorageProjectionKind.InlineFieldPath when
                facts.TryGetCopyOriginFieldValue(slot, projection, out var fieldFact) => fieldFact,
            StorageProjectionKind.InlineFieldPath => facts.CopyOriginRoot(slot)
                .Project(projection.FieldPath),
            _ => CopyOriginFact.Unavailable(),
        };

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

    private static Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>>
        CopyOriginFactsForExpression(BoundExpression expression, PrimitiveType type,
            BackingDependencies facts, BoundFunction function, BoundProgram program,
            ref int remaining, int depth)
    {
        var result = new Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>>();
        var leaves = CopyOriginFieldLeaves(type, program, ref remaining, depth + 1);
        foreach (var leaf in leaves)
        {
            Count(ref remaining, depth + 1);
            var valueFact = CopyOriginOfExpression(expression, type, leaf.Projection,
                facts, function, program, ref remaining, depth + 1);
            if (!valueFact.IsAvailable || valueFact.Origins.Count == 0)
            {
                continue;
            }

            var destinationPath = BoundCopyOriginPath(leaf.Projection.FieldPath);
            var origins = new HashSet<BoundCopyOrigin>(valueFact.Origins.Count);
            foreach (var origin in valueFact.Origins)
            {
                _ = origins.Add(new BoundCopyOrigin(
                    (BoundCopyOriginSourceKind)origin.SourceKind,
                    (BoundCopyOriginSourceView)origin.SourceView,
                    origin.SourceOrdinal, BoundCopyOriginPath(origin.SourcePath)));
            }
            if (!result.TryAdd(destinationPath, origins))
            {
                throw Invalid(expression.Span);
            }
        }
        return result;
    }

    private static CopyOriginValue CopyOriginValueOfExpression(
        BoundExpression expression, PrimitiveType type, BackingDependencies facts,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        var result = new CopyOriginValue();
        foreach (var leaf in CopyOriginFieldLeaves(type, program, ref remaining, depth + 1))
        {
            Count(ref remaining, depth + 1);
            result.Set(BoundCopyOriginPath(leaf.Projection.FieldPath),
                CopyOriginOfExpression(expression, type, leaf.Projection, facts,
                    function, program, ref remaining, depth + 1));
        }
        return result;
    }

    private static CopyOriginValue UnavailableCopyOriginValue(
        PrimitiveType type, BoundProgram program, ref int remaining, int depth)
    {
        var result = new CopyOriginValue();
        foreach (var leaf in CopyOriginFieldLeaves(type, program, ref remaining, depth + 1))
        {
            Count(ref remaining, depth + 1);
            result.Set(BoundCopyOriginPath(leaf.Projection.FieldPath),
                CopyOriginFact.Unavailable());
        }
        return result;
    }

    private static void ApplyCopyOriginCallEffects(
        BoundCall call, BoundFunction callee, InitializationState initialized,
        BoundFunction caller, BoundProgram program, ref int remaining, int depth)
    {
        if (initialized.Dependencies is not BackingDependencies facts)
        {
            return;
        }

        for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            Count(ref remaining, depth + 1);
            var argument = call.Arguments[argumentIndex];
            var parameterIndex = call.ParameterIndex(argumentIndex);
            var parameterSlot = parameterIndex + (callee.IsConstructor ? 1 : 0);
            if ((uint)parameterSlot >= (uint)callee.Variables.Length)
            {
                throw Invalid(call.Span);
            }

            if (argument is BoundNativeOutputReservation reservation)
            {
                if (CarriesCopyOrigin(reservation.PointeeType, program))
                {
                    facts.InvalidateCopyOriginValue(reservation.Variable,
                        StorageProjection.WholeObject, program, ref remaining, depth + 1);
                }
                continue;
            }
            if (argument is BoundOutArgument output)
            {
                if (CarriesCopyOrigin(output.Type, program))
                {
                    facts.InvalidateCopyOriginValue(output.Variable,
                        StorageProjection.WholeObject, program, ref remaining, depth + 1);
                }
                continue;
            }
            if (NativePointerParameterMayWrite(callee, parameterIndex, program))
            {
                InvalidateCopyOriginThroughAddress(argument, facts, program,
                    ref remaining, depth + 1);
            }
            else if (argument.Type == PrimitiveType.NativeRawPointer
                && DirectNativeImportContract.ParameterForParameter(callee, parameterIndex)?.Access
                    != NativeParameterAccess.Read
                && NativeVTableCallContract.ParameterForParameter(callee, parameterIndex)?.Access
                    != NativeParameterAccess.Read)
            {
                InvalidateCopyOriginThroughAddress(argument, facts, program,
                    ref remaining, depth + 1);
            }
            if (argument is BoundMutableReceiverLoan mutableReceiver)
            {
                if (CarriesCopyOrigin(mutableReceiver.Type, program))
                {
                    facts.InvalidateCopyOriginValue(mutableReceiver.Variable,
                        StorageProjection.WholeObject, program, ref remaining, depth + 1);
                }
                continue;
            }
            if (argument is BoundMutableFieldLoan mutableField)
            {
                if (CarriesCopyOrigin(mutableField.Type, program))
                {
                    if (TryGetCopyOriginPlace(mutableField.Place, caller, program,
                        out var variable, out var projection))
                    {
                        facts.InvalidateCopyOriginValue(variable, projection,
                            program, ref remaining, depth + 1);
                    }
                    else
                    {
                        facts.InvalidateAllCopyOriginValues(
                            program, ref remaining, depth + 1);
                    }
                }
                continue;
            }

            var formal = callee.Variables[parameterSlot];
            if (!formal.IsImplicitRef || !formal.IsMutable
                || call.ImplicitReferenceTemporary(argumentIndex) >= 0
                || !CarriesCopyOrigin(formal.Type, program))
            {
                continue;
            }
            if (TryGetCopyOriginPlace(argument, caller, program,
                out var sourceVariable, out var sourceProjection))
            {
                facts.InvalidateCopyOriginValue(sourceVariable, sourceProjection,
                    program, ref remaining, depth + 1);
            }
            else
            {
                facts.InvalidateAllCopyOriginValues(program, ref remaining, depth + 1);
            }
        }
    }

    private static bool NativePointerParameterMayWrite(
        BoundFunction callee, int parameterIndex, BoundProgram program)
    {
        var nativeParameter = DirectNativeImportContract.ParameterForParameter(
            callee, parameterIndex)
            ?? NativeVTableCallContract.ParameterForParameter(callee, parameterIndex);
        if (nativeParameter?.Access == NativeParameterAccess.Write)
        {
            return true;
        }

        var parameterSlot = parameterIndex + (callee.IsConstructor ? 1 : 0);
        if ((uint)parameterSlot >= (uint)callee.Variables.Length)
        {
            return false;
        }
        var type = callee.Variables[parameterSlot].Type;
        return NativePointerTypeContract.IsValidType(type, program.NativePointerTypes)
            && program.NativePointerTypes[NativePointerTypes.Index(type)].IsWritable;
    }

    private static void InvalidateCopyOriginThroughAddress(
        BoundExpression address, BackingDependencies facts, BoundProgram program,
        ref int remaining, int depth)
    {
        var targets = NativePointerBackingOf(address, facts, program,
            ref remaining, depth + 1);
        var mayBeUnknown = NativePointerMayBeUnknownOf(address, facts, program,
            ref remaining, depth + 1);
        if (targets.Count == 0 || mayBeUnknown)
        {
            facts.InvalidateAllCopyOriginValues(program, ref remaining, depth + 1);
            return;
        }

        foreach (var target in targets)
        {
            Count(ref remaining, depth + 1);
            if (target.Kind != ProvenanceAnchorKind.NativeStorage
                || (uint)target.Identity >= (uint)facts.Function.Variables.Length
                || target.Storage is not StorageKey storage)
            {
                continue;
            }
            var projection = storage.Projection.Kind is
                StorageProjectionKind.WholeObject or StorageProjectionKind.InlineFieldPath
                ? storage.Projection : StorageProjection.WholeObject;
            facts.InvalidateCopyOriginValue(target.Identity, projection, program,
                ref remaining, depth + 1);
        }
    }

    private static bool TryGetCopyOriginPlace(BoundExpression expression,
        BoundFunction function, BoundProgram program, out int variable,
        out StorageProjection projection)
    {
        if (expression is BoundRead read)
        {
            ValidateCopyOriginVariable(read.Variable, read.Type, function);
            variable = read.Variable;
            projection = StorageProjection.WholeObject;
            return CarriesCopyOrigin(read.Type, program);
        }
        if (expression is BoundFieldRead field
            && TryGetNativePointerFieldPlace(field, function, program,
                out variable, out projection))
        {
            return CarriesCopyOrigin(field.Type, program);
        }

        variable = -1;
        projection = default;
        return false;
    }

    private static CopyOriginValue CreateCopyOriginCallResult(
        BoundCall call, BoundFunction target, BoundCopyOriginSummary? summary,
        IReadOnlyDictionary<int, StagedCopyOriginInput> stagedInputs,
        BackingDependencies facts, BoundFunction caller, BoundProgram program,
        ref int remaining, int depth)
    {
        var result = UnavailableCopyOriginValue(target.ReturnType, program,
            ref remaining, depth + 1);
        if (summary is null)
        {
            return result;
        }
        ValidateCopyOriginSummary(target, summary, program);

        var inputValues = new Dictionary<int, CopyOriginValue>();
        foreach (var (parameterIndex, input) in stagedInputs)
        {
            var value = input.Snapshot ?? CopyOriginValueOfExpression(
                input.Argument, input.Argument.Type, facts, caller, program,
                ref remaining, depth + 1);
            inputValues.Add(parameterIndex, value);
        }

        foreach (var fact in summary.Facts)
        {
            if (!TryResolveCopyOriginFieldPath(target.ReturnType,
                    fact.DestinationPath, program, out var destinationType))
            {
                throw Invalid(call.Span);
            }
            foreach (var leaf in CopyOriginFieldLeaves(destinationType, program,
                ref remaining, depth + 1))
            {
                var destinationPath = AppendCopyOriginPath(
                    fact.DestinationPath, BoundCopyOriginPath(leaf.Projection.FieldPath));
                var valueFact = CopyOriginFact.KnownEmpty();
                foreach (var origin in fact.Origins)
                {
                    var parameterIndex = CopyOriginCallParameterIndex(target, origin);
                    if (!stagedInputs.TryGetValue(parameterIndex, out var stagedInput)
                        || stagedInput.SourceView != origin.SourceView
                        || !inputValues.TryGetValue(parameterIndex, out var sourceValue)
                        || !TryGetCopyOriginSourceType(target, origin, program,
                            out var sourceRootType)
                        || !TryResolveCopyOriginFieldPath(sourceRootType,
                            origin.SourcePath, program, out var sourceType)
                        || sourceType != destinationType)
                    {
                        throw Invalid(call.Span);
                    }

                    var sourcePath = AppendCopyOriginPath(
                        origin.SourcePath, BoundCopyOriginPath(leaf.Projection.FieldPath));
                    _ = valueFact.Merge(sourceValue.Get(sourcePath));
                }
                result.Set(destinationPath, valueFact);
            }
        }
        return result;
    }

    private static ReferenceFieldValue CreateReferenceCallResult(
        BoundCall call, BoundFunction target, BackingDependencies facts,
        InitializationState state, uint[]? fieldMasks, BoundFunction caller,
        BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (!CheckedReferenceTypeContract.IsValidType(
                call.Type, program.CheckedReferenceTypes))
        {
            return ReferenceFieldValue.Unavailable();
        }

        var resultType = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(call.Type)];
        if (resultType.IsGarbageCollected || resultType.IsInitialization)
        {
            return ReferenceFieldValue.Unavailable();
        }

        var dependency = ResultDependencyContract(target);
        if (dependency is null || dependency.Sources.IsEmpty)
        {
            return resultType.IsOptional
                ? new ReferenceFieldValue(EmptyBackingSources,
                    EmptyBackingSources, EmptyBackingSources, isAvailable: true)
                : ReferenceFieldValue.Unavailable();
        }

        var targets = new HashSet<ProvenanceAnchor>();
        var obligations = new HashSet<ProvenanceAnchor>();
        var backingSources = new HashSet<ProvenanceAnchor>();
        var available = true;

        void Include(ReferenceFieldValue value)
        {
            available &= value.IsAvailable
                && (value.Targets.Count != 0 || resultType.IsOptional);
            targets.UnionWith(value.Targets);
            obligations.UnionWith(value.Obligations);
            backingSources.UnionWith(value.BackingSources);
        }

        void IncludeProjectedPlace(
            ProvenanceAnchor sourceTarget, StorageProjection relativeProjection,
            ref int remaining)
        {
            Count(ref remaining, depth + 1);
            if (relativeProjection.Kind != StorageProjectionKind.InlineFieldPath)
            {
                available = false;
                return;
            }

            if (sourceTarget.Kind == ProvenanceAnchorKind.ReferencePlace
                && sourceTarget.Storage is StorageKey storage)
            {
                var projection = StorageProjection.Combine(
                    storage.Projection, relativeProjection);
                var instances = facts.StorageInstanceSet(sourceTarget.Identity);
                if (!storage.IsKnown || storage.Activation != facts.FunctionActivation
                    || (uint)sourceTarget.Identity >= (uint)caller.Variables.Length
                    || instances.Count != 1 || !instances.Contains(storage.Instance)
                    || projection.Kind != StorageProjectionKind.InlineFieldPath
                    || !TryGetReferencePlaceType(sourceTarget.Identity,
                        projection, caller, program, out var actualType)
                    || actualType != resultType.ReferentType
                    || !ReferenceTargetPlaceInitialized(sourceTarget.Identity,
                        projection, state, fieldMasks, caller, program))
                {
                    available = false;
                    return;
                }

                _ = targets.Add(ProvenanceAnchor.ForReferencePlace(
                    facts.FunctionActivation, sourceTarget.Identity,
                    storage.Instance, projection,
                    state.FieldPlaceGeneration(sourceTarget.Identity, projection)));
                return;
            }

            if (sourceTarget.Kind == ProvenanceAnchorKind.IncomingReference)
            {
                _ = targets.Add(ProvenanceAnchor.ForIncomingReferencePlace(
                    sourceTarget.Identity, relativeProjection, referenceDepth: 1));
                return;
            }

            if (sourceTarget.Kind == ProvenanceAnchorKind.IncomingReferenceContents)
            {
                _ = targets.Add(ProvenanceAnchor.ForIncomingReferencePlace(
                    sourceTarget.Identity, relativeProjection,
                    sourceTarget.IncomingReferenceDepth + 1));
                return;
            }

            if (sourceTarget.Kind == ProvenanceAnchorKind.IncomingReferencePlace
                && sourceTarget.IncomingProjection is StorageProjection incomingProjection)
            {
                var projection = StorageProjection.Combine(
                    incomingProjection, relativeProjection);
                if (projection.Kind != StorageProjectionKind.InlineFieldPath)
                {
                    available = false;
                    return;
                }
                _ = targets.Add(ProvenanceAnchor.ForIncomingReferencePlace(
                    sourceTarget.Identity, projection,
                    sourceTarget.IncomingReferenceDepth));
                return;
            }

            available = false;
        }

        foreach (var source in dependency.Sources)
        {
            Count(ref remaining, depth + 1);
            var parameterIndex = DependencyCallParameterIndex(target, source);
            var argument = DependencyCallArgument(call, target, source);
            var staged = facts.Staged.LastOrDefault(candidate =>
                ReferenceEquals(candidate.Call, call)
                && candidate.ParameterIndex == parameterIndex);
            if (staged.Call is null)
            {
                available = false;
                continue;
            }

            var sourceIsForwardedBacking =
                IsForwardedBackingResultSource(target, source);
            var sourceHasReference = false;
            if (CheckedReferenceTypeContract.IsValidType(
                    argument.Type, program.CheckedReferenceTypes))
            {
                var sourceType = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(argument.Type)];
                if (sourceType.IsGarbageCollected || sourceType.IsInitialization)
                {
                    available = false;
                    continue;
                }

                if (sourceType.ReferentType == resultType.ReferentType)
                {
                    sourceHasReference = true;
                    if (!sourceIsForwardedBacking)
                    {
                        Include(new ReferenceFieldValue(staged.ReferenceTargets,
                            staged.ReferenceObligations, staged.BackingSources,
                            isAvailable: staged.ReferenceTargets.Count != 0
                                || (sourceType.IsOptional && resultType.IsOptional)));
                    }
                }

                if (sourceType.ReferentType == call.Type
                    && IsOrdinaryStoredReferenceType(call.Type, program))
                {
                    sourceHasReference = true;
                    var referenceRead = new BoundReferenceRead(
                        argument, sourceType.ReferentType, call.Span);
                    Include(ReferenceFieldValueOfReferenceRead(
                        referenceRead, sourceType.ReferentType,
                        StorageProjection.WholeObject, facts, state, fieldMasks,
                        caller, program, ref remaining, depth + 1,
                        staged.ReferenceTargets));
                }

                if (IsInlineStructStorage(sourceType.ReferentType, program))
                {
                    var placeLeaves = ReferencePlaceFieldLeaves(
                        sourceType.ReferentType, program, ref remaining,
                        depth + 1);
                    if (!sourceIsForwardedBacking)
                    {
                        foreach (var (projection, placeType) in placeLeaves)
                        {
                            if (placeType != resultType.ReferentType)
                            {
                                continue;
                            }

                            sourceHasReference = true;
                            foreach (var sourceTarget in staged.ReferenceTargets)
                            {
                                IncludeProjectedPlace(sourceTarget, projection,
                                    ref remaining);
                            }
                        }
                    }

                    var leaves = ReferenceFieldLeaves(sourceType.ReferentType,
                        program, ref remaining, depth + 1);
                    var referenceRead = new BoundReferenceRead(
                        argument, sourceType.ReferentType, call.Span);
                    foreach (var (projection, leafType) in leaves)
                    {
                        if (leafType != call.Type)
                        {
                            available = false;
                            continue;
                        }

                        sourceHasReference = true;
                        Include(ReferenceFieldValueOfReferenceRead(
                            referenceRead, sourceType.ReferentType, projection,
                            facts, state, fieldMasks, caller, program,
                            ref remaining, depth + 1, staged.ReferenceTargets));
                    }

                    if (!sourceHasReference && leaves.Count == 0)
                    {
                        available = false;
                    }
                }
                else if (sourceType.ReferentType != resultType.ReferentType
                    && sourceType.ReferentType != call.Type)
                {
                    available = false;
                }
            }
            else if (IsInlineStructStorage(argument.Type, program))
            {
                var isBorrowedReceiver = argument is BoundReceiverLoan
                    or BoundMutableReceiverLoan or BoundFieldLoan
                    or BoundMutableFieldLoan;
                var isMutableReceiver = argument is BoundMutableReceiverLoan
                    or BoundMutableFieldLoan;
                var referenceFieldValues = staged.ReferenceFieldValues;
                ProvenanceAnchor receiverTarget = default;
                var hasReceiverTarget = false;
                if (isBorrowedReceiver)
                {
                    if (TryGetReferencePlaceTarget(argument, facts, state,
                            fieldMasks, caller, program, out receiverTarget))
                    {
                        hasReceiverTarget = true;
                        if (!sourceIsForwardedBacking
                            && argument.Type == resultType.ReferentType)
                        {
                            sourceHasReference = true;
                            _ = targets.Add(receiverTarget);
                        }
                    }
                    else
                    {
                        available = false;
                    }

                    referenceFieldValues = ReferenceFieldValuesOfExpression(
                        argument, argument.Type, facts, state, caller, program,
                        ref remaining, depth + 1, fieldMasks);

                    if (!sourceIsForwardedBacking)
                    {
                        var placeLeaves = ReferencePlaceFieldLeaves(
                            argument.Type, program, ref remaining, depth + 1);
                        foreach (var (projection, placeType) in placeLeaves)
                        {
                            if (placeType != resultType.ReferentType)
                            {
                                continue;
                            }
                            if (isMutableReceiver || !hasReceiverTarget)
                            {
                                available = false;
                                continue;
                            }
                            sourceHasReference = true;
                            IncludeProjectedPlace(receiverTarget, projection,
                                ref remaining);
                        }
                    }
                    else if (ReferenceFieldLeaves(argument.Type, program,
                        ref remaining, depth + 1).Count != 0)
                    {
                        available = false;
                    }
                }

                var leaves = ReferenceFieldLeaves(argument.Type, program,
                    ref remaining, depth + 1);
                foreach (var (projection, leafType) in leaves)
                {
                    if (leafType != call.Type)
                    {
                        available = false;
                        continue;
                    }

                    sourceHasReference = true;
                    if (isMutableReceiver || sourceIsForwardedBacking)
                    {
                        available = false;
                    }
                    else if (referenceFieldValues.TryGetValue(
                        projection, out var fieldValue))
                    {
                        Include(fieldValue);
                    }
                    else
                    {
                        available = false;
                    }
                }
                if (!sourceHasReference && leaves.Count != 0)
                {
                    available = false;
                }
            }

            if (!sourceHasReference
                && argument is BoundReceiverLoan or BoundMutableReceiverLoan
                    or BoundFieldLoan or BoundMutableFieldLoan)
            {
                available = false;
            }
        }

        return new ReferenceFieldValue(targets, obligations, backingSources,
            isAvailable: available && (targets.Count != 0 || resultType.IsOptional));
    }

    private static Dictionary<StorageProjection, ReferenceFieldValue>?
        CreateReferenceFieldCallResult(
            BoundCall call, BoundFunction target, BoundCopyOriginSummary? summary,
            IReadOnlyDictionary<int, StagedCopyOriginInput> stagedInputs,
            BackingDependencies facts, InitializationState state, uint[]? fieldMasks,
            BoundFunction caller, BoundProgram program,
            ref int remaining, int depth)
    {
        var resultLeaves = ReferenceFieldLeaves(call.Type, program,
            ref remaining, depth + 1);
        if (resultLeaves.Count == 0 || summary is null)
        {
            return null;
        }
        ValidateCopyOriginSummary(target, summary, program);

        var result = new Dictionary<StorageProjection, ReferenceFieldValue>();
        foreach (var leaf in resultLeaves)
        {
            if (!result.TryAdd(leaf.Projection, ReferenceFieldValue.Unavailable()))
            {
                throw Invalid(call.Span);
            }
        }

        foreach (var fact in summary.Facts)
        {
            if (!TryResolveCopyOriginFieldPath(target.ReturnType,
                    fact.DestinationPath, program, out var destinationType))
            {
                throw Invalid(call.Span);
            }
            var destinationPrefix = CopyOriginStorageProjection(fact.DestinationPath);
            foreach (var leaf in ReferenceFieldLeaves(destinationType, program,
                ref remaining, depth + 1))
            {
                Count(ref remaining, depth + 1);
                var destinationProjection = StorageProjection.Combine(
                    destinationPrefix, leaf.Projection);
                ReferenceFieldValue? value = null;
                foreach (var origin in fact.Origins)
                {
                    var parameterIndex = CopyOriginCallParameterIndex(target, origin);
                    if (!stagedInputs.TryGetValue(parameterIndex, out var stagedInput)
                        || stagedInput.SourceView != origin.SourceView
                        || !TryGetCopyOriginSourceType(target, origin, program,
                            out var sourceRootType)
                        || !TryResolveCopyOriginFieldPath(sourceRootType,
                            origin.SourcePath, program, out var sourceType)
                        || sourceType != destinationType)
                    {
                        throw Invalid(call.Span);
                    }

                    var sourceProjection = StorageProjection.Combine(
                        CopyOriginStorageProjection(origin.SourcePath),
                        leaf.Projection);
                    ReferenceFieldValue sourceValue;
                    if (stagedInput.ReferenceSnapshot is { } snapshot)
                    {
                        sourceValue = snapshot.TryGetValue(sourceProjection,
                            out var stagedValue)
                            ? stagedValue.Clone()
                            : ReferenceFieldValue.Unavailable();
                    }
                    else
                    {
                        var sourceValues = ReferenceFieldValuesOfExpression(
                            stagedInput.Argument, sourceRootType, facts, state,
                            caller, program, ref remaining, depth + 1, fieldMasks);
                        sourceValue = sourceValues.TryGetValue(sourceProjection,
                            out var currentValue)
                            ? currentValue
                            : ReferenceFieldValue.Unavailable();
                    }

                    if (value is null)
                    {
                        value = sourceValue;
                    }
                    else
                    {
                        _ = value.Merge(sourceValue);
                    }
                }

                if (value is null || !result.ContainsKey(destinationProjection))
                {
                    throw Invalid(call.Span);
                }
                result[destinationProjection] = value;
            }
        }
        return result;
    }

    private static Dictionary<StorageProjection, BackingFieldValue>?
        CreateBackingCallResult(
            BoundCall call, BoundFunction target, BoundCopyOriginSummary? summary,
            IReadOnlyDictionary<int, StagedCopyOriginInput> stagedInputs,
            BackingDependencies facts, BoundFunction caller, BoundProgram program,
            ref int remaining, int depth)
    {
        var resultLeaves = BackingFieldLeaves(call.Type, program,
            ref remaining, depth + 1);
        if (resultLeaves.Count == 0 || summary is null)
        {
            return null;
        }
        ValidateCopyOriginSummary(target, summary, program);

        var callBounds = BackingBoundsOfCallResult(
            call, target, facts, program, ref remaining, depth + 1);

        var result = new Dictionary<StorageProjection, BackingFieldValue>();
        foreach (var leaf in resultLeaves)
        {
            if (!result.TryAdd(leaf.Projection, BackingFieldValue.Unavailable()))
            {
                throw Invalid(call.Span);
            }
        }

        foreach (var fact in summary.Facts)
        {
            if (!TryResolveCopyOriginFieldPath(target.ReturnType,
                    fact.DestinationPath, program, out var destinationType))
            {
                throw Invalid(call.Span);
            }

            var destinationPrefix = CopyOriginStorageProjection(fact.DestinationPath);
            foreach (var leaf in BackingFieldLeaves(destinationType, program,
                ref remaining, depth + 1))
            {
                Count(ref remaining, depth + 1);
                var destinationProjection = StorageProjection.Combine(
                    destinationPrefix, leaf.Projection);
                var sources = new HashSet<ProvenanceAnchor>(callBounds);
                var available = true;
                var hasOrigin = false;
                foreach (var origin in fact.Origins)
                {
                    hasOrigin = true;
                    var parameterIndex = CopyOriginCallParameterIndex(target, origin);
                    if (!stagedInputs.TryGetValue(parameterIndex, out var stagedInput)
                        || stagedInput.SourceView != origin.SourceView
                        || !TryGetCopyOriginSourceType(target, origin, program,
                            out var sourceRootType)
                        || !TryResolveCopyOriginFieldPath(sourceRootType,
                            origin.SourcePath, program, out var sourceType)
                        || sourceType != destinationType)
                    {
                        throw Invalid(call.Span);
                    }

                    var sourceProjection = StorageProjection.Combine(
                        CopyOriginStorageProjection(origin.SourcePath),
                        leaf.Projection);
                    BackingFieldValue sourceValue;
                    if (origin.SourceView == BoundCopyOriginSourceView.EntryValue)
                    {
                        if (stagedInput.BackingSnapshot is null
                            || !stagedInput.BackingSnapshot.TryGetValue(
                                sourceProjection, out sourceValue!))
                        {
                            available = false;
                            continue;
                        }
                    }
                    else
                    {
                        sourceValue = BackingFieldValueOfExpression(
                            stagedInput.Argument, sourceRootType, sourceProjection,
                            facts, caller, program, ref remaining, depth + 1);
                    }

                    sources.UnionWith(sourceValue.Sources);
                    available &= sourceValue.IsAvailable;
                }

                if (!hasOrigin || !result.ContainsKey(destinationProjection))
                {
                    throw Invalid(call.Span);
                }
                result[destinationProjection] = new BackingFieldValue(
                    sources, available && hasOrigin);
            }
        }
        return result;
    }

    private static HashSet<ProvenanceAnchor> BackingBoundsOfCallResult(
        BoundCall call, BoundFunction target, BackingDependencies facts,
        BoundProgram program, ref int remaining, int depth)
    {
        var relationship = target.IsConstructor
            ? ReceiverCaptureContract(target) : ResultDependencyContract(target);
        return relationship is null
            ? []
            : BackingSourcesForCallRelationship(
                call, target, relationship, facts, program, ref remaining, depth + 1);
    }

    private static HashSet<ProvenanceAnchor> BackingSourcesForCallRelationship(
        BoundCall call, BoundFunction target, BoundDependencyContract relationship,
        BackingDependencies facts, BoundProgram program, ref int remaining, int depth)
    {
        var result = new HashSet<ProvenanceAnchor>();
        foreach (var source in relationship.Sources)
        {
            Count(ref remaining, depth + 1);
            var parameterIndex = DependencyCallParameterIndex(target, source);
            var argument = DependencyCallArgument(call, target, source);
            if (!TokenSpanContract.IsDependent(argument.Type, program.Structures)
                && !IsTokenSnapshotType(argument.Type, program)
                && !CheckedReferenceTypeContract.IsValidType(
                    argument.Type, program.CheckedReferenceTypes)
                && BackingFieldLeaves(argument.Type, program,
                    ref remaining, depth + 1).Count == 0
                && (!IsInlineStructStorage(argument.Type, program)
                    || ReferenceFieldLeaves(argument.Type, program,
                        ref remaining, depth + 1).Count == 0))
            {
                continue;
            }
            var variableIndex = parameterIndex + (target.IsConstructor ? 1 : 0);
            if ((uint)variableIndex >= (uint)target.Variables.Length)
            {
                throw Invalid(call.Span);
            }

            if (source.Kind == BoundDependencySourceKind.Receiver
                || target.Variables[variableIndex].IsImplicitRef)
            {
                result.UnionWith(BackingOf(argument, facts, program,
                    ref remaining, depth + 1));
                continue;
            }

            var staged = facts.Staged.LastOrDefault(candidate =>
                ReferenceEquals(candidate.Call, call)
                && candidate.ParameterIndex == parameterIndex);
            if (staged.Call is not null)
            {
                result.UnionWith(staged.BackingSources);
                foreach (var value in staged.ReferenceFieldValues.Values)
                {
                    result.UnionWith(value.BackingSources);
                }
            }
            else
            {
                result.UnionWith(BackingOf(argument, facts, program,
                    ref remaining, depth + 1));
            }
        }
        return result;
    }

    private static ReferenceFieldValue? CapturedReferenceValueForCall(
        BoundCall call, BoundFunction target, BoundDependencyContract relationship,
        PrimitiveType storedReferenceType, BackingDependencies facts,
        InitializationState state, uint[]? fieldMasks, BoundFunction caller,
        BoundProgram program, ref int remaining, int depth)
    {
        ReferenceFieldValue? result = null;
        foreach (var source in relationship.Sources)
        {
            Count(ref remaining, depth + 1);
            var parameterIndex = DependencyCallParameterIndex(target, source);
            var argument = DependencyCallArgument(call, target, source);
            var staged = facts.Staged.LastOrDefault(candidate =>
                ReferenceEquals(candidate.Call, call)
                && candidate.ParameterIndex == parameterIndex);
            if (staged.Call is null)
            {
                throw InvalidEffect(call.Span, "CheckedReference");
            }

            ReferenceFieldValue? sourceValue = null;
            var forwardsSource = IsForwardedCaptureSource(
                target, relationship, source);
            if (CheckedReferenceTypeContract.IsValidType(
                    argument.Type, program.CheckedReferenceTypes))
            {
                var argumentReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(argument.Type)];
                if (argument.Type == storedReferenceType && !forwardsSource)
                {
                    sourceValue = new ReferenceFieldValue(
                        staged.ReferenceTargets, staged.ReferenceObligations,
                        staged.BackingSources,
                        isAvailable: staged.ReferenceTargets.Count != 0);
                }
                else if (argumentReference.ReferentType == storedReferenceType)
                {
                    var referenceRead = new BoundReferenceRead(argument,
                        argumentReference.ReferentType, call.Span);
                    sourceValue = ReferenceFieldValueOfReferenceRead(
                        referenceRead, argumentReference.ReferentType,
                        StorageProjection.WholeObject, facts, state, fieldMasks,
                        caller, program, ref remaining, depth + 1,
                        staged.ReferenceTargets);
                }
                else if (IsInlineStructStorage(
                    argumentReference.ReferentType, program))
                {
                    var leaves = ReferenceFieldLeaves(
                        argumentReference.ReferentType, program,
                        ref remaining, depth + 1);
                    foreach (var (projection, fieldType) in leaves)
                    {
                        if (fieldType != storedReferenceType)
                        {
                            continue;
                        }
                        var referenceRead = new BoundReferenceRead(argument,
                            argumentReference.ReferentType, call.Span);
                        var fieldValue = ReferenceFieldValueOfReferenceRead(
                            referenceRead, argumentReference.ReferentType,
                            projection, facts, state, fieldMasks, caller,
                            program, ref remaining, depth + 1,
                            staged.ReferenceTargets);
                        if (sourceValue is null)
                        {
                            sourceValue = fieldValue;
                        }
                        else
                        {
                            _ = sourceValue.Merge(fieldValue);
                        }
                    }
                }
            }
            else if (IsInlineStructStorage(argument.Type, program))
            {
                foreach (var (projection, fieldType) in ReferenceFieldLeaves(
                    argument.Type, program, ref remaining, depth + 1))
                {
                    if (fieldType != storedReferenceType)
                    {
                        continue;
                    }
                    var fieldValue = staged.ReferenceFieldValues.TryGetValue(
                        projection, out var value)
                        ? value : ReferenceFieldValue.Unavailable();
                    if (sourceValue is null)
                    {
                        sourceValue = fieldValue.Clone();
                    }
                    else
                    {
                        _ = sourceValue.Merge(fieldValue);
                    }
                }
            }

            if (sourceValue is null)
            {
                continue;
            }
            if (result is null)
            {
                result = sourceValue.Clone();
            }
            else
            {
                _ = result.Merge(sourceValue);
            }
        }
        return result;
    }

    private static void ApplyBackingCallEffects(BoundCall call, BoundFunction callee,
        InitializationState initialized, BoundFunction caller, BoundProgram program,
        ref int remaining, int depth)
    {
        if (initialized.Dependencies is not BackingDependencies facts
            || callee.IsConstructor || callee.IsClassConstructor)
        {
            return;
        }
        var hasReceiver = callee.DeclaringStruct is not null && !callee.IsStatic;
        var capture = callee.DependencyContracts.FirstOrDefault(static contract =>
            contract.Relation == BoundDependencyRelation.Captures
            && contract.Destination == BoundDependencyDestination.Receiver);
        if (!hasReceiver)
        {
            if (capture is not null)
            {
                throw InvalidEffect(call.Span, "Ownership");
            }
            return;
        }

        var receiver = DependencyCallArgument(call, callee,
            new BoundDependencySource(BoundDependencySourceKind.Receiver, null));
        var isMutableReceiver = receiver is BoundMutableReceiverLoan or BoundMutableFieldLoan;
        if (!isMutableReceiver && capture is null)
        {
            return;
        }

        var capturedSources = capture is null
            ? new HashSet<ProvenanceAnchor>()
            : BackingSourcesForCallRelationship(
                call, callee, capture, facts, program, ref remaining, depth + 1);
        if (!TryGetNativePointerFieldPlace(receiver, caller, program,
            out var receiverVariable, out var receiverProjection))
        {
            if (capturedSources.Count != 0)
            {
                throw InvalidEffect(call.Span, "Ownership");
            }
            return;
        }
        facts.AddBackingSources(receiverVariable, capturedSources);

        var receiverLeaves = BackingFieldLeaves(receiver.Type, program,
            ref remaining, depth + 1);
        foreach (var leaf in receiverLeaves)
        {
            Count(ref remaining, depth + 1);
            var projection = StorageProjection.Combine(
                receiverProjection, leaf.Projection);
            if (projection.Kind != StorageProjectionKind.InlineFieldPath)
            {
                throw InvalidEffect(call.Span, "Ownership");
            }
            _ = facts.TryGetBackingFieldValue(receiverVariable, projection,
                out var previousValue);
            var possibleSources = new HashSet<ProvenanceAnchor>(previousValue.Sources);
            possibleSources.UnionWith(capturedSources);
            facts.SetBackingFieldValue(receiverVariable, projection,
                new BackingFieldValue(possibleSources, isAvailable: false));
        }

        if (receiverLeaves.Count != 0)
        {
            facts.BackingMayBeUnknown[receiverVariable] = true;
            RefreshBackingAggregateProvenance(receiverVariable,
                caller.Variables[receiverVariable].Type, facts, program,
                ref remaining, depth + 1);
        }
    }

    private static void ApplyReferenceReceiverCallEffects(BoundCall call,
        BoundFunction callee, InitializationState initialized, uint[] fieldMasks,
        BoundFunction caller, BoundProgram program, ref int remaining, int depth)
    {
        if (initialized.Dependencies is not BackingDependencies facts
            || callee.DeclaringStruct is null || callee.IsStatic
            || callee.IsConstructor || callee.IsClassConstructor
            || ReceiverCaptureContract(callee) is not { } capture)
        {
            return;
        }

        var receiver = DependencyCallArgument(call, callee,
            new BoundDependencySource(BoundDependencySourceKind.Receiver, null));
        if (receiver is not (BoundMutableReceiverLoan or BoundMutableFieldLoan)
            || !IsInlineStructStorage(receiver.Type, program)
            || !TryGetNativePointerFieldPlace(receiver, caller, program,
                out var receiverVariable, out var receiverProjection))
        {
            throw InvalidEffect(call.Span, "CheckedReference");
        }

        var referenceFieldLeaves = ReferenceFieldLeaves(receiver.Type,
            program, ref remaining, depth + 1);
        foreach (var (fieldProjection, fieldType) in referenceFieldLeaves)
        {
            var value = CapturedReferenceValueForCall(call, callee, capture,
                fieldType, facts, initialized, fieldMasks, caller, program,
                ref remaining, depth + 1);
            if (value is null)
            {
                continue;
            }
            if (!value.IsAvailable || value.Targets.Count == 0)
            {
                throw InvalidEffect(call.Span, "CheckedReference");
            }

            var destinationProjection = StorageProjection.Combine(
                receiverProjection, fieldProjection);
            if (destinationProjection.Kind
                != StorageProjectionKind.InlineFieldPath)
            {
                throw InvalidEffect(call.Span, "Ownership");
            }

            if (caller.DeclaringStruct is not null && !caller.IsStatic
                && !caller.IsConstructor && !caller.IsClassConstructor
                && receiverVariable == 0)
            {
                VerifyReferenceWriteCapture(caller, receiverVariable,
                    ReferenceCaptureSourcesOfValue(value, call.Span),
                    call.Span);
            }

            _ = facts.TryGetReferenceFieldValue(receiverVariable,
                destinationProjection, out var previousValue);
            _ = previousValue.Merge(value);
            facts.SetReferenceFieldValue(receiverVariable,
                destinationProjection, previousValue);

            var retainedSources = new HashSet<ProvenanceAnchor>(value.Targets);
            retainedSources.UnionWith(value.Obligations);
            retainedSources.UnionWith(value.BackingSources);
            facts.AddReferenceBackingSources(receiverVariable, retainedSources);
        }
    }

    private static void ApplyReferenceBackingCallEffects(BoundCall call,
        BoundFunction callee, InitializationState initialized, uint[] fieldMasks,
        BoundFunction caller, BoundProgram program, ref int remaining, int depth)
    {
        if (initialized.Dependencies is not BackingDependencies facts
            || callee.IsConstructor || callee.IsClassConstructor)
        {
            return;
        }

        ApplyReferenceReceiverCallEffects(call, callee, initialized, fieldMasks,
            caller, program, ref remaining, depth + 1);

        var receiverOffset = callee.DeclaringStruct is not null
            && !callee.IsStatic ? 1 : 0;
        foreach (var capture in callee.DependencyContracts)
        {
            if (capture.Relation != BoundDependencyRelation.Captures
                || capture.Destination != BoundDependencyDestination.RetainedFormal
                || capture.DestinationOrdinal is not int destinationOrdinal)
            {
                continue;
            }
            var destinationSlot = destinationOrdinal + receiverOffset;
            if ((uint)destinationSlot >= (uint)callee.Variables.Length
                || !CheckedReferenceTypeContract.IsValidType(
                    callee.Variables[destinationSlot].Type, program.CheckedReferenceTypes))
            {
                continue;
            }
            var reference = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(callee.Variables[destinationSlot].Type)];
            var capturesBacking = TokenSpanContract.IsDependent(
                reference.ReferentType, program.Structures);
            var capturesReference = IsOrdinaryStoredReferenceType(
                reference.ReferentType, program);
            var referenceFieldLeaves = IsInlineStructStorage(
                    reference.ReferentType, program)
                ? ReferenceFieldLeaves(reference.ReferentType, program,
                    ref remaining, depth + 1)
                : [];
            var capturesReferenceFields = referenceFieldLeaves.Count != 0;
            if (!reference.IsWritable || reference.IsGarbageCollected
                || reference.IsInitialization || reference.IsOptional
                || (!capturesBacking && !capturesReference
                    && !capturesReferenceFields))
            {
                continue;
            }

            var source = new BoundDependencySource(
                BoundDependencySourceKind.Formal, destinationOrdinal);
            var parameterIndex = DependencyCallParameterIndex(callee, source);
            var staged = facts.Staged.LastOrDefault(candidate =>
                ReferenceEquals(candidate.Call, call)
                && candidate.ParameterIndex == parameterIndex);
            if (staged.Call is null || staged.ReferenceTargets.Count == 0)
            {
                throw InvalidEffect(call.Span, "CheckedReference");
            }
            var capturedSources = BackingSourcesForCallRelationship(
                call, callee, capture, facts, program, ref remaining, depth + 1);
            var capturedReferenceValue = capturesReference
                ? CapturedReferenceValueForCall(call, callee, capture,
                    reference.ReferentType, facts, initialized, fieldMasks,
                    caller, program, ref remaining, depth + 1)
                : null;
            var capturedReferenceFieldValues = new Dictionary<
                StorageProjection, ReferenceFieldValue>();
            if (capturesReferenceFields)
            {
                foreach (var (projection, fieldType) in referenceFieldLeaves)
                {
                    var value = CapturedReferenceValueForCall(call, callee,
                        capture, fieldType, facts, initialized, fieldMasks,
                        caller, program, ref remaining, depth + 1);
                    if (value is not null)
                    {
                        capturedReferenceFieldValues.Add(projection, value);
                    }
                }
            }
            if ((capturesBacking && capturedSources.Count == 0)
                || (capturedReferenceValue is not null
                    && (!capturedReferenceValue.IsAvailable
                        || capturedReferenceValue.Targets.Count == 0)))
            {
                throw InvalidEffect(call.Span, "Ownership");
            }
            if (capturedReferenceFieldValues.Values.Any(
                    static value => !value.IsAvailable || value.Targets.Count == 0))
            {
                throw InvalidEffect(call.Span, "CheckedReference");
            }

            void VerifyIncomingCapture(ProvenanceAnchor target)
            {
                if (target.Kind is not (ProvenanceAnchorKind.IncomingReference
                    or ProvenanceAnchorKind.IncomingReferenceContents
                    or ProvenanceAnchorKind.IncomingReferenceField
                    or ProvenanceAnchorKind.IncomingReferencePlace))
                {
                    return;
                }

                var retainedSources = new HashSet<ProvenanceAnchor>();
                if (capturesBacking)
                {
                    retainedSources.UnionWith(capturedSources);
                }
                if (capturedReferenceValue is not null)
                {
                    retainedSources.UnionWith(ReferenceCaptureSourcesOfValue(
                        capturedReferenceValue, call.Span));
                }
                foreach (var capturedValue
                    in capturedReferenceFieldValues.Values)
                {
                    retainedSources.UnionWith(ReferenceCaptureSourcesOfValue(
                        capturedValue, call.Span));
                }
                if (retainedSources.Count != 0)
                {
                    VerifyReferenceWriteCapture(caller, target.Identity,
                        retainedSources, call.Span);
                }
            }

            void ApplyCapturedReferenceFieldValues(ProvenanceAnchor target)
            {
                if (!capturesReferenceFields)
                {
                    return;
                }
                if (target.Kind == ProvenanceAnchorKind.ReferencePlace)
                {
                    var targetProjection = target.Storage?.Projection
                        ?? throw Invalid(call.Span);
                    if (targetProjection.Kind is not (
                            StorageProjectionKind.WholeObject
                            or StorageProjectionKind.InlineFieldPath))
                    {
                        throw InvalidEffect(call.Span, "Ownership");
                    }
                    foreach (var (fieldProjection, capturedValue)
                        in capturedReferenceFieldValues)
                    {
                        var destinationProjection = StorageProjection.Combine(
                            targetProjection, fieldProjection);
                        if (destinationProjection.Kind
                            != StorageProjectionKind.InlineFieldPath)
                        {
                            throw InvalidEffect(call.Span, "Ownership");
                        }
                        _ = facts.TryGetReferenceFieldValue(target.Identity,
                            destinationProjection, out var previousValue);
                        _ = previousValue.Merge(capturedValue);
                        facts.SetReferenceFieldValue(target.Identity,
                            destinationProjection, previousValue);
                    }
                }
                else if (target.Kind is ProvenanceAnchorKind.IncomingReference
                    or ProvenanceAnchorKind.IncomingReferenceContents
                    or ProvenanceAnchorKind.IncomingReferenceField
                    or ProvenanceAnchorKind.IncomingReferencePlace)
                {
                    foreach (var capturedValue
                        in capturedReferenceFieldValues.Values)
                    {
                        var retainedSources = new HashSet<ProvenanceAnchor>(
                            capturedValue.Targets);
                        retainedSources.UnionWith(capturedValue.Obligations);
                        retainedSources.UnionWith(capturedValue.BackingSources);
                        facts.AddReferenceBackingSources(target.Identity,
                            retainedSources);
                    }
                    if (target.Kind == ProvenanceAnchorKind.IncomingReferencePlace
                        && target.IncomingProjection
                            is StorageProjection destinationPrefix)
                    {
                        foreach (var (fieldProjection, capturedValue)
                            in capturedReferenceFieldValues)
                        {
                            var destinationProjection = StorageProjection.Combine(
                                destinationPrefix, fieldProjection);
                            if (destinationProjection.Kind
                                != StorageProjectionKind.InlineFieldPath)
                            {
                                throw InvalidEffect(call.Span, "Ownership");
                            }
                            if (target.IncomingReferenceDepth == 0)
                            {
                                _ = facts.TryGetReferenceFieldValue(
                                    target.Identity, destinationProjection,
                                    out var previousValue);
                                _ = previousValue.Merge(capturedValue);
                                facts.SetReferenceFieldValue(target.Identity,
                                    destinationProjection, previousValue);
                            }
                            else
                            {
                                var destination = ProvenanceAnchor
                                    .ForIncomingReferencePlace(
                                        target.Identity, destinationProjection,
                                        target.IncomingReferenceDepth);
                                _ = facts.TryGetIncomingReferenceFieldValue(
                                    destination, out var previousValue);
                                _ = previousValue.Merge(capturedValue);
                                facts.SetIncomingReferenceFieldValue(destination,
                                    previousValue);
                            }
                        }
                    }
                }
            }

            foreach (var target in staged.ReferenceTargets)
            {
                ValidateReferenceTarget(target, reference.ReferentType,
                    facts, initialized, caller, program, call.Span, fieldMasks);
                VerifyIncomingCapture(target);
                if (target.Kind == ProvenanceAnchorKind.IncomingReferencePlace
                    && capturedReferenceValue is not null)
                {
                    var targetProjection = target.IncomingProjection
                        ?? throw Invalid(call.Span);
                    if (target.IncomingReferenceDepth == 0)
                    {
                        _ = facts.TryGetReferenceFieldValue(target.Identity,
                            targetProjection, out var previousValue);
                        _ = previousValue.Merge(capturedReferenceValue);
                        facts.SetReferenceFieldValue(target.Identity,
                            targetProjection, previousValue);
                    }
                    else
                    {
                        _ = facts.TryGetIncomingReferenceFieldValue(target,
                            out var previousValue);
                        _ = previousValue.Merge(capturedReferenceValue);
                        facts.SetIncomingReferenceFieldValue(target, previousValue);
                    }
                }
                ApplyCapturedReferenceFieldValues(target);
                if (target.Kind == ProvenanceAnchorKind.ReferencePlace)
                {
                    var projection = target.Storage?.Projection
                        ?? throw Invalid(call.Span);
                    if (projection.Kind == StorageProjectionKind.WholeObject)
                    {
                        if (capturesBacking)
                        {
                            facts.AddBackingSources(target.Identity, capturedSources);
                        }
                        if (capturedReferenceValue is not null)
                        {
                            var currentValue = new ReferenceFieldValue(
                                facts.ReferenceTargets[target.Identity]
                                    ?? EmptyBackingSources,
                                facts.ReferenceObligations[target.Identity]
                                    ?? EmptyBackingSources,
                                facts.ReferenceBackingSources[target.Identity]
                                    ?? EmptyBackingSources,
                                isAvailable: facts.ReferenceTargets[target.Identity]
                                    is { Count: > 0 });
                            _ = currentValue.Merge(capturedReferenceValue);
                            facts.SetReferenceTargets(target.Identity,
                                currentValue.Targets);
                            facts.SetReferenceObligations(target.Identity,
                                currentValue.Obligations);
                            facts.SetReferenceBackingSources(target.Identity,
                                currentValue.BackingSources);
                        }
                    }
                    else if (projection.Kind == StorageProjectionKind.InlineFieldPath)
                    {
                        if (capturesBacking)
                        {
                            _ = facts.TryGetBackingFieldValue(target.Identity,
                                projection, out var previousValue);
                            var possibleSources = new HashSet<ProvenanceAnchor>(
                                previousValue.Sources);
                            possibleSources.UnionWith(capturedSources);
                            facts.SetBackingFieldValue(target.Identity, projection,
                                new BackingFieldValue(possibleSources, isAvailable: false));
                            facts.BackingMayBeUnknown[target.Identity] = true;
                            RefreshBackingAggregateProvenance(target.Identity,
                                caller.Variables[target.Identity].Type, facts,
                                program, ref remaining, depth + 1);
                        }
                        if (capturedReferenceValue is not null)
                        {
                            _ = facts.TryGetReferenceFieldValue(target.Identity,
                                projection, out var previousValue);
                            _ = previousValue.Merge(capturedReferenceValue);
                            facts.SetReferenceFieldValue(target.Identity,
                                projection, previousValue);
                        }
                    }
                    else
                    {
                        throw InvalidEffect(call.Span, "Ownership");
                    }
                }
                else if (target.Kind is ProvenanceAnchorKind.IncomingReference
                        or ProvenanceAnchorKind.IncomingReferenceContents
                        or ProvenanceAnchorKind.IncomingReferenceField
                        or ProvenanceAnchorKind.IncomingReferencePlace)
                {
                    if (capturesBacking)
                    {
                        facts.AddReferenceBackingSources(target.Identity,
                            capturedSources);
                    }
                    if (capturedReferenceValue is not null)
                    {
                        var retainedSources = new HashSet<ProvenanceAnchor>(
                            capturedReferenceValue.Targets);
                        retainedSources.UnionWith(
                            capturedReferenceValue.Obligations);
                        retainedSources.UnionWith(
                            capturedReferenceValue.BackingSources);
                        facts.AddReferenceBackingSources(target.Identity,
                            retainedSources);
                    }
                }
                else
                {
                    throw InvalidEffect(call.Span, "CheckedReference");
                }
            }
        }
    }

    private static Dictionary<StorageProjection, NativePointerFieldValue>?
        CreateNativePointerCallResult(
            BoundCall call, BoundFunction target, BoundCopyOriginSummary? summary,
            IReadOnlyDictionary<int, StagedCopyOriginInput> stagedInputs,
            BackingDependencies facts, BoundFunction caller, BoundProgram program,
            ref int remaining, int depth)
    {
        if (!CarriesNativePointer(call.Type, program) || summary is null)
        {
            return null;
        }
        ValidateCopyOriginSummary(target, summary, program);

        var result = new Dictionary<StorageProjection, NativePointerFieldValue>();
        foreach (var leaf in NativePointerFieldLeaves(call.Type, program,
            ref remaining, depth + 1))
        {
            Count(ref remaining, depth + 1);
            if (!result.TryAdd(leaf.Projection,
                new NativePointerFieldValue(EmptyBackingSources, mayBeUnknown: true)))
            {
                throw Invalid(call.Span);
            }
        }

        foreach (var fact in summary.Facts)
        {
            if (!TryResolveCopyOriginFieldPath(target.ReturnType,
                    fact.DestinationPath, program, out var destinationType))
            {
                throw Invalid(call.Span);
            }

            var destinationPrefix = CopyOriginStorageProjection(fact.DestinationPath);
            foreach (var leaf in NativePointerFieldLeaves(destinationType, program,
                ref remaining, depth + 1))
            {
                Count(ref remaining, depth + 1);
                var destinationProjection = StorageProjection.Combine(
                    destinationPrefix, leaf.Projection);
                var sources = new HashSet<ProvenanceAnchor>();
                var extentSources = new HashSet<ProvenanceAnchor>();
                var mayBeUnknown = false;
                var extentMayBeOpaque = false;
                var extentUnavailable = false;
                var hasOrigin = false;
                foreach (var origin in fact.Origins)
                {
                    hasOrigin = true;
                    var parameterIndex = CopyOriginCallParameterIndex(target, origin);
                    if (!stagedInputs.TryGetValue(parameterIndex, out var stagedInput)
                        || stagedInput.SourceView != origin.SourceView
                        || !TryGetCopyOriginSourceType(target, origin, program,
                            out var sourceRootType)
                        || !TryResolveCopyOriginFieldPath(sourceRootType,
                            origin.SourcePath, program, out var sourceType)
                        || sourceType != destinationType)
                    {
                        throw Invalid(call.Span);
                    }

                    var sourceProjection = StorageProjection.Combine(
                        CopyOriginStorageProjection(origin.SourcePath),
                        leaf.Projection);
                    NativePointerFieldValue sourceValue;
                    if (origin.SourceView == BoundCopyOriginSourceView.EntryValue)
                    {
                        if (stagedInput.NativePointerSnapshot is null
                            || !stagedInput.NativePointerSnapshot.TryGetValue(
                                sourceProjection, out sourceValue!))
                        {
                            mayBeUnknown = true;
                            extentUnavailable = true;
                            continue;
                        }
                    }
                    else
                    {
                        var (sourceValueSources, sourceValueMayBeUnknown) =
                            NativePointerValueFactsOfExpression(stagedInput.Argument,
                                stagedInput.Argument.Type, sourceProjection, facts,
                                caller, program, ref remaining, depth + 1);
                        sourceValue = new NativePointerFieldValue(
                            sourceValueSources, sourceValueMayBeUnknown);
                        var sourceExtent = NativePointerExtentValueFactsOfExpression(
                            stagedInput.Argument, stagedInput.Argument.Type,
                            sourceProjection, facts, caller, program,
                            ref remaining, depth + 1);
                        sourceValue.ExtentSources.UnionWith(sourceExtent.Sources);
                        sourceValue.ExtentMayBeOpaque = sourceExtent.MayBeOpaque;
                        sourceValue.ExtentUnavailable = sourceExtent.IsUnavailable;
                    }

                    sources.UnionWith(sourceValue.Sources);
                    mayBeUnknown |= sourceValue.MayBeUnknown;
                    extentSources.UnionWith(sourceValue.ExtentSources);
                    extentMayBeOpaque |= sourceValue.ExtentMayBeOpaque;
                    extentUnavailable |= sourceValue.ExtentUnavailable;
                }

                if (!hasOrigin || !result.ContainsKey(destinationProjection))
                {
                    throw Invalid(call.Span);
                }
                result[destinationProjection] = new NativePointerFieldValue(
                    sources, mayBeUnknown, extentSources,
                    extentMayBeOpaque, extentUnavailable);
            }
        }
        return result;
    }

    private static StorageProjection CopyOriginStorageProjection(
        BoundCopyOriginFieldPath path)
    {
        var projection = StorageProjection.WholeObject;
        foreach (var step in path.Steps)
        {
            projection = projection.AppendField(step.OwnerType, step.FieldOrdinal);
        }
        return projection;
    }

    private static void ValidateCopyOriginSummary(
        BoundFunction function, BoundCopyOriginSummary summary, BoundProgram program)
    {
        if (summary is null || summary.Facts.IsDefaultOrEmpty
            || summary.Facts.Length > MaximumAnalysisVisits
            || function.IsExternal || function.Intrinsic != BoundIntrinsic.None
            || function.IsClassConstructor || function.IsCleanup || function.DoesNotReturn
            || function.DirectNativeImport is not null || function.NativeVTableMethod is not null
            || function.NativeFunctionPointerType is not null
            || function.NativeCallableEntryName is not null)
        {
            throw Invalid(function.Span);
        }

        var resultType = function.ReturnType;
        if (summary.Kind == BoundCopyOriginSummaryKind.NormalResult)
        {
            if (function.IsConstructor || !IsCopyOriginValueType(resultType, program)
                || IsUnique(program, resultType))
            {
                throw Invalid(function.Span);
            }
        }
        else if (summary.Kind == BoundCopyOriginSummaryKind.FreshInlineConstruction)
        {
            if (!function.IsConstructor || !IsInlineStructStorage(resultType, program)
                || (!IsCopyStruct(program, resultType)
                    && !IsUnique(program, resultType)))
            {
                throw Invalid(function.Span);
            }
        }
        else
        {
            throw Invalid(function.Span);
        }

        var destinations = new List<BoundCopyOriginFieldPath>(summary.Facts.Length);
        var remaining = MaximumAnalysisVisits;
        foreach (var fact in summary.Facts)
        {
            if (fact is null || fact.Origins.IsDefaultOrEmpty
                || fact.Origins.Length > MaximumAnalysisVisits
                || !TryResolveCopyOriginFieldPath(resultType,
                    fact.DestinationPath, program, out var destinationType)
                || !IsCopyOriginValueType(destinationType, program)
                || !CarriesCopyOrigin(destinationType, program))
            {
                throw Invalid(function.Span);
            }

            if (destinations.Any(existing =>
                CopyOriginPathIsPrefix(existing, fact.DestinationPath)
                || CopyOriginPathIsPrefix(fact.DestinationPath, existing)))
            {
                throw Invalid(function.Span);
            }
            destinations.Add(fact.DestinationPath);

            var origins = new HashSet<BoundCopyOrigin>();
            var referenceFieldLeaves = ReferenceFieldLeaves(destinationType,
                program, ref remaining, 0);
            foreach (var origin in fact.Origins)
            {
                if (!origins.Add(origin)
                    || !TryGetCopyOriginSourceType(function, origin, program,
                        out var sourceRootType)
                    || !TryResolveCopyOriginFieldPath(sourceRootType,
                        origin.SourcePath, program, out var sourceType)
                    || sourceType != destinationType)
                {
                    throw Invalid(function.Span);
                }
                if (referenceFieldLeaves.Count != 0
                    && !AggregateReferenceReturnDependsOnFormal(function,
                        CopyOriginSourceParameterSlot(function, origin)))
                {
                    throw Invalid(function.Span);
                }
            }
        }
    }

    private static Dictionary<int, BoundCopyOriginSourceView> CopyOriginSummaryInputViews(
        BoundFunction function, BoundCopyOriginSummary summary)
    {
        var result = new Dictionary<int, BoundCopyOriginSourceView>();
        foreach (var fact in summary.Facts)
        {
            foreach (var origin in fact.Origins)
            {
                var parameterIndex = CopyOriginCallParameterIndex(function, origin);
                if (result.TryGetValue(parameterIndex, out var existing)
                    && existing != origin.SourceView)
                {
                    throw Invalid(function.Span);
                }
                result[parameterIndex] = origin.SourceView;
            }
        }
        return result;
    }

    private static bool TryGetCopyOriginSourceType(
        BoundFunction function, BoundCopyOrigin origin, BoundProgram program,
        out PrimitiveType type)
    {
        type = PrimitiveType.Void;
        var hasBorrowedReceiver = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor && !function.IsClassConstructor;
        if (origin.SourceKind == BoundCopyOriginSourceKind.Receiver)
        {
            if (!hasBorrowedReceiver || origin.SourceOrdinal != 0
                || origin.SourceView != BoundCopyOriginSourceView.EntryReferentValue
                || function.Variables.IsDefaultOrEmpty
                || !IsInlineStructStorage(function.Variables[0].Type, program)
                || !IsCopyStruct(program, function.Variables[0].Type))
            {
                return false;
            }
            type = function.Variables[0].Type;
            return true;
        }
        if (origin.SourceKind != BoundCopyOriginSourceKind.Formal)
        {
            return false;
        }

        var formalCount = function.ParameterCount - (hasBorrowedReceiver ? 1 : 0);
        var formalOffset = function.IsConstructor || hasBorrowedReceiver ? 1 : 0;
        if ((uint)origin.SourceOrdinal >= (uint)formalCount)
        {
            return false;
        }
        var slot = origin.SourceOrdinal + formalOffset;
        if ((uint)slot >= (uint)function.Variables.Length)
        {
            return false;
        }
        var formal = function.Variables[slot];
        var expectedView = formal.IsImplicitRef
            ? BoundCopyOriginSourceView.EntryReferentValue
            : BoundCopyOriginSourceView.EntryValue;
        if (origin.SourceView != expectedView || formal.IsOut || formal.IsInit
            || !IsCopyOriginValueType(formal.Type, program)
            || (!formal.IsImplicitRef && IsUnique(program, formal.Type))
            || (formal.IsImplicitRef && !IsImplicitReferenceType(formal.Type, program)))
        {
            return false;
        }
        type = formal.Type;
        return true;
    }

    private static int CopyOriginCallParameterIndex(
        BoundFunction function, BoundCopyOrigin origin)
    {
        if (origin.SourceKind == BoundCopyOriginSourceKind.Receiver)
        {
            return 0;
        }
        var hasBorrowedReceiver = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor && !function.IsClassConstructor;
        return origin.SourceOrdinal + (hasBorrowedReceiver ? 1 : 0);
    }

    private static int CopyOriginSourceParameterSlot(
        BoundFunction function, BoundCopyOrigin origin)
    {
        if (origin.SourceKind == BoundCopyOriginSourceKind.Receiver)
        {
            return 0;
        }
        var hasReceiverSlot = function.IsConstructor
            || (function.DeclaringStruct is not null
                && !function.IsStatic && !function.IsClassConstructor);
        return origin.SourceOrdinal + (hasReceiverSlot ? 1 : 0);
    }

    private static bool TryResolveCopyOriginFieldPath(
        PrimitiveType rootType, BoundCopyOriginFieldPath path,
        BoundProgram program, out PrimitiveType resultType)
    {
        resultType = rootType;
        if (path.Steps.Length > MaximumDepth)
        {
            return false;
        }
        foreach (var step in path.Steps)
        {
            if (step.OwnerType != resultType || !IsInlineStructStorage(resultType, program))
            {
                return false;
            }
            var fields = program.Structures[(int)resultType - 100].Fields;
            if ((uint)step.FieldOrdinal >= (uint)fields.Length)
            {
                return false;
            }
            resultType = fields[step.FieldOrdinal].Type;
        }
        return true;
    }

    private static BoundCopyOriginFieldPath AppendCopyOriginPath(
        BoundCopyOriginFieldPath prefix, BoundCopyOriginFieldPath suffix)
        => new([.. prefix.Steps, .. suffix.Steps]);

    private static bool CopyOriginPathIsPrefix(
        BoundCopyOriginFieldPath prefix, BoundCopyOriginFieldPath path)
    {
        var prefixSteps = prefix.Steps;
        var pathSteps = path.Steps;
        return prefixSteps.Length <= pathSteps.Length
            && prefixSteps.AsSpan().SequenceEqual(pathSteps.AsSpan(0, prefixSteps.Length));
    }

    private static Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>>
        CopyOriginFactsForFreshReceiver(PrimitiveType type, BackingDependencies facts,
            BoundProgram program, ref int remaining, int depth)
    {
        var result = new Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>>();
        var leaves = CopyOriginFieldLeaves(type, program, ref remaining, depth + 1);
        foreach (var leaf in leaves)
        {
            Count(ref remaining, depth + 1);
            if (leaf.Projection.Kind != StorageProjectionKind.InlineFieldPath
                || !facts.TryGetCopyOriginFieldValue(0, leaf.Projection, out var valueFact)
                || !valueFact.IsAvailable || valueFact.Origins.Count == 0)
            {
                continue;
            }

            var destinationPath = BoundCopyOriginPath(leaf.Projection.FieldPath);
            var origins = new HashSet<BoundCopyOrigin>(valueFact.Origins.Count);
            foreach (var origin in valueFact.Origins)
            {
                _ = origins.Add(new BoundCopyOrigin(
                    (BoundCopyOriginSourceKind)origin.SourceKind,
                    (BoundCopyOriginSourceView)origin.SourceView,
                    origin.SourceOrdinal, BoundCopyOriginPath(origin.SourcePath)));
            }
            if (!result.TryAdd(destinationPath, origins))
            {
                throw Invalid();
            }
        }
        return result;
    }

    private static BoundCopyOriginFieldPath BoundCopyOriginPath(FieldProjectionPath path)
        => new([.. path.Segments.Select(static segment =>
            new BoundCopyOriginFieldStep(segment.OwnerType, segment.Field))]);

    private static bool TryGetCopyOriginInput(BoundFunction function, BoundProgram program,
        int slot, out CopyValueOrigin origin)
    {
        origin = default;
        if (slot < 0 || slot >= function.Variables.Length || function.IsClassConstructor)
        {
            return false;
        }

        var hasBorrowedReceiver = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor;
        if (hasBorrowedReceiver && slot == 0)
        {
            var receiverType = function.Variables[0].Type;
            if (!IsInlineStructStorage(receiverType, program)
                || !IsCopyStruct(program, receiverType)
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
        if (formalOrdinal < 0 || formalOrdinal >= formalCount)
        {
            return false;
        }

        var formal = function.Variables[slot];
        if (formal.IsOut || formal.IsInit
            || !IsCopyOriginValueType(formal.Type, program)
            || !CarriesCopyOrigin(formal.Type, program)
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
            if (!IsNativePointerFieldProjection(destinationProjection))
            {
                throw Invalid(expression.Span);
            }
            var (sources, mayBeUnknown) = NativePointerValueFactsOfExpression(expression,
                destinationType, leaf.Projection, facts, function, program,
                ref remaining, depth + 1);
            var extentFact = NativePointerExtentValueFactsOfExpression(
                expression, destinationType, leaf.Projection, facts, function,
                program, ref remaining, depth + 1);
            facts.SetNativePointerFieldValue(destination, destinationProjection,
                sources, mayBeUnknown, extentFact);
        }
    }

    private static void SetBackingFieldValuesFromExpression(int destination,
        StorageProjection destinationPrefix, PrimitiveType destinationType,
        BoundExpression expression, BackingDependencies facts,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        if ((uint)destination >= (uint)function.Variables.Length
            || expression is null || expression.Type != destinationType
            || destinationPrefix.Kind is not (StorageProjectionKind.WholeObject
                or StorageProjectionKind.InlineFieldPath))
        {
            throw Invalid(expression?.Span ?? default);
        }

        var leaves = BackingFieldLeaves(destinationType, program, ref remaining, depth + 1);
        var projectedValues = new List<(StorageProjection Projection, BackingFieldValue Value)>(
            leaves.Count);
        foreach (var leaf in leaves)
        {
            Count(ref remaining, depth + 1);
            var destinationProjection = StorageProjection.Combine(
                destinationPrefix, leaf.Projection);
            if (destinationProjection.Kind != StorageProjectionKind.InlineFieldPath)
            {
                throw Invalid(expression.Span);
            }
            projectedValues.Add((destinationProjection,
                BackingFieldValueOfExpression(expression, destinationType, leaf.Projection,
                    facts, function, program, ref remaining, depth + 1)));
        }

        if (destinationPrefix.Kind == StorageProjectionKind.WholeObject)
        {
            facts.ClearBackingFieldValues(destination);
        }
        else
        {
            facts.ClearBackingFieldValues(destination, destinationPrefix);
        }

        foreach (var (projection, value) in projectedValues)
        {
            Count(ref remaining, depth + 1);
            facts.SetBackingFieldValue(destination, projection, value);
        }

        var ownerType = function.Variables[destination].Type;
        if (BackingFieldLeaves(ownerType, program, ref remaining, depth + 1).Count != 0)
        {
            RefreshBackingAggregateProvenance(destination, ownerType, facts,
                program, ref remaining, depth + 1);
        }
    }

    private static BackingFieldValue BackingFieldValueOfExpression(
        BoundExpression expression, PrimitiveType valueType,
        StorageProjection relativeProjection, BackingDependencies facts,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is null || expression.Type != valueType)
        {
            throw Invalid(expression?.Span ?? default);
        }

        switch (expression)
        {
            case BoundCall call when facts.TryGetBackingCallResult(
                call, relativeProjection, out var callValue):
                return callValue;
            case BoundFieldMove move when facts.TryGetBackingCallResult(
                move, relativeProjection, out var movedValue):
                return movedValue;
            case BoundUnsafeExpression unsafeExpression:
                return BackingFieldValueOfExpression(unsafeExpression.Expression,
                    valueType, relativeProjection, facts, function, program,
                    ref remaining, depth + 1);
            case BoundConditional conditional when conditional.WhenTrue is not null
                && conditional.WhenFalse is not null
                && conditional.WhenTrue.Type == valueType
                && conditional.WhenFalse.Type == valueType:
                var whenTrue = BackingFieldValueOfExpression(conditional.WhenTrue,
                    valueType, relativeProjection, facts, function, program,
                    ref remaining, depth + 1);
                var whenFalse = BackingFieldValueOfExpression(conditional.WhenFalse,
                    valueType, relativeProjection, facts, function, program,
                    ref remaining, depth + 1);
                _ = whenTrue.Merge(whenFalse);
                return whenTrue;
        }

        if (TryGetNativePointerFieldPlace(expression, function, program,
            out var variable, out var sourcePrefix))
        {
            var sourceProjection = StorageProjection.Combine(
                sourcePrefix, relativeProjection);
            if (sourceProjection.Kind == StorageProjectionKind.InlineFieldPath
                && facts.TryGetBackingFieldValue(variable, sourceProjection,
                    out var fieldValue))
            {
                return BackingFieldValueWithBounds(variable, fieldValue, facts);
            }
        }

        var leaves = BackingFieldLeaves(valueType, program, ref remaining, depth + 1);
        return leaves.Count == 1
            ? new BackingFieldValue(
                BackingOf(expression, facts, program, ref remaining, depth + 1),
                isAvailable: true)
            : BackingFieldValue.Unavailable();
    }

    private static BackingFieldValue BackingFieldValueWithBounds(
        int slot, BackingFieldValue value, BackingDependencies facts)
    {
        var sources = new HashSet<ProvenanceAnchor>(value.Sources);
        if (facts.BackingBounds[slot] is { } bounds)
        {
            sources.UnionWith(bounds);
        }
        return new BackingFieldValue(sources, value.IsAvailable);
    }

    private static void RefreshBackingAggregateProvenance(int slot,
        PrimitiveType type, BackingDependencies facts, BoundProgram program,
        ref int remaining, int depth)
    {
        var leaves = BackingFieldLeaves(type, program, ref remaining, depth + 1);
        var sources = new HashSet<ProvenanceAnchor>();
        var mayBeUnknown = leaves.Count == 0;
        foreach (var leaf in leaves)
        {
            Count(ref remaining, depth + 1);
            if (!facts.TryGetBackingFieldValue(slot, leaf.Projection, out var fieldValue)
                || !fieldValue.IsAvailable)
            {
                mayBeUnknown = true;
            }
            sources.UnionWith(fieldValue.Sources);
        }
        facts.SetBackingAggregateProvenance(slot, sources, mayBeUnknown);
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
            if (NativeFixedArrayTypes.IsType(current))
            {
                var arrayIndex = NativeFixedArrayTypes.Index(current);
                if ((uint)arrayIndex
                    < (uint)program.NativeFixedArrayTypes.Length)
                {
                    var array = program.NativeFixedArrayTypes[arrayIndex];
                    if (array.ElementCount > 0)
                    {
                        pending.Push(array.ElementType);
                    }
                }
                continue;
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
                    address.Projections.IsDefaultOrEmpty
                        ? StorageProjection.ForArrayElement(address.Index)
                        : StorageProjection.WholeObject);
            case BoundNativeStorageAddress address
                when (uint)address.Variable < (uint)facts.NativePointerSources.Length:
                return NativeStorageAnchors(address.Variable, facts,
                    StorageProjection.ForStorageAddress(address));
            case BoundNativeFixedArrayElement element
                when CarriesNativePointer(element.Type, program)
                    && (uint)element.Variable < (uint)facts.NativePointerSources.Length:
                return facts.TryGetNativePointerFieldValue(
                    element.Variable,
                    StorageProjection.ForArrayElement(element.Index),
                    out var elementSources, out _)
                        ? elementSources : EmptyBackingSources;
            case BoundRead read when (uint)read.Variable < (uint)facts.NativePointerSources.Length:
                return facts.NativePointerSources[read.Variable] ?? EmptyBackingSources;
            case BoundMove move when (uint)move.Variable < (uint)facts.NativePointerSources.Length:
                return facts.NativePointerSources[move.Variable] ?? EmptyBackingSources;
            case BoundFieldMove move when facts.TryGetNativePointerCallResult(
                move, StorageProjection.WholeObject, out var movedSources, out _):
                return movedSources;
            case BoundNativePointerRead read when CarriesNativePointer(read.Type, program):
                var readSources = new HashSet<ProvenanceAnchor>();
                foreach (var target in NativePointerBackingOf(read.Address, facts, program,
                    ref remaining, depth + 1))
                {
                    if (target.Kind == ProvenanceAnchorKind.NativeStorage
                        && (uint)target.Identity < (uint)facts.NativePointerSources.Length)
                    {
                        if (target.Storage is not StorageKey targetStorage
                            || !targetStorage.IsKnown
                            || targetStorage.Activation != facts.FunctionActivation
                            || !facts.StorageInstanceSet(target.Identity).Contains(targetStorage.Instance))
                        {
                            throw InvalidEffect(read.Span, "NativePointer");
                        }
                        if (targetStorage.Projection.Kind
                                == StorageProjectionKind.WholeObject
                            && facts.NativePointerSources[target.Identity]
                                is { } targetSources)
                        {
                            readSources.UnionWith(targetSources);
                        }
                        else if (IsNativePointerFieldProjection(targetStorage.Projection)
                            && facts.TryGetNativePointerFieldValue(
                                target.Identity, targetStorage.Projection,
                                out var fieldSources, out _))
                        {
                            readSources.UnionWith(fieldSources);
                        }
                    }
                }
                return readSources;
            case BoundFieldRead field:
                return NativePointerFieldFactsOf(field, facts, facts.Function, program,
                    ref remaining, depth + 1).Sources;
            case BoundUnsafeExpression unsafeExpression:
                return NativePointerBackingOf(unsafeExpression.Expression, facts, program, ref remaining, depth + 1);
            case BoundCall call when CarriesNativePointer(call.Type, program):
                if (facts.TryGetNativePointerCallResult(call,
                    StorageProjection.WholeObject, out var resultSources, out _))
                {
                    return resultSources;
                }
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

    private static NativePointerExtentFact NativePointerExtentFactsOfExpression(
        BoundExpression expression, BackingDependencies facts,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
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
                    || !NativePointerTypeContract.IsValidType(address.Type,
                        program.NativePointerTypes))
                {
                    throw InvalidEffect(address.Span, "NativePointer");
                }
                return new NativePointerExtentFact(
                    NativeStorageAnchors(address.Variable, facts,
                        StorageProjection.WholeObject),
                    MayBeOpaque: false,
                    IsUnavailable: HasUnknownStorageInstances(
                        facts.StorageInstanceSet(address.Variable)));
            case BoundNativeFixedArrayPointerAddress address:
                var nestedPath = !address.Projections.IsDefaultOrEmpty;
                var rootLayout = nestedPath
                    ? address.Projections[0].Layout : address.Layout;
                if ((uint)address.Variable >= (uint)function.Variables.Length
                    || function.Variables[address.Variable]
                        .NativeFixedArrayLayout != rootLayout
                    || (nestedPath
                        && (!IsValidNativeFixedArrayPath(address.Variable,
                                address.Projections, function, program,
                                out var pathLeaf)
                            || pathLeaf != address.Layout
                            || address.Index != address.Projections[^1].Index))
                    || !NativeFixedArrayContract.MatchesLayout(
                        address.Layout, program)
                    || !NativePointerTypeContract.IsValidType(address.Type,
                        program.NativePointerTypes))
                {
                    throw InvalidEffect(address.Span, "NativePointer");
                }
                return new NativePointerExtentFact(
                    NativeStorageAnchors(address.Variable, facts,
                        nestedPath ? StorageProjection.WholeObject
                            : StorageProjection.ForArrayElement(address.Index)),
                    MayBeOpaque: false,
                    IsUnavailable: HasUnknownStorageInstances(
                        facts.StorageInstanceSet(address.Variable)));
            case BoundNativeStorageAddress address:
                if (address.Type != PrimitiveType.NativeStorageAddress
                    || address.AddressType != PrimitiveType.NativeStorageAddress
                    || (uint)address.Variable >= (uint)function.Variables.Length
                    || address.ElementCount <= 0 || address.ElementStride <= 0
                    || address.ByteExtent <= 0)
                {
                    throw InvalidEffect(address.Span, "NativePointer");
                }
                return new NativePointerExtentFact(
                    NativeStorageAnchors(address.Variable, facts,
                        StorageProjection.ForStorageAddress(address)),
                    MayBeOpaque: false,
                    IsUnavailable: HasUnknownStorageInstances(
                        facts.StorageInstanceSet(address.Variable)));
            case BoundNativeFixedArrayElement element
                when IsNativePointerValue(element.Type, program):
                if ((uint)element.Variable >= (uint)function.Variables.Length
                    || function.Variables[element.Variable].Type
                        != element.Layout.CarrierType
                    || function.Variables[element.Variable].NativeFixedArrayLayout
                        != element.Layout
                    || !NativeFixedArrayContract.MatchesLayout(
                        element.Layout, program)
                    || element.Type != element.Layout.ElementType)
                {
                    throw InvalidEffect(element.Span, "NativePointer");
                }
                return facts.TryGetNativePointerFieldExtentValue(
                    element.Variable,
                    StorageProjection.ForArrayElement(element.Index),
                    out var elementExtent)
                        ? elementExtent
                        : new NativePointerExtentFact([], MayBeOpaque: false,
                            IsUnavailable: true);
            case BoundRead read when CarriesNativePointer(read.Type, program):
                if ((uint)read.Variable >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != read.Type)
                {
                    throw Invalid(read.Span);
                }
                return new NativePointerExtentFact(
                    facts.NativePointerExtentSources[read.Variable] is { } readSources
                        ? [.. readSources] : [],
                    facts.NativePointerExtentMayBeOpaque[read.Variable],
                    facts.NativePointerExtentUnavailable[read.Variable]);
            case BoundMove move when CarriesNativePointer(move.Type, program):
                if ((uint)move.Variable >= (uint)function.Variables.Length
                    || function.Variables[move.Variable].Type != move.Type)
                {
                    throw Invalid(move.Span);
                }
                return new NativePointerExtentFact(
                    facts.NativePointerExtentSources[move.Variable] is { } moveSources
                        ? [.. moveSources] : [],
                    facts.NativePointerExtentMayBeOpaque[move.Variable],
                    facts.NativePointerExtentUnavailable[move.Variable]);
            case BoundFieldMove move when CarriesNativePointer(move.Type, program)
                && facts.TryGetNativePointerCallExtentResult(
                    move, StorageProjection.WholeObject, out var movedExtent):
                return movedExtent;
            case BoundUnsafeExpression unsafeExpression:
                return NativePointerExtentFactsOfExpression(
                    unsafeExpression.Expression, facts, function, program,
                    ref remaining, depth + 1);
            case BoundConditional conditional when CarriesNativePointer(
                conditional.Type, program):
                var whenTrue = NativePointerExtentFactsOfExpression(
                    conditional.WhenTrue, facts, function, program,
                    ref remaining, depth + 1);
                var whenFalse = NativePointerExtentFactsOfExpression(
                    conditional.WhenFalse, facts, function, program,
                    ref remaining, depth + 1);
                whenTrue.Sources.UnionWith(whenFalse.Sources);
                return new NativePointerExtentFact(whenTrue.Sources,
                    whenTrue.MayBeOpaque || whenFalse.MayBeOpaque,
                    whenTrue.IsUnavailable || whenFalse.IsUnavailable);
            case BoundCall call when CarriesNativePointer(call.Type, program):
                if (facts.TryGetNativePointerCallExtentResult(
                    call, StorageProjection.WholeObject, out var callExtent))
                {
                    return callExtent;
                }
                if ((uint)call.Function >= (uint)program.Functions.Length)
                {
                    throw Invalid(call.Span);
                }
                var callee = program.Functions[call.Function];
                return callee.IsExternal
                    && (callee.DirectNativeImport is not null
                        || callee.NativeVTableMethod is not null)
                    ? new NativePointerExtentFact([], MayBeOpaque: true, IsUnavailable: false)
                    : new NativePointerExtentFact([], MayBeOpaque: false, IsUnavailable: true);
            case BoundIndirectCall call when CarriesNativePointer(call.Type, program):
                return new NativePointerExtentFact([], MayBeOpaque: true, IsUnavailable: false);
            case BoundConstant constant when CarriesNativePointer(constant.Type, program):
                return new NativePointerExtentFact([], MayBeOpaque: false,
                    IsUnavailable: constant.Value != 0);
            case BoundFieldRead field when CarriesNativePointer(field.Type, program):
                return NativePointerExtentValueFactsOfExpression(field, field.Type,
                    StorageProjection.WholeObject, facts, function, program,
                    ref remaining, depth + 1);
            case BoundNativePointerRead read when CarriesNativePointer(read.Type, program):
                return NativePointerExtentFactsOfPointerRead(read,
                    StorageProjection.WholeObject, facts, function, program,
                    ref remaining, depth + 1);
            default:
                return new NativePointerExtentFact([], MayBeOpaque: false,
                    IsUnavailable: CarriesNativePointer(expression.Type, program));
        }
    }

    private static NativePointerExtentFact NativePointerExtentFactsOfPointerRead(
        BoundNativePointerRead read, StorageProjection relativeProjection,
        BackingDependencies facts, BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        var targets = NativePointerBackingOf(read.Address, facts, program,
            ref remaining, depth + 1);
        if (targets.Count == 0 || NativePointerMayBeUnknownOf(read.Address,
            facts, program, ref remaining, depth + 1))
        {
            return new NativePointerExtentFact([], MayBeOpaque: false,
                IsUnavailable: true);
        }

        var sources = new HashSet<ProvenanceAnchor>();
        var mayBeOpaque = false;
        var unavailable = false;
        foreach (var target in targets)
        {
            Count(ref remaining, depth + 1);
            if (target.Kind != ProvenanceAnchorKind.NativeStorage
                || (uint)target.Identity >= (uint)function.Variables.Length
                || target.Storage is not StorageKey storage
                || !storage.IsKnown
                || storage.Activation != facts.FunctionActivation
                || !facts.StorageInstanceSet(target.Identity).Contains(storage.Instance))
            {
                unavailable = true;
                continue;
            }

            if (storage.Projection.Kind == StorageProjectionKind.WholeObject
                && function.Variables[target.Identity].Type == read.Type
                && relativeProjection.Kind == StorageProjectionKind.WholeObject)
            {
                if (facts.NativePointerExtentSources[target.Identity] is { } storedSources)
                {
                    sources.UnionWith(storedSources);
                }
                mayBeOpaque |= facts.NativePointerExtentMayBeOpaque[target.Identity];
                unavailable |= facts.NativePointerExtentUnavailable[target.Identity];
            }
            else
            {
                var projection = StorageProjection.Combine(
                    storage.Projection, relativeProjection);
                if (IsNativePointerFieldProjection(projection)
                    && facts.TryGetNativePointerFieldExtentValue(
                        target.Identity, projection, out var fieldExtent))
                {
                    sources.UnionWith(fieldExtent.Sources);
                    mayBeOpaque |= fieldExtent.MayBeOpaque;
                    unavailable |= fieldExtent.IsUnavailable;
                }
                else
                {
                    unavailable = true;
                }
            }
        }

        return new NativePointerExtentFact(sources, mayBeOpaque, unavailable);
    }

    private static void VerifyNativeParameterValueExtent(BoundExpression argument,
        DirectNativeParameter parameter, bool isUnsafeAuthorized,
        InitializationState initialized, BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        if (parameter.Extent == 0)
        {
            return;
        }

        if (parameter.PointeeType == PrimitiveType.Void
            || initialized.Dependencies is not BackingDependencies facts)
        {
            throw InvalidEffect(argument.Span, "NativeImport");
        }

        var extentFact = NativePointerExtentFactsOfExpression(argument, facts,
            function, program, ref remaining, depth + 1);
        VerifyNativeParameterExtentFact(extentFact, argument.Span, parameter,
            isUnsafeAuthorized, facts, function, program);
    }

    private static void VerifyStagedNativeParameterExtents(BoundCall call,
        BoundFunction target, InitializationState initialized, BoundProgram program,
        ref int remaining, int depth)
    {
        if (initialized.Dependencies is not BackingDependencies facts)
        {
            if (target.DirectNativeImport?.Parameters.IsDefaultOrEmpty == false
                || target.NativeVTableMethod?.Parameters.IsDefaultOrEmpty == false)
            {
                throw InvalidEffect(call.Span, "NativeImport");
            }
            return;
        }

        for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            Count(ref remaining, depth + 1);
            var parameterIndex = call.ParameterIndex(argumentIndex);
            var parameter = DirectNativeImportContract.ParameterForParameter(
                target, parameterIndex)
                ?? NativeVTableCallContract.ParameterForParameter(target, parameterIndex);
            if (parameter is null || parameter.Extent == 0
                || DirectNativeImportContract.IsStringPointerArgument(
                    call.Arguments[argumentIndex], parameter, program))
            {
                continue;
            }

            var stagedIndex = facts.Staged.FindLastIndex(staged =>
                ReferenceEquals(staged.Call, call)
                && staged.ParameterIndex == parameterIndex);
            if (stagedIndex < 0)
            {
                throw InvalidEffect(call.Arguments[argumentIndex].Span, "NativeImport");
            }

            VerifyNativeParameterExtentFact(
                facts.Staged[stagedIndex].NativePointerExtent,
                call.Arguments[argumentIndex].Span, parameter,
                call.IsUnsafeAuthorized, facts, facts.Function, program);
        }
    }

    private static void VerifyStagedNativeParameterExtents(BoundIndirectCall call,
        NativeFunctionPointerSignatureDefinition signature,
        InitializationState initialized, BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        if (initialized.Dependencies is not BackingDependencies facts)
        {
            if (signature.NativeParameters.Any(parameter => parameter.Extent > 0))
            {
                throw InvalidEffect(call.Span, "NativeImport");
            }
            return;
        }

        foreach (var parameter in signature.NativeParameters)
        {
            Count(ref remaining, depth + 1);
            if ((uint)parameter.ParameterIndex >= (uint)call.Arguments.Length)
            {
                throw Invalid(call.Span);
            }
            var argument = call.Arguments[parameter.ParameterIndex];
            if (parameter.Extent == 0
                || DirectNativeImportContract.IsStringPointerArgument(
                    argument, parameter, program))
            {
                continue;
            }

            var stagedIndex = facts.Staged.FindLastIndex(staged =>
                ReferenceEquals(staged.Call, call)
                && staged.ParameterIndex == parameter.ParameterIndex);
            if (stagedIndex < 0)
            {
                throw InvalidEffect(argument.Span, "NativeImport");
            }

            VerifyNativeParameterExtentFact(
                facts.Staged[stagedIndex].NativePointerExtent,
                argument.Span, parameter, call.IsUnsafeAuthorized,
                facts, function, program);
        }
    }

    private static void VerifyNativeParameterExtentFact(
        NativePointerExtentFact extentFact, TextSpan span,
        DirectNativeParameter parameter, bool isUnsafeAuthorized,
        BackingDependencies facts, BoundFunction function, BoundProgram program)
    {
        if (extentFact.IsUnavailable
            || (extentFact.Sources.Count == 0 && !extentFact.MayBeOpaque))
        {
            throw InvalidEffect(span, "NativeImport");
        }

        foreach (var source in extentFact.Sources)
        {
            if (!TryGetNativePointerElementExtent(source, parameter.PointeeType,
                    facts, function, program, out var availableElements)
                || availableElements < parameter.Extent)
            {
                throw InvalidEffect(span, "NativeImport");
            }
        }

        if (extentFact.MayBeOpaque && !isUnsafeAuthorized)
        {
            throw InvalidEffect(span, "UnsafeCaller");
        }
    }

    private static bool TryGetNativePointerElementExtent(ProvenanceAnchor source,
        PrimitiveType pointeeType, BackingDependencies facts, BoundFunction function,
        BoundProgram program, out ulong availableElements)
    {
        availableElements = 0;
        if (source.Kind != ProvenanceAnchorKind.NativeStorage
            || (uint)source.Identity >= (uint)function.Variables.Length
            || source.Storage is not StorageKey storage
            || !storage.IsKnown
            || storage.Activation != facts.FunctionActivation
            || storage.Site != source.Identity)
        {
            return false;
        }

        var storageInstances = facts.StorageInstanceSet(source.Identity);
        if (HasUnknownStorageInstances(storageInstances)
            || !storageInstances.Contains(storage.Instance))
        {
            return false;
        }

        var variable = function.Variables[source.Identity];
        if (variable.NativeFixedArrayLayout is not BoundNativeFixedArrayLayout layout)
        {
            if (storage.Projection.Kind != StorageProjectionKind.WholeObject
                || variable.Type != pointeeType)
            {
                return false;
            }

            availableElements = 1;
            return true;
        }

        if (!NativeFixedArrayContract.MatchesLayout(layout, program)
            || layout.ElementCount <= 0 || layout.ElementType != pointeeType)
        {
            return false;
        }

        switch (storage.Projection.Kind)
        {
            case StorageProjectionKind.ArrayElement:
                if (storage.Projection.Start >= layout.ElementCount)
                {
                    return false;
                }
                availableElements = layout.ElementCount - storage.Projection.Start;
                return true;
            case StorageProjectionKind.UnknownArrayElement:
                availableElements = 1;
                return true;
            case StorageProjectionKind.ElementRange:
                if (storage.Projection.Length == 0
                    || storage.Projection.Start > layout.ElementCount
                    || storage.Projection.Length
                        > layout.ElementCount - storage.Projection.Start)
                {
                    return false;
                }
                availableElements = storage.Projection.Length;
                return true;
            default:
                return false;
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
            case BoundNativeFixedArrayElement element
                when IsNativePointerValue(element.Type, program):
                return !facts.TryGetNativePointerFieldValue(
                        element.Variable,
                        StorageProjection.ForArrayElement(element.Index),
                        out _, out var elementMayBeUnknown)
                    || elementMayBeUnknown;
            case BoundRead read when (uint)read.Variable < (uint)facts.NativePointerMayBeUnknown.Length:
                return facts.NativePointerMayBeUnknown[read.Variable]
                    || !NativePointerStorageInstancesMatch(read.Variable, facts);
            case BoundMove move when (uint)move.Variable < (uint)facts.NativePointerMayBeUnknown.Length:
                return facts.NativePointerMayBeUnknown[move.Variable]
                    || !NativePointerStorageInstancesMatch(move.Variable, facts);
            case BoundFieldMove move when facts.TryGetNativePointerCallResult(
                move, StorageProjection.WholeObject, out _, out var movedMayBeUnknown):
                return movedMayBeUnknown;
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
                        || target.Storage is not StorageKey targetStorage
                        || !targetStorage.IsKnown
                        || targetStorage.Activation != facts.FunctionActivation
                        || !facts.StorageInstanceSet(target.Identity).Contains(targetStorage.Instance))
                    {
                        return true;
                    }

                    if (targetStorage.Projection.Kind
                            == StorageProjectionKind.WholeObject
                        && facts.Function.Variables[target.Identity].Type == read.Type)
                    {
                        if (facts.NativePointerMayBeUnknown[target.Identity]
                            || !NativePointerStorageInstancesMatch(
                                target.Identity, facts))
                        {
                            return true;
                        }
                    }
                    else if (!IsNativePointerFieldProjection(
                            targetStorage.Projection)
                        || !facts.TryGetNativePointerFieldValue(
                            target.Identity, targetStorage.Projection,
                            out _, out var fieldMayBeUnknown)
                        || fieldMayBeUnknown)
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
                if (facts.TryGetNativePointerCallResult(call,
                    StorageProjection.WholeObject, out _, out var resultMayBeUnknown))
                {
                    return resultMayBeUnknown;
                }
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
        => TokenSpanContract.IsSnapshot(type, program.Structures);

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

    private static HashSet<ProvenanceAnchor> BackingSourcesOf(
        int variable, PrimitiveType type, BackingDependencies facts,
        BoundProgram program, TextSpan span)
    {
        if (facts.BackingMayBeUnknown[variable]
            && facts.BackingBounds[variable] is not { Count: > 0 }
            && (TokenSpanContract.IsDependent(type, program.Structures)
                || IsTokenSnapshotType(type, program)))
        {
            throw InvalidEffect(span, "Ownership");
        }
        return IsTokenSnapshotType(type, program)
            ? TokenSnapshotBackingOf(variable, facts)
            : facts.Sources[variable] ?? EmptyBackingSources;
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
        if (facts.Staged.Any(staged => staged.ReferenceTargets.Any(
                target => IsReferenceStorageInstance(target, owner, ownerInstances,
                    facts.FunctionActivation))))
        {
            throw InvalidEffect(span, "CheckedReference");
        }
        if (facts.Staged.Any(staged => staged.ReferenceObligations.Any(
                obligation => IsReferenceDescriptorInstance(obligation, owner,
                    ownerInstances, facts.FunctionActivation))))
        {
            throw InvalidEffect(span, "CheckedReference");
        }
        if (facts.Staged.Any(staged => staged.ReferenceFieldValues.Values.Any(value =>
                value.Targets.Any(target => IsReferenceStorageInstance(target,
                    owner, ownerInstances, facts.FunctionActivation))
                || value.Obligations.Any(obligation =>
                    IsReferenceDescriptorInstance(obligation, owner,
                        ownerInstances, facts.FunctionActivation))
                || value.BackingSources.Any(source =>
                    source.Kind == ProvenanceAnchorKind.BackingSource
                    && source.Identity == owner))))
        {
            throw InvalidEffect(span, "CheckedReference");
        }
        for (var slot = 0; slot < state.Length; slot++)
        {
            if ((state.Possibilities(slot) & (2 | 8)) != 0
                && facts.NativePointerSources[slot]?.Any(
                    source => IsNativeStorageInstance(source, owner, ownerInstances,
                        facts.FunctionActivation)) == true)
            {
                throw InvalidEffect(span, "NativePointer");
            }
            if (slot != owner && (state.Possibilities(slot) & (2 | 8)) != 0
                && HasActiveStorageInstance(slot, facts, state)
                && facts.ReferenceTargets[slot]?.Any(
                    target => IsReferenceStorageInstance(target, owner, ownerInstances,
                        facts.FunctionActivation)) == true)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            if (slot != owner && (state.Possibilities(slot) & (2 | 8)) != 0
                && HasActiveStorageInstance(slot, facts, state)
                && facts.ReferenceObligations[slot]?.Any(
                    obligation => IsReferenceDescriptorInstance(obligation,
                        owner, ownerInstances, facts.FunctionActivation)) == true)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
        foreach (var (key, value) in facts.ReferenceFieldValues)
        {
            if (key.Site == owner || (uint)key.Site >= (uint)state.Length
                || (state.Possibilities(key.Site) & (2 | 8)) == 0
                || !HasActiveStorageInstance(key.Site, facts, state))
            {
                continue;
            }
            if (value.Targets.Any(target => IsReferenceStorageInstance(target,
                    owner, ownerInstances, facts.FunctionActivation))
                || value.Obligations.Any(obligation =>
                    IsReferenceDescriptorInstance(obligation, owner,
                        ownerInstances, facts.FunctionActivation))
                || value.BackingSources.Any(source =>
                    source.Kind == ProvenanceAnchorKind.BackingSource
                    && source.Identity == owner))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
        if (facts.IncomingReferenceFieldValues.Values.Any(value =>
                value.Targets.Any(target => IsReferenceStorageInstance(
                    target, owner, ownerInstances, facts.FunctionActivation))
                || value.Obligations.Any(obligation =>
                    IsReferenceDescriptorInstance(obligation, owner,
                        ownerInstances, facts.FunctionActivation))
                || value.BackingSources.Any(source =>
                    source.Kind == ProvenanceAnchorKind.BackingSource
                    && source.Identity == owner)))
        {
            throw InvalidEffect(span, "CheckedReference");
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

    private static bool IsReferenceStorageInstance(ProvenanceAnchor target, int site,
        HashSet<StorageInstance> instances, int activation)
        => target.Kind == ProvenanceAnchorKind.ReferencePlace
            && target.Identity == site
            && (HasUnknownStorageInstances(instances)
                || target.Storage is not StorageKey key || !key.IsKnown
                || key.Activation != activation
                || instances.Contains(key.Instance));

    private static bool IsReferenceDescriptorInstance(ProvenanceAnchor obligation,
        int site, HashSet<StorageInstance> instances, int activation)
        => obligation.Kind == ProvenanceAnchorKind.ReferenceDescriptor
            && obligation.Identity == site
            && (HasUnknownStorageInstances(instances)
                || obligation.Storage is not StorageKey key || !key.IsKnown
                || key.Activation != activation
                || instances.Contains(key.Instance));

    private static bool HasActiveStorageInstance(int slot,
        BackingDependencies facts, InitializationState state)
        => facts.StorageInstanceSet(slot).Any(instance =>
            !instance.IsKnown
            || state.IsStorageScopeActive(instance.SupportingScopeActivation));

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
                && (state.Possibilities(slot) & (2 | 8)) != 0
                && facts.NativePointerSources[slot] is { Count: > 0 })
            {
                throw InvalidEffect(span, "NativePointer");
            }
        }
    }

    private static void CheckReferenceReturn(BoundExpression? expression,
        BoundFunction function, InitializationState state, uint[] fieldMasks,
        BoundProgram program, TextSpan span, ref int remaining)
    {
        if (expression is null)
        {
            return;
        }
        if (state.Dependencies is not BackingDependencies facts)
        {
            if (CheckedReferenceTypeContract.IsValidType(
                function.ReturnType, program.CheckedReferenceTypes)
                || ReferenceFieldLeaves(function.ReturnType, program,
                    ref remaining, 0).Count != 0)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            return;
        }
        if (!CheckedReferenceTypeContract.IsValidType(
            function.ReturnType, program.CheckedReferenceTypes))
        {
            var leaves = ReferenceFieldLeaves(function.ReturnType, program,
                ref remaining, 0);
            if (leaves.Count == 0)
            {
                return;
            }
            var values = ReferenceFieldValuesOfExpression(expression,
                function.ReturnType, facts, state, function, program,
                ref remaining, 0, fieldMasks);
            foreach (var leaf in leaves)
            {
                if (!values.TryGetValue(leaf.Projection, out var value)
                    || !value.IsAvailable
                    || !CheckedReferenceTypeContract.IsValidType(
                        leaf.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                var descriptorType = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(leaf.Type)];
                if (value.Targets.Count == 0 && !descriptorType.IsOptional)
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                foreach (var target in value.Targets)
                {
                    ValidateReferenceTarget(target, descriptorType.ReferentType,
                        facts, state, function, program, span, fieldMasks);
                    if (target.Kind is not (ProvenanceAnchorKind.IncomingReference
                        or ProvenanceAnchorKind.IncomingReferenceContents
                        or ProvenanceAnchorKind.IncomingReferenceField
                        or ProvenanceAnchorKind.IncomingReferencePlace)
                        || !AggregateReferenceReturnDependsOnFormal(
                            function, target.Identity))
                    {
                        throw InvalidEffect(span, "CheckedReference");
                    }
                }
                foreach (var obligation in value.Obligations)
                {
                    ValidateReferenceObligation(obligation, facts, state,
                        function, program, span, fieldMasks);
                    if (obligation.Kind !=
                            ProvenanceAnchorKind.IncomingReferenceDescriptor
                        || !AggregateReferenceReturnDependsOnFormal(
                            function, obligation.Identity))
                    {
                        throw InvalidEffect(span, "CheckedReference");
                    }
                }
            }
            return;
        }
        var targets = ReferenceTargetsOfExpression(expression, facts, state,
            function, program, ref remaining, 0, fieldMasks);
        var obligations = ReferenceObligationsOfExpression(expression, facts, state,
            function, program, ref remaining, 0, fieldMasks);
        foreach (var target in targets)
        {
            if (target.Kind == ProvenanceAnchorKind.ReferencePlace)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            if (target.Kind is not (ProvenanceAnchorKind.IncomingReference
                    or ProvenanceAnchorKind.IncomingReferenceContents
                    or ProvenanceAnchorKind.IncomingReferenceField
                    or ProvenanceAnchorKind.IncomingReferencePlace)
                || !ReferenceReturnDependsOnFormal(function, target.Identity,
                    requiresSourceStorage: target.Kind is
                        ProvenanceAnchorKind.IncomingReference
                            or ProvenanceAnchorKind.IncomingReferencePlace))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
        foreach (var obligation in obligations)
        {
            if (obligation.Kind != ProvenanceAnchorKind.IncomingReferenceDescriptor
                || !ReferenceReturnDependsOnFormal(function, obligation.Identity))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
    }

    private static bool AggregateReferenceReturnDependsOnFormal(
        BoundFunction function, int parameterSlot)
    {
        if (!function.IsConstructor)
        {
            return ReferenceReturnDependsOnFormal(function, parameterSlot);
        }

        var formalOrdinal = parameterSlot - 1;
        var capture = ReceiverCaptureContract(function);
        return formalOrdinal >= 0 && capture is not null
            && capture.Sources.Any(source =>
                source.Kind == BoundDependencySourceKind.Formal
                && source.FormalOrdinal == formalOrdinal);
    }

    private static bool ReferenceReturnDependsOnFormal(BoundFunction function,
        int parameterSlot, bool requiresSourceStorage = false)
    {
        var dependency = ResultDependencyContract(function);
        if (dependency is null)
        {
            return false;
        }
        var receiverOffset = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor && !function.IsClassConstructor
                ? 1 : 0;
        BoundDependencySource source;
        if (receiverOffset != 0 && parameterSlot == 0)
        {
            source = new BoundDependencySource(
                BoundDependencySourceKind.Receiver, null);
        }
        else
        {
            var formalOrdinal = parameterSlot - receiverOffset;
            if (formalOrdinal < 0)
            {
                return false;
            }
            source = new BoundDependencySource(
                BoundDependencySourceKind.Formal, formalOrdinal);
        }

        if (!dependency.Sources.Contains(source))
        {
            return false;
        }
        return !requiresSourceStorage
            || !IsForwardedBackingResultSource(function, source);
    }

    private static bool IsForwardedBackingResultSource(
        BoundFunction function, BoundDependencySource source)
        => function.DependencyContracts.Any(contract =>
            contract.Relation == BoundDependencyRelation.ForwardsBacking
            && contract.Destination == BoundDependencyDestination.Result
            && contract.Sources.Contains(source));

    private static HashSet<ProvenanceAnchor> ReferenceTargetsSummary(
        BoundExpression expression, BackingDependencies facts, BoundFunction function,
        BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is null
            || !CheckedReferenceTypeContract.IsValidType(
                expression.Type, program.CheckedReferenceTypes))
        {
            throw InvalidEffect(expression?.Span ?? default, "CheckedReference");
        }

        var type = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(expression.Type)];
        var result = new HashSet<ProvenanceAnchor>();
        switch (expression)
        {
            case BoundNull when type.IsOptional:
                break;
            case BoundOptionalLift lift when type.IsOptional:
                if (!SourceOptionalTypes.TryGetRequired(
                    expression.Type, program.CheckedReferenceTypes,
                    out var liftedType) || lift.Value.Type != liftedType)
                {
                    throw InvalidEffect(lift.Span, "CheckedReference");
                }
                result = ReferenceTargetsSummary(lift.Value, facts,
                    function, program, ref remaining, depth + 1);
                break;
            case BoundReferenceFormation formation:
                if (formation.Type != expression.Type
                    || type.IsGarbageCollected || type.IsInitialization || type.IsOptional
                    || formation.Place.Type != type.ReferentType)
                {
                    throw InvalidEffect(formation.Span, "CheckedReference");
                }

                if (TryGetIncomingReferencePlace(
                    formation.Place, function, program, ref remaining,
                    depth + 1, out var incomingPlace, out var incomingWritable))
                {
                    if (type.IsWritable && !incomingWritable)
                    {
                        throw InvalidEffect(formation.Span, "CheckedReference");
                    }
                    _ = result.Add(incomingPlace);
                }
                else if (TryGetReferencePlaceStorage(formation.Place, function,
                    program, out var placeVariable, out var placeProjection))
                {
                    if (type.IsWritable && !IsWritableReferencePlace(
                        formation.Place, function, program))
                    {
                        throw InvalidEffect(formation.Span, "CheckedReference");
                    }
                    _ = result.Add(ProvenanceAnchor.ForReferencePlace(
                        facts.FunctionActivation, placeVariable,
                        new StorageInstance(BackingDependencies.RootScopeActivation, 0),
                        placeProjection));
                }
                else if (TryGetNormalBackingClassPlace(formation.Place,
                    function, program, out var normalBackingVariable,
                    out var normalBackingProjection))
                {
                    if (type.IsWritable && !IsWritableReferencePlace(
                        formation.Place, function, program))
                    {
                        throw InvalidEffect(formation.Span, "CheckedReference");
                    }
                    _ = result.Add(ProvenanceAnchor.ForNormalBackingPlace(
                        facts.FunctionActivation, normalBackingVariable,
                        new StorageInstance(BackingDependencies.RootScopeActivation, 0),
                        normalBackingProjection, placeGeneration: 0));
                }
                else
                {
                    throw InvalidEffect(formation.Span, "CheckedReference");
                }
                break;
            case BoundReferenceReborrow reborrow:
                if (!CheckedReferenceTypeContract.IsValidType(
                        reborrow.Descriptor.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(reborrow.Span, "CheckedReference");
                }
                var sourceReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(reborrow.Descriptor.Type)];
                if (reborrow.Type != expression.Type
                    || sourceReference.IsGarbageCollected || sourceReference.IsInitialization
                    || sourceReference.IsOptional || type.IsGarbageCollected
                    || type.IsInitialization || type.IsOptional
                    || sourceReference.ReferentType != type.ReferentType
                    || (type.IsWritable && !sourceReference.IsWritable))
                {
                    throw InvalidEffect(reborrow.Span, "CheckedReference");
                }
                result = ReferenceTargetsSummary(reborrow.Descriptor, facts,
                    function, program, ref remaining, depth + 1);
                break;
            case BoundFieldRead field:
                if (field.Type != expression.Type)
                {
                    throw InvalidEffect(field.Span, "CheckedReference");
                }
                if (TryGetReferencePlaceStorage(field, function, program,
                    out var fieldVariable, out var fieldProjection)
                    && facts.TryGetReferenceFieldValue(
                        fieldVariable, fieldProjection, out var fieldValue)
                    && fieldValue.IsAvailable)
                {
                    result.UnionWith(fieldValue.Targets);
                }
                break;
            case BoundRead read:
                if ((uint)read.Variable >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != expression.Type)
                {
                    throw InvalidEffect(read.Span, "CheckedReference");
                }
                if (facts.ReferenceTargets[read.Variable] is { Count: > 0 } targets)
                {
                    result.UnionWith(targets);
                }
                break;
            case BoundReferenceRead referenceRead:
                if (!CheckedReferenceTypeContract.IsValidType(
                        referenceRead.Descriptor.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(referenceRead.Span, "CheckedReference");
                }
                var descriptorType = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(referenceRead.Descriptor.Type)];
                if (descriptorType.IsGarbageCollected || descriptorType.IsInitialization
                    || descriptorType.IsOptional || descriptorType.ReferentType != expression.Type)
                {
                    throw InvalidEffect(referenceRead.Span, "CheckedReference");
                }
                foreach (var target in ReferenceTargetsSummary(
                    referenceRead.Descriptor, facts, function, program,
                    ref remaining, depth + 1))
                {
                    if (target.Kind == ProvenanceAnchorKind.IncomingReference)
                    {
                        if (CheckedReferenceTypeContract.IsValidType(
                                expression.Type, program.CheckedReferenceTypes))
                        {
                            if (!IsOrdinaryStoredReferenceType(expression.Type, program))
                            {
                                result.Clear();
                                break;
                            }
                            _ = result.Add(ProvenanceAnchor.ForIncomingReferenceContents(
                                target.Identity, depth: 1));
                        }
                        else
                        {
                            _ = result.Add(target);
                        }
                        continue;
                    }
                    if (target.Kind == ProvenanceAnchorKind.IncomingReferenceContents)
                    {
                        if (CheckedReferenceTypeContract.IsValidType(
                                expression.Type, program.CheckedReferenceTypes))
                        {
                            if (!IsOrdinaryStoredReferenceType(expression.Type, program)
                                || target.IncomingReferenceDepth >= MaximumDepth)
                            {
                                result.Clear();
                                break;
                            }
                            _ = result.Add(ProvenanceAnchor.ForIncomingReferenceContents(
                                target.Identity, target.IncomingReferenceDepth + 1));
                        }
                        else
                        {
                            _ = result.Add(target);
                        }
                        continue;
                    }
                    if (target.Kind != ProvenanceAnchorKind.ReferencePlace
                        || (uint)target.Identity >= (uint)function.Variables.Length
                        || function.Variables[target.Identity].Type != expression.Type
                        || facts.ReferenceTargets[target.Identity] is not { Count: > 0 } nestedTargets)
                    {
                        result.Clear();
                        break;
                    }
                    result.UnionWith(nestedTargets);
                }
                break;
            case BoundCall call when facts.TryGetReferenceFieldCallResult(
                call, StorageProjection.WholeObject, out var callValue):
                if (callValue.IsAvailable)
                {
                    result.UnionWith(callValue.Targets);
                }
                break;
            case BoundCoalesce coalesce:
                result.UnionWith(ReferenceTargetsSummary(coalesce.Optional,
                    facts, function, program, ref remaining, depth + 1));
                result.UnionWith(ReferenceTargetsSummary(coalesce.Fallback,
                    facts, function, program, ref remaining, depth + 1));
                break;
            case BoundConditional conditional:
                if (conditional.Type != expression.Type)
                {
                    throw InvalidEffect(conditional.Span, "CheckedReference");
                }
                var whenTrue = ReferenceTargetsSummary(conditional.WhenTrue,
                    facts, function, program, ref remaining, depth + 1);
                var whenFalse = ReferenceTargetsSummary(conditional.WhenFalse,
                    facts, function, program, ref remaining, depth + 1);
                if (type.IsOptional || (whenTrue.Count != 0 && whenFalse.Count != 0))
                {
                    result.UnionWith(whenTrue);
                    result.UnionWith(whenFalse);
                }
                break;
            case BoundUnsafeExpression unsafeExpression:
                result = ReferenceTargetsSummary(unsafeExpression.Expression,
                    facts, function, program, ref remaining, depth + 1);
                break;
            default:
                break;
        }
        return result;
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
            if (scopedVariables.Contains(slot)
                || (state.Possibilities(slot) & (2 | 8)) == 0)
            {
                continue;
            }

            if (facts.NativePointerSources[slot]?.Any(source =>
                    source.Kind == ProvenanceAnchorKind.NativeStorage
                    && scopedVariables.Contains(source.Identity)) == true)
            {
                throw InvalidEffect(block.Span, "NativePointer");
            }
            if (facts.ReferenceTargets[slot]?.Any(source =>
                    source.Kind == ProvenanceAnchorKind.ReferencePlace
                    && scopedVariables.Contains(source.Identity)) == true)
            {
                throw InvalidEffect(block.Span, "CheckedReference");
            }
            if (facts.ReferenceObligations[slot]?.Any(obligation =>
                    obligation.Kind == ProvenanceAnchorKind.ReferenceDescriptor
                    && scopedVariables.Contains(obligation.Identity)) == true)
            {
                throw InvalidEffect(block.Span, "CheckedReference");
            }
            if (facts.ReferenceBackingSources[slot]?.Any(source =>
                    source.Kind == ProvenanceAnchorKind.BackingSource
                    && scopedVariables.Contains(source.Identity)) == true)
            {
                throw InvalidEffect(block.Span, "Ownership");
            }
        }
        foreach (var (key, value) in facts.ReferenceFieldValues)
        {
            if (scopedVariables.Contains(key.Site)
                || (uint)key.Site >= (uint)state.Length
                || (state.Possibilities(key.Site) & (2 | 8)) == 0)
            {
                continue;
            }
            if (value.Targets.Any(source =>
                    source.Kind == ProvenanceAnchorKind.ReferencePlace
                    && scopedVariables.Contains(source.Identity))
                || value.Obligations.Any(obligation =>
                    obligation.Kind == ProvenanceAnchorKind.ReferenceDescriptor
                    && scopedVariables.Contains(obligation.Identity)))
            {
                throw InvalidEffect(block.Span, "CheckedReference");
            }
            if (value.BackingSources.Any(source =>
                    source.Kind == ProvenanceAnchorKind.BackingSource
                    && scopedVariables.Contains(source.Identity)))
            {
                throw InvalidEffect(block.Span, "Ownership");
            }
        }
        foreach (var value in facts.NormalBackingReferenceFieldValuesSnapshot())
        {
            if (value.Targets.Any(source =>
                    source.Kind == ProvenanceAnchorKind.ReferencePlace
                    && scopedVariables.Contains(source.Identity))
                || value.Obligations.Any(obligation =>
                    obligation.Kind == ProvenanceAnchorKind.ReferenceDescriptor
                    && scopedVariables.Contains(obligation.Identity)))
            {
                throw InvalidEffect(block.Span, "CheckedReference");
            }
            if (value.BackingSources.Any(source =>
                    source.Kind == ProvenanceAnchorKind.BackingSource
                    && scopedVariables.Contains(source.Identity)))
            {
                throw InvalidEffect(block.Span, "Ownership");
            }
        }
        foreach (var value in facts.IncomingReferenceFieldValues.Values)
        {
            if (value.Targets.Any(source =>
                    source.Kind == ProvenanceAnchorKind.ReferencePlace
                    && scopedVariables.Contains(source.Identity))
                || value.Obligations.Any(obligation =>
                    obligation.Kind == ProvenanceAnchorKind.ReferenceDescriptor
                    && scopedVariables.Contains(obligation.Identity)))
            {
                throw InvalidEffect(block.Span, "CheckedReference");
            }
            if (value.BackingSources.Any(source =>
                    source.Kind == ProvenanceAnchorKind.BackingSource
                    && scopedVariables.Contains(source.Identity)))
            {
                throw InvalidEffect(block.Span, "Ownership");
            }
        }
        foreach (var staged in facts.Staged)
        {
            if (staged.ReferenceFieldValues.Values.Any(value =>
                    value.Targets.Any(source =>
                        source.Kind == ProvenanceAnchorKind.ReferencePlace
                        && scopedVariables.Contains(source.Identity))
                    || value.Obligations.Any(obligation =>
                        obligation.Kind == ProvenanceAnchorKind.ReferenceDescriptor
                        && scopedVariables.Contains(obligation.Identity))))
            {
                throw InvalidEffect(block.Span, "CheckedReference");
            }
            if (staged.ReferenceFieldValues.Values.Any(value =>
                    value.BackingSources.Any(source =>
                        source.Kind == ProvenanceAnchorKind.BackingSource
                        && scopedVariables.Contains(source.Identity))))
            {
                throw InvalidEffect(block.Span, "Ownership");
            }
        }
    }

    private static bool TryGetReferencePlaceStorage(BoundExpression place,
        BoundFunction function, BoundProgram program, out int variable,
        out StorageProjection projection)
    {
        switch (place)
        {
            case BoundRead read:
                if ((uint)read.Variable >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != read.Type)
                {
                    throw Invalid(read.Span);
                }
                variable = read.Variable;
                projection = StorageProjection.WholeObject;
                return true;
            case BoundNativeFixedArrayElement element:
                if ((uint)element.Variable >= (uint)function.Variables.Length
                    || function.Variables[element.Variable].Type != element.Layout.CarrierType
                    || function.Variables[element.Variable].NativeFixedArrayLayout != element.Layout
                    || !NativeFixedArrayContract.MatchesLayout(element.Layout, program)
                    || element.Type != element.Layout.ElementType)
                {
                    throw Invalid(element.Span);
                }
                variable = element.Variable;
                projection = StorageProjection.ForArrayElement(element.Index);
                return true;
            case BoundFieldRead field:
                if (!IsReferenceAddressablePlace(field)
                    || !IsReferenceAddressableFieldPath(field, function, program)
                    || !TryGetNativePointerFieldPlace(field, function, program,
                    out variable, out projection))
                {
                    variable = -1;
                    projection = default;
                    return false;
                }
                var root = function.Variables[variable];
                if (root.IsOut || root.IsInit || root.IsInitializationLocal)
                {
                    variable = -1;
                    projection = default;
                    return false;
                }
                if (root.NativeFixedArrayLayout is BoundNativeFixedArrayLayout layout)
                {
                    if (!NativeFixedArrayContract.MatchesLayout(layout, program))
                    {
                        throw Invalid(field.Span);
                    }
                    return true;
                }
                var rootTypeIndex = (int)root.Type - 100;
                if (rootTypeIndex < 0 || rootTypeIndex >= program.Structures.Length
                    || ManagedClassTypes.IsClass(root.Type)
                    || (program.Structures[rootTypeIndex].CleanupMethod is not null
                        && !program.Structures[rootTypeIndex].IsUnique))
                {
                    variable = -1;
                    projection = default;
                    return false;
                }
                return true;
            default:
                variable = -1;
                projection = default;
                return false;
        }
    }

    private static bool TryGetNormalBackingClassPlace(BoundExpression place,
        BoundFunction function, BoundProgram program, out int variable,
        out StorageProjection projection)
    {
        variable = -1;
        projection = default;
        if (program.NativeLoweringAgreement
                != CompilationOptions.WindowsX64NormalBackingAgreement
            || place is not BoundFieldRead field
            || !TryGetFieldProjectionPath(field, function, program,
                out variable, out var fieldPath))
        {
            return false;
        }

        projection = new StorageProjection(
            StorageProjectionKind.InlineFieldPath, 0, 0, fieldPath);
        if (!TryGetNormalBackingClassPlaceType(variable, projection,
                function, program, out var placeType)
            || placeType != place.Type
            || !CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                place.Type, program.Structures, program.Classes,
                program.EnumDefinitions.Length, program.CheckedReferenceTypes))
        {
            variable = -1;
            projection = default;
            return false;
        }
        return true;
    }

    private static bool TryGetNormalBackingClassPlaceType(int variable,
        StorageProjection projection, BoundFunction function,
        BoundProgram program, out PrimitiveType type)
    {
        type = PrimitiveType.Int32;
        if (program.NativeLoweringAgreement
                != CompilationOptions.WindowsX64NormalBackingAgreement
            || (uint)variable >= (uint)function.Variables.Length
            || projection.Kind != StorageProjectionKind.InlineFieldPath
            || projection.FieldPath.Segments.IsEmpty)
        {
            return false;
        }

        type = function.Variables[variable].Type;
        var hasClassBacking = false;
        foreach (var segment in projection.FieldPath.Segments)
        {
            if (type != segment.OwnerType || !HasFields(program, type)
                || (uint)segment.Field >= (uint)Fields(program, type).Length)
            {
                return false;
            }
            hasClassBacking |= ManagedClassTypes.IsClass(type);
            type = Fields(program, type)[segment.Field].Type;
        }
        return hasClassBacking;
    }

    private static bool TryGetIncomingReferencePlace(
        BoundExpression place, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, out ProvenanceAnchor target,
        out bool isWritable)
    {
        Count(ref remaining, depth);
        target = default;
        isWritable = false;
        var fields = new Stack<(PrimitiveType OwnerType, int Field)>();
        var root = place;
        while (root is BoundFieldRead field)
        {
            Count(ref remaining, depth + fields.Count + 1);
            fields.Push((field.Receiver.Type, field.Field));
            root = field.Receiver;
        }

        PrimitiveType ownerType;
        int sourceSlot;
        int referenceDepth;
        var projection = StorageProjection.WholeObject;
        if (root is BoundRead receiver
            && function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor
            && !function.IsClassConstructor
            && receiver.Variable == 0
            && (uint)receiver.Variable < (uint)function.ParameterCount
            && (uint)receiver.Variable < (uint)function.Variables.Length
            && function.Variables[receiver.Variable].Type == receiver.Type
            && !ManagedClassTypes.IsClass(receiver.Type)
            && IsInlineStructStorage(receiver.Type, program))
        {
            ownerType = receiver.Type;
            sourceSlot = receiver.Variable;
            referenceDepth = 0;
            isWritable = function.Variables[receiver.Variable].IsMutable;
        }
        else if (root is BoundReferenceRead referenceRead
            && CheckedReferenceTypeContract.IsValidType(
                referenceRead.Descriptor.Type, program.CheckedReferenceTypes)
            && referenceRead.Descriptor is BoundRead descriptor
            && IsReferenceParameterSlot(function, descriptor.Variable)
            && (uint)descriptor.Variable < (uint)function.Variables.Length
            && function.Variables[descriptor.Variable].Type
                == referenceRead.Descriptor.Type
            && !function.Variables[descriptor.Variable].IsOut
            && !function.Variables[descriptor.Variable].IsInit
            && !function.Variables[descriptor.Variable].IsInitializationLocal)
        {
            var reference = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(referenceRead.Descriptor.Type)];
            if (reference.IsGarbageCollected || reference.IsInitialization
                || reference.IsOptional || reference.ReferentType != referenceRead.Type)
            {
                return false;
            }

            ownerType = reference.ReferentType;
            sourceSlot = descriptor.Variable;
            referenceDepth = 1;
            isWritable = reference.IsWritable;
        }
        else
        {
            return false;
        }

        while (fields.TryPop(out var segment))
        {
            Count(ref remaining, depth + 1);
            var structureIndex = (int)ownerType - 100;
            if (ownerType != segment.OwnerType
                || structureIndex < 0 || structureIndex >= program.Structures.Length
                || ManagedClassTypes.IsClass(ownerType)
                || !IsInlineStructStorage(ownerType, program)
                || (program.Structures[structureIndex].CleanupMethod is not null
                    && !program.Structures[structureIndex].IsUnique)
                || (uint)segment.Field >=
                    (uint)program.Structures[structureIndex].Fields.Length
                || !CanAccessField(function, program, ownerType, segment.Field))
            {
                return false;
            }

            var fieldDefinition = program.Structures[structureIndex].Fields[segment.Field];
            var isLeaf = fields.Count == 0;
            var fieldTypeIsSupported = isLeaf
                ? CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                    fieldDefinition.Type, program.Structures, program.Classes,
                    program.EnumDefinitions.Length, program.CheckedReferenceTypes)
                : IsInlineStructStorage(fieldDefinition.Type, program);
            if (!fieldTypeIsSupported)
            {
                return false;
            }

            projection = projection.AppendField(ownerType, segment.Field);
            ownerType = fieldDefinition.Type;
            isWritable &= fieldDefinition.IsMutable;
        }

        if (ownerType != place.Type)
        {
            return false;
        }
        target = ProvenanceAnchor.ForIncomingReferencePlace(
            sourceSlot, projection, referenceDepth);
        return true;
    }

    private static bool TryGetReferencePlaceTarget(
        BoundExpression place, BackingDependencies facts,
        InitializationState state, uint[]? fieldMasks, BoundFunction function,
        BoundProgram program, out ProvenanceAnchor target)
    {
        target = default;
        if (!TryGetNativePointerFieldPlace(place, function, program,
                out var variable, out var projection)
            || !ReferenceTargetPlaceInitialized(variable, projection,
                state, fieldMasks, function, program))
        {
            return false;
        }

        var instances = facts.StorageInstanceSet(variable);
        if (instances.Count != 1)
        {
            return false;
        }
        foreach (var instance in instances)
        {
            if (!instance.IsKnown)
            {
                return false;
            }
            target = ProvenanceAnchor.ForReferencePlace(
                facts.FunctionActivation, variable, instance, projection,
                state.FieldPlaceGeneration(variable, projection));
            return true;
        }
        return false;
    }

    private static bool IsReferenceAddressableFieldPath(BoundFieldRead field,
        BoundFunction function, BoundProgram program, bool isLeaf = true)
    {
        var receiverIndex = (int)field.Receiver.Type - 100;
        if (receiverIndex < 0 || receiverIndex >= program.Structures.Length
            || ManagedClassTypes.IsClass(field.Receiver.Type)
            || (field.Receiver is not BoundRead
                && !program.Structures[receiverIndex].IsUnique
                && program.Structures[receiverIndex].CleanupMethod is not null)
            || (uint)field.Field >= (uint)program.Structures[receiverIndex].Fields.Length)
        {
            return false;
        }
        var definition = program.Structures[receiverIndex].Fields[field.Field];
        var supportedFieldType = isLeaf
            ? CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                field.Type, program.Structures, program.Classes,
                program.EnumDefinitions.Length, program.CheckedReferenceTypes)
            : HasFields(program, field.Type) && !ManagedClassTypes.IsClass(field.Type);
        return definition.Type == field.Type
            && supportedFieldType
            && (field.Receiver is not BoundFieldRead parent
                || IsReferenceAddressableFieldPath(parent, function, program, isLeaf: false))
            && (field.Receiver is not BoundNativeFixedArrayElement element
                || ((uint)element.Variable < (uint)function.Variables.Length
                    && function.Variables[element.Variable].NativeFixedArrayLayout
                        is BoundNativeFixedArrayLayout layout
                    && NativeFixedArrayContract.MatchesLayout(layout, program)));
    }

    private static bool IsReferenceAddressablePlace(BoundExpression place)
        => place switch
        {
            BoundRead or BoundNativeFixedArrayElement => true,
            BoundFieldRead field => IsReferenceAddressablePlace(field.Receiver),
            _ => false,
        };

    private static void ApplyReferenceOwnerStoreCallEffects(BoundCall call,
        BoundFunction callee, InitializationState initialized, uint[] fieldMasks,
        BoundFunction caller, BoundProgram program, ref int remaining, int depth)
    {
        if (initialized.Dependencies is not BackingDependencies facts
            || callee.IsConstructor || callee.IsClassConstructor)
        {
            return;
        }

        var receiverOffset = callee.DeclaringStruct is not null
            && !callee.IsStatic ? 1 : 0;
        var targets = new HashSet<ProvenanceAnchor>();
        for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var parameterIndex = call.ParameterIndex(argumentIndex);
            var parameterSlot = parameterIndex + receiverOffset;
            if ((uint)parameterSlot >= (uint)callee.Variables.Length
                || !CheckedReferenceTypeContract.IsValidType(
                    callee.Variables[parameterSlot].Type, program.CheckedReferenceTypes))
            {
                continue;
            }

            var reference = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(callee.Variables[parameterSlot].Type)];
            if (!reference.IsWritable || reference.IsGarbageCollected
                || reference.IsInitialization || reference.IsOptional
                || !IsUnique(program, reference.ReferentType))
            {
                continue;
            }

            var staged = facts.Staged.LastOrDefault(candidate =>
                ReferenceEquals(candidate.Call, call)
                && candidate.ParameterIndex == parameterIndex);
            if (staged.Call is null || staged.ReferenceTargets.Count == 0)
            {
                throw InvalidEffect(call.Span, "CheckedReference");
            }
            foreach (var target in staged.ReferenceTargets)
            {
                ValidateReferenceTarget(target, reference.ReferentType,
                    facts, initialized, caller, program, call.Span, fieldMasks);
                _ = targets.Add(target);
            }
        }

        foreach (var target in targets)
        {
            if (target.Kind != ProvenanceAnchorKind.ReferencePlace)
            {
                continue;
            }

            var projection = target.Storage?.Projection
                ?? throw Invalid(call.Span);
            var preservedTargets = new HashSet<ProvenanceAnchor> { target };
            if (projection.Kind == StorageProjectionKind.WholeObject)
            {
                CheckReferencePlaceInvalidation(target.Identity, projection,
                    initialized, call.Span, preservedTargets);
                CheckBackingInvalidation(target.Identity, initialized,
                    call.Span, preservedTargets);
            }
            else if (projection.Kind == StorageProjectionKind.InlineFieldPath)
            {
                CheckReferencePlaceInvalidation(target.Identity, projection,
                    initialized, call.Span, preservedTargets);
                CheckBackingFieldInvalidation(target.Identity, projection,
                    initialized, call.Span, preservedTargets);
            }
            else
            {
                throw InvalidEffect(call.Span, "Ownership");
            }
        }
    }

    private static bool IsWritableReferencePlace(BoundExpression place,
        BoundFunction function, BoundProgram program)
    {
        switch (place)
        {
            case BoundRead read:
                return (uint)read.Variable < (uint)function.Variables.Length
                    && function.Variables[read.Variable].Type == read.Type
                    && function.Variables[read.Variable].IsMutable;
            case BoundNativeFixedArrayElement element:
                return (uint)element.Variable < (uint)function.Variables.Length
                    && function.Variables[element.Variable].NativeFixedArrayLayout == element.Layout
                    && function.Variables[element.Variable].IsMutable
                    && NativeFixedArrayContract.AllowsElementWrites(element.Layout);
            case BoundFieldRead field:
                if (ManagedClassTypes.IsClass(field.Receiver.Type))
                {
                    var classIndex = ManagedClassTypes.ClassIndex(field.Receiver.Type);
                    return (uint)classIndex < (uint)program.Classes.Length
                        && (uint)field.Field < (uint)program.Classes[classIndex].Fields.Length
                        && field.Type == program.Classes[classIndex].Fields[field.Field].Type
                        && program.Classes[classIndex].Fields[field.Field].IsMutable
                        && ManagedClassTypes.IsWritable(field.Receiver.Type);
                }
                var ownerIndex = (int)field.Receiver.Type - 100;
                return ownerIndex >= 0 && ownerIndex < program.Structures.Length
                    && (uint)field.Field < (uint)program.Structures[ownerIndex].Fields.Length
                    && field.Type == program.Structures[ownerIndex].Fields[field.Field].Type
                    && program.Structures[ownerIndex].Fields[field.Field].IsMutable
                    && IsWritableReferencePlace(field.Receiver, function, program);
            default:
                return false;
        }
    }

    private static bool ReferenceTargetPlaceInitialized(int variable,
        StorageProjection projection, InitializationState state, uint[]? fieldMasks,
        BoundFunction function, BoundProgram program)
    {
        if ((uint)variable >= (uint)state.Length || state.HasRetiredValue(variable))
        {
            return false;
        }
        if (projection.Kind is StorageProjectionKind.WholeObject
            or StorageProjectionKind.ArrayElement
            or StorageProjectionKind.UnknownArrayElement)
        {
            return state[variable];
        }
        if (projection.Kind != StorageProjectionKind.InlineFieldPath
            || projection.FieldPath.Segments.IsEmpty)
        {
            return false;
        }
        if (fieldMasks is null)
        {
            return state[variable];
        }
        if ((uint)variable >= (uint)fieldMasks.Length)
        {
            return false;
        }
        var rootType = function.Variables[variable].Type;
        if (rootType != projection.FieldPath.Segments[0].OwnerType)
        {
            return false;
        }
        var segments = projection.FieldPath.Segments;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var ownerPath = FieldPathPrefix(projection.FieldPath, index);
            var (definite, possible) = FieldPathOwnerMasks(variable,
                ownerPath, segment.OwnerType, state, fieldMasks, program);
            var bit = 1u << segment.Field;
            if (index == segments.Length - 1
                ? (definite & bit) == 0
                : (possible & bit) == 0)
            {
                return false;
            }
        }
        if (!TryGetReferencePlaceType(
                variable, projection, function, program, out var referentType))
        {
            return false;
        }
        return !IsUnique(program, referentType) || !HasFields(program, referentType)
            || state.NestedOwnerFields(
                variable, projection.FieldPath, referentType).IsComplete;
    }

    private static Dictionary<StorageProjection, ReferenceFieldValue>
        ReferenceFieldValuesOfExpression(BoundExpression expression, PrimitiveType type,
            BackingDependencies facts, InitializationState state,
            BoundFunction function, BoundProgram program, ref int remaining, int depth,
            uint[]? fieldMasks = null)
    {
        Count(ref remaining, depth);
        if (expression is null || expression.Type != type)
        {
            throw Invalid(expression?.Span ?? default);
        }

        var result = new Dictionary<StorageProjection, ReferenceFieldValue>();
        foreach (var leaf in ReferenceFieldLeaves(type, program, ref remaining, depth + 1))
        {
            Count(ref remaining, depth + 1);
            if (!result.TryAdd(leaf.Projection,
                ReferenceFieldValueOfExpression(expression, type, leaf.Projection,
                    facts, state, function, program, ref remaining, depth + 1,
                    fieldMasks)))
            {
                throw Invalid(expression.Span);
            }
        }
        return result;
    }

    private static HashSet<ProvenanceAnchor> NormalBackingObjectsOfExpression(
        BoundExpression expression, BackingDependencies facts,
        InitializationState state, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, out bool mayBeUnknown)
    {
        Count(ref remaining, depth);
        mayBeUnknown = false;
        if (program.NativeLoweringAgreement
                != CompilationOptions.WindowsX64NormalBackingAgreement
            || !ManagedClassTypes.IsManaged(expression.Type))
        {
            mayBeUnknown = true;
            return [];
        }

        switch (expression)
        {
            case BoundRead read:
                if ((uint)read.Variable >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != expression.Type
                    || !state[read.Variable] || state.HasRetiredValue(read.Variable))
                {
                    mayBeUnknown = true;
                    return [];
                }
                mayBeUnknown = facts.NormalBackingObjectsMayBeUnknownForSlot(
                    read.Variable);
                return [.. facts.NormalBackingObjectsForSlot(read.Variable)];
            case BoundMove move:
                if ((uint)move.Variable >= (uint)function.Variables.Length
                    || function.Variables[move.Variable].Type != expression.Type)
                {
                    mayBeUnknown = true;
                    return [];
                }
                mayBeUnknown = facts.NormalBackingObjectsMayBeUnknownForSlot(
                    move.Variable);
                return [.. facts.NormalBackingObjectsForSlot(move.Variable)];
            case BoundConditional conditional:
                var whenTrue = NormalBackingObjectsOfExpression(
                    conditional.WhenTrue, facts, state, function, program,
                    ref remaining, depth + 1, out var trueUnknown);
                var whenFalse = NormalBackingObjectsOfExpression(
                    conditional.WhenFalse, facts, state, function, program,
                    ref remaining, depth + 1, out var falseUnknown);
                whenTrue.UnionWith(whenFalse);
                mayBeUnknown = trueUnknown || falseUnknown;
                return whenTrue;
            case BoundFieldRead field when IsNormalBackingObjectType(field.Type)
                && IsNormalBackingObjectType(field.Receiver.Type):
                var receiverObjects = NormalBackingObjectsOfExpression(
                    field.Receiver, facts, state, function, program,
                    ref remaining, depth + 1, out var receiverUnknown);
                var projection = StorageProjection.WholeObject.AppendField(
                    field.Receiver.Type, field.Field);
                if (receiverUnknown
                    || !facts.TryGetNormalBackingClassFieldValue(
                        receiverObjects, projection, out var fieldObjects,
                        out mayBeUnknown))
                {
                    mayBeUnknown = true;
                    return [];
                }
                return fieldObjects;
            case BoundFieldRead:
                mayBeUnknown = true;
                return [];
            case BoundNull when ManagedClassTypes.IsOptional(expression.Type):
                return [];
            default:
                mayBeUnknown = true;
                return [];
        }
    }

    private static bool IsNormalBackingObjectType(PrimitiveType type) =>
        ManagedClassTypes.IsClass(type)
        || (ManagedClassTypes.IsOptional(type)
            && ManagedClassTypes.IsClass(ManagedClassTypes.Required(type)));

    private static ReferenceFieldValue NormalBackingReferenceFieldValueOfExpression(
        BoundExpression expression, StorageProjection projection,
        BackingDependencies facts, InitializationState state,
        BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        var objects = NormalBackingObjectsOfExpression(expression, facts, state,
            function, program, ref remaining, depth + 1, out var mayBeUnknown);
        if (mayBeUnknown
            || !facts.TryGetNormalBackingReferenceFieldValue(
                objects, projection, out var value))
        {
            return ReferenceFieldValue.Unavailable();
        }
        return value;
    }

    private static ReferenceFieldValue ReferenceFieldValueOfExpression(
        BoundExpression expression, PrimitiveType valueType,
        StorageProjection relativeProjection, BackingDependencies facts,
        InitializationState state, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, uint[]? fieldMasks)
    {
        Count(ref remaining, depth);
        if (expression is null || expression.Type != valueType)
        {
            throw Invalid(expression?.Span ?? default);
        }
        if (CheckedReferenceTypeContract.IsValidType(
                valueType, program.CheckedReferenceTypes))
        {
            return relativeProjection.Kind == StorageProjectionKind.WholeObject
                && IsOrdinaryStoredReferenceType(valueType, program)
                    ? ReferenceFieldValueOfDescriptorExpression(expression,
                        facts, state, function, program, ref remaining,
                        depth + 1, fieldMasks)
                    : ReferenceFieldValue.Unavailable();
        }
        if (program.NativeLoweringAgreement
                == CompilationOptions.WindowsX64NormalBackingAgreement
            && IsNormalBackingObjectType(valueType)
            && relativeProjection.Kind == StorageProjectionKind.InlineFieldPath
            && !relativeProjection.FieldPath.Segments.IsEmpty)
        {
            return NormalBackingReferenceFieldValueOfExpression(expression,
                relativeProjection, facts, state, function, program,
                ref remaining, depth + 1);
        }
        if (!IsInlineStructStorage(valueType, program)
            || relativeProjection.Kind != StorageProjectionKind.InlineFieldPath
            || relativeProjection.FieldPath.Segments.IsEmpty)
        {
            return ReferenceFieldValue.Unavailable();
        }

        switch (expression)
        {
            case BoundUnsafeExpression unsafeExpression:
                return ReferenceFieldValueOfExpression(unsafeExpression.Expression,
                    valueType, relativeProjection, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
            case BoundConditional conditional when conditional.WhenTrue is not null
                && conditional.WhenFalse is not null
                && conditional.WhenTrue.Type == valueType
                && conditional.WhenFalse.Type == valueType:
                var whenTrue = ReferenceFieldValueOfExpression(conditional.WhenTrue,
                    valueType, relativeProjection, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                var whenFalse = ReferenceFieldValueOfExpression(conditional.WhenFalse,
                    valueType, relativeProjection, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                _ = whenTrue.Merge(whenFalse);
                return whenTrue;
            case BoundRead read:
                if ((uint)read.Variable >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != valueType
                    || !state[read.Variable]
                    || !facts.TryGetReferenceFieldValue(
                        read.Variable, relativeProjection, out var readValue))
                {
                    return ReferenceFieldValue.Unavailable();
                }
                return readValue;
            case BoundReceiverLoan loan:
                if (loan.Type != valueType
                    || (uint)loan.Variable >= (uint)function.Variables.Length
                    || function.Variables[loan.Variable].Type != valueType
                    || !state[loan.Variable]
                    || !facts.TryGetReferenceFieldValue(
                        loan.Variable, relativeProjection, out var receiverValue))
                {
                    return ReferenceFieldValue.Unavailable();
                }
                return receiverValue;
            case BoundMutableReceiverLoan loan:
                if (loan.Type != valueType
                    || (uint)loan.Variable >= (uint)function.Variables.Length
                    || function.Variables[loan.Variable].Type != valueType
                    || !state[loan.Variable]
                    || !facts.TryGetReferenceFieldValue(
                        loan.Variable, relativeProjection, out var mutableReceiverValue))
                {
                    return ReferenceFieldValue.Unavailable();
                }
                return mutableReceiverValue;
            case BoundFieldLoan loan when loan.Place is not null
                && loan.Type == valueType
                && IsInlineStructStorage(loan.Type, program):
                return ReferenceFieldValueOfExpression(loan.Place.Receiver,
                    loan.Place.Receiver.Type, StorageProjection.Combine(
                        StorageProjection.WholeObject.AppendField(
                            loan.Place.Receiver.Type, loan.Place.Field),
                        relativeProjection), facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
            case BoundMutableFieldLoan loan when loan.Place is not null
                && loan.Type == valueType
                && IsInlineStructStorage(loan.Type, program):
                return ReferenceFieldValueOfExpression(loan.Place.Receiver,
                    loan.Place.Receiver.Type, StorageProjection.Combine(
                        StorageProjection.WholeObject.AppendField(
                            loan.Place.Receiver.Type, loan.Place.Field),
                        relativeProjection), facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
            case BoundMove move:
                if (facts.TryGetReferenceFieldCallResult(
                    move, relativeProjection, out var moveCallValue))
                {
                    return moveCallValue;
                }
                if ((uint)move.Variable >= (uint)function.Variables.Length
                    || function.Variables[move.Variable].Type != valueType
                    || !facts.TryGetReferenceFieldValue(
                        move.Variable, relativeProjection, out var moveValue))
                {
                    return ReferenceFieldValue.Unavailable();
                }
                return moveValue;
            case BoundFieldMove fieldMove:
                if (facts.TryGetReferenceFieldCallResult(
                    fieldMove, relativeProjection, out var fieldMoveCallValue))
                {
                    return fieldMoveCallValue;
                }
                if (!TryGetReferencePlaceStorage(fieldMove.Place, function,
                    program, out var moveRoot, out var movePrefix))
                {
                    return ReferenceFieldValue.Unavailable();
                }
                var movedProjection = StorageProjection.Combine(
                    movePrefix, relativeProjection);
                return movedProjection.Kind == StorageProjectionKind.InlineFieldPath
                    && facts.TryGetReferenceFieldValue(
                        moveRoot, movedProjection, out var movedValue)
                    ? movedValue : ReferenceFieldValue.Unavailable();
            case BoundFieldRead field when IsInlineStructStorage(field.Type, program):
                var fieldPrefix = StorageProjection.WholeObject.AppendField(
                    field.Receiver.Type, field.Field);
                var receiverProjection = StorageProjection.Combine(
                    fieldPrefix, relativeProjection);
                return ReferenceFieldValueOfExpression(field.Receiver,
                    field.Receiver.Type, receiverProjection, facts, state,
                    function, program, ref remaining, depth + 1, fieldMasks);
            case BoundReferenceRead referenceRead:
                return ReferenceFieldValueOfReferenceRead(referenceRead, valueType,
                    relativeProjection, facts, state, fieldMasks, function,
                    program, ref remaining, depth + 1);
            case BoundCall call when facts.TryGetReferenceFieldCallResult(
                call, relativeProjection, out var callValue):
                return callValue;
            default:
                return ReferenceFieldValue.Unavailable();
        }
    }

    private static ReferenceFieldValue ReferenceFieldValueOfDescriptorExpression(
        BoundExpression expression, BackingDependencies facts,
        InitializationState state, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, uint[]? fieldMasks)
    {
        Count(ref remaining, depth);
        if (expression is null
            || !CheckedReferenceTypeContract.IsValidType(
                expression.Type, program.CheckedReferenceTypes))
        {
            throw InvalidEffect(expression?.Span ?? default, "CheckedReference");
        }
        switch (expression)
        {
            case BoundNull when program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(expression.Type)].IsOptional:
                return new ReferenceFieldValue(EmptyBackingSources,
                    EmptyBackingSources, EmptyBackingSources, isAvailable: true);
            case BoundOptionalLift lift when program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(expression.Type)].IsOptional:
                return ReferenceFieldValueOfDescriptorExpression(
                    lift.Value, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
            case BoundRead read:
                if ((uint)read.Variable >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != expression.Type
                    || !state[read.Variable] || state.HasRetiredValue(read.Variable))
                {
                    return ReferenceFieldValue.Unavailable();
                }
                var optionalReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(expression.Type)].IsOptional;
                var targets = facts.ReferenceTargets[read.Variable];
                if (targets is not { Count: > 0 } && !optionalReference)
                {
                    return ReferenceFieldValue.Unavailable();
                }
                return new ReferenceFieldValue(targets ?? EmptyBackingSources,
                    facts.ReferenceObligations[read.Variable]
                        ?? EmptyBackingSources,
                    facts.ReferenceBackingSources[read.Variable]
                        ?? EmptyBackingSources,
                    isAvailable: true);
            case BoundFieldRead field:
                if (field.Type != expression.Type)
                {
                    return ReferenceFieldValue.Unavailable();
                }
                if (program.NativeLoweringAgreement
                        == CompilationOptions.WindowsX64NormalBackingAgreement
                    && IsNormalBackingObjectType(field.Receiver.Type))
                {
                    var classFieldProjection = StorageProjection.WholeObject
                        .AppendField(field.Receiver.Type, field.Field);
                    return NormalBackingReferenceFieldValueOfExpression(
                        field.Receiver, classFieldProjection, facts, state,
                        function, program, ref remaining, depth + 1);
                }
                if (!IsInlineStructStorage(field.Receiver.Type, program))
                {
                    return ReferenceFieldValue.Unavailable();
                }
                if (TryGetReferencePlaceStorage(field, function, program,
                    out var variable, out var projection))
                {
                    if (projection.Kind != StorageProjectionKind.InlineFieldPath
                        || !ReferenceTargetPlaceInitialized(variable, projection,
                            state, fieldMasks, function, program)
                        || !facts.TryGetReferenceFieldValue(
                            variable, projection, out var fieldValue))
                    {
                        return ReferenceFieldValue.Unavailable();
                    }
                    return fieldValue;
                }
                var fieldPrefix = StorageProjection.WholeObject.AppendField(
                    field.Receiver.Type, field.Field);
                return ReferenceFieldValueOfExpression(field.Receiver,
                    field.Receiver.Type, fieldPrefix, facts, state, function,
                    program, ref remaining, depth + 1, fieldMasks);
            case BoundFieldMove fieldMove:
                if (fieldMove.Type != expression.Type
                    || !TryGetReferencePlaceStorage(fieldMove.Place, function,
                        program, out var movedVariable, out var movedProjection)
                    || movedProjection.Kind != StorageProjectionKind.InlineFieldPath
                    || !facts.TryGetReferenceFieldValue(
                        movedVariable, movedProjection, out var movedValue))
                {
                    return ReferenceFieldValue.Unavailable();
                }
                return movedValue;
            case BoundMove move:
                if ((uint)move.Variable >= (uint)function.Variables.Length
                    || function.Variables[move.Variable].Type != expression.Type
                    || facts.ReferenceTargets[move.Variable] is not { Count: > 0 } movedTargets)
                {
                    return ReferenceFieldValue.Unavailable();
                }
                return new ReferenceFieldValue(movedTargets,
                    facts.ReferenceObligations[move.Variable] ?? EmptyBackingSources,
                    facts.ReferenceBackingSources[move.Variable] ?? EmptyBackingSources,
                    isAvailable: true);
            case BoundReferenceFormation formation:
                var formedTargets = ReferenceTargetsOfExpression(formation, facts,
                    state, function, program, ref remaining, depth + 1, fieldMasks);
                var formedObligations = ReferenceObligationsOfExpression(
                    formation, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                return new ReferenceFieldValue(formedTargets, formedObligations,
                    EmptyBackingSources, formedTargets.Count != 0);
            case BoundReferenceReborrow reborrow:
                var reborrowTargets = ReferenceTargetsOfExpression(reborrow, facts,
                    state, function, program, ref remaining, depth + 1, fieldMasks);
                var reborrowObligations = ReferenceObligationsOfExpression(
                    reborrow, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                var sourceValue = ReferenceFieldValueOfDescriptorExpression(
                    reborrow.Descriptor, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                return new ReferenceFieldValue(reborrowTargets,
                    reborrowObligations, sourceValue.BackingSources,
                    reborrowTargets.Count != 0 && sourceValue.IsAvailable);
            case BoundReferenceRead referenceRead:
                return ReferenceFieldValueOfReferenceRead(referenceRead,
                    expression.Type, StorageProjection.WholeObject,
                    facts, state, fieldMasks, function, program,
                    ref remaining, depth + 1);
            case BoundCoalesce coalesce:
                var coalesceValue = ReferenceFieldValueOfDescriptorExpression(
                    coalesce.Optional, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                _ = coalesceValue.Merge(ReferenceFieldValueOfDescriptorExpression(
                    coalesce.Fallback, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                return coalesceValue;
            case BoundConditional conditional when conditional.WhenTrue is not null
                && conditional.WhenFalse is not null
                && conditional.WhenTrue.Type == expression.Type
                && conditional.WhenFalse.Type == expression.Type:
                var whenTrue = ReferenceFieldValueOfDescriptorExpression(
                    conditional.WhenTrue, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                var whenFalse = ReferenceFieldValueOfDescriptorExpression(
                    conditional.WhenFalse, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                _ = whenTrue.Merge(whenFalse);
                return whenTrue;
            case BoundUnsafeExpression unsafeExpression:
                return ReferenceFieldValueOfDescriptorExpression(
                    unsafeExpression.Expression, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
            case BoundCall call when facts.TryGetReferenceFieldCallResult(
                call, StorageProjection.WholeObject, out var callValue):
                return callValue;
            case BoundCall:
                return ReferenceFieldValue.Unavailable();
            default:
                throw InvalidEffect(expression.Span, "CheckedReference");
        }
    }

    private static ReferenceFieldValue ReferenceFieldValueOfReferenceRead(
        BoundReferenceRead referenceRead, PrimitiveType valueType,
        StorageProjection relativeProjection, BackingDependencies facts,
        InitializationState state, uint[]? fieldMasks, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        IReadOnlySet<ProvenanceAnchor>? descriptorTargets = null)
    {
        var targets = descriptorTargets ?? ReferenceTargetsOfExpression(
            referenceRead.Descriptor, facts, state, function, program,
            ref remaining, depth + 1, fieldMasks);
        if (relativeProjection.Kind == StorageProjectionKind.WholeObject
            && CheckedReferenceTypeContract.IsValidType(
                valueType, program.CheckedReferenceTypes)
            && IsOrdinaryStoredReferenceType(valueType, program))
        {
            ReferenceFieldValue? descriptorValue = null;
            foreach (var target in targets)
            {
                ReferenceFieldValue value;
                if (target.Kind is ProvenanceAnchorKind.IncomingReference
                    or ProvenanceAnchorKind.IncomingReferenceContents)
                {
                    ValidateReferenceTarget(target, valueType, facts, state,
                        function, program, referenceRead.Span, fieldMasks);
                    if (target.IncomingReferenceDepth >= MaximumDepth)
                    {
                        value = ReferenceFieldValue.Unavailable();
                    }
                    else
                    {
                        var referenceDepth =
                            target.Kind == ProvenanceAnchorKind.IncomingReference
                                ? 1 : target.IncomingReferenceDepth + 1;
                        _ = facts.TryGetIncomingReferenceFieldValue(
                            ProvenanceAnchor.ForIncomingReferencePlace(
                                target.Identity, relativeProjection,
                                referenceDepth),
                            out value);
                    }
                }
                else if (target.Kind == ProvenanceAnchorKind.ReferencePlace
                    && target.Storage is StorageKey storage)
                {
                    ValidateReferenceTarget(target, valueType, facts, state,
                        function, program, referenceRead.Span, fieldMasks);
                    if (storage.Projection.Kind == StorageProjectionKind.WholeObject
                        && facts.ReferenceTargets[target.Identity] is
                            { Count: > 0 } nestedTargets)
                    {
                        value = new ReferenceFieldValue(nestedTargets,
                            facts.ReferenceObligations[target.Identity]
                                ?? EmptyBackingSources,
                            facts.ReferenceBackingSources[target.Identity]
                                ?? EmptyBackingSources, isAvailable: true);
                    }
                    else if (storage.Projection.Kind ==
                            StorageProjectionKind.InlineFieldPath
                        && facts.TryGetReferenceFieldValue(target.Identity,
                            storage.Projection, out var nestedFieldValue))
                    {
                        value = nestedFieldValue;
                    }
                    else
                    {
                        value = ReferenceFieldValue.Unavailable();
                    }
                }
                else
                {
                    value = ReferenceFieldValue.Unavailable();
                }

                if (descriptorValue is null)
                {
                    descriptorValue = value;
                }
                else
                {
                    _ = descriptorValue.Merge(value);
                }
            }
            return descriptorValue ?? ReferenceFieldValue.Unavailable();
        }

        ReferenceFieldValue? result = null;
        foreach (var target in targets)
        {
            if (target.Kind == ProvenanceAnchorKind.IncomingReference)
            {
                var incomingType = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(function.Variables[target.Identity].Type)];
                if (incomingType.ReferentType == valueType
                    && relativeProjection.Kind == StorageProjectionKind.InlineFieldPath)
                {
                    var incomingTarget = ProvenanceAnchor.ForIncomingReferenceField(
                        target.Identity, relativeProjection, referenceDepth: 1);
                    var incomingValue = new ReferenceFieldValue(
                        new HashSet<ProvenanceAnchor> { incomingTarget },
                        EmptyBackingSources,
                        facts.ReferenceBackingSources[target.Identity]
                            ?? EmptyBackingSources,
                        isAvailable: true);
                    if (result is null)
                    {
                        result = incomingValue;
                    }
                    else
                    {
                        _ = result.Merge(incomingValue);
                    }
                }
                continue;
            }
            if (target.Kind == ProvenanceAnchorKind.IncomingReferenceContents)
            {
                ValidateReferenceTarget(target, valueType, facts, state,
                    function, program, referenceRead.Span, fieldMasks);
                if (relativeProjection.Kind == StorageProjectionKind.InlineFieldPath)
                {
                    var incomingTarget = ProvenanceAnchor.ForIncomingReferenceField(
                        target.Identity, relativeProjection,
                        target.IncomingReferenceDepth + 1);
                    var incomingValue = new ReferenceFieldValue(
                        new HashSet<ProvenanceAnchor> { incomingTarget },
                        EmptyBackingSources,
                        facts.ReferenceBackingSources[target.Identity]
                            ?? EmptyBackingSources,
                        isAvailable: true);
                    if (result is null)
                    {
                        result = incomingValue;
                    }
                    else
                    {
                        _ = result.Merge(incomingValue);
                    }
                }
                continue;
            }
            if (target.Kind == ProvenanceAnchorKind.IncomingReferenceField)
            {
                var currentProjection = target.IncomingProjection
                    ?? throw Invalid(referenceRead.Span);
                if (!TryGetIncomingReferenceFieldRootType(
                        target.Identity, target.IncomingReferenceDepth,
                        function, program, out var rootType)
                    || !TryGetReferenceFieldType(rootType,
                        currentProjection, program, out var incomingType)
                    || !CheckedReferenceTypeContract.IsValidType(
                        incomingType, program.CheckedReferenceTypes)
                    || program.CheckedReferenceTypes[
                        CheckedReferenceTypes.Index(incomingType)].ReferentType != valueType
                    || relativeProjection.Kind != StorageProjectionKind.InlineFieldPath)
                {
                    throw InvalidEffect(referenceRead.Span, "CheckedReference");
                }
                var incomingProjection = StorageProjection.Combine(
                    currentProjection, relativeProjection);
                _ = facts.TryGetIncomingReferenceFieldValue(
                    ProvenanceAnchor.ForIncomingReferencePlace(
                        target.Identity, incomingProjection,
                        target.IncomingReferenceDepth),
                    out var incomingValue);
                if (result is null)
                {
                    result = incomingValue;
                }
                else
                {
                    _ = result.Merge(incomingValue);
                }
                continue;
            }
            if (target.Kind == ProvenanceAnchorKind.NormalBackingObjectPlace
                && target.Storage is StorageKey objectStorage)
            {
                var objectProjection = StorageProjection.Combine(
                    objectStorage.Projection, relativeProjection);
                var objectIdentity = ProvenanceAnchor.ForNormalBackingObject(
                    objectStorage.Activation, objectStorage.Site,
                    objectStorage.Instance, target.ObjectType);
                if (objectProjection.Kind != StorageProjectionKind.InlineFieldPath
                    || !facts.TryGetNormalBackingReferenceFieldValue(
                        new HashSet<ProvenanceAnchor> { objectIdentity },
                        objectProjection, out var objectFieldValue))
                {
                    if (result is null)
                    {
                        result = ReferenceFieldValue.Unavailable();
                    }
                    else
                    {
                        _ = result.Merge(ReferenceFieldValue.Unavailable());
                    }
                    continue;
                }
                if (result is null)
                {
                    result = objectFieldValue;
                }
                else
                {
                    _ = result.Merge(objectFieldValue);
                }
                continue;
            }
            if (target.Kind != ProvenanceAnchorKind.ReferencePlace
                || target.Storage is not StorageKey storage)
            {
                throw InvalidEffect(referenceRead.Span, "CheckedReference");
            }
            var projection = StorageProjection.Combine(
                storage.Projection, relativeProjection);
            if (projection.Kind != StorageProjectionKind.InlineFieldPath
                || !ReferenceTargetPlaceInitialized(
                    target.Identity, projection, state, fieldMasks,
                    function, program)
                || !facts.TryGetReferenceFieldValue(
                    target.Identity, projection, out var fieldValue))
            {
                if (result is null)
                {
                    result = ReferenceFieldValue.Unavailable();
                }
                else
                {
                    _ = result.Merge(ReferenceFieldValue.Unavailable());
                }
                continue;
            }
            if (result is null)
            {
                result = fieldValue.Clone();
            }
            else
            {
                _ = result.Merge(fieldValue);
            }
        }
        return result ?? ReferenceFieldValue.Unavailable();
    }

    private static bool TryGetReferenceFieldType(
        PrimitiveType rootType, StorageProjection projection,
        BoundProgram program, out PrimitiveType fieldType)
    {
        fieldType = rootType;
        if (projection.Kind != StorageProjectionKind.InlineFieldPath
            || projection.FieldPath.Segments.IsEmpty)
        {
            return false;
        }
        foreach (var segment in projection.FieldPath.Segments)
        {
            if (fieldType != segment.OwnerType
                || !IsInlineStructStorage(fieldType, program))
            {
                return false;
            }
            var fields = program.Structures[(int)fieldType - 100].Fields;
            if ((uint)segment.Field >= (uint)fields.Length)
            {
                return false;
            }
            fieldType = fields[segment.Field].Type;
        }
        return true;
    }

    private static bool TryGetNormalBackingObjectPlaceType(
        PrimitiveType objectType, StorageProjection projection,
        BoundProgram program, out PrimitiveType type)
    {
        type = PrimitiveType.Void;
        if (program.NativeLoweringAgreement
                != CompilationOptions.WindowsX64NormalBackingAgreement
            || !ManagedClassTypes.IsClass(objectType)
            || projection.Kind != StorageProjectionKind.InlineFieldPath
            || projection.FieldPath.Segments.IsEmpty)
        {
            return false;
        }

        var currentType = objectType;
        var segments = projection.FieldPath.Segments;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (currentType != segment.OwnerType
                || !HasFields(program, currentType)
                || (uint)segment.Field >= (uint)Fields(program, currentType).Length)
            {
                return false;
            }
            currentType = Fields(program, currentType)[segment.Field].Type;
            if (index + 1 < segments.Length
                && ManagedClassTypes.IsClass(currentType))
            {
                return false;
            }
        }
        type = currentType;
        return true;
    }

    private static bool TryGetIncomingReferenceFieldRootType(
        int formal, int referenceDepth, BoundFunction function,
        BoundProgram program, out PrimitiveType rootType)
    {
        rootType = PrimitiveType.Int32;
        var parameterCount = function.ParameterCount
            + (function.IsConstructor ? 1 : 0);
        if ((uint)formal >= (uint)parameterCount
            || referenceDepth < 0 || referenceDepth > MaximumDepth)
        {
            return false;
        }

        rootType = function.Variables[formal].Type;
        for (var depth = 0; depth < referenceDepth; depth++)
        {
            if (!CheckedReferenceTypeContract.IsValidType(
                    rootType, program.CheckedReferenceTypes))
            {
                return false;
            }
            var reference = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(rootType)];
            if (reference.IsGarbageCollected || reference.IsInitialization
                || reference.IsOptional)
            {
                return false;
            }
            rootType = reference.ReferentType;
        }
        return true;
    }

    private static void SetReferenceFieldValuesFromExpression(
        int destination, StorageProjection destinationPrefix,
        PrimitiveType destinationType, BoundExpression expression,
        BackingDependencies facts, InitializationState state,
        BoundFunction function, BoundProgram program,
        ref int remaining, int depth, uint[]? fieldMasks = null)
    {
        Count(ref remaining, depth);
        if ((uint)destination >= (uint)function.Variables.Length
            || expression is null || expression.Type != destinationType
            || destinationPrefix.Kind is not (StorageProjectionKind.WholeObject
                or StorageProjectionKind.InlineFieldPath))
        {
            throw Invalid(expression?.Span ?? default);
        }

        var values = ReferenceFieldValuesOfExpression(expression,
            destinationType, facts, state, function, program,
            ref remaining, depth + 1, fieldMasks);
        if (program.NativeLoweringAgreement
                == CompilationOptions.WindowsX64NormalBackingAgreement
            && IsNormalBackingObjectType(function.Variables[destination].Type))
        {
            if (TryGetNormalBackingClassFieldOwner(destination,
                destinationPrefix, facts, function, program,
                ref remaining, depth + 1,
                out var objectIdentity, out var objectProjection))
            {
                facts.ClearNormalBackingReferenceFieldValues(
                    new HashSet<ProvenanceAnchor> { objectIdentity },
                    objectProjection);
                foreach (var (projection, value) in values)
                {
                    var destinationProjection = StorageProjection.Combine(
                        objectProjection, projection);
                    if (destinationProjection.Kind
                        == StorageProjectionKind.InlineFieldPath)
                    {
                        facts.SetNormalBackingReferenceFieldValue(
                            objectIdentity, destinationProjection, value);
                    }
                }
            }
            return;
        }
        if (destinationPrefix.Kind == StorageProjectionKind.WholeObject)
        {
            facts.ClearReferenceFieldValues(destination);
        }
        else
        {
            facts.ClearReferenceFieldValues(destination, destinationPrefix);
        }

        foreach (var (projection, value) in values)
        {
            var destinationProjection = StorageProjection.Combine(
                destinationPrefix, projection);
            if (destinationProjection.Kind != StorageProjectionKind.InlineFieldPath)
            {
                continue;
            }
            facts.SetReferenceFieldValue(destination,
                destinationProjection, value);
        }
    }

    private static bool TryGetNormalBackingClassFieldOwner(
        int rootVariable, StorageProjection fieldPath,
        BackingDependencies facts, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        out ProvenanceAnchor objectIdentity,
        out StorageProjection objectProjection)
    {
        Count(ref remaining, depth);
        objectIdentity = default;
        objectProjection = default;
        if (program.NativeLoweringAgreement
                != CompilationOptions.WindowsX64NormalBackingAgreement
            || (uint)rootVariable >= (uint)function.Variables.Length
            || !ManagedClassTypes.IsClass(function.Variables[rootVariable].Type)
            || fieldPath.Kind != StorageProjectionKind.InlineFieldPath
            || fieldPath.FieldPath.Segments.IsEmpty
            || facts.NormalBackingObjectsMayBeUnknownForSlot(rootVariable))
        {
            return false;
        }

        var objectIdentities = facts.NormalBackingObjectsForSlot(rootVariable);
        if (objectIdentities.Count != 1)
        {
            return false;
        }
        objectIdentity = objectIdentities.Single();
        var ownerType = function.Variables[rootVariable].Type;
        objectProjection = StorageProjection.WholeObject;
        var segments = fieldPath.FieldPath.Segments;
        for (var index = 0; index < segments.Length; index++)
        {
            Count(ref remaining, depth + index + 1);
            var segment = segments[index];
            if (ownerType != segment.OwnerType
                || !HasFields(program, ownerType)
                || (uint)segment.Field >= (uint)Fields(program, ownerType).Length)
            {
                return false;
            }
            var fieldType = Fields(program, ownerType)[segment.Field].Type;
            objectProjection = objectProjection.AppendField(
                ownerType, segment.Field);
            ownerType = fieldType;
            if (index + 1 == segments.Length
                || !IsNormalBackingObjectType(fieldType))
            {
                continue;
            }

            if (!facts.TryGetNormalBackingClassFieldValue(
                new HashSet<ProvenanceAnchor> { objectIdentity },
                objectProjection, out var nestedObjects,
                out var nestedMayBeUnknown)
                || nestedMayBeUnknown || nestedObjects.Count != 1)
            {
                return false;
            }
            objectIdentity = nestedObjects.Single();
            objectProjection = StorageProjection.WholeObject;
        }
        return true;
    }

    private static void SetNormalBackingClassFieldValueFromExpression(
        int destination, StorageProjection fieldPath, PrimitiveType fieldType,
        BoundExpression expression, BackingDependencies facts,
        InitializationState state, BoundFunction function,
        BoundProgram program, ref int remaining, int depth)
    {
        if (!TryGetNormalBackingClassFieldOwner(destination, fieldPath,
            facts, function, program, ref remaining, depth + 1,
            out var objectIdentity, out var objectProjection))
        {
            return;
        }
        var objects = NormalBackingObjectsOfExpression(expression, facts,
            state, function, program, ref remaining, depth + 1,
            out var mayBeUnknown);
        if (!IsNormalBackingObjectType(fieldType))
        {
            throw Invalid(expression.Span);
        }
        facts.SetNormalBackingClassFieldValue(objectIdentity,
            objectProjection, objects, mayBeUnknown);
    }

    private static HashSet<ProvenanceAnchor> ReferenceTargetsOfExpression(
        BoundExpression expression, BackingDependencies facts, InitializationState state,
        BoundFunction function, BoundProgram program, ref int remaining, int depth,
        uint[]? fieldMasks = null)
    {
        Count(ref remaining, depth);
        if (expression is null || facts is null || state is null || function is null
            || program is null
            || !CheckedReferenceTypeContract.IsValidType(
                expression.Type, program.CheckedReferenceTypes))
        {
            throw InvalidEffect(expression?.Span ?? default, "CheckedReference");
        }

        var type = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(expression.Type)];
        var result = new HashSet<ProvenanceAnchor>();
        switch (expression)
        {
            case BoundNull when type.IsOptional:
                break;
            case BoundOptionalLift lift when type.IsOptional:
                if (!SourceOptionalTypes.TryGetRequired(
                    expression.Type, program.CheckedReferenceTypes,
                    out var liftedType) || lift.Value.Type != liftedType)
                {
                    throw InvalidEffect(lift.Span, "CheckedReference");
                }
                result = ReferenceTargetsOfExpression(lift.Value, facts, state,
                    function, program, ref remaining, depth + 1, fieldMasks);
                break;
            case BoundReferenceFormation formation:
                if (formation.Type != expression.Type
                    || type.IsGarbageCollected || type.IsInitialization || type.IsOptional
                    || formation.Place.Type != type.ReferentType)
                {
                    throw InvalidEffect(formation.Span, "CheckedReference");
                }

                if (TryGetIncomingReferencePlace(
                    formation.Place, function, program, ref remaining,
                    depth + 1, out var incomingPlace, out var incomingWritable))
                {
                    if (type.IsWritable && !incomingWritable)
                    {
                        throw InvalidEffect(formation.Span, "CheckedReference");
                    }
                    ValidateReferenceTarget(incomingPlace, type.ReferentType,
                        facts, state, function, program, formation.Span, fieldMasks);
                    _ = result.Add(incomingPlace);
                }
                else if (TryGetNormalBackingClassPlace(formation.Place,
                    function, program, out var normalBackingVariable,
                    out var normalBackingProjection))
                {
                    if ((type.IsWritable && !IsWritableReferencePlace(
                            formation.Place, function, program))
                        || !ReferenceTargetPlaceInitialized(normalBackingVariable,
                            normalBackingProjection, state, fieldMasks,
                            function, program))
                    {
                        throw InvalidEffect(formation.Span, "CheckedReference");
                    }
                    var instances = facts.StorageInstanceSet(normalBackingVariable);
                    if (instances.Count != 1)
                    {
                        throw InvalidEffect(formation.Span, "CheckedReference");
                    }
                    foreach (var instance in instances)
                    {
                        if (!instance.IsKnown)
                        {
                            throw InvalidEffect(formation.Span, "CheckedReference");
                        }
                        _ = result.Add(ProvenanceAnchor.ForNormalBackingPlace(
                            facts.FunctionActivation, normalBackingVariable, instance,
                            normalBackingProjection,
                            state.FieldPlaceGeneration(normalBackingVariable,
                                normalBackingProjection)));
                    }
                }
                else if (TryGetReferencePlaceStorage(formation.Place, function,
                    program, out var placeVariable, out var placeProjection))
                {
                    if ((type.IsWritable && !IsWritableReferencePlace(
                            formation.Place, function, program))
                        || !ReferenceTargetPlaceInitialized(placeVariable,
                            placeProjection, state, fieldMasks, function, program))
                    {
                        throw InvalidEffect(formation.Span, "CheckedReference");
                    }
                    var instances = facts.StorageInstanceSet(placeVariable);
                    if (instances.Count != 1)
                    {
                        throw InvalidEffect(formation.Span, "CheckedReference");
                    }
                    foreach (var instance in instances)
                    {
                        if (!instance.IsKnown)
                        {
                            throw InvalidEffect(formation.Span, "CheckedReference");
                        }
                        _ = result.Add(ProvenanceAnchor.ForReferencePlace(
                            facts.FunctionActivation, placeVariable, instance,
                            placeProjection,
                            state.FieldPlaceGeneration(placeVariable, placeProjection)));
                    }
                }
                else
                {
                    throw InvalidEffect(formation.Span, "CheckedReference");
                }
                break;
            case BoundReferenceReborrow reborrow:
                if (!CheckedReferenceTypeContract.IsValidType(
                        reborrow.Descriptor.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(reborrow.Span, "CheckedReference");
                }
                var sourceReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(reborrow.Descriptor.Type)];
                if (reborrow.Type != expression.Type
                    || sourceReference.IsGarbageCollected || sourceReference.IsInitialization
                    || sourceReference.IsOptional || type.IsGarbageCollected
                    || type.IsInitialization || type.IsOptional
                    || sourceReference.ReferentType != type.ReferentType
                    || (type.IsWritable && !sourceReference.IsWritable))
                {
                    throw InvalidEffect(reborrow.Span, "CheckedReference");
                }
                _ = ReferenceObligationsOfExpression(reborrow, facts, state,
                    function, program, ref remaining, depth + 1, fieldMasks);
                result = ReferenceTargetsOfExpression(reborrow.Descriptor, facts,
                    state, function, program, ref remaining, depth + 1, fieldMasks);
                foreach (var target in result)
                {
                    ValidateReferenceTarget(target, type.ReferentType, facts,
                        state, function, program, reborrow.Span, fieldMasks);
                }
                break;
            case BoundFieldRead field:
                if (field.Type != expression.Type)
                {
                    throw Invalid(field.Span);
                }
                var fieldReferenceValue = ReferenceFieldValueOfDescriptorExpression(
                    field, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                if (!fieldReferenceValue.IsAvailable
                    || (fieldReferenceValue.Targets.Count == 0 && !type.IsOptional))
                {
                    throw InvalidEffect(field.Span, "CheckedReference");
                }
                foreach (var target in fieldReferenceValue.Targets)
                {
                    ValidateReferenceTarget(target, type.ReferentType,
                        facts, state, function, program, field.Span, fieldMasks);
                    _ = result.Add(target);
                }
                foreach (var obligation in fieldReferenceValue.Obligations)
                {
                    ValidateReferenceObligation(obligation, facts, state,
                        function, program, field.Span, fieldMasks);
                }
                break;
            case BoundRead read:
                if ((uint)read.Variable >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != expression.Type
                    || !state[read.Variable] || state.HasRetiredValue(read.Variable))
                {
                    throw InvalidEffect(read.Span, "CheckedReference");
                }
                if (facts.ReferenceTargets[read.Variable] is not { Count: > 0 } targets)
                {
                    if (type.IsOptional)
                    {
                        break;
                    }
                    throw InvalidEffect(read.Span, "CheckedReference");
                }
                if (facts.ReferenceObligations[read.Variable] is { } readObligations)
                {
                    foreach (var obligation in readObligations)
                    {
                        ValidateReferenceObligation(obligation, facts, state,
                            function, program, read.Span, fieldMasks);
                    }
                }
                foreach (var target in targets)
                {
                    ValidateReferenceTarget(target, type.ReferentType, facts, state,
                        function, program, read.Span, fieldMasks);
                    _ = result.Add(target);
                }
                break;
            case BoundCall call when facts.TryGetReferenceFieldCallResult(
                call, StorageProjection.WholeObject, out var callValue):
                if (!callValue.IsAvailable
                    || (callValue.Targets.Count == 0 && !type.IsOptional))
                {
                    throw InvalidEffect(call.Span, "CheckedReference");
                }
                foreach (var target in callValue.Targets)
                {
                    ValidateReferenceTarget(target, type.ReferentType,
                        facts, state, function, program, call.Span, fieldMasks);
                    _ = result.Add(target);
                }
                foreach (var obligation in callValue.Obligations)
                {
                    ValidateReferenceObligation(obligation, facts, state,
                        function, program, call.Span, fieldMasks);
                }
                break;
            case BoundReferenceRead referenceRead:
                if (!CheckedReferenceTypeContract.IsValidType(
                        referenceRead.Descriptor.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(referenceRead.Span, "CheckedReference");
                }
                var descriptorType = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(referenceRead.Descriptor.Type)];
                if (descriptorType.IsGarbageCollected || descriptorType.IsInitialization
                    || descriptorType.IsOptional || descriptorType.ReferentType != expression.Type)
                {
                    throw InvalidEffect(referenceRead.Span, "CheckedReference");
                }
                var descriptorTargets = ReferenceTargetsOfExpression(
                    referenceRead.Descriptor, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                foreach (var target in descriptorTargets)
                {
                    ValidateReferenceTarget(target, expression.Type, facts, state,
                        function, program, referenceRead.Span, fieldMasks);
                    if (target.Kind == ProvenanceAnchorKind.IncomingReference)
                    {
                        if (CheckedReferenceTypeContract.IsValidType(
                                expression.Type, program.CheckedReferenceTypes))
                        {
                            if (!IsOrdinaryStoredReferenceType(expression.Type, program))
                            {
                                throw InvalidEffect(referenceRead.Span, "CheckedReference");
                            }
                            _ = result.Add(ProvenanceAnchor.ForIncomingReferenceContents(
                                target.Identity, depth: 1));
                        }
                        else
                        {
                            _ = result.Add(target);
                        }
                        continue;
                    }
                    if (target.Kind == ProvenanceAnchorKind.IncomingReferenceContents)
                    {
                        if (CheckedReferenceTypeContract.IsValidType(
                                expression.Type, program.CheckedReferenceTypes))
                        {
                            if (!IsOrdinaryStoredReferenceType(expression.Type, program)
                                || target.IncomingReferenceDepth >= MaximumDepth)
                            {
                                throw InvalidEffect(referenceRead.Span, "CheckedReference");
                            }
                            _ = result.Add(ProvenanceAnchor.ForIncomingReferenceContents(
                                target.Identity, target.IncomingReferenceDepth + 1));
                        }
                        else
                        {
                            _ = result.Add(target);
                        }
                        continue;
                    }
                    if (target.Kind != ProvenanceAnchorKind.ReferencePlace
                        || facts.ReferenceTargets[target.Identity] is not { Count: > 0 } nestedTargets)
                    {
                        throw InvalidEffect(referenceRead.Span, "CheckedReference");
                    }
                    if (facts.ReferenceObligations[target.Identity] is { } nestedObligations)
                    {
                        foreach (var obligation in nestedObligations)
                        {
                            ValidateReferenceObligation(obligation, facts, state,
                                function, program, referenceRead.Span, fieldMasks);
                        }
                    }
                    foreach (var nestedTarget in nestedTargets)
                    {
                        ValidateReferenceTarget(nestedTarget, type.ReferentType,
                            facts, state, function, program, referenceRead.Span,
                            fieldMasks);
                        _ = result.Add(nestedTarget);
                    }
                }
                if (result.Count == 0)
                {
                    throw InvalidEffect(referenceRead.Span, "CheckedReference");
                }
                break;
            case BoundCoalesce coalesce:
                result.UnionWith(ReferenceTargetsOfExpression(coalesce.Optional,
                    facts, state, function, program, ref remaining,
                    depth + 1, fieldMasks));
                result.UnionWith(ReferenceTargetsOfExpression(coalesce.Fallback,
                    facts, state, function, program, ref remaining,
                    depth + 1, fieldMasks));
                break;
            case BoundConditional conditional:
                if (conditional.Type != expression.Type)
                {
                    throw InvalidEffect(conditional.Span, "CheckedReference");
                }
                result.UnionWith(ReferenceTargetsOfExpression(conditional.WhenTrue,
                    facts, state, function, program, ref remaining, depth + 1,
                    fieldMasks));
                result.UnionWith(ReferenceTargetsOfExpression(conditional.WhenFalse,
                    facts, state, function, program, ref remaining, depth + 1,
                    fieldMasks));
                break;
            case BoundUnsafeExpression unsafeExpression:
                result = ReferenceTargetsOfExpression(unsafeExpression.Expression,
                    facts, state, function, program, ref remaining, depth + 1,
                    fieldMasks);
                break;
            default:
                throw InvalidEffect(expression.Span, "CheckedReference");
        }
        return result;
    }

    private static HashSet<ProvenanceAnchor> ReferenceObligationsOfExpression(
        BoundExpression expression, BackingDependencies facts, InitializationState state,
        BoundFunction function, BoundProgram program, ref int remaining, int depth,
        uint[]? fieldMasks = null)
    {
        Count(ref remaining, depth);
        if (expression is null
            || !CheckedReferenceTypeContract.IsValidType(
                expression.Type, program.CheckedReferenceTypes))
        {
            throw InvalidEffect(expression?.Span ?? default, "CheckedReference");
        }

        var result = new HashSet<ProvenanceAnchor>();
        var type = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(expression.Type)];
        switch (expression)
        {
            case BoundNull when type.IsOptional:
                break;
            case BoundOptionalLift lift when type.IsOptional:
                if (!SourceOptionalTypes.TryGetRequired(
                    expression.Type, program.CheckedReferenceTypes,
                    out var liftedType) || lift.Value.Type != liftedType)
                {
                    throw InvalidEffect(lift.Span, "CheckedReference");
                }
                result = ReferenceObligationsOfExpression(lift.Value, facts,
                    state, function, program, ref remaining, depth + 1, fieldMasks);
                break;
            case BoundReferenceFormation formation:
                if (TryGetIncomingReferencePlace(formation.Place, function,
                    program, ref remaining, depth + 1,
                    out var incomingPlace, out _))
                {
                    if (incomingPlace.IncomingReferenceDepth > 0)
                    {
                        var incomingObligation =
                            ProvenanceAnchor.ForIncomingReferenceDescriptor(
                                incomingPlace.Identity);
                        ValidateReferenceObligation(incomingObligation, facts,
                            state, function, program, formation.Span, fieldMasks);
                        _ = result.Add(incomingObligation);
                        if (facts.ReferenceObligations[incomingPlace.Identity]
                            is { } incomingObligations)
                        {
                            foreach (var obligation in incomingObligations)
                            {
                                ValidateReferenceObligation(obligation, facts,
                                    state, function, program, formation.Span,
                                    fieldMasks);
                                _ = result.Add(obligation);
                            }
                        }
                    }
                }
                break;
            case BoundRead read:
                if ((uint)read.Variable >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != expression.Type
                    || !state[read.Variable] || state.HasRetiredValue(read.Variable))
                {
                    throw InvalidEffect(read.Span, "CheckedReference");
                }
                if (facts.ReferenceObligations[read.Variable] is { } obligations)
                {
                    foreach (var obligation in obligations)
                    {
                        ValidateReferenceObligation(obligation, facts, state,
                            function, program, read.Span, fieldMasks);
                        _ = result.Add(obligation);
                    }
                }
                break;
            case BoundFieldRead field:
                if (field.Type != expression.Type)
                {
                    throw InvalidEffect(field.Span, "CheckedReference");
                }
                var fieldValue = ReferenceFieldValueOfDescriptorExpression(
                    field, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                if (!fieldValue.IsAvailable)
                {
                    throw InvalidEffect(field.Span, "CheckedReference");
                }
                foreach (var obligation in fieldValue.Obligations)
                {
                    ValidateReferenceObligation(obligation, facts, state,
                        function, program, field.Span, fieldMasks);
                    _ = result.Add(obligation);
                }
                break;
            case BoundCall call when facts.TryGetReferenceFieldCallResult(
                call, StorageProjection.WholeObject, out var callValue):
                if (!callValue.IsAvailable
                    || (callValue.Targets.Count == 0 && !type.IsOptional))
                {
                    throw InvalidEffect(call.Span, "CheckedReference");
                }
                foreach (var obligation in callValue.Obligations)
                {
                    ValidateReferenceObligation(obligation, facts, state,
                        function, program, call.Span, fieldMasks);
                    _ = result.Add(obligation);
                }
                break;
            case BoundReferenceReborrow reborrow:
                result.UnionWith(ReferenceObligationsOfExpression(
                    reborrow.Descriptor, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                result.UnionWith(ReferenceDescriptorPlacesOfExpression(
                    reborrow.Descriptor, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                break;
            case BoundReferenceRead referenceRead:
                var descriptorTargets = ReferenceTargetsOfExpression(
                    referenceRead.Descriptor, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                foreach (var target in descriptorTargets)
                {
                    if (target.Kind == ProvenanceAnchorKind.IncomingReference)
                    {
                        _ = result.Add(ProvenanceAnchor.ForIncomingReferenceDescriptor(
                            target.Identity));
                        continue;
                    }
                    if (target.Kind == ProvenanceAnchorKind.IncomingReferenceContents)
                    {
                        _ = result.Add(ProvenanceAnchor.ForIncomingReferenceDescriptor(
                            target.Identity));
                        continue;
                    }
                    if (target.Kind == ProvenanceAnchorKind.NormalBackingPlace)
                    {
                        continue;
                    }
                    if (target.Kind != ProvenanceAnchorKind.ReferencePlace)
                    {
                        throw InvalidEffect(referenceRead.Span, "CheckedReference");
                    }
                    if (facts.ReferenceObligations[target.Identity] is { } nestedObligations)
                    {
                        foreach (var obligation in nestedObligations)
                        {
                            ValidateReferenceObligation(obligation, facts, state,
                                function, program, referenceRead.Span, fieldMasks);
                            _ = result.Add(obligation);
                        }
                    }
                }
                break;
            case BoundCoalesce coalesce:
                result.UnionWith(ReferenceObligationsOfExpression(
                    coalesce.Optional, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                result.UnionWith(ReferenceObligationsOfExpression(
                    coalesce.Fallback, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                break;
            case BoundConditional conditional:
                result.UnionWith(ReferenceObligationsOfExpression(
                    conditional.WhenTrue, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                result.UnionWith(ReferenceObligationsOfExpression(
                    conditional.WhenFalse, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                break;
            case BoundUnsafeExpression unsafeExpression:
                result = ReferenceObligationsOfExpression(
                    unsafeExpression.Expression, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                break;
            default:
                throw InvalidEffect(expression.Span, "CheckedReference");
        }
        foreach (var obligation in result)
        {
            ValidateReferenceObligation(obligation, facts, state,
                function, program, expression.Span, fieldMasks);
        }
        return result;
    }

    private static HashSet<ProvenanceAnchor> ReferenceDescriptorPlacesOfExpression(
        BoundExpression expression, BackingDependencies facts, InitializationState state,
        BoundFunction function, BoundProgram program, ref int remaining, int depth,
        uint[]? fieldMasks = null)
    {
        Count(ref remaining, depth);
        if (expression is null
            || !CheckedReferenceTypeContract.IsValidType(
                expression.Type, program.CheckedReferenceTypes))
        {
            throw InvalidEffect(expression?.Span ?? default, "CheckedReference");
        }

        var result = new HashSet<ProvenanceAnchor>();
        var type = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(expression.Type)];
        switch (expression)
        {
            case BoundNull when type.IsOptional:
                break;
            case BoundOptionalLift lift when type.IsOptional:
                if (!SourceOptionalTypes.TryGetRequired(
                    expression.Type, program.CheckedReferenceTypes,
                    out var liftedType) || lift.Value.Type != liftedType)
                {
                    throw InvalidEffect(lift.Span, "CheckedReference");
                }
                result = ReferenceDescriptorPlacesOfExpression(
                    lift.Value, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                break;
            case BoundRead read:
                if ((uint)read.Variable >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != expression.Type
                    || !state[read.Variable] || state.HasRetiredValue(read.Variable))
                {
                    throw InvalidEffect(read.Span, "CheckedReference");
                }
                if (IsReferenceParameterSlot(function, read.Variable))
                {
                    _ = result.Add(ProvenanceAnchor.ForIncomingReferenceDescriptor(
                        read.Variable));
                    break;
                }
                var instances = facts.StorageInstanceSet(read.Variable);
                if (instances.Count != 1)
                {
                    throw InvalidEffect(read.Span, "CheckedReference");
                }
                foreach (var instance in instances)
                {
                    if (!instance.IsKnown)
                    {
                        throw InvalidEffect(read.Span, "CheckedReference");
                    }
                    _ = result.Add(ProvenanceAnchor.ForReferenceDescriptor(
                        facts.FunctionActivation, read.Variable, instance));
                }
                break;
            case BoundFieldRead field:
                if (field.Type != expression.Type
                    || !TryGetReferencePlaceStorage(field, function, program,
                        out var variable, out var projection)
                    || projection.Kind != StorageProjectionKind.InlineFieldPath
                    || !ReferenceTargetPlaceInitialized(variable, projection,
                        state, fieldMasks, function, program))
                {
                    throw InvalidEffect(field.Span, "CheckedReference");
                }
                var fieldInstances = facts.StorageInstanceSet(variable);
                if (fieldInstances.Count != 1)
                {
                    throw InvalidEffect(field.Span, "CheckedReference");
                }
                foreach (var instance in fieldInstances)
                {
                    if (!instance.IsKnown)
                    {
                        throw InvalidEffect(field.Span, "CheckedReference");
                    }
                    _ = result.Add(ProvenanceAnchor.ForReferenceDescriptor(
                        facts.FunctionActivation, variable, instance, projection,
                        state.FieldPlaceGeneration(variable, projection)));
                }
                break;
            case BoundReferenceReborrow reborrow:
                result = ReferenceDescriptorPlacesOfExpression(
                    reborrow.Descriptor, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                break;
            case BoundReferenceRead referenceRead:
                foreach (var target in ReferenceTargetsOfExpression(
                    referenceRead.Descriptor, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks))
                {
                    ValidateReferenceTarget(target, expression.Type, facts,
                        state, function, program, referenceRead.Span, fieldMasks);
                    if (target.Kind == ProvenanceAnchorKind.IncomingReference)
                    {
                        _ = result.Add(ProvenanceAnchor.ForIncomingReferenceDescriptor(
                            target.Identity));
                        continue;
                    }
                    if (target.Kind == ProvenanceAnchorKind.IncomingReferenceContents)
                    {
                        _ = result.Add(ProvenanceAnchor.ForIncomingReferenceDescriptor(
                            target.Identity));
                        continue;
                    }
                    if (target.Kind != ProvenanceAnchorKind.ReferencePlace
                        || target.Storage is not StorageKey key)
                    {
                        throw InvalidEffect(referenceRead.Span, "CheckedReference");
                    }
                    _ = result.Add(ProvenanceAnchor.ForReferenceDescriptor(
                        facts.FunctionActivation, target.Identity, key.Instance));
                }
                break;
            case BoundCoalesce coalesce:
                result.UnionWith(ReferenceDescriptorPlacesOfExpression(
                    coalesce.Optional, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                result.UnionWith(ReferenceDescriptorPlacesOfExpression(
                    coalesce.Fallback, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                break;
            case BoundConditional conditional:
                result.UnionWith(ReferenceDescriptorPlacesOfExpression(
                    conditional.WhenTrue, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                result.UnionWith(ReferenceDescriptorPlacesOfExpression(
                    conditional.WhenFalse, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks));
                break;
            case BoundUnsafeExpression unsafeExpression:
                result = ReferenceDescriptorPlacesOfExpression(
                    unsafeExpression.Expression, facts, state, function, program,
                    ref remaining, depth + 1, fieldMasks);
                break;
            default:
                throw InvalidEffect(expression.Span, "CheckedReference");
        }
        return result;
    }

    private static bool IsReferenceParameterSlot(BoundFunction function, int slot)
        => slot < function.ParameterCount + (function.IsConstructor ? 1 : 0);

    private static void ValidateReferenceObligation(ProvenanceAnchor obligation,
        BackingDependencies facts, InitializationState state, BoundFunction function,
        BoundProgram program, TextSpan span, uint[]? fieldMasks = null)
    {
        if (obligation.Kind == ProvenanceAnchorKind.IncomingReferenceDescriptor)
        {
            var parameterCount = function.ParameterCount + (function.IsConstructor ? 1 : 0);
            if ((uint)obligation.Identity >= (uint)parameterCount
                || !CheckedReferenceTypeContract.IsValidType(
                    function.Variables[obligation.Identity].Type, program.CheckedReferenceTypes)
                || !state[obligation.Identity])
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            return;
        }
        if (obligation.Kind != ProvenanceAnchorKind.ReferenceDescriptor
            || obligation.Storage is not StorageKey key || !key.IsKnown
            || key.Activation != facts.FunctionActivation
            || key.Site != obligation.Identity
            || key.Projection.Kind is not (StorageProjectionKind.WholeObject
                or StorageProjectionKind.InlineFieldPath)
            || (uint)obligation.Identity >= (uint)function.Variables.Length
            || !TryGetReferencePlaceType(obligation.Identity, key.Projection,
                function, program, out var descriptorType)
            || !CheckedReferenceTypeContract.IsValidType(
                descriptorType, program.CheckedReferenceTypes)
            || !state[obligation.Identity] || state.HasRetiredValue(obligation.Identity))
        {
            throw InvalidEffect(span, "CheckedReference");
        }
        if (key.Projection.Kind == StorageProjectionKind.InlineFieldPath
            && (key.PlaceGeneration
                    != state.FieldPlaceGeneration(obligation.Identity, key.Projection)
                || !ReferenceTargetPlaceInitialized(obligation.Identity,
                    key.Projection, state, fieldMasks, function, program)))
        {
            throw InvalidEffect(span, "CheckedReference");
        }
        var instances = facts.StorageInstanceSet(obligation.Identity);
        if (instances.Count != 1 || !instances.Contains(key.Instance))
        {
            throw InvalidEffect(span, "CheckedReference");
        }
    }

    private static bool TryGetReferencePlaceType(int variable,
        StorageProjection projection, BoundFunction function, BoundProgram program,
        out PrimitiveType type)
    {
        if ((uint)variable >= (uint)function.Variables.Length)
        {
            type = PrimitiveType.Int32;
            return false;
        }

        var storage = function.Variables[variable];
        if (projection.Kind is StorageProjectionKind.ArrayElement
            or StorageProjectionKind.UnknownArrayElement)
        {
            if (storage.NativeFixedArrayLayout is not BoundNativeFixedArrayLayout layout
                || !NativeFixedArrayContract.MatchesLayout(layout, program))
            {
                type = PrimitiveType.Int32;
                return false;
            }
            type = layout.ElementType;
        }
        else
        {
            type = storage.Type;
        }

        if (projection.Kind == StorageProjectionKind.WholeObject)
        {
            return true;
        }
        if (projection.Kind is not (StorageProjectionKind.InlineFieldPath
                or StorageProjectionKind.ArrayElement
                or StorageProjectionKind.UnknownArrayElement)
            || (projection.Kind == StorageProjectionKind.InlineFieldPath
                && projection.FieldPath.Segments.IsEmpty))
        {
            return false;
        }
        foreach (var segment in projection.FieldPath.Segments)
        {
            if (type != segment.OwnerType || !HasFields(program, type)
                || (uint)segment.Field >= (uint)Fields(program, type).Length)
            {
                return false;
            }
            type = Fields(program, type)[segment.Field].Type;
        }
        return true;
    }

    private static void ValidateReferenceTarget(ProvenanceAnchor target,
        PrimitiveType referentType, BackingDependencies facts, InitializationState state,
        BoundFunction function, BoundProgram program, TextSpan span,
        uint[]? fieldMasks = null)
    {
        if (target.Kind == ProvenanceAnchorKind.IncomingReferenceContents)
        {
            var parameterCount = function.ParameterCount + (function.IsConstructor ? 1 : 0);
            if ((uint)target.Identity >= (uint)parameterCount
                || target.IncomingReferenceDepth is <= 0 or > MaximumDepth
                || !state[target.Identity])
            {
                throw InvalidEffect(span, "CheckedReference");
            }

            var currentType = function.Variables[target.Identity].Type;
            for (var depth = 0; depth < target.IncomingReferenceDepth; depth++)
            {
                if (!CheckedReferenceTypeContract.IsValidType(
                        currentType, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                var reference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(currentType)];
                if (reference.IsGarbageCollected || reference.IsInitialization
                    || reference.IsOptional)
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                currentType = reference.ReferentType;
            }
            if (!CheckedReferenceTypeContract.IsValidType(
                    currentType, program.CheckedReferenceTypes))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            var contentsReference = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(currentType)];
            if (contentsReference.IsGarbageCollected
                || contentsReference.IsInitialization
                || contentsReference.IsOptional
                || contentsReference.ReferentType != referentType)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            return;
        }
        if (target.Kind == ProvenanceAnchorKind.IncomingReference)
        {
            var parameterCount = function.ParameterCount + (function.IsConstructor ? 1 : 0);
            if ((uint)target.Identity >= (uint)parameterCount
                || !CheckedReferenceTypeContract.IsValidType(
                    function.Variables[target.Identity].Type, program.CheckedReferenceTypes)
                || !state[target.Identity])
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            var incomingType = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(function.Variables[target.Identity].Type)];
            if (incomingType.IsGarbageCollected || incomingType.IsInitialization
                || incomingType.ReferentType != referentType)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            return;
        }
        if (target.Kind == ProvenanceAnchorKind.IncomingReferencePlace)
        {
            var parameterCount = function.ParameterCount
                + (function.IsConstructor ? 1 : 0);
            if ((uint)target.Identity >= (uint)parameterCount
                || !state[target.Identity] || state.HasRetiredValue(target.Identity)
                || function.Variables[target.Identity].IsOut
                || function.Variables[target.Identity].IsInit
                || function.Variables[target.Identity].IsInitializationLocal
                || target.IncomingProjection is not StorageProjection projection
                || target.IncomingReferenceDepth < 0
                || target.IncomingReferenceDepth > MaximumDepth)
            {
                throw InvalidEffect(span, "CheckedReference");
            }

            var rootType = function.Variables[target.Identity].Type;
            if (target.IncomingReferenceDepth == 0)
            {
                if (target.Identity != 0 || function.DeclaringStruct is null
                    || function.IsStatic || function.IsConstructor
                    || function.IsClassConstructor
                    || ManagedClassTypes.IsClass(rootType)
                    || !IsInlineStructStorage(rootType, program))
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
            }
            else
            {
                for (var depth = 0; depth < target.IncomingReferenceDepth; depth++)
                {
                    if (!CheckedReferenceTypeContract.IsValidType(
                            rootType, program.CheckedReferenceTypes))
                    {
                        throw InvalidEffect(span, "CheckedReference");
                    }
                    var inputReference = program.CheckedReferenceTypes[
                        CheckedReferenceTypes.Index(rootType)];
                    if (inputReference.IsGarbageCollected
                        || inputReference.IsInitialization || inputReference.IsOptional)
                    {
                        throw InvalidEffect(span, "CheckedReference");
                    }
                    rootType = inputReference.ReferentType;
                }
            }

            var incomingPlaceType = projection.Kind == StorageProjectionKind.WholeObject
                ? rootType
                : TryGetReferenceFieldType(rootType, projection,
                    program, out var projectedType)
                    ? projectedType : PrimitiveType.Void;
            if (incomingPlaceType != referentType)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            return;
        }
        if (target.Kind == ProvenanceAnchorKind.IncomingReferenceField)
        {
            var parameterCount = function.ParameterCount + (function.IsConstructor ? 1 : 0);
            if ((uint)target.Identity >= (uint)parameterCount
                || !state[target.Identity] || state.HasRetiredValue(target.Identity)
                || function.Variables[target.Identity].IsOut
                || function.Variables[target.Identity].IsInit
                || function.Variables[target.Identity].IsInitializationLocal
                || target.IncomingProjection is not StorageProjection projection
                || target.IncomingReferenceDepth < 0
                || target.IncomingReferenceDepth > MaximumDepth)
            {
                throw InvalidEffect(span, "CheckedReference");
            }

            var rootType = function.Variables[target.Identity].Type;
            for (var depth = 0; depth < target.IncomingReferenceDepth; depth++)
            {
                if (!CheckedReferenceTypeContract.IsValidType(
                        rootType, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                var inputReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(rootType)];
                if (inputReference.IsGarbageCollected
                    || inputReference.IsInitialization || inputReference.IsOptional)
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                rootType = inputReference.ReferentType;
            }
            if (!TryGetReferenceFieldType(rootType, projection,
                    program, out var storedDescriptor)
                || !CheckedReferenceTypeContract.IsValidType(
                    storedDescriptor, program.CheckedReferenceTypes))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            var storedReference = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(storedDescriptor)];
            if (storedReference.IsGarbageCollected || storedReference.IsInitialization
                || storedReference.ReferentType != referentType)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            return;
        }
        if (target.Kind == ProvenanceAnchorKind.NormalBackingPlace)
        {
            if (target.Storage is not StorageKey normalBackingKey
                || !normalBackingKey.IsKnown
                || normalBackingKey.Activation != facts.FunctionActivation
                || normalBackingKey.Site != target.Identity
                || normalBackingKey.PlaceGeneration == StorageKey.UnknownGeneration
                || (uint)target.Identity >= (uint)function.Variables.Length
                || !TryGetNormalBackingClassPlaceType(target.Identity,
                    normalBackingKey.Projection, function, program,
                    out var normalBackingType)
                || normalBackingType != referentType)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            return;
        }
        if (target.Kind == ProvenanceAnchorKind.NormalBackingObjectPlace)
        {
            if (target.Storage is not StorageKey objectPlaceKey
                || !objectPlaceKey.IsKnown
                || objectPlaceKey.Activation != facts.FunctionActivation
                || objectPlaceKey.Site != target.Identity
                || objectPlaceKey.PlaceGeneration
                    == StorageKey.UnknownGeneration
                || !ManagedClassTypes.IsClass(target.ObjectType)
                || !TryGetNormalBackingObjectPlaceType(
                    target.ObjectType, objectPlaceKey.Projection,
                    program, out var objectPlaceType)
                || objectPlaceType != referentType)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            return;
        }
        if (target.Kind != ProvenanceAnchorKind.ReferencePlace
            || target.Storage is not StorageKey key || !key.IsKnown
            || key.Activation != facts.FunctionActivation
            || key.Site != target.Identity
            || (uint)target.Identity >= (uint)function.Variables.Length
            || !TryGetReferencePlaceType(target.Identity, key.Projection,
                function, program, out var actualType)
            || actualType != referentType
            || key.PlaceGeneration
                != state.FieldPlaceGeneration(target.Identity, key.Projection)
            || !ReferenceTargetPlaceInitialized(target.Identity, key.Projection,
                state, fieldMasks, function, program))
        {
            throw InvalidEffect(span, "CheckedReference");
        }
        var instances = facts.StorageInstanceSet(target.Identity);
        if (instances.Count != 1 || !instances.Contains(key.Instance))
        {
            throw InvalidEffect(span, "CheckedReference");
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
            case BoundReferenceFormation formation:
                if (!CheckedReferenceTypeContract.IsValidType(
                        formation.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(formation.Span, "Ownership");
                }
                var formedReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(formation.Type)];
                if (formedReference.IsGarbageCollected || formedReference.IsInitialization
                    || formedReference.IsOptional || formedReference.ReferentType != formation.Place.Type)
                {
                    throw InvalidEffect(formation.Span, "Ownership");
                }
                return BackingOf(formation.Place, facts, program, ref remaining, depth + 1);
            case BoundReferenceRead referenceRead:
                if (!CheckedReferenceTypeContract.IsValidType(
                        referenceRead.Descriptor.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(referenceRead.Span, "Ownership");
                }
                var readReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(referenceRead.Descriptor.Type)];
                if (readReference.IsGarbageCollected || readReference.IsInitialization
                    || readReference.IsOptional || readReference.ReferentType != referenceRead.Type
                    || !TokenSpanContract.IsDependent(referenceRead.Type, program.Structures))
                {
                    throw InvalidEffect(referenceRead.Span, "Ownership");
                }
                var referenceBacking = new HashSet<ProvenanceAnchor>();
                foreach (var target in ReferenceTargetsSummary(
                    referenceRead.Descriptor, facts, facts.Function, program,
                    ref remaining, depth + 1))
                {
                    if (target.Kind == ProvenanceAnchorKind.ReferencePlace
                        && (uint)target.Identity < (uint)facts.Function.Variables.Length
                        && facts.Function.Variables[target.Identity].Type == referenceRead.Type)
                    {
                        var targetProjection = target.Storage?.Projection
                            ?? throw Invalid(referenceRead.Span);
                        if (targetProjection.Kind == StorageProjectionKind.WholeObject)
                        {
                            referenceBacking.UnionWith(IsTokenSnapshotType(
                                    referenceRead.Type, program)
                                ? TokenSnapshotBackingOf(target.Identity, facts)
                                : BackingSourcesOf(target.Identity, referenceRead.Type,
                                    facts, program, referenceRead.Span));
                        }
                        else if (targetProjection.Kind == StorageProjectionKind.InlineFieldPath
                            && facts.TryGetBackingFieldValue(target.Identity,
                                targetProjection, out var fieldValue)
                            && fieldValue.IsAvailable)
                        {
                            referenceBacking.UnionWith(BackingFieldValueWithBounds(
                                target.Identity, fieldValue, facts).Sources);
                        }
                        else if (targetProjection.Kind == StorageProjectionKind.InlineFieldPath
                            && facts.BackingBounds[target.Identity] is { Count: > 0 }
                            && facts.Sources[target.Identity] is { Count: > 0 } boundedSources)
                        {
                            referenceBacking.UnionWith(boundedSources);
                        }
                        else
                        {
                            throw InvalidEffect(referenceRead.Span, "Ownership");
                        }
                    }
                    else if (target.Kind is ProvenanceAnchorKind.IncomingReference
                            or ProvenanceAnchorKind.IncomingReferenceContents
                        && (uint)target.Identity < (uint)facts.Function.ParameterCount
                            + (facts.Function.IsConstructor ? 1u : 0u))
                    {
                        if (facts.ReferenceBackingSources[target.Identity] is
                            { Count: > 0 } capturedSources)
                        {
                            referenceBacking.UnionWith(capturedSources);
                        }
                        else
                        {
                            _ = referenceBacking.Add(
                                ProvenanceAnchor.ForBackingSource(~target.Identity));
                        }
                    }
                    else
                    {
                        throw InvalidEffect(referenceRead.Span, "Ownership");
                    }
                }
                if (referenceBacking.Count == 0)
                {
                    throw InvalidEffect(referenceRead.Span, "Ownership");
                }
                return referenceBacking;
            case BoundSnapshotSpan snapshot when (uint)snapshot.Variable < (uint)facts.Sources.Length:
                return facts.Sources[snapshot.Variable] ?? new HashSet<ProvenanceAnchor>
                {
                    ProvenanceAnchor.ForBackingSource(snapshot.Variable),
                };
            case BoundRead read when (uint)read.Variable < (uint)facts.Sources.Length:
                return BackingSourcesOf(read.Variable, read.Type, facts, program, read.Span);
            case BoundMove move when (uint)move.Variable < (uint)facts.Sources.Length:
                return BackingSourcesOf(move.Variable, move.Type, facts, program, move.Span);
            case BoundFieldMove move when facts.TryGetBackingCallResult(
                move, StorageProjection.WholeObject, out var movedBacking):
                return movedBacking.Sources;
            case BoundReceiverLoan loan when (uint)loan.Variable < (uint)facts.Sources.Length:
                return BackingSourcesOf(loan.Variable, loan.Type, facts, program, loan.Span);
            case BoundMutableReceiverLoan loan when (uint)loan.Variable < (uint)facts.Sources.Length:
                return BackingSourcesOf(loan.Variable, loan.Type, facts, program, loan.Span);
            case BoundFieldRead field:
                if (TryGetNativePointerFieldPlace(field, facts.Function, program,
                    out var variable, out var projection))
                {
                    if (projection.Kind == StorageProjectionKind.InlineFieldPath
                        && facts.TryGetBackingFieldValue(variable, projection, out var fieldValue)
                        && fieldValue.IsAvailable)
                    {
                        return BackingFieldValueWithBounds(
                            variable, fieldValue, facts).Sources;
                    }
                    if (facts.BackingBounds[variable] is { Count: > 0 }
                        && facts.Sources[variable] is { Count: > 0 } boundedSources)
                    {
                        return boundedSources;
                    }
                    if (!facts.BackingMayBeUnknown[variable]
                        && BackingFieldLeaves(field.Receiver.Type, program,
                            ref remaining, depth + 1).Count == 1
                        && facts.Sources[variable] is { Count: > 0 } aggregateSources)
                    {
                        return aggregateSources;
                    }
                    throw InvalidEffect(field.Span, "Ownership");
                }
                if (BackingFieldLeaves(field.Receiver.Type, program,
                    ref remaining, depth + 1).Count == 1)
                {
                    return BackingOf(field.Receiver, facts, program,
                        ref remaining, depth + 1);
                }
                throw InvalidEffect(field.Span, "Ownership");
            case BoundUnsafeExpression unsafeExpression:
                return BackingOf(unsafeExpression.Expression, facts, program, ref remaining, depth + 1);
            case BoundSpanAdvance advance:
                return BackingOf(advance.View, facts, program, ref remaining, depth + 1);
            case BoundCall call when facts.TryGetBackingCallResult(
                call, StorageProjection.WholeObject, out var callBacking):
                return callBacking.Sources;
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
        BoundProgram program, ref int remaining, int depth, uint[]? fieldMasks = null)
    {
        if (initialized.Dependencies is not BackingDependencies dependencies)
        {
            return;
        }

        var hasBacking = TokenSpanContract.IsDependent(argument.Type, program.Structures);
        var hasReference = CheckedReferenceTypeContract.IsValidType(
            argument.Type, program.CheckedReferenceTypes);
        var hasReferenceFields = IsInlineStructStorage(argument.Type, program)
            && ReferenceFieldLeaves(argument.Type, program, ref remaining, depth + 1).Count != 0;
        var resultDependency = ResultDependencyContract(target);
        var hasReferenceResultSource =
            CheckedReferenceTypeContract.IsValidType(
                target.ReturnType, program.CheckedReferenceTypes)
            && resultDependency?.Sources.Any(source =>
                DependencyCallParameterIndex(target, source) == parameterIndex) == true;
        var hasNativePointer = CarriesNativePointer(argument, program);
        if (!hasBacking && !hasReference && !hasReferenceFields
            && !hasReferenceResultSource && !hasNativePointer
            && target.NativeOutputContracts.IsEmpty)
        {
            return;
        }

        var backingSources = hasBacking
            ? new HashSet<ProvenanceAnchor>(BackingOf(argument, dependencies,
                program, ref remaining, depth + 1))
            : [];
        if (hasReference)
        {
            var referenceValue = ReferenceFieldValueOfDescriptorExpression(
                argument, dependencies, initialized, dependencies.Function,
                program, ref remaining, depth + 1, fieldMasks);
            backingSources.UnionWith(referenceValue.BackingSources);
        }
        var referenceTargets = hasReference
            ? ReferenceTargetsOfExpression(argument, dependencies, initialized,
                dependencies.Function, program, ref remaining, depth + 1, fieldMasks)
            : EmptyBackingSources;
        var referenceObligations = hasReference
            ? ReferenceObligationsOfExpression(argument, dependencies, initialized,
                dependencies.Function, program, ref remaining, depth + 1, fieldMasks)
            : EmptyBackingSources;
        var referenceFieldValues = hasReferenceFields
            ? ReferenceFieldValuesOfExpression(argument, argument.Type, dependencies,
                initialized, dependencies.Function, program, ref remaining,
                depth + 1, fieldMasks)
            : EmptyReferenceFieldValues;
        var nativePointerSources = hasNativePointer
            ? NativePointerBackingOf(argument, dependencies, program, ref remaining, depth + 1)
            : EmptyBackingSources;
        var nativePointerMayBeUnknown = hasNativePointer
            && NativePointerMayBeUnknownOf(argument, dependencies, program,
                ref remaining, depth + 1);
        var nativePointerExtent = hasNativePointer
            ? NativePointerExtentFactsOfExpression(argument, dependencies,
                dependencies.Function, program, ref remaining, depth + 1)
            : new NativePointerExtentFact([], MayBeOpaque: false, IsUnavailable: false);
        nativePointerExtent = nativePointerExtent with {
            Sources = [.. nativePointerExtent.Sources],
        };
        dependencies.Staged.Add(new StagedDependencySource(call, parameterIndex,
            [.. backingSources], [.. referenceTargets], [.. referenceObligations],
            referenceFieldValues,
            [.. nativePointerSources],
            nativePointerMayBeUnknown,
            nativePointerExtent));
    }

    private static void StageIndirectDependencySource(BoundIndirectCall call,
        int parameterIndex, BoundExpression argument,
        NativeFunctionPointerSignatureDefinition signature,
        InitializationState initialized, BoundProgram program,
        ref int remaining, int depth)
    {
        if (initialized.Dependencies is not BackingDependencies dependencies)
        {
            return;
        }

        var hasDependency = signature.Dependencies.Any(dependency =>
                dependency.DestinationKind == 2
                && dependency.SourceKind == 1
                && dependency.SourceOrdinal == (uint)parameterIndex);
        var hasNativeParameter = signature.NativeParameters.Any(
            parameter => parameter.ParameterIndex == parameterIndex);
        var hasReferenceFields = IsInlineStructStorage(argument.Type, program)
            && ReferenceFieldLeaves(argument.Type, program, ref remaining, depth + 1).Count != 0;
        if (!hasDependency && !hasNativeParameter && !hasReferenceFields)
        {
            return;
        }

        var hasBacking = TokenSpanContract.IsDependent(argument.Type, program.Structures);
        var hasNativePointer = CarriesNativePointer(argument, program);
        var backingSources = hasBacking
            ? BackingOf(argument, dependencies, program, ref remaining, depth + 1)
            : EmptyBackingSources;
        var referenceFieldValues = hasReferenceFields
            ? ReferenceFieldValuesOfExpression(argument, argument.Type, dependencies,
                initialized, dependencies.Function, program, ref remaining, depth + 1)
            : EmptyReferenceFieldValues;
        var nativePointerSources = hasNativePointer
            ? NativePointerBackingOf(argument, dependencies, program, ref remaining, depth + 1)
            : EmptyBackingSources;
        var nativePointerMayBeUnknown = hasNativePointer
            && NativePointerMayBeUnknownOf(argument, dependencies, program,
                ref remaining, depth + 1);
        var nativePointerExtent = hasNativePointer
            ? NativePointerExtentFactsOfExpression(argument, dependencies,
                dependencies.Function, program, ref remaining, depth + 1)
            : new NativePointerExtentFact([], MayBeOpaque: false, IsUnavailable: false);
        nativePointerExtent = nativePointerExtent with {
            Sources = [.. nativePointerExtent.Sources],
        };
        dependencies.Staged.Add(new StagedDependencySource(call, parameterIndex,
            [.. backingSources], [], [], referenceFieldValues, [.. nativePointerSources],
            nativePointerMayBeUnknown, nativePointerExtent));
    }

    private static void CheckBackingInvalidation(int owner, InitializationState state,
        TextSpan span, IReadOnlySet<ProvenanceAnchor>? preservedTargets = null)
    {
        if (state.Dependencies is not BackingDependencies facts)
        {
            return;
        }
        for (var slot = 0; slot < state.Length; slot++)
        {
            if ((state.Possibilities(slot) & (2 | 8)) != 0)
            {
                if ((facts.BackingMayBeUnknown[slot]
                        && facts.BackingBounds[slot] is not { Count: > 0 })
                    || facts.Sources[slot]?.Contains(
                        ProvenanceAnchor.ForBackingSource(owner)) == true
                    || (facts.ReferenceBackingSources[slot]?.Contains(
                            ProvenanceAnchor.ForBackingSource(owner)) == true
                        && (facts.ReferenceTargets[slot] is not { } targets
                            || targets.Any(target => preservedTargets is null
                                || !preservedTargets.Contains(target)))))
                {
                    throw InvalidEffect(span, "Ownership");
                }
            }
        }
        var ownerSource = ProvenanceAnchor.ForBackingSource(owner);
        if (facts.Staged.Any(staged => staged.BackingSources.Contains(ownerSource))
            || facts.IncomingReferenceFieldValues.Values.Any(value =>
                value.BackingSources.Contains(ownerSource))
            || facts.NormalBackingReferenceFieldValuesSnapshot().Any(value =>
                value.BackingSources.Contains(ownerSource)))
        {
            throw InvalidEffect(span, "Ownership");
        }
    }

    private static void CheckBackingFieldInvalidation(int root,
        StorageProjection projection, InitializationState state, TextSpan span,
        IReadOnlySet<ProvenanceAnchor>? preservedTargets = null)
    {
        if (state.Dependencies is not BackingDependencies facts)
        {
            return;
        }
        if ((uint)root >= (uint)state.Length
            || projection.Kind != StorageProjectionKind.InlineFieldPath
            || projection.FieldPath.Segments.IsEmpty)
        {
            throw InvalidEffect(span, "Ownership");
        }

        var rootSource = ProvenanceAnchor.ForBackingSource(root);
        for (var slot = 0; slot < state.Length; slot++)
        {
            if ((state.Possibilities(slot) & (2 | 8)) == 0)
            {
                continue;
            }
            if ((facts.BackingMayBeUnknown[slot]
                    && facts.BackingBounds[slot] is not { Count: > 0 })
                || facts.Sources[slot]?.Contains(rootSource) == true)
            {
                throw InvalidEffect(span, "Ownership");
            }

            var referenceTargets = facts.ReferenceTargets[slot];
            if (facts.ReferenceBackingSources[slot]?.Contains(rootSource) == true
                && (referenceTargets is null || referenceTargets.Any(target =>
                    target.Kind != ProvenanceAnchorKind.ReferencePlace
                    || target.Identity != root
                    || (ReferenceTargetOverlapsField(root, projection, target, facts)
                        && (preservedTargets is null
                            || !preservedTargets.Contains(target))))))
            {
                throw InvalidEffect(span, "Ownership");
            }
        }

        if (facts.Staged.Any(staged => staged.BackingSources.Contains(rootSource)))
        {
            throw InvalidEffect(span, "Ownership");
        }
        if (facts.NormalBackingReferenceFieldValuesSnapshot().Any(value =>
                value.BackingSources.Contains(rootSource)
                || value.Targets.Any(target =>
                    ReferenceTargetOverlapsField(root, projection, target, facts)
                    && (preservedTargets is null
                        || !preservedTargets.Contains(target)))))
        {
            throw InvalidEffect(span, "Ownership");
        }
    }

    private static void CheckReferencePlaceInvalidation(int root,
        StorageProjection projection, InitializationState state, TextSpan span,
        IReadOnlySet<ProvenanceAnchor>? preservedTargets = null)
    {
        if (state.Dependencies is not BackingDependencies facts)
        {
            return;
        }
        if ((uint)root >= (uint)state.Length
            || (projection.Kind != StorageProjectionKind.WholeObject
                && (projection.Kind != StorageProjectionKind.InlineFieldPath
                    || projection.FieldPath.Segments.IsEmpty)))
        {
            throw InvalidEffect(span, "CheckedReference");
        }

        var rootSource = ProvenanceAnchor.ForBackingSource(root);
        for (var slot = 0; slot < state.Length; slot++)
        {
            if ((state.Possibilities(slot) & 2) == 0
                || !HasActiveStorageInstance(slot, facts, state))
            {
                continue;
            }
            var targets = facts.ReferenceTargets[slot];
            if (targets is not null && targets.Any(target =>
                ReferenceTargetOverlapsField(root, projection, target, facts)
                && (preservedTargets is null || !preservedTargets.Contains(target))))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            if (facts.NativePointerSources[slot]?.Any(target =>
                NativePointerTargetOverlapsField(root, projection, target, facts)) == true)
            {
                throw InvalidEffect(span, "NativePointer");
            }
            if (facts.ReferenceBackingSources[slot]?.Contains(rootSource) == true
                && (targets is null || targets.Any(target =>
                    target.Kind != ProvenanceAnchorKind.ReferencePlace
                    || target.Identity != root)))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            if (facts.ReferenceBackingSources[slot]?.Any(source =>
                    source.Kind == ProvenanceAnchorKind.ReferencePlace
                    && ReferenceTargetOverlapsField(
                        root, projection, source, facts)) == true)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            if (facts.ReferenceObligations[slot]?.Any(obligation =>
                    ReferenceDescriptorOverlapsField(
                        root, projection, obligation, facts)) == true)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
        foreach (var (key, value) in facts.ReferenceFieldValues)
        {
            if ((uint)key.Site >= (uint)state.Length
                || (state.Possibilities(key.Site) & 2) == 0
                || !HasActiveStorageInstance(key.Site, facts, state)
                || (key.Site == root
                    && StorageProjectionsOverlap(key.Projection, projection)))
            {
                continue;
            }
            if (value.Targets.Any(target =>
                    ReferenceTargetOverlapsField(root, projection, target, facts)
                    && (preservedTargets is null
                        || !preservedTargets.Contains(target)))
                || value.Obligations.Any(obligation =>
                    ReferenceDescriptorOverlapsField(
                        root, projection, obligation, facts))
                || value.BackingSources.Contains(rootSource))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
        foreach (var value in facts.NormalBackingReferenceFieldValuesSnapshot())
        {
            if (value.Targets.Any(target =>
                    ReferenceTargetOverlapsField(root, projection, target, facts)
                    && (preservedTargets is null
                        || !preservedTargets.Contains(target)))
                || value.Obligations.Any(obligation =>
                    ReferenceDescriptorOverlapsField(
                        root, projection, obligation, facts))
                || value.BackingSources.Contains(rootSource))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
        foreach (var value in facts.IncomingReferenceFieldValues.Values)
        {
            if (value.Targets.Any(target =>
                    ReferenceTargetOverlapsField(root, projection, target, facts)
                    && (preservedTargets is null
                        || !preservedTargets.Contains(target)))
                || value.Obligations.Any(obligation =>
                    ReferenceDescriptorOverlapsField(
                        root, projection, obligation, facts))
                || value.BackingSources.Contains(rootSource))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }

        foreach (var staged in facts.Staged)
        {
            if (staged.ReferenceTargets.Any(target =>
                    ReferenceTargetOverlapsField(root, projection, target, facts)
                    && (preservedTargets is null || !preservedTargets.Contains(target)))
                || staged.ReferenceFieldValues.Values.Any(value =>
                    value.Targets.Any(target =>
                        ReferenceTargetOverlapsField(root, projection, target, facts)
                        && (preservedTargets is null
                            || !preservedTargets.Contains(target)))
                    || value.Obligations.Any(obligation =>
                        ReferenceDescriptorOverlapsField(
                            root, projection, obligation, facts))
                    || value.BackingSources.Contains(rootSource))
                || staged.NativePointerSources.Any(target =>
                    NativePointerTargetOverlapsField(root, projection, target, facts))
                || staged.ReferenceObligations.Any(obligation =>
                    ReferenceDescriptorOverlapsField(
                        root, projection, obligation, facts))
                || (staged.BackingSources.Contains(rootSource)
                    && staged.ReferenceTargets.Count == 0))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
    }

    private static void CheckReferenceDescriptorInvalidation(int root,
        StorageProjection projection, InitializationState state, TextSpan span)
    {
        if (state.Dependencies is not BackingDependencies facts)
        {
            return;
        }
        if ((uint)root >= (uint)state.Length
            || (projection.Kind != StorageProjectionKind.WholeObject
                && (projection.Kind != StorageProjectionKind.InlineFieldPath
                    || projection.FieldPath.Segments.IsEmpty)))
        {
            throw InvalidEffect(span, "CheckedReference");
        }

        for (var slot = 0; slot < state.Length; slot++)
        {
            if ((state.Possibilities(slot) & 2) != 0
                && HasActiveStorageInstance(slot, facts, state)
                && facts.ReferenceObligations[slot]?.Any(obligation =>
                    ReferenceDescriptorOverlapsField(
                        root, projection, obligation, facts)) == true)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
        foreach (var (key, value) in facts.ReferenceFieldValues)
        {
            if ((uint)key.Site >= (uint)state.Length
                || (state.Possibilities(key.Site) & 2) == 0
                || !HasActiveStorageInstance(key.Site, facts, state)
                || (key.Site == root
                    && StorageProjectionsOverlap(key.Projection, projection)))
            {
                continue;
            }
            if (value.Obligations.Any(obligation =>
                    ReferenceDescriptorOverlapsField(
                        root, projection, obligation, facts)))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
        if (facts.NormalBackingReferenceFieldValuesSnapshot().Any(
                value => value.Obligations.Any(obligation =>
                    ReferenceDescriptorOverlapsField(
                        root, projection, obligation, facts))))
        {
            throw InvalidEffect(span, "CheckedReference");
        }
        if (facts.IncomingReferenceFieldValues.Values.Any(value =>
                value.Obligations.Any(obligation =>
                    ReferenceDescriptorOverlapsField(
                        root, projection, obligation, facts))))
        {
            throw InvalidEffect(span, "CheckedReference");
        }
        foreach (var staged in facts.Staged)
        {
            if (staged.ReferenceObligations.Any(obligation =>
                    ReferenceDescriptorOverlapsField(
                        root, projection, obligation, facts))
                || staged.ReferenceFieldValues.Values.Any(value =>
                    value.Obligations.Any(obligation =>
                        ReferenceDescriptorOverlapsField(
                            root, projection, obligation, facts))))
            {
                throw InvalidEffect(span, "CheckedReference");
            }
        }
    }

    private static bool ReferenceDescriptorOverlapsField(int root,
        StorageProjection projection, ProvenanceAnchor obligation,
        BackingDependencies facts)
    {
        if (obligation.Kind != ProvenanceAnchorKind.ReferenceDescriptor
            || obligation.Identity != root
            || obligation.Storage is not StorageKey key || !key.IsKnown
            || key.Activation != facts.FunctionActivation
            || key.Site != root
            || !facts.StorageInstanceSet(root).Contains(key.Instance))
        {
            return false;
        }
        return StorageProjectionsOverlap(key.Projection, projection);
    }

    private static bool StorageProjectionsOverlap(StorageProjection left,
        StorageProjection right)
    {
        if (left.Kind == StorageProjectionKind.WholeObject
            || right.Kind == StorageProjectionKind.WholeObject)
        {
            return true;
        }
        if (left.Kind != StorageProjectionKind.InlineFieldPath
            || right.Kind != StorageProjectionKind.InlineFieldPath)
        {
            return true;
        }
        return left.FieldPath.IsPrefixOf(right.FieldPath)
            || right.FieldPath.IsPrefixOf(left.FieldPath);
    }

    private static bool NativePointerTargetOverlapsField(int root,
        StorageProjection projection, ProvenanceAnchor target, BackingDependencies facts)
        => target.Kind == ProvenanceAnchorKind.NativeStorage
            && StorageTargetOverlapsField(root, projection, target, facts);

    private static bool ReferenceTargetOverlapsField(int root,
        StorageProjection projection, ProvenanceAnchor target, BackingDependencies facts)
    {
        if (target.Kind == ProvenanceAnchorKind.ReferencePlace)
        {
            return StorageTargetOverlapsField(root, projection, target, facts);
        }
        if (target.Kind != ProvenanceAnchorKind.NormalBackingObjectPlace
            || target.Storage is not StorageKey key || !key.IsKnown
            || (uint)root >= (uint)facts.NormalBackingObjects.Length)
        {
            return false;
        }
        var objectIdentity = new ProvenanceAnchor(
            ProvenanceAnchorKind.NormalBackingObject, key.Site,
            new StorageKey(key.Activation, key.Site, key.Instance,
                StorageProjection.WholeObject),
            ObjectType: target.ObjectType, ObjectPath: target.ObjectPath);
        return facts.NormalBackingObjectsForSlot(root).Contains(objectIdentity)
            && StorageProjectionsOverlap(key.Projection, projection);
    }

    private static bool StorageTargetOverlapsField(int root,
        StorageProjection projection, ProvenanceAnchor target, BackingDependencies facts)
    {
        if (target.Identity != root || target.Storage is not StorageKey key
            || !key.IsKnown || key.Activation != facts.FunctionActivation
            || key.Site != root
            || !facts.StorageInstanceSet(root).Contains(key.Instance))
        {
            return false;
        }
        if (key.Projection.Kind == StorageProjectionKind.WholeObject)
        {
            return true;
        }
        if (projection.Kind == StorageProjectionKind.WholeObject)
        {
            return true;
        }
        return key.Projection.Kind != StorageProjectionKind.InlineFieldPath
            || key.Projection.FieldPath.IsPrefixOf(projection.FieldPath)
            || projection.FieldPath.IsPrefixOf(key.Projection.FieldPath);
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
