using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed class NestedOwnerFieldOperationTests
{
    private const string OwnerSource = """
        unique struct Child {
            public mut OwnedInt32Buffer Left;
            public mut int32 Value;
            public Child(OwnedInt32Buffer left, int32 value) {
                Left = move left;
                Value = value;
            }
            public int32 ReadLeft() { return Left.Read(0); }
        }
        unique struct Owner {
            public mut Child Nested;
            public Owner(Child nested) {
                Nested = move nested;
            }
        }
        OwnedInt32Buffer Create(int32 value) {
            require OwnedInt32Buffer.TryCreate(1, out var raw);
            mut OwnedInt32Buffer buffer = move raw;
            require buffer.TryAppend(value);
            return move buffer;
        }
        """;

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task MovesNestedFieldRepairsItAndPreservesSiblingsAsync(
        BoundRuntimePolicy policy) =>
        ExecuteAsync(OwnerSource + """
            int32 Main() {
                mut Owner owner = new Owner(
                    new Child(Create(40), 2));
                mut int32 siblings = int32(0);
                {
                    ref<int32> sibling = ref owner.Nested.Value;
                    OwnedInt32Buffer extracted = move owner.Nested.Left;
                    owner.Nested.Left = Create(10);
                    siblings = extracted.Read(0) + *sibling;
                    sibling = _;
                }
                int32 repaired = owner.Nested.Left.Read(0)
                    + owner.Nested.Value;
                return siblings + repaired;
            }
            """, policy, 54);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task NestedFieldStateSurvivesARepairingLoopAsync(BoundRuntimePolicy policy) =>
        ExecuteAsync(OwnerSource + """
            int32 Main() {
                mut Owner owner = new Owner(
                    new Child(Create(40), 2));
                mut int32 iteration = int32(0);
                while (iteration < 2) {
                    OwnedInt32Buffer extracted = move owner.Nested.Left;
                    owner.Nested.Left = Create(10 + iteration);
                    iteration = iteration + 1;
                }
                int32 result = owner.Nested.Left.Read(0) + owner.Nested.Value;
                owner = _;
                return result;
            }
            """, policy, 13);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task NestedMaybeLiveFieldReplacementUsesRuntimeStateAsync(
        BoundRuntimePolicy policy) =>
        ExecuteAsync(OwnerSource + """
            int32 Replace(bool moveFirst) {
                mut Owner owner = new Owner(
                    new Child(Create(40), 2));
                if (moveFirst) {
                    OwnedInt32Buffer extracted = move owner.Nested.Left;
                }
                owner.Nested.Left = Create(10);
                int32 result = owner.Nested.Left.Read(0)
                    + owner.Nested.Value;
                owner = _;
                return result;
            }
            int32 Main() { return Replace(false) + Replace(true); }
            """, policy, 24);

    [Test]
    public Task NestedPartialCleanupPreservesRemainingFieldsAfterFailureAsync() =>
        ExecuteAsync("""
            unique struct Child {
                public mut OwnedInt32Buffer Left;
                public mut OwnedInt32Buffer Right;
                public Child(OwnedInt32Buffer left, OwnedInt32Buffer right) {
                    Left = move left;
                    Right = move right;
                }
            }
            unique struct Owner {
                public mut Child Nested;
                public Owner(Child nested) { Nested = move nested; }
            }
            OwnedInt32Buffer Create(int32 value) {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer buffer = move raw;
                require buffer.TryAppend(value);
                return move buffer;
            }
            OwnedInt32Buffer CreateMaybe(bool fail) {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer buffer = move raw;
                require buffer.TryAppend(10);
                if (fail) { require false; }
                return move buffer;
            }
            int32 Main() {
                mut Owner owner = new Owner(
                    new Child(Create(40), Create(2)));
                OwnedInt32Buffer extracted = move owner.Nested.Left;
                unsafe {
                    try {
                        owner.Nested.Left = CreateMaybe(true);
                    } catch {
                        return owner.Nested.Right.Read(0);
                    }
                }
                return 0;
            }
            """, BoundRuntimePolicy.CheckedExceptions, 2);

    [Test]
    public void RejectsWholeUseOfAnIncompleteNestedOwner()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(OwnerSource + """
            int32 Main() {
                mut Owner owner = new Owner(
                    new Child(Create(40), 2));
                OwnedInt32Buffer extracted = move owner.Nested.Left;
                return owner.Nested.ReadLeft();
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsNestedMoveWhenAnAncestorHasAuthoredCleanup()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Child {
                public mut OwnedInt32Buffer Left;
                public Child(OwnedInt32Buffer left) { Left = move left; }
            }
            unique struct Owner {
                public mut Child Nested;
                public Owner(Child nested) { Nested = move nested; }
                [Cleanup] public void Close() {}
            }
            OwnedInt32Buffer Create() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                return move raw;
            }
            int32 Main() {
                mut Owner owner = new Owner(new Child(Create()));
                OwnedInt32Buffer extracted = move owner.Nested.Left;
                return extracted.Count;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RejectsNestedReferenceAfterMovingItsAncestor()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(OwnerSource + """
            int32 Main() {
                mut Owner owner = new Owner(new Child(Create(40), 2));
                ref<int32> value = ref owner.Nested.Value;
                Child extracted = move owner.Nested;
                return *value;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [TestCase("""
        unique struct Child {
            public mut OwnedInt32Buffer Left;
            public Child(OwnedInt32Buffer left) { Left = move left; }
        }
        unique struct Owner {
            public mut Child Nested;
            public Owner(Child nested) { Nested = move nested; }
        }
        OwnedInt32Buffer Create() {
            require OwnedInt32Buffer.TryCreate(1, out var raw);
            return move raw;
        }
        int32 Main() {
            mut Owner owner = new Owner(new Child(Create()));
            OwnedInt32Buffer first = move owner.Nested.Left;
            OwnedInt32Buffer second = move owner.Nested.Left;
            return first.Count + second.Count;
        }
        """)]
    [TestCase("""
        unique struct Child {
            public OwnedInt32Buffer Left;
            public Child(OwnedInt32Buffer left) { Left = move left; }
        }
        unique struct Owner {
            public mut Child Nested;
            public Owner(Child nested) { Nested = move nested; }
        }
        OwnedInt32Buffer Create() {
            require OwnedInt32Buffer.TryCreate(1, out var raw);
            return move raw;
        }
        int32 Main() {
            mut Owner owner = new Owner(new Child(Create()));
            OwnedInt32Buffer extracted = move owner.Nested.Left;
            return extracted.Count;
        }
        """)]
    [TestCase("""
        unique struct Child {
            public mut OwnedInt32Buffer Left;
            public Child(OwnedInt32Buffer left) { Left = move left; }
        }
        unique struct Owner {
            public mut Child Nested;
            public Owner(Child nested) { Nested = move nested; }
        }
        OwnedInt32Buffer Create() {
            require OwnedInt32Buffer.TryCreate(1, out var raw);
            return move raw;
        }
        int32 Main() {
            Owner owner = new Owner(new Child(Create()));
            OwnedInt32Buffer extracted = move owner.Nested.Left;
            return extracted.Count;
        }
        """)]
    [TestCase("""
        unique struct Child {
            public mut OwnedInt32Buffer Left;
            public Child(OwnedInt32Buffer left) { Left = move left; }
        }
        unique struct Owner {
            public Child Nested;
            public Owner(Child nested) { Nested = move nested; }
        }
        OwnedInt32Buffer Create() {
            require OwnedInt32Buffer.TryCreate(1, out var raw);
            return move raw;
        }
        int32 Main() {
            mut Owner owner = new Owner(new Child(Create()));
            OwnedInt32Buffer extracted = move owner.Nested.Left;
            return extracted.Count;
        }
        """)]
    public void RejectsRepeatedNestedMoveAndMissingPathAuthority(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
    }

    private static Task ExecuteAsync(
        string source, BoundRuntimePolicy policy, int expected) =>
        policy == BoundRuntimePolicy.FailFast
            ? JitTests.RunFailFastAsync(source, expected)
            : JitTests.RunCheckedAsync(source, expected);
}
