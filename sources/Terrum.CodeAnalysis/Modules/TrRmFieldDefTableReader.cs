// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates FieldDef references, flags and TypeDef range ownership.</summary>
/// <remarks>The borrowed FieldDef, TypeDef and String sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmFieldDefTableReader
{
    private const int RowSize = 20;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly int _recordCount;

    public TrRmFieldDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || recordCount != types.FieldCount || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidField();
        }

        _rows = section;
        _strings = strings;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var owner = Read(row - 1, 0);
            var name = Read(row - 1, 4);
            var spec = Read(row - 1, 8);
            var flags = Read(row - 1, 12);
            var offset = Read(row - 1, 16);
            if (owner == 0 || owner > types.Count || owner != types.GetFieldOwner(row)
                || name is 0 or > 0xFF_FFFF
                || spec == 0 || spec > typeSpecs.Count
                || (flags & ~7u) != 0 || (flags & 3) == 3
                || (offset != uint.MaxValue && types.GetLayoutRow((int)owner) == 0))
            {
                throw InvalidField();
            }

            _ = _strings.DecodeName((int)name);
        }
    }

    public string GetName(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidField();
        }

        var name = Read(row - 1, 4);
        if (name is 0 or > 0xFF_FFFF)
        {
            throw InvalidField();
        }

        return _strings.DecodeName((int)name);
    }

    private uint Read(int row, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice(row * RowSize + offset, sizeof(uint)));

    private static InvalidDataException InvalidField()
        => new("Invalid TRRM FieldDef table.");
}
