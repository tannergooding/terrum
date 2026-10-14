// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Test]
    public async Task UniqueOwnerSampleAgreesBetweenAotAndJitAsync()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "unique-owner.terrum")).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task NestedStructReceiverSampleAgreesBetweenAotAndJitAsync(bool checkedFailures)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "nested-struct-receiver.terrum")).ConfigureAwait(true);
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task UniqueMutableReceiverUpdatesNamedOwnerAndForwardedThisAsync(bool checkedFailures)
    {
        const string Source = """
            struct Payload {
                public mut int32 Value;
                public Payload(int32 value) { Value = value; }
            }
            unique struct Owner {
                public mut int32 Value;
                public mut Payload Data;
                public Owner(int32 value) { Value = value; Data = new Payload(value); }
                public mut void Advance(int32 amount) {
                    Value = Value + amount;
                    Data.Value = Data.Value + amount;
                }
                public mut void Forward(int32 amount) {
                    Advance(amount);
                    this.Advance(amount);
                }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                mut Owner owner = new Owner(20);
                owner.Forward(11);
                if (owner.Data.Value != 42) { return 1; }
                return owner.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Test]
    public Task CheckedFailureRetainsUniqueOwnerMutationAsync()
    {
        const string Source = """
            unique struct Owner {
                public mut int32 Value;
                public Owner(int32 value) { Value = value; }
                public mut void Fail() {
                    Value = 42;
                    require false;
                }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                mut Owner owner = new Owner(1);
                unsafe {
                    try { owner.Fail(); }
                    catch { return owner.Value; }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task NestedMutableReceiverThroughUniqueOwnerUpdatesOriginalAsync(bool checkedFailures)
    {
        const string Source = """
            struct Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
                public mut void Add(int32 amount) { Value = Value + amount; }
            }
            struct Middle {
                public mut Leaf Nested;
                public Middle(int32 value) { Nested = new Leaf(value); }
            }
            unique struct Owner {
                public mut Middle Data;
                public Owner(int32 value) { Data = new Middle(value); }
                public mut void Forward(int32 amount) { Data.Nested.Add(amount); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                mut Owner owner = new Owner(20);
                owner.Data.Nested.Add(11);
                owner.Forward(11);
                return owner.Data.Nested.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task NestedUniqueChildReceiverUpdatesOriginalAsync(bool checkedFailures)
    {
        const string Source = """
            struct Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
                public mut void Add(int32 amount) { Value = Value + amount; }
            }
            unique struct Child {
                public mut Leaf Data;
                public Child(int32 value) { Data = new Leaf(value); }
                public mut void Forward(int32 amount) { Data.Add(amount); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            unique struct Owner {
                public mut Child Nested;
                public Owner(int32 value) { Nested = new Child(value); }
                public mut void Forward(int32 amount) { Nested.Forward(amount); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                mut Owner owner = new Owner(20);
                owner.Nested.Data.Add(11);
                owner.Forward(11);
                return owner.Nested.Data.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task NestedMutableLoanPermitsReadonlyArgumentQueryAsync(bool checkedFailures)
    {
        const string Source = """
            unique struct Child {
                public mut int32 Value;
                public Child(int32 value) { Value = value; }
                [NoThrow] public int32 Read() { return Value; }
                public mut void Add(int32 value) { Value = Value + value; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            unique struct Owner {
                public mut Child Nested;
                public Owner(int32 value) { Nested = new Child(value); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                mut Owner owner = new Owner(21);
                owner.Nested.Add(owner.Nested.Read());
                return owner.Nested.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(1,"bool","false","true","if (Value) { return 42; } return 0;","")]
    [TestCase(2,"char16","char16(0)","char16(42)","return int32(Value);","")]
    [TestCase(3,"bool","false","true","if (Value) { return 42; } return 0;",        "public bool Second; public bool Third;")]
    [TestCase(4,"int32","0","42","return Value;","")]
    [TestCase(6,"char16","char16(0)","char16(42)","return int32(Value);",        "public char16 Second; public char16 Third;")]
    [TestCase(8,"int64","0","42","return int32(Value);","")]
    [TestCase(12,"int32","0","42","return Value;",        "public int32 Second; public int32 Third;")]
    [TestCase(16,"int64","0","42","return int32(Value);","public int64 Second;")]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(System.Environment.NewLine, compilation.Diagnostics));
        Assert.That(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))().Structures[0].Size, Is.EqualTo(size));
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
    [TestCase(false)]
    [TestCase(true)]
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
    [TestCase(false)]
    [TestCase(true)]
    public Task MutableIndirectStructReceiverWritesOriginalAcrossCallsAsync(bool checkedFailures)
    {
        const string Source = """
            struct Payload {
                public mut int64 First;
                public int64 Second;
                public Payload(int64 first) { First = first; Second = 99; }
                public mut void Set(int64 value) { First = value; }
                public int32 Read() { return int32(First); }
            }
            int32 Main() {
                mut Payload value = new Payload(1);
                value.Set(42);
                return value.Read();
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
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

    [Test]
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
    [TestCase(false)]
    [TestCase(true)]
    public Task NestedMutableReceiverWritesOriginalAcrossForwardingAsync(bool checkedFailures)
    {
        const string Source = """
            struct Leaf {
                public mut int64 Value;
                public Leaf(int64 value) { Value = value; }
                public mut void Increment(int64 amount) { Value = Value + amount; }
            }
            struct Middle {
                public mut Leaf Child;
                public Middle(int64 value) { Child = new Leaf(value); }
            }
            struct Outer {
                public mut Middle Nested;
                public Outer(int64 value) { Nested = new Middle(value); }
                public mut void Forward(int64 amount) { Nested.Child.Increment(amount); }
            }
            int32 Main() {
                mut Outer owner = new Outer(20);
                Outer snapshot = owner;
                owner.Nested.Child.Increment(11);
                owner.Forward(11);
                if (snapshot.Nested.Child.Value != 20) { return 1; }
                return int32(owner.Nested.Child.Value);
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Test]
    public Task CheckedFailureRetainsWritesThroughNestedMutableReceiverAsync()
    {
        const string Source = """
            struct Inner {
                public mut int32 Value;
                public Inner(int32 value) { Value = value; }
                public mut void Fail() { Value = 42; require false; }
            }
            struct Outer {
                public mut Inner Nested;
                public Outer(int32 value) { Nested = new Inner(value); }
            }
            int32 Main() {
                mut Outer owner = new Outer(1);
                unsafe {
                    try { owner.Nested.Fail(); }
                    catch { return owner.Nested.Value; }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task NestedReceiverAllowsReadonlyArgumentFromReservedRootAsync(bool checkedFailures)
    {
        const string Source = """
            struct Inner {
                public mut int32 Value;
                public Inner(int32 value) { Value = value; }
                public mut void Add(int32 amount) { Value = Value + amount; }
            }
            struct Middle {
                public int32 Header;
                public mut Inner Child;
                public int32 Trailer;
                public Middle(int32 value) {
                    Header = 7;
                    Child = new Inner(value);
                    Trailer = 9;
                }
            }
            struct Outer {
                public int32 Prefix;
                public mut Middle Nested;
                public Outer(int32 value) {
                    Prefix = 11;
                    Nested = new Middle(value);
                }
            }
            int32 Main() {
                mut Outer owner = new Outer(21);
                owner.Nested.Child.Add(owner.Nested.Child.Value);
                if (owner.Prefix != 11 || owner.Nested.Header != 7
                    || owner.Nested.Trailer != 9) { return 1; }
                return owner.Nested.Child.Value;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
}
