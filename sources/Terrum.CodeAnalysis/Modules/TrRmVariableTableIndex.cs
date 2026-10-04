// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Bounds one present, offset-indexed TRRM table.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this index is in use.</remarks>
public readonly ref struct TrRmVariableTableIndex
{
    private readonly ReadOnlySpan<byte> _offsets;
    private readonly ReadOnlySpan<byte> _data;
    private readonly int _recordCount;
    private readonly int _minimumRowBytes;
    private readonly int _rowAlignment;

    public int Count => _recordCount;

    public TrRmVariableTableIndex(ReadOnlySpan<byte> section, int recordCount,
        int minimumRowBytes, int rowAlignment)
    {
        var indexBytes = ((long)recordCount + 1) * sizeof(uint);
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || minimumRowBytes < 0 || rowAlignment is not (1 or 4)
            || indexBytes > section.Length)
        {
            throw InvalidIndex();
        }

        _offsets = section.Slice(0, (int)indexBytes);
        _data = section.Slice((int)indexBytes);
        _recordCount = recordCount;
        _minimumRowBytes = minimumRowBytes;
        _rowAlignment = rowAlignment;
        var previous = ReadOffset(0);
        if (previous != 0)
        {
            throw InvalidIndex();
        }

        for (var row = 1; row <= recordCount; row++)
        {
            var next = ReadOffset(row);
            if (next > _data.Length || next < previous + minimumRowBytes
                || next % rowAlignment != 0)
            {
                throw InvalidIndex();
            }

            previous = next;
        }

        if (previous != _data.Length)
        {
            throw InvalidIndex();
        }
    }

    public ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidIndex();
        }

        var start = (int)ReadOffset(row - 1);
        var end = (int)ReadOffset(row);
        // Row alignment is restricted to 1 or 4, so one low-bit mask checks both offsets.
        if (start < 0 || start > _data.Length - _minimumRowBytes
            || end > _data.Length || end < start + _minimumRowBytes
            || ((start | end) & (_rowAlignment - 1)) != 0)
        {
            throw InvalidIndex();
        }

        return _data.Slice(start, end - start);
    }

    private uint ReadOffset(int row)
        => BinaryPrimitives.ReadUInt32LittleEndian(_offsets.Slice(row * sizeof(uint), sizeof(uint)));

    private static InvalidDataException InvalidIndex()
        => new("Invalid TRRM variable table index.");
}
