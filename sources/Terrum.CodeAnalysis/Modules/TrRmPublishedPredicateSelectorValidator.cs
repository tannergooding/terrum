// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmPublishedPredicateSelectorValidator
{
    internal static void Validate(ReadOnlySpan<byte> readRoots, int readRootCount,
        ReadOnlySpan<byte> nodes, int nodeCount,
        ReadOnlySpan<byte> operands, int operandCount, int dependencyCount,
        TrRmPredicateGraphTableReader graphs,
        TrRmPredicateReadFootprintTableReader footprints,
        TrRmPredicateChildEdgeTableReader childEdges,
        TrRmPredicateCaptureTableReader captures,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmTypeSpecTableReader typeSpecs, TrRmContractTableReader contracts)
    {
        var graphCount = graphs.Count;
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

        var usedFootprints = new HashSet<int>();
        var usedReadRoots = new HashSet<int>();
        var usedNodes = new HashSet<int>();
        var usedOperands = new HashSet<int>();
        var nodeKindsByGraph = new byte[graphCount + 1];
        for (var method = 1; method <= methods.Count; method++)
        {
            var predicateContracts = contracts.GetPredicateGraphContracts(method);
            foreach (var predicate in predicateContracts)
            {
                if (predicate.PhaseTag != 3)
                {
                    throw InvalidPredicateSelector();
                }

                var graph = predicate.PredicateGraphRow;
                var footprint = graphs.GetReadFootprintRow(graph);
                var rootNode = graphs.GetRootNodeRow(graph);
                if (!usedFootprints.Add(footprint) || !usedNodes.Add(rootNode)
                    || footprints.GetRootRowCount(footprint) != 1
                    || footprints.GetDependencyRowCount(footprint) != 0)
                {
                    throw InvalidPredicateSelector();
                }

                var rootRow = footprints.GetRootFirstRow(footprint);
                if (!usedReadRoots.Add(rootRow))
                {
                    throw InvalidPredicateSelector();
                }

                var readRoot = GetRow(readRoots, checked((uint)rootRow), 8);
                if (readRoot[0] != 5 || readRoot[1] != 0
                    || BinaryPrimitives.ReadUInt16LittleEndian(readRoot[2..]) != 0
                    || Read(readRoot, 4) != 0)
                {
                    throw InvalidPredicateSelector();
                }

                var node = GetRow(nodes, checked((uint)rootNode), 16);
                var nodeKind = Read(node, 0);
                var nodeResultType = Read(node, 4);
                var operandFirst = Read(node, 8);
                var nodeOperandCount = Read(node, 12);
                if (nodeKind is < 1 or > 6 || nodeOperandCount != 1
                    || !typeSpecs.AreEquivalentTypeSpecRefs(nodeResultType,
                        0xF200_0001)
                    || !HasSupportedOwnerResult(nodeKind,
                        signatures.GetResultTypeSpec(
                            methods.GetSignature(method)), typeSpecs))
                {
                    throw InvalidPredicateSelector();
                }
                nodeKindsByGraph[graph] = checked((byte)nodeKind);

                var operand = GetRow(operands, operandFirst, 8);
                if (!usedOperands.Add(checked((int)operandFirst))
                    || operand[0] != 1 || operand[1] != 0
                    || BinaryPrimitives.ReadUInt16LittleEndian(operand[2..]) != 0
                    || Read(operand, 4) != (uint)rootRow)
                {
                    throw InvalidPredicateSelector();
                }
            }
        }

        if (usedFootprints.Count != graphCount
            || usedReadRoots.Count != readRootCount
            || usedNodes.Count != nodeCount
            || usedOperands.Count != operandCount)
        {
            throw InvalidPredicateSelector();
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            var outputs = contracts.GetOutputStateContracts(method);
            for (var first = 0; first < outputs.Length; first++)
            {
                var firstOutput = outputs[first];
                var firstKind = GetOutputSelectorKind(firstOutput);
                for (var second = first + 1; second < outputs.Length; second++)
                {
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
            var graph = output.PredicateGraphRow;
            if (graph <= 0 || graph >= nodeKindsByGraph.Length
                || nodeKindsByGraph[graph] == 0)
            {
                throw InvalidPredicateSelector();
            }

            return nodeKindsByGraph[graph];
        }
    }

    private static bool SelectorsOverlap(byte left, byte right)
        => (left, right) switch
        {
            (3, 5) or (5, 3) or (3, 6) or (6, 3) or (4, 5) or (5, 4) => true,
            _ => false,
        };

    private static bool HasSupportedOwnerResult(uint nodeKind,
        uint resultTypeSpec, TrRmTypeSpecTableReader typeSpecs)
        => nodeKind switch
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

    private static ReadOnlySpan<byte> GetRow(ReadOnlySpan<byte> rows,
        uint row, int rowSize)
    {
        if (row is 0 or > 0xFF_FFFF
            || (ulong)row * (uint)rowSize > (ulong)rows.Length)
        {
            throw InvalidPredicateSelector();
        }

        return rows.Slice(checked(((int)row - 1) * rowSize), rowSize);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidPredicateSelector()
        => new("Invalid Standard TRRM normal-result predicate selector.");
}
