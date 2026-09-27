// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(63)]
    public async Task ClassStackArgumentsPreserveScalarPositionsThroughTheOrdinaryLimitAsync(int count)
    {
        var parameters = string.Join(", ", Enumerable.Range(1, count).Select(
            index => "int32 a" + index.ToString(CultureInfo.InvariantCulture)));
        var arguments = string.Join(", ", Enumerable.Range(1, count).Select(
            index => index.ToString(CultureInfo.InvariantCulture)));
        var sum = string.Join(" + ", Enumerable.Range(1, count).Select(
            index => "a" + index.ToString(CultureInfo.InvariantCulture) + " * "
                + index.ToString(CultureInfo.InvariantCulture)));
        var expected = (count * (count + 1) * ((2 * count) + 1) / 6).ToString(CultureInfo.InvariantCulture);
        var source = $$"""
            class Box {
                public int32 Value;
                public Box({{parameters}}) {
                    require ManagedHeap.Collect() == 0;
                    Value = {{sum}};
                }
            }
            int32 Main() {
                {
                    require Box.TryCreate({{arguments}}, out var box);
                    require box.Value == {{expected}};
                }
                require ManagedHeap.Collect() == 1;
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(63)]
    public async Task ClassStackArgumentsPreserveIndependentIndirectCopyPayloadsAsync(int count)
    {
        var parameters = string.Join(", ", Enumerable.Range(1, count).Select(
            index => "Payload a" + index.ToString(CultureInfo.InvariantCulture)));
        var arguments = string.Join(", ", Enumerable.Range(1, count).Select(
            index => "new Payload(" + index.ToString(CultureInfo.InvariantCulture) + "i64)"));
        var checks = string.Join(" ", Enumerable.Range(1, count).Select(index =>
            "require a" + index.ToString(CultureInfo.InvariantCulture) + ".First == "
                + index.ToString(CultureInfo.InvariantCulture) + "i64 && a"
                + index.ToString(CultureInfo.InvariantCulture) + ".Second == -9223372036854775808i64;"));
        var source = $$"""
            struct Payload {
                public int64 First;
                public int64 Second;
                public Payload(int64 first) { First = first; Second = -9223372036854775808i64; }
            }
            class Box {
                public Payload Value;
                public Box({{parameters}}) {
                    require ManagedHeap.Collect() == 0;
                    {{checks}}
                    Value = a{{count.ToString(CultureInfo.InvariantCulture)}};
                }
            }
            int32 Main() {
                {
                    require Box.TryCreate({{arguments}}, out var box);
                    require box.Value.First == {{count.ToString(CultureInfo.InvariantCulture)}}i64;
                    require box.Value.Second == -9223372036854775808i64;
                }
                require ManagedHeap.Collect() == 1;
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task ClassStackArgumentsKeepSparseHighArityRootsThroughCollectionAndUnwindAsync()
    {
        static bool IsHandle(int index)
        {
            return index is 1 or 32 or 63;
        }

        var parameters = string.Join(", ", Enumerable.Range(1, 63).Select(
            index => (IsHandle(index) ? "ManagedNode a" : "int64 a")
                + index.ToString(CultureInfo.InvariantCulture)));
        var arguments = string.Join(", ", Enumerable.Range(1, 63).Select(index =>
            IsHandle(index) ? "Create(" + index.ToString(CultureInfo.InvariantCulture) + ")"
                : index.ToString(CultureInfo.InvariantCulture) + "i64"));
        var source = $$"""
            class Box {
                public ManagedNode First;
                public ManagedNode Middle;
                public ManagedNode Last;
                public Box({{parameters}}) {
                    require ManagedHeap.Collect() == 0;
                    First = a1;
                    Middle = a32;
                    Last = a63;
                    require a2 == 2i64;
                }
            }
            ManagedNode Create(int32 value) {
                require ManagedNode.TryCreate(value, out var node);
                require ManagedHeap.Collect() == 0;
                return node;
            }
            int32 Main() {
                {
                    require Box.TryCreate({{arguments}}, out var box);
                    require box.First.Value == 1 && box.Middle.Value == 32 && box.Last.Value == 63;
                }
                require ManagedHeap.Collect() == 4;
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        source = source.Replace("require a2 == 2i64;", "require false;", StringComparison.Ordinal);
        var entry = source.IndexOf("int32 Main()", StringComparison.Ordinal);
        source = source[..entry] + $$"""
            int32 Main() {
                unsafe {
                    try {
                        Box.TryCreate({{arguments}}, out var absent);
                        return 1;
                    } catch {
                        require ManagedHeap.Collect() == 3;
                    }
                }
                return 42;
            }
            """;
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Fact]
    public void ClassStackArgumentsReserveOneOrdinaryArgumentForReceiverAndOutput()
    {
        var parameters = string.Join(", ", Enumerable.Range(0, 64).Select(
            index => "int32 a" + index.ToString(CultureInfo.InvariantCulture)));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "class Box { public int32 Value; public Box(" + parameters
            + ") { Value = 42; } } int32 Main() { return 0; }"));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassStackArgumentsKeepManagedHandlesRootedAcrossCollectionAsync(bool checkedFailures)
    {
        const string Source = """
            struct Payload {
                public int64 First;
                public int64 Second;
                public Payload(int64 first) { First = first; Second = -9223372036854775808i64; }
            }
            class Box {
                public Payload First;
                public Payload Last;
                public ManagedNode Left;
                public ManagedNode Right;
                public uint64 Bits;
                public char16 Unit;
                public bool Ready;
                public Box(Payload first, uint64 bits, bool ready, char16 unit,
                    ManagedNode left, ManagedNode right, Payload last) {
                    require ManagedHeap.Collect() == 0;
                    First = first;
                    Bits = bits;
                    Ready = ready;
                    Unit = unit;
                    Left = left;
                    Right = right;
                    Last = last;
                }
            }
            ManagedNode Create(int32 value) {
                require ManagedNode.TryCreate(value, out var node);
                return node;
            }
            Payload Last() {
                require ManagedHeap.Collect() == 0;
                return new Payload(7i64);
            }
            int32 Main() {
                {
                    require Box.TryCreate(new Payload(1i64), 18446744073709551615u64, true, (char16)55296,
                        Create(20), Create(22), Last(), out var box);
                    require box.First.First == 1i64 && box.Last.First == 7i64
                        && box.First.Second == -9223372036854775808i64 && box.Last.Second == -9223372036854775808i64;
                    require box.Ready && box.Bits == 18446744073709551615u64 && box.Unit == (char16)55296;
                    require box.Left.Value + box.Right.Value == 42;
                }
                require ManagedHeap.Collect() == 3;
                return 42;
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
}
