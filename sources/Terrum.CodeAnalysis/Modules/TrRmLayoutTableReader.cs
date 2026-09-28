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

    public TrRmLayoutTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader types)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || recordCount != types.LayoutCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidLayout();
        }

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

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidLayout()
        => new("Invalid TRRM Layout table.");
}
