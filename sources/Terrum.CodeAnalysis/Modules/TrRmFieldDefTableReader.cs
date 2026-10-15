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

    /// <summary>Validates FieldDef rows and requires layouts for fixed-offset fields.</summary>
    public TrRmFieldDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs)
        : this(section, recordCount, strings, types, typeSpecs, default, false)
    {
    }

    /// <summary>Validates FieldDef rows and fixed offsets against their assigned layout sizes.</summary>
    public TrRmFieldDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmLayoutTableReader layouts)
        : this(section, recordCount, strings, types, typeSpecs, layouts, true)
    {
    }

    private TrRmFieldDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmLayoutTableReader layouts, bool validateLayoutSizes)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || recordCount != types.FieldCount || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidField();
        }

        _rows = section;
        _strings = strings;
        _recordCount = recordCount;
        var invalidLayoutSize = false;
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
                || (flags & ~7u) != 0 || (flags & 3) == 3)
            {
                throw InvalidField();
            }

            if (offset != uint.MaxValue)
            {
                var layoutRow = types.GetLayoutRow((int)owner);
                if (layoutRow == 0)
                {
                    throw InvalidField();
                }

                if (validateLayoutSizes && offset >= layouts.GetSize(layoutRow))
                {
                    invalidLayoutSize = true;
                }
            }

            _ = _strings.DecodeName((int)name);
        }

        if (invalidLayoutSize)
        {
            throw InvalidField();
        }
    }

    /// <summary>Gets the decoded name for a FieldDef row.</summary>
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

    /// <summary>Gets the referenced TypeSpec row for a validated FieldDef row.</summary>
    public int GetTypeSpecRow(int row)
    {
        CheckRow(row);
        return checked((int)Read(row - 1, 8));
    }

    /// <summary>Gets the flags for a validated FieldDef row.</summary>
    public uint GetFlags(int row)
    {
        CheckRow(row);
        return Read(row - 1, 12);
    }

    /// <summary>Gets the byte offset for a validated FieldDef row.</summary>
    public uint GetByteOffset(int row)
    {
        CheckRow(row);
        return Read(row - 1, 16);
    }

    private void CheckRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidField();
        }
    }

    private uint Read(int row, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice((row * RowSize) + offset, sizeof(uint)));

    private static InvalidDataException InvalidField()
        => new("Invalid TRRM FieldDef table.");
}
