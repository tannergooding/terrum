// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal enum NativeCallingConvention : byte
{
    Native,
}

internal sealed record NativeFunctionPointerSignatureDefinition(
    NativeCallingConvention CallingConvention,
    PrimitiveType ReturnType,
    ImmutableArray<PrimitiveType> ParameterTypes);

internal static class NativeFunctionPointerTypes
{
    internal const int First = ManagedClassTypes.OptionalWriteFirst + 256;

    internal static bool IsType(PrimitiveType type) =>
        (int)type >= First && (int)type < NativePointerTypes.First;

    internal static int Index(PrimitiveType type) => checked((int)type - First);

    internal static PrimitiveType Type(int index) => (PrimitiveType)checked(First + index);
}

internal static class NativeFunctionPointerContract
{
    internal static bool IsValidTable(ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        if (signatures.IsDefault)
        {
            return false;
        }

        var state = new byte[signatures.Length];
        for (var index = 0; index < signatures.Length; index++)
        {
            if (!IsValidSignature(index, signatures, state, 0))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsValidType(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures) =>
        NativeFunctionPointerTypes.IsType(type)
            && (uint)NativeFunctionPointerTypes.Index(type) < (uint)signatures.Length;

    internal static bool IsValidSignature(NativeFunctionPointerSignatureDefinition signature,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        if (signature.CallingConvention != NativeCallingConvention.Native
            || signature.ParameterTypes.IsDefault
            || !IsSupportedReturnType(signature.ReturnType, signatures))
        {
            return false;
        }

        foreach (var parameterType in signature.ParameterTypes)
        {
            if (!IsSupportedValueType(parameterType, signatures))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool MatchesFunction(BoundFunction function, PrimitiveType functionPointerType,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        if (!NativeFunctionPointerTypes.IsType(functionPointerType)
            || (uint)NativeFunctionPointerTypes.Index(functionPointerType) >= (uint)signatures.Length
            || function.NativeFunctionPointerType != functionPointerType
            || function.IsExternal || !function.IsNoThrow || !function.RequiresUnsafeCaller
            || function.DoesNotReturn || function.IsConstructor || function.IsClassConstructor
            || function.IsOperator || function.IsCleanup || function.PropertyName is not null
            || function.Intrinsic != BoundIntrinsic.None
            || function.DeclaringStruct is not null && !function.IsStatic
            || function.ParameterCount != signatures[NativeFunctionPointerTypes.Index(functionPointerType)]
                .ParameterTypes.Length
            || function.Variables.Length < function.ParameterCount)
        {
            return false;
        }

        var signature = signatures[NativeFunctionPointerTypes.Index(functionPointerType)];
        if (function.ReturnType != signature.ReturnType)
        {
            return false;
        }

        for (var index = 0; index < signature.ParameterTypes.Length; index++)
        {
            if (function.Variables[index] is not {
                IsOut: false, IsInit: false, IsMutable: false, IsImplicitRef: false
            } parameter
                || parameter.Type != signature.ParameterTypes[index])
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsSupportedValueType(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures) =>
        type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64
            or PrimitiveType.NativeRawPointer
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type)
                && (uint)NativeFunctionPointerTypes.Index(type) < (uint)signatures.Length;

    private static bool IsSupportedReturnType(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures) =>
        type == PrimitiveType.Void || IsSupportedValueType(type, signatures);

    private static bool IsValidSignature(int index,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures, byte[] state, int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return false;
        }
        if (state[index] == 2)
        {
            return true;
        }
        if (state[index] == 1)
        {
            return false;
        }

        state[index] = 1;
        var signature = signatures[index];
        if (signature is null || signature.CallingConvention != NativeCallingConvention.Native
            || signature.ParameterTypes.IsDefault
            || !IsValidType(signature.ReturnType, signatures, state, allowVoid: true, depth: depth + 1))
        {
            return false;
        }
        foreach (var parameterType in signature.ParameterTypes)
        {
            if (!IsValidType(parameterType, signatures, state, allowVoid: false, depth: depth + 1))
            {
                return false;
            }
        }
        state[index] = 2;
        return true;
    }

    private static bool IsValidType(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures, byte[] state, bool allowVoid,
        int depth)
    {
        if (NativeFunctionPointerTypes.IsType(type))
        {
            var index = NativeFunctionPointerTypes.Index(type);
            return (uint)index < (uint)signatures.Length
                && IsValidSignature(index, signatures, state, depth + 1);
        }

        return type == PrimitiveType.NativeRawPointer || NativePointerTypes.IsType(type)
            || type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32 or PrimitiveType.UInt32
                or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64
            || allowVoid && type == PrimitiveType.Void;
    }
}
