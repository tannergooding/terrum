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
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Validates SourceFile rows and their Module ownership.</summary>
    /// <param name="section">The borrowed SourceFile section bytes.</param>
    /// <param name="recordCount">The number of SourceFile rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="modules">The validated Module table.</param>
    public TrRmSourceFileTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleTableReader modules)
        : this(section, recordCount, strings, modules, null)
    {
    }

    internal TrRmSourceFileTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmModuleTableReader modules,
        TrRmA3LayoutResourceLedger? resourceLedger)
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
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            _ = ReadPath(row - 1);
            if (Read(row - 1, 4) != 0)
            {
                throw InvalidSource();
            }
        }
    }

    /// <summary>Gets the diagnostic path recorded by a SourceFile row.</summary>
    /// <param name="row">The one-based SourceFile row.</param>
    /// <returns>The path string; no local file is read.</returns>
    public string GetPath(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidSource();
        }

        return ReadPath(row - 1);
    }

    /// <summary>Gets the number of validated SourceFile rows.</summary>
    public int Count => _recordCount;

    /// <summary>Gets the Module row that owns a SourceFile row.</summary>
    /// <param name="row">The one-based SourceFile row.</param>
    /// <returns>The one-based owning Module row.</returns>
    public int GetOwnerModule(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidSource();
        }

        var low = 1;
        var high = _modules.Count;
        while (low <= high)
        {
            _resourceLedger?.ConsumeWork(2);
            var middle = low + ((high - low) / 2);
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
        if (reference is 0 or > 0xFF_FFFF)
        {
            throw InvalidSource();
        }

        return _strings.DecodeName((int)reference);
    }

    private uint Read(int row, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            _rows.Slice((row * RowSize) + offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidSource()
        => new("Invalid TRRM SourceFile table.");
}
