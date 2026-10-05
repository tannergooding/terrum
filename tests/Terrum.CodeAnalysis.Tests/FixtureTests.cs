// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

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
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "samples", "syntax", name);
        var text = File.ReadAllText(path);
        var tree = SyntaxTree.Parse(text, path);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(text, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("SourceRange.terrum")]
    [InlineData("Utf16CodeUnits.terrum")]
    [InlineData("Utf16Scalar.terrum")]
    [InlineData("CharacterFacts.terrum")]
    [InlineData("RangeTests.terrum")]
    [InlineData("Token.terrum")]
    [InlineData("TokenTests.terrum")]
    [InlineData("WideIntegerFacts.terrum")]
    [InlineData("WideIntegerTests.terrum")]
    public void InitialSelfHostCandidateHasSupportedSyntaxOnly(string name)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "self-host", name);
        var text = File.ReadAllText(path);
        var tree = SyntaxTree.Parse(text, path);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(text, tree.Root.ToFullString());
    }
}
