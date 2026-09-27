// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class LiteralSyntaxTests
{
    [Theory]
    [InlineData("int32 Main() { return int32(42); }")]
    [InlineData("int32 Main() { int64 wide = int64(50000 * 50000); return int32(wide / 2500000000); }")]
    [InlineData("int32 Main() { int64 min = int64(-9223372036854775808); if (min == int64.MinValue) { return 42; } return 0; }")]
    [InlineData("int32 Main() { uint64 wide = uint64(18446744073709551615); if (wide == uint64.MaxValue) { return 42; } return 0; }")]
    [InlineData("struct Box { public int32 Value; public Box(int32 value) { Value = value; } } int32 Main() { Box box = new Box(42); return box.Value; }")]
    public void BindsTypeCallAndContextualLiterals(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData("int32 Main() { return (int32)42; }")]
    [InlineData("int32 Main() { return 42i32; }")]
    [InlineData("int32 Main() { return int32(42, 1); }")]
    [InlineData("int32 Main() { return int32(value: 42); }")]
    [InlineData("int32 Main() { return int32(out var value); }")]
    [InlineData("int32 Main() { mut int32 value = 42; return int32(ref value); }")]
    [InlineData("int32 Main() { return int32(); }")]
    [InlineData("int32 Main() { return int32(true); }")]
    [InlineData("int32 Main() { var value = 42; return 0; }")]
    [InlineData("int32 Main() { int64 value = int32(50000) * 50000; return 0; }")]
    [InlineData("struct Box { public int32 Value; public Box(int32 value) { Value = value; } } int32 Main() { Box box = Box(42); return 0; }")]
    [InlineData("int32 Main() { return new int32(42); }")]
    [InlineData("int32 Main() { int32 int64 = 1; return int64(42); }")]
    public void RejectsLegacyAndMalformedTypeCalls(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
    }

    [Theory]
    [InlineData("int32 Main() { return 1i32; }", "TRM0006")]
    [InlineData("int32 Main() { return 1u64; }", "TRM0006")]
    [InlineData("int32 Main() { return 1.0f32; }", "TRM0006")]
    [InlineData("int32 Main() { return 0x; }", "TRM0007")]
    [InlineData("int32 Main() { return 1_000; }", "TRM1001")]
    public void RejectsAttachedTagsAndUnsupportedSeparators(string source, string diagnostic)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Contains(tree.Diagnostics, error => error.Code == diagnostic);
    }

    [Fact]
    public void OrdinaryNamedFunctionTakesPrecedenceOverConversionTarget()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 int32(int32 value) { return value + 1; } int32 Main() { return int32(41); }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.IsType<BoundProgram>(compilation.Program).Functions
            .Single(function => function.Name == "Main");
        var result = Assert.IsType<BoundReturn>(Assert.Single(main.Body.Statements));
        _ = Assert.IsType<BoundCall>(result.Value);
    }
}
