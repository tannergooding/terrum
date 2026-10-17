using System;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record NativeVTableMethodDefinition(
    int Slot,
    ImmutableArray<DirectNativeParameter> Parameters,
    int VTableByteOffset = 0)
{
    internal ImmutableArray<DirectNativeOutput> Outputs { get; init; } = [];
}

internal static class NativeVTableCallContract
{
    internal static bool IsValid(BoundFunction function, BoundProgram program)
    {
        var isImplementation = !function.IsExternal
            && function.Intrinsic == BoundIntrinsic.NativeVTableCall;
        var isExternalDeclaration = function.IsExternal && function.Intrinsic == BoundIntrinsic.None;
        if ((!isImplementation && !isExternalDeclaration)
            || function.NativeVTableMethod is not NativeVTableMethodDefinition method
            || function.DirectNativeImport is not null
            || function.DeclaringStruct is not null
            || function.IsCleanup || function.IsConstructor || function.IsStatic
            || function.IsOperator || function.IsClassConstructor || function.PropertyName is not null
            || function.DoesNotReturn || !function.RequiresUnsafeCaller
            || function.NativeFunctionPointerType is not null
            || function.Body is null || function.Body.Statements.IsDefault
            || !function.Body.Statements.IsEmpty || function.Variables.IsDefault
            || function.ParameterCount < 1 || function.ParameterCount != function.Variables.Length
            || method.Slot < 0 || method.Slot > int.MaxValue / NativeTargetProfile.PointerSize
            || method.VTableByteOffset < 0
            || method.VTableByteOffset % NativeTargetProfile.PointerSize != 0
            || method.VTableByteOffset > int.MaxValue - NativeTargetProfile.PointerSize
            || method.Parameters.IsDefault
            || method.Outputs.IsDefault
            || !IsNativeValueType(function.ReturnType, program))
        {
            return false;
        }

        var receiver = function.Variables[0];
        if (receiver is null || receiver.IsOut || receiver.IsInit || receiver.IsImplicitRef
            || !IsNativeValueType(receiver.Type, program)
            || (receiver.Type is not (PrimitiveType.NativeRawPointer
                or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer)
                && !NativePointerTypeContract.IsValidType(receiver.Type, program.NativePointerTypes)))
        {
            return false;
        }

        var annotatedParameters = new bool[function.ParameterCount];
        var annotatedOutputs = new bool[function.ParameterCount];
        foreach (var output in method.Outputs)
        {
            if (output is null || output.ParameterIndex <= 0
                || output.ParameterIndex >= function.ParameterCount
                || annotatedOutputs[output.ParameterIndex]
                || !DirectNativeImportContract.IsOutputPointee(output.PointeeType)
                || (NativePointerTypes.IsType(output.PointeeType)
                    && !NativePointerTypeContract.IsValidType(
                        output.PointeeType, program.NativePointerTypes))
                || (NativeFunctionPointerTypes.IsType(output.PointeeType)
                    && !NativeFunctionPointerContract.IsValidType(
                        output.PointeeType, program.NativeFunctionPointerSignatures))
                || !DirectNativeImportContract.IsValidResultCondition(
                    function.ReturnType, output.ResultCondition)
                || output.FailureDisposition != 1
                || function.ReturnType is not (PrimitiveType.Int32 or PrimitiveType.UInt32)
                || function.Variables[output.ParameterIndex] is not {
                    Type: PrimitiveType.NativeStorageAddress,
                    IsOut: true,
                    IsInit: false,
                    IsImplicitRef: false,
                })
            {
                return false;
            }
            annotatedOutputs[output.ParameterIndex] = true;
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter is null || parameter.ParameterIndex <= 0
                || parameter.ParameterIndex >= function.ParameterCount
                || annotatedParameters[parameter.ParameterIndex]
                || annotatedOutputs[parameter.ParameterIndex]
                || parameter.Access is not (NativeParameterAccess.Read
                    or NativeParameterAccess.Write or NativeParameterAccess.ReadWrite)
                || parameter.Retention is not (NativeParameterRetention.NoEscape
                    or NativeParameterRetention.MayEscape)
                || parameter.Extent < 0
                || !DirectNativeImportContract.IsInputPointee(parameter.PointeeType, program.Structures)
                || function.Variables[parameter.ParameterIndex] is not {
                    Type: PrimitiveType.NativeRawPointer,
                    IsOut: false,
                    IsInit: false,
                    IsImplicitRef: false,
                })
            {
                return false;
            }
            annotatedParameters[parameter.ParameterIndex] = true;
        }

        for (var index = 1; index < function.ParameterCount; index++)
        {
            var parameter = function.Variables[index];
            if (parameter is null || parameter.IsInit || parameter.IsImplicitRef
                || parameter.Type == PrimitiveType.Void)
            {
                return false;
            }
            if (annotatedOutputs[index])
            {
                if (parameter.Type != PrimitiveType.NativeStorageAddress || !parameter.IsOut)
                {
                    return false;
                }
                continue;
            }
            if (parameter.IsOut)
            {
                if (annotatedParameters[index]
                    || parameter.Type == PrimitiveType.NativeStorageAddress
                    || !IsNativeValueType(parameter.Type, program))
                {
                    return false;
                }
            }
            else if (!IsNativeValueType(parameter.Type, program))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsOutputParameter(BoundFunction function, int parameterIndex,
        BoundProgram program) =>
        function.Intrinsic == BoundIntrinsic.NativeVTableCall
        && (uint)parameterIndex < (uint)function.ParameterCount
        && function.Variables[parameterIndex] is {
            IsOut: true,
            IsInit: false,
            IsImplicitRef: false,
        } parameter
        && parameter.Type != PrimitiveType.Void
        && IsNativeValueType(parameter.Type, program);

    internal static DirectNativeParameter? ParameterForParameter(BoundFunction function, int parameterIndex)
    {
        if (function.NativeVTableMethod is not NativeVTableMethodDefinition method
            || method.Parameters.IsDefault)
        {
            return null;
        }
        return method.Parameters.FirstOrDefault(parameter => parameter.ParameterIndex == parameterIndex);
    }

    internal static DirectNativeOutput? OutputForParameter(BoundFunction function, int parameterIndex)
    {
        if (function.NativeVTableMethod is not NativeVTableMethodDefinition method
            || method.Outputs.IsDefault)
        {
            return null;
        }
        return method.Outputs.FirstOrDefault(output => output.ParameterIndex == parameterIndex);
    }

    internal static bool IsNativeValueType(PrimitiveType type, BoundProgram program) =>
        IsNativeValueType(type, program.NativeFunctionPointerSignatures, program.NativePointerTypes);

    internal static bool IsNativeValueType(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> functionPointerSignatures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes) =>
        type == PrimitiveType.Void
            || DirectNativeImportContract.IsValueParameter(type,
                functionPointerSignatures, pointerTypes);
}
