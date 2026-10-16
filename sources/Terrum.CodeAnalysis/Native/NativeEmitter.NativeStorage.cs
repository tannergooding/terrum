// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void EmitNativeArrayElementAddress(int variable, BoundExpression index, int spill,
        int elementCount, int elementStride)
    {
        Expression(index, spill);
        _code.Byte(0x3D);
        _code.Int32(elementCount);
        RuntimeBranch(0x83, 3);
        if (elementStride <= sbyte.MaxValue)
        {
            _code.Bytes(0x6B, 0xC0, checked((byte)elementStride));
        }
        else
        {
            _code.Bytes(0x69, 0xC0);
            _code.Int32(elementStride);
        }
        _code.Bytes(0x48, 0x89, 0xC2);
        _code.Address(Slot(variable));
        _code.Bytes(0x48, 0x01, 0xD0);
    }

    private void EmitNativeStorageAddress(BoundNativeStorageAddress address, int spill)
    {
        if (address.Type != address.AddressType
            || address.ElementCount <= 0 || address.ElementStride <= 0
            || address.ByteExtent <= 0)
        {
            throw new InvalidOperationException("The native storage address has an invalid extent.");
        }

        var completeExtent = checked(address.ElementCount * address.ElementStride);
        if (address.ByteExtent > completeExtent)
        {
            throw new InvalidOperationException("The native storage address exceeds its carrier.");
        }
        if (address.ByteExtent == completeExtent)
        {
            Expression(address.Index, spill);
            _code.Byte(0x3D);
            _code.Int32(address.ElementCount);
            RuntimeBranch(0x83, 3);
            _code.Bytes(0x85, 0xC0);
            RuntimeBranch(0x85, 3);
            _code.Address(Slot(address.Variable));
        }
        else
        {
            EmitNativeArrayElementAddress(address.Variable, address.Index, spill,
                address.ElementCount, address.ElementStride);
        }
    }

    private void EmitNativePointerAddress(BoundNativePointerAddress address)
    {
        var variable = _program.Functions[_currentFunction].Variables[address.Variable];
        if (variable.IsImplicitRef)
        {
            _code.Load64(Slot(address.Variable));
        }
        else
        {
            _code.Address(variable.IsOut ? OutSlot(address.Variable) : Slot(address.Variable));
        }
    }

    private void EmitNativePointerRead(BoundNativePointerRead read, int spill)
    {
        Expression(read.Pointer, spill);
        if (!IsIndirect(read.Type))
        {
            _code.LoadPointerField(0, Size(read.Type));
        }
    }

    private void EmitNativePointerWrite(BoundNativePointerWrite write, int spill)
    {
        Expression(write.Pointer, spill + 1);
        _code.Store64(Spill(spill));
        Expression(write.Value, spill + 1);
        if (IsIndirect(write.Value.Type))
        {
            _code.Load64(Spill(spill), destinationRegister: true);
            _code.CopyMemory(Size(write.Value.Type));
            return;
        }

        var width = Size(write.Value.Type);
        if (width == sizeof(ulong))
        {
            _code.Bytes(0x48, 0x89, 0xC1);
        }
        else
        {
            _code.Bytes(0x89, 0xC1);
        }
        _code.Load64(Spill(spill), destinationRegister: true);
        switch (width)
        {
            case 1:
                _code.Bytes(0x88, 0x0A);
                break;
            case 2:
                _code.Bytes(0x66, 0x89, 0x0A);
                break;
            case 4:
                _code.Bytes(0x89, 0x0A);
                break;
            case 8:
                _code.Bytes(0x48, 0x89, 0x0A);
                break;
            default:
                throw new InvalidOperationException("A native pointer write has an unsupported value width.");
        }
    }

    private void EmitNativeFixedArrayElement(BoundNativeFixedArrayElement element, int spill)
    {
        EmitNativeArrayElementAddress(element.Variable, element.Index, spill,
            element.Layout.ElementCount, element.Layout.ElementStride);
        if (IsIndirect(element.Type))
        {
            return;
        }
        switch (Size(element.Type))
        {
            case 1:
                _code.Bytes(0x0F, 0xB6, 0x00);
                break;
            case 2:
                _code.Bytes(0x0F, 0xB7, 0x00);
                break;
            case 4:
                _code.Bytes(0x8B, 0x00);
                break;
            case 8:
                _code.Bytes(0x48, 0x8B, 0x00);
                break;
            default:
                throw new InvalidOperationException("A fixed-array element has an unsupported native width.");
        }
    }

    private void EmitNativeFixedArrayPointerAddress(BoundNativeFixedArrayPointerAddress address, int spill) =>
        EmitNativeArrayElementAddress(address.Variable, address.Index, spill,
            address.Layout.ElementCount, address.Layout.ElementStride);

    private void EmitNativeFixedArrayWrite(BoundNativeFixedArrayWrite write, int spill)
    {
        EmitNativeArrayElementAddress(write.Variable, write.Index, spill,
            write.Layout.ElementCount, write.Layout.ElementStride);
        _code.Store64(Spill(spill));
        Expression(write.Value, spill + 1);
        if (IsIndirect(write.Value.Type))
        {
            _code.Load64(Spill(spill), destinationRegister: true);
            _code.CopyMemory(Size(write.Value.Type));
        }
        else
        {
            if (Size(write.Value.Type) == sizeof(ulong))
            {
                _code.Bytes(0x48, 0x89, 0xC1);
            }
            else
            {
                _code.Bytes(0x89, 0xC1);
            }
            _code.Load64(Spill(spill));
            switch (Size(write.Value.Type))
            {
                case 1:
                    _code.Bytes(0x88, 0x08);
                    break;
                case 2:
                    _code.Bytes(0x66, 0x89, 0x08);
                    break;
                case 4:
                    _code.Bytes(0x89, 0x08);
                    break;
                case 8:
                    _code.Bytes(0x48, 0x89, 0x08);
                    break;
                default:
                    throw new InvalidOperationException("A fixed-array element has an unsupported native width.");
            }
        }
    }
}
