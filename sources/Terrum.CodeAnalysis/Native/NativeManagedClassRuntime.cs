// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void EmitManagedClassFactory(BoundFunction function)
    {
        var output = function.ParameterCount - 1;
        var provisional = function.ParameterCount;
        var type = function.Variables[output].Type;
        var classIndex = (int)type - ManagedClassTypes.First;
        var @class = _program.Classes[classIndex];
        EmitManagedAllocation(classIndex + 1, @class.Size + 32);
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
        for (var index = 0; index < output; index++)
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
        _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
        var complete = _code.Jump();
        _code.PatchRelative(failed, _code.Position);
        _code.Bytes(0x31, 0xC0);
        _code.PatchRelative(complete, _code.Position);
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
