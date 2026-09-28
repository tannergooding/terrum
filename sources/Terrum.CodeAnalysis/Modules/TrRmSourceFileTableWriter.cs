// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Emits optional SourceFile identities owned by Module partitions.</summary>
public static class TrRmSourceFileTableWriter
{
    public static byte[] Encode(IReadOnlyList<int> pathStringRows,
        TrRmStringTableReader strings, TrRmModuleTableReader modules)
    {
        ArgumentNullException.ThrowIfNull(pathStringRows);
        if (pathStringRows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(pathStringRows));
        }

        var result = new byte[pathStringRows.Count * 8];
        for (var index = 0; index < pathStringRows.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * 8),
                unchecked((uint)pathStringRows[index]));
        }

        _ = new TrRmSourceFileTableReader(result, pathStringRows.Count, strings, modules);
        return result;
    }
}
