// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmTypeDefRow(
    int NamespaceStringRow, int NameStringRow, uint Flags,
    int FieldFirst, int FieldCount, int GenericFirst, int GenericCount,
    int LayoutRow, int ContractFirst, int ContractCount);

/// <summary>Emits fixed TypeDef rows with checked names, flags and owned ranges.</summary>
public static class TrRmTypeDefTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmTypeDefRow> rows,
        TrRmStringTableReader strings, TrRmModuleTableReader modules,
        int fieldCount, int genericParamCount, int layoutCount, int contractCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 40];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 40;
            Write(result, offset, row.NamespaceStringRow);
            Write(result, offset + 4, row.NameStringRow);
            Write(result, offset + 8, row.Flags);
            Write(result, offset + 12, row.FieldFirst);
            Write(result, offset + 16, row.FieldCount);
            Write(result, offset + 20, row.GenericFirst);
            Write(result, offset + 24, row.GenericCount);
            Write(result, offset + 28, row.LayoutRow);
            Write(result, offset + 32, row.ContractFirst);
            Write(result, offset + 36, row.ContractCount);
        }

        _ = new TrRmTypeDefTableReader(result, rows.Count, strings, modules,
            fieldCount, genericParamCount, layoutCount, contractCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
