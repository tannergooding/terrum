// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private sealed record NativeOutputDestinationState(object? Version);

    private sealed record NativeOutputDestinationFact(
        int Variable, uint ResultCondition, object Version);

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
            _ => null,
        };
        return fact is not null
            && fact.Destinations.Any(destination => destination.Variable == resultVariable)
                ? null : fact;
    }

    private static void ApplyNativeOutputCallEffects(BoundCall call, BoundFunction target,
        InitializationState initialized)
    {
        if (CallMayInvalidateNativeOutputState(call, target, initialized))
        {
            initialized.InvalidateNativeOutputCorrelations();
        }

        for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var parameterIndex = call.ParameterIndex(argumentIndex);
            var output = NativeOutputForParameter(target, parameterIndex);
            if (output is null
                || UnwrapUnsafeExpression(call.Arguments[argumentIndex])
                    is not BoundNativeStorageAddress address)
            {
                continue;
            }

            initialized.MarkNativeOutputPending(address.Variable);
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
            if (argument is BoundNativeStorageAddress)
            {
                if (NativeOutputForParameter(target, parameterIndex) is not null)
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
                || argument.Type == PrimitiveType.NativeFixedArray
                || HasFields(program, argument.Type))
            {
                return initialized.MayHaveNativeOutputState;
            }
        }

        return NativeOutputs(target).Length > 0;
    }

    private static void ApplyNativeOutputCalls(BoundExpression expression, InitializationState initialized,
        Dictionary<BoundCall, NativeOutputCallFact> callFacts, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        var program = initialized.Program;
        if (expression is BoundCall call)
        {
            foreach (var argument in call.Arguments)
            {
                ApplyNativeOutputCalls(argument, initialized, callFacts, ref remaining, depth + 1);
            }

            if ((uint)call.Function < (uint)program.Functions.Length)
            {
                var target = program.Functions[call.Function];
                ApplyNativeOutputCallEffects(call, target, initialized);
                if (CreateNativeOutputCallFact(call, initialized) is NativeOutputCallFact fact)
                {
                    callFacts[call] = fact;
                }
            }
            return;
        }

        if (expression is BoundIndirectCall)
        {
            foreach (var nested in expression.NestedExpressions)
            {
                ApplyNativeOutputCalls(nested, initialized, callFacts, ref remaining, depth + 1);
            }
            initialized.InvalidateNativeOutputCorrelations();
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

    private static void ApplyNativeOutputCondition(BoundExpression expression,
        InitializationState initialized, Dictionary<BoundCall, NativeOutputCallFact> callFacts,
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
                && binary.Right is BoundConstant { Value: 0 } rightConstant
                && rightConstant.Type == binary.Left.Type)
            {
                ApplyNativeOutputConditionFact(
                    leftFact, binary.Operator, rightConstant.Value, result, initialized);
            }
            else if (TryGetNativeOutputResultFact(binary.Right, initialized, callFacts) is NativeOutputCallFact rightFact
                && binary.Left is BoundConstant { Value: 0 } leftConstant
                && leftConstant.Type == binary.Right.Type)
            {
                ApplyNativeOutputConditionFact(
                    rightFact, ReverseNativeOutputComparison(binary.Operator),
                    leftConstant.Value, result, initialized);
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
        InitializationState initialized, Dictionary<BoundCall, NativeOutputCallFact> callFacts)
    {
        expression = UnwrapUnsafeExpression(expression);
        return expression switch
        {
            BoundRead read when (uint)read.Variable < (uint)initialized.Length
                => initialized.NativeOutputResult(read.Variable),
            BoundCall call when callFacts.TryGetValue(call, out var fact) => fact,
            _ => null,
        };
    }

    private static void ApplyNativeOutputConditionFact(NativeOutputCallFact fact,
        TokenKind comparison, long comparisonValue, bool result, InitializationState initialized)
    {
        foreach (var destination in fact.Destinations)
        {
            var available = (destination.ResultCondition, fact.ResultType, comparison, comparisonValue) switch
            {
                (DirectNativeImportContract.Int32NonZeroResultCondition, PrimitiveType.Int32,
                    TokenKind.BangEquals, 0) => result,
                (DirectNativeImportContract.Int32NonZeroResultCondition, PrimitiveType.Int32,
                    TokenKind.EqualsEquals, 0) => !result,
                (DirectNativeImportContract.UInt32NonZeroResultCondition, PrimitiveType.UInt32,
                    TokenKind.BangEquals, 0) => result,
                (DirectNativeImportContract.UInt32NonZeroResultCondition, PrimitiveType.UInt32,
                    TokenKind.EqualsEquals, 0) => !result,
                (DirectNativeImportContract.Int32NonNegativeResultCondition, PrimitiveType.Int32,
                    TokenKind.GreaterEquals, 0) => result,
                (DirectNativeImportContract.Int32NonNegativeResultCondition, PrimitiveType.Int32,
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
        if (expression is BoundConstant { Value: 0 } constant)
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

    private static ImmutableArray<DirectNativeOutput> NativeOutputs(BoundFunction function)
        => function.DirectNativeImport?.Outputs
            ?? function.NativeVTableMethod?.Outputs
            ?? [];

    private static NativeOutputCallFact? CreateNativeOutputCallFact(
        BoundCall call, InitializationState initialized)
    {
        if ((uint)call.Function >= (uint)initialized.Program.Functions.Length
            || call.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32))
        {
            return null;
        }

        var target = initialized.Program.Functions[call.Function];
        var outputs = NativeOutputs(target);
        if (outputs.IsDefaultOrEmpty)
        {
            return null;
        }

        var destinations = ImmutableArray.CreateBuilder<NativeOutputDestinationFact>(outputs.Length);
        for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var output = NativeOutputForParameter(target, call.ParameterIndex(argumentIndex));
            if (output is null)
            {
                continue;
            }
            if (UnwrapUnsafeExpression(call.Arguments[argumentIndex])
                    is not BoundNativeStorageAddress address
                || (uint)address.Variable >= (uint)initialized.Length
                || initialized.NativeOutputDestinationVersion(address.Variable) is not object version
                || destinations.Any(destination => destination.Variable == address.Variable))
            {
                return null;
            }
            destinations.Add(new NativeOutputDestinationFact(
                address.Variable, output.ResultCondition, version));
        }

        if (destinations.Count != outputs.Length)
        {
            return null;
        }

        foreach (var destination in destinations)
        {
            for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
            {
                var parameterIndex = call.ParameterIndex(argumentIndex);
                if (NativeOutputForParameter(target, parameterIndex) is not null)
                {
                    continue;
                }
                var nativeParameter = DirectNativeImportContract.ParameterForParameter(target, parameterIndex)
                    ?? NativeVTableCallContract.ParameterForParameter(target, parameterIndex);
                if (nativeParameter?.Access is NativeParameterAccess.Write or NativeParameterAccess.ReadWrite
                    && UnwrapUnsafeExpression(call.Arguments[argumentIndex])
                        is BoundNativeStorageAddress address
                    && address.Variable == destination.Variable)
                {
                    return null;
                }
            }
        }

        return new NativeOutputCallFact(call.Type, destinations.MoveToImmutable());
    }

    private static void VerifyDirectNativeOutputAddress(BoundNativeStorageAddress address,
        DirectNativeOutput output, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        var firstLocal = function.ParameterCount + (function.IsConstructor ? 1 : 0);
        if (!DirectNativeImportContract.MatchesAddress(address, output, function, program)
            || address.Variable < firstLocal || !declarations.Contains(address.Variable)
            || !initialized[address.Variable])
        {
            throw InvalidEffect(address.Span, "NativeImport");
        }

        Expression(address.Index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
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

        if (address.CarrierType == PrimitiveType.NativeFixedArray)
        {
            VerifyNativeFixedArrayPlace(address.Variable, address.Index, PrimitiveType.NativeFixedArray,
                0, parameter.Access == NativeParameterAccess.Read,
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
