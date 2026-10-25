using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmConstantSourceValueBindingTests
{
    [Test]
    public void SelectedConstantMapBindsExactFrozenScalarTypeAndPayload()
    {
        var artifact = CreateArtifact(includeFeature11: true, includeConstant: true,
            mapTypeSpecRef: 0xF200_0008, nodeTypeSpecRef: 0xF200_0008);

        ValidateRegisterBodyMaps(artifact);
    }

    [Test]
    public void SelectedConstantMapRequiresFeatureEleven()
    {
        var artifact = CreateArtifact(includeFeature11: false, includeConstant: true,
            mapTypeSpecRef: 0xF200_0008, nodeTypeSpecRef: 0xF200_0008);

        Assert.Throws<NotSupportedException>(() => ValidateRegisterBodyMaps(artifact));
    }

    [Test]
    public void SelectedConstantMapRejectsTypeMismatch()
    {
        var artifact = CreateArtifact(includeFeature11: true, includeConstant: true,
            mapTypeSpecRef: 0xF200_000D, nodeTypeSpecRef: 0xF200_0008);

        Assert.Throws<InvalidDataException>(() => ValidateRegisterBodyMaps(artifact));
    }

    [Test]
    public void EmptyPresentMapDoesNotActivateUnreferencedValueTables()
    {
        var artifact = CreateArtifact(includeFeature11: false, includeConstant: false,
            mapTypeSpecRef: 0, nodeTypeSpecRef: 0xF200_0008);

        ValidateRegisterBodyMaps(artifact);
    }

    [Test]
    public void ConstantFormalDefaultBindsFeatureAndScalarTypeToOwnerModule()
    {
        var valid = AddConstantFormalDefault(
            CreateArtifact(includeFeature11: true, includeConstant: false,
                mapTypeSpecRef: 0, nodeTypeSpecRef: 0xF200_0008),
            0xF200_0008);
        Assert.DoesNotThrow(() => TrRmStructuralMetadataValidator.Validate(valid));

        var missingFeature = AddConstantFormalDefault(
            CreateArtifact(includeFeature11: false, includeConstant: false,
                mapTypeSpecRef: 0, nodeTypeSpecRef: 0xF200_0008),
            0xF200_0008);
        Assert.Throws<NotSupportedException>(
            () => TrRmStructuralMetadataValidator.Validate(missingFeature));

        var mismatchedType = AddConstantFormalDefault(
            CreateArtifact(includeFeature11: true, includeConstant: false,
                mapTypeSpecRef: 0, nodeTypeSpecRef: 0xF200_0008),
            0xF200_000D);
        Assert.Throws<InvalidDataException>(
            () => TrRmStructuralMetadataValidator.Validate(mismatchedType));
    }

    private static void ValidateRegisterBodyMaps(byte[] artifact)
    {
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(
            new TrRmArtifactIndex(artifact));
        tables.ValidateRegisterBodyMapsForMethod(1);
    }

    private static byte[] CreateArtifact(bool includeFeature11, bool includeConstant,
        uint mapTypeSpecRef, uint nodeTypeSpecRef)
    {
        var baseArtifact = TrRmRegisterBodyTestData.StandardInt32EntryArtifact(0x0E);
        var baseIndex = new TrRmArtifactIndex(baseArtifact);
        var sections = new List<TrRmSection>(baseIndex.SectionCount + 5);
        for (var sectionIndex = 0; sectionIndex < baseIndex.SectionCount; sectionIndex++)
        {
            baseIndex.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            var sectionBytes = bytes.ToArray();
            if (kind == 0x0001)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(sectionBytes.AsSpan(24),
                    includeFeature11 ? 1u : 0u);
                BinaryPrimitives.WriteUInt32LittleEndian(sectionBytes.AsSpan(28),
                    includeFeature11 ? 1u : 0u);
            }
            if (kind == 0x0020)
            {
                sectionBytes = TrRmVariableTableWriter.EncodeBodies(
                    [new ReadOnlyMemory<byte>(CreateConstantBody(includeConstant))]);
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount, sectionBytes));
        }

        if (includeFeature11)
        {
            sections.Add(new TrRmSection(0x0004, 0, 1,
                new byte[] { 11, 0, 0, 0, 1, 0, 0, 0 }));
        }
        sections.Add(new TrRmSection(0x0003, 0, 1, CreateInt32Blob()));
        sections.Add(new TrRmSection(0x0030, 0, 1,
            new byte[] { 1, 0, 0, 0, 1, 0, 0, 0 }));
        sections.Add(new TrRmSection(0x003F, 0, 1,
            CreateFrozenScalarNode(nodeTypeSpecRef)));
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        var artifact = TrRmArtifactWriter.Encode(sections);
        return includeConstant && mapTypeSpecRef != 0
            ? ReplaceConstantMapType(artifact, mapTypeSpecRef)
            : artifact;
    }

    private static byte[] ReplaceConstantMapType(byte[] artifact, uint typeSpecRef)
    {
        var index = new TrRmArtifactIndex(artifact);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            var sectionBytes = bytes.ToArray();
            if (kind == 0x0020)
            {
                var bodyReader = new TrRmVariableTableIndex(sectionBytes, recordCount, 1, 1);
                var body = bodyReader.GetRow(1).ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(21), typeSpecRef);
                sectionBytes = TrRmVariableTableWriter.EncodeBodies(
                    [new ReadOnlyMemory<byte>(body)]);
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount, sectionBytes));
        }
        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] CreateConstantBody(bool includeConstant)
    {
        var body = new byte[includeConstant ? 59 : 51];
        body[0] = 1;
        body[1] = 1;
        body[2] = 0x10;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5), includeConstant ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(9), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(13), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(17), 2);
        var blockOffset = 21;
        if (includeConstant)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(21), 0xF200_0008);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(25), 1);
            blockOffset += 8;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(blockOffset), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(blockOffset + 4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(blockOffset + 8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(blockOffset + 10), 0);
        var recordOffsetsOffset = blockOffset + 12;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(recordOffsetsOffset), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(recordOffsetsOffset + 4), 8);
        var recordDataOffset = recordOffsetsOffset + 8;
        body[recordDataOffset] = 1;
        var codeOffset = recordDataOffset + 8;
        body[codeOffset] = includeConstant ? (byte)0x09 : (byte)0x17;
        body[codeOffset + 1] = includeConstant ? (byte)0x00 : (byte)0x0E;
        return body;
    }

    private static byte[] CreateFrozenScalarNode(uint typeSpecRef)
    {
        var row = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(row, typeSpecRef);
        row[4] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(8), 1);
        return row;
    }

    private static byte[] CreateInt32Blob()
        => [0, 0, 0, 0, 4, 0, 0, 0, 42, 0, 0, 0];

    private static byte[] AddConstantFormalDefault(byte[] artifact, uint constantTypeSpecRef)
    {
        var index = new TrRmArtifactIndex(artifact);
        var sections = new List<TrRmSection>(index.SectionCount + 1);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            var sectionBytes = bytes.ToArray();
            if (kind == 0x0017)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(sectionBytes.AsSpan(16), 1);
                BinaryPrimitives.WriteUInt32LittleEndian(sectionBytes.AsSpan(20), 1);
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount, sectionBytes));
        }

        var formal = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(formal, 0x1600_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(formal.AsSpan(8), 3);
        formal[20] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(formal.AsSpan(24),
            constantTypeSpecRef);
        BinaryPrimitives.WriteUInt32LittleEndian(formal.AsSpan(28), 1);
        sections.Add(new TrRmSection(0x0019, 0, 1, formal));
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections);
    }
}
