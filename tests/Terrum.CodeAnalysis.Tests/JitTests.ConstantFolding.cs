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
    [InlineData("int32 Main() { return (int32.MaxValue + 1) - 1; }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return (42 / 0) * 0; }", unchecked((int)0xE0540002))]
    [InlineData("int32 Main() { return int32(uint32.MaxValue); }", unchecked((int)0xE0540001))]
    [InlineData("int32 Main() { return 0 * Fail(); } int32 Fail() { require false; return 42; }", unchecked((int)0xE0540003))]
    public Task UnsafeScalarExpressionsStillFailInOrderAcrossAotAndJitAsync(string source, int failure) =>
        AssertFailFastExecutionAsync(source, failure);

    [Theory]
    [InlineData("int32 Main() { unsafe { try { return (int32.MaxValue + 1) - 1; } catch { return 42; } } }")]
    [InlineData("int32 Main() { unsafe { try { return (42 / 0) * 0; } catch { return 42; } } }")]
    [InlineData("int32 Main() { unsafe { try { return int32(uint32.MaxValue); } catch { return 42; } } }")]
    [InlineData("int32 Main() { unsafe { try { return 0 * Fail(); } catch { return 42; } } } int32 Fail() { require false; return 0; }")]
    public Task UnsafeScalarExpressionsStayCatchableAcrossAotAndJitAsync(string source) =>
        AssertCheckedExecutionAsync(source, 42);
}
