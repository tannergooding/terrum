// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private readonly record struct StandardScalarControlFlowTerminator(
        byte Opcode, int ConditionRegister, int JumpTarget,
        int EdgeRecordIndex, int TrueTarget, int FalseTarget, int HomeIndex);

    private sealed record StandardScalarControlFlowAnalysis(
        ImmutableArray<StandardScalarControlFlowTerminator> Terminators,
        bool[] LiveIn);

    private static bool IsStandardScalarControlFlowBody(
        TrRmRegisterBodyReader body, BoundFunction function,
        TrRmTypeSpecTableReader typeSpecs, TrRmImportTableReader imports,
        List<NativePointerTypeDefinition> nativePointerTypes,
        int[] structureTypeByTypeDef,
        List<CheckedReferenceTypeDefinition> checkedReferenceTypes,
        Dictionary<CheckedReferenceTypeDefinition, int>
            checkedReferenceTypeIndexes,
        IReadOnlyDictionary<uint, PrimitiveType>?
            nominalClassTypesByTypeSpecReference)
    {
        if (body.BlockCount < 2 || body.HasExceptionDirectory
            || body.UsesNativePointerLoads || body.FieldCount != 0
            || body.MethodCount != 0 || body.HomeCount < function.ParameterCount
            || body.RecordCount < body.BlockCount
            || function.DoesNotReturn
            || function.Variables.Length != function.ParameterCount
            || (body.MapMask & ~0x13) != 0
            || (function.ReturnType != PrimitiveType.Void
                && !IsStandardScalarControlFlowType(function.ReturnType)))
        {
            return false;
        }

        for (var parameter = 0; parameter < function.ParameterCount; parameter++)
        {
            var variable = function.Variables[parameter];
            if (variable.IsOut || variable.IsInit
                || !IsStandardScalarControlFlowType(variable.Type))
            {
                return false;
            }
        }

        var parameterHomes = new bool[function.ParameterCount];
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var role = home[0];
            var flags = home[1];
            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                home[2..]);
            var formalOrdinal = BinaryPrimitives.ReadUInt16LittleEndian(
                home[4..]);
            if (BinaryPrimitives.ReadUInt16LittleEndian(home[6..]) != 0
                || flags > 1 || role is not (2 or 3))
            {
                return false;
            }

            var type = GetStandardInitializationBodyType(body,
                typeMapIndex, typeSpecs, imports, nativePointerTypes,
                structureTypeByTypeDef, checkedReferenceTypes,
                checkedReferenceTypeIndexes,
                nominalClassTypesByTypeSpecReference);
            if (!IsStandardScalarControlFlowType(type))
            {
                return false;
            }

            if (role == 2)
            {
                if (formalOrdinal >= function.ParameterCount || flags != 0
                    || parameterHomes[formalOrdinal]
                    || function.Variables[formalOrdinal].Type != type)
                {
                    return false;
                }

                parameterHomes[formalOrdinal] = true;
            }
            else if (formalOrdinal != 0)
            {
                return false;
            }
        }

        if (parameterHomes.Any(static hasHome => !hasHome))
        {
            return false;
        }

        var hasConditionalBranch = false;
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            var entryStateIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                block[8..]);
            if (entryStateIndex != blockIndex)
            {
                return false;
            }

            var entryState = body.GetRecord(entryStateIndex);
            if (entryState.Length != 8 || entryState[0] != 0x01
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    entryState[4..]) != 0)
            {
                return false;
            }

            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var opcode = code[position];
                hasConditionalBranch |= opcode is 0xB1 or 0xE4;
                position += TrRmRegisterBodyReader.InstructionLength(
                    opcode, body.Schema, body.UsesNativePointerLoads);
            }
        }

        return hasConditionalBranch;
    }

    private static StandardScalarControlFlowAnalysis
        AnalyzeStandardScalarControlFlowBody(TrRmRegisterBodyReader body,
            byte[] homeRoles)
    {
        var blockCount = body.BlockCount;
        var homeCount = body.HomeCount;
        var cellCountValue = (long)blockCount * homeCount;
        if (cellCountValue > SemanticVerifier.MaximumAnalysisVisits)
        {
            throw Unsupported(
                "The Standard scalar control-flow Home analysis exceeds its bounded capacity.");
        }

        var cellCount = checked((int)cellCountValue);
        var uses = new bool[cellCount];
        var definitions = new bool[cellCount];
        var liveIn = new bool[cellCount];
        var successors = new List<int>[blockCount];
        var predecessors = new List<int>[blockCount];
        var terminators
            = ImmutableArray.CreateBuilder<
                StandardScalarControlFlowTerminator>(blockCount);
        var nextEdgeRecord = blockCount;
        var remainingWork = (long)SemanticVerifier.MaximumAnalysisVisits;

        void Visit()
        {
            if (remainingWork-- <= 0)
            {
                throw Unsupported(
                    "The Standard scalar control-flow Home analysis exceeds its bounded work limit.");
            }

            body.ResourceLedger?.ConsumeWork();
        }

        int Cell(int blockIndex, int homeIndex)
            => checked((blockIndex * homeCount) + homeIndex);

        int ReadHomeIndex(ReadOnlySpan<byte> instruction, byte opcode)
            => opcode switch {
                0x07 => 0,
                0x08 => 1,
                0x13 => instruction[1] >> 4,
                0x14 => instruction[1] & 0x0F,
                0xD5 => BinaryPrimitives.ReadUInt16LittleEndian(
                    instruction[2..]),
                0xD6 => BinaryPrimitives.ReadUInt16LittleEndian(
                    instruction[2..]),
                0xB8 => instruction[1],
                0xEB => BinaryPrimitives.ReadUInt16LittleEndian(
                    instruction[2..]),
                _ => throw new InvalidOperationException(
                    "A non-Home instruction was used as a Home reference."),
            };

        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            successors[blockIndex] = [];
            predecessors[blockIndex] = [];
            var written = new bool[homeCount];
            var code = body.GetBlockCode(blockIndex);
            StandardScalarControlFlowTerminator terminator = default;
            var foundTerminator = false;

            void ReadHome(ReadOnlySpan<byte> instruction, byte opcode)
            {
                var homeIndex = ReadHomeIndex(instruction, opcode);
                if ((uint)homeIndex >= (uint)homeCount)
                {
                    throw Invalid(
                        "A Standard scalar control-flow Home read is outside the HomeMap.");
                }

                if (homeRoles[homeIndex] == 3 && !written[homeIndex])
                {
                    uses[Cell(blockIndex, homeIndex)] = true;
                }
            }

            void WriteHome(ReadOnlySpan<byte> instruction, byte opcode)
            {
                var homeIndex = ReadHomeIndex(instruction, opcode);
                if ((uint)homeIndex >= (uint)homeCount
                    || homeRoles[homeIndex] != 3)
                {
                    throw Invalid(
                        "A Standard scalar control-flow Home store does not target a local Home.");
                }

                var cell = Cell(blockIndex, homeIndex);
                definitions[cell] = true;
                written[homeIndex] = true;
            }

            for (var position = 0; position < code.Length;)
            {
                Visit();
                var opcode = code[position];
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        body.Schema, body.UsesNativePointerLoads);
                var instruction = code.Slice(position, instructionLength);
                if (position + instructionLength == code.Length)
                {
                    var jumpTarget = -1;
                    var edgeRecordIndex = -1;
                    var trueTarget = -1;
                    var falseTarget = -1;
                    var conditionRegister = -1;
                    var homeIndex = -1;
                    switch (opcode)
                    {
                        case 0x17:
                        case 0x18:
                            if (opcode == 0x18 && instruction[1] != 0)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow unit Return has reserved operand bits.");
                            }
                            break;

                        case 0xB0:
                            jumpTarget = instruction[1];
                            break;

                        case 0xE3:
                            if (instruction[1] != 0)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow Jump has reserved control bits.");
                            }
                            jumpTarget
                                = BinaryPrimitives.ReadUInt16LittleEndian(
                                    instruction[2..]);
                            break;

                        case 0xB1:
                            conditionRegister = instruction[1] & 0x0F;
                            edgeRecordIndex = instruction[1] >> 4;
                            break;

                        case 0xE4:
                            if ((instruction[1] & 0xF0) != 0)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow Boolean branch has reserved control bits.");
                            }
                            conditionRegister = instruction[1] & 0x0F;
                            edgeRecordIndex
                                = BinaryPrimitives.ReadUInt16LittleEndian(
                                    instruction[2..]);
                            break;

                        case 0xB8:
                            homeIndex = ReadHomeIndex(instruction, opcode);
                            break;

                        case 0xEB:
                            if (instruction[1] != 0)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow Home Return has reserved control bits.");
                            }
                            homeIndex = ReadHomeIndex(instruction, opcode);
                            break;

                        default:
                            throw Unsupported(
                                "The Standard scalar control-flow Body has an unsupported terminal instruction.");
                    }

                    if (opcode is 0xB0 or 0xE3)
                    {
                        if ((uint)jumpTarget >= (uint)blockCount)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow Jump targets a Block outside the Body.");
                        }
                        successors[blockIndex].Add(jumpTarget);
                    }
                    else if (opcode is 0xB1 or 0xE4)
                    {
                        if (edgeRecordIndex != nextEdgeRecord
                            || edgeRecordIndex >= body.RecordCount)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow ConditionalEdge is not in canonical block order.");
                        }

                        nextEdgeRecord++;
                        var edge = body.GetRecord(edgeRecordIndex);
                        if (edge.Length != 8 || edge[0] != 0x06)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow branch has no ConditionalEdge record.");
                        }

                        trueTarget
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                edge[4..]);
                        falseTarget
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                edge[6..]);
                        if (trueTarget >= blockCount
                            || falseTarget >= blockCount)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow edge targets a Block outside the Body.");
                        }
                        successors[blockIndex].Add(trueTarget);
                        if (falseTarget != trueTarget)
                        {
                            successors[blockIndex].Add(falseTarget);
                        }
                    }
                    else if (opcode is 0xB8 or 0xEB)
                    {
                        ReadHome(instruction, opcode);
                    }

                    terminator = new StandardScalarControlFlowTerminator(
                        opcode, conditionRegister, jumpTarget,
                        edgeRecordIndex, trueTarget, falseTarget, homeIndex);
                    foundTerminator = true;
                    position += instructionLength;
                    break;
                }

                switch (opcode)
                {
                    case 0x07:
                    case 0x08:
                    case 0x14:
                    case 0xD6:
                        if (opcode == 0xD6 && (instruction[1] >> 4) != 1)
                        {
                            throw Unsupported(
                                "The Standard scalar control-flow Body requires CopyRead Home stores.");
                        }
                        WriteHome(instruction, opcode);
                        break;

                    case 0x13:
                    case 0xD5:
                        if (opcode == 0xD5 && (instruction[1] & 0xF0) != 0)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow Home load has reserved control bits.");
                        }
                        ReadHome(instruction, opcode);
                        break;
                }

                position += instructionLength;
            }

            if (!foundTerminator)
            {
                throw Invalid(
                    "A Standard scalar control-flow Block has no terminal instruction.");
            }

            terminators.Add(terminator);
        }

        if (nextEdgeRecord != body.RecordCount)
        {
            throw Invalid(
                "A Standard scalar control-flow Body contains unused records.");
        }

        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            foreach (var successor in successors[blockIndex])
            {
                Visit();
                predecessors[successor].Add(blockIndex);
            }
        }

        var reachable = new bool[blockCount];
        var reachabilityQueue = new Queue<int>();
        reachable[0] = true;
        reachabilityQueue.Enqueue(0);
        while (reachabilityQueue.TryDequeue(out var blockIndex))
        {
            Visit();
            foreach (var successor in successors[blockIndex])
            {
                Visit();
                if (!reachable[successor])
                {
                    reachable[successor] = true;
                    reachabilityQueue.Enqueue(successor);
                }
            }
        }

        if (Array.Exists(reachable, static isReachable => !isReachable))
        {
            throw Invalid(
                "A Standard scalar control-flow Body contains an unreachable Block.");
        }

        var livenessQueue = new Queue<int>();
        var queued = new bool[blockCount];
        for (var blockIndex = blockCount - 1; blockIndex >= 0; blockIndex--)
        {
            livenessQueue.Enqueue(blockIndex);
            queued[blockIndex] = true;
        }

        while (livenessQueue.TryDequeue(out var blockIndex))
        {
            queued[blockIndex] = false;
            var changed = false;
            for (var homeIndex = 0; homeIndex < homeCount; homeIndex++)
            {
                Visit();
                if (homeRoles[homeIndex] != 3)
                {
                    continue;
                }

                var liveOut = false;
                foreach (var successor in successors[blockIndex])
                {
                    Visit();
                    liveOut |= liveIn[Cell(successor, homeIndex)];
                }

                var cell = Cell(blockIndex, homeIndex);
                if ((uses[cell] || (liveOut && !definitions[cell]))
                    && !liveIn[cell])
                {
                    liveIn[cell] = true;
                    changed = true;
                }
            }

            if (changed)
            {
                foreach (var predecessor in predecessors[blockIndex])
                {
                    Visit();
                    if (!queued[predecessor])
                    {
                        queued[predecessor] = true;
                        livenessQueue.Enqueue(predecessor);
                    }
                }
            }
        }

        return new StandardScalarControlFlowAnalysis(
            terminators.MoveToImmutable(), liveIn);
    }

    private static TrRmScalarGraph DecodeStandardScalarControlFlowBody(
        TrRmRegisterBodyReader body, BoundFunction function,
        ImmutableArray<BoundVariable>.Builder variables,
        TrRmTypeSpecTableReader typeSpecs, TrRmImportTableReader imports,
        List<NativePointerTypeDefinition> nativePointerTypes,
        int[] structureTypeByTypeDef,
        List<CheckedReferenceTypeDefinition> checkedReferenceTypes,
        Dictionary<CheckedReferenceTypeDefinition, int>
            checkedReferenceTypeIndexes,
        IReadOnlyDictionary<uint, PrimitiveType>?
            nominalClassTypesByTypeSpecReference,
        ImmutableArray<BoundConstant> scalarConstants)
    {
        if (scalarConstants.IsDefault
            || scalarConstants.Length != body.ConstantCount
            || !IsStandardScalarControlFlowBody(body, function, typeSpecs,
                imports, nativePointerTypes, structureTypeByTypeDef,
                checkedReferenceTypes, checkedReferenceTypeIndexes,
                nominalClassTypesByTypeSpecReference))
        {
            throw Unsupported(
                "The selected Standard scalar control-flow Body is outside the supported block, Home and value subset.");
        }

        var homeFormalVariables = new int[body.HomeCount];
        var homeTypes = new PrimitiveType[body.HomeCount];
        var homeRoles = new byte[body.HomeCount];
        Array.Fill(homeFormalVariables, -1);
        var parameterHomes = new bool[function.ParameterCount];
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var role = home[0];
            var flags = home[1];
            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                home[2..]);
            var formalOrdinal = BinaryPrimitives.ReadUInt16LittleEndian(
                home[4..]);
            if (flags > 1
                || BinaryPrimitives.ReadUInt16LittleEndian(home[6..]) != 0)
            {
                throw Invalid(
                    "A Standard scalar control-flow HomeMap row has reserved bits.");
            }
            var type = GetStandardInitializationBodyType(body,
                typeMapIndex, typeSpecs, imports, nativePointerTypes,
                structureTypeByTypeDef, checkedReferenceTypes,
                checkedReferenceTypeIndexes,
                nominalClassTypesByTypeSpecReference);
            homeRoles[homeIndex] = role;
            homeTypes[homeIndex] = type;
            if (role == 2)
            {
                parameterHomes[formalOrdinal] = true;
                homeFormalVariables[homeIndex] = formalOrdinal;
            }
        }

        if (parameterHomes.Any(static hasHome => !hasHome))
        {
            throw Invalid(
                "A Standard scalar control-flow Body is missing a formal Home.");
        }

        var analysis = AnalyzeStandardScalarControlFlowBody(body,
            homeRoles);
        var cellCount = checked(body.BlockCount * body.HomeCount);
        var blockParameterVariableByCell = new int[cellCount];
        Array.Fill(blockParameterVariableByCell, -1);
        var blockParameterHomes
            = new ImmutableArray<int>[body.BlockCount];
        var graphParameters
            = new ImmutableArray<int>[body.BlockCount];
        int Cell(int blockIndex, int homeIndex)
            => checked((blockIndex * body.HomeCount) + homeIndex);

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var homeIndexes = ImmutableArray.CreateBuilder<int>();
            var valueIndexes = ImmutableArray.CreateBuilder<int>();
            for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
            {
                var cell = Cell(blockIndex, homeIndex);
                if (homeRoles[homeIndex] != 3 || !analysis.LiveIn[cell])
                {
                    continue;
                }
                if (blockIndex == 0)
                {
                    throw Invalid(
                        "A Standard scalar control-flow local Home is read before its initial definition.");
                }

                var variable = variables.Count;
                if (variable >= SemanticVerifier.MaximumVariables)
                {
                    throw Unsupported(
                        "The Standard scalar control-flow value inventory exceeds its limit.");
                }
                variables.Add(new BoundVariable(
                    $"$trrm.home{homeIndex + 1}.block{blockIndex + 1}",
                    homeTypes[homeIndex], false, default));
                blockParameterVariableByCell[cell] = variable;
                homeIndexes.Add(homeIndex);
                valueIndexes.Add(variable + 1);
            }

            blockParameterHomes[blockIndex] = homeIndexes.ToImmutable();
            graphParameters[blockIndex] = valueIndexes.ToImmutable();
        }

        var graphBlocks = ImmutableArray.CreateBuilder<TrRmScalarBlock>(
            body.BlockCount);
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var operations = ImmutableArray.CreateBuilder<BoundStatement>();
            var registerVariables = new int[14];
            Array.Fill(registerVariables, -1);
            var homeValueVariables = new int[body.HomeCount];
            Array.Fill(homeValueVariables, -1);
            for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
            {
                if (homeRoles[homeIndex] == 2)
                {
                    homeValueVariables[homeIndex]
                        = homeFormalVariables[homeIndex];
                }
                else
                {
                    homeValueVariables[homeIndex]
                        = blockParameterVariableByCell[
                            Cell(blockIndex, homeIndex)];
                }
            }

            var code = body.GetBlockCode(blockIndex);
            var blockTerminator = analysis.Terminators[blockIndex];
            var condition = 0;
            TrRmScalarEdge? whenTrue = null;
            TrRmScalarEdge? whenFalse = null;
            var returnValue = 0;
            var terminated = false;

            int ReadRegister(int register)
            {
                if ((uint)register >= (uint)registerVariables.Length
                    || registerVariables[register] < 0)
                {
                    throw Invalid(
                        "A Standard scalar control-flow instruction reads an undefined register.");
                }

                return registerVariables[register];
            }

            BoundRead ReadRegisterValue(int register)
            {
                var variable = ReadRegister(register);
                return new BoundRead(variable, variables[variable].Type,
                    default);
            }

            int ReadHomeVariable(int homeIndex)
            {
                if ((uint)homeIndex >= (uint)homeValueVariables.Length)
                {
                    throw Invalid(
                        "A Standard scalar control-flow Home read is outside the HomeMap.");
                }

                var variable = homeValueVariables[homeIndex];
                if (variable < 0
                    || variables[variable].Type != homeTypes[homeIndex])
                {
                    throw Invalid(
                        "A Standard scalar control-flow Home has no compatible reaching value.");
                }

                return variable;
            }

            void WriteRegister(int register, BoundExpression value)
            {
                if ((uint)register >= (uint)registerVariables.Length
                    || !IsStandardScalarControlFlowType(value.Type))
                {
                    throw Invalid(
                        "A Standard scalar control-flow instruction produces an unsupported register value.");
                }

                var variable = variables.Count;
                if (variable >= SemanticVerifier.MaximumVariables)
                {
                    throw Unsupported(
                        "The Standard scalar control-flow value inventory exceeds its limit.");
                }
                variables.Add(new BoundVariable(
                    $"$trrm.value{variable + 1}", value.Type, false, default));
                operations.Add(new BoundLocal(variable, value, default));
                registerVariables[register] = variable;
            }

            void StoreHome(int homeIndex, int sourceRegister)
            {
                if ((uint)homeIndex >= (uint)homeValueVariables.Length
                    || homeRoles[homeIndex] != 3)
                {
                    throw Invalid(
                        "A Standard scalar control-flow Body stores through a non-local Home.");
                }

                var value = ReadRegisterValue(sourceRegister);
                if (value.Type != homeTypes[homeIndex])
                {
                    throw Invalid(
                        "A Standard scalar control-flow Home store changes the value type.");
                }

                homeValueVariables[homeIndex] = ReadRegister(sourceRegister);
            }

            TrRmScalarEdge BuildEdge(int target)
            {
                if ((uint)target >= (uint)body.BlockCount)
                {
                    throw Invalid(
                        "A Standard scalar control-flow edge targets a Block outside the Body.");
                }

                var targetHomes = blockParameterHomes[target];
                var arguments
                    = ImmutableArray.CreateBuilder<int>(targetHomes.Length);
                foreach (var homeIndex in targetHomes)
                {
                    var sourceVariable = ReadHomeVariable(homeIndex);
                    var parameterVariable
                        = blockParameterVariableByCell[
                            Cell(target, homeIndex)];
                    if (parameterVariable < 0
                        || variables[sourceVariable].Type
                            != variables[parameterVariable].Type)
                    {
                        throw Invalid(
                            "A Standard scalar control-flow edge changes a local Home value type.");
                    }

                    arguments.Add(sourceVariable + 1);
                }

                return new TrRmScalarEdge(target + 1,
                    arguments.MoveToImmutable());
            }

            for (var position = 0; position < code.Length;)
            {
                var opcode = code[position];
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        body.Schema, body.UsesNativePointerLoads);
                var instruction = code.Slice(position, instructionLength);
                if (opcode is 0x17 or 0x18 or 0xB0 or 0xB1 or 0xB8
                    or 0xE3 or 0xE4 or 0xEB)
                {
                    if (position + instructionLength != code.Length)
                    {
                        throw Invalid(
                            "A Standard scalar control-flow terminator is not the final instruction in its Block.");
                    }

                    switch (opcode)
                    {
                        case 0x17:
                        {
                            if (blockTerminator.Opcode != opcode)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow Return changed after analysis.");
                            }
                            var resultRegister = instruction[1] & 0x0F;
                            if ((instruction[1] & 0xF0) != 0
                                || function.ReturnType
                                    == PrimitiveType.Void)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow Return has invalid result metadata.");
                            }

                            var result = ReadRegister(resultRegister);
                            if (variables[result].Type
                                != function.ReturnType)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow Return changes the MethodSig result type.");
                            }

                            returnValue = result + 1;
                            break;
                        }
                        case 0x18:
                            if (blockTerminator.Opcode != opcode
                                || instruction[1] != 0
                                || function.ReturnType
                                    != PrimitiveType.Void)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow unit Return does not match the MethodSig.");
                            }
                            break;
                        case 0xB0:
                        case 0xE3:
                        {
                            var target = blockTerminator.JumpTarget;
                            if ((opcode == 0xE3 && instruction[1] != 0)
                                || blockTerminator.Opcode != opcode
                                || target < 0
                                || target >= body.BlockCount)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow Jump has an invalid target.");
                            }

                            whenTrue = BuildEdge(target);
                            break;
                        }
                        case 0xB1:
                        case 0xE4:
                        {
                            var control = instruction[1];
                            var conditionRegister = control & 0x0F;
                            if (blockTerminator.Opcode != opcode
                                || blockTerminator.ConditionRegister
                                    != conditionRegister)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow branch changed after analysis.");
                            }
                            if (opcode == 0xB1)
                            {
                                if ((control >> 4)
                                    != blockTerminator.EdgeRecordIndex)
                                {
                                    throw Invalid(
                                        "A Standard scalar control-flow branch changed its ConditionalEdge index.");
                                }
                            }
                            else
                            {
                                if ((control & 0xF0) != 0)
                                {
                                    throw Invalid(
                                        "A Standard scalar control-flow Boolean branch has reserved control bits.");
                                }
                                if (BinaryPrimitives
                                        .ReadUInt16LittleEndian(
                                            instruction[2..])
                                    != blockTerminator.EdgeRecordIndex)
                                {
                                    throw Invalid(
                                        "A Standard scalar control-flow branch changed its ConditionalEdge index.");
                                }
                            }

                            var conditionVariable
                                = ReadRegister(conditionRegister);
                            if (variables[conditionVariable].Type
                                != PrimitiveType.Bool)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow branch condition is not Boolean.");
                            }

                            condition = conditionVariable + 1;
                            whenTrue = BuildEdge(blockTerminator.TrueTarget);
                            whenFalse = BuildEdge(
                                blockTerminator.FalseTarget);
                            break;
                        }
                        case 0xB8:
                        case 0xEB:
                        {
                            var homeIndex = opcode == 0xB8
                                ? instruction[1]
                                : BinaryPrimitives.ReadUInt16LittleEndian(
                                    instruction[2..]);
                            if ((opcode == 0xEB && instruction[1] != 0)
                                || blockTerminator.Opcode != opcode
                                || function.ReturnType
                                    == PrimitiveType.Void
                                || (uint)homeIndex
                                    >= (uint)homeValueVariables.Length
                                || blockTerminator.HomeIndex != homeIndex
                                || homeTypes[homeIndex]
                                    != function.ReturnType)
                            {
                                throw Invalid(
                                    "A Standard scalar control-flow Home Return does not match the MethodSig.");
                            }

                            returnValue
                                = ReadHomeVariable(homeIndex) + 1;
                            break;
                        }
                    }

                    terminated = true;
                    break;
                }

                switch (opcode)
                {
                    case 0x07:
                        StoreHome(0, 0);
                        break;

                    case 0x08:
                        StoreHome(1, 1);
                        break;

                    case 0x09:
                        if (scalarConstants.IsEmpty)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow Body loads a missing ConstantMap entry.");
                        }
                        WriteRegister(0, scalarConstants[0]);
                        break;

                    case 0x11:
                    {
                        var destination = instruction[1] & 0x0F;
                        var formalOrdinal = instruction[1] >> 4;
                        if (formalOrdinal >= function.ParameterCount
                            || function.Variables[formalOrdinal].IsOut
                            || function.Variables[formalOrdinal].IsInit)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow formal load is outside the value signature.");
                        }

                        WriteRegister(destination, new BoundRead(
                            formalOrdinal,
                            function.Variables[formalOrdinal].Type, default));
                        break;
                    }

                    case 0x13:
                    {
                        var destination = instruction[1] & 0x0F;
                        var homeIndex = instruction[1] >> 4;
                        if ((uint)homeIndex >= (uint)homeValueVariables.Length)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow Home load is outside the HomeMap.");
                        }

                        WriteRegister(destination, new BoundRead(
                            ReadHomeVariable(homeIndex), homeTypes[homeIndex],
                            default));
                        break;
                    }

                    case 0x14:
                        StoreHome(instruction[1] & 0x0F,
                            instruction[1] >> 4);
                        break;

                    case 0x16:
                    {
                        var destination = instruction[1] & 0x0F;
                        var constantIndex = instruction[1] >> 4;
                        if (constantIndex >= scalarConstants.Length)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow ConstantMap load is outside the Map.");
                        }

                        WriteRegister(destination,
                            scalarConstants[constantIndex]);
                        break;
                    }

                    case 0x10:
                    {
                        var destination = instruction[1] & 0x0F;
                        var source = instruction[1] >> 4;
                        WriteRegister(destination, ReadRegisterValue(source));
                        break;
                    }

                    case 0xC0:
                        DecodeArithmetic(instruction, ReadRegisterValue,
                            WriteRegister);
                        break;

                    case 0xC1:
                        DecodeBitOperation(instruction, ReadRegisterValue,
                            WriteRegister);
                        break;

                    case 0xC2:
                        DecodeComparison(instruction, ReadRegisterValue,
                            WriteRegister);
                        break;

                    case 0xD3:
                    {
                        var control = instruction[1];
                        var source = control & 0x0F;
                        var destination = control >> 4;
                        var typeMapIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                instruction[2..]);
                        var operand = ReadRegisterValue(source);
                        var resultType = GetStandardInitializationBodyType(
                            body, typeMapIndex, typeSpecs, imports,
                            nativePointerTypes, structureTypeByTypeDef,
                            checkedReferenceTypes,
                            checkedReferenceTypeIndexes,
                            nominalClassTypesByTypeSpecReference);
                        if (!PrimitiveTypeFacts.IsIntegerOrWide(
                                operand.Type)
                            || !PrimitiveTypeFacts.IsIntegerOrWide(
                                resultType)
                            || PrimitiveTypeFacts.ScalarWidth(operand.Type)
                                != PrimitiveTypeFacts.ScalarWidth(resultType)
                            || operand.Type == resultType)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow bit reinterpretation has incompatible types.");
                        }

                        WriteRegister(destination,
                            new TrRmScalarReinterpret(operand, resultType));
                        break;
                    }

                    case 0xD4:
                    {
                        var control = instruction[1];
                        var destination = control & 0x0F;
                        var constantIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                instruction[2..]);
                        if ((control & 0xF0) != 0
                            || constantIndex >= scalarConstants.Length)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow ConstantMap load has invalid operands.");
                        }

                        WriteRegister(destination,
                            scalarConstants[constantIndex]);
                        break;
                    }

                    case 0xD5:
                    {
                        var control = instruction[1];
                        var destination = control & 0x0F;
                        var homeIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                instruction[2..]);
                        if ((control & 0xF0) != 0
                            || (uint)homeIndex
                                >= (uint)homeValueVariables.Length)
                        {
                            throw Invalid(
                                "A Standard scalar control-flow Home load has invalid operands.");
                        }

                        WriteRegister(destination, new BoundRead(
                            ReadHomeVariable(homeIndex), homeTypes[homeIndex],
                            default));
                        break;
                    }

                    case 0xD6:
                    {
                        var control = instruction[1];
                        if ((control >> 4) != 1)
                        {
                            throw Unsupported(
                                "The Standard scalar control-flow Body requires CopyRead Home stores.");
                        }

                        var homeIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                instruction[2..]);
                        StoreHome(homeIndex, control & 0x0F);
                        break;
                    }

                    default:
                        throw Unsupported(
                            "The Standard scalar control-flow Body contains an unsupported register operation.");
                }

                position += instructionLength;
            }

            if (!terminated)
            {
                throw Invalid(
                    "A Standard scalar control-flow Block has no supported terminator.");
            }

            graphBlocks.Add(new TrRmScalarBlock(
                graphParameters[blockIndex], operations.ToImmutable(),
                condition, whenTrue, whenFalse, returnValue, 0));
        }

        var graphTypes = ImmutableArray.CreateBuilder<PrimitiveType>(
            variables.Count);
        foreach (var variable in variables)
        {
            graphTypes.Add(variable.Type);
        }

        return TrRmScalarGraph.FromAuditedBody(graphBlocks.MoveToImmutable(),
            graphTypes.MoveToImmutable(), function.ParameterCount,
            function.ReturnType, function.DoesNotReturn);
    }

    private static void DecodeArithmetic(ReadOnlySpan<byte> instruction,
        Func<int, BoundRead> readRegister,
        Action<int, BoundExpression> writeRegister)
    {
        var control = instruction[1];
        var payload = BinaryPrimitives.ReadUInt16LittleEndian(
            instruction[2..]);
        var destination = payload & 0x0F;
        var secondResult = (payload >> 4) & 0x0F;
        var source0 = (payload >> 8) & 0x0F;
        var source1 = payload >> 12;
        var isUnary = control is 0x04 or 0x08 or 0x25;
        if (secondResult != 0x0F || (isUnary
                ? source1 != 0x0F : source1 >= 14))
        {
            throw Invalid(
                "A Standard scalar arithmetic instruction has invalid register roles.");
        }

        var left = readRegister(source0);
        if (isUnary)
        {
            var isFloatingNegation = control == 0x25;
            var expectedType = isFloatingNegation
                ? IsFloat(left.Type)
                : IsInteger(left.Type);
            if (!expectedType)
            {
                throw Invalid(
                    "A Standard scalar negation has an incompatible operand type.");
            }

            var unary = new BoundUnary(TokenKind.Minus, left, left.Type,
                default);
            if (!isFloatingNegation)
            {
                unary = unary with {
                    OverflowBehavior = control == 0x08
                        ? BoundIntegerOverflowBehavior.Wrapping
                        : BoundIntegerOverflowBehavior.Checked,
                };
            }

            writeRegister(destination, unary);
            return;
        }

        var right = readRegister(source1);
        if (right.Type != left.Type)
        {
            throw Invalid(
                "A Standard scalar arithmetic instruction has mismatched operand types.");
        }

        var isFloating = control is >= 0x20 and <= 0x24;
        if (isFloating != IsFloat(left.Type)
            || (!isFloating && !IsInteger(left.Type)))
        {
            throw Invalid(
                "A Standard scalar arithmetic control does not match its operand type.");
        }

        var operation = control switch {
            0x01 or 0x05 or 0x20 => TokenKind.Plus,
            0x02 or 0x06 or 0x21 => TokenKind.Minus,
            0x03 or 0x07 or 0x22 => TokenKind.Star,
            0x0C or 0x23 => TokenKind.Slash,
            0x0D or 0x24 => TokenKind.Percent,
            _ => throw Unsupported(
                "The Standard scalar arithmetic operation is outside the supported control subset."),
        };
        var overflowBehavior = control switch {
            0x05 or 0x06 or 0x07 => BoundIntegerOverflowBehavior.Wrapping,
            _ => BoundIntegerOverflowBehavior.Checked,
        };
        var binary = new BoundBinary(operation, left, right, left.Type,
            default);
        if (!isFloating)
        {
            binary = binary with { OverflowBehavior = overflowBehavior };
        }

        writeRegister(destination, binary);
    }

    private static void DecodeBitOperation(ReadOnlySpan<byte> instruction,
        Func<int, BoundRead> readRegister,
        Action<int, BoundExpression> writeRegister)
    {
        var control = instruction[1];
        var payload = BinaryPrimitives.ReadUInt16LittleEndian(
            instruction[2..]);
        var destination = payload & 0x0F;
        var secondResult = (payload >> 4) & 0x0F;
        var source0 = (payload >> 8) & 0x0F;
        var source1 = payload >> 12;
        var left = readRegister(source0);
        if (secondResult != 0x0F || !IsInteger(left.Type))
        {
            throw Invalid(
                "A Standard scalar bit operation has invalid result or operand types.");
        }

        if (control == 0x04)
        {
            if (source1 != 0x0F)
            {
                throw Invalid(
                    "A Standard scalar bitwise not has an unexpected second operand.");
            }

            writeRegister(destination, new BoundUnary(TokenKind.Tilde,
                left, left.Type, default));
            return;
        }

        if (control is 0x01 or 0x02 or 0x03)
        {
            var right = readRegister(source1);
            if (right.Type != left.Type)
            {
                throw Invalid(
                    "A Standard scalar bit operation has mismatched operand types.");
            }

            var token = control switch {
                0x01 => TokenKind.Ampersand,
                0x02 => TokenKind.Pipe,
                _ => TokenKind.Caret,
            };
            writeRegister(destination, new BoundBinary(token, left, right,
                left.Type, default));
            return;
        }

        if (control is not (0x05 or 0x06 or 0x07)
            || !IsInteger(readRegister(source1).Type)
            || (control == 0x06
                && left.Type is not (PrimitiveType.Int32
                    or PrimitiveType.Int64))
            || (control == 0x07
                && left.Type is not (PrimitiveType.UInt32
                    or PrimitiveType.UInt64)))
        {
            throw Invalid(
                "A Standard scalar shift has an unsupported control or operand type.");
        }

        var count = readRegister(source1);
        writeRegister(destination, new BoundBinary(
            control == 0x05 ? TokenKind.ShiftLeft : TokenKind.ShiftRight,
            left, count, left.Type, default));
    }

    private static void DecodeComparison(ReadOnlySpan<byte> instruction,
        Func<int, BoundRead> readRegister,
        Action<int, BoundExpression> writeRegister)
    {
        var control = instruction[1];
        var payload = BinaryPrimitives.ReadUInt16LittleEndian(
            instruction[2..]);
        var destination = payload & 0x0F;
        var secondResult = (payload >> 4) & 0x0F;
        var source0 = (payload >> 8) & 0x0F;
        var source1 = payload >> 12;
        if (secondResult != 0x0F || control is not (0x01 or 0x02))
        {
            throw Unsupported(
                "The Standard scalar comparison is outside the Boolean equality subset.");
        }

        var left = readRegister(source0);
        var right = readRegister(source1);
        if (left.Type != PrimitiveType.Bool
            || right.Type != PrimitiveType.Bool)
        {
            throw Invalid(
                "A Standard scalar Boolean equality has non-Boolean operands.");
        }

        writeRegister(destination, new BoundBinary(
            control == 0x01 ? TokenKind.EqualsEquals
                : TokenKind.BangEquals,
            left, right, PrimitiveType.Bool, default));
    }

    private static bool IsStandardScalarControlFlowType(
        PrimitiveType type)
        => type is PrimitiveType.Bool or PrimitiveType.Char16
            or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Float32 or PrimitiveType.Float64;
}
