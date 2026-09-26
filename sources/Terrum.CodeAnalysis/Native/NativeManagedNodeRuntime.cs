// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private readonly int[] _managedRootFrames;
    private readonly Dictionary<int, int>[] _managedRootFlags;
    private readonly List<int> _managedCollectorCalls = [];
    private int _managedCollectorStart;
    private int _managedCollectorEnd;

    private long PrepareManagedRoots(int functionIndex, BoundFunction function, int spillSlots,
        bool needsReturnSnapshot, long at)
    {
        if (!_managedNodes || function.Intrinsic != BoundIntrinsic.None)
        {
            return at;
        }
        var roots = _managedRootFlags[functionIndex];
        for (var i = 0; i < function.Variables.Length; i++)
        {
            if (function.Variables[i].Type != PrimitiveType.ManagedNode)
            {
                continue;
            }
            _ = roots.TryAdd(function.Variables[i].IsOut
                ? OutSlotFor(functionIndex, function, i) : _slots[functionIndex][i], 0);
        }
        for (var spill = 0; spill < spillSlots; spill++)
        {
            _ = roots.TryAdd(_spillBases[functionIndex] + (16 * spill), 0);
        }
        if (function.ReturnType == PrimitiveType.ManagedNode && needsReturnSnapshot)
        {
            _ = roots.TryAdd((_returnSlots[functionIndex] + 15) & ~15, 0);
        }
        if (roots.Count == 0)
        {
            return at;
        }
        at = (at + 7) & ~7L;
        _managedRootFrames[functionIndex] = checked((int)at);
        var position = 0;
        foreach (var slot in roots.Keys)
        {
            roots[slot] = checked((int)(at + 16 + (position++ * 16) + 8));
        }
        return at + 16 + (roots.Count * 16L);
    }

    private int OutSlotFor(int functionIndex, BoundFunction function, int variable)
    {
        var ordinal = 0;
        for (var i = 0; i < variable; i++)
        {
            ordinal += function.Variables[i].IsOut ? 1 : 0;
        }
        return _outBases[functionIndex] + (16 * ordinal);
    }

    private void RegisterManagedRoots(int functionIndex)
    {
        var roots = _managedRootFlags[functionIndex];
        if (roots.Count == 0)
        {
            return;
        }
        var frame = _managedRootFrames[functionIndex];
        foreach (var (slot, flag) in roots)
        {
            _code.Address(slot);
            _code.Store64(flag - 8);
            _code.Clear(flag);
        }
        _code.Address(frame);
        _code.Bytes(0x48, 0xC7, 0x40, 0x08);
        _code.Int32(roots.Count);
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
        _code.Bytes(0x48, 0x8B, 0x4A, 0x08); // previous frame
        _code.Bytes(0x48, 0x89, 0x08); // frame.previous = old head
        _code.Bytes(0x48, 0x89, 0x42, 0x08); // head = frame
        var function = _program.Functions[functionIndex];
        for (var i = function.IsConstructor ? 1 : 0;
            i < function.ParameterCount + (function.IsConstructor ? 1 : 0); i++)
        {
            if (function.Variables[i].Type == PrimitiveType.ManagedNode && !function.Variables[i].IsOut)
            {
                SetManagedRoot(_slots[functionIndex][i], active: true);
            }
        }
    }

    private void UnregisterManagedRoots(int functionIndex)
    {
        if (!_managedNodes || _managedRootFlags[functionIndex].Count == 0)
        {
            return;
        }
        _code.Bytes(0x48, 0x8B, 0x8D);
        _code.Int32(_managedRootFrames[functionIndex]);
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
        _code.Bytes(0x48, 0x89, 0x4A, 0x08); // restore previous root frame without altering the result
    }

    private void SetManagedRoot(int slot, bool active)
    {
        if (!_managedNodes)
        {
            return;
        }
        if (!_managedRootFlags[_currentFunction].TryGetValue(slot, out var flag))
        {
            throw new InvalidOperationException("Managed root has no verified frame slot.");
        }
        if (active)
        {
            _code.Set(flag);
        }
        else
        {
            _code.Clear(flag);
        }
    }

    private void EmitManagedIntrinsic(BoundIntrinsic intrinsic)
    {
        switch (intrinsic)
        {
            case BoundIntrinsic.NodeTryCreate:
                _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
                _code.Bytes(0x81, 0x7A, 0x10);
                _code.Int32(NativeManagedNodeIntrinsics.MaximumNodes);
                var full = _code.JumpIf(0x83);
                _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
                _code.MoveArgument(0, true);
                _code.Bytes(0x31, 0xD2, 0x41, 0xB8, 0x18, 0x00, 0x00, 0x00);
                _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapAlloc"));
                _code.Bytes(0x48, 0x85, 0xC0);
                var allocationFailed = _code.JumpIf(0x84);
                _code.Bytes(0x48, 0x89, 0xC2); // rdx = new node
                _code.Load(Slot(0));
                _code.Bytes(0x89, 0x42, 0x10, 0x48, 0x89, 0xD0);
                _code.Bytes(0x48, 0xC7, 0x00, 0x00, 0x00, 0x00, 0x00);
                _code.Bytes(0xC7, 0x40, 0x14, 0x00, 0x00, 0x00, 0x00);
                _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
                _code.Bytes(0x48, 0x8B, 0x0A, 0x48, 0x89, 0x48, 0x08);
                _code.Bytes(0x48, 0x89, 0x02, 0xFF, 0x42, 0x10);
                _code.Load64(Slot(1), destinationRegister: true);
                _code.Bytes(0x48, 0x89, 0x02, 0xB8, 0x01, 0x00, 0x00, 0x00);
                var success = _code.Jump();
                _code.PatchRelative(full, _code.Position);
                _code.PatchRelative(allocationFailed, _code.Position);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(success, _code.Position);
                break;
            case BoundIntrinsic.NodeValue:
                _code.Load64(Slot(0));
                _code.LoadPointerField(16, 4);
                break;
            case BoundIntrinsic.NodeSetValue:
                _code.Load64(Slot(0), destinationRegister: true);
                _code.Load(Slot(1));
                _code.Bytes(0x89, 0x42, 0x10);
                break;
            case BoundIntrinsic.NodeSetNext:
                _code.Load64(Slot(0), destinationRegister: true);
                _code.Load64(Slot(1));
                _code.Bytes(0x48, 0x89, 0x02);
                break;
            case BoundIntrinsic.NodeClearNext:
                _code.Load64(Slot(0), destinationRegister: true);
                _code.Bytes(0x31, 0xC0, 0x48, 0x89, 0x02);
                break;
            case BoundIntrinsic.NodeTryGetNext:
                _code.Load64(Slot(0));
                _code.Bytes(0x48, 0x8B, 0x00, 0x48, 0x85, 0xC0);
                var empty = _code.JumpIf(0x84);
                _code.Load64(Slot(1), destinationRegister: true);
                _code.Bytes(0x48, 0x89, 0x02, 0xB8, 0x01, 0x00, 0x00, 0x00);
                var present = _code.Jump();
                _code.PatchRelative(empty, _code.Position);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(present, _code.Position);
                break;
            case BoundIntrinsic.NodeCollect:
                _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
                _code.Bytes(0x48, 0x89, 0xD1);
                _managedCollectorCalls.Add(_code.RelativeCall());
                break;
            case BoundIntrinsic.None or BoundIntrinsic.BufferTryCreate or BoundIntrinsic.BufferCount
                or BoundIntrinsic.BufferCapacity or BoundIntrinsic.BufferRead or BoundIntrinsic.BufferSet
                or BoundIntrinsic.BufferTryAppend or BoundIntrinsic.BufferClear:
            default:
                throw new InvalidOperationException("Unknown managed-node intrinsic.");
        }
    }

    private void EmitManagedCollector()
    {
        if (!_managedNodes)
        {
            return;
        }
        _managedCollectorStart = _code.Position;
        foreach (var call in _managedCollectorCalls)
        {
            _code.PatchRelative(call, _managedCollectorStart);
        }
        _code.UseFramePointer = false;
        Prolog(88);
        _code.Bytes(0x48, 0x89, 0xC8); // runtime state is the sole argument
        _code.Store64(32);
        _code.Clear(72);

        // Reset marks on the allocation list.
        _code.Bytes(0x48, 0x8B, 0x01);
        var reset = _code.Position;
        _code.Bytes(0x48, 0x85, 0xC0);
        var marksReset = _code.JumpIf(0x84);
        _code.Bytes(0xC7, 0x40, 0x14, 0x00, 0x00, 0x00, 0x00);
        _code.Bytes(0x48, 0x8B, 0x40, 0x08);
        _code.PatchRelative(_code.Jump(), reset);
        _code.PatchRelative(marksReset, _code.Position);

        _code.Load64(32);
        _code.Bytes(0x48, 0x8B, 0x40, 0x08);
        _code.Store64(40);
        var frameLoop = _code.Position;
        _code.Load64(40);
        _code.Bytes(0x48, 0x85, 0xC0);
        var tracingDone = _code.JumpIf(0x84);
        _code.Clear(48);
        var rootLoop = _code.Position;
        _code.Load64(40, destinationRegister: true);
        _code.Load(48);
        _code.Bytes(0x3B, 0x42, 0x08);
        var nextFrame = _code.JumpIf(0x83);
        _code.Bytes(0x48, 0xC1, 0xE0, 0x04, 0x48, 0x01, 0xD0);
        _code.Bytes(0x83, 0x78, 0x18, 0x00);
        var nextRoot = _code.JumpIf(0x84);
        _code.Bytes(0x48, 0x8B, 0x40, 0x10, 0x48, 0x8B, 0x00);
        var edge = _code.Position;
        _code.Bytes(0x48, 0x85, 0xC0);
        var marked = _code.JumpIf(0x84);
        _code.Bytes(0x83, 0x78, 0x14, 0x00);
        var seen = _code.JumpIf(0x85);
        _code.Bytes(0xC7, 0x40, 0x14, 0x01, 0x00, 0x00, 0x00);
        _code.Bytes(0x48, 0x8B, 0x00);
        _code.PatchRelative(_code.Jump(), edge);
        _code.PatchRelative(marked, _code.Position);
        _code.PatchRelative(seen, _code.Position);
        _code.PatchRelative(nextRoot, _code.Position);
        _code.Bytes(0xFF, 0x84, 0x24, 0x30, 0x00, 0x00, 0x00);
        _code.PatchRelative(_code.Jump(), rootLoop);
        _code.PatchRelative(nextFrame, _code.Position);
        _code.Load64(40);
        _code.Bytes(0x48, 0x8B, 0x00);
        _code.Store64(40);
        _code.PatchRelative(_code.Jump(), frameLoop);
        _code.PatchRelative(tracingDone, _code.Position);

        _code.Load64(32);
        _code.Store64(64); // link is the address of the allocation-list pointer to examine
        var sweep = _code.Position;
        _code.Load64(64, destinationRegister: true);
        _code.Bytes(0x48, 0x8B, 0x12, 0x48, 0x85, 0xD2);
        var finished = _code.JumpIf(0x84);
        _code.Bytes(0x83, 0x7A, 0x14, 0x00);
        var live = _code.JumpIf(0x85);
        _code.Bytes(0x48, 0x89, 0xD0);
        _code.Store64(56); // preserve the node across HeapFree imports
        _code.Bytes(0x48, 0x8B, 0x42, 0x08);
        _code.Load64(64, destinationRegister: true);
        _code.Bytes(0x48, 0x89, 0x02); // unlink before freeing
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
        _code.MoveArgument(0, true);
        _code.Bytes(0x31, 0xD2);
        _code.Load64(56);
        _code.MoveArgument(2, true);
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapFree"));
        _code.Bytes(0x85, 0xC0);
        var freed = _code.JumpIf(0x85);
        _code.Byte(0xB9);
        _code.Int32(unchecked((int)0xE0540005));
        _imports.Add(_code.ImportCall());
        _code.Byte(0xCC);
        _code.PatchRelative(freed, _code.Position);
        _code.Load64(32);
        _code.Bytes(0xFF, 0x48, 0x10); // decrement live count
        _code.Bytes(0xFF, 0x84, 0x24, 0x48, 0x00, 0x00, 0x00);
        _code.PatchRelative(_code.Jump(), sweep);
        _code.PatchRelative(live, _code.Position);
        _code.Bytes(0x48, 0x8D, 0x42, 0x08);
        _code.Store64(64);
        _code.PatchRelative(_code.Jump(), sweep);
        _code.PatchRelative(finished, _code.Position);
        _code.Load(72);
        _frame = 88;
        Epilog();
        _managedCollectorEnd = _code.Position;
        if (_managedCollectorEnd > MaximumText)
        {
            throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
        }
    }
}
