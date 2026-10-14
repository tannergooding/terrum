// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Threading;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class LexerTests
{
    [Test]
    public void StandaloneUnderscoreIsReservedWithoutChangingPrefixedIdentifiers()
    {
        var tokens = Lexer.Lex(SourceText.From("_ _name __ _123")).Tokens;
        Assert.That(tokens.Select(static token => token.Kind), Is.EqualTo([TokenKind.Underscore, TokenKind.Identifier, TokenKind.Identifier,
            TokenKind.Identifier, TokenKind.EndOfFile]));
    }
    [TestCase("𐐀_var",TokenKind.Identifier)]
    [TestCase("℘",TokenKind.Identifier)]
    [TestCase("℮",TokenKind.Identifier)]
    [TestCase("™",TokenKind.Bad)]
    [TestCase("\u0301",TokenKind.Bad)]
    [TestCase("\u200C",TokenKind.Bad)]
    [TestCase("\u200D",TokenKind.Bad)]
    [TestCase("a\u200Cb",TokenKind.Identifier)]
    [TestCase("a\u200Db",TokenKind.Identifier)]
    [TestCase("١",TokenKind.Bad)]
    public void UsesUnicode17XidProfile(string text, TokenKind first)
    {
        var result = Lexer.Lex(SourceText.From(text));
        Assert.That(result.Tokens[0].Kind, Is.EqualTo(first));
        Assert.That(result.Tokens[^1].Kind, Is.EqualTo(TokenKind.EndOfFile));
    }

    [Test]
    public void ContinuesByScalarWithoutNormalizing()
    {
        var source = SourceText.From("a\u0301 a\u00B7b _𐐀2 a\u00E9 a\u0065\u0301");
        var tokens = Lexer.Lex(source).Tokens.Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
        var expected = new[] { "a\u0301", "a\u00B7b", "_𐐀2", "a\u00E9", "ae\u0301" };
        Assert.That(tokens.Select(t => t.Text), Is.EqualTo(expected));
        {var __nunitItems0 = tokens;        Assert.Multiple(() => { foreach (var __nunitItem0 in __nunitItems0) { Assert.That(__nunitItem0.Kind, Is.EqualTo(TokenKind.Identifier)); } });}
        Assert.That(tokens[3].Span, Is.EqualTo(new TextSpan(12, 2)));
    }
    [TestCase("1.2e-3+4",new[] { "1.2e-3", "+", "4" })]
    [TestCase("1.foo",new[] { "1", ".", "foo" })]
    [TestCase(".5 2efoo",new[] { ".", "5", "2", "efoo" })]
    [TestCase("1else",new[] { "1", "else" })]
    [TestCase("0x1f32 0XABCD+0x0 0x1e2",new[] { "0x1f32", "0XABCD", "+", "0x0", "0x1e2" })]
    [TestCase("0b101010 0B0 0b1i32",new[] { "0b101010", "0B0", "0b1i32" })]
    [TestCase("1_000 0x_8000_0000 0b___1010 1__2.3__4e+5_6",        new[] { "1_000", "0x_8000_0000", "0b___1010", "1__2.3__4e+5_6" })]
    [TestCase("_123 1_000foo 1e+2_3 0x___FF 0B__1u32",        new[] { "_123", "1_000", "foo", "1e+2_3", "0x___FF", "0B__1u32" })]
    [TestCase("1_foo 0xF_tail 0b1_tail",        new[] { "1", "_foo", "0xF", "_tail", "0b1", "_tail" })]
    [TestCase("0b 0B2 0b102 0b1f32 0b1u64 0b1_0 0b1.1",        new[] { "0b", "0B", "2", "0b10", "2", "0b1f32", "0b1u64", "0b1_0", "0b1", ".", "1" })]
    [TestCase("0x 0X 0xG 0x1g",new[] { "0x", "0X", "0x", "G", "0x1", "g" })]
    [TestCase("0x1u32 0x1f32 0x1_2 0x1.2 0x1p2",        new[] { "0x1u32", "0x1f32", "0x1_2", "0x1", ".", "2", "0x1p2" })]
    [TestCase("0x1p2 0X.8P-1 0x1.p2 0x.p2 0x1._p2",        new[] { "0x1p2", "0X.8P-1", "0x1.p2", "0x", ".", "p2", "0x1", ".", "_p2" })]
    [TestCase("0x1p 0x1p+ 0x1P- 0x1p2",        new[] { "0x1", "p", "0x1", "p", "+", "0x1", "P", "-", "0x1p2" })]
    [TestCase("12u32 42.3f64",new[] { "12u32", "42.3f64" })]
    [TestCase("10i32 0.1f32 2u128 4f16",new[] { "10i32", "0.1f32", "2u128", "4f16" })]
    [TestCase("1e2f32",new[] { "1e2f32" })]
    [TestCase("10i32extra 0.1f32_ 9f33",new[] { "10", "i32extra", "0.1", "f32_", "9", "f33" })]
    [TestCase("a١2",new[] { "a١2" })]
    public void NumbersEndAtAsciiGrammarBoundary(string text, string[] expected)
    {
        var tokens = Lexer.Lex(SourceText.From(text)).Tokens;
        Assert.That(tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Text), Is.EqualTo(expected));
    }
    [TestCase("1_000")]
    [TestCase("1__2")]
    [TestCase("0x_8000_0000")]
    [TestCase("0X___F_F32")]
    [TestCase("0b_1000_0000")]
    [TestCase("0B___1__0")]
    [TestCase("1__2.3__4e+5_6")]
    [TestCase("1.2_3E-4__5")]
    public void ValidDigitSeparatorsPreserveOneNumericTokenWithoutDiagnostics(string text)
    {
        var result = Lexer.Lex(SourceText.From(text));
        Assert.That(result.Diagnostics, Is.Empty);
        var token = global::System.Linq.Enumerable.Single(result.Tokens.Where(token => token.Kind != TokenKind.EndOfFile));
        Assert.That(token.Kind, Is.EqualTo(TokenKind.NumericLiteral));
        Assert.That(token.Text, Is.EqualTo(text));
    }
    [TestCase("0x","TRM0007",0,2)]
    [TestCase("0XG","TRM0007",0,2)]
    [TestCase("0xi32","TRM0007",0,2)]
    [TestCase("0x2Ai32","TRM0006",4,3)]
    [TestCase("0x2Au64","TRM0006",4,3)]
    [TestCase("0b","TRM0008",0,2)]
    [TestCase("0B2","TRM0008",0,2)]
    [TestCase("0b1f32","TRM0006",3,3)]
    [TestCase("0b1u64","TRM0006",3,3)]
    [TestCase("1_","TRM0009",1,1)]
    [TestCase("1___","TRM0009",1,3)]
    [TestCase("0xF__","TRM0009",3,2)]
    [TestCase("0b1_","TRM0009",3,1)]
    [TestCase("0x___","TRM0007",0,2)]
    [TestCase("0b__","TRM0008",0,2)]
    [TestCase("1_.2","TRM0009",1,1)]
    [TestCase("1.2_","TRM0009",3,1)]
    [TestCase("1e+2_","TRM0009",4,1)]
    [TestCase("0x_F__","TRM0009",4,2)]
    public void HexPrefixAndAttachedWidthTagsRetainExactDiagnosticSpans(string text, string code, int start, int length)
    {
        var result = Lexer.Lex(SourceText.From(text));
        var diagnostic = global::System.Linq.Enumerable.Single(result.Diagnostics);
        Assert.That(diagnostic.Code, Is.EqualTo(code));
        Assert.That(diagnostic.Span, Is.EqualTo(new TextSpan(start, length)));
    }
    [TestCase("1e","1e")]
    [TestCase("1e+","1e+")]
    [TestCase("1e-","1e-")]
    [TestCase("1e+foo","1e+")]
    [TestCase("1e+f32","1e+")]
    [TestCase("1e;","1e")]
    [TestCase("1e ","1e")]
    [TestCase("1e.","1e")]
    public void ReportsIncompleteExponent(string text, string numeric)
    {
        var result = Lexer.Lex(SourceText.From(text));
        Assert.That(result.Tokens[0].Text, Is.EqualTo(numeric));
        Assert.That(global::System.Linq.Enumerable.Any(result.Diagnostics,d => d.Code == "TRM0004"),Is.True);
    }

    [Test]
    public void RecoversFromStringsAndComments()
    {
        var text = "\"ok\\n\\u0041\\U0001F600\" \"\\q\\uD800\\U00110000\" \"bad\n//comment\r\n/*x\n*/end /*unterminated";
        var result = Lexer.Lex(SourceText.From(text));
        var expected = new[] { "TRM0005", "TRM0005", "TRM0005", "TRM0002", "TRM0003" };
        Assert.That(result.Diagnostics.Select(d => d.Code), Is.EqualTo(expected));
        Assert.That(result.Tokens[^2].Kind, Is.EqualTo(TokenKind.Identifier));
        Assert.That(result.Tokens[^2].Text, Is.EqualTo("end"));
        Assert.That(result.Tokens[^1].LeadingTrivia[^1].Kind, Is.EqualTo(TriviaKind.MultiLineComment));
        {var __nunitItems1 = result.Diagnostics;        Assert.Multiple(() => { foreach (var __nunitItem1 in __nunitItems1) { Assert.That(__nunitItem1.Severity, Is.EqualTo(DiagnosticSeverity.Error)); } });}
    }
    [TestCase("'A'")]
    [TestCase("'\\''")]
    [TestCase("'\\u0041'")]
    [TestCase("'\\U0001F600'")]
    [TestCase("'😀'")]
    public void ReadsSingleScalarCharacterLiterals(string text)
    {
        var result = Lexer.Lex(SourceText.From(text));
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Tokens[0].Kind, Is.EqualTo(TokenKind.CharacterLiteral));
        Assert.That(result.Tokens[0].Text, Is.EqualTo(text));
        Assert.That(result.Tokens[1].Kind, Is.EqualTo(TokenKind.EndOfFile));
    }
    [TestCase("''","TRM0001")]
    [TestCase("'ab'","TRM0001")]
    [TestCase("'\\q'","TRM0005")]
    [TestCase("'\\uD800'","TRM0005")]
    [TestCase("'unterminated","TRM0002")]
    public void DiagnosesInvalidCharacterLiteralContent(string text, string code)
    {
        var result = Lexer.Lex(SourceText.From(text));
        var diagnostic = global::System.Linq.Enumerable.Single(result.Diagnostics);
        Assert.That(diagnostic.Code, Is.EqualTo(code));
        Assert.That(result.Tokens[0].Kind, Is.EqualTo(TokenKind.CharacterLiteral));
    }

    [Test]
    public void ReportsMalformedUtf16InCharacterLiteralsWithoutReplacingSource()
    {
        var text = string.Concat('\'', (char)0xD800, '\'');
        var result = Lexer.Lex(SourceText.From(text));
        var diagnostic = global::System.Linq.Enumerable.Single(result.Diagnostics);
        Assert.That(diagnostic.Code, Is.EqualTo("TRM0001"));
        Assert.That(diagnostic.Span, Is.EqualTo(new TextSpan(1, 1)));
        Assert.That(result.Tokens[0].Text, Is.EqualTo(text));
    }

    [Test]
    public void ReportsSurrogatesInCommentsWithoutLosingTrivia()
    {
        var text = "//a\uD800𐐀\uDC00\r\n/*b\uDC00𐐀\uD800*/x";
        var result = Lexer.Lex(SourceText.From(text));
        var expectedCodes = new[] { "TRM0001", "TRM0001", "TRM0001", "TRM0001" };
        var expectedStarts = new[] { 3, 6, 12, 15 };
        Assert.That(result.Diagnostics.Select(d => d.Code), Is.EqualTo(expectedCodes));
        Assert.That(result.Diagnostics.Select(d => d.Span.Start), Is.EqualTo(expectedStarts));
        {var __nunitItems2 = result.Diagnostics;        Assert.Multiple(() => { foreach (var __nunitItem2 in __nunitItems2) { Assert.That(__nunitItem2.Span.Length, Is.EqualTo(1)); } });}
        Assert.That(result.Tokens[0].Text, Is.EqualTo("x"));
        Assert.That(string.Concat(result.Tokens[0].LeadingTrivia.Select(t => t.Text)), Is.EqualTo(text[..^1]));
        Assert.That(string.Concat(result.Tokens[0].LeadingTrivia.Select(t => t.Text)) + result.Tokens[0].Text, Is.EqualTo(text));
    }
    [TestCase("struct",TokenKind.StructKeyword)]
    [TestCase("class",TokenKind.ClassKeyword)]
    [TestCase("interface",TokenKind.InterfaceKeyword)]
    [TestCase("abstract",TokenKind.Identifier)]
    [TestCase("operator",TokenKind.OperatorKeyword)]
    [TestCase("where",TokenKind.WhereKeyword)]
    [TestCase("enum",TokenKind.EnumKeyword)]
    [TestCase("union",TokenKind.UnionKeyword)]
    [TestCase("alias",TokenKind.AliasKeyword)]
    [TestCase("unique",TokenKind.UniqueKeyword)]
    [TestCase("own",TokenKind.OwnKeyword)]
    [TestCase("move",TokenKind.MoveKeyword)]
    [TestCase("new",TokenKind.NewKeyword)]
    [TestCase("for",TokenKind.ForKeyword)]
    [TestCase("foreach",TokenKind.ForeachKeyword)]
    [TestCase("break",TokenKind.BreakKeyword)]
    [TestCase("continue",TokenKind.ContinueKeyword)]
    [TestCase("switch",TokenKind.SwitchKeyword)]
    [TestCase("case",TokenKind.CaseKeyword)]
    [TestCase("default",TokenKind.DefaultKeyword)]
    [TestCase("null",TokenKind.NullKeyword)]
    [TestCase("async",TokenKind.AsyncKeyword)]
    [TestCase("await",TokenKind.AwaitKeyword)]
    [TestCase("require",TokenKind.RequireKeyword)]
    [TestCase("try",TokenKind.TryKeyword)]
    [TestCase("catch",TokenKind.CatchKeyword)]
    [TestCase("finally",TokenKind.FinallyKeyword)]
    [TestCase("throw",TokenKind.ThrowKeyword)]
    [TestCase("using",TokenKind.UsingKeyword)]
    [TestCase("structure",TokenKind.Identifier)]
    [TestCase("int32",TokenKind.Identifier)]
    public void ReservesFutureKeywords(string text, TokenKind kind)
        => Assert.That(Lexer.Lex(SourceText.From(text)).Tokens[0].Kind, Is.EqualTo(kind));

    [Test]
    public void CoversEverySourceUnitExactlyOnceIncludingBadAndEofTrivia()
    {
        var source = SourceText.From(" \t𐐀/*x*/\r\n\uD800$\uDC00 // tail");
        var result = Lexer.Lex(source);
        Assert.That(result.Source, Is.SameAs(source));
        Assert.That(result.Tokens[0].FullSpan.Start, Is.EqualTo(0));
        for (var index = 1; index < result.Tokens.Length; index++)
        {
            Assert.That(result.Tokens[index].FullSpan.Start, Is.EqualTo(result.Tokens[index - 1].FullSpan.End));
        }

        Assert.That(result.Tokens[^1].FullSpan.End, Is.EqualTo(source.Length));
        var expected = new[] { "\uD800", "$", "\uDC00" };
        Assert.That(result.Tokens.Where(t => t.Kind == TokenKind.Bad).Select(t => t.Text), Is.EqualTo(expected));
        Assert.That(result.Tokens[^1].Span, Is.EqualTo(new TextSpan(source.Length, 0)));
        Assert.That(string.Concat(result.Tokens[^1].LeadingTrivia.Select(t => t.Text)), Is.EqualTo(" // tail"));
    }

    [Test]
    public void ReportsNonIdentifierAstralScalarAsOneBadToken()
    {
        var result = Lexer.Lex(SourceText.From("😀a"));
        Assert.That(result.Tokens[0].Kind, Is.EqualTo(TokenKind.Bad));
        Assert.That(result.Tokens[0].Span, Is.EqualTo(new TextSpan(0, 2)));
        Assert.That(result.Diagnostics[0].Span, Is.EqualTo(new TextSpan(0, 2)));
        Assert.That(result.Tokens[1].Span, Is.EqualTo(new TextSpan(2, 1)));
    }
    [TestCase(0x1F600,3,1)]
    [TestCase(0xD800,2,2)]
    public void InvalidEscapesStillAdvanceByScalar(int scalarOrCodeUnit, int escapeLength, int diagnosticCount)
    {
        // Attribute strings pass through metadata UTF-8; construct malformed UTF-16 at runtime.
        var escaped = scalarOrCodeUnit <= char.MaxValue
            ? new string((char)scalarOrCodeUnit, 1)
            : char.ConvertFromUtf32(scalarOrCodeUnit);
        var text = "\"\\" + escaped + "\"";
        var result = Lexer.Lex(SourceText.From(text));
        Assert.That(result.Diagnostics.Length, Is.EqualTo(diagnosticCount));
        var escape = global::System.Linq.Enumerable.Single(result.Diagnostics, diagnostic => diagnostic.Code == "TRM0005");
        Assert.That(escape.Span, Is.EqualTo(new TextSpan(1, escapeLength)));
        Assert.That(result.Tokens[0].Text, Is.EqualTo(text));
    }

    [Test]
    public void RecognizesOperatorsBeforeContextualGenericCloserSplitting()
    {
        var result = Lexer.Lex(SourceText.From(">> >= && & || | == = != !"));
        Assert.That(result.Tokens.Select(t => t.Kind), Is.EqualTo(new[] { TokenKind.ShiftRight, TokenKind.GreaterEquals,
                             TokenKind.AmpersandAmpersand, TokenKind.Ampersand, TokenKind.PipePipe,
                             TokenKind.Pipe, TokenKind.EqualsEquals, TokenKind.Equals,
                             TokenKind.BangEquals, TokenKind.Bang, TokenKind.EndOfFile }));
    }

    [Test]
    public void CancellationIsNotConvertedToDiagnostic()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        _ = Assert.Throws<OperationCanceledException>(() => Lexer.Lex(SourceText.From("abc"), cts.Token))!;
        _ = Assert.Throws<ArgumentNullException>(() => Lexer.Lex(null!))!;
    }
}
