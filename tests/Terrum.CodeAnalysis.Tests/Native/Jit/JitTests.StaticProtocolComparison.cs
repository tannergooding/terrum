// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ComparisonProtocolSampleAgreesBetweenAotAndJitAsync(bool checkedFailures)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(),
            "samples", "LanguageFeatures", "StaticProtocols", "static-protocol-comparison.terrum")).ConfigureAwait(true);
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }
    [TestCase("==",false)]
    [TestCase("!=",true)]
    [TestCase("<",true)]
    [TestCase("<=",true)]
    [TestCase(">",false)]
    [TestCase(">=",false)]
    public async Task EveryComparisonRunsConcreteAndGenericBoolCallsAsync(string op, bool expected)
    {
        var value = expected ? "true" : "false";
        var source = $$"""
            interface Comparison<A, B, C> { static C operator {{op}}(A left, B right); }
            struct Value : Comparison<Value, Value, bool> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static bool operator {{op}}(Value a, Value b) {
                    return a.Number {{op}} b.Number;
                }
            }
            bool Apply<T>(T a, T b) where T : Comparison<T, T, bool>, [Copy] {
                return a {{op}} b;
            }
            int32 Main() {
                if (Apply(new Value(20), new Value(22)) != {{value}}) { return 1; }
                if ((new Value(20) {{op}} new Value(22)) != {{value}}) { return 2; }
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Test]
    public async Task EqualityAndInequalityDoNotImplyComplementaryResultsAsync()
    {
        const string Source = """
            interface Equal<A, B, C> { static C operator ==(A a, B b); }
            interface Unequal<A, B, C> { static C operator !=(A a, B b); }
            struct Value : Equal<Value, Value, bool>, Unequal<Value, Value, bool> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static bool operator ==(Value a, Value b) { return true; }
                public static bool operator !=(Value a, Value b) { return true; }
            }
            bool Both<T>(T a, T b)
                where T : [Copy], Equal<T, T, bool>, Unequal<T, T, bool> {
                return a == b && a != b;
            }
            int32 Main() {
                var value = new Value(1);
                require Both(value, value);
                require value == value && value != value;
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(Source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(Source, 42).ConfigureAwait(true);
    }

    [Test]
    public Task ComparisonWitnessPreservesCheckedFailureAsync()
    {
        const string Source = """
            interface Less<A, B, C> { static C operator <(A left, B right); }
            struct Value : Less<Value, Value, bool> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static bool operator <(Value a, Value b) {
                    require false;
                    return a.Number < b.Number;
                }
            }
            bool Compare<T>(T a, T b) where T : Less<T, T, bool>, [Copy] { return a < b; }
            int32 Main() {
                unsafe {
                    try {
                        if (Compare(new Value(1), new Value(2))) { return 0; }
                        return 1;
                    }
                    catch { return 42; }
                }
            }
            """;
        return AssertCheckedExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task IndirectCopyComparisonReturnsBoolAcrossAotAndJitAsync(bool checkedFailures)
    {
        const string Source = """
            interface Less<A, B, C> { static C operator <(A a, B b); }
            struct Wide : Less<Wide, Wide, bool> {
                public int64 High;
                public int64 Low;
                public Wide(int64 high, int64 low) { High = high; Low = low; }
                public static bool operator <(Wide a, Wide b) {
                    return a.High < b.High;
                }
            }
            bool Compare<T>(T a, T b) where T : Less<T, T, bool>, [Copy] { return a < b; }
            int32 Main() {
                if (Compare(new Wide(20, 1), new Wide(22, 2))
                    && new Wide(20, 1) < new Wide(22, 2)) { return 42; }
                return 0;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
}
