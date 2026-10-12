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

    private enum StaticProofPredicateKind {
        Boolean, Equal, Less, LessEquals, FloatEqual, FloatNotEqual, FloatLess, FloatLessEquals,
        FloatGreater, FloatGreaterEquals,
    }

    private readonly record struct StaticProofPredicate(StaticProofPredicateKind Kind, int Variable,
        PrimitiveType ValueType, PrimitiveType ConstantType, long Constant);

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
        if (DiagnosticCount != 0)
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
            var canRefineLocalPredicates = true;
            functions[index] = functions[index] with {
                Body = (BoundBlock)ResolveConstStatement(functions[index].Body, locals, functions,
                    functions[index].Variables.Length, functions[index].ParameterCount, new(),
                    ref canRefineLocalPredicates),
            };
        }
        return DiagnosticCount == 0;
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
            case BoundVacantLocal or BoundLifetimeEnd:
                return false;
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
        if (!ConstEvalScalar(expression.Type))
        {
            return false;
        }
        switch (expression)
        {
            case BoundUnsafeExpression unsafeExpression:
                return CheckConstExpression(unsafeExpression.Expression, out unsupported);
            case BoundRequiredConstant required:
                return CheckConstExpression(required.Operand, out unsupported);
            case BoundConstant or BoundRead:
                return true;
            case BoundUnary unary:
                return CheckConstExpression(unary.Operand, out unsupported);
            case BoundUpdate { Place: BoundRead }:
                return true;
            case BoundCompoundAssignment { Place: BoundRead } compound:
                return CheckConstExpression(compound.Right, out unsupported);
            case BoundBinary binary:
                return CheckConstExpression(binary.Left, out unsupported)
                    && CheckConstExpression(binary.Right, out unsupported);
            case BoundConditional conditional:
                return CheckConstExpression(conditional.Condition, out unsupported)
                    && CheckConstExpression(conditional.WhenTrue, out unsupported)
                    && CheckConstExpression(conditional.WhenFalse, out unsupported);
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
        IReadOnlyList<BoundFunction> functions, int variableCount, int firstLocalVariable,
        Dictionary<StaticProofPredicate, bool> facts, ref bool canRefineLocalPredicates)
    {
        switch (statement)
        {
            case BoundBlock block:
            {
                var statements = ImmutableArray.CreateBuilder<BoundStatement>(block.Statements.Length);
                foreach (var child in block.Statements)
                {
                    var resolved = ResolveConstStatement(child, locals, functions, variableCount,
                        firstLocalVariable, facts, ref canRefineLocalPredicates);
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
                if (!CheckConstExpression(condition, out _) && !IsStaticProofStableExpression(condition))
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
                    else if (!proof.IsEnsure
                        && canRefineLocalPredicates
                        && TryGetStaticProofPredicate(condition, firstLocalVariable, out var predicate, out var truth))
                    {
                        facts[predicate] = truth;
                    }
                }
                catch (ConstEvalFailure failure)
                {
                    if (!proof.IsEnsure
                        && canRefineLocalPredicates
                        && IsStaticProofStableExpression(condition)
                        && TryGetStaticProofTruth(condition, firstLocalVariable, facts, out var established))
                    {
                        if (!established)
                        {
                            Report("TRM3025", $"{kind} is proven false on this branch.", proof.Span);
                        }
                    }
                    else
                    {
                        Report("TRM3026", $"{kind} is not established: {failure.Message}", proof.Span);
                    }
                }
                return new BoundBlock([], proof.Span);
            }
            case BoundLocal local:
            {
                var initializer = ResolveConstExpression(local.Initializer, locals, functions, variableCount);
                if (!_constRequired.Contains(local))
                {
                    if (!IsStaticProofStableExpression(initializer))
                    {
                        InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                    }
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
            case BoundVacantLocal:
                facts.Clear();
                return statement;
            case BoundLifetimeEnd:
                InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                return statement;
            case BoundIf conditional:
            {
                var condition = ResolveConstExpression(conditional.Condition, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(condition))
                {
                    InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                }
                var thenFacts = new Dictionary<StaticProofPredicate, bool>(facts);
                var elseFacts = new Dictionary<StaticProofPredicate, bool>(facts);
                var thenCanRefineLocalPredicates = canRefineLocalPredicates;
                var elseCanRefineLocalPredicates = canRefineLocalPredicates;
                if (thenCanRefineLocalPredicates)
                {
                    thenFacts = AddStaticProofBranchFacts(condition, true, firstLocalVariable, facts);
                    elseFacts = AddStaticProofBranchFacts(condition, false, firstLocalVariable, facts);
                }
                var then = ResolveConstStatement(conditional.Then, locals, functions, variableCount,
                    firstLocalVariable, thenFacts, ref thenCanRefineLocalPredicates);
                var otherwise = conditional.Else is null ? null
                    : ResolveConstStatement(conditional.Else, locals, functions, variableCount,
                        firstLocalVariable, elseFacts, ref elseCanRefineLocalPredicates);
                facts.Clear();
                canRefineLocalPredicates = thenCanRefineLocalPredicates && elseCanRefineLocalPredicates;
                if (canRefineLocalPredicates)
                {
                    foreach (var (factPredicate, established) in thenFacts)
                    {
                        if (elseFacts.TryGetValue(factPredicate, out var other) && other == established)
                        {
                            facts.Add(factPredicate, established);
                        }
                    }
                }
                return conditional with { Condition = condition, Then = then, Else = otherwise };
            }
            case BoundWhile loop:
            {
                facts.Clear();
                var condition = ResolveConstExpression(loop.Condition, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(condition))
                {
                    InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                }
                var body = ResolveConstStatement(loop.Body, locals, functions, variableCount,
                    firstLocalVariable, new(), ref canRefineLocalPredicates);
                var iterator = loop.Iterator is null ? null
                    : ResolveConstStatement(loop.Iterator, locals, functions, variableCount,
                        firstLocalVariable, new(), ref canRefineLocalPredicates);
                facts.Clear();
                return loop with { Condition = condition, Body = body, Iterator = iterator };
            }
            case BoundTryFinally region:
            {
                facts.Clear();
                var body = (BoundBlock)ResolveConstStatement(region.Body, locals, functions, variableCount,
                    firstLocalVariable, new(), ref canRefineLocalPredicates);
                var finallyBlock = (BoundBlock)ResolveConstStatement(region.Finally, locals, functions,
                    variableCount, firstLocalVariable, new(), ref canRefineLocalPredicates);
                facts.Clear();
                return region with { Body = body, Finally = finallyBlock };
            }
            case BoundTryCatch region:
            {
                facts.Clear();
                var body = (BoundBlock)ResolveConstStatement(region.Body, locals, functions, variableCount,
                    firstLocalVariable, new(), ref canRefineLocalPredicates);
                var catchBlock = (BoundBlock)ResolveConstStatement(region.Catch, locals, functions,
                    variableCount, firstLocalVariable, new(), ref canRefineLocalPredicates);
                var finallyBlock = region.Finally is null ? null
                    : (BoundBlock)ResolveConstStatement(region.Finally, locals, functions, variableCount,
                        firstLocalVariable, new(), ref canRefineLocalPredicates);
                facts.Clear();
                return region with { Body = body, Catch = catchBlock, Finally = finallyBlock };
            }
            case BoundUnsafe region:
            {
                InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                var body = (BoundBlock)ResolveConstStatement(region.Body, locals, functions, variableCount,
                    firstLocalVariable, new(), ref canRefineLocalPredicates);
                facts.Clear();
                return region with { Body = body };
            }
            case BoundViewScope view:
            {
                var owner = ResolveConstExpression(view.Owner, locals, functions, variableCount);
                var start = ResolveConstExpression(view.Start, locals, functions, variableCount);
                var length = ResolveConstExpression(view.Length, locals, functions, variableCount);
                var rangeEnd = view.RangeEnd is null ? null
                    : ResolveConstExpression(view.RangeEnd, locals, functions, variableCount);
                InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                var body = (BoundBlock)ResolveConstStatement(view.Body, locals, functions, variableCount,
                    firstLocalVariable, new(), ref canRefineLocalPredicates);
                facts.Clear();
                return view with { Owner = owner, Start = start, Length = length, RangeEnd = rangeEnd, Body = body };
            }
            case BoundAssignment assignment:
            {
                facts.Clear();
                var value = ResolveConstExpression(assignment.Value, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(value))
                {
                    InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                }
                return assignment with { Value = value };
            }
            case BoundOptionalAssignment optionalAssignment:
                InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                return optionalAssignment with {
                    Place = ResolveConstExpression(optionalAssignment.Place, locals, functions, variableCount),
                    Value = ResolveConstExpression(optionalAssignment.Value, locals, functions, variableCount),
                };
            case BoundFieldAssignment assignment:
                InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                return assignment with { Value = ResolveConstExpression(assignment.Value, locals, functions, variableCount) };
            case BoundBufferAssignment assignment:
                InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                return assignment with {
                    Index = ResolveConstExpression(assignment.Index, locals, functions, variableCount),
                    Value = ResolveConstExpression(assignment.Value, locals, functions, variableCount),
                };
            case BoundViewSet set:
                InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                return set with {
                    Index = ResolveConstExpression(set.Index, locals, functions, variableCount),
                    Value = ResolveConstExpression(set.Value, locals, functions, variableCount),
                };
            case BoundExpressionStatement action:
            {
                var expression = ResolveConstExpression(action.Expression, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(expression))
                {
                    InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                }
                return action with { Expression = expression };
            }
            case BoundRequire requirement:
            {
                var condition = ResolveConstExpression(requirement.Condition, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(condition))
                {
                    InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                }
                else
                {
                    facts.Clear();
                }
                return requirement with { Condition = condition };
            }
            case BoundReturn { Value: BoundExpression value } result:
            {
                facts.Clear();
                var resolved = ResolveConstExpression(value, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(resolved))
                {
                    InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                }
                return result with { Value = resolved };
            }
            case BoundBreak or BoundContinue:
                facts.Clear();
                return statement;
            default:
                InvalidateStaticProofFacts(facts, ref canRefineLocalPredicates);
                return statement;
        }
    }

    private static void InvalidateStaticProofFacts(Dictionary<StaticProofPredicate, bool> facts,
        ref bool canRefineLocalPredicates)
    {
        facts.Clear();
        canRefineLocalPredicates = false;
    }

    private bool IsStaticProofStableExpression(BoundExpression expression)
    {
        if (!IsStaticProofScalar(expression.Type))
        {
            return false;
        }
        return expression switch {
            BoundUnsafeExpression unsafeExpression => IsStaticProofStableExpression(unsafeExpression.Expression),
            BoundRequiredConstant required => IsStaticProofStableExpression(required.Operand),
            BoundConstant or BoundRead => true,
            BoundUnary unary => IsStaticProofStableExpression(unary.Operand),
            BoundBinary binary => IsStaticProofStableExpression(binary.Left)
                && IsStaticProofStableExpression(binary.Right),
            BoundConditional conditional => IsStaticProofStableExpression(conditional.Condition)
                && IsStaticProofStableExpression(conditional.WhenTrue)
                && IsStaticProofStableExpression(conditional.WhenFalse),
            BoundCast cast => IsStaticProofStableExpression(cast.Operand),
            BoundCall call when call.Function >= 0 && call.Function < _signatures.Count
                && _signatures[call.Function].IsConstEval
                => AreStaticProofStableArguments(call.Arguments),
            _ => false,
        };
    }

    private static bool IsStaticProofScalar(PrimitiveType type) =>
        ConstEvalScalar(type) || type is PrimitiveType.Float32 or PrimitiveType.Float64;

    private bool AreStaticProofStableArguments(ImmutableArray<BoundExpression> arguments)
    {
        foreach (var argument in arguments)
        {
            if (!IsStaticProofStableExpression(argument))
            {
                return false;
            }
        }
        return true;
    }

    private static Dictionary<StaticProofPredicate, bool> AddStaticProofBranchFacts(
        BoundExpression expression, bool truth, int firstLocalVariable,
        Dictionary<StaticProofPredicate, bool> inheritedFacts)
    {
        var additions = new Dictionary<StaticProofPredicate, bool>();
        var knownFacts = new Dictionary<StaticProofPredicate, bool>(inheritedFacts);
        if (!CollectStaticProofBranchFacts(
                expression, truth, firstLocalVariable, additions, knownFacts))
        {
            return new Dictionary<StaticProofPredicate, bool>(inheritedFacts);
        }
        foreach (var (predicate, established) in additions)
        {
            if (inheritedFacts.TryGetValue(predicate, out var inherited) && inherited != established)
            {
                return new Dictionary<StaticProofPredicate, bool>(inheritedFacts);
            }
        }
        var facts = new Dictionary<StaticProofPredicate, bool>(inheritedFacts);
        foreach (var (predicate, established) in additions)
        {
            facts[predicate] = established;
        }
        return facts;
    }

    private static bool CollectStaticProofBranchFacts(BoundExpression expression, bool truth,
        int firstLocalVariable, Dictionary<StaticProofPredicate, bool> facts,
        Dictionary<StaticProofPredicate, bool> knownFacts)
    {
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            return CollectStaticProofBranchFacts(unsafeExpression.Expression, truth,
                firstLocalVariable, facts, knownFacts);
        }
        if (expression is BoundUnary { Operator: TokenKind.Bang, Operand: var operand })
        {
            return CollectStaticProofBranchFacts(
                operand, !truth, firstLocalVariable, facts, knownFacts);
        }
        if (expression is BoundBinary { Operator: TokenKind.AmpersandAmpersand } and && truth)
        {
            return CollectStaticProofBranchFacts(
                    and.Left, true, firstLocalVariable, facts, knownFacts)
                && CollectStaticProofBranchFacts(
                    and.Right, true, firstLocalVariable, facts, knownFacts);
        }
        if (expression is BoundBinary { Operator: TokenKind.AmpersandAmpersand } andFalse)
        {
            var leftKnown = TryGetStaticProofTruth(
                andFalse.Left, firstLocalVariable, knownFacts, out var leftTruth);
            var rightKnown = TryGetStaticProofTruth(
                andFalse.Right, firstLocalVariable, knownFacts, out var rightTruth);
            if ((leftKnown && !leftTruth) || (rightKnown && !rightTruth))
            {
                return true;
            }
            if (leftKnown && rightKnown)
            {
                return false;
            }
            if (leftKnown)
            {
                return CollectStaticProofBranchFacts(
                    andFalse.Right, false, firstLocalVariable, facts, knownFacts);
            }
            if (rightKnown)
            {
                return CollectStaticProofBranchFacts(
                    andFalse.Left, false, firstLocalVariable, facts, knownFacts);
            }
        }
        else if (expression is BoundBinary { Operator: TokenKind.PipePipe } or && !truth)
        {
            return CollectStaticProofBranchFacts(
                    or.Left, false, firstLocalVariable, facts, knownFacts)
                && CollectStaticProofBranchFacts(
                    or.Right, false, firstLocalVariable, facts, knownFacts);
        }
        else if (expression is BoundBinary { Operator: TokenKind.PipePipe } orTrue)
        {
            var leftKnown = TryGetStaticProofTruth(
                orTrue.Left, firstLocalVariable, knownFacts, out var leftTruth);
            var rightKnown = TryGetStaticProofTruth(
                orTrue.Right, firstLocalVariable, knownFacts, out var rightTruth);
            if ((leftKnown && leftTruth) || (rightKnown && rightTruth))
            {
                return true;
            }
            if (leftKnown && rightKnown)
            {
                return false;
            }
            if (leftKnown)
            {
                return CollectStaticProofBranchFacts(
                    orTrue.Right, true, firstLocalVariable, facts, knownFacts);
            }
            if (rightKnown)
            {
                return CollectStaticProofBranchFacts(
                    orTrue.Left, true, firstLocalVariable, facts, knownFacts);
            }
        }
        if (TryGetStaticProofPredicate(expression, firstLocalVariable, out var predicate, out var predicateTruth))
        {
            var established = truth ? predicateTruth : !predicateTruth;
            if (knownFacts.TryGetValue(predicate, out var previous))
            {
                return previous == established;
            }
            facts.Add(predicate, established);
            knownFacts.Add(predicate, established);
        }
        return true;
    }

    private static bool TryGetStaticProofTruth(BoundExpression expression, int firstLocalVariable,
        Dictionary<StaticProofPredicate, bool> facts, out bool truth)
    {
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            return TryGetStaticProofTruth(unsafeExpression.Expression, firstLocalVariable, facts, out truth);
        }
        if (expression is BoundUnary { Operator: TokenKind.Bang, Operand: var operand })
        {
            if (TryGetStaticProofTruth(operand, firstLocalVariable, facts, out truth))
            {
                truth = !truth;
                return true;
            }
        }
        else if (expression is BoundBinary { Operator: TokenKind.AmpersandAmpersand } and)
        {
            var leftKnown = TryGetStaticProofTruth(and.Left, firstLocalVariable, facts, out var leftTruth);
            var rightKnown = TryGetStaticProofTruth(and.Right, firstLocalVariable, facts, out var rightTruth);
            if ((leftKnown && !leftTruth) || (rightKnown && !rightTruth))
            {
                truth = false;
                return true;
            }
            if (leftKnown && rightKnown)
            {
                truth = true;
                return true;
            }
        }
        else if (expression is BoundBinary { Operator: TokenKind.PipePipe } or)
        {
            var leftKnown = TryGetStaticProofTruth(or.Left, firstLocalVariable, facts, out var leftTruth);
            var rightKnown = TryGetStaticProofTruth(or.Right, firstLocalVariable, facts, out var rightTruth);
            if ((leftKnown && leftTruth) || (rightKnown && rightTruth))
            {
                truth = true;
                return true;
            }
            if (leftKnown && rightKnown)
            {
                truth = false;
                return true;
            }
        }
        else if (TryGetStaticProofPredicate(expression, firstLocalVariable,
            out var predicate, out var predicateTruth)
            && facts.TryGetValue(predicate, out var established))
        {
            truth = established == predicateTruth;
            return true;
        }
        truth = false;
        return false;
    }

    private static bool TryGetStaticProofPredicate(BoundExpression expression, int firstLocalVariable,
        out StaticProofPredicate predicate, out bool truth)
    {
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            return TryGetStaticProofPredicate(unsafeExpression.Expression, firstLocalVariable,
                out predicate, out truth);
        }
        if (expression is BoundUnary { Operator: TokenKind.Bang, Operand: var operand })
        {
            if (TryGetStaticProofPredicate(operand, firstLocalVariable, out predicate, out truth))
            {
                truth = !truth;
                return true;
            }
        }
        else if (expression is BoundRead { Type: PrimitiveType.Bool } booleanRead
            && booleanRead.Variable >= firstLocalVariable)
        {
            predicate = new StaticProofPredicate(StaticProofPredicateKind.Boolean, booleanRead.Variable,
                PrimitiveType.Bool, PrimitiveType.Bool, 0);
            truth = true;
            return true;
        }
        else if (expression is BoundBinary { Type: PrimitiveType.Bool } binary)
        {
            BoundRead read;
            BoundConstant constant;
            var operation = binary.Operator;
            if (binary.Left is BoundRead leftRead && binary.Right is BoundConstant rightConstant)
            {
                read = leftRead;
                constant = rightConstant;
            }
            else if (binary.Left is BoundConstant leftConstant && binary.Right is BoundRead rightRead)
            {
                read = rightRead;
                constant = leftConstant;
                if (operation == TokenKind.Less)
                {
                    operation = TokenKind.Greater;
                }
                else if (operation == TokenKind.LessEquals)
                {
                    operation = TokenKind.GreaterEquals;
                }
                else if (operation == TokenKind.Greater)
                {
                    operation = TokenKind.Less;
                }
                else if (operation == TokenKind.GreaterEquals)
                {
                    operation = TokenKind.LessEquals;
                }
            }
            else
            {
                predicate = default;
                truth = false;
                return false;
            }
            if (read.Variable < firstLocalVariable || !IsStaticProofScalar(read.Type))
            {
                predicate = default;
                truth = false;
                return false;
            }
            if (read.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
            {
                if (constant.Type != read.Type)
                {
                    predicate = default;
                    truth = false;
                    return false;
                }
                StaticProofPredicateKind? floatingKind = operation switch {
                    TokenKind.EqualsEquals => StaticProofPredicateKind.FloatEqual,
                    TokenKind.BangEquals => StaticProofPredicateKind.FloatNotEqual,
                    TokenKind.Less => StaticProofPredicateKind.FloatLess,
                    TokenKind.LessEquals => StaticProofPredicateKind.FloatLessEquals,
                    TokenKind.Greater => StaticProofPredicateKind.FloatGreater,
                    TokenKind.GreaterEquals => StaticProofPredicateKind.FloatGreaterEquals,
                    _ => null,
                };
                if (floatingKind is not { } floatingPredicateKind)
                {
                    predicate = default;
                    truth = false;
                    return false;
                }
                predicate = new StaticProofPredicate(floatingPredicateKind, read.Variable, read.Type,
                    constant.Type, constant.Value);
                truth = true;
                return true;
            }
            StaticProofPredicateKind kind;
            if (operation is TokenKind.EqualsEquals or TokenKind.BangEquals)
            {
                kind = StaticProofPredicateKind.Equal;
            }
            else if (operation is TokenKind.Less or TokenKind.GreaterEquals)
            {
                kind = StaticProofPredicateKind.Less;
            }
            else if (operation is TokenKind.LessEquals or TokenKind.Greater)
            {
                kind = StaticProofPredicateKind.LessEquals;
            }
            else
            {
                predicate = default;
                truth = false;
                return false;
            }
            predicate = new StaticProofPredicate(kind, read.Variable, read.Type,
                constant.Type, constant.Value);
            truth = operation is TokenKind.EqualsEquals or TokenKind.Less or TokenKind.LessEquals;
            return true;
        }
        predicate = default;
        truth = false;
        return false;
    }

    private BoundExpression ResolveConstExpression(BoundExpression expression, Dictionary<int, BoundConstant> locals,
        IReadOnlyList<BoundFunction> functions, int variableCount)
    {
        switch (expression)
        {
            case BoundUnsafeExpression unsafeExpression:
                return unsafeExpression with {
                    Expression = ResolveConstExpression(unsafeExpression.Expression, locals, functions, variableCount),
                };
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
            case BoundUpdate update:
                return update with { Place = ResolveConstExpression(update.Place, locals, functions, variableCount) };
            case BoundCompoundAssignment compound:
                return compound with {
                    Place = ResolveConstExpression(compound.Place, locals, functions, variableCount),
                    Right = ResolveConstExpression(compound.Right, locals, functions, variableCount),
                };
            case BoundNominalUpdate update:
                return update with { Place = ResolveConstExpression(update.Place, locals, functions, variableCount) };
            case BoundBinary binary:
                return binary with {
                    Left = ResolveConstExpression(binary.Left, locals, functions, variableCount),
                    Right = ResolveConstExpression(binary.Right, locals, functions, variableCount),
                };
            case BoundConditional conditional:
                return conditional with {
                    Condition = ResolveConstExpression(conditional.Condition, locals, functions, variableCount),
                    WhenTrue = ResolveConstExpression(conditional.WhenTrue, locals, functions, variableCount),
                    WhenFalse = ResolveConstExpression(conditional.WhenFalse, locals, functions, variableCount),
                };
            case BoundCoalesce coalesce:
                return coalesce with {
                    Optional = ResolveConstExpression(coalesce.Optional, locals, functions, variableCount),
                    Fallback = ResolveConstExpression(coalesce.Fallback, locals, functions, variableCount),
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
            case BoundBufferLength:
                return expression;
            case BoundViewRead view:
                return view with { Index = ResolveConstExpression(view.Index, locals, functions, variableCount) };
            case BoundSpanLength length:
                return length with { View = ResolveConstExpression(length.View, locals, functions, variableCount) };
            case BoundSpanRead read:
                return read with {
                    View = ResolveConstExpression(read.View, locals, functions, variableCount),
                    Index = ResolveConstExpression(read.Index, locals, functions, variableCount),
                };
            case BoundSpanAdvance advance:
                return advance with { View = ResolveConstExpression(advance.View, locals, functions, variableCount) };
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
            case BoundUnsafeExpression unsafeExpression:
                return EvaluateConstExpression(unsafeExpression.Expression, slots, functions, depth, state);
            case BoundRequiredConstant required:
                return EvaluateConstExpression(required.Operand, slots, functions, depth, state);
            case BoundConstant constant:
                return constant;
            case BoundRead read when slots[read.Variable] is BoundConstant value:
                return value;
            case BoundUpdate { Place: BoundRead read } update when slots[read.Variable] is BoundConstant old:
            {
                var operation = update.Operator == TokenKind.PlusPlus ? TokenKind.Plus : TokenKind.Minus;
                var next = FoldScalarConstant(new BoundBinary(operation, old,
                    new BoundConstant(1, update.Type, update.Span), update.Type, update.Span))
                    as BoundConstant ?? throw new ConstEvalFailure("ConstEval checked update overflowed.");
                slots[read.Variable] = next;
                return update.IsPrefix ? next : old;
            }
            case BoundCompoundAssignment { Place: BoundRead read } compound when slots[read.Variable] is BoundConstant old:
            {
                var right = EvaluateConstExpression(compound.Right, slots, functions, depth, state);
                var next = FoldScalarConstant(new BoundBinary(CompoundOperators.Binary(compound.Operator),
                    old, right, compound.Type, compound.Span)) as BoundConstant
                    ?? throw new ConstEvalFailure("ConstEval checked compound assignment failed.");
                slots[read.Variable] = next;
                return next;
            }
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
            case BoundConditional conditional:
            {
                var condition = EvaluateConstExpression(conditional.Condition, slots, functions, depth, state);
                return EvaluateConstExpression(condition.Value != 0 ? conditional.WhenTrue : conditional.WhenFalse,
                    slots, functions, depth, state);
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
                    arguments[call.ParameterIndex(index)] =
                        EvaluateConstExpression(call.Arguments[index], slots, functions, depth, state);
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
