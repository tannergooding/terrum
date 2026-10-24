// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>An ordered FieldDef reference in one complete Layout variant.</summary>
/// <param name="OwnerLayoutRow">The one-based owning Layout row.</param>
/// <param name="Ordinal">The zero-based position within that Layout's field list.</param>
/// <param name="FieldDefRow">The one-based instance FieldDef row.</param>
public readonly record struct TrRmLayoutFieldRow(
    int OwnerLayoutRow, int Ordinal, int FieldDefRow);

/// <summary>Validates complete ordered field lists for Layout variants.</summary>
/// <remarks>The borrowed LayoutField, Layout, TypeDef and FieldDef sections must remain unchanged.</remarks>
public readonly ref struct TrRmLayoutFieldTableReader
{
    private const int RowSize = 12;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated LayoutField rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates complete ordered field lists for all Layout rows.</summary>
    /// <param name="section">The borrowed LayoutField section bytes.</param>
    /// <param name="recordCount">The number of 12-byte LayoutField rows.</param>
    /// <param name="layouts">The validated Layout table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="fields">The validated FieldDef table.</param>
    public TrRmLayoutFieldTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmLayoutTableReader layouts, TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != layouts.LayoutFieldCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidLayoutField();
        }

        _rows = section;
        _recordCount = recordCount;
        var owners = new int[recordCount + 1];
        for (var layout = 1; layout <= layouts.Count; layout++)
        {
            var (first, count) = layouts.GetLayoutFieldRange(layout);
            var typeDef = layouts.GetOwnerTypeDef(layout);
            var (firstField, endField) = types.GetFieldRange(typeDef);
            var seenFields = new bool[fields.Count + 1];
            var expectedCount = 0;
            for (var field = firstField; field < endField; field++)
            {
                if ((fields.GetFlags(field) & 8) == 0)
                {
                    expectedCount++;
                }
            }

            if (count != expectedCount)
            {
                throw InvalidLayoutField();
            }

            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var row = first + ordinal;
                if (row <= 0 || row > recordCount || owners[row] != 0)
                {
                    throw InvalidLayoutField();
                }

                var offset = (row - 1) * RowSize;
                var ownerLayout = Read(offset);
                var rowOrdinal = Read(offset + 4);
                var fieldDef = Read(offset + 8);
                if (ownerLayout != layout || rowOrdinal != ordinal
                    || fieldDef == 0 || fieldDef > fields.Count
                    || fields.GetOwnerTypeDef((int)fieldDef) != typeDef
                    || (fields.GetFlags((int)fieldDef) & 8) != 0
                    || seenFields[checked((int)fieldDef)])
                {
                    throw InvalidLayoutField();
                }

                seenFields[checked((int)fieldDef)] = true;
                owners[row] = layout;
            }

            for (var field = firstField; field < endField; field++)
            {
                if ((fields.GetFlags(field) & 8) == 0 && !seenFields[field])
                {
                    throw InvalidLayoutField();
                }
            }
        }

        for (var row = 1; row <= recordCount; row++)
        {
            if (owners[row] == 0)
            {
                throw InvalidLayoutField();
            }
        }
    }

    /// <summary>Gets the owning Layout row for a validated LayoutField row.</summary>
    public int GetOwnerLayout(int row)
    {
        CheckRow(row);
        return checked((int)Read((row - 1) * RowSize));
    }

    /// <summary>Gets the zero-based field ordinal in its Layout row.</summary>
    public int GetOrdinal(int row)
    {
        CheckRow(row);
        return checked((int)Read(((row - 1) * RowSize) + 4));
    }

    /// <summary>Gets the referenced FieldDef row.</summary>
    public int GetFieldDef(int row)
    {
        CheckRow(row);
        return checked((int)Read(((row - 1) * RowSize) + 8));
    }

    private void CheckRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidLayoutField();
        }
    }

    private uint Read(int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_rows.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidLayoutField()
        => new("Invalid TRRM LayoutField table.");
}

/// <summary>Emits ordered LayoutField lists and validates their owners and fields.</summary>
public static class TrRmLayoutFieldTableWriter
{
    /// <summary>Encodes ordered LayoutField rows and validates their owners and fields.</summary>
    /// <param name="rows">The LayoutField rows in Layout and ordinal order.</param>
    /// <param name="layouts">The validated Layout table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="fields">The validated FieldDef table.</param>
    /// <returns>The encoded LayoutField section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmLayoutFieldRow> rows,
        TrRmLayoutTableReader layouts, TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 12];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 12;
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(offset), unchecked((uint)row.OwnerLayoutRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(offset + 4), unchecked((uint)row.Ordinal));
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(offset + 8), unchecked((uint)row.FieldDefRow));
        }

        _ = new TrRmLayoutFieldTableReader(result, rows.Count,
            layouts, types, fields);
        return result;
    }
}
