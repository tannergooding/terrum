// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates callable references and module, type, signature and body ownership.</summary>
/// <remarks>The borrowed MethodDef and referenced table sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmMethodDefTableReader
{
    private const int RowSize = 28;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly TrRmModuleTableReader _modules;
    private readonly int _recordCount;
    private readonly int _contractCount;

    public int ContractCount => _contractCount;

    public TrRmMethodDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmMethodSigTableReader signatures,
        int contractCount, int bodyCount)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || recordCount != modules.MethodDefCount
            || contractCount is < 0 or > 0xFF_FFFF || bodyCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidMethod();
        }

        _rows = section;
        _strings = strings;
        _modules = modules;
        _recordCount = recordCount;
        _contractCount = contractCount;
        var ownedBodies = new bool[bodyCount + 1];
        var usedGenericSignatures = new bool[signatures.Count + 1];
        var module = 1;
        for (var row = 1; row <= recordCount; row++)
        {
            while (module <= modules.Count && row >= modules.GetMethodDefRange(module).End)
            {
                module++;
            }

            if (module > modules.Count || row < modules.GetMethodDefRange(module).Start)
            {
                throw InvalidMethod();
            }

            var data = section.Slice((row - 1) * RowSize, RowSize);
            var owner = Read(data, 0);
            var name = Read(data, 4);
            var signature = Read(data, 8);
            var flags = Read(data, 12);
            var first = Read(data, 16);
            var count = Read(data, 20);
            var body = Read(data, 24);
            if (owner > types.Count || (owner != 0 && types.GetOwnerModule((int)owner) != module)
                || name is 0 or > 0xFF_FFFF
                || signature == 0 || signature > signatures.Count
                || flags > 2 || !ValidRange(first, count, contractCount)
                || body > bodyCount || (body != 0 && ownedBodies[body]))
            {
                throw InvalidMethod();
            }

            if (owner == 0 && signatures.GetReceiverTypeSpec((int)signature) != 0)
            {
                throw InvalidMethod();
            }

            if (signatures.GetGenericCount((int)signature) != 0)
            {
                if (usedGenericSignatures[signature])
                {
                    throw InvalidMethod();
                }

                usedGenericSignatures[signature] = true;
            }

            if (body != 0)
            {
                ownedBodies[body] = true;
            }

            _ = _strings.DecodeName((int)name);
        }

        for (var body = 1; body <= bodyCount; body++)
        {
            if (!ownedBodies[body])
            {
                throw InvalidMethod();
            }
        }
    }

    public string GetName(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidMethod();
        }

        var name = Read(_rows.Slice((row - 1) * RowSize, RowSize), 4);
        if (name is 0 or > 0xFF_FFFF)
        {
            throw InvalidMethod();
        }

        return _strings.DecodeName((int)name);
    }

    public int GetOwnerType(int row)
        => (int)Read(GetRow(row), 0);

    public int GetSignature(int row)
        => (int)Read(GetRow(row), 8);

    public uint GetFlags(int row)
        => Read(GetRow(row), 12);

    public int GetBodyRow(int row)
        => (int)Read(GetRow(row), 24);

    public (int Start, int Count) GetContractRange(int row)
    {
        var data = GetRow(row);
        return ((int)Read(data, 16), (int)Read(data, 20));
    }

    public int GetOwnerModule(int row)
    {
        _ = GetRow(row);
        var low = 1;
        var high = _modules.Count;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var (start, end) = _modules.GetMethodDefRange(middle);
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

        throw InvalidMethod();
    }

    public int Count => _recordCount;

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidMethod();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static bool ValidRange(uint first, uint count, int tableCount)
        => count == 0 ? first == 0
            : first != 0 && first <= tableCount && count <= (uint)tableCount - first + 1;

    private static InvalidDataException InvalidMethod()
        => new("Invalid TRRM MethodDef table.");
}
