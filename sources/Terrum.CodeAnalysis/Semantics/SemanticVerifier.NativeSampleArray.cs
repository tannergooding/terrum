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
            var sourceVariable = expression switch {
                BoundRead read => read.Variable,
                BoundMove move => move.Variable,
                _ => -1,
            };
            if (sourceVariable < 0)
            {
                throw Invalid(expression.Span);
            }
            _ = initialized.CopyRawUnionArrayFacts(variable,
                sourceVariable);
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
        initialized.ClearRawUnionArrayFacts(variable);
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
                    && CarriesRawUnionPayloadFacts(
                        program, initialization.Layout.ElementType))
                {
                    initialized.SetRawUnionArrayElementBytes(variable,
                        StorageProjection.WholeObject,
                        initialization.Layout, (ulong)index,
                        InitializedPayloadBytesOfExpression(
                            element, initialization.Layout.ElementType,
                            initialized, function, program));
                }
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

    private static bool VerifyNativeFixedArrayFieldCopy(
        BoundExpression expression, int destinationVariable,
        StorageProjection destinationPrefix, PrimitiveType arrayType,
        InitializationState initialized, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining,
        int depth)
    {
        Count(ref remaining, depth);
        if (expression is not BoundRead read
            || read.Type != arrayType
            || (uint)read.Variable >= (uint)function.Variables.Length
            || read.Variable == destinationVariable
            || !declarations.Contains(read.Variable)
            || !initialized[read.Variable]
            || function.Variables[read.Variable].Type != arrayType
            || !NativeFixedArrayContract.TryGetLayout(arrayType,
                program.Structures, program.NativeFixedArrayTypes,
                out var layout)
            || !NativeFixedArrayContract.MatchesLayout(layout, program)
            || !NativeFixedArrayContract.IsRootlessCopyFieldType(arrayType,
                program.Structures, program.NativeFixedArrayTypes))
        {
            throw InvalidEffect(expression?.Span ?? default,
                "NativeImport");
        }

        _ = initialized.CopyRawUnionArrayFacts(destinationVariable,
            destinationPrefix, read.Variable,
            StorageProjection.WholeObject);
        return true;
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
                    && CarriesRawUnionPayloadFacts(program,
                        layout.ElementType))
                {
                    initialized.SetRawUnionArrayElementBytes(variable,
                        destinationPrefix, layout, (ulong)index,
                        InitializedPayloadBytesOfExpression(element,
                            layout.ElementType, initialized, function,
                            program));
                }
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
        var hasFieldPlace = element.FieldPlace is not null;
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
            || !declarations.Contains(element.Variable)
            || (!hasFieldPlace
                && (!initialized[element.Variable]
                    || function.Variables[element.Variable].Type
                        != element.Layout.CarrierType))
            || !TryGetNativeFixedArrayArrayPrefix(element.Variable,
                element.FieldPlace, element.Layout, function, program,
                out _)
            || !NativeFixedArrayContract.IsCarrier(element.Layout.CarrierType)
            || (!hasFieldPlace && function.Variables[element.Variable].IsMutable
                && !NativeFixedArrayContract.AllowsMutableLocal(element.Layout))
            || !NativeFixedArrayContract.MatchesType(
                element.Layout.CarrierType, element.Layout, program)
            || !NativeFixedArrayContract.MatchesLayout(element.Layout, program)
            || element.Type != element.Layout.ElementType || element.Index is null
            || !NativeFixedArrayContract.IsIndexType(element.Index.Type)
            || (element.Index is BoundConstant index
                && !NativeFixedArrayContract.IsConstantIndexInRange(index,
                    element.Layout.ElementCount)))
        {
            throw InvalidEffect(element.Span, "NativeImport");
        }
        if (element.FieldPlace is not null
            && !EvaluateExpression(element.FieldPlace, initialized,
                fieldMasks, declarations, function, program, ref remaining,
                depth + 1, failures))
        {
            ValidateUnreachableExpression(element.Index, initialized,
                fieldMasks, declarations, function, program,
                ref remaining, depth + 1);
            return;
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
                element.Projections, function, program, out var leafLayout,
                element.FieldPlace)
            || !declarations.Contains(element.Variable)
            || (element.FieldPlace is null
                && !initialized[element.Variable])
            || element.Type != leafLayout.ElementType)
        {
            throw InvalidEffect(element.Span, "NativeImport");
        }

        if (element.FieldPlace is not null
            && !EvaluateExpression(element.FieldPlace, initialized,
                fieldMasks, declarations, function, program, ref remaining,
                depth + 1, failures))
        {
            foreach (var projection in element.Projections)
            {
                ValidateUnreachableExpression(projection.Index,
                    initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1);
            }
            return;
        }

        _ = EvaluateNativeFixedArrayPathIndices(element.Projections, initialized,
            fieldMasks, declarations, function, program, ref remaining,
            depth + 1, failures);
    }

    private static bool IsValidNativeFixedArrayPath(int variable,
        ImmutableArray<BoundNativeFixedArrayProjection> projections,
        BoundFunction function, BoundProgram program,
        out BoundNativeFixedArrayLayout leafLayout,
        BoundExpression? fieldPlace = null)
    {
        leafLayout = default;
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || function.IsExternal || function.IndexedBody is not null
            || variable < function.ParameterCount
                + (function.IsConstructor ? 1 : 0)
            || variable >= function.Variables.Length
            || !NativeFixedArrayContract.IsCarrier(fieldPlace?.Type
                ?? function.Variables[variable].Type)
            || (fieldPlace is not null
                && (projections.IsDefaultOrEmpty
                    || !TryGetNativeFixedArrayArrayPrefix(variable,
                        fieldPlace, projections[0].Layout, function, program,
                        out _)))
            || (fieldPlace is null
                && !NativeFixedArrayContract.MatchesPath(
                    function.Variables[variable].Type, projections, program,
                    out _, out leafLayout))
            || (fieldPlace is not null
                && !NativeFixedArrayContract.MatchesPath(fieldPlace.Type,
                    projections, program,
                    out _, out leafLayout)))
        {
            return false;
        }

        return leafLayout == projections[^1].Layout;
    }

    private static bool TryGetNativeFixedArrayArrayPrefix(int variable,
        BoundExpression? fieldPlace, BoundNativeFixedArrayLayout layout,
        BoundFunction function, BoundProgram program,
        out StorageProjection arrayPrefix)
    {
        arrayPrefix = StorageProjection.WholeObject;
        if ((uint)variable >= (uint)function.Variables.Length
            || !NativeFixedArrayContract.MatchesType(
                layout.CarrierType, layout, program)
            || !NativeFixedArrayContract.MatchesLayout(layout, program))
        {
            return false;
        }

        if (fieldPlace is null)
        {
            return function.Variables[variable].Type
                == layout.CarrierType;
        }

        if (fieldPlace is not BoundFieldRead place
            || place.Type != layout.CarrierType
            || !TryGetFieldProjectionPath(place, function, program,
                out var rootVariable, out var fieldPath)
            || rootVariable != variable
            || fieldPath.Segments.IsEmpty
            || !NativeFixedArrayContract.IsRootlessCopyFieldType(
                function.Variables[rootVariable].Type,
                program.Structures, program.NativeFixedArrayTypes))
        {
            return false;
        }

        var firstLocal = function.ParameterCount
            + (function.IsConstructor
                || (function.DeclaringStruct is not null
                    && !function.IsStatic) ? 1 : 0);
        if (rootVariable < firstLocal)
        {
            return false;
        }

        var ownerType = function.Variables[rootVariable].Type;
        foreach (var segment in fieldPath.Segments)
        {
            if (segment.OwnerType != ownerType
                || IsUnique(program, ownerType)
                || !IsInlineStructStorage(ownerType, program)
                || !HasFields(program, ownerType)
                || (uint)segment.Field
                    >= (uint)Fields(program, ownerType).Length)
            {
                return false;
            }
            ownerType = Fields(program, ownerType)[segment.Field].Type;
        }

        if (ownerType != place.Type)
        {
            return false;
        }

        arrayPrefix = StorageProjection.ForFieldPath(fieldPath);
        return true;
    }

    private static bool TryGetNativeFixedArrayArrayPrefix(int variable,
        BoundExpression? fieldPlace, BoundNativeFixedArrayLayout layout,
        BoundProgram program, out StorageProjection arrayPrefix)
    {
        arrayPrefix = StorageProjection.WholeObject;
        if (!NativeFixedArrayContract.MatchesType(
                layout.CarrierType, layout, program)
            || !NativeFixedArrayContract.MatchesLayout(layout, program))
        {
            return false;
        }
        if (fieldPlace is null)
        {
            return true;
        }
        if (fieldPlace is not BoundFieldRead place
            || place.Type != layout.CarrierType)
        {
            return false;
        }

        var fields = new Stack<BoundFieldRead>();
        BoundExpression receiver = place;
        while (receiver is BoundFieldRead field)
        {
            fields.Push(field);
            receiver = field.Receiver;
        }
        if (receiver is not BoundRead root
            || root.Variable != variable)
        {
            return false;
        }

        var rootType = root.Type;
        if (!NativeFixedArrayContract.IsRootlessCopyFieldType(rootType,
                program.Structures, program.NativeFixedArrayTypes))
        {
            return false;
        }

        var ownerType = rootType;
        var fieldPath = default(FieldProjectionPath);
        while (fields.TryPop(out var field))
        {
            var ownerIndex = (int)ownerType - 100;
            if (field.Receiver.Type != ownerType
                || IsUnique(program, ownerType)
                || !IsInlineStructStorage(ownerType, program)
                || (uint)ownerIndex
                    >= (uint)program.Structures.Length
                || (uint)field.Field
                    >= (uint)program.Structures[ownerIndex].Fields.Length
                || field.Type
                    != program.Structures[ownerIndex].Fields[field.Field].Type)
            {
                return false;
            }

            fieldPath = fieldPath.Append(ownerType, field.Field);
            ownerType = field.Type;
        }

        if (ownerType != place.Type || fieldPath.Segments.IsEmpty)
        {
            return false;
        }
        arrayPrefix = StorageProjection.ForFieldPath(fieldPath);
        return true;
    }

    private static bool IsWritableNativeFixedArrayFieldPlace(
        int variable, BoundExpression? fieldPlace,
        BoundFunction function, BoundProgram program)
    {
        if (fieldPlace is not BoundFieldRead place
            || !function.Variables[variable].IsMutable
            || !TryGetFieldProjectionPath(place, function, program,
                out var rootVariable, out var fieldPath)
            || rootVariable != variable)
        {
            return false;
        }

        var ownerType = function.Variables[variable].Type;
        foreach (var segment in fieldPath.Segments)
        {
            if (segment.OwnerType != ownerType
                || !HasFields(program, ownerType)
                || (uint)segment.Field
                    >= (uint)Fields(program, ownerType).Length
                || !Fields(program, ownerType)[segment.Field].IsMutable)
            {
                return false;
            }
            ownerType = Fields(program, ownerType)[segment.Field].Type;
        }

        return ownerType == place.Type;
    }

    private static StorageProjection GetNativeFixedArrayPathPrefix(
        StorageProjection arrayPrefix,
        ImmutableArray<BoundNativeFixedArrayProjection> projections)
        => projections.IsDefaultOrEmpty
            ? arrayPrefix
            : StorageProjection.Combine(arrayPrefix,
                StorageProjection.ForNativeFixedArrayPath(projections));

    private static StorageProjection GetNativeFixedArrayElementPrefix(
        StorageProjection arrayPrefix, BoundNativeFixedArrayLayout layout,
        BoundExpression index)
        => StorageProjection.Combine(arrayPrefix,
            StorageProjection.ForArrayElement(index, layout.ElementCount));

    private static StorageProjection NativeFixedArrayElementProjection(
        int variable, BoundExpression? fieldPlace,
        BoundNativeFixedArrayLayout layout, BoundExpression index,
        BoundFunction function, BoundProgram program)
    {
        if (!TryGetNativeFixedArrayArrayPrefix(variable, fieldPlace, layout,
                function, program, out var arrayPrefix))
        {
            throw Invalid();
        }

        return GetNativeFixedArrayElementPrefix(arrayPrefix, layout, index);
    }

    private static StorageProjection NativeFixedArrayElementProjection(
        int variable, BoundExpression? fieldPlace,
        BoundNativeFixedArrayLayout layout, BoundExpression index,
        BoundProgram program)
    {
        if (!TryGetNativeFixedArrayArrayPrefix(variable, fieldPlace, layout,
                program, out var arrayPrefix))
        {
            throw Invalid();
        }

        return GetNativeFixedArrayElementPrefix(arrayPrefix, layout, index);
    }

    private static StorageProjection NativeFixedArrayPathProjection(
        int variable, BoundExpression? fieldPlace,
        ImmutableArray<BoundNativeFixedArrayProjection> projections,
        BoundFunction function, BoundProgram program)
    {
        if (!IsValidNativeFixedArrayPath(variable, projections, function,
                program, out _, fieldPlace)
            || !TryGetNativeFixedArrayArrayPrefix(variable, fieldPlace,
                projections[0].Layout, function, program,
                out var arrayPrefix))
        {
            throw Invalid();
        }

        return GetNativeFixedArrayPathPrefix(arrayPrefix, projections);
    }

    private static StorageProjection NativeFixedArrayPathProjection(
        int variable, BoundExpression? fieldPlace,
        ImmutableArray<BoundNativeFixedArrayProjection> projections,
        BoundProgram program)
    {
        if (projections.IsDefaultOrEmpty
            || !NativeFixedArrayContract.MatchesPath(
                projections[0].Layout.CarrierType, projections, program,
                out _, out _)
            || (fieldPlace is not null
                && fieldPlace.Type
                    != projections[0].Layout.CarrierType)
            || !TryGetNativeFixedArrayArrayPrefix(variable, fieldPlace,
                projections[0].Layout, program, out var arrayPrefix))
        {
            throw Invalid();
        }

        return GetNativeFixedArrayPathPrefix(arrayPrefix, projections);
    }

    private static StorageProjection NativeFixedArrayPointerAddressProjection(
        BoundNativeFixedArrayPointerAddress address,
        BoundFunction function, BoundProgram program)
        => address.Projections.IsDefaultOrEmpty
            ? NativeFixedArrayElementProjection(address.Variable,
                address.FieldPlace, address.Layout, address.Index,
                function, program)
            : NativeFixedArrayPathProjection(address.Variable,
                address.FieldPlace, address.Projections, function, program);

    private static StorageProjection NativeFixedArrayPointerAddressProjection(
        BoundNativeFixedArrayPointerAddress address, BoundProgram program)
        => address.Projections.IsDefaultOrEmpty
            ? NativeFixedArrayElementProjection(address.Variable,
                address.FieldPlace, address.Layout, address.Index, program)
            : NativeFixedArrayPathProjection(address.Variable,
                address.FieldPlace, address.Projections, program);

    private static StorageProjection NativeStorageAddressProjection(
        BoundNativeStorageAddress address, BoundFunction function,
        BoundProgram program)
    {
        if (!NativeFixedArrayTypes.IsType(address.CarrierType))
        {
            return StorageProjection.ForStorageAddress(address);
        }

        if (!NativeFixedArrayContract.TryGetLayout(address.CarrierType,
                program.Structures, program.NativeFixedArrayTypes,
                out var rootLayout)
            || !TryGetNativeFixedArrayArrayPrefix(address.Variable,
                address.FieldPlace, rootLayout, function, program,
                out var arrayPrefix))
        {
            throw Invalid();
        }

        return StorageProjection.Combine(arrayPrefix,
            StorageProjection.ForStorageAddress(address));
    }

    private static StorageProjection NativeStorageAddressProjection(
        BoundNativeStorageAddress address, BoundProgram program)
    {
        if (!NativeFixedArrayTypes.IsType(address.CarrierType))
        {
            return StorageProjection.ForStorageAddress(address);
        }

        if (!NativeFixedArrayContract.TryGetLayout(address.CarrierType,
                program.Structures, program.NativeFixedArrayTypes,
                out var rootLayout)
            || !TryGetNativeFixedArrayArrayPrefix(address.Variable,
                address.FieldPlace, rootLayout, program,
                out var arrayPrefix))
        {
            throw Invalid();
        }

        return StorageProjection.Combine(arrayPrefix,
            StorageProjection.ForStorageAddress(address));
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
        var hasFieldPlace = address.FieldPlace is not null;
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
            || (!hasFieldPlace
                && (!initialized[address.Variable]
                    || function.Variables[address.Variable].Type
                        != rootLayout.CarrierType))
            || !TryGetNativeFixedArrayArrayPrefix(address.Variable,
                address.FieldPlace, rootLayout, function, program, out _)
            || !NativeFixedArrayContract.MatchesType(
                rootLayout.CarrierType, rootLayout, program)
            || !NativeFixedArrayContract.MatchesLayout(rootLayout, program)
            || !NativeFixedArrayContract.MatchesLayout(leafLayout, program)
            || leafLayout != address.Layout
            || leafLayout.ByteExtent == 0
            || (!hasFieldPlace
                && function.Variables[address.Variable].IsMutable
                && !NativeFixedArrayContract.AllowsMutableLocal(rootLayout))
            || address.Index is null
            || address.Index != (nestedPath
                ? address.Projections[^1].Index : address.Index)
            || (nestedPath
                ? !IsValidNativeFixedArrayPath(address.Variable,
                    address.Projections, function, program, out _,
                    address.FieldPlace)
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
            || (pointer.IsWritable
                && ((hasFieldPlace
                        ? !IsWritableNativeFixedArrayFieldPlace(
                            address.Variable, address.FieldPlace, function,
                            program)
                        : !function.Variables[address.Variable].IsMutable)
                    || !NativeFixedArrayContract.AllowsMutableLocal(rootLayout)
                    || !NativeFixedArrayContract.AllowsMutableLocal(
                        leafLayout))))
        {
            throw InvalidEffect(address.Span, "NativePointer");
        }

        if (address.FieldPlace is not null
            && !EvaluateExpression(address.FieldPlace, initialized,
                fieldMasks, declarations, function, program, ref remaining,
                depth + 1, failures))
        {
            if (nestedPath)
            {
                foreach (var projection in address.Projections)
                {
                    ValidateUnreachableExpression(projection.Index,
                        initialized, fieldMasks, declarations, function,
                        program, ref remaining, depth + 1);
                }
            }
            else
            {
                ValidateUnreachableExpression(address.Index, initialized,
                    fieldMasks, declarations, function, program,
                    ref remaining, depth + 1);
            }
            return;
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
        var hasFieldPlace = write.FieldPlace is not null;
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
            || !declarations.Contains(write.Variable)
            || (!hasFieldPlace
                && (!initialized[write.Variable]
                    || function.Variables[write.Variable].Type
                        != write.Layout.CarrierType))
            || !TryGetNativeFixedArrayArrayPrefix(write.Variable,
                write.FieldPlace, write.Layout, function, program,
                out var arrayPrefix)
            || !NativeFixedArrayContract.AllowsElementWrites(write.Layout)
            || (!hasFieldPlace && !function.Variables[write.Variable].IsMutable)
            || (hasFieldPlace
                && !IsWritableNativeFixedArrayFieldPlace(write.Variable,
                    write.FieldPlace, function, program))
            || !NativeFixedArrayContract.AllowsMutableLocal(write.Layout)
            || !NativeFixedArrayContract.MatchesType(
                write.Layout.CarrierType, write.Layout, program)
            || !NativeFixedArrayContract.MatchesLayout(write.Layout, program)
            || write.Value.Type != write.Layout.ElementType
            || !NativeFixedArrayContract.IsIndexType(write.Index.Type)
            || (write.Index is BoundConstant index
                && !NativeFixedArrayContract.IsConstantIndexInRange(index,
                    write.Layout.ElementCount)))
        {
            throw InvalidEffect(write.Span, "NativeImport");
        }
        if (write.FieldPlace is not null
            && !EvaluateExpression(write.FieldPlace, initialized,
                fieldMasks, declarations, function, program, ref remaining,
                depth + 1, failures))
        {
            ValidateUnreachableExpression(write.Index, initialized,
                fieldMasks, declarations, function, program,
                ref remaining, depth + 1);
            ValidateUnreachableExpression(write.Value, initialized,
                fieldMasks, declarations, function, program,
                ref remaining, depth + 1);
            return;
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
                    GetNativeFixedArrayElementPrefix(arrayPrefix,
                        write.Layout, write.Index),
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
                    GetNativeFixedArrayElementPrefix(arrayPrefix,
                        write.Layout, write.Index),
                    initialized, fieldMasks, declarations, function, program,
                    false, ref remaining, depth + 1, failures)
                : EvaluateExpression(write.Value, initialized, fieldMasks,
                    declarations, function, program, ref remaining,
                    depth + 1, failures);
        if (valueCompletes
            && CarriesRawUnionPayloadFacts(program,
                write.Layout.ElementType))
        {
            var selectedIndex
                = NativeFixedArrayContract.TryGetUnsignedIndex(
                    write.Index, out var constantIndex)
                    ? constantIndex : (ulong?)null;
            initialized.SetRawUnionArrayElementBytes(write.Variable,
                arrayPrefix, write.Layout, selectedIndex,
                InitializedPayloadBytesOfExpression(write.Value,
                    write.Layout.ElementType, initialized, function,
                    program));
        }
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
        var hasFieldPlace = write.FieldPlace is not null;
        if (!IsValidNativeFixedArrayPath(write.Variable,
                write.Projections, function, program, out var leafLayout,
                write.FieldPlace)
            || (!hasFieldPlace
                && !function.Variables[write.Variable].IsMutable)
            || (hasFieldPlace
                && !IsWritableNativeFixedArrayFieldPlace(write.Variable,
                    write.FieldPlace, function, program))
            || !NativeFixedArrayContract.AllowsMutableLocal(
                write.Projections[0].Layout)
            || !declarations.Contains(write.Variable)
            || (!hasFieldPlace && !initialized[write.Variable])
            || write.Value.Type != leafLayout.ElementType)
        {
            throw InvalidEffect(write.Span, "NativeImport");
        }

        if (!TryGetNativeFixedArrayArrayPrefix(write.Variable,
                write.FieldPlace, write.Projections[0].Layout, function,
                program, out var fieldArrayPrefix))
        {
            throw InvalidEffect(write.Span, "NativeImport");
        }
        if (write.FieldPlace is not null
            && !EvaluateExpression(write.FieldPlace, initialized,
                fieldMasks, declarations, function, program, ref remaining,
                depth + 1, failures))
        {
            foreach (var projection in write.Projections)
            {
                ValidateUnreachableExpression(projection.Index,
                    initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1);
            }
            ValidateUnreachableExpression(write.Value, initialized,
                fieldMasks, declarations, function, program,
                ref remaining, depth + 1);
            return;
        }

        if (!EvaluateNativeFixedArrayPathIndices(write.Projections, initialized,
            fieldMasks, declarations, function, program, ref remaining,
            depth + 1, failures))
        {
            if (write.Value is BoundNativeFixedArrayInitialization nested)
            {
                var unreachableState = initialized.Clone();
                var unreachableMasks = (uint[])fieldMasks.Clone();
                var destinationPrefix = GetNativeFixedArrayPathPrefix(
                    fieldArrayPrefix, write.Projections);
                _ = EvaluateNestedNativeFixedArrayInitialization(nested,
                    write.Variable, destinationPrefix,
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
                    GetNativeFixedArrayPathPrefix(fieldArrayPrefix,
                        write.Projections),
                    initialized, fieldMasks, declarations, function, program,
                    false, ref remaining, depth + 1, failures)
                : EvaluateExpression(write.Value, initialized, fieldMasks,
                    declarations, function, program, ref remaining,
                    depth + 1, failures);
        if (valueCompletes
            && CarriesRawUnionPayloadFacts(program,
                leafLayout.ElementType))
        {
            var parentProjections = write.Projections.Length == 1
                ? [] : write.Projections.RemoveAt(
                    write.Projections.Length - 1);
            var arrayPrefix = GetNativeFixedArrayPathPrefix(
                fieldArrayPrefix, parentProjections);
            var selectedIndex
                = NativeFixedArrayContract.TryGetUnsignedIndex(
                    write.Projections[^1].Index, out var index)
                    ? index : (ulong?)null;
            initialized.SetRawUnionArrayElementBytes(write.Variable,
                arrayPrefix, leafLayout, selectedIndex,
                InitializedPayloadBytesOfExpression(write.Value,
                    leafLayout.ElementType, initialized, function,
                    program));
        }
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
