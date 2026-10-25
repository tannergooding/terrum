// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A predicate expression root and its read footprint.</summary>
/// <param name="RootNodeRow">The one-based PredicateNode root row.</param>
/// <param name="ReadFootprintRow">The one-based PredicateReadFootprint row.</param>
public readonly record struct TrRmPredicateGraphRow(
    int RootNodeRow, int ReadFootprintRow);

/// <summary>A graph-local typed capture for a value, checked reference or type argument.</summary>
/// <param name="GraphRow">The one-based owning PredicateGraph row.</param>
/// <param name="Ordinal">The zero-based capture ordinal.</param>
/// <param name="Role">The assigned capture role.</param>
/// <param name="Flags">The role-specific flags.</param>
/// <param name="CapturedTypeSpecRef">The complete captured TypeSpecRef.</param>
public readonly record struct TrRmPredicateCaptureRow(
    int GraphRow, uint Ordinal, byte Role, byte Flags, uint CapturedTypeSpecRef);

/// <summary>Validates PredicateGraph root and footprint row references.</summary>
public readonly ref struct TrRmPredicateGraphTableReader
{
    private const int RowSize = 8;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated PredicateGraph rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates required root-node and read-footprint row bounds.</summary>
    /// <param name="section">The borrowed 8-byte PredicateGraph rows.</param>
    /// <param name="recordCount">The number of PredicateGraph rows.</param>
    /// <param name="nodeCount">The number of PredicateNode rows.</param>
    /// <param name="readFootprintCount">The number of PredicateReadFootprint rows.</param>
    public TrRmPredicateGraphTableReader(ReadOnlySpan<byte> section,
        int recordCount, int nodeCount, int readFootprintCount)
    {
        if (!ValidCount(recordCount) || !ValidCount(nodeCount)
            || !ValidCount(readFootprintCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidPredicateGraph();
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var rootNode = Read(data, 0);
            var footprint = Read(data, 4);
            if (rootNode is 0 or > 0xFF_FFFF || rootNode > (uint)nodeCount
                || footprint is 0 or > 0xFF_FFFF
                || footprint > (uint)readFootprintCount)
            {
                throw InvalidPredicateGraph();
            }
        }
    }

    /// <summary>Gets the one-based PredicateNode root row.</summary>
    public int GetRootNodeRow(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the one-based PredicateReadFootprint row.</summary>
    public int GetReadFootprintRow(int row)
        => checked((int)Read(GetRow(row), 4));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidPredicateGraph();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidPredicateGraph()
        => new("Invalid TRRM PredicateGraph table.");
}

/// <summary>Validates capture row roles, complete types and graph-local ordinals.</summary>
public readonly ref struct TrRmPredicateCaptureTableReader
{
    private const int RowSize = 16;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated PredicateCapture rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates graph references, contiguous ordinals, role flags and types.</summary>
    /// <param name="section">The borrowed 16-byte PredicateCapture rows.</param>
    /// <param name="recordCount">The number of PredicateCapture rows.</param>
    /// <param name="graphCount">The number of PredicateGraph rows.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    public TrRmPredicateCaptureTableReader(ReadOnlySpan<byte> section,
        int recordCount, int graphCount, TrRmTypeSpecTableReader types)
    {
        if (!ValidCount(recordCount) || !ValidCount(graphCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidPredicateCapture();
        }

        _rows = section;
        _recordCount = recordCount;
        var identities = new ulong[recordCount];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var graph = Read(data, 0);
            var ordinal = Read(data, 4);
            var role = data[8];
            var flags = data[9];
            var reserved = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
            var typeSpecRef = Read(data, 12);
            if (graph is 0 or > 0xFF_FFFF || graph > (uint)graphCount
                || role is < 1 or > 3
                || (role == 1 ? (flags & ~1) != 0 : flags != 0)
                || reserved != 0 || typeSpecRef == 0)
            {
                throw InvalidPredicateCapture();
            }

            _ = types.GetTypeSpecRef(typeSpecRef);
            identities[row - 1] = ((ulong)graph << 32) | ordinal;
        }

        Array.Sort(identities);
        var previousGraph = 0u;
        ulong nextOrdinal = 0;
        foreach (var identity in identities)
        {
            var graph = (uint)(identity >> 32);
            var ordinal = (uint)identity;
            if (graph != previousGraph)
            {
                if (ordinal != 0)
                {
                    throw InvalidPredicateCapture();
                }

                previousGraph = graph;
                nextOrdinal = 1;
            }
            else if (ordinal != nextOrdinal)
            {
                throw InvalidPredicateCapture();
            }
            else
            {
                nextOrdinal++;
            }
        }
    }

    /// <summary>Gets the one-based owning PredicateGraph row.</summary>
    public int GetGraphRow(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the zero-based graph-local capture ordinal.</summary>
    public uint GetOrdinal(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the assigned capture role.</summary>
    public byte GetRole(int row)
        => GetRow(row)[8];

    /// <summary>Gets the role-specific capture flags.</summary>
    public byte GetFlags(int row)
        => GetRow(row)[9];

    /// <summary>Gets the complete captured TypeSpecRef.</summary>
    public uint GetCapturedTypeSpecRef(int row)
        => Read(GetRow(row), 12);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidPredicateCapture();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidPredicateCapture()
        => new("Invalid TRRM PredicateCapture table.");
}

/// <summary>Encodes PredicateGraph rows with checked root and footprint references.</summary>
public static class TrRmPredicateGraphTableWriter
{
    private const int RowSize = 8;

    /// <summary>Encodes PredicateGraph rows in one-based table order.</summary>
    /// <param name="rows">The predicate roots and read-footprint references.</param>
    /// <param name="nodeCount">The number of PredicateNode rows.</param>
    /// <param name="readFootprintCount">The number of PredicateReadFootprint rows.</param>
    /// <returns>The encoded PredicateGraph section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmPredicateGraphRow> rows,
        int nodeCount, int readFootprintCount)
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
            Write(result, offset, unchecked((uint)row.RootNodeRow));
            Write(result, offset + 4, unchecked((uint)row.ReadFootprintRow));
        }

        _ = new TrRmPredicateGraphTableReader(
            result, rows.Count, nodeCount, readFootprintCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}

/// <summary>Encodes graph-local typed predicate captures.</summary>
public static class TrRmPredicateCaptureTableWriter
{
    private const int RowSize = 16;

    /// <summary>Encodes PredicateCapture rows in one-based table order.</summary>
    /// <param name="rows">The graph-local capture identities and complete types.</param>
    /// <param name="graphCount">The number of PredicateGraph rows.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <returns>The encoded PredicateCapture section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmPredicateCaptureRow> rows,
        int graphCount, TrRmTypeSpecTableReader types)
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
            Write(result, offset, unchecked((uint)row.GraphRow));
            Write(result, offset + 4, row.Ordinal);
            result[offset + 8] = row.Role;
            result[offset + 9] = row.Flags;
            Write(result, offset + 12, row.CapturedTypeSpecRef);
        }

        _ = new TrRmPredicateCaptureTableReader(
            result, rows.Count, graphCount, types);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
