// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A named nominal-union case and its payload-field range.</summary>
/// <param name="OwnerTypeDefRow">The one-based owning TypeDef row.</param>
/// <param name="Ordinal">The case ordinal retained from the declaration.</param>
/// <param name="NameStringRow">The one-based nonempty case name.</param>
/// <param name="PayloadFirst">The first owned UnionCaseField row, or zero when empty.</param>
/// <param name="PayloadCount">The number of owned UnionCaseField rows.</param>
public readonly record struct TrRmUnionCaseDefRow(
    int OwnerTypeDefRow, uint Ordinal, int NameStringRow,
    int PayloadFirst, int PayloadCount);

/// <summary>A named, ordered and typed payload field of one union case.</summary>
/// <param name="OwnerUnionCaseDefRow">The one-based owning UnionCaseDef row.</param>
/// <param name="Ordinal">The zero-based position in the case payload.</param>
/// <param name="NameStringRow">The one-based nonempty field name.</param>
/// <param name="TypeSpecRef">The complete payload-field type.</param>
public readonly record struct TrRmUnionCaseFieldRow(
    int OwnerUnionCaseDefRow, uint Ordinal, int NameStringRow, uint TypeSpecRef);

/// <summary>Validates nominal-union case rows and exclusive payload-field ranges.</summary>
public readonly ref struct TrRmUnionCaseDefTableReader
{
    private const int RowSize = 20;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated UnionCaseDef rows.</summary>
    public int Count => _recordCount;

    /// <summary>Gets the number of payload fields partitioned by these cases.</summary>
    public int PayloadFieldCount { get; }

    /// <summary>Validates case owners, names and exclusive payload-field ranges.</summary>
    /// <param name="section">The borrowed 20-byte UnionCaseDef rows.</param>
    /// <param name="recordCount">The number of UnionCaseDef rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="payloadFieldCount">The number of UnionCaseField rows.</param>
    public TrRmUnionCaseDefTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmTypeDefTableReader types, int payloadFieldCount)
    {
        if (!ValidCount(recordCount) || !ValidCount(payloadFieldCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidUnionCaseDef();
        }

        _rows = section;
        _recordCount = recordCount;
        PayloadFieldCount = payloadFieldCount;
        var fieldOwned = new bool[payloadFieldCount + 1];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var owner = Read(data, 0);
            var name = Read(data, 8);
            var first = Read(data, 12);
            var count = Read(data, 16);
            if (owner is 0 or > 0xFF_FFFF || owner > (uint)types.Count
                || name is 0 or > 0xFF_FFFF
                || !ValidRange(first, count, payloadFieldCount))
            {
                throw InvalidUnionCaseDef();
            }

            if (strings.DecodeName((int)name).Length == 0)
            {
                throw InvalidUnionCaseDef();
            }

            for (var field = first; field < first + count; field++)
            {
                if (fieldOwned[field])
                {
                    throw InvalidUnionCaseDef();
                }

                fieldOwned[field] = true;
            }
        }

        for (var field = 1; field <= payloadFieldCount; field++)
        {
            if (!fieldOwned[field])
            {
                throw InvalidUnionCaseDef();
            }
        }
    }

    /// <summary>Gets the one-based owner TypeDef row.</summary>
    public int GetOwnerTypeDef(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the retained case ordinal.</summary>
    public uint GetOrdinal(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the one-based case name String row.</summary>
    public int GetNameStringRow(int row)
        => checked((int)Read(GetRow(row), 8));

    /// <summary>Gets the one-based UnionCaseField payload range.</summary>
    public (int First, int Count) GetPayloadRange(int row)
    {
        var data = GetRow(row);
        return (checked((int)Read(data, 12)), checked((int)Read(data, 16)));
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidUnionCaseDef();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool ValidRange(uint first, uint count, int total)
        => count == 0 ? first == 0
            : first != 0 && first <= (uint)total
                && count <= (uint)total - first + 1;

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidUnionCaseDef()
        => new("Invalid TRRM UnionCaseDef table.");
}

/// <summary>Validates typed payload fields against their exclusive UnionCaseDef ranges.</summary>
public readonly ref struct TrRmUnionCaseFieldTableReader
{
    private const int RowSize = 16;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated UnionCaseField rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates payload owners, ordinals, names and TypeSpecRefs.</summary>
    /// <param name="section">The borrowed 16-byte UnionCaseField rows.</param>
    /// <param name="recordCount">The number of UnionCaseField rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="cases">The validated UnionCaseDef table.</param>
    public TrRmUnionCaseFieldTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmTypeSpecTableReader types, TrRmUnionCaseDefTableReader cases)
    {
        if (!ValidCount(recordCount) || recordCount != cases.PayloadFieldCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidUnionCaseField();
        }

        _rows = section;
        _recordCount = recordCount;
        for (var caseRow = 1; caseRow <= cases.Count; caseRow++)
        {
            var (first, count) = cases.GetPayloadRange(caseRow);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var row = first + ordinal;
                var data = GetRow(row);
                var owner = Read(data, 0);
                var fieldOrdinal = Read(data, 4);
                var name = Read(data, 8);
                var typeSpecRef = Read(data, 12);
                if (owner != (uint)caseRow || fieldOrdinal != (uint)ordinal
                    || name is 0 or > 0xFF_FFFF || typeSpecRef == 0)
                {
                    throw InvalidUnionCaseField();
                }

                if (strings.DecodeName((int)name).Length == 0)
                {
                    throw InvalidUnionCaseField();
                }

                _ = types.GetTypeSpecRef(typeSpecRef);
            }
        }
    }

    /// <summary>Gets the one-based owning UnionCaseDef row.</summary>
    public int GetOwnerUnionCaseDef(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the zero-based payload field ordinal.</summary>
    public uint GetOrdinal(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the one-based payload field name String row.</summary>
    public int GetNameStringRow(int row)
        => checked((int)Read(GetRow(row), 8));

    /// <summary>Gets the complete payload-field TypeSpecRef.</summary>
    public uint GetTypeSpecRef(int row)
        => Read(GetRow(row), 12);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidUnionCaseField();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidUnionCaseField()
        => new("Invalid TRRM UnionCaseField table.");
}

/// <summary>Encodes nominal-union cases and validates their payload ranges.</summary>
public static class TrRmUnionCaseDefTableWriter
{
    private const int RowSize = 20;

    /// <summary>Encodes UnionCaseDef rows in one-based table order.</summary>
    /// <param name="rows">The union cases and their ordered payload ranges.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="payloadFieldCount">The number of UnionCaseField rows.</param>
    /// <returns>The encoded UnionCaseDef section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmUnionCaseDefRow> rows,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types,
        int payloadFieldCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * RowSize;
            Write(result, offset, unchecked((uint)row.OwnerTypeDefRow));
            Write(result, offset + 4, row.Ordinal);
            Write(result, offset + 8, unchecked((uint)row.NameStringRow));
            Write(result, offset + 12, unchecked((uint)row.PayloadFirst));
            Write(result, offset + 16, unchecked((uint)row.PayloadCount));
        }

        _ = new TrRmUnionCaseDefTableReader(
            result, rows.Count, strings, types, payloadFieldCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}

/// <summary>Encodes ordered union payload fields with complete TypeSpecRefs.</summary>
public static class TrRmUnionCaseFieldTableWriter
{
    private const int RowSize = 16;

    /// <summary>Encodes UnionCaseField rows in one-based table order.</summary>
    /// <param name="rows">The ordered payload fields.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="cases">The validated UnionCaseDef table.</param>
    /// <returns>The encoded UnionCaseField section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmUnionCaseFieldRow> rows,
        TrRmStringTableReader strings, TrRmTypeSpecTableReader types,
        TrRmUnionCaseDefTableReader cases)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * RowSize;
            Write(result, offset, unchecked((uint)row.OwnerUnionCaseDefRow));
            Write(result, offset + 4, row.Ordinal);
            Write(result, offset + 8, unchecked((uint)row.NameStringRow));
            Write(result, offset + 12, row.TypeSpecRef);
        }

        _ = new TrRmUnionCaseFieldTableReader(
            result, rows.Count, strings, types, cases);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
