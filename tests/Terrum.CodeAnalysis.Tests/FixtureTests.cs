// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FixtureTests
{
    [TestCase("hello.terrum")]
    [TestCase("control-flow.terrum")]
    public void PublishedSyntaxFixtureParsesAndRoundTrips(string name)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "samples", "syntax", name);
        var text = File.ReadAllText(path);
        var tree = SyntaxTree.Parse(text, path);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
    }
    [TestCase("SourceRange.terrum")]
    [TestCase("Utf16CodeUnits.terrum")]
    [TestCase("Utf16Scalar.terrum")]
    [TestCase("CharacterFacts.terrum")]
    [TestCase("RangeTests.terrum")]
    [TestCase("Token.terrum")]
    [TestCase("TokenTests.terrum")]
    [TestCase("WideIntegerFacts.terrum")]
    [TestCase("WideIntegerTests.terrum")]
    public void InitialSelfHostCandidateHasSupportedSyntaxOnly(string name)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "self-host", name);
        var text = File.ReadAllText(path);
        var tree = SyntaxTree.Parse(text, path);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
    }
}
