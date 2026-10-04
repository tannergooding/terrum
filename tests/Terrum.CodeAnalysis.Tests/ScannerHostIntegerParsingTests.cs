// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ScannerHostIntegerParsingTests
{
    [Theory]
    [InlineData("12__34", 1234)]
    [InlineData("0x__FF_FF", 65535)]
    [InlineData("0b__10_01", 9)]
    public void BindsIntegerWithoutDecodedScannerValues(string spelling, long expected)
    {
        var lexical = Lexer.Lex(SourceText.From($"int32 Main() {{ return {spelling}; }}"));
        var compilation = Compilation.Create(SyntaxTree.Parse(lexical));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = Assert.IsType<BoundProgram>(compilation.Program).Functions[0];
        var statement = Assert.IsType<BoundReturn>(Assert.Single(function.Body.Statements));
        Assert.Equal(expected, Assert.IsType<BoundConstant>(statement.Value).Value);
    }

    [Fact]
    public void RejectsOverflowingIntegerWithoutDecodedValues()
    {
        var lexical = Lexer.Lex(SourceText.From("uint64 Main() { return 18446744073709551616; }"));
        var compilation = Compilation.Create(SyntaxTree.Parse(lexical));
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3013");
    }

    [Theory]
    [InlineData("public int32 Read() { return -2147483648; }", int.MinValue)]
    [InlineData("public int64 Read() { return -9223372036854775808; }", long.MinValue)]
    public void BindsSignedMinimumWithoutDecodedScannerValues(string source, long expected)
    {
        var lexical = Lexer.Lex(SourceText.From(source));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(lexical));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = Assert.IsType<BoundProgram>(compilation.Program).Functions[0];
        var statement = Assert.IsType<BoundReturn>(Assert.Single(function.Body.Statements));
        Assert.Equal(expected, Assert.IsType<BoundConstant>(statement.Value).Value);
    }
}
