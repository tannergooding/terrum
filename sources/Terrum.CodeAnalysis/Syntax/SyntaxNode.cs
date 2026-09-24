// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Syntax;

/// <summary>
/// A lossless syntax node. Children include punctuation and recovery nodes in source order.
/// Nodes belong to one snapshot; neither parent pointers nor semantic state are stored here.
/// </summary>
public sealed class SyntaxNode : SyntaxElement
{
    internal SyntaxNode(SyntaxKind kind, ImmutableArray<SyntaxElement> children, int position)
    {
        Kind = kind;
        Children = children;
        var start = children.IsEmpty ? position : children[0].Span.Start;
        var end = start;
        var fullStart = children.IsEmpty ? position : children[0].FullSpan.Start;
        var fullEnd = fullStart;

        foreach (var child in children)
        {
            // Missing tokens can precede a real token whose leading trivia starts earlier.
            start = Math.Min(start, child.Span.Start);
            end = Math.Max(end, child.Span.End);
            fullStart = Math.Min(fullStart, child.FullSpan.Start);
            fullEnd = Math.Max(fullEnd, child.FullSpan.End);
        }

        Span = new TextSpan(start, end - start);
        FullSpan = new TextSpan(fullStart, fullEnd - fullStart);
    }

    /// <summary>Gets the syntactic construct represented by this node.</summary>
    public SyntaxKind Kind { get; }

    /// <summary>Gets source-ordered child nodes and token leaves.</summary>
    public ImmutableArray<SyntaxElement> Children { get; }

    /// <inheritdoc/>
    public override TextSpan Span { get; }

    /// <inheritdoc/>
    public override TextSpan FullSpan { get; }

    /// <summary>Traverses descendants in pre-order without using the call stack.</summary>
    /// <param name="includeSelf">Whether to yield this node before its descendants.</param>
    /// <returns>The descendant nodes in source order.</returns>
    public IEnumerable<SyntaxNode> DescendantNodes(bool includeSelf = false)
    {
        foreach (var element in DescendantElements(includeSelf))
        {
            if (element is SyntaxNode node)
            {
                yield return node;
            }
        }
    }

    /// <summary>Traverses token leaves in source order, including parser-inserted missing tokens.</summary>
    /// <returns>The tokens contained in this subtree.</returns>
    public IEnumerable<SyntaxToken> DescendantTokens()
    {
        foreach (var element in DescendantElements(false))
        {
            if (element is SyntaxTokenElement leaf)
            {
                yield return leaf.Token;
            }
        }
    }

    /// <summary>Reconstructs this subtree from its leaves, not from a saved source string.</summary>
    /// <returns>The concatenation of each leaf's leading trivia and token text.</returns>
    public string ToFullString()
    {
        var builder = new StringBuilder();

        foreach (var token in DescendantTokens())
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                _ = builder.Append(trivia.Text);
            }

            _ = builder.Append(token.Text);
        }

        return builder.ToString();
    }

    private IEnumerable<SyntaxElement> DescendantElements(bool includeSelf)
    {
        var stack = new Stack<SyntaxElement>();
        stack.Push(this);

        while (stack.TryPop(out var element))
        {
            if (includeSelf || !ReferenceEquals(element, this))
            {
                yield return element;
            }

            if (element is SyntaxNode node)
            {
                for (var index = node.Children.Length - 1; index >= 0; index--)
                {
                    stack.Push(node.Children[index]);
                }
            }
        }
    }
}
