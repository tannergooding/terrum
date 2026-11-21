// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
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
            || nativeCoreRegistration?.ForeignNativeInterface is not null;
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
}
