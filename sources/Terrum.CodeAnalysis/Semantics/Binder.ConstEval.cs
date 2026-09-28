// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private const int ConstEvalDepthLimit = 64;
    private const int ConstEvalWorkLimit = 4096;

    private static bool ConstEvalScalar(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64;

    private bool ResolveRequiredConstants(ImmutableArray<BoundFunction>.Builder functions)
    {
        for (var index = 0; index < _signatures.Count; index++)
        {
            if (_signatures[index].IsConstEval)
            {
                SetContext(_signatures[index].Tree);
                if (!CheckConstStatement(functions[index].Body, out var unsupported))
                {
                    Report("TRM3024", "ConstEval bodies support only scalar locals, assignments, branches, loops, returns, and calls to ConstEval functions.",
                        unsupported);
                }
            }
        }
        if (_diagnostics.Count != 0)
        {
            return false;
        }
        if (_constRequired.Count == 0)
        {
            return true;
        }

        for (var index = 0; index < functions.Count; index++)
        {
            SetContext(_signatures[index].Tree);
            var locals = new Dictionary<int, BoundConstant>();
            functions[index] = functions[index] with {
                Body = (BoundBlock)ResolveConstStatement(functions[index].Body, locals, functions,
                    functions[index].Variables.Length),
            };
        }
        return _diagnostics.Count == 0;
    }

    private bool CheckConstStatement(BoundStatement statement, out TextSpan unsupported)
    {
        unsupported = statement.Span;
        switch (statement)
        {
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    if (!CheckConstStatement(child, out unsupported))
                    {
                        return false;
                    }
                }
                return true;
            case BoundLocal local:
                return CheckConstExpression(local.Initializer, out unsupported);
            case BoundAssignment assignment:
                return CheckConstExpression(assignment.Value, out unsupported);
            case BoundExpressionStatement expression:
                return CheckConstExpression(expression.Expression, out unsupported);
            case BoundIf conditional:
                return CheckConstExpression(conditional.Condition, out unsupported)
                    && CheckConstStatement(conditional.Then, out unsupported)
                    && (conditional.Else is null || CheckConstStatement(conditional.Else, out unsupported));
            case BoundWhile loop:
                return CheckConstExpression(loop.Condition, out unsupported)
                    && CheckConstStatement(loop.Body, out unsupported)
                    && (loop.Iterator is null || CheckConstStatement(loop.Iterator, out unsupported));
            case BoundReturn { Value: BoundExpression value }:
                return CheckConstExpression(value, out unsupported);
            case BoundBreak or BoundContinue:
                return true;
            default:
                return false;
        }
    }

    private bool CheckConstExpression(BoundExpression expression, out TextSpan unsupported)
    {
        unsupported = expression.Span;
        switch (expression)
        {
            case BoundConstant or BoundRead:
                return true;
            case BoundUnary unary:
                return CheckConstExpression(unary.Operand, out unsupported);
            case BoundBinary binary:
                return CheckConstExpression(binary.Left, out unsupported)
                    && CheckConstExpression(binary.Right, out unsupported);
            case BoundCast cast:
                return CheckConstExpression(cast.Operand, out unsupported);
            case BoundCall call when call.Function >= 0 && call.Function < _signatures.Count
                && _signatures[call.Function].IsConstEval:
                foreach (var argument in call.Arguments)
                {
                    if (!CheckConstExpression(argument, out unsupported))
                    {
                        return false;
                    }
                }
                return true;
            default:
                return false;
        }
    }

    private BoundStatement ResolveConstStatement(BoundStatement statement, Dictionary<int, BoundConstant> locals,
        IReadOnlyList<BoundFunction> functions, int variableCount)
    {
        switch (statement)
        {
            case BoundBlock block:
            {
                var statements = ImmutableArray.CreateBuilder<BoundStatement>(block.Statements.Length);
                foreach (var child in block.Statements)
                {
                    statements.Add(ResolveConstStatement(child, locals, functions, variableCount));
                }
                return block with { Statements = statements.MoveToImmutable() };
            }
            case BoundLocal local when _constRequired.Contains(local):
            {
                try
                {
                    var state = new ConstEvalState(_cancellationToken);
                    var slots = new BoundConstant?[variableCount];
                    foreach (var (variable, value) in locals)
                    {
                        slots[variable] = value;
                    }
                    var constant = EvaluateConstExpression(local.Initializer, slots, functions, 0, state);
                    locals.Add(local.Variable, constant);
                    return local with { Initializer = constant with { Span = local.Initializer.Span } };
                }
                catch (ConstEvalFailure failure)
                {
                    Report("TRM3024", failure.Message, local.Initializer.Span);
                    return local;
                }
            }
            case BoundIf conditional:
                return conditional with {
                    Then = ResolveConstStatement(conditional.Then, locals, functions, variableCount),
                    Else = conditional.Else is null ? null
                        : ResolveConstStatement(conditional.Else, locals, functions, variableCount),
                };
            case BoundWhile loop:
                return loop with {
                    Body = ResolveConstStatement(loop.Body, locals, functions, variableCount),
                    Iterator = loop.Iterator is null ? null
                        : ResolveConstStatement(loop.Iterator, locals, functions, variableCount),
                };
            case BoundTryFinally region:
                return region with {
                    Body = (BoundBlock)ResolveConstStatement(region.Body, locals, functions, variableCount),
                    Finally = (BoundBlock)ResolveConstStatement(region.Finally, locals, functions, variableCount),
                };
            case BoundTryCatch region:
                return region with {
                    Body = (BoundBlock)ResolveConstStatement(region.Body, locals, functions, variableCount),
                    Catch = (BoundBlock)ResolveConstStatement(region.Catch, locals, functions, variableCount),
                    Finally = region.Finally is null ? null
                        : (BoundBlock)ResolveConstStatement(region.Finally, locals, functions, variableCount),
                };
            case BoundUnsafe region:
                return region with { Body = (BoundBlock)ResolveConstStatement(region.Body, locals, functions, variableCount) };
            case BoundViewScope view:
                return view with { Body = (BoundBlock)ResolveConstStatement(view.Body, locals, functions, variableCount) };
            default:
                return statement;
        }
    }

    private BoundConstant EvaluateConstExpression(BoundExpression expression, BoundConstant?[] slots,
        IReadOnlyList<BoundFunction> functions, int depth, ConstEvalState state)
    {
        state.Step();
        switch (expression)
        {
            case BoundConstant constant:
                return constant;
            case BoundRead read when slots[read.Variable] is BoundConstant value:
                return value;
            case BoundUnary unary:
            {
                var operand = EvaluateConstExpression(unary.Operand, slots, functions, depth, state);
                return FoldScalarConstant(unary with { Operand = operand }) as BoundConstant
                    ?? throw new ConstEvalFailure("ConstEval checked arithmetic failed.");
            }
            case BoundBinary binary:
            {
                var left = EvaluateConstExpression(binary.Left, slots, functions, depth, state);
                if ((binary.Operator == TokenKind.AmpersandAmpersand && left.Value == 0)
                    || (binary.Operator == TokenKind.PipePipe && left.Value != 0))
                {
                    return new BoundConstant(left.Value == 0 ? 0 : 1, PrimitiveType.Bool, expression.Span);
                }
                var right = EvaluateConstExpression(binary.Right, slots, functions, depth, state);
                return FoldScalarConstant(binary with { Left = left, Right = right }) as BoundConstant
                    ?? throw new ConstEvalFailure("ConstEval checked arithmetic failed (overflow or division by zero).");
            }
            case BoundCast cast:
            {
                var operand = EvaluateConstExpression(cast.Operand, slots, functions, depth, state);
                return FoldScalarConstant(cast with { Operand = operand }) as BoundConstant
                    ?? throw new ConstEvalFailure("ConstEval checked conversion failed.");
            }
            case BoundCall call when call.Function >= 0 && call.Function < _signatures.Count
                && _signatures[call.Function].IsConstEval:
            {
                if (depth >= ConstEvalDepthLimit)
                {
                    throw new ConstEvalFailure("ConstEval call depth limit exceeded.");
                }
                var callee = functions[call.Function];
                var arguments = new BoundConstant?[callee.Variables.Length];
                for (var index = 0; index < call.Arguments.Length; index++)
                {
                    arguments[index] = EvaluateConstExpression(call.Arguments[index], slots, functions, depth, state);
                }
                var result = EvaluateConstStatement(callee.Body, arguments, functions, depth + 1, state);
                return result.Flow == ConstFlow.Return && result.Value is BoundConstant returned
                    ? returned : throw new ConstEvalFailure("ConstEval function did not return a value.");
            }
            default:
                throw new ConstEvalFailure("A required constant needs scalar values and ConstEval calls only.");
        }
    }

    private ConstResult EvaluateConstStatement(BoundStatement statement, BoundConstant?[] slots,
        IReadOnlyList<BoundFunction> functions, int depth, ConstEvalState state)
    {
        state.Step();
        switch (statement)
        {
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    var result = EvaluateConstStatement(child, slots, functions, depth, state);
                    if (result.Flow != ConstFlow.Next)
                    {
                        return result;
                    }
                }
                return default;
            case BoundLocal local:
                slots[local.Variable] = EvaluateConstExpression(local.Initializer, slots, functions, depth, state);
                return default;
            case BoundAssignment assignment:
                slots[assignment.Variable] = EvaluateConstExpression(assignment.Value, slots, functions, depth, state);
                return default;
            case BoundExpressionStatement expression:
                _ = EvaluateConstExpression(expression.Expression, slots, functions, depth, state);
                return default;
            case BoundIf conditional:
                return EvaluateConstExpression(conditional.Condition, slots, functions, depth, state).Value != 0
                    ? EvaluateConstStatement(conditional.Then, slots, functions, depth, state)
                    : conditional.Else is not null
                        ? EvaluateConstStatement(conditional.Else, slots, functions, depth, state) : default;
            case BoundWhile loop:
                while (EvaluateConstExpression(loop.Condition, slots, functions, depth, state).Value != 0)
                {
                    var result = EvaluateConstStatement(loop.Body, slots, functions, depth, state);
                    if (result.Flow is ConstFlow.Return)
                    {
                        return result;
                    }
                    if (result.Flow is ConstFlow.Break)
                    {
                        break;
                    }
                    if (loop.Iterator is not null)
                    {
                        result = EvaluateConstStatement(loop.Iterator, slots, functions, depth, state);
                        if (result.Flow is ConstFlow.Return)
                        {
                            return result;
                        }
                        if (result.Flow is ConstFlow.Break)
                        {
                            break;
                        }
                    }
                }
                return default;
            case BoundReturn { Value: BoundExpression value }:
                return new ConstResult(ConstFlow.Return,
                    EvaluateConstExpression(value, slots, functions, depth, state));
            case BoundBreak:
                return new ConstResult(ConstFlow.Break, null);
            case BoundContinue:
                return new ConstResult(ConstFlow.Continue, null);
            default:
                throw new ConstEvalFailure("Unsupported ConstEval statement.");
        }
    }

    private enum ConstFlow { Next, Return, Break, Continue }

    private readonly record struct ConstResult(ConstFlow Flow, BoundConstant? Value);

    private sealed class ConstEvalState(CancellationToken cancellationToken)
    {
        private int _remaining = ConstEvalWorkLimit;

        internal void Step()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--_remaining < 0)
            {
                throw new ConstEvalFailure("ConstEval work limit exceeded.");
            }
        }
    }

    private sealed class ConstEvalFailure(string message) : Exception(message);
}
