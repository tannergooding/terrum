// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CompactCopyClassFieldsRetainCompleteValuesAcrossCallsAsync(bool checkedFailures)
    {
        const string Source = """
            struct Range {
                public mut int32 Start;
                public int32 Length;
                public Range(int32 start, int32 length) { Start = start; Length = length; }
            }
            struct Token {
                public mut int32 Kind;
                public mut Range Span;
                public Token(int32 kind, Range span) { Kind = kind; Span = span; }
            }
            struct Wide {
                public int64 Left;
                public int64 Right;
                public Wide(int64 left, int64 right) { Left = left; Right = right; }
            }
            mut class Box {
                public bool Ready;
                public mut Token Current;
                public mut Wide Numbers;
                public int32 Tag;
                public ManagedNode? Edge;
                public Box(int32 kind, int32 start, int32 length) {
                    Ready = true;
                    require ManagedHeap.Collect() == 0;
                    Current = new Token(kind, new Range(start, length));
                    Numbers = new Wide(40i64, 2i64);
                    Tag = 7;
                    Edge = null;
                    require ManagedNode.TryCreate(7, out var node);
                    Edge = node;
                }
                public Token Read() { return Current; }
            }
            bool Snapshot(Box box, out Token result) {
                result = box.Current;
                return true;
            }
            int32 Main() {
                require Box.TryCreate(3, 1, 2, out var box);
                Token before = box.Read();
                require Snapshot(box, out var output);
                if (!box.Ready || box.Tag != 7) { return 1; }
                box.Current = new Token(7, new Range(8, 9));
                box.Current.Span.Start = 40;
                box.Current.Kind = 42;
                box.Numbers = new Wide(41i64, 1i64);
                if (before.Kind != 3 || before.Span.Start != 1 || before.Span.Length != 2
                    || output.Kind != 3 || output.Span.Length != 2
                    || box.Current.Kind != 42 || box.Current.Span.Start != 40
                    || box.Current.Span.Length != 9) { return 2; }
                if (box.Numbers.Left + box.Numbers.Right != 42i64) { return 3; }
                if (ManagedHeap.Collect() != 0) { return 4; }
                require box.Edge is some node;
                if (node.Value != 7) { return 5; }
                return box.Read().Kind;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClassCopyAssignmentKeepsOriginallySelectedReceiverRootedAsync(bool checkedFailures)
    {
        const string Source = """
            struct Data {
                public mut int32 Kind;
                public int64 Payload;
                public Data(int32 kind) { Kind = kind; Payload = 1234567890123i64; }
            }
            mut class Leaf {
                public mut Data Value;
                public Leaf(int32 kind) { Value = new Data(kind); }
            }
            mut class Holder {
                public mut write Leaf Target;
                public Holder(write Leaf target) { Target = target; }
            }
            write Leaf CreateOld() {
                require Leaf.TryCreate(1, out var old);
                return old;
            }
            Data Replace(write Holder holder, write Leaf replacement, Data next) {
                holder.Target = replacement;
                require ManagedHeap.Collect() == 0;
                return next;
            }
            int32 ReplaceKind(write Holder holder, write Leaf replacement) {
                holder.Target = replacement;
                require ManagedHeap.Collect() == 0;
                return 42;
            }
            int32 Main() {
                {
                    require Leaf.TryCreate(7, out var replacement);
                    require Holder.TryCreate(CreateOld(), out var holder);
                    holder.Target.Value = Replace(holder, replacement, new Data(42));
                    if (holder.Target.Value.Kind != 7) { return 1; }
                    if (ManagedHeap.Collect() != 1) { return 2; }
                }
                if (ManagedHeap.Collect() != 2) { return 3; }
                {
                    require Leaf.TryCreate(1, out var selected);
                    require Leaf.TryCreate(7, out var replacement);
                    require Holder.TryCreate(selected, out var holder);
                    holder.Target.Value = Replace(holder, replacement, new Data(42));
                    if (selected.Value.Kind != 42 || selected.Value.Payload != 1234567890123i64
                        || holder.Target.Value.Kind != 7) { return 4; }
                }
                if (ManagedHeap.Collect() != 3) { return 5; }
                {
                    require Leaf.TryCreate(7, out var replacement);
                    require Holder.TryCreate(CreateOld(), out var holder);
                    holder.Target.Value.Kind = ReplaceKind(holder, replacement);
                    if (holder.Target.Value.Kind != 7 || ManagedHeap.Collect() != 1) { return 6; }
                }
                if (ManagedHeap.Collect() != 2) { return 7; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Fact]
    public Task CheckedCopyAssignmentRetiresSelectedReceiverWhenRhsFailsAsync()
    {
        const string Source = """
            struct Data {
                public int32 Kind;
                public int32 Other;
                public int32 Last;
                public Data(int32 kind) { Kind = kind; Other = 2; Last = 3; }
            }
            mut class Leaf {
                public mut Data Value;
                public Leaf(int32 kind) { Value = new Data(kind); }
            }
            mut class Holder {
                public mut write Leaf Target;
                public Holder(write Leaf target) { Target = target; }
            }
            write Leaf CreateOld() {
                require Leaf.TryCreate(1, out var old);
                return old;
            }
            Data Fail(write Holder holder, write Leaf replacement) {
                holder.Target = replacement;
                require false;
                return new Data(42);
            }
            int32 Main() {
                {
                    require Leaf.TryCreate(7, out var replacement);
                    require Holder.TryCreate(CreateOld(), out var holder);
                    unsafe {
                        try {
                            holder.Target.Value = Fail(holder, replacement);
                            return 1;
                        } catch {
                            if (ManagedHeap.Collect() != 1) { return 2; }
                            if (holder.Target.Value.Kind != 7) { return 3; }
                        }
                    }
                }
                if (ManagedHeap.Collect() != 2) { return 4; }
                {
                    require Leaf.TryCreate(1, out var retained);
                    require Leaf.TryCreate(7, out var replacement);
                    require Holder.TryCreate(retained, out var holder);
                    unsafe {
                        try {
                            holder.Target.Value = Fail(holder, replacement);
                            return 5;
                        } catch {
                            if (retained.Value.Kind != 1 || retained.Value.Other != 2
                                || retained.Value.Last != 3) { return 6; }
                            if (ManagedHeap.Collect() != 0) { return 7; }
                        }
                    }
                }
                if (ManagedHeap.Collect() != 3) { return 8; }
                return 42;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
