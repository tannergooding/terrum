// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Syntax;

/// <summary>An immutable node or token in a source-ordered concrete syntax tree.</summary>
public abstract class SyntaxElement
{
    private protected SyntaxElement() { }

    public abstract TextSpan Span { get; }

    public abstract TextSpan FullSpan { get; }
}

/// <summary>A token leaf, including its leading trivia and missing-token state.</summary>
public sealed class SyntaxTokenElement : SyntaxElement
{
    internal SyntaxTokenElement(SyntaxToken token)
    {
        Token = token;
    }

    public SyntaxToken Token { get; }

    public override TextSpan Span => Token.Span;

    public override TextSpan FullSpan => Token.FullSpan;
}
