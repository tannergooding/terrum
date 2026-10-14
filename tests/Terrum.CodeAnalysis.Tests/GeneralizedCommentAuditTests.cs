// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class GeneralizedCommentAuditTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(16)]
    [TestCase(257)]
    public void WholeRunsAndSlashSuffixesHaveIndependentExactExtents(int stars)
    {
        foreach (var slashes in new[] { 1, 2 })
        {
            var opener = new string('/', slashes) + new string('*', stars);
            var closer = new string('*', stars) + new string('/', slashes);
            var kind = slashes == 1 ? TriviaKind.MultiLineComment : TriviaKind.DocumentationBlockComment;
            var source = "a" + opener + " opaque \" /* // \r\n\u0085\u2028\u2029 " + closer + "/b";
            var result = Lexer.Lex(SourceText.From(source));
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Tokens.Select(token => token.Kind), Is.EqualTo([TokenKind.Identifier, TokenKind.Slash, TokenKind.Identifier, TokenKind.EndOfFile]));
            var trivia = global::System.Linq.Enumerable.Single(result.Tokens[1].LeadingTrivia);
            Assert.That(trivia.Kind, Is.EqualTo(kind));
            Assert.That(trivia.Span, Is.EqualTo(new TextSpan(1, source.Length - 3)));
            Assert.That(result.Tokens[1].Span, Is.EqualTo(new TextSpan(source.Length - 2, 1)));
            Assert.That(Reconstruct(result), Is.EqualTo(source));

            var shortRun = opener + " " + new string('*', stars - 1) + new string('/', slashes) + " " + closer;
            Assert.That(Lexer.Lex(SourceText.From(shortRun)).Diagnostics, Is.Empty);
            var longRun = opener + " " + "*" + closer + " " + closer;
            var diagnostic = global::System.Linq.Enumerable.Single(Lexer.Lex(SourceText.From(longRun)).Diagnostics);
            Assert.That(diagnostic.Code, Is.EqualTo("TRM0010"));
            Assert.That(diagnostic.Span, Is.EqualTo(new TextSpan(opener.Length + 1, stars + 1 + slashes)));
        }
    }
    [TestCase("/** /* */ text **/")]
    [TestCase("//*** \" /* **/ \r\n ***//")]
    public void EveryTruncatedBlockRetainsItsEntireExtent(string source)
    {
        var openingEnd = source.IndexOf(' ', StringComparison.Ordinal);
        for (var length = openingEnd; length < source.Length; length++)
        {
            var prefix = source[..length];
            var result = Lexer.Lex(SourceText.From(prefix));
            var diagnostic = global::System.Linq.Enumerable.Single(result.Diagnostics);
            Assert.That(diagnostic.Code, Is.EqualTo("TRM0003"));
            Assert.That(diagnostic.Span, Is.EqualTo(new TextSpan(0, length)));
            Assert.That(global::System.Linq.Enumerable.Single(result.Tokens).Kind, Is.EqualTo(TokenKind.EndOfFile));
            Assert.That(Reconstruct(result), Is.EqualTo(prefix));
        }
    }
    [TestCase("///** \" /* **/",TriviaKind.DocumentationLineComment)]
    [TestCase("////* *//",TriviaKind.DocumentationLineComment)]
    [TestCase("// ordinary //* *//",TriviaKind.SingleLineComment)]
    public void LinePriorityLeavesBlockLookingPayloadOpaque(string source, TriviaKind kind)
    {
        var result = Lexer.Lex(SourceText.From(source));
        Assert.That(result.Diagnostics, Is.Empty);
        var comment = global::System.Linq.Enumerable.Single(global::System.Linq.Enumerable.Single(result.Tokens).LeadingTrivia);
        Assert.That(comment.Kind, Is.EqualTo(kind));
        Assert.That(comment.Text, Is.EqualTo(source));
    }

    [Test]
    public void InnerExactCloserEndsTheCommentWithoutRecognizingItsOpener()
    {
        const string Source = "/** /**/ **/";
        var result = Lexer.Lex(SourceText.From(Source));
        var diagnostic = global::System.Linq.Enumerable.Single(result.Diagnostics);
        Assert.That(diagnostic.Code, Is.EqualTo("TRM0011"));
        Assert.That(diagnostic.Span, Is.EqualTo(new TextSpan(9, 3)));
        Assert.That(result.Tokens[0].LeadingTrivia[0].Span, Is.EqualTo(new TextSpan(0, 8)));
        Assert.That(result.Tokens.Select(token => token.Kind), Is.EqualTo([TokenKind.Bad, TokenKind.EndOfFile]));
        Assert.That(Reconstruct(result), Is.EqualTo(Source));
        Assert.That(SyntaxTree.Parse(Source).Root.ToFullString(), Is.EqualTo(Source));
    }

    [Test]
    public void CurrentAdapterRejectsMissingFenceDiagnosticsAndMisclassifiedDocumentation()
    {
        var source = SourceText.From("/* **/ */");
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 1,
            Words((int)TokenKind.EndOfFile, 9, 0), Words(1, 3, 0, 9, 0), Words(0)));
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(SourceText.From("///"),
            1, Words((int)TokenKind.EndOfFile, 3, 0), Words(1, 2, 0, 3, 0), Words(0)));
    }

    private static byte[] Words(params int[] values)
    {
        var bytes = new byte[values.Length * sizeof(int)];
        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), values[index]);
        }
        return bytes;
    }

    private static string Reconstruct(LexResult result)
    {
        var text = new StringBuilder();
        foreach (var token in result.Tokens)
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                text.Append(trivia.Text);
            }
            text.Append(token.Text);
        }
        return text.ToString();
    }
}
