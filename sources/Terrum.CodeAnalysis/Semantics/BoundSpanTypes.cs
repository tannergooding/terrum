using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal readonly record struct BoundSpanTypeDefinition(
    PrimitiveType ElementType, int ElementFormalDefinitionOrdinal,
    int ElementFormalOrdinal, bool IsElementWritable, bool IsWritable,
    bool IsInitialization);

internal static class BoundSpanTypeContract
{
    internal const int ImportedFormalDefinitionOrdinal = -2;

    internal static bool TryCreateTypeIdentityIndex(
        IReadOnlyList<BoundSpanTypeDefinition> spanTypes,
        out Dictionary<(PrimitiveType ElementType,
            int ElementFormalDefinitionOrdinal, int ElementFormalOrdinal,
            bool IsElementWritable, bool IsWritable, bool IsInitialization),
            int> indexes)
    {
        indexes = [];
        if (spanTypes.Count > BoundSpanTypeTypes.MaximumCount)
        {
            return false;
        }

        indexes = new Dictionary<(PrimitiveType ElementType,
            int ElementFormalDefinitionOrdinal, int ElementFormalOrdinal,
            bool IsElementWritable, bool IsWritable, bool IsInitialization),
            int>(spanTypes.Count);
        for (var index = 0; index < spanTypes.Count; index++)
        {
            var spanType = spanTypes[index];
            if (spanType.ElementType == PrimitiveType.Void
                || (BoundSpanTypeTypes.IsType(spanType.ElementType)
                    && (uint)BoundSpanTypeTypes.Index(spanType.ElementType)
                        >= (uint)index)
                || (spanType.ElementFormalDefinitionOrdinal == -1
                    && (spanType.ElementFormalOrdinal != -1
                        || spanType.IsElementWritable))
                || (spanType.ElementFormalDefinitionOrdinal != -1
                    && spanType.ElementFormalOrdinal < 0)
                || (spanType.ElementFormalDefinitionOrdinal
                        <= ImportedFormalDefinitionOrdinal
                    && (long)ImportedFormalDefinitionOrdinal
                        - spanType.ElementFormalDefinitionOrdinal
                        >= BoundSpanTypeTypes.MaximumCount)
                || !indexes.TryAdd((spanType.ElementType,
                    spanType.ElementFormalDefinitionOrdinal,
                    spanType.ElementFormalOrdinal,
                    spanType.IsElementWritable, spanType.IsWritable,
                    spanType.IsInitialization), index))
            {
                indexes.Clear();
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidTable(
        IReadOnlyList<BoundSpanTypeDefinition> spanTypes,
        Func<PrimitiveType, bool> isValidElementType)
    {
        if (!TryCreateTypeIdentityIndex(spanTypes, out _))
        {
            return false;
        }

        for (var index = 0; index < spanTypes.Count; index++)
        {
            var spanType = spanTypes[index];
            var elementType = spanType.ElementType;
            var isImportedFormal = spanType
                .ElementFormalDefinitionOrdinal
                    <= ImportedFormalDefinitionOrdinal;
            if ((!isImportedFormal && !isValidElementType(elementType))
                || (isImportedFormal
                    && (uint)spanType.ElementFormalOrdinal >= 8u)
                || (BoundSpanTypeTypes.IsType(elementType)
                    && (uint)BoundSpanTypeTypes.Index(elementType)
                        >= (uint)index))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidTable(BoundProgram program)
    {
        if (!IsValidTable(program.SpanTypes,
                elementType => SemanticVerifier.IsValidType(elementType,
                    false, program)))
        {
            return false;
        }
        if (program.SpanTypes.IsEmpty)
        {
            return true;
        }

        var formalTypes = new Dictionary<(int DefinitionOrdinal,
            int FormalOrdinal), PrimitiveType>();
        if (program.SourceGenericTypeInstances.IsDefault
            || program.SourceGenericTypeInstances.Length
                > SemanticVerifier.MaximumAnalysisVisits)
        {
            return false;
        }

        var sourceGenericTypeArguments
            = new Dictionary<int, ImmutableArray<PrimitiveType>>();
        foreach (var instance in program.SourceGenericTypeInstances)
        {
            if (instance is null || instance.TypeArguments.IsDefault
                || (uint)instance.StructureIndex
                    >= (uint)program.Structures.Length
                || (uint)instance.DefinitionIndex
                    >= (uint)program.SourceGenericTypeDefinitions.Length
                || instance.TypeArguments.Length
                    > SemanticVerifier.MaximumArguments
                || !sourceGenericTypeArguments.TryAdd(
                    instance.StructureIndex, instance.TypeArguments))
            {
                return false;
            }
        }

        foreach (var function in program.Functions)
        {
            var definitionOrdinal
                = function.GenericMethodProofDefinitionOrdinal;
            if (!IsValidFunctionSpanTypeUses(function, program,
                    sourceGenericTypeArguments))
            {
                return false;
            }
            if ((uint)definitionOrdinal
                    >= (uint)program.GenericMethodSignatures.Length
                || function.GenericMethodProofTypeParameters.IsDefault
                || function.GenericMethodProofTypeParameters.Length
                    != program.GenericMethodSignatures[definitionOrdinal]
                        .Formals.Length)
            {
                continue;
            }

            for (var formalOrdinal = 0;
                 formalOrdinal
                    < function.GenericMethodProofTypeParameters.Length;
                 formalOrdinal++)
            {
                var key = (definitionOrdinal, formalOrdinal);
                var type = function.GenericMethodProofTypeParameters[
                    formalOrdinal];
                if (formalTypes.TryGetValue(key, out var previousType)
                    && previousType != type)
                {
                    return false;
                }
                formalTypes[key] = type;
            }
        }

        foreach (var spanType in program.SpanTypes)
        {
            if (spanType.ElementFormalDefinitionOrdinal < 0)
            {
                continue;
            }

            var key = (spanType.ElementFormalDefinitionOrdinal,
                spanType.ElementFormalOrdinal);
            if ((uint)spanType.ElementFormalDefinitionOrdinal
                    >= (uint)program.GenericMethodSignatures.Length
                || (uint)spanType.ElementFormalOrdinal
                    >= (uint)program.GenericMethodSignatures[
                        spanType.ElementFormalDefinitionOrdinal]
                        .Formals.Length
                || !formalTypes.TryGetValue(key, out var formalType)
                || formalType != spanType.ElementType
                || (spanType.IsElementWritable
                    && !program.GenericMethodSignatures[
                        spanType.ElementFormalDefinitionOrdinal]
                        .Formals[spanType.ElementFormalOrdinal]
                        .RequiresWritable))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidFunctionSpanTypeUses(
        BoundFunction function, BoundProgram program,
        IReadOnlyDictionary<int, ImmutableArray<PrimitiveType>>
            sourceGenericTypeArguments)
    {
        // The containing callsite owns and validates these synthetic actuals.
        if (function.IsExternal
            && (function.GenericMethodProofCall is not null
                || function.ImportedGenericMethod is not null))
        {
            return true;
        }

        var validTypes = new HashSet<PrimitiveType>();
        var visitingTypes = new HashSet<PrimitiveType>();
        if (function.Variables.IsDefault
            || function.Variables.Any(variable =>
                !IsValidFunctionSpanTypeUse(variable.Type, function,
                    program, sourceGenericTypeArguments, validTypes,
                    visitingTypes))
            || !IsValidFunctionSpanTypeUse(function.ReturnType, function,
                program, sourceGenericTypeArguments, validTypes,
                visitingTypes)
            || (function.GenericMethodInstantiation is
                    BoundGenericMethodInstantiation instantiation
                && (instantiation.TypeArguments.IsDefault
                    || instantiation.TypeArguments.Any(type =>
                        !IsValidFunctionSpanTypeUse(type, function,
                            program, sourceGenericTypeArguments, validTypes,
                            visitingTypes))))
            || (function.GenericMethodProofCall is
                    BoundGenericMethodInstantiation proofCall
                && (proofCall.TypeArguments.IsDefault
                    || proofCall.TypeArguments.Any(type =>
                        !IsValidFunctionSpanTypeUse(type, function,
                            program, sourceGenericTypeArguments, validTypes,
                            visitingTypes))))
            || (!function.ImportedGenericMethodTypeArguments
                    .IsDefaultOrEmpty
                && function.ImportedGenericMethodTypeArguments.Any(type =>
                    !IsValidFunctionSpanTypeUse(type, function, program,
                        sourceGenericTypeArguments, validTypes,
                        visitingTypes))))
        {
            return false;
        }

        var valid = true;
        void ValidateExpression(BoundExpression expression)
        {
            valid &= IsValidFunctionSpanTypeUse(expression.Type, function,
                program, sourceGenericTypeArguments, validTypes,
                visitingTypes);
            if (expression is BoundTypeIdentityQuery identityQuery)
            {
                valid &= IsValidFunctionSpanTypeUse(
                    identityQuery.Subject.Type, function, program,
                    sourceGenericTypeArguments, validTypes, visitingTypes);
            }
            if (expression is not BoundCall call)
            {
                return;
            }

            if (call.GenericMethodCall
                    is BoundGenericMethodInstantiation genericCall)
            {
                valid &= !genericCall.TypeArguments.IsDefault
                    && genericCall.TypeArguments.All(type =>
                        IsValidFunctionSpanTypeUse(type, function,
                            program, sourceGenericTypeArguments, validTypes,
                            visitingTypes));
            }
            if (!call.ImportedGenericMethodTypeArguments
                .IsDefaultOrEmpty)
            {
                valid &= call.ImportedGenericMethodTypeArguments.All(
                    type => IsValidFunctionSpanTypeUse(type, function,
                        program, sourceGenericTypeArguments, validTypes,
                        visitingTypes));
            }
        }

        return BoundTreeTraversal.VisitExpressions(function.Body,
                ValidateExpression)
            && BoundTreeTraversal.VisitContractExpressions(function,
                ValidateExpression)
            && valid;
    }

    private static bool IsValidFunctionSpanTypeUse(PrimitiveType type,
        BoundFunction function, BoundProgram program,
        IReadOnlyDictionary<int, ImmutableArray<PrimitiveType>>
            sourceGenericTypeArguments,
        HashSet<PrimitiveType> validTypes,
        HashSet<PrimitiveType> visitingTypes, int depth = 0)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return false;
        }
        if (validTypes.Contains(type))
        {
            return true;
        }
        if (!visitingTypes.Add(type))
        {
            return false;
        }

        var valid = IsValidFunctionSpanTypeUseCore(type, function, program,
            sourceGenericTypeArguments, validTypes, visitingTypes, depth);
        _ = visitingTypes.Remove(type);
        if (valid)
        {
            _ = validTypes.Add(type);
        }
        return valid;
    }

    private static bool IsValidFunctionSpanTypeUseCore(PrimitiveType type,
        BoundFunction function, BoundProgram program,
        IReadOnlyDictionary<int, ImmutableArray<PrimitiveType>>
            sourceGenericTypeArguments,
        HashSet<PrimitiveType> validTypes,
        HashSet<PrimitiveType> visitingTypes, int depth)
    {
        if (BoundSpanTypeTypes.IsType(type))
        {
            if (!BoundSpanTypeTypes.IsValidType(type, program.SpanTypes))
            {
                return false;
            }

            var spanType = program.SpanTypes[
                BoundSpanTypeTypes.Index(type)];
            if (spanType.ElementFormalDefinitionOrdinal >= 0)
            {
                if (spanType.ElementFormalDefinitionOrdinal
                    != function.GenericMethodProofDefinitionOrdinal)
                {
                    return false;
                }
            }
            else if (spanType.ElementFormalDefinitionOrdinal
                <= BoundSpanTypeContract.ImportedFormalDefinitionOrdinal)
            {
                var imported = function.ImportedGenericMethod;
                if (imported is null
                    || imported.FormalTypeOwnerOrdinal < 0
                    || spanType.ElementFormalDefinitionOrdinal
                        != (long)BoundSpanTypeContract
                            .ImportedFormalDefinitionOrdinal
                        - imported.FormalTypeOwnerOrdinal)
                {
                    return false;
                }
            }

            return IsValidFunctionSpanTypeUse(spanType.ElementType,
                function, program, sourceGenericTypeArguments, validTypes,
                visitingTypes, depth + 1);
        }

        if (CheckedReferenceTypes.IsType(type))
        {
            var index = CheckedReferenceTypes.Index(type);
            return (uint)index
                    < (uint)program.CheckedReferenceTypes.Length
                && IsValidFunctionSpanTypeUse(
                    program.CheckedReferenceTypes[index].ReferentType,
                    function, program, sourceGenericTypeArguments,
                    validTypes, visitingTypes, depth + 1);
        }

        if (NativeFixedArrayTypes.IsType(type))
        {
            var index = NativeFixedArrayTypes.Index(type);
            return (uint)index
                    < (uint)program.NativeFixedArrayTypes.Length
                && IsValidFunctionSpanTypeUse(
                    program.NativeFixedArrayTypes[index].ElementType,
                    function, program, sourceGenericTypeArguments,
                    validTypes, visitingTypes, depth + 1);
        }

        if (NativePointerTypes.IsType(type))
        {
            var index = NativePointerTypes.Index(type);
            return (uint)index < (uint)program.NativePointerTypes.Length
                && IsValidFunctionSpanTypeUse(
                    program.NativePointerTypes[index].PointeeType,
                    function, program, sourceGenericTypeArguments,
                    validTypes, visitingTypes, depth + 1);
        }

        if (NativeFunctionPointerTypes.IsType(type))
        {
            var index = NativeFunctionPointerTypes.Index(type);
            if ((uint)index
                >= (uint)program.NativeFunctionPointerSignatures.Length)
            {
                return false;
            }

            var signature
                = program.NativeFunctionPointerSignatures[index];
            return !signature.ParameterTypes.IsDefault
                && IsValidFunctionSpanTypeUse(signature.ReturnType,
                    function, program, sourceGenericTypeArguments,
                    validTypes, visitingTypes, depth + 1)
                && signature.ParameterTypes.All(parameterType =>
                    IsValidFunctionSpanTypeUse(parameterType, function,
                        program, sourceGenericTypeArguments, validTypes,
                        visitingTypes, depth + 1));
        }

        if (BoundGenericNominalTypeContract.IsValidType(type, program))
        {
            var instance = program.GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(type)];
            return !instance.Arguments.IsDefault
                && instance.Arguments.All(argument =>
                    argument.Kind != BoundGenericArgumentKind.Type
                    || IsValidFunctionSpanTypeUse(argument.Type, function,
                        program, sourceGenericTypeArguments, validTypes,
                        visitingTypes, depth + 1));
        }

        if (function.GenericMethodProofDefinitionOrdinal >= 0
            && function.GenericMethodProofTypeParameters.Contains(type))
        {
            return true;
        }

        var typeValue = (int)type;
        if (typeValue is >= 100 and < ManagedClassTypes.First)
        {
            var structureIndex = typeValue - 100;
            if ((uint)structureIndex < (uint)program.Structures.Length
                && sourceGenericTypeArguments.TryGetValue(structureIndex,
                    out var typeArguments))
            {
                return typeArguments.All(typeArgument =>
                    IsValidFunctionSpanTypeUse(typeArgument, function,
                        program, sourceGenericTypeArguments, validTypes,
                        visitingTypes, depth + 1));
            }
        }

        return true;
    }
}

internal static class BoundSpanTypeTypes
{
    // Keep Span instances disjoint from checked references and generic nominal types.
    internal const int First = -1_000_000;
    internal const int MaximumCount = SemanticVerifier.MaximumAnalysisVisits;

    internal static bool IsType(PrimitiveType type) =>
        (int)type is <= First and > First - MaximumCount;

    internal static int Index(PrimitiveType type)
    {
        if (!IsType(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        return checked(First - (int)type);
    }

    internal static PrimitiveType Type(int index)
    {
        if ((uint)index >= MaximumCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return (PrimitiveType)(First - index);
    }

    internal static bool IsValidType(PrimitiveType type,
        IReadOnlyList<BoundSpanTypeDefinition> spanTypes) =>
        IsType(type)
        && (uint)Index(type) < (uint)spanTypes.Count;
}
