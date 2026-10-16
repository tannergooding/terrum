// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private readonly int[] _managedRootFrames;
    private readonly Dictionary<int, int>[] _managedRootFlags;
    private readonly List<int> _managedCollectorCalls = [];
    private readonly List<(int Displacement, int Descriptor)> _managedDescriptorReferences = [];
    private readonly Dictionary<int, int> _managedAbandonedFactories = [];
    private int _managedCollectorStart;
    private int _managedCollectorEnd;

    private long PrepareManagedRoots(int functionIndex, BoundFunction function, int spillSlots,
        bool needsReturnSnapshot, long at)
    {
        if (!_managedNodes || function.Intrinsic is not (BoundIntrinsic.None or BoundIntrinsic.ClassTryCreate))
        {
            return at;
        }
        var roots = _managedRootFlags[functionIndex];
        var outOrdinal = 0;
        for (var i = 0; i < function.Variables.Length; i++)
        {
            var variable = function.Variables[i];
            if (ManagedClassTypes.IsManaged(variable.Type))
            {
                _ = roots.TryAdd(variable.IsOut
                    ? _outBases[functionIndex] + (16 * outOrdinal) : _slots[functionIndex][i], 0);
            }
            if (variable.IsOut)
            {
                outOrdinal++;
            }
        }
        // Large calls can otherwise spend more than the page-safe frame limit on
        // root descriptors for scalar-only staging slots.
        var managedSpills = spillSlots > 64 ? ManagedSpillSlots(function) : null;
        for (var spill = 0; spill < spillSlots; spill++)
        {
            if (managedSpills is null || managedSpills.Contains(spill))
            {
                _ = roots.TryAdd(_spillBases[functionIndex] + (16 * spill), 0);
            }
        }
        if (ManagedClassTypes.IsManaged(function.ReturnType) && needsReturnSnapshot)
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

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only expressions and statements with managed staging slots need descriptors.")]
    private static HashSet<int> ManagedSpillSlots(BoundFunction function)
    {
        var slots = new HashSet<int>();
        CollectStatement(function.Body);
        return slots;

        void CollectStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var child in block.Statements)
                    {
                        CollectStatement(child);
                    }
                    break;
                case BoundViewScope view:
                    CollectExpression(view.Start, 0);
                    CollectExpression(view.RangeEnd ?? view.Length, 1);
                    CollectExpression(view.Owner, 2);
                    CollectStatement(view.Body);
                    break;
                case BoundViewSet write:
                    CollectExpression(write.Index, 0);
                    CollectExpression(write.Value, 1);
                    break;
                case BoundLocal local:
                    CollectExpression(local.Initializer, 0);
                    break;
                case BoundAssignment assignment:
                    CollectExpression(assignment.Value, 0);
                    break;
                case BoundOptionalAssignment optionalAssignment:
                    if (optionalAssignment.Place is BoundFieldRead field)
                    {
                        _ = slots.Add(0);
                        CollectExpression(field.Receiver, 0);
                        CollectExpression(optionalAssignment.Value, 1);
                    }
                    else
                    {
                        CollectExpression(optionalAssignment.Value, 0);
                    }
                    break;
                case BoundFieldAssignment assignment:
                    if (ManagedClassTypes.IsClass(function.Variables[assignment.Variable].Type))
                    {
                        _ = slots.Add(0);
                        CollectExpression(assignment.Value, 1);
                    }
                    else
                    {
                        CollectExpression(assignment.Value, 0);
                    }
                    break;
                case BoundBufferAssignment assignment:
                    CollectExpression(assignment.Index, 0);
                    CollectExpression(assignment.Value, 1);
                    break;
                case BoundExpressionStatement expression:
                    CollectExpression(expression.Expression, 0);
                    break;
                case BoundIf conditional:
                    CollectExpression(conditional.Condition, 0);
                    CollectStatement(conditional.Then);
                    if (conditional.Else is not null)
                    {
                        CollectStatement(conditional.Else);
                    }
                    break;
                case BoundWhile loop:
                    CollectExpression(loop.Condition, 0);
                    CollectStatement(loop.Body);
                    if (loop.Iterator is not null)
                    {
                        CollectStatement(loop.Iterator);
                    }
                    break;
                case BoundTryFinally cleanup:
                    CollectStatement(cleanup.Body);
                    CollectStatement(cleanup.Finally);
                    break;
                case BoundUnsafe unsafeBlock:
                    CollectStatement(unsafeBlock.Body);
                    break;
                case BoundTryCatch recovery:
                    CollectStatement(recovery.Body);
                    CollectStatement(recovery.Catch);
                    if (recovery.Finally is not null)
                    {
                        CollectStatement(recovery.Finally);
                    }
                    break;
                case BoundRequire requirement:
                    CollectExpression(requirement.Condition, 0);
                    break;
                case BoundReturn { Value: BoundExpression value }:
                    CollectExpression(value, 0);
                    break;
            }
        }

        void CollectExpression(BoundExpression expression, int spill)
        {
            switch (expression)
            {
                case BoundUnary unary:
                    CollectExpression(unary.Operand, spill);
                    break;
                case BoundUpdate update:
                    if (update.Place is BoundFieldRead)
                    {
                        _ = slots.Add(spill);
                    }
                    CollectExpression(update.Place is BoundFieldRead receiver ? receiver.Receiver
                        : update.Place is BoundBufferRead updateBuffer ? updateBuffer.Index : update.Place, spill);
                    break;
                case BoundCompoundAssignment compound:
                    if (compound.Place is BoundFieldRead)
                    {
                        _ = slots.Add(spill);
                    }
                    CollectExpression(compound.Place is BoundFieldRead compoundReceiver ? compoundReceiver.Receiver
                        : compound.Place is BoundBufferRead compoundBuffer ? compoundBuffer.Index : compound.Place, spill);
                    CollectExpression(compound.Right, spill + 4);
                    break;
                case BoundNominalUpdate update:
                    if (update.Place is BoundFieldRead)
                    {
                        _ = slots.Add(spill);
                    }
                    CollectExpression(update.Place is BoundFieldRead nominalReceiver
                        ? nominalReceiver.Receiver : update.Place, spill);
                    break;
                case BoundCast cast:
                    CollectExpression(cast.Operand, spill);
                    break;
                case BoundOptionalLift lift:
                    CollectExpression(lift.Value, spill);
                    break;
                case BoundReadonlyProjection projection:
                    CollectExpression(projection.Value, spill);
                    break;
                case BoundIsNull test:
                    CollectExpression(test.Value, spill);
                    break;
                case BoundIsSome pattern:
                    CollectExpression(pattern.Value, spill);
                    break;
                case BoundIsVariablePattern pattern:
                    CollectExpression(pattern.Value, spill);
                    break;
                case BoundFieldRead field:
                    CollectExpression(field.Receiver, spill);
                    break;
                case BoundBufferRead buffer:
                    CollectExpression(buffer.Index, spill);
                    break;
                case BoundViewRead view:
                    CollectExpression(view.Index, spill);
                    break;
                case BoundSpanLength length:
                    CollectExpression(length.View, spill);
                    break;
                case BoundSpanRead read:
                    CollectExpression(read.View, spill);
                    CollectExpression(read.Index, spill + 1);
                    break;
                case BoundSpanAdvance advance:
                    CollectExpression(advance.View, spill);
                    break;
                case BoundBinary binary:
                    CollectExpression(binary.Left, spill);
                    if (ManagedClassTypes.IsManaged(binary.Left.Type))
                    {
                        _ = slots.Add(spill);
                    }
                    CollectExpression(binary.Right,
                        binary.Operator is Terrum.CodeAnalysis.Lexing.TokenKind.AmpersandAmpersand
                            or Terrum.CodeAnalysis.Lexing.TokenKind.PipePipe ? spill : spill + 1);
                    break;
                case BoundConditional conditional:
                    CollectExpression(conditional.Condition, spill);
                    CollectExpression(conditional.WhenTrue, spill);
                    CollectExpression(conditional.WhenFalse, spill);
                    break;
                case BoundCoalesce coalesce:
                    CollectExpression(coalesce.Optional, spill);
                    CollectExpression(coalesce.Fallback, spill);
                    break;
                case BoundUnsafeExpression unsafeExpression:
                    CollectExpression(unsafeExpression.Expression, spill);
                    break;
                case BoundCall call:
                    var argumentsBase = spill + 1;
                    for (var i = 0; i < call.Arguments.Length; i++)
                    {
                        var argument = call.Arguments[i];
                        if (ManagedClassTypes.IsManaged(argument.Type))
                        {
                            _ = slots.Add(argumentsBase + i);
                            if (argument is BoundOutArgument)
                            {
                                _ = slots.Add(argumentsBase + call.Arguments.Length + i);
                            }
                        }
                        if (argument is not BoundOutArgument)
                        {
                            CollectExpression(argument, argumentsBase + (call.Arguments.Length * 2));
                        }
                    }
                    break;
                case BoundIndirectCall call:
                    CollectExpression(call.Target, spill);
                    for (var i = 0; i < call.Arguments.Length; i++)
                    {
                        CollectExpression(call.Arguments[i], spill + 1 + i);
                    }
                    break;
            }
        }
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
            if (ManagedClassTypes.IsManaged(function.Variables[i].Type) && !function.Variables[i].IsOut)
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
                EmitManagedAllocation(0, 48);
                _code.Bytes(0x48, 0x85, 0xC0);
                var allocationFailed = _code.JumpIf(0x84);
                _code.Bytes(0x48, 0x89, 0xC2);
                _code.Bytes(0x48, 0xC7, 0x42, 0x20, 0x00, 0x00, 0x00, 0x00);
                _code.Load(Slot(0));
                _code.Bytes(0x89, 0x42, 0x28, 0x48, 0x89, 0xD0);
                _code.Load64(Slot(1), destinationRegister: true);
                _code.Bytes(0x48, 0x89, 0x02, 0xB8, 0x01, 0x00, 0x00, 0x00);
                var success = _code.Jump();
                _code.PatchRelative(allocationFailed, _code.Position);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(success, _code.Position);
                break;
            case BoundIntrinsic.NodeValue:
                _code.Load64(Slot(0));
                _code.LoadPointerField(40, 4);
                break;
            case BoundIntrinsic.NodeSetValue:
                _code.Load64(Slot(0), destinationRegister: true);
                _code.Load(Slot(1));
                _code.Bytes(0x89, 0x42, 0x28);
                break;
            case BoundIntrinsic.NodeSetNext:
                _code.Load64(Slot(0), destinationRegister: true);
                _code.Load64(Slot(1));
                _code.Bytes(0x48, 0x89, 0x42, 0x20);
                break;
            case BoundIntrinsic.NodeClearNext:
                _code.Load64(Slot(0), destinationRegister: true);
                _code.Bytes(0x31, 0xC0, 0x48, 0x89, 0x42, 0x20);
                break;
            case BoundIntrinsic.NodeTryGetNext:
                _code.Load64(Slot(0));
                _code.Bytes(0x48, 0x8B, 0x40, 0x20, 0x48, 0x85, 0xC0);
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
            case BoundIntrinsic.None or BoundIntrinsic.ClassTryCreate or BoundIntrinsic.BufferTryCreate or BoundIntrinsic.BufferCount
                or BoundIntrinsic.BufferCapacity or BoundIntrinsic.BufferRead or BoundIntrinsic.BufferSet
                or BoundIntrinsic.BufferTryAppend or BoundIntrinsic.BufferClear:
            default:
                throw new InvalidOperationException("Unknown managed-node intrinsic.");
        }
    }

    private void EmitDescriptorAddress(int descriptor)
    {
        _code.Bytes(0x48, 0x8D, 0x0D); // lea rcx,[rip+descriptor]
        var displacement = _code.Position;
        _code.Int32(0);
        _managedDescriptorReferences.Add((displacement, descriptor));
    }

    private void EmitManagedAllocation(int descriptor, int size)
    {
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
        _code.Bytes(0x81, 0x7A, 0x10);
        _code.Int32(NativeManagedNodeIntrinsics.MaximumNodes);
        var full = _code.JumpIf(0x83);
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
        _code.MoveArgument(0, true);
        _code.Bytes(0x31, 0xD2, 0x41, 0xB8);
        _code.Int32(size);
        _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapAlloc"));
        _code.Bytes(0x48, 0x85, 0xC0);
        var failed = _code.JumpIf(0x84);
        _code.Bytes(0x48, 0x89, 0xC2); // rdx = allocated object
        EmitDescriptorAddress(descriptor);
        _code.Bytes(0x48, 0x89, 0x4A, 0x10); // descriptor at +16
        _code.Bytes(0x48, 0xC7, 0x02, 0x00, 0x00, 0x00, 0x00);
        _code.Bytes(0xC7, 0x42, 0x18, 0x00, 0x00, 0x00, 0x00);
        _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
        _code.Bytes(0x48, 0x8B, 0x0A, 0x48, 0x89, 0x48, 0x08);
        _code.Bytes(0x48, 0x89, 0x02, 0xFF, 0x42, 0x10);
        var complete = _code.Jump();
        _code.PatchRelative(full, _code.Position);
        _code.PatchRelative(failed, _code.Position);
        _code.Bytes(0x31, 0xC0);
        _code.PatchRelative(complete, _code.Position);
    }

    private void EmitManagedDescriptors()
    {
        if (!_managedNodes)
        {
            return;
        }
        var offsets = new int[_program.Classes.Length + 1];
        List<int>? managedFieldOffsets = null;
        for (var descriptor = 0; descriptor < offsets.Length; descriptor++)
        {
            offsets[descriptor] = _code.Position;
            if (descriptor == 0)
            {
                _code.Int32(1);
                _code.Int32(48);
                _code.Int32(32);
            }
            else
            {
                var @class = _program.Classes[descriptor - 1];
                managedFieldOffsets?.Clear();
                foreach (var field in @class.Fields)
                {
                    if (ManagedClassTypes.IsManaged(field.Type))
                    {
                        (managedFieldOffsets ??= []).Add(field.Offset);
                    }
                }
                _code.Int32(managedFieldOffsets?.Count ?? 0);
                _code.Int32(@class.Size + 32);
                if (managedFieldOffsets is not null)
                {
                    foreach (var fieldOffset in managedFieldOffsets)
                    {
                        _code.Int32(fieldOffset + 32);
                    }
                }
            }
        }
        foreach (var (displacement, descriptor) in _managedDescriptorReferences)
        {
            _code.PatchRelative(displacement, offsets[descriptor]);
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
        Prolog(120);
        _code.Bytes(0x48, 0x89, 0xC8); // runtime state is the sole argument
        _code.Store64(32);
        _code.Clear(72);
        _code.Bytes(0x31, 0xC0);
        _code.Store64(80);
        var markCalls = new List<int>();

        _code.Bytes(0x48, 0x8B, 0x01);
        var reset = _code.Position;
        _code.Bytes(0x48, 0x85, 0xC0);
        var marksReset = _code.JumpIf(0x84);
        _code.Bytes(0xC7, 0x40, 0x18, 0x00, 0x00, 0x00, 0x00);
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
        markCalls.Add(_code.RelativeCall());
        _code.PatchRelative(nextRoot, _code.Position);
        _code.Bytes(0xFF, 0x84, 0x24, 0x30, 0x00, 0x00, 0x00);
        _code.PatchRelative(_code.Jump(), rootLoop);
        _code.PatchRelative(nextFrame, _code.Position);
        _code.Load64(40);
        _code.Bytes(0x48, 0x8B, 0x00);
        _code.Store64(40);
        _code.PatchRelative(_code.Jump(), frameLoop);
        _code.PatchRelative(tracingDone, _code.Position);

        var workLoop = _code.Position;
        _code.Load64(80);
        _code.Bytes(0x48, 0x85, 0xC0);
        var workDone = _code.JumpIf(0x84);
        _code.Store64(88);
        _code.Bytes(0x48, 0x8B, 0x08);
        _code.Bytes(0x48, 0x89, 0xC8);
        _code.Store64(80); // pop the intrusive work link
        _code.Load64(88);
        _code.Bytes(0x48, 0x8B, 0x40, 0x10);
        _code.Store64(104); // descriptor
        _code.Clear(96);
        var scan = _code.Position;
        _code.Load64(104, destinationRegister: true);
        _code.Load(96);
        _code.Bytes(0x3B, 0x02);
        var nextObject = _code.JumpIf(0x83);
        _code.Bytes(0x8B, 0x4C, 0x82, 0x08); // ecx = descriptor.referenceOffsets[index]
        _code.Load64(88);
        _code.Bytes(0x48, 0x8B, 0x04, 0x08); // rax = object field
        markCalls.Add(_code.RelativeCall());
        _code.Bytes(0xFF, 0x84, 0x24, 0x60, 0x00, 0x00, 0x00);
        _code.PatchRelative(_code.Jump(), scan);
        _code.PatchRelative(nextObject, _code.Position);
        _code.PatchRelative(_code.Jump(), workLoop);
        _code.PatchRelative(workDone, _code.Position);

        _code.Load64(32);
        _code.Store64(64); // link is the address of the allocation-list pointer to examine
        var sweep = _code.Position;
        _code.Load64(64, destinationRegister: true);
        _code.Bytes(0x48, 0x8B, 0x12, 0x48, 0x85, 0xD2);
        var finished = _code.JumpIf(0x84);
        _code.Bytes(0x83, 0x7A, 0x18, 0x00);
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
        _frame = 120;
        Epilog();
        _managedCollectorEnd = _code.Position;
        var mark = _code.Position;
        foreach (var call in markCalls)
        {
            _code.PatchRelative(call, mark);
        }
        _code.Bytes(0x48, 0x85, 0xC0);
        var noObject = _code.JumpIf(0x84);
        _code.Bytes(0x83, 0x78, 0x18, 0x00);
        var alreadyMarked = _code.JumpIf(0x85);
        _code.Bytes(0xC7, 0x40, 0x18, 0x01, 0x00, 0x00, 0x00);
        _code.Bytes(0x48, 0x8B, 0x8C, 0x24, 0x58, 0x00, 0x00, 0x00);
        _code.Bytes(0x48, 0x89, 0x08);
        _code.Bytes(0x48, 0x89, 0x84, 0x24, 0x58, 0x00, 0x00, 0x00);
        _code.PatchRelative(noObject, _code.Position);
        _code.PatchRelative(alreadyMarked, _code.Position);
        _code.Byte(0xC3);
        if (_code.Position > MaximumText)
        {
            throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
        }
    }
}
