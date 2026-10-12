// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Generic;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private readonly record struct CleanupSwitchTransfer(bool IsContinue, int Depth, int Arm = -1);

    private sealed record CleanupCountedLoop(BoundWhile Loop, BoundBlock Body, BoundAssignment Iterator,
        BoundBinary Step, bool InitiallyEnters);

    private static CleanupCountedLoop? RecognizeCleanupCountedLoop(BoundBlock block, BoundFunction function,
        BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (!block.IsManagedLexicalScope
            || block.Statements is not [var initializer, BoundWhile loop]
            || block.Span != loop.Span || loop.IsPostTested || loop.SwitchArmVariable >= 0)
        {
            return null;
        }
        int variable;
        BoundExpression initialValue;
        switch (initializer)
        {
            case BoundLocal local:
                variable = local.Variable;
                initialValue = local.Initializer;
                break;
            case BoundAssignment initialAssignment:
                variable = initialAssignment.Variable;
                initialValue = initialAssignment.Value;
                break;
            default:
                return null;
        }
        if (variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
            || variable >= function.Variables.Length
            || function.Variables[variable] is not {
                Type: PrimitiveType.Int32, IsMutable: true,
                IsOut: false, IsInit: false, IsInitializationLocal: false,
            }
            || initialValue is not BoundConstant {
                Type: PrimitiveType.Int32, Value: >= int.MinValue and <= int.MaxValue,
            } initial
            || loop.Condition is not BoundBinary {
                Operator: TokenKind.Less or TokenKind.Greater, Type: PrimitiveType.Bool,
                Left: BoundRead { Type: PrimitiveType.Int32 } counter,
                Right: BoundConstant { Type: PrimitiveType.Int32, Value: >= int.MinValue and <= int.MaxValue } bound,
            } condition
            || counter.Variable != variable)
        {
            return null;
        }
        // Scalar lowering appends the iterator when there is no local continue;
        // otherwise it uses Iterator so every local continue executes the step.
        BoundBlock body;
        BoundStatement iterator;
        if (loop.Iterator is BoundStatement separate && loop.Body is BoundBlock separateBody)
        {
            body = separateBody;
            iterator = separate;
        }
        else if (loop.Iterator is null
            && loop.Body is BoundBlock { Statements: [BoundBlock appendedBody, BoundAssignment appended] })
        {
            body = appendedBody;
            iterator = appended;
        }
        else
        {
            return null;
        }
        if (iterator is not BoundAssignment assignment || assignment.Variable != variable
            || assignment.Value is not BoundBinary {
                Operator: TokenKind.Plus or TokenKind.Minus, Type: PrimitiveType.Int32,
                Left: BoundRead { Type: PrimitiveType.Int32 } stepCounter,
                Right: BoundConstant { Type: PrimitiveType.Int32, Value: 1 },
            } step
            || stepCounter.Variable != variable
            || (condition.Operator == TokenKind.Less) != (step.Operator == TokenKind.Plus)
            || !CleanupSwitchStateIsPrivate(body, variable, program, ref remaining, depth + 1, allowRead: true))
        {
            return null;
        }
        // Strict bounds and a unit step keep every update inside int32: an
        // ascending step starts below bound <= MaxValue (descending is dual).
        // The mathematical distance to the bound decreases by one, without
        // computing that distance in int32 or enumerating iterations.
        return new CleanupCountedLoop(loop, body, assignment, step,
            condition.Operator == TokenKind.Less ? initial.Value < bound.Value : initial.Value > bound.Value);
    }

    private sealed class CleanupSwitchFlow(bool canComplete)
    {
        internal bool CanComplete { get; set; } = canComplete;
        internal HashSet<CleanupSwitchTransfer> Transfers { get; } = [];

        internal void Join(CleanupSwitchFlow other, ref int remaining, int depth)
        {
            CanComplete |= other.CanComplete;
            foreach (var transfer in other.Transfers)
            {
                Count(ref remaining, depth);
                _ = Transfers.Add(transfer);
            }
        }
    }

    private static CleanupSwitchFlow? ProveCleanupSwitch(BoundWhile loop, BoundFunction function,
        BoundProgram program, ref int remaining, int depth, Dictionary<BoundExpression, byte> canBeFacts)
    {
        Count(ref remaining, depth);
        var variable = loop.SwitchArmVariable;
        if (variable < function.ParameterCount || variable >= function.Variables.Length
            || function.Variables[variable] is not {
                Name: "<switch-arm>", Type: PrimitiveType.Int32, IsMutable: true,
                IsOut: false, IsInit: false, IsInitializationLocal: false,
            }
            || loop.Condition is not BoundConstant { Type: PrimitiveType.Bool, Value: 1 }
            || loop.IsPostTested || loop.Iterator is not null)
        {
            return null;
        }
        var arms = new List<BoundBlock>();
        var selected = loop.Body;
        while (selected is BoundIf conditional)
        {
            Count(ref remaining, depth + arms.Count);
            if (conditional.Condition is not BoundBinary {
                Operator: TokenKind.EqualsEquals, Type: PrimitiveType.Bool,
                Left: BoundRead { Type: PrimitiveType.Int32 } state,
                Right: BoundConstant { Type: PrimitiveType.Int32 } ordinal,
            }
                || state.Variable != variable || ordinal.Value != arms.Count
                || conditional.Then is not BoundBlock arm || conditional.Else is null)
            {
                return null;
            }
            arms.Add(arm);
            selected = conditional.Else;
        }
        if (selected is not BoundBlock fallback || arms.Count == 0)
        {
            return null;
        }
        arms.Add(fallback);
        var edges = new HashSet<int>[arms.Count];
        var exits = new CleanupSwitchFlow[arms.Count];
        for (var index = 0; index < arms.Count; index++)
        {
            var flow = AnalyzeCleanupSwitchStatement(arms[index], variable, arms.Count, 1,
                function, program, ref remaining, depth + 1, canBeFacts);
            if (flow is null || flow.CanComplete)
            {
                // Falling off a dispatcher body is an implicit backedge, not arm completion.
                return null;
            }
            edges[index] = [];
            exits[index] = new CleanupSwitchFlow(false);
            foreach (var transfer in flow.Transfers)
            {
                Count(ref remaining, depth + 1);
                if (transfer.Depth == 1)
                {
                    if (!transfer.IsContinue)
                    {
                        exits[index].CanComplete = true;
                    }
                    else if (transfer.Arm >= 0)
                    {
                        _ = edges[index].Add(transfer.Arm);
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    _ = exits[index].Transfers.Add(transfer with { Depth = transfer.Depth - 1 });
                }
            }
        }
        // Every possible entry arm must reach a finite exit. A back edge in source
        // order is harmless; only a cycle in the reachable transfer graph rejects.
        var colors = new byte[arms.Count];
        var outcomes = new CleanupSwitchFlow?[arms.Count];
        var result = new CleanupSwitchFlow(false);
        for (var arm = 0; arm < arms.Count; arm++)
        {
            var outcome = VisitCleanupSwitchArm(arm, edges, exits, colors, outcomes, ref remaining, depth + 1);
            if (outcome is null)
            {
                return null;
            }
            result.Join(outcome, ref remaining, depth + 1);
        }
        return result;
    }

    private static CleanupSwitchFlow? VisitCleanupSwitchArm(int arm, HashSet<int>[] edges,
        CleanupSwitchFlow[] exits, byte[] colors, CleanupSwitchFlow?[] outcomes, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (colors[arm] == 1)
        {
            return null;
        }
        if (colors[arm] == 2)
        {
            return outcomes[arm];
        }
        colors[arm] = 1;
        var result = new CleanupSwitchFlow(false);
        result.Join(exits[arm], ref remaining, depth + 1);
        foreach (var target in edges[arm])
        {
            var successor = VisitCleanupSwitchArm(target, edges, exits, colors, outcomes, ref remaining, depth + 1);
            if (successor is null)
            {
                return null;
            }
            result.Join(successor, ref remaining, depth + 1);
        }
        if (!result.CanComplete && result.Transfers.Count == 0)
        {
            return null;
        }
        colors[arm] = 2;
        outcomes[arm] = result;
        return result;
    }

    private static CleanupSwitchFlow? AnalyzeCleanupSwitchStatement(BoundStatement statement, int variable,
        int armCount, int dispatcherDepth, BoundFunction function, BoundProgram program, ref int remaining,
        int depth, Dictionary<BoundExpression, byte> canBeFacts)
    {
        Count(ref remaining, depth);
        switch (statement)
        {
            case BoundBlock block:
                var counted = RecognizeCleanupCountedLoop(block, function, program, ref remaining, depth + 1);
                if (counted is not null)
                {
                    return CleanupSwitchStateIsPrivate(counted.Iterator, variable, program, ref remaining, depth + 1)
                        ? AnalyzeCleanupSwitchLoop(counted.Loop, counted.Body, variable, armCount, dispatcherDepth,
                            function, program, ref remaining, depth + 1, canBeFacts, counted) : null;
                }
                var sequence = new CleanupSwitchFlow(true);
                for (var index = 0; index < block.Statements.Length && sequence.CanComplete; index++)
                {
                    CleanupSwitchFlow? next;
                    if (block.Statements[index] is BoundAssignment assignment && assignment.Variable == variable)
                    {
                        if (assignment.Value is not BoundConstant { Type: PrimitiveType.Int32 } target
                            || target.Value < 0 || target.Value >= armCount
                            || index + 1 >= block.Statements.Length
                            || block.Statements[++index] is not BoundContinue transfer
                            || transfer.TargetDepth != dispatcherDepth)
                        {
                            return null;
                        }
                        next = new CleanupSwitchFlow(false);
                        _ = next.Transfers.Add(new CleanupSwitchTransfer(true, dispatcherDepth, (int)target.Value));
                    }
                    else
                    {
                        next = AnalyzeCleanupSwitchStatement(block.Statements[index], variable, armCount, dispatcherDepth,
                            function, program, ref remaining, depth + 1, canBeFacts);
                    }
                    if (next is null)
                    {
                        return null;
                    }
                    sequence.CanComplete = false;
                    sequence.Join(next, ref remaining, depth + 1);
                }
                return sequence;
            case BoundIf conditional:
                if (!CleanupSwitchExpressionIsSafe(conditional.Condition, variable, program, ref remaining, depth + 1))
                {
                    return null;
                }
                var branches = new CleanupSwitchFlow(false);
                for (var outcome = 0; outcome < 2; outcome++)
                {
                    if (!CanBe(conditional.Condition, outcome == 0, ref remaining, canBeFacts))
                    {
                        continue;
                    }
                    var branch = outcome == 0 ? conditional.Then : conditional.Else;
                    var flow = branch is null ? new CleanupSwitchFlow(true)
                        : AnalyzeCleanupSwitchStatement(branch, variable, armCount, dispatcherDepth,
                            function, program, ref remaining, depth + 1, canBeFacts);
                    if (flow is null)
                    {
                        return null;
                    }
                    branches.Join(flow, ref remaining, depth + 1);
                }
                return branches;
            case BoundWhile nested:
                if (nested.SwitchArmVariable >= 0)
                {
                    return nested.SwitchArmVariable != variable
                        && CleanupSwitchStateIsPrivate(nested, variable, program, ref remaining, depth + 1)
                        ? ProveCleanupSwitch(nested, function, program, ref remaining, depth + 1, canBeFacts) : null;
                }
                return AnalyzeCleanupSwitchLoop(nested, nested.Body, variable, armCount, dispatcherDepth,
                    function, program, ref remaining, depth + 1, canBeFacts);
            case BoundTryFinally region:
                if (!CleanupSwitchStateIsPrivate(region.Finally, variable, program, ref remaining, depth + 1)
                    || HasUnsupportedCleanupFlow(region.Finally, function, program, ref remaining, depth + 1, canBeFacts))
                {
                    return null;
                }
                return AnalyzeCleanupSwitchStatement(region.Body, variable, armCount, dispatcherDepth,
                    function, program, ref remaining, depth + 1, canBeFacts);
            case BoundUnsafe scope:
                return AnalyzeCleanupSwitchStatement(scope.Body, variable, armCount, dispatcherDepth,
                    function, program, ref remaining, depth + 1, canBeFacts);
            case BoundBreak exit:
                var broken = new CleanupSwitchFlow(false);
                _ = broken.Transfers.Add(new CleanupSwitchTransfer(false, exit.TargetDepth));
                return broken;
            case BoundContinue continuation:
                var continued = new CleanupSwitchFlow(false);
                _ = continued.Transfers.Add(new CleanupSwitchTransfer(true, continuation.TargetDepth));
                return continued;
            default:
                return CleanupSwitchStateIsPrivate(statement, variable, program, ref remaining, depth + 1)
                    ? new CleanupSwitchFlow(true) : null;
        }
    }

    private static CleanupSwitchFlow? AnalyzeCleanupSwitchLoop(BoundWhile loop, BoundStatement loopBody,
        int variable, int armCount, int dispatcherDepth, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, Dictionary<BoundExpression, byte> canBeFacts, CleanupCountedLoop? counted = null)
    {
        Count(ref remaining, depth);
        if (!CleanupSwitchExpressionIsSafe(loop.Condition, variable, program, ref remaining, depth + 1))
        {
            return null;
        }
        var mayEnter = counted?.InitiallyEnters ?? CanBe(loop.Condition, true, ref remaining, canBeFacts);
        var mayExit = counted is not null ? !counted.InitiallyEnters
            : CanBe(loop.Condition, false, ref remaining, canBeFacts);
        if (!loop.IsPostTested && !mayEnter)
        {
            return new CleanupSwitchFlow(true);
        }
        var body = AnalyzeCleanupSwitchStatement(loopBody, variable, armCount, dispatcherDepth + 1,
            function, program, ref remaining, depth + 1, canBeFacts);
        if (body is null)
        {
            return null;
        }
        var loopExits = new CleanupSwitchFlow(!loop.IsPostTested && mayExit);
        var repeats = body.CanComplete;
        foreach (var transfer in body.Transfers)
        {
            Count(ref remaining, depth + 1);
            if (transfer.Depth == 1)
            {
                if (transfer.IsContinue)
                {
                    if (counted is not null && loop.Iterator is null)
                    {
                        // A forged continue would bypass an appended iterator.
                        return null;
                    }
                    repeats = true;
                }
                else
                {
                    loopExits.CanComplete = true;
                }
            }
            else
            {
                _ = loopExits.Transfers.Add(transfer with { Depth = transfer.Depth - 1 });
            }
        }
        // Reuse the body exit/transfer proof; only the recognized counted loop
        // adds a finite backedge to the existing zero/one-iteration boundary.
        if (repeats)
        {
            if (counted is null && (!loop.IsPostTested || mayEnter
                || loop.Iterator is not null
                    && !CleanupSwitchStateIsPrivate(loop.Iterator, variable, program, ref remaining, depth + 1)))
            {
                return null;
            }
            loopExits.CanComplete = true;
        }
        return loopExits;
    }

    private static bool CleanupSwitchStateIsPrivate(BoundStatement statement, int variable,
        BoundProgram program, ref int remaining, int depth, bool allowRead = false)
    {
        Count(ref remaining, depth);
        switch (statement)
        {
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    if (!CleanupSwitchStateIsPrivate(child, variable, program, ref remaining, depth + 1, allowRead))
                    {
                        return false;
                    }
                }
                return true;
            case BoundLocal local:
                return local.Variable != variable
                    && CleanupSwitchExpressionIsSafe(local.Initializer, variable, program, ref remaining, depth + 1, allowRead);
            case BoundVacantLocal vacant:
                return vacant.Variable != variable;
            case BoundLifetimeEnd end:
                return end.Variable != variable;
            case BoundAssignment assignment:
                return assignment.Variable != variable
                    && CleanupSwitchExpressionIsSafe(assignment.Value, variable, program, ref remaining, depth + 1, allowRead);
            case BoundExpressionStatement expression:
                return CleanupSwitchExpressionIsSafe(expression.Expression, variable, program, ref remaining, depth + 1, allowRead);
            case BoundRequire requirement:
                return CleanupSwitchExpressionIsSafe(requirement.Condition, variable, program, ref remaining, depth + 1, allowRead);
            case BoundIf conditional:
                return CleanupSwitchExpressionIsSafe(conditional.Condition, variable, program, ref remaining, depth + 1, allowRead)
                    && CleanupSwitchStateIsPrivate(conditional.Then, variable, program, ref remaining, depth + 1, allowRead)
                    && (conditional.Else is null
                        || CleanupSwitchStateIsPrivate(conditional.Else, variable, program, ref remaining, depth + 1, allowRead));
            case BoundWhile loop:
                return CleanupSwitchExpressionIsSafe(loop.Condition, variable, program, ref remaining, depth + 1, allowRead)
                    && CleanupSwitchStateIsPrivate(loop.Body, variable, program, ref remaining, depth + 1, allowRead)
                    && (loop.Iterator is null
                        || CleanupSwitchStateIsPrivate(loop.Iterator, variable, program, ref remaining, depth + 1, allowRead));
            case BoundTryFinally region:
                return CleanupSwitchStateIsPrivate(region.Body, variable, program, ref remaining, depth + 1, allowRead)
                    && CleanupSwitchStateIsPrivate(region.Finally, variable, program, ref remaining, depth + 1, allowRead);
            case BoundUnsafe scope:
                return CleanupSwitchStateIsPrivate(scope.Body, variable, program, ref remaining, depth + 1, allowRead);
            case BoundBreak or BoundContinue:
                return true;
            default:
                return false;
        }
    }

    private static bool CleanupSwitchExpressionIsSafe(BoundExpression expression, int variable,
        BoundProgram program, ref int remaining, int depth, bool allowRead = false)
    {
        Count(ref remaining, depth);
        switch (expression)
        {
            case BoundConstant:
                return true;
            case BoundRead read:
                return allowRead || read.Variable != variable;
            case BoundOutArgument output:
                return output.Variable != variable;
            case BoundIsInitialized initialized:
                return initialized.Variable != variable;
            case BoundUnary unary:
                return CleanupSwitchExpressionIsSafe(unary.Operand, variable, program, ref remaining, depth + 1, allowRead);
            case BoundBinary binary:
                return CleanupSwitchExpressionIsSafe(binary.Left, variable, program, ref remaining, depth + 1, allowRead)
                    && CleanupSwitchExpressionIsSafe(binary.Right, variable, program, ref remaining, depth + 1, allowRead);
            case BoundCast cast:
                return CleanupSwitchExpressionIsSafe(cast.Operand, variable, program, ref remaining, depth + 1, allowRead);
            case BoundConditional conditional:
                return CleanupSwitchExpressionIsSafe(conditional.Condition, variable, program, ref remaining, depth + 1, allowRead)
                    && CleanupSwitchExpressionIsSafe(conditional.WhenTrue, variable, program, ref remaining, depth + 1, allowRead)
                    && CleanupSwitchExpressionIsSafe(conditional.WhenFalse, variable, program, ref remaining, depth + 1, allowRead);
            case BoundUpdate update:
                return CleanupSwitchExpressionIsSafe(update.Place, variable, program, ref remaining, depth + 1);
            case BoundCompoundAssignment assignment:
                return CleanupSwitchExpressionIsSafe(assignment.Place, variable, program, ref remaining, depth + 1)
                    && CleanupSwitchExpressionIsSafe(assignment.Right, variable, program, ref remaining, depth + 1, allowRead);
            case BoundCall call:
                if (program.Functions[call.Function].DoesNotReturn)
                {
                    return false;
                }
                foreach (var argument in call.Arguments)
                {
                    if (!CleanupSwitchExpressionIsSafe(argument, variable, program, ref remaining, depth + 1, allowRead))
                    {
                        return false;
                    }
                }
                return true;
            default:
                return false;
        }
    }
}
