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
    public const string ProfileVersion = "native-6";

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

    /// <summary>Binds and verifies library definitions without requiring or selecting a Main entry point.</summary>
    /// <param name="tree">The source snapshot containing the definitions.</param>
    /// <param name="cancellationToken">Cancellation for semantic traversal.</param>
    /// <returns>A compilation whose successful program has EntryPoint -1 and must be linked before execution.</returns>
    public static Compilation CreateLibrary(SyntaxTree tree, CancellationToken cancellationToken = default) =>
        CreateCore(tree, isLibrary: true, cancellationToken);

    /// <summary>Binds consumer source against validated module declarations, without reading producer source or binding its bodies.</summary>
    public static Compilation Create(SyntaxTree tree, CompilationReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(tree, isLibrary: false, cancellationToken, reference);
    }

    private static Compilation CreateCore(SyntaxTree tree, bool isLibrary, CancellationToken cancellationToken,
        CompilationReference? reference = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();

        // Recovery nodes do not have the structural guarantees required for binding.
        if (tree.HasErrors)
        {
            return new Compilation(tree, tree.Diagnostics, null);
        }

        var binder = new Binder(tree, isLibrary, cancellationToken, reference);
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
        CreateCore(trees, isLibrary: false, cancellationToken);

    /// <summary>Binds library definitions across source snapshots, without requiring a process entry point.</summary>
    public static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees, CancellationToken cancellationToken = default) =>
        CreateCore(trees, isLibrary: true, cancellationToken);

    /// <summary>Binds consumer source snapshots against one validated module reference.</summary>
    public static Compilation Create(IEnumerable<SyntaxTree> trees, CompilationReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return CreateCore(trees, isLibrary: false, cancellationToken, reference);
    }

    private static Compilation CreateCore(IEnumerable<SyntaxTree> trees, bool isLibrary, CancellationToken cancellationToken,
        CompilationReference? reference = null)
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
        var binder = new Binder(snapshots.ToImmutable(), [], cancellationToken, isLibrary, reference);
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
