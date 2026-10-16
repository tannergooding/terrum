// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeCallbackContract
{
    internal const string TypeName = "Terrum.Interop.NativeCallback";

    internal static bool SignaturesEqual(NativeCallbackSignatureDefinition left,
        NativeCallbackSignatureDefinition right)
    {
        if (left.CallbackType != right.CallbackType || left.ReturnType != right.ReturnType
            || left.ParameterTypes.Length != right.ParameterTypes.Length)
        {
            return false;
        }

        for (var index = 0; index < left.ParameterTypes.Length; index++)
        {
            if (left.ParameterTypes[index] != right.ParameterTypes[index])
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValid(NativeCallbackSignatureDefinition signature)
    {
        if (signature.CallbackType != PrimitiveType.NativeCallbackAddress
            || signature.ParameterTypes.IsDefault
            || !IsSupportedReturnType(signature.ReturnType))
        {
            return false;
        }

        foreach (var parameterType in signature.ParameterTypes)
        {
            if (!IsSupportedParameterType(parameterType))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool MatchesFunction(BoundFunction function,
        NativeCallbackSignatureDefinition signature)
    {
        if (!IsValid(signature) || function.IsExternal || function.ReturnType != signature.ReturnType
            || !function.IsNoThrow || !function.RequiresUnsafeCaller || function.DoesNotReturn
            || function.IsConstructor || function.IsClassConstructor || function.IsOperator || function.IsCleanup
            || function.PropertyName is not null || function.Intrinsic != BoundIntrinsic.None
            || (function.DeclaringStruct is not null && !function.IsStatic)
            || function.ParameterCount != signature.ParameterTypes.Length
            || function.Variables.Length < function.ParameterCount)
        {
            return false;
        }

        for (var index = 0; index < signature.ParameterTypes.Length; index++)
        {
            if (function.Variables[index] is not { IsOut: false, IsInit: false, IsMutable: false } parameter
                || parameter.Type != signature.ParameterTypes[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSupportedReturnType(PrimitiveType type) =>
        type is PrimitiveType.Void or PrimitiveType.Bool or PrimitiveType.Char16
            or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Float32 or PrimitiveType.Float64;

    private static bool IsSupportedParameterType(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char16
            or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Float32 or PrimitiveType.Float64 or PrimitiveType.NativeRawPointer;
}

internal sealed record NativeCallbackSignatureDefinition(PrimitiveType CallbackType, PrimitiveType ReturnType,
    ImmutableArray<PrimitiveType> ParameterTypes);
