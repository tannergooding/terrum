// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class ForTests
{
    [TestCase("for (mut int32 i = 0; i < 3; i = i + 1) { }")]
    [TestCase("for (/* init */;/* condition */;/* step */) { return; }")]
    [TestCase("for (Start(); Next(out var value); Finish(value)) { Use(value); }")]
    public void ForSyntaxPreservesHeaderClausesAndTrivia(string statement)
    {
        var text = "void F() { " + statement + " }";
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
        var loop = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.ForStatement);
        Assert.That(loop.Children.Length, Is.EqualTo(9));
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual0 = loop.Children[8]; Assert.That(__nunitActual0, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual0!; }))().Kind, Is.EqualTo(SyntaxKind.Block));
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.MissingExpression),Is.False);
    }
    [TestCase("for (mut int32 i = 0 i < 3; i = i + 1) { }")]
    [TestCase("for (mut int32 i = 0; i < 3; i = i + 1 { }")]
    [TestCase("for (; true;) return;")]
    [TestCase("for (mut int32 i = 0; i < 3; mut int32 j = 1) { }")]
    [TestCase("for (")]
    public void MalformedForSyntaxRemainsLossless(string statement)
    {
        var text = "void F() { " + statement + " } void Later() { }";
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
    }
    [TestCase("for (mut int32 i = 0; i < 1; i = i + 1) { } return i;")]
    [TestCase("for (var i = 0; false;) { } return 0;")]
    [TestCase("for (int32 i = 0; i < 1; i = i + 1) { } return 0;")]
    [TestCase("for (; int32(1);) { } return 0;")]
    [TestCase("for (; false; i = 1) { mut int32 i = 0; } return 0;")]
    [TestCase("for (; false; int32(1) + 2) { } return 0;")]
    [TestCase("mut int32 i = 0; for (mut int32 i = 1; false;) { } return 0;")]
    public void ForLoopsPreserveScopeTypeAndUnsupportedControlRules(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { " + body + " }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("mut int32 value = 7; for (; Clear(out value);) { } return value;")]
    [TestCase("mut int32 value = 7; for (mut int32 i = 0; i < 2; Clear(out value)) { Use(value); } return 0;")]
    [TestCase("for (; Clear(out var value);) { } return value;")]
    public void ForLoopsRetainConditionalOutputVacancy(string body)
    {
        var source = "bool Clear(out int32 value) { return false; } void Use(int32 value) { } int32 Main() { " + body + " }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void ForLoopLowersToTheExistingVerifiedModuleRepresentation()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Sum() { mut int32 sum = 0; for (mut int32 i = 0; i < 4; i = i + 1) { sum = sum + i; } return sum; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        var scope = ((global::System.Func<BoundBlock>)(() => { var __nunitActual2 = program.Functions[0].Body.Statements[1]; Assert.That(__nunitActual2, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual2!; }))();
        _ = ((global::System.Func<BoundLocal>)(() => { var __nunitActual3 = scope.Statements[0]; Assert.That(__nunitActual3, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual3!; }))();
        var loop = ((global::System.Func<BoundWhile>)(() => { var __nunitActual4 = scope.Statements[1]; Assert.That(__nunitActual4, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual4!; }))();
        Assert.That(((global::System.Func<BoundBlock>)(() => { var __nunitActual5 = loop.Body; Assert.That(__nunitActual5, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual5!; }))().Statements.Length, Is.EqualTo(2));
        var producer = ModuleCompiler.CreateProducer(program, ["Sum"]);
        var consumer = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Sum(); }"),
            ModuleCompiler.CreateReference(producer));
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = consumer.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), producer);
        SemanticVerifier.Verify(ModuleCompiler.Link(artifact, producer));
    }
}
