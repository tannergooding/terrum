// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A TypeDef's names, flags and owned table ranges.</summary>
/// <param name="NamespaceStringRow">The one-based namespace String row.</param>
/// <param name="NameStringRow">The one-based name String row.</param>
/// <param name="Flags">The assigned visibility and nominal-kind flags.</param>
/// <param name="FieldFirst">The first owned FieldDef row, or zero when empty.</param>
/// <param name="FieldCount">The number of owned FieldDef rows.</param>
/// <param name="GenericFirst">The first owned GenericParam row, or zero when empty.</param>
/// <param name="GenericCount">The number of owned GenericParam rows.</param>
/// <param name="LayoutFirst">The first owned Layout row, or zero when empty.</param>
/// <param name="LayoutCount">The number of owned Layout rows.</param>
/// <param name="ContractFirst">The first owned Contract row, or zero when empty.</param>
/// <param name="ContractCount">The number of owned Contract rows.</param>
public readonly record struct TrRmTypeDefRow(
    int NamespaceStringRow, int NameStringRow, uint Flags,
    int FieldFirst, int FieldCount, int GenericFirst, int GenericCount,
    int LayoutFirst, int LayoutCount, int ContractFirst, int ContractCount);

/// <summary>Emits fixed TypeDef rows with checked names, flags and owned ranges.</summary>
public static class TrRmTypeDefTableWriter
{
    /// <summary>Encodes TypeDef rows and validates their names and owned table ranges.</summary>
    /// <param name="rows">The TypeDef rows in one-based table order.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="modules">The validated Module table.</param>
    /// <param name="fieldCount">The number of FieldDef rows.</param>
    /// <param name="genericParamCount">The number of GenericParam rows.</param>
    /// <param name="layoutCount">The number of Layout rows.</param>
    /// <param name="contractCount">The number of Contract rows.</param>
    /// <returns>The encoded TypeDef section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmTypeDefRow> rows,
        TrRmStringTableReader strings, TrRmModuleTableReader modules,
        int fieldCount, int genericParamCount, int layoutCount, int contractCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 44];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 44;
            Write(result, offset, row.NamespaceStringRow);
            Write(result, offset + 4, row.NameStringRow);
            Write(result, offset + 8, row.Flags);
            Write(result, offset + 12, row.FieldFirst);
            Write(result, offset + 16, row.FieldCount);
            Write(result, offset + 20, row.GenericFirst);
            Write(result, offset + 24, row.GenericCount);
            Write(result, offset + 28, row.LayoutFirst);
            Write(result, offset + 32, row.LayoutCount);
            Write(result, offset + 36, row.ContractFirst);
            Write(result, offset + 40, row.ContractCount);
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
