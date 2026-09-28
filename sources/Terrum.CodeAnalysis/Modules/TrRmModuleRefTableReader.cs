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

    public TrRmModuleRefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleTableReader modules)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || recordCount != modules.ModuleRefCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidRef();
        }

        _rows = section;
        _strings = strings;
        _modules = modules;
        _recordCount = recordCount;
        for (var module = 1; module <= modules.Count; module++)
        {
            var (start, end) = modules.GetModuleRefRange(module);
            for (var row = start; row < end; row++)
            {
                var producer = ReadIdentity(row - 1);
                var target = Read(row - 1, 4);
                var policy = Read(row - 1, 8);
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
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidRef();
        }

        return ReadIdentity(row - 1);
    }

    public int GetOwnerModule(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidRef();
        }

        for (var module = 1; module <= _modules.Count; module++)
        {
            var (start, end) = _modules.GetModuleRefRange(module);
            if (row >= start && row < end)
            {
                return module;
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
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice(row * RowSize + offset, sizeof(uint)));

    private static InvalidDataException InvalidRef()
        => new("Invalid TRRM ModuleRef table.");
}
