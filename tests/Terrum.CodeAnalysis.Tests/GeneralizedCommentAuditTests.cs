// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class GeneralizedCommentAuditTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(257)]
    public void WholeRunsAndSlashSuffixesHaveIndependentExactExtents(int stars)
    {
        foreach (var slashes in new[] { 1, 2 })
        {
            var opener = new string('/', slashes) + new string('*', stars);
            var closer = new string('*', stars) + new string('/', slashes);
            var kind = slashes == 1 ? TriviaKind.MultiLineComment : TriviaKind.DocumentationBlockComment;
            var source = "a" + opener + " opaque \" /* // \r\n\u0085\u2028\u2029 " + closer + "/b";
            var result = Lexer.Lex(SourceText.From(source));
            Assert.Empty(result.Diagnostics);
            Assert.Equal([TokenKind.Identifier, TokenKind.Slash, TokenKind.Identifier, TokenKind.EndOfFile],
                result.Tokens.Select(token => token.Kind));
            var trivia = Assert.Single(result.Tokens[1].LeadingTrivia);
            Assert.Equal(kind, trivia.Kind);
            Assert.Equal(new TextSpan(1, source.Length - 3), trivia.Span);
            Assert.Equal(new TextSpan(source.Length - 2, 1), result.Tokens[1].Span);
            Assert.Equal(source, Reconstruct(result));

            var shortRun = opener + " " + new string('*', stars - 1) + new string('/', slashes) + " " + closer;
            Assert.Empty(Lexer.Lex(SourceText.From(shortRun)).Diagnostics);
            var longRun = opener + " " + "*" + closer + " " + closer;
            var diagnostic = Assert.Single(Lexer.Lex(SourceText.From(longRun)).Diagnostics);
            Assert.Equal("TRM0010", diagnostic.Code);
            Assert.Equal(new TextSpan(opener.Length + 1, stars + 1 + slashes), diagnostic.Span);
        }
    }

    [Theory]
    [InlineData("/** /* */ text **/")]
    [InlineData("//*** \" /* **/ \r\n ***//")]
    public void EveryTruncatedBlockRetainsItsEntireExtent(string source)
    {
        var openingEnd = source.IndexOf(' ', StringComparison.Ordinal);
        for (var length = openingEnd; length < source.Length; length++)
        {
            var prefix = source[..length];
            var result = Lexer.Lex(SourceText.From(prefix));
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("TRM0003", diagnostic.Code);
            Assert.Equal(new TextSpan(0, length), diagnostic.Span);
            Assert.Equal(TokenKind.EndOfFile, Assert.Single(result.Tokens).Kind);
            Assert.Equal(prefix, Reconstruct(result));
        }
    }

    [Theory]
    [InlineData("///** \" /* **/", TriviaKind.DocumentationLineComment)]
    [InlineData("////* *//", TriviaKind.DocumentationLineComment)]
    [InlineData("// ordinary //* *//", TriviaKind.SingleLineComment)]
    public void LinePriorityLeavesBlockLookingPayloadOpaque(string source, TriviaKind kind)
    {
        var result = Lexer.Lex(SourceText.From(source));
        Assert.Empty(result.Diagnostics);
        var comment = Assert.Single(Assert.Single(result.Tokens).LeadingTrivia);
        Assert.Equal(kind, comment.Kind);
        Assert.Equal(source, comment.Text);
    }

    [Fact]
    public void InnerExactCloserEndsTheCommentWithoutRecognizingItsOpener()
    {
        const string Source = "/** /**/ **/";
        var result = Lexer.Lex(SourceText.From(Source));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(new TextSpan(0, 8), result.Tokens[0].LeadingTrivia[0].Span);
        Assert.Equal([TokenKind.Star, TokenKind.Star, TokenKind.Slash, TokenKind.EndOfFile],
            result.Tokens.Select(token => token.Kind));
        Assert.Equal(Source, Reconstruct(result));
        Assert.Equal(Source, SyntaxTree.Parse(Source).Root.ToFullString());
    }

    [Fact]
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
