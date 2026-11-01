// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Writes offset-indexed variable rows; clause and instruction semantics are validated separately.</summary>
public static class TrRmVariableTableWriter
{
    public static byte[] EncodeBlobs(IReadOnlyList<ReadOnlyMemory<byte>> rows)
        => Encode(rows, 0, null);

    public static byte[] EncodeBodies(IReadOnlyList<ReadOnlyMemory<byte>> rows)
        => Encode(rows, 1, null);

    internal static byte[] EncodeBlobs(IReadOnlyList<ReadOnlyMemory<byte>> rows,
        TrRmA3LayoutResourceLedger resourceLedger)
        => Encode(rows, 0, resourceLedger);

    internal static byte[] EncodeBodies(IReadOnlyList<ReadOnlyMemory<byte>> rows,
        TrRmA3LayoutResourceLedger resourceLedger)
        => Encode(rows, 1, resourceLedger);

    private static byte[] Encode(IReadOnlyList<ReadOnlyMemory<byte>> rows,
        int minimumRowBytes, TrRmA3LayoutResourceLedger? resourceLedger)
    {
        ArgumentNullException.ThrowIfNull(rows);
        resourceLedger?.ConsumeWork();
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        resourceLedger?.ConsumeWork(2);
        var indexBytes = ((long)rows.Count + 1) * sizeof(uint);
        var dataBytes = 0L;
        for (var index = 0; index < rows.Count; index++)
        {
            resourceLedger?.ConsumeWork();
            var row = rows[index];
            resourceLedger?.ConsumeWork();
            if (row.Length < minimumRowBytes)
            {
                throw new ArgumentException("A TRRM variable row is shorter than its minimum size.", nameof(rows));
            }

            resourceLedger?.ConsumeWork(2);
            dataBytes += row.Length;
            resourceLedger?.ConsumeWork();
            if (indexBytes + dataBytes > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(rows),
                    "TRRM variable section exceeds host byte-array limits.");
            }
        }

        var resultLength = checked((ulong)(indexBytes + dataBytes));
        resourceLedger?.ConsumeWork(checked(resultLength + 1UL
            + (ulong)rows.Count + 1UL));
        var result = new byte[(int)resultLength];
        var offset = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            resourceLedger?.ConsumeWork();
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * sizeof(uint)), (uint)offset);
            resourceLedger?.ConsumeWork();
            rows[index].Span.CopyTo(result.AsSpan((int)indexBytes + offset));
            resourceLedger?.ConsumeWork();
            offset += rows[index].Length;
        }

        resourceLedger?.ConsumeWork();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(rows.Count * sizeof(uint)), (uint)offset);
        return result;
    }
}
