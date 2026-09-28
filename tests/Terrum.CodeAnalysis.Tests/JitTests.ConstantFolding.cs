// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
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

    [Theory]
    [InlineData("int32 Main() { return (int32.MaxValue + 1) - 1; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return (42 / 0) * 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { return int32(uint32.MaxValue); }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return 0 * Fail(); } int32 Fail() { require false; return 42; }", unchecked((int)0xE0540003))]
    [InlineData("int32 Main() { require uint64.MaxValue < uint64(0); return 0; }", unchecked((int)0xE0540003))]
    [InlineData("int32 Main() { require int32.MaxValue + 1 == int32(0); return 0; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { require Fail() == int32(0); return 0; } int32 Fail() { require false; return 0; }", unchecked((int)0xE0540003))]
    [InlineData("int32 Main() { require true && (int32.MaxValue + 1 > int32(0)); return 0; }", unchecked((int)0xE0540001))]
    public Task UnsafeScalarExpressionsStillFailInOrderAcrossAotAndJitAsync(string source, int failure) =>
        AssertFailFastExecutionAsync(source, failure);

    [Theory]
    [InlineData("int32 Main() { unsafe { try { return (int32.MaxValue + 1) - 1; } catch { return 42; } } }")]
    [InlineData("int32 Main() { unsafe { try { return (42 / 0) * 0; } catch { return 42; } } }")]
    [InlineData("int32 Main() { unsafe { try { return int32(uint32.MaxValue); } catch { return 42; } } }")]
    [InlineData("int32 Main() { unsafe { try { return 0 * Fail(); } catch { return 42; } } } int32 Fail() { require false; return 0; }")]
    [InlineData("int32 Main() { unsafe { try { require uint64.MaxValue < uint64(0); } catch { return 42; } } return 0; }")]
    [InlineData("int32 Main() { unsafe { try { require int32.MaxValue + 1 == int32(0); } catch { return 42; } } return 0; }")]
    [InlineData("int32 Main() { unsafe { try { require Fail() == int32(0); } catch { return 42; } } return 0; } int32 Fail() { require false; return 0; }")]
    [InlineData("int32 Main() { unsafe { try { require true && (int32.MaxValue + 1 > int32(0)); } catch { return 42; } } return 0; }")]
    public Task UnsafeScalarExpressionsStayCatchableAcrossAotAndJitAsync(string source) =>
        AssertCheckedExecutionAsync(source, 42);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ConstantShortCircuitDoesNotDropUnknownOperandAcrossAotAndJitAsync(bool checkedFailures)
    {
        const string Source = """
            bool Fail() { require false; return false; }
            int32 Main() { require true || Fail(); return 42; }
            """;
        return checkedFailures ? AssertCheckedExecutionAsync(Source, 42)
            : AssertFailFastExecutionAsync(Source, 42);
    }

    [Theory]
    [InlineData(false, "uint32", "/", "2147483648", "1")]
    [InlineData(true, "uint32", "%", "2147483648", "2147483647")]
    [InlineData(false, "uint64", "%", "9223372036854775808", "9223372036854775807")]
    [InlineData(true, "uint64", "/", "9223372036854775808", "1")]
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

    [Theory]
    [InlineData("uint32", "/")]
    [InlineData("uint64", "%")]
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
}
