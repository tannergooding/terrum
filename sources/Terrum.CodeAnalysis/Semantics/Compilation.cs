// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>A compiler diagnostic paired with the source snapshot that owns its UTF-16 span.</summary>
public sealed record SourceDiagnostic(SyntaxTree SyntaxTree, Diagnostic Diagnostic);

/// <summary>
/// The semantic result for one or more syntax snapshots. The executable profile supports only
/// checked scalar values and compact POD structs with structured control flow.
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

    internal static bool HasCompositeUnique(ImmutableArray<BoundStruct> structures)
    {
        foreach (var structure in structures)
        {
            if (!structure.IsUnique)
            {
                continue;
            }
            foreach (var field in structure.Fields)
            {
                var index = (int)field.Type - 100;
                if (index >= structures.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(structures), "An aggregate field has an unknown type.");
                }
                if (index >= 0)
                {
                    return true;
                }
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
            var index = (int)type - 100;
            if (index >= structures.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(function), "Function parameter references an unknown structure.");
            }
            if (index >= 0 && structures[index].IsUnique
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
        var unique = false;
        foreach (var structure in program.Structures)
        {
            unique |= structure.IsUnique;
        }
        var composite = HasCompositeUnique(program.Structures);
        foreach (var function in program.Functions)
        {
            if (HasOwnershipTransfer(function, program.Structures))
            {
                return GetNativeProfileVersion(program.RuntimePolicy, unique,
                    ownershipTransfer: true, compositeUnique: composite);
            }
        }
        return GetNativeProfileVersion(program.RuntimePolicy, unique,
            ownershipTransfer: false, compositeUnique: composite);
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
            var mapped = ImmutableArray.CreateBuilder<SourceDiagnostic>(diagnostics.Length);
            foreach (var diagnostic in diagnostics)
            {
                mapped.Add(new SourceDiagnostic(syntaxTree, diagnostic));
            }
            sourceDiagnostics = mapped.ToImmutable();
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
            if (Program is not BoundProgram program)
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

    /// <summary>Binds a source snapshot under an explicit checked-failure runtime policy.</summary>
    public static Compilation Create(SyntaxTree tree, BoundRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        CreateCore([tree], [], isLibrary: false, cancellationToken, runtimePolicy: runtimePolicy);

    /// <summary>Binds a library snapshot under an explicit checked-failure runtime policy.</summary>
    public static Compilation CreateLibrary(SyntaxTree tree, BoundRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        CreateCore([tree], [], isLibrary: true, cancellationToken, runtimePolicy: runtimePolicy);

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

    private static Compilation CreateCore(SyntaxTree tree, bool isLibrary, CancellationToken cancellationToken,
        CompilationReference? reference = null, BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();

        // Recovery nodes do not have the structural guarantees required for binding.
        if (tree.HasErrors)
        {
            return new Compilation(tree, tree.Diagnostics, null);
        }

        var binder = new Binder([tree], [], cancellationToken, isLibrary, reference, runtimePolicy);
        var program = binder.Bind();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
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

    /// <summary>Binds declarations across source files before resolving names and function bodies.</summary>
    /// <exception cref="ArgumentException">No source trees were supplied.</exception>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, CancellationToken cancellationToken = default) =>
        CreateCore(trees, [], isLibrary: false, cancellationToken);

    /// <summary>Binds multiple source snapshots under an explicit checked-failure runtime policy.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, BoundRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default) =>
        CreateCore(trees, [], isLibrary: false, cancellationToken, runtimePolicy: runtimePolicy);

    /// <summary>Binds source snapshots with explicit, fixed-size host buffer capabilities shared by the compilation.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, ImmutableArray<HostBuffer> buffers,
        CancellationToken cancellationToken = default) =>
        CreateCore(trees, buffers, isLibrary: false, cancellationToken);

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

    /// <summary>Binds multiple library snapshots under an explicit checked-failure runtime policy.</summary>
    public static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees, BoundRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default) =>
        CreateCore(trees, [], isLibrary: true, cancellationToken, runtimePolicy: runtimePolicy);

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
    private static Compilation CreateCore(IEnumerable<SyntaxTree> trees, ImmutableArray<HostBuffer> buffers, bool isLibrary,
        CancellationToken cancellationToken, CompilationReference? reference = null,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast)
    {
        ArgumentNullException.ThrowIfNull(trees);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshots = ImmutableArray.CreateBuilder<SyntaxTree>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var sourceDiagnostics = ImmutableArray.CreateBuilder<SourceDiagnostic>();
        foreach (var tree in trees)
        {
            ArgumentNullException.ThrowIfNull(tree);
            snapshots.Add(tree);
            diagnostics.AddRange(tree.Diagnostics);
            foreach (var diagnostic in tree.Diagnostics)
            {
                sourceDiagnostics.Add(new SourceDiagnostic(tree, diagnostic));
            }
        }
        if (snapshots.Count == 0)
        {
            throw new ArgumentException("At least one source tree is required.", nameof(trees));
        }
        if (diagnostics.Count != 0)
        {
            return new Compilation(snapshots[0], diagnostics.ToImmutable(), null,
                snapshots.ToImmutable(), sourceDiagnostics.ToImmutable());
        }
        var binder = new Binder(snapshots.ToImmutable(), buffers, cancellationToken, isLibrary, reference, runtimePolicy);
        var program = binder.Bind();
        diagnostics.AddRange(binder.Diagnostics);
        sourceDiagnostics.AddRange(binder.SourceDiagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        return new Compilation(snapshots[0], diagnostics.ToImmutable(), diagnostics.Count == 0 ? program : null,
            snapshots.ToImmutable(), sourceDiagnostics.ToImmutable());
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
}
