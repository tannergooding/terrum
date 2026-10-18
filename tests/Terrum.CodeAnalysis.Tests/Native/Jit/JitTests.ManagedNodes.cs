// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [TestCase("""
        ManagedNode Create(int32 value) {
            require ManagedNode.TryCreate(value, out var node);
            return node;
        }
        int32 Observe(ManagedNode node, int32 collected) {
            if (collected != 0) { return 1; }
            return node.Value;
        }
        int32 Main() {
            return Observe(Create(42), ManagedHeap.Collect());
        }
        """)]
    [TestCase("""
        ManagedNode Create(int32 value) {
            require ManagedNode.TryCreate(value, out var node);
            return node;
        }
        int32 Observe(ManagedNode first, ManagedNode second, int32 collected) {
            if (collected != 0) { return 1; }
            return first.Value + second.Value;
        }
        int32 Main() {
            return Observe(Create(20), Create(22), ManagedHeap.Collect());
        }
        """)]
    [TestCase("""
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
            return node.Value;
        }
        """)]
    [TestCase("""
        int32 CollectNested(int32 depth) {
            if (depth == 0) { return ManagedHeap.Collect(); }
            return CollectNested(depth - 1);
        }
        void Attach(write ManagedNode parent) {
            require ManagedNode.TryCreate(42, out var child);
            parent.SetNext(child);
        }
        void DiscardCycle() {
            require ManagedNode.TryCreate(1, out var first);
            require ManagedNode.TryCreate(2, out var second);
            first.SetNext(second);
            second.SetNext(first);
        }
        int32 Main() {
            require ManagedNode.TryCreate(0, out var parent);
            Attach(parent);
            DiscardCycle();
            if (CollectNested(8) != 2) { return 1; }
            require parent.TryGetNext(out var child);
            return child.Value;
        }
        """)]
    [TestCase("""
        void CheckRetainedNode() {
            require ManagedNode.TryCreate(0, out var list);
            require ManagedNode.TryCreate(41, out var item);
            list.SetNext(item);
            require list.TryGetNext(out var retained);
            list.ClearNext();
            require !list.TryGetNext(out var absent);
            require ManagedHeap.Collect() == 0;
            retained.SetValue(42);
            list.SetNext(retained);
            require list.TryGetNext(out var reinserted);
            require reinserted.Value == 42 && item.Value == 42;
        }
        int32 Main() {
            CheckRetainedNode();
            if (ManagedHeap.Collect() != 2) { return 1; }
            if (ManagedHeap.Collect() != 0) { return 2; }
            return 42;
        }
        """)]
    [TestCase("""
        bool Refuse(out write ManagedNode result) {
            require ManagedNode.TryCreate(42, out var created);
            result = created;
            return false;
        }
        int32 Main() {
            if (Refuse(out var unpublished)) { return 1; }
            if (ManagedHeap.Collect() != 1) { return 2; }
            return 42;
        }
        """)]
    [TestCase("""
        bool CreateAndCollect(out write ManagedNode result) {
            require ManagedNode.TryCreate(42, out result);
            require ManagedHeap.Collect() == 0;
            return true;
        }
        int32 Main() {
            require CreateAndCollect(out var node);
            require ManagedHeap.Collect() == 0;
            return node.Value;
        }
        """)]
    [TestCase("""
        write ManagedNode Create() {
            require ManagedNode.TryCreate(1, out var node);
            return node;
        }
        bool Refuse(out write ManagedNode result) {
            require ManagedNode.TryCreate(2, out result);
            return false;
        }
        int32 Main() {
            mut write ManagedNode current = Create();
            if (Refuse(out current)) { return 1; }
            if (ManagedHeap.Collect() != 2) { return 2; }
            return 42;
        }
        """)]
    [TestCase("""
        void Attach(write ManagedNode parent) {
            require ManagedNode.TryCreate(42, out var child);
            parent.SetNext(child);
        }
        int32 Main() {
            require ManagedNode.TryCreate(0, out var parent);
            Attach(parent);
            require ManagedHeap.Collect() == 0;
            parent.ClearNext();
            if (parent.TryGetNext(out var missing)) { return 1; }
            if (ManagedHeap.Collect() != 1) { return 2; }
            return 42;
        }
        """)]
    [TestCase("""
        bool CreatePair(out write ManagedNode first, out write ManagedNode second) {
            require ManagedNode.TryCreate(20, out first);
            require ManagedHeap.Collect() == 0;
            require ManagedNode.TryCreate(22, out second);
            first.SetNext(second);
            require ManagedHeap.Collect() == 0;
            return true;
        }
        int32 Main() {
            require CreatePair(out var first, out var second);
            require ManagedHeap.Collect() == 0;
            return first.Value + second.Value;
        }
        """)]
    [TestCase("""
        int32 Main() {
            {
                require ManagedNode.TryCreate(1, out var discarded);
                discarded.SetNext(discarded);
            }
            if (ManagedHeap.Collect() != 1) { return 1; }
            return 42;
        }
        """)]
    [TestCase("""
        int32 Main() {
            mut int32 iteration = 0;
            while (iteration < 64) {
                {
                    require ManagedNode.TryCreate(iteration, out var first);
                    require ManagedNode.TryCreate(iteration + 1, out var second);
                    first.SetNext(second);
                    second.SetNext(first);
                    require ManagedHeap.Collect() == 0;
                }
                if (ManagedHeap.Collect() != 2) { return 1; }
                iteration = iteration + 1;
            }
            return 42;
        }
        """)]
    [TestCase("""
        void MakeGarbage() {
            require ManagedNode.TryCreate(0, out var node);
            node.SetNext(node);
        }
        int32 Main() {
            require ManagedNode.TryCreate(20, out var original);
            mut write ManagedNode current = original;
            {
                require ManagedNode.TryCreate(22, out var replacement);
                current = replacement;
            }
            MakeGarbage();
            if (ManagedHeap.Collect() != 1) { return 1; }
            return original.Value + current.Value;
        }
        """)]
    [TestCase("""
        int32 Main() {
            mut int32 count = 0;
            while (count < 4096) {
                require ManagedNode.TryCreate(count, out var node);
                node.SetNext(node);
                count = count + 1;
            }
            if (ManagedNode.TryCreate(0, out var overflow)) { return 1; }
            if (ManagedHeap.Collect() != 4096) { return 2; }
            require ManagedNode.TryCreate(42, out var recycled);
            return recycled.Value;
        }
        """)]
    [TestCase("""
        write ManagedNode Create() {
            require ManagedNode.TryCreate(0, out var node);
            return node;
        }
        int32 Main() {
            for (write ManagedNode node = Create(); node.Value < 1; node.SetValue(node.Value + 1)) {
                require ManagedHeap.Collect() == 0;
            }
            if (ManagedHeap.Collect() != 1) { return 1; }
            return 42;
        }
        """)]
    [TestCase("""
        int32 Main() {
            require ManagedNode.TryCreate(0, out var head);
            mut write ManagedNode tail = head;
            mut int32 count = 1;
            while (count < 4096) {
                require ManagedNode.TryCreate(count, out var next);
                tail.SetNext(next);
                tail = next;
                count = count + 1;
            }
            if (ManagedHeap.Collect() != 0) { return 1; }
            head.SetNext(head);
            if (ManagedHeap.Collect() != 4094) { return 2; }
            if (tail.Value != 4095 || head.Value != 0) { return 3; }
            return 42;
        }
        """)]
    public Task ManagedRootsAndCyclicCollectionAgreeAcrossAotAndJitAsync(string source) =>
        AssertFailFastExecutionAsync(source, 42);
}
