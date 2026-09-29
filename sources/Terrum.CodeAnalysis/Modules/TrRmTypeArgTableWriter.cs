// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmTypeArgRow(int OwnerTypeSpecRow, int Ordinal, int ArgumentTypeSpecRow);

/// <summary>Emits TypeArg rows with checked constructed-type ownership and ordering.</summary>
public static class TrRmTypeArgTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmTypeArgRow> rows, TrRmTypeSpecTableReader types)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 12];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 12;
            Write(result, offset, row.OwnerTypeSpecRow);
            Write(result, offset + 4, row.Ordinal);
            Write(result, offset + 8, row.ArgumentTypeSpecRow);
        }

        _ = new TrRmTypeArgTableReader(result, rows.Count, types);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), unchecked((uint)value));
}
