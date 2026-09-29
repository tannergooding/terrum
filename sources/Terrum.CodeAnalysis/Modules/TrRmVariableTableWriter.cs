// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Writes offset-indexed variable rows; clause and instruction semantics are validated separately.</summary>
public static class TrRmVariableTableWriter
{
    public static byte[] EncodeBlobs(IReadOnlyList<ReadOnlyMemory<byte>> rows)
        => Encode(rows, 0);

    public static byte[] EncodeBodies(IReadOnlyList<ReadOnlyMemory<byte>> rows)
        => Encode(rows, 1);

    internal static byte[] EncodeContracts(IReadOnlyList<ReadOnlyMemory<byte>> rows)
        => Encode(rows, 8);

    private static byte[] Encode(IReadOnlyList<ReadOnlyMemory<byte>> rows, int minimumRowBytes)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var indexBytes = ((long)rows.Count + 1) * sizeof(uint);
        var dataBytes = 0L;
        foreach (var row in rows)
        {
            if (row.Length < minimumRowBytes)
            {
                throw new ArgumentException("A TRRM variable row is shorter than its minimum size.", nameof(rows));
            }

            dataBytes += row.Length;
            if (indexBytes + dataBytes > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(rows),
                    "TRRM variable section exceeds host byte-array limits.");
            }
        }

        var result = new byte[(int)(indexBytes + dataBytes)];
        var offset = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * sizeof(uint)), (uint)offset);
            rows[index].Span.CopyTo(result.AsSpan((int)indexBytes + offset));
            offset += rows[index].Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(rows.Count * sizeof(uint)), (uint)offset);
        return result;
    }
}
