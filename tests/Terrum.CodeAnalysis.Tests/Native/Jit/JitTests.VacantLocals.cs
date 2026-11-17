// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

internal sealed partial class JitTests
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
             bool Copy<T>(T source, ref<init T> result)  {
                *result = source;
                return true;
            }
            struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) { First = first; Second = second; }
            }
            int32 Main() {
                Pair input = new Pair(40, 2);
                init Pair output;
                require Copy(input, ref init output);
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
            bool Fill(bool publish, ref<init write ManagedNode> node) {
                if (!publish) { return false; }
                require ManagedNode.TryCreate(42, out var created);
                *node = created;
                return true;
            }
            int32 Main() {
                init write ManagedNode node;
                if (Fill(false, ref init node)) { return 1; }
                if (ManagedHeap.Collect() != 0) { return 2; }
                require Fill(true, ref init node);
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
            bool Fill(ref<init write ManagedNode> node) {
                require ManagedNode.TryCreate(42, out var created);
                *node = created;
                return true;
            }
            int32 Main() {
                init write ManagedNode node;
                require Fill(ref init node);
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
                 [Cleanup] public void Close() { }
            }
            bool Create(ref<init Owner> value) {
                *value = new Owner(42);
                return true;
            }
            int32 Main() {
                init Owner owner;
                require Create(ref init owner);
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
             bool Copy<T>(T source, ref<init T> result)  {
                *result = source;
                return true;
            }
            T Relay<T>(T source)  {
                init T first;
                init T second;
                require Copy(source, ref init first);
                require Copy(first, ref init second);
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
            bool Fail(ref<init int32> value) {
                require false;
                *value = 42;
                return true;
            }
            int32 Main() {
                init int32 value;
                unsafe {
                    try { require Fail(ref init value); }
                    catch { return 42; }
                }
                return 1;
            }
            """, 42);
}
