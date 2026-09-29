// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmFieldDefRow(
    int OwnerTypeDefRow, int NameStringRow, int TypeSpecRow, uint Flags, uint ByteOffset);

/// <summary>Emits FieldDef rows with checked TypeDef ownership and TypeSpec references.</summary>
public static class TrRmFieldDefTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmFieldDefRow> rows,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 20];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 20;
            Write(result, offset, row.OwnerTypeDefRow);
            Write(result, offset + 4, row.NameStringRow);
            Write(result, offset + 8, row.TypeSpecRow);
            Write(result, offset + 12, row.Flags);
            Write(result, offset + 16, row.ByteOffset);
        }

        _ = new TrRmFieldDefTableReader(result, rows.Count, strings, types, typeSpecs);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
