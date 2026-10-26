// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
    public void ValidatesReadFootprintRangesAndExclusiveOwnership()
    {
        var bytes = TrRmPredicateReadFootprintTableWriter.Encode([
            new TrRmPredicateReadFootprintRow(1, 2, 1, 1),
            new TrRmPredicateReadFootprintRow(3, 1, 0, 0),
        ], rootCount: 3, dependencyCount: 1);
        var footprints = new TrRmPredicateReadFootprintTableReader(bytes, 2, 3, 1);

        Assert.That(footprints.GetRootFirstRow(1), Is.EqualTo(1));
        Assert.That(footprints.GetRootRowCount(1), Is.EqualTo(2));
        Assert.That(footprints.GetDependencyFirstRow(1), Is.EqualTo(1));
        Assert.That(footprints.GetDependencyRowCount(1), Is.EqualTo(1));
        Assert.That(footprints.GetRootFirstRow(2), Is.EqualTo(3));
        Assert.That(footprints.GetRootRowCount(2), Is.EqualTo(1));
        Assert.That(footprints.GetDependencyFirstRow(2), Is.Zero);
        Assert.That(footprints.GetDependencyRowCount(2), Is.Zero);
    }

    [Test]
    public void RejectsMalformedOrSharedReadFootprintRanges()
    {
        Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(1, 0, 0, 0),
            ], 1, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(0, 1, 0, 0),
            ], 1, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(0x0100_0000, 1, 0, 0),
            ], 1, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(2, 2, 0, 0),
            ], 2, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(1, 1, 0, 0),
                new TrRmPredicateReadFootprintRow(1, 1, 0, 0),
            ], 1, 0));
        Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(0, 0, 1, 1),
                new TrRmPredicateReadFootprintRow(0, 0, 1, 1),
            ], 0, 1));
        Assert.Throws<InvalidDataException>(() =>
            new TrRmPredicateReadFootprintTableReader(new byte[15], 1, 0, 0));
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

    [Test]
    public void StructuralValidatorRejectsOutOfRangeReadFootprintRows()
    {
        var graphBytes = TrRmPredicateGraphTableWriter.Encode([
            new TrRmPredicateGraphRow(1, 1),
        ], nodeCount: 1, readFootprintCount: 1);
        var captureBytes = TrRmPredicateCaptureTableWriter.Encode(
            [new TrRmPredicateCaptureRow(1, 0, 1, 0, 1)],
            graphCount: 1, types: CreateTypeSpecs());
        var footprintBytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(footprintBytes, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(footprintBytes.AsSpan(4), 1);

        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(graphBytes, captureBytes, graphCount: 1,
                    nodeCount: 1, footprintCount: 1,
                    footprintBytes: footprintBytes,
                    rootCount: 1)));
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
        int graphCount, int nodeCount, int footprintCount,
        byte[]? footprintBytes = null, int rootCount = 0,
        int dependencyCount = 0)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows([("Module", 0)]);
        var strings = new TrRmStringTableReader(stringsBytes, 1);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 0, 0, 0);
        var sections = new List<TrRmSection> {
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 1, stringsBytes),
            new TrRmSection(0x0011, 0, 1, TypeSpecBytes()),
            new TrRmSection(0x0037, 0, (uint)graphCount, graphs),
            new TrRmSection(0x0038, 0, (uint)footprintCount,
                footprintBytes ?? new byte[footprintCount * 16]),
        };
        if (rootCount != 0)
        {
            sections.Add(new TrRmSection(0x0039, 0, (uint)rootCount,
                new byte[rootCount * 8]));
        }
        if (dependencyCount != 0)
        {
            sections.Add(new TrRmSection(0x003A, 0, (uint)dependencyCount,
                new byte[dependencyCount * 8]));
        }
        sections.AddRange([
            new TrRmSection(0x003B, 0, (uint)nodeCount,
                new byte[nodeCount * 16]),
            new TrRmSection(0x003E, 0, (uint)(captures.Length / 16), captures),
        ]);
        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] TypeSpecBytes()
        => TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 0, 0, 0);
}
