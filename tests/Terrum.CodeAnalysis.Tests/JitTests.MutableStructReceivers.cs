// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(1, "bool", "false", "true", "if (Value) { return 42; } return 0;", "")]
    [InlineData(2, "char16", "(char16)0", "(char16)42", "return (int32)Value;", "")]
    [InlineData(3, "bool", "false", "true", "if (Value) { return 42; } return 0;",
        "public bool Second; public bool Third;")]
    [InlineData(4, "int32", "0", "42", "return Value;", "")]
    [InlineData(6, "char16", "(char16)0", "(char16)42", "return (int32)Value;",
        "public char16 Second; public char16 Third;")]
    [InlineData(8, "int64", "0i64", "42i64", "return (int32)Value;", "")]
    [InlineData(12, "int32", "0", "42", "return Value;",
        "public int32 Second; public int32 Third;")]
    [InlineData(16, "int64", "0i64", "42i64", "return (int32)Value;", "public int64 Second;")]
    public async Task MutableReceiverPreservesEveryDirectAndIndirectCopyWidthAsync(
        int size, string type, string initial, string updated, string read, string additionalFields)
    {
        System.ArgumentNullException.ThrowIfNull(additionalFields);
        var initializers = additionalFields.Contains("Third", System.StringComparison.Ordinal)
            ? $"Second = {initial}; Third = {initial};"
            : additionalFields.Length != 0 ? $"Second = {initial};" : "";
        var source = $$"""
            struct Packet {
                public mut {{type}} Value;
                {{additionalFields}}
                public Packet() { Value = {{initial}}; {{initializers}} }
                public mut void Set() { Value = {{updated}}; }
                public int32 Read() { {{read}} }
            }
            int32 Main() {
                mut Packet value = new Packet();
                value.Set();
                return value.Read();
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(System.Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(size, Assert.IsType<BoundProgram>(compilation.Program).Structures[0].Size);
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MutableStructReceiverWritesOriginalAcrossNestedCallsAsync(bool checkedFailures)
    {
        const string Source = """
            struct Counter {
                public mut int32 Value;
                public Counter(int32 value) { Value = value; }
                public mut void Advance(int32 increment) {
                    Value = Value + increment;
                }
                public mut void Twice(int32 increment) {
                    Advance(increment);
                    this.Advance(increment);
                }
                public int32 Read() { return Value; }
            }
            int32 Main() {
                mut Counter original = new Counter(20);
                Counter snapshot = original;
                original.Twice(11);
                return original.Read() - snapshot.Read() + 20;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MutableIndirectStructReceiverWritesOriginalAcrossCallsAsync(bool checkedFailures)
    {
        const string Source = """
            struct Payload {
                public mut int64 First;
                public int64 Second;
                public Payload(int64 first) { First = first; Second = 99; }
                public mut void Set(int64 value) { First = value; }
                public int32 Read() { return (int32)First; }
            }
            int32 Main() {
                mut Payload value = new Payload(1i64);
                value.Set(42i64);
                return value.Read();
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MutableReceiverWritesNestedFieldWithoutOverwritingAdjacentBytesAsync(bool checkedFailures)
    {
        const string Source = """
            struct Inner {
                public mut int32 Value;
                public Inner(int32 value) { Value = value; }
            }
            struct Outer {
                public int32 Header;
                public mut Inner Nested;
                public int32 Trailer;
                public Outer() {
                    Header = 7;
                    Nested = new Inner(1);
                    Trailer = 9;
                }
                public mut void Update(int32 value) {
                    Nested.Value = value;
                }
                public int32 Read() { return Nested.Value + Header - Trailer; }
            }
            int32 Main() {
                mut Outer value = new Outer();
                value.Update(44);
                return value.Read();
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Fact]
    public Task CheckedFailureRetainsWritesThroughBorrowedReceiverAsync()
    {
        const string Source = """
            struct Counter {
                public mut int32 Value;
                public Counter(int32 value) { Value = value; }
                public mut void Fail() {
                    Value = 42;
                    require false;
                }
            }
            int32 Main() {
                mut Counter value = new Counter(1);
                unsafe {
                    try { value.Fail(); }
                    catch { return value.Value; }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
