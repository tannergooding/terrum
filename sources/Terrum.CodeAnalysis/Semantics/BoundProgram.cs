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
    /// <summary>An unsigned, checked 32-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names the Terrum primitive type represented by the compiler.")]
    UInt32,
    /// <summary>A UTF-16 code unit, including surrogate code units.</summary>
    Char16,
    /// <summary>A signed, checked 64-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names the Terrum primitive type represented by the compiler.")]
    Int64,
    /// <summary>An unsigned, checked 64-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names the Terrum primitive type represented by the compiler.")]
    UInt64,
    /// <summary>An opaque, uniquely owned native allocation of initialized int32 elements.</summary>
    OwnedInt32Buffer,
    /// <summary>A lexical, nonescaping readonly range of an owned int32 buffer.</summary>
    ReadonlyInt32BufferView,
    /// <summary>A lexical, nonescaping exclusive writable range of an owned int32 buffer.</summary>
    MutableInt32BufferView,
    /// <summary>An aliasable handle to a compiler-managed node in the bounded tracing profile.</summary>
    ManagedNode,
}

/// <summary>Compiler-defined native storage and managed graph operations; user functions cannot declare these implementations.</summary>
public enum BoundIntrinsic
{
    /// <summary>An ordinary source definition or imported declaration.</summary>
    None,
    /// <summary>Allocates bounded storage and publishes an empty owner only on success.</summary>
    BufferTryCreate,
    /// <summary>Reads the number of initialized elements through a readonly receiver loan.</summary>
    BufferCount,
    /// <summary>Reads the fixed capacity through a readonly receiver loan.</summary>
    BufferCapacity,
    /// <summary>Reads a checked index within the initialized prefix.</summary>
    BufferRead,
    /// <summary>Replaces an initialized element through an exclusive mutable receiver loan.</summary>
    BufferSet,
    /// <summary>Appends through an exclusive mutable receiver loan, returning false without mutation when full.</summary>
    BufferTryAppend,
    /// <summary>Resets the initialized prefix of a live owner without freeing its allocation.</summary>
    BufferClear,
    /// <summary>Creates a managed node and publishes its handle only on success.</summary>
    NodeTryCreate,
    /// <summary>Reads the scalar payload of a managed node.</summary>
    NodeValue,
    /// <summary>Replaces the scalar payload of a managed node.</summary>
    NodeSetValue,
    /// <summary>Connects one managed node to another.</summary>
    NodeSetNext,
    /// <summary>Clears the outgoing edge of a managed node.</summary>
    NodeClearNext,
    /// <summary>Copies an outgoing managed edge only when present.</summary>
    NodeTryGetNext,
    /// <summary>Traces exact roots and reclaims unreachable nodes, returning their count.</summary>
    NodeCollect,
}

/// <summary>The verified behavior of compiler-generated checked failures.</summary>
public enum BoundRuntimePolicy
{
    /// <summary>Checked failures terminate the process without exceptional cleanup.</summary>
    FailFast,
    /// <summary>Only the four compiler-generated checked failures participate in native unwinding.</summary>
    CheckedExceptions,
}

/// <summary>A source-mapped, structured program with resolved function and local indexes.</summary>
/// <param name="Functions">Functions in declaration order.</param>
/// <param name="EntryPoint">The index of the parameterless Main function, or -1 for a library requiring linking.</param>
public sealed record BoundProgram(ImmutableArray<BoundFunction> Functions, int EntryPoint)
{
    /// <summary>Validated value layouts; type identifiers are 100 + declaration index.</summary>
    public ImmutableArray<BoundStruct> Structures { get; init; } = [];

    /// <summary>Compiler-supplied, bounded buffers; never represented as source values or raw addresses.</summary>
    public ImmutableArray<HostBuffer> Buffers { get; init; } = [];

    /// <summary>The runtime behavior verified for this program and its imports.</summary>
    public BoundRuntimePolicy RuntimePolicy { get; init; }
}

/// <summary>A compact value type of at most sixteen bytes, with no references or aliases.</summary>
public sealed record BoundStruct(string Name, ImmutableArray<BoundStructField> Fields, int Size)
{
    /// <summary>Whether a value has one noncopyable owner.</summary>
    public bool IsUnique { get; init; }

    /// <summary>The declared NoThrow instance cleanup method, if any.</summary>
    public string? CleanupMethod { get; init; }

    /// <summary>Whether access is limited to its declaring scope.</summary>
    public bool IsPrivate { get; init; }

    /// <summary>Whether access is limited to the current compilation or module.</summary>
    public bool IsInternal { get; init; }
}

/// <summary>A naturally aligned field whose offset and width are in bytes.</summary>
public sealed record BoundStructField(string Name, PrimitiveType Type, int Offset, int Size)
{
    /// <summary>Whether assignment is permitted after the declaring receiver's construction.</summary>
    public bool IsMutable { get; init; }

    /// <summary>Whether access is limited to methods of the declaring struct.</summary>
    public bool IsPrivate { get; init; }

    /// <summary>Whether access is limited to the current compilation or module.</summary>
    public bool IsInternal { get; init; }
}

/// <summary>A function whose parameter slots precede its local slots.</summary>
/// <param name="Name">The declared name.</param>
/// <param name="ReturnType">The declared result type.</param>
/// <param name="Variables">Parameter and local slots.</param>
/// <param name="ParameterCount">The number of parameter slots.</param>
/// <param name="Body">The structured body.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundFunction(string Name, PrimitiveType ReturnType, ImmutableArray<BoundVariable> Variables,
    int ParameterCount, BoundBlock Body, TextSpan Span)
{
    /// <summary>Whether this method consumes its unique receiver during lexical cleanup.</summary>
    public bool IsCleanup { get; init; }

    /// <summary>Whether the declaration guarantees no language-level throwing effects; implementations must prove this bound.</summary>
    public bool IsNoThrow { get; init; }

    /// <summary>Whether this void declaration guarantees no normal continuation; exceptional or fatal failure remains possible.</summary>
    public bool DoesNotReturn { get; init; }

    /// <summary>Whether this is an imported declaration with no implementation. Native emission requires it to be resolved.</summary>
    public bool IsExternal { get; init; }

    /// <summary>Whether this function initializes its receiver's fields and returns the value.</summary>
    public bool IsConstructor { get; init; }

    /// <summary>Whether calls are limited to the declaring struct.</summary>
    public bool IsPrivate { get; init; }

    /// <summary>Whether calls are limited to the current compilation or module.</summary>
    public bool IsInternal { get; init; }

    /// <summary>The fully qualified declaring struct, if this is a member.</summary>
    public string? DeclaringStruct { get; init; }

    /// <summary>Whether a member has no receiver.</summary>
    public bool IsStatic { get; init; }

    /// <summary>A fixed compiler-defined operation, or None for an ordinary function.</summary>
    public BoundIntrinsic Intrinsic { get; init; }
}

/// <summary>A parameter or initialized local declaration.</summary>
/// <param name="Name">The source name.</param>
/// <param name="Type">The stored type.</param>
/// <param name="IsMutable">Whether assignment is permitted.</param>
/// <param name="Span">The declaration range.</param>
public sealed record BoundVariable(string Name, PrimitiveType Type, bool IsMutable, TextSpan Span)
{
    /// <summary>Whether this parameter receives a value only on a true bool return.</summary>
    public bool IsOut { get; init; }
}

/// <summary>A structured statement, retaining its source range.</summary>
/// <param name="Span">The source range.</param>
public abstract record BoundStatement(TextSpan Span);

/// <summary>A lexical block evaluated in source order.</summary>
/// <param name="Statements">The statements.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundBlock(ImmutableArray<BoundStatement> Statements, TextSpan Span) : BoundStatement(Span)
{
    /// <summary>Whether this block establishes a source lexical scope for managed locals.</summary>
    public bool IsManagedLexicalScope { get; init; }

    /// <summary>Managed local handles whose lexical roots retire at this block's exit.</summary>
    public ImmutableArray<int> RetiredManagedLocals { get; init; } = [];
}

/// <summary>Initializes a previously vacant local slot.</summary>
/// <param name="Variable">The local slot.</param>
/// <param name="Initializer">The initializer.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundLocal(int Variable, BoundExpression Initializer, TextSpan Span) : BoundStatement(Span);

/// <summary>Introduces a vacant owner whose initializer is a conditional output in the following region.</summary>
public sealed record BoundOwnerDeclaration(int Variable, TextSpan Span) : BoundStatement(Span);

/// <summary>Assigns an initialized mutable local.</summary>
/// <param name="Variable">The local slot.</param>
/// <param name="Value">The assigned value.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundAssignment(int Variable, BoundExpression Value, TextSpan Span) : BoundStatement(Span);

/// <summary>Writes a field in a mutable local value, without aliasing its storage.</summary>
public sealed record BoundFieldAssignment(int Variable, int Field, BoundExpression Value, TextSpan Span) : BoundStatement(Span)
{
    /// <summary>Additional field indexes descending through the initialized value of <see cref="Field"/>.</summary>
    public ImmutableArray<int> NestedFields { get; init; } = [];
}

/// <summary>A checked write to a compiler-supplied writable buffer.</summary>
public sealed record BoundBufferAssignment(int Buffer, BoundExpression Index, BoundExpression Value, TextSpan Span) : BoundStatement(Span);

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

/// <summary>A cleanup region for normal exits and checked exceptional unwinding, but not fail-fast termination.</summary>
public sealed record BoundTryFinally(BoundBlock Body, BoundBlock Finally, TextSpan Span) : BoundStatement(Span);

/// <summary>A lexical view retaining its owner place and checked initialized-prefix range.</summary>
public sealed record BoundViewScope(int Variable, BoundExpression Owner, BoundExpression Start,
    BoundExpression Length, BoundBlock Body, TextSpan Span) : BoundStatement(Span)
{
    /// <summary>Whether the view holds exclusive writable access to its named mutable owner.</summary>
    public bool IsMutable { get; init; }
}

/// <summary>Writes a checked element through an active exclusive lexical view.</summary>
public sealed record BoundViewSet(int Variable, BoundExpression Index, BoundExpression Value, TextSpan Span) : BoundStatement(Span);

/// <summary>Ends a lexical unique owner if it remains live; a moved owner is vacant.</summary>
public sealed record BoundDrop(int Variable, int CleanupFunction, TextSpan Span) : BoundStatement(Span)
{
    /// <summary>An owning output transfers only on a successful bool return; otherwise it is dropped.</summary>
    public bool IsOutput { get; init; }
}

/// <summary>Drops completed owning fields on checked exceptional constructor exit in reverse acquisition order.</summary>
public sealed record BoundDropConstructedFields(int Variable, ImmutableArray<int> AcquisitionOrder, TextSpan Span) : BoundStatement(Span);

/// <summary>An explicit unsafe source context; only checked failures from its nested recovery regions can be caught.</summary>
public sealed record BoundUnsafe(BoundBlock Body, TextSpan Span) : BoundStatement(Span);

/// <summary>A checked-failure recovery region, with optional cleanup after body or handler completion.</summary>
public sealed record BoundTryCatch(BoundBlock Body, BoundBlock Catch, BoundBlock? Finally, TextSpan Span) : BoundStatement(Span);

/// <summary>Evaluates a boolean once and fails according to the selected runtime policy when false.</summary>
/// <param name="Condition">The boolean success predicate.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundRequire(BoundExpression Condition, TextSpan Span) : BoundStatement(Span);

/// <summary>A function return.</summary>
/// <param name="Value">The result, or null for a void return.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundReturn(BoundExpression? Value, TextSpan Span) : BoundStatement(Span);

/// <summary>A typed expression, evaluated left-to-right except for short-circuit operators.</summary>
/// <param name="Type">The result type.</param>
/// <param name="Span">The source range.</param>
public abstract record BoundExpression(PrimitiveType Type, TextSpan Span);

/// <summary>An integer or canonical boolean constant.</summary>
/// <param name="Value">The 64-bit payload; uint64 uses its unchecked bit pattern, int32/uint32 retain their signed 32-bit bit-pattern payload, char16 uses 0..65535, and booleans use zero or one.</param>
/// <param name="Type">The constant type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundConstant(long Value, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A read of an initialized slot.</summary>
/// <param name="Variable">The slot index.</param>
/// <param name="Type">The slot type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundRead(int Variable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Transfers a named unique owner and vacates its source.</summary>
public sealed record BoundMove(int Variable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A synchronous, nonescaping receiver loan; never a source value.</summary>
public sealed record BoundReceiverLoan(int Variable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A reserved, nonescaping exclusive loan of a named mutable native buffer owner.</summary>
public sealed record BoundMutableReceiverLoan(int Variable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Reads the fixed length of an active lexical native buffer view.</summary>
public sealed record BoundViewLength(int Variable, TextSpan Span) : BoundExpression(PrimitiveType.Int32, Span);

/// <summary>Reads a checked element from an active lexical native buffer view.</summary>
public sealed record BoundViewRead(int Variable, BoundExpression Index, TextSpan Span) : BoundExpression(PrimitiveType.Int32, Span);

/// <summary>A synchronous readonly loan of an initialized owning field place.</summary>
public sealed record BoundFieldLoan(BoundFieldRead Place, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

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

/// <summary>A checked conversion between supported scalar types.</summary>
public sealed record BoundCast(BoundExpression Operand, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Reads a field from a copied struct value.</summary>
public sealed record BoundFieldRead(BoundExpression Receiver, int Field, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A caller-owned output slot passed through private callee scratch storage.</summary>
public sealed record BoundOutArgument(int Variable, bool DeclaresVariable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A checked read from a compiler-supplied buffer.</summary>
public sealed record BoundBufferRead(int Buffer, BoundExpression Index, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);
