// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmExceptionRegionRow(
    byte Kind, byte Flags, ushort ParentRegionIndex, ushort EntryBlock,
    ushort FinallyRegionIndex, uint HandlerFirst, uint HandlerCount);

internal readonly record struct TrRmExceptionHandlerRow(
    ushort ProtectedRegionIndex, byte CatchKind, byte HasBinding,
    ushort CatchTypeMapIndex, ushort BindingHomeIndex, byte HasFilter,
    ushort FilterRegionIndex, ushort HandlerRegionIndex,
    uint ExceptionWitnessRow);

internal readonly ref struct TrRmExceptionDirectoryReader
{
    private const int RecordHeaderSize = 4;
    private const int RegionRowSize = 16;
    private const int HandlerRowSize = 20;

    private readonly ReadOnlySpan<byte> _regions;
    private readonly ReadOnlySpan<byte> _handlers;
    private readonly ReadOnlySpan<byte> _blockRegions;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    internal TrRmExceptionDirectoryReader(ReadOnlySpan<byte> record,
        int blockCount, int homeCount, int typeCount,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (record.Length < 16 || record[0] != 0x0F || record[1] != 0
            || ReadUInt16(record, 2) != 0)
        {
            throw InvalidDirectory();
        }

        _resourceLedger = resourceLedger;
        var regionCount = ReadUInt32(record, RecordHeaderSize);
        var handlerCount = ReadUInt32(record, RecordHeaderSize + 4);
        var blockOwnerCount = ReadUInt32(record, RecordHeaderSize + 8);
        var expectedLength = 16L + ((long)regionCount * RegionRowSize)
            + ((long)handlerCount * HandlerRowSize)
            + ((long)blockOwnerCount * sizeof(ushort));
        if (regionCount is 0 or > 65_536
            || handlerCount > 65_536
            || blockOwnerCount is 0 or > 65_536
            || blockOwnerCount != blockCount
            || expectedLength != record.Length)
        {
            throw InvalidDirectory();
        }

        RegionCount = (int)regionCount;
        HandlerCount = (int)handlerCount;
        BlockOwnerCount = (int)blockOwnerCount;
        var handlerStart = checked(16 + (RegionCount * RegionRowSize));
        var blockOwnerStart = checked(handlerStart + (HandlerCount * HandlerRowSize));
        _regions = record.Slice(16, RegionCount * RegionRowSize);
        _handlers = record.Slice(handlerStart, HandlerCount * HandlerRowSize);
        _blockRegions = record.Slice(blockOwnerStart, BlockOwnerCount * sizeof(ushort));
        Validate(homeCount, typeCount);
    }

    internal int RegionCount { get; }
    internal int HandlerCount { get; }
    internal int BlockOwnerCount { get; }

    internal TrRmExceptionRegionRow GetRegion(int regionIndex)
    {
        _resourceLedger?.ConsumeWork();
        if ((uint)regionIndex >= (uint)RegionCount)
        {
            throw InvalidDirectory();
        }

        var row = _regions.Slice(regionIndex * RegionRowSize, RegionRowSize);
        return new TrRmExceptionRegionRow(
            row[0],
            row[1],
            ReadUInt16(row, 2),
            ReadUInt16(row, 4),
            ReadUInt16(row, 6),
            ReadUInt32(row, 8),
            ReadUInt32(row, 12));
    }

    internal TrRmExceptionHandlerRow GetHandler(int handlerIndex)
    {
        _resourceLedger?.ConsumeWork();
        if ((uint)handlerIndex >= (uint)HandlerCount)
        {
            throw InvalidDirectory();
        }

        var row = _handlers.Slice(handlerIndex * HandlerRowSize, HandlerRowSize);
        return new TrRmExceptionHandlerRow(
            ReadUInt16(row, 0),
            row[2],
            row[3],
            ReadUInt16(row, 4),
            ReadUInt16(row, 6),
            row[8],
            ReadUInt16(row, 10),
            ReadUInt16(row, 12),
            ReadUInt32(row, 16));
    }

    internal int GetBlockRegion(int blockIndex)
    {
        _resourceLedger?.ConsumeWork();
        if ((uint)blockIndex >= (uint)BlockOwnerCount)
        {
            throw InvalidDirectory();
        }

        return ReadUInt16(_blockRegions, blockIndex * sizeof(ushort));
    }

    private void Validate(int homeCount, int typeCount)
    {
        if (homeCount is < 0 or > 65_536 || typeCount is < 0 or > 65_536
            || GetRegion(0) != new TrRmExceptionRegionRow(0, 0, 0, 0, 0, 0, 0))
        {
            throw InvalidDirectory();
        }

        var regionKinds = new byte[RegionCount];
        var parents = new ushort[RegionCount];
        var entryBlocks = new ushort[RegionCount];
        var finallyRegionOwners = new byte[RegionCount];
        var specialRegionOwners = new byte[RegionCount];
        var directBlockCounts = new byte[RegionCount];
        var handlerRowOwners = new byte[HandlerCount];
        var bindingHomeOwners = new bool[homeCount];
        _resourceLedger?.ConsumeWork(checked((ulong)(RegionCount * 8L)
            + (ulong)(HandlerCount * 2L)));

        for (var regionIndex = 0; regionIndex < RegionCount; regionIndex++)
        {
            _resourceLedger?.ConsumeWork();
            var region = GetRegion(regionIndex);
            regionKinds[regionIndex] = region.Kind;
            parents[regionIndex] = region.ParentRegionIndex;
            entryBlocks[regionIndex] = region.EntryBlock;
            if (regionIndex == 0)
            {
                continue;
            }

            if (region.Kind is < 1 or > 4
                || region.ParentRegionIndex >= RegionCount
                || region.ParentRegionIndex == regionIndex
                || region.EntryBlock >= BlockOwnerCount)
            {
                throw InvalidDirectory();
            }

            if (region.Kind == 1)
            {
                if ((region.Flags & 0xFE) != 0
                    || (region.HandlerCount == 0
                        ? region.HandlerFirst != 0
                        : (ulong)region.HandlerFirst + region.HandlerCount
                            > (ulong)HandlerCount)
                    || ((region.Flags & 1) == 0
                        ? region.FinallyRegionIndex != 0
                        : region.FinallyRegionIndex >= RegionCount))
                {
                    throw InvalidDirectory();
                }

                if ((region.Flags & 1) != 0)
                {
                    var finallyRegion = GetRegion(region.FinallyRegionIndex);
                    if (finallyRegion.Kind != 4
                        || finallyRegion.ParentRegionIndex != regionIndex
                        || ++finallyRegionOwners[region.FinallyRegionIndex] != 1)
                    {
                        throw InvalidDirectory();
                    }
                }
            }
            else if (region.Flags != 0 || region.FinallyRegionIndex != 0
                || region.HandlerFirst != 0 || region.HandlerCount != 0)
            {
                throw InvalidDirectory();
            }
        }

        var parentState = new byte[RegionCount];
        var parentPath = new int[RegionCount];
        parentState[0] = 2;
        for (var regionIndex = 1; regionIndex < RegionCount; regionIndex++)
        {
            _resourceLedger?.ConsumeWork();
            if (parentState[regionIndex] == 2)
            {
                continue;
            }

            var pathCount = 0;
            var current = regionIndex;
            while (parentState[current] == 0)
            {
                _resourceLedger?.ConsumeWork();
                parentState[current] = 1;
                parentPath[pathCount++] = current;
                current = parents[current];
            }

            if (parentState[current] == 1)
            {
                throw InvalidDirectory();
            }

            while (pathCount != 0)
            {
                parentState[parentPath[--pathCount]] = 2;
            }
        }

        for (var handlerIndex = 0; handlerIndex < HandlerCount; handlerIndex++)
        {
            _resourceLedger?.ConsumeWork();
            var handler = GetHandler(handlerIndex);
            if (handler.ProtectedRegionIndex >= RegionCount
                || regionKinds[handler.ProtectedRegionIndex] != 1
                || handler.CatchKind > 3 || handler.HasBinding > 1
                || handler.HasFilter > 1
                || !IsZero(_handlers.Slice(
                    (handlerIndex * HandlerRowSize) + 9, 1))
                || ReadUInt16(_handlers, (handlerIndex * HandlerRowSize) + 14) != 0
                || (handler.HasBinding == 0
                    ? handler.BindingHomeIndex != 0
                    : handler.BindingHomeIndex >= homeCount)
                || (handler.HasFilter == 0
                    ? handler.FilterRegionIndex != 0
                    : handler.FilterRegionIndex >= RegionCount
                        || regionKinds[handler.FilterRegionIndex] != 2
                        || parents[handler.FilterRegionIndex]
                            != handler.ProtectedRegionIndex
                        || ++specialRegionOwners[handler.FilterRegionIndex] != 1)
                || handler.HandlerRegionIndex >= RegionCount
                || regionKinds[handler.HandlerRegionIndex] != 3
                || parents[handler.HandlerRegionIndex]
                    != handler.ProtectedRegionIndex
                || ++specialRegionOwners[handler.HandlerRegionIndex] != 1)
            {
                throw InvalidDirectory();
            }

            if (handler.HasBinding != 0)
            {
                if (bindingHomeOwners[handler.BindingHomeIndex])
                {
                    throw InvalidDirectory();
                }

                bindingHomeOwners[handler.BindingHomeIndex] = true;
            }

            switch (handler.CatchKind)
            {
                case 0:
                    if (handler.HasBinding != 0
                        || handler.CatchTypeMapIndex != 0
                        || handler.ExceptionWitnessRow != 0)
                    {
                        throw InvalidDirectory();
                    }
                    break;
                case 1:
                    if (handler.CatchTypeMapIndex >= typeCount
                        || handler.ExceptionWitnessRow is 0 or > 0x00FF_FFFF)
                    {
                        throw InvalidDirectory();
                    }
                    break;
                case 2:
                case 3:
                    if (handler.CatchTypeMapIndex >= typeCount
                        || handler.ExceptionWitnessRow != 0)
                    {
                        throw InvalidDirectory();
                    }
                    break;
            }
        }

        for (var regionIndex = 0; regionIndex < RegionCount; regionIndex++)
        {
            _resourceLedger?.ConsumeWork();
            if ((regionKinds[regionIndex] is 2 or 3
                    && specialRegionOwners[regionIndex] != 1)
                || (regionKinds[regionIndex] == 4
                    && finallyRegionOwners[regionIndex] != 1))
            {
                throw InvalidDirectory();
            }
        }

        for (var regionIndex = 1; regionIndex < RegionCount; regionIndex++)
        {
            _resourceLedger?.ConsumeWork();
            var region = GetRegion(regionIndex);
            for (var ordinal = 0; ordinal < region.HandlerCount; ordinal++)
            {
                var handlerIndex = checked((int)(region.HandlerFirst + (uint)ordinal));
                var handler = GetHandler(handlerIndex);
                if (handler.ProtectedRegionIndex != regionIndex
                    || ++handlerRowOwners[handlerIndex] != 1)
                {
                    throw InvalidDirectory();
                }
            }
        }

        for (var handlerIndex = 0; handlerIndex < HandlerCount; handlerIndex++)
        {
            if (handlerRowOwners[handlerIndex] != 1)
            {
                throw InvalidDirectory();
            }
        }

        for (var blockIndex = 0; blockIndex < BlockOwnerCount; blockIndex++)
        {
            _resourceLedger?.ConsumeWork();
            var regionIndex = GetBlockRegion(blockIndex);
            if (regionIndex >= RegionCount)
            {
                throw InvalidDirectory();
            }

            if (directBlockCounts[regionIndex] != byte.MaxValue)
            {
                directBlockCounts[regionIndex]++;
            }
        }

        for (var regionIndex = 0; regionIndex < RegionCount; regionIndex++)
        {
            _resourceLedger?.ConsumeWork();
            if (directBlockCounts[regionIndex] == 0
                || GetBlockRegion(entryBlocks[regionIndex]) != regionIndex)
            {
                throw InvalidDirectory();
            }
        }
    }

    private static bool IsZero(ReadOnlySpan<byte> bytes)
        => bytes.IndexOfAnyExcept((byte)0) < 0;

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.Slice(offset, sizeof(ushort)));

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidDirectory()
        => new("Invalid TRRM exception directory.");
}
