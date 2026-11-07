// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Bounds one present, offset-indexed TRRM table.</summary>
/// <remarks>The borrowed section bytes must remain unchanged while this index is in use.</remarks>
public readonly ref struct TrRmVariableTableIndex
{
    private readonly ReadOnlySpan<byte> _offsets;
    private readonly ReadOnlySpan<byte> _data;
    private readonly int _recordCount;
    private readonly int _minimumRowBytes;
    private readonly int _rowAlignment;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of indexed rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates and indexes an offset-indexed table section.</summary>
    /// <param name="section">The borrowed section bytes.</param>
    /// <param name="recordCount">The number of rows.</param>
    /// <param name="minimumRowBytes">The minimum size of each row.</param>
    /// <param name="rowAlignment">The required row-end alignment, either one or four bytes.</param>
    public TrRmVariableTableIndex(ReadOnlySpan<byte> section, int recordCount,
        int minimumRowBytes, int rowAlignment)
        : this(section, recordCount, minimumRowBytes, rowAlignment, null)
    {
    }

    internal TrRmVariableTableIndex(ReadOnlySpan<byte> section, int recordCount,
        int minimumRowBytes, int rowAlignment,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        _resourceLedger = resourceLedger;
        ConsumeWork(2);
        var indexBytes = ((long)recordCount + 1) * sizeof(uint);
        ConsumeWork();
        if (recordCount <= 0)
        {
            throw InvalidIndex();
        }

        ConsumeWork();
        if (recordCount > 0xFF_FFFF)
        {
            throw InvalidIndex();
        }

        ConsumeWork();
        if (minimumRowBytes < 0)
        {
            throw InvalidIndex();
        }

        ConsumeWork();
        if (rowAlignment is not (1 or 4))
        {
            throw InvalidIndex();
        }

        ConsumeWork();
        if (indexBytes > section.Length)
        {
            throw InvalidIndex();
        }

        ConsumeWork(2);
        _offsets = section.Slice(0, (int)indexBytes);
        _data = section.Slice((int)indexBytes);
        _recordCount = recordCount;
        _minimumRowBytes = minimumRowBytes;
        _rowAlignment = rowAlignment;
        var previous = ReadOffset(0);
        ConsumeWork();
        if (previous != 0)
        {
            throw InvalidIndex();
        }

        for (var row = 1; row <= recordCount; row++)
        {
            ConsumeWork();
            var next = ReadOffset(row);
            ConsumeWork();
            if (next > _data.Length)
            {
                throw InvalidIndex();
            }

            ConsumeWork();
            var minimumEnd = previous + minimumRowBytes;
            ConsumeWork();
            if (next < minimumEnd)
            {
                throw InvalidIndex();
            }

            ConsumeWork(2);
            if (next % rowAlignment != 0)
            {
                throw InvalidIndex();
            }

            previous = next;
        }

        ConsumeWork();
        if (previous != _data.Length)
        {
            throw InvalidIndex();
        }
    }

    /// <summary>Gets the borrowed bytes of a one-based row.</summary>
    /// <param name="row">The one-based row number.</param>
    /// <returns>The row's bytes.</returns>
    public ReadOnlySpan<byte> GetRow(int row)
    {
        ConsumeWork();
        if (row <= 0)
        {
            throw InvalidIndex();
        }

        ConsumeWork();
        if (row > _recordCount)
        {
            throw InvalidIndex();
        }

        var start = (int)ReadOffset(row - 1);
        var end = (int)ReadOffset(row);
        ConsumeWork();
        if (start < 0)
        {
            throw InvalidIndex();
        }

        ConsumeWork(2);
        if (start > _data.Length - _minimumRowBytes)
        {
            throw InvalidIndex();
        }

        ConsumeWork();
        if (end > _data.Length)
        {
            throw InvalidIndex();
        }

        ConsumeWork(2);
        if (end < start + _minimumRowBytes)
        {
            throw InvalidIndex();
        }

        // Row alignment is restricted to 1 or 4, so one low-bit mask checks both offsets.
        ConsumeWork(2);
        if (((start | end) & (_rowAlignment - 1)) != 0)
        {
            throw InvalidIndex();
        }

        ConsumeWork();
        return _data.Slice(start, end - start);
    }

    private uint ReadOffset(int row)
    {
        ConsumeWork(sizeof(uint) + 2);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            _offsets.Slice(row * sizeof(uint), sizeof(uint)));
    }

    private void ConsumeWork(ulong units = 1)
        => _resourceLedger?.ConsumeWork(units);

    private static InvalidDataException InvalidIndex()
        => new("Invalid TRRM variable table index.");
}
