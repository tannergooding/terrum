// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class UnitLocalValueTests
{
    [TestCase("void token = (); token;")]
    [TestCase("var token = (); token;")]
    [TestCase("mut void token = (); token = (); token;")]
    [TestCase("init void token; require !(token is init); token = (); token; require token is init; token = _; require !(token is init);")]
    [TestCase("init var token; token = (); token; token = _; require !(token is init);")]
    [TestCase("init mut void token; token = (); token = (); token = _; require !(token is init);")]
    [TestCase("init void token; if (true) { token = (); } if (token is init) { token; } else { return 1; }")]
    public void UnitLocalsRetainTheirTypeAndInitializationAcrossRuntimePolicies(
        string body)
    {
        var source = $"int32 Main() {{ {body} return 42; }}";
        foreach (var (boundPolicy, nativePolicy) in new[] {
            (BoundRuntimePolicy.FailFast, NativeRuntimePolicy.FailFast),
            (BoundRuntimePolicy.CheckedExceptions,
                NativeRuntimePolicy.CheckedExceptions),
        })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source),
                new CompilationOptions { RuntimePolicy = boundPolicy });
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = compilation.Program!;
            var token = program.Functions.Single(static function =>
                    function.Name == "Main")
                .Variables.Single(static variable => variable.Name == "token");
            Assert.That(token.Type, Is.EqualTo(PrimitiveType.Void));
            Assert.That(NativeJit.Execute(program, nativePolicy), Is.EqualTo(42));
        }
    }

    [TestCase("void token = Notify(); token;")]
    [TestCase("var token = Notify(); token;")]
    public void UnitLocalsRetainUnitCallResultsAcrossRuntimePolicies(string body)
    {
        var source = $"void Notify() {{}} int32 Main() {{ {body} return 42; }}";
        foreach (var (boundPolicy, nativePolicy) in new[] {
            (BoundRuntimePolicy.FailFast, NativeRuntimePolicy.FailFast),
            (BoundRuntimePolicy.CheckedExceptions,
                NativeRuntimePolicy.CheckedExceptions),
        })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source),
                new CompilationOptions { RuntimePolicy = boundPolicy });
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.That(NativeJit.Execute(compilation.Program!, nativePolicy), Is.EqualTo(42));
        }
    }

    [TestCase("void token = int32(42); return 42;")]
    [TestCase("init void token; token; return 42;")]
    [TestCase("void token = (); token = _; token = (); return 42;")]
    [TestCase("init var token; return 42;")]
    [TestCase("void token; return 42;")]
    public void UnitLocalsPreserveTypeAndInitializationErrors(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"int32 Main() {{ {body} }}"));

        Assert.That(compilation.HasErrors, Is.True);
    }
}
