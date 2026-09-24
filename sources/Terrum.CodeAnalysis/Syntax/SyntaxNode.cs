// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

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
        int start = children.IsEmpty ? position : children[0].Span.Start;
        int end = start;
        int fullStart = children.IsEmpty ? position : children[0].FullSpan.Start;
        int fullEnd = fullStart;

        foreach (SyntaxElement child in children)
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

    public SyntaxKind Kind { get; }

    public ImmutableArray<SyntaxElement> Children { get; }

    public override TextSpan Span { get; }

    public override TextSpan FullSpan { get; }

    /// <summary>Traverses descendants in pre-order without using the call stack.</summary>
    public IEnumerable<SyntaxNode> DescendantNodes(bool includeSelf = false)
    {
        foreach (SyntaxElement element in DescendantElements(includeSelf))
        {
            if (element is SyntaxNode node)
            {
                yield return node;
            }
        }
    }

    public IEnumerable<SyntaxToken> DescendantTokens()
    {
        foreach (SyntaxElement element in DescendantElements(false))
        {
            if (element is SyntaxTokenElement leaf)
            {
                yield return leaf.Token;
            }
        }
    }

    /// <summary>Reconstructs this subtree from its leaves, not from a saved source string.</summary>
    public string ToFullString()
    {
        var builder = new StringBuilder();

        foreach (SyntaxToken token in DescendantTokens())
        {
            foreach (SyntaxTrivia trivia in token.LeadingTrivia)
            {
                builder.Append(trivia.Text);
            }

            builder.Append(token.Text);
        }

        return builder.ToString();
    }

    private IEnumerable<SyntaxElement> DescendantElements(bool includeSelf)
    {
        var stack = new Stack<SyntaxElement>();
        stack.Push(this);

        while (stack.TryPop(out SyntaxElement? element))
        {
            if (includeSelf || !ReferenceEquals(element, this))
            {
                yield return element;
            }

            if (element is SyntaxNode node)
            {
                for (int index = node.Children.Length - 1; index >= 0; index--)
                {
                    stack.Push(node.Children[index]);
                }
            }
        }
    }
}
