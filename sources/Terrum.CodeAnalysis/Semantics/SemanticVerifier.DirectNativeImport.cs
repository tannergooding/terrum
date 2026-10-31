// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private sealed record NativeOutputDestinationState(object? Version)
    {
        internal byte UnavailablePossibilities { get; init; } = 1;
        internal bool IsReserved { get; init; }
    }

    private sealed record NativeOutputDestinationFact(
        int Variable, NativeOutputSelector SuccessSelector,
        NativeOutputSelector ComplementSelector, object Version);

    private sealed record NativeOutputCallFact(
        PrimitiveType ResultType, ImmutableArray<NativeOutputDestinationFact> Destinations);

    private static NativeOutputCallFact? NativeOutputResultForValue(
        BoundExpression expression, InitializationState initialized, int resultVariable)
    {
        expression = UnwrapUnsafeExpression(expression);
        var fact = expression switch
        {
            BoundRead read when read.Variable >= 0 && read.Variable < initialized.Length
                => initialized.NativeOutputResult(read.Variable),
            BoundCall call => CreateNativeOutputCallFact(call, initialized),
            BoundIndirectCall call => CreateNativeOutputCallFact(call, initialized),
            _ => null,
        };
        return fact is not null
            && fact.Destinations.Any(destination => destination.Variable == resultVariable)
                ? null : fact;
    }

    private static void ApplyNativeOutputCallEffects(BoundCall call, BoundFunction target,
        InitializationState initialized, ref int remaining)
    {
        if (CallMayInvalidateNativeOutputState(call, target, initialized))
        {
            initialized.InvalidateNativeOutputCorrelations();
        }

        for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var parameterIndex = call.ParameterIndex(argumentIndex);
            var outputContract = NativeOutputContractForParameter(target, parameterIndex);
            var legacyOutput = outputContract is null
                ? NativeOutputForParameter(target, parameterIndex)
                : null;
            if ((outputContract is null && legacyOutput is null)
                || UnwrapUnsafeExpression(call.Arguments[argumentIndex])
                    is not BoundNativeOutputReservation reservation)
            {
                continue;
            }

            initialized.MarkNativeOutputPending(reservation.Variable);
            if (initialized.Dependencies is BackingDependencies dependencies)
            {
                var sources = new HashSet<ProvenanceAnchor>();
                var mayBeUnknown = false;
                if (outputContract is not null)
                {
                    mayBeUnknown = CarriesNativePointer(
                        outputContract.PointeeType, initialized.Program);
                    foreach (var dependency in outputContract.Dependencies)
                    {
                        Count(ref remaining, 0);
                        if (dependency.SourceKind == NativeOutputDependencySourceKind.Empty)
                        {
                            continue;
                        }
                        if (dependency.SourceKind != NativeOutputDependencySourceKind.Formal)
                        {
                            throw Invalid(call.Span);
                        }

                        var stagedIndex = dependencies.Staged.FindLastIndex(staged =>
                            ReferenceEquals(staged.Call, call)
                            && staged.ParameterIndex == dependency.SourceOrdinal);
                        if (stagedIndex < 0)
                        {
                            throw InvalidEffect(call.Span, "Ownership");
                        }

                        var staged = dependencies.Staged[stagedIndex];
                        sources.UnionWith(staged.NativePointerSources);
                        mayBeUnknown |= staged.NativePointerMayBeUnknown;
                    }
                }
                else if (legacyOutput is not null
                    && CarriesNativePointer(legacyOutput.PointeeType, initialized.Program))
                {
                    mayBeUnknown = true;
                }
                dependencies.SetNativePointerProvenance(
                    reservation.Variable, sources, mayBeUnknown);
                var pointerOutputType = outputContract?.PointeeType
                    ?? legacyOutput?.PointeeType;
                if (pointerOutputType is PrimitiveType outputType
                    && CarriesNativePointer(outputType, initialized.Program))
                {
                    dependencies.SetNativePointerExtentProvenance(
                        reservation.Variable, EmptyBackingSources,
                        mayBeOpaque: true, unavailable: false);
                }
            }
        }
    }

    private static void ApplyNativeOutputIndirectCallEffects(BoundIndirectCall call,
        NativeFunctionPointerSignatureDefinition signature, InitializationState initialized,
        ref int remaining)
    {
        initialized.InvalidateNativeOutputCorrelations();
        for (var formal = 0; formal < signature.ParameterTypes.Length; formal++)
        {
            if (!NativeFunctionPointerContract.TryGetNativeOutputContract(
                signature, formal, initialized.Program.NativePointerTypes,
                out var outputContract)
                || outputContract is null)
            {
                continue;
            }
            if (call.Arguments[formal] is not BoundNativeOutputReservation reservation)
            {
                throw InvalidEffect(call.Span, "NativeImport");
            }

            initialized.MarkNativeOutputPending(reservation.Variable);
            if (initialized.Dependencies is not BackingDependencies dependencies)
            {
                continue;
            }

            var sources = new HashSet<ProvenanceAnchor>();
            var mayBeUnknown = CarriesNativePointer(
                outputContract.PointeeType, initialized.Program);
            foreach (var dependency in outputContract.Dependencies)
            {
                Count(ref remaining, 0);
                if (dependency.SourceKind == NativeOutputDependencySourceKind.Empty)
                {
                    continue;
                }
                if (dependency.SourceKind != NativeOutputDependencySourceKind.Formal)
                {
                    throw Invalid(call.Span);
                }

                var stagedIndex = dependencies.Staged.FindLastIndex(staged =>
                    ReferenceEquals(staged.Call, call)
                    && staged.ParameterIndex == dependency.SourceOrdinal);
                if (stagedIndex < 0)
                {
                    throw InvalidEffect(call.Span, "Ownership");
                }

                var staged = dependencies.Staged[stagedIndex];
                sources.UnionWith(staged.NativePointerSources);
                mayBeUnknown |= staged.NativePointerMayBeUnknown;
            }
            dependencies.SetNativePointerProvenance(
                reservation.Variable, sources, mayBeUnknown);
            if (CarriesNativePointer(outputContract.PointeeType, initialized.Program))
            {
                dependencies.SetNativePointerExtentProvenance(
                    reservation.Variable, EmptyBackingSources,
                    mayBeOpaque: true, unavailable: false);
            }
        }
    }

    private static bool CallMayInvalidateNativeOutputState(
        BoundCall call, BoundFunction target, InitializationState initialized)
    {
        var program = initialized.Program;
        for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var argument = UnwrapUnsafeExpression(call.Arguments[argumentIndex]);
            if (argument is BoundOutArgument output)
            {
                if (initialized.HasNativeOutputState(output.Variable))
                {
                    return true;
                }
                continue;
            }

            var parameterIndex = call.ParameterIndex(argumentIndex);
            var nativeParameter = DirectNativeImportContract.ParameterForParameter(target, parameterIndex)
                ?? NativeVTableCallContract.ParameterForParameter(target, parameterIndex);
            if (argument is BoundNativeOutputReservation)
            {
                if (HasNativeOutputForParameter(target, parameterIndex))
                {
                    return true;
                }
                continue;
            }
            if (argument is BoundNativeStorageAddress)
            {
                if (HasNativeOutputForParameter(target, parameterIndex))
                {
                    return true;
                }
                if ((nativeParameter is null || nativeParameter.Access != NativeParameterAccess.Read)
                    && initialized.MayHaveNativeOutputState)
                {
                    return true;
                }
                continue;
            }

            if (nativeParameter?.Access == NativeParameterAccess.Read
                && nativeParameter.Retention == NativeParameterRetention.NoEscape)
            {
                continue;
            }

            if (argument.Type is PrimitiveType.NativeRawPointer
                    or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                || NativePointerTypeContract.IsValidType(argument.Type, program.NativePointerTypes)
                || NativeFixedArrayContract.IsCarrier(argument.Type)
                || HasFields(program, argument.Type))
            {
                if (initialized.MayHaveNativeOutputState)
                {
                    return true;
                }
                continue;
            }
        }

        return NativeOutputCount(target) > 0;
    }

    private static void ApplyNativeOutputCalls(BoundExpression expression, InitializationState initialized,
        Dictionary<BoundExpression, NativeOutputCallFact> callFacts,
        ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        var program = initialized.Program;
        if (expression is BoundCall call)
        {
            var target = (uint)call.Function < (uint)program.Functions.Length
                ? program.Functions[call.Function] : null;
            var stagedCount = initialized.Dependencies is BackingDependencies
                stagedDependencies ? stagedDependencies.Staged.Count : 0;
            try
            {
                var argumentsComplete = true;
                for (var index = 0; index < call.Arguments.Length; index++)
                {
                    if (!argumentsComplete)
                    {
                        break;
                    }

                    var argument = call.Arguments[index];
                    ApplyNativeOutputCalls(argument, initialized, callFacts, ref remaining, depth + 1);
                    argumentsComplete = CanCompleteNormally(argument, initialized,
                        program, ref remaining, depth + 1);
                    if (!argumentsComplete)
                    {
                        break;
                    }
                    if (target is not null)
                    {
                        StageDependencySource(call, call.ParameterIndex(index), argument, target,
                            initialized, program, ref remaining, depth + 1);
                    }
                }

                if (target is not null && argumentsComplete)
                {
                    ApplyNativePointerCaptureEffects(call, target, initialized, ref remaining);
                    InvalidateNativePointerExtentsForCall(
                        call, target, initialized, program);
                    ApplyNativeOutputCallEffects(call, target, initialized, ref remaining);
                    if (CreateNativeOutputCallFact(call, initialized) is NativeOutputCallFact fact)
                    {
                        callFacts[call] = fact;
                    }
                }
            }
            finally
            {
                if (initialized.Dependencies is BackingDependencies currentDependencies)
                {
                    currentDependencies.Staged.RemoveRange(
                        stagedCount, currentDependencies.Staged.Count - stagedCount);
                }
            }
            return;
        }

        if (expression is BoundIndirectCall indirectCall)
        {
            if (indirectCall.Target is null)
            {
                throw Invalid(indirectCall.Span);
            }

            var type = indirectCall.Target.Type;
            if (!NativeFunctionPointerTypes.IsType(type)
                || (uint)NativeFunctionPointerTypes.Index(type)
                    >= (uint)program.NativeFunctionPointerSignatures.Length)
            {
                throw Invalid(indirectCall.Span);
            }

            var signature = program.NativeFunctionPointerSignatures[
                NativeFunctionPointerTypes.Index(type)];
            if (indirectCall.Arguments.IsDefault
                || indirectCall.Arguments.Length != signature.ParameterTypes.Length)
            {
                throw Invalid(indirectCall.Span);
            }
            var stagedCount = initialized.Dependencies is BackingDependencies stagedDependencies
                ? stagedDependencies.Staged.Count : 0;
            try
            {
                ApplyNativeOutputCalls(indirectCall.Target, initialized,
                    callFacts, ref remaining, depth + 1);
                var argumentsComplete = CanCompleteNormally(
                    indirectCall.Target, initialized, program, ref remaining, depth + 1);
                for (var index = 0; argumentsComplete
                    && index < indirectCall.Arguments.Length; index++)
                {
                    var argument = indirectCall.Arguments[index];
                    ApplyNativeOutputCalls(argument, initialized,
                        callFacts, ref remaining, depth + 1);
                    argumentsComplete = CanCompleteNormally(
                        argument, initialized, program, ref remaining, depth + 1);
                    if (!argumentsComplete)
                    {
                        break;
                    }
                    StageIndirectDependencySource(indirectCall, index, argument,
                        signature, initialized, program, ref remaining, depth + 1);
                }

                if (argumentsComplete
                    && initialized.Dependencies is BackingDependencies dependencies
                    && MayWriteNativePointerStorage(indirectCall, program))
                {
                    dependencies.MarkAllNativePointerProvenanceUnknown();
                }
                if (argumentsComplete)
                {
                    InvalidateNativePointerExtentsForCall(
                        indirectCall, signature, initialized, program);
                }
                if (argumentsComplete && !signature.OutputStates.IsEmpty)
                {
                    ApplyNativeOutputIndirectCallEffects(
                        indirectCall, signature, initialized, ref remaining);
                    if (CreateNativeOutputCallFact(indirectCall, initialized)
                        is NativeOutputCallFact fact)
                    {
                        callFacts[indirectCall] = fact;
                    }
                }
                else if (argumentsComplete)
                {
                    initialized.InvalidateNativeOutputCorrelations();
                }
            }
            finally
            {
                if (initialized.Dependencies is BackingDependencies currentDependencies)
                {
                    currentDependencies.Staged.RemoveRange(
                        stagedCount, currentDependencies.Staged.Count - stagedCount);
                }
            }
            return;
        }

        if (expression is BoundNativePointerWrite)
        {
            foreach (var nested in expression.NestedExpressions)
            {
                ApplyNativeOutputCalls(nested, initialized, callFacts, ref remaining, depth + 1);
            }
            initialized.InvalidateNativeOutputCorrelations();
            return;
        }

        foreach (var nested in expression.NestedExpressions)
        {
            ApplyNativeOutputCalls(nested, initialized, callFacts, ref remaining, depth + 1);
        }
    }

    private static bool HasNativeOutputFlowEffect(
        BoundExpression expression, InitializationState initialized, ref int remaining)
    {
        Count(ref remaining, 0);
        if (expression is BoundRead read
            && (uint)read.Variable < (uint)initialized.Length
            && initialized.NativeOutputResult(read.Variable) is not null)
        {
            return true;
        }
        if (initialized.TryGetNativeOutputFlowEffect(expression, out var cachedEffect)
            && (cachedEffect || !initialized.MayHaveNativeOutputState))
        {
            return cachedEffect;
        }

        var program = initialized.Program;
        var hasEffect = (expression is BoundCall call
            && (uint)call.Function < (uint)program.Functions.Length
            && CallMayInvalidateNativeOutputState(
                call, program.Functions[call.Function], initialized))
            || (expression is BoundIndirectCall indirectCall
                && indirectCall.Target is not null
                && NativeFunctionPointerTypes.IsType(indirectCall.Target.Type)
                && (uint)NativeFunctionPointerTypes.Index(indirectCall.Target.Type)
                    < (uint)program.NativeFunctionPointerSignatures.Length
                && !program.NativeFunctionPointerSignatures[
                    NativeFunctionPointerTypes.Index(indirectCall.Target.Type)]
                    .OutputStates.IsEmpty)
            || (initialized.MayHaveNativeOutputState
                && (expression is BoundIndirectCall or BoundNativePointerWrite));
        if (!hasEffect)
        {
            foreach (var nested in expression.NestedExpressions)
            {
                if (HasNativeOutputFlowEffect(nested, initialized, ref remaining))
                {
                    hasEffect = true;
                    break;
                }
            }
        }
        if (!initialized.MayHaveNativeOutputState)
        {
            initialized.RecordNativeOutputFlowEffect(expression, hasEffect);
        }
        return hasEffect;
    }

    private static bool HasNativePointerExtentFlowEffect(
        BoundExpression expression, InitializationState initialized, ref int remaining)
    {
        Count(ref remaining, 0);
        if (initialized.Dependencies is null)
        {
            return false;
        }

        var program = initialized.Program;
        var hasEffect = false;
        if (expression is BoundCall call
            && (uint)call.Function < (uint)program.Functions.Length)
        {
            var target = program.Functions[call.Function];
            for (var index = 0; index < call.Arguments.Length; index++)
            {
                var parameter = DirectNativeImportContract.ParameterForParameter(
                    target, call.ParameterIndex(index))
                    ?? NativeVTableCallContract.ParameterForParameter(
                        target, call.ParameterIndex(index));
                if (parameter is not null
                    && parameter.Access is NativeParameterAccess.Write
                        or NativeParameterAccess.ReadWrite
                    && CarriesNativePointer(parameter.PointeeType, program))
                {
                    hasEffect = true;
                    break;
                }
            }

            if (!hasEffect
                && initialized.NativePointerCaptureEffects is { } captureEffects
                && (uint)call.Function < (uint)captureEffects.Length)
            {
                hasEffect = captureEffects[call.Function].Count != 0;
            }
        }
        else if (expression is BoundIndirectCall { Target: { } target } indirectCall
            && NativeFunctionPointerTypes.IsType(target.Type)
            && (uint)NativeFunctionPointerTypes.Index(target.Type)
                < (uint)program.NativeFunctionPointerSignatures.Length)
        {
            hasEffect = MayWriteNativePointerStorage(indirectCall, program);
        }

        if (!hasEffect)
        {
            foreach (var nested in expression.NestedExpressions)
            {
                if (HasNativePointerExtentFlowEffect(nested, initialized, ref remaining))
                {
                    hasEffect = true;
                    break;
                }
            }
        }

        return hasEffect;
    }

    private static void ApplyNativeOutputCondition(BoundExpression expression,
        InitializationState initialized,
        Dictionary<BoundExpression, NativeOutputCallFact> callFacts,
        bool result, ref int remaining)
    {
        Count(ref remaining, 0);
        expression = UnwrapUnsafeExpression(expression);
        if (expression is BoundUnary { Operator: TokenKind.Bang } unary)
        {
            ApplyNativeOutputCondition(unary.Operand, initialized, callFacts, !result, ref remaining);
            return;
        }

        if (expression is BoundBinary binary && IsNativeOutputComparison(binary.Operator))
        {
            if (TryGetNativeOutputResultFact(binary.Left, initialized, callFacts) is NativeOutputCallFact leftFact
                && binary.Right is BoundConstant rightConstant
                && rightConstant.Type == binary.Left.Type
                && rightConstant.TryGetLegacyValue(out var rightValue) && rightValue == 0)
            {
                ApplyNativeOutputConditionFact(
                    leftFact, binary.Operator, rightValue, result, initialized);
            }
            else if (TryGetNativeOutputResultFact(binary.Right, initialized, callFacts) is NativeOutputCallFact rightFact
                && binary.Left is BoundConstant leftConstant
                && leftConstant.Type == binary.Right.Type
                && leftConstant.TryGetLegacyValue(out var leftValue) && leftValue == 0)
            {
                ApplyNativeOutputConditionFact(
                    rightFact, ReverseNativeOutputComparison(binary.Operator),
                    leftValue, result, initialized);
            }
            return;
        }

        if (expression is BoundCall call
            && (uint)call.Function < (uint)initialized.Program.Functions.Length
            && TryGetNativeOutputPredicate(call, initialized.Program,
                out var predicateParameter, out var predicateOperator, out var predicateValue))
        {
            var argumentIndex = call.ArgumentIndex(predicateParameter);
            if ((uint)argumentIndex < (uint)call.Arguments.Length
                && TryGetNativeOutputResultFact(
                    call.Arguments[argumentIndex], initialized, callFacts) is NativeOutputCallFact fact)
            {
                ApplyNativeOutputConditionFact(
                    fact, predicateOperator, predicateValue, result, initialized);
            }
        }
    }

    private static NativeOutputCallFact? TryGetNativeOutputResultFact(BoundExpression expression,
        InitializationState initialized,
        Dictionary<BoundExpression, NativeOutputCallFact> callFacts)
    {
        expression = UnwrapUnsafeExpression(expression);
        return expression switch
        {
            BoundRead read when (uint)read.Variable < (uint)initialized.Length
                => initialized.NativeOutputResult(read.Variable),
            BoundCall call when callFacts.TryGetValue(call, out var fact) => fact,
            BoundIndirectCall call when callFacts.TryGetValue(call, out var fact) => fact,
            _ => null,
        };
    }

    private static void ApplyNativeOutputConditionFact(NativeOutputCallFact fact,
        TokenKind comparison, long comparisonValue, bool result, InitializationState initialized)
    {
        foreach (var destination in fact.Destinations)
        {
            var available = (destination.SuccessSelector, destination.ComplementSelector,
                fact.ResultType, comparison, comparisonValue) switch
            {
                (NativeOutputSelector.NonZero, NativeOutputSelector.Zero, PrimitiveType.Int32,
                    TokenKind.BangEquals, 0) => result,
                (NativeOutputSelector.NonZero, NativeOutputSelector.Zero, PrimitiveType.Int32,
                    TokenKind.EqualsEquals, 0) => !result,
                (NativeOutputSelector.NonZero, NativeOutputSelector.Zero, PrimitiveType.UInt32,
                    TokenKind.BangEquals, 0) => result,
                (NativeOutputSelector.NonZero, NativeOutputSelector.Zero, PrimitiveType.UInt32,
                    TokenKind.EqualsEquals, 0) => !result,
                (NativeOutputSelector.NonNegative, NativeOutputSelector.Negative, PrimitiveType.Int32,
                    TokenKind.GreaterEquals, 0) => result,
                (NativeOutputSelector.NonNegative, NativeOutputSelector.Negative, PrimitiveType.Int32,
                    TokenKind.Less, 0) => !result,
                _ => (bool?)null,
            };
            if (available is bool isAvailable)
            {
                initialized.SetNativeOutputAvailability(destination, isAvailable);
            }
        }
    }

    private static bool TryGetNativeOutputPredicate(BoundCall call, BoundProgram program,
        out int parameterIndex, out TokenKind comparison, out long comparisonValue)
    {
        parameterIndex = -1;
        comparison = default;
        comparisonValue = 0;
        var function = program.Functions[call.Function];
        if (call.Type != PrimitiveType.Bool || function.IsExternal
            || function.ReturnType != PrimitiveType.Bool || function.ParameterCount != 1
            || function.Variables.IsDefaultOrEmpty
            || function.Variables[0].Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32)
            || function.Variables[0].IsOut || function.Variables[0].IsInit
            || function.Body is not BoundBlock body || body.Statements.IsEmpty)
        {
            return false;
        }

        if (body.Statements[^1] is not BoundReturn returnStatement
            || returnStatement.Value is null)
        {
            return false;
        }

        var localValues = new Dictionary<int, BoundExpression>();
        for (var statementIndex = 0; statementIndex < body.Statements.Length - 1; statementIndex++)
        {
            if (body.Statements[statementIndex] is not BoundLocal local
                || local.Initializer is null
                || (uint)local.Variable < (uint)function.ParameterCount
                || (uint)local.Variable >= (uint)function.Variables.Length
                || function.Variables[local.Variable].IsOut
                || function.Variables[local.Variable].IsInit
                || !IsSimpleNativeOutputPredicateExpression(local.Initializer, function.Variables.Length))
            {
                return false;
            }
            if (!localValues.TryAdd(local.Variable, local.Initializer))
            {
                return false;
            }
        }

        if (!TryResolveNativeOutputPredicateComparison(returnStatement.Value, localValues,
            out var binary, new HashSet<int>()) || binary is null)
        {
            return false;
        }
        if (IsNativeOutputPredicateParameter(binary.Left, function.Variables[0].Type,
                localValues, new HashSet<int>())
            && IsNativeOutputPredicateZero(binary.Right, function.Variables[0].Type,
                localValues, new HashSet<int>()))
        {
            parameterIndex = 0;
            comparison = binary.Operator;
            comparisonValue = 0;
        }
        else if (IsNativeOutputPredicateParameter(binary.Right, function.Variables[0].Type,
                    localValues, new HashSet<int>())
            && IsNativeOutputPredicateZero(binary.Left, function.Variables[0].Type,
                localValues, new HashSet<int>()))
        {
            parameterIndex = 0;
            comparison = ReverseNativeOutputComparison(binary.Operator);
            comparisonValue = 0;
        }
        return parameterIndex >= 0;
    }

    private static bool IsSimpleNativeOutputPredicateExpression(BoundExpression expression,
        int variableCount, int depth = 0)
    {
        if (depth > variableCount)
        {
            return false;
        }
        expression = UnwrapUnsafeExpression(expression);
        return expression switch
        {
            BoundRead read => (uint)read.Variable < (uint)variableCount,
            BoundConstant => true,
            BoundBinary binary when IsNativeOutputComparison(binary.Operator)
                => IsSimpleNativeOutputPredicateExpression(binary.Left, variableCount, depth + 1)
                    && IsSimpleNativeOutputPredicateExpression(binary.Right, variableCount, depth + 1),
            _ => false,
        };
    }

    private static bool TryResolveNativeOutputPredicateComparison(BoundExpression expression,
        Dictionary<int, BoundExpression> localValues, out BoundBinary? comparison,
        HashSet<int> resolving)
    {
        expression = UnwrapUnsafeExpression(expression);
        if (expression is BoundRead read && localValues.TryGetValue(read.Variable, out var value)
            && resolving.Add(read.Variable))
        {
            var found = TryResolveNativeOutputPredicateComparison(value, localValues,
                out comparison, resolving);
            _ = resolving.Remove(read.Variable);
            return found;
        }

        if (expression is BoundBinary binary && IsNativeOutputComparison(binary.Operator))
        {
            comparison = binary;
            return true;
        }

        comparison = null;
        return false;
    }

    private static bool IsNativeOutputPredicateParameter(BoundExpression expression, PrimitiveType type,
        Dictionary<int, BoundExpression> localValues, HashSet<int> resolving)
    {
        expression = UnwrapUnsafeExpression(expression);
        if (expression is BoundRead { Variable: 0 } parameter)
        {
            return parameter.Type == type;
        }
        return expression is BoundRead read && localValues.TryGetValue(read.Variable, out var value)
            && resolving.Add(read.Variable)
            && IsNativeOutputPredicateParameter(value, type, localValues, resolving);
    }

    private static bool IsNativeOutputPredicateZero(BoundExpression expression, PrimitiveType type,
        Dictionary<int, BoundExpression> localValues, HashSet<int> resolving)
    {
        expression = UnwrapUnsafeExpression(expression);
        if (expression is BoundConstant constant
            && constant.TryGetLegacyValue(out var constantValue) && constantValue == 0)
        {
            return constant.Type == type;
        }
        return expression is BoundRead read && localValues.TryGetValue(read.Variable, out var value)
            && resolving.Add(read.Variable)
            && IsNativeOutputPredicateZero(value, type, localValues, resolving);
    }

    private static bool IsNativeOutputComparison(TokenKind kind)
        => kind is TokenKind.EqualsEquals or TokenKind.BangEquals or TokenKind.Less
            or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals;

    private static TokenKind ReverseNativeOutputComparison(TokenKind comparison)
        => comparison switch
        {
            TokenKind.Less => TokenKind.Greater,
            TokenKind.LessEquals => TokenKind.GreaterEquals,
            TokenKind.Greater => TokenKind.Less,
            TokenKind.GreaterEquals => TokenKind.LessEquals,
            _ => comparison,
        };

    private static DirectNativeOutput? NativeOutputForParameter(BoundFunction function, int parameterIndex)
        => DirectNativeImportContract.OutputForParameter(function, parameterIndex)
            ?? NativeVTableCallContract.OutputForParameter(function, parameterIndex);

    private static NativeOutputContract? NativeOutputContractForParameter(
        BoundFunction function, int parameterIndex)
        => function.NativeOutputContracts.FirstOrDefault(contract =>
            contract.FormalOrdinal == parameterIndex);

    private static bool HasNativeOutputForParameter(BoundFunction function, int parameterIndex)
        => NativeOutputContractForParameter(function, parameterIndex) is not null
            || NativeOutputForParameter(function, parameterIndex) is not null;

    private static ImmutableArray<DirectNativeOutput> NativeOutputs(BoundFunction function)
        => function.DirectNativeImport?.Outputs
            ?? function.NativeVTableMethod?.Outputs
            ?? [];

    private static int NativeOutputCount(BoundFunction function)
        => !function.NativeOutputContracts.IsDefaultOrEmpty
            ? function.NativeOutputContracts.Length
            : NativeOutputs(function).Length;

    private static NativeOutputCallFact? CreateNativeOutputCallFact(
        BoundCall call, InitializationState initialized)
    {
        if ((uint)call.Function >= (uint)initialized.Program.Functions.Length
            || call.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32))
        {
            return null;
        }

        var target = initialized.Program.Functions[call.Function];
        var outputCount = NativeOutputCount(target);
        if (outputCount == 0)
        {
            return null;
        }

        var destinations = ImmutableArray.CreateBuilder<NativeOutputDestinationFact>(outputCount);
        for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var parameterIndex = call.ParameterIndex(argumentIndex);
            var outputContract = NativeOutputContractForParameter(target, parameterIndex);
            var legacyOutput = outputContract is null
                ? NativeOutputForParameter(target, parameterIndex)
                : null;
            if (outputContract is null && legacyOutput is null)
            {
                continue;
            }
            if (UnwrapUnsafeExpression(call.Arguments[argumentIndex])
                    is not BoundNativeOutputReservation reservation
                || (uint)reservation.Variable >= (uint)initialized.Length
                || initialized.NativeOutputDestinationVersion(reservation.Variable) is not object version
                || destinations.Any(destination => destination.Variable == reservation.Variable))
            {
                return null;
            }
            var selectors = outputContract is not null
                ? (outputContract.SuccessSelector, outputContract.ComplementSelector)
                : legacyOutput!.ResultCondition switch
                {
                    DirectNativeImportContract.Int32NonZeroResultCondition
                        or DirectNativeImportContract.UInt32NonZeroResultCondition
                        => (NativeOutputSelector.NonZero, NativeOutputSelector.Zero),
                    DirectNativeImportContract.Int32NonNegativeResultCondition
                        => (NativeOutputSelector.NonNegative, NativeOutputSelector.Negative),
                    _ => default,
                };
            if (selectors == default)
            {
                return null;
            }
            destinations.Add(new NativeOutputDestinationFact(
                reservation.Variable, selectors.Item1, selectors.Item2, version));
        }

        if (destinations.Count != outputCount)
        {
            return null;
        }

        foreach (var destination in destinations)
        {
            for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
            {
                var parameterIndex = call.ParameterIndex(argumentIndex);
                if (HasNativeOutputForParameter(target, parameterIndex))
                {
                    continue;
                }
                var nativeParameter = DirectNativeImportContract.ParameterForParameter(target, parameterIndex)
                    ?? NativeVTableCallContract.ParameterForParameter(target, parameterIndex);
                if (nativeParameter?.Access is NativeParameterAccess.Write or NativeParameterAccess.ReadWrite
                    && UnwrapUnsafeExpression(call.Arguments[argumentIndex])
                        is BoundNativeOutputReservation reservation
                    && reservation.Variable == destination.Variable)
                {
                    return null;
                }
            }
        }

        return new NativeOutputCallFact(call.Type, destinations.MoveToImmutable());
    }

    private static NativeOutputCallFact? CreateNativeOutputCallFact(
        BoundIndirectCall call, InitializationState initialized)
    {
        if (call.Target is null || call.Arguments.IsDefault
            || !NativeFunctionPointerTypes.IsType(call.Target.Type)
            || (uint)NativeFunctionPointerTypes.Index(call.Target.Type)
                >= (uint)initialized.Program.NativeFunctionPointerSignatures.Length
            || call.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32))
        {
            return null;
        }

        var signature = initialized.Program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(call.Target.Type)];
        var outputFormals = signature.OutputStates.Select(
            static state => state.FormalOrdinal).Distinct().ToArray();
        if (outputFormals.Length == 0
            || outputFormals.Length > call.Arguments.Length)
        {
            return null;
        }

        var destinations = ImmutableArray.CreateBuilder<NativeOutputDestinationFact>(
            outputFormals.Length);
        foreach (var formal in outputFormals)
        {
            var formalIndex = checked((int)formal);
            if (formalIndex >= call.Arguments.Length
                || !NativeFunctionPointerContract.TryGetNativeOutputContract(
                    signature, formalIndex, initialized.Program.NativePointerTypes,
                    out var outputContract)
                || outputContract is null
                || UnwrapUnsafeExpression(call.Arguments[formalIndex])
                    is not BoundNativeOutputReservation reservation
                || (uint)reservation.Variable >= (uint)initialized.Length
                || initialized.NativeOutputDestinationVersion(reservation.Variable) is not object version
                || destinations.Any(destination => destination.Variable == reservation.Variable))
            {
                return null;
            }

            destinations.Add(new NativeOutputDestinationFact(
                reservation.Variable, outputContract.SuccessSelector,
                outputContract.ComplementSelector, version));
        }

        foreach (var destination in destinations)
        {
            foreach (var parameter in signature.NativeParameters)
            {
                if (parameter.Access is not (NativeParameterAccess.Write
                    or NativeParameterAccess.ReadWrite))
                {
                    continue;
                }

                var argument = UnwrapUnsafeExpression(
                    call.Arguments[parameter.ParameterIndex]);
                if (argument is BoundNativeOutputReservation reservation
                    && reservation.Variable == destination.Variable)
                {
                    return null;
                }
                if (argument is BoundNativeStorageAddress address
                    && address.Variable == destination.Variable)
                {
                    return null;
                }
            }
        }

        return new NativeOutputCallFact(call.Type, destinations.MoveToImmutable());
    }

    private static bool MatchesReservation(BoundNativeOutputReservation reservation,
        NativeOutputContract output, BoundFunction caller, BoundProgram program) =>
        reservation.Type == PrimitiveType.Void
        && reservation.PointeeType == output.PointeeType
        && DirectNativeImportContract.IsNativeOutputCellType(output.PointeeType, program.Structures,
            program.NativeFunctionPointerSignatures, program.NativePointerTypes)
        && (uint)reservation.Variable < (uint)caller.Variables.Length
        && caller.Variables[reservation.Variable].Type == output.PointeeType
        && caller.Variables[reservation.Variable].IsInitializationLocal
        && ValueLayout.Size(output.PointeeType, program.Structures,
            program.CheckedReferenceTypes, program.NativeLoweringAgreement) > 0;

    private static void VerifyNativeOutputReservation(BoundNativeOutputReservation reservation,
        DirectNativeOutput output, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var firstLocal = function.ParameterCount + (function.IsConstructor ? 1 : 0);
        if (!DirectNativeImportContract.MatchesReservation(reservation, output, function, program)
            || reservation.Variable < firstLocal || !declarations.Contains(reservation.Variable)
            || !function.Variables[reservation.Variable].IsInitializationLocal
            || !initialized.IsVacant(reservation.Variable)
            || fieldMasks[reservation.Variable] != 0
            || (!function.Variables[reservation.Variable].IsMutable
                && initialized.HasRetiredValue(reservation.Variable)))
        {
            throw InvalidEffect(reservation.Span, "NativeImport");
        }

        initialized.ReserveNativeOutput(reservation.Variable);
    }

    private static void VerifyNativeOutputReservation(BoundNativeOutputReservation reservation,
        NativeOutputContract output, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var firstLocal = function.ParameterCount + (function.IsConstructor ? 1 : 0);
        if (!MatchesReservation(reservation, output, function, program)
            || reservation.Variable < firstLocal || !declarations.Contains(reservation.Variable)
            || !initialized.IsVacant(reservation.Variable)
            || fieldMasks[reservation.Variable] != 0
            || (!function.Variables[reservation.Variable].IsMutable
                && initialized.HasRetiredValue(reservation.Variable)))
        {
            throw InvalidEffect(reservation.Span, "NativeImport");
        }

        initialized.ReserveNativeOutput(reservation.Variable);
    }

    private static void VerifyDirectNativeParameterAddress(BoundNativeStorageAddress address,
        DirectNativeParameter parameter, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var firstLocal = function.ParameterCount + (function.IsConstructor ? 1 : 0);
        if (!DirectNativeImportContract.MatchesAddress(address, parameter, function, program)
            || address.Variable < firstLocal || !declarations.Contains(address.Variable)
            || !initialized[address.Variable])
        {
            throw InvalidEffect(address.Span, "NativeImport");
        }

        if (NativeFixedArrayContract.IsCarrier(address.CarrierType))
        {
            VerifyNativeFixedArrayPlace(address.Variable, address.Index,
                address.CarrierType,
                parameter.Access == NativeParameterAccess.Read,
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures);
            return;
        }

        if (HasFields(program, address.ElementType)
            && fieldMasks[address.Variable] != FullMask(program, address.ElementType))
        {
            throw InvalidEffect(address.Span, "NativeImport");
        }

        Expression(address.Index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }
}
