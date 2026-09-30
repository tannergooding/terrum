// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnaryProtocolsExecuteDirectGenericAndBinaryCallsAsync(bool checkedFailures)
    {
        var source = StaticUnaryProtocolTests.Source + """
            int32 Main() {
                Value value = new Value(42);
                Value both = +value + -value - value;
                if (both.Number != 42 || !value != 42 || ~value != true) { return 1; }
                if (Negate<Value, int32>(value) != 42) { return 2; }
                return Compose(value).Number;
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

    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("!")]
    [InlineData("~")]
    public async Task EveryUnaryOperatorUsesItsOwnNominalWitnessAsync(string operation)
    {
        var source = $$"""
            interface Operation<T, R> { static R operator {{operation}}(T value); }
            struct Value : Operation<Value, int32> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static int32 operator {{operation}}(Value value) { return value.Number; }
            }
            int32 Apply<T>(T value) where T : Operation<T, int32>, [Copy] {
                return {{operation}}value;
            }
            int32 Main() {
                if ({{operation}}new Value(42) != 42) { return 1; }
                return Apply(new Value(42));
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Fact]
    public Task UnaryWitnessPreservesCatchableFailureAsync()
    {
        const string Source = """
            interface Negate<T, R> { static R operator -(T value); }
            struct Value : Negate<Value, int32> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static int32 operator -(Value value) {
                    require false;
                    return value.Number;
                }
            }
            int32 Apply<T>(T value) where T : Negate<T, int32>, [Copy] { return -value; }
            int32 Main() {
                unsafe {
                    try { return Apply(new Value(3)); }
                    catch { return 42; }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnaryOperandIsEvaluatedExactlyOnceAsync(bool checkedFailures)
    {
        const string Source = """
            interface Read<T, R> { static R operator !(T value); }
            struct Value : Read<Value, int32> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static int32 operator !(Value value) { return value.Number; }
            }
            struct Counter {
                public mut int32 Calls;
                public Counter() { Calls = 0; }
                public mut Value Next() {
                    Calls = Calls + 1;
                    return new Value(41 + Calls);
                }
            }
            int32 Apply<T>(T value) where T : Read<T, int32>, [Copy] { return !value; }
            int32 Main() {
                mut Counter counter = new Counter();
                int32 result = Apply(counter.Next());
                if (counter.Calls != 1) { return 1; }
                return result;
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
