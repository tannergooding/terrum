// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal static class NativeSourceGrantClassifier
{
    internal static bool[] Classify(BoundProgram program)
    {
        if (!program.RequiresSourceGrant || program.IsInvocationSourceBound
            || program.InvocationProfileId is not null || program.EntryPoint != -1
            || program.Buffers.Length != 1 || program.Buffers[0] is not HostBuffer {
                Name: "Source", ElementType: PrimitiveType.Char16, IsWritable: false,
                IsInvocationScoped: true, InvocationProfileId: null, Values.IsEmpty: true,
            } || !program.Classes.IsEmpty || NativeEmitter.UsesManagedNodes(program))
        {
            throw Unsupported();
        }

        var functionIndices = new Dictionary<string, int>(program.Functions.Length, StringComparer.Ordinal);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            functionIndices.Add(program.Functions[index].Name, index);
        }

        var dependent = new bool[program.Functions.Length];
        var calls = new List<int>[program.Functions.Length];
        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            calls[functionIndex] = [];
            var statements = new Stack<BoundStatement>();
            var expressions = new Stack<BoundExpression>();
            statements.Push(function.Body);

            while (statements.Count != 0 || expressions.Count != 0)
            {
                while (expressions.Count != 0)
                {
                    var expression = expressions.Pop();
                    switch (expression)
                    {
                        case BoundUnsafeExpression unsafeExpression:
                            expressions.Push(unsafeExpression.Expression);
                            break;
                        case BoundCall call:
                            AddCall(call.Function);
                            foreach (var argument in call.Arguments)
                            {
                                if (argument is BoundOutArgument output)
                                {
                                    AddCleanupCall(output.Type);
                                }
                            }
                            PushExpressions(call.Arguments);
                            break;
                        case BoundNominalUpdate update:
                            AddCall(update.Function);
                            expressions.Push(update.Place);
                            break;
                        case BoundBufferLength length when length.Buffer == 0:
                            dependent[functionIndex] = true;
                            break;
                        case BoundBufferRead read when read.Buffer == 0:
                            dependent[functionIndex] = true;
                            expressions.Push(read.Index);
                            break;
                        case BoundSnapshotSpan snapshot when snapshot.SourceBuffer == 0:
                            dependent[functionIndex] = true;
                            break;
                        case BoundOptionalLift lift:
                            expressions.Push(lift.Value);
                            break;
                        case BoundCoalesce coalesce:
                            expressions.Push(coalesce.Optional);
                            expressions.Push(coalesce.Fallback);
                            break;
                        case BoundReadonlyProjection projection:
                            expressions.Push(projection.Value);
                            break;
                        case BoundIsNull test:
                            expressions.Push(test.Value);
                            break;
                        case BoundIsSome test:
                            expressions.Push(test.Value);
                            break;
                        case BoundIsVariablePattern test:
                            expressions.Push(test.Value);
                            break;
                        case BoundSpanLength length:
                            expressions.Push(length.View);
                            break;
                        case BoundSpanRead read:
                            expressions.Push(read.View);
                            expressions.Push(read.Index);
                            break;
                        case BoundSpanAdvance advance:
                            expressions.Push(advance.View);
                            break;
                        case BoundFieldLoan loan:
                            expressions.Push(loan.Place);
                            break;
                        case BoundMutableFieldLoan loan:
                            expressions.Push(loan.Place);
                            break;
                        case BoundUnary unary:
                            expressions.Push(unary.Operand);
                            break;
                        case BoundUpdate update:
                            expressions.Push(update.Place);
                            break;
                        case BoundCompoundAssignment assignment:
                            expressions.Push(assignment.Place);
                            expressions.Push(assignment.Right);
                            break;
                        case BoundBinary binary:
                            expressions.Push(binary.Left);
                            expressions.Push(binary.Right);
                            break;
                        case BoundConditional conditional:
                            expressions.Push(conditional.Condition);
                            expressions.Push(conditional.WhenTrue);
                            expressions.Push(conditional.WhenFalse);
                            break;
                        case BoundCast cast:
                            expressions.Push(cast.Operand);
                            break;
                        case BoundFieldRead field:
                            expressions.Push(field.Receiver);
                            break;
                        case BoundViewRead view:
                            expressions.Push(view.Index);
                            break;
                        case BoundBufferRead read:
                            expressions.Push(read.Index);
                            break;
                        case BoundConstant or BoundNull or BoundRead or BoundMove or BoundReceiverLoan
                            or BoundMutableReceiverLoan or BoundViewLength or BoundOutArgument
                            or BoundIsInitialized:
                            break;
                        default:
                            throw Unsupported();
                    }
                }

                if (statements.Count == 0)
                {
                    continue;
                }

                switch (statements.Pop())
                {
                    case BoundBlock block:
                        PushStatements(block.Statements);
                        break;
                    case BoundLocal local:
                        expressions.Push(local.Initializer);
                        break;
                    case BoundAssignment assignment:
                        expressions.Push(assignment.Value);
                        AddCleanupCall(assignment.Value.Type);
                        break;
                    case BoundOptionalAssignment assignment:
                        expressions.Push(assignment.Place);
                        expressions.Push(assignment.Value);
                        break;
                    case BoundFieldAssignment assignment:
                        expressions.Push(assignment.Value);
                        break;
                    case BoundBufferAssignment assignment:
                        expressions.Push(assignment.Index);
                        expressions.Push(assignment.Value);
                        if (assignment.Buffer == 0)
                        {
                            dependent[functionIndex] = true;
                        }
                        break;
                    case BoundExpressionStatement statement:
                        expressions.Push(statement.Expression);
                        break;
                    case BoundIf conditional:
                        expressions.Push(conditional.Condition);
                        statements.Push(conditional.Then);
                        if (conditional.Else is not null)
                        {
                            statements.Push(conditional.Else);
                        }
                        break;
                    case BoundWhile loop:
                        expressions.Push(loop.Condition);
                        statements.Push(loop.Body);
                        if (loop.Iterator is not null)
                        {
                            statements.Push(loop.Iterator);
                        }
                        break;
                    case BoundTryFinally cleanup:
                        statements.Push(cleanup.Body);
                        statements.Push(cleanup.Finally);
                        break;
                    case BoundViewScope view:
                        expressions.Push(view.Owner);
                        expressions.Push(view.Start);
                        expressions.Push(view.RangeEnd ?? view.Length);
                        statements.Push(view.Body);
                        break;
                    case BoundViewSet write:
                        expressions.Push(write.Index);
                        expressions.Push(write.Value);
                        break;
                    case BoundLifetimeEnd end:
                        AddCallIfPresent(end.CleanupFunction);
                        break;
                    case BoundDrop drop:
                        AddCallIfPresent(drop.CleanupFunction);
                        break;
                    case BoundDropConstructedFields partial:
                        AddConstructedFieldCleanupCalls(partial);
                        break;
                    case BoundUnsafe unsafeBlock:
                        statements.Push(unsafeBlock.Body);
                        break;
                    case BoundTryCatch recovery:
                        statements.Push(recovery.Body);
                        statements.Push(recovery.Catch);
                        if (recovery.Finally is not null)
                        {
                            statements.Push(recovery.Finally);
                        }
                        break;
                    case BoundRequire requirement:
                        expressions.Push(requirement.Condition);
                        break;
                    case BoundReturn { Value: BoundExpression value }:
                        expressions.Push(value);
                        break;
                    case BoundVacantLocal or BoundOwnerDeclaration or BoundDropSpan or BoundReturn
                        or BoundBreak or BoundContinue:
                        break;
                    default:
                        throw Unsupported();
                }
            }

            void AddCall(int callee)
            {
                if ((uint)callee >= (uint)program.Functions.Length)
                {
                    throw Unsupported();
                }
                calls[functionIndex].Add(callee);
            }

            void AddCallIfPresent(int callee)
            {
                if (callee is >= 0)
                {
                    AddCall(callee);
                }
            }

            void AddCleanupCall(PrimitiveType type)
            {
                if ((int)type >= 100 && (int)type < ManagedClassTypes.First
                    && program.Structures[(int)type - 100].CleanupMethod is string cleanup
                    && functionIndices.TryGetValue(cleanup, out var cleanupFunction))
                {
                    AddCall(cleanupFunction);
                }
            }

            void AddConstructedFieldCleanupCalls(BoundDropConstructedFields partial)
            {
                var type = function.ReturnType;
                if ((int)type is < 100 or >= ManagedClassTypes.First)
                {
                    return;
                }
                var structure = program.Structures[(int)type - 100];
                foreach (var fieldIndex in partial.AcquisitionOrder)
                {
                    AddCleanupCall(structure.Fields[fieldIndex].Type);
                }
            }

            void PushExpressions(ImmutableArray<BoundExpression> items)
            {
                foreach (var item in items)
                {
                    expressions.Push(item);
                }
            }

            void PushStatements(ImmutableArray<BoundStatement> items)
            {
                foreach (var item in items)
                {
                    statements.Push(item);
                }
            }
        }

        bool changed;
        do
        {
            changed = false;
            for (var function = 0; function < dependent.Length; function++)
            {
                if (dependent[function])
                {
                    continue;
                }
                foreach (var callee in calls[function])
                {
                    if (dependent[callee])
                    {
                        dependent[function] = true;
                        changed = true;
                        break;
                    }
                }
            }
        } while (changed);

        var reachable = (bool[])dependent.Clone();
        var pending = new Stack<int>();
        for (var functionIndex = 0; functionIndex < reachable.Length; functionIndex++)
        {
            if (reachable[functionIndex])
            {
                pending.Push(functionIndex);
            }
        }
        while (pending.Count != 0)
        {
            foreach (var callee in calls[pending.Pop()])
            {
                if (!reachable[callee])
                {
                    reachable[callee] = true;
                    pending.Push(callee);
                }
            }
        }
        for (var functionIndex = 0; functionIndex < reachable.Length; functionIndex++)
        {
            if (reachable[functionIndex])
            {
                ValidateDependentFunction(program.Functions[functionIndex]);
            }
        }

        return dependent;

        static void ValidateDependentFunction(BoundFunction function)
        {
            if (function.IsExternal || function.IndexedBody is not null
                || function.Intrinsic != BoundIntrinsic.None || function.DeclaringStruct is not null
                || function.IsConstructor || !IsScalar(function.ReturnType))
            {
                throw Unsupported();
            }
            foreach (var variable in function.Variables)
            {
                if (!IsScalar(variable.Type) || variable.IsOut)
                {
                    throw Unsupported();
                }
            }

            var statements = new Stack<BoundStatement>();
            var expressions = new Stack<BoundExpression>();
            statements.Push(function.Body);
            while (statements.Count != 0 || expressions.Count != 0)
            {
                while (expressions.Count != 0)
                {
                    var expression = expressions.Pop();
                    if (!IsScalar(expression.Type))
                    {
                        throw Unsupported();
                    }
                    switch (expression)
                    {
                        case BoundUnsafeExpression unsafeExpression:
                            expressions.Push(unsafeExpression.Expression);
                            break;
                        case BoundConstant or BoundRead:
                            break;
                        case BoundBufferLength length when length.Buffer == 0:
                            break;
                        case BoundBufferRead read when read.Buffer == 0:
                            expressions.Push(read.Index);
                            break;
                        case BoundUnary unary:
                            expressions.Push(unary.Operand);
                            break;
                        case BoundCast cast:
                            expressions.Push(cast.Operand);
                            break;
                        case BoundBinary binary:
                            expressions.Push(binary.Left);
                            expressions.Push(binary.Right);
                            break;
                        case BoundConditional conditional:
                            expressions.Push(conditional.Condition);
                            expressions.Push(conditional.WhenTrue);
                            expressions.Push(conditional.WhenFalse);
                            break;
                        case BoundCall call:
                            PushExpressions(call.Arguments);
                            break;
                        default:
                            throw Unsupported();
                    }
                }

                if (statements.Count == 0)
                {
                    continue;
                }
                switch (statements.Pop())
                {
                    case BoundBlock block:
                        PushStatements(block.Statements);
                        break;
                    case BoundAssignment assignment:
                        expressions.Push(assignment.Value);
                        break;
                    case BoundLocal local:
                        expressions.Push(local.Initializer);
                        break;
                    case BoundExpressionStatement statement:
                        expressions.Push(statement.Expression);
                        break;
                    case BoundIf conditional:
                        expressions.Push(conditional.Condition);
                        statements.Push(conditional.Then);
                        if (conditional.Else is not null)
                        {
                            statements.Push(conditional.Else);
                        }
                        break;
                    case BoundWhile loop when loop.Iterator is null && !loop.IsPostTested:
                        expressions.Push(loop.Condition);
                        statements.Push(loop.Body);
                        break;
                    case BoundReturn { Value: BoundExpression value }:
                        expressions.Push(value);
                        break;
                    case BoundRequire requirement:
                        expressions.Push(requirement.Condition);
                        break;
                    case BoundReturn or BoundBreak or BoundContinue:
                        break;
                    default:
                        throw Unsupported();
                }
            }

            void PushExpressions(ImmutableArray<BoundExpression> items)
            {
                foreach (var item in items)
                {
                    expressions.Push(item);
                }
            }

            void PushStatements(ImmutableArray<BoundStatement> items)
            {
                foreach (var item in items)
                {
                    statements.Push(item);
                }
            }
        }
    }

    internal static bool IsScalar(PrimitiveType type) =>
        type is PrimitiveType.Void or PrimitiveType.Bool or PrimitiveType.Char16
            or PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64;

    private static NotSupportedException Unsupported() =>
        new("The provisional source-grant DLL supports only self-contained structured scalar source-dependent functions.");
}
