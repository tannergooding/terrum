// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmContractTableReaderTests
{
    [Test]
    public void DistinctTypeAndMethodRangesOwnAssignedClauses()
    {
        Create(Section(Cleanup(), Effects()));
        Create([], recordCount: 0, declaredCount: 0,
            typeFirst: 0, typeCount: 0, methodFirst: 0, methodCount: 0);
    }
    [TestCase(0,(byte)(0))]
    [TestCase(3,(byte)(0))]
    [TestCase(4,(byte)(0))]
    [TestCase(4,(byte)(6))]
    [TestCase(6,(byte)(1))]
    [TestCase(8,(byte)(0))]
    [TestCase(8,(byte)(2))]
    public void RejectsMalformedCleanupReferenceKindAndFlags(int offset, byte replacement)
    {
        var row = Cleanup();
        row[offset] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(Section(row, Effects())));
    }

    [Test]
    public void EffectsMaskRejectsUnassignedBitsAndDuplicateSingletons()
    {
        var effects = Effects();
        effects[8] = 8;
        Assert.Throws<InvalidDataException>(() => Create(Section(Cleanup(), effects)));

        Assert.Throws<InvalidDataException>(() => Create(Section(Effects(), Effects()),
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2));
    }

    [Test]
    public void CleanupRequiresDeclaredNoThrowEffectOnItsTargetMethod()
    {
        var withoutNoThrow = Effects();
        withoutNoThrow[8] = 2;
        Assert.Throws<InvalidDataException>(() =>
            Create(Section(Cleanup(), withoutNoThrow)));
        Assert.Throws<InvalidDataException>(() =>
            Create(Section(Cleanup()), recordCount: 1, declaredCount: 1,
                methodFirst: 0, methodCount: 0));
        Create(Section(Effects(), Cleanup()), typeFirst: 2, methodFirst: 1);
    }

    [Test]
    public void RejectsOverlapsOrphansAndMismatchedDeclaredCounts()
    {
        var rows = Section(Cleanup(), Effects());
        Assert.Throws<InvalidDataException>(() => Create(rows,
            typeFirst: 1, typeCount: 2, methodFirst: 2, methodCount: 1));
        Assert.Throws<InvalidDataException>(() => Create(Section(Cleanup(), Effects(), Effects()),
            recordCount: 3, declaredCount: 3));
        Assert.Throws<InvalidDataException>(() => Create(rows, declaredCount: 3));
        Assert.Throws<InvalidDataException>(() => Create(rows[..^1]));
    }

    [Test]
    public void AssignedButNonexecutableClauseShapesAreBounded()
    {
        var output = Contract(0x1600_0001, 2, [0, 0, 0, 0, 1, 0, 0, 0]);
        var transfer = Contract(0x1600_0001, 3, [0, 0, 0, 0, 0, 0, 0, 0]);
        var dependency = Contract(0x1600_0001, 4,
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Create(Section(output), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true);
        Create(Section(transfer), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1);
        Create(Section(dependency), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1);

        output[12] = 2;
        Assert.Throws<InvalidDataException>(() => Create(Section(output),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true));
        transfer[15] = 1;
        Assert.Throws<InvalidDataException>(() => Create(Section(transfer),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1));
        Assert.Throws<InvalidDataException>(() => Create(Section(Contract(0x1600_0001, 6, [])),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1));
    }

    [Test]
    public void OutputStateRequiresBooleanResultAndAnOutputParameterOrdinal()
    {
        var output = Contract(0x1600_0001, 2, [0, 0, 0, 0, 1, 0, 0, 0]);
        var section = Section(output);
        Create(section, recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 2);
        Assert.Throws<InvalidDataException>(() => Create(section,
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1));
        Assert.Throws<InvalidDataException>(() => Create(section,
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, paramMode: 0));
        Assert.Throws<InvalidDataException>(() => Create(section,
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, nonBooleanResult: true));

        output[8] = 1;
        Assert.Throws<InvalidDataException>(() => Create(Section(output),
            recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true));
    }

    [Test]
    public void NativeComInterfaceRequiresUniqueIidAndValidBase()
    {
        Create(Section(NativeComInterface(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"))),
            recordCount: 1, declaredCount: 1,
            typeFirst: 1, typeCount: 1, methodFirst: 0, methodCount: 0,
            typeFlags: 12);
        Assert.Throws<InvalidDataException>(() => Create(
            Section(NativeComInterface(Guid.Empty)), recordCount: 1, declaredCount: 1,
            typeFirst: 1, typeCount: 1, methodFirst: 0, methodCount: 0,
            typeFlags: 12));
        Assert.Throws<InvalidDataException>(() => Create(
            Section(NativeComInterface(Guid.NewGuid(), 2)), recordCount: 1, declaredCount: 1,
            typeFirst: 1, typeCount: 1, methodFirst: 0, methodCount: 0,
            typeFlags: 12));
        Assert.Throws<InvalidDataException>(() => Create(
            Section(NativeComInterface(Guid.NewGuid())), recordCount: 1, declaredCount: 1,
            typeFirst: 1, typeCount: 1, methodFirst: 0, methodCount: 0));
    }

    [Test]
    public void NativeComMethodRequiresUnsafeOwnerInterfaceAndRepresentableSlot()
    {
        var interfaceContract = NativeComInterface(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
        var methodContract = NativeComMethod(slot: 0, effects: 0);
        Create(Section(interfaceContract, Effects(4), methodContract),
            recordCount: 3, declaredCount: 3, typeFirst: 1, typeCount: 1,
            methodFirst: 2, methodCount: 2, methodOwnerType: 1, typeFlags: 12);
        Assert.Throws<InvalidDataException>(() => Create(
            Section(interfaceContract, Effects(0), methodContract),
            recordCount: 3, declaredCount: 3, typeFirst: 1, typeCount: 1,
            methodFirst: 2, methodCount: 2, methodOwnerType: 1, typeFlags: 12));
        Create(Section(interfaceContract, Effects(4), NativeComMethod(slot: 5, effects: 0)),
            recordCount: 3, declaredCount: 3, typeFirst: 1, typeCount: 1,
            methodFirst: 2, methodCount: 2, methodOwnerType: 1, typeFlags: 12);
        Assert.Throws<InvalidDataException>(() => Create(
            Section(interfaceContract, Effects(4), NativeComMethod(
                slot: (uint)(int.MaxValue / NativeTargetProfile.PointerSize + 1), effects: 0)),
            recordCount: 3, declaredCount: 3, typeFirst: 1, typeCount: 1,
            methodFirst: 2, methodCount: 2, methodOwnerType: 1, typeFlags: 12));
        Assert.Throws<InvalidDataException>(() => Create(
            Section(interfaceContract, Effects(4), methodContract),
            recordCount: 3, declaredCount: 3, typeFirst: 1, typeCount: 1,
            methodFirst: 2, methodCount: 2, methodOwnerType: 0, typeFlags: 12));
    }

    [Test]
    public void NativeComOwnerRequiresDeclaredInterfaceAndNoThrowCleanup()
    {
        Create(Section(
                NativeComInterface(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")),
                NativeComOwner(1, 2), Cleanup(2), Effects()),
            recordCount: 4, declaredCount: 4,
            typeFirst: 1, typeCount: 1, methodFirst: 4, methodCount: 1,
            typeFlags: 12, typeRows: 2, secondTypeFirst: 2, secondTypeCount: 2,
            methodOwnerType: 2);

        Assert.Throws<InvalidDataException>(() => Create(Section(
                NativeComInterface(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")),
                NativeComOwner(2, 2), Cleanup(2), Effects()),
            recordCount: 4, declaredCount: 4,
            typeFirst: 1, typeCount: 1, methodFirst: 4, methodCount: 1,
            typeFlags: 12, typeRows: 2, secondTypeFirst: 2, secondTypeCount: 2,
            methodOwnerType: 2));
        Assert.Throws<InvalidDataException>(() => Create(Section(
                NativeComInterface(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")),
                NativeComOwner(1, 2), Effects()),
            recordCount: 3, declaredCount: 3,
            typeFirst: 1, typeCount: 1, methodFirst: 3, methodCount: 1,
            typeFlags: 12, typeRows: 2, secondTypeFirst: 2, secondTypeCount: 1,
            methodOwnerType: 2));
    }

    [Test]
    public void NativeParameterRequiresAnAddressParameterAndAssignedAccess()
    {
        var contract = NativeParameter(ordinal: 0, access: 3, retention: 0, extent: 0);
        Create(Section(contract), recordCount: 1, declaredCount: 1,
            typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 1,
            hasOutputParam: true, pointerParameter: true);
        Assert.Throws<InvalidDataException>(() => Create(Section(contract),
            recordCount: 1, declaredCount: 1, typeFirst: 0, typeCount: 0,
            methodFirst: 1, methodCount: 1, hasOutputParam: true));
        Assert.Throws<InvalidDataException>(() => Create(
            Section(NativeParameter(ordinal: 0, access: 0, retention: 0, extent: 0)),
            recordCount: 1, declaredCount: 1, typeFirst: 0, typeCount: 0,
            methodFirst: 1, methodCount: 1, hasOutputParam: true, pointerParameter: true));
    }

    [Test]
    public void WrittenClausesComposeAcrossTypeAndMethodInFramedArtifact()
    {
        Create(Section(Cleanup(), Effects()), writeRows: true, frameArtifact: true);
        var missingNoThrow = Effects();
        missingNoThrow[8] = 2;
        Assert.Throws<InvalidDataException>(() =>
            Create(Section(Cleanup(), missingNoThrow), writeRows: true));
        var invalidKind = Effects();
        invalidKind[4] = 6;
        Assert.Throws<InvalidDataException>(() =>
            Create(Section(Cleanup(), invalidKind), writeRows: true));
        Assert.Throws<InvalidDataException>(() =>
            Create(Section(Effects(), Effects()),
                typeFirst: 0, typeCount: 0, methodFirst: 1, methodCount: 2,
                writeRows: true));
    }

    private static void Create(byte[] section, int recordCount = 2, int declaredCount = 2,
        uint typeFirst = 1, uint typeCount = 1, uint methodFirst = 2, uint methodCount = 1,
        bool hasOutputParam = false, byte paramMode = 1, bool nonBooleanResult = false,
        bool writeRows = false, bool frameArtifact = false,
        bool pointerParameter = false, int methodOwnerType = 1, uint typeFlags = 4,
        int typeRows = 1, uint secondTypeFirst = 0, uint secondTypeCount = 0)
    {
        var nameBytes = TrRmStringTableWriter.EncodeTextRows(
            [("", 0), ("Module", 0), ("Widget", 0), ("Run", 0), ("Owner", 0)]);
        var names = new TrRmStringTableReader(nameBytes, 5);
        var module = new byte[44];
        Write(module, 0, 2);
        Write(module, 4, 1);
        Write(module, 28, 1);
        Write(module, 32, 1);
        Write(module, 36, 1);
        Write(module, 40, 1);
        var modules = new TrRmModuleTableReader(module, 1, names, 0, typeRows, 1, 0);
        var type = new byte[typeRows * 40];
        for (var index = 0; index < typeRows; index++)
        {
            var offset = index * 40;
            Write(type, offset, 1);
            Write(type, offset + 4, index == 0 ? 3u : 4u);
            Write(type, offset + 8, index == 0 ? typeFlags : 4u);
            Write(type, offset + 32, index == 0 ? typeFirst : secondTypeFirst);
            Write(type, offset + 36, index == 0 ? typeCount : secondTypeCount);
        }
        var types = new TrRmTypeDefTableReader(type, typeRows, names, modules,
            0, 0, 0, declaredCount);
        byte[] spec = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        if (pointerParameter)
        {
            spec = [0, 0, 0, 0, 4, 0, 0, 0, 1, 1, 0x8C, 8];
        }
        else if (hasOutputParam && !nonBooleanResult)
        {
            spec[^1] = 1;
        }

        var typeSpecs = new TrRmTypeSpecTableReader(spec, 1, 1, 0, 0, 0);
        var signature = new byte[24];
        Write(signature, 0, 1);
        if (hasOutputParam)
        {
            Write(signature, 8, 1);
            Write(signature, 12, 1);
        }

        var signatures = new TrRmMethodSigTableReader(signature, 1, typeSpecs,
            hasOutputParam ? 1 : 0, 0);
        var param = hasOutputParam ? new byte[16] : [];
        if (hasOutputParam)
        {
            Write(param, 0, 1);
            Write(param, 8, 1);
            param[12] = paramMode;
        }

        var parameters = hasOutputParam
            ? new TrRmParamTableReader(param, 1, signatures, typeSpecs) : default;
        var method = new byte[28];
        Write(method, 0, (uint)methodOwnerType);
        Write(method, 4, 4);
        Write(method, 8, 1);
        Write(method, 16, methodFirst);
        Write(method, 20, methodCount);
        var methods = new TrRmMethodDefTableReader(method, 1, names, modules,
            types, signatures, declaredCount, 0);
        if (writeRows)
        {
            var index = new TrRmVariableTableIndex(section, recordCount, 8, 1);
            var rows = new TrRmContractRow[recordCount];
            for (var row = 1; row <= recordCount; row++)
            {
                var data = index.GetRow(row);
                rows[row - 1] = new TrRmContractRow(
                    BinaryPrimitives.ReadUInt32LittleEndian(data),
                    BinaryPrimitives.ReadUInt16LittleEndian(data[4..]),
                    data[8..].ToArray());
            }

            var emitted = TrRmContractTableWriter.Encode(
                rows, types, methods, signatures, parameters, typeSpecs);
            Assert.That(emitted, Is.EqualTo(section));
            section = emitted;
        }
        _ = new TrRmContractTableReader(section, recordCount,
            types, methods, signatures, parameters, typeSpecs);
        if (frameArtifact)
        {
            var artifact = TrRmArtifactWriter.Encode([
                new TrRmSection(0x0001, 0, 1, module),
                new TrRmSection(0x0002, 0, 5, nameBytes),
                new TrRmSection(0x0011, 0, 1, spec),
                new TrRmSection(0x0013, 0, (uint)typeRows, type),
                new TrRmSection(0x0016, 0, 1, method),
                new TrRmSection(0x0017, 0, 1, signature),
                new TrRmSection(0x001B, 0, (uint)recordCount, section),
            ]);
            TrRmStructuralMetadataValidator.Validate(artifact);
        }
    }

    private static byte[] Cleanup(uint typeRow = 1)
    {
        var data = new byte[4];
        Write(data, 0, 1);
        return Contract(0x1300_0000u | typeRow, 5, data);
    }

    private static byte[] Effects()
        => Effects(1);

    private static byte[] Effects(uint mask)
    {
        var data = new byte[4];
        Write(data, 0, mask);
        return Contract(0x1600_0001, 1, data);
    }

    private static byte[] NativeComInterface(Guid iid, uint baseTypeDefRow = 0)
    {
        var data = new byte[20];
        _ = iid.TryWriteBytes(data.AsSpan(0, 16), bigEndian: true, out _);
        Write(data, 16, baseTypeDefRow);
        return Contract(0x1300_0001, 7, data);
    }

    private static byte[] NativeComMethod(uint slot, uint effects)
    {
        var data = new byte[8];
        Write(data, 0, slot);
        Write(data, 4, effects);
        return Contract(0x1600_0001, 8, data);
    }

    private static byte[] NativeComOwner(uint interfaceTypeDefRow, uint ownerTypeDefRow = 1)
    {
        var data = new byte[4];
        Write(data, 0, interfaceTypeDefRow);
        return Contract(0x1300_0000u | ownerTypeDefRow, 9, data);
    }

    private static byte[] NativeParameter(uint ordinal, byte access, byte retention, uint extent)
    {
        var data = new byte[12];
        Write(data, 0, ordinal);
        data[4] = access;
        data[5] = retention;
        Write(data, 8, extent);
        return Contract(0x1600_0001, 10, data);
    }

    private static byte[] Contract(uint owner, ushort kind, byte[] data)
    {
        var row = new byte[8 + data.Length];
        Write(row, 0, owner);
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(4), kind);
        data.CopyTo(row.AsSpan(8));
        return row;
    }

    private static byte[] Section(params byte[][] rows)
    {
        var indexBytes = (rows.Length + 1) * sizeof(uint);
        var dataBytes = 0;
        foreach (var row in rows)
        {
            dataBytes += row.Length;
        }

        var section = new byte[indexBytes + dataBytes];
        var offset = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            Write(section, index * sizeof(uint), (uint)offset);
            rows[index].CopyTo(section.AsSpan(indexBytes + offset));
            offset += rows[index].Length;
        }

        Write(section, rows.Length * sizeof(uint), (uint)offset);
        return section;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
