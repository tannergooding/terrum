// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ConstExpressionTests
{
    [Test]
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
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(Source));
        var version = ((global::System.Func<System.Reflection.FieldInfo>)(() => { var __nunitActual0 = typeof(SyntaxTree).GetField(nameof(SyntaxTree.LanguageVersion)); Assert.That(__nunitActual0, Is.InstanceOf<System.Reflection.FieldInfo>()); return (System.Reflection.FieldInfo)__nunitActual0!; }))();
        Assert.That(version.GetRawConstantValue(), Is.EqualTo("syntax-46"));
        Assert.That(tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.ConstExpression), Is.EqualTo(2));
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))().Functions, function => function.Name == "Main");
        var left = ((global::System.Func<BoundBinary>)(() => { var __nunitActual3 = ((global::System.Func<BoundLocal>)(() => { var __nunitActual2 = main.Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual2!; }))().Initializer; Assert.That(__nunitActual3, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual3!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual4 = left.Left; Assert.That(__nunitActual4, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual4!; }))().Value, Is.EqualTo(2));
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual5 = left.Right; Assert.That(__nunitActual5, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual5!; }))().Value, Is.EqualTo(40));
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual7 = ((global::System.Func<BoundLocal>)(() => { var __nunitActual6 = main.Body.Statements[1]; Assert.That(__nunitActual6, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual6!; }))().Initializer; Assert.That(__nunitActual7, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual7!; }))().Value, Is.EqualTo(42));
    }

    [Test]
    public void ContextualConstDoesNotChangeNamedCallsOrVariableReads()
    {
        const string NamedCall = """
            int32 const(int32 value) { return value + 1; }
            int32 Main() { return const(41); }
            """;
        var tree = SyntaxTree.Parse(NamedCall);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.ConstExpression),Is.False);
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))().Functions, function => function.Name == "Main");
        _ = ((global::System.Func<BoundCall>)(() => { var __nunitActual10 = ((global::System.Func<BoundReturn>)(() => { var __nunitActual9 = main.Body.Statements[0]; Assert.That(__nunitActual9, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual9!; }))().Value; Assert.That(__nunitActual10, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual10!; }))();

        const string LocalName = "int32 Main() { int32 const = 40; return const + 2; }";
        tree = SyntaxTree.Parse(LocalName);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.ConstExpression),Is.False);
        compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        main = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))().Functions);
        var sum = ((global::System.Func<BoundBinary>)(() => { var __nunitActual13 = ((global::System.Func<BoundReturn>)(() => { var __nunitActual12 = main.Body.Statements[^1]; Assert.That(__nunitActual12, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual12!; }))().Value; Assert.That(__nunitActual13, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual13!; }))();
        _ = ((global::System.Func<BoundRead>)(() => { var __nunitActual14 = sum.Left; Assert.That(__nunitActual14, Is.TypeOf<BoundRead>()); return (BoundRead)__nunitActual14!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual15 = sum.Right; Assert.That(__nunitActual15, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual15!; }))().Value, Is.EqualTo(2));

        const string SignedRequirement = "int32 Main() { return const (-1) + 43; }";
        compilation = Compilation.Create(SyntaxTree.Parse(SignedRequirement));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        main = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = compilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))().Functions);
        sum = ((global::System.Func<BoundBinary>)(() => { var __nunitActual18 = ((global::System.Func<BoundReturn>)(() => { var __nunitActual17 = main.Body.Statements[0]; Assert.That(__nunitActual17, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual17!; }))().Value; Assert.That(__nunitActual18, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual18!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual19 = sum.Left; Assert.That(__nunitActual19, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual19!; }))().Value, Is.EqualTo(-1));
    }

    [Test]
    public void RequiredArgumentReplacesOnlyThatCallPosition()
    {
        const string Source = """
            [ConstEval] int32 Twice(int32 value) { return value + value; }
            int32 Runtime(int32 value) { return value + 1; }
            int32 Add(int32 left, int32 right) { return left + right; }
            int32 Main() { return Add(Runtime(1), const Twice(20)); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual20 = compilation.Program; Assert.That(__nunitActual20, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual20!; }))().Functions, function => function.Name == "Main");
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual22 = ((global::System.Func<BoundReturn>)(() => { var __nunitActual21 = main.Body.Statements[0]; Assert.That(__nunitActual21, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual21!; }))().Value; Assert.That(__nunitActual22, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual22!; }))();
        _ = ((global::System.Func<BoundCall>)(() => { var __nunitActual23 = call.Arguments[0]; Assert.That(__nunitActual23, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual23!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual24 = call.Arguments[1]; Assert.That(__nunitActual24, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual24!; }))().Value, Is.EqualTo(40));
    }
    [TestCase("return const Divide(1, 0);","checked arithmetic")]
    [TestCase("return const Add(2147483647, 1);","checked arithmetic")]
    [TestCase("return const Recurse(100);","call depth limit")]
    [TestCase("return const Loop(1);","work limit")]
    [TestCase("return const NotConst();","unsupported effects")]
    [TestCase("if (const (false && NotConst() == 1)) { return 1; } return 42;","unsupported effects")]
    [TestCase("if (false && const (Divide(1, 0) == 1)) { return 1; } return 42;","checked arithmetic")]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3024"
            && diagnostic.Message.Contains(message, StringComparison.Ordinal)),Is.True);
    }
    [TestCase("return const value;")]
    [TestCase("return const (value + 1);")]
    public void RuntimeLocalCannotSatisfyAnExpressionRequirement(string result)
    {
        var source = "int32 Read(int32 value) { " + result + " } int32 Main() { return Read(41); }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3024"),Is.True);
    }

    [Test]
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
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        var compilation = Compilation.Create(tree);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3024"
            && diagnostic.Message.Contains("scalar type", StringComparison.Ordinal)),Is.True);
    }

    [Test]
    public void RequiredExpressionInsideConstEvalBodyUsesSameDefinitionSiteRules()
    {
        const string Source = """
            int32 Main() { return const Outer(); }
            [ConstEval] int32 Outer() { return const Inner() + 1; }
            [ConstEval] int32 Inner() { return 41; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual25 = compilation.Program; Assert.That(__nunitActual25, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual25!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual27 = ((global::System.Func<BoundReturn>)(() => { var __nunitActual26 = program.Functions[0].Body.Statements[0]; Assert.That(__nunitActual26, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual26!; }))().Value; Assert.That(__nunitActual27, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual27!; }))().Value, Is.EqualTo(42));
        Assert.That(((global::System.Func<BoundConstant>)(() => {
            var __nunitActual30 = ((global::System.Func<BoundBinary>)(() => { var __nunitActual29 = ((global::System.Func<BoundReturn>)(() => { var __nunitActual28 = program.Functions[1].Body.Statements[0]; Assert.That(__nunitActual28, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual28!; }))().Value; Assert.That(__nunitActual29, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual29!; }))()
                .Left;
            Assert.That(__nunitActual30, Is.TypeOf<BoundConstant>());
            return (BoundConstant)__nunitActual30!;
        }))().Value, Is.EqualTo(41));
    }
}
