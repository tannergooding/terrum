using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed class PartialOwnerFieldLifecycleTests
{
    private const string PairSource = """
        unique struct Pair {
            public mut OwnedInt32Buffer Left;
            public mut OwnedInt32Buffer Right;
            public Pair(OwnedInt32Buffer left, OwnedInt32Buffer right) {
                Left = move left;
                Right = move right;
            }
        }
        """;

    private const string FieldMoveProgram = PairSource + """
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(1, out var leftRaw);
            require OwnedInt32Buffer.TryCreate(1, out var rightRaw);
            mut OwnedInt32Buffer left = move leftRaw;
            mut OwnedInt32Buffer right = move rightRaw;
            require left.TryAppend(40);
            require right.TryAppend(2);
            mut Pair pair = new Pair(move left, move right);
            OwnedInt32Buffer extracted = move pair.Left;
            return extracted.Read(0) + pair.Right.Read(0);
        }
        """;

    private const string FieldMoveFailureProgram = PairSource + """
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(1, out var leftRaw);
            require OwnedInt32Buffer.TryCreate(1, out var rightRaw);
            mut OwnedInt32Buffer left = move leftRaw;
            mut OwnedInt32Buffer right = move rightRaw;
            require left.TryAppend(40);
            require right.TryAppend(2);
            mut Pair pair = new Pair(move left, move right);
            unsafe {
                try {
                    OwnedInt32Buffer extracted = move pair.Left;
                    require false;
                } catch {
                    return pair.Right.Read(0);
                }
            }
            return 0;
        }
        """;

    private const string FieldRepairFailureProgram = PairSource + """
        OwnedInt32Buffer Create(bool fail) {
            require OwnedInt32Buffer.TryCreate(1, out var raw);
            mut OwnedInt32Buffer value = move raw;
            require value.TryAppend(10);
            if (fail) { require false; }
            return move value;
        }
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(1, out var leftRaw);
            require OwnedInt32Buffer.TryCreate(1, out var rightRaw);
            mut OwnedInt32Buffer left = move leftRaw;
            mut OwnedInt32Buffer right = move rightRaw;
            require left.TryAppend(40);
            require right.TryAppend(2);
            mut Pair pair = new Pair(move left, move right);
            OwnedInt32Buffer extracted = move pair.Left;
            unsafe {
                try {
                    pair.Left = Create(true);
                } catch {
                    return pair.Right.Read(0);
                }
            }
            return 0;
        }
        """;

    private const string PartialOwnerReplacementProgram = PairSource + """
        OwnedInt32Buffer Create(int32 value) {
            require OwnedInt32Buffer.TryCreate(1, out var raw);
            mut OwnedInt32Buffer buffer = move raw;
            require buffer.TryAppend(value);
            return move buffer;
        }
        int32 Main() {
            mut Pair pair = new Pair(Create(40), Create(2));
            OwnedInt32Buffer extracted = move pair.Left;
            mut Pair replacement = new Pair(Create(1), Create(1));
            pair = move replacement;
            return extracted.Read(0) + pair.Left.Read(0) + pair.Right.Read(0);
        }
        """;

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task MovesTopLevelFieldAndCleansOnlyLiveChildrenAsync(BoundRuntimePolicy policy) =>
        ExecuteAsync(FieldMoveProgram, policy, 42);

    [Test]
    public Task PartialCleanupRunsAfterCheckedFailureAsync() =>
        ExecuteAsync(FieldMoveFailureProgram, BoundRuntimePolicy.CheckedExceptions, 2);

    [Test]
    public Task FailedVacantFieldRepairPreservesSiblingAsync() =>
        ExecuteAsync(FieldRepairFailureProgram, BoundRuntimePolicy.CheckedExceptions, 2);

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public Task ReplacingPartialWholeOwnerCleansItsLiveSubsetAsync(BoundRuntimePolicy policy) =>
        ExecuteAsync(PartialOwnerReplacementProgram, policy, 42);

    [TestCase("""
        unique struct Pair {
            public mut OwnedInt32Buffer Left;
            public mut OwnedInt32Buffer Right;
            public Pair(OwnedInt32Buffer left, OwnedInt32Buffer right) {
                Left = move left;
                Right = move right;
            }
            public int32 ReadRight() { return Right.Read(0); }
        }
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(1, out var leftRaw);
            require OwnedInt32Buffer.TryCreate(1, out var rightRaw);
            mut OwnedInt32Buffer left = move leftRaw;
            mut OwnedInt32Buffer right = move rightRaw;
            mut Pair pair = new Pair(move left, move right);
            OwnedInt32Buffer extracted = move pair.Left;
            return pair.ReadRight();
        }
        """)]
    [TestCase("""
        unique struct Pair {
            public mut OwnedInt32Buffer Left;
            public mut OwnedInt32Buffer Right;
            public Pair(OwnedInt32Buffer left, OwnedInt32Buffer right) {
                Left = move left;
                Right = move right;
            }
            [Cleanup] public void Close() {}
        }
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(1, out var leftRaw);
            require OwnedInt32Buffer.TryCreate(1, out var rightRaw);
            mut OwnedInt32Buffer left = move leftRaw;
            mut OwnedInt32Buffer right = move rightRaw;
            mut Pair pair = new Pair(move left, move right);
            OwnedInt32Buffer extracted = move pair.Left;
            return extracted.Count;
        }
        """)]
    [TestCase("""
        unique struct Pair {
            public OwnedInt32Buffer Left;
            public mut OwnedInt32Buffer Right;
            public Pair(OwnedInt32Buffer left, OwnedInt32Buffer right) {
                Left = move left;
                Right = move right;
            }
        }
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(1, out var leftRaw);
            require OwnedInt32Buffer.TryCreate(1, out var rightRaw);
            mut OwnedInt32Buffer left = move leftRaw;
            mut OwnedInt32Buffer right = move rightRaw;
            mut Pair pair = new Pair(move left, move right);
            OwnedInt32Buffer extracted = move pair.Left;
            return extracted.Count;
        }
        """)]
    [TestCase("""
        unique struct Pair {
            public mut OwnedInt32Buffer Left;
            public mut OwnedInt32Buffer Right;
            public Pair(OwnedInt32Buffer left, OwnedInt32Buffer right) {
                Left = move left;
                Right = move right;
            }
        }
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(1, out var leftRaw);
            require OwnedInt32Buffer.TryCreate(1, out var rightRaw);
            mut OwnedInt32Buffer left = move leftRaw;
            mut OwnedInt32Buffer right = move rightRaw;
            mut Pair pair = new Pair(move left, move right);
            OwnedInt32Buffer extracted = move pair.Left;
            OwnedInt32Buffer duplicate = move pair.Left;
            return extracted.Count + duplicate.Count;
        }
        """)]
    public void RejectsIncompleteWholeUseInvalidAuthorityAndRepeatedMove(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3023"), Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    private static Task ExecuteAsync(string source, BoundRuntimePolicy policy, int expected) =>
        policy == BoundRuntimePolicy.FailFast
            ? JitTests.RunFailFastAsync(source, expected)
            : JitTests.RunCheckedAsync(source, expected);
}
