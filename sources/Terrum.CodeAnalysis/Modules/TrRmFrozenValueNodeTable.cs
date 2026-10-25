// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A FrozenValueNode row with the fixed flags and reserved fields omitted.</summary>
/// <param name="TypeSpecRef">The complete type reference for this frozen value.</param>
/// <param name="Kind">The assigned FrozenValueNode kind.</param>
/// <param name="ScalarBlobRef">The one-based Blob row for scalar kind 1, or zero for unit.</param>
/// <param name="ChildFirst">The first one-based FrozenValueChild row, or zero when absent.</param>
/// <param name="ChildCount">The number of FrozenValueChild rows.</param>
/// <param name="ActiveUnionCaseDef">The active union case DefId, or zero when absent.</param>
public readonly record struct TrRmFrozenValueNodeRow(
    uint TypeSpecRef,
    byte Kind,
    uint ScalarBlobRef,
    uint ChildFirst,
    uint ChildCount,
    uint ActiveUnionCaseDef);

/// <summary>Validates FrozenValueNode framing and the selected scalar-value codec.</summary>
/// <remarks>The borrowed table and Blob bytes must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmFrozenValueNodeTableReader
{
    private const int RowSize = 24;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmTypeSpecTableReader _typeSpecs;
    private readonly int _blobCount;
    /// <summary>Gets the number of validated FrozenValueNode rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates fixed rows and their referenced TypeSpec, Blob, child and union-case ranges.</summary>
    /// <param name="section">The borrowed 24-byte FrozenValueNode rows.</param>
    /// <param name="recordCount">The number of FrozenValueNode rows.</param>
    /// <param name="typeSpecs">The validated complete-type reference table.</param>
    /// <param name="blobCount">The number of Blob rows.</param>
    /// <param name="childCount">The number of FrozenValueChild rows.</param>
    /// <param name="unionCaseDefCount">The number of UnionCaseDef rows.</param>
    public TrRmFrozenValueNodeTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeSpecTableReader typeSpecs, int blobCount, int childCount,
        int unionCaseDefCount)
    {
        if (!ValidCount(recordCount) || !ValidCount(blobCount) || !ValidCount(childCount)
            || !ValidCount(unionCaseDefCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidFrozenValueNode();
        }

        _rows = section;
        _recordCount = recordCount;
        _typeSpecs = typeSpecs;
        _blobCount = blobCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            _ = _typeSpecs.GetTypeSpecRef(Read(data, 0));
            if (data[4] is < 1 or > 6
                || data[5] != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0)
            {
                throw InvalidFrozenValueNode();
            }

            var scalarBlobRef = Read(data, 8);
            if ((scalarBlobRef & 0xFF00_0000) != 0
                || scalarBlobRef > (uint)blobCount)
            {
                throw InvalidFrozenValueNode();
            }

            var childFirst = Read(data, 12);
            var childRangeCount = Read(data, 16);
            if (childFirst > 0xFF_FFFF || childRangeCount > 0xFF_FFFF
                || (childRangeCount != 0
                    && (childFirst == 0
                        || (ulong)childFirst + childRangeCount - 1 > (uint)childCount)))
            {
                throw InvalidFrozenValueNode();
            }

            var activeUnionCaseDef = Read(data, 20);
            if (activeUnionCaseDef != 0
                && ((activeUnionCaseDef & 0xFF00_0000) != 0x2100_0000
                    || (activeUnionCaseDef & 0x00FF_FFFF) == 0
                    || (activeUnionCaseDef & 0x00FF_FFFF) > (uint)unionCaseDefCount))
            {
                throw InvalidFrozenValueNode();
            }
        }
    }

    /// <summary>Gets the assigned kind tag for a one-based FrozenValueNode row.</summary>
    public byte GetKind(int row)
        => GetRow(row)[4];

    /// <summary>Gets the complete TypeSpecRef for a one-based FrozenValueNode row.</summary>
    public uint GetTypeSpecRef(int row)
        => Read(GetRow(row), 0);

    /// <summary>Gets the one-based scalar Blob row, or zero when the row has no scalar Blob.</summary>
    public uint GetScalarBlobRef(int row)
        => Read(GetRow(row), 8);

    /// <summary>Validates and returns the exact payload bytes for a selected scalar kind-1 value.</summary>
    /// <param name="row">The one-based FrozenValueNode row.</param>
    /// <param name="blobs">The validated Blob table selected with this node table.</param>
    public ReadOnlySpan<byte> GetScalarPayload(int row, TrRmBlobTableReader blobs)
    {
        if (blobs.Count != _blobCount)
        {
            throw InvalidFrozenValueNode();
        }

        var data = GetRow(row);
        if (data[4] != 1
            || data[5] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0
            || Read(data, 12) != 0
            || Read(data, 16) != 0
            || Read(data, 20) != 0)
        {
            throw InvalidFrozenValueNode();
        }

        var type = _typeSpecs.GetTypeSpecRef(Read(data, 0));
        if (type.Form != 0x01 || type.Qualifiers != 0 || !IsRegisteredScalar(type.TypeCode))
        {
            throw new NotSupportedException("Frozen value is outside the registered scalar type domain.");
        }

        var scalarBlobRef = Read(data, 8);
        var width = ScalarWidth(type.TypeCode);
        if (width == 0)
        {
            if (scalarBlobRef != 0)
            {
                throw InvalidFrozenValueNode();
            }
            return [];
        }
        if (scalarBlobRef == 0 || scalarBlobRef > (uint)_blobCount
            || scalarBlobRef > 0xFF_FFFF)
        {
            throw InvalidFrozenValueNode();
        }

        var payload = blobs.GetRow(checked((int)scalarBlobRef));
        if (payload.Length != width
            || (type.TypeCode == 1 && payload[0] > 1)
            || (type.TypeCode == 5 && !IsUnicodeScalar(
                BinaryPrimitives.ReadUInt32LittleEndian(payload))))
        {
            throw InvalidFrozenValueNode();
        }

        return payload;
    }

    /// <summary>Validates the selected row as a complete scalar value without copying its payload.</summary>
    public void ValidateScalarValue(int row, TrRmBlobTableReader blobs)
        => _ = GetScalarPayload(row, blobs);

    internal static bool IsRegisteredScalarTypeCode(byte typeCode)
        => IsRegisteredScalar(typeCode);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidFrozenValueNode();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool IsRegisteredScalar(byte typeCode)
        => typeCode <= 0x0F || typeCode is >= 0x12 and <= 0x19;

    private static int ScalarWidth(byte typeCode) => typeCode switch
    {
        0x00 => 0,
        0x01 or 0x02 or 0x06 or 0x0B => 1,
        0x03 or 0x07 or 0x0C or 0x12 or 0x13 => 2,
        0x04 or 0x05 or 0x08 or 0x0D or 0x14 or 0x17 => 4,
        0x09 or 0x0E or 0x15 or 0x18 => 8,
        0x0A or 0x0F or 0x16 or 0x19 => 16,
        _ => throw new NotSupportedException("Frozen value is outside the registered scalar type domain."),
    };

    private static bool IsUnicodeScalar(uint value)
        => value <= 0x10_FFFF && value is not (>= 0xD800 and <= 0xDFFF);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static InvalidDataException InvalidFrozenValueNode()
        => new("Invalid TRRM FrozenValueNode table or scalar value.");
}

/// <summary>Encodes fixed FrozenValueNode rows and validates scalar kind-1 values.</summary>
public static class TrRmFrozenValueNodeTableWriter
{
    private const int RowSize = 24;

    /// <summary>Encodes one-based FrozenValueNode rows and validates scalar payloads.</summary>
    /// <param name="rows">The rows in one-based table order.</param>
    /// <param name="typeSpecs">The validated complete-type reference table.</param>
    /// <param name="blobs">The validated Blob table selected with these rows.</param>
    /// <param name="childCount">The number of FrozenValueChild rows.</param>
    /// <param name="unionCaseDefCount">The number of UnionCaseDef rows.</param>
    /// <returns>The encoded 24-byte fixed rows.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmFrozenValueNodeRow> rows,
        TrRmTypeSpecTableReader typeSpecs, TrRmBlobTableReader blobs, int childCount,
        int unionCaseDefCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = EncodeRows(rows, typeSpecs, blobs.Count, childCount,
            unionCaseDefCount);
        var reader = new TrRmFrozenValueNodeTableReader(result, rows.Count,
            typeSpecs, blobs.Count, childCount, unionCaseDefCount);
        for (var row = 1; row <= rows.Count; row++)
        {
            if (reader.GetKind(row) == 1)
            {
                reader.ValidateScalarValue(row, blobs);
            }
        }
        return result;
    }

    private static byte[] EncodeRows(IReadOnlyList<TrRmFrozenValueNodeRow> rows,
        TrRmTypeSpecTableReader typeSpecs, int blobCount, int childCount,
        int unionCaseDefCount)
    {
        var result = new byte[checked(rows.Count * RowSize)];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var data = result.AsSpan(index * RowSize, RowSize);
            BinaryPrimitives.WriteUInt32LittleEndian(data, row.TypeSpecRef);
            data[4] = row.Kind;
            BinaryPrimitives.WriteUInt32LittleEndian(data[8..], row.ScalarBlobRef);
            BinaryPrimitives.WriteUInt32LittleEndian(data[12..], row.ChildFirst);
            BinaryPrimitives.WriteUInt32LittleEndian(data[16..], row.ChildCount);
            BinaryPrimitives.WriteUInt32LittleEndian(data[20..], row.ActiveUnionCaseDef);
        }

        _ = new TrRmFrozenValueNodeTableReader(result, rows.Count, typeSpecs,
            blobCount, childCount, unionCaseDefCount);
        return result;
    }
}
