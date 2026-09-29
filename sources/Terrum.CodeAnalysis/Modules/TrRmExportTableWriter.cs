// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmExportRow(uint LocalDefId, int ExportKeyStringRow);

/// <summary>Emits public export keys after checking ownership, visibility and uniqueness.</summary>
public static class TrRmExportTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmExportRow> rows,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmPropertyDefTableReader properties,
        TrRmImportTableReader imports)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 8];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), row.LocalDefId);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 4),
                unchecked((uint)row.ExportKeyStringRow));
        }

        _ = new TrRmExportTableReader(result, rows.Count,
            strings, types, methods, properties, imports);
        return result;
    }
}
