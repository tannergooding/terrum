// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>A lexical token with its preceding trivia, or a parser-inserted missing token.</summary>
public sealed class SyntaxToken
{
    internal SyntaxToken(TokenKind kind, string text, TextSpan span, ImmutableArray<SyntaxTrivia> leadingTrivia, bool isMissing = false)
    {
        Kind = kind;
        Text = text;
        Span = span;
        LeadingTrivia = leadingTrivia;
        IsMissing = isMissing;
        var start = leadingTrivia.IsDefaultOrEmpty ? span.Start : leadingTrivia[0].Span.Start;
        FullSpan = new TextSpan(start, span.End - start);
    }

    internal static SyntaxToken Missing(TokenKind kind, int position)
        => new(kind, string.Empty, new TextSpan(position, 0), [], true);

    /// <summary>Gets the token's lexical kind.</summary>
    public TokenKind Kind { get; }
    /// <summary>Gets the exact token text; missing tokens have empty text.</summary>
    public string Text { get; }
    /// <summary>Gets the half-open UTF-16 range of the token text, excluding leading trivia.</summary>
    public TextSpan Span { get; }
    /// <summary>Gets the range spanning leading trivia and token text.</summary>
    public TextSpan FullSpan { get; }
    /// <summary>Gets the trivia immediately preceding the token in source order.</summary>
    public ImmutableArray<SyntaxTrivia> LeadingTrivia { get; }
    /// <summary>Gets whether the parser inserted this zero-width token during recovery.</summary>
    public bool IsMissing { get; }
}
