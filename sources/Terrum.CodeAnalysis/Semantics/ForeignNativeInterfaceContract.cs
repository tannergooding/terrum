// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class ForeignNativeInterfaceContract
{
    internal static bool IsValidTable(BoundProgram program) =>
        IsValidTable(program.Interfaces, program.NativePointerTypes,
            program.NativeFunctionPointerSignatures,
            program.NativeCoreRegistration);

    internal static bool IsValidTable(
        ImmutableArray<BoundInterfaceDefinition> interfaces,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        NativeCoreRegistrationSet? nativeCoreRegistration)
    {
        if (interfaces.IsDefault || pointerTypes.IsDefault
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
                        pointerTypes, functionPointerSignatures)))
            {
                return false;
            }
        }

        return !hasForeignNativeInterfaces
            || nativeCoreRegistration?.ForeignNativeInterface is not null
            || interfaces.Where(static definition =>
                    definition.ForeignNativeInterface is not null)
                .All(static definition => definition.ModuleImport is not null);
    }

    internal static bool IsValidMethodSignature(
        BoundInterfaceMemberDefinition member,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures)
    {
        if (member.NativeCallableConvention is not (
                NativeCallingConvention.Unmanaged
                or NativeCallingConvention.Cdecl
                or NativeCallingConvention.Stdcall)
            || member.ResultType is not BoundInterfaceType resultType
            || !TryGetNativeValueType(resultType, pointerTypes,
                functionPointerSignatures, allowVoid: true,
                out _)
            || member.Parameters.IsDefault
            || !TryGetReceiverType(member, pointerTypes,
                out var receiverType)
            || !DirectNativeImportContract.IsValueParameter(receiverType,
                functionPointerSignatures, pointerTypes))
        {
            return false;
        }

        foreach (var parameter in member.Parameters)
        {
            if (parameter is null
                || parameter.Mode != BoundInterfaceParameterMode.Value
                || !TryGetNativeValueType(parameter.Type, pointerTypes,
                    functionPointerSignatures, allowVoid: false, out _))
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
                StringComparer.Ordinal.Equals(definition.QualifiedName,
                    foreignCall.Member.DeclaringInterface.NominalName))
            .ToArray();
        if (owners is not [{ } owner]
            || owner.ForeignNativeInterface is not
                BoundForeignNativeInterfaceLayout layout
            || layout.VTableByteOffset != foreignCall.VTableByteOffset
            || (program.NativeCoreRegistration?.ForeignNativeInterface
                    is null
                && owner.ModuleImport is null))
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
            || !IsValidMethodSignature(member, program.NativePointerTypes,
                program.NativeFunctionPointerSignatures))
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
