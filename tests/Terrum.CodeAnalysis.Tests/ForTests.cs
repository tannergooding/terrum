// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ForTests
{
    [Theory]
    [InlineData("for (mut int32 i = 0; i < 3; i = i + 1) { }")]
    [InlineData("for (/* init */;/* condition */;/* step */) { return; }")]
    [InlineData("for (Start(); Next(out var value); Finish(value)) { Use(value); }")]
    public void ForSyntaxPreservesHeaderClausesAndTrivia(string statement)
    {
        var text = "void F() { " + statement + " }";
        var tree = SyntaxTree.Parse(text);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(text, tree.Root.ToFullString());
        var loop = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.ForStatement);
        Assert.Equal(9, loop.Children.Length);
        Assert.Equal(SyntaxKind.Block, Assert.IsType<SyntaxNode>(loop.Children[8]).Kind);
        Assert.DoesNotContain(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.MissingExpression);
    }

    [Theory]
    [InlineData("for (mut int32 i = 0 i < 3; i = i + 1) { }")]
    [InlineData("for (mut int32 i = 0; i < 3; i = i + 1 { }")]
    [InlineData("for (; true;) return;")]
    [InlineData("for (mut int32 i = 0; i < 3; mut int32 j = 1) { }")]
    [InlineData("for (")]
    public void MalformedForSyntaxRemainsLossless(string statement)
    {
        var text = "void F() { " + statement + " } void Later() { }";
        var tree = SyntaxTree.Parse(text);
        Assert.True(tree.HasErrors);
        Assert.Equal(text, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("for (mut int32 i = 0; i < 1; i = i + 1) { } return i;")]
    [InlineData("for (var i = 0; false;) { } return 0;")]
    [InlineData("for (int32 i = 0; i < 1; i = i + 1) { } return 0;")]
    [InlineData("for (; 1i32;) { } return 0;")]
    [InlineData("for (; false; i = 1) { mut int32 i = 0; } return 0;")]
    [InlineData("for (; false; 1i32 + 2) { } return 0;")]
    [InlineData("mut int32 i = 0; for (mut int32 i = 1; false;) { } return 0;")]
    public void ForLoopsPreserveScopeTypeAndUnsupportedControlRules(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { " + body + " }"));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("mut int32 value = 7; for (; Clear(out value);) { } return value;")]
    [InlineData("mut int32 value = 7; for (mut int32 i = 0; i < 2; Clear(out value)) { Use(value); } return 0;")]
    [InlineData("for (; Clear(out var value);) { } return value;")]
    public void ForLoopsRetainConditionalOutputVacancy(string body)
    {
        var source = "bool Clear(out int32 value) { return false; } void Use(int32 value) { } int32 Main() { " + body + " }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ForLoopLowersToTheExistingVerifiedModuleRepresentation()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Sum() { mut int32 sum = 0; for (mut int32 i = 0; i < 4; i = i + 1) { sum = sum + i; } return sum; }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var scope = Assert.IsType<BoundBlock>(program.Functions[0].Body.Statements[1]);
        _ = Assert.IsType<BoundLocal>(scope.Statements[0]);
        var loop = Assert.IsType<BoundWhile>(scope.Statements[1]);
        Assert.Equal(2, Assert.IsType<BoundBlock>(loop.Body).Statements.Length);
        var producer = ModuleCompiler.CreateProducer(program, ["Sum"]);
        var consumer = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Sum(); }"),
            ModuleCompiler.CreateReference(producer));
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumer.Program), producer);
        SemanticVerifier.Verify(ModuleCompiler.Link(artifact, producer));
    }
}
