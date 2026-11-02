// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using Terrum.CodeAnalysis.Text;

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
                    is BoundNativeFixedArrayInitialization nested
                        ? EvaluateNestedNativeFixedArrayInitialization(nested,
                            variable, elementProjection, initialized,
                            fieldMasks, declarations, function, program, true,
                            ref remaining, depth + 1, failures)
                        : EvaluateExpression(element, initialized, fieldMasks,
                            declarations, function, program, ref remaining,
                            depth + 1, failures);
                if (evaluationContinues
                    && element is not BoundNativeFixedArrayInitialization
                    && CarriesRawUnionPayloadFacts(program,
                        layout.ElementType))
                {
                    initialized.SetRawUnionArrayElementBytes(variable,
                        StorageProjection.WholeObject, layout,
                        (ulong)index,
                        InitializedPayloadBytesOfExpression(element,
                            layout.ElementType, initialized, function,
                            program));
                }
            }
            else
            {
                var unreachableState = initialized.Clone();
                var unreachableMasks = (uint[])fieldMasks.Clone();
                if (element is BoundNativeFixedArrayInitialization nested)
                {
                    _ = EvaluateNestedNativeFixedArrayInitialization(nested,
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

    private static void ApplyNativeFixedArrayWriteProvenance(
        BoundNativeFixedArrayWrite write, BackingDependencies facts,
        BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        var newValues = NativePointerFieldValueSnapshotOfExpression(
            write.Value, write.Layout.ElementType, facts, function,
            program, ref remaining, depth + 1);
        var isDynamic = !NativeFixedArrayContract.TryGetUnsignedIndex(
            write.Index, out var selectedIndex);
        var destination = isDynamic
            ? StorageProjection.ForArrayRange(0,
                write.Layout.ElementCount)
            : StorageProjection.ForArrayIndex(selectedIndex);
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
            write.Value, leafLayout.ElementType, facts, function, program,
            ref remaining, depth + 1);
        ApplyPath(0, StorageProjection.WholeObject, false, ref remaining);

        void ApplyPath(int pathIndex, StorageProjection prefix, bool maySelect,
            ref int workRemaining)
        {
            Count(ref workRemaining, depth + 1);
            if (pathIndex == write.Projections.Length)
            {
                ApplyNativeFixedArrayWriteValues(write.Variable, prefix,
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

    private static void VerifyNativeFixedArrayPlace(int variable, BoundExpression index,
        PrimitiveType carrierType, bool isReadonly,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (RequiresNoFailureProof(function)
            && program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
            && index is not BoundConstant)
        {
            throw InvalidNoFailureEffect(function, index?.Span ?? default);
        }
        if (!NativeFixedArrayContract.MatchesProgram(program)
            || !NativeFixedArrayContract.IsCarrier(carrierType)
            || variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || variable >= function.Variables.Length || !declarations.Contains(variable) || !initialized[variable]
            || function.Variables[variable].Type != carrierType
            || !NativeFixedArrayContract.TryGetLayout(
                function.Variables[variable].Type, program.Structures,
                program.NativeFixedArrayTypes, out var layout)
            || layout.CarrierType != carrierType
            || !NativeFixedArrayContract.MatchesLayout(layout, program)
            || (!isReadonly && (!function.Variables[variable].IsMutable
                || !NativeFixedArrayContract.AllowsMutableLocal(layout)))
            || index is null
            || !NativeFixedArrayContract.IsIndexType(index.Type)
            || (index is BoundConstant
                && !NativeFixedArrayContract.IsConstantIndexInRange(index,
                    layout.ElementCount)))
        {
            throw InvalidEffect(index?.Span ?? default, "NativeImport");
        }
        _ = EvaluateExpression(index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }
}
