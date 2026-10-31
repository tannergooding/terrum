// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;

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
            || ((ulong)initialization.Elements.Length != initialization.Layout.ElementCount
                && !(initialization.Elements.IsEmpty
                    && NativeFixedArrayContract.AllowsEmptyInitialization(initialization.Layout)))
            || !NativeFixedArrayContract.MatchesType(
                function.Variables[variable].Type, initialization.Layout, program)
            || !NativeFixedArrayContract.MatchesLayout(initialization.Layout, program))
        {
            throw InvalidEffect(expression.Span, "NativeImport");
        }
        var evaluationContinues = true;
        for (var index = 0; index < initialization.Elements.Length; index++)
        {
            var element = initialization.Elements[index];
            if (element is null || element.Type != initialization.Layout.ElementType)
            {
                throw InvalidEffect(initialization.Span, "NativeImport");
            }
            if (evaluationContinues)
            {
                evaluationContinues = element is BoundNativeFixedArrayInitialization nested
                    ? EvaluateNestedNativeFixedArrayInitialization(nested,
                        initialized, fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures)
                    : EvaluateExpression(element, initialized, fieldMasks,
                        declarations, function, program, ref remaining,
                        depth + 1, failures);
                if (evaluationContinues
                    && CarriesNativePointer(initialization.Layout.ElementType, program)
                    && initialized.Dependencies is BackingDependencies facts)
                {
                    var elementProjection = StorageProjection.ForArrayElement(
                        new BoundConstant(index, PrimitiveType.Int32, element.Span));
                    SetNativePointerFieldValuesFromExpression(variable,
                        elementProjection, initialization.Layout.ElementType,
                        element, facts, function, program, ref remaining,
                        depth + 1);
                }
            }
            else
            {
                var unreachableState = initialized.Clone();
                var unreachableMasks = (uint[])fieldMasks.Clone();
                if (element is BoundNativeFixedArrayInitialization nested)
                {
                    _ = EvaluateNestedNativeFixedArrayInitialization(nested,
                        unreachableState, unreachableMasks, declarations,
                        function, program, ref remaining, depth + 1, null);
                }
                else
                {
                    _ = EvaluateExpression(element, unreachableState,
                        unreachableMasks, declarations, function, program,
                        ref remaining, depth + 1, null);
                }
            }
        }
    }

    private static bool EvaluateNestedNativeFixedArrayInitialization(
        BoundNativeFixedArrayInitialization initialization,
        InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var layout = initialization.Layout;
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || initialization.Type != layout.CarrierType
            || !NativeFixedArrayContract.IsCarrier(layout.CarrierType)
            || !NativeFixedArrayContract.MatchesLayout(layout, program)
            || initialization.Elements.IsDefault
            || (initialization.Elements.IsEmpty
                ? !NativeFixedArrayContract.AllowsEmptyInitialization(layout)
                : (ulong)initialization.Elements.Length != layout.ElementCount)
            || CarriesNativePointer(layout.CarrierType, program))
        {
            throw InvalidEffect(initialization.Span, "NativeImport");
        }

        var evaluationContinues = true;
        foreach (var element in initialization.Elements)
        {
            if (element is null || element.Type != layout.ElementType)
            {
                throw InvalidEffect(element?.Span ?? initialization.Span,
                    "NativeImport");
            }
            if (evaluationContinues)
            {
                evaluationContinues = element
                    is BoundNativeFixedArrayInitialization nested
                        ? EvaluateNestedNativeFixedArrayInitialization(nested,
                            initialized, fieldMasks, declarations, function,
                            program, ref remaining, depth + 1, failures)
                        : EvaluateExpression(element, initialized, fieldMasks,
                            declarations, function, program, ref remaining,
                            depth + 1, failures);
            }
            else
            {
                var unreachableState = initialized.Clone();
                var unreachableMasks = (uint[])fieldMasks.Clone();
                if (element is BoundNativeFixedArrayInitialization nested)
                {
                    _ = EvaluateNestedNativeFixedArrayInitialization(nested,
                        unreachableState, unreachableMasks, declarations,
                        function, program, ref remaining, depth + 1, null);
                }
                else
                {
                    _ = EvaluateExpression(element, unreachableState,
                        unreachableMasks, declarations, function, program,
                        ref remaining, depth + 1, null);
                }
            }
        }

        return evaluationContinues;
    }

    private static void VerifyNativeFixedArrayElement(BoundNativeFixedArrayElement element,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (element.Index is not null && RequiresNoFailureProof(function)
            && program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
            && element.Index is not BoundConstant)
        {
            throw InvalidNoFailureEffect(function, element.Index.Span);
        }
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || function.IsExternal || function.IndexedBody is not null
            || element.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || element.Variable >= function.Variables.Length
            || !declarations.Contains(element.Variable) || !initialized[element.Variable]
            || !NativeFixedArrayContract.IsCarrier(element.Layout.CarrierType)
            || function.Variables[element.Variable].Type != element.Layout.CarrierType
            || (function.Variables[element.Variable].IsMutable
                && !NativeFixedArrayContract.AllowsMutableLocal(element.Layout))
            || !NativeFixedArrayContract.MatchesType(
                function.Variables[element.Variable].Type, element.Layout, program)
            || !NativeFixedArrayContract.MatchesLayout(element.Layout, program)
            || element.Type != element.Layout.ElementType || element.Index is null
            || !NativeFixedArrayContract.IsIndexType(element.Index.Type)
            || (element.Index is BoundConstant index
                && !NativeFixedArrayContract.IsConstantIndexInRange(index,
                    element.Layout.ElementCount)))
        {
            throw InvalidEffect(element.Span, "NativeImport");
        }
        _ = EvaluateExpression(element.Index, initialized, fieldMasks, declarations, function,
            program, ref remaining, depth + 1, failures);
    }

    private static void VerifyNativeFixedArrayPathElement(
        BoundNativeFixedArrayPathElement element,
        InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (!IsValidNativeFixedArrayPath(element.Variable,
                element.Projections, function, program, out var leafLayout)
            || !declarations.Contains(element.Variable)
            || !initialized[element.Variable]
            || element.Type != leafLayout.ElementType
            || CarriesNativePointer(element.Type, program))
        {
            throw InvalidEffect(element.Span, "NativeImport");
        }

        _ = EvaluateNativeFixedArrayPathIndices(element.Projections, initialized,
            fieldMasks, declarations, function, program, ref remaining,
            depth + 1, failures);
    }

    private static bool IsValidNativeFixedArrayPath(int variable,
        ImmutableArray<BoundNativeFixedArrayProjection> projections,
        BoundFunction function, BoundProgram program,
        out BoundNativeFixedArrayLayout leafLayout)
    {
        leafLayout = default;
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || function.IsExternal || function.IndexedBody is not null
            || variable < function.ParameterCount
                + (function.IsConstructor ? 1 : 0)
            || variable >= function.Variables.Length
            || !NativeFixedArrayContract.IsCarrier(
                function.Variables[variable].Type)
            || !NativeFixedArrayContract.TryGetLayout(
                function.Variables[variable].Type, program.Structures,
                program.NativeFixedArrayTypes, out var rootLayout)
            || function.Variables[variable].Type != rootLayout.CarrierType
            || projections.IsDefaultOrEmpty
            || projections.Length < 2
            || projections[0].Layout != rootLayout
            || !NativeFixedArrayContract.MatchesLayout(rootLayout, program))
        {
            return false;
        }

        var expectedCarrier = rootLayout.CarrierType;
        foreach (var projection in projections)
        {
            if (projection.Index is null
                || projection.Layout.CarrierType != expectedCarrier
                || !NativeFixedArrayContract.MatchesLayout(
                    projection.Layout, program)
                || !NativeFixedArrayContract.IsIndexType(
                    projection.Index.Type)
                || (projection.Index is BoundConstant
                    && !NativeFixedArrayContract.IsConstantIndexInRange(
                        projection.Index,
                        projection.Layout.ElementCount)))
            {
                return false;
            }
            expectedCarrier = projection.Layout.ElementType;
        }

        leafLayout = projections[^1].Layout;
        return true;
    }

    private static bool EvaluateNativeFixedArrayPathIndices(
        ImmutableArray<BoundNativeFixedArrayProjection> projections,
        InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures)
    {
        var evaluationContinues = true;
        foreach (var projection in projections)
        {
            if (RequiresNoFailureProof(function)
                && program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
                && projection.Index is not BoundConstant)
            {
                throw InvalidNoFailureEffect(function, projection.Index.Span);
            }
            if (evaluationContinues)
            {
                evaluationContinues = EvaluateExpression(projection.Index,
                    initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
            }
            else
            {
                var unreachableState = initialized.Clone();
                var unreachableMasks = (uint[])fieldMasks.Clone();
                _ = EvaluateExpression(projection.Index, unreachableState,
                    unreachableMasks, declarations, function, program,
                    ref remaining, depth + 1, null);
            }
        }

        return evaluationContinues;
    }

    private static void VerifyNativeFixedArrayPointerAddress(BoundNativeFixedArrayPointerAddress address,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var nestedPath = !address.Projections.IsDefaultOrEmpty;
        var rootLayout = nestedPath
            ? address.Projections[0].Layout : address.Layout;
        var leafLayout = nestedPath
            ? address.Projections[^1].Layout : address.Layout;
        if (!address.IsUnsafeAuthorized
            || !NativePointerTypeContract.IsValidType(address.Type,
                program.NativePointerTypes)
            || !NativeFixedArrayContract.MatchesProgram(program)
            || function.IsExternal || function.IndexedBody is not null
            || address.Variable < function.ParameterCount
                + (function.IsConstructor ? 1 : 0)
            || address.Variable >= function.Variables.Length
            || !declarations.Contains(address.Variable)
            || !initialized[address.Variable]
            || function.Variables[address.Variable].Type
                != rootLayout.CarrierType
            || !NativeFixedArrayContract.MatchesType(
                function.Variables[address.Variable].Type, rootLayout, program)
            || !NativeFixedArrayContract.MatchesLayout(rootLayout, program)
            || !NativeFixedArrayContract.MatchesLayout(leafLayout, program)
            || leafLayout != address.Layout
            || leafLayout.ByteExtent == 0
            || (function.Variables[address.Variable].IsMutable
                && !NativeFixedArrayContract.AllowsMutableLocal(rootLayout))
            || address.Index is null
            || address.Index != (nestedPath
                ? address.Projections[^1].Index : address.Index)
            || (nestedPath
                ? !IsValidNativeFixedArrayPath(address.Variable,
                    address.Projections, function, program, out _)
                    || CarriesNativePointer(leafLayout.ElementType, program)
                : !NativeFixedArrayContract.IsIndexType(address.Index.Type)
                    || (address.Index is BoundConstant index
                        && !NativeFixedArrayContract.IsConstantIndexInRange(
                            index, leafLayout.ElementCount))))
        {
            throw InvalidEffect(address.Span, "NativePointer");
        }

        var pointer = program.NativePointerTypes[
            NativePointerTypes.Index(address.Type)];
        if (pointer.PointeeType != leafLayout.ElementType
            || (pointer.IsWritable && (!function.Variables[address.Variable].IsMutable
                || !NativeFixedArrayContract.AllowsMutableLocal(rootLayout))))
        {
            throw InvalidEffect(address.Span, "NativePointer");
        }

        if (nestedPath)
        {
            _ = EvaluateNativeFixedArrayPathIndices(address.Projections,
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures);
        }
        else
        {
            if (RequiresNoFailureProof(function)
                && program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
                && address.Index is not BoundConstant)
            {
                throw InvalidNoFailureEffect(function, address.Index.Span);
            }
            _ = EvaluateExpression(address.Index, initialized, fieldMasks,
                declarations, function, program, ref remaining, depth + 1,
                failures);
        }
    }

    private static void VerifyNativeFixedArrayWrite(BoundNativeFixedArrayWrite write,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (RequiresNoFailureProof(function)
            && program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
            && write.Index is not BoundConstant)
        {
            throw InvalidNoFailureEffect(function, write.Index.Span);
        }
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || function.IsExternal || function.IndexedBody is not null
            || write.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || write.Variable >= function.Variables.Length
            || !declarations.Contains(write.Variable) || !initialized[write.Variable]
            || !NativeFixedArrayContract.AllowsElementWrites(write.Layout)
            || !function.Variables[write.Variable].IsMutable
            || !NativeFixedArrayContract.AllowsMutableLocal(write.Layout)
            || function.Variables[write.Variable].Type != write.Layout.CarrierType
            || !NativeFixedArrayContract.MatchesType(
                function.Variables[write.Variable].Type, write.Layout, program)
            || !NativeFixedArrayContract.MatchesLayout(write.Layout, program)
            || write.Value.Type != write.Layout.ElementType
            || (NativeFixedArrayTypes.IsType(write.Layout.ElementType)
                && CarriesNativePointer(write.Layout.ElementType, program))
            || !NativeFixedArrayContract.IsIndexType(write.Index.Type)
            || (write.Index is BoundConstant index
                && !NativeFixedArrayContract.IsConstantIndexInRange(index,
                    write.Layout.ElementCount)))
        {
            throw InvalidEffect(write.Span, "NativeImport");
        }
        if (!EvaluateExpression(write.Index, initialized, fieldMasks, declarations, function,
            program, ref remaining, depth + 1, failures))
        {
            if (write.Value is BoundNativeFixedArrayInitialization nested)
            {
                var unreachableState = initialized.Clone();
                var unreachableMasks = (uint[])fieldMasks.Clone();
                _ = EvaluateNestedNativeFixedArrayInitialization(nested,
                    unreachableState, unreachableMasks, declarations, function,
                    program, ref remaining, depth + 1, null);
            }
            else
            {
                ValidateUnreachableExpression(write.Value, initialized,
                    fieldMasks, declarations, function, program,
                    ref remaining, depth + 1);
            }
            return;
        }
        var valueCompletes = write.Value
            is BoundNativeFixedArrayInitialization nestedValue
                ? EvaluateNestedNativeFixedArrayInitialization(nestedValue,
                    initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures)
                : EvaluateExpression(write.Value, initialized, fieldMasks,
                    declarations, function, program, ref remaining,
                    depth + 1, failures);
        if (valueCompletes
            && CarriesNativePointer(write.Layout.ElementType, program)
            && initialized.Dependencies is BackingDependencies facts)
        {
            ApplyNativeFixedArrayWriteProvenance(write, facts, function,
                program, ref remaining, depth + 1);
        }
    }

    private static void VerifyNativeFixedArrayPathWrite(
        BoundNativeFixedArrayPathWrite write, InitializationState initialized,
        uint[] fieldMasks, HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (!IsValidNativeFixedArrayPath(write.Variable,
                write.Projections, function, program, out var leafLayout)
            || !function.Variables[write.Variable].IsMutable
            || !NativeFixedArrayContract.AllowsMutableLocal(
                write.Projections[0].Layout)
            || !declarations.Contains(write.Variable)
            || !initialized[write.Variable]
            || write.Value.Type != leafLayout.ElementType
            || CarriesNativePointer(leafLayout.ElementType, program))
        {
            throw InvalidEffect(write.Span, "NativeImport");
        }

        if (!EvaluateNativeFixedArrayPathIndices(write.Projections, initialized,
            fieldMasks, declarations, function, program, ref remaining,
            depth + 1, failures))
        {
            if (write.Value is BoundNativeFixedArrayInitialization nested)
            {
                var unreachableState = initialized.Clone();
                var unreachableMasks = (uint[])fieldMasks.Clone();
                _ = EvaluateNestedNativeFixedArrayInitialization(nested,
                    unreachableState, unreachableMasks, declarations,
                    function, program, ref remaining, depth + 1, null);
            }
            else
            {
                ValidateUnreachableExpression(write.Value, initialized,
                    fieldMasks, declarations, function, program,
                    ref remaining, depth + 1);
            }
            return;
        }
        var valueCompletes = write.Value
            is BoundNativeFixedArrayInitialization nestedValue
                ? EvaluateNestedNativeFixedArrayInitialization(nestedValue,
                    initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures)
                : EvaluateExpression(write.Value, initialized, fieldMasks,
                    declarations, function, program, ref remaining,
                    depth + 1, failures);
        if (valueCompletes
            && CarriesNativePointer(leafLayout.ElementType, program))
        {
            throw InvalidEffect(write.Span, "NativeImport");
        }
    }
}
