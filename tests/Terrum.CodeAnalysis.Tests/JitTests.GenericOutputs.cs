// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericConditionalSelectionPublishesWholeTokensAndRangesAsync(bool checkedFailures)
    {
        string[] components = [
            Path.Combine(RepositoryRoot(), "self-host", "SourceRange.terrum"),
            Path.Combine(RepositoryRoot(), "self-host", "Token.terrum"),
            Path.Combine(RepositoryRoot(), "self-host", "CopyAlgorithms.terrum"),
        ];
        const string Source = """
            using Terrum;
            using Terrum.CodeAnalysis;
            [NoThrow] bool Choose<T>(int32 index, T first, T second, out T result) where T : [Copy] {
                return TrySelectCopy<T>(index, first, second, out result);
            }
            [NoThrow] bool ChooseInit<T>(int32 index, T first, T second, init ref<T> result) where T : [Copy] {
                return TrySelectCopyInit<T>(index, first, second, init result);
            }
            int32 Main() {
                require SourceRange.TryCreate(12, 5, out var firstRange);
                require SourceRange.TryCreate(42, 7, out var secondRange);
                Token first = new Token(1, firstRange);
                Token second = new Token(2, secondRange);
                if (Choose<Token>(-1, first, second, out var absent)) { return 1; }
                require Choose<Token>(1, first, second, out var chosen);
                require Choose<SourceRange>(0, firstRange, secondRange, out var range);
                if (chosen.Kind != 2 || chosen.Range.Start != 42 || chosen.Range.Length != 7
                    || range.Start != 12 || range.Length != 5) { return 2; }
                if (Choose<Token>(2, first, second, out var missing)) { return 3; }
                require Choose<Token>(0, first, second, out var again);
                require again.Kind == 1 && again.Range.Start == 12;
                if (ChooseInit<Token>(-1, first, second, init var deferred)) { return 4; }
                require ChooseInit<Token>(1, first, second, init deferred);
                require ChooseInit<SourceRange>(0, firstRange, secondRange, init var initRange);
                if (deferred.Kind != 2 || deferred.Range.Start != 42
                    || initRange.Start != 12 || initRange.Length != 5) { return 5; }
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
    public async Task GenericOutputsPreserveMixedStackArgumentsAndWidePayloadsAsync(bool checkedFailures)
    {
        const string Source = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            [NoThrow] bool Forward<T>(T first, T second, bool publish, out T left, out T right,
                out uint64 number, out char16 unit) where T : [Copy] {
                return Split<T>(first, 42, second, publish, out left, out right, out number, out unit);
            }
            [NoThrow] bool Split<T>(T first, int32 marker, T second, bool publish,
                out T left, out T right, out uint64 number, out char16 unit) where T : [Copy] {
                if (marker != 42) { return false; }
                left = first;
                right = second;
                number = 18446744073709551615;
                unit = char16(55296);
                return publish;
            }
            int32 Main() {
                Pair first = new Pair(-9223372036854775808, 9223372036854775807);
                Pair second = new Pair(40, 2);
                if (Forward<Pair>(first, second, false, out var noLeft, out var noRight,
                    out var noNumber, out var noUnit)) { return 1; }
                require Forward<Pair>(first, second, true, out var left, out var right,
                    out var number, out var unit);
                require left.First == -9223372036854775808 && left.Second == 9223372036854775807;
                require number == 18446744073709551615 && unit == char16(55296);
                return int32(right.First + right.Second);
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

    [Fact]
    public async Task GenericScalarResultsParticipateInControlFlowAndNumericContextAsync()
    {
        const string Source = """
            struct Value {
                public int64 Number;
                public Value(int64 number) { Number = number; }
            }
            [NoThrow] bool Enabled<T>(T value, bool enabled) where T : [Copy] { return enabled; }
            [NoThrow] uint64 Number<T>(T value, uint64 number) where T : [Copy] { return number; }
            [NoThrow] int32 Count<T>(T value, int32 count) where T : [Copy] { return count; }
            [NoThrow] char16 Unit<T>(T value, char16 unit) where T : [Copy] { return unit; }
            int32 Main() {
                Value value = new Value(0);
                var count = Count<Value>(value, 40) + 2;
                var number = Number<Value>(value, 18446744073709551615);
                var unit = Unit<Value>(value, char16(55296));
                if (!Enabled<Value>(value, true) || Enabled<Value>(value, false)
                    || number != 18446744073709551615 || unit != char16(55296)) { return 1; }
                return count;
            }
            """;
        await AssertFailFastExecutionAsync(Source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(Source, 42).ConfigureAwait(true);
    }

    [Fact]
    public async Task ConstrainedGenericAccumulationPublishesOnlyOnAcceptedCountsAsync()
    {
        const string Source = """
            interface Addition<A, B, C> { static abstract C operator +(A left, B right); }
            struct Value : Addition<Value, Value, Value> {
                public int64 Number;
                public Value(int64 number) { Number = number; }
                public static Value operator +(Value left, Value right) {
                    return new Value(left.Number + right.Number);
                }
            }
            bool TryAccumulate<T>(T value, T seed, int32 repetitions, out T result)
                where T : Addition<T, T, T>, [Copy] {
                if (repetitions < 0) { return false; }
                mut T sum = seed;
                for (mut int32 i = 0; i < repetitions; i = i + 1) {
                    sum = sum + value;
                }
                result = sum;
                return true;
            }
            int32 Main() {
                Value seed = new Value(40);
                Value step = new Value(1);
                if (TryAccumulate<Value>(step, seed, -1, out var missing)) { return 1; }
                require TryAccumulate<Value>(step, seed, 0, out var unchanged);
                require unchanged.Number == 40;
                require TryAccumulate<Value>(step, seed, 2, out var sum);
                return int32(sum.Number);
            }
            """;
        await AssertFailFastExecutionAsync(Source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(Source, 42).ConfigureAwait(true);
    }

    [Fact]
    public Task GenericOutputFailuresDoNotPreventSubsequentPublicationAsync()
    {
        const string Source = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            bool Try<T>(T value, bool complete, out T result) where T : [Copy] {
                result = value;
                require complete;
                return true;
            }
            int32 Main() {
                mut Pair result = new Pair(1, 2);
                unsafe {
                    try {
                        Try<Pair>(new Pair(-9223372036854775808, 9223372036854775807), false, out result);
                        return 1;
                    } catch {
                        require Try<Pair>(new Pair(40, 2), true, out result);
                        return int32(result.First + result.Second);
                    }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
}
