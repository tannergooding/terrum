// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmRegisterBodyWriterTests
{
    [Test]
    public void WritesPresentMapsBlocksRecordOffsetsAndCode()
    {
        var typeMap = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap, 0x1100_0001);
        var entryState = new byte[13];
        entryState[0] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(entryState.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(entryState.AsSpan(10), 0);
        byte[] code = [0x17, 0];

        var body = TrRmRegisterBodyWriter.Encode(default, typeMap, default,
            default, default, [new TrRmRegisterBodyBlock(0, 2, 0)],
            [entryState], code);

        var reader = new TrRmRegisterBodyReader(body);
        Assert.That(reader.MapMask, Is.EqualTo((byte)0x02));
        Assert.That(reader.TypeCount, Is.EqualTo(1));
        Assert.That(reader.BlockCount, Is.EqualTo(1));
        Assert.That(reader.RecordCount, Is.EqualTo(1));
        Assert.That(reader.GetRecord(0).ToArray(), Is.EqualTo(entryState));
        Assert.That(reader.GetBlockCode(0).ToArray(), Is.EqualTo(code));
    }

    [Test]
    public void ClearsEveryAbsentMapBit()
    {
        var entryState = new byte[8];
        entryState[0] = 0x01;
        byte[] code = [0x17, 0x0F];

        var body = TrRmRegisterBodyWriter.Encode(default, default, default,
            default, default, [new TrRmRegisterBodyBlock(0, 2, 0)],
            [entryState], code);

        Assert.That(body[2], Is.Zero);
        _ = new TrRmRegisterBodyReader(body);
    }

    [Test]
    public void RejectsMalformedMapRowsAndNoncontiguousBlocks()
    {
        var entryState = new byte[8];
        entryState[0] = 0x01;
        byte[] code = [0x17, 0x0F];

        Assert.Throws<ArgumentException>(() => TrRmRegisterBodyWriter.Encode(
            default, new byte[3], default, default, default,
            [new TrRmRegisterBodyBlock(0, 2, 0)], [entryState], code));
        Assert.Throws<ArgumentException>(() => TrRmRegisterBodyWriter.Encode(
            default, default, default, default, default,
            [new TrRmRegisterBodyBlock(1, 1, 0)], [entryState], code));
    }
}
