// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmRegisterBodyWriterTests
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
    public void FramesNativeAddressAndIndirectCallRecords()
    {
        const uint pointerTypeToken = 0x1100_0002;
        const uint targetMethodToken = 0x1600_0007;
        var maps = TrRmRegisterBodyMapBuilder.Create(
            [0x1100_0003, pointerTypeToken, pointerTypeToken],
            [0x1C00_0001, targetMethodToken, targetMethodToken]);
        var entryState = new byte[13];
        entryState[0] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(entryState.AsSpan(4), 1);
        var nativeAddressRecord = TrRmRegisterBodyRecordWriter.EncodeNativeAddress(
            1, 0, maps.GetMethodMapIndex(targetMethodToken),
            maps.GetTypeMapIndex(pointerTypeToken));
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(
            2, 0, 0, 0, [], []);
        byte[] code = [0xE9, 0, 1, 0, 0xE0, 0, 2, 0, 0x17, 0];

        var body = TrRmRegisterBodyWriter.Encode(default, maps.TypeMap,
            maps.MethodMap, default, default,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            [entryState, nativeAddressRecord, callSite], code,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);

        var reader = new TrRmRegisterBodyReader(body,
            schema: TrRmArtifactSchema.ForeignInvocationDraft);
        Assert.That(reader.MapMask, Is.EqualTo((byte)0x06));
        Assert.That(reader.RecordCount, Is.EqualTo(3));
        Assert.That(reader.GetRecord(1)[0], Is.EqualTo((byte)0x0D));
        Assert.That(reader.GetRecord(2)[0], Is.EqualTo((byte)0x02));
        Assert.That(reader.GetBlockCode(0).ToArray(), Is.EqualTo(code));
    }

    [Test]
    public void TypedRecordsRejectInvalidResultAndInputSelectors()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeNativeAddress(1, 14, 0, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeCallSite(2, 0, 14, 0, [], []));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeCallSite(2, 0, 0, 0,
                [new TrRmRegisterBodyCallInput(0, 1, 0)], []));
    }

    [Test]
    public void InitializationReferencePlaceSelectionFlagsAreScoped()
    {
        var standard = TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
            1, 3, 1, 0, 0, 0, 0, 0, 0, 0, 0,
            TrRmArtifactSchema.Standard, flags: 1);
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            standard.AsSpan(22)), Is.EqualTo((ushort)1));

        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                1, 3, 1, 0, 0, 0, 0, 0, 0, 0, 0,
                TrRmArtifactSchema.Standard, flags: 2));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                1, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0,
                TrRmArtifactSchema.Standard, flags: 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodePlaceSelection(
                1, 3, 1, 0, 0, 0, 0, 0, 0, 0, 0,
                TrRmArtifactSchema.Development, flags: 1));
    }

    [Test]
    public void SemanticStoreSupportsHomeDescriptorsAndTransferSources()
    {
        var record = TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
            2, 2, 1, 2, 0, 1, TrRmArtifactSchema.Standard);

        Assert.That(record[4..8], Is.EqualTo(new byte[] { 2, 2, 1, 2 }));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                2, 2, 1, 3, 0, 1, TrRmArtifactSchema.Standard));
    }

    [Test]
    public void CallSiteWriterPreservesInputAndPreparationOrder()
    {
        TrRmRegisterBodyCallInput[] inputs =
        [
            new(2, 3, 5),
            new(1, 1, 3),
        ];
        ushort[] preparations = [6, 2];

        var record = TrRmRegisterBodyRecordWriter.EncodeCallSite(
            1, 1, 4, 0, inputs, preparations);

        Assert.That(record.Length, Is.EqualTo(32));
        Assert.That(record[4], Is.EqualTo((byte)1));
        Assert.That(record[5], Is.EqualTo((byte)1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(8)),
            Is.EqualTo((ushort)4));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(12)),
            Is.EqualTo(2u));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16)),
            Is.EqualTo(2u));
        Assert.That(record[20..24], Is.EqualTo(new byte[] { 2, 3, 5, 0 }));
        Assert.That(record[24..28], Is.EqualTo(new byte[] { 1, 1, 3, 0 }));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(28)),
            Is.EqualTo((ushort)6));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(30)),
            Is.EqualTo((ushort)2));
    }

    [Test]
    public void ConstructorBindingWriterPreservesFormalOrderAndZeroSuppliedShape()
    {
        TrRmRegisterBodyConstructorBinding[] bindings =
        [
            new(1, 1, 0x0123),
            new(2, 0, ushort.MaxValue),
        ];

        var record = TrRmRegisterBodyRecordWriter.EncodeConstructorBinding(
            2, 3, bindings);

        Assert.That(record.Length, Is.EqualTo(36));
        Assert.That(record[0], Is.EqualTo((byte)0x0B));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4)),
            Is.EqualTo((ushort)2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(6)),
            Is.EqualTo((ushort)3));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)),
            Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(12)),
            Is.EqualTo(2u));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16)),
            Is.EqualTo(2u));
        Assert.That(record[20..28], Is.EqualTo(
            new byte[] { 0, 0, 0, 0, 1, 1, 0x23, 0x01 }));
        Assert.That(record[28..36], Is.EqualTo(
            new byte[] { 1, 0, 0, 0, 2, 0, 0xFF, 0xFF }));
    }

    [Test]
    public void ConstructorBindingWriterEmitsZeroParameterPlanForBodyReader()
    {
        var methodMap = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(methodMap, 0x1600_0001);
        var typeMap = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap, 0x1300_0001);
        var entryState = new byte[8];
        entryState[0] = 0x01;
        var record = TrRmRegisterBodyRecordWriter.EncodeConstructorBinding(
            0, 0, []);
        byte[] code = [0x17, 0];

        var body = TrRmRegisterBodyWriter.Encode(default, typeMap, methodMap,
            default, default,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            [entryState, record], code);

        var reader = new TrRmRegisterBodyReader(body);
        Assert.That(reader.GetRecord(1).ToArray(), Is.EqualTo(record));
    }

    [Test]
    public void ConstructorBindingWriterRejectsUnsupportedBindingKindsAndUses()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeConstructorBinding(
                0, 0, [new(0, 1, 0)]));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeConstructorBinding(
                0, 0, [new(1, 6, 0)]));
    }

    [Test]
    public void RequirementFailureWriterEncodesBooleanAndPredicateMaskConditions()
    {
        var boolean = TrRmRegisterBodyRecordWriter.EncodeRequirementFailure(
            2, 7, 1, 15, 0);
        var predicateMask = TrRmRegisterBodyRecordWriter
            .EncodeRequirementFailure(3, 8, 2, 14, 15);

        Assert.That(boolean, Is.EqualTo(new byte[] {
            0x08, 0, 0, 0,
            2, 0, 7, 0, 1, 15, 0, 0,
        }));
        Assert.That(predicateMask, Is.EqualTo(new byte[] {
            0x08, 0, 0, 0,
            3, 0, 8, 0, 2, 14, 15, 0,
        }));
    }

    [Test]
    public void RequirementFailureWriterRejectsInvalidConditionShapes()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeRequirementFailure(
                0, 0, 0, 0, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeRequirementFailure(
                0, 0, 1, 16, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeRequirementFailure(
                0, 0, 1, 0, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyRecordWriter.EncodeRequirementFailure(
                0, 0, 2, 0, 16));
    }

    [Test]
    public void RequirementFailureMustSelectMatchingConstructorExceptionType()
    {
        var typeMap = new byte[2 * sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap, 0x1300_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(typeMap.AsSpan(sizeof(uint)),
            0x1300_0002);
        var methodMap = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(methodMap, 0x1600_0001);
        var entryState = new byte[8];
        entryState[0] = 0x01;
        var constructorBinding
            = TrRmRegisterBodyRecordWriter.EncodeConstructorBinding(
                0, 0, []);
        var matchingFailure
            = TrRmRegisterBodyRecordWriter.EncodeRequirementFailure(
                0, 1, 1, 0, 0);
        var mismatchedFailure
            = TrRmRegisterBodyRecordWriter.EncodeRequirementFailure(
                1, 1, 1, 0, 0);
        byte[] code = [0xB3, 2, 0x00];
        TrRmRegisterBodyBlock[] blocks =
            [new(0, (uint)code.Length, 0)];

        _ = new TrRmRegisterBodyReader(TrRmRegisterBodyWriter.Encode(
            default, typeMap, methodMap, default, default, blocks,
            [entryState, constructorBinding, matchingFailure], code));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmRegisterBodyWriter.Encode(default, typeMap, methodMap,
                default, default, blocks,
                [entryState, constructorBinding, mismatchedFailure], code));
    }

    [Test]
    public void TypeAndMethodMapsPreserveFirstUseOrderAndDeduplicateIndependently()
    {
        var forward = TrRmRegisterBodyMapBuilder.Create(
            [0x1100_0003, 0x1100_0002, 0x1100_0003],
            [0x1C00_0001, 0x1600_0007, 0x1600_0007]);
        var reverse = TrRmRegisterBodyMapBuilder.Create(
            [0x1100_0002, 0x1100_0003],
            [0x1600_0007, 0x1C00_0001]);

        Assert.That(forward.TypeMap.Length, Is.EqualTo(2 * sizeof(uint)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            forward.TypeMap.Span), Is.EqualTo(0x1100_0003u));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            forward.TypeMap.Span[sizeof(uint)..]), Is.EqualTo(0x1100_0002u));
        Assert.That(reverse.TypeMap.ToArray(),
            Is.Not.EqualTo(forward.TypeMap.ToArray()));
        Assert.That(forward.GetTypeMapIndex(0x1100_0003), Is.Zero);
        Assert.That(forward.GetTypeMapIndex(0x1100_0002),
            Is.EqualTo((ushort)1));
        Assert.That(reverse.GetTypeMapIndex(0x1100_0002), Is.Zero);
        Assert.That(reverse.GetTypeMapIndex(0x1100_0003),
            Is.EqualTo((ushort)1));
        Assert.That(forward.GetMethodMapIndex(0x1C00_0001), Is.Zero);
        Assert.That(forward.GetMethodMapIndex(0x1600_0007),
            Is.EqualTo((ushort)1));
        Assert.That(reverse.GetMethodMapIndex(0x1600_0007), Is.Zero);
        Assert.That(reverse.GetMethodMapIndex(0x1C00_0001),
            Is.EqualTo((ushort)1));
    }

    [Test]
    public void BodyMapsUseTheFullU16IndexDomain()
    {
        var maximum = Enumerable.Range(0, 65_536)
            .Select(static index => (uint)index).ToArray();
        var maps = TrRmRegisterBodyMapBuilder.Create(maximum, []);

        Assert.That(maps.TypeMap.Length, Is.EqualTo(65_536 * sizeof(uint)));
        Assert.That(maps.GetTypeMapIndex(65_535), Is.EqualTo(ushort.MaxValue));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrRmRegisterBodyMapBuilder.Create(
                Enumerable.Range(0, 65_537).Select(static index => (uint)index), []));
    }

    [Test]
    public void RejectsMalformedMapRowsAndNoncontiguousBlocks()
    {
        var entryState = new byte[8];
        entryState[0] = 0x01;
        byte[] code = [0x17, 0x0F];

        _ = Assert.Throws<ArgumentException>(() => TrRmRegisterBodyWriter.Encode(
            default, new byte[3], default, default, default,
            [new TrRmRegisterBodyBlock(0, 2, 0)], [entryState], code));
        _ = Assert.Throws<ArgumentException>(() => TrRmRegisterBodyWriter.Encode(
            default, default, default, default, default,
            [new TrRmRegisterBodyBlock(1, 1, 0)], [entryState], code));
    }
}
