// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A FieldDef's owner, name, complete type reference and flags.</summary>
/// <param name="OwnerTypeDefRow">The one-based owning TypeDef row.</param>
/// <param name="NameStringRow">The one-based name String row.</param>
/// <param name="TypeSpecRow">The four-byte TypeSpecRef word.</param>
/// <param name="Flags">The assigned visibility, mutability and static flags.</param>
public readonly record struct TrRmFieldDefRow(
    int OwnerTypeDefRow, int NameStringRow, int TypeSpecRow, uint Flags);

/// <summary>Emits FieldDef rows with checked TypeDef ownership and TypeSpec references.</summary>
public static class TrRmFieldDefTableWriter
{
    /// <summary>Encodes FieldDef rows and validates ownership, names, types and flags.</summary>
    /// <param name="rows">The FieldDef rows in one-based table order.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="typeSpecs">The validated TypeSpec table.</param>
    /// <returns>The encoded FieldDef section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmFieldDefRow> rows,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs)
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
            Write(result, offset, row.OwnerTypeDefRow);
            Write(result, offset + 4, row.NameStringRow);
            Write(result, offset + 8, row.TypeSpecRow);
            Write(result, offset + 12, row.Flags);
        }

        _ = new TrRmFieldDefTableReader(result, rows.Count, strings, types, typeSpecs);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
