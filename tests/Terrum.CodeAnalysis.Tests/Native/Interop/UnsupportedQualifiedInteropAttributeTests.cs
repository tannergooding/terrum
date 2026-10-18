// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Native.Interop;

public sealed class UnsupportedQualifiedInteropAttributeTests
{
    [TestCase("[Terrum.Interop.UnregisteredContract]")]
    public void UnsupportedQualifiedInteropAttributeUsesGenericDiagnostic(string attribute)
    {
        var source = $$"""
            {{attribute}}
            int32 Main() { return 0; }
            """;
        var compilation = Compilation.Create([SyntaxTree.Parse(source)]);

        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3001"), Is.True,
            string.Join(System.Environment.NewLine, compilation.Diagnostics));
    }
}
