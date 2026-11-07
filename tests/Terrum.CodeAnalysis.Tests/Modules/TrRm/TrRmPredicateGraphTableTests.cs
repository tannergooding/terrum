// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmPredicateGraphTableTests
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
    public void ValidatesPredicateChildEdgesAndCanonicalWriterOrder()
    {
        var bytes = EncodeChildEdges([
            new TrRmPredicateChildEdgeRow(2, 0, 3),
            new TrRmPredicateChildEdgeRow(1, 1, 4),
            new TrRmPredicateChildEdgeRow(1, 0, 2),
        ], nodeCount: 4, rootRows: [1]);
        var graphs = ReadGraphs([new TrRmPredicateGraphRow(1, 1)], 4);
        var edges = new TrRmPredicateChildEdgeTableReader(bytes, 3, 4, graphs);

        Assert.That(edges.GetParentNodeRow(1), Is.EqualTo(1));
        Assert.That(edges.GetChildOrdinal(1), Is.Zero);
        Assert.That(edges.GetChildNodeRow(1), Is.EqualTo(2));
        Assert.That(edges.GetParentNodeRow(2), Is.EqualTo(1));
        Assert.That(edges.GetChildOrdinal(2), Is.EqualTo(1u));
        Assert.That(edges.GetChildNodeRow(2), Is.EqualTo(4));
        Assert.That(edges.GetParentNodeRow(3), Is.EqualTo(2));
        Assert.That(edges.GetChildNodeRow(3), Is.EqualTo(3));

        var reordered = new byte[bytes.Length];
        bytes.AsSpan(24, 12).CopyTo(reordered);
        bytes.AsSpan(12, 12).CopyTo(reordered.AsSpan(12));
        bytes.AsSpan(0, 12).CopyTo(reordered.AsSpan(24));
        var reorderedEdges = new TrRmPredicateChildEdgeTableReader(
            reordered, 3, 4, graphs);
        Assert.That(reorderedEdges.GetChildNodeRow(1), Is.EqualTo(3));
        Assert.That(reorderedEdges.GetChildNodeRow(2), Is.EqualTo(4));
        Assert.That(reorderedEdges.GetChildNodeRow(3), Is.EqualTo(2));
    }

    [Test]
    public void AcceptsValidEdgesWhenNodeRowsAreNotRootFirstDepthFirst()
    {
        var graphRows = new[] { new TrRmPredicateGraphRow(3, 1) };
        var graphBytes = TrRmPredicateGraphTableWriter.Encode(
            graphRows, nodeCount: 3, readFootprintCount: 1);
        var graphs = new TrRmPredicateGraphTableReader(graphBytes, 1, 3, 1);
        var edgeBytes = TrRmPredicateChildEdgeTableWriter.Encode([
            new TrRmPredicateChildEdgeRow(3, 0, 1),
            new TrRmPredicateChildEdgeRow(1, 0, 2),
        ], nodeCount: 3, graphs: graphs);
        var edges = new TrRmPredicateChildEdgeTableReader(
            edgeBytes, 2, 3, graphs);

        Assert.That(edges.GetChildCount(3), Is.EqualTo(1));
        Assert.That(edges.GetChildNodeRowByOrdinal(3, 0), Is.EqualTo(1));
        Assert.That(edges.GetChildNodeRowByOrdinal(1, 0), Is.EqualTo(2));
    }

    [Test]
    public void RejectsMalformedPredicateChildOrdinalsAndGraphTopology()
    {
        _ = Assert.Throws<InvalidDataException>(() => EncodeChildEdges([
            new TrRmPredicateChildEdgeRow(0, 0, 1),
        ], nodeCount: 2, rootRows: [1]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeChildEdges([
            new TrRmPredicateChildEdgeRow(1, 0, 0x0100_0000),
        ], nodeCount: 2, rootRows: [1]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeChildEdges([
            new TrRmPredicateChildEdgeRow(1, 1, 2),
        ], nodeCount: 2, rootRows: [1]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeChildEdges([], nodeCount: 2, rootRows: [1, 1]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeChildEdges([
            new TrRmPredicateChildEdgeRow(1, 0, 2),
            new TrRmPredicateChildEdgeRow(1, 1, 2),
        ], nodeCount: 2, rootRows: [1]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeChildEdges([
            new TrRmPredicateChildEdgeRow(1, 0, 2),
            new TrRmPredicateChildEdgeRow(1, 1, 3),
            new TrRmPredicateChildEdgeRow(2, 0, 3),
        ], nodeCount: 3, rootRows: [1]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeChildEdges([
            new TrRmPredicateChildEdgeRow(1, 0, 2),
            new TrRmPredicateChildEdgeRow(2, 0, 3),
            new TrRmPredicateChildEdgeRow(3, 0, 2),
        ], nodeCount: 3, rootRows: [1]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeChildEdges([
            new TrRmPredicateChildEdgeRow(1, 0, 3),
        ], nodeCount: 3, rootRows: [1, 3]));
        _ = Assert.Throws<InvalidDataException>(() =>
            EncodeChildEdges([], nodeCount: 2, rootRows: [1]));
    }

    [Test]
    public void RejectsMalformedOrSharedReadFootprintRanges()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(1, 0, 0, 0),
            ], 1, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(0, 1, 0, 0),
            ], 1, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(0x0100_0000, 1, 0, 0),
            ], 1, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(2, 2, 0, 0),
            ], 2, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(1, 1, 0, 0),
                new TrRmPredicateReadFootprintRow(1, 1, 0, 0),
            ], 1, 0));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateReadFootprintTableWriter.Encode([
                new TrRmPredicateReadFootprintRow(0, 0, 1, 1),
                new TrRmPredicateReadFootprintRow(0, 0, 1, 1),
            ], 0, 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            new TrRmPredicateReadFootprintTableReader(new byte[15], 1, 0, 0));
    }

    [Test]
    public void RejectsInvalidGraphTargetsAndCaptureOrdinalsRolesFlagsOrTypes()
    {
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateGraphTableWriter.Encode([
                new TrRmPredicateGraphRow(0, 1),
            ], 1, 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmPredicateGraphTableWriter.Encode([
                new TrRmPredicateGraphRow(1, 2),
            ], 1, 1));

        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(0, 0, 1, 0, 1),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 1, 1, 0, 1),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 0, 0, 0, 1),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 0, 2, 1, 1),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 0, 3, 1, 1),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures([
            new TrRmPredicateCaptureRow(1, 0, 1, 2, 1),
        ]));
        _ = Assert.Throws<InvalidDataException>(() => EncodeCaptures([
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
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(graphBytes, captureBytes, graphCount: 1,
                    nodeCount: 1, footprintCount: 1)));

        captureBytes = TrRmPredicateCaptureTableWriter.Encode([
            new TrRmPredicateCaptureRow(1, 0, 1, 0, 1),
        ], graphCount: 1, typeSpecs);
        BinaryPrimitives.WriteUInt32LittleEndian(captureBytes.AsSpan(12), 0xF400_0008);
        _ = Assert.Throws<InvalidDataException>(() =>
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

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(graphBytes, captureBytes, graphCount: 1,
                    nodeCount: 1, footprintCount: 1,
                    footprintBytes: footprintBytes,
                    rootCount: 1)));
    }

    [Test]
    public void StructuralValidatorRejectsMalformedPredicateChildEdges()
    {
        var graphs = ReadGraphs([new TrRmPredicateGraphRow(1, 1)], 2);
        var graphBytes = TrRmPredicateGraphTableWriter.Encode([
            new TrRmPredicateGraphRow(1, 1),
        ], nodeCount: 2, readFootprintCount: 1);
        var edgeBytes = TrRmPredicateChildEdgeTableWriter.Encode([
            new TrRmPredicateChildEdgeRow(1, 0, 2),
        ], nodeCount: 2, graphs: graphs);
        var captureBytes = TrRmPredicateCaptureTableWriter.Encode([
            new TrRmPredicateCaptureRow(1, 0, 1, 0, 1),
        ], graphCount: 1, types: CreateTypeSpecs());
        BinaryPrimitives.WriteUInt32LittleEndian(edgeBytes.AsSpan(4), 1);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator.Validate(
                CreateArtifact(graphBytes, captureBytes, graphCount: 1,
                    nodeCount: 2, footprintCount: 1,
                    childEdgeBytes: edgeBytes, childEdgeCount: 1)));
    }

    private static TrRmPredicateGraphTableReader ReadGraphs(
        TrRmPredicateGraphRow[] rows, int nodeCount)
    {
        var graphBytes = TrRmPredicateGraphTableWriter.Encode(rows,
            nodeCount, readFootprintCount: rows.Length);
        return new TrRmPredicateGraphTableReader(
            graphBytes, rows.Length, nodeCount, rows.Length);
    }

    private static byte[] EncodeChildEdges(TrRmPredicateChildEdgeRow[] rows,
        int nodeCount, params int[] rootRows)
    {
        var graphRows = new TrRmPredicateGraphRow[rootRows.Length];
        for (var index = 0; index < rootRows.Length; index++)
        {
            graphRows[index] = new TrRmPredicateGraphRow(rootRows[index], index + 1);
        }

        var graphs = ReadGraphs(graphRows, nodeCount);
        return TrRmPredicateChildEdgeTableWriter.Encode(rows, nodeCount, graphs);
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
        int dependencyCount = 0, byte[]? childEdgeBytes = null,
        int childEdgeCount = 0)
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
        sections.Add(new TrRmSection(0x003B, 0, (uint)nodeCount,
            new byte[nodeCount * 16]));
        if (childEdgeCount != 0)
        {
            sections.Add(new TrRmSection(0x003D, 0, (uint)childEdgeCount,
                childEdgeBytes ?? throw new ArgumentNullException(nameof(childEdgeBytes))));
        }
        sections.Add(new TrRmSection(0x003E, 0, (uint)(captures.Length / 16), captures));
        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] TypeSpecBytes()
        => TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
        ], 0, 0, 0, 0);
}
