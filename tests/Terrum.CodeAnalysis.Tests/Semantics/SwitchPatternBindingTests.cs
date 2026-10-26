// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class SwitchPatternBindingTests
{
    [Test]
    public void OrPatternDoesNotAssumeBindingIsInitializedOnEveryAlternative()
    {
        const string source = """
            int32 Main(int32 selector) {
                switch (selector) {
                    1 or var item => { return item; },
                    _ => { return 0; },
                }
            }
            """;
        var syntaxTree = SyntaxTree.Parse(source);
        Assert.That(syntaxTree.Diagnostics, Is.Empty, string.Join(Environment.NewLine, syntaxTree.Diagnostics));
        var compilation = Compilation.Create(syntaxTree, BoundRuntimePolicy.FailFast);

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void OrAlternativesCannotRedeclareTheSamePatternName()
    {
        const string source = """
            int32 Main(int32 selector) {
                switch (selector) {
                    var item or var item => { return 0; },
                    _ => { return 0; },
                }
            }
            """;
        var syntaxTree = SyntaxTree.Parse(source);
        Assert.That(syntaxTree.Diagnostics, Is.Empty, string.Join(Environment.NewLine, syntaxTree.Diagnostics));
        var compilation = Compilation.Create(syntaxTree, BoundRuntimePolicy.FailFast);

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }
}
