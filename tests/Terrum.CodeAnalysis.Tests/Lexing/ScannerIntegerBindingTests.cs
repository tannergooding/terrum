// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Lexing;

public sealed class ScannerIntegerBindingTests
{
    [Test]
    public void BoundLiteralUsesTrustedDecoderWithoutChangingOrdinaryParsing()
    {
        const string Source = "int32 Main() { return 42; }";
        var lexical = Lexer.Lex(SourceText.From(Source));
        var index = Array.FindIndex([.. lexical.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        var decoded = SyntaxTree.Parse(lexical, [new ScannerIntegerValue(index, "", false, 41)]);
        Assert.That(decoded.Root.ToFullString(), Is.EqualTo(Source));
        var compilation = Compilation.Create(decoded);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))().Functions[0];
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual1 = global::System.Linq.Enumerable.Single(main.Body.Statements); Assert.That(__nunitActual1, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual1!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual2 = result.Value; Assert.That(__nunitActual2, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual2!; }))().Value, Is.EqualTo(41));

        var ordinary = Compilation.Create(SyntaxTree.Parse(Source));
        var host = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = ordinary.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))().Functions[0];
        var hostResult = ((global::System.Func<BoundReturn>)(() => { var __nunitActual4 = global::System.Linq.Enumerable.Single(host.Body.Statements); Assert.That(__nunitActual4, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual4!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual5 = hostResult.Value; Assert.That(__nunitActual5, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual5!; }))().Value, Is.EqualTo(42));
    }

    [Test]
    public void HexLiteralUsesTrustedDecoderWithoutChangingOrdinaryParsing()
    {
        const string Source = "int32 Main() { return 0x2A; }";
        var lexical = Lexer.Lex(SourceText.From(Source));
        var index = Array.FindIndex([.. lexical.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        var decoded = Compilation.Create(SyntaxTree.Parse(lexical,
            [new ScannerIntegerValue(index, "", false, 41)]));
        Assert.That(decoded.HasErrors, Is.False, string.Join(Environment.NewLine, decoded.Diagnostics));
        var main = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = decoded.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))().Functions[0];
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual7 = global::System.Linq.Enumerable.Single(main.Body.Statements); Assert.That(__nunitActual7, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual7!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual8 = result.Value; Assert.That(__nunitActual8, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual8!; }))().Value, Is.EqualTo(41));

        var ordinary = Compilation.Create(SyntaxTree.Parse(Source));
        var host = ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = ordinary.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))().Functions[0];
        var hostResult = ((global::System.Func<BoundReturn>)(() => { var __nunitActual10 = global::System.Linq.Enumerable.Single(host.Body.Statements); Assert.That(__nunitActual10, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual10!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual11 = hostResult.Value; Assert.That(__nunitActual11, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual11!; }))().Value, Is.EqualTo(42));
    }

    [Test]
    public void BinaryLiteralUsesTrustedDecoderWithoutChangingOrdinaryParsing()
    {
        const string Source = "int32 Main() { return 0b101010; }";
        var lexical = Lexer.Lex(SourceText.From(Source));
        var index = Array.FindIndex([.. lexical.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        var decoded = Compilation.Create(SyntaxTree.Parse(lexical,
            [new ScannerIntegerValue(index, "", false, 41)]));
        Assert.That(decoded.HasErrors, Is.False, string.Join(Environment.NewLine, decoded.Diagnostics));
        var main = ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = decoded.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))().Functions[0];
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual13 = global::System.Linq.Enumerable.Single(main.Body.Statements); Assert.That(__nunitActual13, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual13!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual14 = result.Value; Assert.That(__nunitActual14, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual14!; }))().Value, Is.EqualTo(41));

        var ordinary = Compilation.Create(SyntaxTree.Parse(Source));
        var host = ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = ordinary.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))().Functions[0];
        var hostResult = ((global::System.Func<BoundReturn>)(() => { var __nunitActual16 = global::System.Linq.Enumerable.Single(host.Body.Statements); Assert.That(__nunitActual16, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual16!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual17 = hostResult.Value; Assert.That(__nunitActual17, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual17!; }))().Value, Is.EqualTo(42));
    }
    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("wrong-token")]
    [TestCase("wrong-tag")]
    [TestCase("inconsistent-status")]
    [TestCase("extra")]
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
        _ = Assert.Throws<ArgumentException>(() => SyntaxTree.Parse(lexical, records))!;
    }

    [Test]
    public void RequiresNoIntegerRecordForFloatingFormsButRejectsAnInjectedRecord()
    {
        var lexical = Lexer.Lex(SourceText.From("int32 Main() { return 1.5; }"));
        var index = Array.FindIndex([.. lexical.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        _ = SyntaxTree.Parse(lexical, []);
        _ = Assert.Throws<ArgumentException>(() => SyntaxTree.Parse(lexical,
            [new ScannerIntegerValue(index, "", false, 1)]))!;
    }
    [TestCase("0x")]
    [TestCase("0XG")]
    [TestCase("0X")]
    [TestCase("0x___")]
    [TestCase("0b")]
    [TestCase("0B")]
    [TestCase("0b___")]
    public void RequiresNoRecordForMalformedHexForms(string spelling)
    {
        var lexical = Lexer.Lex(SourceText.From($"int32 Main() {{ return {spelling}; }}"));
        var index = Array.FindIndex([.. lexical.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        _ = SyntaxTree.Parse(lexical, []);
        _ = Assert.Throws<ArgumentException>(() => SyntaxTree.Parse(lexical,
            [new ScannerIntegerValue(index, "", false, 1)]))!;
    }

    [Test]
    public void SignedMinimumUsesDecodedMagnitudeAndOverflowKeepsRangeDiagnostic()
    {
        var minimum = Lexer.Lex(SourceText.From("public int64 Read() { return -1; }"));
        var index = Array.FindIndex([.. minimum.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(minimum,
            [new ScannerIntegerValue(index, "", false, 9223372036854775808UL)]));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var statement = ((global::System.Func<BoundReturn>)(() => { var __nunitActual19 = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual18 = compilation.Program; Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual18!; }))().Functions[0].Body.Statements); Assert.That(__nunitActual19, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual19!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual20 = statement.Value; Assert.That(__nunitActual20, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual20!; }))().Value, Is.EqualTo(long.MinValue));

        var overflow = Lexer.Lex(SourceText.From("public uint64 Read() { return 18446744073709551616; }"));
        var overflowIndex = Array.FindIndex([.. overflow.Tokens], static token => token.Kind == TokenKind.NumericLiteral);
        var invalid = Compilation.CreateLibrary(SyntaxTree.Parse(overflow,
            [new ScannerIntegerValue(overflowIndex, "", true, null)]));
        Assert.That(global::System.Linq.Enumerable.Any(invalid.Diagnostics,static diagnostic => diagnostic.Code == "TRM3013"),Is.True);
    }
}
