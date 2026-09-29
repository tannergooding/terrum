// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmLayoutRow(
    int OwnerTypeDefRow, uint TargetRequirement, uint Size, uint Alignment, uint Pack, uint Flags);

/// <summary>Emits Layout rows with checked published TypeDef ownership and assigned target.</summary>
public static class TrRmLayoutTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmLayoutRow> rows, TrRmTypeDefTableReader types)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 24];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 24;
            Write(result, offset, row.OwnerTypeDefRow);
            Write(result, offset + 4, row.TargetRequirement);
            Write(result, offset + 8, row.Size);
            Write(result, offset + 12, row.Alignment);
            Write(result, offset + 16, row.Pack);
            Write(result, offset + 20, row.Flags);
        }

        _ = new TrRmLayoutTableReader(result, rows.Count, types);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
