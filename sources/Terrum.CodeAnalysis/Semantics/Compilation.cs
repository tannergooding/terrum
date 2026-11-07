// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>A compiler diagnostic paired with the source snapshot that owns its UTF-16 span.</summary>
public sealed record SourceDiagnostic(SyntaxTree SyntaxTree, Diagnostic Diagnostic);

/// <summary>The default overflow policy for ordinary source integer arithmetic.</summary>
public enum SourceArithmeticProfile
{
    /// <summary>Integer addition, subtraction, multiplication and negation are checked.</summary>
    Development = 0,
    /// <summary>Integer addition, subtraction, multiplication and negation wrap to their destination width.</summary>
    Release = 1,
}

/// <summary>Immutable semantic options for one source compilation.</summary>
public readonly record struct CompilationOptions
{
    /// <summary>The exact host-selected Windows x64 agreement for normal-backed references.</summary>
    public const string WindowsX64NormalBackingAgreement =
        "terrum.experimental.windows-x64.normal-backing";

    /// <summary>The exact host-selected Windows x64 agreement for externally bounded rootless references.</summary>
    public const string WindowsX64RootlessReferencesAgreement =
        "terrum.experimental.windows-x64.rootless-references";

    /// <summary>The source arithmetic defaults; zero-initialized options select Development.</summary>
    public SourceArithmeticProfile SourceArithmeticProfile { get; init; }

    /// <summary>The checked-failure delivery policy; zero-initialized options select FailFast.</summary>
    public BoundRuntimePolicy RuntimePolicy { get; init; }

    /// <summary>The explicit host-selected Core native registration for this using compilation, if any.</summary>
    public NativeCoreRegistrationSet? NativeCoreRegistration { get; init; }

    /// <summary>The explicit host-selected Core layout registration for this using compilation, if any.</summary>
    public LayoutCoreRegistrationSet? LayoutCoreRegistration { get; init; }

    /// <summary>The explicit host-selected static-initialization Core registration for this using compilation, if any.</summary>
    public StaticInitializationCoreRegistrationSet?
        StaticInitializationCoreRegistration { get; init; }

    /// <summary>The independently selected LazyInitialization Core registration for this using compilation, if any.</summary>
    public LazyInitializationCoreRegistrationSet?
        LazyInitializationCoreRegistration { get; init; }

    /// <summary>The explicit host-selected Core exception anchors for using Modules, if any.</summary>
    public ImmutableArray<CoreExceptionIdentitySelection>
        CoreExceptionIdentitySelections { get; init; }

    /// <summary>The explicit host-selected Core contract-failure identities for using Modules, if any.</summary>
    public CoreContractFailureIdentitySelection?
        CoreContractFailureIdentitySelection { get; init; }

    /// <summary>The explicit host-selected target-lowering agreement, if any.</summary>
    public string? NativeLoweringAgreement { get; init; }
}

/// <summary>
/// The semantic result for one or more syntax snapshots. The executable profile supports only
/// admitted scalar values and compact POD structs with structured control flow. Source arithmetic
/// profile selection is provisional and is resolved into individual integer operations.
/// </summary>
public sealed class Compilation
{
    /// <summary>The version of the supported executable semantic profile.</summary>
    public const string ProfileVersion = "native-10";

    /// <summary>Resolves the checked-failure and compact-ownership semantic profile.</summary>
    public static string GetNativeProfileVersion(BoundRuntimePolicy policy, bool uniqueValues) => policy switch {
        BoundRuntimePolicy.FailFast => uniqueValues ? "native-12" : ProfileVersion,
        BoundRuntimePolicy.CheckedExceptions => uniqueValues ? "native-12" : "native-11",
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    /// <summary>Includes declaration-derived ownership transfer across function boundaries.</summary>
    public static string GetNativeProfileVersion(BoundRuntimePolicy policy, bool uniqueValues, bool ownershipTransfer)
    {
        var baseline = GetNativeProfileVersion(policy, uniqueValues);
        return ownershipTransfer ? "native-13" : baseline;
    }

    /// <summary>Includes unique structs containing compact aggregate field layouts.</summary>
    public static string GetNativeProfileVersion(BoundRuntimePolicy policy, bool uniqueValues,
        bool ownershipTransfer, bool compositeUnique)
    {
        var baseline = GetNativeProfileVersion(policy, uniqueValues, ownershipTransfer);
        return compositeUnique ? "native-14" : baseline;
    }

    /// <summary>Includes the compiler-defined owned native buffer ABI and cleanup obligation.</summary>
    public static string GetNativeProfileVersion(BoundRuntimePolicy policy, bool uniqueValues,
        bool ownershipTransfer, bool compositeUnique, bool ownedNativeBuffer)
    {
        var baseline = GetNativeProfileVersion(policy, uniqueValues, ownershipTransfer, compositeUnique);
        return ownedNativeBuffer ? "native-15" : baseline;
    }

    /// <summary>Includes nonescaping lexical readonly views over initialized native buffer ranges.</summary>
    public static string GetNativeProfileVersion(BoundRuntimePolicy policy, bool uniqueValues,
        bool ownershipTransfer, bool compositeUnique, bool ownedNativeBuffer, bool readonlyNativeView)
    {
        var baseline = GetNativeProfileVersion(policy, uniqueValues, ownershipTransfer, compositeUnique, ownedNativeBuffer);
        return readonlyNativeView ? "native-16" : baseline;
    }

    /// <summary>Includes exclusive lexical writable views and in-place native buffer prefix reset.</summary>
    public static string GetNativeProfileVersion(BoundRuntimePolicy policy, bool uniqueValues,
        bool ownershipTransfer, bool compositeUnique, bool ownedNativeBuffer, bool readonlyNativeView,
        bool mutableNativeViewOrClear)
    {
        var baseline = GetNativeProfileVersion(policy, uniqueValues, ownershipTransfer, compositeUnique,
            ownedNativeBuffer, readonlyNativeView);
        return mutableNativeViewOrClear ? "native-17" : baseline;
    }

    internal static bool HasOwnedNativeBuffer(ImmutableArray<BoundStruct> structures,
        IEnumerable<BoundFunction> functions)
    {
        if (HasOwnedNativeBuffer(structures))
        {
            return true;
        }
        foreach (var function in functions)
        {
            if (function.ReturnType == PrimitiveType.OwnedInt32Buffer
                || function.Variables.Any(static variable => variable.Type == PrimitiveType.OwnedInt32Buffer))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool HasOwnedNativeBuffer(ImmutableArray<BoundStruct> structures)
    {
        foreach (var structure in structures)
        {
            if (structure.Fields.Any(static field => field.Type == PrimitiveType.OwnedInt32Buffer))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool HasCompositeUnique(ImmutableArray<BoundStruct> structures)
        => HasCompositeUnique(structures, out _);

    internal static bool HasCompositeUnique(ImmutableArray<BoundStruct> structures, out bool unique)
    {
        unique = false;
        foreach (var structure in structures)
        {
            unique |= structure.IsUnique;
            if (HasCompositeUnique(structure, structures.Length, nameof(structures)))
            {
                unique = true;
                return true;
            }
        }
        return false;
    }

    private static bool HasCompositeUnique(BoundStruct structure, int structureCount, string parameterName)
    {
        if (!structure.IsUnique)
        {
            return false;
        }
        foreach (var field in structure.Fields)
        {
            if (NativePointerTypes.IsType(field.Type) || NativeFunctionPointerTypes.IsType(field.Type))
            {
                continue;
            }
            if (BoundEnumTypes.IsType(field.Type))
            {
                continue;
            }
            var index = (int)field.Type - 100;
            if (index >= structureCount)
            {
                throw new ArgumentOutOfRangeException(parameterName, "An aggregate field has an unknown type.");
            }
            if (index >= 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether a declaration transfers compact ownership through a parameter.</summary>
    internal static bool HasOwnershipTransfer(BoundFunction function, ImmutableArray<BoundStruct> structures)
    {
        for (var slot = function.IsConstructor ? 1 : 0;
            slot < function.ParameterCount + (function.IsConstructor ? 1 : 0); slot++)
        {
            var type = function.Variables[slot].Type;
            if (ManagedClassTypes.IsManaged(type) || NativePointerTypes.IsType(type)
                || NativeFunctionPointerTypes.IsType(type) || BoundEnumTypes.IsType(type))
            {
                continue;
            }
            var index = (int)type - 100;
            if (index >= structures.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(function), "Function parameter references an unknown structure.");
            }
            if ((type == PrimitiveType.OwnedInt32Buffer || (index >= 0 && structures[index].IsUnique))
                && !(slot == 0 && function.DeclaringStruct is not null && !function.IsStatic))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Computes the profile from retained types and function declarations.</summary>
    public static string GetNativeProfileVersion(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.NativeLoweringAgreement is not null)
        {
            throw new NotSupportedException(
                "A host-selected native lowering agreement has no persisted native profile.");
        }
        if (program.Classes.Any(static item => item.Fields.Any(
                static field => ManagedClassTypes.IsOptional(field.Type))))
        {
            return "native-20";
        }
        if (!program.Classes.IsEmpty)
        {
            if (program.Functions.Any(static function => ManagedClassTypes.IsOptional(function.ReturnType)
                    || function.Variables.Any(static variable => ManagedClassTypes.IsOptional(variable.Type))))
            {
                return "native-20";
            }
            return "native-19";
        }
        var usesManagedNodes = false;
        var ownedBuffer = false;
        var readonlyView = false;
        var mutableView = false;
        foreach (var function in program.Functions)
        {
            if (ManagedClassTypes.IsOptional(function.ReturnType))
            {
                return "native-20";
            }
            ownedBuffer |= function.ReturnType == PrimitiveType.OwnedInt32Buffer;
            var functionUsesManagedNodes = function.Intrinsic is >= BoundIntrinsic.NodeTryCreate and <= BoundIntrinsic.ClassTryCreate
                || function.ReturnType is PrimitiveType.ManagedNode or PrimitiveType.WriteManagedNode;
            foreach (var variable in function.Variables)
            {
                if (ManagedClassTypes.IsOptional(variable.Type))
                {
                    return "native-20";
                }
                ownedBuffer |= variable.Type == PrimitiveType.OwnedInt32Buffer;
                readonlyView |= variable.Type == PrimitiveType.ReadonlyInt32BufferView;
                mutableView |= variable.Type == PrimitiveType.MutableInt32BufferView;
                functionUsesManagedNodes |= variable.Type is PrimitiveType.ManagedNode
                    or PrimitiveType.WriteManagedNode;
            }
            usesManagedNodes |= functionUsesManagedNodes;
        }
        if (usesManagedNodes)
        {
            return "native-18";
        }
        var structures = program.Structures;
        var composite = HasCompositeUnique(structures, out var unique);
        ownedBuffer = ownedBuffer || HasOwnedNativeBuffer(structures);
        // BufferClear requires an OwnedInt32Buffer receiver, so buffer-free programs need no body walk.
        var mutableViewOrClear = mutableView || (ownedBuffer && UsesBufferClear(program));
        foreach (var function in program.Functions)
        {
            if (HasOwnershipTransfer(function, program.Structures))
            {
                return GetNativeProfileVersion(program.RuntimePolicy, unique,
                    ownershipTransfer: true, compositeUnique: composite, ownedNativeBuffer: ownedBuffer,
                    readonlyNativeView: readonlyView, mutableNativeViewOrClear: mutableViewOrClear);
            }
        }
        return GetNativeProfileVersion(program.RuntimePolicy, unique,
            ownershipTransfer: false, compositeUnique: composite, ownedNativeBuffer: ownedBuffer,
            readonlyNativeView: readonlyView, mutableNativeViewOrClear: mutableViewOrClear);
    }

    private static bool UsesBufferClear(BoundProgram program)
    {
        var statements = new Stack<BoundStatement>();
        var expressions = new Stack<BoundExpression>();
        foreach (var function in program.Functions)
        {
            statements.Push(function.Body);
            while (statements.Count != 0 || expressions.Count != 0)
            {
                if (expressions.TryPop(out var expression))
                {
                    switch (expression)
                    {
                        case BoundCall call:
                            if (program.Functions[call.Function].Intrinsic == BoundIntrinsic.BufferClear)
                            {
                                return true;
                            }
                            foreach (var argument in call.Arguments)
                            {
                                expressions.Push(argument);
                            }
                            break;
                        case BoundIndirectCall call:
                            expressions.Push(call.Target);
                            foreach (var argument in call.Arguments)
                            {
                                expressions.Push(argument);
                            }
                            break;
                        case BoundUnsafeExpression unsafeExpression:
                            expressions.Push(unsafeExpression.Expression);
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
                        case BoundCoalesce coalesce:
                            expressions.Push(coalesce.Optional);
                            expressions.Push(coalesce.Fallback);
                            break;
                        case BoundUnary unary:
                            expressions.Push(unary.Operand);
                            break;
                        case BoundUpdate update:
                            expressions.Push(update.Place);
                            break;
                        case BoundCompoundAssignment compound:
                            expressions.Push(compound.Place);
                            expressions.Push(compound.Right);
                            break;
                        case BoundNominalUpdate update:
                            expressions.Push(update.Place);
                            break;
                        case BoundCast cast:
                            expressions.Push(cast.Operand);
                            break;
                        case BoundFieldRead field:
                            expressions.Push(field.Receiver);
                            break;
                        case BoundFieldMove move:
                            expressions.Push(move.Place);
                            break;
                        case BoundFieldLoan loan:
                            expressions.Push(loan.Place);
                            break;
                        case BoundMutableFieldLoan loan:
                            expressions.Push(loan.Place);
                            break;
                        case BoundViewRead view:
                            expressions.Push(view.Index);
                            break;
                        case BoundBufferRead buffer:
                            expressions.Push(buffer.Index);
                            break;
                        case BoundConstant or BoundRead or BoundMove or BoundReceiverLoan
                            or BoundMutableReceiverLoan or BoundViewLength or BoundBufferLength or BoundOutArgument:
                            break;
                        default:
                            throw new ArgumentException("Unknown bound expression.", nameof(program));
                    }
                    continue;
                }
                var statement = statements.Pop();
                switch (statement)
                {
                    case BoundBlock block:
                        foreach (var child in block.Statements)
                        {
                            statements.Push(child);
                        }
                        break;
                    case BoundLocal local:
                        expressions.Push(local.Initializer);
                        break;
                    case BoundAssignment assignment:
                        expressions.Push(assignment.Value);
                        break;
                    case BoundOptionalAssignment optionalAssignment:
                        expressions.Push(optionalAssignment.Place);
                        expressions.Push(optionalAssignment.Value);
                        break;
                    case BoundFieldAssignment field:
                        expressions.Push(field.Value);
                        break;
                    case BoundBufferAssignment write:
                        expressions.Push(write.Index);
                        expressions.Push(write.Value);
                        break;
                    case BoundViewSet viewWrite:
                        expressions.Push(viewWrite.Index);
                        expressions.Push(viewWrite.Value);
                        break;
                    case BoundExpressionStatement effects:
                        expressions.Push(effects.Expression);
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
                    case BoundLifetimeEnd or BoundOwnerDeclaration or BoundVacantLocal or BoundDrop or BoundDropConstructedFields or BoundReturn
                        or BoundBreak or BoundContinue:
                        break;
                    default:
                        throw new ArgumentException("Unknown bound statement.", nameof(program));
                }
            }
        }
        return false;
    }

    private Compilation(SyntaxTree syntaxTree, ImmutableArray<Diagnostic> diagnostics, BoundProgram? program,
        ImmutableArray<SyntaxTree> syntaxTrees = default, ImmutableArray<SourceDiagnostic> sourceDiagnostics = default)
    {
        SyntaxTree = syntaxTree;
        Diagnostics = diagnostics;
        Program = program;
        SyntaxTrees = syntaxTrees.IsDefault ? [syntaxTree] : syntaxTrees;
        if (sourceDiagnostics.IsDefault)
        {
            if (diagnostics.IsEmpty)
            {
                sourceDiagnostics = [];
            }
            else
            {
                var mapped = ImmutableArray.CreateBuilder<SourceDiagnostic>(diagnostics.Length);
                foreach (var diagnostic in diagnostics)
                {
                    mapped.Add(new SourceDiagnostic(syntaxTree, diagnostic));
                }
                sourceDiagnostics = mapped.MoveToImmutable();
            }
        }
        SourceDiagnostics = sourceDiagnostics;
    }

    /// <summary>Gets the first source snapshot; use SyntaxTrees for multi-source compilations.</summary>
    public SyntaxTree SyntaxTree { get; }

    /// <summary>Gets all source snapshots in input order.</summary>
    public ImmutableArray<SyntaxTree> SyntaxTrees { get; }

    /// <summary>Gets syntax and semantic diagnostics in source order, then ordinal code order.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets each diagnostic with the source snapshot that owns its span.</summary>
    public ImmutableArray<SourceDiagnostic> SourceDiagnostics { get; }

    /// <summary>Gets the verified program, or null on error. Libraries and programs with external declarations require linking.</summary>
    public BoundProgram? Program { get; }

    /// <summary>Gets the native semantic profile of a successful compilation, including its ownership ABI.</summary>
    public string? NativeProfileVersion
    {
        get
        {
            if (Program is not BoundProgram program
                || program.NativeLoweringAgreement is not null)
            {
                return null;
            }
            return GetNativeProfileVersion(program);
        }
    }

    /// <summary>Gets whether the compilation contains errors.</summary>
    public bool HasErrors
    {
        get
        {
            foreach (var diagnostic in Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Declares and binds the functions in a syntax snapshot without modifying it.</summary>
    /// <param name="tree">The parsed source snapshot.</param>
    /// <param name="cancellationToken">Cancellation for semantic traversal.</param>
    /// <returns>A compilation; its program is null whenever there are errors.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static Compilation Create(SyntaxTree tree, CancellationToken cancellationToken = default) =>
        CreateCore(tree, isLibrary: false, cancellationToken);

    /// <summary>Binds a source snapshot using explicit source arithmetic and checked-failure runtime options.</summary>
    public static Compilation Create(SyntaxTree tree, CompilationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        return CreateCore(tree, isLibrary: false, cancellationToken,
            runtimePolicy: options.RuntimePolicy, sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds a source snapshot under an explicit checked-failure runtime policy.</summary>
    public static Compilation Create(SyntaxTree tree, BoundRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        CreateCore([tree], [], isLibrary: false, cancellationToken, runtimePolicy: runtimePolicy);

    /// <summary>Binds source snapshots for explicit native-import emission.</summary>
    public static Compilation CreateWithNativeImports(SyntaxTree tree,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return CreateCore([tree], [], isLibrary: false, cancellationToken, runtimePolicy: runtimePolicy);
    }

    /// <summary>Binds source for native-import emission using explicit compilation options.</summary>
    public static Compilation CreateWithNativeImports(SyntaxTree tree, CompilationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(tree);
        return CreateCore([tree], [], isLibrary: false, cancellationToken,
            runtimePolicy: options.RuntimePolicy, sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds source snapshots for explicit native-import emission.</summary>
    public static Compilation CreateWithNativeImports(IEnumerable<SyntaxTree> trees,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast, CancellationToken cancellationToken = default) =>
        CreateCore(trees, [], isLibrary: false, cancellationToken, runtimePolicy: runtimePolicy);

    /// <summary>Binds source snapshots for native-import emission using explicit compilation options.</summary>
    public static Compilation CreateWithNativeImports(IEnumerable<SyntaxTree> trees, CompilationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        return CreateCore(trees, [], isLibrary: false, cancellationToken,
            runtimePolicy: options.RuntimePolicy, sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds a library snapshot under an explicit checked-failure runtime policy.</summary>
    public static Compilation CreateLibrary(SyntaxTree tree, BoundRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        CreateCore([tree], [], isLibrary: true, cancellationToken, runtimePolicy: runtimePolicy);

    /// <summary>Binds library definitions using explicit source arithmetic and checked-failure runtime options.</summary>
    public static Compilation CreateLibrary(SyntaxTree tree, CompilationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        return CreateCore(tree, isLibrary: true, cancellationToken,
            runtimePolicy: options.RuntimePolicy, sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds and verifies library definitions without requiring or selecting a Main entry point.</summary>
    /// <param name="tree">The source snapshot containing the definitions.</param>
    /// <param name="cancellationToken">Cancellation for semantic traversal.</param>
    /// <returns>A compilation whose successful program has EntryPoint -1 and must be linked before execution.</returns>
    public static Compilation CreateLibrary(SyntaxTree tree, CancellationToken cancellationToken = default) =>
        CreateCore(tree, isLibrary: true, cancellationToken);

    /// <summary>Binds a library against validated external declarations without a process entry point.</summary>
    public static Compilation CreateLibrary(SyntaxTree tree, CompilationReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(tree, isLibrary: true, cancellationToken, reference);
    }

    /// <summary>Binds a library against a reference under the selected checked-failure policy.</summary>
    public static Compilation CreateLibrary(SyntaxTree tree, CompilationReference reference,
        BoundRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(tree, isLibrary: true, cancellationToken, reference, runtimePolicy);
    }

    /// <summary>Binds a library against a reference using explicit compilation options.</summary>
    public static Compilation CreateLibrary(SyntaxTree tree, CompilationReference reference,
        CompilationOptions options, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(tree, isLibrary: true, cancellationToken, reference,
            options.RuntimePolicy, options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds consumer source against validated module declarations, without reading producer source or binding its bodies.</summary>
    public static Compilation Create(SyntaxTree tree, CompilationReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(tree, isLibrary: false, cancellationToken, reference);
    }

    /// <summary>Binds a consumer against a producer with the selected checked-failure behavior.</summary>
    public static Compilation Create(SyntaxTree tree, CompilationReference reference, BoundRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(tree, isLibrary: false, cancellationToken, reference, runtimePolicy);
    }

    /// <summary>Binds a consumer against a producer using explicit compilation options.</summary>
    public static Compilation Create(SyntaxTree tree, CompilationReference reference,
        CompilationOptions options, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(tree, isLibrary: false, cancellationToken, reference,
            options.RuntimePolicy, options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    internal static void ValidateOptions(CompilationOptions options)
    {
        if (options.SourceArithmeticProfile is not (SourceArithmeticProfile.Development or SourceArithmeticProfile.Release)
            || options.RuntimePolicy is not (BoundRuntimePolicy.FailFast or BoundRuntimePolicy.CheckedExceptions))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        if (options.NativeCoreRegistration is NativeCoreRegistrationSet registration
            && !registration.IsValid())
        {
            throw new ArgumentException("The selected Core native registration is invalid.",
                nameof(options));
        }
        if (options.LayoutCoreRegistration is LayoutCoreRegistrationSet layoutRegistration
            && !layoutRegistration.IsValid())
        {
            throw new ArgumentException("The selected Core layout registration is invalid.",
                nameof(options));
        }
        if (options.StaticInitializationCoreRegistration is
                StaticInitializationCoreRegistrationSet staticInitializationRegistration
            && !staticInitializationRegistration.IsValid())
        {
            throw new ArgumentException(
                "The selected static-initialization Core registration is invalid.",
                nameof(options));
        }
        if (options.LazyInitializationCoreRegistration is
                LazyInitializationCoreRegistrationSet
                    lazyInitializationRegistration
            && !lazyInitializationRegistration.IsValid())
        {
            throw new ArgumentException(
                "The selected LazyInitialization Core registration is invalid.",
                nameof(options));
        }
        if (options.NativeCoreRegistration is NativeCoreRegistrationSet nativeRegistration
            && options.LayoutCoreRegistration is LayoutCoreRegistrationSet selectedLayoutRegistration
            && !ReferenceEquals(nativeRegistration.ModuleBindings,
                selectedLayoutRegistration.ModuleBindings))
        {
            throw new ArgumentException(
                "The selected Core native and layout registrations must share one provider binding snapshot.",
                nameof(options));
        }
        var selectedBindings = options.NativeCoreRegistration?.ModuleBindings
            ?? options.LayoutCoreRegistration?.ModuleBindings
            ?? options.StaticInitializationCoreRegistration?.ModuleBindings
            ?? options.LazyInitializationCoreRegistration?.ModuleBindings;
        if ((options.LayoutCoreRegistration is LayoutCoreRegistrationSet composedLayoutRegistration
                && !ReferenceEquals(selectedBindings,
                    composedLayoutRegistration.ModuleBindings))
            || (options.StaticInitializationCoreRegistration is
                    StaticInitializationCoreRegistrationSet composedStaticRegistration
                && !ReferenceEquals(selectedBindings,
                    composedStaticRegistration.ModuleBindings))
            || (options.LazyInitializationCoreRegistration is
                    LazyInitializationCoreRegistrationSet composedLazyRegistration
                && !ReferenceEquals(selectedBindings,
                    composedLazyRegistration.ModuleBindings)))
        {
            throw new ArgumentException(
                "Selected Core registrations must share one provider binding snapshot.",
                nameof(options));
        }
        if (!CoreExceptionIdentitySelectionContract.IsValidTable(
            options.CoreExceptionIdentitySelections))
        {
            throw new ArgumentException(
                "The selected Core exception identities are invalid.",
                nameof(options));
        }
        if (options.CoreContractFailureIdentitySelection is
                CoreContractFailureIdentitySelection contractFailureSelection
            && !CoreContractFailureIdentitySelectionContract.IsValidTable(
                [contractFailureSelection]))
        {
            throw new ArgumentException(
                "The selected Core contract-failure identities are invalid.",
                nameof(options));
        }
        if (options.NativeLoweringAgreement is string agreement
            && agreement is not (
                CompilationOptions.WindowsX64NormalBackingAgreement
                or CompilationOptions.WindowsX64RootlessReferencesAgreement))
        {
            throw new ArgumentException("The selected native lowering agreement is unsupported.",
                nameof(options));
        }
    }

    private static Compilation CreateCore(SyntaxTree tree, bool isLibrary, CancellationToken cancellationToken,
        CompilationReference? reference = null, BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast,
        SourceArithmeticProfile sourceArithmeticProfile = SourceArithmeticProfile.Development,
        NativeCoreRegistrationSet? nativeCoreRegistration = null,
        LayoutCoreRegistrationSet? layoutCoreRegistration = null,
        StaticInitializationCoreRegistrationSet?
            staticInitializationCoreRegistration = null,
        LazyInitializationCoreRegistrationSet?
            lazyInitializationCoreRegistration = null,
        string? nativeLoweringAgreement = null,
        ImmutableArray<CoreExceptionIdentitySelection> coreExceptionIdentitySelections = default,
        CoreContractFailureIdentitySelection?
            coreContractFailureIdentitySelection = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();
        var selectedNativeCoreRegistration = SelectNativeCoreRegistration(
            reference, nativeCoreRegistration);
        var selectedLayoutCoreRegistration = SelectLayoutCoreRegistration(
            reference, layoutCoreRegistration);
        var selectedStaticInitializationCoreRegistration
            = SelectStaticInitializationCoreRegistration(reference,
                staticInitializationCoreRegistration);
        var selectedLazyInitializationCoreRegistration
            = SelectLazyInitializationCoreRegistration(reference,
                lazyInitializationCoreRegistration);
        ValidateCoreRegistrationComposition(selectedNativeCoreRegistration,
            selectedLayoutCoreRegistration,
            selectedStaticInitializationCoreRegistration,
            selectedLazyInitializationCoreRegistration);
        var selectedCoreExceptionIdentitySelections
            = SelectCoreExceptionIdentitySelections(reference,
                coreExceptionIdentitySelections);
        var selectedCoreContractFailureIdentitySelections
            = SelectCoreContractFailureIdentitySelections(reference,
                coreContractFailureIdentitySelection);

        // Recovery nodes do not have the structural guarantees required for binding.
        if (tree.HasErrors)
        {
            return new Compilation(tree, tree.Diagnostics, null);
        }

        var binder = new Binder([tree], [], cancellationToken, isLibrary, reference, runtimePolicy,
            sourceArithmeticProfile: sourceArithmeticProfile,
            nativeCoreRegistration: selectedNativeCoreRegistration,
            layoutCoreRegistration: selectedLayoutCoreRegistration,
            staticInitializationCoreRegistration:
                selectedStaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                selectedLazyInitializationCoreRegistration,
            nativeLoweringAgreement: nativeLoweringAgreement,
            coreExceptionIdentitySelections:
                selectedCoreExceptionIdentitySelections,
            coreContractFailureIdentitySelections:
                selectedCoreContractFailureIdentitySelections,
            coreContractFailureIdentitySelection:
                coreContractFailureIdentitySelection);
        var program = binder.Bind();
        var diagnosticCount = tree.Diagnostics.Length + binder.Diagnostics.Count;
        if (diagnosticCount == 0)
        {
            return new Compilation(tree, [], program);
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>(diagnosticCount);
        diagnostics.AddRange(tree.Diagnostics);
        diagnostics.AddRange(binder.Diagnostics);
        diagnostics.Sort(static (left, right) => {
            var position = left.Span.Start.CompareTo(right.Span.Start);

            return position != 0 ? position : string.CompareOrdinal(left.Code, right.Code);
        });
        var result = diagnostics.ToImmutable();
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var diagnostic in result)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                program = null;
                break;
            }
        }

        return new Compilation(tree, result, program);
    }

    private static NativeCoreRegistrationSet? SelectNativeCoreRegistration(
        CompilationReference? reference, NativeCoreRegistrationSet? configured)
    {
        if (configured is not null && reference?.NativeCoreRegistration is not null)
        {
            throw new ArgumentException(
                "A Core native registration can be supplied only once for a using compilation.",
                nameof(configured));
        }

        var selected = configured ?? reference?.NativeCoreRegistration;
        if (selected is not null && !selected.IsValid())
        {
            if (configured is not null)
            {
                throw new ArgumentException(
                    "The selected Core native registration is invalid.",
                    nameof(configured));
            }

            throw new ArgumentException(
                "The referenced Core native registration is invalid.",
                nameof(reference));
        }
        return selected;
    }

    private static LayoutCoreRegistrationSet? SelectLayoutCoreRegistration(
        CompilationReference? reference, LayoutCoreRegistrationSet? configured)
    {
        if (configured is not null && reference?.LayoutCoreRegistration is not null)
        {
            throw new ArgumentException(
                "A Core layout registration can be supplied only once for a using compilation.",
                nameof(configured));
        }

        var selected = configured ?? reference?.LayoutCoreRegistration;
        if (selected is not null && !selected.IsValid())
        {
            if (configured is not null)
            {
                throw new ArgumentException(
                    "The selected Core layout registration is invalid.",
                    nameof(configured));
            }

            throw new ArgumentException(
                "The referenced Core layout registration is invalid.",
                nameof(reference));
        }
        return selected;
    }

    private static StaticInitializationCoreRegistrationSet?
        SelectStaticInitializationCoreRegistration(
            CompilationReference? reference,
            StaticInitializationCoreRegistrationSet? configured)
    {
        if (configured is not null
            && reference?.StaticInitializationCoreRegistration is not null)
        {
            throw new ArgumentException(
                "A static-initialization Core registration can be supplied only once for a using compilation.",
                nameof(configured));
        }

        var selected = configured
            ?? reference?.StaticInitializationCoreRegistration;
        if (selected is not null && !selected.IsValid())
        {
            if (configured is not null)
            {
                throw new ArgumentException(
                    "The selected static-initialization Core registration is invalid.",
                    nameof(configured));
            }

            throw new ArgumentException(
                "The referenced static-initialization Core registration is invalid.",
                nameof(reference));
        }
        return selected;
    }

    private static LazyInitializationCoreRegistrationSet?
        SelectLazyInitializationCoreRegistration(
            CompilationReference? reference,
            LazyInitializationCoreRegistrationSet? configured)
    {
        if (configured is not null
            && reference?.LazyInitializationCoreRegistration is not null)
        {
            throw new ArgumentException(
                "A LazyInitialization Core registration can be supplied only once for a using compilation.",
                nameof(configured));
        }

        var selected = configured
            ?? reference?.LazyInitializationCoreRegistration;
        if (selected is not null && !selected.IsValid())
        {
            if (configured is not null)
            {
                throw new ArgumentException(
                    "The selected LazyInitialization Core registration is invalid.",
                    nameof(configured));
            }

            throw new ArgumentException(
                "The referenced LazyInitialization Core registration is invalid.",
                nameof(reference));
        }
        return selected;
    }

    private static void ValidateCoreRegistrationComposition(
        NativeCoreRegistrationSet? nativeRegistration,
        LayoutCoreRegistrationSet? layoutRegistration,
        StaticInitializationCoreRegistrationSet?
            staticInitializationRegistration,
        LazyInitializationCoreRegistrationSet?
            lazyInitializationRegistration)
    {
        var moduleBindings = nativeRegistration?.ModuleBindings
            ?? layoutRegistration?.ModuleBindings
            ?? staticInitializationRegistration?.ModuleBindings
            ?? lazyInitializationRegistration?.ModuleBindings;
        if ((layoutRegistration is not null
                && !ReferenceEquals(moduleBindings,
                    layoutRegistration.ModuleBindings))
            || (staticInitializationRegistration is not null
                && !ReferenceEquals(moduleBindings,
                    staticInitializationRegistration.ModuleBindings))
            || (lazyInitializationRegistration is not null
                && !ReferenceEquals(moduleBindings,
                    lazyInitializationRegistration.ModuleBindings)))
        {
            throw new ArgumentException(
                "Selected Core registrations must share one provider binding snapshot.");
        }
    }

    private static ImmutableArray<CoreExceptionIdentitySelection>
        SelectCoreExceptionIdentitySelections(CompilationReference? reference,
            ImmutableArray<CoreExceptionIdentitySelection> configured)
    {
        var referenceSelections = reference?.CoreExceptionIdentitySelections
            ?? [];
        if (referenceSelections.IsDefaultOrEmpty
            && configured.IsDefaultOrEmpty)
        {
            return [];
        }

        var selections = ImmutableArray.CreateBuilder<
            CoreExceptionIdentitySelection>(
            referenceSelections.Length
                + (configured.IsDefault ? 0 : configured.Length));
        if (!referenceSelections.IsDefaultOrEmpty)
        {
            selections.AddRange(referenceSelections);
        }
        if (!configured.IsDefaultOrEmpty)
        {
            selections.AddRange(configured);
        }

        var result = selections.MoveToImmutable();
        if (!CoreExceptionIdentitySelectionContract.IsValidTable(result))
        {
            throw new ArgumentException(
                "Reference and compilation Core exception selections are duplicate or invalid.",
                nameof(configured));
        }

        return result;
    }

    private static ImmutableArray<CoreContractFailureIdentitySelection>
        SelectCoreContractFailureIdentitySelections(
            CompilationReference? reference,
            CoreContractFailureIdentitySelection? configured)
    {
        var referenceSelections
            = reference?.CoreContractFailureIdentitySelections ?? [];
        if (referenceSelections.IsDefaultOrEmpty
            && configured is null)
        {
            return [];
        }

        var selections = ImmutableArray.CreateBuilder<
            CoreContractFailureIdentitySelection>(
            referenceSelections.Length
                + (configured is null ? 0 : 1));
        if (!referenceSelections.IsDefaultOrEmpty)
        {
            selections.AddRange(referenceSelections);
        }
        if (configured is CoreContractFailureIdentitySelection selected)
        {
            selections.Add(selected);
        }

        var result = selections.MoveToImmutable();
        if (!CoreContractFailureIdentitySelectionContract.IsValidTable(
            result))
        {
            throw new ArgumentException(
                "Reference and compilation Core contract-failure selections are duplicate or invalid.",
                nameof(configured));
        }

        return result;
    }

    /// <summary>Binds declarations across source files before resolving names and function bodies.</summary>
    /// <exception cref="ArgumentException">No source trees were supplied.</exception>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, CancellationToken cancellationToken = default) =>
        CreateCore(trees, [], isLibrary: false, cancellationToken);

    /// <summary>Binds source snapshots using explicit source arithmetic and checked-failure runtime options.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, CompilationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        return CreateCore(trees, [], isLibrary: false, cancellationToken,
            runtimePolicy: options.RuntimePolicy, sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds multiple source snapshots under an explicit checked-failure runtime policy.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, BoundRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default) =>
        CreateCore(trees, [], isLibrary: false, cancellationToken, runtimePolicy: runtimePolicy);

    /// <summary>Binds source snapshots with explicit, fixed-size host buffer capabilities shared by the compilation.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, ImmutableArray<HostBuffer> buffers,
        CancellationToken cancellationToken = default) =>
        CreateCore(trees, buffers, isLibrary: false, cancellationToken);

    /// <summary>Binds source snapshots and host buffers using explicit compilation options.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, ImmutableArray<HostBuffer> buffers,
        CompilationOptions options, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        return CreateCore(trees, buffers, isLibrary: false, cancellationToken,
            runtimePolicy: options.RuntimePolicy, sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds source snapshots and shared host buffers under an explicit checked-failure runtime policy.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, ImmutableArray<HostBuffer> buffers,
        BoundRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        CreateCore(trees, buffers, isLibrary: false, cancellationToken, runtimePolicy: runtimePolicy);

    /// <summary>Binds host buffers under the selected checked-failure runtime policy.</summary>
    public static Compilation Create(SyntaxTree tree, ImmutableArray<HostBuffer> buffers, BoundRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default) =>
        CreateCore([tree], buffers, isLibrary: false, cancellationToken, runtimePolicy: runtimePolicy);

    /// <summary>Binds library definitions across source snapshots, without requiring a process entry point.</summary>
    public static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees, CancellationToken cancellationToken = default) =>
        CreateCore(trees, [], isLibrary: true, cancellationToken);

    /// <summary>Binds library source snapshots using explicit compilation options.</summary>
    public static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees, CompilationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        return CreateCore(trees, [], isLibrary: true, cancellationToken,
            runtimePolicy: options.RuntimePolicy, sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds multiple library snapshots under an explicit checked-failure runtime policy.</summary>
    public static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees, BoundRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default) =>
        CreateCore(trees, [], isLibrary: true, cancellationToken, runtimePolicy: runtimePolicy);

    /// <summary>Binds library definitions against explicit host buffer capabilities under the selected runtime policy.</summary>
    public static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees, ImmutableArray<HostBuffer> buffers,
        BoundRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        CreateCore(trees, buffers, isLibrary: true, cancellationToken, runtimePolicy: runtimePolicy);

    /// <summary>Binds library source snapshots and host buffers using explicit compilation options.</summary>
    public static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees, ImmutableArray<HostBuffer> buffers,
        CompilationOptions options, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        return CreateCore(trees, buffers, isLibrary: true, cancellationToken,
            runtimePolicy: options.RuntimePolicy, sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds the compiler-owned primitive declarations and APIs of a Core library without a standard-library reference.</summary>
    public static Compilation CreateCoreLibrary(IEnumerable<SyntaxTree> trees,
        CancellationToken cancellationToken = default) =>
        CreateCore(trees, [], isLibrary: true, cancellationToken, coreLibrary: true);

    /// <summary>Binds the compiler-owned primitive declarations and APIs of a Core library without a standard-library reference.</summary>
    public static Compilation CreateCoreLibrary(IEnumerable<SyntaxTree> trees, BoundRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default) =>
        CreateCore(trees, [], isLibrary: true, cancellationToken, runtimePolicy: runtimePolicy,
            coreLibrary: true);

    /// <summary>Binds compiler-owned Core declarations using explicit compilation options.</summary>
    public static Compilation CreateCoreLibrary(IEnumerable<SyntaxTree> trees, CompilationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        return CreateCore(trees, [], isLibrary: true, cancellationToken,
            runtimePolicy: options.RuntimePolicy, coreLibrary: true,
            sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds multiple library source snapshots against validated external declarations.</summary>
    public static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees, CompilationReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(trees, [], isLibrary: true, cancellationToken, reference);
    }

    /// <summary>Binds multiple library source snapshots against a reference under the selected runtime policy.</summary>
    public static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees, CompilationReference reference,
        BoundRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(trees, [], isLibrary: true, cancellationToken, reference, runtimePolicy);
    }

    /// <summary>Binds library snapshots against a reference using explicit compilation options.</summary>
    public static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees, CompilationReference reference,
        CompilationOptions options, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(trees, [], isLibrary: true, cancellationToken, reference,
            options.RuntimePolicy, sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    /// <summary>Binds consumer source snapshots against one validated module reference.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, CompilationReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(trees, [], isLibrary: false, cancellationToken, reference);
    }

    /// <summary>Binds multiple consumer snapshots against the selected reference runtime policy.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, CompilationReference reference, BoundRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(trees, [], isLibrary: false, cancellationToken, reference, runtimePolicy);
    }

    /// <summary>Binds consumer snapshots against a reference using explicit compilation options.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, CompilationReference reference,
        CompilationOptions options, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(trees, [], isLibrary: false, cancellationToken, reference,
            options.RuntimePolicy, sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections:
                options.CoreExceptionIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
    }

    private static Compilation CreateCore(IEnumerable<SyntaxTree> trees, ImmutableArray<HostBuffer> buffers, bool isLibrary,
        CancellationToken cancellationToken, CompilationReference? reference = null,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast, bool coreLibrary = false,
        SourceArithmeticProfile sourceArithmeticProfile = SourceArithmeticProfile.Development,
        NativeCoreRegistrationSet? nativeCoreRegistration = null,
        LayoutCoreRegistrationSet? layoutCoreRegistration = null,
        StaticInitializationCoreRegistrationSet?
            staticInitializationCoreRegistration = null,
        LazyInitializationCoreRegistrationSet?
            lazyInitializationCoreRegistration = null,
        string? nativeLoweringAgreement = null,
        ImmutableArray<CoreExceptionIdentitySelection> coreExceptionIdentitySelections = default,
        CoreContractFailureIdentitySelection?
            coreContractFailureIdentitySelection = null)
    {
        ArgumentNullException.ThrowIfNull(trees);
        cancellationToken.ThrowIfCancellationRequested();
        var selectedNativeCoreRegistration = SelectNativeCoreRegistration(
            reference, nativeCoreRegistration);
        var selectedLayoutCoreRegistration = SelectLayoutCoreRegistration(
            reference, layoutCoreRegistration);
        var selectedStaticInitializationCoreRegistration
            = SelectStaticInitializationCoreRegistration(reference,
                staticInitializationCoreRegistration);
        var selectedLazyInitializationCoreRegistration
            = SelectLazyInitializationCoreRegistration(reference,
                lazyInitializationCoreRegistration);
        ValidateCoreRegistrationComposition(selectedNativeCoreRegistration,
            selectedLayoutCoreRegistration,
            selectedStaticInitializationCoreRegistration,
            selectedLazyInitializationCoreRegistration);
        var selectedCoreExceptionIdentitySelections
            = SelectCoreExceptionIdentitySelections(reference,
                coreExceptionIdentitySelections);
        var selectedCoreContractFailureIdentitySelections
            = SelectCoreContractFailureIdentitySelections(reference,
                coreContractFailureIdentitySelection);
        var snapshots = ImmutableArray.CreateBuilder<SyntaxTree>(
            trees is ICollection<SyntaxTree> collection ? collection.Count : 0);
        ImmutableArray<Diagnostic>.Builder? diagnostics = null;
        ImmutableArray<SourceDiagnostic>.Builder? sourceDiagnostics = null;
        foreach (var tree in trees)
        {
            ArgumentNullException.ThrowIfNull(tree);
            snapshots.Add(tree);
            if (!tree.Diagnostics.IsEmpty)
            {
                diagnostics ??= ImmutableArray.CreateBuilder<Diagnostic>(tree.Diagnostics.Length);
                diagnostics.AddRange(tree.Diagnostics);
                sourceDiagnostics ??= ImmutableArray.CreateBuilder<SourceDiagnostic>(tree.Diagnostics.Length);
                foreach (var diagnostic in tree.Diagnostics)
                {
                    sourceDiagnostics.Add(new SourceDiagnostic(tree, diagnostic));
                }
            }
        }
        if (snapshots.Count == 0)
        {
            throw new ArgumentException("At least one source tree is required.", nameof(trees));
        }
        var snapshotArray = snapshots.ToImmutable();
        if (diagnostics is not null)
        {
            var sourceDiagnosticBuilder = sourceDiagnostics
                ?? throw new InvalidOperationException("Syntax diagnostics require source provenance.");
            return new Compilation(snapshotArray[0], diagnostics.ToImmutable(), null,
                snapshotArray, sourceDiagnosticBuilder.ToImmutable());
        }
        var binder = new Binder(snapshotArray, buffers, cancellationToken, isLibrary, reference,
            runtimePolicy, coreLibrary, sourceArithmeticProfile,
            selectedNativeCoreRegistration, selectedLayoutCoreRegistration,
            nativeLoweringAgreement,
            selectedCoreExceptionIdentitySelections,
            selectedStaticInitializationCoreRegistration,
            selectedCoreContractFailureIdentitySelections,
            coreContractFailureIdentitySelection,
            lazyInitializationCoreRegistration:
                selectedLazyInitializationCoreRegistration);
        var program = binder.Bind();
        if (binder.Diagnostics.Count != 0)
        {
            diagnostics = ImmutableArray.CreateBuilder<Diagnostic>(binder.Diagnostics.Count);
            diagnostics.AddRange(binder.Diagnostics);
        }
        if (binder.SourceDiagnostics.Count != 0)
        {
            sourceDiagnostics = ImmutableArray.CreateBuilder<SourceDiagnostic>(binder.SourceDiagnostics.Count);
            sourceDiagnostics.AddRange(binder.SourceDiagnostics);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var diagnosticResult = diagnostics?.MoveToImmutable() ?? [];
        var sourceDiagnosticResult = sourceDiagnostics?.MoveToImmutable() ?? [];
        return new Compilation(snapshotArray[0], diagnosticResult, diagnosticResult.IsEmpty ? program : null,
            snapshotArray, sourceDiagnosticResult);
    }

    /// <summary>Binds one source snapshot with explicit, fixed-size host buffer capabilities.</summary>
    public static Compilation Create(SyntaxTree tree, ImmutableArray<HostBuffer> buffers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();
        if (tree.HasErrors)
        {
            return new Compilation(tree, tree.Diagnostics, null);
        }
        var binder = new Binder([tree], buffers, cancellationToken);
        var program = binder.Bind();
        var diagnostics = tree.Diagnostics.AddRange(binder.Diagnostics);
        return new Compilation(tree, diagnostics, diagnostics.IsEmpty ? program : null);
    }

    /// <summary>Binds a source snapshot with host buffers using explicit compilation options.</summary>
    public static Compilation Create(SyntaxTree tree, ImmutableArray<HostBuffer> buffers,
        CompilationOptions options, CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();
        if (tree.HasErrors)
        {
            return new Compilation(tree, tree.Diagnostics, null);
        }
        var coreExceptionIdentitySelections
            = SelectCoreExceptionIdentitySelections(null,
                options.CoreExceptionIdentitySelections);
        var coreContractFailureIdentitySelections
            = SelectCoreContractFailureIdentitySelections(null,
                options.CoreContractFailureIdentitySelection);
        var binder = new Binder([tree], buffers, cancellationToken, runtimePolicy: options.RuntimePolicy,
            sourceArithmeticProfile: options.SourceArithmeticProfile,
            nativeCoreRegistration: options.NativeCoreRegistration,
            layoutCoreRegistration: options.LayoutCoreRegistration,
            staticInitializationCoreRegistration:
                options.StaticInitializationCoreRegistration,
            lazyInitializationCoreRegistration:
                options.LazyInitializationCoreRegistration,
            nativeLoweringAgreement: options.NativeLoweringAgreement,
            coreExceptionIdentitySelections: coreExceptionIdentitySelections,
            coreContractFailureIdentitySelections:
                coreContractFailureIdentitySelections,
            coreContractFailureIdentitySelection:
                options.CoreContractFailureIdentitySelection);
        var program = binder.Bind();
        var diagnostics = tree.Diagnostics.AddRange(binder.Diagnostics);
        return new Compilation(tree, diagnostics, diagnostics.IsEmpty ? program : null);
    }
}
