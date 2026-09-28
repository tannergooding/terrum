// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DiscardEvaluatesValueAndEffectsOnceAcrossAotAndJitAsync(bool checkedFailures)
    {
        const string Source = """
            struct Counter {
                public mut int32 Calls;
                public Counter() { Calls = 0; }
                public mut int32 Read() { Calls = Calls + 1; return 40; }
            }
            int32 Main() {
                mut Counter counter = new Counter();
                _ = counter.Read() + int32(2);
                _ = int32(7);
                if (counter.Calls != 1) { return 1; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DiscardedVoidCallKeepsExistingCallSemanticsAsync(bool checkedFailures)
    {
        const string Source = """
            struct Counter {
                public mut int32 Calls;
                public Counter() { Calls = 0; }
                public mut void Increment() { Calls = Calls + 1; }
            }
            int32 Main() {
                mut Counter counter = new Counter();
                _ = counter.Increment();
                return counter.Calls + 41;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DiscardedOwningResultUsesExistingCleanupAsync(bool checkedFailures)
    {
        const string Source = """
            unique struct Owner {
                public OwnedInt32Buffer Buffer;
                public Owner() {
                    require OwnedInt32Buffer.TryCreate(1, out var created);
                    Buffer = move created;
                }
            }
            int32 Main() {
                _ = new Owner();
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DiscardedManagedResultRetiresItsRootBeforeNextStatementAsync(bool checkedFailures)
    {
        const string Source = """
            ManagedNode Create() {
                require ManagedNode.TryCreate(42, out var node);
                return node;
            }
            int32 Main() {
                _ = Create();
                if (ManagedHeap.Collect() != 1) { return 1; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData("int32 Main() { _ = int32(1) / int32(0); return 42; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { _ = Fail(); return 42; } int32 Fail() { require false; return 0; }",
        unchecked((int)0xE0540003))]
    public Task DiscardPreservesCheckedFailureOrderAsync(string source, int expected) =>
        AssertFailFastExecutionAsync(source, expected);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task DiscardedMutableViewSetMutatesOnceAcrossAotAndJitAsync(bool checkedFailures, bool parenthesized)
    {
        var action = parenthesized
            ? "((window.Set(Observe(other.TryAppend(1), other.Count), Observe(other.TryAppend(2), other.Count) + 38)))"
            : "window.Set(Observe(other.TryAppend(1), other.Count), Observe(other.TryAppend(2), other.Count) + 38)";
        var source = $$"""
            int32 Observe(bool appended, int32 count) { return count; }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var created);
                mut OwnedInt32Buffer buffer = move created;
                require buffer.TryAppend(40);
                require buffer.TryAppend(1);
                require OwnedInt32Buffer.TryCreate(2, out var extra);
                mut OwnedInt32Buffer other = move extra;
                {
                    mut MutableInt32BufferView window = buffer.SliceMutable(0, 2);
                    _ = {{action}};
                    if (other.Count != 2 || window.Read(1) != 40) { return 0; }
                }
                return buffer.Read(1) + other.Count;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(source, 42)
            : AssertFailFastExecutionAsync(source, 42);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1")]
    public Task DiscardedMutableViewSetRejectsInvalidIndexAcrossAotAndJitAsync(string index)
    {
        var source = $$"""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var created);
                mut OwnedInt32Buffer buffer = move created;
                require buffer.TryAppend(42);
                mut MutableInt32BufferView window = buffer.SliceMutable(0, 1);
                _ = window.Set({{index}}, 1);
                return 0;
            }
            """;
        return AssertFailFastExecutionAsync(source, unchecked((int)0xE0540003));
    }

    [Fact]
    public Task DiscardedMutableViewSetFailureIsCatchableAcrossAotAndJitAsync()
    {
        const string Source = """
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var created);
                mut OwnedInt32Buffer buffer = move created;
                require buffer.TryAppend(42);
                unsafe {
                    try {
                        mut MutableInt32BufferView window = buffer.SliceMutable(0, 1);
                        _ = window.Set(1, 0);
                    }
                    catch {
                        return buffer.Read(0);
                    }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }

    [Fact]
    public Task DiscardedCheckedDivisionFailureIsCatchableAcrossAotAndJitAsync() =>
        AssertCheckedExecutionAsync("""
            int32 Main() {
                unsafe {
                    try { _ = int32(42) / int32(0); }
                    catch { return 42; }
                }
                return 0;
            }
            """, 42);
}
