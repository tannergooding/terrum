// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task GenericNonBooleanAndVoidOutputsPublishCopyValuesAsync(bool checkedFailures)
    {
        const string Source = """
            struct Counter {
                public int64 Number;
                public Counter(int64 number) { Number = number; }
            }
            [NoThrow] int32 Copy<T>(T value, T duplicate, out T result,
                out T secondResult) where T : [Copy] {
                result = value;
                secondResult = duplicate;
                return 7;
            }
            [NoThrow] void CopyVoid<T>(T value, out T result) where T : [Copy] {
                result = value;
            }
            int32 Main() {
                Counter counter = new Counter(40);
                int32 status = Copy(counter, new Counter(41), out var first, out var second);
                CopyVoid<Counter>(new Counter(2), out var voidOutput);
                if (status != 7 || first.Number != 40 || second.Number != 41
                    || voidOutput.Number != 2) { return 1; }
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
    [TestCase(false)]
    [TestCase(true)]
    public async Task GenericInitNonBooleanAndVoidOutputsPublishCopyValuesAsync(bool checkedFailures)
    {
        const string Source = """
            struct Counter {
                public int64 Number;
                public Counter(int64 number) { Number = number; }
            }
            [NoThrow] int32 Fill<T>(T value, int32 status, init ref<T> result) where T : [Copy] {
                result = value;
                return status;
            }
            [NoThrow] void FillVoid<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
            }
            int32 Main() {
                int32 zeroStatus = Fill(new Counter(20), 0, init var first);
                int32 status = Fill(new Counter(20), 7, init var second);
                FillVoid<Counter>(new Counter(2), init var third);
                int32 boolStatus = Fill(true, 0, init var flag);
                if (boolStatus != 0 || !flag) { return 1; }
                return zeroStatus + status + int32(first.Number) + int32(second.Number)
                    + int32(third.Number) - 7;
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
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task GenericInitNonBooleanOutputPublishesManagedHandleAsync(NativeRuntimePolicy policy)
    {
        const string Source = """
            [NoThrow] int32 Fill<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                return 7;
            }
            int32 Make(init ref<write ManagedNode> result) {
                require ManagedNode.TryCreate(42, out var created);
                return Fill(created, init result);
            }
            int32 Main() {
                if (ManagedHeap.Collect() != 0) { return 1; }
                int32 status = Make(init var copy);
                if (ManagedHeap.Collect() != 0) { return 2; }
                return status + copy.Value;
            }
            """;
        if (policy == NativeRuntimePolicy.CheckedExceptions)
        {
            await AssertCheckedExecutionAsync(Source, 49).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(Source, 49).ConfigureAwait(true);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task FloatingCopySpecializationsPreserveConditionalAndVacancyOutputsAsync(bool checkedFailures)
    {
        const string Source = """
            struct Inner {
                public float32 Single;
                public int32 Marker;
                [NoThrow] public Inner(float32 single, int32 marker) { Single = single; Marker = marker; }
            }
            struct Pair {
                public Inner Nested;
                public float64 Wide;
                [NoThrow] public Pair(Inner nested, float64 wide) { Nested = nested; Wide = wide; }
                [NoThrow] public float32 ReadSingle() { return Nested.Single; }
            }
            [NoThrow] T Identity<T>(T value) where T : [Copy] { return value; }
            [NoThrow] bool TryCreatePair(init ref<Pair> result) {
                result = new Pair(new Inner(float32(1), 42), float64(2));
                return true;
            }
            [NoThrow] float32 CopyFloat(float32 value) { return value; }
            [NoThrow] T Forward<T>(T value) where T : [Copy] {
                if (!TryCreatePair(init var pair)) { return Identity(value); }
                if (CopyFloat(pair.ReadSingle()) != float32(1) || pair.Wide != float64(2)) {
                    return Identity(value);
                }
                return Identity(value);
            }
            [NoThrow] bool TryCopy<T>(T value, bool publish, out T result) where T : [Copy] {
                result = Forward(value);
                return publish;
            }
            [NoThrow] bool TryCopyInit<T>(T value, bool publish, init ref<T> result) where T : [Copy] {
                if (!publish) { return false; }
                result = Forward(value);
                return true;
            }
            int32 Main() {
                float32 first = Identity<float32>(float32(40));
                float64 second = Forward(float64(2));
                if (TryCopy(first, false, out var absent32)) { return 1; }
                require TryCopy<float32>(first, true, out var copied32);
                if (TryCopyInit(second, false, init var absent64)) { return 2; }
                require TryCopyInit<float64>(second, true, init var copied64);
                if (copied32 != float32(40) || copied64 != float64(2)) { return 3; }
                return int32(copied32) + int32(copied64);
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
                require SourceRange.TryCreate(12, 5, init var firstRange);
                require SourceRange.TryCreate(42, 7, init var secondRange);
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
    [TestCase(false)]
    [TestCase(true)]
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
            [NoThrow] bool ForwardInit<T>(T first, T second, bool publish,
                init ref<T> left, init ref<T> right, init ref<uint64> number, init ref<char16> unit)
                where T : [Copy] {
                return SplitInit<T>(first, 42, second, publish, init left, init right,
                    init number, init unit);
            }
            [NoThrow] bool SplitInit<T>(T first, int32 marker, T second, bool publish,
                init ref<T> left, init ref<T> right, init ref<uint64> number, init ref<char16> unit)
                where T : [Copy] {
                if (marker != 42 || !publish) { return false; }
                left = first;
                right = second;
                number = 18446744073709551615;
                unit = char16(55296);
                return true;
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
                if (ForwardInit<Pair>(first, second, false, init var initLeft, init var initRight,
                    init var initNumber, init var initUnit)) { return 2; }
                require ForwardInit<Pair>(first, second, true, init initLeft, init initRight,
                    init initNumber, init initUnit);
                require initLeft.First == -9223372036854775808
                    && initLeft.Second == 9223372036854775807;
                require initRight.First == right.First && initRight.Second == right.Second;
                require initNumber == number && initUnit == unit;
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

    [Test]
    public async Task GenericInitDoesNotPublishAfterCheckedFailureAsync()
    {
        const string Source = """
            bool FailAfterWrite<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                require false;
                return true;
            }
            [NoThrow] bool Fill<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                return true;
            }
            [NoThrow] bool None<T>(init ref<T> result) where T : [Copy] { return false; }
            int32 Main() {
                if (None<int32>(init var target)) { return 1; }
                unsafe {
                    try { if (FailAfterWrite<int32>(41, init target)) { return 1; } }
                    catch { }
                }
                require Fill(42, init target);
                return target;
            }
            """;
        await AssertCheckedExecutionAsync(Source, 42).ConfigureAwait(true);
    }

    [Test]
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

    [Test]
    public async Task ConstrainedGenericAccumulationPublishesOnlyOnAcceptedCountsAsync()
    {
        const string Source = """
            interface Addition<A, B, C> { static C operator +(A left, B right); }
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

    [Test]
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

    [Test]
    public Task GenericNonBooleanOutputFailureDoesNotPublishAsync()
    {
        const string Source = """
            struct Value {
                public int64 Number;
                public Value(int64 number) { Number = number; }
            }
            int32 Fill<T>(T value, bool complete, out T result) where T : [Copy] {
                result = value;
                require complete;
                return 7;
            }
            int32 Main() {
                mut Value result = new Value(1);
                unsafe {
                    try {
                        Fill(new Value(40), false, out result);
                        return 1;
                    } catch {
                        int32 status = Fill<Value>(new Value(42), true, out result);
                        if (status != 7) { return 3; }
                        return int32(result.Number);
                    }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }

    [Test]
    public Task GenericInitNonBooleanOutputFailureLeavesDestinationVacantAsync()
    {
        const string Source = """
            struct Value {
                public int64 Number;
                public Value(int64 number) { Number = number; }
            }
            int32 Fill<T>(T value, bool complete, init ref<T> result) where T : [Copy] {
                result = value;
                require complete;
                return 7;
            }
            int32 Main() {
                init Value result;
                unsafe {
                    try {
                        Fill(new Value(40), false, init result);
                        return 1;
                    } catch {
                        if (result is init) { return 2; }
                        Fill<Value>(new Value(42), true, init result);
                        return int32(result.Number);
                    }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task GenericMixedOutputsPublishUnderTheirDistinctModesAsync(bool checkedFailures)
    {
        const string Source = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            [NoThrow] int32 Fill<T, U>(T value, U second, int32 status, out T result,
                init ref<U> initialized) where T : [Copy] where U : [Copy] {
                result = value;
                initialized = second;
                return status;
            }
            [NoThrow] void FillVoid<T, U>(T value, U second, out T result,
                init ref<U> initialized) where T : [Copy] where U : [Copy] {
                result = value;
                initialized = second;
            }
            [NoThrow] int32 FillSame<T>(T value, out T result, init ref<T> initialized)
                where T : [Copy] {
                result = value;
                initialized = value;
                return 7;
            }
            int32 Main() {
                mut Pair previous = new Pair(1, 2);
                int32 zeroStatus = Fill(new Pair(40, 2), int64(3), 0, out previous, init var first);
                int32 status = Fill<Pair, int64>(new Pair(42, 2), int64(5), 7,
                    out previous, init var second);
                mut Pair voidResult = new Pair(0, 0);
                init int64 voidInitialized;
                FillVoid(new Pair(40, 2), int64(6), out voidResult, init voidInitialized);
                mut Pair sameTypeResult = new Pair(0, 0);
                int32 sameTypeStatus = FillSame(new Pair(44, 2), out sameTypeResult,
                    init var sameTypeInitialized);
                if (zeroStatus != 0 || status != 7 || previous.First != 42 || first != 3 || second != 5
                    || voidResult.First != 40 || voidInitialized != 6 || sameTypeStatus != 7
                    || sameTypeResult.First != 44 || sameTypeInitialized.First != 44) {
                    return 1;
                }
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
    [TestCase(false)]
    [TestCase(true)]
    public async Task GenericFixedScalarOutputsPublishAlongsideGenericOutputsAsync(bool checkedFailures)
    {
        const string Source = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            int32 Fill<T>(T value, out T result, out int32 count, init ref<float64> score)
                where T : [Copy] {
                result = value;
                count = 2;
                score = float64(3.5);
                return 7;
            }
            int32 Main() {
                mut Pair copied = new Pair(0, 0);
                int32 status = Fill(new Pair(40, 2), out copied, out var count, init var score);
                if (status != 7 || copied.First != 40 || count != 2 || score != float64(3.5)) {
                    return 1;
                }
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
    [TestCase(false)]
    [TestCase(true)]
    public async Task FloatingGenericNonBooleanAndVoidOutputsPublishAsync(bool checkedFailures)
    {
        const string Source = """
            [NoThrow] int32 Fill<T>(T value, int32 status, out T copied, init ref<T> initialized)
                where T : [Copy] {
                copied = value;
                initialized = value;
                return status;
            }
            [NoThrow] void FillVoid<T>(T value, out T copied, init ref<T> initialized)
                where T : [Copy] {
                copied = value;
                initialized = value;
            }
            int32 Main() {
                int32 zeroStatus = Fill(float32(1.5), 0, out var first, init var firstCopy);
                int32 status = Fill<float64>(float64(2.5), 7, out var second, init var secondCopy);
                FillVoid(float32(3.5), out var voidCopy, init var voidInitialized);
                int32 booleanStatus = Fill<bool>(true, 0, out var booleanCopy,
                    init var booleanInitialized);
                if (zeroStatus != 0 || status != 7 || booleanStatus != 0
                    || !booleanCopy || !booleanInitialized || first != float32(1.5)
                    || firstCopy != float32(1.5) || second != float64(2.5)
                    || secondCopy != float64(2.5) || voidCopy != float32(3.5)
                    || voidInitialized != float32(3.5)) {
                    return 1;
                }
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
    [TestCase(false)]
    [TestCase(true)]
    public async Task GenericFloatingResultsPreserveZeroAndSubnormalOutputsAsync(bool checkedFailures)
    {
        const string Source = """
            [NoThrow] float32 Fill32<T>(T value, out T copied, init ref<T> initialized,
                out int32 count, init ref<float64> score)
                where T : [Copy] {
                copied = value;
                initialized = value;
                count = 2;
                score = float64(3.5);
                return float32(-0.0);
            }
            [NoThrow] float64 Fill64<T>(T value, out T copied, init ref<T> initialized,
                out int32 count, init ref<float64> score)
                where T : [Copy] {
                copied = value;
                initialized = value;
                count = 3;
                score = float64(4.5);
                return float64(-0.0);
            }
            [NoThrow] float32 Tiny32<T>(T value, out T copied) where T : [Copy] {
                copied = value;
                return float32(1.401298464324817e-45);
            }
            [NoThrow] float64 Tiny64<T>(T value, init ref<T> initialized) where T : [Copy] {
                initialized = value;
                return float64(4.9406564584124654e-324);
            }
            int32 Main() {
                float32 negative32 = Fill32(int32(42), out var copied32, init var initialized32,
                    out var count32, init var score32);
                float64 negative64 = Fill64<int32>(int32(43), out var copied64, init var initialized64,
                    out var count64, init var score64);
                float32 tiny32 = Tiny32(int32(44), out var copiedTiny32);
                float64 tiny64 = Tiny64<int32>(int32(45), init var initializedTiny64);
                if (negative32 != float32(0.0) || float32(1.0) / negative32 >= float32(0.0)
                    || negative64 != float64(0.0) || float64(1.0) / negative64 >= float64(0.0)
                    || copied32 != 42 || initialized32 != 42 || copied64 != 43 || initialized64 != 43
                    || count32 != 2 || score32 != float64(3.5)
                    || count64 != 3 || score64 != float64(4.5)
                    || tiny32 != float32(1.401298464324817e-45)
                    || copiedTiny32 != 44 || tiny64 != float64(4.9406564584124654e-324)
                    || initializedTiny64 != 45) {
                    return 1;
                }
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

    [Test]
    public Task GenericFloatingResultDoesNotPublishAfterCheckedFailureAsync()
    {
        const string Source = """
            float64 Fill<T>(T value, bool complete, out T copied, init ref<T> initialized,
                out int32 count, init ref<float64> score) where T : [Copy] {
                copied = value;
                initialized = value;
                count = 2;
                score = float64(3.5);
                require complete;
                return float64(-0.0);
            }
            int32 Main() {
                mut int32 copied = 40;
                init int32 initialized;
                mut int32 count = 0;
                init float64 score;
                unsafe {
                    try {
                        Fill(int32(41), false, out copied, init initialized, out count, init score);
                        return 1;
                    } catch {
                        if (initialized is init || score is init) { return 2; }
                        float64 result = Fill<int32>(42, true, out copied, init initialized,
                            out count, init score);
                        if (result != float64(0.0) || float64(1.0) / result >= float64(0.0)
                            || copied != 42 || initialized != 42 || count != 2
                            || score != float64(3.5)) { return 3; }
                        return 42;
                    }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }

    [Test]
    public Task GenericFixedScalarOutputsDoNotPublishAfterCheckedFailureAsync()
    {
        const string Source = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            int32 Fill<T>(T value, bool complete, out T result, out int32 count,
                init ref<float64> score) where T : [Copy] {
                result = value;
                count = 2;
                score = float64(3.5);
                require complete;
                return 7;
            }
            int32 Main() {
                mut Pair copied = new Pair(1, 2);
                init float64 score;
                unsafe {
                    try {
                        Fill(new Pair(40, 2), false, out copied, out var count, init score);
                        return 1;
                    } catch {
                        if (score is init) { return 2; }
                        int32 status = Fill(new Pair(42, 2), true, out copied,
                            out var retriedCount, init score);
                        if (status != 7 || copied.First != 42 || retriedCount != 2
                            || score != float64(3.5)) { return 3; }
                        return 42;
                    }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task GenericMixedOutputsRetainManagedInitRootsAsync(NativeRuntimePolicy policy)
    {
        const string Source = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            [NoThrow] int32 Copy<T, U>(T value, U second, int32 status, out T result,
                init ref<U> initialized) where T : [Copy] where U : [Copy] {
                result = value;
                initialized = second;
                return status;
            }
            int32 Fill(out Pair result, init ref<write ManagedNode> initialized) {
                require ManagedNode.TryCreate(42, out var created);
                return Copy(new Pair(40, 2), created, 7, out result, init initialized);
            }
            int32 Main() {
                if (ManagedHeap.Collect() != 0) { return 1; }
                mut Pair pair = new Pair(0, 0);
                int32 status = Fill(out pair, init var node);
                if (ManagedHeap.Collect() != 0) { return 2; }
                if (pair.First != 40 || pair.Second != 2) { return 3; }
                return status + node.Value;
            }
            """;
        if (policy == NativeRuntimePolicy.CheckedExceptions)
        {
            await AssertCheckedExecutionAsync(Source, 49).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(Source, 49).ConfigureAwait(true);
        }
    }

    [Test]
    public Task GenericMixedManagedOutputsRetireRootsAfterCheckedFailureAsync()
    {
        const string Source = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            int32 Copy<T, U>(T value, U second, bool complete, out T result,
                init ref<U> initialized) where T : [Copy] where U : [Copy] {
                result = value;
                initialized = second;
                require complete;
                return 7;
            }
            int32 Fill(bool complete, out Pair result, init ref<write ManagedNode> initialized) {
                require ManagedNode.TryCreate(42, out var created);
                return Copy(new Pair(42, 2), created, complete, out result, init initialized);
            }
            int32 Main() {
                mut Pair previous = new Pair(40, 2);
                init write ManagedNode node;
                unsafe {
                    try {
                        Fill(false, out previous, init node);
                        return 1;
                    } catch {
                        if (node is init) { return 2; }
                        if (ManagedHeap.Collect() != 1) { return 3; }
                        int32 status = Fill(true, out previous, init node);
                        if (ManagedHeap.Collect() != 0 || previous.First != 42) { return 4; }
                        return status + node.Value;
                    }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 49);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task GenericBooleanMixedOutputPublicationRemainsConditionalAsync(bool checkedFailures)
    {
        const string Source = """
            [NoThrow] bool Fill<T>(T value, int32 number, bool publish, out T result,
                init ref<int32> initialized) where T : [Copy] {
                result = value;
                initialized = number;
                return publish;
            }
            int32 Main() {
                require ManagedNode.TryCreate(40, out var value);
                require ManagedNode.TryCreate(42, out var replacement);
                if (Fill(value, 3, false, out var absent, init var number)) { return 1; }
                require Fill(replacement, 5, true, out var result, init number);
                return result.Value + number - 5;
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
    public Task GenericMixedOutputsDoNotPublishAfterCheckedFailureAsync()
    {
        const string Source = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            int32 Fill<T, U>(T value, U second, bool complete, out T result,
                init ref<U> initialized) where T : [Copy] where U : [Copy] {
                result = value;
                initialized = second;
                require complete;
                return 7;
            }
            int32 Main() {
                mut Pair previous = new Pair(40, 2);
                init int64 initialized;
                unsafe {
                    try {
                        Fill(new Pair(42, 3), int64(4), false, out previous, init initialized);
                        return 1;
                    } catch {
                        if (initialized is init) { return 2; }
                        int32 status = Fill<Pair, int64>(new Pair(42, 2), int64(5), true,
                            out previous, init initialized);
                        if (status != 7 || initialized != 5) { return 3; }
                        return int32(previous.First);
                    }
                }
                return 0;
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }

    [Test]
    public Task GenericInitManagedOutputFailureRetiresTemporaryRootAsync()
    {
        const string Source = """
            int32 Copy<T>(T value, bool complete, init ref<T> result) where T : [Copy] {
                result = value;
                require complete;
                return 7;
            }
            int32 Fill(bool complete, init ref<write ManagedNode> result) {
                require ManagedNode.TryCreate(42, out var created);
                return Copy(created, complete, init result);
            }
            int32 Main() {
                init write ManagedNode node;
                unsafe {
                    try {
                        Fill(false, init node);
                        return 1;
                    } catch {
                        if (node is init) { return 2; }
                        if (ManagedHeap.Collect() != 1) { return 3; }
                        int32 status = Fill(true, init node);
                        if (ManagedHeap.Collect() != 0) { return 4; }
                        return status + node.Value;
                    }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 49);
    }
}
