// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task FoldedScalarNoThrowResultsAgreeAcrossAotAndJitAsync(bool checkedFailures)
    {
        const string Source = """
            [NoThrow] int32 Fold() {
                return int32(uint32(42)) + int32(uint64.MaxValue % uint64(100));
            }
            [NoThrow] uint64 Bits() { return uint64.MaxValue - 1; }
            int32 Main() {
                if (Bits() != uint64.MaxValue - 1) { return 0; }
                return Fold() - 15;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false)]
    [TestCase(true)]
    public Task FoldedConstantComparisonsAgreeAcrossAotAndJitAsync(bool checkedFailures)
    {
        const string Source = """
            [NoThrow] int32 Check() {
                require uint64.MaxValue > uint64(int64.MaxValue);
                require char16(65535) >= char16(65535);
                require (true && !false) == true;
                require (true ^ false) | false;
                if (uint32.MaxValue < uint32(0)) { return 0; }
                return 42;
            }
            int32 Main() { return Check(); }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase("int32 Main() { return (int32.MaxValue + 1) - 1; }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { return (42 / 0) * 0; }",unchecked((int)0xE0540002))]
    [TestCase("int32 Main() { return int32(uint32.MaxValue); }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { return 0 * Fail(); } int32 Fail() { require false; return 42; }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { require uint64.MaxValue < uint64(0); return 0; }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { require int32.MaxValue + 1 == int32(0); return 0; }",unchecked((int)0xE0540001))]
    [TestCase("int32 Main() { require Fail() == int32(0); return 0; } int32 Fail() { require false; return 0; }",unchecked((int)0xE0540003))]
    [TestCase("int32 Main() { require true && (int32.MaxValue + 1 > int32(0)); return 0; }",unchecked((int)0xE0540001))]
    public Task UnsafeScalarExpressionsStillFailInOrderAcrossAotAndJitAsync(string source, int failure) =>
        AssertFailFastExecutionAsync(source, failure);
    [TestCase("int32 Main() { unsafe { try { return (int32.MaxValue + 1) - 1; } catch { return 42; } } }")]
    [TestCase("int32 Main() { unsafe { try { return (42 / 0) * 0; } catch { return 42; } } }")]
    [TestCase("int32 Main() { unsafe { try { return int32(uint32.MaxValue); } catch { return 42; } } }")]
    [TestCase("int32 Main() { unsafe { try { return 0 * Fail(); } catch { return 42; } } } int32 Fail() { require false; return 0; }")]
    [TestCase("int32 Main() { unsafe { try { require uint64.MaxValue < uint64(0); } catch { return 42; } } return 0; }")]
    [TestCase("int32 Main() { unsafe { try { require int32.MaxValue + 1 == int32(0); } catch { return 42; } } return 0; }")]
    [TestCase("int32 Main() { unsafe { try { require Fail() == int32(0); } catch { return 42; } } return 0; } int32 Fail() { require false; return 0; }")]
    [TestCase("int32 Main() { unsafe { try { require true && (int32.MaxValue + 1 > int32(0)); } catch { return 42; } } return 0; }")]
    public Task UnsafeScalarExpressionsStayCatchableAcrossAotAndJitAsync(string source) =>
        AssertCheckedExecutionAsync(source, 42);
    [TestCase(false)]
    [TestCase(true)]
    public Task ConstantShortCircuitDoesNotDropUnknownOperandAcrossAotAndJitAsync(bool checkedFailures)
    {
        const string Source = """
            bool Fail() { require false; return false; }
            int32 Main() { require true || Fail(); return 42; }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }
    [TestCase(false,"uint32","/","2147483648","1")]
    [TestCase(true,"uint32","%","2147483648","2147483647")]
    [TestCase(false,"uint64","%","9223372036854775808","9223372036854775807")]
    [TestCase(true,"uint64","/","9223372036854775808","1")]
    public Task UnsignedPowerOfTwoOperationsAgreeAcrossAotAndJitAsync(
        bool checkedFailures, string type, string operation, string divisor, string expected)
    {
        var source = $$"""
            {{type}} Calculate({{type}} n) { return n {{operation}} {{type}}({{divisor}}); }
            int32 Main() {
                if (Calculate({{type}}.MaxValue) != {{type}}({{expected}})) { return 1; }
                return 42;
            }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(source, 42)
            : AssertFailFastExecutionAsync(source, 42);
    }
    [TestCase("uint32","/")]
    [TestCase("uint64","%")]
    public Task UnsignedPowerOfTwoNumeratorFailuresRemainCatchableAcrossAotAndJitAsync(string type, string operation)
    {
        var source = $$"""
            {{type}} Fail() { require false; return {{type}}(1); }
            int32 Main() {
                unsafe {
                    try { {{type}} result = Fail() {{operation}} {{type}}(2); }
                    catch { return 42; }
                }
                return 1;
            }
            """;
        return AssertCheckedExecutionAsync(source, 42);
    }
    [TestCase("int32","0","1")]
    [TestCase("int32","-1","1")]
    [TestCase("int32","-2147483648","1")]
    [TestCase("int32","2147483647","1")]
    [TestCase("int32","-3","2")]
    [TestCase("int32","3","2")]
    [TestCase("int32","-4","2")]
    [TestCase("int32","-2147483648","2")]
    [TestCase("int32","2147483647","2")]
    [TestCase("int32","-1073741825","1073741824")]
    [TestCase("int32","-2147483648","1073741824")]
    [TestCase("int32","2147483647","1073741824")]
    [TestCase("int64","0","1")]
    [TestCase("int64","-1","1")]
    [TestCase("int64","-9223372036854775808","1")]
    [TestCase("int64","9223372036854775807","1")]
    [TestCase("int64","-3","2")]
    [TestCase("int64","3","2")]
    [TestCase("int64","-9223372036854775808","2")]
    [TestCase("int64","9223372036854775807","2")]
    [TestCase("int64","-4611686018427387905","4611686018427387904")]
    [TestCase("int64","-9223372036854775808","4611686018427387904")]
    [TestCase("int64","9223372036854775807","4611686018427387904")]
    public async Task SignedPowerOfTwoDivisionAndRemainderMatchExactValuesAcrossAotAndJitAsync(
        string type, string numerator, string divisor)
    {
        var value = long.Parse(numerator, System.Globalization.CultureInfo.InvariantCulture);
        var power = long.Parse(divisor, System.Globalization.CultureInfo.InvariantCulture);
        var source = $$"""
            {{type}} Quot({{type}} n) { return n / {{type}}({{divisor}}); }
            {{type}} Rem({{type}} n) { return n % {{type}}({{divisor}}); }
            int32 Main() {
                {{type}} n = {{type}}({{numerator}});
                if (Quot(n) != {{type}}({{value / power}})
                    || Rem(n) != {{type}}({{value % power}})) { return 1; }
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
    [TestCase("int32","/","1")]
    [TestCase("int32","%","1073741824")]
    [TestCase("int64","/","4611686018427387904")]
    [TestCase("int64","%","1")]
    public async Task SignedPowerOfTwoNumeratorEffectsAndFailuresRemainObservableAcrossAotAndJitAsync(
        string type, string operation, string divisor)
    {
        var source = $$"""
            struct Counter {
                public mut int32 Calls;
                public Counter() { Calls = 0; }
                public mut {{type}} Read() { Calls = Calls + 1; return {{type}}(-3); }
            }
            int32 Main() {
                mut Counter counter = new Counter();
                {{type}} result = counter.Read() {{operation}} {{type}}({{divisor}});
                if (counter.Calls != 1) { return 1; }
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);

        var failing = $$"""
            {{type}} Fail() { require false; return {{type}}(1); }
            int32 Main() {
                unsafe {
                    try {
                        {{type}} result = Fail() {{operation}} {{type}}({{divisor}});
                    }
                    catch { return 42; }
                }
                return 1;
            }
            """;
        await AssertCheckedExecutionAsync(failing, 42).ConfigureAwait(true);
        await AssertFailFastExecutionAsync($$"""
            {{type}} Fail() { require false; return {{type}}(1); }
            int32 Main() {
                {{type}} result = Fail() {{operation}} {{type}}({{divisor}});
                return 1;
            }
            """, unchecked((int)0xE0540003)).ConfigureAwait(true);
    }
    [TestCase("int32","-3","-2","1","-1")]
    [TestCase("int32","-2147483648","-2","1073741824","0")]
    [TestCase("int32","-2147483648","-1",null,"0")]
    [TestCase("int64","-3","-2","1","-1")]
    [TestCase("int64","-9223372036854775808","-2","4611686018427387904","0")]
    [TestCase("int64","-9223372036854775808","-1",null,"0")]
    public async Task SignedNegativeDivisorControlsKeepExistingBehaviorAcrossAotAndJitAsync(
        string type, string numerator, string divisor, string? quotient, string remainder)
    {
        var source = $$"""
            {{type}} Quot({{type}} n) { return n / {{type}}({{divisor}}); }
            {{type}} Rem({{type}} n) { return n % {{type}}({{divisor}}); }
            int32 Main() {
                {{type}} n = {{type}}({{numerator}});
                if (Rem(n) != {{type}}({{remainder}})) { return 1; }
                {{(quotient is null ? "Quot(n); return 1;" : $"if (Quot(n) != {type}({quotient})) {{ return 1; }} return 42;")}}
            }
            """;
        var expected = quotient is null ? unchecked((int)0xE0540001) : 42;
        await AssertFailFastExecutionAsync(source, expected).ConfigureAwait(true);
        if (quotient is null)
        {
            await AssertCheckedExecutionAsync($$"""
                {{type}} Quot({{type}} n) { return n / {{type}}({{divisor}}); }
                int32 Main() {
                    unsafe {
                        try { {{type}} result = Quot({{type}}.MinValue); }
                        catch { return 42; }
                    }
                    return 1;
                }
                """, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertCheckedExecutionAsync(source, expected).ConfigureAwait(true);
        }
    }
    [TestCase("int32","/")]
    [TestCase("int32","%")]
    [TestCase("int64","/")]
    [TestCase("int64","%")]
    public async Task SignedZeroDivisorStillFailsAcrossAotAndJitAsync(string type, string operation)
    {
        var source = $$"""
            {{type}} Calculate({{type}} n) { return n {{operation}} {{type}}(0); }
            int32 Main() {
                {{type}} result = Calculate({{type}}.MinValue);
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, unchecked((int)0xE0540002)).ConfigureAwait(true);
        await AssertCheckedExecutionAsync($$"""
            {{type}} Calculate({{type}} n) { return n {{operation}} {{type}}(0); }
            int32 Main() {
                unsafe {
                    try { {{type}} result = Calculate({{type}}.MinValue); }
                    catch { return 42; }
                }
                return 1;
            }
            """, 42).ConfigureAwait(true);
    }
}
