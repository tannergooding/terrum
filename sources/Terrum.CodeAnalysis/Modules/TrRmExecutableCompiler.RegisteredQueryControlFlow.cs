// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static StandardRegisteredQueryLoopControlFlow
        AnalyzeStandardRegisteredQueryLoopControlFlow(
            TrRmRegisterBodyReader body)
    {
        var blockCount = body.BlockCount;
        if (blockCount < 1
            || (long)blockCount * blockCount
                > SemanticVerifier.MaximumAnalysisVisits
            || (long)blockCount * 14
                > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "The registered-query loop CFG exceeds bounded block, exception or analysis capacity.");
        }

        var dispatchBlocks = new bool[blockCount];
        var dispatchEntryBlocks = new bool[blockCount];
        var ordinaryBlockCount = blockCount;
        if (body.HasExceptionDirectory)
        {
            var directory = body.ExceptionDirectory;
            if (directory.HandlerCount != 0)
            {
                throw Unsupported(
                    "The registered-query E1 loop consumer does not admit handlers or filters.");
            }
            for (var regionIndex = 1;
                regionIndex < directory.RegionCount; regionIndex++)
            {
                var region = directory.GetRegion(regionIndex);
                if (region.Kind == 1
                    && (region.Flags & 1) != 0)
                {
                    continue;
                }
                if (region.Kind != 4)
                {
                    throw Unsupported(
                        "The registered-query E1 loop consumer admits only Protected and Finally regions.");
                }
                dispatchEntryBlocks[region.EntryBlock] = true;
            }

            ordinaryBlockCount = 0;
            for (var blockIndex = 0; blockIndex < blockCount;
                blockIndex++)
            {
                dispatchBlocks[blockIndex]
                    = directory.GetRegion(
                        directory.GetBlockRegion(blockIndex)).Kind == 4;
                if (!dispatchBlocks[blockIndex])
                {
                    ordinaryBlockCount++;
                }
            }
        }

        var successorLists = new List<int>[blockCount];
        var predecessorLists = new List<int>[blockCount];
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            successorLists[blockIndex] = [];
            predecessorLists[blockIndex] = [];
        }

        var referencedEdges = new HashSet<int>();
        var remainingGraphWork
            = (long)SemanticVerifier.MaximumAnalysisVisits;
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var terminalOffset = -1;
            var terminalLength = 0;
            for (var position = 0; position < code.Length;)
            {
                if (remainingGraphWork-- <= 0)
                {
                    throw Unsupported(
                        "The registered-query loop CFG exceeds bounded graph work.");
                }

                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], body.Schema);
                if (instructionLength > code.Length - position)
                {
                    throw Invalid(
                        "A registered-query loop CFG contains a truncated instruction.");
                }

                terminalOffset = position;
                terminalLength = instructionLength;
                position += instructionLength;
            }

            if (terminalOffset < 0)
            {
                throw Invalid(
                    "A registered-query loop CFG contains an empty Block.");
            }

            var terminal = code.Slice(terminalOffset, terminalLength);
            switch (terminal[0])
            {
                case 0xE3:
                    if (terminal[1] != 0)
                    {
                        throw Invalid(
                            "A registered-query loop branch has nonzero control bits.");
                    }
                    successorLists[blockIndex].Add(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            terminal[2..]));
                    break;

                case 0xE4:
                {
                    if ((terminal[1] & 0xF0) != 0)
                    {
                        throw Invalid(
                            "A registered-query loop branch has reserved condition bits.");
                    }

                    var edgeRecordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            terminal[2..]);
                    if (!referencedEdges.Add(edgeRecordIndex))
                    {
                        throw Invalid(
                            "A registered-query loop reuses a ConditionalEdge record.");
                    }

                    var edge = body.GetRecord(edgeRecordIndex);
                    if (edge.Length != 8 || edge[0] != 0x06)
                    {
                        throw Invalid(
                            "A registered-query loop branch has no ConditionalEdge record.");
                    }

                    var trueBlock
                        = BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]);
                    var falseBlock
                        = BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]);
                    successorLists[blockIndex].Add(trueBlock);
                    if (falseBlock != trueBlock)
                    {
                        successorLists[blockIndex].Add(falseBlock);
                    }
                    break;
                }

                case 0x17:
                    break;

                case 0xB6:
                case 0xEA:
                {
                    if (!body.HasExceptionDirectory)
                    {
                        throw Invalid(
                            "A registered-query RegionExit has no selected E1 directory.");
                    }
                    var recordIndex = terminal[0] == 0xB6
                        ? terminal[1]
                        : BinaryPrimitives.ReadUInt16LittleEndian(
                            terminal[2..]);
                    var record = body.GetRecord(recordIndex);
                    if (record.Length < 16 || record[0] != 0x0A)
                    {
                        throw Invalid(
                            "A registered-query RegionExit has no matching record.");
                    }
                    switch (record[4])
                    {
                        case 1:
                            successorLists[blockIndex].Add(
                                BinaryPrimitives.ReadUInt16LittleEndian(
                                    record[6..]));
                            break;
                        case 4:
                            if (!dispatchBlocks[blockIndex])
                            {
                                throw Invalid(
                                    "FinallyComplete occurs outside a Finally region.");
                            }
                            break;
                        default:
                            throw Unsupported(
                                "The registered-query E1 loop consumer does not admit handler exits.");
                    }
                    break;
                }

                default:
                    throw Unsupported(
                        $"The registered-query loop CFG has unsupported terminal opcode 0x{terminal[0]:X2}.");
            }

            foreach (var target in successorLists[blockIndex])
            {
                if ((uint)target >= (uint)blockCount)
                {
                    throw Invalid(
                        "A registered-query loop CFG edge leaves the Block table.");
                }

                predecessorLists[target].Add(blockIndex);
            }
        }

        var successors = new int[blockCount][];
        var predecessors = new int[blockCount][];
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            successors[blockIndex] = [.. successorLists[blockIndex]];
            predecessors[blockIndex]
                = [.. predecessorLists[blockIndex]];
        }

        var reachable = new bool[blockCount];
        var reachabilityQueue = new Queue<int>();
        reachabilityQueue.Enqueue(0);
        var reachableCount = 0;
        while (reachabilityQueue.TryDequeue(out var blockIndex))
        {
            if (remainingGraphWork-- <= 0)
            {
                throw Unsupported(
                    "The registered-query loop CFG exceeds bounded reachability work.");
            }

            if (reachable[blockIndex])
            {
                continue;
            }

            reachable[blockIndex] = true;
            reachableCount++;
            foreach (var target in successors[blockIndex])
            {
                reachabilityQueue.Enqueue(target);
            }
        }

        if (reachableCount != ordinaryBlockCount)
        {
            throw Invalid(
                "The registered-query loop CFG contains an unreachable ordinary Block.");
        }

        var blockUses = new HashSet<int>[blockCount];
        var blockDefinitions = new HashSet<int>[blockCount];
        var remainingLivenessWork
            = (long)SemanticVerifier.MaximumAnalysisVisits;
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            var uses = new HashSet<int>();
            var definitions = new HashSet<int>();
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                if (remainingLivenessWork-- <= 0)
                {
                    throw Unsupported(
                        "Registered-query loop liveness exceeds bounded analysis work.");
                }

                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], body.Schema);
                if (instructionLength > code.Length - position)
                {
                    throw Invalid(
                        "Registered-query loop liveness found a truncated instruction.");
                }

                var instruction = code.Slice(position, instructionLength);
                switch (instruction[0])
                {
                    case 0x10:
                    {
                        var destination = instruction[1] & 0x0F;
                        var source = instruction[1] >> 4;
                        if (destination >= 14 || source >= 14)
                        {
                            throw Invalid(
                                "A registered-query loop mov uses a nonbinding register.");
                        }
                        if (!definitions.Contains(source))
                        {
                            _ = uses.Add(source);
                        }
                        _ = definitions.Add(destination);
                        break;
                    }

                    case 0x11:
                    case 0xDE:
                    {
                        var destination = instruction[1] & 0x0F;
                        if (destination >= 14)
                        {
                            throw Invalid(
                                "A registered-query loop definition uses a nonbinding register.");
                        }
                        _ = definitions.Add(destination);
                        break;
                    }

                    case 0xD5:
                    {
                        var destination = instruction[1];
                        if (destination >= 14)
                        {
                            throw Invalid(
                                "A registered-query HomeLoad uses a nonbinding register.");
                        }
                        _ = definitions.Add(destination);
                        break;
                    }

                    case 0xD6:
                    {
                        var source = instruction[1] & 0x0F;
                        if ((instruction[1] >> 4) != 1 || source >= 14)
                        {
                            throw Unsupported(
                                "Registered-query HomeStore requires the CopyRead source-use form.");
                        }
                        if (!definitions.Contains(source))
                        {
                            _ = uses.Add(source);
                        }
                        break;
                    }

                    case 0x17:
                    {
                        var result = instruction[1];
                        if (result >= 14)
                        {
                            throw Invalid(
                                "A registered-query loop Return uses a nonbinding register.");
                        }
                        if (!definitions.Contains(result))
                        {
                            _ = uses.Add(result);
                        }
                        break;
                    }

                    case 0xE3:
                        if (instruction[1] != 0)
                        {
                            throw Invalid(
                                "A registered-query loop branch has nonzero control bits.");
                        }
                        break;

                    case 0xE4:
                    {
                        var condition = instruction[1] & 0x0F;
                        if ((instruction[1] & 0xF0) != 0
                            || condition >= 14)
                        {
                            throw Invalid(
                                "A registered-query loop branch has invalid condition bits.");
                        }
                        if (!definitions.Contains(condition))
                        {
                            _ = uses.Add(condition);
                        }
                        break;
                    }

                    case 0xB6:
                    case 0xEA:
                        break;

                    case 0xC2:
                    {
                        var payload = BinaryPrimitives
                            .ReadUInt16LittleEndian(instruction[2..]);
                        var destination = payload & 0x0F;
                        var source0 = (payload >> 8) & 0x0F;
                        var source1 = payload >> 12;
                        if (destination >= 14 || source0 >= 14
                            || source1 >= 14)
                        {
                            throw Invalid(
                                "A registered-query loop comparison uses a nonbinding register.");
                        }
                        if (!definitions.Contains(source0))
                        {
                            _ = uses.Add(source0);
                        }
                        if (!definitions.Contains(source1))
                        {
                            _ = uses.Add(source1);
                        }
                        _ = definitions.Add(destination);
                        break;
                    }

                    default:
                        throw Unsupported(
                            $"Registered-query loop liveness does not admit opcode 0x{instruction[0]:X2}.");
                }

                position += instructionLength;
            }

            blockUses[blockIndex] = uses;
            blockDefinitions[blockIndex] = definitions;
        }

        var liveIn = new HashSet<int>[blockCount];
        var liveOut = new HashSet<int>[blockCount];
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            liveIn[blockIndex] = [];
            liveOut[blockIndex] = [];
        }

        var livenessChanged = true;
        while (livenessChanged)
        {
            if (remainingLivenessWork-- <= 0)
            {
                throw Unsupported(
                    "Registered-query loop liveness exceeds bounded fixed-point work.");
            }

            livenessChanged = false;
            for (var blockIndex = blockCount - 1; blockIndex >= 0;
                blockIndex--)
            {
                var nextOut = new HashSet<int>();
                foreach (var successor in successors[blockIndex])
                {
                    if (remainingLivenessWork-- <= 0)
                    {
                        throw Unsupported(
                            "Registered-query loop liveness exceeds bounded fixed-point work.");
                    }
                    nextOut.UnionWith(liveIn[successor]);
                }

                var nextIn = new HashSet<int>(blockUses[blockIndex]);
                foreach (var register in nextOut)
                {
                    if (remainingLivenessWork-- <= 0)
                    {
                        throw Unsupported(
                            "Registered-query loop liveness exceeds bounded fixed-point work.");
                    }
                    if (!blockDefinitions[blockIndex].Contains(register))
                    {
                        _ = nextIn.Add(register);
                    }
                }

                if (!liveOut[blockIndex].SetEquals(nextOut))
                {
                    liveOut[blockIndex] = nextOut;
                    livenessChanged = true;
                }
                if (!liveIn[blockIndex].SetEquals(nextIn))
                {
                    liveIn[blockIndex] = nextIn;
                    livenessChanged = true;
                }
            }
        }

        var dominators = new bool[blockCount][];
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            dominators[blockIndex] = new bool[blockCount];
            if (blockIndex == 0 || dispatchEntryBlocks[blockIndex])
            {
                dominators[blockIndex][blockIndex] = true;
            }
            else
            {
                Array.Fill(dominators[blockIndex], true);
            }
        }

        var remainingDominatorWork
            = (long)SemanticVerifier.MaximumAnalysisVisits;
        var dominatorsChanged = true;
        while (dominatorsChanged)
        {
            if (remainingDominatorWork-- <= 0)
            {
                throw Unsupported(
                    "Registered-query loop dominators exceed bounded analysis work.");
            }

            dominatorsChanged = false;
            for (var blockIndex = 1; blockIndex < blockCount; blockIndex++)
            {
                if (!reachable[blockIndex])
                {
                    continue;
                }
                if (predecessors[blockIndex].Length == 0)
                {
                    throw Invalid(
                        "A reachable registered-query loop Block has no predecessor.");
                }

                var next = new bool[blockCount];
                Array.Fill(next, true);
                foreach (var predecessor in predecessors[blockIndex])
                {
                    if (remainingDominatorWork < blockCount)
                    {
                        throw Unsupported(
                            "Registered-query loop dominators exceed bounded analysis work.");
                    }
                    remainingDominatorWork -= blockCount;
                    for (var candidate = 0; candidate < blockCount;
                        candidate++)
                    {
                        next[candidate]
                            &= dominators[predecessor][candidate];
                    }
                }

                next[blockIndex] = true;
                if (!dominators[blockIndex].AsSpan().SequenceEqual(next))
                {
                    dominators[blockIndex] = next;
                    dominatorsChanged = true;
                }
            }
        }

        var backEdges = new HashSet<(int Source, int Target)>();
        for (var sourceBlock = 0; sourceBlock < blockCount; sourceBlock++)
        {
            if (!reachable[sourceBlock])
            {
                continue;
            }
            foreach (var targetBlock in successors[sourceBlock])
            {
                if (reachable[targetBlock]
                    && dominators[sourceBlock][targetBlock])
                {
                    _ = backEdges.Add((sourceBlock, targetBlock));
                }
            }
        }
        var nonBackEdgeInDegrees = new int[blockCount];
        for (var sourceBlock = 0; sourceBlock < blockCount; sourceBlock++)
        {
            if (!reachable[sourceBlock])
            {
                continue;
            }
            foreach (var targetBlock in successors[sourceBlock])
            {
                if (reachable[targetBlock]
                    && !backEdges.Contains((sourceBlock, targetBlock)))
                {
                    nonBackEdgeInDegrees[targetBlock]++;
                }
            }
        }

        var ready = new Queue<int>();
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            if (reachable[blockIndex]
                && nonBackEdgeInDegrees[blockIndex] == 0)
            {
                ready.Enqueue(blockIndex);
            }
        }

        var orderedBlocks = 0;
        while (ready.TryDequeue(out var sourceBlock))
        {
            if (remainingDominatorWork-- <= 0)
            {
                throw Unsupported(
                    "Registered-query loop reducibility exceeds bounded analysis work.");
            }
            orderedBlocks++;
            foreach (var targetBlock in successors[sourceBlock])
            {
                if (!reachable[targetBlock])
                {
                    continue;
                }
                if (backEdges.Contains((sourceBlock, targetBlock)))
                {
                    continue;
                }
                if (--nonBackEdgeInDegrees[targetBlock] == 0)
                {
                    ready.Enqueue(targetBlock);
                }
            }
        }
        if (orderedBlocks != reachableCount)
        {
            throw Unsupported(
                "The registered-query CFG contains an irreducible loop.");
        }

        var loopHeaders = new SortedSet<int>();
        foreach (var (_, targetBlock) in backEdges)
        {
            _ = loopHeaders.Add(targetBlock);
        }

        var loops = ImmutableArray.CreateBuilder<
            StandardRegisteredQueryLoopRegion>(loopHeaders.Count);
        var remainingLoopWork
            = (long)SemanticVerifier.MaximumAnalysisVisits;
        foreach (var header in loopHeaders)
        {
            var latches = new SortedSet<int>();
            foreach (var (sourceBlock, targetBlock) in backEdges)
            {
                if (targetBlock == header)
                {
                    _ = latches.Add(sourceBlock);
                }
            }

            var members = new HashSet<int> { header };
            var pending = new Stack<int>(latches);
            while (pending.TryPop(out var member))
            {
                if (remainingLoopWork-- <= 0)
                {
                    throw Unsupported(
                        "Registered-query natural-loop reconstruction exceeds bounded work.");
                }
                if (!members.Add(member))
                {
                    continue;
                }
                foreach (var predecessor in predecessors[member])
                {
                    if (remainingLoopWork-- <= 0)
                    {
                        throw Unsupported(
                            "Registered-query natural-loop reconstruction exceeds bounded work.");
                    }
                    if (predecessor != header)
                    {
                        pending.Push(predecessor);
                    }
                }
            }

            var exits = new SortedSet<int>();
            foreach (var member in members)
            {
                foreach (var successor in successors[member])
                {
                    if (!members.Contains(successor))
                    {
                        _ = exits.Add(successor);
                    }
                }
                if (member == header)
                {
                    continue;
                }
                foreach (var predecessor in predecessors[member])
                {
                    if (!members.Contains(predecessor))
                    {
                        throw Unsupported(
                            "A registered-query natural loop has an entry edge that bypasses its header.");
                    }
                }
            }

            var latchArray = new int[latches.Count];
            latches.CopyTo(latchArray);
            var exitArray = new int[exits.Count];
            exits.CopyTo(exitArray);
            loops.Add(new StandardRegisteredQueryLoopRegion(header,
                members, latchArray, exitArray));
        }

        for (var leftIndex = 0; leftIndex < loops.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1;
                rightIndex < loops.Count; rightIndex++)
            {
                var left = loops[leftIndex].Members;
                var right = loops[rightIndex].Members;
                if (left.Overlaps(right)
                    && !left.IsSubsetOf(right)
                    && !right.IsSubsetOf(left))
                {
                    throw Unsupported(
                        "The registered-query CFG has overlapping nonnested natural loops.");
                }
            }
        }

        if (liveIn[0].Count != 0)
        {
            throw Invalid(
                "The registered-query loop entry Block reads an undefined register.");
        }
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            var entryStateRecordIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(block[8..]);
            var expectedEntryStateRecordIndex
                = blockIndex + (body.HasExceptionDirectory ? 1 : 0);
            if (entryStateRecordIndex
                != expectedEntryStateRecordIndex)
            {
                throw Invalid(
                    "A registered-query loop liveness state is not in block order.");
            }
            var entry = body.GetRecord(entryStateRecordIndex);
            var registerCount = BinaryPrimitives.ReadUInt32LittleEndian(
                entry[4..]);
            foreach (var register in liveIn[blockIndex])
            {
                var present = false;
                for (var row = 0; row < registerCount; row++)
                {
                    if (entry[8 + (row * 5)] == register)
                    {
                        present = true;
                        break;
                    }
                }
                if (!present)
                {
                    throw Invalid(
                        "A live registered-query loop register is absent from BlockEntryState.");
                }
            }
        }

        return new StandardRegisteredQueryLoopControlFlow(successors,
            predecessors, liveIn, loops.MoveToImmutable());
    }

    private static StandardRegisteredQueryLoopRegion
        ValidateStandardRegisteredQueryLoopRegion(
            StandardRegisteredQueryLoopControlFlow controlFlow,
            int blockCount)
    {
        var exitBlock = blockCount - 1;
        if (controlFlow.Loops.Length != 1
            || controlFlow.Loops[0].Header != 1
            || controlFlow.Loops[0].Members.Count != blockCount - 2
            || controlFlow.Loops[0].ExitTargets is not [var exitTarget]
            || exitTarget != exitBlock)
        {
            throw Unsupported(
                "The registered-query Body does not contain one reducible loop with one exit.");
        }

        for (var blockIndex = 1; blockIndex < exitBlock; blockIndex++)
        {
            if (!controlFlow.Loops[0].Members.Contains(blockIndex))
            {
                throw Invalid(
                    "The registered-query loop has an unstructured block outside its natural loop.");
            }
        }

        return controlFlow.Loops[0];
    }
}
