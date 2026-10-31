using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record NativePointerTypeDefinition(PrimitiveType PointeeType, bool IsWritable);

internal static class NativePointerTypes
{
    internal const int First = 1 << 30;

    internal static bool IsType(PrimitiveType type) => (int)type >= First;

    internal static int Index(PrimitiveType type) => checked((int)type - First);

    internal static PrimitiveType Type(int index) => (PrimitiveType)checked(First + index);
}

internal static class NativePointerTypeContract
{
    internal static bool IsValidTable(
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointers,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrays = default)
    {
        if (pointerTypes.IsDefault || structures.IsDefault || functionPointers.IsDefault
            || pointerTypes.Length > SemanticVerifier.MaximumAnalysisVisits
            || !NativeFunctionPointerContract.IsValidTable(functionPointers)
            || (!fixedArrays.IsDefault
                && !NativeFixedArrayContract.IsValidTable(structures,
                    fixedArrays)))
        {
            return false;
        }

        var pointerStates = new byte[pointerTypes.Length];
        var functionPointerStates = new byte[functionPointers.Length];
        var arrayStates = fixedArrays.IsDefault
            ? [] : new byte[fixedArrays.Length];
        for (var index = 0; index < arrayStates.Length; index++)
        {
            if (!IsValidFixedArray(index, pointerTypes, structures,
                    functionPointers, fixedArrays, pointerStates,
                    functionPointerStates, arrayStates, 0))
            {
                return false;
            }
        }
        for (var index = 0; index < pointerTypes.Length; index++)
        {
            if (!IsValidPointer(index, pointerTypes, structures, functionPointers,
                fixedArrays, pointerStates, functionPointerStates, arrayStates,
                0))
            {
                return false;
            }
        }

        for (var index = 0; index < functionPointers.Length; index++)
        {
            if (!IsValidFunctionPointer(index, pointerTypes, structures, functionPointers,
                fixedArrays, pointerStates, functionPointerStates, arrayStates,
                0))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidType(PrimitiveType type,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes) =>
        NativePointerTypes.IsType(type)
        && (uint)NativePointerTypes.Index(type) < (uint)pointerTypes.Length;

    private static bool IsValidPointer(int index,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointers,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrays,
        byte[] pointerStates, byte[] functionPointerStates,
        byte[] arrayStates, int depth)
    {
        if ((uint)index >= (uint)pointerTypes.Length || depth > SemanticVerifier.MaximumDepth
            || pointerStates[index] == 1)
        {
            return false;
        }
        if (pointerStates[index] == 2)
        {
            return true;
        }

        var pointerType = pointerTypes[index];
        if (pointerType is null)
        {
            return false;
        }
        if (pointerType.PointeeType == PrimitiveType.Void && !pointerType.IsWritable)
        {
            return false;
        }
        pointerStates[index] = 1;
        if (!IsValidPointee(pointerType.PointeeType, pointerTypes, structures, functionPointers,
            fixedArrays, pointerStates, functionPointerStates, arrayStates,
            depth + 1))
        {
            return false;
        }
        pointerStates[index] = 2;
        return true;
    }

    private static bool IsValidFunctionPointer(int index,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointers,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrays,
        byte[] pointerStates, byte[] functionPointerStates,
        byte[] arrayStates, int depth)
    {
        if ((uint)index >= (uint)functionPointers.Length || depth > SemanticVerifier.MaximumDepth
            || functionPointerStates[index] == 1)
        {
            return false;
        }
        if (functionPointerStates[index] == 2)
        {
            return true;
        }

        var signature = functionPointers[index];
        if (signature is null)
        {
            return false;
        }
        functionPointerStates[index] = 1;
        if (signature.ReturnType != PrimitiveType.Void
            && !IsValidFunctionPointerValue(signature.ReturnType, pointerTypes, structures,
                functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, depth + 1))
        {
            return false;
        }
        foreach (var parameterType in signature.ParameterTypes)
        {
            if (!IsValidFunctionPointerValue(parameterType, pointerTypes, structures, functionPointers,
                fixedArrays, pointerStates, functionPointerStates,
                arrayStates, depth + 1))
            {
                return false;
            }
        }
        functionPointerStates[index] = 2;
        return true;
    }

    private static bool IsValidPointee(PrimitiveType type,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointers,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrays,
        byte[] pointerStates, byte[] functionPointerStates,
        byte[] arrayStates, int depth)
    {
        if (type == PrimitiveType.Void)
        {
            return true;
        }
        if (NativePointerTypes.IsType(type))
        {
            return IsValidPointer(NativePointerTypes.Index(type), pointerTypes, structures,
                functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, depth + 1);
        }
        if (NativeFunctionPointerTypes.IsType(type))
        {
            return IsValidFunctionPointer(NativeFunctionPointerTypes.Index(type), pointerTypes,
                structures, functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, depth + 1);
        }
        if (NativeFixedArrayTypes.IsType(type))
        {
            return IsValidFixedArray(NativeFixedArrayTypes.Index(type),
                pointerTypes, structures, functionPointers, fixedArrays,
                pointerStates, functionPointerStates, arrayStates, depth + 1);
        }
        if (IsSupportedScalar(type))
        {
            return true;
        }

        var index = (int)type - 100;
        return index >= 0 && index < structures.Length;
    }

    private static bool IsValidFixedArray(int index,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointers,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrays,
        byte[] pointerStates, byte[] functionPointerStates,
        byte[] arrayStates, int depth)
    {
        if (fixedArrays.IsDefault || (uint)index >= (uint)fixedArrays.Length
            || depth > SemanticVerifier.MaximumDepth
            || arrayStates[index] == 1)
        {
            return false;
        }
        if (arrayStates[index] == 2)
        {
            return true;
        }

        arrayStates[index] = 1;
        if (!IsValidPointee(fixedArrays[index].ElementType, pointerTypes,
                structures, functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, depth + 1))
        {
            return false;
        }
        arrayStates[index] = 2;
        return true;
    }

    private static bool IsValidFunctionPointerValue(PrimitiveType type,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointers,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrays,
        byte[] pointerStates, byte[] functionPointerStates,
        byte[] arrayStates, int depth)
    {
        if (NativePointerTypes.IsType(type))
        {
            return IsValidPointer(NativePointerTypes.Index(type), pointerTypes, structures,
                functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, depth + 1);
        }
        if (NativeFunctionPointerTypes.IsType(type))
        {
            return IsValidFunctionPointer(NativeFunctionPointerTypes.Index(type), pointerTypes,
                structures, functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, depth + 1);
        }
        return IsSupportedFunctionPointerScalar(type);
    }

    private static bool IsSupportedScalar(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32
            or PrimitiveType.Rune or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
            or PrimitiveType.Int64 or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64
            or PrimitiveType.NativeRawPointer or PrimitiveType.NativeUtf16ZPointer
            or PrimitiveType.NativeUtf8ZPointer;

    private static bool IsSupportedFunctionPointerScalar(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32
            or PrimitiveType.Rune or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
            or PrimitiveType.Int64 or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64
            or PrimitiveType.NativeRawPointer;
}
