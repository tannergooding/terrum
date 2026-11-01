// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmExceptionDirectoryReaderTests
{
    [Test]
    public void ReadsRootOnlyDirectory()
    {
        var bytes = new byte[34];
        bytes[0] = 0x0F;
        Write(bytes, 4, 1);
        Write(bytes, 12, 1);

        var directory = new TrRmExceptionDirectoryReader(bytes, 1, 0, 0);

        Assert.That(directory.RegionCount, Is.EqualTo(1));
        Assert.That(directory.HandlerCount, Is.Zero);
        Assert.That(directory.BlockOwnerCount, Is.EqualTo(1));
        Assert.That(directory.GetRegion(0).Kind, Is.Zero);
        Assert.That(directory.GetBlockRegion(0), Is.Zero);
    }

    [Test]
    public void ExceptionRecordWritersEmitAssignedAnchorsAndPayloads()
    {
        var root = new TrRmExceptionRegionRow(0, 0, 0, 0, 0, 0, 0);
        var directory = TrRmRegisterBodyRecordWriter.EncodeExceptionDirectory(
            [root], [], new byte[sizeof(ushort)]);
        Assert.That(directory, Is.EqualTo(CreateRootOnlyDirectory()));
        Assert.That(TrRmRegisterBodyRecordWriter.EncodeRegionExit(
            1, 1, 0, 0, 0, 0, [1]), Is.EqualTo(LeaveRecord(0, 1)));
    }

    [Test]
    public void ReadsOrderedCatchFilterHandlerAndFinallyRows()
    {
        var bytes = new byte[126];
        bytes[0] = 0x0F;
        Write(bytes, 4, 5);
        Write(bytes, 8, 1);
        Write(bytes, 12, 5);

        var protectedRegion = 16 + 16;
        bytes[protectedRegion] = 1;
        bytes[protectedRegion + 1] = 1;
        Write(bytes, protectedRegion + 4, 1);
        Write(bytes, protectedRegion + 6, 4);
        Write(bytes, protectedRegion + 8, 0);
        Write(bytes, protectedRegion + 12, 1);

        var filterRegion = 16 + 32;
        bytes[filterRegion] = 2;
        Write(bytes, filterRegion + 2, 1);
        Write(bytes, filterRegion + 4, 2);

        var handlerRegion = 16 + 48;
        bytes[handlerRegion] = 3;
        Write(bytes, handlerRegion + 2, 1);
        Write(bytes, handlerRegion + 4, 3);

        var finallyRegion = 16 + 64;
        bytes[finallyRegion] = 4;
        Write(bytes, finallyRegion + 2, 1);
        Write(bytes, finallyRegion + 4, 4);

        var handler = 16 + (5 * 16);
        Write(bytes, handler, 1);
        bytes[handler + 2] = 1;
        bytes[handler + 3] = 1;
        bytes[handler + 8] = 1;
        Write(bytes, handler + 10, 2);
        Write(bytes, handler + 12, 3);
        Write(bytes, handler + 16, 1);

        var blockOwners = 16 + (5 * 16) + 20;
        for (var index = 0; index < 5; index++)
        {
            Write(bytes, blockOwners + (index * sizeof(ushort)),
                checked((ushort)index));
        }

        var directory = new TrRmExceptionDirectoryReader(bytes, 5, 1, 1);

        Assert.That(directory.GetRegion(1).Kind, Is.EqualTo(1));
        Assert.That(directory.GetRegion(1).FinallyRegionIndex, Is.EqualTo(4));
        Assert.That(directory.GetHandler(0).FilterRegionIndex, Is.EqualTo(2));
        Assert.That(directory.GetHandler(0).HandlerRegionIndex, Is.EqualTo(3));
        Assert.That(directory.GetBlockRegion(4), Is.EqualTo(4));
    }

    [Test]
    public void RejectsMalformedDirectoryOwnershipAndExtents()
    {
        var rootOnly = CreateRootOnlyDirectory();
        rootOnly[16] = 1;
        Assert.Throws<InvalidDataException>(() =>
            new TrRmExceptionDirectoryReader(rootOnly, 1, 0, 0));

        rootOnly = CreateRootOnlyDirectory();
        Write(rootOnly, 12, 2);
        Assert.Throws<InvalidDataException>(() =>
            new TrRmExceptionDirectoryReader(rootOnly, 1, 0, 0));

        rootOnly = CreateRootOnlyDirectory();
        rootOnly[1] = 1;
        Assert.Throws<InvalidDataException>(() =>
            new TrRmExceptionDirectoryReader(rootOnly, 1, 0, 0));
    }

    [Test]
    public void RegisterExceptionRegionsBodyUsesAnchoredDirectoryAndExactLeavePath()
    {
        var schema = TrRmArtifactSchema.RegisterExceptionRegionsDraft;
        var directory = CreateRootAndProtectedDirectory(2, [0, 1]);
        var records = new ReadOnlyMemory<byte>[]
        {
            directory,
            BlockEntryState(),
            BlockEntryState(),
            LeaveRecord(0, 1),
        };
        var blocks = new[]
        {
            new TrRmRegisterBodyBlock(0, 2, 1),
            new TrRmRegisterBodyBlock(2, 2, 2),
        };

        _ = TrRmRegisterBodyWriter.Encode(ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, blocks,
            records, new byte[] { 0xB0, 1, 0xB6, 3 }, schema: schema);

        var invalidPath = LeaveRecord(0, 0);
        records[3] = invalidPath;
        Assert.Throws<InvalidDataException>(() =>
            TrRmRegisterBodyWriter.Encode(ReadOnlyMemory<byte>.Empty,
                ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
                ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, blocks,
                records, new byte[] { 0xB0, 1, 0xB6, 3 }, schema: schema));

        var invalidEntry = CreateRootAndProtectedDirectory(3, [0, 1, 1]);
        records = [
            invalidEntry,
            BlockEntryState(),
            BlockEntryState(),
            BlockEntryState(),
            LeaveRecord(0, 1),
        ];
        blocks = [
            new TrRmRegisterBodyBlock(0, 2, 1),
            new TrRmRegisterBodyBlock(2, 2, 2),
            new TrRmRegisterBodyBlock(4, 2, 3),
        ];
        Assert.Throws<InvalidDataException>(() =>
            TrRmRegisterBodyWriter.Encode(ReadOnlyMemory<byte>.Empty,
                ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
                ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, blocks,
                records, new byte[] { 0xB0, 2, 0xB6, 4, 0xB6, 4 },
                schema: schema));
    }

    [Test]
    public void RegisterExceptionRegionsBodyAdmitsFreshThrowAndExecutedUnreachable()
    {
        var schema = TrRmArtifactSchema.RegisterExceptionRegionsDraft;
        var throwRecord = new byte[12];
        throwRecord[0] = 0x09;
        throwRecord[4] = 1;
        throwRecord[5] = 1;
        foreach (var code in new byte[][]
        {
            [0xB4, 2],
            [0xE8, 0, 2, 0],
        })
        {
            var records = new ReadOnlyMemory<byte>[]
            {
                CreateRootOnlyDirectory(),
                BlockEntryState(),
                throwRecord,
            };
            _ = TrRmRegisterBodyWriter.Encode(ReadOnlyMemory<byte>.Empty,
                ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
                ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
                [new TrRmRegisterBodyBlock(0, (uint)code.Length, 1)],
                records, code, schema: schema);
        }

        _ = TrRmRegisterBodyWriter.Encode(ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
            [new TrRmRegisterBodyBlock(0, 2, 1)],
            new ReadOnlyMemory<byte>[]
            {
                CreateRootOnlyDirectory(),
                BlockEntryState(),
            },
            new byte[] { 0xB7, 0 }, schema: schema);
    }

    [Test]
    public void RegisterExceptionRegionsBodyValidatesAllExitKindsAndDispatchEntries()
    {
        var directory = new byte[16 + (5 * 16) + 20 + (5 * sizeof(ushort))];
        directory[0] = 0x0F;
        Write(directory, 4, 5);
        Write(directory, 8, 1);
        Write(directory, 12, 5);
        WriteRegion(directory, 1, 1, 1, 0, 1, 4, 0, 1);
        WriteRegion(directory, 2, 2, 0, 1, 2, 0, 0, 0);
        WriteRegion(directory, 3, 3, 0, 1, 3, 0, 0, 0);
        WriteRegion(directory, 4, 4, 0, 1, 4, 0, 0, 0);
        var handlerOffset = 16 + (5 * 16);
        Write(directory, handlerOffset, 1);
        directory[handlerOffset + 2] = 0;
        directory[handlerOffset + 8] = 1;
        Write(directory, handlerOffset + 10, 2);
        Write(directory, handlerOffset + 12, 3);
        var blockOwnerOffset = handlerOffset + 20;
        for (var index = 0; index < 5; index++)
        {
            Write(directory, blockOwnerOffset + (index * sizeof(ushort)),
                checked((ushort)index));
        }

        var records = new ReadOnlyMemory<byte>[]
        {
            directory,
            BlockEntryState(),
            BlockEntryState(),
            BlockEntryState(),
            BlockEntryState(),
            BlockEntryState(),
            RegionExitRecord(1, 1, 0, 0, 0, 0, 1),
            RegionExitRecord(3, 0, 0, 1, 1, 0, 2),
            RegionExitRecord(2, 0, 0, 0, 0, 0, 3, 1),
            RegionExitRecord(4, 0, 0, 0, 0, 0, 4),
        };
        var blocks = new[]
        {
            new TrRmRegisterBodyBlock(0, 2, 1),
            new TrRmRegisterBodyBlock(2, 2, 2),
            new TrRmRegisterBodyBlock(4, 2, 3),
            new TrRmRegisterBodyBlock(6, 2, 4),
            new TrRmRegisterBodyBlock(8, 2, 5),
        };

        _ = TrRmRegisterBodyWriter.Encode(ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, blocks,
            records, new byte[]
            {
                0xB0, 1,
                0xB6, 6,
                0xB6, 7,
                0xB6, 8,
                0xB6, 9,
            }, schema: TrRmArtifactSchema.RegisterExceptionRegionsDraft);
    }

    private static byte[] CreateRootAndProtectedDirectory(int blockCount,
        ushort[] blockRegions)
    {
        var bytes = new byte[16 + (2 * 16) + (blockCount * sizeof(ushort))];
        bytes[0] = 0x0F;
        Write(bytes, 4, 2);
        Write(bytes, 12, checked((uint)blockCount));
        bytes[32] = 1;
        Write(bytes, 32 + 4, 1);
        for (var index = 0; index < blockRegions.Length; index++)
        {
            Write(bytes, 48 + (index * sizeof(ushort)), blockRegions[index]);
        }

        return bytes;
    }

    private static byte[] BlockEntryState()
    {
        var bytes = new byte[8];
        bytes[0] = 0x01;
        return bytes;
    }

    private static void WriteRegion(byte[] bytes, int regionIndex,
        byte kind, byte flags, ushort parent, ushort entry, ushort finallyRegion,
        uint handlerFirst, uint handlerCount)
    {
        var offset = 16 + (regionIndex * 16);
        bytes[offset] = kind;
        bytes[offset + 1] = flags;
        Write(bytes, offset + 2, parent);
        Write(bytes, offset + 4, entry);
        Write(bytes, offset + 6, finallyRegion);
        Write(bytes, offset + 8, handlerFirst);
        Write(bytes, offset + 12, handlerCount);
    }

    private static byte[] RegionExitRecord(byte exitKind, byte hasTarget,
        ushort targetBlock, byte valueKind, byte valueUse, ushort valueIndex,
        params ushort[] regions)
    {
        var bytes = new byte[16 + (regions.Length * sizeof(ushort))];
        bytes[0] = 0x0A;
        bytes[4] = exitKind;
        bytes[5] = hasTarget;
        Write(bytes, 6, targetBlock);
        bytes[8] = valueKind;
        bytes[9] = valueUse;
        Write(bytes, 10, valueIndex);
        Write(bytes, 12, checked((uint)regions.Length));
        for (var index = 0; index < regions.Length; index++)
        {
            Write(bytes, 16 + (index * sizeof(ushort)), regions[index]);
        }

        return bytes;
    }

    private static byte[] LeaveRecord(ushort targetBlock, ushort region)
    {
        var bytes = new byte[18];
        bytes[0] = 0x0A;
        bytes[4] = 1;
        bytes[5] = 1;
        Write(bytes, 6, targetBlock);
        Write(bytes, 12, 1);
        Write(bytes, 16, region);
        return bytes;
    }

    private static byte[] CreateRootOnlyDirectory()
    {
        var bytes = new byte[34];
        bytes[0] = 0x0F;
        Write(bytes, 4, 1);
        Write(bytes, 12, 1);
        return bytes;
    }

    private static void Write(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
