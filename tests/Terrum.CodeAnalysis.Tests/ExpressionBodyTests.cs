// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ExpressionBodyTests
{
    internal const string Source = """
        interface Addition<TLeft, TRight, TResult> {
            static abstract TResult operator +(TLeft left, TRight right);
        }
        struct Meter : Addition<Meter, Meter, Meter> {
            public int32 Value;
            public Meter(int32 value) => Value = value;
            public int32 Read() => Value;
            public int32 X => Value;
            public int32 Y { [NoThrow] get => Value; }
            public static Meter operator +(Meter left, Meter right) => new Meter(left.Value + right.Value);
        }
        class Box {
            int32 _value;
            public Box(int32 value) => _value = value;
            public int32 Value => _value;
        }
        T Identity<T>(T value) where T : [Copy] => value;
        void Ensure(int32 value) => require value > 0;
        void Discard(int32 value) => _ = value + 1;
        int32 Main() {
            Meter meter = Identity(new Meter(20) + new Meter(20));
            Ensure(meter.Read());
            Discard(meter.Y);
            require Box.TryCreate(2, out var box);
            return meter.X + box.Value;
        }
        """;

    [Fact]
    public void EverySupportedCallableSurfaceRetainsSyntaxAndBinds()
    {
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(Source, tree.Root.ToFullString());
        Assert.Equal(10, tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.ExpressionBody));
        Assert.All(tree.Root.DescendantNodes().Where(node => node.Kind == SyntaxKind.ExpressionBody), body => {
            Assert.Equal(TokenKind.Arrow, Assert.IsType<SyntaxTokenElement>(body.Children[0]).Token.Kind);
            Assert.Equal(TokenKind.Semicolon, body.DescendantTokens().Last().Kind);
        });
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.True(Assert.Single(program.Functions, function => function.Name == "Meter.#get:Y").IsNoThrow);
        Assert.Equal(42, Native.NativeJit.Execute(program, Native.NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("int32 Main() => _ = 42;", "TRM3010")]
    [InlineData("int32 Main() => require true;", "TRM3010")]
    [InlineData("void Main() => 42;", "TRM3009")]
    [InlineData("struct S { int32 Value; public S(int32 value) => value; } int32 Main() => 0;", "TRM3009")]
    [InlineData("struct S { int32 Value; public int32 Result => _ = Value; } int32 Main() => 0;", "TRM3010")]
    [InlineData("[NoThrow] int32 Unsafe(int32 x) => x / 0; int32 Main() => 0;", "TRM3017")]
    [InlineData("struct S { public int32 Value; public S(int32 x) => Value = x; [NoThrow] public int32 Result => Value / 0; } int32 Main() => 0;", "TRM3001")]
    public void InvalidExpressionBodyStatementDiagnoses(string source, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Null(compilation.Program);
    }
}
