// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task StandaloneVacantScalarPublishesOnlyOnTrueAsync(bool checkedFailures) =>
        checkedFailures ? AssertCheckedExecutionAsync(VacantLocalTests.ScalarSource, 42)
            : AssertFailFastExecutionAsync(VacantLocalTests.ScalarSource, 42);
    [TestCase(false)]
    [TestCase(true)]
    public Task GenericCopyInfersStandaloneVacantOutputAsync(bool checkedFailures)
    {
        const string Source = """
            [NoThrow] bool Copy<T>(T source, init ref<T> result) where T : [Copy] {
                result = source;
                return true;
            }
            struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) { First = first; Second = second; }
            }
            int32 Main() {
                Pair input = new Pair(40, 2);
                init var output;
                require Copy(input, init output);
                return output.First + output.Second;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task StandaloneVacantManagedRootStartsInactiveAndPublishesOnSuccessAsync(bool checkedFailures)
    {
        const string Source = """
            bool Fill(bool publish, init ref<write ManagedNode> node) {
                if (!publish) { return false; }
                require ManagedNode.TryCreate(42, out var created);
                node = created;
                return true;
            }
            int32 Main() {
                init var node;
                if (Fill(false, init node)) { return 1; }
                if (ManagedHeap.Collect() != 0) { return 2; }
                require Fill(true, init node);
                if (ManagedHeap.Collect() != 0) { return 3; }
                return node.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task ExplicitManagedVacancyRetainsTheWritableHandleAsync(bool checkedFailures)
    {
        const string Source = """
            bool Fill(init ref<write ManagedNode> node) {
                require ManagedNode.TryCreate(42, out var created);
                node = created;
                return true;
            }
            int32 Main() {
                init write ManagedNode node;
                require Fill(init node);
                if (ManagedHeap.Collect() != 0) { return 1; }
                return node.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task ExplicitVacantOwnerIsMovedAndCleanedAtScopeExitAsync(bool checkedFailures)
    {
        const string Source = """
            unique struct Owner {
                public int32 Number;
                public Owner(int32 number) { Number = number; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            bool Create(init ref<Owner> value) {
                value = new Owner(42);
                return true;
            }
            int32 Main() {
                init Owner owner;
                require Create(init owner);
                return owner.Number;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task GenericTemplatesPreserveTypedAndInferredVacantSlotsAsync(bool checkedFailures)
    {
        const string Source = """
            [NoThrow] bool Copy<T>(T source, init ref<T> result) where T : [Copy] {
                result = source;
                return true;
            }
            T Relay<T>(T source) where T : [Copy] {
                init T first;
                init var second;
                require Copy(source, init first);
                require Copy(first, init second);
                return second;
            }
            int32 Main() { return Relay(int32(42)); }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Test]
    public Task CheckedFailureCannotPublishStandaloneVacantStorageAsync() =>
        AssertCheckedExecutionAsync("""
            bool Fail(init ref<int32> value) {
                value = 42;
                require false;
                return true;
            }
            int32 Main() {
                init int32 value;
                unsafe {
                    try { require Fail(init value); }
                    catch { return 42; }
                }
                return 1;
            }
            """, 42);
}
