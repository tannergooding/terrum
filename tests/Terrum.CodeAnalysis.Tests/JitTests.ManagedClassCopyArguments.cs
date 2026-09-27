// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData("bool", "true", "false")]
    [InlineData("char16", "(char16)55296", "(char16)0")]
    [InlineData("int32", "-2147483648", "0")]
    [InlineData("int64", "-9223372036854775808i64", "0i64")]
    public async Task DirectCopyFactoryArgumentsSurviveCollectingConstructorsAsync(
        string type, string value, string replacement)
    {
        var source = $$"""
            struct Payload {
                public {{type}} Value;
                public Payload({{type}} value) { Value = value; }
            }
            class Box {
                public Payload Data;
                public uint64 Stamp;
                public ManagedNode Child;
                public Box(Payload data, uint64 stamp, ManagedNode child) {
                    require ManagedHeap.Collect() == 0;
                    Data = data;
                    Stamp = stamp;
                    Child = child;
                }
            }
            ManagedNode CreateChild() {
                require ManagedNode.TryCreate(42, out var child);
                return child;
            }
            int32 Main() {
                {
                    mut Payload payload = new Payload({{value}});
                    require Box.TryCreate(payload, 18446744073709551615u64, CreateChild(), out var box);
                    payload = new Payload({{replacement}});
                    if (box.Data.Value != {{value}} || box.Stamp != 18446744073709551615u64
                        || box.Child.Value != 42 || payload.Value != {{replacement}}) { return 1; }
                    require ManagedHeap.Collect() == 0;
                }
                require ManagedHeap.Collect() == 2;
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(16)]
    public async Task IndirectCopyFactoryArgumentsPreserveEveryByteAndArgumentPositionAsync(int size)
    {
        var declaration = size switch {
            3 => "public bool First; public bool Second; public bool Third;",
            6 => "public char16 First; public char16 Second; public char16 Third;",
            12 => "public int32 First; public int32 Second; public int32 Third;",
            _ => "public int64 First; public int32 Second; public int32 Third;",
        };
        var firstType = size switch { 3 => "bool", 6 => "char16", 12 => "int32", _ => "int64" };
        var first = size switch { 3 => "true", 6 => "(char16)65535", 12 => "-2147483648", _ => "-9223372036854775808i64" };
        var second = size switch { 3 => "false", 6 => "(char16)55296", _ => "2147483647" };
        var third = size switch { 3 => "true", 6 => "(char16)42", _ => "-123456789" };
        var source = $$"""
            struct Payload {
                {{declaration}}
                public Payload({{firstType}} first) {
                    First = first;
                    Second = {{second}};
                    Third = {{third}};
                }
            }
            class Box {
                public Payload First;
                public Payload Second;
                public Payload Third;
                public Box(Payload first, Payload second, Payload third) {
                    require ManagedHeap.Collect() == 0;
                    First = first;
                    Second = second;
                    Third = third;
                }
            }
            Payload Create() { return new Payload({{first}}); }
            int32 Main() {
                {
                    require Box.TryCreate(Create(), Create(), Create(), out var box);
                    if (box.First.First != {{first}} || box.Second.First != {{first}}
                        || box.Third.First != {{first}} || box.First.Second != {{second}}
                        || box.Second.Second != {{second}} || box.Third.Second != {{second}}
                        || box.First.Third != {{third}} || box.Second.Third != {{third}}
                        || box.Third.Third != {{third}}) { return 1; }
                }
                require ManagedHeap.Collect() == 1;
                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(size, Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Structures).Size);
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task CopyFactoryArgumentsAreSnapshotsBeforeLaterArgumentEffectsAsync()
    {
        const string Source = """
            struct Data {
                public int32 First;
                public int32 Second;
                public int32 Third;
                public Data(int32 first) { First = first; Second = 20; Third = 22; }
            }
            mut class Source {
                public mut Data Value;
                public Source(Data value) { Value = value; }
            }
            class Box {
                public Data Value;
                public ManagedNode Child;
                public Box(Data value, ManagedNode child) {
                    Value = value;
                    require ManagedHeap.Collect() == 0;
                    Child = child;
                }
            }
            ManagedNode Replace(write Source source) {
                source.Value = new Data(7);
                require ManagedNode.TryCreate(2, out var child);
                require ManagedHeap.Collect() == 0;
                return child;
            }
            int32 Main() {
                {
                    require Source.TryCreate(new Data(40), out var source);
                    require Box.TryCreate(source.Value, Replace(source), out var box);
                    require source.Value.First == 7;
                    require box.Value.First == 40 && box.Value.Second == 20 && box.Value.Third == 22;
                    require box.Child.Value == 2;
                }
                require ManagedHeap.Collect() == 3;
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(Source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(Source, 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task CopyFactoryConstructorLocalMutationDoesNotChangeTheCallerAsync()
    {
        const string Source = """
            struct Data {
                public mut int64 First;
                public mut int64 Second;
                public Data(int64 first, int64 second) { First = first; Second = second; }
            }
            class Box {
                public Data Value;
                public Box(Data input) {
                    mut Data local = input;
                    local.First = 40i64;
                    local.Second = 2i64;
                    require ManagedHeap.Collect() == 0;
                    Value = local;
                }
            }
            int32 Main() {
                mut Data original = new Data(-9223372036854775808i64, 9223372036854775807i64);
                require Box.TryCreate(original, out var box);
                require original.First == -9223372036854775808i64 && original.Second == 9223372036854775807i64;
                original.First = 7i64;
                require box.Value.First == 40i64 && box.Value.Second == 2i64;
                return (int32)(box.Value.First + box.Value.Second);
            }
            """;
        await AssertFailFastExecutionAsync(Source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(Source, 42).ConfigureAwait(true);
    }

    [Fact]
    public Task CheckedCopyFactoryFailureRetiresUnpublishedStorageAndArgumentsAsync()
    {
        const string Source = """
            struct Data {
                public int64 First;
                public int64 Second;
                public Data(int64 first) { First = first; Second = 42i64; }
            }
            class Box {
                public Data Value;
                public ManagedNode Child;
                public Box(Data value, ManagedNode child, bool complete) {
                    Value = value;
                    require ManagedHeap.Collect() == 0;
                    Child = child;
                    require complete;
                }
            }
            ManagedNode CreateChild() {
                require ManagedNode.TryCreate(42, out var child);
                return child;
            }
            int32 Main() {
                unsafe {
                    try {
                        Box.TryCreate(new Data(-9223372036854775808i64), CreateChild(), false, out var absent);
                        return 1;
                    } catch {
                        require ManagedHeap.Collect() == 1;
                    }
                }
                {
                    require Box.TryCreate(new Data(40i64), CreateChild(), true, out var box);
                    require box.Value.First == 40i64 && box.Value.Second == 42i64 && box.Child.Value == 42;
                }
                require ManagedHeap.Collect() == 2;
                return 42;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
