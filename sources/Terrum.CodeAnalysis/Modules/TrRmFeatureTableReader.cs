// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>An exact required feature and rule revision in a Module manifest.</summary>
public readonly record struct TrRmFeatureRow(uint FeatureId, uint RuleRevision)
{
    /// <summary>Gets the exact NativeBoundaryContracts requirement.</summary>
    public static TrRmFeatureRow NativeBoundaryContracts
        => new(TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact RootlessNativeValues requirement.</summary>
    public static TrRmFeatureRow RootlessNativeValues
        => new(TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact DirectNativeCalls requirement.</summary>
    public static TrRmFeatureRow DirectNativeCalls
        => new(TrRmFeatureTableReader.DirectNativeCallsFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact NativeExportEntries requirement.</summary>
    public static TrRmFeatureRow NativeExportEntries
        => new(TrRmFeatureTableReader.NativeExportEntriesFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact NativeInterfaceCalls requirement.</summary>
    public static TrRmFeatureRow NativeInterfaceCalls
        => new(TrRmFeatureTableReader.NativeInterfaceCallsFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact NativeDataImports requirement.</summary>
    public static TrRmFeatureRow NativeDataImports
        => new(TrRmFeatureTableReader.NativeDataImportsFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact NativeExceptionUnwind requirement.</summary>
    public static TrRmFeatureRow NativeExceptionUnwind
        => new(TrRmFeatureTableReader.NativeExceptionUnwindFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact NominalUnionMetadata requirement.</summary>
    public static TrRmFeatureRow NominalUnionMetadata
        => new(TrRmFeatureTableReader.NominalUnionMetadataFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact PublishedPredicates requirement.</summary>
    public static TrRmFeatureRow PublishedPredicates
        => new(TrRmFeatureTableReader.PublishedPredicatesFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact TypedFrozenValues requirement.</summary>
    public static TrRmFeatureRow TypedFrozenValues
        => new(TrRmFeatureTableReader.TypedFrozenValuesFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact ConstantExecution requirement.</summary>
    public static TrRmFeatureRow ConstantExecution
        => new(TrRmFeatureTableReader.ConstantExecutionFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    /// <summary>Gets the exact LayoutQueries requirement.</summary>
    public static TrRmFeatureRow LayoutQueries
        => new(TrRmFeatureTableReader.LayoutQueriesFeatureId,
            TrRmFeatureTableReader.RuleRevision);

    internal static TrRmFeatureRow ForeignInvocationContractsCandidate
        => new(TrRmFeatureTableReader.ForeignInvocationContractsCandidateFeatureId, 1);
    internal static TrRmFeatureRow NativeOutputCellsCandidate
        => new(TrRmFeatureTableReader.NativeOutputCellsCandidateFeatureId, 1);
    internal static TrRmFeatureRow RootPinRuntimeCandidate
        => new(TrRmFeatureTableReader.RootPinRuntimeFeatureId, 1);
    internal static TrRmFeatureRow GeneralPredicateGraphsCandidate
        => new(TrRmFeatureTableReader.GeneralPredicateGraphsFeatureId, 1);
    internal static TrRmFeatureRow ImmutableDataIdentitiesCandidate
        => new(TrRmFeatureTableReader.ImmutableDataIdentitiesFeatureId, 1);
    internal static TrRmFeatureRow FixedInlineArrayStorageCandidate
        => new(TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId, 1);
    internal static TrRmFeatureRow RegisterExceptionRegionsCandidate
        => new(TrRmFeatureTableReader.RegisterExceptionRegionsFeatureId, 1);
}

/// <summary>Structurally validates eight-byte Feature rows without assuming row order.</summary>
/// <remarks>The borrowed Feature section must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmFeatureTableReader
{
    /// <summary>The assigned identifier for NativeBoundaryContracts.</summary>
    public const uint NativeBoundaryContractsFeatureId = 1;
    /// <summary>The assigned identifier for RootlessNativeValues.</summary>
    public const uint RootlessNativeValuesFeatureId = 2;
    /// <summary>The assigned identifier for DirectNativeCalls.</summary>
    public const uint DirectNativeCallsFeatureId = 3;
    /// <summary>The assigned identifier for NativeExportEntries.</summary>
    public const uint NativeExportEntriesFeatureId = 4;
    /// <summary>The assigned identifier for NativeInterfaceCalls.</summary>
    public const uint NativeInterfaceCallsFeatureId = 5;
    /// <summary>The assigned identifier for NativeDataImports.</summary>
    public const uint NativeDataImportsFeatureId = 6;
    /// <summary>The assigned identifier for NativeExceptionUnwind.</summary>
    public const uint NativeExceptionUnwindFeatureId = 7;
    /// <summary>The assigned identifier for NominalUnionMetadata.</summary>
    public const uint NominalUnionMetadataFeatureId = 9;
    /// <summary>The assigned identifier for PublishedPredicates.</summary>
    public const uint PublishedPredicatesFeatureId = 10;
    /// <summary>The assigned identifier for TypedFrozenValues.</summary>
    public const uint TypedFrozenValuesFeatureId = 11;
    /// <summary>The assigned identifier for ConstantExecution.</summary>
    public const uint ConstantExecutionFeatureId = 12;
    /// <summary>The assigned identifier for LayoutQueries.</summary>
    public const uint LayoutQueriesFeatureId = 13;
    internal const uint RootPinRuntimeFeatureId = 14;
    internal const uint CopyOriginContractsCandidateFeatureId = 15;
    internal const uint ForeignInvocationContractsCandidateFeatureId = 16;
    internal const uint GeneralPredicateGraphsFeatureId = 17;
    internal const uint NativeOutputCellsCandidateFeatureId = 8;
    internal const uint ImmutableDataIdentitiesFeatureId = 19;
    internal const uint FixedInlineArrayStorageFeatureId = 20;
    internal const uint RegisterExceptionRegionsFeatureId = 21;
    /// <summary>The assigned Feature rule revision shared by currently assigned features.</summary>
    public const uint RuleRevision = 1;

    private const int RowSize = 8;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmArtifactSchema _schema;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated Feature rows.</summary>
    public int Count => _recordCount;
    internal TrRmA3LayoutResourceLedger? ResourceLedger => _resourceLedger;

    /// <summary>Validates the fixed-width rows in a Feature section.</summary>
    /// <param name="section">The borrowed Feature section bytes.</param>
    /// <param name="recordCount">The number of eight-byte Feature rows.</param>
    public TrRmFeatureTableReader(ReadOnlySpan<byte> section, int recordCount)
        : this(section, recordCount, TrRmArtifactSchema.Standard)
    {
    }

    internal TrRmFeatureTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (recordCount < 0 || recordCount > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length
            || schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.Development
                or TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft
                or TrRmArtifactSchema.CopyOriginDraft
                or TrRmArtifactSchema.ForeignInvocationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                or TrRmArtifactSchema.ScalarFrozenValuesDraft
                or TrRmArtifactSchema.NominalEnumsDraft
                or TrRmArtifactSchema.RootPinRuntimeDraft
                or TrRmArtifactSchema.GeneralPredicateGraphsDraft
                or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            throw InvalidFeature();
        }

        resourceLedger?.ConsumeWork(checked((ulong)recordCount));
        _rows = section;
        _recordCount = recordCount;
        _schema = schema;
        _resourceLedger = resourceLedger;
        for (var row = 0; row < recordCount; row++)
        {
            if (Read(row * RowSize) == 0 || Read((row * RowSize) + 4) == 0)
            {
                throw InvalidFeature();
            }
        }
    }

    /// <summary>Gets a validated Feature row by its one-based row number.</summary>
    public TrRmFeatureRow GetFeature(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidFeature();
        }

        var offset = (row - 1) * RowSize;
        return new TrRmFeatureRow(Read(offset), Read(offset + 4));
    }

    /// <summary>Finds a non-conflicting exact Feature pair by identifier.</summary>
    public bool TryGetFeature(uint featureId, out TrRmFeatureRow feature)
    {
        var found = false;
        feature = default;
        for (var row = 0; row < _recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var offset = row * RowSize;
            if (Read(offset) != featureId)
            {
                continue;
            }

            var current = new TrRmFeatureRow(featureId, Read(offset + 4));
            if (found && current.RuleRevision != feature.RuleRevision)
            {
                feature = default;
                return false;
            }

            feature = current;
            found = true;
        }

        return found;
    }

    internal static bool TryGetAssignedRuleRevision(uint featureId, out uint ruleRevision)
    {
        ruleRevision = featureId is (>= 1 and <= 7) or (>= 9 and <= 13) ? 1u : 0u;
        return ruleRevision != 0;
    }

    internal static bool TryGetRuleRevision(uint featureId,
        TrRmArtifactSchema schema, out uint ruleRevision)
    {
        if (schema.UsesForeignInvocationStorageFixedArrays()
            && featureId == FixedInlineArrayStorageFeatureId)
        {
            ruleRevision = 1;
            return true;
        }

        if (schema == TrRmArtifactSchema.CopyOriginDraft
            && featureId == CopyOriginContractsCandidateFeatureId)
        {
            ruleRevision = TrRmProvisionalFeatureRevisions.CopyOriginContractsRuleRevision;
            return true;
        }

        if (schema.UsesForeignInvocationContracts()
            && featureId is NativeOutputCellsCandidateFeatureId
                or ForeignInvocationContractsCandidateFeatureId)
        {
            ruleRevision = featureId == NativeOutputCellsCandidateFeatureId
                ? TrRmProvisionalFeatureRevisions.NativeOutputCellsRuleRevision
                : TrRmProvisionalFeatureRevisions.ForeignInvocationContractsRuleRevision;
            return true;
        }

        if (schema.SupportsNominalEnums() && featureId == 18)
        {
            ruleRevision = 1;
            return true;
        }

        if (schema.UsesRootPinRuntime()
            && featureId == RootPinRuntimeFeatureId)
        {
            ruleRevision = 1;
            return true;
        }

        if (schema.UsesGeneralPredicateGraphs()
            && featureId == GeneralPredicateGraphsFeatureId)
        {
            ruleRevision = 1;
            return true;
        }

        if (schema.UsesGeneralPredicateGraphs()
            && featureId == ImmutableDataIdentitiesFeatureId)
        {
            ruleRevision = 1;
            return true;
        }

        if (schema.UsesRegisterExceptionRegions()
            && featureId == RegisterExceptionRegionsFeatureId)
        {
            ruleRevision = 1;
            return true;
        }

        if (schema is not (TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.CopyOriginDraft
            or TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
            or TrRmArtifactSchema.ScalarFrozenValuesDraft
            or TrRmArtifactSchema.NominalEnumsDraft
            or TrRmArtifactSchema.RootPinRuntimeDraft
            or TrRmArtifactSchema.GeneralPredicateGraphsDraft
            or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            ruleRevision = 0;
            return false;
        }

        return TryGetAssignedRuleRevision(featureId, out ruleRevision);
    }

    internal TrRmArtifactSchema Schema => _schema;

    private uint Read(int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            _rows.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidFeature()
        => new("Invalid TRRM Feature table.");
}

internal static class TrRmProvisionalFeatureRevisions
{
    internal const uint ScalarInitOutputInitialRuleRevision = 1;
    internal const uint ScalarInitOutputRuleRevision = 8;
    internal const uint CopyOriginContractsRuleRevision = 1;
    internal const uint ForeignInvocationContractsRuleRevision = 1;
    internal const uint NativeOutputCellsRuleRevision = 1;
}

internal static class TrRmFeatureManifestValidator
{
    internal static void Validate(TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmArtifactSchema schema = TrRmArtifactSchema.Standard)
    {
        var resourceLedger = features.ResourceLedger;
        resourceLedger?.ConsumeWork();
        if (schema is not (TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.CopyOriginDraft
            or TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
            or TrRmArtifactSchema.ScalarFrozenValuesDraft
            or TrRmArtifactSchema.NominalEnumsDraft
            or TrRmArtifactSchema.RootPinRuntimeDraft
            or TrRmArtifactSchema.GeneralPredicateGraphsDraft
            or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            throw InvalidManifest();
        }
        resourceLedger?.ConsumeWork(1UL
            + (22UL * (1UL + sizeof(uint))));
        Span<uint> revisions = stackalloc uint[22];
        for (var module = 1; module <= modules.Count; module++)
        {
            resourceLedger?.ConsumeWork();
            ExpandModuleRequirements(features, modules, module, revisions,
                schema);
        }
    }

    internal static bool Requires(TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, int module, uint featureId)
        => Requires(features, modules, module, featureId, features.Schema);

    internal static bool Requires(TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, int module, uint featureId,
        TrRmArtifactSchema schema)
    {
        var resourceLedger = features.ResourceLedger;
        resourceLedger?.ConsumeWork();
        if (schema is not (TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.CopyOriginDraft
            or TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
            or TrRmArtifactSchema.ScalarFrozenValuesDraft
            or TrRmArtifactSchema.NominalEnumsDraft
            or TrRmArtifactSchema.RootPinRuntimeDraft
            or TrRmArtifactSchema.GeneralPredicateGraphsDraft
            or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            throw InvalidManifest();
        }
        resourceLedger?.ConsumeWork();
        if (!TrRmFeatureTableReader.TryGetRuleRevision(featureId, schema, out _))
        {
            throw InvalidManifest();
        }

        resourceLedger?.ConsumeWork(1UL
            + (22UL * (1UL + sizeof(uint))));
        Span<uint> revisions = stackalloc uint[22];
        ExpandModuleRequirements(features, modules, module, revisions, schema);
        resourceLedger?.ConsumeWork(sizeof(uint) + 2);
        return revisions[(int)featureId] != 0;
    }

    private static void ExpandModuleRequirements(TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, int module, Span<uint> revisions,
        TrRmArtifactSchema schema)
    {
        var resourceLedger = features.ResourceLedger;
        resourceLedger?.ConsumeWork(1UL + (22UL * sizeof(uint)));
        revisions.Clear();
        resourceLedger?.ConsumeWork((2UL * sizeof(uint)) + 1);
        var (first, count) = modules.GetFeatureRange(module);
        for (var offset = 0; offset < count; offset++)
        {
            resourceLedger?.ConsumeWork();
            var feature = features.GetFeature(first + offset);
            resourceLedger?.ConsumeWork();
            if (feature.FeatureId >= revisions.Length)
            {
                throw InvalidManifest();
            }
            resourceLedger?.ConsumeWork();
            if (!TrRmFeatureTableReader.TryGetRuleRevision(
                    feature.FeatureId, schema, out var assignedRevision))
            {
                throw InvalidManifest();
            }
            resourceLedger?.ConsumeWork();
            if (feature.RuleRevision != assignedRevision)
            {
                throw InvalidManifest();
            }

            resourceLedger?.ConsumeWork(sizeof(uint) + 1);
            ref var revision = ref revisions[(int)feature.FeatureId];
            resourceLedger?.ConsumeWork();
            if (revision != 0 && revision != feature.RuleRevision)
            {
                throw InvalidManifest();
            }

            resourceLedger?.ConsumeWork(sizeof(uint) + 1);
            revision = feature.RuleRevision;
        }

        var expanded = true;
        while (expanded)
        {
            expanded = false;
            for (uint featureId = 1; featureId < revisions.Length; featureId++)
            {
                if (revisions[(int)featureId] == 0)
                {
                    continue;
                }

                var prerequisiteCount = PrerequisiteCount(featureId, schema);
                for (var prerequisiteIndex = 0; prerequisiteIndex < prerequisiteCount;
                    prerequisiteIndex++)
                {
                    var prerequisite = Prerequisite(featureId, prerequisiteIndex, schema);
                    ref var revision = ref revisions[(int)prerequisite];
                    if (revision == 0)
                    {
                        revision = 1;
                        expanded = true;
                    }
                    else if (revision != 1)
                    {
                        throw InvalidManifest();
                    }
                }
            }
        }
    }

    private static int PrerequisiteCount(uint featureId, TrRmArtifactSchema schema) =>
        featureId switch
        {
            2 or 7 or 16 => 1,
            8 => 3,
            3 or 5 or 6 or 12 => 2,
            4 => 1,
            10 when schema.UsesForeignInvocationContracts()
                || schema.UsesGeneralPredicateGraphs() => 0,
            10 => 1,
            11 when !schema.UsesScalarFrozenValues() => 1,
            17 when schema.UsesGeneralPredicateGraphs() => 1,
            18 when schema.SupportsNominalEnums() => 1,
            19 when schema.UsesGeneralPredicateGraphs() => 0,
            14 when schema.UsesRootPinRuntime() => 0,
            20 when schema.UsesForeignInvocationStorageFixedArrays() => 1,
            _ => 0,
        };

    private static uint Prerequisite(uint featureId, int index,
        TrRmArtifactSchema schema) => featureId switch
        {
            2 or 7 or 16 => 1,
            8 => index switch {
                0 => 1,
                1 => 2,
                2 => 10,
                _ => throw InvalidManifest(),
            },
            3 or 5 or 6 => index == 0 ? 1u : 2u,
            4 => 3,
            10 when !schema.UsesForeignInvocationContracts()
                && !schema.UsesGeneralPredicateGraphs() => 11,
            11 when !schema.UsesScalarFrozenValues() => 9,
            12 when schema.UsesGeneralPredicateGraphs()
                => index == 0 ? 11u : 17u,
            12 => index == 0 ? 10u : 11u,
            17 when schema.UsesGeneralPredicateGraphs() => 10,
            18 when schema.SupportsNominalEnums() => 11,
            20 when schema.UsesForeignInvocationStorageFixedArrays() => 2,
            _ => throw InvalidManifest(),
        };

    private static InvalidDataException InvalidManifest()
        => new("Invalid TRRM Module Feature manifest.");
}
