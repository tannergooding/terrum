// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class UnionLayoutTests
{
    [Test]
    public void RawUnionAlternativesUseOverlappingStorage()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public uint32 Narrow;
                public uint64 Wide;
            }
            """), BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var rawWord = compilation.Program!.Structures.Single();
        Assert.That(rawWord.IsUnion, Is.True);
        Assert.That(rawWord.Size, Is.EqualTo(8));
        Assert.That(rawWord.Fields[0].Offset, Is.Zero);
        Assert.That(rawWord.Fields[1].Offset, Is.Zero);
        Assert.That(rawWord.Fields[0].Size, Is.EqualTo(4));
        Assert.That(rawWord.Fields[1].Size, Is.EqualTo(8));
    }

    [Test]
    public void RawUnionAlternativeAccessRequiresUnsafeContext()
    {
        const string prefix = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public uint32 Narrow;
                public uint64 Wide;
            }
            public uint32 Read(RawWord value) {
            """;

        var unsafeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + "unsafe { return value.Narrow; } }"),
            BoundRuntimePolicy.FailFast);
        Assert.That(unsafeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                unsafeCompilation.Diagnostics));

        var safeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + "return value.Narrow; }"),
            BoundRuntimePolicy.FailFast);
        Assert.That(safeCompilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3028"), Is.True,
            string.Join(Environment.NewLine, safeCompilation.Diagnostics));
    }

    [Test]
    public void RejectsOwningAlternativeInRawUnion()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public unique struct Owner {
                public uint32 Value;
            }

            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawValue {
                public Owner Owned;
                public uint64 Bits;
            }
            """), BoundRuntimePolicy.FailFast);

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3023"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PrivateModuleRoundTripPreservesRawUnionLayout()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public uint32 Narrow;
                public uint64 Wide;
            }
            public uint32 Read(RawWord value) {
                unsafe { return value.Narrow; }
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(
            compilation.Program!, ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var rawWord = reference.Structures.Single(static structure =>
            structure.Name == "RawWord");
        Assert.That(rawWord.IsUnion, Is.True);
        Assert.That(rawWord.Size, Is.EqualTo(8));
        Assert.That(rawWord.Fields[0].Offset, Is.Zero);
        Assert.That(rawWord.Fields[1].Offset, Is.Zero);
    }
}
