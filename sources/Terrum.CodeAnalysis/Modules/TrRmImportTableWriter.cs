// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmImportRow(
    int ModuleRefRow, int ExportKeyStringRow, uint ExpectedLocalDefId);

/// <summary>Emits retained import expectations without binding a producer or its contract.</summary>
public static class TrRmImportTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmImportRow> rows,
        TrRmStringTableReader strings, TrRmModuleRefTableReader references,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties)
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
            Write(result, offset, unchecked((uint)row.ModuleRefRow));
            Write(result, offset + 4, unchecked((uint)row.ExportKeyStringRow));
            Write(result, offset + 8, row.ExpectedLocalDefId);
        }

        _ = new TrRmImportTableReader(result, rows.Count,
            strings, references, types, methods, properties);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
