// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task FullClassHeapTracesLongOptionalCycleAndReusesEverySlotAsync(bool checkedFailures)
    {
        const string Source = """
            mut class Link {
                public int32 Value;
                public mut write Link? Next;
                public Link(int32 value) {
                    Value = value;
                    Next = null;
                }
            }
            int32 Main() {
                for (mut int32 round = 0; round < 2; round = round + 1) {
                    {
                        mut write Link? head = null;
                        mut write Link? oldest = null;
                        for (mut int32 i = 0; i < 4096; i = i + 1) {
                            require Link.TryCreate(i, out var node);
                            node.Next = head;
                            head = node;
                            if (i == 0) { oldest = node; }
                        }
                        if (Link.TryCreate(42, out var unavailable)) { return 1; }
                        if (ManagedHeap.Collect() != 0) { return 2; }
                        {
                            require head is some first;
                            require oldest is some last;
                            last.Next = first;
                            if (ManagedHeap.Collect() != 0) { return 3; }
                            if (first.Value != 4095 || last.Value != 0) { return 4; }
                        }
                    }
                    if (ManagedHeap.Collect() != 4096) { return 5; }
                }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task MixedClassDescriptorTracksAliasesWithoutScanningScalarFieldsAsync(bool checkedFailures)
    {
        const string Source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            mut class Mixed {
                public mut Leaf? First;
                public uint64 Impostor;
                public mut Leaf? Second;
                public mut ManagedNode? Native;
                public mut Leaf? Third;
                public Mixed(Leaf first, Leaf second, ManagedNode node) {
                    First = first;
                    Impostor = uint64.MaxValue;
                    Second = first;
                    Native = node;
                    Third = second;
                }
            }
            write Mixed Create() {
                require Leaf.TryCreate(40, out var first);
                require Leaf.TryCreate(2, out var second);
                require ManagedNode.TryCreate(7, out var node);
                require Mixed.TryCreate(first, second, node, out var mixed);
                return mixed;
            }
            int32 Main() {
                {
                    var graph = Create();
                    if (graph.Impostor != uint64.MaxValue
                        || ManagedHeap.Collect() != 0) { return 1; }
                    {
                        require graph.Second is some secondLeaf;
                        require graph.Third is some tail;
                        require graph.Native is some node;
                        if (secondLeaf.Value + tail.Value != 42 || node.Value != 7) { return 2; }
                    }
                    graph.First = null;
                    if (ManagedHeap.Collect() != 0) { return 3; }
                    graph.Second = null;
                    if (ManagedHeap.Collect() != 1) { return 4; }
                    graph.Third = null;
                    if (ManagedHeap.Collect() != 1) { return 5; }
                    graph.Native = null;
                    if (ManagedHeap.Collect() != 1) { return 6; }
                    if (!(graph.First is null) || !(graph.Second is null)
                        || !(graph.Third is null) || !(graph.Native is null)
                        || graph.Impostor != uint64.MaxValue) { return 7; }
                }
                if (ManagedHeap.Collect() != 1) { return 8; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
}
