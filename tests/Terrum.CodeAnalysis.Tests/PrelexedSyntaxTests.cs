// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class PrelexedSyntaxTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" // trailing\r\n")]
    [InlineData("void F() { return; }")]
    [InlineData("void F() { return 1 } // trailing \uD800")]
    [InlineData("void F() { @ return; } /* unterminated")]
    public void SuppliedLexingMatchesOrdinaryParsingAndRecovery(string text)
    {
        var lexResult = Lexer.Lex(SourceText.From(text, "fixture.terrum"));
        var supplied = SyntaxTree.Parse(lexResult);
        var ordinary = SyntaxTree.Parse(lexResult.Source);

        Assert.Same(lexResult.Source, supplied.Source);
        Assert.Equal(lexResult.Tokens.Length, supplied.Tokens.Length);
        for (var index = 0; index < supplied.Tokens.Length; index++)
        {
            Assert.Same(lexResult.Tokens[index], supplied.Tokens[index]);
        }

        Assert.Equal(text, supplied.Root.ToFullString());
        Assert.Equal(ordinary.Root.ToFullString(), supplied.Root.ToFullString());
        Assert.Equal(ordinary.ContentId, supplied.ContentId);
        Assert.Equal<Diagnostic>(ordinary.Diagnostics, supplied.Diagnostics);
        Assert.Equal<(SyntaxKind, TextSpan, TextSpan)>(
            ordinary.Root.DescendantNodes(includeSelf: true).Select(node => (node.Kind, node.Span, node.FullSpan)),
            supplied.Root.DescendantNodes(includeSelf: true).Select(node => (node.Kind, node.Span, node.FullSpan)));
        Assert.Equal<(TokenKind, TextSpan, bool)>(
            ordinary.Root.DescendantTokens().Select(token => (token.Kind, token.Span, token.IsMissing)),
            supplied.Root.DescendantTokens().Select(token => (token.Kind, token.Span, token.IsMissing)));
    }

    [Fact]
    public void SuppliedClassificationIsAcceptedWithoutRelexing()
    {
        var lexResult = Lexer.Lex(SourceText.From("void F() { return true; }"));
        var index = lexResult.Tokens.IndexOf(lexResult.Tokens.Single(token => token.Kind == TokenKind.TrueKeyword));
        var original = lexResult.Tokens[index];
        var substituted = ForgeToken(TokenKind.FalseKeyword, original.Text, original.Span, original.LeadingTrivia);
        var tree = SyntaxTree.Parse(lexResult with { Tokens = lexResult.Tokens.SetItem(index, substituted) });

        Assert.Same(substituted, tree.Tokens[index]);
        Assert.Contains(tree.Root.DescendantTokens(), token => ReferenceEquals(substituted, token));
        Assert.Equal(lexResult.Source.Text, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("null-source")]
    [InlineData("default-tokens")]
    [InlineData("empty-tokens")]
    [InlineData("null-token")]
    [InlineData("missing-eof")]
    [InlineData("extra-eof")]
    [InlineData("early-eof")]
    [InlineData("missing-token")]
    [InlineData("invalid-kind")]
    [InlineData("token-text")]
    [InlineData("token-gap")]
    [InlineData("default-trivia")]
    [InlineData("null-trivia")]
    [InlineData("invalid-trivia-kind")]
    [InlineData("trivia-text")]
    [InlineData("trivia-gap")]
    [InlineData("zero-trivia")]
    [InlineData("eof-position")]
    [InlineData("default-diagnostics")]
    [InlineData("null-diagnostic")]
    [InlineData("diagnostic-code")]
    [InlineData("null-code")]
    [InlineData("diagnostic-message")]
    [InlineData("null-message")]
    [InlineData("diagnostic-severity")]
    [InlineData("diagnostic-span")]
    public void RejectsMalformedExternalLexicalResults(string caseName)
    {
        var lexResult = Lexer.Lex(SourceText.From(" a"));
        var token = lexResult.Tokens[0];
        var diagnostic = new Diagnostic("TRM0001", DiagnosticSeverity.Error, "bad", new TextSpan(0, 1));
        var malformed = caseName switch {
            "null-source" => lexResult with { Source = null! },
            "default-tokens" => lexResult with { Tokens = default },
            "empty-tokens" => lexResult with { Tokens = [] },
            "null-token" => lexResult with { Tokens = lexResult.Tokens.SetItem(0, null!) },
            "missing-eof" => lexResult with { Tokens = lexResult.Tokens.RemoveAt(1) },
            "extra-eof" => lexResult with { Tokens = lexResult.Tokens.Add(lexResult.Tokens[1]) },
            "early-eof" => lexResult with { Tokens = lexResult.Tokens.Insert(0, Lexer.Lex(SourceText.From("")).Tokens[0]) },
            "missing-token" => lexResult with {
                Tokens = lexResult.Tokens.SetItem(0,
                SyntaxTree.Parse("void F() { return 1 }").Root.DescendantTokens().Single(item => item.IsMissing))
            },
            "invalid-kind" => WithFirstToken(lexResult, ForgeToken((TokenKind)int.MaxValue, token.Text, token.Span, token.LeadingTrivia)),
            "token-text" => WithFirstToken(lexResult, ForgeToken(token.Kind, "z", token.Span, token.LeadingTrivia)),
            "token-gap" => WithFirstToken(lexResult, ForgeToken(token.Kind, token.Text, token.Span, [])),
            "default-trivia" => WithFirstToken(lexResult, ForgeToken(token.Kind, token.Text, token.Span, default)),
            "null-trivia" => WithFirstToken(lexResult, ForgeToken(token.Kind, token.Text, token.Span,
                token.LeadingTrivia.Add(null!))),
            "invalid-trivia-kind" => WithFirstToken(lexResult, ForgeToken(token.Kind, token.Text, token.Span,
                [token.LeadingTrivia[0] with { Kind = (TriviaKind)int.MaxValue }])),
            "trivia-text" => WithFirstToken(lexResult, ForgeToken(token.Kind, token.Text, token.Span,
                [token.LeadingTrivia[0] with { Text = "z" }])),
            "trivia-gap" => WithFirstToken(lexResult, ForgeToken(token.Kind, token.Text, token.Span,
                [token.LeadingTrivia[0] with { Span = new TextSpan(1, 1), Text = "a" }])),
            "zero-trivia" => WithFirstToken(lexResult, ForgeToken(token.Kind, token.Text, token.Span,
                [token.LeadingTrivia[0] with { Span = new TextSpan(0, 0), Text = "" }])),
            "eof-position" => lexResult with { Tokens = lexResult.Tokens.SetItem(1, Lexer.Lex(SourceText.From("a")).Tokens[1]) },
            "default-diagnostics" => lexResult with { Diagnostics = default },
            "null-diagnostic" => lexResult with { Diagnostics = [null!] },
            "diagnostic-code" => lexResult with { Diagnostics = [diagnostic with { Code = "BAD" }] },
            "null-code" => lexResult with { Diagnostics = [diagnostic with { Code = null! }] },
            "diagnostic-message" => lexResult with { Diagnostics = [diagnostic with { Message = " " }] },
            "null-message" => lexResult with { Diagnostics = [diagnostic with { Message = null! }] },
            "diagnostic-severity" => lexResult with { Diagnostics = [diagnostic with { Severity = (DiagnosticSeverity)int.MaxValue }] },
            "diagnostic-span" => lexResult with { Diagnostics = [diagnostic with { Span = new TextSpan(3, 0) }] },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };

        _ = Assert.Throws<ArgumentException>(() => SyntaxTree.Parse(malformed));
    }

    [Fact]
    public void RejectsNullAndHonorsCancellation()
    {
        _ = Assert.Throws<ArgumentNullException>(() => SyntaxTree.Parse((LexResult)null!));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var lexResult = Lexer.Lex(SourceText.From("void F() {}"));
        _ = Assert.Throws<OperationCanceledException>(() => SyntaxTree.Parse(lexResult, cancellation.Token));
    }

    private static LexResult WithFirstToken(LexResult result, SyntaxToken token)
        => result with { Tokens = result.Tokens.SetItem(0, token) };

    private static SyntaxToken ForgeToken(TokenKind kind, string text, TextSpan span, ImmutableArray<SyntaxTrivia> leadingTrivia)
    {
        var constructor = Assert.Single(typeof(SyntaxToken).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        return Assert.IsType<SyntaxToken>(constructor.Invoke([kind, text, span, leadingTrivia, false]));
    }
}
