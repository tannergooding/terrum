// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Emits canonical exact Feature requirements for TRRM Module manifests.</summary>
public static class TrRmFeatureTableWriter
{
    /// <summary>Encodes assigned exact Feature pairs in ascending identifier order without duplicates.</summary>
    /// <param name="rows">The required feature pairs; repeated identical pairs are coalesced.</param>
    /// <returns>The encoded Feature section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmFeatureRow> rows)
        => Encode(rows, TrRmArtifactSchema.Standard);

    internal static byte[] Encode(IReadOnlyList<TrRmFeatureRow> rows,
        TrRmArtifactSchema schema)
        => Encode(rows, schema, null);

    internal static byte[] Encode(IReadOnlyList<TrRmFeatureRow> rows,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        ArgumentNullException.ThrowIfNull(rows);
        resourceLedger?.ConsumeWork();
        if (schema is not (TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
            or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft
            or TrRmArtifactSchema.CopyOriginDraft
            or TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignNativeInterfaceDraft
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
            or TrRmArtifactSchema.InitializationReferencePointerLoadNativeParameterDraft
            or TrRmArtifactSchema.InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft
            or TrRmArtifactSchema.RegisterExceptionRegionsDraft
            or TrRmArtifactSchema.GenericMethodCallsDraft
            or TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft
            or TrRmArtifactSchema.GenericMethodCallsParameterLabelsReceiverAccessDraft
            or TrRmArtifactSchema.C1Draft
            or TrRmArtifactSchema.C1ParameterLabelsReceiverAccessDraft
            or TrRmArtifactSchema.C1M1Draft
            or TrRmArtifactSchema.C1M1ParameterLabelsReceiverAccessDraft
            or TrRmArtifactSchema.C1M1ConstructionDraft
            or TrRmArtifactSchema.C1M1ConstructionParameterLabelsReceiverAccessDraft
            or TrRmArtifactSchema.C1M1Construction2Draft
            or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessDraft
            or TrRmArtifactSchema.C1M1Construction2InitializerCopyEvidenceDraft
            or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft
            or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft
            or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsDraft
            or TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsAssociatedRelationConstraintsInitializerCopyEvidenceDraft
            or TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateDraft
            or TrRmArtifactSchema.C1M1Construction2L1R1PropertyInitializedStateDraft
            or TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureDraft
            or TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureDraft
            or TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureInitializerCopyEvidenceDraft
            or TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureInitializerCopyEvidenceDraft
            or TrRmArtifactSchema.C1M1ConstructionL1R1EnumClosureClassInstanceLayoutInitializerCopyEvidenceDraft
            or TrRmArtifactSchema.C1M1Construction2L1R1EnumClosureClassInstanceLayoutInitializerCopyEvidenceDraft
            or TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateEnumClosureDraft
            or TrRmArtifactSchema.C1M1Construction2L1R1PropertyInitializedStateEnumClosureDraft
            or TrRmArtifactSchema.GenericConstantMethodsDraft))
        {
            throw InvalidFeature();
        }
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var featureCount = (ulong)rows.Count;
        var orderedStateWork = checked(
            1UL + (featureCount * (1UL + (2UL * sizeof(uint)))));
        resourceLedger?.ConsumeWork(orderedStateWork);
        var orderedRows = new TrRmFeatureRow[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            resourceLedger?.ConsumeWork();
            var row = rows[index];
            resourceLedger?.ConsumeWork(2);
            if (!TrRmFeatureTableReader.TryGetRuleRevision(row.FeatureId,
                    schema, out var assignedRevision)
                || row.RuleRevision != assignedRevision)
            {
                throw InvalidFeature();
            }

            orderedRows[index] = row;
        }

        if (resourceLedger is null)
        {
            Array.Sort(orderedRows,
                static (left, right) => left.FeatureId.CompareTo(right.FeatureId));
        }
        else
        {
            for (var index = 1; index < orderedRows.Length; index++)
            {
                resourceLedger.ConsumeWork();
                var row = orderedRows[index];
                var insertionIndex = index;
                while (insertionIndex > 0)
                {
                    resourceLedger.ConsumeWork(2);
                    if (orderedRows[insertionIndex - 1].FeatureId
                        <= row.FeatureId)
                    {
                        break;
                    }

                    resourceLedger.ConsumeWork(1
                        + (2 * sizeof(uint)));
                    orderedRows[insertionIndex]
                        = orderedRows[insertionIndex - 1];
                    insertionIndex--;
                }

                resourceLedger.ConsumeWork(1 + (2 * sizeof(uint)));
                orderedRows[insertionIndex] = row;
            }
        }

        var distinctCount = 0;
        for (var index = 0; index < orderedRows.Length; index++)
        {
            resourceLedger?.ConsumeWork();
            var row = orderedRows[index];
            resourceLedger?.ConsumeWork();
            if (distinctCount != 0
                && orderedRows[distinctCount - 1].FeatureId == row.FeatureId)
            {
                resourceLedger?.ConsumeWork();
                if (orderedRows[distinctCount - 1].RuleRevision != row.RuleRevision)
                {
                    throw InvalidFeature();
                }

                continue;
            }

            resourceLedger?.ConsumeWork(1 + (2 * sizeof(uint)));
            orderedRows[distinctCount++] = row;
        }

        resourceLedger?.ConsumeWork(checked(1UL
            + ((ulong)distinctCount * (1UL + (2UL * sizeof(uint))))));
        var result = new byte[distinctCount * 8];
        for (var index = 0; index < distinctCount; index++)
        {
            resourceLedger?.ConsumeWork(2);
            var row = orderedRows[index];
            Write(result, index * 8, row.FeatureId);
            Write(result, (index * 8) + 4, row.RuleRevision);
        }

        _ = new TrRmFeatureTableReader(result, distinctCount, schema,
            resourceLedger);
        return result;
    }

    internal static byte[] EncodeModuleRanges(
        IReadOnlyList<TrRmFeatureRow> rows,
        TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[checked(rows.Count * 2 * sizeof(uint))];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!TrRmFeatureTableReader.TryGetRuleRevision(row.FeatureId,
                    schema, out var assignedRevision)
                || row.RuleRevision != assignedRevision)
            {
                throw InvalidFeature();
            }

            var offset = index * 2 * sizeof(uint);
            Write(result, offset, row.FeatureId);
            Write(result, offset + sizeof(uint), row.RuleRevision);
        }

        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static InvalidDataException InvalidFeature()
        => new("Invalid TRRM Feature table.");
}
