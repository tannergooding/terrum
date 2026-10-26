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

/// <summary>An ordered child edge between two predicate-node occurrences.</summary>
/// <param name="ParentNodeRow">The one-based PredicateNode parent row.</param>
/// <param name="ChildOrdinal">The zero-based child ordinal.</param>
/// <param name="ChildNodeRow">The one-based PredicateNode child row.</param>
public readonly record struct TrRmPredicateChildEdgeRow(
    int ParentNodeRow, uint ChildOrdinal, int ChildNodeRow);

/// <summary>A graph's exclusively owned read-root and direct-dependency ranges.</summary>
/// <param name="RootFirstRow">The one-based first PredicateReadRoot row, or zero when empty.</param>
/// <param name="RootRowCount">The number of PredicateReadRoot rows.</param>
/// <param name="DependencyFirstRow">The one-based first PredicateReadDependency row, or zero when empty.</param>
/// <param name="DependencyRowCount">The number of PredicateReadDependency rows.</param>
public readonly record struct TrRmPredicateReadFootprintRow(
    int RootFirstRow, int RootRowCount, int DependencyFirstRow, int DependencyRowCount);

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

/// <summary>Validates child ordinals and PredicateGraph tree ownership.</summary>
public readonly ref struct TrRmPredicateChildEdgeTableReader
{
    private const int RowSize = 12;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated PredicateChildEdge rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates edge references, ordering, graph ownership and tree topology.</summary>
    /// <param name="section">The borrowed 12-byte PredicateChildEdge rows.</param>
    /// <param name="recordCount">The number of PredicateChildEdge rows.</param>
    /// <param name="nodeCount">The number of PredicateNode rows.</param>
    /// <param name="graphs">The validated PredicateGraph roots.</param>
    public TrRmPredicateChildEdgeTableReader(ReadOnlySpan<byte> section,
        int recordCount, int nodeCount, TrRmPredicateGraphTableReader graphs)
    {
        if (!ValidCount(recordCount) || !ValidCount(nodeCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidPredicateChildEdge();
        }

        _rows = section;
        _recordCount = recordCount;
        var childOffsets = new int[nodeCount + 2];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var parent = Read(data, 0);
            var child = Read(data, 8);
            if (parent is 0 or > 0xFF_FFFF || parent > (uint)nodeCount
                || child is 0 or > 0xFF_FFFF || child > (uint)nodeCount
                || child <= parent)
            {
                throw InvalidPredicateChildEdge();
            }

            childOffsets[checked((int)parent) + 1]++;
        }
        for (var index = 1; index < childOffsets.Length; index++)
        {
            childOffsets[index] = checked(childOffsets[index] + childOffsets[index - 1]);
        }

        var children = new int[recordCount];
        var ordinalSet = new byte[(recordCount + 7) / 8];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var parent = checked((int)Read(data, 0));
            var ordinal = Read(data, 4);
            var parentFirst = childOffsets[parent];
            var parentCount = childOffsets[parent + 1] - parentFirst;
            if (ordinal >= (uint)parentCount)
            {
                throw InvalidPredicateChildEdge();
            }

            var childIndex = checked(parentFirst + (int)ordinal);
            if (IsSet(ordinalSet, childIndex))
            {
                throw InvalidPredicateChildEdge();
            }

            Set(ordinalSet, childIndex);
            children[childIndex] = checked((int)Read(data, 8));
        }

        var rootFlags = new byte[(nodeCount + 7) / 8];
        for (var graph = 1; graph <= graphs.Count; graph++)
        {
            var root = graphs.GetRootNodeRow(graph);
            if (root > nodeCount || IsRoot(rootFlags, root))
            {
                throw InvalidPredicateChildEdge();
            }

            SetRoot(rootFlags, root);
        }

        var pending = new int[nodeCount];
        var nextNodeRow = 1;
        for (var root = 1; root <= nodeCount; root++)
        {
            if (!IsRoot(rootFlags, root))
            {
                continue;
            }
            if (root != nextNodeRow)
            {
                throw InvalidPredicateChildEdge();
            }

            var pendingCount = 0;
            pending[pendingCount++] = root;
            while (pendingCount != 0)
            {
                var parent = pending[--pendingCount];
                if (parent != nextNodeRow)
                {
                    throw InvalidPredicateChildEdge();
                }
                nextNodeRow++;

                var firstChild = childOffsets[parent];
                var childEnd = childOffsets[parent + 1];
                for (var child = childEnd - 1; child >= firstChild; child--)
                {
                    var childRow = children[child];
                    if (IsRoot(rootFlags, childRow))
                    {
                        throw InvalidPredicateChildEdge();
                    }

                    if (pendingCount == pending.Length)
                    {
                        throw InvalidPredicateChildEdge();
                    }

                    pending[pendingCount++] = childRow;
                }
            }
        }

        if (nextNodeRow != nodeCount + 1)
        {
            throw InvalidPredicateChildEdge();
        }
    }

    /// <summary>Gets the one-based PredicateNode parent row.</summary>
    public int GetParentNodeRow(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the zero-based child ordinal.</summary>
    public uint GetChildOrdinal(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the one-based PredicateNode child row.</summary>
    public int GetChildNodeRow(int row)
        => checked((int)Read(GetRow(row), 8));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidPredicateChildEdge();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool IsRoot(byte[] rootFlags, int row)
    {
        var index = row - 1;
        return (rootFlags[index >> 3] & (1 << (index & 7))) != 0;
    }

    private static bool IsSet(byte[] bitSet, int index)
        => (bitSet[index >> 3] & (1 << (index & 7))) != 0;

    private static void Set(byte[] bitSet, int index)
        => bitSet[index >> 3] |= (byte)(1 << (index & 7));

    private static void SetRoot(byte[] rootFlags, int row)
    {
        var index = row - 1;
        rootFlags[index >> 3] |= (byte)(1 << (index & 7));
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidPredicateChildEdge()
        => new("Invalid TRRM PredicateChildEdge table.");
}

/// <summary>Validates disjoint PredicateReadFootprint ranges.</summary>
public readonly ref struct TrRmPredicateReadFootprintTableReader
{
    private const int RowSize = 16;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated PredicateReadFootprint rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates range shape, row bounds and exclusive ownership.</summary>
    /// <param name="section">The borrowed 16-byte PredicateReadFootprint rows.</param>
    /// <param name="recordCount">The number of PredicateReadFootprint rows.</param>
    /// <param name="rootCount">The number of PredicateReadRoot rows.</param>
    /// <param name="dependencyCount">The number of PredicateReadDependency rows.</param>
    public TrRmPredicateReadFootprintTableReader(ReadOnlySpan<byte> section,
        int recordCount, int rootCount, int dependencyCount)
    {
        if (!ValidCount(recordCount) || !ValidCount(rootCount)
            || !ValidCount(dependencyCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidPredicateReadFootprint();
        }

        _rows = section;
        _recordCount = recordCount;
        var rootOwnership = new byte[(rootCount + 7) / 8];
        var dependencyOwnership = new byte[(dependencyCount + 7) / 8];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            ValidateRange(Read(data, 0), Read(data, 4), rootCount, rootOwnership);
            ValidateRange(Read(data, 8), Read(data, 12), dependencyCount,
                dependencyOwnership);
        }
    }

    /// <summary>Gets the one-based first PredicateReadRoot row, or zero when empty.</summary>
    public int GetRootFirstRow(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the number of PredicateReadRoot rows.</summary>
    public int GetRootRowCount(int row)
        => checked((int)Read(GetRow(row), 4));

    /// <summary>Gets the one-based first PredicateReadDependency row, or zero when empty.</summary>
    public int GetDependencyFirstRow(int row)
        => checked((int)Read(GetRow(row), 8));

    /// <summary>Gets the number of PredicateReadDependency rows.</summary>
    public int GetDependencyRowCount(int row)
        => checked((int)Read(GetRow(row), 12));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidPredicateReadFootprint();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static void ValidateRange(uint first, uint count, int targetCount,
        byte[] ownership)
    {
        if (count == 0)
        {
            if (first != 0)
            {
                throw InvalidPredicateReadFootprint();
            }

            return;
        }

        var last = (ulong)first + count - 1;
        if (first is 0 or > 0xFF_FFFF || last > (uint)targetCount)
        {
            throw InvalidPredicateReadFootprint();
        }

        var firstIndex = checked((int)first - 1);
        var lastIndex = checked((int)last - 1);
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            var byteIndex = index >> 3;
            var mask = (byte)(1 << (index & 7));
            if ((ownership[byteIndex] & mask) != 0)
            {
                throw InvalidPredicateReadFootprint();
            }

            ownership[byteIndex] |= mask;
        }
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidPredicateReadFootprint()
        => new("Invalid TRRM PredicateReadFootprint table.");
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
            var ordinal = unchecked((uint)identity);
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

/// <summary>Encodes canonically ordered PredicateChildEdge rows.</summary>
public static class TrRmPredicateChildEdgeTableWriter
{
    private const int RowSize = 12;

    /// <summary>Encodes child edges by parent row and then child ordinal.</summary>
    /// <param name="rows">The ordered child relationships.</param>
    /// <param name="nodeCount">The number of PredicateNode rows.</param>
    /// <param name="graphs">The validated PredicateGraph roots.</param>
    /// <returns>The encoded PredicateChildEdge section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmPredicateChildEdgeRow> rows,
        int nodeCount, TrRmPredicateGraphTableReader graphs)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var orderedIndexes = new int[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            orderedIndexes[index] = index;
        }
        Array.Sort(orderedIndexes, (leftIndex, rightIndex) =>
        {
            var left = rows[leftIndex];
            var right = rows[rightIndex];
            var parentOrder = left.ParentNodeRow.CompareTo(right.ParentNodeRow);
            return parentOrder != 0
                ? parentOrder : left.ChildOrdinal.CompareTo(right.ChildOrdinal);
        });

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < orderedIndexes.Length; index++)
        {
            var row = rows[orderedIndexes[index]];
            var offset = index * RowSize;
            Write(result, offset, unchecked((uint)row.ParentNodeRow));
            Write(result, offset + 4, row.ChildOrdinal);
            Write(result, offset + 8, unchecked((uint)row.ChildNodeRow));
        }

        _ = new TrRmPredicateChildEdgeTableReader(
            result, rows.Count, nodeCount, graphs);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}

/// <summary>Encodes disjoint PredicateReadFootprint ranges.</summary>
public static class TrRmPredicateReadFootprintTableWriter
{
    private const int RowSize = 16;

    /// <summary>Encodes PredicateReadFootprint rows in one-based table order.</summary>
    /// <param name="rows">The graph-owned read-root and direct-dependency ranges.</param>
    /// <param name="rootCount">The number of PredicateReadRoot rows.</param>
    /// <param name="dependencyCount">The number of PredicateReadDependency rows.</param>
    /// <returns>The encoded PredicateReadFootprint section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmPredicateReadFootprintRow> rows,
        int rootCount, int dependencyCount)
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
            Write(result, offset, unchecked((uint)row.RootFirstRow));
            Write(result, offset + 4, unchecked((uint)row.RootRowCount));
            Write(result, offset + 8, unchecked((uint)row.DependencyFirstRow));
            Write(result, offset + 12, unchecked((uint)row.DependencyRowCount));
        }

        _ = new TrRmPredicateReadFootprintTableReader(
            result, rows.Count, rootCount, dependencyCount);
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
