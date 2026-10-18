// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Lexing;

public sealed class CommentFenceTests
{
    [TestCase("/* */",TriviaKind.MultiLineComment,"")]
    [TestCase("/** /* */ text **/",TriviaKind.MultiLineComment,"")]
    [TestCase("/**/",TriviaKind.MultiLineComment,"TRM0003")]
    [TestCase("/*** **/ ***/",TriviaKind.MultiLineComment,"")]
    [TestCase("//* *//",TriviaKind.DocumentationBlockComment,"")]
    [TestCase("//* */",TriviaKind.DocumentationBlockComment,"TRM0003")]
    [TestCase("//** / /* **//",TriviaKind.DocumentationBlockComment,"")]
    [TestCase("/// text",TriviaKind.DocumentationLineComment,"")]
    [TestCase("// text",TriviaKind.SingleLineComment,"")]
    [TestCase("/* **/ */",TriviaKind.MultiLineComment,"TRM0010")]
    [TestCase("//** ***// **//",TriviaKind.DocumentationBlockComment,"TRM0010")]
    [TestCase("/** /**/ **/",TriviaKind.MultiLineComment,"TRM0011")]
    [TestCase("/* \"*/",TriviaKind.MultiLineComment,"")]
    public void CommentFencesPreserveLosslessTriviaAndDiagnostics(string text, TriviaKind kind, string error)
    {
        var lexed = Lexer.Lex(SourceText.From(text));
        var comment = lexed.Tokens[0].LeadingTrivia[0];
        Assert.That(comment.Kind, Is.EqualTo(kind));
        Assert.That(string.Concat(lexed.Tokens.SelectMany(token => token.LeadingTrivia)
            .Select(trivia => trivia.Text)) + string.Concat(lexed.Tokens.Select(token => token.Text)), Is.EqualTo(text));
        Assert.That(lexed.Diagnostics.Select(diagnostic => diagnostic.Code),
            Is.EqualTo(error.Length == 0 ? Array.Empty<string>() : [error]));
    }

    [Test]
    public void MalformedSurrogatesInOpaquePayloadRetainDiagnostics()
    {
        var text = "/*" + '\uD800' + "\U0001F600*/ //**" + '\uDC00' + "**//";
        var lexed = Lexer.Lex(SourceText.From(text));
        Assert.That(lexed.Diagnostics.Select(diagnostic => diagnostic.Code), Is.EqualTo(["TRM0001", "TRM0001"]));
        Assert.That(lexed.Diagnostics.Select(diagnostic => diagnostic.Span.Start), Is.EqualTo([2, 12]));
        Assert.That(string.Concat(lexed.Tokens.SelectMany(token => token.LeadingTrivia)
            .Select(trivia => trivia.Text)) + string.Concat(lexed.Tokens.Select(token => token.Text)), Is.EqualTo(text));
    }
    [TestCase("/* *//",TriviaKind.MultiLineComment)]
    [TestCase("//* *///",TriviaKind.DocumentationBlockComment)]
    public void ClosingFenceConsumesOnlyItsRequiredSlashes(string text, TriviaKind kind)
    {
        var result = Lexer.Lex(SourceText.From(text));
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Tokens[0].LeadingTrivia[0].Kind, Is.EqualTo(kind));
        Assert.That(result.Tokens[0].Text, Is.EqualTo("/"));
        Assert.That(result.Tokens[0].Span.Start, Is.EqualTo(text.Length - 1));
        Assert.That(result.Tokens[1].Kind, Is.EqualTo(TokenKind.EndOfFile));
    }
    [TestCase("/* **/ */",3,3)]
    [TestCase("//** ***// **//",5,5)]
    public void OverlongFenceReportsTheWholeRunAndRequiredSuffix(string source, int start, int length)
    {
        var result = Lexer.Lex(SourceText.From(source));
        var diagnostic = global::System.Linq.Enumerable.Single(result.Diagnostics);
        Assert.That(diagnostic.Code, Is.EqualTo("TRM0010"));
        Assert.That(diagnostic.Span, Is.EqualTo(new TextSpan(start, length)));
        Assert.That(result.Tokens[0].LeadingTrivia[0].Text, Is.EqualTo(source));
    }

    [Test]
    public void RejectedFenceWithoutLaterCloserAlsoReportsUnterminatedComment()
    {
        var result = Lexer.Lex(SourceText.From("/* **/"));
        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Code), Is.EqualTo(["TRM0010", "TRM0003"]));
        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Span), Is.EqualTo([new TextSpan(3, 3), new TextSpan(0, 6)]));
        Assert.That(result.Tokens[0].LeadingTrivia[0].Text, Is.EqualTo("/* **/"));
    }

    [Test]
    public void DocumentationTriviaAtEndOfFileRemainsInTheLosslessTree()
    {
        const string Source = "int32 Main() { return 42; } /// line\r\n//** block **//";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(Source));
        Assert.That(tree.Tokens[^1].LeadingTrivia[^1].Kind, Is.EqualTo(TriviaKind.DocumentationBlockComment));
        Assert.That(tree.Diagnostics, Is.Empty);
    }
    [TestCase("/* **/ */")]
    [TestCase("//** ***// **//")]
    [TestCase("/** /* */ text **/")]
    [TestCase("//* */")]
    [TestCase("x/*a*/ / /** /**/ **/")]
    public Task CompiledScannerRetainsFenceKindsAndExactErrorSpansAsync(string source) =>
        ScannerTests.CheckScannerAsync(source);

    [Test]
    public Task CompiledDocumentationBlockPreservesMalformedSurrogateAsync() =>
        ScannerTests.CheckScannerAsync("//**" + '\uD800' + "\r\n\U0001F600**//");

    [Test]
    public async Task MaximumSourceBoundAndDenseStarRunsRemainInBoundsAsync()
    {
        var exact = "/*" + new string('*', ScannerHost.MaximumSourceLength - 4) + "*/";
        var lexed = Lexer.Lex(SourceText.From(exact));
        Assert.That(global::System.Linq.Enumerable.Single(lexed.Diagnostics).Code, Is.EqualTo("TRM0003"));
        Assert.That(ScannerHost.CreateBuffers(lexed.Source)[0].Values.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        await ScannerTests.CheckScannerAsync(exact).ConfigureAwait(true);
    }
    [TestCase(1)]
    [TestCase(2)]
    public Task MaximumBalancedFenceExecutesWithoutAStarCountQuotaAsync(int slashes)
    {
        var fence = new string('*', (ScannerHost.MaximumSourceLength / 2) - slashes - 1);
        var suffix = new string('/', slashes);
        var source = suffix + fence + "  " + fence + suffix;
        Assert.That(source.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        Assert.That(Lexer.Lex(SourceText.From(source)).Diagnostics, Is.Empty);
        return ScannerTests.CheckScannerAsync(source);
    }

    [Test]
    public void OldFixedFenceRecordsCannotMasqueradeAsCurrentScannerOutput()
    {
        var source = SourceText.From("/* **/ */");
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 3,
            Encode([(int)TokenKind.Star, 7, 1, (int)TokenKind.Slash, 8, 1, (int)TokenKind.EndOfFile, 9, 0]),
            Encode([2, (int)TriviaKind.MultiLineComment, 0, 6, 0, (int)TriviaKind.Whitespace, 6, 1, 0]),
            Encode([0])));

        static byte[] Encode(int[] words)
        {
            var bytes = new byte[words.Length * sizeof(int)];
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), words[index]);
            }
            return bytes;
        }
    }
}
