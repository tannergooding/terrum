// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static void VerifyNativeFixedArrayConstruction(
        BoundExpression construction, int variable,
        InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures)
    {
        switch (construction)
        {
            case BoundNativeFixedArrayInitialization initialization:
                VerifyNativeFixedArrayInitialization(initialization,
                    variable, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth, failures);
                break;
            case BoundNativeFixedArrayRepeatInitialization repeat:
                VerifyNativeFixedArrayRepeatInitialization(repeat, variable,
                    initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth, failures);
                break;
            default:
                VerifyNativeFixedArrayInitialization(construction,
                    variable, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth, failures);
                break;
        }
    }

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
            || !NativeFixedArrayContract.MatchesType(
                function.Variables[variable].Type, layout, program)
            || !NativeFixedArrayContract.MatchesLayout(layout, program)
            || (function.Variables[variable].IsMutable
                && !NativeFixedArrayContract.AllowsMutableLocal(layout))
            || initialization.Elements.IsDefault
            || (initialization.Elements.IsEmpty
                ? !NativeFixedArrayContract.AllowsEmptyInitialization(layout)
                : (ulong)initialization.Elements.Length != layout.ElementCount))
        {
            throw InvalidEffect(initialization.Span, "NativeImport");
        }

        initialized.ClearRawUnionArrayFacts(variable);
        var evaluationContinues = true;
        for (var index = 0; index < initialization.Elements.Length; index++)
        {
            var element = initialization.Elements[index];
            if (element is null || element.Type != layout.ElementType)
            {
                throw InvalidEffect(element?.Span ?? initialization.Span, "NativeImport");
            }
            if (evaluationContinues)
            {
                var elementProjection = StorageProjection.ForArrayIndex(
                    (ulong)index);
                evaluationContinues = element
                    is BoundNativeFixedArrayInitialization
                        or BoundNativeFixedArrayRepeatInitialization
                    ? EvaluateNestedNativeFixedArrayConstruction(element,
                            variable, elementProjection, initialized,
                            fieldMasks, declarations, function, program, true,
                            ref remaining, depth + 1, failures)
                        : EvaluateExpression(element, initialized, fieldMasks,
                            declarations, function, program, ref remaining,
                            depth + 1, failures);
                if (evaluationContinues
                    && element is not (BoundNativeFixedArrayInitialization
                        or BoundNativeFixedArrayRepeatInitialization)
                    && CarriesRawUnionPayloadFacts(program,
                        layout.ElementType))
                {
                    initialized.SetRawUnionArrayElementBytes(variable,
                        StorageProjection.WholeObject, layout,
                        (ulong)index,
                        InitializedPayloadBytesOfExpression(element,
                            layout.ElementType, initialized, function,
                            program, ref remaining, depth + 1));
                }
            }
            else
            {
                var unreachableState = initialized.Clone();
                var unreachableMasks = (uint[])fieldMasks.Clone();
                if (element is BoundNativeFixedArrayInitialization
                    or BoundNativeFixedArrayRepeatInitialization)
                {
                    _ = EvaluateNestedNativeFixedArrayConstruction(element,
                        variable, StorageProjection.ForArrayIndex(
                            (ulong)index), unreachableState,
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
    }

    private static void VerifyNativeFixedArrayRepeatInitialization(
        BoundNativeFixedArrayRepeatInitialization initialization,
        int variable, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var layout = initialization.Layout;
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || (uint)variable >= (uint)function.Variables.Length
            || variable < function.ParameterCount
                + (function.IsConstructor ? 1 : 0)
            || function.Variables[variable].Type != layout.CarrierType
            || initialization.Type != layout.CarrierType
            || initialization.Seed is null
            || initialization.Seed.Type != layout.ElementType
            || !NativeFixedArrayContract.MatchesType(
                function.Variables[variable].Type, layout, program)
            || !NativeFixedArrayContract.MatchesLayout(layout, program)
            || !NativeFixedArrayContract.IsRootlessCopyFieldType(
                layout.ElementType, program.Structures,
                program.NativeFixedArrayTypes)
            || (function.Variables[variable].IsMutable
                && !NativeFixedArrayContract.AllowsMutableLocal(layout)))
        {
            throw InvalidEffect(initialization.Span, "NativeImport");
        }

        var staged = initialized.Clone();
        var stagedMasks = (uint[])fieldMasks.Clone();
        if (!EvaluateNestedNativeFixedArrayRepeatInitialization(
                initialization, variable, StorageProjection.WholeObject,
                staged, stagedMasks, declarations, function, program, true,
                ref remaining, depth + 1, failures))
        {
            return;
        }

        staged.CopyTo(initialized);
        stagedMasks.CopyTo(fieldMasks, 0);
    }

    private static void ApplyNativeFixedArrayWriteProvenance(
        BoundNativeFixedArrayWrite write, BackingDependencies facts,
        BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        var newValues = NativePointerFieldValueSnapshotOfExpression(
            write.Value, write.Layout.ElementType, facts, function,
            program, ref remaining, depth + 1);
        if (!TryGetNativeFixedArrayArrayPrefix(write.Variable,
                write.FieldPlace, write.Layout, function, program,
                out var arrayPrefix))
        {
            throw Invalid(write.Span);
        }
        var isDynamic = !NativeFixedArrayContract.TryGetUnsignedIndex(
            write.Index, out var selectedIndex);
        var destination = StorageProjection.Combine(arrayPrefix,
            isDynamic
                ? StorageProjection.ForArrayRange(0,
                    write.Layout.ElementCount)
                : StorageProjection.ForArrayIndex(selectedIndex));
        ApplyNativeFixedArrayWriteValues(write.Variable, destination,
            isDynamic, newValues, facts, write.Span);
    }

    private static void ApplyNativeFixedArrayPathWriteProvenance(
        BoundNativeFixedArrayPathWrite write,
        BoundNativeFixedArrayLayout leafLayout, BackingDependencies facts,
        BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        var newValues = NativePointerFieldValueSnapshotOfExpression(
            write.Value, write.Value.Type, facts, function, program,
            ref remaining, depth + 1);
        if (!TryGetNativeFixedArrayArrayPrefix(write.Variable,
                write.FieldPlace, write.Projections[0].Layout, function,
                program, out var arrayPrefix))
        {
            throw Invalid(write.Span);
        }
        ApplyPath(0, arrayPrefix, false, ref remaining);

        void ApplyPath(int pathIndex, StorageProjection prefix, bool maySelect,
            ref int workRemaining)
        {
            Count(ref workRemaining, depth + 1);
            if (pathIndex == write.Projections.Length)
            {
                var destinationPrefix = prefix;
                foreach (var field in write.LeafFields)
                {
                    destinationPrefix = destinationPrefix.AppendField(
                        field.OwnerType, field.Field);
                }
                if (!write.LeafFields.IsEmpty)
                {
                    InvalidateNativeFixedArrayPathWriteUnionOverlaps(write,
                        leafLayout.ElementType, prefix, facts, program);
                }
                ApplyNativeFixedArrayWriteValues(write.Variable,
                    destinationPrefix,
                    maySelect, newValues, facts, write.Span);
                return;
            }

            var projection = write.Projections[pathIndex];
            if (NativeFixedArrayContract.TryGetUnsignedIndex(
                    projection.Index, out var selectedIndex))
            {
                ApplyPath(pathIndex + 1,
                    StorageProjection.Combine(prefix,
                        StorageProjection.ForArrayIndex(selectedIndex)),
                    maySelect, ref workRemaining);
                return;
            }

            ApplyPath(pathIndex + 1,
                StorageProjection.Combine(prefix,
                    StorageProjection.ForArrayRange(0,
                        projection.Layout.ElementCount)),
                true, ref workRemaining);
        }
    }

    private static void ApplyNativeFixedArrayPathCopyOrigin(
        BoundNativeFixedArrayPathWrite write,
        BackingDependencies facts, BoundFunction function,
        BoundProgram program, ref int remaining, int depth)
    {
        var newValues = CopyOriginFieldValueSnapshotOfExpression(
            write.Value, write.Value.Type, facts, function, program,
            ref remaining, depth + 1);
        if (!TryGetNativeFixedArrayArrayPrefix(write.Variable,
                write.FieldPlace, write.Projections[0].Layout, function,
                program, out var arrayPrefix))
        {
            throw Invalid(write.Span);
        }
        ApplyPath(0, arrayPrefix, false, ref remaining);

        void ApplyPath(int pathIndex, StorageProjection prefix, bool maySelect,
            ref int workRemaining)
        {
            Count(ref workRemaining, depth + 1);
            if (pathIndex == write.Projections.Length)
            {
                var destinationPrefix = prefix;
                foreach (var field in write.LeafFields)
                {
                    destinationPrefix = destinationPrefix.AppendField(
                        field.OwnerType, field.Field);
                }
                if (!write.LeafFields.IsEmpty)
                {
                    InvalidateNativeFixedArrayPathWriteUnionOverlaps(write,
                        write.Projections[^1].Layout.ElementType, prefix,
                        facts, program);
                }
                ApplyNativeFixedArrayCopyOriginWriteValues(write.Variable,
                    destinationPrefix, maySelect, newValues, facts,
                    write.Span, ref workRemaining);
                return;
            }

            var projection = write.Projections[pathIndex];
            if (NativeFixedArrayContract.TryGetUnsignedIndex(
                    projection.Index, out var selectedIndex))
            {
                ApplyPath(pathIndex + 1,
                    StorageProjection.Combine(prefix,
                        StorageProjection.ForArrayIndex(selectedIndex)),
                    maySelect, ref workRemaining);
                return;
            }

            ApplyPath(pathIndex + 1,
                StorageProjection.Combine(prefix,
                    StorageProjection.ForArrayRange(0,
                        projection.Layout.ElementCount)),
                true, ref workRemaining);
        }
    }

    private static void ApplyNativeFixedArrayCopyOriginWriteValues(
        int variable, StorageProjection destinationPrefix, bool maySelect,
        IReadOnlyDictionary<StorageProjection, CopyOriginFact> newValues,
        BackingDependencies facts, TextSpan span, ref int remaining)
    {
        foreach (var (relativeProjection, newValue) in newValues)
        {
            Count(ref remaining, 0);
            var projection = StorageProjection.Combine(
                destinationPrefix, relativeProjection);
            if (!IsCopyOriginFieldProjection(projection))
            {
                throw Invalid(span);
            }

            if (!projection.ProjectionPath.IsConcrete)
            {
                if (maySelect)
                {
                    facts.MergeCopyOriginFieldValueUnknown(variable,
                        projection, newValue, ref remaining);
                }
                else
                {
                    facts.SetCopyOriginFieldValueTemplate(variable,
                        projection, newValue, ref remaining);
                }
                continue;
            }

            if (!maySelect)
            {
                facts.SetCopyOriginFieldValue(variable, projection, newValue);
                continue;
            }

            _ = facts.TryGetCopyOriginFieldValue(variable, projection,
                out var previousValue);
            _ = previousValue.Merge(newValue);
            facts.SetCopyOriginFieldValue(variable, projection, previousValue);
        }
    }

    private static void InvalidateNativeFixedArrayPathWriteUnionOverlaps(
        BoundNativeFixedArrayPathWrite write, PrimitiveType elementType,
        StorageProjection elementProjection, BackingDependencies facts,
        BoundProgram program)
    {
        var fieldPath = default(FieldProjectionPath);
        var ownerType = elementType;
        ulong writeSize = 0;
        foreach (var field in write.LeafFields)
        {
            if (field.OwnerType != ownerType
                || !HasFields(program, ownerType)
                || (uint)field.Field >= (uint)Fields(program,
                        ownerType).Length)
            {
                throw Invalid(write.Span);
            }
            var declaration = Fields(program, ownerType)[field.Field];
            fieldPath = fieldPath.Append(ownerType, field.Field);
            ownerType = declaration.Type;
            writeSize = declaration.Size;
        }

        if (writeSize == 0
            || !TryGetFieldPathByteOffset(elementType, fieldPath, program,
                out var writeOffset))
        {
            return;
        }

        var segments = fieldPath.Segments;
        for (var unionOwnerIndex = 0;
            unionOwnerIndex < segments.Length;
            unionOwnerIndex++)
        {
            var unionType = segments[unionOwnerIndex].OwnerType;
            if (!IsRawUnionType(program, unionType))
            {
                continue;
            }

            var unionOwnerPath = default(FieldProjectionPath);
            var unionProjection = elementProjection;
            for (var index = 0; index < unionOwnerIndex; index++)
            {
                var segment = segments[index];
                unionOwnerPath = unionOwnerPath.Append(segment.OwnerType,
                        segment.Field);
                unionProjection = unionProjection.AppendField(
                        segment.OwnerType, segment.Field);
            }
            var unionOffset = 0;
            if (!unionOwnerPath.Segments.IsEmpty
                && !TryGetFieldPathByteOffset(elementType,
                        unionOwnerPath, program, out unionOffset))
            {
                throw Invalid(write.Span);
            }
            if (unionOffset > writeOffset)
            {
                throw Invalid(write.Span);
            }

            var unionWriteOffset
                = checked((ulong)(writeOffset - unionOffset));
            var writeEnd = checked(unionWriteOffset + writeSize);
            var unionSize = program.Structures[
                (int)unionType - 100].Size;
            var unionFields = Fields(program, unionType);
            if ((uint)segments[unionOwnerIndex].Field
                        >= (uint)unionFields.Length
                || unionWriteOffset > unionSize
                || writeSize > unionSize - unionWriteOffset)
            {
                throw Invalid(write.Span);
            }

            var writtenAlternative = segments[unionOwnerIndex].Field;
            var wholeAlternativeWrite = unionOwnerIndex
                        == segments.Length - 1
                && unionWriteOffset
                        == unionFields[writtenAlternative].Offset
                && writeSize == unionFields[writtenAlternative].Size;
            for (var field = 0; field < unionFields.Length; field++)
            {
                if (wholeAlternativeWrite && field == writtenAlternative)
                {
                        continue;
                }

                var candidate = unionFields[field];
                var candidateEnd = checked(candidate.Offset
                        + candidate.Size);
                if (candidate.Size == 0
                        || candidate.Offset >= writeEnd
                        || unionWriteOffset >= candidateEnd)
                {
                        continue;
                }

                var projection = unionProjection.AppendField(unionType,
                        field);
                facts.ClearCopyOriginFieldValues(write.Variable,
                        projection);
                var inlineFieldOnlyProjection
                    = projection.Kind
                        == StorageProjectionKind.InlineFieldPath;
                foreach (var segment in projection.ProjectionPath.Segments)
                {
                    inlineFieldOnlyProjection &= segment.Kind
                        == StorageProjectionSegmentKind.Field;
                }
                if (inlineFieldOnlyProjection)
                {
                    facts.ClearBackingFieldValues(write.Variable, projection);
                    facts.ClearReferenceFieldValues(write.Variable,
                        projection);
                }
                facts.InvalidateNativePointerFieldValues(write.Variable,
                        projection);
            }
        }
    }

    private static void ApplyNativeFixedArrayWriteValues(
        int variable, StorageProjection destinationPrefix, bool maySelect,
        IReadOnlyDictionary<StorageProjection, NativePointerFieldValue> newValues,
        BackingDependencies facts, TextSpan span)
    {
        foreach (var (relativeProjection, newValue) in newValues)
        {
            var projection = StorageProjection.Combine(
                destinationPrefix, relativeProjection);
            if (!IsNativePointerFieldProjection(projection))
            {
                throw Invalid(span);
            }

            if (!projection.ProjectionPath.IsConcrete)
            {
                if (!maySelect)
                {
                    throw Invalid(span);
                }
                facts.MergeNativePointerFieldValueUnknown(variable,
                    projection, newValue.Sources,
                    newValue.MayBeUnknown,
                    new NativePointerExtentFact(newValue.ExtentSources,
                        newValue.ExtentMayBeOpaque,
                        newValue.ExtentUnavailable));
                continue;
            }

            if (!maySelect)
            {
                facts.SetNativePointerFieldValue(variable, projection,
                    newValue.Sources, newValue.MayBeUnknown,
                    new NativePointerExtentFact(newValue.ExtentSources,
                        newValue.ExtentMayBeOpaque,
                        newValue.ExtentUnavailable));
                continue;
            }

            var hasValue = facts.TryGetNativePointerFieldValue(variable,
                projection, out var previousSources,
                out var previousMayBeUnknown);
            var hasExtent = facts.TryGetNativePointerFieldExtentValue(
                variable, projection, out var previousExtent);
            previousSources.UnionWith(newValue.Sources);
            previousExtent.Sources.UnionWith(newValue.ExtentSources);
            var extent = new NativePointerExtentFact(previousExtent.Sources,
                previousExtent.MayBeOpaque || newValue.ExtentMayBeOpaque,
                !hasValue || !hasExtent || previousExtent.IsUnavailable
                    || newValue.ExtentUnavailable);
            facts.SetNativePointerFieldValue(variable, projection,
                previousSources,
                previousMayBeUnknown || newValue.MayBeUnknown, extent);
        }
    }

    private static void VerifyNativeFixedArrayPlace(int variable,
        BoundExpression? fieldPlace, BoundExpression index,
        PrimitiveType carrierType, bool isReadonly,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var hasFieldPlace = fieldPlace is not null;
        if (RequiresNoFailureProof(function)
            && program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
            && index is not BoundConstant)
        {
            throw InvalidNoFailureEffect(function, index?.Span ?? default);
        }
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || !NativeFixedArrayContract.IsCarrier(carrierType)
            || !NativeFixedArrayContract.TryGetLayout(carrierType,
                program.Structures, program.NativeFixedArrayTypes,
                out var layout)
            || variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || variable >= function.Variables.Length
            || !declarations.Contains(variable)
            || (!hasFieldPlace
                && (!initialized[variable]
                    || function.Variables[variable].Type != carrierType))
            || !TryGetNativeFixedArrayArrayPrefix(variable, fieldPlace,
                layout, function, program, out _)
            || layout.CarrierType != carrierType
            || !NativeFixedArrayContract.MatchesLayout(layout, program)
            || (!isReadonly
                && ((hasFieldPlace
                        ? !IsWritableNativeFixedArrayFieldPlace(variable,
                            fieldPlace, function, program)
                        : !function.Variables[variable].IsMutable)
                    || !NativeFixedArrayContract.AllowsMutableLocal(layout)))
            || index is null
            || !NativeFixedArrayContract.IsIndexType(index.Type)
            || (index is BoundConstant
                && !NativeFixedArrayContract.IsConstantIndexInRange(index,
                    layout.ElementCount)))
        {
            throw InvalidEffect(index?.Span ?? default, "NativeImport");
        }
        if (fieldPlace is not null
            && !EvaluateExpression(fieldPlace, initialized, fieldMasks,
                declarations, function, program, ref remaining,
                depth + 1, failures))
        {
            ValidateUnreachableExpression(index, initialized, fieldMasks,
                declarations, function, program, ref remaining, depth + 1);
            return;
        }
        _ = EvaluateExpression(index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }
}
