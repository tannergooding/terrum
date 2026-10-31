// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validated module dependency references; external producers still require binding.</summary>
/// <remarks>The borrowed sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmModuleRefTableReader
{
    private const int RowSize = 12;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly TrRmModuleTableReader _modules;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    public int Count => _recordCount;

    public TrRmModuleRefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleTableReader modules)
        : this(section, recordCount, strings, modules, null)
    {
    }

    internal TrRmModuleRefTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmModuleTableReader modules,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(3);
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != modules.ModuleRefCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidRef();
        }

        _rows = section;
        _strings = strings;
        _modules = modules;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        for (var module = 1; module <= modules.Count; module++)
        {
            _resourceLedger?.ConsumeWork();
            var (start, end) = modules.GetModuleRefRange(module);
            for (var row = start; row < end; row++)
            {
                _resourceLedger?.ConsumeWork();
                var producer = ReadIdentity(row - 1);
                var target = Read(row - 1, 4);
                var policy = Read(row - 1, 8);
                _resourceLedger?.ConsumeWork(2);
                if (target > 1 || policy > 1)
                {
                    throw InvalidRef();
                }

                if (modules.TryFindModule(producer, out var selected))
                {
                    var selectedRequirements = modules.GetRequirements(selected);
                    if (target != selectedRequirements.TargetRequirement
                        || policy != selectedRequirements.RuntimePolicy)
                    {
                        throw InvalidRef();
                    }
                }
            }
        }
    }

    public string GetProducerIdentity(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidRef();
        }

        return ReadIdentity(row - 1);
    }

    public (uint TargetRequirement, uint RuntimePolicy) GetRequirements(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidRef();
        }

        return (Read(row - 1, 4), Read(row - 1, 8));
    }

    public int GetOwnerModule(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidRef();
        }

        var low = 1;
        var high = _modules.Count;
        while (low <= high)
        {
            _resourceLedger?.ConsumeWork();
            var middle = low + (high - low) / 2;
            var (start, end) = _modules.GetModuleRefRange(middle);
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

        throw InvalidRef();
    }

    private string ReadIdentity(int row)
    {
        var reference = Read(row, 0);
        if (reference == 0 || reference > 0xFF_FFFF)
        {
            throw InvalidRef();
        }

        return _strings.DecodeName((int)reference);
    }

    private uint Read(int row, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            _rows.Slice(row * RowSize + offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidRef()
        => new("Invalid TRRM ModuleRef table.");
}
