// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A typed constant source and its kind-specific payload row.</summary>
/// <param name="Kind">The assigned ConstantSource tag.</param>
/// <param name="PayloadReference">The nonzero row in the tag-selected table.</param>
public readonly record struct TrRmConstantSourceRow(uint Kind, uint PayloadReference);

/// <summary>Validates ConstantSource tags and their selected table-row bounds.</summary>
public readonly ref struct TrRmConstantSourceTableReader
{
    private const int RowSize = 8;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated ConstantSource rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates each payload reference against its selected record table.</summary>
    /// <param name="section">The borrowed 8-byte ConstantSource rows.</param>
    /// <param name="recordCount">The number of ConstantSource rows.</param>
    /// <param name="frozenValueNodeCount">The number of FrozenValueNode rows.</param>
    /// <param name="constantExecutionCount">The number of ConstantExecution rows.</param>
    /// <param name="targetValueBindingCount">The number of TargetValueBinding rows.</param>
    public TrRmConstantSourceTableReader(ReadOnlySpan<byte> section, int recordCount,
        int frozenValueNodeCount, int constantExecutionCount, int targetValueBindingCount)
    {
        if (!ValidCount(recordCount) || !ValidCount(frozenValueNodeCount)
            || !ValidCount(constantExecutionCount) || !ValidCount(targetValueBindingCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidConstantSource();
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var kind = Read(data, 0);
            var payload = Read(data, 4);
            var selectedCount = kind switch
            {
                1 => frozenValueNodeCount,
                2 => constantExecutionCount,
                3 => targetValueBindingCount,
                _ => -1,
            };
            if (selectedCount < 0 || payload is 0 or > 0xFF_FFFF
                || payload > (uint)selectedCount)
            {
                throw InvalidConstantSource();
            }
        }
    }

    /// <summary>Gets the assigned kind tag for a ConstantSource row.</summary>
    public uint GetKind(int row)
        => ReadRow(row, 0);

    /// <summary>Gets the kind-specific one-based payload row.</summary>
    public uint GetPayloadReference(int row)
        => ReadRow(row, 4);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidConstantSource();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint ReadRow(int row, int offset)
        => Read(GetRow(row), offset);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static InvalidDataException InvalidConstantSource()
        => new("Invalid TRRM ConstantSource table.");
}

/// <summary>Encodes ConstantSource rows with tag-selected payload bounds.</summary>
public static class TrRmConstantSourceTableWriter
{
    private const int RowSize = 8;

    /// <summary>Encodes ConstantSource rows in one-based table order.</summary>
    /// <param name="rows">The typed constant-source rows.</param>
    /// <param name="frozenValueNodeCount">The number of FrozenValueNode rows.</param>
    /// <param name="constantExecutionCount">The number of ConstantExecution rows.</param>
    /// <param name="targetValueBindingCount">The number of TargetValueBinding rows.</param>
    /// <returns>The encoded ConstantSource section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmConstantSourceRow> rows,
        int frozenValueNodeCount, int constantExecutionCount, int targetValueBindingCount)
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
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), row.Kind);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(offset + 4), row.PayloadReference);
        }

        _ = new TrRmConstantSourceTableReader(result, rows.Count,
            frozenValueNodeCount, constantExecutionCount, targetValueBindingCount);
        return result;
    }
}
