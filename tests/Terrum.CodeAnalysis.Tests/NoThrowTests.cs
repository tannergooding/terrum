// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NoThrowTests
{
    [Fact]
    public void ParameterlessAttributesRetainLosslessSyntaxAndTraversal()
    {
        const string Source = "[ /* contract */ NoThrow ] public int32 Value() { return 42; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors);
        Assert.Equal(Source, tree.Root.ToFullString());
        var attribute = Assert.Single(tree.Root.DescendantNodes(), static node => node.Kind == SyntaxKind.Attribute);
        Assert.Equal(3, attribute.Children.Length);
        Assert.Equal("NoThrow", Assert.IsType<SyntaxTokenElement>(attribute.Children[1]).Token.Text);
    }

    [Theory]
    [InlineData("[NoThrow")]
    [InlineData("[NoThrow(1)] void F() { }")]
    [InlineData("[] void F() { }")]
    [InlineData("[[NoThrow]] void F() { }")]
    public void MalformedAttributesRemainLossless(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.True(tree.HasErrors);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("[NoThrow] [NoThrow] int32 F() { return 0; }")]
    [InlineData("[Unknown] int32 F() { return 0; }")]
    [InlineData("[NoThrow] struct S { public int32 Value; }")]
    [InlineData("struct S { [NoThrow] public int32 Value; }")]
    public void UnknownDuplicateAndMisplacedContractsAreRejected(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3001");
    }

    [Theory]
    [InlineData("[NoThrow] void F() { require true || false; }")]
    [InlineData("[NoThrow] int32 F() { return -1; }")]
    [InlineData("[NoThrow] int32 F() { return -2147483648; }")]
    [InlineData("[NoThrow] int64 F() { return -9223372036854775808; }")]
    [InlineData("[NoThrow] bool F(int32 a, int32 b) { return a < b || a == b; }")]
    [InlineData("[NoThrow] int32 F(int32 a, int32 b) { return +(a & b) ^ ~a; }")]
    [InlineData("[NoThrow] int64 F(int32 value) { return (int64)value; }")]
    [InlineData("[NoThrow] int64 F(uint32 value) { return (int64)value; }")]
    [InlineData("[NoThrow] uint64 F(uint32 value) { return (uint64)value; }")]
    [InlineData("[NoThrow] uint32 F(char16 value) { return (uint32)value; }")]
    [InlineData("[NoThrow] int32 F(char16 value) { return (int32)value; }")]
    [InlineData("[NoThrow] int64 F(char16 value) { return (int64)value; }")]
    [InlineData("[NoThrow] uint64 F(char16 value) { return (uint64)value; }")]
    [InlineData("[NoThrow] char16 F(char16 value) { return (char16)value; }")]
    [InlineData("[NoThrow] bool F(bool success, out int32 value) { if (!success) { return false; } value = 42; return true; }")]
    [InlineData("[NoThrow] int32 F(bool choose) { if (choose) { return G(false); } return 42; } [NoThrow] int32 G(bool choose) { return F(choose); }")]
    public void InfallibleOperationsSatisfyDeclaredContracts(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.All(program.Functions, static function => Assert.True(function.IsNoThrow));
        SemanticVerifier.VerifyLibrary(program);
    }

    [Theory]
    [InlineData("int32 F(int32 value) { return value + 1; }")]
    [InlineData("int32 F(int32 value) { return value - 1; }")]
    [InlineData("int64 F(int64 value) { return value * 2; }")]
    [InlineData("uint64 F(uint64 value) { return value / 2; }")]
    [InlineData("int32 F(int32 value) { return value % 2; }")]
    [InlineData("int32 F(int32 value) { return -value; }")]
    [InlineData("int32 F() { return -int32.MinValue; }")]
    [InlineData("int64 F() { return -int64.MinValue; }")]
    [InlineData("char16 F(int32 value) { return (char16)value; }")]
    [InlineData("uint32 F(int32 value) { return (uint32)value; }")]
    [InlineData("int32 F(uint32 value) { return (int32)value; }")]
    [InlineData("uint64 F(int64 value) { return (uint64)value; }")]
    [InlineData("int64 F(uint64 value) { return (int64)value; }")]
    [InlineData("int32 F(int64 value) { return (int32)value; }")]
    [InlineData("void F(bool success) { require success; }")]
    [InlineData("int32 F() { return G(); } int32 G() { return 42; }")]
    public void SourceAndPublicIrCannotAssertUnprovedNoThrowContracts(string source)
    {
        var syntax = SyntaxTree.Parse("[NoThrow] " + source + " int32 Main() { return 0; }");
        Assert.False(syntax.HasErrors);
        var invalid = Compilation.Create(syntax);
        Assert.True(invalid.HasErrors);
        Assert.Null(invalid.Program);
        var diagnostic = Assert.Single(invalid.Diagnostics);
        Assert.Equal("TRM3017", diagnostic.Code);
        Assert.True(diagnostic.Span.Length > 0);

        var baseline = Compilation.Create(SyntaxTree.Parse(source + " int32 Main() { return 0; }"));
        Assert.False(baseline.HasErrors, string.Join(Environment.NewLine, baseline.Diagnostics));
        var program = Assert.IsType<BoundProgram>(baseline.Program);
        var index = program.Functions.IndexOf(program.Functions.Single(static function => function.Name == "F"));
        var forged = program with { Functions = program.Functions.SetItem(index, program.Functions[index] with { IsNoThrow = true }) };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("return Host.Data[1];")]
    [InlineData("Host.Data[1] = 1; return 0;")]
    [InlineData("int32 index = 0; return Host.Data[index];")]
    public void CheckedBufferAccessIsNotAnImplicitNoThrowCapability(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("[NoThrow] int32 Main() { " + body + " }"),
            [new HostBuffer("Data", PrimitiveType.Int32, [42], true)]);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3017");
    }

    [Fact]
    public void NoThrowDoesNotWaiveConditionalOutputAssignment()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] bool Try(bool success, out int32 value) {
                if (!success) { return false; }
                value = 42; return true;
            }
            [NoThrow] int32 Main() {
                if (Try(false, out var value)) { }
                return value;
            }
            """));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3016");
    }
}
