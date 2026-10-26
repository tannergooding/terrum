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
}
