// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

// Reject-by-default admission for the unapproved, internal test-only source ABI.
internal static class NativeSourceGrantExperiment
{
    internal static bool CanClassify(BoundProgram program)
    {
        if (!program.RequiresSourceGrant || program.IsInvocationSourceBound
            || program.InvocationProfileId is not null || program.EntryPoint != -1
            || program.Buffers.Length != 1 || program.Buffers[0] is not HostBuffer {
                Name: "Source", ElementType: PrimitiveType.Char16, IsWritable: false,
                IsInvocationScoped: true, InvocationProfileId: null,
            } || program.Buffers[0].Values.Length > 16_384
            || !program.Structures.IsEmpty || !program.Classes.IsEmpty)
        {
            return false;
        }

        foreach (var function in program.Functions)
        {
            if (function.IsExternal || function.IndexedBody is not null
                || function.Intrinsic != BoundIntrinsic.None || function.DeclaringStruct is not null
                || function.IsConstructor || !Scalar(function.ReturnType)
                || function.Variables.Any(static variable => !Scalar(variable.Type) || variable.IsOut))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool[] Classify(BoundProgram program)
    {
        if (!CanClassify(program))
        {
            throw Unsupported();
        }

        var dependent = new bool[program.Functions.Length];
        var calls = new List<int>[program.Functions.Length];
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            calls[index] = [];
            VisitStatement(function.Body, index);
        }

        bool changed;
        do
        {
            changed = false;
            for (var index = 0; index < dependent.Length; index++)
            {
                foreach (var callee in calls[index])
                {
                    if (!dependent[index] && dependent[callee])
                    {
                        dependent[index] = true;
                        changed = true;
                    }
                }
            }
        } while (changed);

        return dependent;

        void VisitStatement(BoundStatement statement, int function)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var child in block.Statements)
                    {
                        VisitStatement(child, function);
                    }
                    break;
                case BoundAssignment assignment:
                    VisitExpression(assignment.Value, function);
                    break;
                case BoundLocal local:
                    VisitExpression(local.Initializer, function);
                    break;
                case BoundExpressionStatement expression:
                    VisitExpression(expression.Expression, function);
                    break;
                case BoundIf conditional:
                    VisitExpression(conditional.Condition, function);
                    VisitStatement(conditional.Then, function);
                    if (conditional.Else is not null)
                    {
                        VisitStatement(conditional.Else, function);
                    }
                    break;
                case BoundWhile loop when loop.Iterator is null && !loop.IsPostTested:
                    VisitExpression(loop.Condition, function);
                    VisitStatement(loop.Body, function);
                    break;
                case BoundReturn result:
                    if (result.Value is not null)
                    {
                        VisitExpression(result.Value, function);
                    }
                    break;
                case BoundRequire requirement:
                    VisitExpression(requirement.Condition, function);
                    break;
                default:
                    throw Unsupported();
            }
        }

        void VisitExpression(BoundExpression expression, int function)
        {
            if (!Scalar(expression.Type))
            {
                throw Unsupported();
            }
            switch (expression)
            {
                case BoundUnsafeExpression unsafeExpression:
                    VisitExpression(unsafeExpression.Expression, function);
                    break;
                case BoundConstant:
                case BoundRead:
                    break;
                case BoundBufferLength length when length.Buffer == 0:
                    dependent[function] = true;
                    break;
                case BoundBufferRead read when read.Buffer == 0 && !read.IsEndRelativeOffset:
                    dependent[function] = true;
                    VisitExpression(read.Index, function);
                    break;
                case BoundUnary unary:
                    VisitExpression(unary.Operand, function);
                    break;
                case BoundCast cast:
                    VisitExpression(cast.Operand, function);
                    break;
                case BoundBinary binary:
                    VisitExpression(binary.Left, function);
                    VisitExpression(binary.Right, function);
                    break;
                case BoundConditional conditional:
                    VisitExpression(conditional.Condition, function);
                    VisitExpression(conditional.WhenTrue, function);
                    VisitExpression(conditional.WhenFalse, function);
                    break;
                case BoundCall call:
                    calls[function].Add(call.Function);
                    foreach (var argument in call.Arguments)
                    {
                        VisitExpression(argument, function);
                    }
                    break;
                default:
                    throw Unsupported();
            }
        }
    }

    private static bool Scalar(PrimitiveType type) =>
        type is PrimitiveType.Void or PrimitiveType.Bool or PrimitiveType.Char16
            or PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64;

    private static NotSupportedException Unsupported() =>
        new("The provisional source-grant DLL experiment supports only self-contained structured scalar bodies.");
}
