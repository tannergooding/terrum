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
    public void RawUnionTypedReadsRequireTheSelectedPayloadBytes()
    {
        const string prefix = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
                public RawWord(uint32 value) {
                    unsafe { Narrow = value; }
                }
            }
            """;

        var narrowCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint32 ReadNarrow(RawWord value) {
                    unsafe { return value.Narrow; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(narrowCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                narrowCompilation.Diagnostics));

        var wideCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadWide(RawWord value) {
                    unsafe { return value.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(wideCompilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, wideCompilation.Diagnostics));

        var uninitializedNarrowCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint32 InitializeAndReadNarrow() {
                    init mut RawWord value;
                    unsafe {
                        value.Narrow = 7;
                        return value.Narrow;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(uninitializedNarrowCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                uninitializedNarrowCompilation.Diagnostics));

        var uninitializedWideCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 InitializeAndReadWide() {
                    init mut RawWord value;
                    unsafe {
                        value.Narrow = 7;
                        return value.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(uninitializedWideCompilation.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                uninitializedWideCompilation.Diagnostics));

        var overwriteWideCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 OverwriteWide() {
                    mut RawWord value = new RawWord(7);
                    unsafe {
                        value.Wide = 9;
                        value.Narrow = 3;
                        return value.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(overwriteWideCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                overwriteWideCompilation.Diagnostics));

        var copiedWideCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 CopyNarrowUnion() {
                    mut RawWord value = new RawWord(7);
                    mut RawWord copy = value;
                    unsafe { return copy.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(copiedWideCompilation.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                copiedWideCompilation.Diagnostics));

        var joinedNarrowCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint32 ReadJoinedCommonBytes(bool chooseWide) {
                    init mut RawWord value;
                    if (chooseWide) {
                        unsafe { value.Wide = 9; }
                    } else {
                        unsafe { value.Narrow = 3; }
                    }
                    unsafe { return value.Narrow; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(joinedNarrowCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                joinedNarrowCompilation.Diagnostics));

        var joinedWideCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 ReadJoinedWideBytes(bool chooseWide) {
                    init mut RawWord value;
                    if (chooseWide) {
                        unsafe { value.Wide = 9; }
                    } else {
                        unsafe { value.Narrow = 3; }
                    }
                    unsafe { return value.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(joinedWideCompilation.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                joinedWideCompilation.Diagnostics));
    }

    [Test]
    public void RawUnionAlternativeStoreInvalidatesPaddingCoverage()
    {
        var partialCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Sparse {
                public mut uint8 First;
                public mut uint32 Last;
                public Sparse(uint8 first, uint32 last) {
                    First = first;
                    Last = last;
                }
            }

            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint64 Wide;
                public mut Sparse Padded;
            }

            public uint32 PartialAlternative() {
                init mut RawWord value;
                unsafe { value.Padded.First = 1; }
                return 0;
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(partialCompilation.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                partialCompilation.Diagnostics));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Sparse {
                public uint8 First;
                public uint32 Last;
                public Sparse(uint8 first, uint32 last) {
                    First = first;
                    Last = last;
                }
            }

            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint64 Wide;
                public mut Sparse Padded;
                public RawWord(uint64 value) {
                    unsafe { Wide = value; }
                }
            }

            public uint64 ReadWideAfterPaddedStore() {
                mut RawWord value = new RawWord(1);
                unsafe {
                    value.Wide = 1;
                    value.Padded = new Sparse(1, 2);
                    return value.Wide;
                }
            }
            """), BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RawUnionOverlappingStoreInvalidatesPointerValidity()
    {
        var preservedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable]
                [Layout(LayoutKind.Union)]
                public struct RawPointer {
                    public mut ptr<int32> Pointer;
                    public mut uint64 Bits;
                }

                public unsafe int32 ReadPointer() {
                    mut int32 value = 42;
                    init mut RawPointer rawValue;
                    unsafe {
                        rawValue.Pointer = &value;
                        return *rawValue.Pointer;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(preservedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                preservedCompilation.Diagnostics));

        var invalidatedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable]
                [Layout(LayoutKind.Union)]
                public struct RawPointer {
                    public mut ptr<int32> Pointer;
                    public mut uint64 Bits;
                }

                public unsafe int32 ReadAfterOverwrite() {
                    mut int32 value = 42;
                    init mut RawPointer rawValue;
                    unsafe {
                        rawValue.Pointer = &value;
                        rawValue.Bits = 0;
                        return *rawValue.Pointer;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(invalidatedCompilation.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                invalidatedCompilation.Diagnostics));
    }

    [Test]
    public void RawUnionCallSitesConsumeVerifiedReturnCoverage()
    {
        var wideCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
                public RawWord(uint64 value) {
                    unsafe { Wide = value; }
                }
            }

            public uint64 ObserveWide() {
                mut RawWord value = MakeWide();
                unsafe { return value.Wide; }
            }

            public RawWord MakeWide() {
                init mut RawWord value;
                unsafe { value.Wide = 7; }
                return value;
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(wideCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, wideCompilation.Diagnostics));

        var narrowCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
                public RawWord(uint32 value) {
                    unsafe { Narrow = value; }
                }
            }

            public uint64 ObserveWide() {
                mut RawWord value = MakeNarrow();
                unsafe { return value.Wide; }
            }

            public RawWord MakeNarrow() {
                init mut RawWord value;
                unsafe { value.Narrow = 7; }
                return value;
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(narrowCompilation.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                narrowCompilation.Diagnostics));
    }

    [Test]
    public void RawUnionAlternativeInitializationTracksWrittenBytes()
    {
        const string prefix = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
                public RawWord(uint32 value) {
                    unsafe { Narrow = value; }
                }
            }
            """;

        var narrowCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint32 ReadNarrow() {
                    mut RawWord value = new RawWord(7);
                    unsafe { return value.Narrow; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(narrowCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                narrowCompilation.Diagnostics));

        var wideCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadWide() {
                    mut RawWord value = new RawWord(7);
                    unsafe { return value.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(wideCompilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, wideCompilation.Diagnostics));
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
