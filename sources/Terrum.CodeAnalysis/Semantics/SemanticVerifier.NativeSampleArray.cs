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
        if (expression is BoundRead or BoundMove)
        {
            if ((uint)variable >= (uint)function.Variables.Length
                || variable < function.ParameterCount
                    + (function.IsConstructor ? 1 : 0))
            {
                throw InvalidEffect(expression.Span, "NativeImport");
            }

            var copyType = function.Variables[variable].Type;
            if (!NativeFixedArrayContract.TryGetLayout(
                    copyType,
                    program.Structures, program.NativeFixedArrayTypes,
                    out var copyLayout)
                || !NativeFixedArrayContract.MatchesLayout(copyLayout,
                    program)
                || expression.Type != copyLayout.CarrierType
                || copyType != copyLayout.CarrierType)
            {
                throw InvalidEffect(expression.Span, "NativeImport");
            }

            Dictionary<StorageProjection, NativePointerFieldValue>? copyPointerValues
                = null;
            if (CarriesNativePointer(expression.Type, program)
                && initialized.Dependencies is BackingDependencies copyFacts)
            {
                copyPointerValues = NativePointerFieldValueSnapshotOfExpression(
                    expression, expression.Type, copyFacts, function, program,
                    ref remaining, depth + 1);
            }

            var copyCompletes = true;
            if (expression is BoundRead copyRead)
            {
                if (IsUnique(program, copyRead.Type))
                {
                    throw InvalidEffect(copyRead.Span, "Ownership");
                }
                if (copyRead.Variable < 0
                    || copyRead.Variable >= initialized.Length
                    || !initialized[copyRead.Variable]
                    || copyRead.Type
                        != function.Variables[copyRead.Variable].Type)
                {
                    throw Invalid(copyRead.Span);
                }
            }
            else
            {
                copyCompletes = EvaluateExpression(expression, initialized,
                    fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
            }
            if (copyCompletes && copyPointerValues is not null
                && initialized.Dependencies is BackingDependencies targetFacts)
            {
                targetFacts.ClearNativePointerFieldValues(variable,
                    StorageProjection.WholeObject);
                foreach (var (projection, value) in copyPointerValues)
                {
                    Count(ref remaining, depth + 1);
                    targetFacts.SetNativePointerFieldValue(variable,
                        projection, value.Sources, value.MayBeUnknown,
                        new NativePointerExtentFact(value.ExtentSources,
                            value.ExtentMayBeOpaque,
                            value.ExtentUnavailable));
                }
                RefreshNativePointerAggregateProvenance(variable,
                    expression.Type, targetFacts, program, ref remaining,
                    depth + 1);
            }
            return;
        }

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
                var elementProjection = StorageProjection.ForArrayIndex(
                    (ulong)index);
                evaluationContinues = element is BoundNativeFixedArrayInitialization nested
                    ? EvaluateNestedNativeFixedArrayInitialization(nested,
                        variable, elementProjection, initialized, fieldMasks,
                        declarations, function, program, true, ref remaining,
                        depth + 1, failures)
                    : EvaluateExpression(element, initialized, fieldMasks,
                        declarations, function, program, ref remaining,
                        depth + 1, failures);
                if (evaluationContinues
                    && element is not BoundNativeFixedArrayInitialization
                    && CarriesNativePointer(initialization.Layout.ElementType, program)
                    && initialized.Dependencies is BackingDependencies facts)
                {
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
                var elementProjection = StorageProjection.ForArrayIndex(
                    (ulong)index);
                if (element is BoundNativeFixedArrayInitialization nested)
                {
                    _ = EvaluateNestedNativeFixedArrayInitialization(nested,
                        variable, elementProjection, unreachableState,
                        unreachableMasks, declarations, function, program,
                        false, ref remaining, depth + 1, null);
                }
                else
                {
                    _ = EvaluateExpression(element, unreachableState,
                        unreachableMasks, declarations, function, program,
                        ref remaining, depth + 1, null);
                }
            }
        }
        if (evaluationContinues
            && CarriesNativePointer(initialization.Type, program)
            && initialized.Dependencies is BackingDependencies pointerFacts)
        {
            RefreshNativePointerAggregateProvenance(variable,
                initialization.Type, pointerFacts, program, ref remaining,
                depth + 1);
        }
    }

    private static bool EvaluateNestedNativeFixedArrayInitialization(
        BoundNativeFixedArrayInitialization initialization,
        int variable, StorageProjection destinationPrefix,
        InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        bool trackPointerFacts, ref int remaining, int depth,
        FailureEdges? failures)
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
                : (ulong)initialization.Elements.Length != layout.ElementCount))
        {
            throw InvalidEffect(initialization.Span, "NativeImport");
        }

        var evaluationContinues = true;
        for (var index = 0; index < initialization.Elements.Length; index++)
        {
            var element = initialization.Elements[index];
            if (element is null || element.Type != layout.ElementType)
            {
                throw InvalidEffect(element?.Span ?? initialization.Span,
                    "NativeImport");
            }
            var elementProjection = StorageProjection.Combine(
                destinationPrefix, StorageProjection.ForArrayIndex(
                    (ulong)index));
            if (evaluationContinues)
            {
                evaluationContinues = element
                    is BoundNativeFixedArrayInitialization nested
                        ? EvaluateNestedNativeFixedArrayInitialization(nested,
                            variable, elementProjection, initialized,
                            fieldMasks, declarations, function, program,
                            trackPointerFacts, ref remaining, depth + 1,
                            failures)
                        : EvaluateExpression(element, initialized, fieldMasks,
                            declarations, function, program, ref remaining,
                            depth + 1, failures);
                if (evaluationContinues
                    && trackPointerFacts
                    && element is not BoundNativeFixedArrayInitialization
                    && CarriesNativePointer(layout.ElementType, program)
                    && initialized.Dependencies is BackingDependencies facts)
                {
                    SetNativePointerFieldValuesFromExpression(variable,
                        elementProjection, layout.ElementType, element, facts,
                        function, program, ref remaining, depth + 1);
                }
            }
            else
            {
                var unreachableState = initialized.Clone();
                var unreachableMasks = (uint[])fieldMasks.Clone();
                if (element is BoundNativeFixedArrayInitialization nested)
                {
                    _ = EvaluateNestedNativeFixedArrayInitialization(nested,
                        variable, elementProjection, unreachableState,
                        unreachableMasks, declarations, function, program,
                        false, ref remaining, depth + 1, null);
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
            || element.Type != leafLayout.ElementType)
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
            || !NativeFixedArrayContract.MatchesPath(
                function.Variables[variable].Type, projections, program,
                out _, out leafLayout))
        {
            return false;
        }

        return leafLayout == projections[^1].Layout;
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
                    write.Variable,
                    StorageProjection.ForArrayElement(write.Index,
                        write.Layout.ElementCount),
                    unreachableState, unreachableMasks, declarations, function,
                    program, false, ref remaining, depth + 1, null);
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
                    write.Variable,
                    StorageProjection.ForArrayElement(write.Index,
                        write.Layout.ElementCount),
                    initialized, fieldMasks, declarations, function, program,
                    false, ref remaining, depth + 1, failures)
                : EvaluateExpression(write.Value, initialized, fieldMasks,
                    declarations, function, program, ref remaining,
                    depth + 1, failures);
        if (valueCompletes
            && CarriesNativePointer(write.Layout.ElementType, program)
            && initialized.Dependencies is BackingDependencies facts)
        {
            ApplyNativeFixedArrayWriteProvenance(write, facts, function,
                program, ref remaining, depth + 1);
            RefreshNativePointerAggregateProvenance(write.Variable,
                function.Variables[write.Variable].Type, facts, program,
                ref remaining, depth + 1);
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
            || write.Value.Type != leafLayout.ElementType)
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
                    write.Variable,
                    StorageProjection.ForNativeFixedArrayPath(
                        write.Projections),
                    unreachableState, unreachableMasks, declarations,
                    function, program, false, ref remaining, depth + 1, null);
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
                    write.Variable,
                    StorageProjection.ForNativeFixedArrayPath(
                        write.Projections),
                    initialized, fieldMasks, declarations, function, program,
                    false, ref remaining, depth + 1, failures)
                : EvaluateExpression(write.Value, initialized, fieldMasks,
                    declarations, function, program, ref remaining,
                    depth + 1, failures);
        if (valueCompletes
            && CarriesNativePointer(leafLayout.ElementType, program)
            && initialized.Dependencies is BackingDependencies facts)
        {
            ApplyNativeFixedArrayPathWriteProvenance(write, leafLayout,
                facts, function, program, ref remaining, depth + 1);
            RefreshNativePointerAggregateProvenance(write.Variable,
                function.Variables[write.Variable].Type, facts, program,
                ref remaining, depth + 1);
        }
    }
}
