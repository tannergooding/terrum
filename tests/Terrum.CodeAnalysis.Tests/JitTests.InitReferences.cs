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
}
