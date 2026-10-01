// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private static (ImmutableArray<BoundClass> Classes, ImmutableArray<ModuleFunction> Functions)
        NormalizeLegacyManagedAccess(ImmutableArray<BoundClass> classes,
            ImmutableArray<ModuleFunction> functions, ImmutableArray<BoundStruct> structures)
    {
        var normalizedClasses = classes.Select(@class => @class with {
            IsMutable = true,
            Fields = [.. @class.Fields.Select(field => field with { Type = LegacyType(field.Type) })],
        }).ToImmutableArray();
        var normalizedFunctions = functions.Select(item => {
            if (item.Function is not BoundFunction function)
            {
                return item;
            }
            var converted = function.Intrinsic is >= BoundIntrinsic.NodeTryCreate and <= BoundIntrinsic.NodeCollect
                ? NormalizeLegacyIntrinsic(function)
                : function with {
                    ReturnType = LegacyType(function.ReturnType),
                    Variables = [.. function.Variables.Select(variable => variable with {
                        Type = LegacyType(variable.Type),
                    })],
                    Body = (BoundBlock)LegacyStatement(function.Body),
                };
            return item with { Function = converted, Contract = Contract(converted, structures) };
        }).ToImmutableArray();
        return (normalizedClasses, normalizedFunctions);
    }

    private static PrimitiveType LegacyType(PrimitiveType type) =>
        ManagedClassTypes.IsManaged(type) ? ManagedClassTypes.Writable(type) : type;

    private static BoundFunction NormalizeLegacyIntrinsic(BoundFunction function)
    {
        var expected = NativeManagedNodeIntrinsics.Functions[
            (int)function.Intrinsic - (int)BoundIntrinsic.NodeTryCreate];
        if (function.Name != expected.Name || function.ReturnType != expected.ReturnType
            || function.ParameterCount != expected.ParameterCount
            || function.DeclaringStruct != expected.DeclaringStruct
            || function.Variables.Length != expected.Variables.Length
            || function.IsExternal || function.IsPrivate || !function.IsInternal
            || function.IsConstructor || function.IsClassConstructor || function.ClassConstructor != -1
            || function.IsCleanup || function.IsStatic != expected.IsStatic
            || function.IsNoThrow != expected.IsNoThrow || function.DoesNotReturn
            || function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty)
        {
            throw new ModuleFormatException("Legacy managed intrinsic does not match its declaration.");
        }
        for (var index = 0; index < expected.Variables.Length; index++)
        {
            var actual = function.Variables[index];
            var standard = expected.Variables[index];
            if (actual.Name != standard.Name
                || actual.Type != ManagedClassTypes.Readonly(standard.Type)
                || actual.IsOut != standard.IsOut || actual.IsMutable != standard.IsMutable)
            {
                throw new ModuleFormatException("Legacy managed intrinsic has an invalid parameter.");
            }
        }
        return expected;
    }

    private static BoundStatement LegacyStatement(BoundStatement statement) => statement switch {
        BoundBlock block => block with { Statements = [.. block.Statements.Select(LegacyStatement)] },
        BoundLocal local => local with { Initializer = LegacyExpression(local.Initializer) },
        BoundLifetimeEnd or BoundOwnerDeclaration or BoundVacantLocal or BoundBreak or BoundContinue or BoundDrop or BoundDropConstructedFields
            or SymbolicDrop => statement,
        BoundAssignment assignment => assignment with { Value = LegacyExpression(assignment.Value) },
        BoundOptionalAssignment assignment => assignment with {
            Place = LegacyExpression(assignment.Place),
            Value = LegacyExpression(assignment.Value),
        },
        BoundFieldAssignment field => field with { Value = LegacyExpression(field.Value) },
        BoundExpressionStatement expression => expression with { Expression = LegacyExpression(expression.Expression) },
        BoundIf conditional => conditional with {
            Condition = LegacyExpression(conditional.Condition),
            Then = LegacyStatement(conditional.Then),
            Else = conditional.Else is null ? null : LegacyStatement(conditional.Else),
        },
        BoundWhile loop => loop with {
            Condition = LegacyExpression(loop.Condition),
            Body = LegacyStatement(loop.Body),
            Iterator = loop.Iterator is null ? null : LegacyStatement(loop.Iterator),
        },
        BoundTryFinally cleanup => cleanup with {
            Body = (BoundBlock)LegacyStatement(cleanup.Body),
            Finally = (BoundBlock)LegacyStatement(cleanup.Finally),
        },
        BoundViewScope view => view with {
            Owner = LegacyExpression(view.Owner),
            Start = LegacyExpression(view.Start),
            Length = LegacyExpression(view.Length),
            Body = (BoundBlock)LegacyStatement(view.Body),
        },
        BoundViewSet write => write with {
            Index = LegacyExpression(write.Index),
            Value = LegacyExpression(write.Value),
        },
        BoundUnsafe unsafeBlock => unsafeBlock with { Body = (BoundBlock)LegacyStatement(unsafeBlock.Body) },
        BoundTryCatch recovery => recovery with {
            Body = (BoundBlock)LegacyStatement(recovery.Body),
            Catch = (BoundBlock)LegacyStatement(recovery.Catch),
            Finally = recovery.Finally is null ? null : (BoundBlock)LegacyStatement(recovery.Finally),
        },
        BoundRequire requirement => requirement with { Condition = LegacyExpression(requirement.Condition) },
        BoundReturn result => result with {
            Value = result.Value is null ? null : LegacyExpression(result.Value),
        },
        _ => throw new ModuleFormatException("Unknown legacy statement kind."),
    };

    private static BoundExpression LegacyExpression(BoundExpression expression)
    {
        var type = LegacyType(expression.Type);
        return expression switch {
            BoundConstant => expression,
            BoundNull nullValue => nullValue with { Type = type },
            BoundRead read => read with { Type = type },
            BoundMove move => move with { Type = type },
            BoundReceiverLoan loan => loan with { Type = type },
            BoundMutableReceiverLoan loan => loan with { Type = type },
            BoundOutArgument output => output with { Type = type },
            BoundViewLength => expression,
            BoundViewRead view => view with { Index = LegacyExpression(view.Index) },
            BoundFieldLoan loan => loan with {
                Type = type,
                Place = (BoundFieldRead)LegacyExpression(loan.Place),
            },
            BoundUnary unary => unary with { Type = type, Operand = LegacyExpression(unary.Operand) },
            BoundUpdate update => update with { Type = type, Place = LegacyExpression(update.Place) },
            BoundCompoundAssignment compound => compound with {
                Type = type, Place = LegacyExpression(compound.Place),
                Right = LegacyExpression(compound.Right),
            },
            BoundNominalUpdate update => update with { Type = type, Place = LegacyExpression(update.Place) },
            SymbolicNominalUpdate update => update with { Type = type, Place = LegacyExpression(update.Place) },
            BoundBinary binary => binary with {
                Type = type,
                Left = LegacyExpression(binary.Left),
                Right = LegacyExpression(binary.Right),
            },
            BoundConditional conditional => conditional with {
                Type = type,
                Condition = LegacyExpression(conditional.Condition),
                WhenTrue = LegacyExpression(conditional.WhenTrue),
                WhenFalse = LegacyExpression(conditional.WhenFalse),
            },
            BoundCoalesce coalesce => coalesce with {
                Type = type,
                Optional = LegacyExpression(coalesce.Optional),
                Fallback = LegacyExpression(coalesce.Fallback),
            },
            BoundCast cast => cast with { Type = type, Operand = LegacyExpression(cast.Operand) },
            BoundOptionalLift lift => lift with { Type = type, Value = LegacyExpression(lift.Value) },
            BoundIsNull test => test with { Value = LegacyExpression(test.Value) },
            BoundIsSome pattern => pattern with { Value = LegacyExpression(pattern.Value) },
            BoundFieldRead field => field with {
                Type = type,
                Receiver = LegacyExpression(field.Receiver),
            },
            BoundCall call => call with {
                Type = type,
                Arguments = LegacyArguments(call.Arguments, null),
            },
            SymbolicCall call => call with {
                Type = type,
                Arguments = LegacyArguments(call.Arguments, call.Name),
            },
            _ => throw new ModuleFormatException("Unknown legacy expression kind."),
        };
    }

    private static ImmutableArray<BoundExpression> LegacyArguments(ImmutableArray<BoundExpression> arguments,
        string? target)
    {
        var converted = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Length);
        for (var index = 0; index < arguments.Length; index++)
        {
            var value = LegacyExpression(arguments[index]);
            if (index == 0 && target is "ManagedNode.Value" or "ManagedNode.TryGetNext"
                && ManagedClassTypes.IsWritable(value.Type))
            {
                value = new BoundReadonlyProjection(value, ManagedClassTypes.Readonly(value.Type), value.Span);
            }
            converted.Add(value);
        }
        return converted.MoveToImmutable();
    }
}
