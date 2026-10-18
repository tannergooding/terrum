// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Syntax;

public sealed class FixtureTests
{
    [TestCase("tests/Terrum.CodeAnalysis.Tests/Syntax/Fixtures/hello.terrum")]
    [TestCase("tests/Terrum.CodeAnalysis.Tests/Syntax/Fixtures/control-flow.terrum")]
    public void SyntaxTestFixtureParsesAndRoundTrips(string name)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var path = TerrumProjectPaths.RepositoryFile(root, name);
        var text = File.ReadAllText(path);
        var tree = SyntaxTree.Parse(text, path);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
    }

    [TestCase("sources/Terrum.CodeAnalysis.Terrum/Text/SourceRange.terrum")]
    [TestCase("sources/Terrum.CodeAnalysis.Terrum/Text/Utf16CodeUnits.terrum")]
    [TestCase("sources/Terrum.CodeAnalysis.Terrum/Text/Utf16Scalar.terrum")]
    [TestCase("sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterFacts.terrum")]
    [TestCase("tests/Terrum.CodeAnalysis.Terrum.Tests/Text/RangeTests.terrum")]
    [TestCase("sources/Terrum.CodeAnalysis.Terrum/Lexing/Token.terrum")]
    [TestCase("tests/Terrum.CodeAnalysis.Terrum.Tests/Lexing/TokenTests.terrum")]
    [TestCase("sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/WideIntegerFacts.terrum")]
    [TestCase("tests/Terrum.CodeAnalysis.Terrum.Tests/Utilities/Numerics/WideIntegerTests.terrum")]
    public void TerrumProjectSourceHasSupportedSyntaxOnly(string name)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var path = TerrumProjectPaths.RepositoryFile(root, name);
        var text = File.ReadAllText(path);
        var tree = SyntaxTree.Parse(text, path);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
    }
}
