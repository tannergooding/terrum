// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Syntax;

public sealed class SwitchPatternSyntaxTests
{
    [Test]
    public void StatementSwitchParsesPatternCombinatorsBindingsAndGuards()
    {
        var cases = new (string Pattern, SyntaxKind[] Kinds)[] {
            ("1", [SyntaxKind.SwitchConstantPattern]),
            ("_", [SyntaxKind.SwitchWildcardPattern]),
            ("not null", [SyntaxKind.SwitchNotPattern, SyntaxKind.SwitchNullPattern]),
            ("(1 or 2) and not 3", [
                SyntaxKind.SwitchParenthesizedPattern,
                SyntaxKind.SwitchOrPattern,
                SyntaxKind.SwitchAndPattern,
                SyntaxKind.SwitchNotPattern,
            ]),
            ("(1, _)", [SyntaxKind.SwitchTuplePattern, SyntaxKind.SwitchWildcardPattern]),
            ("var item", [SyntaxKind.SwitchInferredBindingPattern]),
            ("mut var? item", [SyntaxKind.SwitchInferredBindingPattern]),
            ("int32 item", [SyntaxKind.SwitchTypedBindingPattern]),
            ("mut int32 item", [SyntaxKind.SwitchTypedBindingPattern]),
            ("Box<int32> item", [SyntaxKind.SwitchTypedBindingPattern]),
            ("T? item", [SyntaxKind.SwitchTypedBindingPattern]),
            ("List<_> items", [
                SyntaxKind.SwitchTypedBindingPattern,
                SyntaxKind.SwitchGenericTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
            ("List<var T> items", [
                SyntaxKind.SwitchTypedBindingPattern,
                SyntaxKind.SwitchGenericTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
            ("List<var? T> items", [
                SyntaxKind.SwitchGenericTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
            ("List<42> items", [
                SyntaxKind.SwitchGenericTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
            ("List<(1 + 2)> items", [
                SyntaxKind.SwitchGenericTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
            ("write List<var T> items", [
                SyntaxKind.SwitchTypedBindingPattern,
                SyntaxKind.SwitchGenericTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
            ("List<Color.Red> items", [
                SyntaxKind.SwitchGenericTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
            ("Box<List<_>> item", [
                SyntaxKind.SwitchGenericTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
            ("Color.Red", [SyntaxKind.SwitchQualifiedPattern]),
            ("Color.Found(var item, _)", [
                SyntaxKind.SwitchQualifiedPattern,
                SyntaxKind.SwitchInferredBindingPattern,
                SyntaxKind.SwitchWildcardPattern,
            ]),
            ("Lookup<int32>.Missing", [
                SyntaxKind.SwitchQualifiedPattern,
                SyntaxKind.SwitchGenericTypePattern,
            ]),
            ("Lookup<int32>.Found(var item)", [
                SyntaxKind.SwitchQualifiedPattern,
                SyntaxKind.SwitchGenericTypePattern,
                SyntaxKind.SwitchInferredBindingPattern,
            ]),
            ("Lookup<Box<int32>>.Found(var item)", [
                SyntaxKind.SwitchQualifiedPattern,
                SyntaxKind.SwitchGenericTypePattern,
                SyntaxKind.SwitchInferredBindingPattern,
            ]),
            ("ref<int32> item", [
                SyntaxKind.SwitchReferenceTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
            ("mut write ref<var T> item", [
                SyntaxKind.SwitchReferenceTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
            ("write ref<_> item", [
                SyntaxKind.SwitchReferenceTypePattern,
                SyntaxKind.SwitchPatternTypeArgument,
            ]),
        };

        foreach (var (pattern, expectedKinds) in cases)
        {
            var guarded = pattern == "1";
            var guard = guarded ? " when true" : "";
            var source = $"void Main() {{ switch (int32(1)) {{ {pattern}{guard} => {{}}, _ => {{}} }} }}";
            var tree = SyntaxTree.Parse(source);

            Assert.That(tree.Diagnostics, Is.Empty, source);
            Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
            var actualKinds = tree.Root.DescendantNodes()
                .Select(static node => node.Kind);
            foreach (var kind in expectedKinds)
            {
                Assert.That(actualKinds, Does.Contain(kind), source);
            }
            if (guarded)
            {
                Assert.That(actualKinds, Does.Contain(SyntaxKind.SwitchWhenClause), source);
            }
        }
    }

    [TestCase("not")]
    [TestCase("(1,)")]
    [TestCase("var?")]
    public void MalformedSwitchPatternsRetainSourceAndReportSyntaxDiagnostics(string pattern)
    {
        var source = $"void Main() {{ switch (int32(1)) {{ {pattern} => {{}}, _ => {{}} }} }}";
        var tree = SyntaxTree.Parse(source);

        Assert.That(tree.Diagnostics, Is.Not.Empty, source);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
    }

    [TestCase("value is not null", SyntaxKind.SwitchNotPattern, SyntaxKind.SwitchNullPattern)]
    [TestCase("value is int32 item", SyntaxKind.SwitchTypedBindingPattern)]
    [TestCase("value is Lookup<int32>.Found(var item)",
        SyntaxKind.SwitchQualifiedPattern, SyntaxKind.SwitchGenericTypePattern,
        SyntaxKind.SwitchInferredBindingPattern)]
    public void IsExpressionsParseTheSharedPatternGrammar(
        string expression, params SyntaxKind[] expectedKinds)
    {
        var source = $"void Main() {{ int32 value = 1; bool matched = {expression}; }}";
        var tree = SyntaxTree.Parse(source);

        Assert.That(tree.Diagnostics, Is.Empty, source);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        var actualKinds = tree.Root.DescendantNodes().Select(static node => node.Kind);
        Assert.That(actualKinds, Does.Contain(SyntaxKind.IsPatternExpression), source);
        foreach (var kind in expectedKinds)
        {
            Assert.That(actualKinds, Does.Contain(kind), source);
        }
    }
}
