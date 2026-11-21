// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmForeignInterfaceParameterSurfaceRow(
    uint OwnerMethodDefRow, uint Ordinal, uint NameStringRow,
    uint DefaultConstantSourceRow);

internal static class TrRmForeignInterfaceParameterSurfaceTableWriter
{
    private const int RowSize = 16;

    internal static byte[] Encode(
        IReadOnlyList<TrRmForeignInterfaceParameterSurfaceRow> rows,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!schema.UsesForeignNativeInterfaceParameterSurface())
        {
            throw new InvalidDataException(
                "The MethodParameterSurface writer requires the exact FNI parameter profile.");
        }
        if (rows.Count is < 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        if (rows.Count == 0)
        {
            return [];
        }

        resourceLedger?.ConsumeWork(checked(1UL
            + ((ulong)rows.Count * (1UL + 4UL + (4UL * sizeof(uint))))));
        var orderedRows
            = new TrRmForeignInterfaceParameterSurfaceRow[rows.Count];
        for (var index = 0; index < orderedRows.Length; index++)
        {
            orderedRows[index] = rows[index];
        }

        Array.Sort(orderedRows, static (left, right) =>
        {
            var ownerOrder = left.OwnerMethodDefRow.CompareTo(
                right.OwnerMethodDefRow);
            return ownerOrder != 0 ? ownerOrder
                : left.Ordinal.CompareTo(right.Ordinal);
        });

        var result = new List<byte>(checked(orderedRows.Length * RowSize));
        for (var index = 0; index < orderedRows.Length; index++)
        {
            resourceLedger?.ConsumeWork();
            AppendRow(result, orderedRows[index]);
        }

        var encoded = result.ToArray();
        _ = new TrRmMethodParameterLabelTableReader(encoded,
            orderedRows.Length, strings, methods, signatures, parameters,
            typeSpecs, constantSources, frozenValueNodes, blobs, features,
            modules, schema, resourceLedger);
        return encoded;
    }

    internal static void AppendRow(List<byte> bytes,
        TrRmForeignInterfaceParameterSurfaceRow row)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        AppendUInt32(bytes, row.OwnerMethodDefRow);
        AppendUInt32(bytes, row.Ordinal);
        AppendUInt32(bytes, row.NameStringRow);
        AppendUInt32(bytes, row.DefaultConstantSourceRow);
    }

    private static void AppendUInt32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 24));
    }
}
