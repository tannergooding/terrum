// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private const int NormalBackingControlSize = 32;
    internal const int NormalBackingRuntimeStateSize = 32;
    private const int NormalBackingCallPlanHeadOffset = 24;
    private const int NormalBackingCallPlanHeaderSize = 40;
    private const int NormalBackingCallParameterRecordSize = 16;
    private const int NormalBackingCallPlanPreviousOffset = 0;
    private const int NormalBackingCallPlanTargetOffset = 8;
    private const int NormalBackingCallPlanStateOffset = 12;
    private const int NormalBackingCallPlanInputCountOffset = 16;
    private const int NormalBackingCallPlanResultCountOffset = 20;
    private const int NormalBackingCallPlanResultOffset = 24;
    private const int NormalBackingCallPlanResultFlagsOffset = 32;
    private const int NormalBackingCallPlanPrepared = 1;
    private const int NormalBackingCallPlanEntered = 2;
    private const int NormalBackingCallPlanResultPrepared = 3;
    private const int NormalBackingCallPlanClosed = 4;
    private const int NormalBackingConstructionMetadataSize = 8 + ManagedClassTypes.MaximumFields;
    private const int NormalBackingPublishedOffset = 0;
    private const int NormalBackingAcquiredCountOffset = 4;
    private const int NormalBackingFieldRankOffset = 8;
    private const int NormalBackingHelperArgumentSlot = 32;
    private const int NormalBackingHelperCurrentControlSlot = 40;
    private const int NormalBackingHelperChildControlSlot = 48;
    private const int NormalBackingHelperRankSlot = 56;
    private const int NormalBackingHelperCountSlot = 64;
    private const int NormalBackingReleaseFrameSize = 120;

    private static int NormalBackingConstructionMetadataOffset(int payloadSize) =>
        (NormalBackingControlSize + payloadSize + 7) & ~7;

    internal static void ValidateNormalBackingProgram(BoundProgram program)
    {
        if (program.NativeLoweringAgreement
                != CompilationOptions.WindowsX64NormalBackingAgreement
            || program.RequiresSourceGrant
            || program.CoreTaskType is not null || !program.Interfaces.IsEmpty
            || !program.NativeFunctionPointerSignatures.IsEmpty)
        {
            throw new NotSupportedException(
                "The selected normal-backing runtime supports only source-independent scalar class storage.");
        }

        var referencedFunctions = new HashSet<int>();
        foreach (var function in program.Functions)
        {
            if (function.IndexedBody is not null
                || !BoundTreeTraversal.VisitExpressions(function.Body, expression =>
                {
                    if (expression is BoundCall call)
                    {
                        _ = referencedFunctions.Add(call.Function);
                    }
                }))
            {
                throw new NotSupportedException(
                    "The selected normal-backing runtime requires a complete source-bound function body.");
            }
        }

        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            if (function.Intrinsic is >= BoundIntrinsic.NodeTryCreate and <= BoundIntrinsic.NodeCollect)
            {
                if (referencedFunctions.Contains(functionIndex))
                {
                    throw new NotSupportedException(
                        $"The selected normal-backing runtime cannot call managed-node intrinsic '{function.Name}'.");
                }
                continue;
            }
            if (function.IsCleanup)
            {
                throw new NotSupportedException(
                    $"The selected normal-backing runtime does not support cleanup function '{function.Name}'.");
            }
            if (function.IsExternal || function.DirectNativeImport is not null
                || function.NativeCallableConvention is not null
                || function.NativeFunctionPointerType is not null
                || function.NativeVTableMethod is not null
                || function.NativeOutputRole is not null)
            {
                throw new NotSupportedException(
                    $"The selected normal-backing runtime does not support foreign function '{function.Name}'.");
            }
            if (function.Intrinsic is not (BoundIntrinsic.None or BoundIntrinsic.ClassTryCreate))
            {
                throw new NotSupportedException(
                    $"The selected normal-backing runtime does not support intrinsic function '{function.Name}'.");
            }
            if (IsNormalBackingHandle(function.ReturnType))
            {
                throw new NotSupportedException(
                    $"The selected normal-backing runtime does not support handle results from '{function.Name}'.");
            }
            if (function.ReturnType != PrimitiveType.Void
                && !IsNormalBackingRuntimeValue(function.ReturnType, program, []))
            {
                throw new NotSupportedException(
                    $"The selected normal-backing runtime does not support the result type of '{function.Name}'.");
            }

            for (var variableIndex = 0; variableIndex < function.Variables.Length; variableIndex++)
            {
                var variable = function.Variables[variableIndex];
                var isReference = CheckedReferenceTypes.IsType(variable.Type);
                var containsReference = ContainsNormalBackingReferenceValue(variable.Type, program);
                if (containsReference && variable.IsOut)
                {
                    throw new NotSupportedException(
                        "The selected normal-backing runtime does not support reference-bearing output parameters.");
                }
                if ((!IsNormalBackingHandle(variable.Type)
                        && !IsNormalBackingRuntimeValue(variable.Type, program, [])
                        && !IsNormalBackingLocalReference(variable.Type, program))
                    || (variable.IsOut && (IsNormalBackingHandle(variable.Type) || isReference)
                        && function.Intrinsic != BoundIntrinsic.ClassTryCreate))
                {
                    throw new NotSupportedException(
                        "The selected normal-backing runtime does not support this managed value route.");
                }
            }

            string? unsupportedHandleCall = null;
            var expressionsVisited = BoundTreeTraversal.VisitExpressions(function.Body, expression =>
                {
                    if (expression is not BoundCall call)
                    {
                        return;
                    }
                    var target = program.Functions[call.Function];
                    for (var index = 0; index < call.Arguments.Length; index++)
                    {
                        var argument = call.Arguments[index];
                        var isReceiver = (argument is BoundReceiverLoan or BoundMutableReceiverLoan)
                            || (index == 0 && !target.IsStatic && argument is BoundRead);
                        if ((IsNormalBackingHandle(argument.Type)
                                || (ContainsNormalBackingReferenceValue(argument.Type, program)
                                    && argument is BoundOutArgument))
                            && !isReceiver
                            && !(target.Intrinsic == BoundIntrinsic.ClassTryCreate
                                && argument is BoundOutArgument))
                        {
                            unsupportedHandleCall = $"{target.Name}({argument.GetType().Name}:{argument.Type})";
                        }
                    }
                });
            if (!expressionsVisited)
            {
                throw new NotSupportedException(
                    "The selected normal-backing runtime requires a complete bound-expression traversal.");
            }
            if (unsupportedHandleCall is not null)
            {
                throw new NotSupportedException(
                    $"The selected normal-backing runtime does not support by-value handle call handoff: {unsupportedHandleCall}.");
            }

            var unsupportedOptionalFieldStore = false;
            if (!BoundTreeTraversal.VisitStatements(function.Body, statement =>
                {
                    if (statement is BoundOptionalAssignment {
                        Place: BoundFieldRead fieldRead,
                    } && ManagedClassTypes.IsClass(fieldRead.Receiver.Type)
                        && IsNormalBackingHandle(fieldRead.Type))
                    {
                        unsupportedOptionalFieldStore = true;
                    }
                }) || unsupportedOptionalFieldStore)
            {
                throw new NotSupportedException(
                    "The selected normal-backing runtime does not support optional assignment to class handle fields.");
            }
        }

        foreach (var structure in program.Structures)
        {
            if (structure.IsUnique || structure.CleanupMethod is not null
                || !IsNormalBackingRuntimeValue(structure, program, [], []))
            {
                throw new NotSupportedException(
                    "The selected normal-backing runtime requires scalar or inline-copy structure fields.");
            }
        }

        foreach (var @class in program.Classes)
        {
            foreach (var field in @class.Fields)
            {
                var unsupportedValue = !IsNormalBackingHandle(field.Type)
                    && !IsNormalBackingRuntimeValue(field.Type, program, []);
                if (unsupportedValue)
                {
                    throw new NotSupportedException(
                        "The selected normal-backing runtime does not support this class field type.");
                }
            }
        }
    }

    private static bool IsNormalBackingRuntimeValue(PrimitiveType type, BoundProgram program,
        HashSet<int> activeStructures) =>
        IsNormalBackingRuntimeValue(type, program, activeStructures, []);

    private static bool IsNormalBackingRuntimeValue(PrimitiveType type, BoundProgram program,
        HashSet<int> activeStructures, HashSet<int> activeReferences)
    {
        if (ManagedClassTypes.IsManaged(type) || BoundGenericNominalTypeTypes.IsType(type)
            || type is PrimitiveType.OwnedInt32Buffer or PrimitiveType.ReadonlyInt32BufferView
                or PrimitiveType.MutableInt32BufferView or PrimitiveType.TokenSpan)
        {
            return false;
        }
        if (CheckedReferenceTypes.IsType(type))
        {
            return IsNormalBackingStoredReference(
                type, program, activeStructures, activeReferences);
        }
        if (NativePointerTypes.IsType(type) || NativeFunctionPointerTypes.IsType(type)
            || BoundEnumTypes.IsType(type))
        {
            return true;
        }
        if (type is >= (PrimitiveType)100 and < (PrimitiveType)ManagedClassTypes.First)
        {
            var index = (int)type - 100;
            if ((uint)index >= (uint)program.Structures.Length
                || !activeStructures.Add(index))
            {
                return false;
            }
            var structure = program.Structures[index];
            var supported = !structure.IsUnique && structure.CleanupMethod is null
                && IsNormalBackingRuntimeValue(
                    structure, program, activeStructures, activeReferences);
            _ = activeStructures.Remove(index);
            return supported;
        }
        return ValueLayout.Size(type, program.Structures, program.CheckedReferenceTypes,
            program.NativeLoweringAgreement) > 0;
    }

    private static bool IsNormalBackingRuntimeValue(BoundStruct structure, BoundProgram program,
        HashSet<int> activeStructures, HashSet<int> activeReferences)
    {
        foreach (var field in structure.Fields)
        {
            if (!IsNormalBackingRuntimeValue(
                field.Type, program, activeStructures, activeReferences))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsNormalBackingStoredReference(PrimitiveType type,
        BoundProgram program, HashSet<int> activeStructures,
        HashSet<int> activeReferences)
    {
        if (!CheckedReferenceTypeContract.IsValidType(type, program.CheckedReferenceTypes)
            || !CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                type, program.Structures, program.Classes,
                program.EnumDefinitions.Length, program.CheckedReferenceTypes))
        {
            return false;
        }
        var index = CheckedReferenceTypes.Index(type);
        if (!activeReferences.Add(index))
        {
            return true;
        }

        var reference = program.CheckedReferenceTypes[index];
        var supported = !reference.IsGarbageCollected && !reference.IsInitialization
            && (IsNormalBackingHandle(reference.ReferentType)
                || IsNormalBackingRuntimeValue(
                    reference.ReferentType, program, [], activeReferences));
        _ = activeReferences.Remove(index);
        return supported;
    }

    private static bool ContainsNormalBackingReferenceValue(
        PrimitiveType type, BoundProgram program, HashSet<int>? activeStructures = null)
    {
        if (CheckedReferenceTypes.IsType(type))
        {
            return IsNormalBackingStoredReference(
                type, program, [], []);
        }
        if ((int)type is < 100 or >= ManagedClassTypes.First)
        {
            return false;
        }
        var structureIndex = (int)type - 100;
        if ((uint)structureIndex >= (uint)program.Structures.Length)
        {
            return false;
        }
        activeStructures ??= [];
        if (!activeStructures.Add(structureIndex))
        {
            return false;
        }
        var contains = false;
        foreach (var field in program.Structures[structureIndex].Fields)
        {
            if (ContainsNormalBackingReferenceValue(field.Type, program, activeStructures))
            {
                contains = true;
                break;
            }
        }
        _ = activeStructures.Remove(structureIndex);
        return contains;
    }

    private static List<(int Offset, PrimitiveType Type)> NormalBackingReferenceLeaves(
        PrimitiveType type, BoundProgram program)
    {
        var result = new List<(int Offset, PrimitiveType Type)>();
        Collect(type, 0, []);
        return result;

        void Collect(PrimitiveType current, int offset, HashSet<int> activeStructures)
        {
            if (CheckedReferenceTypes.IsType(current))
            {
                if (!IsNormalBackingStoredReference(current, program, [], []))
                {
                    throw new InvalidOperationException(
                        "A normal-backed value contains an unsupported reference descriptor.");
                }
                result.Add((offset, current));
                return;
            }
            if ((int)current is < 100 or >= ManagedClassTypes.First)
            {
                return;
            }
            var structureIndex = (int)current - 100;
            if ((uint)structureIndex >= (uint)program.Structures.Length
                || !activeStructures.Add(structureIndex))
            {
                throw new InvalidOperationException(
                    "A normal-backed aggregate has an invalid or recursive inline layout.");
            }
            foreach (var field in program.Structures[structureIndex].Fields)
            {
                Collect(field.Type, checked(offset + field.Offset), activeStructures);
            }
            _ = activeStructures.Remove(structureIndex);
        }
    }

    private static bool IsNormalBackingHandle(PrimitiveType type) =>
        ManagedClassTypes.IsClass(type)
        || (ManagedClassTypes.IsOptional(type)
            && ManagedClassTypes.IsClass(ManagedClassTypes.Required(type)));

    private static bool IsNormalBackingLocalReference(PrimitiveType type, BoundProgram program)
        => IsNormalBackingStoredReference(type, program, [], []);

    private static bool RequiresNormalBackingCallPlan(
        BoundFunction function, BoundProgram program)
    {
        if (ContainsNormalBackingReferenceValue(function.ReturnType, program))
        {
            return true;
        }

        var parameterOffset = function.IsConstructor ? 1 : 0;
        for (var index = parameterOffset;
            index < function.ParameterCount + parameterOffset; index++)
        {
            var parameter = function.Variables[index];
            if (!parameter.IsImplicitRef && !parameter.IsOut
                && ContainsNormalBackingReferenceValue(parameter.Type, program))
            {
                return true;
            }
        }
        return false;
    }

    private static int NormalBackingCallPlanSize(
        BoundCall call, BoundProgram program)
    {
        var target = program.Functions[call.Function];
        var inputCount = 0;
        var parameterOffset = target.IsConstructor ? 1 : 0;
        for (var index = parameterOffset;
            index < target.ParameterCount + parameterOffset; index++)
        {
            var parameter = target.Variables[index];
            if (!parameter.IsImplicitRef && !parameter.IsOut)
            {
                inputCount = checked(inputCount
                    + NormalBackingReferenceLeaves(parameter.Type, program).Count);
            }
        }

        var resultCount = NormalBackingReferenceLeaves(call.Type, program).Count;
        return checked(NormalBackingCallPlanHeaderSize
            + (NormalBackingCallParameterRecordSize * target.ParameterCount)
            + (sizeof(ulong) * inputCount)
            + (sizeof(ulong) * resultCount));
    }

    private void EmitBeginNormalBackingCallPlan(
        BoundCall call, int planOffset, int[] argumentSpills, int resultSpill)
    {
        var target = _program.Functions[call.Function];
        var parameterOffset = target.IsConstructor ? 1 : 0;
        var inputCount = 0;
        for (var formal = 0; formal < target.ParameterCount; formal++)
        {
            var parameter = target.Variables[formal + parameterOffset];
            if (!parameter.IsImplicitRef && !parameter.IsOut)
            {
                inputCount = checked(inputCount
                    + NormalBackingReferenceLeaves(parameter.Type, _program).Count);
            }
        }

        var resultLeaves = NormalBackingReferenceLeaves(call.Type, _program);
        var resultCount = resultLeaves.Count;
        var planSize = NormalBackingCallPlanSize(call, _program);
        ZeroStruct(planOffset, planSize);

        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(),
            _normalBackingRuntimeOffset + NormalBackingCallPlanHeadOffset));
        _code.Bytes(0x48, 0x8B, 0x02);
        _code.Store64(planOffset + NormalBackingCallPlanPreviousOffset);
        EmitStoreNormalBackingPlanImmediate(planOffset + NormalBackingCallPlanTargetOffset,
            call.Function);
        EmitStoreNormalBackingPlanImmediate(planOffset + NormalBackingCallPlanStateOffset,
            NormalBackingCallPlanPrepared);
        EmitStoreNormalBackingPlanImmediate(planOffset + NormalBackingCallPlanInputCountOffset,
            inputCount);
        EmitStoreNormalBackingPlanImmediate(planOffset + NormalBackingCallPlanResultCountOffset,
            resultCount);

        var recordsOffset = NormalBackingCallPlanHeaderSize;
        var inputFlagsOffset = checked(recordsOffset
            + (NormalBackingCallParameterRecordSize * target.ParameterCount));
        var resultFlagsOffset = checked(inputFlagsOffset + (sizeof(ulong) * inputCount));
        if (resultCount != 0)
        {
            EmitStoreNormalBackingPlanPointer(
                planOffset, NormalBackingCallPlanResultOffset, Spill(resultSpill));
            EmitStoreNormalBackingPlanPointer(
                planOffset, NormalBackingCallPlanResultFlagsOffset,
                planOffset + resultFlagsOffset);
            for (var leafIndex = 0; leafIndex < resultLeaves.Count; leafIndex++)
            {
                var supportSlot = checked(Spill(resultSpill)
                    + resultLeaves[leafIndex].Offset + sizeof(ulong));
                if (!_managedRootFlags[_currentFunction].TryGetValue(
                    supportSlot, out var rootFlag))
                {
                    throw new InvalidOperationException(
                        "A normal-backed call result has no verified support flag.");
                }
                EmitStoreNormalBackingPlanPointer(
                    planOffset, resultFlagsOffset + (sizeof(ulong) * leafIndex), rootFlag);
            }
        }

        var inputLeafIndex = 0;
        for (var formal = 0; formal < target.ParameterCount; formal++)
        {
            var parameter = target.Variables[formal + parameterOffset];
            if (parameter.IsImplicitRef || parameter.IsOut)
            {
                continue;
            }
            var leaves = NormalBackingReferenceLeaves(parameter.Type, _program);
            if (leaves.Count == 0)
            {
                continue;
            }
            var actual = call.ArgumentIndex(formal);
            if ((uint)actual >= (uint)call.Arguments.Length
                || call.Arguments[actual] is BoundOutArgument
                    or BoundReceiverLoan or BoundMutableReceiverLoan
                    or BoundFieldLoan or BoundMutableFieldLoan)
            {
                throw new InvalidOperationException(
                    "A support-bearing call formal has no by-value staged argument.");
            }

            var stagedValue = Spill(argumentSpills[actual]);
            var parameterRecord = recordsOffset
                + (NormalBackingCallParameterRecordSize * formal);
            EmitStoreNormalBackingPlanPointer(
                planOffset, parameterRecord, stagedValue);
            EmitStoreNormalBackingPlanPointer(
                planOffset, parameterRecord + sizeof(ulong),
                planOffset + inputFlagsOffset + (sizeof(ulong) * inputLeafIndex));
            foreach (var leaf in leaves)
            {
                var supportSlot = checked(stagedValue + leaf.Offset + sizeof(ulong));
                if (!_managedRootFlags[_currentFunction].TryGetValue(
                    supportSlot, out var rootFlag))
                {
                    throw new InvalidOperationException(
                        "A normal-backed call input has no verified support flag.");
                }
                EmitStoreNormalBackingPlanPointer(
                    planOffset, inputFlagsOffset + (sizeof(ulong) * inputLeafIndex),
                    rootFlag);
                inputLeafIndex++;
            }
        }
        if (inputLeafIndex != inputCount)
        {
            throw new InvalidOperationException(
                "A normal-backed call plan has an inconsistent input map.");
        }

        _code.Address(planOffset);
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(),
            _normalBackingRuntimeOffset + NormalBackingCallPlanHeadOffset));
        _code.StorePointerField(sizeof(ulong));
    }

    private void EmitValidateNormalBackingCallPlanEntry(
        int functionIndex, BoundFunction function)
    {
        var planSlot = _normalBackingIncomingCallPlanSlots[functionIndex];
        var temporary = _normalBackingTemporarySlots[functionIndex];
        if (planSlot < 0 || temporary < 0)
        {
            throw new InvalidOperationException(
                "A support-bearing function has no incoming call-plan storage.");
        }

        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(),
            _normalBackingRuntimeOffset + NormalBackingCallPlanHeadOffset));
        _code.Bytes(0x48, 0x8B, 0x02);
        _code.Store64(planSlot);
        var failures = new List<int>();
        _code.Load64(planSlot);
        _code.Bytes(0x48, 0x85, 0xC0);
        failures.Add(_code.JumpIf(0x84));
        EmitCompareNormalBackingPlanField(planSlot,
            NormalBackingCallPlanTargetOffset, functionIndex, failures);
        EmitCompareNormalBackingPlanField(planSlot,
            NormalBackingCallPlanStateOffset, NormalBackingCallPlanPrepared, failures);

        var parameterOffset = function.IsConstructor ? 1 : 0;
        var expectedInputCount = 0;
        for (var formal = 0; formal < function.ParameterCount; formal++)
        {
            var parameter = function.Variables[formal + parameterOffset];
            if (!parameter.IsImplicitRef && !parameter.IsOut)
            {
                expectedInputCount = checked(expectedInputCount
                    + NormalBackingReferenceLeaves(parameter.Type, _program).Count);
            }
        }
        var resultLeaves = NormalBackingReferenceLeaves(function.ReturnType, _program);
        EmitCompareNormalBackingPlanField(planSlot,
            NormalBackingCallPlanInputCountOffset, expectedInputCount, failures);
        EmitCompareNormalBackingPlanField(planSlot,
            NormalBackingCallPlanResultCountOffset, resultLeaves.Count, failures);

        _code.Load64(planSlot);
        _code.PointerOffset(NormalBackingCallPlanResultOffset);
        _code.LoadPointerField(0, sizeof(ulong));
        if (resultLeaves.Count == 0)
        {
            _code.Bytes(0x48, 0x85, 0xC0);
            failures.Add(_code.JumpIf(0x85));
        }
        else
        {
            _code.Store64(temporary);
            _code.Load64(_returnSlot);
            _code.Bytes(0x48, 0x3B, 0x85);
            _code.Int32(temporary);
            failures.Add(_code.JumpIf(0x85));
            _code.Load64(planSlot);
            _code.PointerOffset(NormalBackingCallPlanResultFlagsOffset);
            _code.LoadPointerField(0, sizeof(ulong));
            _code.Bytes(0x48, 0x85, 0xC0);
            failures.Add(_code.JumpIf(0x84));
        }

        for (var formal = 0; formal < function.ParameterCount; formal++)
        {
            var slot = formal + parameterOffset;
            var parameter = function.Variables[slot];
            if (parameter.IsImplicitRef || parameter.IsOut)
            {
                continue;
            }
            var leaves = NormalBackingReferenceLeaves(parameter.Type, _program);
            if (leaves.Count == 0)
            {
                continue;
            }
            var parameterRecord = NormalBackingCallPlanHeaderSize
                + (NormalBackingCallParameterRecordSize * formal);
            _code.Load64(planSlot);
            _code.PointerOffset(parameterRecord);
            _code.LoadPointerField(0, sizeof(ulong));
            _code.Store64(temporary);
            _code.Load64(Slot(slot));
            _code.Bytes(0x48, 0x3B, 0x85);
            _code.Int32(temporary);
            failures.Add(_code.JumpIf(0x85));
            _code.Load64(planSlot);
            _code.PointerOffset(parameterRecord + sizeof(ulong));
            _code.LoadPointerField(0, sizeof(ulong));
            _code.Bytes(0x48, 0x85, 0xC0);
            failures.Add(_code.JumpIf(0x84));
        }

        var complete = _code.Jump();
        var failure = _code.Position;
        foreach (var branch in failures)
        {
            _code.PatchRelative(branch, failure);
        }
        EmitNormalBackingContractFailure();
        _code.PatchRelative(complete, _code.Position);
    }

    private void EmitTransferNormalBackingCallInputs(
        int functionIndex, BoundFunction function)
    {
        var planSlot = _normalBackingIncomingCallPlanSlots[functionIndex];
        if (planSlot < 0)
        {
            return;
        }

        var temporary = _normalBackingTemporarySlots[functionIndex];
        var parameterOffset = function.IsConstructor ? 1 : 0;
        for (var formal = 0; formal < function.ParameterCount; formal++)
        {
            var slot = formal + parameterOffset;
            var parameter = function.Variables[slot];
            if (parameter.IsImplicitRef || parameter.IsOut)
            {
                continue;
            }
            var leaves = NormalBackingReferenceLeaves(parameter.Type, _program);
            if (leaves.Count == 0)
            {
                continue;
            }

            var parameterRecord = NormalBackingCallPlanHeaderSize
                + (NormalBackingCallParameterRecordSize * formal);
            _code.Load64(planSlot);
            _code.PointerOffset(parameterRecord);
            _code.LoadPointerField(0, sizeof(ulong));
            _code.Store64(temporary);
            _code.Load64(planSlot);
            _code.PointerOffset(parameterRecord + sizeof(ulong));
            _code.LoadPointerField(0, sizeof(ulong));
            _code.Store64(temporary + sizeof(ulong));

            for (var leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
            {
                EmitZeroNormalBackingPointer(temporary, leaves[leafIndex].Offset);
                EmitZeroNormalBackingPointer(
                    temporary, leaves[leafIndex].Offset + sizeof(ulong));
                _code.Load64(temporary + sizeof(ulong));
                _code.PointerOffset(sizeof(ulong) * leafIndex);
                _code.LoadPointerField(0, sizeof(ulong));
                _code.Bytes(0xC7, 0x00);
                _code.Int32(0);
                AdoptManagedRootAtAddress(
                    checked(Slot(slot) + leaves[leafIndex].Offset + sizeof(ulong)));
            }
        }

        EmitStoreNormalBackingCallPointerField(planSlot,
            NormalBackingCallPlanStateOffset, NormalBackingCallPlanEntered);
    }

    private void EmitPrepareNormalBackingCallResult(
        int functionIndex, PrimitiveType resultType)
    {
        var leaves = NormalBackingReferenceLeaves(resultType, _program);
        if (leaves.Count == 0)
        {
            return;
        }

        var planSlot = _normalBackingIncomingCallPlanSlots[functionIndex];
        var temporary = _normalBackingTemporarySlots[functionIndex];
        _code.Load64(planSlot);
        _code.PointerOffset(NormalBackingCallPlanResultFlagsOffset);
        _code.LoadPointerField(0, sizeof(ulong));
        _code.Store64(temporary);
        for (var leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
        {
            _code.Load64(temporary);
            _code.PointerOffset(sizeof(ulong) * leafIndex);
            _code.LoadPointerField(0, sizeof(ulong));
            _code.Bytes(0xC7, 0x00);
            _code.Int32(1);
        }

        EmitStoreNormalBackingCallPointerField(
            planSlot, NormalBackingCallPlanStateOffset,
            NormalBackingCallPlanResultPrepared);
    }

    private void EmitFinishNormalBackingCallPlan(int planOffset, PrimitiveType resultType)
    {
        var temporary = _normalBackingTemporarySlots[_currentFunction]
            + (3 * sizeof(ulong));
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(),
            _normalBackingRuntimeOffset + NormalBackingCallPlanHeadOffset));
        _code.Bytes(0x48, 0x8B, 0x02);
        _code.Store64(temporary);
        _code.Address(planOffset);
        _code.Bytes(0x48, 0x3B, 0x85);
        _code.Int32(temporary);
        var failures = new List<int> { _code.JumpIf(0x85) };
        var expectedState = NormalBackingReferenceLeaves(resultType, _program).Count != 0
            ? NormalBackingCallPlanResultPrepared : NormalBackingCallPlanEntered;
        EmitCompareNormalBackingFramePlanField(planOffset,
            NormalBackingCallPlanStateOffset, expectedState, failures);

        var complete = _code.Jump();
        var failure = _code.Position;
        foreach (var branch in failures)
        {
            _code.PatchRelative(branch, failure);
        }
        EmitNormalBackingContractFailure();
        _code.PatchRelative(complete, _code.Position);

        _code.Address(planOffset + NormalBackingCallPlanPreviousOffset);
        _code.LoadPointerField(0, sizeof(ulong));
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(),
            _normalBackingRuntimeOffset + NormalBackingCallPlanHeadOffset));
        _code.StorePointerField(sizeof(ulong));
        EmitStoreNormalBackingFramePlanPointerField(
            planOffset, NormalBackingCallPlanStateOffset, NormalBackingCallPlanClosed);
    }

    private void EmitAbortNormalBackingCallPlan(int planOffset)
    {
        var temporary = _normalBackingTemporarySlots[_currentFunction]
            + (3 * sizeof(ulong));
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(),
            _normalBackingRuntimeOffset + NormalBackingCallPlanHeadOffset));
        _code.Bytes(0x48, 0x8B, 0x02);
        _code.Store64(temporary);
        _code.Address(planOffset);
        _code.Bytes(0x48, 0x3B, 0x85);
        _code.Int32(temporary);
        var noPlan = _code.JumpIf(0x85);

        _code.Address(planOffset + NormalBackingCallPlanPreviousOffset);
        _code.LoadPointerField(0, sizeof(ulong));
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(),
            _normalBackingRuntimeOffset + NormalBackingCallPlanHeadOffset));
        _code.StorePointerField(sizeof(ulong));
        EmitStoreNormalBackingFramePlanPointerField(
            planOffset, NormalBackingCallPlanStateOffset, NormalBackingCallPlanClosed);
        _code.PatchRelative(noPlan, _code.Position);
    }

    private void EmitCompareNormalBackingPlanField(
        int planOffset, int fieldOffset, int expected, List<int> failures)
    {
        _code.Load64(planOffset);
        _code.PointerOffset(fieldOffset);
        _code.LoadPointerField(0, sizeof(uint));
        _code.Byte(0x3D);
        _code.Int32(expected);
        failures.Add(_code.JumpIf(0x85));
    }

    private void EmitCompareNormalBackingFramePlanField(
        int planOffset, int fieldOffset, int expected, List<int> failures)
    {
        _code.Address(checked(planOffset + fieldOffset));
        _code.LoadPointerField(0, sizeof(uint));
        _code.Byte(0x3D);
        _code.Int32(expected);
        failures.Add(_code.JumpIf(0x85));
    }

    private void EmitStoreNormalBackingPlanImmediate(int frameOffset, int value)
    {
        if (!_code.UseFramePointer)
        {
            throw new InvalidOperationException(
                "Normal-backed call plans require a frame-pointer-based source function.");
        }
        _code.Bytes(0xC7, 0x85);
        _code.Int32(frameOffset);
        _code.Int32(value);
    }

    private void EmitStoreNormalBackingPlanPointer(
        int planOffset, int fieldOffset, int sourceOffset)
    {
        _code.Address(checked(planOffset + fieldOffset));
        _code.Bytes(0x48, 0x89, 0xC2);
        _code.Address(sourceOffset);
        _code.StorePointerField(sizeof(ulong));
    }

    private void EmitStoreNormalBackingCallPointerField(
        int planOffset, int fieldOffset, int value)
    {
        _code.Load64(planOffset);
        _code.PointerOffset(fieldOffset);
        _code.Bytes(0xC7, 0x00);
        _code.Int32(value);
    }

    private void EmitStoreNormalBackingFramePlanPointerField(
        int planOffset, int fieldOffset, int value)
    {
        _code.Address(checked(planOffset + fieldOffset));
        _code.Bytes(0xC7, 0x00);
        _code.Int32(value);
    }

    private void EmitZeroNormalBackingPointer(int baseSlot, int offset)
    {
        _code.Load64(baseSlot);
        if (offset != 0)
        {
            _code.PointerOffset(offset);
        }
        _code.Bytes(0x48, 0x89, 0xC2, 0x31, 0xC0);
        _code.StorePointerField(sizeof(ulong));
    }

    private bool IsNormalBackingOptionalReference(PrimitiveType type) =>
        _normalBacking
        && SourceOptionalTypes.IsOptionalReference(type, _program.CheckedReferenceTypes)
        && IsNormalBackingLocalReference(type, _program);

    private void EmitManagedClassFactory(BoundFunction function)
    {
        var output = function.ParameterCount - 1;
        var provisional = function.ParameterCount;
        var type = function.Variables[output].Type;
        var classIndex = ManagedClassTypes.ClassIndex(type);
        var @class = _program.Classes[classIndex];
        if (_normalBacking)
        {
            EmitNormalBackingAllocation(classIndex, @class.Size);
        }
        else
        {
            EmitManagedAllocation(classIndex + 1, @class.Size + 32);
        }
        _code.Bytes(0x48, 0x85, 0xC0);
        var failed = _code.JumpIf(0x84);
        _code.Store64(Slot(provisional));
        foreach (var field in @class.Fields)
        {
            if (ManagedClassTypes.IsManaged(field.Type))
            {
                _code.Bytes(0x48, 0xC7, 0x80);
                _code.Int32(32 + field.Offset);
                _code.Int32(0);
            }
            if (_normalBacking)
            {
                foreach (var leaf in NormalBackingReferenceLeaves(field.Type, _program))
                {
                    _code.Bytes(0x48, 0xC7, 0x80);
                    _code.Int32(32 + field.Offset + leaf.Offset);
                    _code.Int32(0);
                    _code.Bytes(0x48, 0xC7, 0x80);
                    _code.Int32(32 + field.Offset + leaf.Offset + sizeof(ulong));
                    _code.Int32(0);
                }
            }
        }
        SetManagedRoot(Slot(provisional), active: true);
        var constructorBegin = _code.Position;
        for (var index = 3; index < output; index++)
        {
            LoadValue(Slot(index), function.Variables[index].Type);
            _code.StoreOutgoing64(32 + (8 * (index - 3)));
        }
        for (var index = 0; index < output && index < 3; index++)
        {
            var argumentType = function.Variables[index].Type;
            LoadValue(Slot(index), argumentType);
            _code.MoveArgument(index + 1, IsStruct(argumentType) || IsWide(argumentType));
        }
        _code.Load64(Slot(provisional));
        _code.MoveArgument(0, true);
        EmitTargetCall(function.ClassConstructor);
        if (_checkedExceptions)
        {
            _code.Byte(0x90);
            var funclet = _funclets.Count;
            _funclets.Add((new BoundBlock([], function.Span), _currentFunction, 0, 0));
            _managedAbandonedFactories.Add(funclet, (Slot(provisional), classIndex));
            _scopes[_currentFunction].Add(new NativeExceptionScope(constructorBegin,
                _code.Position, -funclet - 1, 0));
        }
        _code.Load64(Slot(provisional));
        if (_normalBacking)
        {
            EmitNormalBackingPublish(Slot(provisional), classIndex);
            _code.Load64(Slot(provisional));
        }
        _code.Load64(Slot(output), destinationRegister: true);
        _code.Bytes(0x48, 0x89, 0x02);
        if (_normalBacking)
        {
            TransferManagedRoot(Slot(provisional));
        }
        _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
        var complete = _code.Jump();
        _code.PatchRelative(failed, _code.Position);
        _code.Bytes(0x31, 0xC0);
        _code.PatchRelative(complete, _code.Position);
    }

    private void EmitNormalBackingAllocation(int classIndex, int payloadSize)
    {
        var metadataOffset = NormalBackingConstructionMetadataOffset(payloadSize);
        var allocationSize = metadataOffset + NormalBackingConstructionMetadataSize;
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
        _code.MoveArgument(0, true);
        _code.Bytes(0x31, 0xD2, 0x41, 0xB8);
        _code.Int32(allocationSize);
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapAlloc"));
        _code.Bytes(0x48, 0x85, 0xC0);
        var failed = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0x89, 0xC0); // r8 = control
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _normalBackingRuntimeOffset));
        _code.Bytes(0x48, 0xFF, 0x02); // next generation
        _code.Bytes(0x48, 0x83, 0x3A, 0x00);
        var generationExhausted = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0x89, 0x10); // control.runtime = runtime
        _code.Bytes(0x48, 0x8B, 0x02, 0x49, 0x89, 0x40, 0x08); // control.generation
        _code.Bytes(0x49, 0xC7, 0x40, 0x10, 0x00, 0x00, 0x00, 0x00); // hold count
        _code.Bytes(0x41, 0xC7, 0x40, 0x18);
        _code.Int32(classIndex);
        _code.Bytes(0x41, 0xC7, 0x40, 0x1C);
        _code.Int32(payloadSize);
        _code.Bytes(0x41, 0xC7, 0x80);
        _code.Int32(metadataOffset);
        _code.Int32(0);
        _code.Bytes(0x41, 0xC7, 0x80);
        _code.Int32(metadataOffset + 4);
        _code.Int32(0);
        for (var field = 0; field < ManagedClassTypes.MaximumFields; field++)
        {
            _code.Bytes(0x41, 0xC6, 0x80);
            _code.Int32(metadataOffset + 8 + field);
            _code.Byte(0xFF);
        }
        _code.Bytes(0x48, 0xFF, 0x42, 0x10); // live allocations
        _code.Bytes(0x48, 0x83, 0x7A, 0x10, 0x00);
        var liveCountExhausted = _code.JumpIf(0x84);
        _code.Bytes(0x4C, 0x89, 0xC0); // rax = control
        var complete = _code.Jump();
        var failure = _code.Position;
        _code.PatchRelative(generationExhausted, failure);
        _code.PatchRelative(liveCountExhausted, failure);
        EmitNormalBackingContractFailure();
        _code.PatchRelative(failed, _code.Position);
        _code.Bytes(0x31, 0xC0);
        _code.PatchRelative(complete, _code.Position);
    }

    private void EmitNormalBackingPublish(int provisionalSlot, int classIndex)
    {
        var metadataOffset = NormalBackingConstructionMetadataOffset(
            _program.Classes[classIndex].Size);
        _code.Load64(provisionalSlot);
        _code.Bytes(0xC7, 0x80);
        _code.Int32(metadataOffset);
        _code.Int32(1);
        _code.Bytes(0xC7, 0x80);
        _code.Int32(metadataOffset + 4);
        _code.Int32(0);
    }

    private void EmitNormalBackingRecordFieldStore(int ownerSlot, int classIndex,
        int fieldIndex, int oldSlot, int newSlot)
    {
        var @class = _program.Classes[classIndex];
        var metadataOffset = NormalBackingConstructionMetadataOffset(@class.Size);
        var failures = new List<int>();
        _code.Load64(ownerSlot);
        _code.PointerOffset(metadataOffset + NormalBackingPublishedOffset);
        _code.LoadPointerField(0, sizeof(uint));
        _code.Bytes(0x85, 0xC0);
        var published = _code.JumpIf(0x85);

        _code.Load64(oldSlot);
        _code.Bytes(0x48, 0x85, 0xC0);
        var oldAbsent = _code.JumpIf(0x84);
        _code.Load64(ownerSlot);
        _code.PointerOffset(metadataOffset + NormalBackingFieldRankOffset + fieldIndex);
        _code.LoadPointerField(0, sizeof(byte));
        _code.Bytes(0x3C, 0xFF);
        failures.Add(_code.JumpIf(0x84));
        _code.Bytes(0x44, 0x0F, 0xB6, 0xC8); // r9d = removed rank
        _code.Load64(ownerSlot);
        _code.PointerOffset(metadataOffset);
        _code.Bytes(0x83, 0x78, NormalBackingAcquiredCountOffset, 0x00);
        failures.Add(_code.JumpIf(0x84));
        _code.Bytes(0xFF, 0x48, NormalBackingAcquiredCountOffset);
        for (var index = 0; index < @class.Fields.Length; index++)
        {
            if (index == fieldIndex
                || !IsNormalBackingTrackedField(@class.Fields[index].Type, _program))
            {
                continue;
            }
            var rankOffset = metadataOffset + NormalBackingFieldRankOffset + index;
            _code.Load64(ownerSlot);
            _code.PointerOffset(rankOffset);
            _code.LoadPointerField(0, sizeof(byte));
            _code.Bytes(0x3C, 0xFF);
            var absent = _code.JumpIf(0x84);
            _code.Bytes(0x44, 0x39, 0xC8);
            var earlier = _code.JumpIf(0x86);
            _code.Bytes(0xFF, 0xC8);
            _code.Load64(ownerSlot);
            _code.PointerOffset(rankOffset);
            _code.Bytes(0x88, 0x00);
            var next = _code.Position;
            _code.PatchRelative(absent, next);
            _code.PatchRelative(earlier, next);
        }
        _code.Load64(ownerSlot);
        _code.PointerOffset(metadataOffset + NormalBackingFieldRankOffset + fieldIndex);
        _code.Bytes(0xC6, 0x00, 0xFF);
        var oldRemoved = _code.Position;
        _code.PatchRelative(oldAbsent, oldRemoved);

        _code.Load64(newSlot);
        _code.Bytes(0x48, 0x85, 0xC0);
        var newAbsent = _code.JumpIf(0x84);
        _code.Load64(ownerSlot);
        _code.PointerOffset(metadataOffset + NormalBackingAcquiredCountOffset);
        _code.LoadPointerField(0, sizeof(uint));
        _code.Bytes(0x83, 0xF8, ManagedClassTypes.MaximumFields);
        failures.Add(_code.JumpIf(0x83));
        _code.Bytes(0x89, 0xC1); // ecx = appended rank
        _code.Load64(ownerSlot);
        _code.PointerOffset(metadataOffset + NormalBackingFieldRankOffset + fieldIndex);
        _code.Bytes(0x88, 0x08); // rank[field] = ecx
        _code.Load64(ownerSlot);
        _code.PointerOffset(metadataOffset + NormalBackingAcquiredCountOffset);
        _code.Bytes(0xFF, 0x00); // append one acquired field
        var complete = _code.Jump();
        var failure = _code.Position;
        foreach (var branch in failures)
        {
            _code.PatchRelative(branch, failure);
        }
        EmitNormalBackingContractFailure();
        var done = _code.Position;
        _code.PatchRelative(published, done);
        _code.PatchRelative(newAbsent, done);
        _code.PatchRelative(complete, done);
    }

    private void EmitNormalBackingReferenceMarker(int valueSlot,
        PrimitiveType valueType, int markerSlot)
    {
        _code.Bytes(0x31, 0xC0);
        _code.Store64(markerSlot);
        foreach (var leaf in NormalBackingReferenceLeaves(valueType, _program))
        {
            EmitNormalBackingAccumulateReferenceMarker(
                valueSlot + leaf.Offset + sizeof(ulong), markerSlot);
        }
    }

    private void EmitNormalBackingReferenceMarkerWithReplacement(
        int oldValueSlot, PrimitiveType oldValueType, int replacementOffset,
        PrimitiveType replacementType, int replacementValueSlot, int markerSlot)
    {
        _code.Bytes(0x31, 0xC0);
        _code.Store64(markerSlot);
        var replacementEnd = checked(replacementOffset + Size(replacementType));
        var replacementLeaves = NormalBackingReferenceLeaves(
            replacementType, _program);
        foreach (var leaf in NormalBackingReferenceLeaves(oldValueType, _program))
        {
            var supportSlot = oldValueSlot + leaf.Offset + sizeof(ulong);
            if (leaf.Offset >= replacementOffset && leaf.Offset < replacementEnd)
            {
                var replacementLeafOffset = leaf.Offset - replacementOffset;
                var found = false;
                foreach (var replacementLeaf in replacementLeaves)
                {
                    if (replacementLeaf.Offset == replacementLeafOffset)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    throw new InvalidOperationException(
                        "A normal-backed field replacement changed descriptor leaves.");
                }
                supportSlot = replacementValueSlot
                    + replacementLeafOffset + sizeof(ulong);
            }
            EmitNormalBackingAccumulateReferenceMarker(supportSlot, markerSlot);
        }
    }

    private void EmitNormalBackingAccumulateReferenceMarker(
        int supportSlot, int markerSlot)
    {
        _code.Load64(supportSlot);
        _code.Bytes(0x48, 0x89, 0xC1);
        _code.Load64(markerSlot);
        _code.Bytes(0x48, 0x09, 0xC8);
        _code.Store64(markerSlot);
    }

    private void EmitNormalBackingRetain(int slot)
    {
        _code.Load64(slot);
        _code.Bytes(0x48, 0x85, 0xC0);
        var absent = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0x89, 0xC0); // r8 = control
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _normalBackingRuntimeOffset));
        _code.Bytes(0x49, 0x39, 0x10); // control.runtime == runtime
        var wrongRuntime = _code.JumpIf(0x85);
        _code.Bytes(0x49, 0x83, 0x78, 0x08, 0x00);
        var invalidGeneration = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0xFF, 0x40, 0x10); // checked strong hold
        _code.Bytes(0x49, 0x83, 0x78, 0x10, 0x00);
        var countExhausted = _code.JumpIf(0x84);
        var complete = _code.Jump();
        var failure = _code.Position;
        _code.PatchRelative(wrongRuntime, failure);
        _code.PatchRelative(invalidGeneration, failure);
        _code.PatchRelative(countExhausted, failure);
        EmitNormalBackingContractFailure();
        var done = _code.Position;
        _code.PatchRelative(absent, done);
        _code.PatchRelative(complete, done);
    }

    private void EmitNormalBackingValidateHandle(int slot, int expectedClassIndex)
    {
        if ((uint)expectedClassIndex >= (uint)_program.Classes.Length)
        {
            throw new InvalidOperationException("The normal-backed class identity is invalid.");
        }

        var @class = _program.Classes[expectedClassIndex];
        _code.Load64(slot);
        _code.Bytes(0x48, 0x85, 0xC0);
        var failures = new List<int> { _code.JumpIf(0x84) };
        _code.Bytes(0x49, 0x89, 0xC0); // r8 = control
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _normalBackingRuntimeOffset));
        _code.Bytes(0x49, 0x39, 0x10); // control.runtime == runtime
        failures.Add(_code.JumpIf(0x85));
        _code.Bytes(0x49, 0x83, 0x78, 0x08, 0x00);
        failures.Add(_code.JumpIf(0x84));
        _code.Bytes(0x49, 0x83, 0x78, 0x10, 0x00);
        failures.Add(_code.JumpIf(0x84));
        _code.Bytes(0x41, 0x81, 0x78, 0x18);
        _code.Int32(expectedClassIndex);
        failures.Add(_code.JumpIf(0x85));
        _code.Bytes(0x41, 0x81, 0x78, 0x1C);
        _code.Int32(@class.Size);
        failures.Add(_code.JumpIf(0x85));
        var complete = _code.Jump();
        var failure = _code.Position;
        foreach (var branch in failures)
        {
            _code.PatchRelative(branch, failure);
        }
        EmitNormalBackingContractFailure();
        _code.PatchRelative(complete, _code.Position);
    }

    private void EmitNormalBackingRelease(int slot)
    {
        _code.Load64(slot);
        _code.MoveArgument(0, true);
        _normalBackingReleaseCalls.Add(_code.RelativeCall());
    }

    private void EmitNormalBackingRuntimeHelper()
    {
        if (!_normalBacking || _normalBackingReleaseCalls.Count == 0)
        {
            return;
        }

        _normalBackingReleaseStart = _code.Position;
        var savedFrame = _frame;
        var useFramePointer = _code.UseFramePointer;
        _code.UseFramePointer = false;
        Prolog(NormalBackingReleaseFrameSize);
        _code.Bytes(0x48, 0x89, 0x4C, 0x24, NormalBackingHelperArgumentSlot);
        _code.Load64(NormalBackingHelperArgumentSlot);
        _code.Bytes(0x48, 0x85, 0xC0);
        var absent = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0x89, 0xC0); // r8 = control
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _normalBackingRuntimeOffset));
        _code.Bytes(0x49, 0x39, 0x10); // control.runtime == runtime
        var wrongRuntime = _code.JumpIf(0x85);
        _code.Bytes(0x49, 0x83, 0x78, 0x08, 0x00);
        var invalidGeneration = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0x83, 0x78, 0x10, 0x00);
        var holdUnderflow = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0xFF, 0x48, 0x10); // release strong hold
        _code.Bytes(0x49, 0x83, 0x78, 0x10, 0x00);
        var retained = _code.JumpIf(0x85);
        _code.Bytes(0x48, 0x8B, 0x42, 0x08); // worklist head
        _code.Bytes(0x49, 0x89, 0x40, 0x08); // control.next = head
        _code.Bytes(0x4C, 0x89, 0x42, 0x08); // head = control
        var workLoop = _code.Position;
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _normalBackingRuntimeOffset));
        _code.Bytes(0x48, 0x8B, 0x42, 0x08, 0x48, 0x85, 0xC0);
        var workDone = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0x89, 0xC0); // r8 = current control
        _code.Bytes(0x49, 0x8B, 0x48, 0x08, 0x48, 0x89, 0x4A, 0x08); // pop work link
        _code.Store64FromRegister(NormalBackingHelperCurrentControlSlot, 8);

        var failures = new List<int>();
        var matchedClasses = new List<int>();
        for (var classIndex = 0; classIndex < _program.Classes.Length; classIndex++)
        {
            _code.Load64(NormalBackingHelperCurrentControlSlot);
            _code.Bytes(0x8B, 0x40, 0x18);
            _code.Byte(0x3D);
            _code.Int32(classIndex);
            var nextClass = _code.JumpIf(0x85);
            EmitNormalBackingRetireClass(classIndex, failures);
            matchedClasses.Add(_code.Jump());
            _code.PatchRelative(nextClass, _code.Position);
        }
        failures.Add(_code.Jump());
        var freeCurrent = _code.Position;
        foreach (var matchedClass in matchedClasses)
        {
            _code.PatchRelative(matchedClass, freeCurrent);
        }

        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _normalBackingRuntimeOffset));
        _code.Bytes(0x48, 0x83, 0x7A, 0x10, 0x00);
        failures.Add(_code.JumpIf(0x84));
        _code.Bytes(0x48, 0xFF, 0x4A, 0x10); // live allocations
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
        _code.MoveArgument(0, true);
        _code.Bytes(0x31, 0xD2);
        _code.Load64(NormalBackingHelperCurrentControlSlot);
        _code.MoveArgument(2, true);
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapFree"));
        _code.Bytes(0x85, 0xC0);
        failures.Add(_code.JumpIf(0x84));
        _code.PatchRelative(_code.Jump(), workLoop);

        var failure = _code.Position;
        foreach (var branch in failures)
        {
            _code.PatchRelative(branch, failure);
        }
        EmitNormalBackingContractFailure();
        _code.PatchRelative(wrongRuntime, failure);
        var done = _code.Position;
        _code.PatchRelative(absent, done);
        _code.PatchRelative(retained, done);
        _code.PatchRelative(workDone, done);
        _code.PatchRelative(invalidGeneration, failure);
        _code.PatchRelative(holdUnderflow, failure);
        _frame = NormalBackingReleaseFrameSize;
        Epilog();
        _normalBackingReleaseEnd = _code.Position;
        _frame = savedFrame;
        _code.UseFramePointer = useFramePointer;
        foreach (var call in _normalBackingReleaseCalls)
        {
            _code.PatchRelative(call, _normalBackingReleaseStart);
        }
    }

    private void EmitNormalBackingRetireClass(int classIndex, List<int> failures)
    {
        var @class = _program.Classes[classIndex];
        var metadataOffset = NormalBackingConstructionMetadataOffset(@class.Size);
        _code.Load64(NormalBackingHelperCurrentControlSlot);
        _code.PointerOffset(metadataOffset + NormalBackingPublishedOffset);
        _code.LoadPointerField(0, sizeof(uint));
        _code.Bytes(0x85, 0xC0);
        var published = _code.JumpIf(0x85);

        _code.Load64(NormalBackingHelperCurrentControlSlot);
        _code.PointerOffset(metadataOffset + NormalBackingAcquiredCountOffset);
        _code.LoadPointerField(0, sizeof(uint));
        _code.Store64(NormalBackingHelperCountSlot);
        _code.Bytes(0x85, 0xC0);
        var acquisitionsDone = _code.JumpIf(0x84);
        _code.Bytes(0x31, 0xC0);
        _code.Store64(NormalBackingHelperRankSlot);
        var rankLoop = _code.Position;
        _code.Load(NormalBackingHelperRankSlot);
        _code.Bytes(0x89, 0xC1);
        _code.Load64(NormalBackingHelperCountSlot);
        _code.Bytes(0x39, 0xC1);
        var ranksComplete = _code.JumpIf(0x83);
        for (var fieldIndex = 0; fieldIndex < @class.Fields.Length; fieldIndex++)
        {
            var field = @class.Fields[fieldIndex];
            if (!IsNormalBackingTrackedField(field.Type, _program))
            {
                continue;
            }
            _code.Load64(NormalBackingHelperRankSlot);
            _code.Bytes(0x89, 0xC1);
            _code.Load64(NormalBackingHelperCurrentControlSlot);
            _code.PointerOffset(metadataOffset + NormalBackingFieldRankOffset + fieldIndex);
            _code.LoadPointerField(0, sizeof(byte));
            _code.Bytes(0x39, 0xC8);
            var notThisRank = _code.JumpIf(0x85);
            _code.Load64(NormalBackingHelperCurrentControlSlot);
            _code.PointerOffset(metadataOffset + NormalBackingFieldRankOffset + fieldIndex);
            _code.Bytes(0xC6, 0x00, 0xFF);
            EmitNormalBackingQueueFieldSupportAtOffset(
                field.Offset, field.Type, failures);
            _code.Load(NormalBackingHelperRankSlot);
            _code.Bytes(0xFF, 0xC0);
            _code.Store(NormalBackingHelperRankSlot);
            _code.PatchRelative(_code.Jump(), rankLoop);
            _code.PatchRelative(notThisRank, _code.Position);
        }
        failures.Add(_code.Jump());
        var ranksCompleteTarget = _code.Position;
        _code.PatchRelative(ranksComplete, ranksCompleteTarget);
        var skipPublishedFields = _code.Jump();
        var publishedFields = _code.Position;
        _code.PatchRelative(published, publishedFields);
        for (var fieldIndex = @class.Fields.Length - 1; fieldIndex >= 0; fieldIndex--)
        {
            var field = @class.Fields[fieldIndex];
            EmitNormalBackingQueueFieldSupportAtOffset(
                field.Offset, field.Type, failures);
        }
        var afterFields = _code.Position;
        _code.PatchRelative(acquisitionsDone, afterFields);
        _code.PatchRelative(skipPublishedFields, afterFields);
    }

    private void EmitNormalBackingAbandon(int provisionalSlot, int classIndex)
    {
        var @class = _program.Classes[classIndex];
        var metadataOffset = NormalBackingConstructionMetadataOffset(@class.Size);
        var temporarySupport = _normalBackingTemporarySlots[_currentFunction] + sizeof(ulong);

        EmitNormalBackingValidateHandle(provisionalSlot, classIndex);
        EmitNormalBackingRetain(provisionalSlot);

        for (var rank = @class.Fields.Length - 1; rank >= 0; rank--)
        {
            _code.Load64(provisionalSlot);
            _code.PointerOffset(metadataOffset + NormalBackingAcquiredCountOffset);
            _code.LoadPointerField(0, sizeof(uint));
            _code.Byte(0x3D);
            _code.Int32(rank + 1);
            var rankNotAcquired = _code.JumpIf(0x82);
            var completedRank = new List<int>();
            for (var fieldIndex = 0; fieldIndex < @class.Fields.Length; fieldIndex++)
            {
                var field = @class.Fields[fieldIndex];
                if (!IsNormalBackingTrackedField(field.Type, _program))
                {
                    continue;
                }

                _code.Load64(provisionalSlot);
                _code.PointerOffset(metadataOffset + NormalBackingFieldRankOffset + fieldIndex);
                _code.LoadPointerField(0, sizeof(byte));
                _code.Byte(0x3C);
                _code.Byte((byte)rank);
                var notThisRank = _code.JumpIf(0x85);

                _code.Load64(provisionalSlot);
                _code.PointerOffset(metadataOffset + NormalBackingFieldRankOffset + fieldIndex);
                _code.Bytes(0xC6, 0x00, 0xFF);
                _code.Load64(provisionalSlot);
                _code.PointerOffset(metadataOffset + NormalBackingAcquiredCountOffset);
                _code.Bytes(0xFF, 0x08);
                if (IsNormalBackingHandle(field.Type))
                {
                    _code.Load64(provisionalSlot);
                    _code.PointerOffset(NormalBackingControlSize + field.Offset);
                    _code.LoadPointerField(0, sizeof(ulong));
                    _code.Store64(temporarySupport);
                    EmitNormalBackingRelease(temporarySupport);
                }
                else
                {
                    var leaves = NormalBackingReferenceLeaves(field.Type, _program);
                    for (var leafIndex = leaves.Count - 1; leafIndex >= 0; leafIndex--)
                    {
                        var leaf = leaves[leafIndex];
                        _code.Load64(provisionalSlot);
                        _code.PointerOffset(NormalBackingControlSize
                            + field.Offset + leaf.Offset + sizeof(ulong));
                        _code.LoadPointerField(0, sizeof(ulong));
                        _code.Store64(temporarySupport);
                        EmitNormalBackingRelease(temporarySupport);
                    }
                }
                completedRank.Add(_code.Jump());
                _code.PatchRelative(notThisRank, _code.Position);
            }

            EmitNormalBackingContractFailure();
            var rankComplete = _code.Position;
            foreach (var branch in completedRank)
            {
                _code.PatchRelative(branch, rankComplete);
            }
            _code.PatchRelative(rankNotAcquired, rankComplete);
        }

        _code.Load64(provisionalSlot);
        _code.PointerOffset(metadataOffset + NormalBackingAcquiredCountOffset);
        _code.LoadPointerField(0, sizeof(uint));
        _code.Bytes(0x85, 0xC0);
        var countIsZero = _code.JumpIf(0x84);
        EmitNormalBackingContractFailure();
        _code.PatchRelative(countIsZero, _code.Position);
        EmitNormalBackingRelease(provisionalSlot);
    }

    private void EmitNormalBackingQueueChildAtOffset(int fieldOffset, int expectedClassIndex,
        List<int> failures)
    {
        _code.Load64(NormalBackingHelperCurrentControlSlot);
        _code.PointerOffset(NormalBackingControlSize + fieldOffset);
        _code.LoadPointerField(0, sizeof(ulong));
        _code.Store64(NormalBackingHelperChildControlSlot);
        EmitNormalBackingEnqueueChildFromSlot(
            NormalBackingHelperChildControlSlot, expectedClassIndex, failures);
    }

    private void EmitNormalBackingQueueFieldSupportAtOffset(int fieldOffset,
        PrimitiveType fieldType, List<int> failures)
    {
        if (IsNormalBackingHandle(fieldType))
        {
            EmitNormalBackingQueueChildAtOffset(fieldOffset,
                ManagedClassTypes.ClassIndex(fieldType), failures);
            return;
        }

        var leaves = NormalBackingReferenceLeaves(fieldType, _program);
        for (var leafIndex = leaves.Count - 1; leafIndex >= 0; leafIndex--)
        {
            var leaf = leaves[leafIndex];
            EmitNormalBackingQueueReferenceSupportAtOffset(
                fieldOffset + leaf.Offset + sizeof(ulong), failures);
        }
    }

    private void EmitNormalBackingQueueReferenceSupportAtOffset(int supportOffset,
        List<int> failures)
    {
        _code.Load64(NormalBackingHelperCurrentControlSlot);
        _code.PointerOffset(NormalBackingControlSize + supportOffset);
        _code.LoadPointerField(0, sizeof(ulong));
        _code.Store64(NormalBackingHelperChildControlSlot);
        EmitNormalBackingEnqueueReferenceFromSlot(
            NormalBackingHelperChildControlSlot, failures);
    }

    private void EmitNormalBackingEnqueueChildFromSlot(int slot, int expectedClassIndex,
        List<int> failures)
    {
        _code.Load64(slot);
        _code.Bytes(0x48, 0x85, 0xC0);
        var absent = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0x89, 0xC0); // r8 = child control
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _normalBackingRuntimeOffset));
        _code.Bytes(0x49, 0x39, 0x10); // child.runtime == runtime
        failures.Add(_code.JumpIf(0x85));
        _code.Bytes(0x49, 0x83, 0x78, 0x08, 0x00);
        failures.Add(_code.JumpIf(0x84));
        _code.Bytes(0x41, 0x81, 0x78, 0x18);
        _code.Int32(expectedClassIndex);
        failures.Add(_code.JumpIf(0x85));
        _code.Bytes(0x49, 0x83, 0x78, 0x10, 0x00);
        failures.Add(_code.JumpIf(0x84));
        _code.Bytes(0x49, 0xFF, 0x48, 0x10); // release child hold
        _code.Bytes(0x49, 0x83, 0x78, 0x10, 0x00);
        var retained = _code.JumpIf(0x85);
        _code.Bytes(0x48, 0x8B, 0x42, 0x08);
        _code.Bytes(0x49, 0x89, 0x40, 0x08);
        _code.Bytes(0x4C, 0x89, 0x42, 0x08);
        var complete = _code.Jump();
        var done = _code.Position;
        _code.PatchRelative(absent, done);
        _code.PatchRelative(retained, done);
        _code.PatchRelative(complete, done);
    }

    private void EmitNormalBackingEnqueueReferenceFromSlot(
        int slot, List<int> failures)
    {
        _code.Load64(slot);
        _code.Bytes(0x48, 0x85, 0xC0);
        var absent = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0x89, 0xC0); // r8 = support control
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _normalBackingRuntimeOffset));
        _code.Bytes(0x49, 0x39, 0x10); // support.runtime == runtime
        failures.Add(_code.JumpIf(0x85));
        _code.Bytes(0x49, 0x83, 0x78, 0x08, 0x00);
        failures.Add(_code.JumpIf(0x84));
        _code.Bytes(0x49, 0x83, 0x78, 0x10, 0x00);
        failures.Add(_code.JumpIf(0x84));
        _code.Bytes(0x49, 0xFF, 0x48, 0x10); // release descriptor support hold
        _code.Bytes(0x49, 0x83, 0x78, 0x10, 0x00);
        var retained = _code.JumpIf(0x85);
        _code.Bytes(0x48, 0x8B, 0x42, 0x08); // worklist head
        _code.Bytes(0x49, 0x89, 0x40, 0x08); // control.next = head
        _code.Bytes(0x4C, 0x89, 0x42, 0x08); // head = control
        var complete = _code.Jump();
        var done = _code.Position;
        _code.PatchRelative(absent, done);
        _code.PatchRelative(retained, done);
        _code.PatchRelative(complete, done);
    }

    private static bool IsNormalBackingTrackedField(
        PrimitiveType type, BoundProgram program) =>
        IsNormalBackingHandle(type)
        || ContainsNormalBackingReferenceValue(type, program);

    private void EmitNormalBackingContractFailure()
    {
        _code.Byte(0xB9);
        _code.Int32(unchecked((int)0xE0540005));
        _imports.Add(_code.ImportCall());
        _code.Byte(0xCC);
    }

    private void EmitManagedAbandon(int provisional)
    {
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
        _code.Bytes(0x48, 0x89, 0xD1); // rcx = address of allocation-list head
        var search = _code.Position;
        _code.Bytes(0x48, 0x8B, 0x01);
        _code.Load64(provisional, destinationRegister: true);
        _code.Bytes(0x48, 0x39, 0xD0);
        var found = _code.JumpIf(0x84);
        _code.Bytes(0x48, 0x8D, 0x48, 0x08);
        _code.PatchRelative(_code.Jump(), search);
        _code.PatchRelative(found, _code.Position);
        _code.Bytes(0x48, 0x8B, 0x50, 0x08, 0x48, 0x89, 0x11);
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
        _code.Bytes(0xFF, 0x4A, 0x10);
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
        _code.MoveArgument(0, true);
        _code.Bytes(0x31, 0xD2);
        _code.Load64(provisional);
        _code.MoveArgument(2, true);
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapFree"));
        _code.Bytes(0x85, 0xC0);
        var freed = _code.JumpIf(0x85);
        _code.Byte(0xB9);
        _code.Int32(unchecked((int)0xE0540005));
        _imports.Add(_code.ImportCall());
        _code.Byte(0xCC);
        _code.PatchRelative(freed, _code.Position);
    }
}
