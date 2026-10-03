// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Threading;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CompilationTests
{
    [Theory]
    [InlineData("int32 Main() { return 1 + 2 * 3; }")]
    [InlineData("void Main() { return (); }")]
    [InlineData("int32 Main() { var x = int32(3); return x; }")]
    [InlineData("int32 Main() { int32 x = -2147483648; return x % -1; }")]
    [InlineData("int32 Main() { var x = true; if (x) { return 1; } else { return 0; } }")]
    [InlineData("int32 Main() { if (true) { return 1; } }")]
    [InlineData("int32 Main() { while (true) { } }")]
    [InlineData("int32 Main() { var x = F(4); return x; } int32 F(int32 value) { return value; }")]
    [InlineData("int32 Main() { int32 x = 1; var y = (2 + 3) + x; return y; }")]
    [InlineData("int32 Main() { mut var x = int32(0); while (x < 10) { x = x + 1; } return x; }")]
    [InlineData("void Main() { require Ready(); } bool Ready() { return true; }")]
    [InlineData("struct S { int32 X; } int32 Main() { return 0; }")]
    public void BindsTheExecutableProfile(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        Assert.Same(tree, compilation.SyntaxTree);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Theory]
    [InlineData("int32 F() { return 1; }")]
    [InlineData("int32 Main(int32 value) { return value; }")]
    [InlineData("bool Main() { return true; }")]
    [InlineData("int32 Main() { }")]
    [InlineData("int32 Main() { if (false) { return 1; } }")]
    [InlineData("int32 Main() { var value = 1; return value; }")]
    [InlineData("int32 Main() { var value = 1 + 2; return value; }")]
    [InlineData("int32 Main() { return uint32(1); }")]
    [InlineData("int32 Main() { return 2147483648; }")]
    [InlineData("int32 Main() { return -2147483649; }")]
    [InlineData("int32 Main() { return 1.5; }")]
    [InlineData("int32 Main() { return true; }")]
    [InlineData("int32 Main() { if (1) { return 0; } return 1; }")]
    [InlineData("int32 Main() { var x = int32(1); x = 2; return x; }")]
    [InlineData("int32 Main() { mut bool x = true; x = 2; return 0; }")]
    [InlineData("int32 Main() { int32 x = x; return x; }")]
    [InlineData("int32 Main() { return missing; }")]
    [InlineData("int32 Main() { { int32 hidden = 1; } return hidden; }")]
    [InlineData("int32 Main() { int32 x = 1; int32 x = 2; return x; }")]
    [InlineData("int32 Main() { return 1; } int32 Main() { return 2; }")]
    [InlineData("int32 F(int32 a, int32 a) { return a; } int32 Main() { return 0; }")]
    [InlineData("void Main() { return 1; }")]
    [InlineData("int32 Main() { return; }")]
    [InlineData("int32 Main() { return F(); } int32 F(int32 x) { return x; }")]
    [InlineData("int32 Main() { return F(true); } int32 F(int32 x) { return x; }")]
    [InlineData("int32 Main() { return F(out var x); } int32 F(int32 value) { return value; }")]
    [InlineData("int32 Main() { int32? x = 1; return 0; }")]
    [InlineData("int32 Main() { int128 x = 1; return 0; }")]
    [InlineData("int32 Main() { var x = \"text\"; return 0; }")]
    [InlineData("int32 Main() { 1 + 2; return 0; }")]
    [InlineData("int32 Main() { return true + false; }")]
    [InlineData("int32 Main() { if (int32(1) == true) { return 0; } return 1; }")]
    [InlineData("void Main() { require 1; }")]
    [InlineData("void Main() { require int32(1); }")]
    [InlineData("void Main() { require 1 < 2; }")]
    [InlineData("void Main() { require (); }")]
    public void InvalidOrUnsupportedProgramsNeverProduceBoundOutput(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        var compilation = Compilation.Create(tree);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.NotEmpty(compilation.Diagnostics);
        Assert.All(compilation.Diagnostics, diagnostic => {
            Assert.InRange(diagnostic.Span.Start, 0, source.Length);
            Assert.InRange(diagnostic.Span.End, diagnostic.Span.Start, source.Length);
        });
        var repeated = Compilation.Create(tree);
        Assert.Equal(compilation.Diagnostics.ToArray(), repeated.Diagnostics.ToArray());
    }

    [Fact]
    public void SyntaxErrorsAndCancellationDoNotBecomeSuccessfulCompilations()
    {
        var tree = SyntaxTree.Parse("int32 Main( { @");
        var compilation = Compilation.Create(tree);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Equal(tree.Diagnostics, compilation.Diagnostics);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _ = Assert.Throws<OperationCanceledException>(() => Compilation.Create(
            SyntaxTree.Parse("void Main() {}"), cancellation.Token));
    }

    [Fact]
    public void LongExpressionChainsDiagnoseRatherThanOverflowTheHostStack()
    {
        var tree = SyntaxTree.Parse("int32 Main() { return " + string.Join(" + ", Enumerable.Repeat("1", 5000)) + "; }");
        Assert.False(tree.HasErrors);
        var compilation = Compilation.Create(tree);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }
}
