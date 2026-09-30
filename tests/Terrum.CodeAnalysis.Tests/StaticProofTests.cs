// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProofTests
{
    [Fact]
    public void ProvenBodyChecksEraseWhileOrdinaryRequireRemainsExecutable()
    {
        const string Source = """
            [ConstEval] bool True() { return true; }
            int32 Main() {
                const bool known = True();
                static require known;
                static ensure True();
                require known;
                return 42;
            }
            """;
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(Source, tree.Root.ToFullString());
        _ = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.StaticRequireStatement);
        _ = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.StaticEnsureStatement);
        var version = Assert.IsAssignableFrom<System.Reflection.FieldInfo>(
            typeof(SyntaxTree).GetField(nameof(SyntaxTree.LanguageVersion)));
        Assert.Equal("syntax-31", version.GetRawConstantValue());

        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Main");
        Assert.Collection(main.Body.Statements,
            statement => Assert.IsType<BoundLocal>(statement),
            statement => Assert.IsType<BoundRequire>(statement),
            statement => Assert.IsType<BoundReturn>(statement));
    }

    [Fact]
    public void OuterEnsureIsIndependentOfEarlierNormalReturnPaths()
    {
        const string Source = """
            int32 Main() {
                if (true) { return 42; }
                static ensure true;
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions);
        Assert.DoesNotContain(main.Body.Statements,
            statement => statement.GetType().Name.Contains("Static", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("static require false;", "TRM3025", "evaluates to false")]
    [InlineData("static ensure false;", "TRM3025", "evaluates to false")]
    [InlineData("static require input == 42;", "TRM3026", "not established")]
    [InlineData("static ensure input == 42;", "TRM3026", "not established")]
    [InlineData("static require int32(1) / int32(0) == 0;", "TRM3026", "checked arithmetic")]
    [InlineData("static require Recurse(100) == 0;", "TRM3026", "call depth")]
    [InlineData("static require Runtime();", "TRM3027", "unsupported effects")]
    [InlineData("static ensure Runtime();", "TRM3027", "unsupported effects")]
    [InlineData("const bool known = true; static ensure known;", "TRM3026", "not established")]
    public void FalseUnknownAndUnsupportedProofsHaveDistinctDiagnostics(string statement, string code, string text)
    {
        var source = """
            [ConstEval] int32 Recurse(int32 remaining) {
                if (remaining == 0) { return 0; }
                return 1 + Recurse(remaining - 1);
            }
            bool Runtime() { return true; }
            int32 Verify(int32 input) { STATEMENT return 42; }
            int32 Main() { return Verify(42); }
            """.Replace("STATEMENT", statement, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code
            && diagnostic.Message.Contains(text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("if (true) { static ensure true; }", "TRM3026")]
    [InlineData("static ensure true;", "TRM3026")]
    public void NestedEnsureAndConstructorEnsureAreNotPointChecks(string statement, string code)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var source = statement.StartsWith("if", StringComparison.Ordinal)
            ? "int32 Main() { " + statement + " return 42; }"
            : "struct Box { public int32 Value; public Box(int32 value) { "
                + statement + " Value = value; } } int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void StaticProofsCannotBePublishedAsTopLevelClauses()
    {
        var tree = SyntaxTree.Parse("static require true; int32 Main() { return 42; }");
        Assert.True(tree.HasErrors);
    }

    [Fact]
    public void RuntimeParametersInGenericTemplatesDoNotBecomeProofs()
    {
        const string Source = """
            T Forward<T>(T value) where T : [Copy] {
                static require true;
                return value;
            }
            int32 Main() { return Forward(int32(42)); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3026");
    }
}
