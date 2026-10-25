// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmPredicateGraphTableTests
{
    [Test]
    public void ValidatesGraphReferencesAndTypedCaptureRoles()
    {
        var typeSpecs = CreateTypeSpecs();
        var graphBytes = TrRmPredicateGraphTableWriter.Encode([
            new TrRmPredicateGraphRow(1, 1),
            new TrRmPredicateGraphRow(2, 2),
        ], nodeCount: 2, readFootprintCount: 2);
        var graphs = new TrRmPredicateGraphTableReader(graphBytes, 2, 2, 2);
        var captureBytes = TrRmPredicateCaptureTableWriter.Encode([
            new TrRmPredicateCaptureRow(2, 0, 3, 0, 1),
            new TrRmPredicateCaptureRow(1, 1, 1, 1, 0xF200_0008),
            new TrRmPredicateCaptureRow(1, 0, 2, 0, 1),
        ], graphCount: 2, types: typeSpecs);
        var captures = new TrRmPredicateCaptureTableReader(
            captureBytes, 3, 2, typeSpecs);

        Assert.That(graphs.GetRootNodeRow(2), Is.EqualTo(2));
        Assert.That(graphs.GetReadFootprintRow(1), Is.EqualTo(1));
        Assert.That(captures.GetGraphRow(2), Is.EqualTo(1));
        Assert.That(captures.GetOrdinal(2), Is.EqualTo(1u));
        Assert.That(captures.GetRole(2), Is.EqualTo((byte)1));
        Assert.That(captures.GetFlags(2), Is.EqualTo((byte)1));
        Assert.That(captures.GetCapturedTypeSpecRef(2), Is.EqualTo(0xF200_0008u));
        Assert.That(graphs.Count, Is.EqualTo(2));
    }

    [Test]
    public void RejectsInvalidGraphTargetsAndCaptureOrdinalsRolesFlagsOrTypes()
    {
        Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateGraphTableWriter.Encode([
                new TrRmPredicateGraphRow(0, 1),
            ], 1, 1));
        Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateGraphTableWriter.Encode([
                new TrRmPredicateGraphRow(1, 2),
            ], 1, 1));

        Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(0, 0, 1, 0, 1),
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 1, 1, 0, 1),
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 0, 0, 0, 1),
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 0, 2, 1, 1),
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 0, 3, 1, 1),
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 0, 1, 2, 1),
        ]));
        Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 0, 1, 0, 0xF400_0008),
        ]));
    }

    [Test]
    public void StructuralValidatorRejectsMalformedCaptureReferences()
    {
        var typeSpecs = CreateTypeSpecs();
        var graphBytes = TrRmPredicateGraphTableWriter.Encode([
            new TrRmPredicateGraphRow(1, 1),
        ], nodeCount: 1, readFootprintCount: 1);
        var captureBytes = TrRmPredicateCaptureTableWriter.Encode([
            new TrRmPredicateCaptureRow(1, 0, 1, 0, 1),
        ], graphCount: 1, types: typeSpecs);

        BinaryPrimitives.WriteUInt32LittleEndian(captureBytes.AsSpan(4), 1);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(graphBytes, captureBytes, graphCount: 1,
                    nodeCount: 1, footprintCount: 1)));

        captureBytes = TrRmPredicateCaptureTableWriter.Encode([
            new TrRmPredicateCaptureRow(1, 0, 1, 0, 1),
        ], graphCount: 1, typeSpecs);
        BinaryPrimitives.WriteUInt32LittleEndian(captureBytes.AsSpan(12), 0xF400_0008);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(graphBytes, captureBytes, graphCount: 1,
                    nodeCount: 1, footprintCount: 1)));
    }

    private static byte[] EncodeCaptures(TrRmPredicateCaptureRow[] rows)
        => TrRmPredicateCaptureTableWriter.Encode(rows,
            graphCount: 2, types: CreateTypeSpecs());

    private static TrRmTypeSpecTableReader CreateTypeSpecs()
    {
        var bytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 0, 0, 0);
        return new TrRmTypeSpecTableReader(bytes, 1, 0, 0, 0, 0);
    }

    private static byte[] CreateArtifact(byte[] graphs, byte[] captures,
        int graphCount, int nodeCount, int footprintCount)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([("Module", 0)]);
        var strings = new TrRmStringTableReader(stringsBytes, 1);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 0, 0, 0);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 1, stringsBytes),
            new TrRmSection(0x0011, 0, 1, TypeSpecBytes()),
            new TrRmSection(0x0037, 0, (uint)graphCount, graphs),
            new TrRmSection(0x0038, 0, (uint)footprintCount,
                new byte[footprintCount * 16]),
            new TrRmSection(0x003B, 0, (uint)nodeCount,
                new byte[nodeCount * 16]),
            new TrRmSection(0x003E, 0, (uint)(captures.Length / 16), captures),
        ]);
    }

    private static byte[] TypeSpecBytes()
        => TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 0, 0, 0);
}
