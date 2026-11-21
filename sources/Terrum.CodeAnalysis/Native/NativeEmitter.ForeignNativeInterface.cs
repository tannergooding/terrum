// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void EmitForeignNativeInterfaceCall(BoundCall call, int spill)
    {
        var caller = _program.Functions[_currentFunction];
        if (call.ForeignInterfaceCall is not
                BoundForeignInterfaceCall foreignCall
            || call.Function != BoundCall.ForeignInterfaceFunction
            || !ForeignNativeInterfaceContract.IsValidCall(call, caller,
                _program)
            || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                foreignCall, _program, out var target))
        {
            throw new InvalidOperationException(
                "The native emitter received an invalid ForeignNativeInterface call.");
        }

        NativeEmitter.EnsureNativeCallingConventionSupported(
            foreignCall.CallingConvention);
        if (IsIndirectResult(call.Type))
        {
            throw new NotSupportedException(
                "The native emitter does not support indirect results from ForeignNativeInterface calls.");
        }

        var parameterArgumentIndexes = new int[target.ParameterCount];
        Array.Fill(parameterArgumentIndexes, -1);
        for (var argumentIndex = 0;
            argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var parameterIndex = call.ParameterIndex(argumentIndex);
            if ((uint)parameterIndex
                    >= (uint)parameterArgumentIndexes.Length
                || parameterArgumentIndexes[parameterIndex] >= 0)
            {
                throw new InvalidOperationException(
                    "A verified ForeignNativeInterface call has an invalid formal map.");
            }

            parameterArgumentIndexes[parameterIndex] = argumentIndex;
        }
        if (parameterArgumentIndexes.Contains(-1))
        {
            throw new InvalidOperationException(
                "A verified ForeignNativeInterface call omits a native formal.");
        }

        var argumentsBase = spill + CallResultSpills(call);
        var receiverSpill = argumentsBase;
        var nextSpill = receiverSpill
            + SpillSlotsForType(foreignCall.Receiver.Type);
        var argumentSpills = new int[call.Arguments.Length];
        for (var index = 0; index < call.Arguments.Length; index++)
        {
            argumentSpills[index] = nextSpill;
            nextSpill += CallArgumentValueSpills(call, index);
        }
        for (var index = 0; index < call.Arguments.Length; index++)
        {
            nextSpill += CallArgumentAuxiliarySpills(
                call.Arguments[index]);
        }

        var expressionSpill = nextSpill;
        var ignoredMaximumArguments = 0;
        var receiverExpressionSpills = ExpressionSpills(
            foreignCall.Receiver, ref ignoredMaximumArguments);
        var argumentExpressionSpills = 0;
        foreach (var argument in call.Arguments)
        {
            argumentExpressionSpills = Math.Max(argumentExpressionSpills,
                ExpressionSpills(argument, ref ignoredMaximumArguments));
        }

        Expression(foreignCall.Receiver, expressionSpill);
        StoreValue(SpillOffsetForType(receiverSpill,
            foreignCall.Receiver.Type), foreignCall.Receiver.Type);
        ClearNormalBackingSpillRoots(expressionSpill,
            receiverExpressionSpills);

        for (var argumentIndex = 0;
            argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var argument = call.Arguments[argumentIndex];
            if (argument is BoundNativeOutputReservation)
            {
                Expression(argument, expressionSpill);
                _code.Store64(Spill(argumentSpills[argumentIndex]));
            }
            else
            {
                if (ManagedClassTypes.IsManaged(argument.Type)
                    || IsUnique(argument.Type))
                {
                    throw new NotSupportedException(
                        "A ForeignNativeInterface call cannot stage managed or uniquely owned values.");
                }

                Expression(argument, expressionSpill);
                StoreValue(SpillOffsetForType(
                    argumentSpills[argumentIndex], argument.Type),
                    argument.Type);
            }

            ClearNormalBackingSpillRoots(expressionSpill,
                argumentExpressionSpills);
        }

        for (var parameterIndex = 0;
            parameterIndex < target.ParameterCount; parameterIndex++)
        {
            var argumentIndex
                = parameterArgumentIndexes[parameterIndex];
            var abiArgumentIndex = parameterIndex + 1;
            if (abiArgumentIndex < 4)
            {
                continue;
            }

            LoadForeignNativeInterfaceArgument(call, target,
                parameterIndex, argumentIndex, argumentSpills);
            _code.StoreOutgoing64(32 + (8 * (abiArgumentIndex - 4)));
        }

        _code.Load64(Spill(receiverSpill));
        _code.MoveArgument(0, wide: true);
        for (var parameterIndex = 0;
            parameterIndex < target.ParameterCount; parameterIndex++)
        {
            var abiArgumentIndex = parameterIndex + 1;
            if (abiArgumentIndex >= 4)
            {
                break;
            }

            var argumentIndex
                = parameterArgumentIndexes[parameterIndex];
            LoadForeignNativeInterfaceArgument(call, target,
                parameterIndex, argumentIndex, argumentSpills);
            var parameterType = target.Variables[parameterIndex].Type;
            if (call.Arguments[argumentIndex]
                    is not BoundNativeOutputReservation
                && parameterType is (PrimitiveType.Float32
                    or PrimitiveType.Float64))
            {
                _code.MoveFloatingArgument(abiArgumentIndex,
                    parameterType == PrimitiveType.Float64);
            }
            else
            {
                _code.MoveArgument(abiArgumentIndex,
                    call.Arguments[argumentIndex]
                        is BoundNativeOutputReservation
                        || IsStruct(parameterType) || IsWide(parameterType)
                        || IsIndirect(parameterType));
            }
        }

        EmitForeignNativeInterfaceVTableCall(
            foreignCall.Member.MemberOrdinal,
            foreignCall.VTableByteOffset);
        if (foreignCall.DoesNotReturn)
        {
            _code.Bytes(0x0F, 0x0B);
            return;
        }

        if (call.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
        {
            _code.MoveFloatingReturnToAccumulator(
                call.Type == PrimitiveType.Float64);
        }
        else if (call.Type != PrimitiveType.Void)
        {
            NormalizeNativeNarrowReturn(call.Type);
        }
    }

    private void LoadForeignNativeInterfaceArgument(BoundCall call,
        BoundFunction target, int parameterIndex, int argumentIndex,
        int[] argumentSpills)
    {
        var argument = call.Arguments[argumentIndex];
        if (argument is BoundNativeOutputReservation)
        {
            _code.Load64(Spill(argumentSpills[argumentIndex]));
            return;
        }

        LoadValue(SpillOffsetForType(argumentSpills[argumentIndex],
            argument.Type), target.Variables[parameterIndex].Type);
    }

    private void EmitForeignNativeInterfaceVTableCall(int slot,
        uint vtableByteOffset)
    {
        if (slot < 0)
        {
            throw new InvalidOperationException(
                "A verified ForeignNativeInterface call has a negative vtable slot.");
        }

        if (vtableByteOffset <= int.MaxValue)
        {
            var offset = checked((int)vtableByteOffset);
            if (offset == 0)
            {
                _code.Bytes(0x48, 0x8B, 0x01);
            }
            else if (offset <= sbyte.MaxValue)
            {
                _code.Bytes(0x48, 0x8B, 0x41, checked((byte)offset));
            }
            else
            {
                _code.Bytes(0x48, 0x8B, 0x81);
                _code.Int32(offset);
            }
        }
        else
        {
            _code.Bytes(0x48, 0xB8);
            _code.Int64(vtableByteOffset);
            _code.Bytes(0x48, 0x01, 0xC8);
            _code.Bytes(0x48, 0x8B, 0x00);
        }

        var byteOffset = checked(slot * NativeTargetProfile.PointerSize);
        if (byteOffset <= sbyte.MaxValue)
        {
            _code.Bytes(0xFF, 0x50, checked((byte)byteOffset));
        }
        else
        {
            _code.Bytes(0xFF, 0x90);
            _code.Int32(byteOffset);
        }
    }
}
