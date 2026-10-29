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

    private void EmitNativeOutputReservation(BoundNativeOutputReservation reservation)
    {
        var function = _program.Functions[_currentFunction];
        if ((uint)reservation.Variable >= (uint)function.Variables.Length
            || function.Variables[reservation.Variable].Type != reservation.PointeeType
            || !function.Variables[reservation.Variable].IsInitializationLocal
            || !DirectNativeImportContract.IsNativeOutputCellType(reservation.PointeeType,
                _program.Structures, _program.NativeFunctionPointerSignatures,
                _program.NativePointerTypes))
        {
            throw new InvalidOperationException("The native output reservation has an invalid destination.");
        }

        _code.Address(Slot(reservation.Variable));
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
        Expression(read.Address, spill);
        if (!IsIndirect(read.Type))
        {
            _code.LoadPointerField(0, Size(read.Type));
        }
    }

    private void EmitCheckedReferenceFormation(BoundReferenceFormation formation, int spill)
    {
        if (!CheckedReferenceTypeContract.IsValidType(
            formation.Type, _program.CheckedReferenceTypes))
        {
            throw new InvalidOperationException("The checked-reference place is invalid.");
        }

        var referenceType = _program.CheckedReferenceTypes[CheckedReferenceTypes.Index(formation.Type)];
        if (referenceType.IsGarbageCollected || referenceType.IsInitialization
            || referenceType.IsOptional || referenceType.ReferentType != formation.Place.Type)
        {
            throw new InvalidOperationException("The checked-reference formation has an unsupported type.");
        }

        if (formation.Place is BoundRead read)
        {
            if ((uint)read.Variable
                >= (uint)_program.Functions[_currentFunction].Variables.Length)
            {
                throw new InvalidOperationException("The checked-reference local is invalid.");
            }
            var variable = _program.Functions[_currentFunction].Variables[read.Variable];
            if (variable.Type != read.Type
                || referenceType.ReferentType != variable.Type)
            {
                throw new InvalidOperationException("The checked-reference local type is invalid.");
            }
            if (variable.IsImplicitRef)
            {
                _code.Load64(Slot(read.Variable));
            }
            else
            {
                _code.Address(variable.IsOut ? OutSlot(read.Variable) : Slot(read.Variable));
            }
        }
        else
        {
            EmitCheckedReferencePlaceAddress(formation.Place, spill);
        }
    }

    private void EmitCheckedReferencePlaceAddress(BoundExpression place, int spill)
    {
        switch (place)
        {
            case BoundRead read:
                var variable = _program.Functions[_currentFunction].Variables[read.Variable];
                if (variable.IsImplicitRef)
                {
                    _code.Load64(Slot(read.Variable));
                }
                else
                {
                    _code.Address(variable.IsOut ? OutSlot(read.Variable) : Slot(read.Variable));
                }
                break;
            case BoundNativeFixedArrayElement element:
                if ((uint)element.Variable
                    >= (uint)_program.Functions[_currentFunction].Variables.Length
                    || _program.Functions[_currentFunction].Variables[element.Variable].Type
                        != element.Layout.CarrierType
                    || _program.Functions[_currentFunction].Variables[element.Variable]
                        .NativeFixedArrayLayout != element.Layout
                    || !NativeFixedArrayContract.MatchesLayout(element.Layout, _program)
                    || element.Type != element.Layout.ElementType)
                {
                    throw new InvalidOperationException(
                        "The checked-reference array element place is invalid.");
                }
                EmitNativeArrayElementAddress(element.Variable, element.Index, spill,
                    element.Layout.ElementCount, element.Layout.ElementStride);
                break;
            case BoundFieldRead field:
                var receiverIndex = (int)field.Receiver.Type - 100;
                if (receiverIndex < 0 || receiverIndex >= _program.Structures.Length
                    || ManagedClassTypes.IsClass(field.Receiver.Type))
                {
                    throw new InvalidOperationException(
                        "A checked reference to this field place is not supported.");
                }
                var fields = _program.Structures[receiverIndex].Fields;
                if ((uint)field.Field >= (uint)fields.Length
                    || fields[field.Field].Type != field.Type)
                {
                    throw new InvalidOperationException(
                        "The checked reference field place is invalid.");
                }
                if (IsUnique(field.Receiver.Type))
                {
                    if (field.Receiver is not BoundRead)
                    {
                        throw new InvalidOperationException(
                            "A checked reference to a nested owner field is not supported.");
                    }
                    EmitUniqueFieldPlace(field);
                    break;
                }
                if (_program.Structures[receiverIndex].CleanupMethod is not null)
                {
                    throw new InvalidOperationException(
                        "A checked reference to this cleanup-bearing field place is not supported.");
                }
                EmitCheckedReferencePlaceAddress(field.Receiver, spill);
                var offset = fields[field.Field].Offset;
                if (offset != 0)
                {
                    _code.PointerOffset(offset);
                }
                break;
            default:
                throw new InvalidOperationException(
                    "The checked-reference place has an unsupported storage form.");
        }
    }

    private void EmitCheckedReferenceReborrow(BoundReferenceReborrow reborrow, int spill)
    {
        if (!CheckedReferenceTypeContract.IsValidType(
                reborrow.Type, _program.CheckedReferenceTypes)
            || !CheckedReferenceTypeContract.IsValidType(
                reborrow.Descriptor.Type, _program.CheckedReferenceTypes))
        {
            throw new InvalidOperationException("The checked-reference reborrow has an invalid type.");
        }
        var target = _program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(reborrow.Type)];
        var source = _program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(reborrow.Descriptor.Type)];
        if (target.IsGarbageCollected || target.IsInitialization || target.IsOptional
            || source.IsGarbageCollected || source.IsInitialization || source.IsOptional
            || target.ReferentType != source.ReferentType
            || (target.IsWritable && !source.IsWritable))
        {
            throw new InvalidOperationException(
                "The checked-reference reborrow changes its referent or amplifies authority.");
        }

        Expression(reborrow.Descriptor, spill);
    }

    private void EmitCheckedReferenceRead(BoundReferenceRead read, int spill)
    {
        if (!CheckedReferenceTypeContract.IsValidType(read.Descriptor.Type, _program.CheckedReferenceTypes)
            || Size(read.Type) == 0)
        {
            throw new InvalidOperationException("The checked-reference read has an unsupported type.");
        }

        var referenceType = _program.CheckedReferenceTypes[CheckedReferenceTypes.Index(read.Descriptor.Type)];
        if (referenceType.IsGarbageCollected || referenceType.IsInitialization
            || referenceType.IsOptional || referenceType.ReferentType != read.Type)
        {
            throw new InvalidOperationException("The checked-reference descriptor does not match its referent.");
        }

        Expression(read.Descriptor, spill);
        if (!IsIndirect(read.Type))
        {
            _code.LoadPointerField(0, Size(read.Type));
        }
    }

    private void EmitNativePointerWrite(BoundNativePointerWrite write, int spill)
    {
        EmitPointerValueWrite(write.Address, write.Value, spill);
    }

    private void EmitCheckedReferenceWrite(BoundReferenceWrite write, int spill)
    {
        if (!CheckedReferenceTypeContract.IsValidType(
                write.Descriptor.Type, _program.CheckedReferenceTypes))
        {
            throw new InvalidOperationException("The checked-reference store has an invalid descriptor type.");
        }

        var referenceType = _program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(write.Descriptor.Type)];
        if (!referenceType.IsWritable || referenceType.IsGarbageCollected
            || referenceType.IsInitialization || referenceType.IsOptional
            || referenceType.ReferentType != write.Value.Type
            || !IsSupportedCheckedReferenceStoreType(write.Value.Type))
        {
            throw new InvalidOperationException("The checked-reference store violates its access or value contract.");
        }

        if (IsUnique(write.Value.Type))
        {
            EmitCheckedReferenceOwnerWrite(write, spill);
            return;
        }

        EmitPointerValueWrite(write.Descriptor, write.Value, spill);
    }

    private void EmitCheckedReferenceOwnerWrite(BoundReferenceWrite write, int spill)
    {
        var type = write.Value.Type;
        var replacement = _replacementSlots[_currentFunction];
        var oldReplacement = _oldReplacementSlots[_currentFunction];
        if (replacement < 0 || oldReplacement < 0)
        {
            throw new InvalidOperationException(
                "Verified reference owner replacement has no staging storage.");
        }

        Expression(write.Descriptor, spill + 1);
        _code.Store64(Spill(spill));
        Expression(write.Value, spill + 1);
        StoreValue(replacement, type);

        _code.Load64(Spill(spill));
        CopyToFrame(oldReplacement, Size(type));
        EmitDropValue(oldReplacement, type);

        _code.Load64(Spill(spill), destinationRegister: true);
        LoadValue(replacement, type);
        if (IsIndirect(type))
        {
            _code.CopyMemory(Size(type));
            return;
        }

        switch (Size(type))
        {
            case 1:
                _code.Bytes(0x88, 0x02);
                break;
            case 2:
                _code.Bytes(0x66, 0x89, 0x02);
                break;
            case 4:
                _code.Bytes(0x89, 0x02);
                break;
            case 8:
                _code.Bytes(0x48, 0x89, 0x02);
                break;
            default:
                throw new InvalidOperationException(
                    "A checked-reference owner store has an unsupported value width.");
        }
    }

    private bool IsSupportedCheckedReferenceStoreType(PrimitiveType type) =>
        CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
            type, _program.Structures, _program.Classes,
            _program.EnumDefinitions.Length);

    private void EmitPointerValueWrite(BoundExpression address, BoundExpression value, int spill)
    {
        Expression(address, spill + 1);
        _code.Store64(Spill(spill));
        Expression(value, spill + 1);
        if (IsIndirect(value.Type))
        {
            _code.Load64(Spill(spill), destinationRegister: true);
            _code.CopyMemory(Size(value.Type));
            return;
        }

        var width = Size(value.Type);
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
