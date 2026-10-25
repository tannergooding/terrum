// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A generated text-content type and its ordered shape ranges.</summary>
/// <param name="GeneratedTypeDefRow">The one-based generated TypeDef row.</param>
/// <param name="CaptureFirst">The first owned TextCaptureShape row, or zero when empty.</param>
/// <param name="CaptureCount">The number of owned TextCaptureShape rows.</param>
/// <param name="FragmentFirst">The first owned TextFragmentShape row, or zero when empty.</param>
/// <param name="FragmentCount">The number of owned TextFragmentShape rows.</param>
public readonly record struct TrRmTextContentShapeRow(
    int GeneratedTypeDefRow, int CaptureFirst, int CaptureCount,
    int FragmentFirst, int FragmentCount);

/// <summary>A typed value capture in one generated text-content shape.</summary>
/// <param name="ShapeRow">The one-based owning TextContentShape row.</param>
/// <param name="Ordinal">The zero-based capture ordinal.</param>
/// <param name="TypeSpecRef">The complete captured type.</param>
/// <param name="Mode">The assigned copy or ownership-transfer mode.</param>
public readonly record struct TrRmTextCaptureShapeRow(
    int ShapeRow, uint Ordinal, uint TypeSpecRef, byte Mode);

/// <summary>Validates generated type owners and exclusive capture/fragment ranges.</summary>
public readonly ref struct TrRmTextContentShapeTableReader
{
    private const int RowSize = 20;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated TextContentShape rows.</summary>
    public int Count => _recordCount;

    /// <summary>Gets the number of TextCaptureShape rows partitioned by the shapes.</summary>
    public int CaptureCount { get; }

    /// <summary>Gets the number of TextFragmentShape rows partitioned by the shapes.</summary>
    public int FragmentCount { get; }

    /// <summary>Validates generated type references and exclusive shape ranges.</summary>
    /// <param name="section">The borrowed 20-byte TextContentShape rows.</param>
    /// <param name="recordCount">The number of TextContentShape rows.</param>
    /// <param name="typeDefs">The validated TypeDef table.</param>
    /// <param name="captureCount">The number of TextCaptureShape rows.</param>
    /// <param name="fragmentCount">The number of TextFragmentShape rows.</param>
    public TrRmTextContentShapeTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeDefTableReader typeDefs,
        int captureCount, int fragmentCount)
    {
        if (!ValidCount(recordCount) || !ValidCount(captureCount)
            || !ValidCount(fragmentCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidTextContentShape();
        }

        _rows = section;
        _recordCount = recordCount;
        CaptureCount = captureCount;
        FragmentCount = fragmentCount;
        var ownedCaptures = new bool[captureCount + 1];
        var ownedFragments = new bool[fragmentCount + 1];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var generatedTypeDef = Read(data, 0);
            var captureFirst = Read(data, 4);
            var captures = Read(data, 8);
            var fragmentFirst = Read(data, 12);
            var fragments = Read(data, 16);
            if (generatedTypeDef is 0 or > 0xFF_FFFF
                || generatedTypeDef > (uint)typeDefs.Count
                || !ValidRange(captureFirst, captures, captureCount)
                || !ValidRange(fragmentFirst, fragments, fragmentCount))
            {
                throw InvalidTextContentShape();
            }

            MarkRange(ownedCaptures, captureFirst, captures);
            MarkRange(ownedFragments, fragmentFirst, fragments);
        }

        if (HasUnownedRows(ownedCaptures) || HasUnownedRows(ownedFragments))
        {
            throw InvalidTextContentShape();
        }
    }

    /// <summary>Gets the one-based generated TypeDef row.</summary>
    public int GetGeneratedTypeDef(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the one-based capture range.</summary>
    public (int First, int Count) GetCaptureRange(int row)
    {
        var data = GetRow(row);
        return (checked((int)Read(data, 4)), checked((int)Read(data, 8)));
    }

    /// <summary>Gets the one-based fragment range.</summary>
    public (int First, int Count) GetFragmentRange(int row)
    {
        var data = GetRow(row);
        return (checked((int)Read(data, 12)), checked((int)Read(data, 16)));
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidTextContentShape();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static void MarkRange(bool[] owners, uint first, uint count)
    {
        for (var row = first; row < first + count; row++)
        {
            if (owners[row])
            {
                throw InvalidTextContentShape();
            }

            owners[row] = true;
        }
    }

    private static bool HasUnownedRows(bool[] owners)
    {
        for (var row = 1; row < owners.Length; row++)
        {
            if (!owners[row])
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValidRange(uint first, uint count, int total)
        => count == 0 ? first == 0
            : first != 0 && first <= (uint)total
                && count <= (uint)total - first + 1;

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidTextContentShape()
        => new("Invalid TRRM TextContentShape table.");
}

/// <summary>Validates ordered typed captures in their owning text-content shape.</summary>
public readonly ref struct TrRmTextCaptureShapeTableReader
{
    private const int RowSize = 16;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated TextCaptureShape rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates capture ownership, ordinal, mode and complete type.</summary>
    /// <param name="section">The borrowed 16-byte TextCaptureShape rows.</param>
    /// <param name="recordCount">The number of TextCaptureShape rows.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="shapes">The validated TextContentShape table.</param>
    public TrRmTextCaptureShapeTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeSpecTableReader types,
        TrRmTextContentShapeTableReader shapes)
    {
        if (!ValidCount(recordCount) || recordCount != shapes.CaptureCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidTextCaptureShape();
        }

        _rows = section;
        _recordCount = recordCount;
        for (var shape = 1; shape <= shapes.Count; shape++)
        {
            var (first, count) = shapes.GetCaptureRange(shape);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var row = first + ordinal;
                var data = GetRow(row);
                var owner = Read(data, 0);
                var captureOrdinal = Read(data, 4);
                var typeSpecRef = Read(data, 8);
                var mode = data[12];
                var flags = data[13];
                var reserved = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
                if (owner != (uint)shape || captureOrdinal != (uint)ordinal
                    || typeSpecRef == 0 || mode is not (1 or 2)
                    || flags != 0 || reserved != 0)
                {
                    throw InvalidTextCaptureShape();
                }

                _ = types.GetTypeSpecRef(typeSpecRef);
            }
        }
    }

    /// <summary>Gets the one-based owning TextContentShape row.</summary>
    public int GetShapeRow(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the zero-based capture ordinal.</summary>
    public uint GetOrdinal(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the complete captured TypeSpecRef.</summary>
    public uint GetTypeSpecRef(int row)
        => Read(GetRow(row), 8);

    /// <summary>Gets the assigned copy or ownership-transfer mode.</summary>
    public byte GetMode(int row)
        => GetRow(row)[12];

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidTextCaptureShape();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidTextCaptureShape()
        => new("Invalid TRRM TextCaptureShape table.");
}

/// <summary>Encodes generated text-content shapes and their exclusive ranges.</summary>
public static class TrRmTextContentShapeTableWriter
{
    private const int RowSize = 20;

    /// <summary>Encodes TextContentShape rows in one-based table order.</summary>
    /// <param name="rows">The generated types and their capture/fragment ranges.</param>
    /// <param name="typeDefs">The validated TypeDef table.</param>
    /// <param name="captureCount">The number of TextCaptureShape rows.</param>
    /// <param name="fragmentCount">The number of TextFragmentShape rows.</param>
    /// <returns>The encoded TextContentShape section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmTextContentShapeRow> rows,
        TrRmTypeDefTableReader typeDefs, int captureCount, int fragmentCount)
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
            Write(result, offset, unchecked((uint)row.GeneratedTypeDefRow));
            Write(result, offset + 4, unchecked((uint)row.CaptureFirst));
            Write(result, offset + 8, unchecked((uint)row.CaptureCount));
            Write(result, offset + 12, unchecked((uint)row.FragmentFirst));
            Write(result, offset + 16, unchecked((uint)row.FragmentCount));
        }

        _ = new TrRmTextContentShapeTableReader(
            result, rows.Count, typeDefs, captureCount, fragmentCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}

/// <summary>Encodes typed text-content captures with checked shape ownership.</summary>
public static class TrRmTextCaptureShapeTableWriter
{
    private const int RowSize = 16;

    /// <summary>Encodes TextCaptureShape rows in one-based table order.</summary>
    /// <param name="rows">The ordered typed captures and acquisition modes.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="shapes">The validated TextContentShape table.</param>
    /// <returns>The encoded TextCaptureShape section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmTextCaptureShapeRow> rows,
        TrRmTypeSpecTableReader types,
        TrRmTextContentShapeTableReader shapes)
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
            Write(result, offset, unchecked((uint)row.ShapeRow));
            Write(result, offset + 4, row.Ordinal);
            Write(result, offset + 8, row.TypeSpecRef);
            result[offset + 12] = row.Mode;
        }

        _ = new TrRmTextCaptureShapeTableReader(
            result, rows.Count, types, shapes);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
