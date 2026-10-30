// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Primitive type identities used by the compiler.</summary>
[SuppressMessage("Design", "CA1027:Mark enums with FlagsAttribute", Justification = "Primitive type ordinals are disjoint discriminants, not combinable flags.")]
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
    /// <summary>A compiler-owned UTF-16-NUL image address for a matching nonescaping native parameter.</summary>
    NativeUtf16ZPointer,
    /// <summary>Retained ordinal for the retired catalog-selected callback carrier.</summary>
    LegacyNativeCallbackAddress = 19,
    /// <summary>Inline storage whose closed geometry is carried by selected native fixed-array metadata.</summary>
    NativeFixedArray,
    /// <summary>A compiler-owned ASCII UTF-8-NUL image address for a matching nonescaping native parameter.</summary>
    NativeUtf8ZPointer,
    /// <summary>Retained ordinal for the removed native-address carrier.</summary>
    LegacyNativeAddress = 28,
    /// <summary>An address of checked storage validated against a native pointer contract.</summary>
    NativeStorageAddress,
    /// <summary>Retained ordinal for the retired catalog-selected native storage record.</summary>
    LegacyNativeStorageRecord = 30,
    /// <summary>An unmanaged opaque <c>ptr&lt;void&gt;</c> value with no pinning or ownership guarantee.</summary>
    NativeRawPointer,
    /// <summary>An unsigned eight-bit raw code unit.</summary>
    Char8 = 32,
    /// <summary>An unsigned thirty-two-bit raw code unit.</summary>
    Char32,
    /// <summary>A validated Unicode scalar value.</summary>
    Rune,
    /// <summary>A signed eight-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names the Terrum primitive type represented by the compiler.")]
    Int8,
    /// <summary>A signed sixteen-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names the Terrum primitive type represented by the compiler.")]
    Int16,
    /// <summary>An unsigned eight-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names the Terrum primitive type represented by the compiler.")]
    UInt8,
    /// <summary>An unsigned sixteen-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names the Terrum primitive type represented by the compiler.")]
    UInt16,
    /// <summary>A signed one-hundred-twenty-eight-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names the Terrum primitive type represented by the compiler.")]
    Int128 = 39,
    /// <summary>An unsigned one-hundred-twenty-eight-bit integer.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names the Terrum primitive type represented by the compiler.")]
    UInt128,
    /// <summary>An IEEE-754 binary16 value stored as its raw sixteen-bit representation.</summary>
    Float16,
    /// <summary>A bfloat16 value stored as its raw sixteen-bit representation.</summary>
    BFloat16,
    /// <summary>An IEEE-754 binary128 value stored as its raw one-hundred-twenty-eight-bit representation.</summary>
    Float128,
    /// <summary>An IEEE decimal32 BID value stored as its raw thirty-two-bit representation.</summary>
    Decimal32,
    /// <summary>An IEEE decimal64 BID value stored as its raw sixty-four-bit representation.</summary>
    Decimal64,
    /// <summary>An IEEE decimal128 BID value stored as its raw one-hundred-twenty-eight-bit representation.</summary>
    Decimal128,
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

internal enum BoundInterfaceTypeKind : byte
{
    Primitive = 1,
    TypeParameter = 2,
    Nominal = 3,
    CheckedReference = 4,
}

internal enum BoundInterfaceTypeParameterScope : byte
{
    None = 0,
    Interface = 1,
    Member = 2,
}

internal enum BoundInterfaceTypeArgumentKind : byte
{
    Type = 1,
    Constant = 2,
}

internal sealed record BoundInterfaceTypeArgument(
    BoundInterfaceTypeArgumentKind Kind,
    BoundInterfaceType? Type,
    BoundInterfaceType? ConstantType,
    BoundConstant? ConstantValue,
    BoundInterfaceTypeParameterScope ConstantParameterScope,
    int ConstantParameterOrdinal)
{
    internal static BoundInterfaceTypeArgument ForType(BoundInterfaceType type) =>
        new(BoundInterfaceTypeArgumentKind.Type, type, null, null,
            BoundInterfaceTypeParameterScope.None, -1);

    internal static BoundInterfaceTypeArgument ForConstant(
        BoundInterfaceType type, BoundConstant value) =>
        new(BoundInterfaceTypeArgumentKind.Constant, null, type, value,
            BoundInterfaceTypeParameterScope.None, -1);

    internal static BoundInterfaceTypeArgument ForConstantParameter(
        BoundInterfaceType type, BoundInterfaceTypeParameterScope scope,
        int ordinal) =>
        new(BoundInterfaceTypeArgumentKind.Constant, null, type, null,
            scope, ordinal);
}

internal sealed record BoundInterfaceType(
    BoundInterfaceTypeKind Kind,
    PrimitiveType PrimitiveType,
    string? NominalName,
    int TypeParameterOrdinal,
    ImmutableArray<BoundInterfaceTypeArgument> TypeArguments,
    bool IsWritable,
    bool IsOptional,
    bool IsGarbageCollected,
    bool IsInitialization)
{
    internal BoundInterfaceTypeParameterScope TypeParameterScope { get; init; }
    internal BoundModuleImport? ModuleImport { get; init; }
}

internal enum BoundInterfaceFormalKind : byte
{
    Type = 1,
    Constant = 2,
}

internal sealed record BoundInterfaceFormal(
    int Ordinal,
    string Name,
    BoundInterfaceFormalKind Kind,
    bool IsSelf,
    ImmutableArray<string> Attributes,
    ImmutableArray<string> AttributeTokens,
    BoundInterfaceType? ConstantType,
    BoundInterfaceType? TypeDefault,
    BoundConstant? ConstantDefault);

internal enum BoundInterfaceConstraintKind : byte
{
    Type = 1,
    Attribute = 2,
}

internal sealed record BoundInterfaceConstraint(
    int FormalOrdinal,
    BoundInterfaceConstraintKind Kind,
    BoundInterfaceType? Type,
    string? AttributeName)
{
    internal BoundInterfaceTypeParameterScope FormalScope { get; init; }
        = BoundInterfaceTypeParameterScope.Interface;
}

internal sealed record BoundInterfaceDefinition(
    string QualifiedName,
    bool IsPublic,
    bool IsPartial,
    ImmutableArray<BoundInterfaceFormal> Formals,
    ImmutableArray<BoundInterfaceType> Bases,
    ImmutableArray<BoundInterfaceConstraint> Constraints,
    ImmutableArray<BoundInterfaceMemberDefinition> Members)
{
    internal BoundInterfaceAccessibility Accessibility { get; init; }
    internal ImmutableArray<string> AttributeTokens { get; init; } = [];
    internal BoundModuleImport? ModuleImport { get; init; }
}

internal enum BoundInterfaceAccessibility : byte
{
    Default = 0,
    Public = 1,
    Internal = 2,
    File = 3,
    Friend = 4,
    Private = 5,
}

internal enum BoundInterfaceMemberKind : byte
{
    Callable = 1,
    Operator = 2,
    Property = 3,
    Indexer = 4,
    AssociatedType = 5,
    Constant = 6,
}

internal enum BoundInterfaceParameterMode : byte
{
    Value = 0,
    Out = 1,
    Init = 2,
    ImplicitRef = 3,
}

internal enum BoundInterfaceAccessorKind : byte
{
    Get = 1,
    Set = 2,
    Init = 3,
}

internal sealed record BoundInterfaceParameter(
    string Name,
    BoundInterfaceType Type,
    BoundInterfaceParameterMode Mode,
    ImmutableArray<string> Attributes,
    ImmutableArray<string> AttributeTokens);

internal sealed record BoundInterfaceAccessor(
    BoundInterfaceAccessorKind Kind,
    BoundInterfaceAccessibility Accessibility,
    bool IsMutable,
    bool RequiresUnsafeCaller,
    bool DoesNotReturn,
    bool HasBody,
    ImmutableArray<string> Attributes,
    ImmutableArray<string> AttributeTokens,
    ImmutableArray<BoundDependencyContract> Dependencies,
    ImmutableArray<string> ContractTokens);

internal sealed record BoundInterfaceMemberDefinition(
    int Ordinal,
    string Name,
    string DeclarationKey,
    BoundInterfaceMemberKind Kind,
    BoundInterfaceAccessibility Accessibility,
    bool IsStatic,
    bool IsExternal,
    bool IsPartial,
    bool RequiresUnsafeCaller,
    bool DoesNotReturn,
    bool IsAsync,
    bool HasBody,
    ImmutableArray<string> Attributes,
    ImmutableArray<string> AttributeTokens,
    ImmutableArray<string> ContractTokens,
    TokenKind Operator,
    BoundInterfaceType? ResultType,
    ImmutableArray<BoundInterfaceFormal> TypeParameters,
    ImmutableArray<BoundInterfaceConstraint> Constraints,
    ImmutableArray<BoundInterfaceParameter> Parameters,
    ImmutableArray<BoundDependencyContract> Dependencies,
    ImmutableArray<BoundInterfaceAccessor> Accessors,
    BoundInterfaceType? AssociatedDefaultType,
    BoundInterfaceType? AssociatedConstantType,
    BoundConstant? AssociatedConstantDefault);

/// <summary>The resolved overflow behavior of a source integer operation.</summary>
internal enum BoundIntegerOverflowBehavior
{
    Checked,
    Wrapping,
}

/// <summary>A source-mapped, structured program with resolved function and local indexes.</summary>
/// <param name="Functions">Functions in declaration order.</param>
/// <param name="EntryPoint">The index of the parameterless Main function, or -1 for a library requiring linking.</param>
public sealed record BoundProgram(ImmutableArray<BoundFunction> Functions, int EntryPoint)
{
    /// <summary>Validated value layouts; type identifiers are 100 + declaration index.</summary>
    public ImmutableArray<BoundStruct> Structures { get; init; } = [];

    internal ImmutableArray<BoundEnumDefinition> EnumDefinitions { get; init; } = [];

    internal NativeCoreRegistrationSet? NativeCoreRegistration { get; init; }

    internal ImmutableArray<NativeFunctionPointerSignatureDefinition> NativeFunctionPointerSignatures { get; init; } = [];

    internal ImmutableArray<NativePointerTypeDefinition> NativePointerTypes { get; init; } = [];

    internal ImmutableArray<CheckedReferenceTypeDefinition> CheckedReferenceTypes { get; init; } = [];

    internal ImmutableArray<BoundInterfaceDefinition> Interfaces { get; init; } = [];

    /// <summary>Exact provider and TypeDef identities for imported nominal declarations.</summary>
    public ImmutableArray<BoundGenericNominalTypeDefinition> GenericNominalTypeDefinitions { get; init; } = [];

    /// <summary>Ordered nominal TypeDef substitutions used by this program's type identities.</summary>
    public ImmutableArray<BoundGenericNominalTypeInstance> GenericNominalTypeInstances { get; init; } = [];

    /// <summary>The host-selected exact Core Task definition, without enabling Task runtime behavior.</summary>
    public CoreTaskTypeIdentity? CoreTaskType { get; init; }

    internal string? NativeLoweringAgreement { get; init; }

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

    /// <summary>The selected instance cleanup method, if any.</summary>
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

internal enum BoundDependencyRelation : byte
{
    DependsOn = 1,
    Captures = 2,
    ForwardsBacking = 3,
}

internal enum BoundDependencyDestination : byte
{
    Result = 1,
    Receiver = 2,
    InitializedOutput = 3,
    RetainedFormal = 4,
    Operation = 5,
}

internal enum BoundDependencySourceKind : byte
{
    Receiver = 1,
    Formal = 2,
}

internal readonly record struct BoundDependencySource(
    BoundDependencySourceKind Kind, int? FormalOrdinal);

internal sealed record BoundDependencyContract(
    BoundDependencyRelation Relation,
    BoundDependencyDestination Destination,
    int? DestinationOrdinal,
    ImmutableArray<BoundDependencySource> Sources);

internal enum BoundCopyOriginSummaryKind : byte
{
    NormalResult = 1,
    FreshInlineConstruction = 2,
}

internal enum BoundCopyOriginSourceKind : byte
{
    Formal = 1,
    Receiver = 2,
}

internal enum BoundCopyOriginSourceView : byte
{
    EntryValue = 1,
    EntryReferentValue = 2,
}

internal readonly record struct BoundCopyOriginFieldStep(PrimitiveType OwnerType, int FieldOrdinal);

internal readonly struct BoundCopyOriginFieldPath : IEquatable<BoundCopyOriginFieldPath>
{
    private readonly ImmutableArray<BoundCopyOriginFieldStep> _steps;

    internal BoundCopyOriginFieldPath(ImmutableArray<BoundCopyOriginFieldStep> steps)
    {
        _steps = steps;
    }

    internal ImmutableArray<BoundCopyOriginFieldStep> Steps => _steps.IsDefault ? [] : _steps;

    public bool Equals(BoundCopyOriginFieldPath other)
        => Steps.AsSpan().SequenceEqual(other.Steps.AsSpan());

    public override bool Equals(object? obj)
        => obj is BoundCopyOriginFieldPath other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var step in Steps)
        {
            hash.Add(step);
        }
        return hash.ToHashCode();
    }
}

internal readonly record struct BoundCopyOrigin(
    BoundCopyOriginSourceKind SourceKind,
    BoundCopyOriginSourceView SourceView,
    int SourceOrdinal,
    BoundCopyOriginFieldPath SourcePath);

internal sealed record BoundCopyFact(
    BoundCopyOriginFieldPath DestinationPath,
    ImmutableArray<BoundCopyOrigin> Origins);

internal sealed record BoundCopyOriginSummary(
    BoundCopyOriginSummaryKind Kind,
    ImmutableArray<BoundCopyFact> Facts);

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
    internal NativeOutputDeclarationRole? NativeOutputRole { get; init; }
    internal ImmutableArray<NativeOutputContract> NativeOutputContracts { get; init; } = [];
    internal BoundModuleImport? ModuleImport { get; init; }
    /// <summary>Whether this external declaration originated in a module reference rather than source import syntax.</summary>
    internal bool IsModuleReference { get; init; }
    internal PrimitiveType? NativeFunctionPointerType { get; init; }
    internal NativeCallingConvention? NativeCallableConvention { get; init; }
    internal string? NativeCallableEntryName { get; init; }

    internal NativeVTableMethodDefinition? NativeVTableMethod { get; init; }
    internal TrRmScalarGraph? IndexedBody { get; init; }
    /// <summary>The declared property member name, distinct from the callable implementation's name.</summary>
    public string? PropertyName { get; init; }

    /// <summary>Whether this method consumes its unique receiver during lexical cleanup.</summary>
    public bool IsCleanup { get; init; }

    /// <summary>Whether mandatory verification must prove this function body satisfies a scoped no-failure obligation.</summary>
    public bool NoFailureProof { get; init; }

    /// <summary>Whether calling this declaration requires an explicit unsafe caller context.</summary>
    public bool RequiresUnsafeCaller { get; init; }

    internal bool DeclaredRequiresUnsafeCaller { get; init; }

    /// <summary>Whether this declaration guarantees no normal continuation; exceptional or fatal failure remains possible.</summary>
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
    /// <summary>Input parameter ordinals that constrain a forwarded Span result. Never persisted.</summary>
    internal ImmutableArray<int> ResultDependencyParameters { get; init; } = [];
    /// <summary>Whether the receiver is an admitted source of a forwarded Span result. Never persisted.</summary>
    internal bool ResultDependencyIncludesReceiver { get; init; }

    internal ImmutableArray<BoundDependencyContract> DependencyContracts { get; init; } = [];
    internal BoundCopyOriginSummary? CopyOriginSummary { get; init; }
}

internal sealed record BoundModuleImport(string ProducerIdentity, string ExportKey,
    uint TargetRequirement, uint RuntimePolicy);

internal enum NativeImportResolutionMode : byte
{
    Link = 1,
    Dynamic = 2,
}

internal enum NativeImportResolutionPolicy : byte
{
    NotApplicable = 0,
    Default = 1,
    CompatibilitySearch = 2,
}

internal sealed record DirectNativeImport(string LibraryName, string ExportName,
    ImmutableArray<DirectNativeOutput> Outputs)
{
    internal NativeCallingConvention CallingConvention { get; init; } = NativeCallingConvention.Native;
    internal NativeImportResolutionMode ResolutionMode { get; init; } = NativeImportResolutionMode.Link;
    internal NativeImportResolutionPolicy ResolutionPolicy { get; init; }
    internal ImmutableArray<DirectNativeParameter> Parameters { get; init; } = [];
}

internal sealed record DirectNativeOutput(int ParameterIndex, PrimitiveType PointeeType,
    uint ResultCondition, uint FailureDisposition);

internal enum NativeOutputDeclarationRole : byte
{
    DirectImport = 1,
    ForeignInterfaceMethod = 2,
}

internal enum NativeOutputFormation : byte
{
    VacantWholeObject = 1,
}

internal enum NativeOutputPublicationPhase : byte
{
    NormalCompletion = 1,
}

internal enum NativeOutputSelector : byte
{
    NonZero = 3,
    Zero = 4,
    NonNegative = 5,
    Negative = 6,
}

internal enum NativeOutputState : byte
{
    Vacant = 0,
    Live = 1,
}

internal enum NativeOutputDependencySourceKind : byte
{
    Empty = 0,
    Formal = 1,
    Receiver = 2,
}

internal enum NativeOutputDependencyRelationship : byte
{
    Empty = 0,
    Borrow = 1,
    Forward = 2,
    Union = 3,
}

internal readonly record struct NativeOutputDependency(
    byte DestinationKind,
    NativeOutputDependencySourceKind SourceKind,
    NativeOutputDependencyRelationship Relationship,
    NativeOutputPublicationPhase Phase,
    int DestinationOrdinal,
    int SourceOrdinal);

internal sealed record NativeOutputContract(
    int FormalOrdinal,
    PrimitiveType PointeeType,
    NativeOutputFormation Formation,
    NativeOutputPublicationPhase PublicationPhase,
    NativeOutputSelector SuccessSelector,
    NativeOutputState SuccessState,
    NativeOutputSelector ComplementSelector,
    NativeOutputState ComplementState,
    ImmutableArray<NativeOutputDependency> Dependencies);

internal sealed record DirectNativeParameter(int ParameterIndex, PrimitiveType PointeeType,
    NativeParameterAccess Access, NativeParameterRetention Retention, uint Extent);

/// <summary>A parameter or local storage declaration.</summary>
/// <param name="Name">The source name.</param>
/// <param name="Type">The stored type.</param>
/// <param name="IsMutable">Whether assignment is permitted.</param>
/// <param name="Span">The declaration range.</param>
public sealed record BoundVariable(string Name, PrimitiveType Type, bool IsMutable, TextSpan Span)
{
    internal BoundNativeFixedArrayLayout? NativeFixedArrayLayout { get; init; }

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
public sealed record BoundConstant : BoundExpression
{
    private readonly long? _legacyValue;

    /// <summary>The source-compatible 64-bit payload for values whose complete representation fits.</summary>
    /// <exception cref="InvalidOperationException">The complete value uses more than sixty-four bits.</exception>
    public long Value => _legacyValue
        ?? throw new InvalidOperationException("This scalar constant requires its complete bit payload.");

    internal ScalarBits Bits { get; }

    /// <summary>Creates a constant using the existing payload representation.</summary>
    /// <param name="value">The 64-bit payload using the type's existing bound representation.</param>
    /// <param name="type">The constant type.</param>
    /// <param name="span">The source range.</param>
    public BoundConstant(long value, PrimitiveType type, TextSpan span) : base(type, span)
    {
        if (BoundEnumTypes.IsType(type) && value is < int.MinValue or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        if (PrimitiveTypeFacts.ScalarWidth(type) > sizeof(ulong))
        {
            throw new ArgumentException("A wide scalar constant requires its complete bit payload.", nameof(value));
        }
        _legacyValue = value;
        Bits = ScalarBits.FromLegacyValue(value, type);
    }

    internal BoundConstant(ScalarBits bits, PrimitiveType type, TextSpan span) : base(type, span)
    {
        Bits = bits;
        _legacyValue = bits.TryGetLegacyValue(type, out var value) ? value : null;
    }

    internal bool TryGetLegacyValue(out long value)
    {
        value = _legacyValue.GetValueOrDefault();
        return _legacyValue.HasValue;
    }

    /// <summary>Deconstructs a value that has a source-compatible 64-bit payload.</summary>
    public void Deconstruct(out long value, out PrimitiveType type, out TextSpan span)
    {
        value = Value;
        type = Type;
        span = Span;
    }

    /// <summary>Returns a diagnostic description of this constant.</summary>
    public override string ToString() => $"BoundConstant {{ Type = {Type}, Span = {Span}, Bits = {Bits} }}";
}

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
public sealed record BoundNativePointerRead(BoundExpression Address, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Address];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Address = rewrite(Address) };
}

/// <summary>Forms a checked non-owning reference to an addressable local place.</summary>
public sealed record BoundReferenceFormation(BoundExpression Place, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Place];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Place = rewrite(Place) };
}

/// <summary>Reborrows the exact referent of an existing ordinary checked reference.</summary>
public sealed record BoundReferenceReborrow(
    BoundExpression Descriptor, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Descriptor];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite)
        => this with { Descriptor = rewrite(Descriptor) };
}

/// <summary>Reads the initialized referent value through a checked ordinary reference.</summary>
public sealed record BoundReferenceRead(BoundExpression Descriptor, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Descriptor];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Descriptor = rewrite(Descriptor) };
}

/// <summary>Writes an exact value through a checked writable reference.</summary>
public sealed record BoundReferenceWrite(BoundExpression Descriptor, BoundExpression Value, TextSpan Span)
    : BoundExpression(PrimitiveType.Void, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Descriptor, Value];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            Descriptor = rewrite(Descriptor),
            Value = rewrite(Value),
        };
}

/// <summary>Writes a value through an explicitly unsafe writable native pointer.</summary>
public sealed record BoundNativePointerWrite(BoundExpression Address, BoundExpression Value, TextSpan Span)
    : BoundExpression(PrimitiveType.Void, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Address, Value];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            Address = rewrite(Address),
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
    internal override bool HasValidNestedExpressions =>
        !Elements.IsDefault && Elements.All(static element => element is not null);
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

/// <summary>A call-extent reservation of vacant local storage for a native output formal.</summary>
public sealed record BoundNativeOutputReservation(int Variable, PrimitiveType PointeeType,
    TextSpan Span) : BoundExpression(PrimitiveType.Void, Span);

/// <summary>An explicitly absent optional managed handle.</summary>
public sealed record BoundNull(PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);

/// <summary>Widens a known non-null handle without changing its pointer bits.</summary>
public sealed record BoundOptionalLift(BoundExpression Value, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Value];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Value = rewrite(Value) };
}

/// <summary>Reads one initialized optional value and evaluates its fallback only when absent.</summary>
public sealed record BoundCoalesce(BoundExpression Optional, BoundExpression Fallback,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Optional, Fallback];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            Optional = rewrite(Optional),
            Fallback = rewrite(Fallback),
        };
}

/// <summary>Projects a writable managed handle to readonly without changing its pointer bits.</summary>
public sealed record BoundReadonlyProjection(BoundExpression Value, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Value];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Value = rewrite(Value) };
}

/// <summary>Tests an optional handle for absence.</summary>
public sealed record BoundIsNull(BoundExpression Value, TextSpan Span) : BoundExpression(PrimitiveType.Bool, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Value];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Value = rewrite(Value) };
}

/// <summary>Tests an optional handle and snapshots its non-null value into a scoped variable on success.</summary>
public sealed record BoundIsSome(BoundExpression Value, int Variable, TextSpan Span) : BoundExpression(PrimitiveType.Bool, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Value];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Value = rewrite(Value) };
}

/// <summary>Tests whether a named local or parameter currently contains an initialized value.</summary>
public sealed record BoundIsInitialized(int Variable, TextSpan Span) : BoundExpression(PrimitiveType.Bool, Span);

/// <summary>Tests optional-handle presence or captures a supported inferred value on a successful match.</summary>
public sealed record BoundIsVariablePattern(BoundExpression Value, int Variable, bool IncludeNull, TextSpan Span)
    : BoundExpression(PrimitiveType.Bool, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Value];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Value = rewrite(Value) };
}

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
public sealed record BoundViewRead(int Variable, BoundExpression Index, TextSpan Span)
    : BoundExpression(PrimitiveType.Int32, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Index];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Index = rewrite(Index) };
}

/// <summary>Validates a named TokenSnapshot's initialized records before publishing its readonly descriptor.</summary>
public sealed record BoundSnapshotSpan(int Variable, int SourceBuffer, TextSpan Span)
    : BoundExpression(PrimitiveType.TokenSpan, Span);

/// <summary>Reads a descriptor's unsigned native-width count using the compilation target's integer carrier.</summary>
public sealed record BoundSpanLength(BoundExpression View, TextSpan Span)
    : BoundExpression(TokenSpanContract.IndexCarrier, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [View];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { View = rewrite(View) };
}

/// <summary>Checks an unsigned native-width index and copies one complete token record.</summary>
public sealed record BoundSpanRead(BoundExpression View, BoundExpression Index, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [View, Index];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            View = rewrite(View),
            Index = rewrite(Index),
        };
}

/// <summary>The cursor-only nonempty remaining-view operation; all backing dependencies are preserved.</summary>
public sealed record BoundSpanAdvance(BoundExpression View, TextSpan Span)
    : BoundExpression(PrimitiveType.TokenSpan, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [View];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { View = rewrite(View) };
}

/// <summary>A synchronous readonly loan of an initialized owning field place.</summary>
public sealed record BoundFieldLoan(BoundFieldRead Place, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Place];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            Place = (BoundFieldRead)rewrite(Place),
        };
}

/// <summary>A reserved, nonescaping exclusive loan of a mutable field in an initialized struct place.</summary>
public sealed record BoundMutableFieldLoan(BoundFieldRead Place, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Place];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            Place = (BoundFieldRead)rewrite(Place),
        };
}

/// <summary>A unary source operation with resolved arithmetic behavior.</summary>
/// <param name="Operator">The operator token.</param>
/// <param name="Operand">The input.</param>
/// <param name="Type">The result type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundUnary(TokenKind Operator, BoundExpression Operand, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal BoundIntegerOverflowBehavior OverflowBehavior { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Operand];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Operand = rewrite(Operand) };
}

/// <summary>Reads a writable integer place once and commits a resolved replacement before returning its selected value.</summary>
public sealed record BoundUpdate(TokenKind Operator, BoundExpression Place, bool IsPrefix,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    internal BoundIntegerOverflowBehavior OverflowBehavior { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Place];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Place = rewrite(Place) };
}

/// <summary>Snapshots a writable integer place before evaluating the RHS and commits only a successful result.</summary>
public sealed record BoundCompoundAssignment(TokenKind Operator, BoundExpression Place, BoundExpression Right,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    internal BoundIntegerOverflowBehavior OverflowBehavior { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Place, Right];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            Place = rewrite(Place),
            Right = rewrite(Right),
        };
}

/// <summary>Calls a static Copy-struct operator on a saved place value, then commits its exact-type result.</summary>
public sealed record BoundNominalUpdate(TokenKind Operator, BoundExpression Place, bool IsPrefix,
    int Function, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Place];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Place = rewrite(Place) };
}

/// <summary>A binary or short-circuit boolean operation with resolved arithmetic behavior.</summary>
/// <param name="Operator">The operator token.</param>
/// <param name="Left">The first input.</param>
/// <param name="Right">The second input.</param>
/// <param name="Type">The result type.</param>
/// <param name="Span">The source range.</param>
public sealed record BoundBinary(TokenKind Operator, BoundExpression Left, BoundExpression Right,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    internal BoundIntegerOverflowBehavior OverflowBehavior { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Left, Right];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            Left = rewrite(Left),
            Right = rewrite(Right),
        };
}

/// <summary>Evaluates the condition once and only the selected result arm.</summary>
public sealed record BoundConditional(BoundExpression Condition, BoundExpression WhenTrue,
    BoundExpression WhenFalse, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Condition, WhenTrue, WhenFalse];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with {
            Condition = rewrite(Condition),
            WhenTrue = rewrite(WhenTrue),
            WhenFalse = rewrite(WhenFalse),
        };
}

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

    internal override bool HasValidNestedExpressions =>
        !Arguments.IsDefault && Arguments.All(static argument => argument is not null);
    internal override ImmutableArray<BoundExpression> NestedExpressions =>
        Arguments.IsDefault ? [] : Arguments;
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => Arguments.IsDefault ? this
        : this with { Arguments = [.. Arguments.Select(rewrite)] };
}

/// <summary>A checked call through a concrete native function-pointer signature.</summary>
public sealed record BoundIndirectCall(BoundExpression Target, ImmutableArray<BoundExpression> Arguments,
    PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }

    internal override bool HasValidNestedExpressions =>
        !Arguments.IsDefault && Target is not null && Arguments.All(static argument => argument is not null);
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
public sealed record BoundCast(BoundExpression Operand, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Operand];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Operand = rewrite(Operand) };
}

/// <summary>A lexical unsafe scope limited to one expression.</summary>
public sealed record BoundUnsafeExpression(BoundExpression Expression, TextSpan Span)
    : BoundExpression(Expression.Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Expression];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Expression = rewrite(Expression) };
}

/// <summary>Reads a field from a copied struct value.</summary>
public sealed record BoundFieldRead(BoundExpression Receiver, int Field, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal bool IsUnsafeAuthorized { get; init; }

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Receiver];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Receiver = rewrite(Receiver) };
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

    internal override ImmutableArray<BoundExpression> NestedExpressions => [Index];
    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) => this with { Index = rewrite(Index) };
}
