// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A complete structural field path with ordered FieldReferences.</summary>
/// <param name="RootTypeSpecRef">The required complete root type.</param>
/// <param name="FieldReferences">The ordered FieldDef DefIds or FieldDef Imports.</param>
public readonly record struct TrRmFieldPathRow(
    uint RootTypeSpecRef, IReadOnlyList<uint> FieldReferences);

/// <summary>Validates offset-indexed FieldPath rows and their typed references.</summary>
public readonly ref struct TrRmFieldPathTableReader
{
    private readonly TrRmVariableTableIndex _rows;

    /// <summary>Gets the number of validated FieldPath rows.</summary>
    public int Count => _rows.Count;

    /// <summary>Validates every complete path and its FieldReference sequence.</summary>
    /// <param name="section">The borrowed FieldPath offset-indexed section.</param>
    /// <param name="recordCount">The number of variable FieldPath rows.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="fields">The validated FieldDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    public TrRmFieldPathTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeSpecTableReader types, TrRmFieldDefTableReader fields,
        TrRmImportTableReader imports)
    {
        _rows = new TrRmVariableTableIndex(section, recordCount, 8, 4);
        for (var row = 1; row <= recordCount; row++)
        {
            var data = _rows.GetRow(row);
            var root = Read(data, 0);
            var fieldCount = Read(data, 4);
            if (root == 0 || fieldCount == 0
                || (8L + (4L * fieldCount)) != data.Length)
            {
                throw InvalidFieldPath();
            }

            _ = types.GetTypeSpecRef(root);
            for (var field = 0; field < fieldCount; field++)
            {
                TrRmFieldReferenceValidator.Validate(
                    Read(data, 8 + checked((int)field * sizeof(uint))), fields, imports);
            }
        }
    }

    /// <summary>Gets the complete root TypeSpecRef.</summary>
    public uint GetRootTypeSpecRef(int row)
        => Read(GetRow(row), 0);

    /// <summary>Gets the number of ordered FieldReferences in a path.</summary>
    public int GetFieldCount(int row)
        => checked((int)Read(GetRow(row), 4));

    /// <summary>Gets an ordered FieldDef DefId or FieldDef Import token.</summary>
    public uint GetFieldReference(int row, int ordinal)
    {
        var data = GetRow(row);
        var fieldCount = checked((int)Read(data, 4));
        if (ordinal < 0 || ordinal >= fieldCount)
        {
            throw InvalidFieldPath();
        }

        return Read(data, 8 + (ordinal * sizeof(uint)));
    }

    private ReadOnlySpan<byte> GetRow(int row)
        => _rows.GetRow(row);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidFieldPath()
        => new("Invalid TRRM FieldPath table.");
}

/// <summary>Encodes offset-indexed FieldPath rows with validated FieldReferences.</summary>
public static class TrRmFieldPathTableWriter
{
    /// <summary>Encodes complete FieldPath rows in one-based table order.</summary>
    /// <param name="rows">The structural paths and their ordered fields.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="fields">The validated FieldDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <returns>The encoded FieldPath section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmFieldPathRow> rows,
        TrRmTypeSpecTableReader types, TrRmFieldDefTableReader fields,
        TrRmImportTableReader imports)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var encodedRows = new ReadOnlyMemory<byte>[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            ArgumentNullException.ThrowIfNull(row.FieldReferences);
            if (row.FieldReferences.Count <= 0)
            {
                throw new InvalidDataException("A FieldPath requires at least one FieldReference.");
            }

            var rowLength = 8L + (4L * row.FieldReferences.Count);
            if (rowLength > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(rows));
            }

            var bytes = new byte[(int)rowLength];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, row.RootTypeSpecRef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(4), checked((uint)row.FieldReferences.Count));
            for (var field = 0; field < row.FieldReferences.Count; field++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(8 + (field * sizeof(uint))), row.FieldReferences[field]);
            }

            encodedRows[index] = bytes;
        }

        var result = TrRmVariableTableWriter.EncodeBlobs(encodedRows);
        _ = new TrRmFieldPathTableReader(result, rows.Count, types, fields, imports);
        return result;
    }
}
