// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task VacantAssignmentPublishesCopyAndUniqueStorageAsync(bool checkedFailures)
    {
        const string Source = """
            struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) { First = first; Second = second; }
            }
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            int32 Main() {
                init Pair pair;
                pair = new Pair(20, 22);
                init var owner;
                owner = new Owner(pair.First + pair.Second);
                Owner moved = move owner;
                owner = new Owner(moved.Value);
                return owner.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task VacantAssignmentRootsManagedStorageOnlyAfterPublicationAsync(bool checkedFailures)
    {
        const string Source = """
            write ManagedNode Create() {
                require ManagedNode.TryCreate(42, out var node);
                return node;
            }
            int32 Main() {
                init write ManagedNode node;
                if (ManagedHeap.Collect() != 0) { return 1; }
                node = Create();
                if (ManagedHeap.Collect() != 0) { return 2; }
                return node.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Fact]
    public Task FailedVacantAssignmentLeavesStorageAvailableInTheHandlerAsync() =>
        AssertCheckedExecutionAsync("""
            int32 Divide(int32 divisor) { return 42 / divisor; }
            int32 Main() {
                init int32 value;
                unsafe {
                    try { value = Divide(0); }
                    catch { value = 42; return value; }
                }
                return 1;
            }
            """, 42);
}
