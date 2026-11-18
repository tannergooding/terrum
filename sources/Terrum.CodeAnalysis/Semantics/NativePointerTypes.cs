using System;
using System.Collections.Generic;
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
    private readonly record struct NominalPointeeTypes(
        ImmutableArray<BoundClass> Classes,
        ImmutableArray<BoundGenericNominalTypeInstance> Instances,
        ImmutableArray<BoundGenericNominalTypeDefinition> Definitions)
    {
        internal bool IsValidClassType(PrimitiveType type)
        {
            if ((int)type >= ManagedClassTypes.First
                && (int)type < ManagedClassTypes.OptionalWriteFirst + 256)
            {
                var classIndex = ManagedClassTypes.ClassIndex(type);
                return !Classes.IsDefault
                    && (uint)classIndex < (uint)Classes.Length
                    && Classes[classIndex] is not null;
            }

            return !Instances.IsDefault && !Definitions.IsDefault
                && BoundGenericNominalTypeContract.IsNominalClassType(type,
                    Instances, Definitions);
        }
    }

    internal static bool IsSourcePointee(
        PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        string? nativeLoweringAgreement,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes) =>
        IsOptionalValuePointee(type, structures, checkedReferenceTypes,
            nativeLoweringAgreement, fixedArrayTypes)
        || DirectNativeImportContract.IsInputPointee(type, structures);

    internal static bool IsCopyableSourcePointee(
        PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        string? nativeLoweringAgreement,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes)
    {
        return IsOptionalValuePointee(type, structures,
                checkedReferenceTypes, nativeLoweringAgreement,
                fixedArrayTypes)
            || DirectNativeImportContract.IsCopyablePointee(type,
                structures);
    }

    private static bool IsOptionalValuePointee(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        string? nativeLoweringAgreement,
        IReadOnlyList<BoundNativeFixedArrayType>? fixedArrayTypes) =>
        CheckedReferenceTypes.IsType(type)
        && CheckedReferenceTypeContract.IsOptionalValueType(type,
            checkedReferenceTypes)
        && ValueLayout.TryGetOptionalValueLayout(type, structures,
            checkedReferenceTypes, nativeLoweringAgreement, fixedArrayTypes,
            out _);

    internal static bool IsValidTable(
        BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return IsValidTable(program.NativePointerTypes, program.Structures,
            program.NativeFunctionPointerSignatures,
            program.NativeFixedArrayTypes, program.CheckedReferenceTypes,
            program.EnumDefinitions, program.Classes,
            program.GenericNominalTypeInstances,
            program.GenericNominalTypeDefinitions);
    }

    internal static bool IsValidTable(CompilationReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return IsValidTable(reference.NativePointerTypes,
            reference.Structures, reference.NativeFunctionPointerSignatures,
            reference.NativeFixedArrayTypes, reference.CheckedReferenceTypes,
            reference.EnumDefinitions, reference.Classes,
            genericNominalTypeInstances:
                reference.GenericNominalTypeInstances,
            genericNominalTypeDefinitions:
                reference.GenericNominalTypeDefinitions);
    }

    internal static bool IsValidTable(
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointers,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrays = default,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes = default,
        IReadOnlyList<BoundEnumDefinition>? enumDefinitions = null,
        ImmutableArray<BoundClass> classes = default,
        ImmutableArray<BoundGenericNominalTypeInstance>
            genericNominalTypeInstances = default,
        ImmutableArray<BoundGenericNominalTypeDefinition>
            genericNominalTypeDefinitions = default)
    {
        var completeReferenceTypes = checkedReferenceTypes.IsDefault
            ? []
            : checkedReferenceTypes;
        var nominalPointeeTypes = new NominalPointeeTypes(classes,
            genericNominalTypeInstances, genericNominalTypeDefinitions);
        if (pointerTypes.IsDefault || structures.IsDefault || functionPointers.IsDefault
            || pointerTypes.Length > SemanticVerifier.MaximumAnalysisVisits
            || !CheckedReferenceTypeContract.IsValidTable(
                completeReferenceTypes, pointerTypes)
            || !NativeFunctionPointerContract.IsValidTable(functionPointers,
                type => DirectNativeImportContract.IsInputPointee(type,
                    structures), enumDefinitions)
            || !NativeFunctionPointerContract.IsValidNativeImportContractTable(
                functionPointers, pointerTypes, structures, enumDefinitions)
            || (!fixedArrays.IsDefault
                && !NativeFixedArrayContract.IsValidTable(structures,
                    fixedArrays, completeReferenceTypes, enumDefinitions)))
        {
            return false;
        }

        var pointerStates = new byte[pointerTypes.Length];
        var functionPointerStates = new byte[functionPointers.Length];
        var arrayStates = fixedArrays.IsDefault
            ? [] : new byte[fixedArrays.Length];
        var optionalValueStates = new byte[completeReferenceTypes.Length];
        for (var index = 0; index < arrayStates.Length; index++)
        {
            if (!IsValidFixedArray(index, pointerTypes, structures,
                    functionPointers, fixedArrays, pointerStates,
                    functionPointerStates, arrayStates,
                    completeReferenceTypes, optionalValueStates,
                    enumDefinitions, nominalPointeeTypes, 0))
            {
                return false;
            }
        }
        for (var index = 0; index < pointerTypes.Length; index++)
        {
            if (!IsValidPointer(index, pointerTypes, structures, functionPointers,
                fixedArrays, pointerStates, functionPointerStates, arrayStates,
                completeReferenceTypes, optionalValueStates,
                enumDefinitions, nominalPointeeTypes, 0))
            {
                return false;
            }
        }

        for (var index = 0; index < functionPointers.Length; index++)
        {
            if (!IsValidFunctionPointer(index, pointerTypes, structures, functionPointers,
                fixedArrays, pointerStates, functionPointerStates, arrayStates,
                completeReferenceTypes, optionalValueStates,
                enumDefinitions, nominalPointeeTypes, 0))
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
        byte[] arrayStates,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        byte[] optionalValueStates,
        IReadOnlyList<BoundEnumDefinition>? enumDefinitions,
        NominalPointeeTypes nominalPointeeTypes, int depth)
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
            checkedReferenceTypes, optionalValueStates, enumDefinitions,
            nominalPointeeTypes, allowClassHandleStorage: true,
            depth: depth + 1))
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
        byte[] arrayStates,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        byte[] optionalValueStates,
        IReadOnlyList<BoundEnumDefinition>? enumDefinitions,
        NominalPointeeTypes nominalPointeeTypes, int depth)
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
                functionPointerStates, arrayStates, checkedReferenceTypes,
                optionalValueStates, enumDefinitions,
                nominalPointeeTypes, depth + 1))
        {
            return false;
        }
        foreach (var parameterType in signature.ParameterTypes)
        {
            if (!IsValidFunctionPointerValue(parameterType, pointerTypes, structures, functionPointers,
                fixedArrays, pointerStates, functionPointerStates,
                arrayStates, checkedReferenceTypes, optionalValueStates,
                enumDefinitions, nominalPointeeTypes, depth + 1))
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
        byte[] arrayStates,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        byte[] optionalValueStates,
        IReadOnlyList<BoundEnumDefinition>? enumDefinitions,
        NominalPointeeTypes nominalPointeeTypes,
        bool allowClassHandleStorage, int depth)
    {
        if (type == PrimitiveType.Void)
        {
            return true;
        }
        if (NativePointerTypes.IsType(type))
        {
            return IsValidPointer(NativePointerTypes.Index(type), pointerTypes, structures,
                functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, checkedReferenceTypes,
                optionalValueStates, enumDefinitions,
                nominalPointeeTypes, depth + 1);
        }
        if (NativeFunctionPointerTypes.IsType(type))
        {
            return IsValidFunctionPointer(NativeFunctionPointerTypes.Index(type), pointerTypes,
                structures, functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, checkedReferenceTypes,
                optionalValueStates, enumDefinitions,
                nominalPointeeTypes, depth + 1);
        }
        if (NativeFixedArrayTypes.IsType(type))
        {
            return IsValidFixedArray(NativeFixedArrayTypes.Index(type),
                pointerTypes, structures, functionPointers, fixedArrays,
                pointerStates, functionPointerStates, arrayStates,
                checkedReferenceTypes, optionalValueStates, enumDefinitions,
                nominalPointeeTypes, depth + 1);
        }
        if (CheckedReferenceTypes.IsType(type))
        {
            return IsValidOptionalValue(CheckedReferenceTypes.Index(type),
                pointerTypes, structures, functionPointers, fixedArrays,
                pointerStates, functionPointerStates, arrayStates,
                checkedReferenceTypes, optionalValueStates, enumDefinitions,
                nominalPointeeTypes, depth + 1);
        }
        if (BoundEnumTypes.IsType(type))
        {
            return true;
        }
        if (IsSupportedScalar(type))
        {
            return true;
        }
        if (allowClassHandleStorage
            && nominalPointeeTypes.IsValidClassType(type))
        {
            return true;
        }

        var index = (int)type - 100;
        return index >= 0 && index < structures.Length;
    }

    private static bool IsValidOptionalValue(int index,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointers,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrays,
        byte[] pointerStates, byte[] functionPointerStates,
        byte[] arrayStates,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        byte[] optionalValueStates,
        IReadOnlyList<BoundEnumDefinition>? enumDefinitions,
        NominalPointeeTypes nominalPointeeTypes, int depth)
    {
        if ((uint)index >= (uint)checkedReferenceTypes.Length
            || depth > SemanticVerifier.MaximumDepth
            || optionalValueStates[index] == 1)
        {
            return false;
        }
        if (optionalValueStates[index] == 2)
        {
            return true;
        }

        var type = CheckedReferenceTypes.Type(index);
        if (!CheckedReferenceTypeContract.IsOptionalValueType(type,
                checkedReferenceTypes)
            || !ValueLayout.TryGetOptionalValueLayout(type, structures,
                checkedReferenceTypes, null,
                fixedArrays.IsDefault
                    ? []
                    : fixedArrays,
                out _, enumDefinitions))
        {
            return false;
        }

        optionalValueStates[index] = 1;
        var payloadType = checkedReferenceTypes[index].ReferentType;
        if (!IsValidPointee(payloadType, pointerTypes, structures,
                functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, checkedReferenceTypes,
                optionalValueStates, enumDefinitions, nominalPointeeTypes,
                allowClassHandleStorage: false, depth: depth + 1))
        {
            return false;
        }
        optionalValueStates[index] = 2;
        return true;
    }

    private static bool IsValidFixedArray(int index,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointers,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrays,
        byte[] pointerStates, byte[] functionPointerStates,
        byte[] arrayStates,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        byte[] optionalValueStates,
        IReadOnlyList<BoundEnumDefinition>? enumDefinitions,
        NominalPointeeTypes nominalPointeeTypes, int depth)
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
                functionPointerStates, arrayStates, checkedReferenceTypes,
                optionalValueStates, enumDefinitions,
                nominalPointeeTypes, allowClassHandleStorage: false,
                depth: depth + 1))
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
        byte[] arrayStates,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        byte[] optionalValueStates,
        IReadOnlyList<BoundEnumDefinition>? enumDefinitions,
        NominalPointeeTypes nominalPointeeTypes, int depth)
    {
        if (NativePointerTypes.IsType(type))
        {
            return IsValidPointer(NativePointerTypes.Index(type), pointerTypes, structures,
                functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, checkedReferenceTypes,
                optionalValueStates, enumDefinitions,
                nominalPointeeTypes, depth + 1);
        }
        if (NativeFunctionPointerTypes.IsType(type))
        {
            return IsValidFunctionPointer(NativeFunctionPointerTypes.Index(type), pointerTypes,
                structures, functionPointers, fixedArrays, pointerStates,
                functionPointerStates, arrayStates, checkedReferenceTypes,
                optionalValueStates, enumDefinitions,
                nominalPointeeTypes, depth + 1);
        }
        return IsSupportedFunctionPointerScalar(type);
    }

    private static bool IsSupportedScalar(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32
            or PrimitiveType.Rune or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
            or PrimitiveType.Int64 or PrimitiveType.NativeInt
            or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.UInt64 or PrimitiveType.NativeUInt
            or PrimitiveType.Float32 or PrimitiveType.Float64
            or PrimitiveType.NativeRawPointer or PrimitiveType.NativeUtf16ZPointer
            or PrimitiveType.NativeUtf8ZPointer;

    private static bool IsSupportedFunctionPointerScalar(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32
            or PrimitiveType.Rune or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
            or PrimitiveType.Int64 or PrimitiveType.NativeInt
            or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.UInt64 or PrimitiveType.NativeUInt
            or PrimitiveType.Float32 or PrimitiveType.Float64
            or PrimitiveType.NativeRawPointer
            || BoundEnumTypes.IsType(type);
}
