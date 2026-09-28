// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validated index over one present TRRM String section.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmStringTableReader
{
    private readonly ReadOnlySpan<byte> _index;
    private readonly ReadOnlySpan<byte> _data;
    private readonly int _recordCount;

    public TrRmStringTableReader(ReadOnlySpan<byte> section, int recordCount)
    {
        var indexLength = ((long)recordCount + 1) * sizeof(uint);
        if (recordCount <= 0 || recordCount > 0xFF_FFFF || indexLength > section.Length)
        {
            throw InvalidIndex();
        }

        _recordCount = recordCount;
        _index = section.Slice(0, (int)indexLength);
        _data = section.Slice((int)indexLength);
        var previous = ReadOffset(0);
        if (previous != 0)
        {
            throw InvalidIndex();
        }

        for (var row = 1; row <= recordCount; row++)
        {
            var next = ReadOffset(row);
            if (next > _data.Length || next < previous + 8 || (next & 3) != 0)
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

    public string DecodeName(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw new InvalidDataException("TRRM String row index is out of range.");
        }

        var start = (int)ReadOffset(row - 1);
        var end = (int)ReadOffset(row);
        if (start < 0 || start > _data.Length - 8 || end > _data.Length
            || end < start + 8 || ((start | end) & 3) != 0)
        {
            throw InvalidIndex();
        }

        return TrRmStringRowReader.DecodeName(_data.Slice(start, end - start));
    }

    private uint ReadOffset(int index)
        => BinaryPrimitives.ReadUInt32LittleEndian(_index.Slice(index * sizeof(uint), sizeof(uint)));

    private static InvalidDataException InvalidIndex()
        => new("Invalid TRRM String table index.");
}
