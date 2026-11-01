// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Emits registered target-profile identities.</summary>
public static class TrRmTargetProfileTableWriter
{
    /// <summary>Encodes target-profile rows supported by this writer.</summary>
    /// <param name="rows">The ordered profile identities.</param>
    /// <returns>The encoded TargetProfile section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmTargetProfileRow> rows)
        => Encode(rows, TrRmArtifactSchema.Standard);

    internal static byte[] Encode(IReadOnlyList<TrRmTargetProfileRow> rows,
        TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF
            || schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.Development
                or TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft
                or TrRmArtifactSchema.CopyOriginDraft
                or TrRmArtifactSchema.ForeignInvocationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                or TrRmArtifactSchema.ScalarFrozenValuesDraft
                or TrRmArtifactSchema.NominalEnumsDraft
                or TrRmArtifactSchema.RootPinRuntimeDraft
                or TrRmArtifactSchema.GeneralPredicateGraphsDraft))
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * sizeof(ulong)];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!TrRmTargetProfileTableReader.IsAssigned(
                    row.ProfileId, row.RuleRevision, schema))
            {
                throw new InvalidDataException("Unassigned TRRM TargetProfile identity.");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(index * sizeof(ulong)), row.ProfileId);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan((index * sizeof(ulong)) + sizeof(uint)), row.RuleRevision);
        }

        _ = new TrRmTargetProfileTableReader(result, rows.Count, schema);
        return result;
    }
}
