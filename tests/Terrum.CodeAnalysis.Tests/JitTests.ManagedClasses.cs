// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task WritableOptionalFieldProjectionAndReadonlyReceiverArgumentAgreeAcrossAotAndJitAsync(
        bool checkedFailures)
    {
        const string Source = """
            mut class Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            class Holder {
                public write Leaf? Child;
                public Holder(write Leaf child) { Child = child; }
                public void Update(write Leaf target) { target.Value = 42; }
            }
            bool Create(out write Leaf result) {
                return Leaf.TryCreate(40, out result);
            }
            int32 Main() {
                require Create(out var leaf);
                require Holder.TryCreate(leaf, out var holder);
                write Leaf? optional = holder.Child;
                require optional is some projected;
                holder.Update(projected);
                require holder.Child is some field;
                require field.Value == 42 && ManagedHeap.Collect() == 0;
                return leaf.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task ManagedClassSampleAgreesBetweenAotAndJitAsync(bool checkedFailures)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "managed-classes.terrum")).ConfigureAwait(true);
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task ManagedClassReturnStaysRootedAcrossFollowingArgumentAsync(bool checkedFailures)
    {
        const string Source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            Leaf Create() {
                require Leaf.TryCreate(42, out var leaf);
                return leaf;
            }
            int32 Observe(Leaf leaf, int32 reclaimed) { return leaf.Value + reclaimed; }
            int32 Main() { return Observe(Create(), ManagedHeap.Collect()); }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task ExplicitlyWritableLeafFieldCanBeWrittenThroughReadonlyManagedHandleChainAsync(bool checkedFailures)
    {
        const string Source = """
            mut class Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            class Pair {
                public write Leaf Left;
                public Pair(write Leaf left) { Left = left; }
            }
            class Wrapper {
                public Pair Parent;
                public Wrapper(Pair parent) { Parent = parent; }
            }
            int32 Main() {
                require Leaf.TryCreate(40, out var leaf);
                require Pair.TryCreate(leaf, out var pair);
                require Wrapper.TryCreate(pair, out var wrapper);
                pair.Left.Value = 41;
                wrapper.Parent.Left.Value = 42;
                return leaf.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task RetiredClassHandlesDoNotTurnScalarsIntoRootsAsync(bool checkedFailures)
    {
        const string Source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Main() {
                mut int32 scalar = 42;
                {
                    require Leaf.TryCreate(7, out var temporary);
                    scalar = scalar + temporary.Value - temporary.Value;
                }
                if (ManagedHeap.Collect() != 1) { return 1; }
                return scalar;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task ExhaustedClassCapacityFailsWithoutPublishingAndReusesCollectedSlotsAsync(bool checkedFailures)
    {
        const string Source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Main() {
                for (mut int32 i = 0; i < 4096; i = i + 1) {
                    require Leaf.TryCreate(i, out var temporary);
                }
                if (Leaf.TryCreate(1, out var unavailable)) { return 1; }
                if (ManagedHeap.Collect() != 4096) { return 2; }
                require Leaf.TryCreate(42, out var reused);
                return reused.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task RepeatedClassAndNodeCollectionsReuseUnrootedObjectsAsync(bool checkedFailures)
    {
        const string Source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            class Pair {
                public Leaf Left;
                public ManagedNode Edge;
                public Pair(Leaf left, ManagedNode edge) { Left = left; Edge = edge; }
            }
            int32 Main() {
                for (mut int32 i = 0; i < 128; i = i + 1) {
                    {
                        require Leaf.TryCreate(40, out var leaf);
                        require ManagedNode.TryCreate(2, out var edge);
                        require Pair.TryCreate(leaf, edge, out var pair);
                        if (pair.Left.Value + pair.Edge.Value != 42) { return 1; }
                    }
                    if (ManagedHeap.Collect() != 3) { return 2; }
                }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task SixteenManagedEdgesKeepLastFieldAliveAcrossCollectionAsync(bool checkedFailures)
    {
        const string Source = """
            class Wide {
                public ManagedNode N0;
                public ManagedNode N1;
                public ManagedNode N2;
                public ManagedNode N3;
                public ManagedNode N4;
                public ManagedNode N5;
                public ManagedNode N6;
                public ManagedNode N7;
                public ManagedNode N8;
                public ManagedNode N9;
                public ManagedNode N10;
                public ManagedNode N11;
                public ManagedNode N12;
                public ManagedNode N13;
                public ManagedNode N14;
                public ManagedNode N15;
                public Wide(ManagedNode node) {
                    N0 = node;
                    N1 = node;
                    N2 = node;
                    N3 = node;
                    N4 = node;
                    N5 = node;
                    N6 = node;
                    N7 = node;
                    N8 = node;
                    N9 = node;
                    N10 = node;
                    N11 = node;
                    N12 = node;
                    N13 = node;
                    N14 = node;
                    N15 = node;
                }
            }
            bool CreateWide(out Wide result) {
                require ManagedNode.TryCreate(42, out var node);
                return Wide.TryCreate(node, out result);
            }
            int32 Main() {
                require CreateWide(out var wide);
                if (ManagedHeap.Collect() != 0) { return 1; }
                return wide.N15.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Test]
    public Task CheckedConstructorFailureReleasesProvisionalObjectsForReuseAsync()
    {
        const string Source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) {
                    Value = value;
                    require value != 0;
                }
            }
            int32 Main() {
                for (mut int32 i = 0; i < 4100; i = i + 1) {
                    unsafe {
                        try {
                            if (Leaf.TryCreate(0, out var incomplete)) { return 1; }
                            return 2;
                        } catch {}
                    }
                }
                require Leaf.TryCreate(42, out var complete);
                return complete.Value;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }

    [Test]
    public Task CheckedConstructorFailureReleasesNestedReferenceBeforeOuterRecoveryAsync()
    {
        const string Source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            class Outer {
                public Leaf Inner;
                public Outer(int32 value, bool fail) {
                    require Leaf.TryCreate(value, out var child);
                    Inner = child;
                    require !fail;
                }
            }
            int32 Main() {
                unsafe {
                    try {
                        if (Outer.TryCreate(40, true, out var abandoned)) { return 1; }
                        return 2;
                    } catch {
                        if (ManagedHeap.Collect() != 1) { return 3; }
                        require Outer.TryCreate(42, false, out var complete);
                        return complete.Inner.Value;
                    }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
