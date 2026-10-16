// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class DirectNativeImportContract
{
    internal const uint Int32NonZeroResultCondition = 1;
    internal const uint UInt32NonZeroResultCondition = 2;
    internal const uint Int32NonNegativeResultCondition = 3;

    internal static uint DefaultResultCondition(PrimitiveType returnType) => returnType switch {
        PrimitiveType.Int32 => Int32NonZeroResultCondition,
        PrimitiveType.UInt32 => UInt32NonZeroResultCondition,
        _ => 0,
    };

    internal static bool IsValidResultCondition(PrimitiveType returnType, uint resultCondition) =>
        (returnType == PrimitiveType.Int32
            && resultCondition is Int32NonZeroResultCondition or Int32NonNegativeResultCondition)
        || (returnType == PrimitiveType.UInt32
            && resultCondition == UInt32NonZeroResultCondition);

    internal static bool IsValidLibraryName(string name) =>
        name is { Length: >= 5 and <= 256 }
        && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        && name.All(static character => character is (>= '0' and <= '9') or (>= 'A' and <= 'Z')
            or (>= 'a' and <= 'z') or '_' or '-' or '.');

    internal static bool IsValidExportName(string name) =>
        name is { Length: >= 1 and <= 256 }
        && name.All(static character => character is >= '!' and <= '~');

    internal static bool IsScalar(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64;

    internal static bool IsValueParameter(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointerSignatures = default,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes = default) =>
        IsScalar(type) || type is PrimitiveType.NativeRawPointer or PrimitiveType.NativeUtf16ZPointer
            or PrimitiveType.NativeUtf8ZPointer
            || NativePointerTypeContract.IsValidType(type, pointerTypes)
            || NativeFunctionPointerContract.IsValidType(type, functionPointerSignatures);

    internal static bool IsStringPointerArgument(BoundExpression argument,
        DirectNativeParameter parameter, BoundProgram program) =>
        parameter.Access == NativeParameterAccess.Read
        && parameter.Retention == NativeParameterRetention.NoEscape
        && parameter.Extent == 0
        && (argument switch {
            BoundConstant {
                Type: PrimitiveType.NativeUtf16ZPointer,
                Value: >= 0,
            } literal => (parameter.PointeeType is PrimitiveType.Void or PrimitiveType.Char16)
                && literal.Value < program.NativeUtf16ZLiterals.Length,
            BoundConstant {
                Type: PrimitiveType.NativeUtf8ZPointer,
                Value: >= 0,
            } literal => parameter.PointeeType == PrimitiveType.Void
                && literal.Value < program.NativeUtf8ZLiterals.Length,
            _ => false,
        });

    internal static bool IsOutputPointee(PrimitiveType type) =>
        IsScalar(type) || type is PrimitiveType.NativeRawPointer or PrimitiveType.NativeUtf16ZPointer
            or PrimitiveType.NativeUtf8ZPointer || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type);

    internal static bool IsInputPointee(PrimitiveType type, IReadOnlyList<BoundStruct> structures)
    {
        if (type is PrimitiveType.Void or PrimitiveType.NativeRawPointer
                or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
            || IsScalar(type) || NativePointerTypes.IsType(type) || NativeFunctionPointerTypes.IsType(type))
        {
            return true;
        }
        return IsBlittableAggregate(type, structures);
    }

    private static bool IsBlittableAggregate(PrimitiveType type, IReadOnlyList<BoundStruct> structures)
    {
        var index = (int)type - 100;
        return index >= 0 && index < structures.Count
            && structures[index].BlittableTargetRequirement == FixedLayoutStorage.WindowsX64TargetRequirement
            && (structures[index].IsSequential || structures[index].IsTransparent);
    }

    internal static bool IsValid(BoundFunction function, IReadOnlyList<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointerSignatures = default,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes = default)
    {
        if (function.DirectNativeImport is not DirectNativeImport import
            || !function.IsExternal || function.NativeImportKey is not null
            || !function.RequiresUnsafeCaller
            || !IsValidLibraryName(import.LibraryName) || !IsValidExportName(import.ExportName)
            || import.Outputs.IsDefault || import.Parameters.IsDefault
            || function.Variables.IsDefault || function.ParameterCount < 0
            || function.ParameterCount > function.Variables.Length
            || function.ReturnType != PrimitiveType.Void
                && !IsValueParameter(function.ReturnType, functionPointerSignatures, pointerTypes)
            || import.Outputs.Length > function.ParameterCount)
        {
            return false;
        }

        var outputs = new bool[function.ParameterCount];
        var nativeParameters = new bool[function.ParameterCount];
        foreach (var output in import.Outputs)
        {
            if (output is null || output.ParameterIndex < 0 || output.ParameterIndex >= function.ParameterCount
                || outputs[output.ParameterIndex] || !IsOutputPointee(output.PointeeType)
                || NativePointerTypes.IsType(output.PointeeType)
                    && !NativePointerTypeContract.IsValidType(output.PointeeType, pointerTypes)
                || !IsValidResultCondition(function.ReturnType, output.ResultCondition)
                || output.FailureDisposition != 1
                || function.Variables[output.ParameterIndex] is not BoundVariable outputParameter
                || outputParameter.Type != PrimitiveType.NativeStorageAddress
                || !outputParameter.IsOut || outputParameter.IsInit
                || function.ReturnType is not (PrimitiveType.Int32 or PrimitiveType.UInt32))
            {
                return false;
            }
            outputs[output.ParameterIndex] = true;
        }

        foreach (var parameter in import.Parameters)
        {
            if (parameter is null || parameter.ParameterIndex < 0
                || parameter.ParameterIndex >= function.ParameterCount
                || nativeParameters[parameter.ParameterIndex] || outputs[parameter.ParameterIndex]
                || !IsInputPointee(parameter.PointeeType, structures)
                || NativePointerTypes.IsType(parameter.PointeeType)
                    && !NativePointerTypeContract.IsValidType(parameter.PointeeType, pointerTypes)
                || parameter.Access is not (NativeParameterAccess.Read
                    or NativeParameterAccess.Write or NativeParameterAccess.ReadWrite)
                || parameter.Retention != NativeParameterRetention.NoEscape
                || parameter.Extent < 0
                || (parameter.PointeeType == PrimitiveType.Void
                    ? parameter.Access != NativeParameterAccess.Read || parameter.Extent != 0
                    : false)
                || function.Variables[parameter.ParameterIndex] is not BoundVariable pointerParameter
                || pointerParameter.Type != PrimitiveType.NativeRawPointer
                || pointerParameter.IsOut || pointerParameter.IsInit)
            {
                return false;
            }
            nativeParameters[parameter.ParameterIndex] = true;
        }

        for (var index = 0; index < function.ParameterCount; index++)
        {
            if (function.Variables[index] is not BoundVariable parameter)
            {
                return false;
            }
            var parameterType = parameter.Type;
            if (parameterType == PrimitiveType.NativeStorageAddress)
            {
                if (!outputs[index] || !parameter.IsOut || parameter.IsInit)
                {
                    return false;
                }
            }
            else if (parameterType == PrimitiveType.NativeRawPointer)
            {
                if (outputs[index] || parameter.IsOut || parameter.IsInit)
                {
                    return false;
                }
            }
            else if (!IsValueParameter(parameterType, functionPointerSignatures, pointerTypes)
                || outputs[index] || parameter.IsOut || parameter.IsInit)
            {
                return false;
            }
        }
        return true;
    }

    internal static DirectNativeOutput? OutputForParameter(BoundFunction function, int parameterIndex)
    {
        if (function.DirectNativeImport is not DirectNativeImport import
            || import.Outputs.IsDefault)
        {
            return null;
        }
        return import.Outputs.FirstOrDefault(output => output.ParameterIndex == parameterIndex);
    }

    internal static DirectNativeParameter? ParameterForParameter(BoundFunction function, int parameterIndex)
    {
        if (function.DirectNativeImport is not DirectNativeImport import
            || import.Parameters.IsDefault)
        {
            return null;
        }
        return import.Parameters.FirstOrDefault(parameter => parameter.ParameterIndex == parameterIndex);
    }

    internal static bool MatchesAddress(BoundNativeStorageAddress address, DirectNativeOutput output,
        BoundFunction caller, BoundProgram program)
    {
        var extent = ValueLayout.Size(output.PointeeType, program.Structures);
        return address.Type == PrimitiveType.NativeStorageAddress
            && address.AddressType == PrimitiveType.NativeStorageAddress
            && address.CarrierType == output.PointeeType
            && address.ElementType == output.PointeeType
            && address.ElementCount == 1
            && address.ElementStride == extent
            && address.ByteExtent == extent
            && !address.IsReadonly
            && address.Index is BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
            && (uint)address.Variable < (uint)caller.Variables.Length
            && caller.Variables[address.Variable].Type == output.PointeeType
            && caller.Variables[address.Variable].IsMutable
            && !caller.Variables[address.Variable].IsInitializationLocal;
    }

    internal static bool MatchesAddress(BoundNativeStorageAddress address, DirectNativeParameter parameter,
        BoundFunction caller, BoundProgram program)
    {
        if (address.Type != PrimitiveType.NativeStorageAddress
            || address.AddressType != PrimitiveType.NativeStorageAddress
            || address.IsReadonly != (parameter.Access == NativeParameterAccess.Read)
            || address.Index is not BoundConstant { Type: PrimitiveType.Int32, Value: 0 }
            || (uint)address.Variable >= (uint)caller.Variables.Length
            || caller.Variables[address.Variable].Type != address.CarrierType
            || caller.Variables[address.Variable].IsInitializationLocal
            || (parameter.Access != NativeParameterAccess.Read
                && !caller.Variables[address.Variable].IsMutable))
        {
            return false;
        }

        if (address.CarrierType == PrimitiveType.NativeFixedArray)
        {
            return caller.Variables[address.Variable].NativeFixedArrayLayout
                    is BoundNativeFixedArrayLayout layout
                && NativeFixedArrayContract.MatchesLayout(layout, program.Structures)
                && address.ElementType == layout.ElementType
                && (parameter.PointeeType == PrimitiveType.Void
                    || parameter.PointeeType == layout.ElementType)
                && address.ElementCount == layout.ElementCount
                && address.ElementStride == layout.ElementStride
                && address.ByteExtent == layout.ByteExtent
                && (parameter.Extent == 0 || address.ElementCount >= parameter.Extent)
                && (parameter.PointeeType != PrimitiveType.Void
                    || DirectNativeImportContract.IsInputPointee(layout.ElementType, program.Structures))
                && (parameter.Access == NativeParameterAccess.Read
                    || NativeFixedArrayContract.AllowsMutableLocal(layout));
        }

        var elementExtent = ValueLayout.Size(address.ElementType, program.Structures);
        return address.CarrierType == address.ElementType
            && (parameter.PointeeType == PrimitiveType.Void
                ? DirectNativeImportContract.IsInputPointee(address.ElementType, program.Structures)
                : address.ElementType == parameter.PointeeType)
            && address.ElementCount == 1
            && parameter.Extent <= 1
            && address.ElementStride == elementExtent
            && address.ByteExtent == elementExtent;
    }
}
