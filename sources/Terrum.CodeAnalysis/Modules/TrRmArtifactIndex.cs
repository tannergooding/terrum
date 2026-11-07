// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal enum TrRmArtifactSchema
{
    Standard,
    Development,
    DevelopmentFunctionPointerEffectsDraft,
    DevelopmentNativeConventionsDraft,
    CopyOriginDraft,
    ForeignInvocationDraft,
    ForeignInvocationStorageAddressDraft,
    ForeignInvocationStorageFixedArrayCopyDraft,
    ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft,
    ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft,
    ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft,
    ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft,
    ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft,
    LocalAggregateLayoutDraft,
    ForeignInvocationStorageFixedArrayGenericCopyDraft,
    ScalarFrozenValuesDraft,
    NominalEnumsDraft,
    RootPinRuntimeDraft,
    GeneralPredicateGraphsDraft,
    RegisterExceptionRegionsDraft,
}

internal static class TrRmArtifactSchemaFacts
{
    internal static bool IsDevelopmentProfile(this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.Development
            or TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft
            or TrRmArtifactSchema.DevelopmentNativeConventionsDraft;

    internal static bool UsesDevelopmentFunctionPointerEffects(
        this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft
            or TrRmArtifactSchema.DevelopmentNativeConventionsDraft;

    internal static bool UsesDevelopmentNativeConventions(
        this TrRmArtifactSchema schema) =>
        schema == TrRmArtifactSchema.DevelopmentNativeConventionsDraft;

    internal static bool SupportsNominalEnums(this TrRmArtifactSchema schema) =>
        schema == TrRmArtifactSchema.NominalEnumsDraft;

    internal static bool SupportsDeclaredCopyEvidence(
        this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;

    internal static bool SupportsDeclaredUniqueEvidence(
        this TrRmArtifactSchema schema) =>
        schema == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;

    internal static bool UsesRootPinRuntime(this TrRmArtifactSchema schema) =>
        schema == TrRmArtifactSchema.RootPinRuntimeDraft;

    internal static bool UsesScalarFrozenValues(this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.ScalarFrozenValuesDraft
            or TrRmArtifactSchema.NominalEnumsDraft
            or TrRmArtifactSchema.GeneralPredicateGraphsDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft;

    internal static bool UsesCopyOrigin(this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.CopyOriginDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft;

    internal static bool UsesRawUnionValueCoverage(
        this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft;

    internal static bool UsesForeignInvocationStorageFixedArrayRepeatInitialization(
        this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft;

    internal static bool UsesGeneralPredicateGraphs(this TrRmArtifactSchema schema) =>
        schema == TrRmArtifactSchema.GeneralPredicateGraphsDraft;

    internal static bool UsesRegisterExceptionRegions(
        this TrRmArtifactSchema schema) =>
        schema == TrRmArtifactSchema.RegisterExceptionRegionsDraft;

    internal static bool UsesForeignInvocationContracts(
        this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;

    internal static bool UsesForeignInvocationStorageAddresses(
        this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;

    internal static bool UsesForeignInvocationStorageFixedArrays(
        this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft;

    internal static bool UsesForeignInvocationStorageFixedArrayEmptyInitialization(
        this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft;

    internal static bool UsesForeignInvocationStorageFixedArrayUnitInitialization(
        this TrRmArtifactSchema schema) =>
        schema is TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft;
}

/// <summary>Validates TRRM (1, 0) framing and indexes present sections without binding them.</summary>
/// <remarks>The borrowed artifact bytes must remain unchanged while this index is in use.</remarks>
public readonly ref struct TrRmArtifactIndex
{
    private readonly ReadOnlySpan<byte> _artifact;
    private readonly ReadOnlySpan<byte> _directory;
    private readonly int _entryCount;
    private readonly TrRmArtifactSchema _schema;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of indexed sections.</summary>
    public int SectionCount => _entryCount;
    internal TrRmArtifactSchema Schema => _schema;

    /// <summary>Validates and indexes a standard TRRM artifact.</summary>
    /// <param name="artifact">The borrowed artifact bytes, which must remain unchanged while this index is in use.</param>
    public TrRmArtifactIndex(ReadOnlySpan<byte> artifact)
        : this(artifact, TrRmArtifactSchema.Standard)
    {
    }

    internal TrRmArtifactIndex(ReadOnlySpan<byte> artifact, TrRmArtifactSchema schema)
        : this(artifact, schema, null)
    {
    }

    internal TrRmArtifactIndex(ReadOnlySpan<byte> artifact,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        _resourceLedger = resourceLedger;
        if (schema is not (TrRmArtifactSchema.Standard or TrRmArtifactSchema.Development
            or TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft
            or TrRmArtifactSchema.DevelopmentNativeConventionsDraft
            or TrRmArtifactSchema.CopyOriginDraft
            or TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            or TrRmArtifactSchema.LocalAggregateLayoutDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
            or TrRmArtifactSchema.ScalarFrozenValuesDraft
            or TrRmArtifactSchema.NominalEnumsDraft
            or TrRmArtifactSchema.RootPinRuntimeDraft
            or TrRmArtifactSchema.GeneralPredicateGraphsDraft
            or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            throw InvalidArtifact();
        }

        if (resourceLedger is null)
        {
            if (artifact.Length < 32 || !artifact[..4].SequenceEqual("TRRM"u8)
                || ReadU16(artifact, 4) != 1 || ReadU16(artifact, 6) != 0
                || ReadU32(artifact, 8) != 32
                || ReadU32(artifact, 16) != artifact.Length
                || ReadU32(artifact, 20) != 32)
            {
                throw InvalidArtifact();
            }
        }
        else
        {
            ValidateA3HeaderFields(artifact, resourceLedger);
        }

        for (var offset = 24; offset < 32; offset++)
        {
            ConsumeWork(2);
            if (artifact[offset] != 0)
            {
                throw InvalidArtifact();
            }
        }

        uint entryCount;
        if (resourceLedger is null)
        {
            entryCount = ReadU32(artifact, 12);
        }
        else
        {
            entryCount = ReadU32AndCharge(artifact, 12);
            ConsumeWork();
            if (entryCount is 0 or > 32790)
            {
                throw InvalidArtifact();
            }
        }

        ConsumeWork(2);
        var directoryEnd = 32L + (entryCount * 16L);
        ConsumeWork();
        if (directoryEnd > artifact.Length)
        {
            throw InvalidArtifact();
        }

        ConsumeWork(2);
        _artifact = artifact;
        _directory = artifact.Slice(32, (int)directoryEnd - 32);
        _entryCount = (int)entryCount;
        _schema = schema;
        ConsumeWork(0x5E + 1);
        Span<uint> sectionCounts = stackalloc uint[0x5E + 1];
        sectionCounts.Clear();
        var previousKind = 0;
        // Keep directory-ordered payloads allocation-free while retaining support for alternate layouts.
        var previousStart = 0u;
        var rangesOrdered = true;
        var moduleFound = false;
        for (var entry = 0; entry < _entryCount; entry++)
        {
            ConsumeWork();
            var kind = Kind(entry);
            var flags = ReadU16AndCharge(_directory, (entry * 16) + 2);
            var count = ReadU32AndCharge(_directory, (entry * 16) + 4);
            var start = ReadU32AndCharge(_directory, (entry * 16) + 8);
            var length = ReadU32AndCharge(_directory, (entry * 16) + 12);
            ConsumeWork();
            if (kind <= previousKind)
            {
                throw InvalidArtifact();
            }

            ConsumeWork();
            if (count is 0 or > 0xFF_FFFF)
            {
                throw InvalidArtifact();
            }

            ConsumeWork();
            if (length == 0)
            {
                throw InvalidArtifact();
            }

            ConsumeWork();
            if (start < directoryEnd)
            {
                throw InvalidArtifact();
            }

            ConsumeWork();
            var extentEnd = (long)start + length;
            ConsumeWork();
            if (extentEnd > artifact.Length)
            {
                throw InvalidArtifact();
            }

            previousKind = kind;
            if (entry != 0)
            {
                ConsumeWork();
                if (start < previousStart)
                {
                    rangesOrdered = false;
                }
            }

            previousStart = start;
            ConsumeWork();
            if (kind == 0x0001)
            {
                moduleFound = true;
            }

            ConsumeWork();
            var stride = FixedStride(kind, schema);
            ConsumeWork();
            if (kind < 0x8000 && stride == -1)
            {
                throw new InvalidDataException(
                    $"Invalid TRRM section 0x{kind:X4} for {schema} profile.");
            }

            ConsumeWork();
            if (kind < 0x8000 && flags != (kind == 0x7F00 ? 2 : 0))
            {
                throw new InvalidDataException(
                    $"Invalid TRRM section 0x{kind:X4} for {schema} profile.");
            }

            ConsumeWork();
            if (kind >= 0x8000 && flags is not (1 or 2 or 3))
            {
                throw new InvalidDataException(
                    $"Invalid TRRM section 0x{kind:X4} for {schema} profile.");
            }

            ConsumeWork();
            if (stride > 0)
            {
                ConsumeWork();
                var expectedLength = (long)count * stride;
                ConsumeWork();
                if (length != expectedLength)
                {
                    throw new InvalidDataException(
                        $"Invalid TRRM section 0x{kind:X4} for {schema} profile.");
                }
            }

            ConsumeWork();
            if (kind == 0x0002 && (start & 3) != 0)
            {
                throw new InvalidDataException(
                    $"Invalid TRRM section 0x{kind:X4} for {schema} profile.");
            }

            ConsumeWork();
            if (kind <= 0x005E)
            {
                ConsumeWork();
                sectionCounts[kind] = count;
            }
        }

        ConsumeWork();
        if (!moduleFound)
        {
            throw InvalidArtifact();
        }

        ConsumeWork();
        var featureCount = (int)sectionCounts[0x0004];
        if (featureCount != 0)
        {
            if (!TryGetSection(0x0004, out var featureBytes,
                    out var actualFeatureCount))
            {
                throw InvalidArtifact();
            }

            ConsumeWork();
            if (actualFeatureCount != featureCount)
            {
                throw InvalidArtifact();
            }

            ConsumeWork();
            _ = new TrRmFeatureTableReader(featureBytes, featureCount, schema,
                resourceLedger);
        }

        if (!TryGetSection(0x0001, out var moduleBytes, out var moduleCount))
        {
            throw InvalidArtifact();
        }

        for (var module = 0; module < moduleCount; module++)
        {
            ConsumeWork();
            ConsumeWork();
            var offset = module * 48;
            var featureFirst = ReadU32AndCharge(moduleBytes, offset + 24);
            var moduleFeatureCount = ReadU32AndCharge(moduleBytes, offset + 28);
            if (!ValidFeatureRange(featureFirst, moduleFeatureCount, featureCount))
            {
                throw InvalidArtifact();
            }
        }

        var cursor = (int)directoryEnd;
        if (rangesOrdered)
        {
            for (var entry = 0; entry < _entryCount; entry++)
            {
                ConsumeWork();
                var start = ReadU32AndCharge(_directory, (entry * 16) + 8);
                var length = ReadU32AndCharge(_directory, (entry * 16) + 12);
                ConsumeWork();
                if (start < cursor)
                {
                    throw InvalidArtifact();
                }

                if (HasNonzeroPadding(artifact, cursor, (int)start))
                {
                    throw InvalidArtifact();
                }

                ConsumeWork();
                cursor = (int)((long)start + length);
            }
        }
        else
        {
            ConsumeWork(checked(1UL + ((ulong)_entryCount * (1UL + (2UL * sizeof(uint))))));
            var extents = new (uint Start, uint End)[_entryCount];
            for (var entry = 0; entry < _entryCount; entry++)
            {
                ConsumeWork();
                var start = ReadU32AndCharge(_directory, (entry * 16) + 8);
                var length = ReadU32AndCharge(_directory, (entry * 16) + 12);
                ConsumeWork();
                extents[entry] = (start, checked(start + length));
            }

            if (resourceLedger is null)
            {
                Array.Sort(extents,
                    static (left, right) => left.Start.CompareTo(right.Start));
            }
            else
            {
                SortExtents(extents, resourceLedger);
            }

            foreach (var (start, end) in extents)
            {
                ConsumeWork();
                ConsumeWork();
                if (start < cursor)
                {
                    throw InvalidArtifact();
                }

                if (HasNonzeroPadding(artifact, cursor, (int)start))
                {
                    throw InvalidArtifact();
                }

                cursor = (int)end;
            }
        }

        ConsumeWork();
        if (cursor != artifact.Length)
        {
            throw InvalidArtifact();
        }

        for (var entry = 0; entry < _entryCount; entry++)
        {
            ConsumeWork();
            if (ReadU16AndCharge(_directory, (entry * 16) + 2) == 2)
            {
                var start = ReadU32AndCharge(_directory, (entry * 16) + 8);
                var length = ReadU32AndCharge(_directory, (entry * 16) + 12);
                var count = ReadU32AndCharge(_directory, (entry * 16) + 4);
                ValidateSlots(artifact.Slice((int)start, (int)length), Kind(entry),
                    count, sectionCounts, schema);
            }
        }
    }

    internal static TrRmArtifactIndex ForDevelopment(ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.Development);

    internal static TrRmArtifactIndex ForDevelopmentFunctionPointerEffectsDraft(
        ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft);

    internal static TrRmArtifactIndex ForDevelopmentNativeConventionsDraft(
        ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.DevelopmentNativeConventionsDraft);

    internal static TrRmArtifactIndex ForCopyOriginDraft(ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.CopyOriginDraft);

    internal static TrRmArtifactIndex ForForeignInvocationDraft(ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.ForeignInvocationDraft);

    internal static TrRmArtifactIndex
        ForForeignInvocationStorageAddressDraft(ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.ForeignInvocationStorageAddressDraft);

    internal static TrRmArtifactIndex
        ForForeignInvocationStorageFixedArrayCopyDraft(ReadOnlySpan<byte> artifact)
        => new(artifact,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft);

    internal static TrRmArtifactIndex
        ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
            ReadOnlySpan<byte> artifact)
        => new(artifact,
            TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft);

    internal static TrRmArtifactIndex
        ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
            ReadOnlySpan<byte> artifact)
        => new(artifact,
            TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft);

    internal static TrRmArtifactIndex
        ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
            ReadOnlySpan<byte> artifact)
        => new(artifact,
            TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft);

    internal static TrRmArtifactIndex
        ForForeignInvocationStorageFixedArrayCopyReturnCoverageDraft(
            ReadOnlySpan<byte> artifact)
        => new(artifact,
            TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft);

    internal static TrRmArtifactIndex
        ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
            ReadOnlySpan<byte> artifact)
        => new(artifact,
            TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft);

    internal static TrRmArtifactIndex
        ForLocalAggregateLayoutDraft(ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.LocalAggregateLayoutDraft);

    internal static TrRmArtifactIndex
        ForForeignInvocationStorageFixedArrayGenericCopyDraft(
            ReadOnlySpan<byte> artifact)
        => new(artifact,
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft);

    internal static TrRmArtifactIndex ForNominalEnumsDraft(ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.NominalEnumsDraft);

    internal static TrRmArtifactIndex ForScalarFrozenValuesDraft(ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.ScalarFrozenValuesDraft);

    internal static TrRmArtifactIndex ForRootPinRuntimeDraft(ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.RootPinRuntimeDraft);

    internal static TrRmArtifactIndex ForGeneralPredicateGraphsDraft(
        ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.GeneralPredicateGraphsDraft);

    internal static TrRmArtifactIndex ForRegisterExceptionRegionsDraft(
        ReadOnlySpan<byte> artifact)
        => new(artifact, TrRmArtifactSchema.RegisterExceptionRegionsDraft);

    /// <summary>Finds a section by kind without throwing when it is absent.</summary>
    /// <param name="kind">The assigned section kind to find.</param>
    /// <param name="section">The borrowed section bytes, or an empty span when not found.</param>
    /// <param name="recordCount">The section's row count, or zero when not found.</param>
    /// <returns><see langword="true"/> when the section is present.</returns>
    public bool TryGetSection(ushort kind, out ReadOnlySpan<byte> section, out int recordCount)
    {
        var low = 0;
        var high = _entryCount;
        while (true)
        {
            ConsumeWork();
            if (low >= high)
            {
                break;
            }

            ConsumeWork(3);
            var middle = low + ((high - low) / 2);
            var middleKind = Kind(middle);
            ConsumeWork();
            if (middleKind < kind)
            {
                ConsumeWork();
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        ConsumeWork();
        if (low < _entryCount)
        {
            var foundKind = Kind(low);
            ConsumeWork();
            if (foundKind == kind)
            {
                ConsumeWork();
                var start = ReadU32AndCharge(_directory, (low * 16) + 8);
                var length = ReadU32AndCharge(_directory, (low * 16) + 12);
                ConsumeWork();
                section = _artifact.Slice((int)start, (int)length);
                ConsumeWork();
                recordCount = checked((int)ReadU32AndCharge(_directory,
                    (low * 16) + 4));
                ConsumeWork();
                return true;
            }
        }

        section = default;
        recordCount = 0;
        return false;
    }

    /// <summary>Gets the section at a zero-based directory index.</summary>
    /// <param name="entry">The zero-based section-directory index.</param>
    /// <param name="kind">The assigned section kind.</param>
    /// <param name="flags">The section flags.</param>
    /// <param name="section">The borrowed section bytes.</param>
    /// <param name="recordCount">The section's row count.</param>
    public void GetSection(int entry, out ushort kind, out ushort flags,
        out ReadOnlySpan<byte> section, out int recordCount)
    {
        ConsumeWork();
        if (entry < 0 || entry >= _entryCount)
        {
            throw InvalidArtifact();
        }

        ConsumeWork();
        var directory = entry * 16;
        kind = (ushort)Kind(entry);
        flags = ReadU16AndCharge(_directory, directory + 2);
        recordCount = checked((int)ReadU32AndCharge(_directory, directory + 4));
        var start = checked((int)ReadU32AndCharge(_directory, directory + 8));
        var length = checked((int)ReadU32AndCharge(_directory, directory + 12));
        ConsumeWork();
        section = _artifact.Slice(start, length);
        ConsumeWork();
    }

    private int Kind(int entry)
        => ReadU16AndCharge(_directory, entry * 16);

    private void ValidateSlots(ReadOnlySpan<byte> section, int kind,
        uint recordCount, ReadOnlySpan<uint> sectionCounts,
        TrRmArtifactSchema schema)
    {
        var resourceLedger = _resourceLedger;
        resourceLedger?.ConsumeWork();
        if (section.Length < 4)
        {
            throw InvalidArtifact();
        }

        var slotCount = ReadU32AndCharge(section, 0);
        resourceLedger?.ConsumeWork(2);
        var preludeEnd = 4L + (slotCount * 16L);
        resourceLedger?.ConsumeWork();
        if (preludeEnd > section.Length)
        {
            throw InvalidArtifact();
        }

        resourceLedger?.ConsumeWork();
        if (kind == 0x7F00)
        {
            resourceLedger?.ConsumeWork(2);
            var end = preludeEnd + (recordCount * 16L);
            resourceLedger?.ConsumeWork();
            if (end != section.Length)
            {
                throw InvalidArtifact();
            }
        }

        for (var slot = 0; slot < slotCount; slot++)
        {
            resourceLedger?.ConsumeWork();
            resourceLedger?.ConsumeWork(2);
            var offset = 4 + ((int)slot * 16);
            var targetKind = ReadU16AndCharge(section, offset);
            var selector = ReadU16AndCharge(section, offset + 2);
            var row = ReadU32AndCharge(section, offset + 4);
            var block = ReadU32AndCharge(section, offset + 8);
            var ordinal = ReadU32AndCharge(section, offset + 12);
            var reserved = ReadU16AndCharge(section, offset + 10);
            if (!IsValidSlot(targetKind, selector, row, block, ordinal,
                    reserved, kind, sectionCounts, schema))
            {
                throw InvalidArtifact();
            }
        }
    }

    private bool IsValidSlot(ushort targetKind, ushort selector, uint row,
        uint block, uint ordinal, ushort reserved, int kind,
        ReadOnlySpan<uint> sectionCounts, TrRmArtifactSchema schema)
    {
        ConsumeWork();
        if (row is 0 or > 0xFF_FFFF)
        {
            return false;
        }

        ConsumeWork();
        if (reserved != 0)
        {
            return false;
        }

        ConsumeWork();
        if (selector > 1)
        {
            return false;
        }

        if (selector == 0)
        {
            ConsumeWork();
            if (block != 0 || ordinal != 0)
            {
                return false;
            }

            ConsumeWork();
            if (!IsReferenceSlotTableKind(targetKind, schema))
            {
                return false;
            }

            ConsumeWork();
            if (FixedStride(targetKind, schema) == -1)
            {
                return false;
            }

            ConsumeWork();
            if (sectionCounts[targetKind] == 0)
            {
                return false;
            }

            ConsumeWork();
            if (row > sectionCounts[targetKind])
            {
                return false;
            }
        }
        else
        {
            var requiredTargetKind = schema.IsDevelopmentProfile()
                ? (ushort)0x0020
                : (ushort)0;
            ConsumeWork();
            if (targetKind != requiredTargetKind)
            {
                return false;
            }

            ConsumeWork();
            if (sectionCounts[0x0020] == 0)
            {
                return false;
            }

            ConsumeWork();
            if (row > sectionCounts[0x0020])
            {
                return false;
            }

            if (schema.IsDevelopmentProfile())
            {
                ConsumeWork();
                if (block == 0)
                {
                    return false;
                }
            }
        }

        ConsumeWork();
        if (kind == 0x7F00)
        {
            var requiredTargetKind = selector == 0
                ? (ushort)0x001F
                : schema.IsDevelopmentProfile() ? (ushort)0x0020 : (ushort)0;
            ConsumeWork();
            if (targetKind != requiredTargetKind)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsReferenceSlotTableKind(int kind, TrRmArtifactSchema schema)
        => !schema.IsDevelopmentProfile()
            ? kind is >= 0x0001 and <= 0x004B
                || (schema.SupportsNominalEnums()
                    && (kind is 0x0053 or 0x0054))
                || (schema.UsesGeneralPredicateGraphs()
                    && (kind is (>= 0x004D and <= 0x0052)
                        or (>= 0x0055 and <= 0x005E)))
            : kind is >= 0x0001 and <= 0x0020 and not 0x0004;

    private static int FixedStride(int kind, TrRmArtifactSchema schema)
    {
        if (schema.IsDevelopmentProfile())
        {
            return kind switch
            {
                0x0001 => 48,
                0x0002 or 0x0003 or 0x0011 or 0x001B or 0x0020 or 0x7F00 => 0,
                0x0004 => 8,
                0x0010 => 12,
                0x0013 => 44,
                0x0014 => 16,
                0x0015 or 0x0016 => 28,
                0x0017 => 24,
                0x0018 => 16,
                0x0019 => 32,
                0x001A => 12,
                0x001C => 12,
                0x001D => 8,
                0x001E => 20,
                0x001F => 8,
                0x0023 => 12,
                0x0030 => 16,
                0x0031 => 8,
                0x0032 => 16,
                0x0033 => 8,
                0x0048 => 8,
                _ => -1,
            };
        }

        return kind switch
        {
            0x0001 => 48,
            0x0002 or 0x0003 or 0x0011 or 0x001B or 0x0020
                or 0x004B or 0x7F00 => 0,
            0x0012 when schema.UsesForeignInvocationStorageFixedArrays() => 40,
            0x0004 => 8,
            0x0010 => 12,
            0x0013 => 44,
            0x0014 => 16,
            0x0015 or 0x0016 => 28,
            0x0017 => 24,
            0x0018 => 16,
            0x0019 => 32,
            0x001A => 12,
            0x001C => 12,
            0x001D => 8,
            0x001E => 20,
            0x001F => 8,
            0x0021 or 0x0022 => 16,
            0x0023 or 0x0024 or 0x0026 or 0x0027 or 0x0032 or 0x0036 => 12,
            0x0025 => 48,
            0x0028 => 32,
            0x0029 or 0x002A => 20,
            0x002B or 0x002C => 12,
            0x002D => 20,
            0x002E => 16,
            0x002F => 40,
            0x0030 => 8,
            0x0031 or 0x0033 => 16,
            0x0034 => 20,
            0x0035 => 16,
            0x0037 or 0x0039 or 0x003A or 0x003C => 8,
            0x0038 => 16,
            0x003B => 16,
            0x003D => 12,
            0x003E => 16,
            0x003F => 24,
            0x0040 or 0x0041 => 12,
            0x0042 => 80,
            0x0043 or 0x0044 => 8,
            0x0045 or 0x0046 => 20,
            0x0047 => 24,
            0x0048 => 8,
            0x0049 => 16,
            0x004A => 4,
            0x004D => schema.UsesGeneralPredicateGraphs() ? 4 : -1,
            0x004E => schema.UsesGeneralPredicateGraphs() ? 0 : -1,
            0x004F => schema.UsesGeneralPredicateGraphs() ? 4 : -1,
            0x0050 => schema.UsesGeneralPredicateGraphs() ? 8 : -1,
            0x0051 => schema.UsesGeneralPredicateGraphs() ? 12 : -1,
            0x0052 => schema.UsesGeneralPredicateGraphs() ? 0 : -1,
            0x0053 or 0x0054 when schema.SupportsNominalEnums() => 20,
            0x0055 or 0x0056 or 0x0057 when schema.UsesGeneralPredicateGraphs() => 20,
            0x0058 when schema.UsesGeneralPredicateGraphs() => 16,
            0x0059 when schema.UsesGeneralPredicateGraphs() => 8,
            0x005A when schema.UsesGeneralPredicateGraphs() => 12,
            0x005B when schema.UsesGeneralPredicateGraphs() => 16,
            0x005C when schema.UsesGeneralPredicateGraphs() => 24,
            0x005D when schema.UsesGeneralPredicateGraphs() => 16,
            0x005E when schema.UsesGeneralPredicateGraphs() => 8,
            _ => -1,
        };
    }

    private bool ValidFeatureRange(uint first, uint count, int featureCount)
    {
        ConsumeWork();
        if (first == 0)
        {
            ConsumeWork();
            return count == 0;
        }

        ConsumeWork();
        if (count == 0)
        {
            return false;
        }

        ConsumeWork();
        if (first > 0xFF_FFFF)
        {
            return false;
        }

        ConsumeWork();
        if (count > 0xFF_FFFF)
        {
            return false;
        }

        ConsumeWork();
        var last = (ulong)first + count - 1;
        ConsumeWork();
        if (last > 0xFF_FFFF)
        {
            return false;
        }

        ConsumeWork();
        var end = (ulong)first + count;
        ConsumeWork();
        return end <= ((ulong)featureCount + 1);
    }

    private static void ValidateA3HeaderFields(ReadOnlySpan<byte> artifact,
        TrRmA3LayoutResourceLedger resourceLedger)
    {
        resourceLedger.ConsumeWork();
        if (artifact.Length < 32)
        {
            throw InvalidArtifact();
        }

        var magic = "TRRM"u8;
        for (var index = 0; index < magic.Length; index++)
        {
            resourceLedger.ConsumeWork(2);
            if (artifact[index] != magic[index])
            {
                throw InvalidArtifact();
            }
        }

        resourceLedger.ConsumeWork(3);
        var majorVersion = ReadU16(artifact, 4);
        resourceLedger.ConsumeWork();
        if (majorVersion != 1)
        {
            throw InvalidArtifact();
        }

        resourceLedger.ConsumeWork(3);
        var minorVersion = ReadU16(artifact, 6);
        resourceLedger.ConsumeWork();
        if (minorVersion != 0)
        {
            throw InvalidArtifact();
        }

        resourceLedger.ConsumeWork(5);
        var headerSize = ReadU32(artifact, 8);
        resourceLedger.ConsumeWork();
        if (headerSize != 32)
        {
            throw InvalidArtifact();
        }

        resourceLedger.ConsumeWork(5);
        var totalLength = ReadU32(artifact, 16);
        resourceLedger.ConsumeWork();
        if (totalLength != artifact.Length)
        {
            throw InvalidArtifact();
        }

        resourceLedger.ConsumeWork(5);
        var directoryOffset = ReadU32(artifact, 20);
        resourceLedger.ConsumeWork();
        if (directoryOffset != 32)
        {
            throw InvalidArtifact();
        }
    }

    private bool HasNonzeroPadding(ReadOnlySpan<byte> artifact, int start,
        int end)
    {
        if (_resourceLedger is null)
        {
            return artifact.Slice(start, end - start).IndexOfAnyExcept((byte)0) >= 0;
        }

        for (var offset = start; offset < end; offset++)
        {
            ConsumeWork(2);
            if (artifact[offset] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void SortExtents((uint Start, uint End)[] extents,
        TrRmA3LayoutResourceLedger resourceLedger)
    {
        var entryCount = extents.Length;
        resourceLedger.ConsumeWork(checked(
            1UL + ((ulong)entryCount * (1UL + (sizeof(uint) * 2UL)))));
        var scratch = new (uint Start, uint End)[entryCount];
        var source = extents;
        var destination = scratch;
        var width = 1;
        while (width < entryCount)
        {
            for (var start = 0; start < entryCount;)
            {
                resourceLedger.ConsumeWork(3);
                var middle = Math.Min(start + width, entryCount);
                var end = Math.Min(start + (2 * width), entryCount);
                var left = start;
                var right = middle;
                var output = start;
                while (true)
                {
                    resourceLedger.ConsumeWork();
                    if (left >= middle)
                    {
                        break;
                    }

                    resourceLedger.ConsumeWork();
                    if (right >= end)
                    {
                        break;
                    }

                    resourceLedger.ConsumeWork(3);
                    if (source[left].Start <= source[right].Start)
                    {
                        resourceLedger.ConsumeWork(1 + (sizeof(uint) * 2));
                        destination[output++] = source[left++];
                    }
                    else
                    {
                        resourceLedger.ConsumeWork(1 + (sizeof(uint) * 2));
                        destination[output++] = source[right++];
                    }
                }

                while (left < middle)
                {
                    resourceLedger.ConsumeWork(1 + (sizeof(uint) * 2));
                    destination[output++] = source[left++];
                }

                while (right < end)
                {
                    resourceLedger.ConsumeWork(1 + (sizeof(uint) * 2));
                    destination[output++] = source[right++];
                }

                resourceLedger.ConsumeWork(2);
                start = checked(start + (2 * width));
            }

            (source, destination) = (destination, source);
            resourceLedger.ConsumeWork(2);
            width = width > entryCount / 2 ? entryCount : width * 2;
        }

        if (!ReferenceEquals(source, extents))
        {
            for (var index = 0; index < entryCount; index++)
            {
                resourceLedger.ConsumeWork(1 + (sizeof(uint) * 2));
                extents[index] = source[index];
            }
        }
    }

    private void ConsumeWork(ulong units = 1)
        => _resourceLedger?.ConsumeWork(units);

    private ushort ReadU16AndCharge(ReadOnlySpan<byte> bytes, int offset)
    {
        ConsumeWork(sizeof(ushort) + 1);
        return ReadU16(bytes, offset);
    }

    private uint ReadU32AndCharge(ReadOnlySpan<byte> bytes, int offset)
    {
        ConsumeWork(sizeof(uint) + 1);
        return ReadU32(bytes, offset);
    }

    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));

    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidArtifact()
        => new("Invalid TRRM (1, 0) artifact framing.");
}
