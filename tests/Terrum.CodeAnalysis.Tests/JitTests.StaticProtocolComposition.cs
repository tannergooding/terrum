// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ConcreteManagedFactoryInCopyTemplatePreservesConditionalOutputsAsync(bool checkedFailures)
    {
        const string Source = """
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            bool TryForward<T>(T value, bool succeed, out T result) where T : [Copy] {
                if (Cell.TryCreate(40, out var cell)) {
                    ManagedHeap.Collect();
                    require cell.Value == 40;
                }
                if (succeed) { result = value; return true; }
                return false;
            }
            int32 Main() {
                require !TryForward(int32(1), false, out var absent);
                require TryForward(int32(42), true, out var answer);
                return answer;
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

    [Test]
    public Task ConcreteManagedFactoryInCopyTemplateRetainsCheckedFailureCleanupAsync()
    {
        const string Source = """
            class Cell {
                public int32 Value;
                public Cell(int32 value) {
                    Value = value;
                    require value != 0;
                }
            }
            int32 CreateOrRecover<T>(T value) where T : [Copy] {
                unsafe {
                    try {
                        if (Cell.TryCreate(0, out var incomplete)) { return 1; }
                        return 2;
                    } catch {
                        require ManagedHeap.Collect() == 0;
                        return 42;
                    }
                }
            }
            int32 Main() { return CreateOrRecover(int32(1)); }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }

    private const string CompositionValue = """
        interface Addition<A, B, C> { static C operator +(A left, B right); }
        struct Value : Addition<Value, Value, Value> {
            public int64 First;
            public int64 Second;
            public Value(int64 first, int64 second) { First = first; Second = second; }
            public static Value operator +(Value left, Value right) {
                return new Value(left.First + right.First, left.Second + right.Second);
            }
        }
        """;
    [TestCase(false)]
    [TestCase(true)]
    public async Task ConcreteCopyStructHelperArgumentsExecuteAcrossIndirectValuesAsync(bool checkedFailures)
    {
        var source = CompositionValue + """
            T Forward<T>(T value) where T : [Copy] {
                var fixedValue = Second<T, Value>(value, new Value(19, 23));
                return Second<Value, T>(fixedValue, value);
            }
            U Second<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            int32 Main() {
                var result = Forward(new Value(40, 2));
                return int32(result.First + result.Second);
            }
            """;
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }
    [TestCase(false,false)]
    [TestCase(false,true)]
    [TestCase(true,false)]
    [TestCase(true,true)]
    public async Task CopyOnlyGenericSelectionPreservesNestedTokenValuesAsync(bool checkedFailures, bool infer)
    {
        string[] components = [
            Path.Combine(RepositoryRoot(), "self-host", "SourceRange.terrum"),
            Path.Combine(RepositoryRoot(), "self-host", "Token.terrum"),
            Path.Combine(RepositoryRoot(), "self-host", "CopyAlgorithms.terrum"),
        ];
        const string Source = """
            using Terrum;
            using Terrum.CodeAnalysis;
            [NoThrow] T Choose<T>(T first, T second) where T : [Copy] {
                return SelectCopy(false, SelectCopy(true, first, second), second);
            }
            int32 Main() {
                require SourceRange.TryCreate(12, 5, init var firstRange);
                require SourceRange.TryCreate(42, 7, init var secondRange);
                mut Token first = new Token(1, firstRange);
                mut Token second = new Token(2, secondRange);
                var chosen = Choose(first, second);
                var range = Choose(firstRange, secondRange);
                first = new Token(3, secondRange);
                second = new Token(4, firstRange);
                if (chosen.Kind != 2 || chosen.Range.Start != 42 || chosen.Range.Length != 7
                    || range.Start != 42 || range.Length != 7) { return 1; }
                return chosen.Range.Start;
            }
            """;
        var source = infer ? Source : Source.Replace("SelectCopy(", "SelectCopy<T>(", StringComparison.Ordinal)
            .Replace("Choose(first, second)", "Choose<Token>(first, second)", StringComparison.Ordinal)
            .Replace("Choose(firstRange, secondRange)", "Choose<SourceRange>(firstRange, secondRange)", StringComparison.Ordinal);
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42, components).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42, components).ConfigureAwait(true);
        }
    }
    [TestCase(false,false)]
    [TestCase(false,true)]
    [TestCase(true,false)]
    [TestCase(true,true)]
    public async Task RecursiveGenericHelpersPreserveIndirectCopyResultsAsync(bool mutual, bool infer)
    {
        var source = CompositionValue + """
            T Repeat<T>(T value, uint32 count) where T : Addition<T, T, T>, [Copy] {
                if (count == 0) { return value; }
                return Repeat(value + value, count - 1);
            }
            int32 Main() {
                var value = Repeat(new Value(2, 3), 3);
                return int32(value.First + value.Second) + 2;
            }
            """;
        if (mutual)
        {
            source = source.Replace("return Repeat(value + value", "return Again(value + value", StringComparison.Ordinal)
                + """
                U Again<U>(U value, uint32 count) where U : Addition<U, U, U>, [Copy] {
                    return Repeat(value, count);
                }
                """;
        }
        if (!infer)
        {
            source = source.Replace("return Repeat(value + value", "return Repeat<T>(value + value", StringComparison.Ordinal)
                .Replace("return Again(value + value", "return Again<T>(value + value", StringComparison.Ordinal)
                .Replace("return Repeat(value, count)", "return Repeat<U>(value, count)", StringComparison.Ordinal)
                .Replace("var value = Repeat(new Value", "var value = Repeat<Value>(new Value", StringComparison.Ordinal);
        }
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task ComposedHelpersPreserveMixedStackArgumentsAndNestedCopyTemporariesAsync(bool infer)
    {
        var source = CompositionValue + """
            T Compose<T>(T value, T extra) where T : Addition<T, T, T>, [Copy] {
                return Combine<T>(value, 10, extra, true, value + extra, 20,
                    Combine<T>(value, 10, extra, true, value, 20, extra, 30), 30);
            }
            U Combine<U>(U first, int32 a, U second, bool b, U third, int64 c, U fourth, uint64 d)
                where U : Addition<U, U, U>, [Copy] {
                require (a == 10 && b && c == 20 && d == 30);
                return first + second + third + fourth;
            }
            int32 Main() {
                var result = Compose(new Value(1, 2), new Value(3, 4));
                if (result.First != 16 || result.Second != 24) { return 1; }
                return 42;
            }
            """;
        if (!infer)
        {
            source = source.Replace("var result = Compose(new Value", "var result = Compose<Value>(new Value",
                StringComparison.Ordinal);
        }
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task InferredArgumentMethodWithMutableReceiverRunsExactlyOnceAsync(bool checkedFailures)
    {
        const string Source = """
            struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
            }
            struct Counter {
                public mut int32 Number;
                public Counter(int32 number) { Number = number; }
                public mut Value Next() {
                    Number = Number + 1;
                    return new Value(Number);
                }
            }
            T Identity<T>(T value) where T : [Copy] { return value; }
            int32 Read<T>(T value, int32 count) where T : [Copy] { return count; }
            int32 Main() {
                mut Counter counter = new Counter(40);
                var result = Identity(Identity(counter.Next()));
                if (counter.Number != 41 || result.Number != 41) { return 1; }
                return Read(result, result.Number) + 1;
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
    [TestCase(false)]
    [TestCase(true)]
    public async Task ScalarCopyHelpersPreserveValuesAndConditionalOutputsAsync(bool checkedFailures)
    {
        const string Source = """
            using Terrum;
            [NoThrow] T Choose<T>(bool first, T left, T right) where T : [Copy] {
                return SelectCopy(first, left, right);
            }
            int32 Main() {
                int64 contextual = Choose(true, 42, 0);
                int64 typed = Choose<int64>(false, int64(0), int64(41));
                int64 direct = SelectCopy<int64>(false, int64(0), int64(42));
                int32 signed = SelectCopy(true, int32(42), int32(0));
                uint64 wide = SelectCopy<uint64>(true, uint64(0xFFFFFFFFFFFFFFFF), uint64(0));
                uint32 narrow = SelectCopy(false, uint32(0), uint32(42));
                char16 codeUnit = SelectCopy<char16>(true, char16(65), char16(66));
                bool selected = SelectCopy(false, true, false);
                mut uint64 destination = uint64(7);
                if (TrySelectCopy(2, wide, uint64(0), out destination)) { return 2; }
                require TrySelectCopy(0, wide, uint64(0), out destination);
                require TrySelectCopy(0, wide, uint64(0), out var output);
                require TrySelectCopy(1, int32(0), signed, out var intOutput);
                require TrySelectCopy(0, true, false, out var boolOutput);
                if (TrySelectCopyInit(2, wide, uint64(0), init var vacant)) { return 3; }
                require TrySelectCopyInit(0, wide, uint64(0), init vacant);
                if (contextual != 42 || typed != 41 || direct != 42 || intOutput != 42
                    || wide != output || destination != wide || narrow != 42
                    || vacant != wide || codeUnit != char16(65) || selected || !boolOutput) { return 1; }
                return int32(contextual);
            }
            """;
        string[] components = [Path.Combine(RepositoryRoot(), "self-host", "CopyAlgorithms.terrum")];
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(Source, 42, components).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(Source, 42, components).ConfigureAwait(true);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task InferredOutputCallsRespectConditionalPublicationAsync(bool checkedFailures)
    {
        const string Source = """
            struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
            }
            [NoThrow] bool None<T>(out T result) where T : [Copy] { return false; }
            [NoThrow] bool TryCopy<T>(T value, bool publish, out T result) where T : [Copy] {
                result = value;
                return publish;
            }
            int32 Main() {
                mut Value target = new Value(7);
                if (None(out target)) { return 1; }
                require TryCopy(new Value(42), true, out target);
                if (target.Number != 42) { return 3; }
                if (TryCopy(new Value(42), false, out target)) { return 2; }
                require TryCopy(new Value(42), true, out var result);
                return result.Number;
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

    [Test]
    public async Task ConstrainedHelperFailureUnwindsToCallerAndAllowsSubsequentCallsAsync()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "static-protocol-power.terrum")).ConfigureAwait(true);
        source = source[..source.IndexOf("int32 Main()", StringComparison.Ordinal)] + """
            int32 Main() {
                mut int32 cleaned = 0;
                unsafe {
                    try {
                        try { Power<Int32Value>(new Int32Value(2), new Int32Value(1), 32); }
                        finally { cleaned = 2; }
                    }
                    catch {
                        return Power<Int32Value>(new Int32Value(2), new Int32Value(1), 5).Value
                            + cleaned + 8;
                    }
                }
                return 1;
            }
            """;
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
}
