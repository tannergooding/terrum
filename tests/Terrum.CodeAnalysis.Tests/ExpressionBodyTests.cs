// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ExpressionBodyTests
{
    internal const string Source = """
        interface Addition<TLeft, TRight, TResult> {
            static TResult operator +(TLeft left, TRight right);
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

    [Test]
    public void EverySupportedCallableSurfaceRetainsSyntaxAndBinds()
    {
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(Source));
        Assert.That(tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.ExpressionBody), Is.EqualTo(10));
        {var __nunitItems0 = tree.Root.DescendantNodes().Where(node => node.Kind == SyntaxKind.ExpressionBody);        Assert.Multiple(() => {
            foreach (var __nunitItem1 in __nunitItems0)
            {
                {
                    Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual0 = __nunitItem1.Children[0]; Assert.That(__nunitActual0, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual0!; }))().Token.Kind, Is.EqualTo(TokenKind.Arrow));
                    Assert.That(__nunitItem1.DescendantTokens().Last().Kind, Is.EqualTo(TokenKind.Semicolon));
                }
            }
        });}
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Meter.#get:Y").IsNoThrow, Is.True);
        Assert.That(Native.NativeJit.Execute(program, Native.NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase("int32 Main() => _ = 42;","TRM3010")]
    [TestCase("int32 Main() => require true;","TRM3010")]
    [TestCase("void Main() => 42;","TRM3009")]
    [TestCase("struct S { int32 Value; public S(int32 value) => value; } int32 Main() => 0;","TRM3009")]
    [TestCase("struct S { int32 Value; public int32 Result => _ = Value; } int32 Main() => 0;","TRM3010")]
    [TestCase("[NoThrow] int32 Unsafe(int32 x) => x / 0; int32 Main() => 0;","TRM3017")]
    [TestCase("struct S { public int32 Value; public S(int32 x) => Value = x; [NoThrow] public int32 Result => Value / 0; } int32 Main() => 0;","TRM3001")]
    public void InvalidExpressionBodyStatementDiagnoses(string source, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
}
