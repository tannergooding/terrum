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
                default:
                    return false;
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
        Func<BoundExpression, BoundExpression> rewrite) =>
        rewrite(expression.RewriteNestedExpressions(item => RewriteExpressions(item, rewrite)));

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

    private static BoundExpression RewriteCalls(BoundExpression expression, Func<BoundCall, BoundCall> rewrite)
    {
        var rewritten = expression.RewriteNestedExpressions(item => RewriteCalls(item, rewrite));
        return rewritten is BoundCall call ? rewrite(call) : rewritten;
    }
}
