// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates Param types, modes, ordinals and MethodSig ownership.</summary>
/// <remarks>The borrowed Param, MethodSig and TypeSpec sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmParamTableReader
{
    private const int RowSize = 16;
    private readonly ReadOnlySpan<byte> _rows;

    public TrRmParamTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader types)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || recordCount != signatures.ParamCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidParam();
        }

        _rows = section;
        var previousOwner = 0;
        var ordinal = 0;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = section.Slice((row - 1) * RowSize, RowSize);
            var owner = Read(data, 0);
            var expectedOwner = signatures.GetParamOwner(row);
            ordinal = expectedOwner == previousOwner ? ordinal + 1 : 0;
            var type = Read(data, 8);
            if (owner != expectedOwner || Read(data, 4) != ordinal
                || type == 0 || type > types.Count
                || data[12] > 4 || data[13] != 0
                || data[14] != 0 || data[15] != 0)
            {
                throw InvalidParam();
            }

            previousOwner = expectedOwner;
        }
    }

    public byte GetMode(int row)
    {
        if (row <= 0 || (long)row * RowSize > _rows.Length)
        {
            throw InvalidParam();
        }

        return _rows[(row - 1) * RowSize + 12];
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidParam()
        => new("Invalid TRRM Param table.");
}
