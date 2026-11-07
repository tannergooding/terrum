using System;
using System.Buffers.Binary;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class CheckedReferenceTypeTests
{
    [TestCase("ref<int32>")]
    [TestCase("ref<ref<int32>>")]
    [TestCase("ref<int32>?")]
    [TestCase("write ref<init int32>")]
    [TestCase("gc ref<int32>")]
    public void BindsCompleteReferenceTypeParameters(string type)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"void Accept({type} value) {{}}"));

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsGarbageCollectedInitializationReferences()
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("void Accept(gc ref<init int32> value) {}"));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsGarbageCollectionQualificationOnScalarTypes()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("void Accept(gc int32 value) {}"));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void PrivateRevision106RoundTripsCheckedReferenceTypesAndRejectsMalformedRows()
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Accept(ref<int32> value) {}
                public unsafe void AcceptPointer(ref<ptr<int32>> value) {}
                public void AcceptNested(ref<ref<int32>> value) {}
                public void AcceptOptional(ref<int32>? value) {}
                public void AcceptNestedOptional(ref<ref<int32>?>? value) {}
                """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["Accept", "AcceptPointer", "AcceptNested", "AcceptOptional",
                "AcceptNestedOptional"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            producer.AsSpan(sizeof(int), sizeof(int))), Is.EqualTo(106));

        var reference = ModuleCompiler.CreateReference(producer);
        var declarations = reference.Functions;
        var scalarParameter = global::System.Linq.Enumerable.Single(
            declarations, static function => function.Name == "Accept")
            .Variables[0].Type;
        var pointerParameter = global::System.Linq.Enumerable.Single(
            declarations, static function => function.Name == "AcceptPointer")
            .Variables[0].Type;
        var nestedParameter = global::System.Linq.Enumerable.Single(
            declarations, static function => function.Name == "AcceptNested")
            .Variables[0].Type;
        var optionalParameter = global::System.Linq.Enumerable.Single(
            declarations, static function => function.Name == "AcceptOptional")
            .Variables[0].Type;
        var nestedOptionalParameter = global::System.Linq.Enumerable.Single(
            declarations,
            static function => function.Name == "AcceptNestedOptional")
            .Variables[0].Type;
        Assert.That((int)scalarParameter, Is.LessThan(0));
        Assert.That((int)pointerParameter, Is.LessThan(0));
        Assert.That((int)nestedParameter, Is.LessThan(0));
        Assert.That((int)optionalParameter, Is.LessThan(0));
        Assert.That((int)nestedOptionalParameter, Is.LessThan(0));
        Assert.That(pointerParameter, Is.Not.EqualTo(scalarParameter));
        Assert.That(nestedParameter, Is.Not.EqualTo(scalarParameter));
        Assert.That(optionalParameter, Is.Not.EqualTo(scalarParameter));
        Assert.That(nestedOptionalParameter,
            Is.Not.EqualTo(optionalParameter));

        var malformed = (byte[])producer.Clone();
        var typeRowOffset = FindPrivate106CheckedReferenceTypeRowOffset(
            malformed);
        var expectedScalarRow = new byte[8];
        expectedScalarRow[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(
            expectedScalarRow.AsSpan(4), (int)PrimitiveType.Int32);
        Assert.That(producer.AsSpan(typeRowOffset, expectedScalarRow.Length)
            .SequenceEqual(expectedScalarRow), Is.True);
        malformed[typeRowOffset + sizeof(byte) + sizeof(byte)] = 1;
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));

        malformed = (byte[])producer.Clone();
        typeRowOffset = FindPrivate106CheckedReferenceTypeRowOffset(
            malformed);
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(typeRowOffset + 4, sizeof(int)), -5);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
    }

    private static int FindPrivate106CheckedReferenceTypeRowOffset(
        byte[] module)
    {
        var offset = 2 * sizeof(int);
        SkipPrivateString(module, ref offset);
        offset += 3 * sizeof(byte);
        SkipPrivateString(module, ref offset);
        Assert.That(ReadInt32(module, ref offset), Is.Zero);
        offset += sizeof(int);

        for (var tableIndex = 0; tableIndex < 4; tableIndex++)
        {
            Assert.That(ReadInt32(module, ref offset), Is.Zero);
        }

        var pointerTypeCount = ReadInt32(module, ref offset);
        offset = checked(offset + (pointerTypeCount
            * (sizeof(int) + sizeof(byte))));
        Assert.That(ReadInt32(module, ref offset), Is.Zero);
        Assert.That(ReadInt32(module, ref offset), Is.EqualTo(6));
        return offset;
    }

    private static int ReadInt32(byte[] data, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(
            data.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        return value;
    }

    private static void SkipPrivateString(byte[] data, ref int offset)
    {
        var length = ReadInt32(data, ref offset);
        offset = checked(offset + length);
    }

    [Test]
    public void RejectsWritesThroughReadonlyReferences()
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("void Set(ref<int32> value) { *value = int32(7); }"));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsWritableReferenceFormationFromImmutablePlaces()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            void Set(write ref<int32> value) {}
            int32 Main() {
                int32 local = int32(1);
                Set(ref local);
                return 42;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsReturningReferenceToLocalStorage()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            ref<int32> Escape() {
                int32 local = int32(1);
                return ref local;
            }
            int32 Main() { return *Escape(); }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsReferenceToLocalStorageEscapingItsScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 outer = int32(0);
                mut write ref<int32> alias = ref outer;
                {
                    mut int32 inner = int32(1);
                    alias = ref inner;
                }
                return *alias;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsReferenceReborrowThatAmplifiesWriteAuthority()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 local = int32(1);
                ref<int32> readonlyReference = ref local;
                write ref<int32> writableReference = ref *readonlyReference;
                return *writableReference;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsReborrowEscapingItsSourceDescriptorLifetime()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 local = int32(1);
                mut ref<int32> outer = ref local;
                {
                    ref<int32> inner = ref local;
                    outer = ref *inner;
                }
                return *outer;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsWritableReferenceToReadonlyField()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            int32 Main() {
                mut Cell cell = new Cell(int32(1));
                write ref<int32> value = ref cell.Value;
                return *value;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsFieldReferenceEscapingItsStorageScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            int32 Main() {
                mut int32 result = int32(0);
                mut ref<int32> escaped = ref result;
                {
                    mut Cell cell = new Cell(int32(42));
                    escaped = ref cell.Value;
                }
                return *escaped;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.HasErrors, Is.True);
    }
    [Test]
    public void RejectsFixedArrayElementReferenceEscapingItsStorageScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut [uint32; 3] outer = [1, 2, 3];
                mut ref<uint32> escaped = ref outer[0];
                {
                    mut [uint32; 3] inner = [4, 5, 6];
                    escaped = ref inner[1];
                }
                return int32(*escaped);
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsRetiringUniqueOwnerWhileItsFieldReferenceIsLive()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct ReferenceOwner {
                public mut int32 Value;
                public ReferenceOwner(int32 value) { Value = value; }
            }
            int32 Main() {
                mut ReferenceOwner owner = new ReferenceOwner(int32(1));
                ref<int32> value = ref owner.Value;
                owner = _;
                return *value;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void PreservesSiblingFieldReferenceAcrossConditionalPartialOwnerMove()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unique struct Pair {
                public mut OwnedInt32Buffer Owned;
                public mut int32 Value;
                public Pair(OwnedInt32Buffer owned, int32 value) {
                    Owned = move owned;
                    Value = value;
                }
            }
            int32 ReadAfterOptionalMove(bool shouldMove, OwnedInt32Buffer owned) {
                mut Pair pair = new Pair(move owned, int32(42));
                ref<int32> value = ref pair.Value;
                if (shouldMove) {
                    OwnedInt32Buffer extracted = move pair.Owned;
                }
                return *value;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsReferenceToIncompleteUniqueReceiverAsAWhole()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct PartialCell {
                public mut int32 First;
                public mut int32 Second;
                public PartialCell(int32 first, int32 second) {
                    First = first;
                    ref<PartialCell> whole = ref this;
                    Second = second;
                }
            }
            int32 Main() { return 42; }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }
}
