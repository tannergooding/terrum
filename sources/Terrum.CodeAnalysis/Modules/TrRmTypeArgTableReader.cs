// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates TypeArg ownership, ordinals and structural TypeSpec ranges.</summary>
/// <remarks>The borrowed TypeArg and TypeSpec sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmTypeArgTableReader
{
    private const int RowSize = 12;
    private readonly ReadOnlySpan<byte> _rows;

    public TrRmTypeArgTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeSpecTableReader types)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF || recordCount != types.TypeArgCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidArgument();
        }

        _rows = section;
        var ownedCounts = new Dictionary<uint, int>();
        for (var row = 1; row <= recordCount; row++)
        {
            var owner = Read(row, 0);
            if (owner == 0 || owner > types.Count)
            {
                throw InvalidArgument();
            }

            var spec = types.GetRow((int)owner);
            if (spec[0] != 0x05)
            {
                throw InvalidArgument();
            }

            var first = BinaryPrimitives.ReadUInt32LittleEndian(spec[6..]);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(spec[10..]);
            if (row < first || row >= first + count
                || Read(row, 4) != row - first
                || Read(row, 8) == 0 || Read(row, 8) >= owner)
            {
                throw InvalidArgument();
            }

            ownedCounts.TryGetValue(owner, out var owned);
            ownedCounts[owner] = owned + 1;
        }

        for (var row = 1; row <= types.Count; row++)
        {
            var spec = types.GetRow(row);
            if (spec[0] == 0x05)
            {
                ownedCounts.TryGetValue((uint)row, out var owned);
                if (owned != BinaryPrimitives.ReadUInt32LittleEndian(spec[10..]))
                {
                    throw InvalidArgument();
                }
            }
        }
    }

    private uint Read(int row, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice((row - 1) * RowSize + offset, sizeof(uint)));

    private static InvalidDataException InvalidArgument()
        => new("Invalid TRRM TypeArg table.");
}
