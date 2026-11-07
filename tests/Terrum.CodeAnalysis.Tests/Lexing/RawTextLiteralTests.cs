// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Lexing;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class RawTextLiteralTests
{
    [TestCase("\"\"\"plain\"\"\"", "plain")]
    [TestCase("\"\"\"a\\n{value}\"\"\"", "a\\n{value}")]
    [TestCase("\"\"\"a\"\"b\"\"\"", "a\"\"b")]
    [TestCase("\"\"\"\"He wrote \"\"\" literally.\"\"\"\"", "He wrote \"\"\" literally.")]
    [TestCase("\"\"\"\\n\"\"\"", "\\n")]
    [TestCase("\"\"\"😀\"\"\"", "😀")]
    public void DecodesSingleLineRawTextWithoutEscapeOrBraceProcessing(string spelling, string expected)
        => AssertValid(spelling, expected);

    [Test]
    public void PreservesRawSpellingAndStopsAtTheClosingFence()
    {
        const string source = "\"\"\"raw\"\"\" next";
        var result = Lexer.Lex(SourceText.From(source));

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Tokens[0].Kind, Is.EqualTo(TokenKind.StringLiteral));
        Assert.That(result.Tokens[0].Text, Is.EqualTo("\"\"\"raw\"\"\""));
        Assert.That(result.Tokens[1].Text, Is.EqualTo("next"));
        Assert.That(StringLiteralDecoder.TryDecode(result.Tokens[0].Text.AsSpan(), out var value), Is.True);
        Assert.That(value, Is.EqualTo("raw"));
    }

    [TestCase("\"\"\"text\"\"\"\"", "TRM0001")]
    [TestCase("\"\"\"a\"\"\"\"b\"\"\"", "TRM0001")]
    [TestCase("\"\"\"\"text\"\"\"", "TRM0002")]
    [TestCase("\"\"\"\"edge\"\"\"", "TRM0002")]
    [TestCase("\"\"\"edge\"\"\"\"", "TRM0001")]
    [TestCase("\"\"\"\"\"\"", "TRM0002")]
    [TestCase("\"\"\"first\nsecond\"\"\"", "TRM0002")]
    public void RejectsInvalidSingleLineFencesAndPhysicalLineBreaks(string spelling, string diagnosticCode)
        => AssertInvalid(spelling, diagnosticCode);

    [Test]
    public void DecodesFramedTextAndRemovesTheExactClosingIndentation()
    {
        const string spelling = "\"\"\"\n  alpha\n  beta\n  \"\"\"";
        AssertValid(spelling, "alpha\nbeta");
    }

    [Test]
    public void EmptyFramingExcludesItsSharedNewline()
    {
        AssertValid("\"\"\"\r\n\"\"\"", string.Empty);
    }

    [Test]
    public void BlankLinesRemoveOnlyThePermittedIndentationPrefix()
    {
        const string spelling = "\"\"\"\n  alpha\n \n   \n  omega\n  \"\"\"";
        AssertValid(spelling, "alpha\n\n \nomega");
    }

    [Test]
    public void EmptyClosingIndentationPreservesContentIndentation()
    {
        const string spelling = "\"\"\"\n\tindented\n\"\"\"";
        AssertValid(spelling, "\tindented");
    }

    [TestCase("\"\"\"\n  alpha\n beta\n  \"\"\"", "TRM0001")]
    [TestCase("\"\"\"\n  alpha\n\t\n  \"\"\"", "TRM0001")]
    public void RejectsContentLinesThatCannotApplyClosingIndentation(string spelling, string diagnosticCode)
        => AssertInvalid(spelling, diagnosticCode);

    [TestCase("\\n", "\n")]
    [TestCase("\\r", "\r")]
    [TestCase("\\u000B", "\v")]
    [TestCase("\\u000C", "\f")]
    [TestCase("\\u0085", "\u0085")]
    [TestCase("\\u2028", "\u2028")]
    [TestCase("\\U00002029", "\u2029")]
    [TestCase("\\r\\n", "\r\n")]
    [TestCase("\\u000D\\u000A", "\r\n")]
    [TestCase("\\u000D\\n", "\r\n")]
    public void NormalizesEveryInteriorSourceBreakUsingTheSelectedDirective(string directive, string separator)
    {
        var spelling = "\"\"\"" + " \t" + directive + "\t " + "\n"
            + "first\r\nsecond\nthird\r\n\"\"\"";
        AssertValid(spelling, "first" + separator + "second" + separator + "third");
    }

    [TestCase("\\t")]
    [TestCase("\\v")]
    [TestCase("\\f")]
    [TestCase("\\0")]
    [TestCase("\\n\\r")]
    [TestCase("\\u000A\\u000D")]
    [TestCase("\\r\\n\\n")]
    [TestCase("\\u1234")]
    public void RejectsUnsupportedOrCombinedNewlineDirectives(string directive)
        => AssertInvalid("\"\"\"" + directive + "\ntext\n\"\"\"", "TRM0001");

    [Test]
    public void MalformedNewlineDirectiveConsumesItsRawFenceBeforeLexingFollowingSource()
    {
        const string source = "\"\"\"\\t\ntext\n\"\"\" next";
        var result = Lexer.Lex(SourceText.From(source));

        Assert.That(result.Diagnostics.Length, Is.EqualTo(1));
        Assert.That(result.Diagnostics[0].Code, Is.EqualTo("TRM0001"));
        Assert.That(result.Tokens[0].Text, Is.EqualTo("\"\"\"\\t\ntext\n\"\"\""));
        Assert.That(result.Tokens[1].Kind, Is.EqualTo(TokenKind.Identifier));
        Assert.That(result.Tokens[1].Text, Is.EqualTo("next"));
        Assert.That(result.Tokens[2].Kind, Is.EqualTo(TokenKind.EndOfFile));
    }

    [Test]
    public void PreservesInteriorLineBreakSpellingsWithoutANewlineDirective()
    {
        const string spelling = "\"\"\"\nA\r\nB\u2028C\u0085D\n\"\"\"";
        AssertValid(spelling, "A\r\nB\u2028C\u0085D");
    }

    [TestCase("\r")]
    [TestCase("\n")]
    [TestCase("\r\n")]
    [TestCase("\u0085")]
    [TestCase("\u2028")]
    [TestCase("\u2029")]
    public void TreatsEachRecognizedSourceLineEndingAsOneFramingBreak(string lineEnding)
    {
        var spelling = "\"\"\"\nfirst" + lineEnding + "second" + lineEnding + "\"\"\"";
        AssertValid(spelling, "first" + lineEnding + "second");
    }

    [Test]
    public void RejectsMalformedUtf16WithoutReplacingRawSource()
    {
        var spelling = string.Concat("\"\"\"", (char)0xD800, "\"\"\"");
        var result = Lexer.Lex(SourceText.From(spelling));

        Assert.That(result.Tokens[0].Text, Is.EqualTo(spelling));
        Assert.That(result.Diagnostics.Length, Is.EqualTo(1));
        Assert.That(result.Diagnostics[0].Code, Is.EqualTo("TRM0001"));
        Assert.That(StringLiteralDecoder.TryDecode(result.Tokens[0].Text.AsSpan(), out _), Is.False);
    }

    [Test]
    public void OrdinaryStringEscapeDecodingRemainsUnchanged()
    {
        const string spelling = "\"ordinary\\ntext\"";
        var result = Lexer.Lex(SourceText.From(spelling));

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Tokens[0].Text, Is.EqualTo(spelling));
        Assert.That(StringLiteralDecoder.TryDecode(result.Tokens[0].Text.AsSpan(), out var value), Is.True);
        Assert.That(value, Is.EqualTo("ordinary\ntext"));
    }

    private static void AssertValid(string spelling, string expected)
    {
        var result = Lexer.Lex(SourceText.From(spelling));

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Tokens[0].Kind, Is.EqualTo(TokenKind.StringLiteral));
        Assert.That(result.Tokens[0].Text, Is.EqualTo(spelling));
        Assert.That(result.Tokens[1].Kind, Is.EqualTo(TokenKind.EndOfFile));
        Assert.That(StringLiteralDecoder.TryDecode(result.Tokens[0].Text.AsSpan(), out var value), Is.True);
        Assert.That(value, Is.EqualTo(expected));
    }

    private static void AssertInvalid(string spelling, string diagnosticCode)
    {
        var result = Lexer.Lex(SourceText.From(spelling));

        Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == diagnosticCode), Is.True);
        Assert.That(result.Tokens[0].Kind, Is.EqualTo(TokenKind.StringLiteral));
        Assert.That(StringLiteralDecoder.TryDecode(result.Tokens[0].Text.AsSpan(), out _), Is.False);
    }
}
