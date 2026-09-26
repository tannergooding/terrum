// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalCycleSampleAgreesBetweenAotAndJitAsync(bool checkedFailures)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "managed-optional-cycle.terrum")).ConfigureAwait(true);
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ConstructorCompletesOptionalFieldsBeforeSelfCycleAsync(bool checkedFailures)
    {
        const string Source = """
            class Cycle {
                public int32 Value;
                public mut Cycle? Next;

                public Cycle(int32 value) {
                    Value = value;
                    Next = null;
                    Next = this;
                }
            }
            int32 Main() {
                {
                    require Cycle.TryCreate(42, out var root);
                    if (ManagedHeap.Collect() != 0) { return 1; }
                    require root.Next is some self;
                    if (self.Value != 42) { return 2; }
                }
                if (ManagedHeap.Collect() != 1) { return 3; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NullableConstructorArgumentSurvivesFactoryAndDetachmentAsync(bool checkedFailures)
    {
        const string Source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            class Host {
                public mut Leaf? Edge;
                public Host(Leaf? edge) { Edge = edge; }
            }
            int32 Main() {
                require Host.TryCreate(null, out var empty);
                if (!(empty.Edge is null)) { return 1; }
                {
                    require Leaf.TryCreate(42, out var leaf);
                    require Host.TryCreate(leaf, out var linked);
                    if (linked.Edge is some snapshot) {
                        linked.Edge = null;
                        if (ManagedHeap.Collect() != 0) { return 2; }
                        if (snapshot.Value != 42) { return 3; }
                    } else { return 4; }
                }
                if (ManagedHeap.Collect() != 2) { return 5; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedTokenCandidateRejectsInvalidMutationsAndCollectsClearedLinksAsync(bool checkedFailures)
    {
        var component = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "self-host",
            "ManagedTokenChain.terrum")).ConfigureAwait(true);
        var source = component + """

            int32 Main() {
                require ManagedTokenChain.TryCreate(out var chain);
                if (chain.GetCount() != 0 || chain.GetVersion() != 0 || chain.TryClear()) { return 1; }
                require chain.TryAppend(1, 0);
                require chain.TryAppend(7, 42);
                if (chain.TryAppend(-1, 43) || chain.TryAppend(9, 41)) { return 2; }
                if (chain.TryGet(-1, out var invalidKind, out var invalidOffset)) { return 3; }
                require chain.TryGet(1, out var kind, out var sourceOffset);
                if (kind != 7 || sourceOffset != 42 || chain.GetCount() != 2
                    || chain.GetVersion() != 2) { return 4; }
                require chain.TryClear();
                if (chain.GetCount() != 0 || chain.GetVersion() != 3 || chain.TryClear()) { return 5; }
                if (ManagedHeap.Collect() != 2) { return 6; }
                for (mut int32 i = 0; i < 64; i = i + 1) {
                    require chain.TryAppend(i, i);
                }
                if (chain.TryAppend(64, 64) || chain.GetCount() != 64
                    || chain.GetVersion() != 67) { return 7; }
                require chain.TryGet(63, out var lastKind, out var lastOffset);
                if (lastKind != 63 || lastOffset != 63) { return 8; }
                require chain.TryClear();
                if (ManagedHeap.Collect() != 64) { return 9; }
                return 42;
            }
            """;
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }

    [Fact]
    public Task CheckedFailureAbandonsPartiallyInitializedOptionalEdgesAsync()
    {
        const string Source = """
            class Envelope {
                public ManagedNode? Edge;
                public int32 Value;

                public Envelope(bool fail) {
                    Edge = null;
                    require ManagedNode.TryCreate(7, out var child);
                    Edge = child;
                    Value = 42;
                    require !fail;
                }
            }
            int32 Main() {
                unsafe {
                    try {
                        if (Envelope.TryCreate(true, out var abandoned)) { return 1; }
                        return 2;
                    } catch {
                        if (ManagedHeap.Collect() != 1) { return 3; }
                        require Envelope.TryCreate(false, out var complete);
                        if (complete.Edge is some snapshot) {
                            if (ManagedHeap.Collect() != 0) { return 4; }
                            return snapshot.Value + 35;
                        }
                        return 5;
                    }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
