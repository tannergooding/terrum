// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    private const string CompositionValue = """
        interface Addition<A, B, C> { static abstract C operator +(A left, B right); }
        struct Value : Addition<Value, Value, Value> {
            public int64 First;
            public int64 Second;
            public Value(int64 first, int64 second) { First = first; Second = second; }
            public static Value operator +(Value left, Value right) {
                return new Value(left.First + right.First, left.Second + right.Second);
            }
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyOnlyGenericSelectionPreservesNestedTokenValuesAsync(bool checkedFailures)
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
                return SelectCopy<T>(false, SelectCopy<T>(true, first, second), second);
            }
            int32 Main() {
                require SourceRange.TryCreate(12, 5, out var firstRange);
                require SourceRange.TryCreate(42, 7, out var secondRange);
                mut Token first = new Token(1, firstRange);
                mut Token second = new Token(2, secondRange);
                var chosen = Choose<Token>(first, second);
                var range = Choose<SourceRange>(firstRange, secondRange);
                first = new Token(3, secondRange);
                second = new Token(4, firstRange);
                if (chosen.Kind != 2 || chosen.Range.Start != 42 || chosen.Range.Length != 7
                    || range.Start != 42 || range.Length != 7) { return 1; }
                return chosen.Range.Start;
            }
            """;
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(Source, 42, components).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(Source, 42, components).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecursiveGenericHelpersPreserveIndirectCopyResultsAsync(bool mutual)
    {
        var source = CompositionValue + """
            T Repeat<T>(T value, uint32 count) where T : Addition<T, T, T>, [Copy] {
                if (count == 0) { return value; }
                return Repeat<T>(value + value, count - 1);
            }
            int32 Main() {
                var value = Repeat<Value>(new Value(2, 3), 3);
                return int32(value.First + value.Second) + 2;
            }
            """;
        if (mutual)
        {
            source = source.Replace("return Repeat<T>", "return Again<T>", StringComparison.Ordinal)
                + """
                U Again<U>(U value, uint32 count) where U : Addition<U, U, U>, [Copy] {
                    return Repeat<U>(value, count);
                }
                """;
        }
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task ComposedHelpersPreserveMixedStackArgumentsAndNestedCopyTemporariesAsync()
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
                var result = Compose<Value>(new Value(1, 2), new Value(3, 4));
                if (result.First != 16 || result.Second != 24) { return 1; }
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Fact]
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
