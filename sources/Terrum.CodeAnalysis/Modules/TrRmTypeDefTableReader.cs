// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates nominal declaration framing, flags and owned table ranges.</summary>
/// <remarks>The borrowed TypeDef, Module and String sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmTypeDefTableReader
{
    private const int RowSize = 44;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly TrRmModuleTableReader _modules;
    private readonly int _recordCount;
    private readonly int _fieldCount;
    private readonly int _layoutCount;
    private readonly int _contractCount;
    private readonly int[] _fieldOwners;

    public int Count => _recordCount;
    public int FieldCount => _fieldCount;
    public int LayoutCount => _layoutCount;
    public int ContractCount => _contractCount;

    /// <summary>Validates TypeDef rows, names, flags and owned table ranges.</summary>
    /// <param name="section">The borrowed 44-byte-row TypeDef section.</param>
    /// <param name="recordCount">The number of TypeDef rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="modules">The validated Module table.</param>
    /// <param name="fieldCount">The number of FieldDef rows.</param>
    /// <param name="genericParamCount">The number of GenericParam rows.</param>
    /// <param name="layoutCount">The number of Layout rows.</param>
    /// <param name="contractCount">The number of Contract rows.</param>
    public TrRmTypeDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleTableReader modules,
        int fieldCount, int genericParamCount, int layoutCount, int contractCount)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != modules.TypeDefCount
            || (long)recordCount * RowSize != section.Length
            || !ValidCount(fieldCount) || !ValidCount(genericParamCount)
            || !ValidCount(layoutCount) || !ValidCount(contractCount)
            || (recordCount == 0 && (fieldCount != 0 || layoutCount != 0)))
        {
            throw InvalidType();
        }

        _rows = section;
        _strings = strings;
        _modules = modules;
        _recordCount = recordCount;
        _fieldCount = fieldCount;
        _layoutCount = layoutCount;
        _contractCount = contractCount;
        _fieldOwners = new int[fieldCount + 1];
        for (var row = 1; row <= recordCount; row++)
        {
            _ = ReadNamespace(row - 1);
            _ = ReadName(row - 1);
            var flags = Read(row - 1, 8);
            if ((flags & ~0x0Fu) != 0 || (flags & 3) == 3
                || ((flags >> 2) & 3) == 0
                || !ValidRange(row, 12, fieldCount)
                || !ValidRange(row, 20, genericParamCount)
                || !ValidRange(row, 28, layoutCount)
                || !ValidRange(row, 36, contractCount))
            {
                throw InvalidType();
            }

            var first = Read(row - 1, 12);
            var count = Read(row - 1, 16);
            for (var field = first; field < first + count; field++)
            {
                if (_fieldOwners[field] != 0)
                {
                    throw InvalidType();
                }

                _fieldOwners[field] = row;
            }
        }

        for (var field = 1; field <= fieldCount; field++)
        {
            if (_fieldOwners[field] == 0)
            {
                throw InvalidType();
            }
        }
    }

    public string GetName(int row)
    {
        CheckRow(row);
        return ReadName(row - 1);
    }

    public string GetNamespace(int row)
    {
        CheckRow(row);
        return ReadNamespace(row - 1);
    }

    public (int Start, int End) GetFieldRange(int row)
    {
        CheckRow(row);
        var first = (int)Read(row - 1, 12);
        var count = (int)Read(row - 1, 16);
        if (!ValidRange(row, 12, _fieldCount))
        {
            throw InvalidType();
        }

        return (first, first == 0 ? 0 : first + count);
    }

    public int GetOwnerModule(int row)
    {
        CheckRow(row);
        var low = 1;
        var high = _modules.Count;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var (start, end) = _modules.GetTypeDefRange(middle);
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

        throw InvalidType();
    }

    public int GetFieldOwner(int row)
    {
        if (row <= 0 || row > _fieldCount)
        {
            throw InvalidType();
        }

        return _fieldOwners[row];
    }

    public uint GetFlags(int row)
    {
        CheckRow(row);
        return Read(row - 1, 8);
    }

    public (int Start, int Count) GetGenericRange(int row)
    {
        CheckRow(row);
        return ((int)Read(row - 1, 20), (int)Read(row - 1, 24));
    }

    /// <summary>Gets the one-based Layout range owned by a validated TypeDef row.</summary>
    public (int Start, int Count) GetLayoutRange(int row)
    {
        CheckRow(row);
        return ((int)Read(row - 1, 28), (int)Read(row - 1, 32));
    }

    public (int Start, int Count) GetContractRange(int row)
    {
        CheckRow(row);
        return ((int)Read(row - 1, 36), (int)Read(row - 1, 40));
    }

    private string ReadNamespace(int row)
    {
        var reference = Read(row, 0);
        if (reference is 0 or > 0xFF_FFFF)
        {
            throw InvalidType();
        }

        return _strings.DecodeNamespace((int)reference);
    }

    private string ReadName(int row)
    {
        var reference = Read(row, 4);
        if (reference is 0 or > 0xFF_FFFF)
        {
            throw InvalidType();
        }

        return _strings.DecodeName((int)reference);
    }

    private bool ValidRange(int row, int offset, int tableCount)
    {
        var first = Read(row - 1, offset);
        var count = Read(row - 1, offset + 4);
        return count == 0 ? first == 0
            : first != 0 && first <= tableCount && count <= (uint)tableCount - first + 1;
    }

    private void CheckRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidType();
        }
    }

    private uint Read(int row, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice(row * RowSize + offset, sizeof(uint)));

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static InvalidDataException InvalidType()
        => new("Invalid TRRM TypeDef table.");
}
