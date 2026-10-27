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
    internal const uint CopyOriginContractsCandidateFeatureId = 15;
    /// <summary>The assigned Feature rule revision shared by currently assigned features.</summary>
    public const uint RuleRevision = 1;

    private const int RowSize = 8;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmArtifactSchema _schema;

    /// <summary>Gets the number of validated Feature rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates the fixed-width rows in a Feature section.</summary>
    /// <param name="section">The borrowed Feature section bytes.</param>
    /// <param name="recordCount">The number of eight-byte Feature rows.</param>
    public TrRmFeatureTableReader(ReadOnlySpan<byte> section, int recordCount)
        : this(section, recordCount, TrRmArtifactSchema.Standard)
    {
    }

    internal TrRmFeatureTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmArtifactSchema schema)
    {
        if (recordCount < 0 || recordCount > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length
            || schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.Development or TrRmArtifactSchema.CopyOriginDraft))
        {
            throw InvalidFeature();
        }

        _rows = section;
        _recordCount = recordCount;
        _schema = schema;
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
        if (schema == TrRmArtifactSchema.CopyOriginDraft
            && featureId == CopyOriginContractsCandidateFeatureId)
        {
            ruleRevision = TrRmProvisionalFeatureRevisions.CopyOriginContractsRuleRevision;
            return true;
        }

        if (schema is not (TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.CopyOriginDraft))
        {
            ruleRevision = 0;
            return false;
        }

        return TryGetAssignedRuleRevision(featureId, out ruleRevision);
    }

    internal TrRmArtifactSchema Schema => _schema;

    private uint Read(int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidFeature()
        => new("Invalid TRRM Feature table.");
}

internal static class TrRmProvisionalFeatureRevisions
{
    internal const uint ScalarInitOutputInitialRuleRevision = 1;
    internal const uint ScalarInitOutputRuleRevision = 8;
    internal const uint CopyOriginContractsRuleRevision = 1;
}

internal static class TrRmFeatureManifestValidator
{
    internal static void Validate(TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmArtifactSchema schema = TrRmArtifactSchema.Standard)
    {
        if (schema is not (TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.CopyOriginDraft))
        {
            throw InvalidManifest();
        }
        Span<uint> revisions = stackalloc uint[16];
        for (var module = 1; module <= modules.Count; module++)
        {
            ExpandModuleRequirements(features, modules, module, revisions, schema);
        }
    }

    internal static bool Requires(TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, int module, uint featureId)
        => Requires(features, modules, module, featureId, features.Schema);

    internal static bool Requires(TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, int module, uint featureId,
        TrRmArtifactSchema schema)
    {
        if (schema is not (TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.CopyOriginDraft))
        {
            throw InvalidManifest();
        }
        if (!TrRmFeatureTableReader.TryGetRuleRevision(featureId, schema, out _))
        {
            throw InvalidManifest();
        }

        Span<uint> revisions = stackalloc uint[16];
        ExpandModuleRequirements(features, modules, module, revisions, schema);
        return revisions[(int)featureId] != 0;
    }

    private static void ExpandModuleRequirements(TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, int module, Span<uint> revisions,
        TrRmArtifactSchema schema)
    {
        revisions.Clear();
        var (first, count) = modules.GetFeatureRange(module);
        for (var offset = 0; offset < count; offset++)
        {
            var feature = features.GetFeature(first + offset);
            if (feature.FeatureId >= revisions.Length
                || !TrRmFeatureTableReader.TryGetRuleRevision(
                    feature.FeatureId, schema, out var assignedRevision)
                || feature.RuleRevision != assignedRevision)
            {
                throw InvalidManifest();
            }

            ref var revision = ref revisions[(int)feature.FeatureId];
            if (revision != 0 && revision != feature.RuleRevision)
            {
                throw InvalidManifest();
            }

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

                var prerequisiteCount = PrerequisiteCount(featureId);
                for (var prerequisiteIndex = 0; prerequisiteIndex < prerequisiteCount;
                    prerequisiteIndex++)
                {
                    var prerequisite = Prerequisite(featureId, prerequisiteIndex);
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

    private static int PrerequisiteCount(uint featureId) => featureId switch
    {
        2 or 7 => 1,
        3 or 5 or 6 or 12 => 2,
        4 => 1,
        _ => 0,
    };

    private static uint Prerequisite(uint featureId, int index) => featureId switch
    {
        2 or 7 => 1,
        3 or 5 or 6 => index == 0 ? 1u : 2u,
        4 => 3,
        12 => index == 0 ? 10u : 11u,
        _ => throw InvalidManifest(),
    };

    private static InvalidDataException InvalidManifest()
        => new("Invalid TRRM Module Feature manifest.");
}
