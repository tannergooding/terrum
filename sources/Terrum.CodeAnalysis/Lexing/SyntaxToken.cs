// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using System.Collections.Immutable;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

public sealed class SyntaxToken
{
    internal SyntaxToken(TokenKind kind, string text, TextSpan span, ImmutableArray<SyntaxTrivia> leadingTrivia, bool isMissing = false)
    {
        Kind = kind;
        Text = text;
        Span = span;
        LeadingTrivia = leadingTrivia;
        IsMissing = isMissing;
        int start = leadingTrivia.IsDefaultOrEmpty ? span.Start : leadingTrivia[0].Span.Start;
        FullSpan = new TextSpan(start, span.End - start);
    }

    internal static SyntaxToken Missing(TokenKind kind, int position)
        => new(kind, string.Empty, new TextSpan(position, 0), ImmutableArray<SyntaxTrivia>.Empty, true);

    public TokenKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }
    public TextSpan FullSpan { get; }
    public ImmutableArray<SyntaxTrivia> LeadingTrivia { get; }
    public bool IsMissing { get; }
}
