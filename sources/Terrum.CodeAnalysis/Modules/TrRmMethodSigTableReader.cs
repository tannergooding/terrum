// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates structural MethodSig references and complete, disjoint Param ranges.</summary>
/// <remarks>The borrowed MethodSig and TypeSpec sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmMethodSigTableReader
{
    private const int RowSize = 24;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int[] _paramOwners;

    public int Count { get; }
    public int ParamCount => _paramOwners.Length - 1;

    public TrRmMethodSigTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeSpecTableReader types, int paramCount, int genericParamCount)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || paramCount is < 0 or > 0xFF_FFFF
            || genericParamCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidSignature();
        }

        Count = recordCount;
        _rows = section;
        _paramOwners = new int[paramCount + 1];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = section.Slice((row - 1) * RowSize, RowSize);
            var result = Read(data, 0);
            var receiver = Read(data, 4);
            var first = Read(data, 8);
            var count = Read(data, 12);
            var genericFirst = Read(data, 16);
            var genericCount = Read(data, 20);
            if (result == 0 || result > types.Count || receiver > types.Count
                || !ValidRange(first, count, paramCount)
                || !ValidRange(genericFirst, genericCount, genericParamCount))
            {
                throw InvalidSignature();
            }

            for (var param = first; param < first + count; param++)
            {
                if (_paramOwners[param] != 0)
                {
                    throw InvalidSignature();
                }

                _paramOwners[param] = row;
            }
        }

        for (var param = 1; param <= paramCount; param++)
        {
            if (_paramOwners[param] == 0)
            {
                throw InvalidSignature();
            }
        }
    }

    public int GetParamOwner(int row)
    {
        if (row <= 0 || row >= _paramOwners.Length)
        {
            throw InvalidSignature();
        }

        return _paramOwners[row];
    }

    public int GetReceiverTypeSpec(int row)
    {
        if (row <= 0 || row > Count)
        {
            throw InvalidSignature();
        }

        return (int)Read(_rows.Slice((row - 1) * RowSize, RowSize), 4);
    }

    public int GetGenericCount(int row)
    {
        if (row <= 0 || row > Count)
        {
            throw InvalidSignature();
        }

        return (int)Read(_rows.Slice((row - 1) * RowSize, RowSize), 20);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static bool ValidRange(uint first, uint count, int tableCount)
        => count == 0 ? first == 0
            : first != 0 && first <= tableCount && count <= (uint)tableCount - first + 1;

    private static InvalidDataException InvalidSignature()
        => new("Invalid TRRM MethodSig table.");
}
