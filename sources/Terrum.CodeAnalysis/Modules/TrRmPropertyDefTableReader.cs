// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates PropertyDef references and getter ownership and result type.</summary>
/// <remarks>The borrowed PropertyDef and referenced table sections must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmPropertyDefTableReader
{
    private const int RowSize = 20;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly int _recordCount;

    public TrRmPropertyDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidProperty();
        }

        _rows = section;
        _strings = strings;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = section.Slice((row - 1) * RowSize, RowSize);
            var owner = Read(data, 0);
            var name = Read(data, 4);
            var type = Read(data, 8);
            var getter = Read(data, 12);
            var flags = Read(data, 16);
            if (owner == 0 || owner > types.Count
                || name is 0 or > 0xFF_FFFF
                || type == 0 || type > typeSpecs.Count || flags > 2
                || getter == 0 || getter > methods.Count
                || methods.GetOwnerType((int)getter) != owner
                || signatures.GetResultTypeSpec(methods.GetSignature((int)getter)) != type)
            {
                throw InvalidProperty();
            }

            _ = _strings.DecodeName((int)name);
        }
    }

    public string GetName(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidProperty();
        }

        var name = Read(_rows.Slice((row - 1) * RowSize, RowSize), 4);
        if (name is 0 or > 0xFF_FFFF)
        {
            throw InvalidProperty();
        }

        return _strings.DecodeName((int)name);
    }

    public int Count => _recordCount;

    public int GetOwnerType(int row)
        => (int)Read(GetRow(row), 0);

    public uint GetFlags(int row)
        => Read(GetRow(row), 16);

    public int GetGetterMethod(int row)
        => (int)Read(GetRow(row), 12);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidProperty();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidProperty()
        => new("Invalid TRRM PropertyDef table.");
}
