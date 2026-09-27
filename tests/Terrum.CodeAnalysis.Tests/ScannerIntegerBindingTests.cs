// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ScannerIntegerBindingTests
{
    [Fact]
    public void BoundLiteralUsesTrustedDecoderWithoutChangingOrdinaryParsing()
    {
        const string Source = "int32 Main() { return 42; }";
        var lexical = Lexer.Lex(SourceText.From(Source));
        var index = Array.FindIndex([.. lexical.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        var decoded = SyntaxTree.Parse(lexical, [new ScannerIntegerValue(index, "", false, 41)]);
        Assert.Equal(Source, decoded.Root.ToFullString());
        var compilation = Compilation.Create(decoded);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.IsType<BoundProgram>(compilation.Program).Functions[0];
        var result = Assert.IsType<BoundReturn>(Assert.Single(main.Body.Statements));
        Assert.Equal(41, Assert.IsType<BoundConstant>(result.Value).Value);

        var ordinary = Compilation.Create(SyntaxTree.Parse(Source));
        var host = Assert.IsType<BoundProgram>(ordinary.Program).Functions[0];
        var hostResult = Assert.IsType<BoundReturn>(Assert.Single(host.Body.Statements));
        Assert.Equal(42, Assert.IsType<BoundConstant>(hostResult.Value).Value);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("wrong-token")]
    [InlineData("wrong-tag")]
    [InlineData("inconsistent-status")]
    [InlineData("extra")]
    public void RejectsMissingOrIncompatibleIntegerRecords(string variant)
    {
        var lexical = Lexer.Lex(SourceText.From("int32 Main() { return 42 + 1; }"));
        var positions = lexical.Tokens.Select((token, index) => (token, index))
            .Where(static item => item.token.Kind == TokenKind.NumericLiteral)
            .Select(static item => item.index).ToArray();
        var first = new ScannerIntegerValue(positions[0], "", false, 42);
        var second = new ScannerIntegerValue(positions[1], "", false, 1);
        ImmutableArray<ScannerIntegerValue> records = variant switch {
            "missing" => [first],
            "duplicate" => [first, first, second],
            "wrong-token" => [first with { TokenIndex = positions[0] - 1 }, second],
            "wrong-tag" => [first with { Tag = "u32" }, second],
            "inconsistent-status" => [first with { IsOverflow = true }, second],
            "extra" => [first, second, second],
            _ => throw new InvalidOperationException(),
        };
        _ = Assert.Throws<ArgumentException>(() => SyntaxTree.Parse(lexical, records));
    }

    [Fact]
    public void RequiresNoIntegerRecordForFloatingFormsButRejectsAnInjectedRecord()
    {
        var lexical = Lexer.Lex(SourceText.From("int32 Main() { return 1.5; }"));
        var index = Array.FindIndex([.. lexical.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        _ = SyntaxTree.Parse(lexical, []);
        _ = Assert.Throws<ArgumentException>(() => SyntaxTree.Parse(lexical,
            [new ScannerIntegerValue(index, "", false, 1)]));
    }

    [Fact]
    public void SignedMinimumUsesDecodedMagnitudeAndOverflowKeepsRangeDiagnostic()
    {
        var minimum = Lexer.Lex(SourceText.From("public int64 Read() { return -1; }"));
        var index = Array.FindIndex([.. minimum.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(minimum,
            [new ScannerIntegerValue(index, "", false, 9223372036854775808UL)]));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var statement = Assert.IsType<BoundReturn>(
            Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions[0].Body.Statements));
        Assert.Equal(long.MinValue, Assert.IsType<BoundConstant>(statement.Value).Value);

        var overflow = Lexer.Lex(SourceText.From("public uint64 Read() { return 18446744073709551616; }"));
        var overflowIndex = Array.FindIndex([.. overflow.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        var invalid = Compilation.CreateLibrary(SyntaxTree.Parse(overflow,
            [new ScannerIntegerValue(overflowIndex, "", true, null)]));
        Assert.Contains(invalid.Diagnostics, static diagnostic => diagnostic.Code == "TRM3013");
    }
}
