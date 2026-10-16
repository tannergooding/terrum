// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
    /// <summary>An explicitly nullable managed-node handle.</summary>
    OptionalManagedNode,
    /// <summary>A writable handle to a compiler-managed node.</summary>
    WriteManagedNode,
    /// <summary>An explicitly nullable writable managed-node handle.</summary>
    OptionalWriteManagedNode,
    /// <summary>An IEEE-754 binary32 value stored as its raw 32-bit representation.</summary>
    Float32,
    /// <summary>An IEEE-754 binary64 value stored as its raw 64-bit representation.</summary>
    Float64,
    /// <summary>A compilation-local readonly descriptor of validated Copy token records, not a public ABI type.</summary>
    TokenSpan,
    /// <summary>A compiler-owned UTF-16-NUL image address, restricted to reviewed no-escape imports.</summary>
    NativeUtf16ZPointer,
    /// <summary>A native callback address restricted by its selected signature and lifetime contract.</summary>
    NativeCallbackAddress = 19,
    /// <summary>Inline storage whose closed geometry is carried by selected native fixed-array metadata.</summary>
    NativeFixedArray,
    /// <summary>A compiler-owned ASCII UTF-8-NUL image address for reviewed no-escape imports.</summary>
    NativeUtf8ZPointer,
    // Keep the removed native-address value reserved so private module type ordinals remain stable.
    ReservedNativeAddress = 28,
    /// <summary>A catalogued, no-escape address of a checked plain aggregate value.</summary>
    NativeStorageAddress,
    /// <summary>A catalogued immutable native storage record whose layout is local to its selected profile.</summary>
    NativeStorageRecord,
    /// <summary>An unmanaged opaque <c>ptr&lt;void&gt;</c> value with no pinning or ownership guarantee.</summary>
    NativeRawPointer,
}

/// <summary>Disjoint source-class type identifiers start above the complete inline-struct range.</summary>
public static class ManagedClassTypes
{
    /// <summary>The number of fields tracked by the construction verifier's 32-bit mask.</summary>
    public const int MaximumFields = 32;
    /// <summary>The first source-defined managed class type identifier.</summary>
    public const int First = 1024;
    /// <summary>The first nullable class handle; class identifiers remain disjoint.</summary>
    public const int OptionalFirst = First + 256;
    /// <summary>The first writable source-class handle.</summary>
    public const int WriteFirst = OptionalFirst + 256;
    /// <summary>The first nullable writable source-class handle.</summary>
    public const int OptionalWriteFirst = WriteFirst + 256;

    /// <summary>The bounded per-object payload size, independent of compact inline values.</summary>
    public const int MaximumPayload = 128;

    /// <summary>Whether a type is a source-defined managed class handle.</summary>
    public static bool IsClass(PrimitiveType type) => (int)type is (>= First and < OptionalFirst)
        or (>= WriteFirst and < OptionalWriteFirst);

    /// <summary>Whether a type is an optional managed handle.</summary>
    public static bool IsOptional(PrimitiveType type) => type is PrimitiveType.OptionalManagedNode
        or PrimitiveType.OptionalWriteManagedNode
        || (int)type is (>= OptionalFirst and < WriteFirst)
            or (>= OptionalWriteFirst and < OptionalWriteFirst + 256);

    /// <summary>The declaration index shared by all qualified forms of a class.</summary>
    public static int ClassIndex(PrimitiveType type) => (int)type switch {
        >= First and < WriteFirst => ((int)type - First) % 256,
        >= WriteFirst and < OptionalWriteFirst + 256 => ((int)type - WriteFirst) % 256,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Whether the handle grants writable referent access.</summary>
    public static bool IsWritable(PrimitiveType type) => type is PrimitiveType.WriteManagedNode
        or PrimitiveType.OptionalWriteManagedNode
        || (int)type is >= WriteFirst and < OptionalWriteFirst + 256;

    /// <summary>The readonly projection of a handle, preserving nullability.</summary>
    public static PrimitiveType Readonly(PrimitiveType type)
    {
        if (type == PrimitiveType.WriteManagedNode)
        {
            return PrimitiveType.ManagedNode;
        }
        if (type == PrimitiveType.OptionalWriteManagedNode)
        {
            return PrimitiveType.OptionalManagedNode;
        }
        return (int)type is >= WriteFirst and < OptionalWriteFirst + 256
            ? (PrimitiveType)((int)type - 512) : type;
    }

    /// <summary>The writable form of a managed handle, preserving nullability.</summary>
    public static PrimitiveType Writable(PrimitiveType type)
    {
        if (type == PrimitiveType.ManagedNode)
        {
            return PrimitiveType.WriteManagedNode;
        }
        if (type == PrimitiveType.OptionalManagedNode)
        {
            return PrimitiveType.OptionalWriteManagedNode;
        }
        if ((int)type is >= First and < WriteFirst)
        {
            return (PrimitiveType)((int)type + 512);
        }
        if (!IsWritable(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }
        return type;
    }

    /// <summary>The non-null type stored by an optional managed handle.</summary>
    public static PrimitiveType Required(PrimitiveType type)
    {
        if (type == PrimitiveType.OptionalManagedNode)
        {
            return PrimitiveType.ManagedNode;
        }
        if (type == PrimitiveType.OptionalWriteManagedNode)
        {
            return PrimitiveType.WriteManagedNode;
        }
        if (!IsOptional(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }
        return (PrimitiveType)((int)type - 256);
    }

    /// <summary>The optional form of an existing managed handle.</summary>
    public static PrimitiveType Optional(PrimitiveType type)
    {
        if (type == PrimitiveType.ManagedNode)
        {
            return PrimitiveType.OptionalManagedNode;
        }
        if (type == PrimitiveType.WriteManagedNode)
        {
            return PrimitiveType.OptionalWriteManagedNode;
        }
        if (!IsClass(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }
        return (PrimitiveType)((int)type + 256);
    }

    /// <summary>Whether a type is a managed reference handled by the image-private collector.</summary>
    public static bool IsManaged(PrimitiveType type) => type is PrimitiveType.ManagedNode
        or PrimitiveType.WriteManagedNode
        || IsClass(type) || IsOptional(type);
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
    /// <summary>Allocates and constructs a source class, publishing its handle only on success.</summary>
    ClassTryCreate,
    /// <summary>Dispatches an unsafe function through an explicit native vtable slot.</summary>
    NativeVTableCall,
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

    internal ImmutableArray<NativeFunctionPointerSignatureDefinition> NativeFunctionPointerSignatures { get; init; } = [];

    internal ImmutableArray<NativePointerTypeDefinition> NativePointerTypes { get; init; } = [];

    /// <summary>Source-defined managed layouts; handles have type identifiers starting at <see cref="ManagedClassTypes.First"/>.</summary>
    public ImmutableArray<BoundClass> Classes { get; init; } = [];

    /// <summary>Compiler-supplied, bounded buffers; never represented as source values or raw addresses.</summary>
    public ImmutableArray<HostBuffer> Buffers { get; init; } = [];

    /// <summary>Validated UTF-16-NUL literals whose addresses may only be passed to reviewed imports.</summary>
    public ImmutableArray<string> NativeUtf16ZLiterals { get; init; } = [];

    /// <summary>Validated ASCII UTF-8-NUL literals whose addresses may only be passed to reviewed imports.</summary>
    public ImmutableArray<string> NativeUtf8ZLiterals { get; init; } = [];

    /// <summary>Whether execution requires an invocation-scoped immutable <c>Host.Source</c> grant.</summary>
    public bool RequiresSourceGrant { get; internal init; }

    /// <summary>The named invocation profile required by this program, or null for the source-only profile.</summary>
    public string? InvocationProfileId { get; internal init; }

    /// <summary>The selected native import catalog identity, when source FFI is present.</summary>
    public string? NativeImportCatalogId { get; internal init; }

    internal NativeImportCatalog? NativeImportCatalog { get; init; }

    internal bool IsInvocationSourceBound { get; init; }

    /// <summary>The runtime behavior verified for this program and its imports.</summary>
    public BoundRuntimePolicy RuntimePolicy { get; init; }
}

/// <summary>A sealed, image-private managed payload with non-null initialized fields.</summary>
public sealed record BoundClass(string Name, ImmutableArray<BoundStructField> Fields, int Size)
{
    /// <summary>Whether access is limited to the current compilation.</summary>
    public bool IsInternal { get; init; }
    /// <summary>Whether writable handles to this class may be published.</summary>
    public bool IsMutable { get; init; }
}

/// <summary>A compact value type of at most sixteen bytes, with checked value fields, a compilation-local token cursor capture, or a compiler-sealed opaque payload.</summary>
public sealed record BoundStruct(string Name, ImmutableArray<BoundStructField> Fields, int Size)
{
    internal TokenSpanTypeRole TokenSpanRole { get; init; }

    /// <summary>Whether the value requests declaration-order natural layout; a public commitment also requires <see cref="BlittableTargetRequirement"/>.</summary>
    public bool IsSequential { get; init; }

    /// <summary>The provisional fixed-storage target requirement, or zero for no commitment. This bound-IR opt-in does not register a source attribute.</summary>
    public int BlittableTargetRequirement { get; init; }

    /// <summary>Whether this Copy value has the storage and call ABI of its sole scalar field.</summary>
    public bool IsTransparent { get; init; }

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

    /// <summary>Whether direct access requires an explicit unsafe caller scope.</summary>
    public bool RequiresUnsafeCaller { get; init; }
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
    internal DirectNativeImport? DirectNativeImport { get; init; }
    internal PrimitiveType? NativeFunctionPointerType { get; init; }

    internal NativeVTableMethodDefinition? NativeVTableMethod { get; init; }
    internal ImmutableArray<NativeImportBoundParameterMapping> NativeImportParameterMappings { get; init; }

    internal TrRmScalarGraph? IndexedBody { get; init; }
    /// <summary>The declared property member name, distinct from the callable implementation's name.</summary>
    public string? PropertyName { get; init; }

    /// <summary>Whether this method consumes its unique receiver during lexical cleanup.</summary>
    public bool IsCleanup { get; init; }

    /// <summary>Whether the declaration guarantees no language-level throwing effects; implementations must prove this bound.</summary>
    public bool IsNoThrow { get; init; }

    /// <summary>Whether calling this declaration requires an explicit unsafe caller context.</summary>
    public bool RequiresUnsafeCaller { get; init; }

    /// <summary>Whether this void declaration guarantees no normal continuation; exceptional or fatal failure remains possible.</summary>
    public bool DoesNotReturn { get; init; }

    /// <summary>Whether this is an imported declaration with no implementation. Native emission requires it to be resolved.</summary>
    public bool IsExternal { get; init; }

    /// <summary>The explicit native import key selected by a source semantic attribute, if any.</summary>
    internal string? NativeImportKey { get; init; }

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

    /// <summary>Whether this function was declared with static operator syntax.</summary>
    public bool IsOperator { get; init; }

    /// <summary>A fixed compiler-defined operation, or None for an ordinary function.</summary>
    public BoundIntrinsic Intrinsic { get; init; }

    /// <summary>Whether this source constructor initializes an unpublished managed receiver.</summary>
    public bool IsClassConstructor { get; init; }

    /// <summary>Source constructor called by a compiler-defined class factory, or -1.</summary>
    public int ClassConstructor { get; init; } = -1;

    /// <summary>Constructor input slots whose backing dependencies must be forwarded before publication. Never persisted.</summary>
    public ImmutableArray<int> CapturedParameters { get; init; } = [];

    /// <summary>Whether a Span result forwards backing from its declared input parameters. Never persisted.</summary>
    internal bool ReturnsForwardedBacking { get; init; }

    /// <summary>Input parameter ordinals that conservatively constrain a forwarded Span result. Never persisted.</summary>
    internal ImmutableArray<int> ResultDependencyParameters { get; init; } = [];
}

internal sealed record DirectNativeImport(string LibraryName, string ExportName,
    ImmutableArray<DirectNativeOutput> Outputs)
{
    internal ImmutableArray<DirectNativeParameter> Parameters { get; init; } = [];
}

internal sealed record DirectNativeOutput(int ParameterIndex, PrimitiveType PointeeType,
    uint ResultCondition, uint FailureDisposition);

internal sealed record DirectNativeParameter(int ParameterIndex, PrimitiveType PointeeType,
    NativeParameterAccess Access, NativeParameterRetention Retention, int Extent);

/// <summary>A parameter or local storage declaration.</summary>
/// <param name="Name">The source name.</param>
/// <param name="Type">The stored type.</param>
/// <param name="IsMutable">Whether assignment is permitted.</param>
/// <param name="Span">The declaration range.</param>
public sealed record BoundVariable(string Name, PrimitiveType Type, bool IsMutable, TextSpan Span)
{
    internal BoundNativeFixedArrayLayout? NativeFixedArrayLayout { get; init; }

    internal NativeStorageRecordLayoutDefinition? NativeStorageRecordLayout { get; init; }

    internal NativeStorageRecordFacts? NativeStorageRecordFacts { get; init; }

    /// <summary>Whether this parameter receives a value only on a true bool return.</summary>
    public bool IsOut { get; init; }

    /// <summary>Whether this source parameter uses the vacant initialization loan mode.</summary>
    public bool IsInit { get; init; }

    /// <summary>Whether this source parameter aliases an existing readonly scalar place.</summary>
    internal bool IsImplicitRef { get; init; }

    /// <summary>Whether this local is a hidden temporary backing an implicit-reference argument.</summary>
    internal bool IsImplicitReferenceTemporary { get; init; }

    /// <summary>Whether a vacant local may initialize its storage without replacing a live value.</summary>
    public bool IsInitializationLocal { get; init; }
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

/// <summary>Declares typed storage that cannot be read until checked initialization publishes its value.</summary>
public sealed record BoundVacantLocal(int Variable, TextSpan Span) : BoundStatement(Span);

/// <summary>Ends a definitely live named local value, performing its scope-exit obligations and leaving its fixed binding vacant.</summary>
/// <param name="Variable">The declared local slot, never a parameter or receiver.</param>
/// <param name="CleanupFunction">The verified unique cleanup function, or -1 for compiler-owned cleanup or values without cleanup.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundLifetimeEnd(int Variable, int CleanupFunction, TextSpan Span) : BoundStatement(Span);

/// <summary>Introduces a vacant owner whose initializer is a conditional output in the following region.</summary>
public sealed record BoundOwnerDeclaration(int Variable, TextSpan Span) : BoundStatement(Span);

/// <summary>Assigns mutable storage or initializes a vacant initialization local without granting new authority.</summary>
/// <param name="Variable">The local slot.</param>
/// <param name="Value">The assigned value.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundAssignment(int Variable, BoundExpression Value, TextSpan Span) : BoundStatement(Span);

/// <summary>Conditionally writes an initialized optional local or class field.</summary>
public sealed record BoundOptionalAssignment(BoundExpression Place, BoundExpression Value, TextSpan Span)
    : BoundStatement(Span);

/// <summary>Writes a field in a mutable local value, without aliasing its storage.</summary>
public sealed record BoundFieldAssignment(int Variable, int Field, BoundExpression Value, TextSpan Span) : BoundStatement(Span)
{
    /// <summary>Additional field indexes descending through the initialized value of <see cref="Field"/>.</summary>
    public ImmutableArray<int> NestedFields { get; init; } = [];

    internal bool IsUnsafeAuthorized { get; init; }
}

/// <summary>A checked write to a compiler-supplied writable buffer.</summary>
public sealed record BoundBufferAssignment(int Buffer, BoundExpression Index, BoundExpression Value, TextSpan Span) : BoundStatement(Span)
{
    /// <summary>Whether <see cref="Index"/> stores an end-relative offset instead of an absolute index.</summary>
    internal bool IsEndRelativeOffset { get; init; }
}

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

/// <summary>A structured loop with a checked condition.</summary>
/// <param name="Condition">A boolean condition.</param>
/// <param name="Body">The loop body.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundWhile(BoundExpression Condition, BoundStatement Body, TextSpan Span) : BoundStatement(Span)
{
    /// <summary>The scoped for-loop iterator, evaluated after body fallthrough or continue, before the next condition.</summary>
    public BoundStatement? Iterator { get; init; }

    /// <summary>Whether the first condition follows the first body execution.</summary>
    public bool IsPostTested { get; init; }

    // A source-audited scalar switch dispatcher, not a trusted termination certificate.
    internal int SwitchArmVariable { get; init; } = -1;
}

/// <summary>Exits an enclosing loop after leaving its nested lexical regions.</summary>
public sealed record BoundBreak(TextSpan Span) : BoundStatement(Span)
{
    /// <summary>One-based lexical loop depth, with one denoting the nearest loop.</summary>
    public int TargetDepth { get; init; } = 1;
}

/// <summary>Continues an enclosing loop after leaving its nested lexical regions.</summary>
public sealed record BoundContinue(TextSpan Span) : BoundStatement(Span)
{
    /// <summary>One-based lexical loop depth, with one denoting the nearest loop.</summary>
    public int TargetDepth { get; init; } = 1;
}

/// <summary>A cleanup region for normal exits and checked exceptional unwinding, but not fail-fast termination.</summary>
public sealed record BoundTryFinally(BoundBlock Body, BoundBlock Finally, TextSpan Span) : BoundStatement(Span);

/// <summary>A lexical view retaining its owner place and checked initialized-prefix range.</summary>
public sealed record BoundViewScope(int Variable, BoundExpression Owner, BoundExpression Start,
    BoundExpression Length, BoundBlock Body, TextSpan Span) : BoundStatement(Span)
{
    /// <summary>Whether the view holds exclusive writable access to its named mutable owner.</summary>
    public bool IsMutable { get; init; }

    /// <summary>The exclusive upper bound or end-relative offset, evaluated once after <see cref="Start"/>.</summary>
    public BoundExpression? RangeEnd { get; init; }

    /// <summary>Whether <see cref="Start"/> stores an offset from the owner's end.</summary>
    public bool IsStartEndRelative { get; init; }

    /// <summary>Whether <see cref="RangeEnd"/> stores an offset from the owner's end.</summary>
    public bool IsRangeEndRelative { get; init; }
}

/// <summary>Writes a checked element through an active exclusive lexical view.</summary>
public sealed record BoundViewSet(int Variable, BoundExpression Index, BoundExpression Value, TextSpan Span) : BoundStatement(Span);

/// <summary>Ends a lexical unique owner if it remains live; a moved owner is vacant.</summary>
public sealed record BoundDrop(int Variable, int CleanupFunction, TextSpan Span) : BoundStatement(Span)
{
    /// <summary>An owning output transfers only on a successful bool return; otherwise it is dropped.</summary>
    public bool IsOutput { get; init; }
}

/// <summary>Retires a lexical span descriptor's dependency, without releasing its backing owner.</summary>
public sealed record BoundDropSpan(int Variable, TextSpan Span) : BoundStatement(Span);

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
public abstract record BoundExpression(PrimitiveType Type, TextSpan Span)
{
    // Composite expressions override both hooks so bounded walks and rewrites see the same children.
    internal virtual bool HasValidNestedExpressions => true;
    internal virtual ImmutableArray<BoundExpression> NestedExpressions => [];
    internal virtual BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this;
}

/// <summary>A scalar constant represented by its canonical or raw-bit payload.</summary>
/// <param name="Value">The 64-bit payload; uint64 uses its unchecked bit pattern, float32/float64 use IEEE-754 bits, int32/uint32 retain their signed 32-bit bit-pattern payload, char16 uses 0..65535, booleans use zero or one, and native UTF-NUL pointers use a compiler-owned literal index.</param>
/// <param name="Type">The constant type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundConstant(long Value, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A verified address of a static function used as a native callback.</summary>
public sealed record BoundNativeCallbackAddress(int Function, TextSpan Span)
    : BoundExpression(PrimitiveType.NativeCallbackAddress, Span);

/// <summary>A verified address of a static function with a concrete native ABI signature.</summary>
public sealed record BoundNativeFunctionAddress(int Function, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }
}

/// <summary>A raw address of a local value formed inside an unsafe scope.</summary>
public sealed record BoundNativePointerAddress(int Variable, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }
}

/// <summary>Reads a value through an explicitly unsafe native pointer.</summary>
public sealed record BoundNativePointerRead(BoundExpression Pointer, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Pointer];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Pointer = rewrite(Pointer) };
}

/// <summary>Writes a value through an explicitly unsafe writable native pointer.</summary>
public sealed record BoundNativePointerWrite(BoundExpression Pointer, BoundExpression Value, TextSpan Span)
    : BoundExpression(PrimitiveType.Void, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Pointer, Value];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            Pointer = rewrite(Pointer),
            Value = rewrite(Value),
        };
}

/// <summary>The verified storage geometry for a bounded native fixed-array carrier.</summary>
public readonly record struct BoundNativeFixedArrayLayout(PrimitiveType CarrierType, PrimitiveType ElementType,
    int ElementCount, int ElementStride, int ByteExtent, int Alignment);

/// <summary>A source-ordered initializer for a bounded native fixed-array carrier.</summary>
public sealed record BoundNativeFixedArrayInitialization(BoundNativeFixedArrayLayout Layout,
    ImmutableArray<BoundExpression> Elements, TextSpan Span)
    : BoundExpression(Layout.CarrierType, Span)
{
    internal override bool HasValidNestedExpressions => !Elements.IsDefault;
    internal override ImmutableArray<BoundExpression> NestedExpressions => Elements.IsDefault ? [] : Elements;
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => Elements.IsDefault ? this
        : this with { Elements = [.. Elements.Select(rewrite)] };
}

/// <summary>A checked read of one initialized element from a bounded native fixed-array local.</summary>
public sealed record BoundNativeFixedArrayElement(int Variable, BoundNativeFixedArrayLayout Layout,
    BoundExpression Index, TextSpan Span) : BoundExpression(Layout.ElementType, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Index];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Index = rewrite(Index) };
}

/// <summary>Forms an unsafe typed pointer to one element of a bounded native fixed-array local.</summary>
public sealed record BoundNativeFixedArrayPointerAddress(int Variable, BoundNativeFixedArrayLayout Layout,
    BoundExpression Index, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Index];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Index = rewrite(Index) };
}

/// <summary>A checked write to one element of a mutable bounded native fixed-array local.</summary>
public sealed record BoundNativeFixedArrayWrite(int Variable, BoundNativeFixedArrayLayout Layout,
    BoundExpression Index, BoundExpression Value, TextSpan Span) : BoundExpression(PrimitiveType.Void, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Index, Value];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            Index = rewrite(Index),
            Value = rewrite(Value),
        };
}

/// <summary>A bounded address into initialized contiguous local storage.</summary>
public sealed record BoundNativeStorageAddress(
    int Variable,
    BoundExpression Index,
    PrimitiveType CarrierType,
    PrimitiveType ElementType,
    PrimitiveType AddressType,
    int ElementCount,
    int ElementStride,
    int ByteExtent,
    bool IsReadonly,
    TextSpan Span) : BoundExpression(AddressType, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Index];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Index = rewrite(Index) };
}

internal sealed record NativeStorageLoan(int BackingVariable, PrimitiveType CarrierType,
    PrimitiveType ElementType, bool IsReadonly, int ByteExtent);

/// <summary>A catalogued local record with a readonly dependency on bounded native storage.</summary>
internal sealed record BoundNativeStorageRecordInitialization(
    NativeStorageRecordLayoutDefinition Layout,
    BoundExpression DataSource,
    ImmutableArray<BoundExpression> Fields,
    TextSpan Span) : BoundExpression(PrimitiveType.NativeStorageRecord, Span)
{
    internal NativeStorageLoan? Loan { get; init; }

    internal override bool HasValidNestedExpressions => !Fields.IsDefault;
    internal override ImmutableArray<BoundExpression> NestedExpressions =>
        Fields.IsDefault ? [DataSource] : [DataSource, .. Fields];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => Fields.IsDefault ? this
        : this with {
            DataSource = rewrite(DataSource),
            Fields = [.. Fields.Select(rewrite)],
        };
}

internal sealed record NativeAggregateStorageParameterDefinition(
    int ParameterIndex,
    PrimitiveType AddressType,
    int ByteExtent,
    int RequiredAlignment,
    bool IsReadonly,
    string? SourceType = null);

internal sealed record NativeAggregateStorageLayoutDefinition(string TypeName, int ByteExtent, int Alignment)
{
    internal ImmutableArray<NativeAggregateStorageFieldLayoutDefinition> Fields { get; init; } = [];
}

internal sealed record NativeAggregateStorageFieldLayoutDefinition(
    string Name, string TypeName, int Offset, int Size);

/// <summary>An explicitly absent optional managed handle.</summary>
public sealed record BoundNull(PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Widens a known non-null handle without changing its pointer bits.</summary>
public sealed record BoundOptionalLift(BoundExpression Value, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Reads one initialized optional value and evaluates its fallback only when absent.</summary>
public sealed record BoundCoalesce(BoundExpression Optional, BoundExpression Fallback,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Projects a writable managed handle to readonly without changing its pointer bits.</summary>
public sealed record BoundReadonlyProjection(BoundExpression Value, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Tests an optional handle for absence.</summary>
public sealed record BoundIsNull(BoundExpression Value, TextSpan Span) : BoundExpression(PrimitiveType.Bool, Span);

/// <summary>Tests an optional handle and snapshots its non-null value into a scoped variable on success.</summary>
public sealed record BoundIsSome(BoundExpression Value, int Variable, TextSpan Span) : BoundExpression(PrimitiveType.Bool, Span);

/// <summary>Tests whether a named local or parameter currently contains an initialized value.</summary>
public sealed record BoundIsInitialized(int Variable, TextSpan Span) : BoundExpression(PrimitiveType.Bool, Span);

/// <summary>Tests optional-handle presence, including named-storage initialization, and captures the match.</summary>
public sealed record BoundIsVariablePattern(BoundExpression Value, int Variable, bool IncludeNull, TextSpan Span)
    : BoundExpression(PrimitiveType.Bool, Span);

/// <summary>A read of an initialized slot.</summary>
/// <param name="Variable">The slot index.</param>
/// <param name="Type">The slot type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundRead(int Variable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Transfers a named unique owner and vacates its source.</summary>
public sealed record BoundMove(int Variable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A synchronous, nonescaping receiver loan; never a source value.</summary>
public sealed record BoundReceiverLoan(int Variable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A reserved, nonescaping exclusive loan of a named mutable receiver place.</summary>
public sealed record BoundMutableReceiverLoan(int Variable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Reads the fixed length of an active lexical native buffer view.</summary>
public sealed record BoundViewLength(int Variable, TextSpan Span) : BoundExpression(PrimitiveType.Int32, Span);

/// <summary>Reads the length of a compiler-supplied host buffer.</summary>
public sealed record BoundBufferLength(int Buffer, TextSpan Span) : BoundExpression(PrimitiveType.Int32, Span);

/// <summary>Reads a checked element from an active lexical native buffer view.</summary>
public sealed record BoundViewRead(int Variable, BoundExpression Index, TextSpan Span) : BoundExpression(PrimitiveType.Int32, Span);

/// <summary>Validates a named TokenSnapshot's initialized records before publishing its readonly descriptor.</summary>
public sealed record BoundSnapshotSpan(int Variable, int SourceBuffer, TextSpan Span)
    : BoundExpression(PrimitiveType.TokenSpan, Span);

/// <summary>Reads a descriptor's unsigned native-width count using the compilation target's integer carrier.</summary>
public sealed record BoundSpanLength(BoundExpression View, TextSpan Span)
    : BoundExpression(TokenSpanContract.IndexCarrier, Span);

/// <summary>Checks an unsigned native-width index and copies one complete token record.</summary>
public sealed record BoundSpanRead(BoundExpression View, BoundExpression Index, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span);

/// <summary>The cursor-only nonempty remaining-view operation; all backing dependencies are preserved.</summary>
public sealed record BoundSpanAdvance(BoundExpression View, TextSpan Span)
    : BoundExpression(PrimitiveType.TokenSpan, Span);

/// <summary>A synchronous readonly loan of an initialized owning field place.</summary>
public sealed record BoundFieldLoan(BoundFieldRead Place, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A reserved, nonescaping exclusive loan of a mutable field in an initialized struct place.</summary>
public sealed record BoundMutableFieldLoan(BoundFieldRead Place, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A checked unary operation.</summary>
/// <param name="Operator">The operator token.</param>
/// <param name="Operand">The input.</param>
/// <param name="Type">The result type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundUnary(TokenKind Operator, BoundExpression Operand, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Reads a writable integer place once and commits a checked replacement before returning its selected value.</summary>
public sealed record BoundUpdate(TokenKind Operator, BoundExpression Place, bool IsPrefix,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Snapshots a writable integer place before evaluating the RHS and commits only a successful result.</summary>
public sealed record BoundCompoundAssignment(TokenKind Operator, BoundExpression Place, BoundExpression Right,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Calls a static Copy-struct operator on a saved place value, then commits its exact-type result.</summary>
public sealed record BoundNominalUpdate(TokenKind Operator, BoundExpression Place, bool IsPrefix,
    int Function, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }
}

/// <summary>A checked binary or short-circuit boolean operation.</summary>
/// <param name="Operator">The operator token.</param>
/// <param name="Left">The first input.</param>
/// <param name="Right">The second input.</param>
/// <param name="Type">The result type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundBinary(TokenKind Operator, BoundExpression Left, BoundExpression Right,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Evaluates the condition once and only the selected result arm.</summary>
public sealed record BoundConditional(BoundExpression Condition, BoundExpression WhenTrue,
    BoundExpression WhenFalse, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A statically resolved direct call.</summary>
/// <param name="Function">The callee index.</param>
/// <param name="Arguments">Arguments evaluated once in source order.</param>
/// <param name="Type">The result type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundCall(int Function, ImmutableArray<BoundExpression> Arguments,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    /// <summary>Maps source-order arguments to formal argument positions when binding is non-positional.</summary>
    internal ImmutableArray<int> ArgumentParameterIndices { get; init; }

    /// <summary>Per source-order argument, the backing temporary for implicit-reference materialization, or -1.</summary>
    internal ImmutableArray<int> ArgumentImplicitReferenceTemporaries { get; init; }

    internal int ParameterIndex(int argumentIndex) =>
        ArgumentParameterIndices.IsDefault ? argumentIndex : ArgumentParameterIndices[argumentIndex];

    internal int ImplicitReferenceTemporary(int argumentIndex) =>
        ArgumentImplicitReferenceTemporaries.IsDefault
            ? -1 : ArgumentImplicitReferenceTemporaries[argumentIndex];

    internal int ArgumentIndex(int parameterIndex)
    {
        if (ArgumentParameterIndices.IsDefault)
        {
            return parameterIndex;
        }
        for (var index = 0; index < ArgumentParameterIndices.Length; index++)
        {
            if (ArgumentParameterIndices[index] == parameterIndex)
            {
                return index;
            }
        }
        throw new InvalidOperationException("Call parameter has no bound argument.");
    }

    internal bool IsUnsafeAuthorized { get; init; }
}

/// <summary>A checked call through a concrete native function-pointer signature.</summary>
public sealed record BoundIndirectCall(BoundExpression Target, ImmutableArray<BoundExpression> Arguments,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }

    internal override bool HasValidNestedExpressions => !Arguments.IsDefault;
    internal override ImmutableArray<BoundExpression> NestedExpressions => Arguments.IsDefault
        ? [Target] : [Target, .. Arguments];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => Arguments.IsDefault ? this
        : this with {
            Target = rewrite(Target),
            Arguments = [.. Arguments.Select(rewrite)],
        };
}

/// <summary>A checked conversion between supported scalar types.</summary>
public sealed record BoundCast(BoundExpression Operand, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>A lexical unsafe scope limited to one expression.</summary>
public sealed record BoundUnsafeExpression(BoundExpression Expression, TextSpan Span)
    : BoundExpression(Expression.Type, Span);

/// <summary>Reads a field from a copied struct value.</summary>
public sealed record BoundFieldRead(BoundExpression Receiver, int Field, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }
}

/// <summary>A caller-owned output slot passed through private callee scratch storage.</summary>
public sealed record BoundOutArgument(int Variable, bool DeclaresVariable, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    /// <summary>Requires vacant caller storage before the conditional initialization call.</summary>
    public bool IsInit { get; init; }
}

/// <summary>A checked read from a compiler-supplied buffer.</summary>
public sealed record BoundBufferRead(int Buffer, BoundExpression Index, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    /// <summary>Whether <see cref="Index"/> stores an end-relative offset instead of an absolute index.</summary>
    internal bool IsEndRelativeOffset { get; init; }
}
