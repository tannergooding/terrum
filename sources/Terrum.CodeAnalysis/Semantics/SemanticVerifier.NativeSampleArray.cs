// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyNativeFixedArrayInitialization(BoundExpression expression, int variable,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (expression is not BoundNativeFixedArrayInitialization initialization
            || !NativeFixedArrayContract.MatchesProgram(program)
            || initialization.Type != initialization.Layout.CarrierType
            || variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || variable >= function.Variables.Length
            || function.Variables[variable].Type != initialization.Layout.CarrierType
            || !NativeFixedArrayContract.IsCarrier(initialization.Layout.CarrierType)
            || initialization.Elements.IsDefault
            || (initialization.Elements.Length != initialization.Layout.ElementCount
                && !(initialization.Elements.IsEmpty
                    && NativeFixedArrayContract.AllowsEmptyInitialization(initialization.Layout,
                        program.Structures, program.NativeImportCatalog)))
            || function.Variables[variable].NativeFixedArrayLayout != initialization.Layout
            || !NativeFixedArrayContract.MatchesLayout(initialization.Layout, program.Structures,
                program.NativeImportCatalog))
        {
            throw InvalidEffect(expression.Span, "NativeImport");
        }
        foreach (var element in initialization.Elements)
        {
            if (element is null || element.Type != initialization.Layout.ElementType)
            {
                throw InvalidEffect(initialization.Span, "NativeImport");
            }
            Expression(element, initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures);
        }
    }

    private static void VerifyNativeFixedArrayElement(BoundNativeFixedArrayElement element,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || function.IsExternal || function.IndexedBody is not null
            || element.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || element.Variable >= function.Variables.Length
            || !declarations.Contains(element.Variable) || !initialized[element.Variable]
            || !NativeFixedArrayContract.IsCarrier(element.Layout.CarrierType)
            || function.Variables[element.Variable].Type != element.Layout.CarrierType
            || (function.Variables[element.Variable].IsMutable
                && !NativeFixedArrayContract.AllowsMutableLocal(element.Layout,
                    program.Structures, program.NativeImportCatalog))
            || function.Variables[element.Variable].NativeFixedArrayLayout != element.Layout
            || !NativeFixedArrayContract.MatchesLayout(element.Layout, program.Structures,
                program.NativeImportCatalog)
            || element.Type != element.Layout.ElementType || element.Index is null
            || (element.Index.Type != PrimitiveType.Int32
                && !(element.Index.Type == PrimitiveType.UInt32
                    && NativeFixedArrayContract.AllowsUInt32Indices(element.Layout,
                        program.Structures, program.NativeImportCatalog)))
            || (function.IsNoThrow
                && program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
                && element.Index is not BoundConstant)
            || (element.Index is BoundConstant index
                && (index.Value < 0 || index.Value >= element.Layout.ElementCount)))
        {
            throw InvalidEffect(element.Span, "NativeImport");
        }
        Expression(element.Index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }

    private static void VerifyNativeFixedArrayPointerAddress(BoundNativeFixedArrayPointerAddress address,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (!address.IsUnsafeAuthorized
            || !NativePointerTypeContract.IsValidType(address.Type, program.NativePointerTypes)
            || !NativeFixedArrayContract.MatchesProgram(program)
            || function.IsExternal || function.IndexedBody is not null
            || address.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || address.Variable >= function.Variables.Length
            || !declarations.Contains(address.Variable) || !initialized[address.Variable]
            || function.Variables[address.Variable].Type != address.Layout.CarrierType
            || function.Variables[address.Variable].NativeFixedArrayLayout != address.Layout
            || !NativeFixedArrayContract.MatchesLayout(address.Layout, program.Structures,
                program.NativeImportCatalog)
            || (function.Variables[address.Variable].IsMutable
                && !NativeFixedArrayContract.AllowsMutableLocal(address.Layout,
                    program.Structures, program.NativeImportCatalog))
            || address.Index is null
            || (address.Index.Type != PrimitiveType.Int32
                && !(address.Index.Type == PrimitiveType.UInt32
                    && NativeFixedArrayContract.AllowsUInt32Indices(address.Layout,
                        program.Structures, program.NativeImportCatalog)))
            || (function.IsNoThrow
                && program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
                && address.Index is not BoundConstant)
            || (address.Index is BoundConstant index
                && (index.Value < 0 || index.Value >= address.Layout.ElementCount)))
        {
            throw InvalidEffect(address.Span, "NativePointer");
        }

        var pointer = program.NativePointerTypes[NativePointerTypes.Index(address.Type)];
        if (pointer.PointeeType != address.Layout.ElementType
            || pointer.IsWritable && (!function.Variables[address.Variable].IsMutable
                || !NativeFixedArrayContract.AllowsMutableLocal(address.Layout,
                    program.Structures, program.NativeImportCatalog)))
        {
            throw InvalidEffect(address.Span, "NativePointer");
        }

        Expression(address.Index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }

    private static void VerifyNativeFixedArrayWrite(BoundNativeFixedArrayWrite write,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || function.IsExternal || function.IndexedBody is not null
            || write.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || write.Variable >= function.Variables.Length
            || !declarations.Contains(write.Variable) || !initialized[write.Variable]
            || !NativeFixedArrayContract.AllowsElementWrites(write.Layout,
                program.Structures, program.NativeImportCatalog)
            || !function.Variables[write.Variable].IsMutable
            || !NativeFixedArrayContract.AllowsMutableLocal(write.Layout,
                program.Structures, program.NativeImportCatalog)
            || function.Variables[write.Variable].Type != write.Layout.CarrierType
            || function.Variables[write.Variable].NativeFixedArrayLayout != write.Layout
            || !NativeFixedArrayContract.MatchesLayout(write.Layout, program.Structures,
                program.NativeImportCatalog)
            || write.Value.Type != write.Layout.ElementType
            || (write.Index.Type != PrimitiveType.Int32
                && !(write.Index.Type == PrimitiveType.UInt32
                    && NativeFixedArrayContract.AllowsUInt32Indices(write.Layout,
                        program.Structures, program.NativeImportCatalog)))
            || (function.IsNoThrow
                && program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
                && write.Index is not BoundConstant)
            || (write.Index is BoundConstant index
                && (index.Value < 0 || index.Value >= write.Layout.ElementCount)))
        {
            throw InvalidEffect(write.Span, "NativeImport");
        }
        Expression(write.Index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
        Expression(write.Value, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }

}
