// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class BoundTreeTraversal
{
    internal static bool IsWithinLimits(BoundBlock body)
    {
        var pending = new Stack<(object? Node, int Depth)>();
        pending.Push((body, 0));
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var item))
        {
            if (item.Node is null || item.Depth > SemanticVerifier.MaximumDepth || --remaining < 0)
            {
                return false;
            }
            var depth = item.Depth + 1;
            switch (item.Node)
            {
                case BoundBlock block:
                    if (block.Statements.IsDefault)
                    {
                        return false;
                    }
                    foreach (var statement in block.Statements)
                    {
                        pending.Push((statement, depth));
                    }
                    break;
                case BoundLocal local:
                    pending.Push((local.Initializer, depth));
                    break;
                case BoundAssignment assignment:
                    pending.Push((assignment.Value, depth));
                    break;
                case BoundOptionalAssignment assignment:
                    pending.Push((assignment.Place, depth));
                    pending.Push((assignment.Value, depth));
                    break;
                case BoundFieldAssignment assignment:
                    pending.Push((assignment.Value, depth));
                    break;
                case BoundBufferAssignment assignment:
                    pending.Push((assignment.Index, depth));
                    pending.Push((assignment.Value, depth));
                    break;
                case BoundViewSet view:
                    pending.Push((view.Index, depth));
                    pending.Push((view.Value, depth));
                    break;
                case BoundExpressionStatement expression:
                    pending.Push((expression.Expression, depth));
                    break;
                case BoundIf branch:
                    pending.Push((branch.Condition, depth));
                    pending.Push((branch.Then, depth));
                    if (branch.Else is not null)
                    {
                        pending.Push((branch.Else, depth));
                    }
                    break;
                case BoundWhile loop:
                    pending.Push((loop.Condition, depth));
                    pending.Push((loop.Body, depth));
                    if (loop.Iterator is not null)
                    {
                        pending.Push((loop.Iterator, depth));
                    }
                    break;
                case BoundTryFinally cleanup:
                    pending.Push((cleanup.Body, depth));
                    pending.Push((cleanup.Finally, depth));
                    break;
                case BoundTryCatch recovery:
                    pending.Push((recovery.Body, depth));
                    pending.Push((recovery.Catch, depth));
                    if (recovery.Finally is not null)
                    {
                        pending.Push((recovery.Finally, depth));
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    pending.Push((unsafeBlock.Body, depth));
                    break;
                case BoundViewScope view:
                    pending.Push((view.Owner, depth));
                    pending.Push((view.Start, depth));
                    pending.Push((view.Length, depth));
                    pending.Push((view.Body, depth));
                    if (view.RangeEnd is not null)
                    {
                        pending.Push((view.RangeEnd, depth));
                    }
                    break;
                case BoundReturn { Value: BoundExpression value }:
                    pending.Push((value, depth));
                    break;
                case BoundRequire require:
                    pending.Push((require.Condition, depth));
                    break;
                case BoundCall call:
                    if (call.Arguments.IsDefault)
                    {
                        return false;
                    }
                    foreach (var argument in call.Arguments)
                    {
                        pending.Push((argument, depth));
                    }
                    break;
                case BoundIndirectCall call:
                    if (call.Arguments.IsDefault)
                    {
                        return false;
                    }
                    pending.Push((call.Target, depth));
                    foreach (var argument in call.Arguments)
                    {
                        pending.Push((argument, depth));
                    }
                    break;
                case BoundOptionalLift optional:
                    pending.Push((optional.Value, depth));
                    break;
                case BoundCoalesce coalesce:
                    pending.Push((coalesce.Optional, depth));
                    pending.Push((coalesce.Fallback, depth));
                    break;
                case BoundReadonlyProjection projection:
                    pending.Push((projection.Value, depth));
                    break;
                case BoundIsNull isNull:
                    pending.Push((isNull.Value, depth));
                    break;
                case BoundIsSome isSome:
                    pending.Push((isSome.Value, depth));
                    break;
                case BoundIsVariablePattern pattern:
                    pending.Push((pattern.Value, depth));
                    break;
                case BoundViewRead view:
                    pending.Push((view.Index, depth));
                    break;
                case BoundSpanLength span:
                    pending.Push((span.View, depth));
                    break;
                case BoundSpanRead span:
                    pending.Push((span.View, depth));
                    pending.Push((span.Index, depth));
                    break;
                case BoundSpanAdvance span:
                    pending.Push((span.View, depth));
                    break;
                case BoundFieldLoan loan:
                    pending.Push((loan.Place, depth));
                    break;
                case BoundMutableFieldLoan loan:
                    pending.Push((loan.Place, depth));
                    break;
                case BoundUpdate update:
                    pending.Push((update.Place, depth));
                    break;
                case BoundCompoundAssignment compound:
                    pending.Push((compound.Place, depth));
                    pending.Push((compound.Right, depth));
                    break;
                case BoundNominalUpdate update:
                    pending.Push((update.Place, depth));
                    break;
                case BoundUnsafeExpression unsafeExpression:
                    pending.Push((unsafeExpression.Expression, depth));
                    break;
                case BoundBufferRead buffer:
                    pending.Push((buffer.Index, depth));
                    break;
                case BoundFieldRead field:
                    pending.Push((field.Receiver, depth));
                    break;
                case BoundUnary unary:
                    pending.Push((unary.Operand, depth));
                    break;
                case BoundCast cast:
                    pending.Push((cast.Operand, depth));
                    break;
                case BoundBinary binary:
                    pending.Push((binary.Left, depth));
                    pending.Push((binary.Right, depth));
                    break;
                case BoundConditional conditional:
                    pending.Push((conditional.Condition, depth));
                    pending.Push((conditional.WhenTrue, depth));
                    pending.Push((conditional.WhenFalse, depth));
                    break;
                case BoundExpression expression:
                    if (!expression.HasValidNestedExpressions)
                    {
                        return false;
                    }
                    foreach (var child in expression.NestedExpressions)
                    {
                        pending.Push((child, depth));
                    }
                    break;
            }
        }
        return true;
    }

    internal static bool ContainsStatement(BoundBlock body, Predicate<BoundStatement> predicate)
    {
        var pending = new Stack<(BoundStatement? Statement, int Depth)>();
        pending.Push((body, 0));
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var item))
        {
            if (item.Statement is null || item.Depth > SemanticVerifier.MaximumDepth || --remaining < 0)
            {
                return false;
            }
            if (predicate(item.Statement))
            {
                return true;
            }
            var depth = item.Depth + 1;
            switch (item.Statement)
            {
                case BoundBlock block:
                    if (block.Statements.IsDefault)
                    {
                        return false;
                    }
                    foreach (var statement in block.Statements)
                    {
                        pending.Push((statement, depth));
                    }
                    break;
                case BoundIf branch:
                    pending.Push((branch.Then, depth));
                    if (branch.Else is not null)
                    {
                        pending.Push((branch.Else, depth));
                    }
                    break;
                case BoundWhile loop:
                    pending.Push((loop.Body, depth));
                    if (loop.Iterator is not null)
                    {
                        pending.Push((loop.Iterator, depth));
                    }
                    break;
                case BoundTryFinally cleanup:
                    pending.Push((cleanup.Body, depth));
                    pending.Push((cleanup.Finally, depth));
                    break;
                case BoundTryCatch recovery:
                    pending.Push((recovery.Body, depth));
                    pending.Push((recovery.Catch, depth));
                    if (recovery.Finally is not null)
                    {
                        pending.Push((recovery.Finally, depth));
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    pending.Push((unsafeBlock.Body, depth));
                    break;
                case BoundViewScope view:
                    pending.Push((view.Body, depth));
                    break;
            }
        }
        return false;
    }

    internal static BoundFunction RewriteCalls(BoundFunction function, Func<BoundCall, BoundCall> rewrite) =>
        function with { Body = (BoundBlock)RewriteCalls(function.Body, rewrite) };

    internal static BoundFunction RewriteExpressions(BoundFunction function,
        Func<BoundExpression, BoundExpression> rewrite) =>
        function with { Body = (BoundBlock)RewriteExpressions(function.Body, rewrite) };

    private static BoundStatement RewriteExpressions(BoundStatement statement,
        Func<BoundExpression, BoundExpression> rewrite) =>
        statement switch {
            BoundBlock block => block with {
                Statements = [.. block.Statements.Select(item => RewriteExpressions(item, rewrite))],
            },
            BoundIf branch => branch with {
                Condition = RewriteExpressions(branch.Condition, rewrite),
                Then = RewriteExpressions(branch.Then, rewrite),
                Else = branch.Else is null ? null : RewriteExpressions(branch.Else, rewrite),
            },
            BoundTryFinally scope => scope with {
                Body = (BoundBlock)RewriteExpressions(scope.Body, rewrite),
                Finally = (BoundBlock)RewriteExpressions(scope.Finally, rewrite),
            },
            BoundTryCatch scope => scope with {
                Body = (BoundBlock)RewriteExpressions(scope.Body, rewrite),
                Catch = (BoundBlock)RewriteExpressions(scope.Catch, rewrite),
                Finally = scope.Finally is null ? null : (BoundBlock)RewriteExpressions(scope.Finally, rewrite),
            },
            BoundWhile loop => loop with {
                Condition = RewriteExpressions(loop.Condition, rewrite),
                Body = RewriteExpressions(loop.Body, rewrite),
                Iterator = loop.Iterator is null ? null : RewriteExpressions(loop.Iterator, rewrite),
            },
            BoundUnsafe unsafeBlock => unsafeBlock with {
                Body = (BoundBlock)RewriteExpressions(unsafeBlock.Body, rewrite),
            },
            BoundViewScope view => view with {
                Owner = RewriteExpressions(view.Owner, rewrite),
                Start = RewriteExpressions(view.Start, rewrite),
                Length = RewriteExpressions(view.Length, rewrite),
                Body = (BoundBlock)RewriteExpressions(view.Body, rewrite),
                RangeEnd = view.RangeEnd is null ? null : RewriteExpressions(view.RangeEnd, rewrite),
            },
            BoundLocal local => local with { Initializer = RewriteExpressions(local.Initializer, rewrite) },
            BoundAssignment assignment => assignment with { Value = RewriteExpressions(assignment.Value, rewrite) },
            BoundOptionalAssignment assignment => assignment with {
                Place = RewriteExpressions(assignment.Place, rewrite),
                Value = RewriteExpressions(assignment.Value, rewrite),
            },
            BoundFieldAssignment assignment => assignment with { Value = RewriteExpressions(assignment.Value, rewrite) },
            BoundBufferAssignment assignment => assignment with {
                Index = RewriteExpressions(assignment.Index, rewrite),
                Value = RewriteExpressions(assignment.Value, rewrite),
            },
            BoundViewSet view => view with {
                Index = RewriteExpressions(view.Index, rewrite),
                Value = RewriteExpressions(view.Value, rewrite),
            },
            BoundExpressionStatement expression => expression with {
                Expression = RewriteExpressions(expression.Expression, rewrite),
            },
            BoundReturn result => result with {
                Value = result.Value is null ? null : RewriteExpressions(result.Value, rewrite),
            },
            BoundRequire require => require with { Condition = RewriteExpressions(require.Condition, rewrite) },
            _ => statement,
        };

    private static BoundExpression RewriteExpressions(BoundExpression expression,
        Func<BoundExpression, BoundExpression> rewrite)
    {
        var rewritten = expression switch {
            BoundCall call => call with {
                Arguments = [.. call.Arguments.Select(item => RewriteExpressions(item, rewrite))],
            },
            BoundIndirectCall call => call with {
                Target = RewriteExpressions(call.Target, rewrite),
                Arguments = [.. call.Arguments.Select(item => RewriteExpressions(item, rewrite))],
            },
            BoundOptionalLift optional => optional with { Value = RewriteExpressions(optional.Value, rewrite) },
            BoundCoalesce coalesce => coalesce with {
                Optional = RewriteExpressions(coalesce.Optional, rewrite),
                Fallback = RewriteExpressions(coalesce.Fallback, rewrite),
            },
            BoundReadonlyProjection projection => projection with {
                Value = RewriteExpressions(projection.Value, rewrite),
            },
            BoundIsNull isNull => isNull with { Value = RewriteExpressions(isNull.Value, rewrite) },
            BoundIsSome isSome => isSome with { Value = RewriteExpressions(isSome.Value, rewrite) },
            BoundIsVariablePattern pattern => pattern with { Value = RewriteExpressions(pattern.Value, rewrite) },
            BoundViewRead view => view with { Index = RewriteExpressions(view.Index, rewrite) },
            BoundSpanLength span => span with { View = RewriteExpressions(span.View, rewrite) },
            BoundSpanRead span => span with {
                View = RewriteExpressions(span.View, rewrite),
                Index = RewriteExpressions(span.Index, rewrite),
            },
            BoundSpanAdvance span => span with { View = RewriteExpressions(span.View, rewrite) },
            BoundFieldLoan loan => loan with {
                Place = (BoundFieldRead)RewriteExpressions(loan.Place, rewrite),
            },
            BoundMutableFieldLoan loan => loan with {
                Place = (BoundFieldRead)RewriteExpressions(loan.Place, rewrite),
            },
            BoundUnary unary => unary with { Operand = RewriteExpressions(unary.Operand, rewrite) },
            BoundCast cast => cast with { Operand = RewriteExpressions(cast.Operand, rewrite) },
            BoundUnsafeExpression unsafeExpression => unsafeExpression with {
                Expression = RewriteExpressions(unsafeExpression.Expression, rewrite),
            },
            BoundFieldRead field => field with { Receiver = RewriteExpressions(field.Receiver, rewrite) },
            BoundUpdate update => update with { Place = RewriteExpressions(update.Place, rewrite) },
            BoundCompoundAssignment compound => compound with {
                Place = RewriteExpressions(compound.Place, rewrite),
                Right = RewriteExpressions(compound.Right, rewrite),
            },
            BoundNominalUpdate update => update with { Place = RewriteExpressions(update.Place, rewrite) },
            BoundBinary binary => binary with {
                Left = RewriteExpressions(binary.Left, rewrite),
                Right = RewriteExpressions(binary.Right, rewrite),
            },
            BoundBufferRead buffer => buffer with { Index = RewriteExpressions(buffer.Index, rewrite) },
            BoundConditional conditional => conditional with {
                Condition = RewriteExpressions(conditional.Condition, rewrite),
                WhenTrue = RewriteExpressions(conditional.WhenTrue, rewrite),
                WhenFalse = RewriteExpressions(conditional.WhenFalse, rewrite),
            },
            _ => expression.RewriteNestedExpressions(item => RewriteExpressions(item, rewrite)),
        };
        return rewrite(rewritten);
    }

    private static BoundStatement RewriteCalls(BoundStatement statement, Func<BoundCall, BoundCall> rewrite) =>
        statement switch {
            BoundBlock block => block with {
                Statements = [.. block.Statements.Select(item => RewriteCalls(item, rewrite))],
            },
            BoundIf branch => branch with {
                Condition = RewriteCalls(branch.Condition, rewrite),
                Then = RewriteCalls(branch.Then, rewrite),
                Else = branch.Else is null ? null : RewriteCalls(branch.Else, rewrite),
            },
            BoundTryFinally scope => scope with {
                Body = (BoundBlock)RewriteCalls(scope.Body, rewrite),
                Finally = (BoundBlock)RewriteCalls(scope.Finally, rewrite),
            },
            BoundTryCatch scope => scope with {
                Body = (BoundBlock)RewriteCalls(scope.Body, rewrite),
                Catch = (BoundBlock)RewriteCalls(scope.Catch, rewrite),
                Finally = scope.Finally is null ? null : (BoundBlock)RewriteCalls(scope.Finally, rewrite),
            },
            BoundWhile loop => loop with {
                Condition = RewriteCalls(loop.Condition, rewrite),
                Body = RewriteCalls(loop.Body, rewrite),
                Iterator = loop.Iterator is null ? null : RewriteCalls(loop.Iterator, rewrite),
            },
            BoundUnsafe unsafeBlock => unsafeBlock with {
                Body = (BoundBlock)RewriteCalls(unsafeBlock.Body, rewrite),
            },
            BoundViewScope view => view with {
                Owner = RewriteCalls(view.Owner, rewrite),
                Start = RewriteCalls(view.Start, rewrite),
                Length = RewriteCalls(view.Length, rewrite),
                Body = (BoundBlock)RewriteCalls(view.Body, rewrite),
                RangeEnd = view.RangeEnd is null ? null : RewriteCalls(view.RangeEnd, rewrite),
            },
            BoundLocal local => local with { Initializer = RewriteCalls(local.Initializer, rewrite) },
            BoundAssignment assignment => assignment with { Value = RewriteCalls(assignment.Value, rewrite) },
            BoundOptionalAssignment assignment => assignment with {
                Place = RewriteCalls(assignment.Place, rewrite),
                Value = RewriteCalls(assignment.Value, rewrite),
            },
            BoundFieldAssignment assignment => assignment with { Value = RewriteCalls(assignment.Value, rewrite) },
            BoundBufferAssignment assignment => assignment with {
                Index = RewriteCalls(assignment.Index, rewrite),
                Value = RewriteCalls(assignment.Value, rewrite),
            },
            BoundViewSet view => view with {
                Index = RewriteCalls(view.Index, rewrite),
                Value = RewriteCalls(view.Value, rewrite),
            },
            BoundExpressionStatement expression => expression with {
                Expression = RewriteCalls(expression.Expression, rewrite),
            },
            BoundReturn result => result with {
                Value = result.Value is null ? null : RewriteCalls(result.Value, rewrite),
            },
            BoundRequire require => require with { Condition = RewriteCalls(require.Condition, rewrite) },
            _ => statement,
        };

    private static BoundExpression RewriteCalls(BoundExpression expression, Func<BoundCall, BoundCall> rewrite) =>
        expression switch {
            BoundCall call => rewrite(call with {
                Arguments = [.. call.Arguments.Select(item => RewriteCalls(item, rewrite))],
            }),
            BoundIndirectCall call => call with {
                Target = RewriteCalls(call.Target, rewrite),
                Arguments = [.. call.Arguments.Select(item => RewriteCalls(item, rewrite))],
            },
            BoundOptionalLift optional => optional with { Value = RewriteCalls(optional.Value, rewrite) },
            BoundCoalesce coalesce => coalesce with {
                Optional = RewriteCalls(coalesce.Optional, rewrite),
                Fallback = RewriteCalls(coalesce.Fallback, rewrite),
            },
            BoundReadonlyProjection projection => projection with {
                Value = RewriteCalls(projection.Value, rewrite),
            },
            BoundIsNull isNull => isNull with { Value = RewriteCalls(isNull.Value, rewrite) },
            BoundIsSome isSome => isSome with { Value = RewriteCalls(isSome.Value, rewrite) },
            BoundIsVariablePattern pattern => pattern with { Value = RewriteCalls(pattern.Value, rewrite) },
            BoundViewRead view => view with { Index = RewriteCalls(view.Index, rewrite) },
            BoundSpanLength span => span with { View = RewriteCalls(span.View, rewrite) },
            BoundSpanRead span => span with {
                View = RewriteCalls(span.View, rewrite),
                Index = RewriteCalls(span.Index, rewrite),
            },
            BoundSpanAdvance span => span with { View = RewriteCalls(span.View, rewrite) },
            BoundFieldLoan loan => loan with { Place = (BoundFieldRead)RewriteCalls(loan.Place, rewrite) },
            BoundMutableFieldLoan loan => loan with {
                Place = (BoundFieldRead)RewriteCalls(loan.Place, rewrite),
            },
            BoundUnary unary => unary with { Operand = RewriteCalls(unary.Operand, rewrite) },
            BoundCast cast => cast with { Operand = RewriteCalls(cast.Operand, rewrite) },
            BoundUnsafeExpression unsafeExpression => unsafeExpression with {
                Expression = RewriteCalls(unsafeExpression.Expression, rewrite),
            },
            BoundFieldRead field => field with { Receiver = RewriteCalls(field.Receiver, rewrite) },
            BoundUpdate update => update with { Place = RewriteCalls(update.Place, rewrite) },
            BoundCompoundAssignment compound => compound with {
                Place = RewriteCalls(compound.Place, rewrite),
                Right = RewriteCalls(compound.Right, rewrite),
            },
            BoundNominalUpdate update => update with { Place = RewriteCalls(update.Place, rewrite) },
            BoundBinary binary => binary with {
                Left = RewriteCalls(binary.Left, rewrite),
                Right = RewriteCalls(binary.Right, rewrite),
            },
            BoundBufferRead buffer => buffer with { Index = RewriteCalls(buffer.Index, rewrite) },
            BoundConditional conditional => conditional with {
                Condition = RewriteCalls(conditional.Condition, rewrite),
                WhenTrue = RewriteCalls(conditional.WhenTrue, rewrite),
                WhenFalse = RewriteCalls(conditional.WhenFalse, rewrite),
            },
            _ => expression.RewriteNestedExpressions(item => RewriteCalls(item, rewrite)),
        };
}
