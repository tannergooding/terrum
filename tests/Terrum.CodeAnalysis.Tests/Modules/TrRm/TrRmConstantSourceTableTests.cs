// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmConstantSourceTableTests
{
    [Test]
    public void ValidatesAssignedKindsAndTheirSelectedPayloadTables()
    {
        var bytes = TrRmConstantSourceTableWriter.Encode([
            new TrRmConstantSourceRow(1, 1),
            new TrRmConstantSourceRow(2, 2),
            new TrRmConstantSourceRow(3, 3),
        ], frozenValueNodeCount: 1, constantExecutionCount: 2,
            targetValueBindingCount: 3);
        var reader = new TrRmConstantSourceTableReader(bytes, 3, 1, 2, 3);

        Assert.That(reader.Count, Is.EqualTo(3));
        Assert.That((reader.GetKind(1), reader.GetPayloadReference(1)),
            Is.EqualTo((1u, 1u)));
        Assert.That((reader.GetKind(2), reader.GetPayloadReference(2)),
            Is.EqualTo((2u, 2u)));
        Assert.That((reader.GetKind(3), reader.GetPayloadReference(3)),
            Is.EqualTo((3u, 3u)));
    }

    [Test]
    public void RejectsUnknownKindsNullPayloadsAndOutOfRangeReferences()
    {
        Assert.Throws<InvalidDataException>(() =>
            Encode(new TrRmConstantSourceRow(4, 1), 1, 1, 1));
        Assert.Throws<InvalidDataException>(() =>
            Encode(new TrRmConstantSourceRow(1, 0), 1, 1, 1));
        Assert.Throws<InvalidDataException>(() =>
            Encode(new TrRmConstantSourceRow(1, 2), 1, 1, 1));
        Assert.Throws<InvalidDataException>(() =>
            Encode(new TrRmConstantSourceRow(2, 2), 1, 1, 1));
        Assert.Throws<InvalidDataException>(() =>
            Encode(new TrRmConstantSourceRow(3, 0x0100_0001), 1, 1, 1));
    }

    [Test]
    public void StructuralValidatorRejectsUnassignedSourceKind()
    {
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(4, 1)));
    }

    [Test]
    public void StructuralValidatorSelectsPayloadCountFromTheAssignedTag()
    {
        TrRmStructuralMetadataValidator.Validate(CreateArtifact(
            2, 1, constantExecutionCount: 1));
        TrRmStructuralMetadataValidator.Validate(CreateArtifact(
            3, 1, targetValueBindingCount: 1));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(
                2, 2, constantExecutionCount: 1)));
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(CreateArtifact(
                3, 2, targetValueBindingCount: 1)));
    }

    private static byte[] Encode(TrRmConstantSourceRow row,
        int frozenValueNodeCount, int constantExecutionCount,
        int targetValueBindingCount)
        => TrRmConstantSourceTableWriter.Encode([row],
            frozenValueNodeCount, constantExecutionCount, targetValueBindingCount);

    private static byte[] CreateArtifact(uint kind, uint payloadReference,
        int frozenValueNodeCount = 0, int constantExecutionCount = 0,
        int targetValueBindingCount = 0)
    {
        var strings = TrRmStringTableWriter.EncodeNames([("Module", 0)]);
        var stringReader = new TrRmStringTableReader(strings, 1);
        var modules = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], stringReader, 0, 0, 0, 0, 0);
        var constantSources = new byte[2 * sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(constantSources, kind);
        BinaryPrimitives.WriteUInt32LittleEndian(
            constantSources.AsSpan(sizeof(uint)), payloadReference);
        var sections = new System.Collections.Generic.List<TrRmSection>
        {
            new(0x0001, 0, 1, modules),
            new(0x0002, 0, 1, strings),
            new(0x0030, 0, 1, constantSources),
        };
        if (frozenValueNodeCount != 0)
        {
            sections.Add(new TrRmSection(0x003F, 0,
                (uint)frozenValueNodeCount, new byte[frozenValueNodeCount * 24]));
        }
        if (constantExecutionCount != 0)
        {
            sections.Add(new TrRmSection(0x0042, 0,
                (uint)constantExecutionCount, new byte[constantExecutionCount * 80]));
        }
        if (targetValueBindingCount != 0)
        {
            sections.Add(new TrRmSection(0x0049, 0,
                (uint)targetValueBindingCount, new byte[targetValueBindingCount * 16]));
        }

        return TrRmArtifactWriter.Encode(sections);
    }
}
