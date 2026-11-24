// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class ForeignNativeInterfaceContract
{
    internal static bool IsValidTable(BoundProgram program) =>
        IsValidTable(program.Interfaces, program.Structures,
            program.NativePointerTypes,
            program.NativeFunctionPointerSignatures,
            program.NativeCoreRegistration, program.EnumDefinitions);

    internal static bool IsValidTable(
        ImmutableArray<BoundInterfaceDefinition> interfaces,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        NativeCoreRegistrationSet? nativeCoreRegistration,
        ImmutableArray<BoundEnumDefinition> enumDefinitions)
    {
        if (interfaces.IsDefault || structures.IsDefault
            || pointerTypes.IsDefault
            || functionPointerSignatures.IsDefault)
        {
            return false;
        }

        var hasForeignNativeInterfaces = false;
        foreach (var definition in interfaces)
        {
            if (definition is null)
            {
                return false;
            }
            if (definition.ForeignNativeInterface is null)
            {
                continue;
            }

            hasForeignNativeInterfaces = true;
            if (definition.Members.IsDefaultOrEmpty
                || definition.Members.Any(member => member is null
                    || !IsValidMethodSignature(member,
                        structures, pointerTypes,
                        functionPointerSignatures, enumDefinitions)))
            {
                return false;
            }
        }

        return !hasForeignNativeInterfaces
            || nativeCoreRegistration?.ForeignNativeInterface is not null
            || interfaces.Where(static definition =>
                    definition.ForeignNativeInterface is not null)
                .All(static definition => definition.ModuleImport is not null
                    || definition.PrivateForeignInterfaceOrigin is not null);
    }

    internal static bool IsValidMethodSignature(
        BoundInterfaceMemberDefinition member,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<BoundEnumDefinition> enumDefinitions)
    {
        if (member.NativeCallableConvention is not (
                NativeCallingConvention.Unmanaged
                or NativeCallingConvention.Cdecl
                or NativeCallingConvention.Stdcall)
            || member.ResultType is not BoundInterfaceType resultType
            || !TryGetNativeValueType(resultType, pointerTypes,
                functionPointerSignatures, enumDefinitions,
                allowVoid: true,
                out _)
            || member.Parameters.IsDefault
            || !TryGetReceiverType(member, pointerTypes,
                out var receiverType)
            || !DirectNativeImportContract.IsValueParameter(receiverType,
                functionPointerSignatures, pointerTypes))
        {
            return false;
        }

        if (!HasValidEnumClosure(member, structures, pointerTypes,
                functionPointerSignatures, enumDefinitions))
        {
            return false;
        }

        foreach (var parameter in member.Parameters)
        {
            if (parameter is null
                || parameter.Mode != BoundInterfaceParameterMode.Value
                || !TryGetNativeValueType(parameter.Type, pointerTypes,
                    functionPointerSignatures, enumDefinitions,
                    allowVoid: false, out _))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidCall(BoundCall call, BoundFunction caller,
        BoundProgram program)
    {
        var foreignCall = call.ForeignInterfaceCall;
        if (foreignCall?.Receiver is null)
        {
            return false;
        }

        var receiver = foreignCall.Receiver;
        while (receiver is BoundUnsafeExpression unsafeExpression)
        {
            receiver = unsafeExpression.Expression;
        }

        if (DirectNativeImportContract.IsNullNativePointerValue(receiver)
            || call.Function != BoundCall.ForeignInterfaceFunction
            || call.InterfaceCallIdentity is not null
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || !call.ImportedGenericMethodTypeArguments.IsDefaultOrEmpty
            || !call.ArgumentImplicitReferenceTemporaries.IsDefaultOrEmpty
            || !call.DefaultedArguments.IsDefaultOrEmpty
            || !call.IsUnsafeAuthorized
            || call.Arguments.IsDefault
            || call.Arguments.Length
                >= SemanticVerifier.MaximumArguments
            || (!call.ArgumentParameterIndices.IsDefault
                && call.ArgumentParameterIndices.Length
                    != call.Arguments.Length)
            || foreignCall.NativeOutputContracts.IsDefault
            || foreignCall.Member is null
            || foreignCall.Member.DeclaringInterface is null
            || foreignCall.Member.AccessorOrdinal != -1
            || !BoundInterfaceContract.TryGetInterfaceMemberDefinition(
                foreignCall.Member, program.Interfaces, static _ => true,
                out var member,
                genericNominalTypeDefinitions:
                    program.GenericNominalTypeDefinitions,
                sourceGenericTypeDefinitions:
                    program.SourceGenericTypeDefinitions)
            || member is null
            || member.Kind != BoundInterfaceMemberKind.Callable
            || member.Accessibility != BoundInterfaceAccessibility.Public
            || member.IsStatic || member.IsExternal || member.IsPartial
            || member.IsAsync || member.HasBody
            || !member.TypeParameters.IsEmpty
            || !member.Constraints.IsEmpty
            || !member.DeclaredRequiresUnsafeCaller
            || !member.RequiresUnsafeCaller
            || member.NativeCallableConvention
                != foreignCall.CallingConvention
            || member.RequiresMutableReceiver
                != foreignCall.RequiresMutableReceiver
            || member.DoesNotReturn != foreignCall.DoesNotReturn
            || member.ResultType is not BoundInterfaceType {
                Kind: BoundInterfaceTypeKind.Primitive,
            } resultType
            || resultType.PrimitiveType != call.Type
            || member.Parameters.IsDefault
            || call.Arguments.Length != member.Parameters.Length
            || !AreNativeOutputContractsEqual(
                member.NativeOutputContracts,
                foreignCall.NativeOutputContracts)
            || !IsValidMethodSignature(member, program.NativePointerTypes,
                program.NativeFunctionPointerSignatures)
            || !TryGetReceiverType(member, program.NativePointerTypes,
                out var receiverType)
            || !IsNativeValueTypeCompatible(foreignCall.Receiver.Type,
                receiverType, program.NativePointerTypes, out _))
        {
            return false;
        }

        var owners = program.Interfaces.Where(definition =>
                !definition.Members.IsDefault
                && definition.Members.Any(candidate =>
                    ReferenceEquals(candidate, member)))
            .ToArray();
        if (owners is not [{ } owner]
            || owner.ForeignNativeInterface is not
                BoundForeignNativeInterfaceLayout layout
            || layout.VTableByteOffset != foreignCall.VTableByteOffset
            || (program.NativeCoreRegistration?.ForeignNativeInterface
                    is null
                && owner.ModuleImport is null
                && owner.PrivateForeignInterfaceOrigin is null))
        {
            return false;
        }

        var mappedParameters = new bool[member.Parameters.Length];
        var outputTargets = new HashSet<int>();
        foreach (var (argument, argumentIndex) in call.Arguments
                     .Select(static (argument, argumentIndex) =>
                         (argument, argumentIndex)))
        {
            var parameterIndex = call.ArgumentParameterIndices.IsDefault
                ? argumentIndex
                : (uint)argumentIndex
                    < (uint)call.ArgumentParameterIndices.Length
                        ? call.ArgumentParameterIndices[argumentIndex]
                        : -1;
            if ((uint)parameterIndex >= (uint)mappedParameters.Length
                || mappedParameters[parameterIndex]
                || member.Parameters[parameterIndex].Type
                    is not BoundInterfaceType {
                        Kind: BoundInterfaceTypeKind.Primitive,
                    } parameterType)
            {
                return false;
            }

            mappedParameters[parameterIndex] = true;
            var output = member.NativeOutputContracts.FirstOrDefault(
                candidate => candidate.FormalOrdinal == parameterIndex);
            if (output is not null)
            {
                if (argument is not BoundNativeOutputReservation reservation
                    || reservation.Type != PrimitiveType.Void
                    || reservation.PointeeType != output.PointeeType
                    || (uint)reservation.Variable
                        >= (uint)caller.Variables.Length
                    || caller.Variables[reservation.Variable].Type
                        != output.PointeeType
                    || !caller.Variables[reservation.Variable]
                        .IsInitializationLocal
                    || !outputTargets.Add(reservation.Variable))
                {
                    return false;
                }
            }
            else if (argument is BoundNativeOutputReservation
                || !IsNativeValueTypeCompatible(argument.Type,
                    parameterType.PrimitiveType,
                    program.NativePointerTypes, out _))
            {
                return false;
            }
        }

        return mappedParameters.All(static mapped => mapped);
    }

    internal static bool IsNativeValueTypeCompatible(
        PrimitiveType sourceType, PrimitiveType targetType,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        out bool accessAttenuation)
    {
        accessAttenuation = false;
        if (sourceType == targetType)
        {
            return true;
        }

        if (sourceType == PrimitiveType.NativeRawPointer
            && NativePointerTypeContract.IsValidType(targetType,
                pointerTypes))
        {
            var targetPointer = pointerTypes[
                NativePointerTypes.Index(targetType)];
            return targetPointer.PointeeType == PrimitiveType.Void
                && !targetPointer.IsWritable;
        }

        if (!NativePointerTypeContract.IsValidType(sourceType,
                pointerTypes))
        {
            return false;
        }

        var sourcePointer = pointerTypes[NativePointerTypes.Index(sourceType)];
        if (targetType == PrimitiveType.NativeRawPointer)
        {
            if (sourcePointer.PointeeType != PrimitiveType.Void)
            {
                return false;
            }
            accessAttenuation = sourcePointer.IsWritable;
            return true;
        }
        if (!NativePointerTypeContract.IsValidType(targetType,
                pointerTypes))
        {
            return false;
        }

        var targetDefinition = pointerTypes[
            NativePointerTypes.Index(targetType)];
        accessAttenuation = sourcePointer.PointeeType
                == targetDefinition.PointeeType
            && sourcePointer.IsWritable && !targetDefinition.IsWritable;
        return accessAttenuation;
    }

    internal static bool TryCreateCallTarget(
        BoundForeignInterfaceCall foreignCall, BoundProgram program,
        out BoundFunction target)
    {
        target = null!;
        if (foreignCall.Member is null)
        {
            return false;
        }
        if (foreignCall.Member.DeclaringInterface is null)
        {
            return false;
        }

        var owners = program.Interfaces.Where(definition =>
                StringComparer.Ordinal.Equals(definition.QualifiedName,
                    foreignCall.Member.DeclaringInterface.NominalName))
            .ToArray();
        if (foreignCall.Member.AccessorOrdinal != -1
            || owners is not [{ } owner]
            || owner.ForeignNativeInterface is not
                BoundForeignNativeInterfaceLayout layout
            || layout.VTableByteOffset != foreignCall.VTableByteOffset
            || (program.NativeCoreRegistration?.ForeignNativeInterface
                    is null
                && owner.ModuleImport is null)
            || !BoundInterfaceContract.TryGetInterfaceMemberDefinition(
                foreignCall.Member, program.Interfaces, static _ => true,
                out var member,
                genericNominalTypeDefinitions:
                    program.GenericNominalTypeDefinitions,
                sourceGenericTypeDefinitions:
                    program.SourceGenericTypeDefinitions)
            || member is null
            || member.Kind != BoundInterfaceMemberKind.Callable
            || member.Accessibility != BoundInterfaceAccessibility.Public
            || member.IsStatic || member.IsExternal || member.IsPartial
            || member.IsAsync || member.HasBody
            || !member.TypeParameters.IsEmpty
            || !member.Constraints.IsEmpty
            || !member.DeclaredRequiresUnsafeCaller
            || !member.RequiresUnsafeCaller
            || member.NativeCallableConvention
                != foreignCall.CallingConvention
            || member.RequiresMutableReceiver
                != foreignCall.RequiresMutableReceiver
            || member.DoesNotReturn != foreignCall.DoesNotReturn
            || member.ResultType is not BoundInterfaceType {
                Kind: BoundInterfaceTypeKind.Primitive,
            } resultType
            || member.Parameters.IsDefault
            || !AreNativeOutputContractsEqual(
                member.NativeOutputContracts,
                foreignCall.NativeOutputContracts)
            || !IsValidMethodSignature(member, program.Structures,
                program.NativePointerTypes,
                program.NativeFunctionPointerSignatures,
                program.EnumDefinitions))
        {
            return false;
        }

        var variables = ImmutableArray.CreateBuilder<BoundVariable>(
            member.Parameters.Length);
        foreach (var parameter in member.Parameters)
        {
            if (parameter is null
                || parameter.Type.Kind
                    != BoundInterfaceTypeKind.Primitive)
            {
                return false;
            }

            variables.Add(new BoundVariable(parameter.Name,
                parameter.Type.PrimitiveType, false,
                member.SourceDeclarationSpan));
        }

        // This temporary verifier view contains explicit formals only; the receiver is staged separately.
        target = new BoundFunction(member.Name,
            resultType.PrimitiveType, variables.MoveToImmutable(),
            member.Parameters.Length, new BoundBlock([], default), default) {
            NativeOutputContracts = member.NativeOutputContracts,
            NativeOutputRole = member.NativeOutputContracts.IsEmpty
                ? null : NativeOutputDeclarationRole.ForeignInterfaceMethod,
            NativeCallableConvention = member.NativeCallableConvention,
            RequiresUnsafeCaller = member.RequiresUnsafeCaller,
            DeclaredRequiresUnsafeCaller =
                member.DeclaredRequiresUnsafeCaller,
            DoesNotReturn = member.DoesNotReturn,
            IsExternal = true,
            IsStatic = true,
            SourceInputOrdinal = member.SourceInputOrdinal,
            SourceDeclarationInputOrdinal = member.SourceInputOrdinal,
            SourceDeclarationSpan = member.SourceDeclarationSpan,
        };
        return true;
    }

    internal static bool TryGetReceiverType(
        BoundInterfaceMemberDefinition member,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        out PrimitiveType receiverType)
    {
        if (!member.RequiresMutableReceiver)
        {
            receiverType = PrimitiveType.NativeRawPointer;
            return true;
        }

        for (var index = 0; index < pointerTypes.Length; index++)
        {
            if (pointerTypes[index].PointeeType == PrimitiveType.Void
                && pointerTypes[index].IsWritable
                && NativePointerTypeContract.IsValidType(
                    NativePointerTypes.Type(index), pointerTypes))
            {
                receiverType = NativePointerTypes.Type(index);
                return true;
            }
        }

        receiverType = default;
        return false;
    }

    private static bool TryGetNativeValueType(
        BoundInterfaceType type,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<BoundEnumDefinition> enumDefinitions,
        bool allowVoid,
        out PrimitiveType nativeType)
    {
        nativeType = default;
        if (type.Kind != BoundInterfaceTypeKind.Primitive
            || type.IsOptional || type.IsGarbageCollected
            || type.IsInitialization
            || (type.PrimitiveType == PrimitiveType.Void
                && (!allowVoid || type.IsWritable)))
        {
            return false;
        }

        if (type.PrimitiveType == PrimitiveType.Void)
        {
            nativeType = PrimitiveType.Void;
            return true;
        }

        if (BoundEnumTypes.IsType(type.PrimitiveType))
        {
            var enumIndex = BoundEnumTypes.Index(type.PrimitiveType);
            if (!BoundEnumTypes.IsValidType(type.PrimitiveType,
                    enumDefinitions)
                || enumDefinitions[enumIndex]
                    is not BoundEnumDefinition definition)
            {
                return false;
            }

            if (definition.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || definition.UnderlyingType is not (
                    PrimitiveType.Int8 or PrimitiveType.Int16
                    or PrimitiveType.Int32 or PrimitiveType.Int64
                    or PrimitiveType.UInt8 or PrimitiveType.UInt16
                    or PrimitiveType.UInt32 or PrimitiveType.UInt64
                    or PrimitiveType.NativeInt or PrimitiveType.NativeUInt))
            {
                return false;
            }
        }

        if (!DirectNativeImportContract.IsValueParameter(type.PrimitiveType,
                functionPointerSignatures, pointerTypes))
        {
            return false;
        }

        if (NativePointerTypes.IsType(type.PrimitiveType))
        {
            var pointer = pointerTypes[NativePointerTypes.Index(
                type.PrimitiveType)];
            if (type.IsWritable != pointer.IsWritable)
            {
                return false;
            }
        }
        else if (type.IsWritable)
        {
            return false;
        }

        nativeType = type.PrimitiveType;
        return true;
    }

    private static bool HasValidEnumClosure(
        BoundInterfaceMemberDefinition member,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<BoundEnumDefinition> enumDefinitions)
    {
        if (structures.IsDefault || pointerTypes.IsDefault
            || functionPointerSignatures.IsDefault)
        {
            return false;
        }

        var structureStates = new byte[structures.Length];
        var pointerStates = new byte[pointerTypes.Length];
        var functionPointerStates
            = new byte[functionPointerSignatures.Length];

        bool Visit(PrimitiveType current, bool throughPointer, int depth)
        {
            if (depth > SemanticVerifier.MaximumDepth)
            {
                return false;
            }

            if (NativePointerTypes.IsType(current))
            {
                var index = NativePointerTypes.Index(current);
                if ((uint)index >= (uint)pointerTypes.Length
                    || pointerTypes[index] is not NativePointerTypeDefinition
                        pointer)
                {
                    return false;
                }
                if (pointerStates[index] == 2)
                {
                    return true;
                }
                if (pointerStates[index] == 1
                    || !NativePointerTypeContract.IsValidType(
                        current, pointerTypes))
                {
                    return false;
                }

                pointerStates[index] = 1;
                var isValid = Visit(pointer.PointeeType, true, depth + 1);
                pointerStates[index] = 2;
                return isValid;
            }

            if (NativeFunctionPointerTypes.IsType(current))
            {
                var index = NativeFunctionPointerTypes.Index(current);
                if ((uint)index >= (uint)functionPointerSignatures.Length
                    || functionPointerSignatures[index]
                        is not NativeFunctionPointerSignatureDefinition
                            signature)
                {
                    return false;
                }
                if (functionPointerStates[index] == 2)
                {
                    return true;
                }
                if (functionPointerStates[index] == 1
                    || signature.ParameterTypes.IsDefault)
                {
                    return false;
                }

                functionPointerStates[index] = 1;
                if (!Visit(signature.ReturnType, true, depth + 1))
                {
                    return false;
                }
                foreach (var parameterType in signature.ParameterTypes)
                {
                    if (!Visit(parameterType, true, depth + 1))
                    {
                        return false;
                    }
                }
                functionPointerStates[index] = 2;
                return true;
            }

            if (BoundEnumTypes.IsType(current))
            {
                var index = BoundEnumTypes.Index(current);
                return BoundEnumTypes.IsValidType(current, enumDefinitions)
                    && enumDefinitions[index]
                        is BoundEnumDefinition definition
                    && definition.BlittableTargetRequirement
                        == FixedLayoutStorage.WindowsX64TargetRequirement;
            }

            var value = (int)current;
            if (value is < 100 || value >= BoundEnumTypes.First)
            {
                return true;
            }

            var structureIndex = value - 100;
            if ((uint)structureIndex >= (uint)structures.Length)
            {
                return false;
            }
            if (structureStates[structureIndex] == 2)
            {
                return true;
            }
            if (structureStates[structureIndex] == 1)
            {
                return throughPointer;
            }

            var structure = structures[structureIndex];
            if (structure is null
                || structure.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || structure.LayoutKind is not (1 or 2 or 3)
                || structure.IsUnique
                || structure.CleanupMethod is not null
                || structure.Fields.IsDefaultOrEmpty)
            {
                return false;
            }

            structureStates[structureIndex] = 1;
            foreach (var field in structure.Fields)
            {
                if (field is null
                    || NativeFixedArrayTypes.IsType(field.Type)
                    || !Visit(field.Type, false, depth + 1))
                {
                    return false;
                }
            }
            structureStates[structureIndex] = 2;
            return true;
        }

        if (member.ResultType is not BoundInterfaceType resultType
            || !Visit(resultType.PrimitiveType, false, 0))
        {
            return false;
        }

        foreach (var parameter in member.Parameters)
        {
            if (parameter is null
                || parameter.Type is not BoundInterfaceType parameterType
                || !Visit(parameterType.PrimitiveType, false, 0))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreNativeOutputContractsEqual(
        ImmutableArray<NativeOutputContract> left,
        ImmutableArray<NativeOutputContract> right)
    {
        if (left.IsDefault || right.IsDefault || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            var leftOutput = left[index];
            var rightOutput = right[index];
            if (leftOutput is null || rightOutput is null
                || leftOutput.FormalOrdinal != rightOutput.FormalOrdinal
                || leftOutput.PointeeType != rightOutput.PointeeType
                || leftOutput.Formation != rightOutput.Formation
                || leftOutput.PublicationPhase
                    != rightOutput.PublicationPhase
                || leftOutput.SuccessSelector != rightOutput.SuccessSelector
                || leftOutput.SuccessState != rightOutput.SuccessState
                || leftOutput.ComplementSelector
                    != rightOutput.ComplementSelector
                || leftOutput.ComplementState != rightOutput.ComplementState
                || leftOutput.Dependencies.IsDefault
                || rightOutput.Dependencies.IsDefault
                || !leftOutput.Dependencies.SequenceEqual(
                    rightOutput.Dependencies))
            {
                return false;
            }
        }

        return true;
    }
}
