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

        var evaluationContinues = true;
        foreach (var element in initialization.Elements)
        {
            if (element is null || element.Type != layout.ElementType)
            {
                throw InvalidEffect(element?.Span ?? initialization.Span, "NativeImport");
            }
            if (evaluationContinues)
            {
                evaluationContinues = EvaluateExpression(element, initialized, fieldMasks,
                    declarations, function, program, ref remaining, depth + 1, failures);
            }
            else
            {
                var unreachableState = initialized.Clone();
                var unreachableMasks = (uint[])fieldMasks.Clone();
                _ = EvaluateExpression(element, unreachableState, unreachableMasks,
                    declarations, function, program, ref remaining, depth + 1, null);
            }
        }
    }

    private static void ApplyNativeFixedArrayWriteProvenance(
        BoundNativeFixedArrayWrite write, BackingDependencies facts,
        BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        if (write.Index is BoundConstant)
        {
            SetNativePointerFieldValuesFromExpression(write.Variable,
                StorageProjection.ForArrayElement(write.Index),
                write.Layout.ElementType, write.Value, facts, function,
                program, ref remaining, depth + 1);
            return;
        }

        var newValues = NativePointerFieldValueSnapshotOfExpression(
            write.Value, write.Layout.ElementType, facts, function,
            program, ref remaining, depth + 1);
        for (ulong index = 0; index < write.Layout.ElementCount; index++)
        {
            Count(ref remaining, depth + 1);
            var elementProjection = StorageProjection.ForArrayIndex(index);
            foreach (var (relativeProjection, newValue) in newValues)
            {
                Count(ref remaining, depth + 1);
                var projection = StorageProjection.Combine(
                    elementProjection, relativeProjection);
                if (!IsNativePointerFieldProjection(projection))
                {
                    throw Invalid(write.Span);
                }

                var hasValue = facts.TryGetNativePointerFieldValue(write.Variable,
                    projection, out var previousSources, out var previousMayBeUnknown);
                var hasExtent = facts.TryGetNativePointerFieldExtentValue(
                    write.Variable, projection, out var previousExtent);
                previousSources.UnionWith(newValue.Sources);
                previousExtent.Sources.UnionWith(newValue.ExtentSources);
                var extent = new NativePointerExtentFact(previousExtent.Sources,
                    previousExtent.MayBeOpaque || newValue.ExtentMayBeOpaque,
                    !hasValue || !hasExtent || previousExtent.IsUnavailable
                        || newValue.ExtentUnavailable);
                facts.SetNativePointerFieldValue(write.Variable, projection,
                    previousSources,
                    previousMayBeUnknown || newValue.MayBeUnknown,
                    extent);
            }
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
