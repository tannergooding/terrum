// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Validates untrusted structured IR before a native backend consumes it.</summary>
public static class SemanticVerifier
{
    internal const int MaximumFunctions = 1024;
    internal const int MaximumVariables = 256;
    internal const int MaximumArguments = 64;
    internal const int MaximumDepth = 128;
    internal const int MaximumNodes = 100_000;

    /// <summary>Checks types, control flow, slot mutation, definite assignment, and boundedness of a program.</summary>
    /// <exception cref="ArgumentException">The program is malformed.</exception>
    /// <exception cref="NotSupportedException">The program exceeds an emitter resource limit.</exception>
    public static void Verify(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Functions.IsDefaultOrEmpty || program.EntryPoint < 0 || program.EntryPoint >= program.Functions.Length)
        {
            throw Invalid();
        }
        if (program.Functions.Length > MaximumFunctions)
        {
            throw Limit();
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < program.Functions.Length; i++)
        {
            var function = program.Functions[i];
            if (function is null || string.IsNullOrEmpty(function.Name) || !names.Add(function.Name)
                || !ValidType(function.ReturnType, allowVoid: true) || function.Variables.IsDefault
                || function.ParameterCount < 0 || function.ParameterCount > function.Variables.Length
                || function.Body is null)
            {
                throw Invalid();
            }
            if (function.Variables.Length > MaximumVariables || function.ParameterCount > MaximumArguments)
            {
                throw Limit();
            }
            foreach (var variable in function.Variables)
            {
                if (variable is null || string.IsNullOrEmpty(variable.Name) || !ValidType(variable.Type, allowVoid: false))
                {
                    throw Invalid();
                }
            }
        }

        var entry = program.Functions[program.EntryPoint];
        if (entry.Name != "Main" || entry.ParameterCount != 0
            || entry.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32))
        {
            throw Invalid();
        }

        var remaining = MaximumNodes;
        foreach (var function in program.Functions)
        {
            var initialized = new bool[function.Variables.Length];
            Array.Fill(initialized, true, 0, function.ParameterCount);
            var declarations = new HashSet<int>();
            var returns = Statement(function.Body, initialized, declarations, function, program, ref remaining, 0);
            if (function.ReturnType != PrimitiveType.Void && !returns)
            {
                throw Invalid();
            }
            for (var i = function.ParameterCount; i < function.Variables.Length; i++)
            {
                if (!declarations.Contains(i))
                {
                    throw Invalid();
                }
            }
        }
    }

    private static bool Statement(BoundStatement statement, bool[] initialized, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        switch (statement)
        {
            case BoundBlock block:
                if (block.Statements.IsDefault)
                {
                    throw Invalid();
                }
                var returns = false;
                foreach (var child in block.Statements)
                {
                    if (child is null)
                    {
                        throw Invalid();
                    }
                    var childReturns = Statement(child, initialized, declarations, function, program, ref remaining, depth + 1);
                    returns |= childReturns;
                }
                return returns;
            case BoundLocal local:
                if (local.Variable < function.ParameterCount
                    || local.Variable >= initialized.Length
                    || !declarations.Add(local.Variable) || initialized[local.Variable])
                {
                    throw Invalid();
                }
                Expression(local.Initializer, initialized, function, program, ref remaining, depth + 1);
                if (local.Initializer.Type != function.Variables[local.Variable].Type)
                {
                    throw Invalid();
                }
                initialized[local.Variable] = true;
                return false;
            case BoundAssignment assignment:
                if (assignment.Variable < function.ParameterCount
                    || assignment.Variable >= initialized.Length || !initialized[assignment.Variable]
                    || !function.Variables[assignment.Variable].IsMutable)
                {
                    throw Invalid();
                }
                Expression(assignment.Value, initialized, function, program, ref remaining, depth + 1);
                if (assignment.Value.Type != function.Variables[assignment.Variable].Type)
                {
                    throw Invalid();
                }
                return false;
            case BoundExpressionStatement expressionStatement:
                if (expressionStatement.Expression is not BoundCall)
                {
                    throw Invalid();
                }
                Expression(expressionStatement.Expression, initialized, function, program, ref remaining, depth + 1);
                return false;
            case BoundIf conditional:
                Expression(conditional.Condition, initialized, function, program, ref remaining, depth + 1);
                if (conditional.Condition.Type != PrimitiveType.Bool || conditional.Then is null)
                {
                    throw Invalid();
                }
                var thenState = (bool[])initialized.Clone();
                var thenReturns = Statement(conditional.Then, thenState, declarations, function, program, ref remaining, depth + 1);
                var elseState = (bool[])initialized.Clone();
                var elseReturns = conditional.Else is not null
                    && Statement(conditional.Else, elseState, declarations, function, program, ref remaining, depth + 1);
                for (var i = 0; i < initialized.Length; i++)
                {
                    initialized[i] = thenReturns ? elseState[i] : elseReturns ? thenState[i] : thenState[i] && elseState[i];
                }
                return thenReturns && elseReturns;
            case BoundWhile loop:
                Expression(loop.Condition, initialized, function, program, ref remaining, depth + 1);
                if (loop.Condition.Type != PrimitiveType.Bool || loop.Body is null)
                {
                    throw Invalid();
                }
                var bodyState = (bool[])initialized.Clone();
                _ = Statement(loop.Body, bodyState, declarations, function, program, ref remaining, depth + 1);
                return false;
            case BoundReturn result:
                if (result.Value is null)
                {
                    if (function.ReturnType != PrimitiveType.Void)
                    {
                        throw Invalid();
                    }
                }
                else
                {
                    Expression(result.Value, initialized, function, program, ref remaining, depth + 1);
                    if (result.Value.Type != function.ReturnType)
                    {
                        throw Invalid();
                    }
                }
                return true;
            default:
                throw Invalid();
        }
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Unsupported token kinds must remain invalid rather than becoming accepted operators.")]
    private static void Expression(BoundExpression expression, bool[] initialized, BoundFunction function,
        BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is null || !ValidType(expression.Type, allowVoid: expression is BoundCall))
        {
            throw Invalid();
        }
        switch (expression)
        {
            case BoundConstant constant:
                if (constant.Type == PrimitiveType.Void || (constant.Type == PrimitiveType.Bool && constant.Value is not (0 or 1)))
                {
                    throw Invalid();
                }
                break;
            case BoundRead read:
                if (read.Variable < 0 || read.Variable >= initialized.Length || !initialized[read.Variable]
                    || read.Type != function.Variables[read.Variable].Type)
                {
                    throw Invalid();
                }
                break;
            case BoundUnary unary:
                Expression(unary.Operand, initialized, function, program, ref remaining, depth + 1);
                var unaryValid = unary.Operator switch {
                    TokenKind.Plus or TokenKind.Minus or TokenKind.Tilde =>
                        unary.Operand.Type == PrimitiveType.Int32 && unary.Type == PrimitiveType.Int32,
                    TokenKind.Bang => unary.Operand.Type == PrimitiveType.Bool && unary.Type == PrimitiveType.Bool,
                    _ => false,
                };
                if (!unaryValid)
                {
                    throw Invalid();
                }
                break;
            case BoundBinary binary:
                Expression(binary.Left, initialized, function, program, ref remaining, depth + 1);
                Expression(binary.Right, initialized, function, program, ref remaining, depth + 1);
                var operands = binary.Left.Type == binary.Right.Type && binary.Left.Type != PrimitiveType.Void;
                var binaryValid = binary.Operator switch {
                    TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent
                        or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals =>
                        operands && binary.Left.Type == PrimitiveType.Int32
                            && binary.Type == (binary.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                                or TokenKind.Slash or TokenKind.Percent ? PrimitiveType.Int32 : PrimitiveType.Bool),
                    TokenKind.EqualsEquals or TokenKind.BangEquals =>
                        operands && binary.Type == PrimitiveType.Bool,
                    TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret =>
                        operands && binary.Type == binary.Left.Type,
                    TokenKind.AmpersandAmpersand or TokenKind.PipePipe =>
                        operands && binary.Left.Type == PrimitiveType.Bool && binary.Type == PrimitiveType.Bool,
                    _ => false,
                };
                if (!binaryValid)
                {
                    throw Invalid();
                }
                break;
            case BoundCall call:
                if (call.Function < 0 || call.Function >= program.Functions.Length || call.Arguments.IsDefault)
                {
                    throw Invalid();
                }
                if (call.Arguments.Length > MaximumArguments)
                {
                    throw Limit();
                }
                var target = program.Functions[call.Function];
                if (call.Arguments.Length != target.ParameterCount || call.Type != target.ReturnType)
                {
                    throw Invalid();
                }
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    Expression(call.Arguments[i], initialized, function, program, ref remaining, depth + 1);
                    if (call.Arguments[i].Type != target.Variables[i].Type)
                    {
                        throw Invalid();
                    }
                }
                break;
            default:
                throw Invalid();
        }
    }

    private static bool ValidType(PrimitiveType type, bool allowVoid) =>
        type is PrimitiveType.Int32 or PrimitiveType.Bool || (allowVoid && type == PrimitiveType.Void);

    private static void Count(ref int remaining, int depth)
    {
        if (depth > MaximumDepth || --remaining < 0)
        {
            throw Limit();
        }
    }

    private static ArgumentException Invalid() => new("Invalid bound program.", "program");
    private static NotSupportedException Limit() => new("Bound program exceeds native resource limits.");
}
