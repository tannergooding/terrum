// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    private const int ConstEvalDepthLimit = 64;
    private const int ConstEvalWorkLimit = 4096;
    private bool _hasRequiredExpression;
    private bool _hasStaticProof;

    private sealed record BoundRequiredConstant(BoundExpression Operand, TextSpan Span)
        : BoundExpression(Operand.Type, Span);

    private sealed record BoundStaticProof(BoundExpression Condition, bool IsEnsure, TextSpan Span)
        : BoundStatement(Span);

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
        if (_constRequired.Count == 0 && !_hasRequiredExpression && !_hasStaticProof)
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
            case BoundStaticProof proof:
                return CheckConstExpression(proof.Condition, out unsupported);
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
            case BoundRequiredConstant required:
                return CheckConstExpression(required.Operand, out unsupported);
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
                    var resolved = ResolveConstStatement(child, locals, functions, variableCount);
                    if (child is not BoundStaticProof)
                    {
                        statements.Add(resolved);
                    }
                }
                return block with { Statements = statements.ToImmutable() };
            }
            case BoundStaticProof proof:
            {
                var proofLocals = proof.IsEnsure ? new Dictionary<int, BoundConstant>() : locals;
                var condition = ResolveConstExpression(proof.Condition, proofLocals, functions, variableCount);
                var kind = proof.IsEnsure ? "Static ensure" : "Static require";
                if (!CheckConstExpression(condition, out _))
                {
                    Report("TRM3027", $"{kind} cannot use unsupported effects or calls.", proof.Span);
                    return new BoundBlock([], proof.Span);
                }
                try
                {
                    var state = new ConstEvalState(_cancellationToken);
                    var result = EvaluateConstExpression(condition, ConstSlots(proofLocals, variableCount),
                        functions, 0, state);
                    if (result.Value == 0)
                    {
                        Report("TRM3025", $"{kind} evaluates to false.", proof.Span);
                    }
                }
                catch (ConstEvalFailure failure)
                {
                    Report("TRM3026", $"{kind} is not established: {failure.Message}", proof.Span);
                }
                return new BoundBlock([], proof.Span);
            }
            case BoundLocal local:
            {
                var initializer = ResolveConstExpression(local.Initializer, locals, functions, variableCount);
                if (!_constRequired.Contains(local))
                {
                    return local with { Initializer = initializer };
                }
                try
                {
                    var state = new ConstEvalState(_cancellationToken);
                    var constant = EvaluateConstExpression(initializer, ConstSlots(locals, variableCount),
                        functions, 0, state);
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
                    Condition = ResolveConstExpression(conditional.Condition, locals, functions, variableCount),
                    Then = ResolveConstStatement(conditional.Then, locals, functions, variableCount),
                    Else = conditional.Else is null ? null
                        : ResolveConstStatement(conditional.Else, locals, functions, variableCount),
                };
            case BoundWhile loop:
                return loop with {
                    Condition = ResolveConstExpression(loop.Condition, locals, functions, variableCount),
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
                return view with {
                    Owner = ResolveConstExpression(view.Owner, locals, functions, variableCount),
                    Start = ResolveConstExpression(view.Start, locals, functions, variableCount),
                    Length = ResolveConstExpression(view.Length, locals, functions, variableCount),
                    Body = (BoundBlock)ResolveConstStatement(view.Body, locals, functions, variableCount),
                };
            case BoundAssignment assignment:
                return assignment with { Value = ResolveConstExpression(assignment.Value, locals, functions, variableCount) };
            case BoundFieldAssignment assignment:
                return assignment with { Value = ResolveConstExpression(assignment.Value, locals, functions, variableCount) };
            case BoundBufferAssignment assignment:
                return assignment with {
                    Index = ResolveConstExpression(assignment.Index, locals, functions, variableCount),
                    Value = ResolveConstExpression(assignment.Value, locals, functions, variableCount),
                };
            case BoundViewSet set:
                return set with {
                    Index = ResolveConstExpression(set.Index, locals, functions, variableCount),
                    Value = ResolveConstExpression(set.Value, locals, functions, variableCount),
                };
            case BoundExpressionStatement action:
                return action with {
                    Expression = ResolveConstExpression(action.Expression, locals, functions, variableCount),
                };
            case BoundRequire requirement:
                return requirement with {
                    Condition = ResolveConstExpression(requirement.Condition, locals, functions, variableCount),
                };
            case BoundReturn { Value: BoundExpression value } result:
                return result with { Value = ResolveConstExpression(value, locals, functions, variableCount) };
            default:
                return statement;
        }
    }

    private BoundExpression ResolveConstExpression(BoundExpression expression, Dictionary<int, BoundConstant> locals,
        IReadOnlyList<BoundFunction> functions, int variableCount)
    {
        switch (expression)
        {
            case BoundRequiredConstant required:
            {
                var operand = ResolveConstExpression(required.Operand, locals, functions, variableCount);
                if (!CheckConstExpression(operand, out _))
                {
                    Report("TRM3024", "A required constant expression cannot contain unsupported effects or calls.",
                        required.Span);
                    return expression;
                }
                try
                {
                    var state = new ConstEvalState(_cancellationToken);
                    var constant = EvaluateConstExpression(operand, ConstSlots(locals, variableCount),
                        functions, 0, state);
                    return constant with { Span = required.Span };
                }
                catch (ConstEvalFailure failure)
                {
                    Report("TRM3024", failure.Message, required.Span);
                    return expression;
                }
            }
            case BoundUnary unary:
                return unary with { Operand = ResolveConstExpression(unary.Operand, locals, functions, variableCount) };
            case BoundBinary binary:
                return binary with {
                    Left = ResolveConstExpression(binary.Left, locals, functions, variableCount),
                    Right = ResolveConstExpression(binary.Right, locals, functions, variableCount),
                };
            case BoundCast cast:
                return cast with { Operand = ResolveConstExpression(cast.Operand, locals, functions, variableCount) };
            case BoundCall call:
            {
                var arguments = ImmutableArray.CreateBuilder<BoundExpression>(call.Arguments.Length);
                foreach (var argument in call.Arguments)
                {
                    arguments.Add(ResolveConstExpression(argument, locals, functions, variableCount));
                }
                return call with { Arguments = arguments.MoveToImmutable() };
            }
            case BoundFieldRead field:
                return field with { Receiver = ResolveConstExpression(field.Receiver, locals, functions, variableCount) };
            case BoundBufferRead buffer:
                return buffer with { Index = ResolveConstExpression(buffer.Index, locals, functions, variableCount) };
            case BoundViewRead view:
                return view with { Index = ResolveConstExpression(view.Index, locals, functions, variableCount) };
            case BoundOptionalLift lift:
                return lift with { Value = ResolveConstExpression(lift.Value, locals, functions, variableCount) };
            case BoundReadonlyProjection projection:
                return projection with { Value = ResolveConstExpression(projection.Value, locals, functions, variableCount) };
            case BoundIsNull test:
                return test with { Value = ResolveConstExpression(test.Value, locals, functions, variableCount) };
            case BoundIsSome test:
                return test with { Value = ResolveConstExpression(test.Value, locals, functions, variableCount) };
            case BoundFieldLoan loan:
                return loan with { Place = (BoundFieldRead)ResolveConstExpression(loan.Place, locals, functions, variableCount) };
            case BoundMutableFieldLoan loan:
                return loan with { Place = (BoundFieldRead)ResolveConstExpression(loan.Place, locals, functions, variableCount) };
            default:
                return expression;
        }
    }

    private static BoundConstant?[] ConstSlots(Dictionary<int, BoundConstant> locals, int variableCount)
    {
        var slots = new BoundConstant?[variableCount];
        foreach (var (variable, value) in locals)
        {
            slots[variable] = value;
        }
        return slots;
    }

    private BoundConstant EvaluateConstExpression(BoundExpression expression, BoundConstant?[] slots,
        IReadOnlyList<BoundFunction> functions, int depth, ConstEvalState state)
    {
        state.Step();
        switch (expression)
        {
            case BoundRequiredConstant required:
                return EvaluateConstExpression(required.Operand, slots, functions, depth, state);
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
                var firstIteration = true;
                while ((firstIteration && loop.IsPostTested)
                    || EvaluateConstExpression(loop.Condition, slots, functions, depth, state).Value != 0)
                {
                    firstIteration = false;
                    var result = EvaluateConstStatement(loop.Body, slots, functions, depth, state);
                    if (result.Flow is ConstFlow.Return)
                    {
                        return result;
                    }
                    if (result.Flow is ConstFlow.Break)
                    {
                        if (result.TargetDepth > 1)
                        {
                            return result with { TargetDepth = result.TargetDepth - 1 };
                        }
                        break;
                    }
                    if (result.Flow is ConstFlow.Continue && result.TargetDepth > 1)
                    {
                        return result with { TargetDepth = result.TargetDepth - 1 };
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
            case BoundBreak transfer:
                return new ConstResult(ConstFlow.Break, null, transfer.TargetDepth);
            case BoundContinue transfer:
                return new ConstResult(ConstFlow.Continue, null, transfer.TargetDepth);
            default:
                throw new ConstEvalFailure("Unsupported ConstEval statement.");
        }
    }

    private enum ConstFlow { Next, Return, Break, Continue }

    private readonly record struct ConstResult(ConstFlow Flow, BoundConstant? Value, int TargetDepth = 0);

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

    [SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Private evaluator failure signal, caught when reporting a required constant or proof.")]
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Private evaluator failure signal, never part of the public API.")]
    private sealed class ConstEvalFailure(string message) : Exception(message);
}
