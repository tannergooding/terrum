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
                }
            }
        }

        return true;
    }

    internal static bool VisitExpressions(BoundBlock body, Action<BoundExpression> visitor)
        => VisitExpressionsCore(body, visitor, requireComplete: false);

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
                    or BoundDrop or BoundDropSpan or BoundDropConstructedFields:
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
        function with { Body = (BoundBlock)RewriteCalls(function.Body, rewrite) };

    internal static BoundFunction RewriteExpressions(BoundFunction function,
        Func<BoundExpression, BoundExpression> rewrite) =>
        function with { Body = (BoundBlock)RewriteExpressions(function.Body, rewrite) };

    internal static BoundStruct RewriteTypes(BoundStruct structure,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        var fields = RewriteFields(structure.Fields, rewriteType);
        var origin = structure.StandardTypeOrigin;
        if (origin?.ExpectedDeclaration is BoundStruct expectedDeclaration)
        {
            origin = origin with {
                ExpectedDeclaration = expectedDeclaration with {
                    Fields = RewriteFields(expectedDeclaration.Fields,
                        rewriteType),
                },
            };
        }

        return structure with {
            Fields = fields,
            StandardTypeOrigin = origin,
        };
    }

    private static ImmutableArray<BoundStructField> RewriteFields(
        ImmutableArray<BoundStructField> fields,
        Func<PrimitiveType, PrimitiveType> rewriteType)
        => [.. fields.Select(field => field with {
            Type = rewriteType(field.Type),
        })];

    internal static BoundNativeFixedArrayType RewriteTypes(
        BoundNativeFixedArrayType fixedArray,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        fixedArray with { ElementType = rewriteType(fixedArray.ElementType) };

    internal static CheckedReferenceTypeDefinition RewriteTypes(
        CheckedReferenceTypeDefinition referenceType,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        referenceType with { ReferentType = rewriteType(referenceType.ReferentType) };

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
                ? new BoundConstant(constant.Bits, rewriteType(constant.Type), constant.Span)
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
                ? new BoundConstant(constant.Bits, rewriteType(constant.Type), constant.Span) : null,
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
            })],
            AssociatedDefaultType = member.AssociatedDefaultType is BoundInterfaceType associatedDefault
                ? RewriteTypes(associatedDefault, rewriteType) : null,
            AssociatedConstantType = member.AssociatedConstantType is BoundInterfaceType associatedConstantType
                ? RewriteTypes(associatedConstantType, rewriteType) : null,
            AssociatedConstantDefault = member.AssociatedConstantDefault is BoundConstant associatedConstant
                ? new BoundConstant(associatedConstant.Bits, rewriteType(associatedConstant.Type),
                    associatedConstant.Span) : null,
        };

    internal static BoundInterfaceDefinition RewriteTypes(
        BoundInterfaceDefinition @interface,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        @interface with {
            Formals = [.. @interface.Formals.Select(formal => RewriteTypes(formal, rewriteType))],
            Bases = [.. @interface.Bases.Select(baseType => RewriteTypes(baseType, rewriteType))],
            Constraints = [.. @interface.Constraints.Select(constraint =>
                RewriteTypes(constraint, rewriteType))],
            Members = [.. @interface.Members.Select(member => RewriteTypes(member, rewriteType))],
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
        defaultValue.DecodedType is PrimitiveType decodedType
            ? defaultValue with { DecodedType = rewriteType(decodedType) }
            : defaultValue;

    internal static BoundGenericNominalTypeFormal RewriteTypes(
        BoundGenericNominalTypeFormal formal,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        formal with {
            Default = formal.Default is BoundGenericNominalTypeDefault defaultValue
                ? RewriteTypes(defaultValue, rewriteType) : null,
        };

    internal static BoundGenericNominalTypeDefinition RewriteTypes(
        BoundGenericNominalTypeDefinition definition,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        definition with {
            Formals = [.. definition.Formals.Select(formal =>
                RewriteTypes(formal, rewriteType))],
        };

    internal static BoundGenericNominalTypeArgument RewriteTypes(
        BoundGenericNominalTypeArgument argument,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        argument.Kind == BoundGenericArgumentKind.Type
            ? argument with { Type = rewriteType(argument.Type) }
            : argument;

    internal static BoundGenericNominalTypeInstance RewriteTypes(
        BoundGenericNominalTypeInstance instance,
        Func<PrimitiveType, PrimitiveType> rewriteType) =>
        instance with {
            Arguments = [.. instance.Arguments.Select(argument =>
                RewriteTypes(argument, rewriteType))],
        };

    internal static BoundFunction RewriteTypes(BoundFunction function,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        var remapped = RewriteExpressions(function,
            expression => RewriteExpressionTypes(expression, rewriteType));
        return remapped with {
            ReturnType = rewriteType(function.ReturnType),
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
            })],
        };
    }

    private static BoundExpression RewriteExpressionTypes(BoundExpression expression,
        Func<PrimitiveType, PrimitiveType> rewriteType)
    {
        var type = rewriteType(expression.Type);
        return expression switch {
            BoundNativeStorageAddress address => address with {
                Type = type,
                CarrierType = rewriteType(address.CarrierType),
                ElementType = rewriteType(address.ElementType),
                AddressType = rewriteType(address.AddressType),
            },
            BoundNativeFixedArrayInitialization fixedArray => fixedArray with {
                Type = type,
                Layout = RewriteLayout(fixedArray.Layout, rewriteType),
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
            },
            _ => expression with { Type = type },
        };
    }

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
