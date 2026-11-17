// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private sealed record StandardRegisteredQueryStructuredLoop(
        StandardRegisteredQueryLoopRegion Region, bool IsPostTested,
        int ConditionBlock, int BodyBlock, int ContinueTarget,
        int NormalExit);

    private readonly record struct StandardRegisteredQueryDecodedBlock(
        ImmutableArray<BoundStatement> Statements, byte Terminator,
        int JumpTarget, BoundExpression? Condition, int TrueTarget,
        int FalseTarget, BoundExpression? ReturnValue,
        byte RegionExitKind = 0);

    private readonly record struct StandardRegisteredQueryDecodedSequence(
        ImmutableArray<BoundStatement> Statements, int NextBlock);

    private static ImmutableArray<BoundStatement>
        DecodeStandardRegisteredQueryStructuredLoopBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
    {
        var decoder = new StandardRegisteredQueryStructuredLoopDecoder(
            body, function, variables, typeDefs, typeSpecs, fields,
            fieldPaths, imports, nativePointerTypes,
            signatureIndexByAnchorMethod, structureTypeByTypeDef);
        return decoder.Decode();
    }

    private static bool HasStandardRegisteredQueryBackwardEdge(
        TrRmRegisterBodyReader body)
    {
        for (var block = 0; block < body.BlockCount; block++)
        {
            var code = body.GetBlockCode(block);
            var terminalOffset = -1;
            var terminalLength = 0;
            for (var position = 0; position < code.Length;)
            {
                var length = TrRmRegisterBodyReader.InstructionLength(
                    code[position], body.Schema);
                if (length > code.Length - position)
                {
                    throw Invalid(
                        "A registered-query Block contains a truncated instruction.");
                }
                terminalOffset = position;
                terminalLength = length;
                position += length;
            }
            if (terminalOffset < 0)
            {
                throw Invalid(
                    "A registered-query Body contains an empty Block.");
            }
            var terminal = code.Slice(terminalOffset, terminalLength);
            if (terminal[0] == 0xE3)
            {
                if (BinaryPrimitives.ReadUInt16LittleEndian(
                        terminal[2..]) <= block)
                {
                    return true;
                }
            }
            else if (terminal[0] == 0xE4)
            {
                var edgeIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        terminal[2..]);
                var edge = body.GetRecord(edgeIndex);
                if (edge.Length != 8 || edge[0] != 0x06)
                {
                    throw Invalid(
                        "A registered-query condition has no ConditionalEdge.");
                }
                if (BinaryPrimitives.ReadUInt16LittleEndian(edge[4..])
                        <= block
                    || BinaryPrimitives.ReadUInt16LittleEndian(edge[6..])
                        <= block)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool RequiresStructuredRegisteredQueryLoopDecoder(
        TrRmRegisterBodyReader body,
        StandardRegisteredQueryLoopControlFlow controlFlow)
    {
        if (controlFlow.Loops.IsEmpty)
        {
            return false;
        }
        if (controlFlow.Loops.Length > 2)
        {
            return true;
        }
        foreach (var loop in controlFlow.Loops)
        {
            if (loop.ExitTargets.Length > 1)
            {
                return true;
            }
        }

        var conditionalBlocks = 0;
        for (var block = 0; block < body.BlockCount; block++)
        {
            var code = body.GetBlockCode(block);
            var terminalOffset = -1;
            for (var position = 0; position < code.Length;)
            {
                terminalOffset = position;
                position += TrRmRegisterBodyReader.InstructionLength(
                    code[position], body.Schema);
            }
            if (terminalOffset >= 0 && code[terminalOffset] == 0xE4)
            {
                conditionalBlocks++;
            }
        }
        return conditionalBlocks > controlFlow.Loops.Length;
    }

    private ref struct StandardRegisteredQueryStructuredLoopDecoder
    {
        private readonly TrRmRegisterBodyReader _body;
        private readonly BoundFunction _function;
        private readonly ImmutableArray<BoundVariable>.Builder _variables;
        private readonly TrRmTypeDefTableReader _typeDefs;
        private readonly TrRmTypeSpecTableReader _typeSpecs;
        private readonly TrRmFieldDefTableReader _fields;
        private readonly TrRmFieldPathTableReader _fieldPaths;
        private readonly TrRmImportTableReader _imports;
        private readonly List<NativePointerTypeDefinition>
            _nativePointerTypes;
        private readonly int[] _signatureIndexByAnchorMethod;
        private readonly int[] _structureTypeByTypeDef;
        private readonly StandardRegisteredQueryLoopControlFlow _flow;
        private readonly int[] _immediatePostDominators;
        private readonly Dictionary<int,
            StandardRegisteredQueryStructuredLoop> _loopsByEntry;
        private readonly int[] _registerVariables;
        private readonly PrimitiveType[] _registerTypes;
        private readonly bool[] _hasRegisterTypes;
        private readonly int[] _identityRegisterTypeMapIndices;
        private readonly Dictionary<PrimitiveType, int> _identityTags;
        private readonly int[] _homeRegisters;
        private readonly PrimitiveType[] _homeTypes;
        private readonly bool[] _hasHomeTypes;
        private readonly bool[] _declaredRegisters;
        private readonly bool[] _visitedBlocks;
        private readonly HashSet<int> _referencedEdges;
        private long _remainingWork;

        internal StandardRegisteredQueryStructuredLoopDecoder(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeDefTableReader typeDefs,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] signatureIndexByAnchorMethod,
            int[] structureTypeByTypeDef)
        {
            _body = body;
            _function = function;
            _variables = variables;
            _typeDefs = typeDefs;
            _typeSpecs = typeSpecs;
            _fields = fields;
            _fieldPaths = fieldPaths;
            _imports = imports;
            _nativePointerTypes = nativePointerTypes;
            _signatureIndexByAnchorMethod = signatureIndexByAnchorMethod;
            _structureTypeByTypeDef = structureTypeByTypeDef;
            _flow = AnalyzeStandardRegisteredQueryLoopControlFlow(body);
            _immediatePostDominators
                = ComputeImmediatePostDominators(_flow.Successors);
            _loopsByEntry = [];
            _registerVariables = new int[14];
            Array.Fill(_registerVariables, -1);
            _registerTypes = new PrimitiveType[14];
            _hasRegisterTypes = new bool[14];
            _identityRegisterTypeMapIndices = new int[14];
            Array.Fill(_identityRegisterTypeMapIndices, -1);
            _identityTags = [];
            _homeRegisters = new int[body.HomeCount];
            Array.Fill(_homeRegisters, -1);
            _homeTypes = new PrimitiveType[body.HomeCount];
            _hasHomeTypes = new bool[body.HomeCount];
            _declaredRegisters = new bool[14];
            _visitedBlocks = new bool[body.BlockCount];
            _referencedEdges = [];
            _remainingWork = SemanticVerifier.MaximumAnalysisVisits;
            ReadExceptionHomeTypes();
            ReadLoopCarriedRegisters();
            ReadLoopShapes();
        }

        internal ImmutableArray<BoundStatement> Decode()
        {
            if (_body.BlockCount < 4
                || _body.BlockCount
                    > MaximumStandardRegisteredQueryFlowBlocks
                || _body.RecordCount
                    > MaximumStandardRegisteredQueryFlowRecords
                || (_body.HomeCount != 0
                    && !_body.HasExceptionDirectory)
                || _body.MethodCount != 0
                || _body.ConstantCount != 0
                || (_body.MapMask & ~0x0B) != 0
                || (_body.MapMask & 0x02) == 0
                || (_function.ReturnType != PrimitiveType.NativeInt
                    && _function.ReturnType != PrimitiveType.Bool)
                || _function.ParameterCount != _function.Variables.Length
                || !SupportsExceptionDirectory())
            {
                throw Unsupported(
                    "The structured registered-query consumer requires a bounded loop Body with a supported result and TypeIdentity carrier.");
            }
            if (_loopsByEntry.Count == 0)
            {
                throw Unsupported(
                    "The structured registered-query consumer found no validated natural-loop regions.");
            }

            var preheader = DecodeBlock(0, isPreheader: true);
            if (preheader.Terminator != 0xE3
                || preheader.JumpTarget < 0
                || !_loopsByEntry.ContainsKey(preheader.JumpTarget))
            {
                throw Invalid(
                    "The structured registered-query preheader does not enter a natural loop.");
            }
            for (var register = 0;
                register < _registerVariables.Length; register++)
            {
                if (_hasRegisterTypes[register]
                    && !_declaredRegisters[register])
                {
                    throw Invalid(
                        "A loop-carried register is not initialized by the preheader.");
                }
            }

            var body = DecodeSequence(preheader.JumpTarget,
                _body.BlockCount, [], conditionalPath: false);
            if (body.NextBlock >= 0)
            {
                throw Invalid(
                    "The structured registered-query Body does not terminate in its declared result.");
            }
            for (var block = 0; block < _visitedBlocks.Length; block++)
            {
                if (!_visitedBlocks[block])
                {
                    throw Invalid(
                        "The structured registered-query Body contains an unreachable Block.");
                }
            }
            var conditionalEdgeCount = 0;
            for (var recordIndex = 0;
                recordIndex < _body.RecordCount; recordIndex++)
            {
                if (_body.GetRecord(recordIndex)[0] == 0x06)
                {
                    conditionalEdgeCount++;
                }
            }
            if (_referencedEdges.Count != conditionalEdgeCount)
            {
                throw Invalid(
                    "The structured registered-query Body has an unused ConditionalEdge.");
            }

            return [.. preheader.Statements, .. body.Statements];
        }

        private readonly bool SupportsExceptionDirectory()
        {
            if (!_body.HasExceptionDirectory)
            {
                return true;
            }

            var directory = _body.ExceptionDirectory;
            if (directory.HandlerCount != 0)
            {
                return false;
            }
            for (var regionIndex = 1;
                regionIndex < directory.RegionCount; regionIndex++)
            {
                var region = directory.GetRegion(regionIndex);
                if (region.Kind == 1)
                {
                    if (region.Flags != 1 || region.HandlerCount != 0)
                    {
                        return false;
                    }
                }
                else if (region.Kind != 4)
                {
                    return false;
                }
            }
            return true;
        }

        private void ReadExceptionHomeTypes()
        {
            for (var homeIndex = 0; homeIndex < _body.HomeCount;
                homeIndex++)
            {
                CountWork();
                var home = _body.HomeMap.Slice(homeIndex * 8, 8);
                if (home[0] != 3 || home[1] != 1
                    || BinaryPrimitives.ReadUInt16LittleEndian(
                        home[6..]) != 0)
                {
                    throw Unsupported(
                        "A registered-query exception Home is outside the mutable local scalar form.");
                }

                var typeMapIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(home[2..]);
                var type = ReadStandardRegisteredQueryType(_body,
                    typeMapIndex, _typeSpecs, _nativePointerTypes,
                    _signatureIndexByAnchorMethod,
                    _structureTypeByTypeDef, _imports);
                if (type is not (PrimitiveType.Bool
                    or PrimitiveType.NativeInt))
                {
                    throw Unsupported(
                        "A registered-query exception Home has an unsupported value type.");
                }
                _homeTypes[homeIndex] = type;
                _hasHomeTypes[homeIndex] = true;
            }
        }

        private void ReadLoopCarriedRegisters()
        {
            if (_flow.Loops.IsEmpty)
            {
                throw Unsupported(
                    "The structured registered-query reader requires a natural-loop region.");
            }

            for (var block = 0; block < _body.BlockCount; block++)
            {
                CountWork();
                var blockEntry = _body.GetBlock(block);
                var entryStateRecordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        blockEntry[8..]);
                var record = _body.GetRecord(entryStateRecordIndex);
                if (record.Length < 8 || record[0] != 0x01
                    || record[1] != 0 || record[2] != 0
                    || record[3] != 0)
                {
                    throw Invalid(
                        "A structured registered-query BlockEntryState has an invalid header.");
                }
                var count = BinaryPrimitives.ReadUInt32LittleEndian(
                    record[4..]);
                if (count > 14
                    || record.Length != 8 + (count * 5))
                {
                    throw Invalid(
                        "A structured registered-query BlockEntryState has an invalid extent.");
                }
                for (var row = 0; row < count; row++)
                {
                    CountWork();
                    var offset = 8 + (row * 5);
                    var register = record[offset];
                    var carrierKind = record[offset + 1];
                    if (register >= _registerVariables.Length
                        || carrierKind is not (0 or 2)
                        || record[offset + 4] != 0)
                    {
                        throw Unsupported(
                            "A structured registered-query loop carries an unsupported register type.");
                    }

                    var typeMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            record[(offset + 2)..]);
                    var type = carrierKind == 2
                        ? PrimitiveType.TypeIdentity
                        : ReadStandardRegisteredQueryType(_body,
                            typeMapIndex, _typeSpecs, _nativePointerTypes,
                            _signatureIndexByAnchorMethod,
                            _structureTypeByTypeDef, _imports);
                    if ((carrierKind == 2 && typeMapIndex != 0)
                        || (carrierKind == 0
                            && type == PrimitiveType.TypeIdentity))
                    {
                        throw Invalid(
                            "A structured registered-query loop has an invalid TypeIdentity carrier.");
                    }
                    if (type is not (PrimitiveType.Bool
                        or PrimitiveType.NativeInt
                        or PrimitiveType.TypeIdentity))
                    {
                        throw Unsupported(
                            "A structured registered-query loop carries an unsupported register type.");
                    }
                    if (_hasRegisterTypes[register]
                        && _registerTypes[register] != type)
                    {
                        throw Invalid(
                            "A loop-carried register changes type across Blocks.");
                    }
                    _registerTypes[register] = type;
                    _hasRegisterTypes[register] = true;
                }
            }

            for (var register = 0;
                register < _registerVariables.Length; register++)
            {
                if (!_hasRegisterTypes[register])
                {
                    continue;
                }

                var name = GetUniqueStandardRegisteredQueryLocalName(
                    _variables, "__trrm_loop_register_");
                _registerVariables[register] = _variables.Count;
                _variables.Add(new BoundVariable(name,
                    _registerTypes[register]
                            == PrimitiveType.TypeIdentity
                        ? PrimitiveType.NativeInt
                        : _registerTypes[register],
                    true, default));
            }
        }

        private void ReadLoopShapes()
        {
            foreach (var region in _flow.Loops)
            {
                CountWork();
                List<(int Block, int FalseTarget)> postTestedCandidates = [];
                foreach (var latch in region.Latches)
                {
                    var (opcode, _, trueTarget, falseTarget)
                        = ReadTerminal(latch);
                    if (opcode == 0xE4
                        && trueTarget == region.Header
                        && !region.Members.Contains(falseTarget))
                    {
                        postTestedCandidates.Add((latch,
                            falseTarget));
                    }
                }

                StandardRegisteredQueryStructuredLoop loop;
                if (postTestedCandidates.Count == 1)
                {
                    var (conditionBlock, normalExit)
                        = postTestedCandidates[0];
                    loop = new StandardRegisteredQueryStructuredLoop(
                        region, IsPostTested: true, conditionBlock,
                        BodyBlock: region.Header,
                        ContinueTarget: conditionBlock, normalExit);
                }
                else if (postTestedCandidates.Count == 0)
                {
                    var (opcode, _, trueTarget, falseTarget)
                        = ReadTerminal(region.Header);
                    if (opcode != 0xE4
                        || !region.Members.Contains(trueTarget)
                        || region.Members.Contains(falseTarget))
                    {
                        throw Unsupported(
                            "A structured registered-query loop has no unique pretest/post-test condition Block.");
                    }
                    var iteratorCandidates = new List<int>();
                    foreach (var latch in region.Latches)
                    {
                        if (latch == region.Header)
                        {
                            continue;
                        }
                        var (latchOpcode, latchTarget, _, _)
                            = ReadTerminal(latch);
                        if (latchOpcode == 0xE3
                            && latchTarget == region.Header)
                        {
                            iteratorCandidates.Add(latch);
                        }
                    }
                    var continueTarget = iteratorCandidates.Count == 1
                        ? iteratorCandidates[0] : region.Header;
                    loop = new StandardRegisteredQueryStructuredLoop(
                        region, IsPostTested: false,
                        ConditionBlock: region.Header,
                        BodyBlock: trueTarget,
                        ContinueTarget: continueTarget,
                        NormalExit: falseTarget);
                }
                else
                {
                    throw Unsupported(
                        "A structured registered-query loop has multiple post-test condition Blocks.");
                }
                if (!_loopsByEntry.TryAdd(region.Header, loop))
                {
                    throw Invalid(
                        "Two structured registered-query loops share a header.");
                }
            }
        }

        private StandardRegisteredQueryDecodedSequence DecodeSequence(
            int startBlock, int stopBlock,
            List<StandardRegisteredQueryStructuredLoop> activeLoops,
            bool conditionalPath, int skipProtectedRegion = 0)
        {
            var statements = ImmutableArray.CreateBuilder<BoundStatement>();
            var currentBlock = startBlock;
            while (currentBlock != stopBlock)
            {
                CountWork();
                if ((uint)currentBlock >= (uint)_body.BlockCount)
                {
                    throw Invalid(
                        "A structured registered-query path leaves the Body before its join.");
                }
                var protectedRegionIndex
                    = GetProtectedRegionEntry(currentBlock);
                if (protectedRegionIndex != 0
                    && protectedRegionIndex != skipProtectedRegion)
                {
                    var directory = _body.ExceptionDirectory;
                    var protectedRegion = directory.GetRegion(
                        protectedRegionIndex);
                    if (protectedRegion.Kind != 1
                        || (protectedRegion.Flags & 1) == 0
                        || protectedRegion.HandlerCount != 0)
                    {
                        throw Unsupported(
                            "The registered-query E1 consumer requires a handler-free Protected region with a Finally.");
                    }

                    var finallyRegionIndex
                        = protectedRegion.FinallyRegionIndex;
                    var finallyRegion = directory.GetRegion(
                        finallyRegionIndex);
                    if (finallyRegion.Kind != 4
                        || finallyRegion.ParentRegionIndex
                            != protectedRegionIndex)
                    {
                        throw Invalid(
                            "A registered-query Protected region has no matching Finally region.");
                    }

                    var protectedBody = DecodeSequence(
                        protectedRegion.EntryBlock, stopBlock, activeLoops,
                        conditionalPath: false,
                        skipProtectedRegion: protectedRegionIndex);
                    var finallyBody = DecodeSequence(
                        finallyRegion.EntryBlock, _body.BlockCount,
                        activeLoops, conditionalPath: false);
                    if (finallyBody.NextBlock >= 0)
                    {
                        throw Invalid(
                            "A registered-query Finally region has an ordinary continuation.");
                    }

                    statements.Add(new BoundTryFinally(
                        new BoundBlock(protectedBody.Statements, default),
                        new BoundBlock(finallyBody.Statements, default),
                        default));
                    if (protectedBody.NextBlock < 0)
                    {
                        return new StandardRegisteredQueryDecodedSequence(
                            statements.ToImmutable(), -1);
                    }
                    currentBlock = protectedBody.NextBlock;
                    conditionalPath = false;
                    continue;
                }
                if (conditionalPath
                    && TryGetTransferTarget(currentBlock, activeLoops,
                        out var targetedTransfer))
                {
                    statements.Add(targetedTransfer);
                    return new StandardRegisteredQueryDecodedSequence(
                        statements.ToImmutable(), -1);
                }

                if (_loopsByEntry.TryGetValue(currentBlock,
                        out var nestedLoop)
                    && (activeLoops.Count == 0
                        || !ReferenceEquals(activeLoops[^1], nestedLoop)))
                {
                    statements.Add(DecodeLoop(nestedLoop, activeLoops));
                    currentBlock = nestedLoop.NormalExit;
                    conditionalPath = true;
                    continue;
                }

                var decoded = DecodeBlock(currentBlock,
                    isPreheader: false);
                statements.AddRange(decoded.Statements);
                switch (decoded.Terminator)
                {
                    case 0xE3:
                        if (decoded.JumpTarget == stopBlock
                            && !conditionalPath)
                        {
                            return new StandardRegisteredQueryDecodedSequence(
                                statements.ToImmutable(), stopBlock);
                        }
                        if (TryGetTransferTarget(decoded.JumpTarget,
                                activeLoops, out var regionTransfer))
                        {
                            statements.Add(regionTransfer);
                            return new StandardRegisteredQueryDecodedSequence(
                                statements.ToImmutable(), -1);
                        }
                        if (decoded.JumpTarget == stopBlock)
                        {
                            return new StandardRegisteredQueryDecodedSequence(
                                statements.ToImmutable(), stopBlock);
                        }
                        currentBlock = decoded.JumpTarget;
                        conditionalPath = false;
                        break;

                    case 0xE4:
                    {
                        var joinBlock
                            = _immediatePostDominators[currentBlock];
                        var regionKind = _body.HasExceptionDirectory
                            ? _body.ExceptionDirectory.GetRegion(
                                _body.ExceptionDirectory.GetBlockRegion(
                                    currentBlock)).Kind : (byte)0;
                        if (activeLoops.Count != 0
                            && regionKind is not (2 or 3 or 4)
                            && (joinBlock == _body.BlockCount
                                || !activeLoops[^1].Region.Members
                                    .Contains(joinBlock)))
                        {
                            joinBlock
                                = activeLoops[^1].ContinueTarget;
                        }
                        var entryIdentityTypeMaps
                            = (int[])_identityRegisterTypeMapIndices.Clone();
                        var whenTrue = DecodeSequence(
                            decoded.TrueTarget, joinBlock, activeLoops,
                            conditionalPath: true,
                            skipProtectedRegion);
                        var trueIdentityTypeMaps
                            = (int[])_identityRegisterTypeMapIndices.Clone();
                        Array.Copy(entryIdentityTypeMaps,
                            _identityRegisterTypeMapIndices,
                            _identityRegisterTypeMapIndices.Length);
                        var whenFalse = DecodeSequence(
                            decoded.FalseTarget, joinBlock, activeLoops,
                            conditionalPath: true,
                            skipProtectedRegion);
                        var falseIdentityTypeMaps
                            = (int[])_identityRegisterTypeMapIndices.Clone();
                        MergeIdentityBranchValues(
                            whenTrue.NextBlock >= 0,
                            trueIdentityTypeMaps,
                            whenFalse.NextBlock >= 0,
                            falseIdentityTypeMaps, entryIdentityTypeMaps);
                        var thenBlock = new BoundBlock(
                            whenTrue.Statements, default);
                        var elseBlock = whenFalse.Statements.IsEmpty
                            ? null : new BoundBlock(whenFalse.Statements,
                                default);
                        statements.Add(new BoundIf(decoded.Condition!,
                            thenBlock, elseBlock, default));
                        if (whenTrue.NextBlock < 0
                            && whenFalse.NextBlock < 0)
                        {
                            return new StandardRegisteredQueryDecodedSequence(
                                statements.ToImmutable(), -1);
                        }
                        var continuationBlock = -1;
                        if (whenTrue.NextBlock >= 0
                            && whenTrue.NextBlock != joinBlock)
                        {
                            continuationBlock = whenTrue.NextBlock;
                        }
                        if (whenFalse.NextBlock >= 0
                            && whenFalse.NextBlock != joinBlock)
                        {
                            if (continuationBlock >= 0
                                && continuationBlock != whenFalse.NextBlock)
                            {
                                throw Unsupported(
                                    "A structured registered-query conditional has distinct normal continuation Blocks.");
                            }
                            continuationBlock = whenFalse.NextBlock;
                        }
                        if (continuationBlock >= 0)
                        {
                            return new StandardRegisteredQueryDecodedSequence(
                                statements.ToImmutable(),
                                continuationBlock);
                        }
                        if (joinBlock == _body.BlockCount)
                        {
                            throw Invalid(
                                "A structured registered-query conditional has no concrete join Block.");
                        }
                        currentBlock = joinBlock;
                        conditionalPath = false;
                        break;
                    }

                    case 0xB6:
                    case 0xEA:
                    {
                        if (decoded.RegionExitKind == 4)
                        {
                            return new StandardRegisteredQueryDecodedSequence(
                                statements.ToImmutable(), -1);
                        }
                        if (decoded.RegionExitKind != 1
                            || decoded.JumpTarget < 0)
                        {
                            throw Unsupported(
                                "A structured registered-query Body uses an unsupported RegionExit kind.");
                        }
                        if (TryGetTransferTarget(decoded.JumpTarget,
                                activeLoops, out var transfer))
                        {
                            statements.Add(transfer);
                            return new StandardRegisteredQueryDecodedSequence(
                                statements.ToImmutable(), -1);
                        }
                        return new StandardRegisteredQueryDecodedSequence(
                            statements.ToImmutable(), decoded.JumpTarget);
                    }

                    case 0x17:
                        statements.Add(new BoundReturn(
                            decoded.ReturnValue, default));
                        return new StandardRegisteredQueryDecodedSequence(
                            statements.ToImmutable(), -1);

                    default:
                        throw Invalid(
                            "A structured registered-query Block has an invalid terminal instruction.");
                }
            }

            return new StandardRegisteredQueryDecodedSequence(
                statements.ToImmutable(), currentBlock);
        }

        private readonly int GetProtectedRegionEntry(int blockIndex)
        {
            if (!_body.HasExceptionDirectory)
            {
                return 0;
            }

            var regionIndex
                = _body.ExceptionDirectory.GetBlockRegion(blockIndex);
            if (regionIndex == 0)
            {
                return 0;
            }
            var region = _body.ExceptionDirectory.GetRegion(regionIndex);
            return region.Kind == 1 && region.EntryBlock == blockIndex
                ? regionIndex : 0;
        }

        private BoundWhile DecodeLoop(
            StandardRegisteredQueryStructuredLoop loop,
            List<StandardRegisteredQueryStructuredLoop> activeLoops)
        {
            List<StandardRegisteredQueryStructuredLoop> nestedLoops = [
                .. activeLoops, loop,
            ];
            BoundExpression condition;
            StandardRegisteredQueryDecodedSequence body;
            ImmutableArray<BoundStatement> iteratorStatements = [];
            if (loop.IsPostTested)
            {
                body = DecodeSequence(loop.BodyBlock,
                    loop.ContinueTarget, nestedLoops,
                    conditionalPath: false);
                var conditionBlock = DecodeBlock(loop.ConditionBlock,
                    isPreheader: false);
                if (conditionBlock.Terminator != 0xE4
                    || conditionBlock.TrueTarget != loop.Region.Header
                    || conditionBlock.FalseTarget != loop.NormalExit
                    || !conditionBlock.Statements.IsEmpty)
                {
                    throw Invalid(
                        "A structured post-test loop has an invalid condition Block.");
                }
                condition = conditionBlock.Condition!;
            }
            else
            {
                var conditionBlock = DecodeBlock(loop.ConditionBlock,
                    isPreheader: false);
                if (conditionBlock.Terminator != 0xE4
                    || conditionBlock.TrueTarget != loop.BodyBlock
                    || conditionBlock.FalseTarget != loop.NormalExit
                    || !conditionBlock.Statements.IsEmpty)
                {
                    throw Invalid(
                        "A structured pretest loop has an invalid condition Block.");
                }
                condition = conditionBlock.Condition!;
                body = DecodeSequence(loop.BodyBlock,
                    loop.ContinueTarget, nestedLoops,
                    conditionalPath: false);
                if (loop.ContinueTarget != loop.ConditionBlock)
                {
                    var iterator = DecodeSequence(loop.ContinueTarget,
                        loop.ConditionBlock, nestedLoops,
                        conditionalPath: false);
                    if (iterator.NextBlock != loop.ConditionBlock)
                    {
                        throw Invalid(
                            "A structured registered-query iterator does not reach its loop condition.");
                    }
                    iteratorStatements = iterator.Statements;
                }
            }

            if (body.NextBlock >= 0
                && body.NextBlock != loop.ContinueTarget)
            {
                throw Invalid(
                    "A structured registered-query loop body has an invalid continuation.");
            }
            return new BoundWhile(condition,
                new BoundBlock(body.Statements, default), default) {
                IsPostTested = loop.IsPostTested,
                Iterator = iteratorStatements.IsEmpty ? null
                    : new BoundBlock(iteratorStatements, default),
            };
        }

        private static bool TryGetTransferTarget(int targetBlock,
            List<StandardRegisteredQueryStructuredLoop> activeLoops,
            out BoundStatement transfer)
        {
            for (var loopIndex = activeLoops.Count - 1;
                loopIndex >= 0; loopIndex--)
            {
                var targetDepth = activeLoops.Count - loopIndex;
                var loop = activeLoops[loopIndex];
                if (targetBlock == loop.ContinueTarget)
                {
                    transfer = new BoundContinue(default) {
                        TargetDepth = targetDepth,
                    };
                    return true;
                }
                if (targetBlock == loop.NormalExit)
                {
                    transfer = new BoundBreak(default) {
                        TargetDepth = targetDepth,
                    };
                    return true;
                }
            }

            transfer = null!;
            return false;
        }

        private readonly (byte ExitKind, int TargetBlock) ReadRegionExit(
            ReadOnlySpan<byte> instruction)
        {
            if (!_body.HasExceptionDirectory
                || instruction.Length is not (2 or 4)
                || instruction[0] is not (0xB6 or 0xEA))
            {
                throw Invalid(
                    "A registered-query exception exit has an invalid instruction.");
            }

            var recordIndex = instruction[0] == 0xB6
                ? instruction[1]
                : BinaryPrimitives.ReadUInt16LittleEndian(
                    instruction[2..]);
            var record = _body.GetRecord(recordIndex);
            if (record.Length < 16 || record[0] != 0x0A)
            {
                throw Invalid(
                    "A registered-query exception exit has no RegionExit record.");
            }

            var exitKind = record[4];
            var hasTarget = record[5];
            var targetBlock = hasTarget == 0 ? -1
                : BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
            return (exitKind, targetBlock);
        }

        private StandardRegisteredQueryDecodedBlock DecodeBlock(
            int blockIndex, bool isPreheader)
        {
            if ((uint)blockIndex >= (uint)_body.BlockCount)
            {
                throw Invalid(
                    "A structured registered-query decoder leaves the Block table.");
            }
            if (_visitedBlocks[blockIndex])
            {
                throw Invalid(
                    $"A structured registered-query decoder revisits Block {blockIndex}.");
            }
            _visitedBlocks[blockIndex] = true;
            var code = _body.GetBlockCode(blockIndex);
            var terminalOffset = -1;
            var terminalLength = 0;
            for (var position = 0; position < code.Length;)
            {
                CountWork();
                var length = TrRmRegisterBodyReader.InstructionLength(
                    code[position], _body.Schema);
                if (length > code.Length - position)
                {
                    throw Invalid(
                        "A structured registered-query Block has a truncated instruction.");
                }
                terminalOffset = position;
                terminalLength = length;
                position += length;
            }
            if (terminalOffset < 0)
            {
                throw Invalid(
                    "A structured registered-query Block is empty.");
            }

            var registerValues = new BoundExpression?[14];
            var relationValues
                = new StandardRegisteredQueryRelation?[14];
            var entryStateRecordIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    _body.GetBlock(blockIndex)[8..]);
            var entry = _body.GetRecord(entryStateRecordIndex);
            var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(
                entry[4..]);
            for (var row = 0; row < entryCount; row++)
            {
                var offset = 8 + (row * 5);
                var register = entry[offset];
                if (entry[offset + 1] == 2)
                {
                    if (_registerTypes[register]
                            != PrimitiveType.TypeIdentity
                        || _registerVariables[register] < 0
                        || _identityRegisterTypeMapIndices[register] == -1)
                    {
                        throw Invalid(
                            "A structured registered-query BlockEntryState has no TypeIdentity expression value.");
                    }
                    registerValues[register] = new BoundRead(
                        _registerVariables[register],
                        PrimitiveType.NativeInt, default);
                    continue;
                }

                if (_registerVariables[register] < 0
                    || _registerTypes[register]
                        == PrimitiveType.TypeIdentity)
                {
                    throw Invalid(
                        "A structured registered-query BlockEntryState has no local variable.");
                }

                registerValues[register] = new BoundRead(
                    _registerVariables[register], _registerTypes[register],
                    default);
            }

            var statements = ImmutableArray.CreateBuilder<BoundStatement>();
            for (var position = 0; position < terminalOffset;)
            {
                CountWork();
                var length = TrRmRegisterBodyReader.InstructionLength(
                    code[position], _body.Schema);
                var instruction = code.Slice(position, length);
                switch (instruction[0])
                {
                    case 0x10:
                    {
                        var destination = instruction[1] & 0x0F;
                        var source = instruction[1] >> 4;
                        if (destination >= registerValues.Length
                            || source >= registerValues.Length)
                        {
                            throw Invalid(
                                "A structured registered-query move names an unavailable register.");
                        }
                        if (relationValues[source]
                            is StandardRegisteredQueryRelation relation)
                        {
                            if (_registerVariables[destination] >= 0
                                || _hasRegisterTypes[destination]
                                || IsTypeIdentityRegister(destination))
                            {
                                throw Invalid(
                                    "A structured registered-query relation cannot move into a typed value carrier.");
                            }
                            relationValues[destination] = relation;
                            registerValues[destination] = null;
                            break;
                        }
                        if (registerValues[source] is not
                            BoundExpression sourceValue)
                        {
                            throw Invalid(
                                "A structured registered-query move names an unavailable register.");
                        }
                        DefineRegister(destination, sourceValue,
                            isPreheader, statements, registerValues,
                            IsTypeIdentityRegister(source)
                                ? _identityRegisterTypeMapIndices[source] : -1);
                        relationValues[destination] = null;
                        break;
                    }
                    case 0x11:
                    {
                        var destination = instruction[1] & 0x0F;
                        var parameter = instruction[1] >> 4;
                        if (destination >= registerValues.Length
                            || parameter >= _function.ParameterCount
                            || _function.Variables[parameter].Type
                                != PrimitiveType.Bool
                            || _function.Variables[parameter].IsOut
                            || _function.Variables[parameter].IsInit
                            || _function.Variables[parameter].IsImplicitRef)
                        {
                            throw Invalid(
                                "A structured registered-query local reads a non-Boolean value formal.");
                        }
                        DefineRegister(destination,
                            new BoundRead(parameter, PrimitiveType.Bool,
                                default), isPreheader, statements,
                            registerValues);
                        relationValues[destination] = null;
                        break;
                    }
                    case 0xD5:
                    {
                        var destination = instruction[1];
                        var homeIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                instruction[2..]);
                        if (destination >= registerValues.Length
                            || homeIndex >= _homeRegisters.Length
                            || !_hasHomeTypes[homeIndex]
                            || _homeRegisters[homeIndex] < 0
                            || IsTypeIdentityRegister(
                                _homeRegisters[homeIndex])
                            || _registerTypes[_homeRegisters[homeIndex]]
                                != _homeTypes[homeIndex])
                        {
                            throw Invalid(
                                "A structured registered-query HomeLoad has no established scalar Home value.");
                        }
                        var homeRegister = _homeRegisters[homeIndex];
                        DefineRegister(destination,
                            new BoundRead(_registerVariables[homeRegister],
                                _homeTypes[homeIndex], default),
                            isPreheader, statements, registerValues);
                        relationValues[destination] = null;
                        break;
                    }
                    case 0xD6:
                    {
                        var source = instruction[1] & 0x0F;
                        var homeIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                instruction[2..]);
                        if ((instruction[1] >> 4) != 1
                            || source >= registerValues.Length
                            || homeIndex >= _homeRegisters.Length
                            || !_hasHomeTypes[homeIndex]
                            || registerValues[source] is not
                                BoundExpression sourceValue
                            || relationValues[source] is not null
                            || IsTypeIdentityRegister(source)
                            || sourceValue.Type != _homeTypes[homeIndex]
                            || _registerVariables[source] < 0
                            || (_homeRegisters[homeIndex] >= 0
                                && _homeRegisters[homeIndex] != source))
                        {
                            throw Invalid(
                                "A structured registered-query HomeStore does not preserve one scalar loop local.");
                        }
                        _homeRegisters[homeIndex] = source;
                        break;
                    }
                    case 0xDE:
                    {
                        var destination = instruction[1] & 0x0F;
                        if (destination >= registerValues.Length)
                        {
                            throw Invalid(
                                "A structured registered-query query defines a nonbinding register.");
                        }
                        var value = (instruction[1] >> 4) == 5
                            ? (BoundExpression)
                                ReadStandardRegisteredQueryIdentityInstruction(
                                    _body, instruction, destination,
                                    _typeSpecs, _nativePointerTypes,
                                    _signatureIndexByAnchorMethod,
                                    _structureTypeByTypeDef, _imports)
                            : ReadStandardRegisteredQueryValueInstruction(
                                _body, instruction, destination, _typeDefs,
                                _typeSpecs, _fields, _fieldPaths, _imports,
                                _nativePointerTypes,
                                _signatureIndexByAnchorMethod,
                                _structureTypeByTypeDef);
                        DefineRegister(destination, value, isPreheader,
                            statements, registerValues,
                            (instruction[1] >> 4) == 5
                                ? BinaryPrimitives.ReadUInt16LittleEndian(
                                    instruction[2..]) : -1);
                        relationValues[destination] = null;
                        break;
                    }
                    case 0xC0:
                    {
                        var operation = instruction[1];
                        var destination = instruction[2] & 0x0F;
                        var leftRegister = instruction[3] & 0x0F;
                        var rightRegister = instruction[3] >> 4;
                        if ((instruction[2] & 0xF0) != 0xF0
                            || destination >= registerValues.Length
                            || leftRegister >= registerValues.Length
                            || rightRegister >= registerValues.Length
                            || registerValues[leftRegister]
                                is not BoundExpression left
                            || registerValues[rightRegister]
                                is not BoundExpression right
                            || relationValues[leftRegister] is not null
                            || relationValues[rightRegister] is not null
                            || IsTypeIdentityRegister(leftRegister)
                            || IsTypeIdentityRegister(rightRegister)
                            || left.Type != PrimitiveType.NativeInt
                            || right.Type != PrimitiveType.NativeInt)
                        {
                            throw Invalid(
                                "A structured registered-query arithmetic operation has invalid registers or types.");
                        }
                        var (operatorKind, overflowBehavior) = operation
                            switch {
                                0x01 => (TokenKind.Plus,
                                    BoundIntegerOverflowBehavior.Checked),
                                0x02 => (TokenKind.Minus,
                                    BoundIntegerOverflowBehavior.Checked),
                                0x03 => (TokenKind.Star,
                                    BoundIntegerOverflowBehavior.Checked),
                                0x05 => (TokenKind.Plus,
                                    BoundIntegerOverflowBehavior.Wrapping),
                                0x06 => (TokenKind.Minus,
                                    BoundIntegerOverflowBehavior.Wrapping),
                                0x07 => (TokenKind.Star,
                                    BoundIntegerOverflowBehavior.Wrapping),
                                0x0C => (TokenKind.Slash,
                                    BoundIntegerOverflowBehavior.Checked),
                                0x0D => (TokenKind.Percent,
                                    BoundIntegerOverflowBehavior.Checked),
                                _ => throw Unsupported(
                                    "A structured registered-query arithmetic opcode is unsupported."),
                            };
                        DefineRegister(destination,
                            new BoundBinary(operatorKind, left, right,
                                PrimitiveType.NativeInt, default) {
                                OverflowBehavior = overflowBehavior,
                            }, isPreheader, statements, registerValues);
                        relationValues[destination] = null;
                        break;
                    }
                    case 0xC2:
                    {
                        var destination = instruction[2] & 0x0F;
                        var leftRegister = instruction[3] & 0x0F;
                        var rightRegister = instruction[3] >> 4;
                        if ((instruction[2] & 0xF0) != 0xF0
                            || destination >= registerValues.Length
                            || leftRegister >= registerValues.Length
                            || rightRegister >= registerValues.Length
                            || registerValues[leftRegister]
                                is not BoundExpression left
                            || registerValues[rightRegister]
                                is not BoundExpression right)
                        {
                            throw Invalid(
                                "A structured registered-query comparison has invalid registers.");
                        }
                        if (relationValues[leftRegister] is not null
                            || relationValues[rightRegister] is not null)
                        {
                            throw Invalid(
                                "A structured registered-query comparison uses an unconsumed relation register.");
                        }
                        var leftIsTypeIdentity
                            = IsTypeIdentityRegister(leftRegister);
                        var rightIsTypeIdentity
                            = IsTypeIdentityRegister(rightRegister);
                        if (instruction[1] == 0)
                        {
                            if (_registerVariables[destination] >= 0
                                || leftIsTypeIdentity
                                || rightIsTypeIdentity
                                || left.Type != PrimitiveType.NativeInt
                                || right.Type != PrimitiveType.NativeInt)
                            {
                                throw Invalid(
                                    "A structured registered-query ordering relation has invalid operands.");
                            }
                            relationValues[destination]
                                = new StandardRegisteredQueryRelation(
                                    left, right);
                            break;
                        }
                        var equalityOperator = instruction[1] switch {
                            1 => TokenKind.EqualsEquals,
                            2 => TokenKind.BangEquals,
                            _ => throw Unsupported(
                                "A structured registered-query comparison opcode is unsupported."),
                        };
                        var identityOperandType = leftIsTypeIdentity
                            ? left.Type : PrimitiveType.NativeInt;
                        if (leftIsTypeIdentity != rightIsTypeIdentity
                            || left.Type != identityOperandType
                            || right.Type != identityOperandType
                            || (leftIsTypeIdentity
                                && identityOperandType is not (
                                    PrimitiveType.NativeInt
                                    or PrimitiveType.TypeIdentity)))
                        {
                            throw Invalid(
                                "A structured registered-query equality compares incompatible types.");
                        }
                        DefineRegister(destination,
                            new BoundBinary(equalityOperator, left, right,
                                PrimitiveType.Bool, default), isPreheader,
                            statements, registerValues);
                        relationValues[destination] = null;
                        break;
                    }
                    case 0xC3:
                    {
                        var destination = instruction[2] & 0x0F;
                        var relationRegister = instruction[2] >> 4;
                        if (destination >= registerValues.Length
                            || relationRegister >= registerValues.Length
                            || instruction[3] != byte.MaxValue
                            || relationValues[relationRegister]
                                is not StandardRegisteredQueryRelation relation)
                        {
                            throw Invalid(
                                "A structured registered-query relation conversion has no comparison relation.");
                        }
                        if (!TryCreateStandardRelationCondition(relation,
                            instruction[1], out var condition))
                        {
                            throw Unsupported(
                                "A structured registered-query relation predicate is unsupported.");
                        }
                        DefineRegister(destination, condition, isPreheader,
                            statements, registerValues);
                        relationValues[destination] = null;
                        relationValues[relationRegister] = null;
                        break;
                    }
                    case 0x40:
                    {
                        var destination = instruction[1] & 0x0F;
                        var rightRegister = instruction[1] >> 4;
                        if (destination >= registerValues.Length
                            || rightRegister >= registerValues.Length
                            || !IsTypeIdentityRegister(destination)
                            || !IsTypeIdentityRegister(rightRegister)
                            || registerValues[destination]
                                is not BoundExpression left
                            || registerValues[rightRegister]
                                is not BoundExpression right
                            || left.Type != PrimitiveType.NativeInt
                            || right.Type != PrimitiveType.NativeInt)
                        {
                            throw Invalid(
                                "A structured registered-query short comparison requires two available TypeIdentity carriers.");
                        }

                        relationValues[destination]
                            = new StandardRegisteredQueryRelation(left, right,
                                IsTypeIdentity: true);
                        registerValues[destination] = null;
                        _identityRegisterTypeMapIndices[destination] = -1;
                        break;
                    }
                    case 0xC4:
                    {
                        var destination = instruction[2] & 0x0F;
                        var conditionRegister = instruction[2] >> 4;
                        var trueRegister = instruction[3] & 0x0F;
                        var falseRegister = instruction[3] >> 4;
                        if (destination >= registerValues.Length
                            || conditionRegister >= registerValues.Length
                            || trueRegister >= registerValues.Length
                            || falseRegister >= registerValues.Length)
                        {
                            throw Invalid(
                                "A structured registered-query select has invalid registers.");
                        }

                        BoundExpression condition;
                        if (relationValues[conditionRegister]
                            is StandardRegisteredQueryRelation relation)
                        {
                            if (!TryCreateStandardRelationCondition(relation,
                                instruction[1], out condition))
                            {
                                throw Unsupported(
                                    "A structured registered-query select tests an unsupported relation.");
                            }
                            relationValues[conditionRegister] = null;
                        }
                        else if (registerValues[conditionRegister]
                                is not BoundExpression predicate
                            || !TryCreateStandardPredicateCondition(
                                predicate, instruction[1], out condition))
                        {
                            throw Unsupported(
                                "A structured registered-query select has no supported predicate.");
                        }

                        if (relationValues[trueRegister] is not null
                            || relationValues[falseRegister] is not null
                            || registerValues[trueRegister]
                                is not BoundExpression whenTrue
                            || registerValues[falseRegister]
                                is not BoundExpression whenFalse
                            || whenTrue.Type != whenFalse.Type
                            || IsTypeIdentityRegister(trueRegister)
                                != IsTypeIdentityRegister(falseRegister)
                            || (IsTypeIdentityRegister(trueRegister)
                                && whenTrue.Type is not (
                                    PrimitiveType.NativeInt
                                    or PrimitiveType.TypeIdentity)))
                        {
                            throw Invalid(
                                "A structured registered-query select has incompatible value inputs.");
                        }

                        var isTypeIdentity
                            = IsTypeIdentityRegister(trueRegister);
                        DefineRegister(destination,
                            new BoundConditional(condition, whenTrue,
                                whenFalse, whenTrue.Type, default),
                            isPreheader, statements, registerValues,
                            isTypeIdentity ? -2 : -1);
                        relationValues[destination] = null;
                        break;
                    }
                    case 0xC5:
                    {
                        var destination = instruction[2] & 0x0F;
                        var conditionRegister = instruction[2] >> 4;
                        var valueRegister = instruction[3] & 0x0F;
                        if (destination >= registerValues.Length
                            || conditionRegister >= registerValues.Length
                            || valueRegister >= registerValues.Length
                            || (instruction[3] >> 4) != 0x0F
                            || relationValues[destination] is not null
                            || relationValues[valueRegister] is not null
                            || registerValues[destination]
                                is not BoundExpression previousValue
                            || registerValues[valueRegister]
                                is not BoundExpression selectedValue
                            || previousValue.Type != selectedValue.Type
                            || IsTypeIdentityRegister(destination)
                                != IsTypeIdentityRegister(valueRegister)
                            || (IsTypeIdentityRegister(destination)
                                && previousValue.Type is not (
                                    PrimitiveType.NativeInt
                                    or PrimitiveType.TypeIdentity)))
                        {
                            throw Invalid(
                                "A structured registered-query conditional move has incompatible value inputs.");
                        }

                        BoundExpression condition;
                        if (relationValues[conditionRegister]
                            is StandardRegisteredQueryRelation relation)
                        {
                            if (!TryCreateStandardRelationCondition(relation,
                                instruction[1], out condition))
                            {
                                throw Unsupported(
                                    "A structured registered-query conditional move tests an unsupported relation.");
                            }
                            relationValues[conditionRegister] = null;
                        }
                        else if (registerValues[conditionRegister]
                                is not BoundExpression predicate
                            || !TryCreateStandardPredicateCondition(
                                predicate, instruction[1], out condition))
                        {
                            throw Unsupported(
                                "A structured registered-query conditional move has no supported predicate.");
                        }

                        DefineRegister(destination,
                            new BoundConditional(condition, selectedValue,
                                previousValue, previousValue.Type, default),
                            isPreheader, statements, registerValues,
                            IsTypeIdentityRegister(destination) ? -2 : -1);
                        relationValues[destination] = null;
                        break;
                    }
                    default:
                        throw Unsupported(
                            $"A structured registered-query Block does not admit opcode 0x{instruction[0]:X2}.");
                }
                position += length;
            }

            var terminal = code.Slice(terminalOffset, terminalLength);
            if (terminal[0] == 0xE3)
            {
                if (terminal[1] != 0)
                {
                    throw Invalid(
                        "A structured registered-query branch has nonzero control bits.");
                }
                return new StandardRegisteredQueryDecodedBlock(
                    statements.ToImmutable(), 0xE3,
                    BinaryPrimitives.ReadUInt16LittleEndian(terminal[2..]),
                    null, -1, -1, null);
            }
            if (terminal[0] == 0xE4)
            {
                if ((terminal[1] & 0xF0) != 0)
                {
                    throw Invalid(
                        "A structured registered-query condition has reserved control bits.");
                }
                var conditionRegister = terminal[1] & 0x0F;
                if (conditionRegister >= registerValues.Length
                    || registerValues[conditionRegister] is not
                        BoundExpression condition
                    || condition.Type != PrimitiveType.Bool)
                {
                    throw Invalid(
                        "A structured registered-query branch has no Boolean condition value.");
                }
                var edgeIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        terminal[2..]);
                if (!_referencedEdges.Add(edgeIndex))
                {
                    throw Invalid(
                        "A structured registered-query Body reuses a ConditionalEdge.");
                }
                var edge = _body.GetRecord(edgeIndex);
                if (edge.Length != 8 || edge[0] != 0x06
                    || edge[1] != 0 || edge[2] != 0 || edge[3] != 0)
                {
                    throw Invalid(
                        "A structured registered-query branch has no valid ConditionalEdge.");
                }
                return new StandardRegisteredQueryDecodedBlock(
                    statements.ToImmutable(), 0xE4, -1, condition,
                    BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]),
                    null);
            }
            if (terminal[0] == 0x17)
            {
                var resultRegister = terminal[1];
                if (resultRegister >= registerValues.Length
                    || IsTypeIdentityRegister(resultRegister)
                    || relationValues[resultRegister] is not null
                    || registerValues[resultRegister] is not
                        BoundExpression result
                    || result.Type != _function.ReturnType)
                {
                    throw Invalid(
                        "A structured registered-query Return has no matching result value.");
                }
                return new StandardRegisteredQueryDecodedBlock(
                    statements.ToImmutable(), 0x17, -1, null, -1, -1,
                    result);
            }
            if (terminal[0] is 0xB6 or 0xEA)
            {
                var (exitKind, targetBlock) = ReadRegionExit(terminal);
                return new StandardRegisteredQueryDecodedBlock(
                    statements.ToImmutable(), terminal[0], targetBlock,
                    null, -1, -1, null, exitKind);
            }
            throw Unsupported(
                $"A structured registered-query Block has unsupported terminal opcode 0x{terminal[0]:X2}.");
        }

        private readonly void DefineRegister(int destination,
            BoundExpression value, bool preheader,
            ImmutableArray<BoundStatement>.Builder blockStatements,
            BoundExpression?[] registerValues,
            int identityTypeMapIndex = -1)
        {
            if ((uint)destination >= (uint)registerValues.Length)
            {
                throw Invalid(
                    "A structured registered-query definition uses a nonbinding register.");
            }
            var hasDeclaredType = _hasRegisterTypes[destination];
            var declaredIdentity = hasDeclaredType
                && _registerTypes[destination]
                    == PrimitiveType.TypeIdentity;
            var identityValue = value.Type == PrimitiveType.TypeIdentity;
            var carriesIdentity = identityValue
                || identityTypeMapIndex != -1;
            if (hasDeclaredType && declaredIdentity != carriesIdentity)
            {
                throw Invalid(
                    "A structured registered-query definition changes a loop-carried register type.");
            }
            if (hasDeclaredType && !declaredIdentity
                && value.Type != _registerTypes[destination])
            {
                throw Invalid(
                    "A structured registered-query definition changes a loop-carried register type.");
            }

            if (identityValue)
            {
                if (identityTypeMapIndex == -1
                    || identityTypeMapIndex < -2)
                {
                    throw Invalid(
                        "A structured registered-query TypeIdentity has no exact TypeOf evidence.");
                }
                value = new BoundConstant(GetTypeIdentityTag(value),
                    PrimitiveType.NativeInt, value.Span);
            }
            else if (carriesIdentity)
            {
                if (identityTypeMapIndex == -1
                    || value.Type != PrimitiveType.NativeInt)
                {
                    throw Invalid(
                        "A structured registered-query TypeIdentity transfer has no exact identity value.");
                }
            }

            if (carriesIdentity)
            {
                _identityRegisterTypeMapIndices[destination] = -2;
            }
            else
            {
                if (identityTypeMapIndex != -1)
                {
                    throw Invalid(
                        "A structured registered-query non-identity value has TypeIdentity evidence.");
                }
                _identityRegisterTypeMapIndices[destination] = -1;
            }

            var variable = _registerVariables[destination];
            if (variable < 0)
            {
                registerValues[destination] = value;
                return;
            }
            var storageType = carriesIdentity
                ? PrimitiveType.NativeInt : _registerTypes[destination];
            if (_variables[variable].Type != storageType
                || value.Type != storageType)
            {
                throw Invalid(
                    "A structured registered-query definition changes a loop-carried register storage type.");
            }
            if (!_declaredRegisters[destination])
            {
                if (!preheader)
                {
                    throw Invalid(
                        "A loop-carried register is not initialized before entering its loop.");
                }
                blockStatements.Add(new BoundLocal(variable, value, default));
                _declaredRegisters[destination] = true;
            }
            else if (value is not BoundRead { Variable: var sourceVariable }
                || sourceVariable != variable)
            {
                blockStatements.Add(new BoundAssignment(variable, value,
                    default));
            }
            registerValues[destination] = new BoundRead(variable,
                storageType, default);
        }

        private readonly bool IsTypeIdentityRegister(int register)
            => (uint)register < (uint)_registerTypes.Length
                && _identityRegisterTypeMapIndices[register] != -1;

        private readonly void MergeIdentityBranchValues(bool whenTrueIsLive,
            int[] whenTrueTypeMaps, bool whenFalseIsLive,
            int[] whenFalseTypeMaps, int[] entryTypeMaps)
        {
            if (!whenTrueIsLive && !whenFalseIsLive)
            {
                Array.Copy(entryTypeMaps, _identityRegisterTypeMapIndices,
                    _identityRegisterTypeMapIndices.Length);
                return;
            }

            for (var register = 0;
                register < _registerTypes.Length; register++)
            {
                if (whenTrueIsLive && whenFalseIsLive)
                {
                    var trueTypeMap = whenTrueTypeMaps[register];
                    var falseTypeMap = whenFalseTypeMaps[register];
                    if (trueTypeMap == -1 && falseTypeMap == -1)
                    {
                        _identityRegisterTypeMapIndices[register] = -1;
                        continue;
                    }
                    if (trueTypeMap == -1 || falseTypeMap == -1)
                    {
                        throw Invalid(
                            "A live registered-query TypeIdentity merge has an incoming edge without identity evidence.");
                    }

                    _identityRegisterTypeMapIndices[register] = -2;
                    continue;
                }

                var liveTypeMaps = whenTrueIsLive
                    ? whenTrueTypeMaps : whenFalseTypeMaps;
                if (liveTypeMaps[register] == -1)
                {
                    _identityRegisterTypeMapIndices[register] = -1;
                    continue;
                }

                _identityRegisterTypeMapIndices[register] = -2;
            }
        }

        private readonly int GetTypeIdentityTag(
            BoundExpression identityValue)
        {
            if (identityValue is not BoundTypeIdentityQuery identity)
            {
                throw Invalid(
                    "A structured registered-query TypeIdentity has no TypeOf identity.");
            }
            if (!TryGetTypeIdentityKey(identity, out var identityType))
            {
                throw Unsupported(
                    "The structured registered-query consumer requires a closed TypeIdentity subject.");
            }

            if (_identityTags.TryGetValue(identityType, out var existing))
            {
                return existing;
            }

            if (_identityTags.Count >= SemanticVerifier.MaximumAnalysisVisits)
            {
                throw Limit();
            }

            var tag = _identityTags.Count + 1;
            _identityTags.Add(identityType, tag);
            return tag;
        }

        private readonly bool TryGetTypeIdentityKey(
            BoundTypeIdentityQuery identity,
            out PrimitiveType identityType)
        {
            if (identity.Subject.TypeIdentityTerm is not
                BoundGenericMethodTypeTerm term)
            {
                identityType = identity.Subject.Type;
                return true;
            }

            if (!BoundGenericMethodTypeTerms.TryGetFormalDependencies(term,
                    out var typeDependencies)
                || !BoundGenericMethodTypeTerms
                    .TryGetConstantFormalDependencies(term,
                        out var constantDependencies))
            {
                identityType = default;
                return false;
            }

            if (!typeDependencies.IsEmpty
                || !constantDependencies.IsEmpty)
            {
                identityType = default;
                return false;
            }

            identityType = identity.Subject.Type;
            return true;
        }

        private readonly (byte Opcode, int ConditionRegister, int TrueTarget,
            int FalseTarget) ReadTerminal(int blockIndex)
        {
            if ((uint)blockIndex >= (uint)_body.BlockCount)
            {
                throw Invalid(
                    "A structured registered-query loop references an invalid Block.");
            }
            var code = _body.GetBlockCode(blockIndex);
            var terminalOffset = -1;
            var terminalLength = 0;
            for (var position = 0; position < code.Length;)
            {
                var length = TrRmRegisterBodyReader.InstructionLength(
                    code[position], _body.Schema);
                if (length > code.Length - position)
                {
                    throw Invalid(
                        "A structured registered-query Block contains a truncated instruction.");
                }
                terminalOffset = position;
                terminalLength = length;
                position += length;
            }
            if (terminalOffset < 0)
            {
                throw Invalid(
                    "A structured registered-query Block is empty.");
            }
            var terminal = code.Slice(terminalOffset, terminalLength);
            if (terminal[0] != 0xE4)
            {
                return (terminal[0], -1, -1, -1);
            }
            if ((terminal[1] & 0xF0) != 0)
            {
                throw Invalid(
                    "A structured registered-query condition has reserved control bits.");
            }
            var edgeIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    terminal[2..]);
            var edge = _body.GetRecord(edgeIndex);
            if (edge.Length != 8 || edge[0] != 0x06)
            {
                throw Invalid(
                    "A structured registered-query condition has no ConditionalEdge.");
            }
            return (0xE4, terminal[1] & 0x0F,
                BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]));
        }

        private static int[] ComputeImmediatePostDominators(
            int[][] successors)
        {
            var blockCount = successors.Length;
            var virtualExit = blockCount;
            var sets = new bool[blockCount + 1][];
            for (var block = 0; block <= blockCount; block++)
            {
                sets[block] = new bool[blockCount + 1];
                if (block == virtualExit)
                {
                    sets[block][virtualExit] = true;
                }
                else if (successors[block].Length == 0)
                {
                    sets[block][block] = true;
                    sets[block][virtualExit] = true;
                }
                else
                {
                    Array.Fill(sets[block], true);
                }
            }

            var remaining = (long)SemanticVerifier.MaximumAnalysisVisits;
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var block = blockCount - 1; block >= 0; block--)
                {
                    if (remaining-- <= 0)
                    {
                        throw Unsupported(
                            "Structured registered-query postdominators exceed bounded analysis work.");
                    }
                    if (successors[block].Length == 0)
                    {
                        continue;
                    }
                    var next = new bool[blockCount + 1];
                    Array.Fill(next, true);
                    foreach (var successor in successors[block])
                    {
                        for (var candidate = 0;
                            candidate <= blockCount; candidate++)
                        {
                            if (remaining-- <= 0)
                            {
                                throw Unsupported(
                                    "Structured registered-query postdominators exceed bounded analysis work.");
                            }
                            next[candidate] &= sets[successor][candidate];
                        }
                    }
                    next[block] = true;
                    if (!sets[block].AsSpan().SequenceEqual(next))
                    {
                        sets[block] = next;
                        changed = true;
                    }
                }
            }

            var immediate = new int[blockCount];
            for (var block = 0; block < blockCount; block++)
            {
                var candidate = -1;
                for (var possible = 0; possible <= blockCount;
                    possible++)
                {
                    if (possible == block || !sets[block][possible])
                    {
                        continue;
                    }
                    var closest = true;
                    for (var other = 0; other <= blockCount; other++)
                    {
                        if (other != block && other != possible
                            && sets[block][other]
                            && !sets[possible][other])
                        {
                            closest = false;
                            break;
                        }
                    }
                    if (closest)
                    {
                        if (candidate >= 0)
                        {
                            throw Unsupported(
                                "A structured registered-query Block has ambiguous postdominators.");
                        }
                        candidate = possible;
                    }
                }
                if (candidate < 0)
                {
                    throw Unsupported(
                        "A structured registered-query Block has no immediate postdominator.");
                }
                immediate[block] = candidate;
            }
            return immediate;
        }

        private void CountWork()
        {
            if (_remainingWork-- <= 0)
            {
                throw Unsupported(
                    "Structured registered-query decoding exceeds bounded work.");
            }
        }
    }
}
