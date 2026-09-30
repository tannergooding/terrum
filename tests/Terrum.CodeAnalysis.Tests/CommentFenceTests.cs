// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CommentFenceTests
{
    [Theory]
    [InlineData("/* */", TriviaKind.MultiLineComment, "")]
    [InlineData("/** /* */ text **/", TriviaKind.MultiLineComment, "")]
    [InlineData("/**/", TriviaKind.MultiLineComment, "TRM0003")]
    [InlineData("/*** **/ ***/", TriviaKind.MultiLineComment, "")]
    [InlineData("//* *//", TriviaKind.DocumentationBlockComment, "")]
    [InlineData("//* */", TriviaKind.DocumentationBlockComment, "TRM0003")]
    [InlineData("//** / /* **//", TriviaKind.DocumentationBlockComment, "")]
    [InlineData("/// text", TriviaKind.DocumentationLineComment, "")]
    [InlineData("// text", TriviaKind.SingleLineComment, "")]
    [InlineData("/* **/ */", TriviaKind.MultiLineComment, "TRM0010")]
    [InlineData("//** ***// **//", TriviaKind.DocumentationBlockComment, "TRM0010")]
    [InlineData("/** /**/ **/", TriviaKind.MultiLineComment, "")]
    [InlineData("/* \"*/", TriviaKind.MultiLineComment, "")]
    public void CommentFencesPreserveLosslessTriviaAndDiagnostics(string text, TriviaKind kind, string error)
    {
        var lexed = Lexer.Lex(SourceText.From(text));
        var comment = lexed.Tokens[0].LeadingTrivia[0];
        Assert.Equal(kind, comment.Kind);
        Assert.Equal(text, string.Concat(lexed.Tokens.SelectMany(token => token.LeadingTrivia)
            .Select(trivia => trivia.Text)) + string.Concat(lexed.Tokens.Select(token => token.Text)));
        Assert.Equal(error.Length == 0 ? [] : [error], lexed.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void MalformedSurrogatesInOpaquePayloadRetainDiagnostics()
    {
        var text = "/*" + '\uD800' + "\U0001F600*/ //**" + '\uDC00' + "**//";
        var lexed = Lexer.Lex(SourceText.From(text));
        Assert.Equal(["TRM0001", "TRM0001"], lexed.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Equal([2, 12], lexed.Diagnostics.Select(diagnostic => diagnostic.Span.Start));
        Assert.Equal(text, string.Concat(lexed.Tokens.SelectMany(token => token.LeadingTrivia)
            .Select(trivia => trivia.Text)) + string.Concat(lexed.Tokens.Select(token => token.Text)));
    }

    [Theory]
    [InlineData("/* *//", TriviaKind.MultiLineComment)]
    [InlineData("//* *///", TriviaKind.DocumentationBlockComment)]
    public void ClosingFenceConsumesOnlyItsRequiredSlashes(string text, TriviaKind kind)
    {
        var result = Lexer.Lex(SourceText.From(text));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(kind, result.Tokens[0].LeadingTrivia[0].Kind);
        Assert.Equal("/", result.Tokens[0].Text);
        Assert.Equal(text.Length - 1, result.Tokens[0].Span.Start);
        Assert.Equal(TokenKind.EndOfFile, result.Tokens[1].Kind);
    }

    [Theory]
    [InlineData("/* **/ */", 3, 3)]
    [InlineData("//** ***// **//", 5, 5)]
    public void OverlongFenceReportsTheWholeRunAndRequiredSuffix(string source, int start, int length)
    {
        var result = Lexer.Lex(SourceText.From(source));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TRM0010", diagnostic.Code);
        Assert.Equal(new TextSpan(start, length), diagnostic.Span);
        Assert.Equal(source, result.Tokens[0].LeadingTrivia[0].Text);
    }

    [Fact]
    public void RejectedFenceWithoutLaterCloserAlsoReportsUnterminatedComment()
    {
        var result = Lexer.Lex(SourceText.From("/* **/"));
        Assert.Equal(["TRM0010", "TRM0003"], result.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Equal([new TextSpan(3, 3), new TextSpan(0, 6)],
            result.Diagnostics.Select(diagnostic => diagnostic.Span));
        Assert.Equal("/* **/", result.Tokens[0].LeadingTrivia[0].Text);
    }

    [Fact]
    public void DocumentationTriviaAtEndOfFileRemainsInTheLosslessTree()
    {
        const string Source = "int32 Main() { return 42; } /// line\r\n//** block **//";
        var tree = SyntaxTree.Parse(Source);
        Assert.Equal(Source, tree.Root.ToFullString());
        Assert.Equal(TriviaKind.DocumentationBlockComment,
            tree.Tokens[^1].LeadingTrivia[^1].Kind);
        Assert.Empty(tree.Diagnostics);
    }

    [Theory]
    [InlineData("/* **/ */")]
    [InlineData("//** ***// **//")]
    [InlineData("/** /* */ text **/")]
    [InlineData("//* */")]
    [InlineData("x/*a*/ / /** /**/ **/")]
    public Task CompiledScannerRetainsFenceKindsAndExactErrorSpansAsync(string source) =>
        ScannerTests.CheckScannerAsync(source);

    [Fact]
    public Task CompiledDocumentationBlockPreservesMalformedSurrogateAsync() =>
        ScannerTests.CheckScannerAsync("//**" + '\uD800' + "\r\n\U0001F600**//");

    [Fact]
    public async Task MaximumSourceBoundAndDenseStarRunsRemainInBoundsAsync()
    {
        var exact = "/*" + new string('*', ScannerHost.MaximumSourceLength - 4) + "*/";
        var lexed = Lexer.Lex(SourceText.From(exact));
        Assert.Equal("TRM0003", Assert.Single(lexed.Diagnostics).Code);
        Assert.Equal(ScannerHost.MaximumSourceLength, ScannerHost.CreateBuffers(lexed.Source)[0].Values.Length);
        await ScannerTests.CheckScannerAsync(exact).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task MaximumBalancedFenceExecutesWithoutAStarCountQuotaAsync(int slashes)
    {
        var fence = new string('*', (ScannerHost.MaximumSourceLength / 2) - slashes - 1);
        var suffix = new string('/', slashes);
        var source = suffix + fence + "  " + fence + suffix;
        Assert.Equal(ScannerHost.MaximumSourceLength, source.Length);
        Assert.Empty(Lexer.Lex(SourceText.From(source)).Diagnostics);
        return ScannerTests.CheckScannerAsync(source);
    }

    [Fact]
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
