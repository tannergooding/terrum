// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates optional diagnostic source identities without reading local files.</summary>
/// <remarks>The borrowed SourceFile, Module and String sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmSourceFileTableReader
{
    private const int RowSize = 8;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly TrRmModuleTableReader _modules;
    private readonly int _recordCount;

    public TrRmSourceFileTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleTableReader modules)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || recordCount != modules.SourceFileCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidSource();
        }

        _rows = section;
        _strings = strings;
        _modules = modules;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            _ = ReadPath(row - 1);
            if (Read(row - 1, 4) != 0)
            {
                throw InvalidSource();
            }
        }
    }

    public string GetPath(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidSource();
        }

        return ReadPath(row - 1);
    }

    public int GetOwnerModule(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidSource();
        }

        var low = 1;
        var high = _modules.Count;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var (start, end) = _modules.GetSourceFileRange(middle);
            if (row < start)
            {
                high = middle - 1;
            }
            else if (row >= end)
            {
                low = middle + 1;
            }
            else
            {
                return middle;
            }
        }

        throw InvalidSource();
    }

    private string ReadPath(int row)
    {
        var reference = Read(row, 0);
        if (reference == 0 || reference > 0xFF_FFFF)
        {
            throw InvalidSource();
        }

        return _strings.DecodeName((int)reference);
    }

    private uint Read(int row, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice(row * RowSize + offset, sizeof(uint)));

    private static InvalidDataException InvalidSource()
        => new("Invalid TRRM SourceFile table.");
}
