// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivateInitReferencePublishesOnlyOnSuccessAsync(bool checkedFailures)
    {
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(InitReferenceTests.ScalarSource, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(InitReferenceTests.ScalarSource, 42).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FloatingInitReferencePublishesOnSuccessAsync(bool checkedFailures)
    {
        const string Source = """
            bool Publish64(init ref<float64> value) {
                value = 0x1.fp+2;
                return true;
            }
            bool Publish32(init ref<float32> value) {
                value = 3.5;
                return true;
            }
            int32 Main() {
                init float64 value64;
                init float32 value32;
                if (!Publish64(init value64) || !Publish32(init value32)) { return 1; }
                return value64 == 7.75 && value32 == 3.5 ? 42 : 0;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Fact]
    public Task CheckedInitReferenceFailurePropagatesWithoutPublicationAsync()
    {
        const string Source = """
            bool Fail(init ref<int32> value) {
                require false;
                value = 42;
                return true;
            }
            int32 Main() {
                unsafe {
                    try {
                        if (Fail(init var missing)) { return 1; }
                        return 2;
                    }
                    catch { return 42; }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }

    [Fact]
    public Task CheckedNonBooleanUniqueInitFailureDoesNotPublishAsync()
    {
        const string Source = """
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Fail(init ref<Owner> value) {
                value = new Owner(42);
                require false;
                return 0;
            }
            int32 Main() {
                init Owner value;
                unsafe {
                    try {
                        int32 status = Fail(init value);
                        return status;
                    } catch {
                        return value is init ? 42 : 43;
                    }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 43);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyAggregateInitReferencePublishesCompleteValueAsync(bool checkedFailures)
    {
        const string Source = """
            struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) {
                    First = first;
                    Second = second;
                }
            }
            bool Fill(bool publish, init ref<Pair> value) {
                if (!publish) { return false; }
                value = new Pair(40, 2);
                return true;
            }
            int32 Main() {
                if (Fill(false, init var pair)) { return 1; }
                require Fill(true, init pair);
                return pair.First + pair.Second;
            }
            """;
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(Source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(Source, 42).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OwningInitReferenceTransfersTheCompleteValueAsync(bool checkedFailures)
    {
        const string Source = """
            unique struct Owner {
                public mut OwnedInt32Buffer Buffer;
                public Owner(int32 number) {
                    require OwnedInt32Buffer.TryCreate(1, out var buffer);
                    Buffer = move buffer;
                    require Buffer.TryAppend(number);
                }
            }
            bool Create(bool publish, init ref<Owner> result) {
                if (!publish) { return false; }
                result = new Owner(42);
                return true;
            }
            int32 Main() {
                if (Create(false, init var owner)) { return 1; }
                require Create(true, init owner);
                return owner.Buffer.Read(0);
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ManagedInitReferenceRootsOnlyPublishedHandlesAsync(bool checkedFailures) =>
        checkedFailures ? AssertCheckedExecutionAsync(InitReferenceTests.ManagedSource, 42)
            : AssertFailFastExecutionAsync(InitReferenceTests.ManagedSource, 42);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ManagedInitPublishesAnInitializedNullDistinctFromVacancyAsync(bool checkedFailures)
    {
        const string Source = """
            bool Fill(bool publish, init ref<ManagedNode?> value) {
                if (!publish) { return false; }
                value = null;
                return true;
            }
            int32 Main() {
                if (Fill(false, init var node)) { return 1; }
                require Fill(true, init node);
                if (node is some present) { return present.Value; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Fact]
    public Task CheckedManagedInitFailureDoesNotPublishOrRetainTheScratchHandleAsync() =>
        AssertCheckedExecutionAsync("""
            bool Fail(init ref<write ManagedNode> value) {
                require ManagedNode.TryCreate(42, out var created);
                value = created;
                require false;
                return true;
            }
            int32 Main() {
                unsafe {
                    try {
                        if (Fail(init var missing)) { return 1; }
                        return 2;
                    } catch {
                        if (ManagedHeap.Collect() != 1) { return 3; }
                        return 42;
                    }
                }
            }
            """, 42);
}
