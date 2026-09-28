// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmModuleRefRow(
    int ProducerIdentityStringRow, uint TargetRequirement, uint RuntimePolicy);

/// <summary>Emits ModuleRef rows with checked ownership and co-located producer requirements.</summary>
/// <remarks>External producers remain unresolved until import binding.</remarks>
public static class TrRmModuleRefTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmModuleRefRow> rows,
        TrRmStringTableReader strings, TrRmModuleTableReader modules)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 12];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 12;
            Write(result, offset, unchecked((uint)row.ProducerIdentityStringRow));
            Write(result, offset + 4, row.TargetRequirement);
            Write(result, offset + 8, row.RuntimePolicy);
        }

        _ = new TrRmModuleRefTableReader(result, rows.Count, strings, modules);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
