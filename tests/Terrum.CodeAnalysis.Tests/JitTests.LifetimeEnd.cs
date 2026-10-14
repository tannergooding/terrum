// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task LifetimeEndRetainsGenericCopyAndClosedParameterAuthorityAsync(bool checkedFailures)
    {
        const string Source = """
            T Copy<T>(T input) where T : [Copy] {
                T local = input;
                local = _;
                return input;
            }
            int32 Main() { return Copy(int32(42)); }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task LifetimeEndPreservesCopyAndWideStorageAuthorityAsync(bool checkedFailures)
    {
        const string Source = """
            struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) { First = first; Second = second; }
            }
            int32 Main() {
                Pair original = new Pair(1, 2);
                Pair copy = original;
                original = _;
                init Pair pair;
                pair = copy;
                copy = _;
                pair = _;
                pair = new Pair(20, 22);
                mut uint64 wide = 18446744073709551615;
                wide = _;
                wide = uint64(pair.First + pair.Second);
                return int32(wide);
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task LifetimeEndRetiresOnlyTheSelectedManagedRootAndCanPublishANewLifetimeAsync(bool checkedFailures)
    {
        const string Source = """
            write ManagedNode Create(int32 value) {
                require ManagedNode.TryCreate(value, out var created);
                return created;
            }
            int32 Main() {
                init write ManagedNode node;
                node = Create(1);
                write ManagedNode copy = node;
                node = _;
                if (ManagedHeap.Collect() != 0 || copy.Value != 1) { return 1; }
                copy = _;
                if (ManagedHeap.Collect() != 1) { return 2; }
                node = Create(42);
                if (ManagedHeap.Collect() != 0 || node.Value != 42) { return 3; }
                node = _;
                if (ManagedHeap.Collect() != 1) { return 4; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task LifetimeEndRetiresClassRootsWithoutDestroyingDuplicateHandlesAsync(bool checkedFailures)
    {
        const string Source = """
            class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            Box Create(int32 value) {
                require Box.TryCreate(value, out var created);
                return created;
            }
            int32 Main() {
                mut Box first = Create(1);
                Box second = first;
                first = _;
                if (ManagedHeap.Collect() != 0 || second.Value != 1) { return 1; }
                second = _;
                if (ManagedHeap.Collect() != 1) { return 2; }
                first = Create(42);
                if (ManagedHeap.Collect() != 0 || first.Value != 42) { return 3; }
                first = _;
                if (ManagedHeap.Collect() != 1) { return 4; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task LifetimeEndRetiresBufferViewsAndPreservesTheirActualBackingAsync(bool checkedFailures)
    {
        const string Source = """
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var raw);
                mut OwnedInt32Buffer owner = move raw;
                require owner.TryAppend(42);
                ReadonlyInt32BufferView view = owner.Slice(0, 1);
                int32 result = view.Read(0);
                view = _;
                mut MutableInt32BufferView writeView = owner.SliceMutable(0, 1);
                writeView.Set(0, result);
                writeView = _;
                require owner.TryAppend(0);
                owner = _;
                return result;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Test]
    public Task LifetimeEndFailedReinitializationLeavesVacancyForRecoveryAsync() =>
        AssertCheckedExecutionAsync("""
            int32 Divide(int32 divisor) { return 42 / divisor; }
            int32 Main() {
                init int32 value;
                value = 1;
                value = _;
                unsafe {
                    try { value = Divide(0); }
                    catch { value = 42; return value; }
                }
                return 1;
            }
            """, 42);

    [Test]
    public Task LifetimeEndRetiredManagedRootStaysRetiredAcrossFailedReinitializationAsync() =>
        AssertCheckedExecutionAsync("""
            write ManagedNode Fail() { require false; require ManagedNode.TryCreate(0, out var node); return node; }
            int32 Main() {
                require ManagedNode.TryCreate(1, out var raw);
                init write ManagedNode node;
                node = raw;
                raw = _;
                node = _;
                unsafe {
                    try { node = Fail(); }
                    catch { if (ManagedHeap.Collect() == 1) { return 42; } return 2; }
                }
                return 1;
            }
            """, 42);
}
