// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ConstExpressionTests
{
    [Fact]
    public void PrefixPrecedenceAndParenthesesSelectTheRequiredSubexpression()
    {
        const string Source = """
            [ConstEval] int32 Add(int32 left, int32 right) { return left + right; }
            int32 Main() {
                int32 left = const Add(1, 1) + 40;
                int32 whole = const (Add(1, 1) + 40);
                return left + whole;
            }
            """;
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(Source, tree.Root.ToFullString());
        var version = Assert.IsAssignableFrom<System.Reflection.FieldInfo>(
            typeof(SyntaxTree).GetField(nameof(SyntaxTree.LanguageVersion)));
        Assert.Equal("syntax-27", version.GetRawConstantValue());
        Assert.Equal(2, tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.ConstExpression));
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Main");
        var left = Assert.IsType<BoundBinary>(Assert.IsType<BoundLocal>(main.Body.Statements[0]).Initializer);
        Assert.Equal(2, Assert.IsType<BoundConstant>(left.Left).Value);
        Assert.Equal(40, Assert.IsType<BoundConstant>(left.Right).Value);
        Assert.Equal(42, Assert.IsType<BoundConstant>(
            Assert.IsType<BoundLocal>(main.Body.Statements[1]).Initializer).Value);
    }

    [Fact]
    public void ContextualConstDoesNotChangeNamedCallsOrVariableReads()
    {
        const string NamedCall = """
            int32 const(int32 value) { return value + 1; }
            int32 Main() { return const(41); }
            """;
        var tree = SyntaxTree.Parse(NamedCall);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Empty(tree.Root.DescendantNodes().Where(node => node.Kind == SyntaxKind.ConstExpression));
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Main");
        _ = Assert.IsType<BoundCall>(Assert.IsType<BoundReturn>(main.Body.Statements[0]).Value);

        const string LocalName = "int32 Main() { int32 const = 40; return const + 2; }";
        tree = SyntaxTree.Parse(LocalName);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Empty(tree.Root.DescendantNodes().Where(node => node.Kind == SyntaxKind.ConstExpression));
        compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions);
        var sum = Assert.IsType<BoundBinary>(Assert.IsType<BoundReturn>(main.Body.Statements[^1]).Value);
        _ = Assert.IsType<BoundRead>(sum.Left);
        Assert.Equal(2, Assert.IsType<BoundConstant>(sum.Right).Value);

        const string SignedRequirement = "int32 Main() { return const (-1) + 43; }";
        compilation = Compilation.Create(SyntaxTree.Parse(SignedRequirement));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions);
        sum = Assert.IsType<BoundBinary>(Assert.IsType<BoundReturn>(main.Body.Statements[0]).Value);
        Assert.Equal(-1, Assert.IsType<BoundConstant>(sum.Left).Value);
    }

    [Fact]
    public void RequiredArgumentReplacesOnlyThatCallPosition()
    {
        const string Source = """
            [ConstEval] int32 Twice(int32 value) { return value + value; }
            int32 Runtime(int32 value) { return value + 1; }
            int32 Add(int32 left, int32 right) { return left + right; }
            int32 Main() { return Add(Runtime(1), const Twice(20)); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Main");
        var call = Assert.IsType<BoundCall>(Assert.IsType<BoundReturn>(main.Body.Statements[0]).Value);
        _ = Assert.IsType<BoundCall>(call.Arguments[0]);
        Assert.Equal(40, Assert.IsType<BoundConstant>(call.Arguments[1]).Value);
    }

    [Theory]
    [InlineData("return const Divide(1, 0);", "checked arithmetic")]
    [InlineData("return const Add(2147483647, 1);", "checked arithmetic")]
    [InlineData("return const Recurse(100);", "call depth limit")]
    [InlineData("return const Loop(1);", "work limit")]
    [InlineData("return const NotConst();", "unsupported effects")]
    [InlineData("if (const (false && NotConst() == 1)) { return 1; } return 42;", "unsupported effects")]
    [InlineData("if (false && const (Divide(1, 0) == 1)) { return 1; } return 42;", "checked arithmetic")]
    public void RequiredExpressionCannotFallBackToRuntime(string statement, string message)
    {
        var source = """
            [ConstEval] int32 Divide(int32 a, int32 b) { return a / b; }
            [ConstEval] int32 Add(int32 a, int32 b) { return a + b; }
            [ConstEval] int32 Recurse(int32 value) {
                if (value == 0) { return 0; }
                return Recurse(value - 1);
            }
            [ConstEval] int32 Loop(int32 value) { while (true) { } return value; }
            int32 NotConst() { return 1; }
            int32 Main() { STATEMENT }
            """.Replace("STATEMENT", statement, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3024"
            && diagnostic.Message.Contains(message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("return const value;")]
    [InlineData("return const (value + 1);")]
    public void RuntimeLocalCannotSatisfyAnExpressionRequirement(string result)
    {
        var source = "int32 Read(int32 value) { " + result + " } int32 Main() { return Read(41); }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3024");
    }

    [Fact]
    public void RequiredExpressionRejectsNonScalarResult()
    {
        const string Source = """
            struct Box { public int32 Value; public Box(int32 value) { Value = value; } }
            int32 Main() {
                Box box = const new Box(42);
                return box.Value;
            }
            """;
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        var compilation = Compilation.Create(tree);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3024"
            && diagnostic.Message.Contains("scalar type", StringComparison.Ordinal));
    }

    [Fact]
    public void RequiredExpressionInsideConstEvalBodyUsesSameDefinitionSiteRules()
    {
        const string Source = """
            int32 Main() { return const Outer(); }
            [ConstEval] int32 Outer() { return const Inner() + 1; }
            [ConstEval] int32 Inner() { return 41; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(42, Assert.IsType<BoundConstant>(
            Assert.IsType<BoundReturn>(program.Functions[0].Body.Statements[0]).Value).Value);
        Assert.Equal(41, Assert.IsType<BoundConstant>(
            Assert.IsType<BoundBinary>(Assert.IsType<BoundReturn>(program.Functions[1].Body.Statements[0]).Value)
                .Left).Value);
    }
}
