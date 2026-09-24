// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Syntax;

/// <summary>An immutable, source-owning parse snapshot shared by all tooling clients.</summary>
public sealed class SyntaxTree
{
    /// <summary>Identifies the syntax contract used by this parser.</summary>
    public const string LanguageVersion = "syntax-2";

    private SyntaxTree(SourceText source, LexResult lexResult, SyntaxNode root, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        Source = source;
        Tokens = lexResult.Tokens;
        Root = root;
        Diagnostics = diagnostics;
        ContentId = ComputeContentId(source.Text, cancellationToken);
    }

    /// <summary>Gets the source snapshot represented by this tree.</summary>
    public SourceText Source { get; }

    /// <summary>The lexical stream, including EOF; parser-inserted missing tokens occur only in Root.</summary>
    public ImmutableArray<SyntaxToken> Tokens { get; }

    /// <summary>Gets the parsed compilation-unit node, including any recovery syntax.</summary>
    public SyntaxNode Root { get; }

    /// <summary>Lexical and parser diagnostics ordered by source position, then ordinal code.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>SHA-256 of exact UTF-16LE code units, including malformed surrogates. Not a semantic identity.</summary>
    public string ContentId { get; }

    /// <summary>Gets whether any lexical or parser diagnostic has error severity.</summary>
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

    /// <summary>Lexes and parses source text into an immutable snapshot.</summary>
    /// <param name="text">The exact source content.</param>
    /// <param name="path">The associated path, or an empty string for unnamed source.</param>
    /// <param name="cancellationToken">A token to cancel parsing or content hashing.</param>
    /// <returns>The source-owning parse snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="path"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static SyntaxTree Parse(string text, string path = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Parse(SourceText.From(text, path), cancellationToken);
    }

    /// <summary>Lexes and parses an existing source snapshot.</summary>
    /// <param name="source">The source snapshot to parse.</param>
    /// <param name="cancellationToken">A token to cancel parsing or content hashing.</param>
    /// <returns>The parsed snapshot, retaining <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static SyntaxTree Parse(SourceText source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var lexResult = Lexer.Lex(source, cancellationToken);
        var parser = new Parser(lexResult.Tokens, cancellationToken);
        var root = parser.ParseCompilationUnit();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        diagnostics.AddRange(lexResult.Diagnostics);
        diagnostics.AddRange(parser.Diagnostics);
        diagnostics.Sort(static (left, right) => {
            var position = left.Span.Start.CompareTo(right.Span.Start);

            return position != 0 ? position : string.CompareOrdinal(left.Code, right.Code);
        });
        cancellationToken.ThrowIfCancellationRequested();

        return new SyntaxTree(source, lexResult, root, diagnostics.ToImmutable(), cancellationToken);
    }

    /// <summary>
    /// Validates a complete edit batch against this snapshot and returns a fully reparsed snapshot.
    /// Changes must be ordered and nonoverlapping; multiple insertions at one position are rejected.
    /// No source file is written and no mutable workspace is updated.
    /// </summary>
    /// <param name="expectedContentId">The content identifier expected for this original snapshot.</param>
    /// <param name="changes">Edits in original-source coordinates, ordered by start offset.</param>
    /// <param name="cancellationToken">A token to cancel validation, parsing, or content hashing.</param>
    /// <returns>A fully reparsed snapshot with all changes applied; this tree remains unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expectedContentId"/>, <paramref name="changes"/>, or an edit is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="expectedContentId"/> does not match this snapshot.</exception>
    /// <exception cref="ArgumentException">Edits are out of order, overlap, repeat a start offset, or extend past the source.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public SyntaxTree WithChanges(string expectedContentId, IReadOnlyList<TextChange> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedContentId);
        ArgumentNullException.ThrowIfNull(changes);

        if (!string.Equals(expectedContentId, ContentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The edits target a different source snapshot.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var builder = new StringBuilder();
        var position = 0;
        var previousStart = -1;

        foreach (var change in changes)
        {
            ArgumentNullException.ThrowIfNull(change);
            cancellationToken.ThrowIfCancellationRequested();

            if (change.Span.Start < position || change.Span.Start == previousStart || change.Span.End > Source.Length)
            {
                throw new ArgumentException("Changes must be ordered, nonoverlapping, and within the original source.", nameof(changes));
            }

            _ = builder.Append(Source.Text, position, change.Span.Start - position);
            _ = builder.Append(change.NewText);
            previousStart = change.Span.Start;
            position = change.Span.End;
        }

        _ = builder.Append(Source.Text, position, Source.Length - position);

        return Parse(builder.ToString(), Source.Path, cancellationToken);
    }

    private static string ComputeContentId(string text, CancellationToken cancellationToken)
    {
        // Encoding.Unicode replaces malformed surrogates. Hash the actual code units instead.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[1024];
        var position = 0;

        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length / 2, text.Length - position);

            for (var index = 0; index < count; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(index * 2, 2), text[position + index]);
            }

            hash.AppendData(buffer[..(count * 2)]);
            position += count;
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
