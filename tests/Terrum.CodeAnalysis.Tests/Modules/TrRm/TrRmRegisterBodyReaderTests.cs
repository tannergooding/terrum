// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmRegisterBodyReaderTests
{
    [TestCase(0x00)]
    [TestCase(0x01)]
    [TestCase(0x02)]
    [TestCase(0x03)]
    [TestCase(0x04)]
    [TestCase(0x05)]
    [TestCase(0x06)]
    [TestCase(0x07)]
    [TestCase(0x08)]
    [TestCase(0x09)]
    [TestCase(0x0A)]
    [TestCase(0x0B)]
    [TestCase(0x0C)]
    [TestCase(0x0D)]
    [TestCase(0x0E)]
    [TestCase(0x0F)]
    [TestCase(0x10)]
    [TestCase(0x11)]
    [TestCase(0x12)]
    [TestCase(0x13)]
    [TestCase(0x14)]
    [TestCase(0x15)]
    [TestCase(0x16)]
    [TestCase(0x17)]
    [TestCase(0x18)]
    [TestCase(0x19)]
    [TestCase(0x1A)]
    [TestCase(0x1B)]
    [TestCase(0x1C)]
    [TestCase(0x1D)]
    [TestCase(0x1E)]
    [TestCase(0x1F)]
    public void AcceptsEveryMapMaskIncludingEmptyPresentMaps(byte mapMask)
    {
        var body = CreateBody(mapMask);
        var reader = new TrRmRegisterBodyReader(body);

        Assert.That(reader.MapMask, Is.EqualTo(mapMask));
        Assert.That(reader.BlockCount, Is.EqualTo(1));
        Assert.That(reader.RecordCount, Is.EqualTo(1));
        Assert.That(reader.GetRecord(0).Length, Is.EqualTo(8));
        Assert.That(reader.GetBlockCode(0).ToArray(), Is.EqualTo(new byte[] { 0 }));
    }

    [Test]
    public void AcceptsAllInstructionWidthsAndMaximumMapCount()
    {
        AssertBody(new byte[] { 0x00 });
        AssertBody(new byte[] { 0x18, 0 });
        AssertBody(new byte[] { 0xE3, 0, 0, 0 });
        AssertBody(new byte[] { 0x0A, 0x17, 0 });

        var counts = new uint[5];
        counts[1] = 65_536;
        var body = CreateBody(0x02, counts);
        var reader = new TrRmRegisterBodyReader(body);
        Assert.That(reader.TypeCount, Is.EqualTo(65_536));
        Assert.That(reader.TypeMap.Length, Is.EqualTo(65_536 * sizeof(uint)));
    }

    [Test]
    public void RejectsInvalidPrefixCountsAndInstructionExtents()
    {
        AssertInvalid(Mutate(CreateBody(), 0, 0x02));
        AssertInvalid(Mutate(CreateBody(), 1, 0x02));
        AssertInvalid(Mutate(CreateBody(), 2, 0x20));
        AssertInvalid(Mutate(CreateBody(), 3, 1));
        AssertInvalid(Mutate(CreateBody(), 5, 0));
        AssertInvalid(Mutate(CreateBody(), 5, 65_537));
        AssertInvalid(Mutate(CreateBody(), 9, 65_537));
        AssertInvalid(Mutate(CreateBody(), 13, 0));
        AssertInvalid(CreateBody(0, code: [0x10]));
        AssertInvalid(CreateBody(0, code: [0xCC, 0, 0, 0]));
        AssertInvalid(CreateBody(0, code: [0x00, 0]));
        AssertInvalid(CreateBody(0, code: [0xB3, 0]));
        AssertInvalid(MutateByte(CreateBody(), 45, 0x01));
    }

    [Test]
    public void RejectsMalformedBlockAndTypedRecordDirectories()
    {
        var body = CreateBody();
        AssertInvalid(Mutate(body, 17, 1));
        AssertInvalid(Mutate(body, 21, 0));
        AssertInvalid(Mutate(body, 25, 1));
        AssertInvalid(Mutate(body, 29, 1));
        AssertInvalid(Mutate(body, 33, 7));
        AssertInvalid(Mutate(body, 37, 2));
        AssertInvalid(Mutate(body, 38, 1));
        AssertInvalid(Mutate(body, 39, 1));
        AssertInvalid(MutateByte(body, 45, 1));
    }

    [Test]
    public void ValidatesRegisterEntryStateCarriersAndTypeIndexes()
    {
        var counts = new uint[5];
        counts[1] = 1;
        var valueState = new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 };
        _ = new TrRmRegisterBodyReader(
            CreateBodyWithRecord(valueState, 0x02, counts));

        var identityState = new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 0, 2, 0, 0, 0 };
        _ = new TrRmRegisterBodyReader(CreateBodyWithRecord(identityState));

        var invalidRegister = (byte[])valueState.Clone();
        invalidRegister[8] = 14;
        AssertInvalid(CreateBodyWithRecord(invalidRegister, 0x02, counts));
        var invalidCarrier = (byte[])valueState.Clone();
        invalidCarrier[9] = 3;
        AssertInvalid(CreateBodyWithRecord(invalidCarrier, 0x02, counts));
        var invalidAccess = (byte[])valueState.Clone();
        invalidAccess[12] = 1;
        AssertInvalid(CreateBodyWithRecord(invalidAccess, 0x02, counts));
        var invalidTypeMapIndex = (byte[])valueState.Clone();
        invalidTypeMapIndex[10] = 1;
        AssertInvalid(CreateBodyWithRecord(invalidTypeMapIndex, 0x02, counts));
    }

    [Test]
    public void ValidatesAssignedTypedRecordLayouts()
    {
        var blockState = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        AssertValidRecords([blockState]);
        AssertValidRecords([
            blockState,
            [2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ], 0x04, [0, 0, 1, 0, 0]);
        AssertValidRecords([
            blockState,
            [3, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [6, 0, 0, 0, 0, 0, 0, 0],
            [7, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ], 0x02, [0, 1, 0, 0, 0]);
        AssertValidRecords([
            blockState,
            [4, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ], 0x03, [1, 1, 0, 0, 0]);
        AssertValidRecords([
            blockState,
            [5, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ]);
        AssertValidRecords([
            blockState,
            [0x0B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [8, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0],
        ], 0x06, [0, 1, 1, 0, 0]);
        AssertValidRecords([
            blockState,
            [9, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0],
            [0x0A, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0],
        ]);
    }

    [Test]
    public void RejectsMalformedTypedRecordPayloadsAndLocalReferences()
    {
        var blockState = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        AssertInvalid(CreateBodyWithRecords([
            blockState,
            [2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ]));
        AssertInvalid(CreateBodyWithRecords([
            blockState,
            [6, 0, 0, 0, 0, 0, 1, 0],
        ]));
        AssertInvalid(CreateBodyWithRecords([
            blockState,
            [8, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0],
        ]));
        AssertInvalid(CreateBodyWithRecords([
            blockState,
            [0x0B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ]));
        AssertInvalid(CreateBodyWithRecords([
            blockState,
            [0x0A, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0],
        ]));
    }

    [Test]
    public void ValidatesCompactDescriptorPayloadsAndReferenceKinds()
    {
        ValidateImplementation(
            [0x02, 0, 0, 0, 1, 0, 0, 0x14], 0);
        ValidateImplementation(
            [0x03, 0, 0, 0, 1, 0, 0, 0x1C, 0, 0, 2, 0], 0);
        ValidateImplementation(
            [0x04, 0, 0, 0, 1, 0, 0, 0x14, 1, 0, 0, 0], 1);
        ValidateImplementation(
            [0x05, 0, 0, 0, 1, 0, 0, 0x16], 0);

        AssertInvalidImplementation([0x02, 0, 0, 0, 1, 0, 0, 0x16], 0);
        AssertInvalidImplementation([0x03, 0, 0, 0, 1, 0, 0, 0x14, 0, 0, 4, 0], 0);
        AssertInvalidImplementation([0x04, 0, 0, 0, 1, 0, 0, 0x14, 2, 0, 0, 0], 1);
        AssertInvalidImplementation([0x05, 1, 0, 0, 1, 0, 0, 0x16], 0);
    }

    [Test]
    public void StandardStructuralValidationUsesTheRegisterBodyCodec()
    {
        var artifact = CreateStandardArtifact(CreateBody());
        TrRmStructuralMetadataValidator.Validate(artifact);
        Assert.That(GetSelectedRegisterBodyFirstOpcode(artifact), Is.EqualTo((byte)0));

        var malformedBody = CreateBody();
        malformedBody[2] = 0x20;
        var malformedArtifact = CreateStandardArtifact(malformedBody);
        TrRmStructuralMetadataValidator.Validate(malformedArtifact);
        Assert.Throws<InvalidDataException>(() => GetSelectedRegisterBodyFirstOpcode(malformedArtifact));
    }

    private static void AssertBody(byte[] code)
    {
        var reader = new TrRmRegisterBodyReader(CreateBody(0, code: code));
        Assert.That(reader.GetBlockCode(0).ToArray(), Is.EqualTo(code));
    }

    private static void AssertInvalid(byte[] body)
    {
        Assert.Throws<InvalidDataException>(() => ReadRegisterBody(body));
    }

    private static void ReadRegisterBody(byte[] body)
        => _ = new TrRmRegisterBodyReader(body);

    private static void AssertValidRecords(byte[][] records,
        byte mapMask = 0, uint[]? counts = null)
        => _ = new TrRmRegisterBodyReader(CreateBodyWithRecords(records, mapMask, counts));

    private static void ValidateImplementation(byte[] body, int contractCount)
    {
        var section = TrRmVariableTableWriter.EncodeBodies([body]);
        var bodies = new TrRmBodyTableReader(section, 1);
        bodies.ValidateSelectedImplementation(1, contractCount);
    }

    private static byte GetSelectedRegisterBodyFirstOpcode(byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        if (!index.TryGetSection(0x0020, out var section, out var count))
        {
            throw new InvalidDataException("Body section is absent.");
        }

        var bodies = new TrRmBodyTableReader(section, count);
        return bodies.GetRegisterBody(1).GetBlockCode(0)[0];
    }

    private static void AssertInvalidImplementation(byte[] body, int contractCount)
        => Assert.Throws<InvalidDataException>(() =>
            ValidateImplementation(body, contractCount));

    private static byte[] CreateBody(byte mapMask = 0, uint[]? mapCounts = null,
        byte[]? code = null)
        => CreateBodyWithRecord([0x01, 0, 0, 0, 0, 0, 0, 0],
            mapMask, mapCounts, code);

    private static byte[] CreateBodyWithRecord(byte[] record, byte mapMask = 0,
        uint[]? mapCounts = null, byte[]? code = null)
        => CreateBodyWithRecords([record], mapMask, mapCounts, code);

    private static byte[] CreateBodyWithRecords(byte[][] records, byte mapMask = 0,
        uint[]? mapCounts = null, byte[]? code = null)
    {
        var counts = mapCounts ?? new uint[5];
        if (counts.Length != 5 || records.Length == 0
            || records[0].Length == 0 || records[0][0] != 0x01)
        {
            throw new ArgumentException("Five map counts and a leading entry-state record are required.");
        }

        var instructions = code ?? [0x00];
        var bytes = new List<byte>(128);
        bytes.Add(0x01);
        bytes.Add(0x01);
        bytes.Add(mapMask);
        bytes.Add(0);
        bytes.Add(0);
        for (var map = 0; map < counts.Length; map++)
        {
            if ((mapMask & (1 << map)) != 0)
            {
                Write(bytes, counts[map]);
            }
        }

        Write(bytes, 1);
        Write(bytes, (uint)records.Length);
        Write(bytes, (uint)instructions.Length);
        for (var map = 0; map < counts.Length; map++)
        {
            if ((mapMask & (1 << map)) == 0)
            {
                continue;
            }

            for (var entry = 0; entry < counts[map]; entry++)
            {
                switch (map)
                {
                    case 0:
                        bytes.AddRange([3, 0, 0, 0, 0, 0, 0, 0]);
                        break;
                    case 1:
                        Write(bytes, 0xF2000008);
                        break;
                    case 2:
                        Write(bytes, 0x16000001);
                        break;
                    case 3:
                        Write(bytes, 0x14000001);
                        break;
                    case 4:
                        Write(bytes, 0xF2000008);
                        Write(bytes, 1);
                        break;
                }
            }
        }

        Write(bytes, 0);
        Write(bytes, (uint)instructions.Length);
        Write(bytes, 0);
        var recordOffset = 0u;
        Write(bytes, recordOffset);
        foreach (var record in records)
        {
            recordOffset += (uint)record.Length;
            Write(bytes, recordOffset);
        }
        foreach (var record in records)
        {
            bytes.AddRange(record);
        }

        bytes.AddRange(instructions);
        return bytes.ToArray();
    }

    private static byte[] CreateStandardArtifact(byte[] body)
    {
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("module", 0), ("main", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 1, 1, 1, 1, 1),
        ], strings, 0, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, 0);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(0),
        ], 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 1, 0, 0, 0);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF2000000, 0, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, 0, 0);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, 0, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 3, stringBytes),
            new TrRmSection(0x0011, 0, 1, typeSpecBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, signatureBytes),
            new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBodies([body])),
        ]);
    }

    private static byte[] Mutate(byte[] body, int offset, uint value)
    {
        var copy = (byte[])body.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static byte[] MutateByte(byte[] body, int offset, byte value)
    {
        var copy = (byte[])body.Clone();
        copy[offset] = value;
        return copy;
    }

    private static void Write(List<byte> bytes, uint value)
    {
        bytes.Add((byte)(value & 0xFF));
        bytes.Add((byte)((value >> 8) & 0xFF));
        bytes.Add((byte)((value >> 16) & 0xFF));
        bytes.Add((byte)((value >> 24) & 0xFF));
    }
}
