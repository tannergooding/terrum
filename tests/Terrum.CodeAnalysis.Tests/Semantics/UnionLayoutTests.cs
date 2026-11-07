// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class UnionLayoutTests
{
    [Test]
    public void RawUnionAlternativesUseOverlappingStorage()
    {
        var compilation = CreateLayoutLibrary(SyntaxTree.Parse("""
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

        var unsafeCompilation = CreateLayoutLibrary(
            SyntaxTree.Parse(prefix + "unsafe { return value.Narrow; } }"),
            BoundRuntimePolicy.FailFast);
        Assert.That(unsafeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                unsafeCompilation.Diagnostics));

        var safeCompilation = CreateLayoutLibrary(
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

        var narrowCompilation = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint32 ReadNarrow(RawWord value) {
                    unsafe { return value.Narrow; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(narrowCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                narrowCompilation.Diagnostics));

        var wideCompilation = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadWide(RawWord value) {
                    unsafe { return value.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(wideCompilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, wideCompilation.Diagnostics));

        var uninitializedNarrowCompilation = CreateLayoutLibrary(
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

        var uninitializedWideCompilation = CreateLayoutLibrary(
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

        var overwriteWideCompilation = CreateLayoutLibrary(
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

        var copiedWideCompilation = CreateLayoutLibrary(
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

        var joinedNarrowCompilation = CreateLayoutLibrary(
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

        var joinedWideCompilation = CreateLayoutLibrary(
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
    public void RawUnionNestedFieldStoresCarryPayloadFacts()
    {
        const string prefix = """
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

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Envelope {
                public mut RawWord Word;
                public Envelope(RawWord word) {
                    Word = word;
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct WideEnvelope {
                public mut RawWord Word;
                public WideEnvelope(uint64 value) {
                    unsafe { Word.Wide = value; }
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct MultiEnvelope {
                public mut RawWord Word;
                public mut uint32 Tag;
            }
            """;

        var constructionOnly = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public int32 ConstructEnvelope() {
                    mut Envelope value = new Envelope(new RawWord(1));
                    return 0;
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(constructionOnly.HasErrors, Is.False,
            string.Join(Environment.NewLine, constructionOnly.Diagnostics));

        var storedWideOnly = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public int32 StoreNestedWide() {
                    mut Envelope value = new Envelope(new RawWord(1));
                    unsafe { value.Word.Wide = 7; }
                    return 0;
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(storedWideOnly.HasErrors, Is.False,
            string.Join(Environment.NewLine, storedWideOnly.Diagnostics));

        var constructedNestedWide = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ConstructNestedWide() {
                    mut WideEnvelope value = new WideEnvelope(7);
                    unsafe { return value.Word.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(constructedNestedWide.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                constructedNestedWide.Diagnostics));

        var vacantNestedConstruction = CreateLayoutLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 InitializeNestedUnionAlternative() {
                    init mut WideEnvelope value;
                    unsafe {
                        value.Word.Wide = 7;
                        return value.Word.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(vacantNestedConstruction.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                vacantNestedConstruction.Diagnostics));

        var repeatedNestedInitialization = CreateLayoutLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 RejectSecondNestedUnionInitialization() {
                    init mut WideEnvelope value;
                    unsafe {
                        value.Word.Wide = 7;
                        value.Word.Padded = new Sparse(1, 2);
                        return value.Word.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(repeatedNestedInitialization.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                repeatedNestedInitialization.Diagnostics));

        var partialNestedChild = CreateLayoutLibrary(
            SyntaxTree.Parse(prefix + """
                public int32 RejectPartialNestedUnionConstruction() {
                    init mut WideEnvelope value;
                    unsafe { value.Word.Padded.First = 1; }
                    return 0;
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(partialNestedChild.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                partialNestedChild.Diagnostics));

        var completedMultiParent = CreateLayoutLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 CompleteMultiFieldParent() {
                    init mut MultiEnvelope value;
                    unsafe { value.Word.Wide = 7; }
                    value.Tag = 1;
                    unsafe { return value.Word.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(completedMultiParent.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                completedMultiParent.Diagnostics));

        var incompleteMultiParent = CreateLayoutLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 RejectIncompleteMultiFieldParent() {
                    init mut MultiEnvelope value;
                    unsafe {
                        value.Word.Wide = 7;
                        return value.Word.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(incompleteMultiParent.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                incompleteMultiParent.Diagnostics));

        var storedWide = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 StoreAndReadNestedWide() {
                    mut Envelope value = new Envelope(new RawWord(1));
                    unsafe {
                        value.Word.Wide = 7;
                        return value.Word.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(storedWide.HasErrors, Is.False,
            string.Join(Environment.NewLine, storedWide.Diagnostics));

        var narrowThenWide = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadNestedWideAfterNarrow() {
                    mut Envelope value = new Envelope(new RawWord(1));
                    unsafe {
                        value.Word.Padded = new Sparse(1, 2);
                        return value.Word.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(narrowThenWide.HasErrors, Is.True,
            string.Join(Environment.NewLine, narrowThenWide.Diagnostics));

        var copiedWide = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 CopyNestedWideBeforeOverlappingStore() {
                    mut Envelope value = new Envelope(new RawWord(1));
                    unsafe { value.Word.Wide = 7; }
                    mut Envelope copy = value;
                    unsafe {
                        value.Word.Padded = new Sparse(1, 2);
                        return copy.Word.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(copiedWide.HasErrors, Is.False,
            string.Join(Environment.NewLine, copiedWide.Diagnostics));

        var joinedNarrow = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadNestedWideAfterConditionalStore(bool widen) {
                    mut Envelope value = new Envelope(new RawWord(1));
                    unsafe {
                        value.Word.Padded = new Sparse(1, 2);
                        if (widen) {
                            value.Word.Wide = 7;
                        }
                        return value.Word.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(joinedNarrow.HasErrors, Is.True,
            string.Join(Environment.NewLine, joinedNarrow.Diagnostics));

        var joinedWide = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadNestedWideAfterBothBranches(bool widen) {
                    mut Envelope value = new Envelope(new RawWord(1));
                    unsafe {
                        value.Word.Padded = new Sparse(1, 2);
                        if (widen) {
                            value.Word.Wide = 7;
                        } else {
                            value.Word.Wide = 9;
                        }
                        return value.Word.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(joinedWide.HasErrors, Is.False,
            string.Join(Environment.NewLine, joinedWide.Diagnostics));

        var loopNarrow = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadNestedWideAfterOptionalLoop(
                    bool shouldWrite) {
                    mut Envelope value = new Envelope(new RawWord(1));
                    mut bool write = shouldWrite;
                    unsafe {
                        value.Word.Padded = new Sparse(1, 2);
                        while (write) {
                            value.Word.Wide = 7;
                            write = false;
                        }
                        return value.Word.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(loopNarrow.HasErrors, Is.True,
            string.Join(Environment.NewLine, loopNarrow.Diagnostics));

        var finallyNarrow = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadNestedWideAfterFinallyStore() {
                    mut Envelope value = new Envelope(new RawWord(1));
                    try {
                        unsafe { value.Word.Wide = 7; }
                    } finally {
                        unsafe {
                            value.Word.Padded = new Sparse(1, 2);
                        }
                    }
                    unsafe { return value.Word.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(finallyNarrow.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                finallyNarrow.Diagnostics));

        var failureWide = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadNestedWideAfterCheckedFailure() {
                    mut Envelope value = new Envelope(new RawWord(1));
                    unsafe {
                        try {
                            value.Word.Wide = 7;
                            require false;
                        } catch {
                            return value.Word.Wide;
                        }
                    }
                    return 0;
                }
                """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(failureWide.HasErrors, Is.False,
            string.Join(Environment.NewLine, failureWide.Diagnostics));

        var failureNarrow = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 RejectNestedWideAfterCheckedFailure() {
                    mut Envelope value = new Envelope(new RawWord(1));
                    unsafe {
                        try {
                            value.Word.Padded = new Sparse(1, 2);
                            require false;
                        } catch {
                            return value.Word.Wide;
                        }
                    }
                    return 0;
                }
                """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(failureNarrow.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                failureNarrow.Diagnostics));

        var selfCopy = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadNestedWideAfterSelfCopy() {
                    mut Envelope value = new Envelope(new RawWord(1));
                    unsafe {
                        value.Word.Wide = 7;
                        value.Word = value.Word;
                        return value.Word.Wide;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(selfCopy.HasErrors, Is.False,
            string.Join(Environment.NewLine, selfCopy.Diagnostics));

        var conditionalFull = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadNestedWideAfterFullConditional(bool choose) {
                    mut RawWord first = new RawWord(0);
                    mut RawWord second = new RawWord(0);
                    unsafe {
                        first.Wide = 7;
                        second.Wide = 9;
                    }
                    RawWord selected = choose ? first : second;
                    mut Envelope value = new Envelope(selected);
                    unsafe { return value.Word.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(conditionalFull.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                conditionalFull.Diagnostics));

        var conditionalPartial = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 RejectNestedWideAfterPartialConditional(
                    bool choose) {
                    mut RawWord first = new RawWord(0);
                    mut RawWord second = new RawWord(0);
                    unsafe {
                        first.Wide = 7;
                        second.Padded = new Sparse(1, 2);
                    }
                    RawWord selected = choose ? first : second;
                    mut Envelope value = new Envelope(selected);
                    unsafe { return value.Word.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(conditionalPartial.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                conditionalPartial.Diagnostics));
    }

    [Test]
    public void RawUnionNestedFieldStoreInvalidatesPointerValidity()
    {
        const string prefix = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawPointer {
                public mut ptr<int32> Pointer;
                public mut uint64 Bits;
                public RawPointer(uint64 bits) {
                    unsafe { Bits = bits; }
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Envelope {
                public mut RawPointer Value;
                public Envelope(RawPointer value) {
                    Value = value;
                }
            }
            """;

        var preserved = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public unsafe int32 ReadNestedPointer() {
                    int32 value = 42;
                    mut Envelope envelope
                        = new Envelope(new RawPointer(0));
                    unsafe {
                        envelope.Value.Pointer = &value;
                        return *envelope.Value.Pointer;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(preserved.HasErrors, Is.False,
            string.Join(Environment.NewLine, preserved.Diagnostics));

        var invalidated = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public unsafe int32 ReadNestedPointerAfterOverwrite() {
                    int32 value = 42;
                    mut Envelope envelope
                        = new Envelope(new RawPointer(0));
                    unsafe {
                        envelope.Value.Pointer = &value;
                        envelope.Value.Bits = 0;
                        return *envelope.Value.Pointer;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(invalidated.HasErrors, Is.True,
            string.Join(Environment.NewLine, invalidated.Diagnostics));
        Assert.That(invalidated.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
                && diagnostic.Message.Contains("NativePointer",
                    StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, invalidated.Diagnostics));

        var copiedInvalidated = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public unsafe int32 ReadCopiedNestedPointerAfterOverwrite() {
                    int32 value = 42;
                    mut Envelope envelope
                        = new Envelope(new RawPointer(0));
                    unsafe {
                        envelope.Value.Pointer = &value;
                        envelope.Value.Bits = 0;
                        mut Envelope copy = envelope;
                        return *copy.Value.Pointer;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(copiedInvalidated.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                copiedInvalidated.Diagnostics));
        Assert.That(copiedInvalidated.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
                && diagnostic.Message.Contains("NativePointer",
                    StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine,
                copiedInvalidated.Diagnostics));

        var restored = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public unsafe int32 ReadNestedPointerAfterRestore() {
                    int32 value = 42;
                    mut Envelope envelope
                        = new Envelope(new RawPointer(0));
                    unsafe {
                        envelope.Value.Pointer = &value;
                        envelope.Value.Bits = 0;
                        envelope.Value.Pointer = &value;
                        return *envelope.Value.Pointer;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(restored.HasErrors, Is.False,
            string.Join(Environment.NewLine, restored.Diagnostics));

        var maybeInvalidated = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public unsafe int32 ReadNestedPointerAfterConditionalOverwrite(
                    bool overwrite) {
                    int32 value = 42;
                    mut Envelope envelope
                        = new Envelope(new RawPointer(0));
                    unsafe {
                        envelope.Value.Pointer = &value;
                        if (overwrite) {
                            envelope.Value.Bits = 0;
                        }
                        return *envelope.Value.Pointer;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(maybeInvalidated.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                maybeInvalidated.Diagnostics));
        Assert.That(maybeInvalidated.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
                && diagnostic.Message.Contains("NativePointer",
                    StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine,
                maybeInvalidated.Diagnostics));

        const string siblingPrefix = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawPointer {
                public mut ptr<int32> Pointer;
                public mut uint64 Bits;
                public RawPointer(uint64 bits) {
                    unsafe { Bits = bits; }
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Envelope {
                public mut RawPointer Value;
                public mut RawPointer Other;
                public Envelope(RawPointer value, RawPointer other) {
                    Value = value;
                    Other = other;
                }
            }
            """;
        var unrelatedPointer = CreateLayoutLibrary(
            SyntaxTree.Parse(siblingPrefix + """
                public unsafe int32 ReadUnrelatedPointerAfterOverwrite() {
                    int32 value = 42;
                    int32 other = 17;
                    mut Envelope envelope = new Envelope(
                        new RawPointer(0), new RawPointer(0));
                    unsafe {
                        envelope.Value.Pointer = &value;
                        envelope.Other.Pointer = &other;
                        envelope.Value.Bits = 0;
                        return *envelope.Other.Pointer;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(unrelatedPointer.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                unrelatedPointer.Diagnostics));
    }

    [Test]
    public void RawUnionNestedPayloadFactsForwardThroughByValueCalls()
    {
        const string prefix = """
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

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Envelope {
                public mut RawWord Word;
                public Envelope(RawWord word) {
                    Word = word;
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public uint32 Tag;
                public mut RawWord Value;
                public Holder(uint32 tag, RawWord value) {
                    Tag = tag;
                    Value = value;
                }
            }

            public Envelope Wrap(RawWord value, int32 ignored) {
                return new Envelope(value);
            }

            public Envelope WrapField(Holder value, int32 ignored) {
                return new Envelope(value.Value);
            }

            public Envelope Identity(Envelope value) { return value; }

            public int32 Replace(out RawWord value) {
                value = new RawWord(0);
                unsafe { value.Padded = new Sparse(1, 2); }
                return 0;
            }
            """;

        var forwarded = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ForwardWideThroughCalls() {
                    mut RawWord word = new RawWord(0);
                    unsafe { word.Wide = 7; }
                    Envelope envelope = Wrap(word, 0);
                    Envelope copy = Identity(envelope);
                    unsafe { return copy.Word.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(forwarded.HasErrors, Is.False,
            string.Join(Environment.NewLine, forwarded.Diagnostics));

        var staged = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 PreserveStagedWideAcrossLaterArgument() {
                    mut RawWord word = new RawWord(0);
                    unsafe { word.Wide = 9; }
                    Envelope envelope = Wrap(word, Replace(out word));
                    unsafe { return envelope.Word.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(staged.HasErrors, Is.False,
            string.Join(Environment.NewLine, staged.Diagnostics));

        var nestedPath = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ForwardWideThroughNestedField() {
                    mut RawWord word = new RawWord(0);
                    unsafe { word.Wide = 11; }
                    Holder holder = new Holder(1, word);
                    Envelope envelope = WrapField(holder, 0);
                    unsafe { return envelope.Word.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(nestedPath.HasErrors, Is.False,
            string.Join(Environment.NewLine, nestedPath.Diagnostics));

        var narrow = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 RejectPaddedWideThroughConstructor() {
                    mut RawWord word = new RawWord(0);
                    unsafe { word.Padded = new Sparse(1, 2); }
                    Envelope envelope = Wrap(word, 0);
                    unsafe { return envelope.Word.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(narrow.HasErrors, Is.True,
            string.Join(Environment.NewLine, narrow.Diagnostics));

        var nestedNarrow = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 RejectPaddedWideThroughNestedField() {
                    mut RawWord word = new RawWord(0);
                    unsafe { word.Padded = new Sparse(1, 2); }
                    Holder holder = new Holder(1, word);
                    Envelope envelope = WrapField(holder, 0);
                    unsafe { return envelope.Word.Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(nestedNarrow.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                nestedNarrow.Diagnostics));
    }

    [Test]
    public void RawUnionAlternativeStoreInvalidatesPaddingCoverage()
    {
        var partialCompilation = CreateLayoutLibrary(SyntaxTree.Parse("""
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

        var compilation = CreateLayoutLibrary(SyntaxTree.Parse("""
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
        var preservedCompilation = CreateLayoutLibrary(
            SyntaxTree.Parse("""
                [Blittable]
                [Layout(LayoutKind.Union)]
                public struct RawPointer {
                    public mut ptr<int32> Pointer;
                    public mut uint64 Bits;
                    public RawPointer(uint64 bits) {
                        unsafe { Bits = bits; }
                    }
                }

                public unsafe int32 ReadPointer() {
                    int32 value = 42;
                    mut RawPointer rawValue = new RawPointer(0);
                    unsafe {
                        rawValue.Pointer = &value;
                        return *rawValue.Pointer;
                    }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(preservedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                preservedCompilation.Diagnostics));

        var invalidatedCompilation = CreateLayoutLibrary(
            SyntaxTree.Parse("""
                [Blittable]
                [Layout(LayoutKind.Union)]
                public struct RawPointer {
                    public mut ptr<int32> Pointer;
                    public mut uint64 Bits;
                    public RawPointer(uint64 bits) {
                        unsafe { Bits = bits; }
                    }
                }

                public unsafe int32 ReadAfterOverwrite() {
                    int32 value = 42;
                    mut RawPointer rawValue = new RawPointer(0);
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
        Assert.That(invalidatedCompilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
                && diagnostic.Message.Contains("NativePointer",
                    StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine,
                invalidatedCompilation.Diagnostics));
    }

    [Test]
    public void RawUnionCallSitesConsumeVerifiedReturnCoverage()
    {
        var wideCompilation = CreateLayoutLibrary(SyntaxTree.Parse("""
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

        var narrowCompilation = CreateLayoutLibrary(SyntaxTree.Parse("""
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
    public void NestedRawUnionReturnCoverageIntersectsNormalResults()
    {
        const string declarations = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Envelope {
                public RawWord Inner;

                internal Envelope(RawWord inner) {
                    Inner = inner;
                }
            }
            """;

        var complete = CreateLayoutLibrary(SyntaxTree.Parse(
            declarations + """
            public Envelope MakeWide(bool chooseFirst) {
                init mut RawWord value;
                if (chooseFirst) {
                    unsafe { value.Wide = uint64(7); }
                } else {
                    unsafe { value.Wide = uint64(9); }
                }
                return new Envelope(value);
            }

            public uint64 ReadNestedWide(bool chooseFirst) {
                mut Envelope value = MakeWide(chooseFirst);
                unsafe { return value.Inner.Wide; }
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(complete.HasErrors, Is.False,
            string.Join(Environment.NewLine, complete.Diagnostics));

        var incomplete = CreateLayoutLibrary(SyntaxTree.Parse(
            declarations + """
            public Envelope MakeMixed(bool chooseWide) {
                init mut RawWord value;
                if (chooseWide) {
                    unsafe { value.Wide = uint64(7); }
                } else {
                    unsafe { value.Narrow = uint32(9); }
                }
                return new Envelope(value);
            }

            public uint64 ReadNestedWide(bool chooseWide) {
                mut Envelope value = MakeMixed(chooseWide);
                unsafe { return value.Inner.Wide; }
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(incomplete.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                incomplete.Diagnostics));
        Assert.That(incomplete.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"), Is.True,
            string.Join(Environment.NewLine,
                incomplete.Diagnostics));
    }

    [Test]
    public void ExceptionalBranchesDoNotWeakenNormalUnionReturnCoverage()
    {
        var compilation = CreateLayoutLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
            }

            public RawWord MakeWideOrThrow(bool chooseWide) {
                if (chooseWide) {
                    init mut RawWord value;
                    unsafe { value.Wide = uint64(7); }
                    return value;
                }
                require false;
            }

            public uint64 ObserveWide(bool chooseWide) {
                mut RawWord value = MakeWideOrThrow(chooseWide);
                unsafe { return value.Wide; }
            }
            """), BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void MixedRecursiveUnionReturnsDoNotPublishCoverage()
    {
        var compilation = CreateLayoutLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint32 Narrow;
                public mut uint64 Wide;
            }

            public RawWord MakeWide() {
                init mut RawWord value;
                unsafe { value.Wide = uint64(7); }
                return value;
            }

            public RawWord MakeMixed(bool chooseWide) {
                if (chooseWide) {
                    return MakeWide();
                }
                return MakeMixed(true);
            }

            public uint64 ObserveWide() {
                mut RawWord value = MakeMixed(true);
                unsafe { return value.Wide; }
            }
            """), BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"), Is.True,
            string.Join(Environment.NewLine,
                compilation.Diagnostics));
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

        var narrowCompilation = CreateLayoutLibrary(SyntaxTree.Parse(
            prefix + """
                public uint32 ReadNarrow() {
                    mut RawWord value = new RawWord(7);
                    unsafe { return value.Narrow; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(narrowCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                narrowCompilation.Diagnostics));

        var wideCompilation = CreateLayoutLibrary(SyntaxTree.Parse(
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
        var compilation = CreateLayoutLibrary(SyntaxTree.Parse("""
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
        var compilation = CreateLayoutLibrary(SyntaxTree.Parse("""
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

    private static Compilation CreateLayoutLibrary(SyntaxTree tree,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
        => Compilation.CreateLibrary(tree, new CompilationOptions {
            RuntimePolicy = policy,
            LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
        });
}
