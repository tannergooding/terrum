// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmConstantExecutionSubstitutionRow(
    byte Kind, uint Reference);

internal readonly ref struct TrRmConstantExecutionSubstitutionTableReader
{
    private const int RowSize = 8;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmConstantExecutionSubstitutionTableReader(
        ReadOnlySpan<byte> section, int recordCount, int constantSourceCount,
        TrRmTypeSpecTableReader typeSpecs, TrRmArtifactSchema schema)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || constantSourceCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length
            || schema != TrRmArtifactSchema.GeneralPredicateGraphsDraft)
        {
            throw InvalidTable();
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var kind = data[0];
            var reference = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            var invalidReference = kind switch
            {
                1 => reference == 0,
                2 => !TrRmConstantExecutionTableValidation.IsRowReference(
                    reference, constantSourceCount),
                _ => true,
            };
            if (data[1] != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != 0
                || invalidReference)
            {
                throw InvalidTable();
            }

            if (kind == 1)
            {
                _ = typeSpecs.GetTypeSpecRef(reference);
            }
        }
    }

    internal byte GetKind(int row) => GetRow(row)[0];

    internal uint GetReference(int row)
        => BinaryPrimitives.ReadUInt32LittleEndian(GetRow(row)[4..]);

    internal uint GetTypeSpecRef(int row)
    {
        if (GetKind(row) != 1)
        {
            throw InvalidTable();
        }

        return GetReference(row);
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidTable();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static InvalidDataException InvalidTable()
        => new("Invalid TRRM ConstantExecutionSubstitution table.");
}

internal static class TrRmConstantExecutionSubstitutionTableWriter
{
    private const int RowSize = 8;

    internal static byte[] Encode(
        IReadOnlyList<TrRmConstantExecutionSubstitutionRow> rows,
        int constantSourceCount, TrRmTypeSpecTableReader typeSpecs,
        TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            var offset = index * RowSize;
            result[offset] = rows[index].Kind;
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(offset + 4), rows[index].Reference);
        }

        _ = new TrRmConstantExecutionSubstitutionTableReader(
            result, rows.Count, constantSourceCount, typeSpecs, schema);
        return result;
    }
}
