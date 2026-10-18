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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Syntax;

public sealed class PrelexedSyntaxTests
{
    [TestCase("")]
    [TestCase(" // trailing\r\n")]
    [TestCase("void F() { return; }")]
    [TestCase("void F() { return 1 } // trailing \uD800")]
    [TestCase("void F() { @ return; } /* unterminated")]
    public void SuppliedLexingMatchesOrdinaryParsingAndRecovery(string text)
    {
        var lexResult = Lexer.Lex(SourceText.From(text, "fixture.terrum"));
        var supplied = SyntaxTree.Parse(lexResult);
        var ordinary = SyntaxTree.Parse(lexResult.Source);

        Assert.That(supplied.Source, Is.SameAs(lexResult.Source));
        Assert.That(supplied.Tokens.Length, Is.EqualTo(lexResult.Tokens.Length));
        for (var index = 0; index < supplied.Tokens.Length; index++)
        {
            Assert.That(supplied.Tokens[index], Is.SameAs(lexResult.Tokens[index]));
        }

        Assert.That(supplied.Root.ToFullString(), Is.EqualTo(text));
        Assert.That(supplied.Root.ToFullString(), Is.EqualTo(ordinary.Root.ToFullString()));
        Assert.That(supplied.ContentId, Is.EqualTo(ordinary.ContentId));
        Assert.That(supplied.Diagnostics, Is.EqualTo(ordinary.Diagnostics));
        Assert.That(supplied.Root.DescendantNodes(includeSelf: true).Select(node => (node.Kind, node.Span, node.FullSpan)), Is.EqualTo(ordinary.Root.DescendantNodes(includeSelf: true).Select(node => (node.Kind, node.Span, node.FullSpan))));
        Assert.That(supplied.Root.DescendantTokens().Select(token => (token.Kind, token.Span, token.IsMissing)), Is.EqualTo(ordinary.Root.DescendantTokens().Select(token => (token.Kind, token.Span, token.IsMissing))));
    }

    [Test]
    public void SuppliedClassificationIsAcceptedWithoutRelexing()
    {
        var lexResult = Lexer.Lex(SourceText.From("void F() { return true; }"));
        var index = lexResult.Tokens.IndexOf(lexResult.Tokens.Single(token => token.Kind == TokenKind.TrueKeyword));
        var original = lexResult.Tokens[index];
        var substituted = ForgeToken(TokenKind.FalseKeyword, original.Text, original.Span, original.LeadingTrivia);
        var tree = SyntaxTree.Parse(lexResult with { Tokens = lexResult.Tokens.SetItem(index, substituted) });

        Assert.That(tree.Tokens[index], Is.SameAs(substituted));
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantTokens(),token => ReferenceEquals(substituted, token)),Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(lexResult.Source.Text));
    }

    [Test]
    public void ReservedUnderscoreCannotBeForgedAsAnIdentifierInPrelexedSource()
    {
        var lexResult = Lexer.Lex(SourceText.From("int32 Main() { _ = int32(42); return 42; }"));
        var index = lexResult.Tokens.IndexOf(
            lexResult.Tokens.Single(static token => token.Kind == TokenKind.Underscore));
        var token = lexResult.Tokens[index];
        var forged = ForgeToken(TokenKind.Identifier, token.Text, token.Span, token.LeadingTrivia);

        _ = Assert.Throws<ArgumentException>(() =>
            SyntaxTree.Parse(lexResult with { Tokens = lexResult.Tokens.SetItem(index, forged) }))!;
    }

    [Test]
    public void TrustedCallerClassificationIsNotHostileCompletedScannerInterchange()
    {
        const string text = "int32 !!() { return 1; }";
        var lexed = Lexer.Lex(SourceText.From(text));
        var token = global::System.Linq.Enumerable.Single(lexed.Tokens, item => item.Kind == TokenKind.Bad);
        var forged = ForgeToken(TokenKind.Identifier, token.Text, token.Span, token.LeadingTrivia);
        var tree = SyntaxTree.Parse(lexed with {
            Tokens = lexed.Tokens.SetItem(lexed.Tokens.IndexOf(token), forged),
            Diagnostics = [],
        });
        Assert.That(tree.Diagnostics, Is.Empty);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
    }
    [TestCase("null-source")]
    [TestCase("default-tokens")]
    [TestCase("empty-tokens")]
    [TestCase("null-token")]
    [TestCase("missing-eof")]
    [TestCase("extra-eof")]
    [TestCase("early-eof")]
    [TestCase("missing-token")]
    [TestCase("invalid-kind")]
    [TestCase("token-text")]
    [TestCase("token-gap")]
    [TestCase("default-trivia")]
    [TestCase("null-trivia")]
    [TestCase("invalid-trivia-kind")]
    [TestCase("trivia-text")]
    [TestCase("trivia-gap")]
    [TestCase("zero-trivia")]
    [TestCase("eof-position")]
    [TestCase("default-diagnostics")]
    [TestCase("null-diagnostic")]
    [TestCase("diagnostic-code")]
    [TestCase("null-code")]
    [TestCase("diagnostic-message")]
    [TestCase("null-message")]
    [TestCase("diagnostic-severity")]
    [TestCase("diagnostic-span")]
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

        _ = Assert.Throws<ArgumentException>(() => SyntaxTree.Parse(malformed))!;
    }

    [Test]
    public void RejectsNullAndHonorsCancellation()
    {
        _ = Assert.Throws<ArgumentNullException>(() => SyntaxTree.Parse((LexResult)null!))!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var lexResult = Lexer.Lex(SourceText.From("void F() {}"));
        _ = Assert.Throws<OperationCanceledException>(() => SyntaxTree.Parse(lexResult, cancellation.Token))!;
    }

    [Test]
    public void SuppliedShiftClassificationWithDifferentTextDoesNotSplitSource()
    {
        var lex = Lexer.Lex(SourceText.From("void F(Box<Box<int32 x value) {}"));
        var index = Enumerable.Range(0, lex.Tokens.Length).Single(i => lex.Tokens[i].Text == "x");
        var token = lex.Tokens[index];
        var classified = ForgeToken(TokenKind.ShiftRight, token.Text, token.Span, token.LeadingTrivia);
        var tree = SyntaxTree.Parse(lex with { Tokens = lex.Tokens.SetItem(index, classified) });
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(lex.Source.Text));
        Assert.That(tree.Tokens[index], Is.SameAs(classified));
        Assert.That(tree.Root.DescendantTokens(), Does.Contain(classified));
    }

    private static LexResult WithFirstToken(LexResult result, SyntaxToken token)
        => result with { Tokens = result.Tokens.SetItem(0, token) };

    private static SyntaxToken ForgeToken(TokenKind kind, string text, TextSpan span, ImmutableArray<SyntaxTrivia> leadingTrivia)
    {
        var constructor = global::System.Linq.Enumerable.Single(typeof(SyntaxToken).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        return ((global::System.Func<SyntaxToken>)(() => { var __nunitActual0 = constructor.Invoke([kind, text, span, leadingTrivia, false]); Assert.That(__nunitActual0, Is.TypeOf<SyntaxToken>()); return (SyntaxToken)__nunitActual0!; }))();
    }
}
