// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Syntax;

/// <summary>An immutable node or token in a source-ordered concrete syntax tree.</summary>
public abstract class SyntaxElement
{
    private protected SyntaxElement() { }

    /// <summary>Gets the half-open UTF-16 range excluding outer trivia.</summary>
    public abstract TextSpan Span { get; }

    /// <summary>Gets the half-open UTF-16 range including leading trivia.</summary>
    public abstract TextSpan FullSpan { get; }
}

/// <summary>A token leaf, including its leading trivia and missing-token state.</summary>
public sealed class SyntaxTokenElement : SyntaxElement
{
    internal SyntaxTokenElement(SyntaxToken token)
    {
        Token = token;
    }

    /// <summary>Gets the token represented by this leaf.</summary>
    public SyntaxToken Token { get; }

    /// <inheritdoc/>
    public override TextSpan Span => Token.Span;

    /// <inheritdoc/>
    public override TextSpan FullSpan => Token.FullSpan;
}
