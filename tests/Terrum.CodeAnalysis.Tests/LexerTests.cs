// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class LexerTests
{
    [Theory]
    [InlineData("𐐀_var", TokenKind.Identifier)]
    [InlineData("℘", TokenKind.Identifier)]
    [InlineData("℮", TokenKind.Identifier)]
    [InlineData("™", TokenKind.Bad)]
    [InlineData("\u0301", TokenKind.Bad)]
    [InlineData("\u200C", TokenKind.Bad)]
    [InlineData("\u200D", TokenKind.Bad)]
    [InlineData("a\u200Cb", TokenKind.Identifier)]
    [InlineData("a\u200Db", TokenKind.Identifier)]
    [InlineData("١", TokenKind.Bad)]
    public void UsesUnicode17XidProfile(string text, TokenKind first)
    {
        LexResult result = Lexer.Lex(SourceText.From(text));
        Assert.Equal(first, result.Tokens[0].Kind);
        Assert.Equal(TokenKind.EndOfFile, result.Tokens[^1].Kind);
    }

    [Fact]
    public void ContinuesByScalarWithoutNormalizing()
    {
        SourceText source = SourceText.From("a\u0301 a\u00B7b _𐐀2 a\u00E9 a\u0065\u0301");
        SyntaxToken[] tokens = Lexer.Lex(source).Tokens.Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
        Assert.Equal(new[] { "a\u0301", "a\u00B7b", "_𐐀2", "a\u00E9", "ae\u0301" },
                     tokens.Select(t => t.Text));
        Assert.All(tokens, t => Assert.Equal(TokenKind.Identifier, t.Kind));
        Assert.Equal(new TextSpan(12, 2), tokens[3].Span);
    }

    [Theory]
    [InlineData("1.2e-3+4", new[] { "1.2e-3", "+", "4" })]
    [InlineData("1.foo", new[] { "1", ".", "foo" })]
    [InlineData(".5 2efoo", new[] { ".", "5", "2", "efoo" })]
    [InlineData("1else", new[] { "1", "else" })]
    [InlineData("12u32 42.3f64", new[] { "12u32", "42.3f64" })]
    [InlineData("10i32 0.1f32 2u128 4f16", new[] { "10i32", "0.1f32", "2u128", "4f16" })]
    [InlineData("1e2f32", new[] { "1e2f32" })]
    [InlineData("10i32extra 0.1f32_ 9f33", new[] { "10", "i32extra", "0.1", "f32_", "9", "f33" })]
    [InlineData("a١2", new[] { "a١2" })]
    public void NumbersEndAtAsciiGrammarBoundary(string text, string[] expected)
    {
        System.Collections.Immutable.ImmutableArray<SyntaxToken> tokens = Lexer.Lex(SourceText.From(text)).Tokens;
        Assert.Equal(expected, tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Text));
    }

    [Theory]
    [InlineData("1e", "1e")]
    [InlineData("1e+", "1e+")]
    [InlineData("1e-", "1e-")]
    [InlineData("1e+foo", "1e+")]
    [InlineData("1e+f32", "1e+")]
    [InlineData("1e;", "1e")]
    [InlineData("1e ", "1e")]
    [InlineData("1e.", "1e")]
    public void ReportsIncompleteExponent(string text, string numeric)
    {
        LexResult result = Lexer.Lex(SourceText.From(text));
        Assert.Equal(numeric, result.Tokens[0].Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "TRM0004");
    }

    [Fact]
    public void RecoversFromStringsAndComments()
    {
        string text = "\"ok\\n\\u0041\\U0001F600\" \"\\q\\uD800\\U00110000\" \"bad\n//comment\r\n/*x\n*/end /*unterminated";
        LexResult result = Lexer.Lex(SourceText.From(text));
        Assert.Equal(new[] { "TRM0005", "TRM0005", "TRM0005", "TRM0002", "TRM0003" },
                     result.Diagnostics.Select(d => d.Code));
        Assert.Equal(TokenKind.Identifier, result.Tokens[^2].Kind);
        Assert.Equal("end", result.Tokens[^2].Text);
        Assert.Equal(TriviaKind.MultiLineComment, result.Tokens[^1].LeadingTrivia[^1].Kind);
        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void ReportsSurrogatesInCommentsWithoutLosingTrivia()
    {
        string text = "//a\uD800𐐀\uDC00\r\n/*b\uDC00𐐀\uD800*/x";
        LexResult result = Lexer.Lex(SourceText.From(text));
        Assert.Equal(new[] { "TRM0001", "TRM0001", "TRM0001", "TRM0001" },
                     result.Diagnostics.Select(d => d.Code));
        Assert.Equal(new[] { 3, 6, 12, 15 }, result.Diagnostics.Select(d => d.Span.Start));
        Assert.All(result.Diagnostics, d => Assert.Equal(1, d.Span.Length));
        Assert.Equal("x", result.Tokens[0].Text);
        Assert.Equal(text[..^1], string.Concat(result.Tokens[0].LeadingTrivia.Select(t => t.Text)));
        Assert.Equal(text, string.Concat(result.Tokens[0].LeadingTrivia.Select(t => t.Text)) + result.Tokens[0].Text);
    }

    [Theory]
    [InlineData("struct", TokenKind.StructKeyword)]
    [InlineData("class", TokenKind.ClassKeyword)]
    [InlineData("interface", TokenKind.InterfaceKeyword)]
    [InlineData("enum", TokenKind.EnumKeyword)]
    [InlineData("union", TokenKind.UnionKeyword)]
    [InlineData("unique", TokenKind.UniqueKeyword)]
    [InlineData("own", TokenKind.OwnKeyword)]
    [InlineData("move", TokenKind.MoveKeyword)]
    [InlineData("new", TokenKind.NewKeyword)]
    [InlineData("for", TokenKind.ForKeyword)]
    [InlineData("foreach", TokenKind.ForeachKeyword)]
    [InlineData("break", TokenKind.BreakKeyword)]
    [InlineData("continue", TokenKind.ContinueKeyword)]
    [InlineData("switch", TokenKind.SwitchKeyword)]
    [InlineData("case", TokenKind.CaseKeyword)]
    [InlineData("default", TokenKind.DefaultKeyword)]
    [InlineData("null", TokenKind.NullKeyword)]
    [InlineData("async", TokenKind.AsyncKeyword)]
    [InlineData("await", TokenKind.AwaitKeyword)]
    [InlineData("require", TokenKind.RequireKeyword)]
    [InlineData("try", TokenKind.TryKeyword)]
    [InlineData("catch", TokenKind.CatchKeyword)]
    [InlineData("finally", TokenKind.FinallyKeyword)]
    [InlineData("throw", TokenKind.ThrowKeyword)]
    [InlineData("using", TokenKind.UsingKeyword)]
    [InlineData("structure", TokenKind.Identifier)]
    [InlineData("int32", TokenKind.Identifier)]
    public void ReservesFutureKeywords(string text, TokenKind kind)
        => Assert.Equal(kind, Lexer.Lex(SourceText.From(text)).Tokens[0].Kind);

    [Fact]
    public void CoversEverySourceUnitExactlyOnceIncludingBadAndEofTrivia()
    {
        SourceText source = SourceText.From(" \t𐐀/*x*/\r\n\uD800$\uDC00 // tail");
        LexResult result = Lexer.Lex(source);
        Assert.Same(source, result.Source);
        Assert.Equal(0, result.Tokens[0].FullSpan.Start);
        for (int index = 1; index < result.Tokens.Length; index++)
        {
            Assert.Equal(result.Tokens[index - 1].FullSpan.End, result.Tokens[index].FullSpan.Start);
        }

        Assert.Equal(source.Length, result.Tokens[^1].FullSpan.End);
        Assert.Equal(new[] { "\uD800", "$", "\uDC00" },
                     result.Tokens.Where(t => t.Kind == TokenKind.Bad).Select(t => t.Text));
        Assert.Equal(new TextSpan(source.Length, 0), result.Tokens[^1].Span);
        Assert.Equal(" // tail", string.Concat(result.Tokens[^1].LeadingTrivia.Select(t => t.Text)));
    }

    [Fact]
    public void ReportsNonIdentifierAstralScalarAsOneBadToken()
    {
        LexResult result = Lexer.Lex(SourceText.From("😀a"));
        Assert.Equal(TokenKind.Bad, result.Tokens[0].Kind);
        Assert.Equal(new TextSpan(0, 2), result.Tokens[0].Span);
        Assert.Equal(new TextSpan(0, 2), result.Diagnostics[0].Span);
        Assert.Equal(new TextSpan(2, 1), result.Tokens[1].Span);
    }

    [Theory]
    [InlineData(0x1F600, 3, 1)]
    [InlineData(0xD800, 2, 2)]
    public void InvalidEscapesStillAdvanceByScalar(int scalarOrCodeUnit, int escapeLength, int diagnosticCount)
    {
        // Attribute strings pass through metadata UTF-8; construct malformed UTF-16 at runtime.
        string escaped = scalarOrCodeUnit <= char.MaxValue
            ? new string((char)scalarOrCodeUnit, 1)
            : char.ConvertFromUtf32(scalarOrCodeUnit);
        string text = "\"\\" + escaped + "\"";
        LexResult result = Lexer.Lex(SourceText.From(text));
        Assert.Equal(diagnosticCount, result.Diagnostics.Length);
        Diagnostic escape = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "TRM0005");
        Assert.Equal(new TextSpan(1, escapeLength), escape.Span);
        Assert.Equal(text, result.Tokens[0].Text);
    }

    [Fact]
    public void RecognizesOperatorsWithoutMergingGenericClosers()
    {
        LexResult result = Lexer.Lex(SourceText.From(">> >= && & || | == = != !"));
        Assert.Equal(new[] { TokenKind.Greater, TokenKind.Greater, TokenKind.GreaterEquals,
                             TokenKind.AmpersandAmpersand, TokenKind.Ampersand, TokenKind.PipePipe,
                             TokenKind.Pipe, TokenKind.EqualsEquals, TokenKind.Equals,
                             TokenKind.BangEquals, TokenKind.Bang, TokenKind.EndOfFile },
                     result.Tokens.Select(t => t.Kind));
    }

    [Fact]
    public void CancellationIsNotConvertedToDiagnostic()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Lexer.Lex(SourceText.From("abc"), cts.Token));
        Assert.Throws<ArgumentNullException>(() => Lexer.Lex(null!));
    }
}
