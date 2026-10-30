// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private const int NormalBackingControlSize = 32;

    internal static void ValidateNormalBackingProgram(BoundProgram program)
    {
        if (program.NativeLoweringAgreement
                != CompilationOptions.WindowsX64NormalBackingAgreement
            || program.RequiresSourceGrant || !program.CheckedReferenceTypes.IsEmpty
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

            foreach (var variable in function.Variables)
            {
                if ((!IsNormalBackingHandle(variable.Type)
                        && !IsNormalBackingRuntimeValue(variable.Type, program, []))
                    || (variable.IsOut && IsNormalBackingHandle(variable.Type)
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
                        if (IsNormalBackingHandle(argument.Type)
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
        }

        foreach (var structure in program.Structures)
        {
            if (structure.IsUnique || structure.CleanupMethod is not null
                || !IsNormalBackingRuntimeValue(structure, program, []))
            {
                throw new NotSupportedException(
                    "The selected normal-backing runtime requires scalar or inline-copy structure fields.");
            }
        }

        foreach (var @class in program.Classes)
        {
            foreach (var field in @class.Fields)
            {
                if (!IsNormalBackingRuntimeValue(field.Type, program, []))
                {
                    throw new NotSupportedException(
                        "The selected normal-backing runtime does not support managed class fields yet.");
                }
            }
        }
    }

    private static bool IsNormalBackingRuntimeValue(PrimitiveType type, BoundProgram program,
        HashSet<int> activeStructures)
    {
        if (ManagedClassTypes.IsManaged(type) || CheckedReferenceTypes.IsType(type)
            || BoundGenericNominalTypeTypes.IsType(type)
            || type is PrimitiveType.OwnedInt32Buffer or PrimitiveType.ReadonlyInt32BufferView
                or PrimitiveType.MutableInt32BufferView or PrimitiveType.TokenSpan)
        {
            return false;
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
                && IsNormalBackingRuntimeValue(structure, program, activeStructures);
            _ = activeStructures.Remove(index);
            return supported;
        }
        return ValueLayout.Size(type, program.Structures, program.CheckedReferenceTypes,
            program.NativeLoweringAgreement) > 0;
    }

    private static bool IsNormalBackingRuntimeValue(BoundStruct structure, BoundProgram program,
        HashSet<int> activeStructures)
    {
        foreach (var field in structure.Fields)
        {
            if (!IsNormalBackingRuntimeValue(field.Type, program, activeStructures))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsNormalBackingHandle(PrimitiveType type) =>
        ManagedClassTypes.IsClass(type)
        || (ManagedClassTypes.IsOptional(type)
            && ManagedClassTypes.IsClass(ManagedClassTypes.Required(type)));

    private void EmitManagedClassFactory(BoundFunction function)
    {
        var output = function.ParameterCount - 1;
        var provisional = function.ParameterCount;
        var type = function.Variables[output].Type;
        var classIndex = ManagedClassTypes.ClassIndex(type);
        var @class = _program.Classes[classIndex];
        if (_normalBacking)
        {
            EmitNormalBackingAllocation(classIndex, @class.Size + NormalBackingControlSize);
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
            if (!ManagedClassTypes.IsManaged(field.Type))
            {
                continue;
            }
            _code.Bytes(0x48, 0xC7, 0x80);
            _code.Int32(32 + field.Offset);
            _code.Int32(0);
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
            _managedAbandonedFactories.Add(funclet, Slot(provisional));
            _scopes[_currentFunction].Add(new NativeExceptionScope(constructorBegin,
                _code.Position, -funclet - 1, 0));
        }
        _code.Load64(Slot(provisional));
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

    private void EmitNormalBackingAllocation(int classIndex, int size)
    {
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
        _code.MoveArgument(0, true);
        _code.Bytes(0x31, 0xD2, 0x41, 0xB8);
        _code.Int32(size);
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
        _code.Int32(size - NormalBackingControlSize);
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

    private void EmitNormalBackingRelease(int slot)
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
        _code.Bytes(0x49, 0x83, 0x78, 0x10, 0x00);
        var holdUnderflow = _code.JumpIf(0x84);
        _code.Bytes(0x49, 0xFF, 0x48, 0x10); // release strong hold
        _code.Bytes(0x49, 0x83, 0x78, 0x10, 0x00);
        var retained = _code.JumpIf(0x85);
        _code.Bytes(0x48, 0x83, 0x7A, 0x10, 0x00);
        var liveUnderflow = _code.JumpIf(0x84);
        _code.Bytes(0x48, 0xFF, 0x4A, 0x10); // live allocations
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
        _code.MoveArgument(0, true);
        _code.Bytes(0x31, 0xD2);
        _code.Load64(slot);
        _code.MoveArgument(2, true);
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapFree"));
        _code.Bytes(0x85, 0xC0);
        var freeFailed = _code.JumpIf(0x84);
        var complete = _code.Jump();
        var failure = _code.Position;
        _code.PatchRelative(wrongRuntime, failure);
        _code.PatchRelative(invalidGeneration, failure);
        _code.PatchRelative(holdUnderflow, failure);
        _code.PatchRelative(liveUnderflow, failure);
        _code.PatchRelative(freeFailed, failure);
        EmitNormalBackingContractFailure();
        var done = _code.Position;
        _code.PatchRelative(absent, done);
        _code.PatchRelative(retained, done);
        _code.PatchRelative(complete, done);
    }

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
