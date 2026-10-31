// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmPublishedPredicateSelectorValidator
{
    internal static byte[] Validate(ReadOnlySpan<byte> readRoots, int readRootCount,
        ReadOnlySpan<byte> nodes, int nodeCount,
        ReadOnlySpan<byte> operands, int operandCount, int dependencyCount,
        TrRmPredicateGraphTableReader graphs,
        TrRmPredicateReadFootprintTableReader footprints,
        TrRmPredicateChildEdgeTableReader childEdges,
        TrRmPredicateCaptureTableReader captures,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmTypeSpecTableReader typeSpecs, TrRmContractTableReader contracts,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        var graphCount = graphs.Count;
        resourceLedger?.ConsumeWork(8);
        if ((long)readRootCount * 8 != readRoots.Length
            || (long)nodeCount * 16 != nodes.Length
            || (long)operandCount * 8 != operands.Length
            || contracts.PredicateGraphAttachmentCount != graphCount
            || footprints.Count != graphCount || readRootCount != graphCount
            || nodeCount != graphCount || operandCount != graphCount
            || dependencyCount != 0 || childEdges.Count != 0
            || captures.Count != 0)
        {
            throw InvalidPredicateSelector();
        }

        var stateWork = checked(((ulong)graphCount * 5UL) + 6UL);
        resourceLedger?.ConsumeWork(stateWork);
        var usedFootprints = new HashSet<int>(graphCount);
        var usedReadRoots = new HashSet<int>(graphCount);
        var usedNodes = new HashSet<int>(graphCount);
        var usedOperands = new HashSet<int>(graphCount);
        var nodeKindsByGraph = new byte[graphCount + 1];
        for (var method = 1; method <= methods.Count; method++)
        {
            resourceLedger?.ConsumeWork();
            var predicateContracts = contracts.GetPredicateGraphContracts(method);
            foreach (var predicate in predicateContracts)
            {
                resourceLedger?.ConsumeWork();
                if (predicate.PhaseTag != 3)
                {
                    throw InvalidPredicateSelector();
                }

                var graph = predicate.PredicateGraphRow;
                var footprint = graphs.GetReadFootprintRow(graph);
                var rootNode = graphs.GetRootNodeRow(graph);
                resourceLedger?.ConsumeWork(2);
                var newFootprint = usedFootprints.Add(footprint);
                resourceLedger?.ConsumeWork(2);
                var newNode = usedNodes.Add(rootNode);
                if (!newFootprint || !newNode
                    || footprints.GetRootRowCount(footprint) != 1
                    || footprints.GetDependencyRowCount(footprint) != 0)
                {
                    throw InvalidPredicateSelector();
                }

                var rootRow = footprints.GetRootFirstRow(footprint);
                resourceLedger?.ConsumeWork(2);
                if (!usedReadRoots.Add(rootRow))
                {
                    throw InvalidPredicateSelector();
                }

                var readRoot = GetRow(readRoots, checked((uint)rootRow), 8,
                    resourceLedger);
                if (ReadByte(readRoot, 0, resourceLedger) != 5
                    || ReadByte(readRoot, 1, resourceLedger) != 0
                    || ReadUInt16(readRoot, 2, resourceLedger) != 0
                    || Read(readRoot, 4, resourceLedger) != 0)
                {
                    throw InvalidPredicateSelector();
                }

                var node = GetRow(nodes, checked((uint)rootNode), 16,
                    resourceLedger);
                var nodeKind = Read(node, 0, resourceLedger);
                var nodeResultType = Read(node, 4, resourceLedger);
                var operandFirst = Read(node, 8, resourceLedger);
                var nodeOperandCount = Read(node, 12, resourceLedger);
                resourceLedger?.ConsumeWork(2);
                if (nodeKind is < 1 or > 6 || nodeOperandCount != 1
                    || !typeSpecs.AreEquivalentTypeSpecRefs(nodeResultType,
                        0xF200_0001)
                    || !HasSupportedOwnerResult(nodeKind,
                        signatures.GetResultTypeSpec(
                            methods.GetSignature(method)), typeSpecs,
                        resourceLedger))
                {
                    throw InvalidPredicateSelector();
                }
                resourceLedger?.ConsumeWork();
                nodeKindsByGraph[graph] = checked((byte)nodeKind);

                var operand = GetRow(operands, operandFirst, 8,
                    resourceLedger);
                resourceLedger?.ConsumeWork(2);
                var newOperand = usedOperands.Add(checked((int)operandFirst));
                if (!newOperand
                    || ReadByte(operand, 0, resourceLedger) != 1
                    || ReadByte(operand, 1, resourceLedger) != 0
                    || ReadUInt16(operand, 2, resourceLedger) != 0
                    || Read(operand, 4, resourceLedger) != (uint)rootRow)
                {
                    throw InvalidPredicateSelector();
                }
            }
        }

        resourceLedger?.ConsumeWork(4);
        if (usedFootprints.Count != graphCount
            || usedReadRoots.Count != readRootCount
            || usedNodes.Count != nodeCount
            || usedOperands.Count != operandCount)
        {
            throw InvalidPredicateSelector();
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            resourceLedger?.ConsumeWork();
            var outputs = contracts.GetOutputStateContracts(method);
            for (var first = 0; first < outputs.Length; first++)
            {
                resourceLedger?.ConsumeWork();
                var firstOutput = outputs[first];
                var firstKind = GetOutputSelectorKind(firstOutput);
                for (var second = first + 1; second < outputs.Length; second++)
                {
                    resourceLedger?.ConsumeWork();
                    var secondOutput = outputs[second];
                    if (firstOutput.ParameterOrdinal != secondOutput.ParameterOrdinal)
                    {
                        continue;
                    }

                    var secondKind = GetOutputSelectorKind(secondOutput);
                    if (firstKind == secondKind
                        || (firstOutput.State != secondOutput.State
                            && SelectorsOverlap(firstKind, secondKind)))
                    {
                        throw InvalidPredicateSelector();
                    }
                }
            }
        }

        byte GetOutputSelectorKind(TrRmOutputStateContract output)
        {
            resourceLedger?.ConsumeWork();
            var graph = output.PredicateGraphRow;
            if (graph <= 0 || graph >= nodeKindsByGraph.Length
                || nodeKindsByGraph[graph] == 0)
            {
                throw InvalidPredicateSelector();
            }

            return nodeKindsByGraph[graph];
        }

        return nodeKindsByGraph;
    }

    private static bool SelectorsOverlap(byte left, byte right)
        => (left, right) switch
        {
            (3, 5) or (5, 3) or (3, 6) or (6, 3) or (4, 5) or (5, 4) => true,
            _ => false,
        };

    private static bool HasSupportedOwnerResult(uint nodeKind,
        uint resultTypeSpec, TrRmTypeSpecTableReader typeSpecs,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork();
        return nodeKind switch
        {
            1 or 2 => typeSpecs.AreEquivalentTypeSpecRefs(resultTypeSpec,
                0xF200_0001),
            3 or 4 => typeSpecs.AreEquivalentTypeSpecRefs(resultTypeSpec,
                0xF200_0008)
                || typeSpecs.AreEquivalentTypeSpecRefs(resultTypeSpec,
                    0xF200_000D),
            5 or 6 => typeSpecs.AreEquivalentTypeSpecRefs(resultTypeSpec,
                0xF200_0008),
            _ => false,
        };
    }

    private static ReadOnlySpan<byte> GetRow(ReadOnlySpan<byte> rows,
        uint row, int rowSize,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork();
        if (row is 0 or > 0xFF_FFFF
            || (ulong)row * (uint)rowSize > (ulong)rows.Length)
        {
            throw InvalidPredicateSelector();
        }

        return rows.Slice(checked(((int)row - 1) * rowSize), rowSize);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, int offset,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(sizeof(byte) + 1);
        return data[offset];
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(sizeof(ushort) + 1);
        return BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(offset, sizeof(ushort)));
    }

    private static InvalidDataException InvalidPredicateSelector()
        => new("Invalid Standard TRRM normal-result predicate selector.");
}
