// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmFrozenValueNodeTableTests
{
    [TestCase(0x00, 0)]
    [TestCase(0x01, 1)]
    [TestCase(0x02, 1)]
    [TestCase(0x03, 2)]
    [TestCase(0x04, 4)]
    [TestCase(0x05, 4)]
    [TestCase(0x06, 1)]
    [TestCase(0x07, 2)]
    [TestCase(0x08, 4)]
    [TestCase(0x09, 8)]
    [TestCase(0x0A, 16)]
    [TestCase(0x0B, 1)]
    [TestCase(0x0C, 2)]
    [TestCase(0x0D, 4)]
    [TestCase(0x0E, 8)]
    [TestCase(0x0F, 16)]
    [TestCase(0x12, 2)]
    [TestCase(0x13, 2)]
    [TestCase(0x14, 4)]
    [TestCase(0x15, 8)]
    [TestCase(0x16, 16)]
    [TestCase(0x17, 4)]
    [TestCase(0x18, 8)]
    [TestCase(0x19, 16)]
    public void ScalarCodecPreservesExactFixedWidthPayload(int typeCode, int width)
    {
        var payload = CreatePayload(typeCode, width);
        var table = CreateScalarTable(typeCode, payload);
        var typeSpecs = CreateTypeSpecs(typeCode);
        var blobs = CreateBlobs(table.Blobs, table.BlobCount);
        var nodes = new TrRmFrozenValueNodeTableReader(table.Nodes, 1, typeSpecs,
            table.BlobCount, 0, 0);

        Assert.That(nodes.GetKind(1), Is.EqualTo((byte)1));
        Assert.That(nodes.GetScalarPayload(1, blobs).ToArray(), Is.EqualTo(payload));
    }

    [TestCase((uint)0xD800)]
    [TestCase((uint)0x110000)]
    public void ScalarCodecRejectsInvalidRuneValues(uint value)
    {
        var payload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, value);
        var table = CreateScalarTable(0x05, payload);
        var typeSpecs = CreateTypeSpecs(0x05);
        var blobs = CreateBlobs(table.Blobs, table.BlobCount);
        var nodes = new TrRmFrozenValueNodeTableReader(table.Nodes, 1, typeSpecs,
            table.BlobCount, 0, 0);

        AssertInvalidScalar(nodes, blobs);
    }

    [Test]
    public void ScalarCodecRejectsNonBooleanByteAndWrongBlobExtent()
    {
        var boolTable = CreateScalarTable(0x01, [2]);
        var boolTypes = CreateTypeSpecs(0x01);
        var boolBlobs = CreateBlobs(boolTable.Blobs, boolTable.BlobCount);
        var boolNodes = new TrRmFrozenValueNodeTableReader(boolTable.Nodes, 1,
            boolTypes, boolTable.BlobCount, 0, 0);
        AssertInvalidScalar(boolNodes, boolBlobs);

        var wrongWidthTable = CreateScalarTable(0x08, [1, 2]);
        var intTypes = CreateTypeSpecs(0x08);
        var wrongWidthBlobs = CreateBlobs(wrongWidthTable.Blobs, wrongWidthTable.BlobCount);
        var wrongWidthNodes = new TrRmFrozenValueNodeTableReader(wrongWidthTable.Nodes,
            1, intTypes, wrongWidthTable.BlobCount, 0, 0);
        AssertInvalidScalar(wrongWidthNodes, wrongWidthBlobs);
    }

    [Test]
    public void UnitRequiresNoBlobReference()
    {
        var typeSpecs = CreateTypeSpecs(0x00);
        var blobBytes = TrRmVariableTableWriter.EncodeBlobs(
            [ReadOnlyMemory<byte>.Empty]);
        var blobs = new TrRmBlobTableReader(blobBytes, 1);
        var nodesBytes = TrRmFrozenValueNodeTableWriter.Encode(
            [new TrRmFrozenValueNodeRow(1, 1, 1, 0, 0, 0)],
            typeSpecs, 1, 0, 0);
        var nodes = new TrRmFrozenValueNodeTableReader(nodesBytes, 1,
            typeSpecs, 1, 0, 0);

        AssertInvalidScalar(nodes, blobs);
    }

    [Test]
    public void ScalarCodecRejectsNativeWidthAndNominalTypesAsUnsupported()
    {
        var nativeWidthTable = CreateScalarTable(0x10, new byte[8]);
        var nativeWidthTypes = CreateTypeSpecs(0x10);
        var nativeWidthBlobs = CreateBlobs(nativeWidthTable.Blobs, nativeWidthTable.BlobCount);
        var nativeWidthNodes = new TrRmFrozenValueNodeTableReader(nativeWidthTable.Nodes,
            1, nativeWidthTypes, nativeWidthTable.BlobCount, 0, 0);
        AssertUnsupportedScalar(nativeWidthNodes, nativeWidthBlobs);

        var nominalTypeBytes = TrRmTypeSpecTableWriter.Encode(
            [new ReadOnlyMemory<byte>(TrRmTypeSpecTableWriter.Reference(0x02, 1))],
            1, 0, 0);
        var nominalTypes = new TrRmTypeSpecTableReader(nominalTypeBytes, 1, 1, 0, 0);
        var nominalBlobRows = new ReadOnlyMemory<byte>[] { new byte[4] };
        var nominalBlobBytes = TrRmVariableTableWriter.EncodeBlobs(nominalBlobRows);
        var nominalBlobs = new TrRmBlobTableReader(nominalBlobBytes, nominalBlobRows.Length);
        var nominalNodeBytes = TrRmFrozenValueNodeTableWriter.Encode(
            [new TrRmFrozenValueNodeRow(1, 1, 1, 0, 0, 0)],
            nominalTypes, nominalBlobRows.Length, 0, 0);
        var nominalNodes = new TrRmFrozenValueNodeTableReader(nominalNodeBytes,
            1, nominalTypes, nominalBlobRows.Length, 0, 0);
        AssertUnsupportedScalar(nominalNodes, nominalBlobs);
    }

    [Test]
    public void StructuralReaderRejectsUnknownKindsReservedFieldsAndBadReferences()
    {
        var typeSpecs = CreateTypeSpecs(0x08);
        var valid = CreateScalarTable(0x08, new byte[4]);
        var blobs = CreateBlobs(valid.Blobs, valid.BlobCount);

        var unknownKind = (byte[])valid.Nodes.Clone();
        unknownKind[4] = 7;
        AssertInvalidNode(unknownKind, typeSpecs, valid.BlobCount);

        var reserved = (byte[])valid.Nodes.Clone();
        reserved[6] = 1;
        AssertInvalidNode(reserved, typeSpecs, valid.BlobCount);

        var invalidTypeReference = (byte[])valid.Nodes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidTypeReference, 2);
        AssertInvalidNode(invalidTypeReference, typeSpecs, valid.BlobCount);

        var invalidBlobReference = (byte[])valid.Nodes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidBlobReference.AsSpan(8), 2);
        AssertInvalidNode(invalidBlobReference, typeSpecs, valid.BlobCount);

        var invalidBlobTag = (byte[])valid.Nodes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidBlobTag.AsSpan(8), 0x0100_0001);
        AssertInvalidNode(invalidBlobTag, typeSpecs, valid.BlobCount);

        var invalidChildRange = (byte[])valid.Nodes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidChildRange.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(invalidChildRange.AsSpan(16), 1);
        AssertInvalidNode(invalidChildRange, typeSpecs, valid.BlobCount);

        var invalidUnionCase = (byte[])valid.Nodes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidUnionCase.AsSpan(20), 0x2100_0001);
        AssertInvalidNode(invalidUnionCase, typeSpecs, valid.BlobCount);

        Assert.That(blobs.Count, Is.EqualTo(1));
    }

    [Test]
    public void StructuralMetadataValidatorReadsFrozenValueNodeRows()
    {
        var baseArtifact = TrRmRegisterBodyTestData.StandardInt32EntryArtifact(0x0E);
        var blobBytes = TrRmVariableTableWriter.EncodeBlobs(
            [new ReadOnlyMemory<byte>([42, 0, 0, 0])]);
        var nodeBytes = TrRmFrozenValueNodeTableWriter.Encode(
            [new TrRmFrozenValueNodeRow(0xF200_0008, 1, 1, 0, 0, 0)],
            CreateTypeSpecs(0x08), 1, 0, 0);
        var valid = AddValueSections(baseArtifact, nodeBytes, blobBytes);

        Assert.DoesNotThrow(() => TrRmStructuralMetadataValidator.Validate(valid));

        nodeBytes[5] = 1;
        var malformed = AddValueSections(baseArtifact, nodeBytes, blobBytes);
        Assert.Throws<InvalidDataException>(
            () => TrRmStructuralMetadataValidator.Validate(malformed));
    }

    private static (byte[] Nodes, byte[] Blobs, int BlobCount) CreateScalarTable(
        int typeCode, byte[] payload)
    {
        var typeSpecs = CreateTypeSpecs(typeCode);
        ReadOnlyMemory<byte>[] blobRows = payload.Length == 0
            ? []
            : [new ReadOnlyMemory<byte>(payload)];
        var blobBytes = blobRows.Length == 0
            ? Array.Empty<byte>()
            : TrRmVariableTableWriter.EncodeBlobs(blobRows);
        var nodes = TrRmFrozenValueNodeTableWriter.Encode(
            [new TrRmFrozenValueNodeRow(1, 1,
                blobRows.Length == 0 ? 0u : 1u, 0, 0, 0)],
            typeSpecs, blobRows.Length, 0, 0);
        return (nodes, blobBytes, blobRows.Length);
    }

    private static TrRmTypeSpecTableReader CreateTypeSpecs(int typeCode)
    {
        var bytes = TrRmTypeSpecTableWriter.Encode(
            [new ReadOnlyMemory<byte>(TrRmTypeSpecTableWriter.Builtin((byte)typeCode))],
            0, 0, 0);
        return new TrRmTypeSpecTableReader(bytes, 1, 0, 0, 0);
    }

    private static TrRmBlobTableReader CreateBlobs(byte[] bytes, int count)
        => count == 0 ? default : new TrRmBlobTableReader(bytes, count);

    private static byte[] CreatePayload(int typeCode, int width)
    {
        var payload = new byte[width];
        Array.Fill(payload, (byte)0xA5);
        if (typeCode == 0x01)
        {
            payload[0] = 1;
        }
        else if (typeCode == 0x05)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x10_FFFF);
        }
        return payload;
    }

    private static void AssertInvalidScalar(TrRmFrozenValueNodeTableReader nodes,
        TrRmBlobTableReader blobs)
    {
        try
        {
            nodes.ValidateScalarValue(1, blobs);
            Assert.Fail("Expected an invalid scalar row.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void AssertUnsupportedScalar(TrRmFrozenValueNodeTableReader nodes,
        TrRmBlobTableReader blobs)
    {
        try
        {
            nodes.ValidateScalarValue(1, blobs);
            Assert.Fail("Expected an unsupported scalar form.");
        }
        catch (NotSupportedException)
        {
        }
    }

    private static void AssertInvalidNode(byte[] bytes,
        TrRmTypeSpecTableReader typeSpecs, int blobCount)
    {
        try
        {
            _ = new TrRmFrozenValueNodeTableReader(bytes, 1, typeSpecs,
                blobCount, 0, 0);
            Assert.Fail("Expected an invalid FrozenValueNode row.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static byte[] AddValueSections(byte[] artifact, byte[] nodeBytes,
        byte[] blobBytes)
    {
        var index = new TrRmArtifactIndex(artifact);
        var sections = new List<TrRmSection>(index.SectionCount + 2);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var sectionBytes, out var recordCount);
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount,
                sectionBytes.ToArray()));
        }
        sections.Add(new TrRmSection(0x0003, 0, 1, blobBytes));
        sections.Add(new TrRmSection(0x003F, 0, 1, nodeBytes));
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections);
    }
}
