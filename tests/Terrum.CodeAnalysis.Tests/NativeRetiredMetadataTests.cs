// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeRetiredMetadataTests
{
    [TestCase("[Terrum.Interop.NativeComInterface(\"9df7f7b6-1cbb-4ad5-8c10-743c19e4319f\")]")]
    [TestCase("[Terrum.Interop.NativeComMethod(\"Run\")]")]
    [TestCase("[Terrum.Interop.NativeComOwner(\"IFixture\")]")]
    [TestCase("[Terrum.Interop.NativeComAcquisition(\"FixtureOwner\")]")]
    [TestCase("[Terrum.Interop.NativeComHiddenParameter(\"Iid\")]")]
    [TestCase("[Terrum.Interop.NativeComParameterAdaptation(\"Pointer\")]")]
    public void RetiredComSourceMetadataProducesADiagnostic(string attribute)
    {
        var source = $$"""
            {{attribute}}
            int32 Main() { return 0; }
            """;
        var compilation = Compilation.Create([SyntaxTree.Parse(source)]);

        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
    }
}
