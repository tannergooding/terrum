// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>
/// The semantic result for a single syntax snapshot. The executable profile supports only
/// top-level functions, int32, bool, and void; directives and other declarations are rejected.
/// </summary>
public sealed class Compilation
{
    /// <summary>The version of the supported executable semantic profile.</summary>
    public const string ProfileVersion = "native-1";

    private Compilation(SyntaxTree syntaxTree, ImmutableArray<Diagnostic> diagnostics, BoundProgram? program)
    {
        SyntaxTree = syntaxTree;
        Diagnostics = diagnostics;
        Program = program;
    }

    /// <summary>Gets the source snapshot used to create this compilation.</summary>
    public SyntaxTree SyntaxTree { get; }

    /// <summary>Gets syntax and semantic diagnostics in source order, then ordinal code order.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets the executable program, or null if any error occurred.</summary>
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
    public static Compilation Create(SyntaxTree tree, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();

        // Recovery nodes do not have the structural guarantees required for binding.
        if (tree.HasErrors)
        {
            return new Compilation(tree, tree.Diagnostics, null);
        }

        var binder = new Binder(tree, cancellationToken);
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
}
