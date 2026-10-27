// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmRegisterBodyNativeAddressTests
{
    [Test]
    public void AcceptsRegisterHomeAndDiscardNativeAddressResults()
    {
        _ = new TrRmRegisterBodyReader(CreateBody(
            [0x0D, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0],
            [0xE9, 0, 1, 0, 0x17, 0]));
        _ = new TrRmRegisterBodyReader(CreateBody(
            [0x0D, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0],
            [0xE9, 0, 1, 0, 0x17, 0], mapMask: 0x07));
        _ = new TrRmRegisterBodyReader(CreateBody(
            [0x0D, 0, 0, 0, 1, 0, 15, 0, 0, 0, 0, 0],
            [0xE9, 0, 1, 0, 0x17, 0]));
    }

    [Test]
    public void RejectsMalformedNativeAddressRecordsAndReferences()
    {
        var validRecord = new byte[] {
            0x0D, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0,
        };
        AssertInvalid(CreateBody(validRecord, [0xE9, 1, 1, 0, 0x17, 0]));
        AssertInvalid(CreateBody(validRecord, [0xE9, 0, 0, 0, 0x17, 0]));
        AssertInvalid(CreateBody(validRecord, [0xE9, 0, 2, 0, 0x17, 0]));

        var invalidResultKind = (byte[])validRecord.Clone();
        invalidResultKind[4] = 0;
        AssertInvalid(CreateBody(invalidResultKind, [0xE9, 0, 1, 0, 0x17, 0]));

        var reservedResultFlags = (byte[])validRecord.Clone();
        reservedResultFlags[5] = 1;
        AssertInvalid(CreateBody(reservedResultFlags, [0xE9, 0, 1, 0, 0x17, 0]));

        var invalidRegister = (byte[])validRecord.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(invalidRegister.AsSpan(6), 14);
        AssertInvalid(CreateBody(invalidRegister, [0xE9, 0, 1, 0, 0x17, 0]));

        var invalidHome = (byte[])validRecord.Clone();
        invalidHome[4] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(invalidHome.AsSpan(6), 1);
        AssertInvalid(CreateBody(invalidHome, [0xE9, 0, 1, 0, 0x17, 0],
            mapMask: 0x07));

        var invalidMethodMapIndex = (byte[])validRecord.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(invalidMethodMapIndex.AsSpan(8), 1);
        AssertInvalid(CreateBody(invalidMethodMapIndex, [0xE9, 0, 1, 0, 0x17, 0]));

        var invalidTypeMapIndex = (byte[])validRecord.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(invalidTypeMapIndex.AsSpan(10), 1);
        AssertInvalid(CreateBody(invalidTypeMapIndex, [0xE9, 0, 1, 0, 0x17, 0]));

        var unassignedRecord = (byte[])validRecord.Clone();
        unassignedRecord[0] = 0x0C;
        AssertInvalid(CreateBody(unassignedRecord, [0xE9, 0, 1, 0, 0x17, 0]));
        AssertInvalid(CreateBody(validRecord[..^1], [0xE9, 0, 1, 0, 0x17, 0]));
    }

    private static byte[] CreateBody(byte[] addressRecord, byte[] code,
        byte mapMask = 0x06)
    {
        var bytes = new List<byte> { 0x01, 0x01, mapMask, 0, 0 };
        if ((mapMask & 0x01) != 0)
        {
            WriteUInt32(bytes, 1);
        }
        if ((mapMask & 0x02) != 0)
        {
            WriteUInt32(bytes, 1);
        }
        if ((mapMask & 0x04) != 0)
        {
            WriteUInt32(bytes, 1);
        }
        WriteUInt32(bytes, 1);
        WriteUInt32(bytes, 2);
        WriteUInt32(bytes, (uint)code.Length);

        if ((mapMask & 0x01) != 0)
        {
            bytes.AddRange([3, 0, 0, 0, 0, 0, 0, 0]);
        }
        if ((mapMask & 0x02) != 0)
        {
            WriteUInt32(bytes, 0x1100_0001);
        }
        if ((mapMask & 0x04) != 0)
        {
            WriteUInt32(bytes, 0x1600_0001);
        }

        WriteUInt32(bytes, 0);
        WriteUInt32(bytes, (uint)code.Length);
        WriteUInt16(bytes, 0);
        WriteUInt16(bytes, 0);
        WriteUInt32(bytes, 0);
        WriteUInt32(bytes, 8);
        WriteUInt32(bytes, 20);
        bytes.AddRange([0x01, 0, 0, 0, 0, 0, 0, 0]);
        bytes.AddRange(addressRecord);
        bytes.AddRange(code);
        return [.. bytes];
    }

    private static void AssertInvalid(byte[] body)
        => Assert.Throws<InvalidDataException>(() => _ = new TrRmRegisterBodyReader(body));

    private static void WriteUInt16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }

    private static void WriteUInt32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 24));
    }
}
