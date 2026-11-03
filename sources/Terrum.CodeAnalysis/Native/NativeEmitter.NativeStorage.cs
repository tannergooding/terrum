// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal sealed partial class PeCompiler
{
    private void EmitNativeArrayElementAddress(int variable, BoundExpression index, int spill,
        ulong elementCount, ulong elementStride,
        ulong requiredElementCount = 1)
    {
        EmitNativeArrayElementOffset(index, spill, elementCount, elementStride,
            requiredElementCount);
        _code.Bytes(0x48, 0x89, 0xC2);
        _code.Address(Slot(variable));
        _code.Bytes(0x48, 0x01, 0xD0);
    }

    private void EmitNativeFixedArrayElementAddress(int variable,
        BoundExpression? fieldPlace, BoundExpression index, int spill,
        ulong elementCount, ulong elementStride,
        ulong requiredElementCount = 1)
    {
        if (fieldPlace is null)
        {
            EmitNativeArrayElementAddress(variable, index, spill,
                elementCount, elementStride, requiredElementCount);
            return;
        }

        _ = UpdateAddress(fieldPlace, spill + 1);
        _code.Store64(Spill(spill));
        EmitNativeArrayElementAddressFromBase(Spill(spill), index,
            spill + 1, elementCount, elementStride,
            requiredElementCount);
    }

    private void EmitNativeArrayElementAddressFromBase(int baseAddressSlot,
        BoundExpression index, int spill, ulong elementCount,
        ulong elementStride, ulong requiredElementCount = 1)
    {
        EmitNativeArrayElementOffset(index, spill, elementCount, elementStride,
            requiredElementCount);
        _code.Bytes(0x48, 0x89, 0xC2);
        _code.Load64(baseAddressSlot);
        _code.Bytes(0x48, 0x01, 0xD0);
    }

    private void EmitNativeFixedArrayPathAddress(int variable,
        ImmutableArray<BoundNativeFixedArrayProjection> projections, int spill,
        ulong requiredElementCount = 1,
        BoundExpression? fieldPlace = null)
    {
        if (projections.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(
                "A nested fixed-array access has no projections.");
        }

        var first = projections[0];
        EmitNativeFixedArrayElementAddress(variable, fieldPlace,
            first.Index, spill,
            first.Layout.ElementCount, first.Layout.ElementStride,
            projections.Length == 1 ? requiredElementCount : 1);
        for (var index = 1; index < projections.Length; index++)
        {
            var projection = projections[index];
            _code.Store64(Spill(spill));
            EmitNativeArrayElementAddressFromBase(Spill(spill),
                projection.Index, spill + 1, projection.Layout.ElementCount,
                projection.Layout.ElementStride,
                index + 1 == projections.Length
                    ? requiredElementCount : 1);
        }
    }

    private void EmitNativeArrayElementOffset(BoundExpression index,
        int indexSpill, ulong elementCount, ulong elementStride,
        ulong requiredElementCount = 1)
    {
        var validIndexCount = requiredElementCount is > 0
            && requiredElementCount <= elementCount
                ? elementCount - requiredElementCount + 1
                : 0;
        Expression(index, indexSpill);
        if (index.Type is PrimitiveType.Int128 or PrimitiveType.UInt128)
        {
            // Wide scalar expressions are address-backed; load high then low limbs for the checks below.
            _code.Bytes(0x48, 0x8B, 0x50, 0x08);
            _code.Bytes(0x48, 0x8B, 0x00);
        }

        switch (index.Type)
        {
            case PrimitiveType.Int8:
            case PrimitiveType.Int16:
            case PrimitiveType.Int32:
                _code.Bytes(0x85, 0xC0);
                RuntimeBranch(0x88, 3);
                goto case PrimitiveType.UInt32;
            case PrimitiveType.UInt8:
            case PrimitiveType.UInt16:
            case PrimitiveType.UInt32:
                if (validIndexCount <= uint.MaxValue)
                {
                    _code.Byte(0x3D);
                    _code.Int32((int)validIndexCount);
                    RuntimeBranch(0x83, 3);
                }
                break;
            case PrimitiveType.Int64:
                _code.Bytes(0x48, 0x85, 0xC0);
                RuntimeBranch(0x88, 3);
                goto case PrimitiveType.UInt64;
            case PrimitiveType.UInt128:
            case PrimitiveType.Int128:
                _code.Bytes(0x48, 0x85, 0xD2);
                RuntimeBranch(0x85, 3);
                goto case PrimitiveType.UInt64;
            case PrimitiveType.UInt64:
                _code.Bytes(0x48, 0xBA);
                _code.Int64(unchecked((long)validIndexCount));
                _code.Bytes(0x48, 0x39, 0xD0);
                RuntimeBranch(0x83, 3);
                break;
            default:
                throw new InvalidOperationException(
                    "A fixed-array index has an unsupported integer type.");
        }

        if (elementStride == 0)
        {
            _code.Bytes(0x31, 0xC0);
        }
        else if (elementStride <= int.MaxValue)
        {
            _code.Bytes(0x48, 0x69, 0xC0);
            _code.Int32((int)elementStride);
        }
        else
        {
            _code.Bytes(0x48, 0xBA);
            _code.Int64(unchecked((long)elementStride));
            _code.Bytes(0x48, 0x0F, 0xAF, 0xC2);
        }
    }

    private void EmitNativeStorageAddress(BoundNativeStorageAddress address, int spill)
    {
        if (address.Type != address.AddressType
            || address.ElementCount == 0 || address.ElementStride == 0
            || address.ByteExtent == 0
            || address.ByteExtent % address.ElementStride != 0)
        {
            throw new InvalidOperationException("The native storage address has an invalid extent.");
        }

        var completeExtent = checked(address.ElementCount * address.ElementStride);
        var requiredElementCount = address.ByteExtent / address.ElementStride;
        if (address.ByteExtent > completeExtent
            || requiredElementCount == 0
            || requiredElementCount > address.ElementCount)
        {
            throw new InvalidOperationException("The native storage address exceeds its carrier.");
        }
        if (address.FieldPlace is not null)
        {
            if (address.Projections.IsDefaultOrEmpty)
            {
                EmitNativeFixedArrayElementAddress(address.Variable,
                    address.FieldPlace, address.Index, spill,
                    address.ElementCount, address.ElementStride,
                    requiredElementCount);
            }
            else
            {
                if (address.Index != address.Projections[^1].Index)
                {
                    throw new InvalidOperationException(
                        "The native storage address has an invalid field projection.");
                }
                EmitNativeFixedArrayPathAddress(address.Variable,
                    address.Projections, spill, requiredElementCount,
                    address.FieldPlace);
            }
        }
        else if (address.Projections.IsDefaultOrEmpty
            && NativeFixedArrayContract.IsConstantIndex(address.Index, 0))
        {
            _code.Address(Slot(address.Variable));
        }
        else if (!address.Projections.IsDefaultOrEmpty)
        {
            if (address.Projections.Length < 2
                || address.Index != address.Projections[^1].Index)
            {
                throw new InvalidOperationException(
                    "The native storage address has an invalid nested path.");
            }

            EmitNativeFixedArrayPathAddress(address.Variable,
                address.Projections, spill, requiredElementCount);
        }
        else
        {
            EmitNativeArrayElementAddress(address.Variable, address.Index, spill,
                address.ElementCount, address.ElementStride,
                requiredElementCount);
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

        var supportSlot = _normalBacking ? Spill(spill) + sizeof(ulong) : -1;
        if (_normalBacking)
        {
            _code.Bytes(0x31, 0xC0);
            _code.Store64(supportSlot);
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
            EmitCheckedReferencePlaceAddress(formation.Place, spill, supportSlot);
        }
        if (_normalBacking)
        {
            _code.Store64(Spill(spill));
            _code.Address(Spill(spill));
        }
    }

    private void EmitCheckedReferencePlaceAddress(
        BoundExpression place, int spill, int supportSlot = -1)
    {
        switch (place)
        {
            case BoundRead read:
                var variable = _program.Functions[_currentFunction].Variables[read.Variable];
                if (read.Variable == 0 && IsBorrowedReceiver())
                {
                    _code.Load64(Slot(0));
                }
                else if (variable.IsImplicitRef)
                {
                    _code.Load64(Slot(read.Variable));
                }
                else
                {
                    _code.Address(variable.IsOut ? OutSlot(read.Variable) : Slot(read.Variable));
                }
                break;
            case BoundReferenceRead referenceRead:
                if (!CheckedReferenceTypeContract.IsValidType(
                        referenceRead.Descriptor.Type, _program.CheckedReferenceTypes)
                    || Size(referenceRead.Type) == 0)
                {
                    throw new InvalidOperationException(
                        "The checked-reference place has an invalid descriptor type.");
                }
                var referenceType = _program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(referenceRead.Descriptor.Type)];
                if (referenceType.IsGarbageCollected
                    || referenceType.IsInitialization || referenceType.IsOptional
                    || referenceType.ReferentType != referenceRead.Type)
                {
                    throw new InvalidOperationException(
                        "The checked-reference place does not match its referent.");
                }
                Expression(referenceRead.Descriptor, spill);
                if (_normalBacking)
                {
                    if (supportSlot >= 0)
                    {
                        var temporary = _normalBackingTemporarySlots[_currentFunction];
                        if (temporary < 0)
                        {
                            throw new InvalidOperationException(
                                "A normal-backed reborrow has no verified temporary.");
                        }
                        _code.Store64(temporary);
                        _code.Load64(temporary);
                        _code.LoadPointerField(sizeof(ulong), sizeof(ulong));
                        _code.Store64(supportSlot);
                        _code.Load64(temporary);
                    }
                    _code.LoadPointerField(0, sizeof(ulong));
                }
                break;
            case BoundNativeFixedArrayElement element:
                if ((uint)element.Variable
                    >= (uint)_program.Functions[_currentFunction].Variables.Length
                    || (element.FieldPlace is null
                        && _program.Functions[_currentFunction].Variables[element.Variable].Type
                            != element.Layout.CarrierType)
                    || (element.FieldPlace is not null
                        && element.FieldPlace.Type
                            != element.Layout.CarrierType)
                    || !NativeFixedArrayContract.MatchesType(
                        element.Layout.CarrierType,
                        element.Layout, _program)
                    || !NativeFixedArrayContract.MatchesLayout(element.Layout, _program)
                    || element.Type != element.Layout.ElementType)
                {
                    throw new InvalidOperationException(
                        "The checked-reference array element place is invalid.");
                }
                EmitNativeFixedArrayElementAddress(element.Variable,
                    element.FieldPlace, element.Index, spill,
                    element.Layout.ElementCount,
                    element.Layout.ElementStride);
                break;
            case BoundFieldRead field:
                if (ManagedClassTypes.IsClass(field.Receiver.Type))
                {
                    var classIndex = ManagedClassTypes.ClassIndex(field.Receiver.Type);
                    if (!_normalBacking || supportSlot < 0
                        || (uint)classIndex >= (uint)_program.Classes.Length
                        || (uint)field.Field >=
                            (uint)_program.Classes[classIndex].Fields.Length)
                    {
                        throw new InvalidOperationException(
                            "A checked reference to this normal-backed class field is not supported.");
                    }
                    var @class = _program.Classes[classIndex];
                    var classField = @class.Fields[field.Field];
                    var fieldSize = Size(field.Type);
                    if (classField.Type != field.Type || fieldSize <= 0
                        || classField.Size != fieldSize || classField.Offset < 0
                        || classField.Offset > @class.Size - fieldSize
                        || !CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                            field.Type, _program.Structures, _program.Classes,
                            _program.EnumDefinitions.Length, _program.CheckedReferenceTypes))
                    {
                        throw new InvalidOperationException(
                            "The checked-reference class field layout is invalid.");
                    }
                    Expression(field.Receiver, spill);
                    _code.Store64(Spill(spill));
                    EmitNormalBackingValidateHandle(Spill(spill), classIndex);
                    _code.Load64(Spill(spill));
                    _code.Store64(supportSlot);
                    _code.Load64(Spill(spill));
                    _code.PointerOffset(32 + classField.Offset);
                    break;
                }
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
                    EmitUniqueFieldPlace(field);
                    break;
                }
                if (_program.Structures[receiverIndex].CleanupMethod is not null)
                {
                    throw new InvalidOperationException(
                        "A checked reference to this cleanup-bearing field place is not supported.");
                }
                EmitCheckedReferencePlaceAddress(field.Receiver, spill, supportSlot);
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
        if (_normalBacking)
        {
            _code.LoadPointerField(0, sizeof(ulong));
        }
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

        if (_normalBacking && IsNormalBackingHandle(write.Value.Type))
        {
            EmitNormalBackingHandleReferenceWrite(write, spill);
            return;
        }

        if (IsUnique(write.Value.Type))
        {
            EmitCheckedReferenceOwnerWrite(write, spill);
            return;
        }

        EmitPointerValueWrite(write.Descriptor, write.Value, spill);
    }

    private void EmitNormalBackingHandleReferenceWrite(
        BoundReferenceWrite write, int spill)
    {
        var variables = _program.Functions[_currentFunction].Variables;
        if (write.Descriptor is not BoundRead descriptor
            || (uint)descriptor.Variable >= (uint)variables.Length
            || variables[descriptor.Variable].Type != descriptor.Type
            || !IsNormalBackingLocalReference(descriptor.Type, _program))
        {
            throw new NotSupportedException(
                "The selected normal-backing runtime only supports handle-slot writes through local reference descriptors.");
        }

        var maximumArguments = 0;
        var descriptorSpills = ExpressionSpills(
            write.Descriptor, ref maximumArguments);
        _ = ExpressionSpills(write.Value, ref maximumArguments);
        var descriptorBase = spill + 4;
        var descriptorSlot = Spill(descriptorBase + descriptorSpills);
        var valueBase = descriptorBase + descriptorSpills + 1;
        var addressSlot = Spill(spill);
        var supportSlot = Spill(spill + 1);
        var newHandleSlot = Spill(spill + 2);
        var oldHandleSlot = Spill(spill + 3);

        Expression(write.Descriptor, descriptorBase);
        _code.Store64(descriptorSlot);
        _code.Load64(descriptorSlot);
        _code.LoadPointerField(0, sizeof(ulong));
        _code.Store64(addressSlot);
        _code.Load64(descriptorSlot);
        _code.LoadPointerField(sizeof(ulong), sizeof(ulong));
        _code.Store64(supportSlot);

        Expression(write.Value, valueBase);
        _code.Store64(newHandleSlot);
        EmitNormalBackingRetain(newHandleSlot);
        _code.Load64(addressSlot);
        _code.LoadPointerField(0, sizeof(ulong));
        _code.Store64(oldHandleSlot);

        _code.Load64(supportSlot);
        _code.Bytes(0x48, 0x85, 0xC0);
        var normalBackingSlot = _code.JumpIf(0x84);

        var completedStores = new List<int>();
        for (var classIndex = 0; classIndex < _program.Classes.Length; classIndex++)
        {
            var @class = _program.Classes[classIndex];
            _code.Load64(supportSlot);
            _code.LoadPointerField(24, sizeof(uint));
            _code.Byte(0x3D);
            _code.Int32(classIndex);
            var anotherClass = _code.JumpIf(0x85);
            EmitNormalBackingValidateHandle(supportSlot, classIndex);

            foreach (var (field, fieldIndex) in @class.Fields
                .Select(static (field, index) => (field, index)))
            {
                if (!field.IsMutable || field.Type != write.Value.Type
                    || !IsNormalBackingHandle(field.Type))
                {
                    continue;
                }
                if (field.Size != Size(field.Type) || field.Offset < 0
                    || field.Offset > @class.Size - field.Size)
                {
                    throw new InvalidOperationException(
                        "The normal-backed class handle field layout is invalid.");
                }

                _code.Load64(supportSlot);
                _code.PointerOffset(32 + field.Offset);
                _code.Load64(addressSlot, destinationRegister: true);
                _code.Bytes(0x48, 0x39, 0xD0);
                var anotherField = _code.JumpIf(0x85);
                EmitNormalBackingRecordFieldStore(
                    supportSlot, classIndex, fieldIndex, oldHandleSlot, newHandleSlot);
                _code.Load64(addressSlot, destinationRegister: true);
                _code.Load64(newHandleSlot);
                _code.Bytes(0x48, 0x89, 0x02);
                EmitNormalBackingRelease(oldHandleSlot);
                completedStores.Add(_code.Jump());
                _code.PatchRelative(anotherField, _code.Position);
            }

            EmitNormalBackingContractFailure();
            _code.PatchRelative(anotherClass, _code.Position);
        }
        EmitNormalBackingContractFailure();

        var localStore = _code.Position;
        _code.PatchRelative(normalBackingSlot, localStore);
        _code.Load64(addressSlot, destinationRegister: true);
        _code.Load64(newHandleSlot);
        _code.Bytes(0x48, 0x89, 0x02);
        EmitNormalBackingRelease(oldHandleSlot);
        var complete = _code.Position;
        foreach (var branch in completedStores)
        {
            _code.PatchRelative(branch, complete);
        }
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
            _program.EnumDefinitions.Length, _program.CheckedReferenceTypes);

    private void EmitPointerValueWrite(BoundExpression address, BoundExpression value, int spill)
    {
        Expression(address, spill + 1);
        if (_normalBacking && CheckedReferenceTypes.IsType(address.Type))
        {
            _code.LoadPointerField(0, sizeof(ulong));
        }
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
        EmitNativeFixedArrayElementAddress(element.Variable,
            element.FieldPlace, element.Index, spill,
            element.Layout.ElementCount, element.Layout.ElementStride);
        if (Size(element.Type) == 0)
        {
            _code.Bytes(0x31, 0xC0);
            return;
        }
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

    private void EmitNativeFixedArrayPathElement(
        BoundNativeFixedArrayPathElement element, int spill)
    {
        EmitNativeFixedArrayPathAddress(element.Variable,
            element.Projections, spill, fieldPlace: element.FieldPlace);
        if (Size(element.Type) == 0)
        {
            _code.Bytes(0x31, 0xC0);
            return;
        }
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
                throw new InvalidOperationException(
                    "A nested fixed-array element has an unsupported native width.");
        }
    }

    private void EmitNativeFixedArrayPointerAddress(
        BoundNativeFixedArrayPointerAddress address, int spill)
    {
        if (address.Projections.IsDefaultOrEmpty)
        {
            EmitNativeFixedArrayElementAddress(address.Variable,
                address.FieldPlace, address.Index, spill,
                address.Layout.ElementCount,
                address.Layout.ElementStride);
        }
        else
        {
            EmitNativeFixedArrayPathAddress(address.Variable,
                address.Projections, spill,
                fieldPlace: address.FieldPlace);
        }
    }

    private void EmitNativeFixedArrayWrite(BoundNativeFixedArrayWrite write, int spill)
    {
        EmitNativeFixedArrayElementAddress(write.Variable,
            write.FieldPlace, write.Index, spill,
            write.Layout.ElementCount, write.Layout.ElementStride);
        _code.Store64(Spill(spill));
        if (write.Value is BoundNativeFixedArrayInitialization initialization)
        {
            EmitNativeFixedArrayInitializationAtAddress(initialization,
                Spill(spill), spill + 1);
            return;
        }
        if (write.Value is BoundNativeFixedArrayRepeatInitialization repeat)
        {
            EmitNativeFixedArrayRepeatInitializationAtAddress(repeat,
                Spill(spill), spill + 1);
            return;
        }
        Expression(write.Value, spill + 1);
        if (Size(write.Value.Type) == 0)
        {
            return;
        }
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

    private void EmitNativeFixedArrayPathWrite(
        BoundNativeFixedArrayPathWrite write, int spill)
    {
        EmitNativeFixedArrayPathAddress(write.Variable, write.Projections,
            spill, fieldPlace: write.FieldPlace);
        var targetType = write.Projections[^1].Layout.ElementType;
        var fieldOffset = 0;
        foreach (var projection in write.LeafFields)
        {
            var ownerIndex = (int)targetType - 100;
            if (projection.OwnerType != targetType
                || (uint)ownerIndex >= (uint)_program.Structures.Length
                || (uint)projection.Field
                    >= (uint)_program.Structures[ownerIndex].Fields.Length)
            {
                throw new InvalidOperationException(
                    "A fixed-array field write has an invalid field path.");
            }

            var field = _program.Structures[ownerIndex].Fields[
                projection.Field];
            if (field.Offset < 0)
            {
                throw new InvalidOperationException(
                    "A fixed-array field write has an invalid field offset.");
            }
            fieldOffset = checked(fieldOffset + field.Offset);
            targetType = field.Type;
        }
        if (targetType != write.Value.Type)
        {
            throw new InvalidOperationException(
                "A fixed-array field write has a mismatched value type.");
        }
        if (fieldOffset != 0)
        {
            _code.PointerOffset(fieldOffset);
        }
        _code.Store64(Spill(spill));
        if (write.Value is BoundNativeFixedArrayInitialization initialization)
        {
            EmitNativeFixedArrayInitializationAtAddress(initialization,
                Spill(spill), spill + 1);
            return;
        }
        if (write.Value is BoundNativeFixedArrayRepeatInitialization repeat)
        {
            EmitNativeFixedArrayRepeatInitializationAtAddress(repeat,
                Spill(spill), spill + 1);
            return;
        }

        Expression(write.Value, spill + 1);
        if (Size(write.Value.Type) == 0)
        {
            return;
        }
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
        _code.Load64(Spill(spill));
        switch (width)
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
                throw new InvalidOperationException(
                    "A nested fixed-array element has an unsupported native width.");
        }
    }

    private void EmitNativeFixedArrayRepeatInitialization(
        BoundNativeFixedArrayRepeatInitialization initialization,
        int destinationOffset, int scratchSpill)
    {
        ValidateNativeFixedArrayRepeatInitialization(initialization);
        var seedScratchOffset = Spill(scratchSpill);
        EmitNativeFixedArraySeedAtAddress(initialization.Seed,
            seedScratchOffset,
            scratchSpill + NativeFixedArrayScratchSlots(
                initialization.Layout.ElementType));
        EmitNativeFixedArrayRepeatCopy(initialization.Layout, seedScratchOffset,
            destinationOffset);
    }

    private void EmitNativeFixedArrayRepeatInitializationAtAddress(
        BoundNativeFixedArrayRepeatInitialization initialization,
        int destinationAddressSlot, int scratchSpill)
    {
        ValidateNativeFixedArrayRepeatInitialization(initialization);
        var seedScratchOffset = Spill(scratchSpill);
        EmitNativeFixedArraySeedAtAddress(initialization.Seed,
            seedScratchOffset,
            scratchSpill + NativeFixedArrayScratchSlots(
                initialization.Layout.ElementType));
        EmitNativeFixedArrayRepeatCopy(initialization.Layout,
            seedScratchOffset, destinationAddressSlot,
            destinationIsAddress: true);
    }

    private void EmitNativeFixedArraySeedAtAddress(BoundExpression seed,
        int destinationOffset, int spill)
    {
        switch (seed)
        {
            case BoundNativeFixedArrayInitialization initialization:
                EmitNativeFixedArrayInitialization(initialization,
                    destinationOffset, spill);
                return;
            case BoundNativeFixedArrayRepeatInitialization repeat:
                EmitNativeFixedArrayRepeatInitialization(repeat,
                    destinationOffset, spill);
                return;
        }

        Expression(seed, spill);
        if (Size(seed.Type) == 0)
        {
            return;
        }
        if (IsIndirect(seed.Type))
        {
            _code.AddressToRdx(destinationOffset);
            _code.CopyMemory(Size(seed.Type));
        }
        else
        {
            StoreValue(destinationOffset, seed.Type);
        }
    }

    private void EmitNativeFixedArrayRepeatCopy(
        BoundNativeFixedArrayLayout layout, int seedOffset,
        int destinationOffset, bool destinationIsAddress = false)
    {
        var elementStride = checked((int)layout.ElementStride);
        if (layout.ElementCount == 0 || elementStride == 0)
        {
            return;
        }

        _code.Address(seedOffset);
        _code.Bytes(0x49, 0x89, 0xC3); // r11 = seed address
        if (destinationIsAddress)
        {
            _code.Load64(destinationOffset);
        }
        else
        {
            _code.Address(destinationOffset);
        }
        _code.Bytes(0x49, 0x89, 0xC1); // r9 = destination address
        _code.Bytes(0x49, 0xBA);
        _code.Int64(unchecked((long)layout.ElementCount));
        var loop = _code.Position;
        _code.Bytes(0x4D, 0x85, 0xD2); // test r10,r10
        var done = _code.JumpIf(0x84);
        _code.Bytes(0x4C, 0x89, 0xD8); // rax = r11
        _code.Bytes(0x4C, 0x89, 0xCA); // rdx = r9
        _code.CopyMemory(elementStride);
        _code.Bytes(0x49, 0x81, 0xC1); // add r9, element stride
        _code.Int32(elementStride);
        _code.Bytes(0x49, 0xFF, 0xCA); // dec r10
        _code.PatchRelative(_code.Jump(), loop);
        _code.PatchRelative(done, _code.Position);
    }

    private void ValidateNativeFixedArrayRepeatInitialization(
        BoundNativeFixedArrayRepeatInitialization initialization)
    {
        if (initialization.Type != initialization.Layout.CarrierType
            || initialization.Seed is null
            || initialization.Seed.Type
                != initialization.Layout.ElementType
            || initialization.Layout.ElementStride
                != (ulong)Size(initialization.Layout.ElementType))
        {
            throw new InvalidOperationException(
                "A repeated fixed-array seed has an invalid target layout.");
        }
    }

    private int NativeFixedArrayScratchSlots(PrimitiveType type)
        => Math.Max(1, checked((int)(((long)Size(type) + 7) / 8)));

    private void EmitNativeFixedArrayInitializationAtAddress(
        BoundNativeFixedArrayInitialization initialization,
        int destinationAddressSlot, int scratchSpill)
    {
        var stride = initialization.Layout.ElementStride;
        for (var index = 0; index < initialization.Elements.Length; index++)
        {
            var element = initialization.Elements[index];
            var elementOffset = checked((int)checked((ulong)index * stride));
            _code.Load64(destinationAddressSlot);
            if (elementOffset != 0)
            {
                _code.PointerOffset(elementOffset);
            }
            _code.Store64(Spill(scratchSpill));
            if (element is BoundNativeFixedArrayInitialization nested)
            {
                EmitNativeFixedArrayInitializationAtAddress(nested,
                    Spill(scratchSpill), scratchSpill + 1);
                continue;
            }
            if (element
                is BoundNativeFixedArrayRepeatInitialization repeat)
            {
                EmitNativeFixedArrayRepeatInitializationAtAddress(repeat,
                    Spill(scratchSpill), scratchSpill + 1);
                continue;
            }

            Expression(element, scratchSpill + 1);
            if (stride == 0)
            {
                continue;
            }
            if (IsIndirect(element.Type))
            {
                _code.Load64(Spill(scratchSpill),
                    destinationRegister: true);
                _code.CopyMemory(Size(element.Type));
                continue;
            }

            var width = Size(element.Type);
            if (width == sizeof(ulong))
            {
                _code.Bytes(0x48, 0x89, 0xC1);
            }
            else
            {
                _code.Bytes(0x89, 0xC1);
            }
            _code.Load64(Spill(scratchSpill));
            switch (width)
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
                    throw new InvalidOperationException(
                        "A fixed-array element has an unsupported native width.");
            }
        }
    }

    private void EmitNativeFixedArrayInitialization(
        BoundNativeFixedArrayInitialization initialization, int destinationOffset,
        int spill)
    {
        var stride = initialization.Layout.ElementStride;
        for (var index = 0; index < initialization.Elements.Length; index++)
        {
            var element = initialization.Elements[index];
            var elementDestination = checked(destinationOffset
                + (int)checked((ulong)index * stride));
            if (element is BoundNativeFixedArrayInitialization nested)
            {
                EmitNativeFixedArrayInitialization(nested, elementDestination,
                    spill);
                continue;
            }
            if (element
                is BoundNativeFixedArrayRepeatInitialization repeat)
            {
                EmitNativeFixedArrayRepeatInitialization(repeat,
                    elementDestination, spill);
                continue;
            }

            Expression(element, spill);
            if (stride == 0)
            {
                continue;
            }
            if (IsIndirect(element.Type))
            {
                CopyToFrame(elementDestination, checked((int)stride));
            }
            else if (element.Type is PrimitiveType.Bool or PrimitiveType.Char16)
            {
                _code.StoreField(elementDestination, Size(element.Type));
            }
            else
            {
                StoreValue(elementDestination, element.Type);
            }
        }
    }
}
