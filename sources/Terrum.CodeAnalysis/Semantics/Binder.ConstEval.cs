// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
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
    private ImmutableArray<BoundVariable> _staticProofVariables;
    private HashSet<(PrimitiveType Type, long Value)> _staticProofJoinConstants = [];

    private sealed record BoundRequiredConstant(BoundExpression Operand, TextSpan Span)
        : BoundExpression(Operand.Type, Span);

    private sealed record BoundStaticProof(BoundExpression Condition, bool IsEnsure, TextSpan Span)
        : BoundStatement(Span);

    private enum StaticProofPredicateKind {
        Boolean, Equal, Less, LessEquals, VariableEqual, VariableLess, ReflexiveEquality, FloatEqual,
        FloatNotEqual, FloatLess, FloatLessEquals, FloatGreater, FloatGreaterEquals,
    }

    private readonly record struct StaticProofPredicate(StaticProofPredicateKind Kind, int Variable,
        int OtherVariable, PrimitiveType ValueType, PrimitiveType ConstantType, long Constant);
    private readonly record struct SavedStaticProofPredicate(
        StaticProofPredicate Predicate, bool TruthWhenPredicateTrue);

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
            _staticProofVariables = functions[index].Variables;
            _staticProofJoinConstants = [];
            CollectStaticProofJoinConstants(functions[index].Body, _staticProofJoinConstants);
            var locals = new Dictionary<int, BoundConstant>();
            var canRefineLocalPredicates = true;
            var isReachable = true;
            var savedPredicates = new Dictionary<int, SavedStaticProofPredicate>();
            functions[index] = functions[index] with {
                Body = (BoundBlock)ResolveConstStatement(functions[index].Body, locals, functions,
                    functions[index].Variables.Length, functions[index].ParameterCount, new(), savedPredicates,
                    ref canRefineLocalPredicates, ref isReachable),
            };
        }
        _staticProofVariables = [];
        _staticProofJoinConstants.Clear();
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

    private static void CollectStaticProofJoinConstants(BoundStatement statement,
        HashSet<(PrimitiveType Type, long Value)> constants)
    {
        switch (statement)
        {
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    CollectStaticProofJoinConstants(child, constants);
                }
                break;
            case BoundLocal local:
                if (local.Initializer.Type == PrimitiveType.Bool)
                {
                    CollectStaticProofJoinConstants(local.Initializer, constants);
                }
                break;
            case BoundAssignment assignment:
                if (assignment.Value.Type == PrimitiveType.Bool)
                {
                    CollectStaticProofJoinConstants(assignment.Value, constants);
                }
                break;
            case BoundIf conditional:
                CollectStaticProofJoinConstants(conditional.Condition, constants);
                CollectStaticProofJoinConstants(conditional.Then, constants);
                if (conditional.Else is not null)
                {
                    CollectStaticProofJoinConstants(conditional.Else, constants);
                }
                break;
            case BoundWhile loop:
                CollectStaticProofJoinConstants(loop.Condition, constants);
                CollectStaticProofJoinConstants(loop.Body, constants);
                if (loop.Iterator is not null)
                {
                    CollectStaticProofJoinConstants(loop.Iterator, constants);
                }
                break;
            case BoundTryFinally region:
                CollectStaticProofJoinConstants(region.Body, constants);
                CollectStaticProofJoinConstants(region.Finally, constants);
                break;
            case BoundTryCatch region:
                CollectStaticProofJoinConstants(region.Body, constants);
                CollectStaticProofJoinConstants(region.Catch, constants);
                if (region.Finally is not null)
                {
                    CollectStaticProofJoinConstants(region.Finally, constants);
                }
                break;
            case BoundUnsafe region:
                CollectStaticProofJoinConstants(region.Body, constants);
                break;
            case BoundViewScope view:
                CollectStaticProofJoinConstants(view.Body, constants);
                break;
            case BoundRequire requirement:
                CollectStaticProofJoinConstants(requirement.Condition, constants);
                break;
            case BoundStaticProof proof:
                CollectStaticProofJoinConstants(proof.Condition, constants);
                break;
        }
    }

    private static void CollectStaticProofJoinConstants(BoundExpression expression,
        HashSet<(PrimitiveType Type, long Value)> constants)
    {
        switch (expression)
        {
            case BoundConstant constant
                when TryGetStaticProofIntegerValue(constant.Type, constant.Value, out _):
                constants.Add((constant.Type, constant.Value));
                break;
            case BoundRequiredConstant required:
                CollectStaticProofJoinConstants(required.Operand, constants);
                break;
            case BoundUnsafeExpression unsafeExpression:
                CollectStaticProofJoinConstants(unsafeExpression.Expression, constants);
                break;
            case BoundUnary unary:
                CollectStaticProofJoinConstants(unary.Operand, constants);
                break;
            case BoundBinary binary:
                CollectStaticProofJoinConstants(binary.Left, constants);
                CollectStaticProofJoinConstants(binary.Right, constants);
                break;
            case BoundConditional conditional:
                CollectStaticProofJoinConstants(conditional.Condition, constants);
                CollectStaticProofJoinConstants(conditional.WhenTrue, constants);
                CollectStaticProofJoinConstants(conditional.WhenFalse, constants);
                break;
            case BoundCast cast:
                CollectStaticProofJoinConstants(cast.Operand, constants);
                break;
            case BoundCall call:
                foreach (var argument in call.Arguments)
                {
                    CollectStaticProofJoinConstants(argument, constants);
                }
                break;
        }
    }

    private BoundStatement ResolveConstStatement(BoundStatement statement, Dictionary<int, BoundConstant> locals,
        IReadOnlyList<BoundFunction> functions, int variableCount, int firstLocalVariable,
        Dictionary<StaticProofPredicate, bool> facts,
        Dictionary<int, SavedStaticProofPredicate> savedPredicates, ref bool canRefineLocalPredicates,
        ref bool isReachable)
    {
        switch (statement)
        {
            case BoundBlock block:
            {
                var statements = ImmutableArray.CreateBuilder<BoundStatement>(block.Statements.Length);
                foreach (var child in block.Statements)
                {
                    var resolved = ResolveConstStatement(child, locals, functions, variableCount,
                        firstLocalVariable, facts, savedPredicates, ref canRefineLocalPredicates,
                        ref isReachable);
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
                if (!isReachable && !proof.IsEnsure)
                {
                    if (!CheckConstExpression(condition, out _) && !IsStaticProofStableExpression(condition))
                    {
                        Report("TRM3027", "Static require cannot use unsupported effects or calls.", proof.Span);
                    }
                    return new BoundBlock([], proof.Span);
                }
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
                    if (!IsStaticProofStableExpression(initializer)
                        && !TryPreserveStaticProofFactsAcrossCall(initializer, facts, savedPredicates))
                    {
                        InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                    }
                    else if (canRefineLocalPredicates && initializer.Type == PrimitiveType.Bool
                        && CanUseStaticProofVariable(local.Variable, firstLocalVariable))
                    {
                        if (TryGetStaticProofTruth(initializer, firstLocalVariable, facts, out var value))
                        {
                            facts[new StaticProofPredicate(StaticProofPredicateKind.Boolean, local.Variable,
                                -1, PrimitiveType.Bool, PrimitiveType.Bool, 0)] = value;
                        }
                        if (TryGetStaticProofSavedPredicate(initializer, firstLocalVariable,
                            savedPredicates, out var savedPredicate))
                        {
                            savedPredicates[local.Variable] = savedPredicate;
                        }
                    }
                    else if (canRefineLocalPredicates
                        && CanUseStaticProofVariable(local.Variable, firstLocalVariable)
                        && TryGetStaticProofIntegerInterval(initializer, firstLocalVariable, facts,
                            out var lower, out var upper))
                    {
                        AddStaticProofIntegerIntervalFacts(local.Variable, initializer.Type, lower, upper, facts);
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
                savedPredicates.Clear();
                return statement;
            case BoundLifetimeEnd:
                InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                return statement;
            case BoundIf conditional:
            {
                var condition = ResolveConstExpression(conditional.Condition, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(condition)
                    && !TryPreserveStaticProofFactsAcrossCall(condition, facts, savedPredicates))
                {
                    InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                }
                var thenFacts = new Dictionary<StaticProofPredicate, bool>(facts);
                var elseFacts = new Dictionary<StaticProofPredicate, bool>(facts);
                var thenSavedPredicates = new Dictionary<int, SavedStaticProofPredicate>(savedPredicates);
                var elseSavedPredicates = new Dictionary<int, SavedStaticProofPredicate>(savedPredicates);
                var thenCanRefineLocalPredicates = canRefineLocalPredicates;
                var elseCanRefineLocalPredicates = canRefineLocalPredicates;
                var conditionKnown = TryGetStaticProofTruth(condition, firstLocalVariable, facts,
                    out var conditionTruth);
                var thenReachable = isReachable && (!conditionKnown || conditionTruth);
                var elseReachable = isReachable && (!conditionKnown || !conditionTruth);
                if (thenCanRefineLocalPredicates)
                {
                    thenFacts = AddStaticProofBranchFacts(condition, true, firstLocalVariable, facts,
                        savedPredicates,
                        out var thenEdgeReachable);
                    elseFacts = AddStaticProofBranchFacts(condition, false, firstLocalVariable, facts,
                        savedPredicates,
                        out var elseEdgeReachable);
                    thenReachable &= thenEdgeReachable;
                    elseReachable &= elseEdgeReachable;
                }
                var then = ResolveConstStatement(conditional.Then, locals, functions, variableCount,
                    firstLocalVariable, thenFacts, thenSavedPredicates,
                    ref thenCanRefineLocalPredicates, ref thenReachable);
                var otherwise = conditional.Else is null ? null
                    : ResolveConstStatement(conditional.Else, locals, functions, variableCount,
                        firstLocalVariable, elseFacts, elseSavedPredicates,
                        ref elseCanRefineLocalPredicates, ref elseReachable);
                facts.Clear();
                savedPredicates.Clear();
                isReachable = thenReachable || elseReachable;
                if (thenReachable && !elseReachable)
                {
                    canRefineLocalPredicates = thenCanRefineLocalPredicates;
                    CopySavedStaticProofPredicates(savedPredicates, thenSavedPredicates);
                    foreach (var (predicate, established) in thenFacts)
                    {
                        facts.Add(predicate, established);
                    }
                }
                else if (elseReachable && !thenReachable)
                {
                    canRefineLocalPredicates = elseCanRefineLocalPredicates;
                    CopySavedStaticProofPredicates(savedPredicates, elseSavedPredicates);
                    foreach (var (predicate, established) in elseFacts)
                    {
                        facts.Add(predicate, established);
                    }
                }
                else if (thenReachable && elseReachable)
                {
                    canRefineLocalPredicates = thenCanRefineLocalPredicates && elseCanRefineLocalPredicates;
                    MergeSavedStaticProofPredicates(savedPredicates, thenSavedPredicates, elseSavedPredicates);
                    if (canRefineLocalPredicates)
                    {
                        MergeStaticProofFacts(facts, thenFacts, elseFacts);
                    }
                }
                else
                {
                    canRefineLocalPredicates = false;
                    savedPredicates.Clear();
                }
                return conditional with { Condition = condition, Then = then, Else = otherwise };
            }
            case BoundWhile loop:
            {
                facts.Clear();
                var loopSavedPredicates = new Dictionary<int, SavedStaticProofPredicate>(savedPredicates);
                var condition = ResolveConstExpression(loop.Condition, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(condition))
                {
                    InvalidateStaticProofFacts(facts, loopSavedPredicates, ref canRefineLocalPredicates);
                }
                var conditionKnown = TryGetStaticProofTruth(condition, firstLocalVariable, facts,
                    out var conditionTruth);
                var iterationReachable = isReachable && (!conditionKnown || conditionTruth);
                var bodyFacts = new Dictionary<StaticProofPredicate, bool>();
                if (canRefineLocalPredicates)
                {
                    bodyFacts = AddStaticProofBranchFacts(condition, true, firstLocalVariable, new(),
                        loopSavedPredicates,
                        out var bodyEdgeReachable);
                    iterationReachable &= bodyEdgeReachable;
                }
                var bodyReachable = iterationReachable;
                var bodySavedPredicates = new Dictionary<int, SavedStaticProofPredicate>(loopSavedPredicates);
                var body = ResolveConstStatement(loop.Body, locals, functions, variableCount,
                    firstLocalVariable, bodyFacts, bodySavedPredicates,
                    ref canRefineLocalPredicates, ref bodyReachable);
                var iteratorReachable = iterationReachable;
                var iterator = loop.Iterator is null ? null
                    : ResolveConstStatement(loop.Iterator, locals, functions, variableCount,
                        firstLocalVariable, new(), bodySavedPredicates,
                        ref canRefineLocalPredicates, ref iteratorReachable);
                facts.Clear();
                savedPredicates.Clear();
                return loop with { Condition = condition, Body = body, Iterator = iterator };
            }
            case BoundTryFinally region:
            {
                facts.Clear();
                savedPredicates.Clear();
                var bodyReachable = isReachable;
                var body = (BoundBlock)ResolveConstStatement(region.Body, locals, functions, variableCount,
                    firstLocalVariable, new(), new(), ref canRefineLocalPredicates, ref bodyReachable);
                var finallyReachable = isReachable;
                var finallyBlock = (BoundBlock)ResolveConstStatement(region.Finally, locals, functions,
                    variableCount, firstLocalVariable, new(), new(), ref canRefineLocalPredicates,
                    ref finallyReachable);
                isReachable = bodyReachable && finallyReachable;
                facts.Clear();
                savedPredicates.Clear();
                return region with { Body = body, Finally = finallyBlock };
            }
            case BoundTryCatch region:
            {
                facts.Clear();
                savedPredicates.Clear();
                var bodyReachable = isReachable;
                var body = (BoundBlock)ResolveConstStatement(region.Body, locals, functions, variableCount,
                    firstLocalVariable, new(), new(), ref canRefineLocalPredicates, ref bodyReachable);
                var catchReachable = isReachable;
                var catchBlock = (BoundBlock)ResolveConstStatement(region.Catch, locals, functions,
                    variableCount, firstLocalVariable, new(), new(), ref canRefineLocalPredicates,
                    ref catchReachable);
                var finallyReachable = bodyReachable || catchReachable;
                var finallyBlock = region.Finally is null ? null
                    : (BoundBlock)ResolveConstStatement(region.Finally, locals, functions, variableCount,
                        firstLocalVariable, new(), new(), ref canRefineLocalPredicates, ref finallyReachable);
                isReachable = (bodyReachable || catchReachable) && finallyReachable;
                facts.Clear();
                savedPredicates.Clear();
                return region with { Body = body, Catch = catchBlock, Finally = finallyBlock };
            }
            case BoundUnsafe region:
            {
                InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                var bodyReachable = isReachable;
                var body = (BoundBlock)ResolveConstStatement(region.Body, locals, functions, variableCount,
                    firstLocalVariable, new(), new(), ref canRefineLocalPredicates, ref bodyReachable);
                isReachable = bodyReachable;
                facts.Clear();
                savedPredicates.Clear();
                return region with { Body = body };
            }
            case BoundViewScope view:
            {
                var owner = ResolveConstExpression(view.Owner, locals, functions, variableCount);
                var start = ResolveConstExpression(view.Start, locals, functions, variableCount);
                var length = ResolveConstExpression(view.Length, locals, functions, variableCount);
                var rangeEnd = view.RangeEnd is null ? null
                    : ResolveConstExpression(view.RangeEnd, locals, functions, variableCount);
                InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                var bodyReachable = isReachable;
                var body = (BoundBlock)ResolveConstStatement(view.Body, locals, functions, variableCount,
                    firstLocalVariable, new(), new(), ref canRefineLocalPredicates, ref bodyReachable);
                isReachable = bodyReachable;
                facts.Clear();
                savedPredicates.Clear();
                return view with { Owner = owner, Start = start, Length = length, RangeEnd = rangeEnd, Body = body };
            }
            case BoundAssignment assignment:
            {
                var previousFacts = new Dictionary<StaticProofPredicate, bool>(facts);
                var previousSavedPredicates = new Dictionary<int, SavedStaticProofPredicate>(savedPredicates);
                var value = ResolveConstExpression(assignment.Value, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(value))
                {
                    if (TryPreserveStaticProofFactsAcrossCall(value, facts, savedPredicates))
                    {
                        InvalidateStaticProofVariableFacts(facts, assignment.Variable);
                        InvalidateSavedStaticProofPredicates(savedPredicates, assignment.Variable);
                    }
                    else
                    {
                        InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                    }
                }
                else
                {
                    if (assignment.Variable < firstLocalVariable)
                    {
                        facts.Clear();
                        savedPredicates.Clear();
                    }
                    else
                    {
                        InvalidateStaticProofVariableFacts(facts, assignment.Variable);
                        InvalidateSavedStaticProofPredicates(savedPredicates, assignment.Variable);
                    }
                    if (canRefineLocalPredicates && value.Type == PrimitiveType.Bool
                        && CanUseStaticProofVariable(assignment.Variable, firstLocalVariable))
                    {
                        if (TryGetStaticProofTruth(value, firstLocalVariable, previousFacts, out var knownValue))
                        {
                            facts[new StaticProofPredicate(StaticProofPredicateKind.Boolean, assignment.Variable,
                                -1, PrimitiveType.Bool, PrimitiveType.Bool, 0)] = knownValue;
                        }
                        if (TryGetStaticProofSavedPredicate(value, firstLocalVariable, previousSavedPredicates,
                            out var savedPredicate))
                        {
                            savedPredicates[assignment.Variable] = savedPredicate;
                        }
                    }
                    else if (canRefineLocalPredicates
                        && CanUseStaticProofVariable(assignment.Variable, firstLocalVariable)
                        && TryGetStaticProofIntegerInterval(value, firstLocalVariable, previousFacts,
                            out var lower, out var upper))
                    {
                        AddStaticProofIntegerIntervalFacts(assignment.Variable, value.Type, lower, upper, facts);
                    }
                }
                return assignment with { Value = value };
            }
            case BoundOptionalAssignment optionalAssignment:
                InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                return optionalAssignment with {
                    Place = ResolveConstExpression(optionalAssignment.Place, locals, functions, variableCount),
                    Value = ResolveConstExpression(optionalAssignment.Value, locals, functions, variableCount),
                };
            case BoundFieldAssignment assignment:
                InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                return assignment with { Value = ResolveConstExpression(assignment.Value, locals, functions, variableCount) };
            case BoundBufferAssignment assignment:
                InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                return assignment with {
                    Index = ResolveConstExpression(assignment.Index, locals, functions, variableCount),
                    Value = ResolveConstExpression(assignment.Value, locals, functions, variableCount),
                };
            case BoundViewSet set:
                InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                return set with {
                    Index = ResolveConstExpression(set.Index, locals, functions, variableCount),
                    Value = ResolveConstExpression(set.Value, locals, functions, variableCount),
                };
            case BoundExpressionStatement action:
            {
                var expression = ResolveConstExpression(action.Expression, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(expression)
                    && !TryPreserveStaticProofFactsAcrossCall(expression, facts, savedPredicates))
                {
                    InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                }
                return action with { Expression = expression };
            }
            case BoundRequire requirement:
            {
                var condition = ResolveConstExpression(requirement.Condition, locals, functions, variableCount);
                var stable = IsStaticProofStableExpression(condition);
                if (!stable && !TryPreserveStaticProofFactsAcrossCall(condition, facts, savedPredicates))
                {
                    InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                }
                var conditionKnown = TryGetStaticProofTruth(condition, firstLocalVariable, facts,
                    out var conditionTruth);
                if (conditionKnown && !conditionTruth)
                {
                    isReachable = false;
                }
                if (stable && canRefineLocalPredicates)
                {
                    var successfulFacts = AddStaticProofBranchFacts(condition, true,
                        firstLocalVariable, facts, savedPredicates, out var successfulEdgeReachable);
                    facts.Clear();
                    foreach (var (predicate, established) in successfulFacts)
                    {
                        facts.Add(predicate, established);
                    }
                    isReachable &= successfulEdgeReachable;
                }
                else if (stable)
                {
                    facts.Clear();
                    savedPredicates.Clear();
                }
                return requirement with { Condition = condition };
            }
            case BoundReturn { Value: BoundExpression value } result:
            {
                facts.Clear();
                var resolved = ResolveConstExpression(value, locals, functions, variableCount);
                if (!IsStaticProofStableExpression(resolved))
                {
                    InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                }
                isReachable = false;
                savedPredicates.Clear();
                return result with { Value = resolved };
            }
            case BoundReturn result:
                facts.Clear();
                savedPredicates.Clear();
                isReachable = false;
                return result;
            case BoundBreak or BoundContinue:
                facts.Clear();
                savedPredicates.Clear();
                isReachable = false;
                return statement;
            default:
                InvalidateStaticProofFacts(facts, savedPredicates, ref canRefineLocalPredicates);
                return statement;
        }
    }

    private static void InvalidateStaticProofFacts(Dictionary<StaticProofPredicate, bool> facts,
        Dictionary<int, SavedStaticProofPredicate> savedPredicates,
        ref bool canRefineLocalPredicates)
    {
        facts.Clear();
        savedPredicates.Clear();
        canRefineLocalPredicates = false;
    }

    private bool TryPreserveStaticProofFactsAcrossCall(BoundExpression expression,
        Dictionary<StaticProofPredicate, bool> facts,
        Dictionary<int, SavedStaticProofPredicate> savedPredicates)
    {
        if (expression is not BoundCall call)
        {
            return false;
        }
        foreach (var argument in call.Arguments)
        {
            if (argument is not BoundOutArgument && !IsStaticProofStableExpression(argument))
            {
                return false;
            }
        }
        foreach (var argument in call.Arguments)
        {
            if (argument is BoundOutArgument output)
            {
                InvalidateStaticProofVariableFacts(facts, output.Variable);
                InvalidateSavedStaticProofPredicates(savedPredicates, output.Variable);
            }
        }
        return true;
    }

    private static void InvalidateStaticProofVariableFacts(
        Dictionary<StaticProofPredicate, bool> facts, int variable)
    {
        List<StaticProofPredicate> invalidated = [];
        foreach (var predicate in facts.Keys)
        {
            if (predicate.Variable == variable || predicate.OtherVariable == variable)
            {
                invalidated.Add(predicate);
            }
        }
        foreach (var predicate in invalidated)
        {
            facts.Remove(predicate);
        }
    }

    private static void InvalidateSavedStaticProofPredicates(
        Dictionary<int, SavedStaticProofPredicate> savedPredicates, int variable)
    {
        List<int> invalidated = [];
        foreach (var (savedVariable, relation) in savedPredicates)
        {
            if (savedVariable == variable || relation.Predicate.Variable == variable
                || relation.Predicate.OtherVariable == variable)
            {
                invalidated.Add(savedVariable);
            }
        }
        foreach (var savedVariable in invalidated)
        {
            savedPredicates.Remove(savedVariable);
        }
    }

    private bool TryGetStaticProofSavedPredicate(BoundExpression expression, int firstLocalVariable,
        Dictionary<int, SavedStaticProofPredicate> savedPredicates,
        out SavedStaticProofPredicate savedPredicate)
    {
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            return TryGetStaticProofSavedPredicate(unsafeExpression.Expression, firstLocalVariable,
                savedPredicates, out savedPredicate);
        }
        if (expression is BoundRequiredConstant required)
        {
            return TryGetStaticProofSavedPredicate(required.Operand, firstLocalVariable,
                savedPredicates, out savedPredicate);
        }
        if (expression is BoundUnary { Operator: TokenKind.Bang, Operand: var operand }
            && TryGetStaticProofSavedPredicate(operand, firstLocalVariable, savedPredicates, out savedPredicate))
        {
            savedPredicate = savedPredicate with {
                TruthWhenPredicateTrue = !savedPredicate.TruthWhenPredicateTrue,
            };
            return true;
        }
        if (expression is BoundRead { Type: PrimitiveType.Bool } read
            && CanUseStaticProofVariable(read.Variable, firstLocalVariable)
            && savedPredicates.TryGetValue(read.Variable, out savedPredicate))
        {
            return true;
        }
        if (IsStaticProofStableExpression(expression)
            && TryGetStaticProofPredicate(expression, firstLocalVariable,
                out var predicate, out var truthWhenPredicateTrue)
            && predicate.Kind != StaticProofPredicateKind.Boolean)
        {
            savedPredicate = new SavedStaticProofPredicate(predicate, truthWhenPredicateTrue);
            return true;
        }
        savedPredicate = default;
        return false;
    }

    private static void CopySavedStaticProofPredicates(
        Dictionary<int, SavedStaticProofPredicate> destination,
        Dictionary<int, SavedStaticProofPredicate> source)
    {
        destination.Clear();
        foreach (var (variable, predicate) in source)
        {
            destination.Add(variable, predicate);
        }
    }

    private static void MergeSavedStaticProofPredicates(
        Dictionary<int, SavedStaticProofPredicate> destination,
        Dictionary<int, SavedStaticProofPredicate> left,
        Dictionary<int, SavedStaticProofPredicate> right)
    {
        destination.Clear();
        foreach (var (variable, predicate) in left)
        {
            if (right.TryGetValue(variable, out var other) && other == predicate)
            {
                destination.Add(variable, predicate);
            }
        }
    }

    private void MergeStaticProofFacts(
        Dictionary<StaticProofPredicate, bool> destination,
        Dictionary<StaticProofPredicate, bool> left,
        Dictionary<StaticProofPredicate, bool> right)
    {
        // Retain only candidate integer comparisons entailed by both reachable edge domains.
        List<StaticProofPredicate> candidates = [];
        HashSet<StaticProofPredicate> seenCandidates = [];
        foreach (var predicate in left.Keys)
        {
            if (seenCandidates.Add(predicate))
            {
                candidates.Add(predicate);
            }
        }
        foreach (var predicate in right.Keys)
        {
            if (seenCandidates.Add(predicate))
            {
                candidates.Add(predicate);
            }
        }
        HashSet<(int Variable, PrimitiveType Type)> trackedIntegerVariables = [];
        foreach (var predicate in candidates)
        {
            if (predicate.OtherVariable < 0 && predicate.ConstantType == predicate.ValueType
                && TryGetStaticProofIntegerValue(predicate.ValueType, predicate.Constant, out _))
            {
                trackedIntegerVariables.Add((predicate.Variable, predicate.ValueType));
            }
        }
        foreach (var (variable, type) in trackedIntegerVariables)
        {
            switch (type)
            {
                case PrimitiveType.Int32:
                    AddStaticProofIntegerPredicateCandidates(variable, type, 0, candidates, seenCandidates);
                    AddStaticProofIntegerPredicateCandidates(variable, type, -1, candidates, seenCandidates);
                    AddStaticProofIntegerPredicateCandidates(variable, type, int.MinValue, candidates, seenCandidates);
                    AddStaticProofIntegerPredicateCandidates(variable, type, int.MaxValue, candidates, seenCandidates);
                    break;
                case PrimitiveType.UInt32:
                    AddStaticProofIntegerPredicateCandidates(variable, type, 0, candidates, seenCandidates);
                    AddStaticProofIntegerPredicateCandidates(variable, type, -1, candidates, seenCandidates);
                    break;
                case PrimitiveType.Int64:
                    AddStaticProofIntegerPredicateCandidates(variable, type, 0, candidates, seenCandidates);
                    AddStaticProofIntegerPredicateCandidates(variable, type, -1, candidates, seenCandidates);
                    AddStaticProofIntegerPredicateCandidates(variable, type, long.MinValue, candidates, seenCandidates);
                    AddStaticProofIntegerPredicateCandidates(variable, type, long.MaxValue, candidates, seenCandidates);
                    break;
                case PrimitiveType.UInt64:
                case PrimitiveType.Char16:
                    AddStaticProofIntegerPredicateCandidates(variable, type, 0, candidates, seenCandidates);
                    AddStaticProofIntegerPredicateCandidates(variable, type, -1, candidates, seenCandidates);
                    break;
            }
            foreach (var (constantType, constant) in _staticProofJoinConstants)
            {
                if (constantType == type)
                {
                    AddStaticProofIntegerPredicateCandidates(variable, type, constant,
                        candidates, seenCandidates);
                }
            }
        }
        var originalCandidateCount = candidates.Count;
        for (var index = 0; index < originalCandidateCount; index++)
        {
            var predicate = candidates[index];
            if (predicate.OtherVariable < 0 && predicate.ConstantType == predicate.ValueType
                && TryGetStaticProofIntegerValue(predicate.ConstantType, predicate.Constant, out _)
                && predicate.Kind is (StaticProofPredicateKind.Equal
                    or StaticProofPredicateKind.Less
                    or StaticProofPredicateKind.LessEquals))
            {
                var equal = predicate with { Kind = StaticProofPredicateKind.Equal };
                var less = predicate with { Kind = StaticProofPredicateKind.Less };
                var lessEquals = predicate with { Kind = StaticProofPredicateKind.LessEquals };
                if (seenCandidates.Add(equal))
                {
                    candidates.Add(equal);
                }
                if (seenCandidates.Add(less))
                {
                    candidates.Add(less);
                }
                if (seenCandidates.Add(lessEquals))
                {
                    candidates.Add(lessEquals);
                }
            }
        }

        destination.Clear();
        foreach (var predicate in candidates)
        {
            if (TryGetStaticProofFact(predicate, left, out var leftEstablished)
                && TryGetStaticProofFact(predicate, right, out var rightEstablished)
                && leftEstablished == rightEstablished)
            {
                destination.Add(predicate, leftEstablished);
            }
        }
    }

    private void AddStaticProofIntegerIntervalFacts(int variable, PrimitiveType type,
        BigInteger lower, BigInteger upper, Dictionary<StaticProofPredicate, bool> facts)
    {
        foreach (var (constantType, constant) in _staticProofJoinConstants)
        {
            if (constantType == type)
            {
                AddConstant(constant);
            }
        }
        switch (type)
        {
            case PrimitiveType.Int32:
                AddConstant(0);
                AddConstant(-1);
                AddConstant(int.MinValue);
                AddConstant(int.MaxValue);
                break;
            case PrimitiveType.UInt32:
            case PrimitiveType.UInt64:
            case PrimitiveType.Char16:
                AddConstant(0);
                AddConstant(-1);
                break;
            case PrimitiveType.Int64:
                AddConstant(0);
                AddConstant(-1);
                AddConstant(long.MinValue);
                AddConstant(long.MaxValue);
                break;
        }

        void AddConstant(long constant)
        {
            if (!TryGetStaticProofIntegerValue(type, constant, out var numericValue))
            {
                return;
            }
            var value = new BigInteger(numericValue);
            bool? equal = lower == upper && lower == value ? true
                : value < lower || value > upper ? false : null;
            bool? less = upper < value ? true : lower >= value ? false : null;
            bool? lessEquals = upper <= value ? true : lower > value ? false : null;
            AddFact(StaticProofPredicateKind.Equal, equal, constant);
            AddFact(StaticProofPredicateKind.Less, less, constant);
            AddFact(StaticProofPredicateKind.LessEquals, lessEquals, constant);
        }

        void AddFact(StaticProofPredicateKind kind, bool? established, long constant)
        {
            if (established is bool truth)
            {
                facts[new StaticProofPredicate(kind, variable, -1, type, type, constant)] = truth;
            }
        }
    }

    private static void AddStaticProofIntegerPredicateCandidates(int variable, PrimitiveType type, long constant,
        List<StaticProofPredicate> candidates, HashSet<StaticProofPredicate> seenCandidates)
    {
        if (!TryGetStaticProofIntegerValue(type, constant, out _))
        {
            return;
        }
        Add(StaticProofPredicateKind.Equal);
        Add(StaticProofPredicateKind.Less);
        Add(StaticProofPredicateKind.LessEquals);

        void Add(StaticProofPredicateKind kind)
        {
            var predicate = new StaticProofPredicate(kind, variable, -1, type, type, constant);
            if (seenCandidates.Add(predicate))
            {
                candidates.Add(predicate);
            }
        }
    }

    private static bool TryGetStaticProofFact(StaticProofPredicate predicate,
        Dictionary<StaticProofPredicate, bool> facts, out bool established)
    {
        if (facts.TryGetValue(predicate, out established))
        {
            return true;
        }
        return TryGetStaticProofIntegerPredicateTruth(predicate, facts, out established);
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

    private Dictionary<StaticProofPredicate, bool> AddStaticProofBranchFacts(
        BoundExpression expression, bool truth, int firstLocalVariable,
        Dictionary<StaticProofPredicate, bool> inheritedFacts,
        Dictionary<int, SavedStaticProofPredicate> savedPredicates, out bool reachable)
    {
        reachable = true;
        var additions = new Dictionary<StaticProofPredicate, bool>();
        var knownFacts = new Dictionary<StaticProofPredicate, bool>(inheritedFacts);
        if (!CollectStaticProofBranchFacts(
                expression, truth, firstLocalVariable, additions, knownFacts, savedPredicates))
        {
            reachable = false;
            return new Dictionary<StaticProofPredicate, bool>(inheritedFacts);
        }
        foreach (var (predicate, established) in additions)
        {
            if (inheritedFacts.TryGetValue(predicate, out var inherited) && inherited != established)
            {
                reachable = false;
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

    private bool CollectStaticProofBranchFacts(BoundExpression expression, bool truth,
        int firstLocalVariable, Dictionary<StaticProofPredicate, bool> facts,
        Dictionary<StaticProofPredicate, bool> knownFacts,
        Dictionary<int, SavedStaticProofPredicate> savedPredicates)
    {
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            return CollectStaticProofBranchFacts(unsafeExpression.Expression, truth,
                firstLocalVariable, facts, knownFacts, savedPredicates);
        }
        if (expression is BoundUnary { Operator: TokenKind.Bang, Operand: var operand })
        {
            return CollectStaticProofBranchFacts(
                operand, !truth, firstLocalVariable, facts, knownFacts, savedPredicates);
        }
        if (expression is BoundBinary {
            Operator: TokenKind.AmpersandAmpersand or TokenKind.Ampersand, Type: PrimitiveType.Bool
        } and && truth)
        {
            return CollectStaticProofBranchFacts(
                    and.Left, true, firstLocalVariable, facts, knownFacts, savedPredicates)
                && CollectStaticProofBranchFacts(
                    and.Right, true, firstLocalVariable, facts, knownFacts, savedPredicates);
        }
        if (expression is BoundBinary {
            Operator: TokenKind.AmpersandAmpersand or TokenKind.Ampersand, Type: PrimitiveType.Bool
        } andFalse)
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
                    andFalse.Right, false, firstLocalVariable, facts, knownFacts, savedPredicates);
            }
            if (rightKnown)
            {
                return CollectStaticProofBranchFacts(
                    andFalse.Left, false, firstLocalVariable, facts, knownFacts, savedPredicates);
            }
        }
        else if (expression is BoundBinary {
            Operator: TokenKind.PipePipe or TokenKind.Pipe, Type: PrimitiveType.Bool
        } or && !truth)
        {
            return CollectStaticProofBranchFacts(
                    or.Left, false, firstLocalVariable, facts, knownFacts, savedPredicates)
                && CollectStaticProofBranchFacts(
                    or.Right, false, firstLocalVariable, facts, knownFacts, savedPredicates);
        }
        else if (expression is BoundBinary {
            Operator: TokenKind.PipePipe or TokenKind.Pipe, Type: PrimitiveType.Bool
        } orTrue)
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
                    orTrue.Right, true, firstLocalVariable, facts, knownFacts, savedPredicates);
            }
            if (rightKnown)
            {
                return CollectStaticProofBranchFacts(
                    orTrue.Left, true, firstLocalVariable, facts, knownFacts, savedPredicates);
            }
        }
        else if (expression is BoundBinary { Operator: TokenKind.Caret, Type: PrimitiveType.Bool } xor)
        {
            var leftKnown = TryGetStaticProofTruth(
                xor.Left, firstLocalVariable, knownFacts, out var leftTruth);
            var rightKnown = TryGetStaticProofTruth(
                xor.Right, firstLocalVariable, knownFacts, out var rightTruth);
            if (leftKnown && rightKnown)
            {
                return (leftTruth ^ rightTruth) == truth;
            }
            if (leftKnown)
            {
                return CollectStaticProofBranchFacts(
                    xor.Right, truth ^ leftTruth, firstLocalVariable, facts, knownFacts, savedPredicates);
            }
            if (rightKnown)
            {
                return CollectStaticProofBranchFacts(
                    xor.Left, truth ^ rightTruth, firstLocalVariable, facts, knownFacts, savedPredicates);
            }
        }
        if (expression is BoundRead { Type: PrimitiveType.Bool } savedRead
            && savedPredicates.TryGetValue(savedRead.Variable, out var savedPredicate))
        {
            var established = truth
                ? savedPredicate.TruthWhenPredicateTrue
                : !savedPredicate.TruthWhenPredicateTrue;
            if (knownFacts.TryGetValue(savedPredicate.Predicate, out var previous))
            {
                if (previous != established)
                {
                    return false;
                }
            }
            else
            {
                facts.Add(savedPredicate.Predicate, established);
                knownFacts.Add(savedPredicate.Predicate, established);
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

    private bool TryGetStaticProofTruth(BoundExpression expression, int firstLocalVariable,
        Dictionary<StaticProofPredicate, bool> facts, out bool truth)
    {
        if (expression is BoundConstant { Type: PrimitiveType.Bool } constant)
        {
            truth = constant.Value != 0;
            return true;
        }
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
        else if (expression is BoundBinary {
            Operator: TokenKind.AmpersandAmpersand or TokenKind.Ampersand, Type: PrimitiveType.Bool
        } and)
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
        else if (expression is BoundBinary {
            Operator: TokenKind.PipePipe or TokenKind.Pipe, Type: PrimitiveType.Bool
        } or)
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
        else if (expression is BoundBinary { Operator: TokenKind.Caret, Type: PrimitiveType.Bool } xor)
        {
            if (TryGetStaticProofTruth(xor.Left, firstLocalVariable, facts, out var leftTruth)
                && TryGetStaticProofTruth(xor.Right, firstLocalVariable, facts, out var rightTruth))
            {
                truth = leftTruth ^ rightTruth;
                return true;
            }
        }
        else if (TryGetStaticProofPredicate(expression, firstLocalVariable,
            out var predicate, out var predicateTruth))
        {
            if (facts.TryGetValue(predicate, out var established))
            {
                truth = established == predicateTruth;
                return true;
            }
            if (predicate.Kind == StaticProofPredicateKind.ReflexiveEquality)
            {
                truth = predicateTruth;
                return true;
            }
            if (TryGetStaticProofIntegerPredicateTruth(predicate, facts, out established))
            {
                truth = established == predicateTruth;
                return true;
            }
        }
        if (TryGetStaticProofIntegerComparisonTruth(expression, firstLocalVariable, facts, out truth))
        {
            return true;
        }
        truth = false;
        return false;
    }

    private static bool TryGetStaticProofIntegerPredicateTruth(StaticProofPredicate predicate,
        Dictionary<StaticProofPredicate, bool> facts, out bool truth)
    {
        truth = false;
        if (predicate.ConstantType != predicate.ValueType
            || predicate.OtherVariable >= 0
            || predicate.Kind is not (StaticProofPredicateKind.Equal
                or StaticProofPredicateKind.Less or StaticProofPredicateKind.LessEquals)
            || !TryGetStaticProofIntegerRange(predicate.ValueType, out var lower, out var upper))
        {
            return false;
        }

        var excludedValues = new HashSet<decimal>();
        foreach (var (fact, established) in facts)
        {
            if (fact.Variable != predicate.Variable || fact.ValueType != predicate.ValueType
                || fact.OtherVariable >= 0
                || fact.ConstantType != fact.ValueType
                || !TryGetStaticProofIntegerValue(fact.ConstantType, fact.Constant, out var value))
            {
                continue;
            }
            switch (fact.Kind)
            {
                case StaticProofPredicateKind.Equal when established:
                    lower = Math.Max(lower, value);
                    upper = Math.Min(upper, value);
                    break;
                case StaticProofPredicateKind.Equal:
                    excludedValues.Add(value);
                    break;
                case StaticProofPredicateKind.Less:
                    if (established)
                    {
                        upper = Math.Min(upper, value - 1);
                    }
                    else
                    {
                        lower = Math.Max(lower, value);
                    }
                    break;
                case StaticProofPredicateKind.LessEquals:
                    if (established)
                    {
                        upper = Math.Min(upper, value);
                    }
                    else
                    {
                        lower = Math.Max(lower, value + 1);
                    }
                    break;
            }
        }

        while (lower <= upper && excludedValues.Contains(lower))
        {
            lower++;
        }
        while (lower <= upper && excludedValues.Contains(upper))
        {
            upper--;
        }

        if (lower > upper
            || !TryGetStaticProofIntegerValue(predicate.ConstantType, predicate.Constant, out var constant))
        {
            return false;
        }
        switch (predicate.Kind)
        {
            case StaticProofPredicateKind.Equal when lower == upper && lower == constant:
                truth = true;
                return true;
            case StaticProofPredicateKind.Equal when excludedValues.Contains(constant):
                truth = false;
                return true;
            case StaticProofPredicateKind.Equal when constant < lower || constant > upper:
                truth = false;
                return true;
            case StaticProofPredicateKind.Less when upper < constant:
                truth = true;
                return true;
            case StaticProofPredicateKind.Less when lower >= constant:
                truth = false;
                return true;
            case StaticProofPredicateKind.LessEquals when upper <= constant:
                truth = true;
                return true;
            case StaticProofPredicateKind.LessEquals when lower > constant:
                truth = false;
                return true;
            default:
                return false;
        }
    }
    private static bool TryGetStaticProofIntegerWidth(PrimitiveType type, out int width)
    {
        switch (type)
        {
            case PrimitiveType.Int32:
            case PrimitiveType.UInt32:
                width = 32;
                return true;
            case PrimitiveType.Int64:
            case PrimitiveType.UInt64:
                width = 64;
                return true;
            default:
                width = 0;
                return false;
        }
    }

    private static BigInteger ToStaticProofBitPattern(BigInteger value, int width) =>
        value < BigInteger.Zero ? value + (BigInteger.One << width) : value;

    private static BigInteger InterpretStaticProofBitPattern(BigInteger value, PrimitiveType type, int width)
    {
        var modulus = BigInteger.One << width;
        if (type is PrimitiveType.Int32 or PrimitiveType.Int64
            && value >= (BigInteger.One << (width - 1)))
        {
            value -= modulus;
        }
        return value;
    }

    private static bool TryGetStaticProofIntegerRange(PrimitiveType type,
        out decimal lower, out decimal upper)
    {
        (lower, upper) = type switch
        {
            PrimitiveType.Int32 => ((decimal)int.MinValue, (decimal)int.MaxValue),
            PrimitiveType.UInt32 => ((decimal)uint.MinValue, (decimal)uint.MaxValue),
            PrimitiveType.Int64 => ((decimal)long.MinValue, (decimal)long.MaxValue),
            PrimitiveType.UInt64 => ((decimal)ulong.MinValue, (decimal)ulong.MaxValue),
            PrimitiveType.Char16 => ((decimal)ushort.MinValue, (decimal)ushort.MaxValue),
            _ => (decimal.Zero, decimal.Zero),
        };
        return type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
            or PrimitiveType.UInt64 or PrimitiveType.Char16;
    }

    private static bool TryGetStaticProofIntegerValue(PrimitiveType type, long value, out decimal result)
    {
        switch (type)
        {
            case PrimitiveType.Int32:
                result = unchecked((int)value);
                return true;
            case PrimitiveType.UInt32:
                result = unchecked((uint)value);
                return true;
            case PrimitiveType.Int64:
                result = value;
                return true;
            case PrimitiveType.UInt64:
                result = unchecked((ulong)value);
                return true;
            case PrimitiveType.Char16:
                result = unchecked((ushort)value);
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private bool TryGetStaticProofIntegerComparisonTruth(BoundExpression expression,
        int firstLocalVariable, Dictionary<StaticProofPredicate, bool> facts, out bool truth)
    {
        truth = false;
        if (expression is not BoundBinary { Type: PrimitiveType.Bool } comparison
            || comparison.Left.Type != comparison.Right.Type
            || !TryGetStaticProofIntegerInterval(comparison.Left, firstLocalVariable, facts,
                out var leftLower, out var leftUpper)
            || !TryGetStaticProofIntegerInterval(comparison.Right, firstLocalVariable, facts,
                out var rightLower, out var rightUpper))
        {
            return false;
        }

        switch (comparison.Operator)
        {
            case TokenKind.EqualsEquals:
                if (leftUpper < rightLower || rightUpper < leftLower)
                {
                    truth = false;
                    return true;
                }
                if (leftLower == leftUpper && rightLower == rightUpper && leftLower == rightLower)
                {
                    truth = true;
                    return true;
                }
                break;
            case TokenKind.BangEquals:
                if (leftUpper < rightLower || rightUpper < leftLower)
                {
                    truth = true;
                    return true;
                }
                if (leftLower == leftUpper && rightLower == rightUpper && leftLower == rightLower)
                {
                    truth = false;
                    return true;
                }
                break;
            case TokenKind.Less:
                if (leftUpper < rightLower)
                {
                    truth = true;
                    return true;
                }
                if (leftLower >= rightUpper)
                {
                    truth = false;
                    return true;
                }
                break;
            case TokenKind.LessEquals:
                if (leftUpper <= rightLower)
                {
                    truth = true;
                    return true;
                }
                if (leftLower > rightUpper)
                {
                    truth = false;
                    return true;
                }
                break;
            case TokenKind.Greater:
                if (leftLower > rightUpper)
                {
                    truth = true;
                    return true;
                }
                if (leftUpper <= rightLower)
                {
                    truth = false;
                    return true;
                }
                break;
            case TokenKind.GreaterEquals:
                if (leftLower >= rightUpper)
                {
                    truth = true;
                    return true;
                }
                if (leftUpper < rightLower)
                {
                    truth = false;
                    return true;
                }
                break;
        }
        return false;
    }

    private bool TryGetStaticProofIntegerInterval(BoundExpression expression,
        int firstLocalVariable, Dictionary<StaticProofPredicate, bool> facts,
        out BigInteger lower, out BigInteger upper)
    {
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            return TryGetStaticProofIntegerInterval(unsafeExpression.Expression, firstLocalVariable, facts,
                out lower, out upper);
        }
        if (expression is BoundRequiredConstant required)
        {
            return TryGetStaticProofIntegerInterval(required.Operand, firstLocalVariable, facts,
                out lower, out upper);
        }
        if (expression is BoundConstant constant
            && TryGetStaticProofIntegerConstant(constant, out lower))
        {
            upper = lower;
            return true;
        }
        if (expression is BoundRead read
            && CanUseStaticProofVariable(read.Variable, firstLocalVariable)
            && TryGetStaticProofIntegerRange(read.Type, out var lowerBound, out var upperBound))
        {
            lower = new BigInteger(lowerBound);
            upper = new BigInteger(upperBound);
            HashSet<BigInteger> excludedValues = [];
            foreach (var (fact, established) in facts)
            {
                if (fact.Variable != read.Variable || fact.ValueType != read.Type
                    || fact.OtherVariable >= 0 || fact.ConstantType != fact.ValueType
                    || !TryGetStaticProofIntegerValue(fact.ConstantType, fact.Constant, out var value))
                {
                    continue;
                }

                var candidate = new BigInteger(value);
                switch (fact.Kind)
                {
                    case StaticProofPredicateKind.Equal when established:
                        lower = BigInteger.Max(lower, candidate);
                        upper = BigInteger.Min(upper, candidate);
                        break;
                    case StaticProofPredicateKind.Equal:
                        excludedValues.Add(candidate);
                        break;
                    case StaticProofPredicateKind.Less when established:
                        upper = BigInteger.Min(upper, candidate - BigInteger.One);
                        break;
                    case StaticProofPredicateKind.Less:
                        lower = BigInteger.Max(lower, candidate);
                        break;
                    case StaticProofPredicateKind.LessEquals when established:
                        upper = BigInteger.Min(upper, candidate);
                        break;
                    case StaticProofPredicateKind.LessEquals:
                        lower = BigInteger.Max(lower, candidate + BigInteger.One);
                        break;
                }
            }

            while (lower <= upper && excludedValues.Contains(lower))
            {
                lower++;
            }
            while (lower <= upper && excludedValues.Contains(upper))
            {
                upper--;
            }
            return lower <= upper;
        }
        if (expression is BoundUnary { Operator: TokenKind.Tilde } bitwiseNot
            && TryGetStaticProofIntegerWidth(bitwiseNot.Type, out var bitwiseNotWidth)
            && TryGetStaticProofIntegerInterval(bitwiseNot.Operand, firstLocalVariable, facts,
                out var bitwiseNotLower, out var bitwiseNotUpper)
            && TryGetStaticProofIntegerRange(bitwiseNot.Type, out var bitwiseNotMinimum,
                out var bitwiseNotMaximum))
        {
            if (bitwiseNotLower == bitwiseNotUpper)
            {
                var modulus = BigInteger.One << bitwiseNotWidth;
                var bits = ToStaticProofBitPattern(bitwiseNotLower, bitwiseNotWidth);
                lower = InterpretStaticProofBitPattern((~bits) & (modulus - BigInteger.One),
                    bitwiseNot.Type, bitwiseNotWidth);
                upper = lower;
                return true;
            }
            lower = new BigInteger(bitwiseNotMinimum);
            upper = new BigInteger(bitwiseNotMaximum);
            return true;
        }
        if (expression is BoundUnary unary
            && unary.Type == unary.Operand.Type
            && TryGetStaticProofIntegerInterval(unary.Operand, firstLocalVariable, facts,
                out var operandLower, out var operandUpper))
        {
            switch (unary.Operator)
            {
                case TokenKind.Plus:
                    lower = operandLower;
                    upper = operandUpper;
                    break;
                case TokenKind.Minus:
                    lower = -operandUpper;
                    upper = -operandLower;
                    break;
                default:
                    lower = BigInteger.Zero;
                    upper = BigInteger.Zero;
                    return false;
            }
            return IsWithinStaticProofIntegerRange(unary.Type, lower, upper);
        }
        if (expression is BoundBinary shift
            && (shift.Operator is TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned)
            && TryGetStaticProofIntegerWidth(shift.Type, out var width)
            && shift.Type == shift.Left.Type && shift.Right.Type == PrimitiveType.Int32
            && TryGetStaticProofIntegerInterval(shift.Left, firstLocalVariable, facts,
                out var shiftedLower, out var shiftedUpper)
            && TryGetStaticProofIntegerInterval(shift.Right, firstLocalVariable, facts,
                out var shiftCountLower, out var shiftCountUpper)
            && TryGetStaticProofIntegerRange(shift.Type, out var shiftMinimum, out var shiftMaximum))
        {
            if (shiftCountLower < BigInteger.Zero || shiftCountUpper >= width)
            {
                lower = BigInteger.Zero;
                upper = BigInteger.Zero;
                return false;
            }
            if (shiftedLower == shiftedUpper && shiftCountLower == shiftCountUpper)
            {
                var count = (int)shiftCountLower;
                var bits = ToStaticProofBitPattern(shiftedLower, width);
                var shifted = shift.Operator switch {
                    TokenKind.ShiftLeft => (bits << count) & ((BigInteger.One << width) - BigInteger.One),
                    TokenKind.ShiftRight when shift.Type is PrimitiveType.Int32 or PrimitiveType.Int64
                        => shiftedLower >> count,
                    TokenKind.ShiftRight => bits >> count,
                    TokenKind.ShiftRightUnsigned => bits >> count,
                    _ => throw new InvalidOperationException(),
                };
                lower = InterpretStaticProofBitPattern(shifted, shift.Type, width);
                upper = lower;
                return true;
            }
            lower = new BigInteger(shiftMinimum);
            upper = new BigInteger(shiftMaximum);
            return true;
        }
        if (expression is BoundBinary bitwise
            && (bitwise.Operator is TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret)
            && TryGetStaticProofIntegerWidth(bitwise.Type, out var bitwiseWidth)
            && bitwise.Left.Type == bitwise.Type && bitwise.Right.Type == bitwise.Type
            && TryGetStaticProofIntegerInterval(bitwise.Left, firstLocalVariable, facts,
                out var bitwiseLeftLower, out var bitwiseLeftUpper)
            && TryGetStaticProofIntegerInterval(bitwise.Right, firstLocalVariable, facts,
                out var bitwiseRightLower, out var bitwiseRightUpper)
            && TryGetStaticProofIntegerRange(bitwise.Type, out var bitwiseMinimum, out var bitwiseMaximum))
        {
            if (bitwiseLeftLower == bitwiseLeftUpper && bitwiseRightLower == bitwiseRightUpper)
            {
                var leftBits = ToStaticProofBitPattern(bitwiseLeftLower, bitwiseWidth);
                var rightBits = ToStaticProofBitPattern(bitwiseRightLower, bitwiseWidth);
                var result = bitwise.Operator switch {
                    TokenKind.Ampersand => leftBits & rightBits,
                    TokenKind.Pipe => leftBits | rightBits,
                    TokenKind.Caret => leftBits ^ rightBits,
                    _ => throw new InvalidOperationException(),
                };
                lower = InterpretStaticProofBitPattern(result, bitwise.Type, bitwiseWidth);
                upper = lower;
                return true;
            }
            lower = new BigInteger(bitwiseMinimum);
            upper = new BigInteger(bitwiseMaximum);
            return true;
        }
        if (expression is BoundBinary binary
            && binary.Left.Type == binary.Right.Type
            && binary.Type == binary.Left.Type
            && TryGetStaticProofIntegerInterval(binary.Left, firstLocalVariable, facts,
                out var leftLower, out var leftUpper)
            && TryGetStaticProofIntegerInterval(binary.Right, firstLocalVariable, facts,
                out var rightLower, out var rightUpper))
        {
            switch (binary.Operator)
            {
                case TokenKind.Plus:
                    lower = leftLower + rightLower;
                    upper = leftUpper + rightUpper;
                    break;
                case TokenKind.Minus:
                    lower = leftLower - rightUpper;
                    upper = leftUpper - rightLower;
                    break;
                case TokenKind.Star:
                    // A bilinear product reaches its interval extrema at a rectangle corner.
                    var product1 = leftLower * rightLower;
                    var product2 = leftLower * rightUpper;
                    var product3 = leftUpper * rightLower;
                    var product4 = leftUpper * rightUpper;
                    lower = BigInteger.Min(BigInteger.Min(product1, product2), BigInteger.Min(product3, product4));
                    upper = BigInteger.Max(BigInteger.Max(product1, product2), BigInteger.Max(product3, product4));
                    break;
                case TokenKind.Slash:
                    return TryGetStaticProofDivisionInterval(binary.Type, binary.Right,
                        leftLower, leftUpper, rightLower, rightUpper,
                        firstLocalVariable, facts, out lower, out upper);
                case TokenKind.Percent:
                    return TryGetStaticProofRemainderInterval(binary.Type, binary.Right,
                        leftLower, leftUpper, rightLower, rightUpper,
                        firstLocalVariable, facts, out lower, out upper);
                default:
                    lower = BigInteger.Zero;
                    upper = BigInteger.Zero;
                    return false;
            }
            return IsWithinStaticProofIntegerRange(binary.Type, lower, upper);
        }

        lower = BigInteger.Zero;
        upper = BigInteger.Zero;
        return false;
    }

    private bool TryGetStaticProofDivisionInterval(PrimitiveType type, BoundExpression divisor,
        BigInteger dividendLower, BigInteger dividendUpper, BigInteger divisorLower, BigInteger divisorUpper,
        int firstLocalVariable, Dictionary<StaticProofPredicate, bool> facts,
        out BigInteger lower, out BigInteger upper)
    {
        lower = BigInteger.Zero;
        upper = BigInteger.Zero;
        if (divisorLower <= BigInteger.Zero && divisorUpper >= BigInteger.Zero
            && !IsStaticProofIntegerZeroExcluded(divisor, firstLocalVariable, facts))
        {
            return false;
        }

        List<(BigInteger Lower, BigInteger Upper)> dividendRanges = [];
        if (dividendLower < BigInteger.Zero)
        {
            var negativeUpper = BigInteger.Min(dividendUpper, -BigInteger.One);
            if (dividendLower <= negativeUpper)
            {
                dividendRanges.Add((dividendLower, negativeUpper));
            }
        }
        if (dividendUpper >= BigInteger.Zero)
        {
            var nonnegativeLower = BigInteger.Max(dividendLower, BigInteger.Zero);
            if (nonnegativeLower <= dividendUpper)
            {
                dividendRanges.Add((nonnegativeLower, dividendUpper));
            }
        }

        List<(BigInteger Lower, BigInteger Upper)> divisorRanges = [];
        if (divisorLower < BigInteger.Zero)
        {
            var negativeUpper = BigInteger.Min(divisorUpper, -BigInteger.One);
            if (divisorLower <= negativeUpper)
            {
                divisorRanges.Add((divisorLower, negativeUpper));
            }
        }
        if (divisorUpper > BigInteger.Zero)
        {
            var positiveLower = BigInteger.Max(divisorLower, BigInteger.One);
            if (positiveLower <= divisorUpper)
            {
                divisorRanges.Add((positiveLower, divisorUpper));
            }
        }

        var minimumQuotient = BigInteger.Zero;
        var maximumQuotient = BigInteger.Zero;
        var hasQuotient = false;
        var isSigned = type is PrimitiveType.Int32 or PrimitiveType.Int64;
        var signedMinimum = type == PrimitiveType.Int32 ? new BigInteger(int.MinValue) : new BigInteger(long.MinValue);
        foreach (var (numeratorLower, numeratorUpper) in dividendRanges)
        {
            foreach (var (denominatorLower, denominatorUpper) in divisorRanges)
            {
                if (isSigned && numeratorLower <= signedMinimum && numeratorUpper >= signedMinimum
                    && denominatorLower <= -BigInteger.One && denominatorUpper >= -BigInteger.One)
                {
                    return false;
                }

                var quotient1 = numeratorLower / denominatorLower;
                var quotient2 = numeratorLower / denominatorUpper;
                var quotient3 = numeratorUpper / denominatorLower;
                var quotient4 = numeratorUpper / denominatorUpper;
                var pairLower = BigInteger.Min(BigInteger.Min(quotient1, quotient2),
                    BigInteger.Min(quotient3, quotient4));
                var pairUpper = BigInteger.Max(BigInteger.Max(quotient1, quotient2),
                    BigInteger.Max(quotient3, quotient4));
                if (!hasQuotient)
                {
                    minimumQuotient = pairLower;
                    maximumQuotient = pairUpper;
                    hasQuotient = true;
                }
                else
                {
                    minimumQuotient = BigInteger.Min(minimumQuotient, pairLower);
                    maximumQuotient = BigInteger.Max(maximumQuotient, pairUpper);
                }
            }
        }
        if (!hasQuotient || !IsWithinStaticProofIntegerRange(type, minimumQuotient, maximumQuotient))
        {
            return false;
        }

        lower = minimumQuotient;
        upper = maximumQuotient;
        return true;
    }

    private bool TryGetStaticProofRemainderInterval(PrimitiveType type, BoundExpression divisor,
        BigInteger dividendLower, BigInteger dividendUpper, BigInteger divisorLower, BigInteger divisorUpper,
        int firstLocalVariable, Dictionary<StaticProofPredicate, bool> facts,
        out BigInteger lower, out BigInteger upper)
    {
        lower = BigInteger.Zero;
        upper = BigInteger.Zero;
        if (divisorLower <= BigInteger.Zero && divisorUpper >= BigInteger.Zero
            && !IsStaticProofIntegerZeroExcluded(divisor, firstLocalVariable, facts))
        {
            return false;
        }

        var dividendMagnitude = BigInteger.Max(BigInteger.Abs(dividendLower), BigInteger.Abs(dividendUpper));
        var divisorMagnitude = BigInteger.Max(BigInteger.Abs(divisorLower), BigInteger.Abs(divisorUpper));
        if (divisorMagnitude <= BigInteger.Zero)
        {
            return false;
        }
        var remainderMagnitude = BigInteger.Min(dividendMagnitude, divisorMagnitude - BigInteger.One);
        if (dividendLower >= BigInteger.Zero)
        {
            lower = BigInteger.Zero;
            upper = remainderMagnitude;
        }
        else if (dividendUpper <= BigInteger.Zero)
        {
            lower = -remainderMagnitude;
            upper = BigInteger.Zero;
        }
        else
        {
            lower = -remainderMagnitude;
            upper = remainderMagnitude;
        }
        return IsWithinStaticProofIntegerRange(type, lower, upper);
    }

    private bool IsStaticProofIntegerZeroExcluded(BoundExpression expression,
        int firstLocalVariable, Dictionary<StaticProofPredicate, bool> facts)
    {
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            return IsStaticProofIntegerZeroExcluded(unsafeExpression.Expression, firstLocalVariable, facts);
        }
        if (expression is not BoundRead read
            || !CanUseStaticProofVariable(read.Variable, firstLocalVariable))
        {
            return false;
        }
        foreach (var (fact, established) in facts)
        {
            if (fact.Variable == read.Variable && fact.ValueType == read.Type
                && fact.OtherVariable < 0 && fact.ConstantType == fact.ValueType
                && fact.Kind == StaticProofPredicateKind.Equal && !established
                && TryGetStaticProofIntegerValue(fact.ConstantType, fact.Constant, out var value)
                && value == decimal.Zero)
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryGetStaticProofIntegerConstant(BoundConstant constant, out BigInteger value)
    {
        if (TryGetStaticProofIntegerValue(constant.Type, constant.Value, out var numericValue))
        {
            value = new BigInteger(numericValue);
            return true;
        }
        value = BigInteger.Zero;
        return false;
    }

    private static bool IsWithinStaticProofIntegerRange(PrimitiveType type,
        BigInteger lower, BigInteger upper) =>
        TryGetStaticProofIntegerRange(type, out var minimum, out var maximum)
            && lower >= new BigInteger(minimum) && upper <= new BigInteger(maximum);

    private bool TryGetStaticProofPredicate(BoundExpression expression, int firstLocalVariable,
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
            && CanUseStaticProofVariable(booleanRead.Variable, firstLocalVariable))
        {
            predicate = new StaticProofPredicate(StaticProofPredicateKind.Boolean, booleanRead.Variable,
                -1, PrimitiveType.Bool, PrimitiveType.Bool, 0);
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
            else if (binary.Left is BoundRead leftVariable && binary.Right is BoundRead rightVariable)
            {
                if (leftVariable.Variable == rightVariable.Variable
                    && leftVariable.Type == rightVariable.Type
                    && leftVariable.Type is not (PrimitiveType.Float32 or PrimitiveType.Float64)
                    && IsStaticProofScalar(leftVariable.Type)
                    && (operation is TokenKind.EqualsEquals or TokenKind.BangEquals))
                {
                    predicate = new StaticProofPredicate(StaticProofPredicateKind.ReflexiveEquality,
                        -1, -1, leftVariable.Type, rightVariable.Type, 0);
                    truth = operation == TokenKind.EqualsEquals;
                    return true;
                }
                return TryGetStaticProofVariablePredicate(leftVariable, rightVariable, operation,
                    firstLocalVariable, out predicate, out truth);
            }
            else
            {
                predicate = default;
                truth = false;
                return false;
            }
            if (!CanUseStaticProofVariable(read.Variable, firstLocalVariable) || !IsStaticProofScalar(read.Type))
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
                predicate = new StaticProofPredicate(floatingPredicateKind, read.Variable, -1, read.Type,
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
            predicate = new StaticProofPredicate(kind, read.Variable, -1, read.Type,
                constant.Type, constant.Value);
            truth = operation is TokenKind.EqualsEquals or TokenKind.Less or TokenKind.LessEquals;
            return true;
        }
        predicate = default;
        truth = false;
        return false;
    }

    private bool TryGetStaticProofVariablePredicate(BoundRead left, BoundRead right,
        TokenKind operation, int firstLocalVariable, out StaticProofPredicate predicate, out bool truth)
    {
        predicate = default;
        truth = false;
        if (!CanUseStaticProofVariable(left.Variable, firstLocalVariable)
            || !CanUseStaticProofVariable(right.Variable, firstLocalVariable)
            || left.Type != right.Type || !IsStaticProofScalar(left.Type))
        {
            return false;
        }

        StaticProofPredicateKind kind;
        var variable = left.Variable;
        var otherVariable = right.Variable;
        switch (operation)
        {
            case TokenKind.EqualsEquals:
            case TokenKind.BangEquals:
                kind = StaticProofPredicateKind.VariableEqual;
                if (variable > otherVariable)
                {
                    (variable, otherVariable) = (otherVariable, variable);
                }
                truth = operation == TokenKind.EqualsEquals;
                break;
            case TokenKind.Less:
            case TokenKind.GreaterEquals:
                kind = StaticProofPredicateKind.VariableLess;
                truth = operation == TokenKind.Less;
                break;
            case TokenKind.Greater:
            case TokenKind.LessEquals:
                kind = StaticProofPredicateKind.VariableLess;
                (variable, otherVariable) = (otherVariable, variable);
                truth = operation == TokenKind.Greater;
                break;
            default:
                return false;
        }

        predicate = new StaticProofPredicate(kind, variable, otherVariable, left.Type, right.Type, 0);
        return true;
    }

    private bool CanUseStaticProofVariable(int variable, int firstLocalVariable) =>
        variable >= 0 && variable < _staticProofVariables.Length
            && IsStaticProofScalar(_staticProofVariables[variable].Type)
            && (variable >= firstLocalVariable
                || (!_staticProofVariables[variable].IsOut && !_staticProofVariables[variable].IsInit
                    && !_staticProofVariables[variable].IsImplicitRef));

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
            case BoundNativeFixedArrayInitialization fixedArray:
            {
                var elements = ImmutableArray.CreateBuilder<BoundExpression>(fixedArray.Elements.Length);
                foreach (var element in fixedArray.Elements)
                {
                    elements.Add(ResolveConstExpression(element, locals, functions, variableCount));
                }
                return fixedArray with { Elements = elements.MoveToImmutable() };
            }
            case BoundNativeFixedArrayElement fixedArrayElement:
                return fixedArrayElement with {
                    Index = ResolveConstExpression(fixedArrayElement.Index, locals, functions, variableCount),
                };
            case BoundNativeStorageAddress address:
                return address with { Index = ResolveConstExpression(address.Index, locals, functions, variableCount) };
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
