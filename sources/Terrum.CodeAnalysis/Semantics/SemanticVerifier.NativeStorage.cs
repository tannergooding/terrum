// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyNativeFixedArrayInitialization(BoundNativeFixedArrayInitialization initialization,
        int variable, InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var layout = initialization.Layout;
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || (uint)variable >= (uint)function.Variables.Length
            || variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || function.Variables[variable].Type != layout.CarrierType
            || function.Variables[variable].NativeFixedArrayLayout != layout
            || !NativeFixedArrayContract.MatchesLayout(layout, program.Structures, program.NativeImportCatalog)
            || (function.Variables[variable].IsMutable
                && !NativeFixedArrayContract.AllowsMutableLocal(layout, program.Structures,
                    program.NativeImportCatalog))
            || initialization.Elements.IsDefault
            || (initialization.Elements.IsEmpty
                ? !NativeFixedArrayContract.AllowsEmptyInitialization(layout, program.Structures,
                    program.NativeImportCatalog)
                : initialization.Elements.Length != layout.ElementCount))
        {
            throw InvalidEffect(initialization.Span, "NativeImport");
        }

        foreach (var element in initialization.Elements)
        {
            if (element is null || element.Type != layout.ElementType)
            {
                throw InvalidEffect(element?.Span ?? initialization.Span, "NativeImport");
            }
            Expression(element, initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures);
        }
    }

    private static void VerifyNativeStorageParameterAddress(BoundNativeStorageAddress address,
        NativeStorageParameterDefinition parameter,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (!NativeStorageParameterContract.MatchesAddress(address, parameter)
            || (parameter.AddressType == PrimitiveType.NativeStorageAddress
                && (parameter.RequiredStartIndex != 0
                    || address.Index is not BoundConstant {
                            Type: PrimitiveType.Int32,
                            Value: 0,
                        }))
            || address.Variable < 0 || address.Variable >= function.Variables.Length
            || function.Variables[address.Variable].NativeFixedArrayLayout is not BoundNativeFixedArrayLayout layout
            || layout.CarrierType != parameter.CarrierType
            || layout.ElementType != address.ElementType
            || address.ElementCount != layout.ElementCount
            || address.ElementStride != layout.ElementStride
            || address.ByteExtent != layout.ByteExtent
            || NativeFixedArrayContract.ElementTypeName(layout.ElementType, program.Structures)
                != parameter.ElementTypeName
            || (parameter.AddressType == PrimitiveType.NativeUtf16ZPointer
                && !NativeFixedArrayContract.AllowsReadonlyUtf16ZLoan(
                    layout, program.Structures, program.NativeImportCatalog))
            || !NativeFixedArrayContract.MatchesLayout(layout, program.Structures, program.NativeImportCatalog))
        {
            throw InvalidEffect(address.Span, "NativeImport");
        }
        VerifyNativeFixedArrayPlace(address.Variable, address.Index, parameter.CarrierType,
            parameter.RequiredStartIndex, parameter.IsReadonly, initialized, fieldMasks, declarations,
            function, program, ref remaining, depth + 1, failures);
    }

    private static void VerifyNativeFixedArrayPlace(int variable, BoundExpression index,
        PrimitiveType carrierType, int? requiredStartIndex, bool isReadonly,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || !NativeFixedArrayContract.IsCarrier(carrierType)
            || variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || variable >= function.Variables.Length || !declarations.Contains(variable) || !initialized[variable]
            || function.Variables[variable].Type != carrierType
            || function.Variables[variable].NativeFixedArrayLayout is not BoundNativeFixedArrayLayout layout
            || layout.CarrierType != carrierType
            || !NativeFixedArrayContract.MatchesLayout(layout, program.Structures, program.NativeImportCatalog)
            || (!isReadonly && (!function.Variables[variable].IsMutable
                || !NativeFixedArrayContract.AllowsMutableLocal(layout,
                    program.Structures, program.NativeImportCatalog)))
            || index is null
            || (index.Type != PrimitiveType.Int32
                && !(index.Type == PrimitiveType.UInt32
                    && NativeFixedArrayContract.AllowsUInt32Indices(
                        layout, program.Structures, program.NativeImportCatalog)))
            || function.IsNoThrow
                && program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
                && index is not BoundConstant
            || (requiredStartIndex is int required
                && index is BoundConstant { Type: PrimitiveType.Int32, Value: var actual }
                && actual != required)
            || (requiredStartIndex is null && index is BoundConstant constant
                && (constant.Value < 0 || constant.Value >= layout.ElementCount)))
        {
            throw InvalidEffect(index?.Span ?? default, "NativeImport");
        }
        Expression(index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }
}
