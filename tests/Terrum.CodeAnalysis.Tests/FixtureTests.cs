// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using System;
using System.IO;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FixtureTests
{
    [Theory]
    [InlineData("hello.terrum")]
    [InlineData("control-flow.terrum")]
    public void PublishedSyntaxFixtureParsesAndRoundTrips(string name)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string path = Path.Combine(root, "samples", "syntax", name);
        string text = File.ReadAllText(path);
        SyntaxTree tree = SyntaxTree.Parse(text, path);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(text, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("SourceRange.terrum")]
    [InlineData("CharacterFacts.terrum")]
    [InlineData("RangeTests.terrum")]
    public void InitialSelfHostCandidateHasSupportedSyntaxOnly(string name)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string path = Path.Combine(root, "self-host", name);
        string text = File.ReadAllText(path);
        SyntaxTree tree = SyntaxTree.Parse(text, path);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(text, tree.Root.ToFullString());
    }
}
