// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmConstraintRow(
    int OwnerGenericParamRow, ushort Kind, int TargetTypeSpecRow, uint WitnessRef);

/// <summary>Emits structural constraints without assigning executable kind or witness semantics.</summary>
public static class TrRmConstraintTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmConstraintRow> rows,
        TrRmGenericParamTableReader formals, TrRmTypeSpecTableReader types)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 16];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 16;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), unchecked((uint)row.OwnerGenericParamRow));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset + 4), row.Kind);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 8), unchecked((uint)row.TargetTypeSpecRow));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 12), row.WitnessRef);
        }

        _ = new TrRmConstraintTableReader(result, rows.Count, formals, types);
        return result;
    }
}
