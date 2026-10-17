using System;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void EmitNativeVTableCall(int slot, int vtableByteOffset)
    {
        var byteOffset = checked(slot * NativeTargetProfile.PointerSize);
        if (vtableByteOffset == 0)
        {
            _code.Bytes(0x48, 0x8B, 0x01);
        }
        else if (vtableByteOffset <= sbyte.MaxValue)
        {
            _code.Bytes(0x48, 0x8B, 0x41, checked((byte)vtableByteOffset));
        }
        else
        {
            _code.Bytes(0x48, 0x8B, 0x81);
            _code.Int32(vtableByteOffset);
        }
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
            var nativeOutput = NativeVTableCallContract.OutputForParameter(function, index);
            if (parameter.IsOut && nativeOutput is null)
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
                if (nativeOutput is null && !parameter.IsOut
                    && parameter.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                {
                    _code.MoveFloatingArgument(index, parameter.Type == PrimitiveType.Float64);
                }
                else
                {
                    _code.MoveArgument(index,
                        (parameter.IsOut && nativeOutput is null)
                            || Size(parameter.Type) > 4 || IsIndirect(parameter.Type));
                }
            }
            else
            {
                _code.StoreOutgoing64(32 + (8 * (index - 4)));
            }
        }

        EmitNativeVTableCall(method.Slot, method.VTableByteOffset);
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
            hasOutputParameter |= function.Variables[index].IsOut
                && NativeVTableCallContract.OutputForParameter(function, index) is null;
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
