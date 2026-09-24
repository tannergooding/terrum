// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Primitive types supported by the first executable profile.</summary>
public enum PrimitiveType
{
    /// <summary>No result.</summary>
    Void,
    /// <summary>A boolean value.</summary>
    Bool,
    /// <summary>A signed, checked 32-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names the Terrum primitive type represented by the compiler.")]
    Int32,
}

/// <summary>A source-mapped, structured program with resolved function and local indexes.</summary>
/// <param name="Functions">Functions in declaration order.</param>
/// <param name="EntryPoint">The index of the parameterless Main function.</param>
public sealed record BoundProgram(ImmutableArray<BoundFunction> Functions, int EntryPoint);

/// <summary>A function whose parameter slots precede its local slots.</summary>
/// <param name="Name">The declared name.</param>
/// <param name="ReturnType">The declared result type.</param>
/// <param name="Variables">Parameter and local slots.</param>
/// <param name="ParameterCount">The number of parameter slots.</param>
/// <param name="Body">The structured body.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundFunction(string Name, PrimitiveType ReturnType, ImmutableArray<BoundVariable> Variables,
    int ParameterCount, BoundBlock Body, TextSpan Span);

/// <summary>A parameter or initialized local declaration.</summary>
/// <param name="Name">The source name.</param>
/// <param name="Type">The stored type.</param>
/// <param name="IsMutable">Whether assignment is permitted.</param>
/// <param name="Span">The declaration range.</param>
public sealed record BoundVariable(string Name, PrimitiveType Type, bool IsMutable, TextSpan Span);

/// <summary>A structured statement, retaining its source range.</summary>
/// <param name="Span">The source range.</param>
public abstract record BoundStatement(TextSpan Span);

/// <summary>A lexical block evaluated in source order.</summary>
/// <param name="Statements">The statements.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundBlock(ImmutableArray<BoundStatement> Statements, TextSpan Span) : BoundStatement(Span);

/// <summary>Initializes a previously vacant local slot.</summary>
/// <param name="Variable">The local slot.</param>
/// <param name="Initializer">The initializer.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundLocal(int Variable, BoundExpression Initializer, TextSpan Span) : BoundStatement(Span);

/// <summary>Assigns an initialized mutable local.</summary>
/// <param name="Variable">The local slot.</param>
/// <param name="Value">The assigned value.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundAssignment(int Variable, BoundExpression Value, TextSpan Span) : BoundStatement(Span);

/// <summary>Evaluates a call for its effects, discarding its result.</summary>
/// <param name="Expression">The call.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundExpressionStatement(BoundExpression Expression, TextSpan Span) : BoundStatement(Span);

/// <summary>A conditional with explicit structured branches.</summary>
/// <param name="Condition">A boolean condition.</param>
/// <param name="Then">The true branch.</param>
/// <param name="Else">The false branch, if present.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundIf(BoundExpression Condition, BoundStatement Then, BoundStatement? Else, TextSpan Span) : BoundStatement(Span);

/// <summary>A loop whose condition is evaluated before each iteration.</summary>
/// <param name="Condition">A boolean condition.</param>
/// <param name="Body">The loop body.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundWhile(BoundExpression Condition, BoundStatement Body, TextSpan Span) : BoundStatement(Span);

/// <summary>A function return.</summary>
/// <param name="Value">The result, or null for a void return.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundReturn(BoundExpression? Value, TextSpan Span) : BoundStatement(Span);

/// <summary>A typed expression, evaluated left-to-right except for short-circuit operators.</summary>
/// <param name="Type">The result type.</param>
/// <param name="Span">The source range.</param>
public abstract record BoundExpression(PrimitiveType Type, TextSpan Span);

/// <summary>An integer or canonical boolean constant.</summary>
/// <param name="Value">The value; booleans are zero or one.</param>
/// <param name="Type">The constant type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundConstant(int Value, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A read of an initialized slot.</summary>
/// <param name="Variable">The slot index.</param>
/// <param name="Type">The slot type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundRead(int Variable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A checked unary operation.</summary>
/// <param name="Operator">The operator token.</param>
/// <param name="Operand">The input.</param>
/// <param name="Type">The result type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundUnary(TokenKind Operator, BoundExpression Operand, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A checked binary or short-circuit boolean operation.</summary>
/// <param name="Operator">The operator token.</param>
/// <param name="Left">The first input.</param>
/// <param name="Right">The second input.</param>
/// <param name="Type">The result type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundBinary(TokenKind Operator, BoundExpression Left, BoundExpression Right,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A statically resolved direct call.</summary>
/// <param name="Function">The callee index.</param>
/// <param name="Arguments">Arguments evaluated once in source order.</param>
/// <param name="Type">The result type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundCall(int Function, ImmutableArray<BoundExpression> Arguments,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);
