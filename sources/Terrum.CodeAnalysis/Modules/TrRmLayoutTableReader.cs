// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Checks Layout row framing, assigned target and unique TypeDef ownership.</summary>
/// <remarks>Does not establish target-specific size, alignment, packing or ABI validity. Borrowed sections must remain unchanged.</remarks>
public readonly ref struct TrRmLayoutTableReader
{
    private const int RowSize = 24;
    private readonly ReadOnlySpan<byte> _rows;

    public TrRmLayoutTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader types)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || recordCount != types.LayoutCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidLayout();
        }

        _rows = section;
        var owners = new int[recordCount + 1];
        for (var type = 1; type <= types.Count; type++)
        {
            var row = types.GetLayoutRow(type);
            if (row == 0)
            {
                continue;
            }

            if (row > recordCount || owners[row] != 0)
            {
                throw InvalidLayout();
            }

            owners[row] = type;
        }

        for (var row = 1; row <= recordCount; row++)
        {
            var data = section.Slice((row - 1) * RowSize, RowSize);
            if (owners[row] == 0 || Read(data, 0) != owners[row]
                || Read(data, 4) > 1 || Read(data, 20) != 0)
            {
                throw InvalidLayout();
            }
        }
    }

    public uint GetSize(int row)
    {
        if (row <= 0 || (long)row * RowSize > _rows.Length)
        {
            throw InvalidLayout();
        }

        return Read(_rows.Slice((row - 1) * RowSize, RowSize), 8);
    }

    /// <summary>Gets the target requirement for a validated Layout row.</summary>
    public uint GetTargetRequirement(int row) => GetValue(row, 4);

    /// <summary>Gets the alignment for a validated Layout row.</summary>
    public uint GetAlignment(int row) => GetValue(row, 12);

    /// <summary>Gets the packing value for a validated Layout row.</summary>
    public uint GetPack(int row) => GetValue(row, 16);

    /// <summary>Gets the flags for a validated Layout row.</summary>
    public uint GetFlags(int row) => GetValue(row, 20);

    private uint GetValue(int row, int offset)
    {
        if (row <= 0 || (long)row * RowSize > _rows.Length)
        {
            throw InvalidLayout();
        }

        return Read(_rows.Slice((row - 1) * RowSize, RowSize), offset);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidLayout()
        => new("Invalid TRRM Layout table.");
}
