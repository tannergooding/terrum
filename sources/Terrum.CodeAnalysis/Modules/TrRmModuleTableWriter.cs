// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmModuleRow(
    int IdentityStringRow, uint TargetRequirement, uint RuntimePolicy, int EntryMethodDefRow,
    int ModuleRefStart, int TypeDefStart, int MethodDefStart, int SourceFileStart);

/// <summary>Emits Module rows with validated identities and artifact-wide ownership partitions.</summary>
public static class TrRmModuleTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmModuleRow> rows,
        TrRmStringTableReader strings, int moduleRefCount, int typeDefCount,
        int methodDefCount, int sourceFileCount)
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
            Write(result, offset, row.IdentityStringRow);
            Write(result, offset + 4, row.TargetRequirement);
            Write(result, offset + 8, row.RuntimePolicy);
            Write(result, offset + 12, row.EntryMethodDefRow);
            Write(result, offset + 28, row.ModuleRefStart);
            Write(result, offset + 32, row.TypeDefStart);
            Write(result, offset + 36, row.MethodDefStart);
            Write(result, offset + 40, row.SourceFileStart);
        }

        _ = new TrRmModuleTableReader(result, rows.Count, strings,
            moduleRefCount, typeDefCount, methodDefCount, sourceFileCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
