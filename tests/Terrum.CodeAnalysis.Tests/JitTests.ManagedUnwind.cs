// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData("""
        int32 Main() {
            require ManagedNode.TryCreate(40, out var kept);
            mut int32 reclaimed = 0;
            unsafe {
                try {
                    require ManagedNode.TryCreate(2, out var departed);
                    require false;
                } catch {
                    reclaimed = ManagedHeap.Collect();
                }
            }
            if (reclaimed != 1) { return 1; }
            return kept.Value + reclaimed + 1;
        }
        """)]
    [InlineData("""
        void DiscardCycle() {
            require ManagedNode.TryCreate(1, out var first);
            require ManagedNode.TryCreate(2, out var second);
            first.SetNext(second);
            second.SetNext(first);
            require false;
        }
        int32 Main() {
            unsafe {
                try { DiscardCycle(); }
                catch {
                    if (ManagedHeap.Collect() != 2) { return 1; }
                    return 42;
                }
            }
            return 0;
        }
        """)]
    [InlineData("""
        ManagedNode Create() {
            require ManagedNode.TryCreate(42, out var node);
            return node;
        }
        int32 Combine(ManagedNode first, int32 second) { return first.Value + second; }
        int32 Main() {
            unsafe {
                try { return Combine(Create(), 1 / 0); }
                catch {
                    if (ManagedHeap.Collect() != 1) { return 1; }
                    return 42;
                }
            }
        }
        """)]
    [InlineData("""
        bool CreateAndFail(out write ManagedNode result) {
            require ManagedNode.TryCreate(42, out result);
            require false;
            return true;
        }
        int32 Main() {
            unsafe {
                try {
                    if (CreateAndFail(out var unpublished)) { return 1; }
                } catch {
                    if (ManagedHeap.Collect() != 1) { return 2; }
                    return 42;
                }
            }
            return 0;
        }
        """)]
    [InlineData("""
        unique struct CollectOnExit {
            private int32 _value;
            public CollectOnExit() { _value = 0; }
            [Cleanup][NoThrow] public void Close() {
                int32 collected = ManagedHeap.Collect();
            }
        }
        ManagedNode Create() {
            require ManagedNode.TryCreate(42, out var node);
            return node;
        }
        ManagedNode ReturnThroughCleanup() {
            CollectOnExit cleanup = new CollectOnExit();
            return Create();
        }
        int32 Main() {
            ManagedNode node = ReturnThroughCleanup();
            if (ManagedHeap.Collect() != 0) { return 1; }
            return node.Value;
        }
        """)]
    [InlineData("""
        int32 Main() {
            require ManagedNode.TryCreate(40, out var kept);
            mut int32 released = 0;
            unsafe {
                try {
                    try {
                        require ManagedNode.TryCreate(2, out var departed);
                        require false;
                    } finally {
                        released = ManagedHeap.Collect();
                    }
                } catch {
                    if (released != 1 || ManagedHeap.Collect() != 0) { return 1; }
                    return kept.Value + 2;
                }
            }
        }
        """)]
    [InlineData("""
        int32 Main() {
            unsafe {
                try {
                    try { require false; }
                    catch {
                        require ManagedNode.TryCreate(42, out var departed);
                        require false;
                    }
                } catch {
                    if (ManagedHeap.Collect() != 1) { return 1; }
                    return 42;
                }
            }
            return 0;
        }
        """)]
    [InlineData("""
        bool Refuse(out write ManagedNode result) {
            require ManagedNode.TryCreate(42, out result);
            return false;
        }
        int32 Main() {
            if (Refuse(out var unpublished)) { return 1; }
            if (ManagedHeap.Collect() != 1) { return 2; }
            return 42;
        }
        """)]
    public Task CheckedManagedRootsRetireAcrossAotAndJitAsync(string source) =>
        AssertCheckedExecutionAsync(source, 42);

    [Fact]
    public Task UnhandledManagedCheckedFailureRetainsPrivateReasonAsync() =>
        AssertCheckedExecutionAsync("""
            int32 Main() {
                require ManagedNode.TryCreate(42, out var node);
                require false;
                return node.Value;
            }
            """, unchecked((int)0xE0540003));
}
