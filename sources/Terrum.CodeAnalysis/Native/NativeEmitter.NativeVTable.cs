using System;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void EmitNativeVTableCall(int slot)
    {
        var byteOffset = checked(slot * NativeTargetProfile.PointerSize);
        _code.Bytes(0x48, 0x8B, 0x01);
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

    private void EmitNativeVTableMethodFunction(BoundFunction function)
    {
        if (!NativeVTableCallContract.IsValid(function, _program)
            || function.NativeVTableMethod is not NativeVTableMethodDefinition method)
        {
            throw new InvalidOperationException("The native vtable declaration does not retain its ABI contract.");
        }

        for (var index = 0; index < function.ParameterCount; index++)
        {
            var parameter = function.Variables[index];
            if (parameter.IsOut)
            {
                _code.Address(OutSlot(index));
            }
            else if (parameter.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
            {
                LoadValue(Slot(index), parameter.Type);
            }
            else
            {
                LoadValue(Slot(index), parameter.Type);
            }

            if (index < 4)
            {
                if (!parameter.IsOut
                    && parameter.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                {
                    _code.MoveFloatingArgument(index, parameter.Type == PrimitiveType.Float64);
                }
                else
                {
                    _code.MoveArgument(index,
                        parameter.IsOut || Size(parameter.Type) > 4 || IsIndirect(parameter.Type));
                }
            }
            else
            {
                _code.StoreOutgoing64(32 + (8 * (index - 4)));
            }
        }

        EmitNativeVTableCall(method.Slot);
        var returnType = function.ReturnType;
        if (returnType is PrimitiveType.Float32 or PrimitiveType.Float64)
        {
            _code.MoveFloatingReturnToAccumulator(returnType == PrimitiveType.Float64);
        }
        else
        {
            NormalizeNativeNarrowReturn(returnType);
        }

        var hasOutputParameter = false;
        for (var index = 0; index < function.ParameterCount; index++)
        {
            hasOutputParameter |= function.Variables[index].IsOut;
        }
        if (hasOutputParameter)
        {
            if (returnType is not (PrimitiveType.Bool or PrimitiveType.Void))
            {
                StoreValue(PendingReturnSlot(), returnType);
            }
            CommitOutParameters();
            if (returnType is not (PrimitiveType.Bool or PrimitiveType.Void))
            {
                LoadValue(PendingReturnSlot(), returnType);
            }
        }
    }
}
