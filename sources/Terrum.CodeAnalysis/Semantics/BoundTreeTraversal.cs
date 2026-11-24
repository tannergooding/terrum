// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal static class BoundTreeTraversal
{
    internal static bool IsWithinLimits(BoundBlock body)
        => VisitExpressionsCore(body, static _ => { }, requireComplete: true);

    internal static bool VisitGenericNominalTypeArguments(
        BoundProgram program, Action<PrimitiveType> visitor)
    {
        if (program.GenericNominalTypeInstances.IsDefault)
        {
            return false;
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        foreach (var instance in program.GenericNominalTypeInstances)
        {
            if (instance is null || instance.Arguments.IsDefault)
            {
                return false;
            }
            foreach (var argument in instance.Arguments)
            {
                if (argument is null || --remaining < 0)
                {
                    return false;
                }
                if (argument.Kind == BoundGenericArgumentKind.Type)
                {
                    visitor(argument.Type);
                    if (argument.TypeArgumentTerm is
                        BoundGenericMethodTypeTerm typeArgumentTerm
                        && !BoundGenericMethodTypeTerms.TryRewrite(
                            typeArgumentTerm, type => {
                                visitor(type);
                                return type;
                            }, static ordinal => ordinal,
                            static definition => definition,
                            static identity => identity, out _))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    internal static bool VisitStandardClassDeclarationTypeSpecs(
        ImmutableArray<BoundStandardClassDeclaration> declarations,
        Action<BoundSourceTypeProviderSnapshot, uint> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        if (declarations.IsDefault)
        {
            return false;
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        foreach (var declaration in declarations)
        {
            if (declaration is null || --remaining < 0
                || declaration.Expectations.IsDefault
                || !Visit(declaration.Declaration))
            {
                return false;
            }

            foreach (var expectation in declaration.Expectations)
            {
                if (expectation is null || --remaining < 0
                    || !Visit(expectation.ExpectedDeclaration))
                {
                    return false;
                }
            }
        }

        return true;

        bool Visit(BoundStandardClassTypeView view)
        {
            if (view is null || view.Provider is null
                || view.Fields.IsDefault || view.Methods.IsDefault
                || view.Properties.IsDefault)
            {
                return false;
            }

            foreach (var field in view.Fields)
            {
                if (field is null || --remaining < 0)
                {
                    return false;
                }

                visitor(view.Provider, field.TypeSpecReference);
            }

            foreach (var property in view.Properties)
            {
                if (property is null || --remaining < 0)
                {
                    return false;
                }

                visitor(view.Provider, property.TypeSpecReference);
            }

            foreach (var method in view.Methods)
            {
                if (method is null || method.Parameters.IsDefault
                    || --remaining < 0)
                {
                    return false;
                }

                visitor(view.Provider, method.ReturnTypeSpecReference);
                if (method.ReceiverTypeSpecReference != 0)
                {
                    visitor(view.Provider,
                        method.ReceiverTypeSpecReference);
                }

                foreach (var parameter in method.Parameters)
                {
                    if (parameter is null || --remaining < 0)
                    {
                        return false;
                    }

                    visitor(view.Provider, parameter.TypeSpecReference);
                }
            }

            return true;
        }
    }

    internal static ImmutableArray<BoundStandardClassDeclaration>
        RewriteStandardClassDeclarations(
            ImmutableArray<BoundStandardClassDeclaration> declarations,
            Func<BoundStandardClassTypeView, BoundStandardClassTypeView>
                    rewriteView,
            Func<BoundSourceTypeExpectation, BoundSourceTypeExpectation>
                rewriteExpectation)
    {
        ArgumentNullException.ThrowIfNull(rewriteView);
            ArgumentNullException.ThrowIfNull(rewriteExpectation);
        if (declarations.IsDefault)
        {
            throw new ArgumentException(
                "Standard class declarations cannot be default.",
                nameof(declarations));
        }

        return [.. declarations.Select(declaration =>
            declaration with {
                Declaration = rewriteView(declaration.Declaration),
                Expectations = [.. declaration.Expectations.Select(
                    expectation => expectation with {
                        Binding = rewriteExpectation(expectation.Binding),
                        ExpectedDeclaration = rewriteView(
                            expectation.ExpectedDeclaration),
                    })],
            })];
    }

    internal static bool VisitSourceGenericTypeArguments(
        BoundProgram program, Action<PrimitiveType> visitor)
    {
        if (program.SourceGenericTypeInstances.IsDefault)
        {
            return false;
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        foreach (var instance in program.SourceGenericTypeInstances)
        {
            if (instance is null || instance.TypeArguments.IsDefault
                || instance.ConstantArguments.IsDefault)
            {
                return false;
            }
            foreach (var argument in instance.TypeArguments)
            {
                if (--remaining < 0)
                {
                    return false;
                }
                visitor(argument);
            }
            foreach (var argument in instance.ConstantArguments)
            {
                if (argument is null || --remaining < 0)
                {
                    return false;
                }

                if (argument.Kind
                    == BoundGenericMethodConstantArgumentKind.Value)
                {
                    if (remaining < 2)
                    {
                        return false;
                    }
                    remaining -= 2;
                    visitor(argument.DomainType);
                    visitor(argument.CarrierType);
                }
                else if (argument.Kind
                    != BoundGenericMethodConstantArgumentKind.Formal)
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static bool VisitGenericMethodFunctions(
        BoundProgram program, Action<BoundFunction> visitor)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(visitor);
        if (program.GenericMethodDefinitions.IsDefault)
        {
            return false;
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        foreach (var definition in program.GenericMethodDefinitions)
        {
            if (definition is null || definition.ProofProgram is not BoundProgram proofProgram
                || proofProgram.Functions.IsDefault)
            {
                return false;
            }
            foreach (var function in proofProgram.Functions)
            {
                if (function is null || --remaining < 0)
                {
                    return false;
                }

                var complete = true;
                if (!VisitExpressions(function.Body, _ =>
                    {
                        if (--remaining < 0)
                        {
                            complete = false;
                        }
                    }) || !complete
                    || !VisitContractExpressions(function, _ =>
                    {
                        if (--remaining < 0)
                        {
                            complete = false;
                        }
                    }) || !complete)
                {
                    return false;
                }
                visitor(function);
            }
        }

        return true;
    }

    internal static BoundProgram RewriteGenericMethodDefinitions(
        BoundProgram program,
        ImmutableArray<BoundGenericMethodDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (definitions.IsDefault)
        {
            throw new ArgumentException(
                "Generic method definitions cannot be default.",
                nameof(definitions));
        }

        return program with { GenericMethodDefinitions = definitions };
    }

    internal static bool TrySubstituteGenericMethodTypeDefault(
        BoundGenericMethodTypeDefault typeDefault,
        ImmutableArray<BoundGenericMethodTypeTerm> typeArguments,
        out BoundGenericMethodTypeDefault substituted) =>
        BoundGenericMethodTypeTerms.TrySubstitute(typeDefault,
            typeArguments, out substituted);

    internal static bool TryRewriteGenericMethodTypeDefault(
        BoundGenericMethodTypeDefault typeDefault,
        Func<PrimitiveType, PrimitiveType> rewriteType,
        Func<int, int> rewriteFormalOrdinal,
        Func<int, int> rewriteSourceGenericDefinition,
        Func<BoundGenericNominalTypeIdentity,
            BoundGenericNominalTypeIdentity> rewriteNominalIdentity,
        out BoundGenericMethodTypeDefault rewritten) =>
        BoundGenericMethodTypeTerms.TryRewrite(typeDefault, rewriteType,
            rewriteFormalOrdinal, rewriteSourceGenericDefinition,
            rewriteNominalIdentity, out rewritten);

    internal static bool TryRewriteGenericMethodTypeTerm(
        BoundGenericMethodTypeTerm typeTerm,
        Func<PrimitiveType, PrimitiveType> rewriteType,
        Func<int, int> rewriteFormalOrdinal,
        Func<int, int> rewriteSourceGenericDefinition,
        Func<BoundGenericNominalTypeIdentity,
            BoundGenericNominalTypeIdentity> rewriteNominalIdentity,
        out BoundGenericMethodTypeTerm rewritten) =>
        BoundGenericMethodTypeTerms.TryRewrite(typeTerm, rewriteType,
            rewriteFormalOrdinal, rewriteSourceGenericDefinition,
            rewriteNominalIdentity, out rewritten);

    internal static bool VisitExpressions(BoundBlock body, Action<BoundExpression> visitor)
        => VisitExpressionsCore(body, visitor, requireComplete: false);

    internal static bool VisitExpressionTree(BoundExpression expression,
        Action<BoundExpression> visitor)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(visitor);
        var pending = new Stack<(BoundExpression Expression, int Depth)>();
        pending.Push((expression, 0));
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var item))
        {
            if (item.Depth > SemanticVerifier.MaximumDepth
                || --remaining < 0
                || !item.Expression.HasValidNestedExpressions)
            {
                return false;
            }

            visitor(item.Expression);
            foreach (var child in item.Expression.NestedExpressions)
            {
                pending.Push((child, item.Depth + 1));
            }
        }

        return true;
    }

    internal static bool VisitContractExpressions(BoundFunction function,
        Action<BoundExpression> visitor)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(visitor);
        if (function.DeclarationContractClauses.IsDefault
            || function.CheckedOutputStatePredicates.IsDefault)
        {
            return false;
        }

        var statements = ImmutableArray.CreateBuilder<BoundStatement>(
            function.DeclarationContractClauses.Length
                + function.CheckedOutputStatePredicates.Length);
        foreach (var clause in function.DeclarationContractClauses)
        {
            if (clause is null || clause.Predicate is null)
            {
                return false;
            }
            statements.Add(new BoundExpressionStatement(clause.Predicate,
                clause.Span));
        }
        foreach (var predicate in function.CheckedOutputStatePredicates)
        {
            if (predicate is null || predicate.Predicate is null)
            {
                return false;
            }

            statements.Add(new BoundExpressionStatement(predicate.Predicate,
                function.Span));
        }

        return VisitExpressionsCore(new BoundBlock(statements.ToImmutable(),
            function.Span), visitor, requireComplete: true);
    }

    internal static bool VisitStatements(BoundBlock body, Action<BoundStatement> visitor)
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
            visitor(item.Statement);
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
        return true;
    }

    private static bool VisitExpressionsCore(BoundBlock body, Action<BoundExpression> visitor,
        bool requireComplete)
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
                case BoundVacantLocal or BoundLifetimeEnd or BoundOwnerDeclaration or BoundBreak or BoundContinue
                    or BoundDrop or BoundDropSpan or BoundDropConstructedFields
                    or BoundSymbolicLifetimeEnd or BoundSymbolicDrop:
                    if (requireComplete)
                    {
                        return false;
                    }
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
                case BoundStaticFieldAssignment assignment:
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
                case BoundReturn { Value: null }:
                    if (requireComplete)
                    {
                        return false;
                    }
                    break;
                case BoundRequire require:
                    pending.Push((require.Condition, depth));
                    break;
                case BoundExpression expression:
                    if (!expression.HasValidNestedExpressions)
                    {
                        return false;
                    }
                    visitor(expression);
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
        function with {
            Body = (BoundBlock)RewriteCalls(function.Body, rewrite),
            IndexedBody = function.IndexedBody?.RewriteCalls(rewrite),
            OrdinaryParameterDefaults =
                RewriteOrdinaryParameterDefaults(
                    function.OrdinaryParameterDefaults,
                    expression => RewriteCalls(expression, rewrite)),
            DeclarationContractClauses = RewriteDeclarationContractClauses(
                function.DeclarationContractClauses,
                predicate => RewriteCalls(predicate, rewrite)),
            CheckedOutputStatePredicates =
                RewriteOutputStatePredicates(
                    function.CheckedOutputStatePredicates,
                    predicate => RewriteCalls(predicate, rewrite)),
        };

    internal static BoundFunction RewriteExpressions(BoundFunction function,
        Func<BoundExpression, BoundExpression> rewrite) =>
        function with {
            Body = (BoundBlock)RewriteExpressions(function.Body, rewrite),
            IndexedBody = function.IndexedBody?.RewriteExpressions(rewrite),
            OrdinaryParameterDefaults =
                RewriteOrdinaryParameterDefaults(
                    function.OrdinaryParameterDefaults,
                    expression => RewriteExpressions(expression, rewrite)),
            DeclarationContractClauses = RewriteDeclarationContractClauses(
                function.DeclarationContractClauses,
                predicate => RewriteExpressions(predicate, rewrite)),
            CheckedOutputStatePredicates =
                RewriteOutputStatePredicates(
                    function.CheckedOutputStatePredicates,
                    predicate => RewriteExpressions(predicate, rewrite)),
        };

    internal static BoundFunction RewriteVariables(BoundFunction function,
        ImmutableArray<BoundVariable> variables, Func<int, int> rewriteVariable)
    {
        ArgumentNullException.ThrowIfNull(rewriteVariable);
        return function with {
            Variables = variables,
            Body = (BoundBlock)RewriteVariables(function.Body,
                rewriteVariable),
            IndexedBody = function.IndexedBody?.RewriteVariables(variables,
                rewriteVariable),
            OrdinaryParameterDefaults =
                RewriteOrdinaryParameterDefaults(
                    function.OrdinaryParameterDefaults,
                    expression => RewriteVariables(expression,
                        rewriteVariable)),
            DeclarationContractClauses = RewriteDeclarationContractClauses(
                function.DeclarationContractClauses,
                predicate => RewriteVariables(predicate,
                    rewriteVariable)),
            CheckedOutputStatePredicates =
                RewriteOutputStatePredicates(
                    function.CheckedOutputStatePredicates,
                    predicate => RewriteVariables(predicate,
                        rewriteVariable),
                    rewriteVariable),
            ConstructionHomeIndex = RewriteVariable(
                function.ConstructionHomeIndex, rewriteVariable),
        };
    }

    private static int RewriteVariable(int variable,
        Func<int, int> rewriteVariable)
        => variable < 0 ? variable : rewriteVariable(variable);

    internal static bool VisitOrdinaryParameterDefaultExpressions(
        BoundFunction function, Action<BoundExpression> visitor)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(visitor);
        if (function.OrdinaryParameterDefaults.IsDefault)
        {
            return false;
        }

        var pending = new Stack<(BoundExpression Expression, int Depth)>();
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        foreach (var parameterDefault
            in function.OrdinaryParameterDefaults)
        {
            if (parameterDefault is null || parameterDefault.IsPending
                || (parameterDefault.Value is null)
                    == (parameterDefault.Expression is null)
                || --remaining < 0)
            {
                return false;
            }

            pending.Push((
                parameterDefault.Value
                    ?? parameterDefault.Expression!,
                0));
        }

        while (pending.TryPop(out var item))
        {
            if (item.Depth > SemanticVerifier.MaximumDepth
                || --remaining < 0
                || !item.Expression.HasValidNestedExpressions)
            {
                return false;
            }

            visitor(item.Expression);
            foreach (var child in item.Expression.NestedExpressions)
            {
                pending.Push((child, item.Depth + 1));
            }
        }

        return true;
    }

    private static ImmutableArray<BoundOrdinaryParameterDefault>
        RewriteOrdinaryParameterDefaults(
            ImmutableArray<BoundOrdinaryParameterDefault> defaults,
            Func<BoundExpression, BoundExpression> rewrite)
    {
        ArgumentNullException.ThrowIfNull(rewrite);
        if (defaults.IsDefault)
        {
            return default;
        }

        var rewritten
            = ImmutableArray.CreateBuilder<BoundOrdinaryParameterDefault>(
                defaults.Length);
        foreach (var parameterDefault in defaults)
        {
            if (parameterDefault is null)
            {
                throw new InvalidOperationException(
                    "An ordinary parameter default entry is null.");
            }

            var value = parameterDefault.Value is BoundConstant constant
                ? rewrite(constant) as BoundConstant
                    ?? throw new InvalidOperationException(
                        "A rewritten ordinary parameter constant is not constant.")
                : null;
            var expression
                = parameterDefault.Expression is BoundExpression defaultExpression
                    ? rewrite(defaultExpression) : null;
            rewritten.Add(parameterDefault with {
                Value = value,
                Expression = expression,
            });
        }

        return rewritten.MoveToImmutable();
    }

    internal static BoundStatement RewriteVariables(BoundStatement statement,
        Func<int, int> rewriteVariable)
    {
        int Variable(int variable)
        {
            return RewriteVariable(variable, rewriteVariable);
        }

        BoundExpression Expression(BoundExpression expression)
        {
            return RewriteVariables(expression, rewriteVariable);
        }

        return statement switch {
            BoundBlock block => block with {
                Statements = [.. block.Statements.Select(child =>
                    RewriteVariables(child, rewriteVariable))],
                RetiredManagedLocals = block.RetiredManagedLocals.IsDefault
                    ? default : [.. block.RetiredManagedLocals.Select(Variable)],
            },
            BoundLocal local => local with {
                Variable = Variable(local.Variable),
                Initializer = Expression(local.Initializer),
            },
            BoundVacantLocal local => local with {
                Variable = Variable(local.Variable),
            },
            BoundLifetimeEnd lifetimeEnd => lifetimeEnd with {
                Variable = Variable(lifetimeEnd.Variable),
            },
            BoundSymbolicLifetimeEnd lifetimeEnd =>
                lifetimeEnd with {
                    Variable = Variable(lifetimeEnd.Variable),
                },
            BoundOwnerDeclaration owner => owner with {
                Variable = Variable(owner.Variable),
            },
            BoundAssignment assignment => assignment with {
                Variable = Variable(assignment.Variable),
                Value = Expression(assignment.Value),
            },
            BoundOptionalAssignment assignment => assignment with {
                Place = Expression(assignment.Place),
                Value = Expression(assignment.Value),
            },
            BoundFieldAssignment assignment => assignment with {
                Variable = Variable(assignment.Variable),
                Value = Expression(assignment.Value),
            },
            BoundStaticFieldAssignment assignment => assignment with {
                Value = Expression(assignment.Value),
            },
            BoundBufferAssignment assignment => assignment with {
                Index = Expression(assignment.Index),
                Value = Expression(assignment.Value),
            },
            BoundExpressionStatement expression => expression with {
                Expression = Expression(expression.Expression),
            },
            BoundIf branch => branch with {
                Condition = Expression(branch.Condition),
                Then = RewriteVariables(branch.Then, rewriteVariable),
                Else = branch.Else is null ? null
                    : RewriteVariables(branch.Else, rewriteVariable),
            },
            BoundWhile loop => loop with {
                Condition = Expression(loop.Condition),
                Body = RewriteVariables(loop.Body, rewriteVariable),
                Iterator = loop.Iterator is null ? null
                    : RewriteVariables(loop.Iterator, rewriteVariable),
                SwitchArmVariable = Variable(loop.SwitchArmVariable),
            },
            BoundTryFinally scope => scope with {
                Body = (BoundBlock)RewriteVariables(scope.Body,
                    rewriteVariable),
                Finally = (BoundBlock)RewriteVariables(scope.Finally,
                    rewriteVariable),
            },
            BoundTryCatch scope => scope with {
                Body = (BoundBlock)RewriteVariables(scope.Body,
                    rewriteVariable),
                Catch = (BoundBlock)RewriteVariables(scope.Catch,
                    rewriteVariable),
                Finally = scope.Finally is null ? null
                    : (BoundBlock)RewriteVariables(scope.Finally,
                        rewriteVariable),
            },
            BoundViewScope view => view with {
                Variable = Variable(view.Variable),
                Owner = Expression(view.Owner),
                Start = Expression(view.Start),
                Length = Expression(view.Length),
                Body = (BoundBlock)RewriteVariables(view.Body,
                    rewriteVariable),
                RangeEnd = view.RangeEnd is null ? null
                    : Expression(view.RangeEnd),
            },
            BoundViewSet view => view with {
                Variable = Variable(view.Variable),
                Index = Expression(view.Index),
                Value = Expression(view.Value),
            },
            BoundDrop drop => drop with {
                Variable = Variable(drop.Variable),
            },
            BoundSymbolicDrop drop => drop with {
                Variable = Variable(drop.Variable),
            },
            BoundDropSpan drop => drop with {
                Variable = Variable(drop.Variable),
            },
            BoundDropConstructedFields drop => drop with {
                Variable = Variable(drop.Variable),
            },
            BoundUnsafe unsafeBlock => unsafeBlock with {
                Body = (BoundBlock)RewriteVariables(unsafeBlock.Body,
                    rewriteVariable),
            },
            BoundRequire require => require with {
                Condition = Expression(require.Condition),
            },
            BoundReturn result => result with {
                Value = result.Value is null ? null : Expression(result.Value),
            },
            _ => statement,
        };
    }

    private static BoundExpression RewriteVariables(BoundExpression expression,
        Func<int, int> rewriteVariable)
    {
        var rewritten = expression.RewriteNestedExpressions(child =>
            RewriteVariables(child, rewriteVariable));
        int Variable(int variable)
        {
            return RewriteVariable(variable, rewriteVariable);
        }

        return rewritten switch {
            BoundNativePointerAddress address => address with {
                Variable = Variable(address.Variable),
            },
            BoundNativeFixedArrayElement element => element with {
                Variable = Variable(element.Variable),
            },
            BoundNativeFixedArrayPathElement element => element with {
                Variable = Variable(element.Variable),
            },
            BoundNativeFixedArrayPointerAddress address => address with {
                Variable = Variable(address.Variable),
            },
            BoundNativeFixedArrayWrite write => write with {
                Variable = Variable(write.Variable),
            },
            BoundNativeFixedArrayPathWrite write => write with {
                Variable = Variable(write.Variable),
            },
            BoundNativeStorageAddress address => address with {
                Variable = Variable(address.Variable),
            },
            BoundPrivateReferenceForm formation => formation with {
                Place = RewritePrivatePlaceVariables(formation.Place,
                    rewriteVariable),
            },
            BoundPrivatePlaceRead read => read with {
                Place = RewritePrivatePlaceVariables(read.Place,
                    rewriteVariable),
            },
            BoundPrivatePlaceIsInitialized test => test with {
                Place = RewritePrivatePlaceVariables(test.Place,
                    rewriteVariable),
            },
            BoundPrivateCheckedStore store => store with {
                Destination = RewritePrivatePlaceVariables(
                    store.Destination, rewriteVariable),
            },
            BoundImplicitReferenceMaterialize materialize => materialize with {
                BackingVariable = Variable(materialize.BackingVariable),
            },
            BoundNativeOutputReservation reservation => reservation with {
                Variable = Variable(reservation.Variable),
            },
            BoundIsSome test => test with {
                Variable = Variable(test.Variable),
            },
            BoundTypedValuePattern test => test with {
                Variable = Variable(test.Variable),
            },
            BoundReferenceTypeTestPattern test => test with {
                Variable = test.Variable < 0
                    ? -1 : Variable(test.Variable),
            },
            BoundIsInitialized test => test with {
                Variable = Variable(test.Variable),
            },
            BoundPropertyInitialized test => test with {
                ReceiverVariable = Variable(test.ReceiverVariable),
            },
            BoundIsVariablePattern test => test with {
                Variable = Variable(test.Variable),
            },
            BoundRead read => read with {
                Variable = Variable(read.Variable),
            },
            BoundMove move => move with {
                Variable = Variable(move.Variable),
            },
            BoundReceiverLoan loan => loan with {
                Variable = Variable(loan.Variable),
            },
            BoundMutableReceiverLoan loan => loan with {
                Variable = Variable(loan.Variable),
            },
            BoundViewLength length => length with {
                Variable = Variable(length.Variable),
            },
            BoundViewRead read => read with {
                Variable = Variable(read.Variable),
            },
            BoundSnapshotSpan snapshot => snapshot with {
                Variable = Variable(snapshot.Variable),
            },
            BoundOutArgument argument => argument with {
                Variable = Variable(argument.Variable),
            },
            _ => rewritten,
        };
    }

    private static BoundPrivatePlace RewritePrivatePlaceVariables(
        BoundPrivatePlace place, Func<int, int> rewriteVariable)
    {
        int Variable(int variable)
        {
            return RewriteVariable(variable, rewriteVariable);
        }

        return place switch {
            BoundPrivateLocalPlace local => local with {
                Variable = Variable(local.Variable),
            },
            BoundPrivateInlineFieldPlace field => field with {
                Receiver = RewritePrivatePlaceVariables(field.Receiver,
                    rewriteVariable),
            },
            BoundPrivateObjectFieldPlace field => field,
            BoundPrivateReferentPlace referent => referent,
            BoundPrivateFixedArrayElementPlace element => element with {
                ArrayPlace = RewritePrivatePlaceVariables(
                    element.ArrayPlace, rewriteVariable),
            },
            BoundPrivateInvocationBufferElementPlace buffer => buffer,
            BoundPrivateLexicalViewElementPlace view => view with {
                Variable = Variable(view.Variable),
            },
            _ => throw new InvalidOperationException(
                "Unknown private place kind."),
        };
    }

    internal static BoundStruct RewriteTypes(BoundStruct structure,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        var fields = RewriteFields(structure.Fields, rewriteType);
        var origin = structure.StandardTypeOrigin;
        if (origin?.ExpectedDeclaration is BoundStruct expectedDeclaration)
        {
            var expectedFixedArrayTypes = origin.ExpectedFixedArrayTypes.IsDefault
                ? default
                : ImmutableArray.CreateRange(origin.ExpectedFixedArrayTypes
                    .Select(fixedArray => RewriteTypes(fixedArray,
                        rewriteType)));
            var expectedNativePointerTypes
                = origin.ExpectedNativePointerTypes.IsDefault
                    ? default
                    : ImmutableArray.CreateRange(
                        origin.ExpectedNativePointerTypes.Select(pointerType
                            => pointerType with {
                                PointeeType = rewriteType(
                                    pointerType.PointeeType),
                            }));
            var additionalExpectedDeclarations
                = origin.AdditionalExpectedDeclarations.IsDefault
                    ? default
                    : ImmutableArray.CreateRange(
                        origin.AdditionalExpectedDeclarations.Select(
                            declaration => RewriteTypes(declaration,
                                rewriteType) with {
                                    StandardTypeOrigin = null,
                                    StandardLayoutOnly = false,
                                }));
            origin = origin with {
                ExpectedDeclaration = expectedDeclaration with {
                    Fields = RewriteFields(expectedDeclaration.Fields,
                        rewriteType),
                },
                ExpectedFixedArrayTypes = expectedFixedArrayTypes,
                ExpectedNativePointerTypes = expectedNativePointerTypes,
                AdditionalExpectedDeclarations
                    = additionalExpectedDeclarations,
            };
        }

        return structure with {
            Fields = fields,
            StandardTypeOrigin = origin,
        };
    }

    internal static BoundStaticOwnerIdentity RewriteTypes(
        BoundStaticOwnerIdentity owner,
        Func<PrimitiveType, PrimitiveType> rewriteType)
        => owner with {
            TypeArguments = owner.TypeArguments.ToImmutableDictionary(
                static argument => argument.Key,
                argument => rewriteType(argument.Value),
                StringComparer.Ordinal),
        };

    internal static BoundStaticFieldDefinition RewriteTypes(
        BoundStaticFieldDefinition field,
        Func<PrimitiveType, PrimitiveType> rewriteType)
        => field with {
            Owner = RewriteTypes(field.Owner, rewriteType),
            OwnerType = rewriteType(field.OwnerType),
            Type = rewriteType(field.Type),
            FriendTargets = [.. field.FriendTargets.Select(target =>
                RewriteTypes(target, rewriteType))],
        };

    internal static BoundFriendTarget RewriteTypes(
        BoundFriendTarget target,
        Func<PrimitiveType, PrimitiveType> rewriteType)
        => target with {
            Type = target.Type == PrimitiveType.Void
                ? PrimitiveType.Void : rewriteType(target.Type),
            TypeArguments = [.. target.TypeArguments.Select<
                PrimitiveType?, PrimitiveType?>(
                    argument => argument is PrimitiveType type
                        ? rewriteType(type) : null)],
        };

    internal static BoundStaticInitializationGroup RewriteTypes(
        BoundStaticInitializationGroup group,
        Func<PrimitiveType, PrimitiveType> rewriteType)
        => group with {
            Owner = RewriteTypes(group.Owner, rewriteType),
            OwnerType = rewriteType(group.OwnerType),
        };

    private static ImmutableArray<BoundStructField> RewriteFields(
        ImmutableArray<BoundStructField> fields,
        Func<PrimitiveType, PrimitiveType> rewriteType)
        => [.. fields.Select(field => field with {
            Type = rewriteType(field.Type),
            StandardFieldOrigin = RewriteStandardFieldOrigin(
                field.StandardFieldOrigin, rewriteType),
        })];

    private static BoundStandardFieldOrigin? RewriteStandardFieldOrigin(
        BoundStandardFieldOrigin? origin,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        if (origin is null)
        {
            return null;
        }

        var expectedDeclaration
            = origin.ExpectedDeclaration is BoundStructField declaration
                ? declaration with {
                    Type = rewriteType(declaration.Type),
                    StandardFieldOrigin = null,
                } : null;
        var additionalExpectedDeclarations
            = origin.AdditionalExpectedDeclarations.IsDefault
                ? default
                : ImmutableArray.CreateRange(
                    origin.AdditionalExpectedDeclarations.Select(
                        declaration => declaration with {
                            Type = rewriteType(declaration.Type),
                            StandardFieldOrigin = null,
                        }));
        return origin with {
            ExpectedDeclaration = expectedDeclaration,
            AdditionalExpectedDeclarations
                = additionalExpectedDeclarations,
        };
    }

    internal static BoundNativeFixedArrayType RewriteTypes(
        BoundNativeFixedArrayType fixedArray,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        fixedArray with { ElementType = rewriteType(fixedArray.ElementType) };

    internal static CheckedReferenceTypeDefinition RewriteTypes(
        CheckedReferenceTypeDefinition referenceType,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        referenceType with { ReferentType = rewriteType(referenceType.ReferentType) };

    internal static BoundSpanTypeDefinition RewriteTypes(
        BoundSpanTypeDefinition spanType,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        spanType with { ElementType = rewriteType(spanType.ElementType) };

    internal static BoundGenericMethodOwner RewriteTypes(
        BoundGenericMethodOwner owner,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        owner with {
            ClosedType = rewriteType(owner.ClosedType),
            TypeArguments = [.. owner.TypeArguments.Select(rewriteType)],
            ConstantArguments = [.. owner.ConstantArguments.Select(
                argument => RewriteGenericMethodConstantArgument(argument,
                    rewriteType))],
        };

    internal static BoundSourceGenericTypeInstance RewriteTypes(
        BoundSourceGenericTypeInstance instance,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        instance with {
            TypeArguments = [.. instance.TypeArguments.Select(rewriteType)],
            ConstantArguments = [.. instance.ConstantArguments.Select(
                argument => RewriteGenericMethodConstantArgument(argument,
                    rewriteType))],
        };

    internal static BoundSourceGenericTypeDefinition RewriteTypes(
        BoundSourceGenericTypeDefinition definition,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        definition with {
            Formals = [.. definition.Formals.Select(formal =>
                formal with {
                    ConstantDomain = formal.ConstantDomain is
                        BoundGenericMethodTypeTerm domain
                        ? RewriteGenericMethodTypeTerm(domain, rewriteType)
                        : null,
                })],
            ConstantFormals = [.. definition.ConstantFormals.Select(formal =>
                formal with {
                    Domain = RewriteGenericMethodTypeTerm(formal.Domain,
                        rewriteType),
                })],
            TransparentConstantCarrierType =
                definition.TransparentConstantCarrierType is PrimitiveType
                    carrierType
                    ? rewriteType(carrierType) : null,
        };

    internal static BoundInterfaceType RewriteTypes(
        BoundInterfaceType type,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        type with {
            PrimitiveType = type.Kind == BoundInterfaceTypeKind.Primitive
                ? rewriteType(type.PrimitiveType) : type.PrimitiveType,
            TypeArguments = [.. type.TypeArguments.Select(argument =>
                RewriteTypes(argument, rewriteType))],
        };

    internal static BoundInterfaceTypeArgument RewriteTypes(
        BoundInterfaceTypeArgument argument,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        argument with {
            Type = argument.Type is BoundInterfaceType type
                ? RewriteTypes(type, rewriteType) : null,
            ConstantType = argument.ConstantType is BoundInterfaceType constantType
                ? RewriteTypes(constantType, rewriteType) : null,
            ConstantValue = argument.ConstantValue is BoundConstant constant
                ? RewriteConstantType(constant, rewriteType)
                : null,
        };

    internal static BoundInterfaceFormal RewriteTypes(
        BoundInterfaceFormal formal,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        formal with {
            ConstantType = formal.ConstantType is BoundInterfaceType constantType
                ? RewriteTypes(constantType, rewriteType) : null,
            TypeDefault = formal.TypeDefault is BoundInterfaceType typeDefault
                ? RewriteTypes(typeDefault, rewriteType) : null,
            ConstantDefault = formal.ConstantDefault is BoundConstant constant
                ? RewriteConstantType(constant, rewriteType) : null,
        };

    internal static BoundInterfaceConstraint RewriteTypes(
        BoundInterfaceConstraint constraint,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        constraint with {
            Type = constraint.Type is BoundInterfaceType type
                ? RewriteTypes(type, rewriteType) : null,
        };

    internal static BoundInterfaceMemberDefinition RewriteTypes(
        BoundInterfaceMemberDefinition member,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        member with {
            ResultType = member.ResultType is BoundInterfaceType resultType
                ? RewriteTypes(resultType, rewriteType) : null,
            TypeParameters = [.. member.TypeParameters.Select(formal =>
                RewriteTypes(formal, rewriteType))],
            Constraints = [.. member.Constraints.Select(constraint =>
                RewriteTypes(constraint, rewriteType))],
            Parameters = [.. member.Parameters.Select(parameter => parameter with {
                Type = RewriteTypes(parameter.Type, rewriteType),
                DefaultValue = parameter.DefaultValue is BoundConstant defaultValue
                    ? RewriteConstantType(defaultValue, rewriteType) : null,
                DefaultExpression =
                    parameter.DefaultExpression is BoundExpression expression
                        ? RewriteExpressionTypes(expression, rewriteType) : null,
            })],
            AssociatedDefaultType = member.AssociatedDefaultType is BoundInterfaceType associatedDefault
                ? RewriteTypes(associatedDefault, rewriteType) : null,
            AssociatedConstantType = member.AssociatedConstantType is BoundInterfaceType associatedConstantType
                ? RewriteTypes(associatedConstantType, rewriteType) : null,
            AssociatedConstantDefault = member.AssociatedConstantDefault is BoundConstant associatedConstant
                ? RewriteConstantType(associatedConstant, rewriteType) : null,
            NativeOutputContracts = [.. member.NativeOutputContracts.Select(
                output => output with {
                    PointeeType = rewriteType(output.PointeeType),
                })],
        };

    internal static BoundInterfaceMemberIdentity RewriteTypes(
        BoundInterfaceMemberIdentity identity,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        identity with {
            DeclaringInterface = RewriteTypes(identity.DeclaringInterface,
                rewriteType),
        };

    internal static BoundGenericMethodAssociatedRelation RewriteTypes(
        BoundGenericMethodAssociatedRelation relation,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        relation with {
            AssociatedType = RewriteTypes(relation.AssociatedType,
                rewriteType),
            RequiredType = RewriteGenericMethodTypeTerm(
                relation.RequiredType, rewriteType),
        };

    internal static BoundInterfaceWitnessMember RewriteTypes(
        BoundInterfaceWitnessMember member,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        member with {
            Requirement = RewriteTypes(member.Requirement, rewriteType),
            DefaultTarget = member.DefaultTarget is
                BoundInterfaceMemberIdentity defaultTarget
                    ? RewriteTypes(defaultTarget, rewriteType) : null,
        };

    internal static BoundInterfaceAssociatedTypeBinding RewriteTypes(
        BoundInterfaceAssociatedTypeBinding binding,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        binding with {
            Requirement = RewriteTypes(binding.Requirement, rewriteType),
            Type = RewriteTypes(binding.Type, rewriteType),
        };

    internal static BoundInterfaceAssociatedConstantBinding RewriteTypes(
        BoundInterfaceAssociatedConstantBinding binding,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        binding with {
            Requirement = RewriteTypes(binding.Requirement, rewriteType),
            Type = RewriteTypes(binding.Type, rewriteType),
            Value = RewriteConstantType(binding.Value, rewriteType),
        };

    internal static BoundInterfaceWitness RewriteTypes(
        BoundInterfaceWitness witness,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        witness with {
            ImplementingType = rewriteType(witness.ImplementingType),
            InterfaceType = RewriteTypes(witness.InterfaceType,
                rewriteType),
            Members = [.. witness.Members.Select(member =>
                RewriteTypes(member, rewriteType))],
            AssociatedTypes = [.. witness.AssociatedTypes.Select(binding =>
                RewriteTypes(binding, rewriteType))],
            AssociatedConstants = [.. witness.AssociatedConstants.Select(
                binding => RewriteTypes(binding, rewriteType))],
        };

    internal static BoundInterfaceCallIdentity RewriteTypes(
        BoundInterfaceCallIdentity identity,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        identity with {
            ImplementingType = rewriteType(identity.ImplementingType),
            WitnessInterface = RewriteTypes(identity.WitnessInterface,
                rewriteType),
            RequestedInterface = RewriteTypes(identity.RequestedInterface,
                rewriteType),
            Requirement = RewriteTypes(identity.Requirement,
                rewriteType),
        };

    internal static BoundForeignInterfaceCall RewriteTypes(
        BoundForeignInterfaceCall call,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        call with {
            Member = RewriteTypes(call.Member, rewriteType),
            NativeOutputContracts =
                call.NativeOutputContracts.IsDefault ? default : [
                    .. call.NativeOutputContracts.Select(output =>
                        output with {
                            PointeeType = rewriteType(output.PointeeType),
                        })],
        };

    internal static BoundGenericMethodFormal RewriteTypes(
        BoundGenericMethodFormal formal,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        formal with {
            PlaceholderType = rewriteType(formal.PlaceholderType),
            InterfaceConstraints = [.. formal.InterfaceConstraints.Select(
                constraint => RewriteTypes(constraint, rewriteType))],
            AssociatedRelations = [.. formal.AssociatedRelations.Select(
                relation => RewriteTypes(relation, rewriteType))],
        };

    internal static BoundGenericMethodFormalSignature RewriteTypes(
        BoundGenericMethodFormalSignature formal,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        formal with {
            InterfaceConstraints = [.. formal.InterfaceConstraints.Select(
                constraint => RewriteTypes(constraint, rewriteType))],
            AssociatedRelations = [.. formal.AssociatedRelations.Select(
                relation => RewriteTypes(relation, rewriteType))],
        };

    internal static BoundProgram RewriteInterfaceWitnesses(
        BoundProgram program,
        ImmutableArray<BoundInterfaceWitness> witnesses,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(rewriteType);
        if (witnesses.IsDefault)
        {
            throw new ArgumentException(
                "Interface witness rows cannot be default.",
                nameof(witnesses));
        }
        return program with {
            InterfaceWitnesses = [.. witnesses.Select(witness =>
                RewriteTypes(witness, rewriteType))],
        };
    }

    internal static BoundConstant RewriteConstantType(
        BoundConstant constant,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        var type = rewriteType(constant.Type);
        if (BoundEnumTypes.IsType(constant.Type)
            != BoundEnumTypes.IsType(type))
        {
            throw new InvalidOperationException(
                "Type rewriting cannot erase or invent enum value identity.");
        }
        var representationType = rewriteType(
            constant.RepresentationType);
        if (representationType == type)
        {
            return new BoundConstant(constant.Bits, type, constant.Span);
        }
        return new BoundConstant(constant.Bits, type,
            representationType, constant.Span);
    }

    internal static BoundInterfaceDefinition RewriteTypes(
        BoundInterfaceDefinition @interface,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        @interface with {
            Formals = [.. @interface.Formals.Select(formal => RewriteTypes(formal, rewriteType))],
            Bases = [.. @interface.Bases.Select(baseType => RewriteTypes(baseType, rewriteType))],
            Constraints = [.. @interface.Constraints.Select(constraint =>
                RewriteTypes(constraint, rewriteType))],
            Members = [.. @interface.Members.Select(member => RewriteTypes(member, rewriteType))],
            ForeignNativeInterfaceExpectedViews = [
                .. @interface.ForeignNativeInterfaceExpectedViews.Select(
                    expectedView => expectedView with {
                        Definition = RewriteTypes(expectedView.Definition,
                            rewriteType),
                    }),
            ],
        };

    internal static BoundClass RewriteTypes(BoundClass @class,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        @class with {
            Fields = [.. @class.Fields.Select(field => field with {
                Type = rewriteType(field.Type),
            })],
        };

    internal static BoundGenericNominalTypeDefault RewriteTypes(
        BoundGenericNominalTypeDefault defaultValue,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        defaultValue with {
            DecodedType = defaultValue.DecodedType is PrimitiveType decodedType
                ? rewriteType(decodedType) : null,
            DecodedConstantArgument =
                defaultValue.DecodedConstantArgument is
                    BoundGenericNominalTypeArgument constantArgument
                    ? RewriteTypes(constantArgument, rewriteType) : null,
        };

    internal static BoundGenericNominalTypeFormal RewriteTypes(
        BoundGenericNominalTypeFormal formal,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        formal with {
            ConstantDomainType = formal.ConstantDomainType is
                PrimitiveType domainType ? rewriteType(domainType) : null,
            ConstantCarrierType = formal.ConstantCarrierType is
                PrimitiveType carrierType
                    ? rewriteType(carrierType) : null,
            ConstantDomainTypeTerm =
                formal.ConstantDomainTypeTerm is
                    BoundGenericMethodTypeTerm domainTerm
                    ? RewriteGenericMethodTypeTerm(domainTerm, rewriteType)
                    : null,
            Default = formal.Default is BoundGenericNominalTypeDefault defaultValue
                ? RewriteTypes(defaultValue, rewriteType) : null,
        };

    internal static BoundGenericNominalTypeDefinition RewriteTypes(
        BoundGenericNominalTypeDefinition definition,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        definition with {
            Formals = [.. definition.Formals.Select(formal =>
                RewriteTypes(formal, rewriteType))],
            TransparentConstantCarrierType =
                definition.TransparentConstantCarrierType is
                    PrimitiveType carrierType
                    ? rewriteType(carrierType) : null,
        };

    internal static BoundGenericNominalTypeArgument RewriteTypes(
        BoundGenericNominalTypeArgument argument,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        if (argument.Kind == BoundGenericArgumentKind.Type)
        {
            return argument with {
                Type = rewriteType(argument.Type),
                TypeArgumentTerm = argument.TypeArgumentTerm is
                    BoundGenericMethodTypeTerm typeArgumentTerm
                    ? RewriteGenericMethodTypeTerm(typeArgumentTerm,
                        rewriteType) : null,
            };
        }
        if (argument.HasConstantFormalIdentity)
        {
            if (!BoundGenericMethodTypeTerms.TryRewrite(
                argument.ConstantFormalDomain!, rewriteType,
                static ordinal => ordinal,
                static definition => definition,
                static identity => identity, out var domain))
            {
                throw new InvalidOperationException(
                    "A generic nominal constant formal has an invalid domain term.");
            }

            return argument with { ConstantFormalDomain = domain };
        }

        return argument with {
            ConstantDomainType = argument.ConstantDomainType is
                PrimitiveType domainType
                    ? rewriteType(domainType) : null,
            ConstantCarrierType = argument.ConstantCarrierType is
                PrimitiveType carrierType
                    ? rewriteType(carrierType) : null,
        };
    }

    internal static BoundGenericNominalTypeInstance RewriteTypes(
        BoundGenericNominalTypeInstance instance,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        instance with {
            Arguments = [.. instance.Arguments.Select(argument =>
                RewriteTypes(argument, rewriteType))],
        };

    internal static BoundGenericMethodInstantiation RewriteTypes(
        BoundGenericMethodInstantiation instantiation,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        instantiation with {
            TypeArguments = [.. instantiation.TypeArguments.Select(
                rewriteType)],
            ConstantArguments = [.. instantiation.ConstantArguments.Select(
                argument => RewriteGenericMethodConstantArgument(
                    argument, rewriteType))],
        };

    private static BoundGenericMethodConstantArgument
        RewriteGenericMethodConstantArgument(
            BoundGenericMethodConstantArgument argument,
            Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        return argument with {
            Domain = RewriteGenericMethodTypeTerm(argument.Domain,
                rewriteType),
            DomainType = rewriteType(argument.DomainType),
            CarrierType = rewriteType(argument.CarrierType),
        };
    }

    private static BoundGenericMethodTypeTerm RewriteGenericMethodTypeTerm(
        BoundGenericMethodTypeTerm typeTerm,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        if (!TryRewriteGenericMethodTypeTerm(typeTerm, rewriteType,
                static ordinal => ordinal,
                static definition => definition,
                static identity => identity, out var rewritten))
        {
            throw new InvalidOperationException(
                "A generic constant type term is invalid.");
        }

        return rewritten;
    }

    internal static BoundFunction RewriteTypes(BoundFunction function,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        var remapped = RewriteExpressions(function with {
            IndexedBody = null,
        },
            expression => RewriteExpressionTypes(expression, rewriteType));
        return remapped with {
            Body = RewriteGenericUniquePropertyTypes(remapped.Body,
                rewriteType),
            IndexedBody = function.IndexedBody?.RewriteTypes(rewriteType),
            ReturnType = rewriteType(function.ReturnType),
            GenericMethodInstantiation =
                function.GenericMethodInstantiation is
                    BoundGenericMethodInstantiation instantiation
                    ? RewriteTypes(instantiation, rewriteType) : null,
            GenericMethodProofCall =
                function.GenericMethodProofCall is
                    BoundGenericMethodInstantiation proofCall
                    ? RewriteTypes(proofCall, rewriteType) : null,
            ImportedGenericMethodTypeArguments =
                [.. function.ImportedGenericMethodTypeArguments.Select(
                    rewriteType)],
            ExplicitInterfaceOwner =
                function.ExplicitInterfaceOwner is
                    BoundInterfaceType explicitInterfaceOwner
                    ? RewriteTypes(explicitInterfaceOwner, rewriteType)
                    : null,
            InterfaceRequirement =
                function.InterfaceRequirement is
                    BoundInterfaceMemberIdentity interfaceRequirement
                    ? RewriteTypes(interfaceRequirement, rewriteType)
                    : null,
            InterfaceRequirementTypeArguments =
                [.. function.InterfaceRequirementTypeArguments.Select(
                    rewriteType)],
            GenericMethodProofTypeParameters =
                [.. function.GenericMethodProofTypeParameters.Select(
                    rewriteType)],
            FriendTargets = [.. function.FriendTargets.Select(target =>
                RewriteTypes(target, rewriteType))],
            NativeFunctionPointerType = function.NativeFunctionPointerType is PrimitiveType functionPointerType
                ? rewriteType(functionPointerType) : null,
            NativeOutputContracts = [.. function.NativeOutputContracts.Select(output => output with {
                PointeeType = rewriteType(output.PointeeType),
            })],
            DirectNativeImport = function.DirectNativeImport is DirectNativeImport import
                ? import with {
                    Outputs = [.. import.Outputs.Select(output => output with {
                        PointeeType = rewriteType(output.PointeeType),
                    })],
                    Parameters = [.. import.Parameters.Select(parameter => parameter with {
                        PointeeType = rewriteType(parameter.PointeeType),
                    })],
                }
                : null,
            NativeVTableMethod = function.NativeVTableMethod is NativeVTableMethodDefinition vtableMethod
                ? vtableMethod with {
                    Parameters = [.. vtableMethod.Parameters.Select(parameter => parameter with {
                        PointeeType = rewriteType(parameter.PointeeType),
                    })],
                    Outputs = [.. vtableMethod.Outputs.Select(output => output with {
                        PointeeType = rewriteType(output.PointeeType),
                    })],
                }
                : null,
            Variables = [.. function.Variables.Select(variable => variable with {
                Type = rewriteType(variable.Type),
                GenericUniquePropertyWhenTrue =
                    RewriteGenericUniquePropertyFacts(
                        variable.GenericUniquePropertyWhenTrue,
                        rewriteType),
                GenericUniquePropertyWhenFalse =
                    RewriteGenericUniquePropertyFacts(
                        variable.GenericUniquePropertyWhenFalse,
                        rewriteType),
            })],
            DeclarationContractClauses = RewriteDeclarationContractClauses(
                function.DeclarationContractClauses, predicate =>
                    RewriteExpressionTypes(predicate, rewriteType),
                failureType => rewriteType(failureType)),
            CheckedOutputStatePredicates =
                RewriteOutputStatePredicates(
                    function.CheckedOutputStatePredicates,
                    predicate => RewriteExpressionTypes(predicate,
                        rewriteType)),
        };
    }

    private static BoundBlock RewriteGenericUniquePropertyTypes(
        BoundBlock body, Func<PrimitiveType, PrimitiveType> rewriteType) =>
        (BoundBlock)RewriteGenericUniquePropertyTypes(
            (BoundStatement)body, rewriteType);

    private static BoundStatement RewriteGenericUniquePropertyTypes(
        BoundStatement statement,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        statement switch {
            BoundBlock block => block with {
                Statements = [.. block.Statements.Select(child =>
                    RewriteGenericUniquePropertyTypes(child, rewriteType))],
            },
            BoundIf branch => branch with {
                Then = RewriteGenericUniquePropertyTypes(branch.Then,
                    rewriteType),
                Else = branch.Else is null ? null
                    : RewriteGenericUniquePropertyTypes(branch.Else,
                        rewriteType),
                GenericUniquePropertyWhenTrue =
                    RewriteGenericUniquePropertyFacts(
                        branch.GenericUniquePropertyWhenTrue, rewriteType),
                GenericUniquePropertyWhenFalse =
                    RewriteGenericUniquePropertyFacts(
                        branch.GenericUniquePropertyWhenFalse, rewriteType),
            },
            BoundWhile loop => loop with {
                Body = RewriteGenericUniquePropertyTypes(loop.Body,
                    rewriteType),
                Iterator = loop.Iterator is null ? null
                    : RewriteGenericUniquePropertyTypes(loop.Iterator,
                        rewriteType),
            },
            BoundTryFinally cleanup => cleanup with {
                Body = (BoundBlock)RewriteGenericUniquePropertyTypes(
                    cleanup.Body, rewriteType),
                Finally = (BoundBlock)RewriteGenericUniquePropertyTypes(
                    cleanup.Finally, rewriteType),
            },
            BoundTryCatch recovery => recovery with {
                Body = (BoundBlock)RewriteGenericUniquePropertyTypes(
                    recovery.Body, rewriteType),
                Catch = (BoundBlock)RewriteGenericUniquePropertyTypes(
                    recovery.Catch, rewriteType),
                Finally = recovery.Finally is null ? null
                    : (BoundBlock)RewriteGenericUniquePropertyTypes(
                        recovery.Finally, rewriteType),
            },
            BoundUnsafe unsafeBlock => unsafeBlock with {
                Body = (BoundBlock)RewriteGenericUniquePropertyTypes(
                    unsafeBlock.Body, rewriteType),
            },
            BoundViewScope view => view with {
                Body = (BoundBlock)RewriteGenericUniquePropertyTypes(
                    view.Body, rewriteType),
            },
            _ => statement,
        };

    private static ImmutableArray<BoundGenericUniquePropertyFact>
        RewriteGenericUniquePropertyFacts(
            ImmutableArray<BoundGenericUniquePropertyFact> facts,
            Func<PrimitiveType, PrimitiveType> rewriteType) =>
        [.. facts.Select(fact => fact with {
            FormalType = rewriteType(fact.FormalType),
        })];

    internal static ImmutableArray<BoundDeclarationContractClause>
        RewriteDeclarationContractClauses(
            ImmutableArray<BoundDeclarationContractClause> clauses,
            Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        ArgumentNullException.ThrowIfNull(rewriteType);
        return RewriteDeclarationContractClauses(clauses,
            predicate => RewriteExpressionTypes(predicate, rewriteType),
            rewriteType);
    }

    private static ImmutableArray<BoundDeclarationContractClause>
        RewriteDeclarationContractClauses(
            ImmutableArray<BoundDeclarationContractClause> clauses,
            Func<BoundExpression, BoundExpression> rewritePredicate,
            Func<PrimitiveType, PrimitiveType>? rewriteType = null)
    {
        if (clauses.IsDefault)
        {
            return default;
        }
        if (clauses.Any(static clause => clause is null))
        {
            throw new ArgumentException(
                "A declaration contract clause cannot be null.",
                nameof(clauses));
        }

        return [
            .. clauses.Select(clause => clause with {
                Predicate = rewritePredicate(clause.Predicate),
                ExplicitFailureType =
                    clause.ExplicitFailureType is PrimitiveType failureType
                        ? rewriteType?.Invoke(failureType) ?? failureType
                        : null,
            }),
        ];
    }

    private static ImmutableArray<BoundOutputStatePredicate>
        RewriteOutputStatePredicates(
            ImmutableArray<BoundOutputStatePredicate> predicates,
            Func<BoundExpression, BoundExpression> rewritePredicate,
            Func<int, int>? rewriteFormal = null)
    {
        ArgumentNullException.ThrowIfNull(rewritePredicate);
        if (predicates.IsDefault)
        {
            return default;
        }
        if (predicates.Any(static predicate => predicate is null))
        {
            throw new ArgumentException(
                "An OutputState predicate cannot be null.",
                nameof(predicates));
        }

        return [
            .. predicates.Select(predicate => predicate with {
                FormalOrdinal = rewriteFormal?.Invoke(
                    predicate.FormalOrdinal) ?? predicate.FormalOrdinal,
                Predicate = rewritePredicate(predicate.Predicate),
            }),
        ];
    }

    internal static BoundExpression RewriteExpressionTypes(BoundExpression expression,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        var type = expression.Type == PrimitiveType.TypeIdentity
            ? PrimitiveType.TypeIdentity : rewriteType(expression.Type);
        return expression switch {
            BoundTypeIdentityQuery identity => identity with {
                Subject = RewriteQuerySubjectTypes(
                    identity.Subject, rewriteType),
            },
            BoundLayoutQuery query => query with {
                Subject = RewriteQuerySubjectTypes(
                    query.Subject, rewriteType),
            },
            BoundPrivateReferenceForm formation => formation with {
                Place = RewritePrivatePlaceTypes(formation.Place,
                    rewriteType),
                Type = type,
            },
            BoundPrivatePlaceRead read => read with {
                Place = RewritePrivatePlaceTypes(read.Place, rewriteType),
                Type = type,
            },
            BoundPrivatePlaceIsInitialized test => test with {
                Place = RewritePrivatePlaceTypes(test.Place, rewriteType),
            },
            BoundPrivateCheckedStore store => store with {
                Destination = RewritePrivatePlaceTypes(
                    store.Destination, rewriteType),
            },
            BoundNativeStorageAddress address => address with {
                Type = type,
                CarrierType = rewriteType(address.CarrierType),
                ElementType = rewriteType(address.ElementType),
                AddressType = rewriteType(address.AddressType),
                Projections = RewriteArrayProjections(
                    address.Projections, rewriteType),
            },
            BoundNativeFixedArrayInitialization fixedArray => fixedArray with {
                Type = type,
                Layout = RewriteLayout(fixedArray.Layout, rewriteType),
            },
            BoundNativeFixedArrayRepeatInitialization repeat => repeat with {
                Type = type,
                Layout = RewriteLayout(repeat.Layout, rewriteType),
            },
            BoundNativeFixedArrayElement fixedArrayElement => fixedArrayElement with {
                Type = type,
                Layout = RewriteLayout(fixedArrayElement.Layout, rewriteType),
            },
            BoundNativeFixedArrayPathElement pathElement => pathElement with {
                Type = type,
                Projections = RewriteArrayProjections(
                    pathElement.Projections, rewriteType),
            },
            BoundNativeFixedArrayPointerAddress pointerAddress => pointerAddress with {
                Type = type,
                Layout = RewriteLayout(pointerAddress.Layout, rewriteType),
                Projections = RewriteArrayProjections(
                    pointerAddress.Projections, rewriteType),
            },
            BoundNativeFixedArrayWrite fixedArrayWrite => fixedArrayWrite with {
                Type = type,
                Layout = RewriteLayout(fixedArrayWrite.Layout, rewriteType),
            },
            BoundNativeFixedArrayPathWrite pathWrite => pathWrite with {
                Type = type,
                Projections = RewriteArrayProjections(
                    pathWrite.Projections, rewriteType),
                LeafFields = pathWrite.LeafFields.IsDefaultOrEmpty ? [] : [
                    .. pathWrite.LeafFields.Select(field =>
                        field with { OwnerType = rewriteType(field.OwnerType) })],
            },
            BoundConstant constant => constant with {
                Type = type,
                GenericUniquePropertyWhenTrue =
                    RewriteGenericUniquePropertyFacts(
                        constant.GenericUniquePropertyWhenTrue, rewriteType),
                GenericUniquePropertyWhenFalse =
                    RewriteGenericUniquePropertyFacts(
                        constant.GenericUniquePropertyWhenFalse, rewriteType),
            },
            BoundPendingInterfaceDefault pending => pending with {
                Type = type,
                Requirement = RewriteTypes(pending.Requirement,
                    rewriteType),
            },
            BoundGenericTypePropertyTest propertyTest =>
                propertyTest with {
                    FormalType = rewriteType(propertyTest.FormalType),
                },
            BoundTypeTestPattern typeTest => typeTest with {
                TestedType = rewriteType(typeTest.TestedType),
            },
            BoundReferenceTypeTestPattern referenceTypeTest =>
                referenceTypeTest with {
                    TestedType = rewriteType(
                        referenceTypeTest.TestedType),
                },
            BoundTypedValuePattern typedValueTest => typedValueTest with {
                TestedType = rewriteType(typedValueTest.TestedType),
            },
            BoundCall call => call with {
                Type = type,
                InterfaceCallIdentity =
                    call.InterfaceCallIdentity is
                        BoundInterfaceCallIdentity interfaceCall
                        ? RewriteTypes(interfaceCall, rewriteType) : null,
                ForeignInterfaceCall =
                    call.ForeignInterfaceCall is
                        BoundForeignInterfaceCall foreignInterfaceCall
                        ? RewriteTypes(foreignInterfaceCall, rewriteType)
                        : null,
                GenericMethodCall = call.GenericMethodCall is
                    BoundGenericMethodInstantiation instantiation
                    ? RewriteTypes(instantiation, rewriteType) : null,
                ImportedGenericMethodTypeArguments =
                    [.. call.ImportedGenericMethodTypeArguments.Select(
                        rewriteType)],
            },
            _ => expression with { Type = type },
        };
    }

    private static BoundQuerySubject RewriteQuerySubjectTypes(
        BoundQuerySubject subject,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        BoundGenericMethodTypeTerm? genericTypeTerm = null;
        if (subject.TypeIdentityTerm is BoundGenericMethodTypeTerm term)
        {
            if (!TryRewriteGenericMethodTypeTerm(term, rewriteType,
                    static ordinal => ordinal,
                    static definition => definition,
                    static identity => identity,
                    out var rewrittenTerm))
            {
                throw new InvalidOperationException(
                    "A query TypeIdentity term could not be remapped.");
            }
            genericTypeTerm = rewrittenTerm;
        }

        return subject with {
            Type = rewriteType(subject.Type),
            FieldPath = subject.FieldPath is BoundQueryFieldPath fieldPath
                ? fieldPath with {
                    RootType = rewriteType(fieldPath.RootType),
                    Steps = [
                        .. fieldPath.Steps.Select(step => step with {
                            OwnerType = rewriteType(step.OwnerType),
                            FieldType = rewriteType(step.FieldType),
                            StandardFieldOrigin = RewriteStandardFieldOrigin(
                                step.StandardFieldOrigin, rewriteType),
                            StandardClassFieldOrigin =
                                step.StandardClassFieldOrigin is
                                    BoundStandardClassFieldDeclaration
                                    classField
                                    ? classField with {
                                        Type = rewriteType(classField.Type),
                                    } : null,
                        }),
                    ],
                } : null,
            TypeIdentityTerm = genericTypeTerm,
        };
    }

    private static BoundPrivatePlace RewritePrivatePlaceTypes(
        BoundPrivatePlace place,
        Func<PrimitiveType, PrimitiveType> rewriteType)
        => place switch {
            BoundPrivateLocalPlace local => local with {
                Type = rewriteType(local.Type),
            },
            BoundPrivateInlineFieldPlace field => field with {
                OwnerType = rewriteType(field.OwnerType),
                Receiver = RewritePrivatePlaceTypes(field.Receiver,
                    rewriteType),
                Type = rewriteType(field.Type),
            },
            BoundPrivateObjectFieldPlace field => field with {
                OwnerType = rewriteType(field.OwnerType),
                Type = rewriteType(field.Type),
            },
            BoundPrivateReferentPlace referent => referent with {
                Type = rewriteType(referent.Type),
            },
            BoundPrivateFixedArrayElementPlace element => element with {
                ArrayType = rewriteType(element.ArrayType),
                ArrayPlace = RewritePrivatePlaceTypes(element.ArrayPlace,
                    rewriteType),
                Type = rewriteType(element.Type),
            },
            BoundPrivateInvocationBufferElementPlace buffer => buffer with {
                Type = rewriteType(buffer.Type),
            },
            BoundPrivateLexicalViewElementPlace view => view with {
                Type = rewriteType(view.Type),
            },
            _ => throw new InvalidOperationException(
                "Unknown private place kind."),
        };

    private static BoundNativeFixedArrayLayout RewriteLayout(
        BoundNativeFixedArrayLayout layout, Func<PrimitiveType, PrimitiveType> rewriteType) =>
        layout with {
            CarrierType = rewriteType(layout.CarrierType),
            ElementType = rewriteType(layout.ElementType),
        };

    private static ImmutableArray<BoundNativeFixedArrayProjection>
        RewriteArrayProjections(
            ImmutableArray<BoundNativeFixedArrayProjection> projections,
            Func<PrimitiveType, PrimitiveType> rewriteType)
        => projections.IsDefault ? [] : [
            .. projections.Select(projection => projection with {
                Layout = RewriteLayout(projection.Layout, rewriteType),
            })];

    internal static BoundStatement RewriteExpressions(BoundStatement statement,
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
            BoundStaticFieldAssignment assignment => assignment with {
                Value = RewriteExpressions(assignment.Value, rewrite),
            },
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

    internal static BoundExpression RewriteExpressions(BoundExpression expression,
        Func<BoundExpression, BoundExpression> rewrite) =>
        rewrite(expression.RewriteNestedExpressions(item => RewriteExpressions(item, rewrite)));

    internal static BoundStatement RewriteCalls(BoundStatement statement, Func<BoundCall, BoundCall> rewrite) =>
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
            BoundStaticFieldAssignment assignment => assignment with {
                Value = RewriteCalls(assignment.Value, rewrite),
            },
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

    internal static BoundExpression RewriteCalls(BoundExpression expression, Func<BoundCall, BoundCall> rewrite)
    {
        var rewritten = expression.RewriteNestedExpressions(item => RewriteCalls(item, rewrite));
        return rewritten is BoundCall call ? rewrite(call) : rewritten;
    }
}
