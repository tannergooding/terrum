// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record TrRmScalarEdge(int Target, ImmutableArray<int> Arguments);

/// <summary>An audited, equal-width integer bit reinterpretation; never a structured source cast.</summary>
internal sealed record TrRmScalarReinterpret(BoundExpression Operand, PrimitiveType Type)
    : BoundExpression(Type, default);

internal sealed record TrRmScalarConvert(BoundExpression Operand, PrimitiveType Type, byte Policy)
    : BoundExpression(Type, default);

internal sealed record TrRmScalarBlock(ImmutableArray<int> Parameters,
    ImmutableArray<BoundStatement> Operations, int Condition, TrRmScalarEdge? True,
    TrRmScalarEdge? False, int ReturnValue, int FailureReason);

/// <summary>Immutable, source-free scalar SSA body admitted only after compact Body validation.</summary>
internal sealed class TrRmScalarGraph
{
    internal ImmutableArray<TrRmScalarBlock> Blocks { get; }
    internal ImmutableArray<PrimitiveType> Types { get; }
    internal int ParameterCount { get; }
    internal PrimitiveType ResultType { get; }
    internal bool NoThrow { get; }
    internal bool DoesNotReturn { get; }

    private TrRmScalarGraph(ImmutableArray<TrRmScalarBlock> blocks,
        ImmutableArray<PrimitiveType> types, int parameterCount,
        PrimitiveType resultType, bool noThrow, bool doesNotReturn)
    {
        Blocks = blocks;
        Types = types;
        ParameterCount = parameterCount;
        ResultType = resultType;
        NoThrow = noThrow;
        DoesNotReturn = doesNotReturn;
    }

    internal static TrRmScalarGraph FromAuditedBody(ImmutableArray<TrRmScalarBlock> blocks,
        ImmutableArray<PrimitiveType> types, int parameterCount,
        PrimitiveType resultType, bool noThrow, bool doesNotReturn)
        => new(blocks, types, parameterCount, resultType, noThrow, doesNotReturn);

    internal void Verify(BoundProgram program, BoundFunction function, ref int remaining)
    {
        if (program.RuntimePolicy != BoundRuntimePolicy.FailFast || function.IsExternal
            || function.DoesNotReturn != DoesNotReturn || function.IsConstructor || function.IsClassConstructor
            || function.ClassConstructor != -1 || function.DeclaringStruct is not null
            || function.IsStatic || function.IsCleanup || function.PropertyName is not null
            || function.Intrinsic != BoundIntrinsic.None
            || function.ParameterCount != ParameterCount || function.ReturnType != ResultType
            || function.IsNoThrow != NoThrow || function.Variables.Length != Types.Length
            || Blocks.IsDefaultOrEmpty || Blocks.Length > SemanticVerifier.MaximumAnalysisVisits
            || Types.Length > SemanticVerifier.MaximumVariables)
        {
            throw new ArgumentException("Indexed scalar Body does not match its declaration.");
        }
        for (var value = 0; value < Types.Length; value++)
        {
            if (function.Variables[value].Type != Types[value]
                || function.Variables[value].IsOut || function.Variables[value].IsInit
                || Types[value] == PrimitiveType.Void)
            {
                throw new ArgumentException("Indexed scalar value inventory does not match its declaration.");
            }
        }
        if (function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty
            || function.Body.IsManagedLexicalScope || !function.Body.RetiredManagedLocals.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Indexed scalar Body cannot also contain structured statements.");
        }
        foreach (var block in Blocks)
        {
            Visit(ref remaining);
            for (var operationIndex = 0; operationIndex < block.Operations.Length; operationIndex++)
            {
                var operation = block.Operations[operationIndex];
                Visit(ref remaining);
                var call = operation switch
                {
                    BoundLocal { Initializer: BoundCall value } => value,
                    BoundExpressionStatement { Expression: BoundCall value } => value,
                    _ => null,
                };
                if (call is null)
                {
                    continue;
                }
                var hasNonPositionalArguments = false;
                if (!call.ArgumentParameterIndices.IsDefault)
                {
                    for (var index = 0; index < call.ArgumentParameterIndices.Length; index++)
                    {
                        if (call.ArgumentParameterIndices[index] != index)
                        {
                            hasNonPositionalArguments = true;
                            break;
                        }
                    }
                }
                if (hasNonPositionalArguments)
                {
                    throw new ArgumentException("Indexed calls require source-order and parameter-order arguments to match.");
                }
                if ((uint)call.Function >= (uint)program.Functions.Length)
                {
                    throw new ArgumentException("Indexed call target is outside the module.");
                }
                var target = program.Functions[call.Function];
                if (target is null || target.Variables.IsDefault
                    || target.Variables.Length < call.Arguments.Length
                    || target.IsExternal || target.IsConstructor
                    || target.IsClassConstructor || target.ClassConstructor != -1
                    || target.DeclaringStruct is not null || target.IsStatic || target.IsCleanup
                    || target.PropertyName is not null || target.Intrinsic != BoundIntrinsic.None
                    || target.ReturnType != call.Type || target.ParameterCount != call.Arguments.Length
                    || function.IsNoThrow && !target.IsNoThrow)
                {
                    throw new ArgumentException("Indexed call target declaration does not match.");
                }
                if (target.DoesNotReturn
                    && (operationIndex != block.Operations.Length - 1
                        || operation is not BoundExpressionStatement
                        || block.Condition != 0 || block.True is not null || block.False is not null
                        || block.FailureReason != 1))
                {
                    throw new ArgumentException("A non-returning call must terminate its indexed block.");
                }
                for (var index = 0; index < call.Arguments.Length; index++)
                {
                    Visit(ref remaining);
                    var parameter = target.Variables[index];
                    if (parameter.IsOut || parameter.IsInit
                        || parameter.Type != call.Arguments[index].Type)
                    {
                        throw new ArgumentException("Indexed call argument declaration does not match.");
                    }
                }
            }
            if (block.FailureReason is < 0 or > 4
                || (block.FailureReason != 0 && (block.Condition != 0
                    || block.True is not null || block.False is not null))
                || (NoThrow && block.FailureReason != 0
                    && !HasTerminalDoesNotReturnCall(block, program)))
            {
                throw new ArgumentException("Indexed failure terminator violates the declared effects.");
            }
        }
        if (DoesNotReturn)
        {
            VerifyDoesNotReturn(program, ref remaining);
        }
    }

    private void VerifyDoesNotReturn(BoundProgram program, ref int remaining)
    {
        var reachable = new bool[Blocks.Length];
        var queued = new bool[Blocks.Length];
        var hasIncoming = new bool[Types.Length + 1];
        var booleanConstants = new bool?[Types.Length + 1];
        var pending = new Queue<int>();
        reachable[0] = true;
        queued[0] = true;
        pending.Enqueue(0);
        while (pending.TryDequeue(out var index))
        {
            queued[index] = false;
            Visit(ref remaining);
            var block = Blocks[index];
            foreach (var operation in block.Operations)
            {
                Visit(ref remaining);
                if (operation is BoundLocal { Initializer: var initializer } local
                    && local.Variable >= 0 && local.Variable < Types.Length)
                {
                    booleanConstants[local.Variable + 1] =
                        EvaluateBoolean(initializer, booleanConstants);
                }
            }
            if (block.FailureReason != 0 || HasTerminalDoesNotReturnCall(block, program))
            {
                continue;
            }
            if (block.Condition != 0)
            {
                if (block.Condition < 1 || block.Condition >= booleanConstants.Length)
                {
                    throw new ArgumentException("Indexed branch condition is outside the value inventory.");
                }
                var constant = block.Condition < booleanConstants.Length
                    ? booleanConstants[block.Condition] : null;
                if (constant is not false)
                {
                    Enqueue(block.True, ref remaining);
                }
                if (constant is not true)
                {
                    Enqueue(block.False, ref remaining);
                }
            }
            else if (block.True is not null)
            {
                Enqueue(block.True, ref remaining);
            }
            else
            {
                if (DoesNotReturn)
                {
                    throw new ArgumentException("DoesNotReturn body has a reachable normal return.");
                }
            }
        }

        void Enqueue(TrRmScalarEdge? edge, ref int remaining)
        {
            if (edge is null || edge.Target < 1 || edge.Target > Blocks.Length)
            {
                throw new ArgumentException("Indexed control-flow edge is outside the Body.");
            }
            var target = edge.Target - 1;
            var targetParameters = Blocks[target].Parameters;
            if (targetParameters.Length != edge.Arguments.Length)
            {
                throw new ArgumentException("Indexed edge argument count does not match its target.");
            }
            var changed = false;
            for (var parameterIndex = 0; parameterIndex < targetParameters.Length; parameterIndex++)
            {
                Visit(ref remaining);
                var parameter = targetParameters[parameterIndex];
                if (parameter < 1 || parameter >= booleanConstants.Length)
                {
                    throw new ArgumentException("Indexed block parameter is outside the value inventory.");
                }
                var argument = edge.Arguments[parameterIndex];
                var constant = argument >= 1 && argument < booleanConstants.Length
                    ? booleanConstants[argument] : null;
                if (!hasIncoming[parameter])
                {
                    hasIncoming[parameter] = true;
                    booleanConstants[parameter] = constant;
                    changed |= constant is not null;
                }
                else if (booleanConstants[parameter] is bool previous && previous != constant)
                {
                    booleanConstants[parameter] = null;
                    changed = true;
                }
            }
            if (!reachable[target])
            {
                reachable[target] = true;
                changed = true;
            }
            if (changed && !queued[target])
            {
                queued[target] = true;
                pending.Enqueue(target);
            }
        }
    }

    private static bool HasTerminalDoesNotReturnCall(TrRmScalarBlock block, BoundProgram program)
    {
        if (block.Operations.IsDefaultOrEmpty
            || block.Operations[^1] is not BoundExpressionStatement { Expression: BoundCall call }
            || (uint)call.Function >= (uint)program.Functions.Length)
        {
            return false;
        }
        var target = program.Functions[call.Function];
        return target is not null && target.DoesNotReturn;
    }

    private static bool? EvaluateBoolean(BoundExpression expression, bool?[] known)
    {
        switch (expression)
        {
            case BoundConstant { Type: PrimitiveType.Bool } constant:
                return constant.Value != 0;
            case BoundRead read when read.Variable >= 0 && read.Variable + 1 < known.Length:
                return known[read.Variable + 1];
            case BoundConditional conditional:
                var condition = EvaluateBoolean(conditional.Condition, known);
                if (condition is bool selected)
                {
                    return EvaluateBoolean(selected ? conditional.WhenTrue : conditional.WhenFalse, known);
                }
                var whenTrue = EvaluateBoolean(conditional.WhenTrue, known);
                var whenFalse = EvaluateBoolean(conditional.WhenFalse, known);
                return whenTrue == whenFalse ? whenTrue : null;
            case BoundUnary { Operator: TokenKind.Bang } unary:
                return EvaluateBoolean(unary.Operand, known) is bool operand ? !operand : null;
            case BoundBinary { Operator: TokenKind.AmpersandAmpersand } andBinary:
                var leftAnd = EvaluateBoolean(andBinary.Left, known);
                var rightAnd = EvaluateBoolean(andBinary.Right, known);
                return leftAnd == false || rightAnd == false ? false
                    : leftAnd == true && rightAnd == true ? true : null;
            case BoundBinary { Operator: TokenKind.PipePipe } orBinary:
                var leftOr = EvaluateBoolean(orBinary.Left, known);
                var rightOr = EvaluateBoolean(orBinary.Right, known);
                return leftOr == true || rightOr == true ? true
                    : leftOr == false && rightOr == false ? false : null;
            case BoundBinary { Operator: TokenKind.EqualsEquals } equalBinary:
                var leftEqual = EvaluateBoolean(equalBinary.Left, known);
                var rightEqual = EvaluateBoolean(equalBinary.Right, known);
                return leftEqual is bool leftEqualValue && rightEqual is bool rightEqualValue
                    ? leftEqualValue == rightEqualValue : null;
            case BoundBinary { Operator: TokenKind.BangEquals } unequalBinary:
                var leftUnequal = EvaluateBoolean(unequalBinary.Left, known);
                var rightUnequal = EvaluateBoolean(unequalBinary.Right, known);
                return leftUnequal is bool leftUnequalValue && rightUnequal is bool rightUnequalValue
                    ? leftUnequalValue != rightUnequalValue : null;
            default:
                return null;
        }
    }

    private static void Visit(ref int remaining)
    {
        if (--remaining < 0)
        {
            throw new NotSupportedException("Scalar graph exceeds the native verification visit limit.");
        }
    }
}
