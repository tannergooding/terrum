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
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (schema is not (TrRmArtifactSchema.Standard
            or TrRmArtifactSchema.CopyOriginDraft
            or TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
            or TrRmArtifactSchema.ScalarFrozenValuesDraft
            or TrRmArtifactSchema.NominalEnumsDraft
            or TrRmArtifactSchema.RootPinRuntimeDraft
            or TrRmArtifactSchema.GeneralPredicateGraphsDraft))
        {
            throw InvalidFeature();
        }
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var orderedRows = new TrRmFeatureRow[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!TrRmFeatureTableReader.TryGetRuleRevision(row.FeatureId,
                    schema, out var assignedRevision)
                || row.RuleRevision != assignedRevision)
            {
                throw InvalidFeature();
            }

            orderedRows[index] = row;
        }

        Array.Sort(orderedRows, static (left, right) => left.FeatureId.CompareTo(right.FeatureId));
        var distinctCount = 0;
        for (var index = 0; index < orderedRows.Length; index++)
        {
            var row = orderedRows[index];
            if (distinctCount != 0
                && orderedRows[distinctCount - 1].FeatureId == row.FeatureId)
            {
                if (orderedRows[distinctCount - 1].RuleRevision != row.RuleRevision)
                {
                    throw InvalidFeature();
                }

                continue;
            }

            orderedRows[distinctCount++] = row;
        }

        var result = new byte[distinctCount * 8];
        for (var index = 0; index < distinctCount; index++)
        {
            var row = orderedRows[index];
            Write(result, index * 8, row.FeatureId);
            Write(result, (index * 8) + 4, row.RuleRevision);
        }

        _ = new TrRmFeatureTableReader(result, distinctCount, schema);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static InvalidDataException InvalidFeature()
        => new("Invalid TRRM Feature table.");
}
