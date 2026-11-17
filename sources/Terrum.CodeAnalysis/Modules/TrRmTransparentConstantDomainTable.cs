// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmTransparentConstantDomainRow(
    int OwnerTypeDef, uint CarrierTypeSpecRef, int CarrierFieldDef);

internal readonly ref struct TrRmTransparentConstantDomainTableReader
{
    private const int RowSize = 12;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal TrRmTransparentConstantDomainTableReader(
        ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader typeDefs, TrRmFieldDefTableReader fields,
        TrRmTypeSpecTableReader typeSpecs)
    {
        if (!typeDefs.Schema.UsesGenericConstantMethods()
            || recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidTable();
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var owner = Read(data, 0);
            var carrierTypeSpecRef = Read(data, 4);
            var carrierFieldDef = Read(data, 8);
            if (owner == 0 || owner > (uint)typeDefs.Count
                || typeDefs.GetKind(checked((int)owner)) != 1
                || carrierTypeSpecRef == 0
                || carrierFieldDef == 0
                || carrierFieldDef > (uint)fields.Count)
            {
                throw InvalidTable();
            }

            var carrierType = typeSpecs.GetTypeSpecRef(carrierTypeSpecRef);
            if (carrierType.Form != 0x01 || carrierType.Qualifiers != 0
                || carrierType.ArgumentCode != 0
                || !TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                    carrierType.TypeCode, out var primitiveType)
                || !(PrimitiveTypeFacts.IsIntegerOrWide(primitiveType)
                    || primitiveType == PrimitiveType.Bool))
            {
                throw InvalidTable();
            }
        }
    }

    internal int Count => _recordCount;

    internal int GetOwnerTypeDef(int row)
        => checked((int)Read(GetRow(row), 0));

    internal uint GetCarrierTypeSpecRef(int row)
        => Read(GetRow(row), 4);

    internal int GetCarrierFieldDef(int row)
        => checked((int)Read(GetRow(row), 8));

    internal bool TryGetForOwner(int ownerTypeDef,
        out uint carrierTypeSpecRef, out int carrierFieldDef)
    {
        for (var row = 1; row <= _recordCount; row++)
        {
            var data = GetRow(row);
            if (Read(data, 0) != (uint)ownerTypeDef)
            {
                continue;
            }

            carrierTypeSpecRef = Read(data, 4);
            carrierFieldDef = checked((int)Read(data, 8));
            return true;
        }

        carrierTypeSpecRef = 0;
        carrierFieldDef = 0;
        return false;
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidTable();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset,
            sizeof(uint)));

    private static InvalidDataException InvalidTable()
        => new("Invalid TRRM TransparentConstantDomain table.");
}

internal static class TrRmTransparentConstantDomainTableWriter
{
    internal static byte[] Encode(
        IReadOnlyList<TrRmTransparentConstantDomainRow> rows,
        TrRmTypeDefTableReader typeDefs, TrRmFieldDefTableReader fields,
        TrRmTypeSpecTableReader typeSpecs)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!typeDefs.Schema.UsesGenericConstantMethods()
            || rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[checked(rows.Count * 12)];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 12;
            Write(result, offset, row.OwnerTypeDef);
            Write(result, offset + 4, row.CarrierTypeSpecRef);
            Write(result, offset + 8, row.CarrierFieldDef);
        }

        _ = new TrRmTransparentConstantDomainTableReader(result,
            rows.Count, typeDefs, fields, typeSpecs);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
