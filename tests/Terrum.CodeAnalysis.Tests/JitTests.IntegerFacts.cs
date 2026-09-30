// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Fact]
    public async Task CheckedInt32PredicatesMatchWidenedArithmeticAcrossAotAndJitAsync()
    {
        var checks = new List<string>();
        int[] values = [int.MinValue, int.MinValue + 1, -46341, -1, 0, 1,
            46340, 46341, int.MaxValue];
        foreach (var left in values)
        {
            var leftLiteral = Int32Literal(left);
            checks.Add($"CanNegateInt32({leftLiteral}) != {(left != int.MinValue ? "true" : "false")}");
            checks.Add($"CanAbsoluteInt32({leftLiteral}) != {(left != int.MinValue ? "true" : "false")}");
            foreach (var right in values)
            {
                var rightLiteral = Int32Literal(right);
                var sum = (long)left + right;
                var difference = (long)left - right;
                var product = (long)left * right;
                checks.Add($"CanAddInt32({leftLiteral}, {rightLiteral}) != "
                    + (sum is >= int.MinValue and <= int.MaxValue ? "true" : "false"));
                checks.Add($"CanSubtractInt32({leftLiteral}, {rightLiteral}) != "
                    + (difference is >= int.MinValue and <= int.MaxValue ? "true" : "false"));
                checks.Add($"CanMultiplyInt32({leftLiteral}, {rightLiteral}) != "
                    + (product is >= int.MinValue and <= int.MaxValue ? "true" : "false"));
                checks.Add($"CanDivideInt32({leftLiteral}, {rightLiteral}) != "
                    + (right != 0 && (left != int.MinValue || right != -1) ? "true" : "false"));
                checks.Add($"CanRemainderInt32({leftLiteral}, {rightLiteral}) != "
                    + (right != 0 ? "true" : "false"));
            }
        }

        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFacts.terrum"))
            .ConfigureAwait(true);
        foreach (var batch in checks.Chunk(32))
        {
            var assertions = string.Join(Environment.NewLine, batch.Select((check, index) =>
                $"if ({check}) {{ return {index + 1}; }}"));
            var source = component + """

                int32 Main() {
                    static require !CanAddInt32(int32.MaxValue, 1);
                    static require !CanSubtractInt32(int32.MinValue, 1);
                    static require !CanMultiplyInt32(int32.MaxValue, 2);
                    static require !CanDivideInt32(int32.MinValue, -1);
                    static require CanRemainderInt32(int32.MinValue, -1);
                    static require !CanNegateInt32(int32.MinValue);
                    static require !CanAbsoluteInt32(int32.MinValue);
                """ + assertions + "\nreturn 42;\n}";
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task CheckedUInt32PredicatesMatchWidenedArithmeticAcrossAotAndJitAsync()
    {
        var checks = new List<string>();
        uint[] values = [0, 1, 2, 65535, 65536, 2147483647, 2147483648, uint.MaxValue];
        foreach (var left in values)
        {
            foreach (var right in values)
            {
                var sum = (ulong)left + right;
                var product = (ulong)left * right;
                checks.Add($"CanAddUInt32({left}, {right}) != "
                    + (sum <= uint.MaxValue ? "true" : "false"));
                checks.Add($"CanSubtractUInt32({left}, {right}) != "
                    + (left >= right ? "true" : "false"));
                checks.Add($"CanMultiplyUInt32({left}, {right}) != "
                    + (product <= uint.MaxValue ? "true" : "false"));
                checks.Add($"CanDivideUInt32({left}, {right}) != "
                    + (right != 0 ? "true" : "false"));
                checks.Add($"CanRemainderUInt32({left}, {right}) != "
                    + (right != 0 ? "true" : "false"));
            }
        }

        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFacts.terrum"))
            .ConfigureAwait(true);
        foreach (var batch in checks.Chunk(32))
        {
            var assertions = string.Join(Environment.NewLine, batch.Select((check, index) =>
                $"if ({check}) {{ return {index + 1}; }}"));
            var source = component + """

                int32 Main() {
                    static require !CanAddUInt32(uint32.MaxValue, 1);
                    static require CanAddUInt32(0, uint32.MaxValue);
                    static require !CanSubtractUInt32(0, 1);
                    static require CanSubtractUInt32(uint32.MaxValue, uint32.MaxValue);
                    static require !CanMultiplyUInt32(uint32.MaxValue, 2);
                    static require CanMultiplyUInt32(0, uint32.MaxValue);
                    static require !CanDivideUInt32(1, 0);
                    static require !CanRemainderUInt32(1, 0);
                """ + assertions + "\nreturn 42;\n}";
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task CheckedInt64PredicatesMatchExactArithmeticAcrossAotAndJitAsync()
    {
        var checks = new List<string>();
        long[] values = [long.MinValue, long.MinValue + 1, -3037000500,
            -3037000499, -1, 0, 1, 3037000499, 3037000500,
            long.MaxValue - 1, long.MaxValue];
        foreach (var left in values)
        {
            var leftLiteral = Int64Literal(left);
            checks.Add($"CanNegateInt64({leftLiteral}) != "
                + (left != long.MinValue ? "true" : "false"));
            checks.Add($"CanAbsoluteInt64({leftLiteral}) != "
                + (left != long.MinValue ? "true" : "false"));
            foreach (var right in values)
            {
                var rightLiteral = Int64Literal(right);
                var sum = (BigInteger)left + right;
                var difference = (BigInteger)left - right;
                var product = (BigInteger)left * right;
                checks.Add($"CanAddInt64({leftLiteral}, {rightLiteral}) != "
                    + (sum >= long.MinValue && sum <= long.MaxValue ? "true" : "false"));
                checks.Add($"CanSubtractInt64({leftLiteral}, {rightLiteral}) != "
                    + (difference >= long.MinValue && difference <= long.MaxValue ? "true" : "false"));
                checks.Add($"CanMultiplyInt64({leftLiteral}, {rightLiteral}) != "
                    + (product >= long.MinValue && product <= long.MaxValue ? "true" : "false"));
                checks.Add($"CanDivideInt64({leftLiteral}, {rightLiteral}) != "
                    + (right != 0 && (left != long.MinValue || right != -1) ? "true" : "false"));
                checks.Add($"CanRemainderInt64({leftLiteral}, {rightLiteral}) != "
                    + (right != 0 ? "true" : "false"));
            }
        }

        await AssertIntegerFactsAsync(checks, """
            static require !CanAddInt64(int64.MaxValue, 1);
            static require !CanSubtractInt64(int64.MinValue, 1);
            static require !CanMultiplyInt64(int64.MinValue, -1);
            static require CanMultiplyInt64(int64.MinValue, 1);
            static require CanMultiplyInt64(0, int64.MinValue);
            static require !CanDivideInt64(int64.MinValue, -1);
            static require CanRemainderInt64(int64.MinValue, -1);
            static require !CanNegateInt64(int64.MinValue);
            static require !CanAbsoluteInt64(int64.MinValue);
            """).ConfigureAwait(true);
    }

    [Fact]
    public async Task CheckedUInt64PredicatesMatchExactArithmeticAcrossAotAndJitAsync()
    {
        var checks = new List<string>();
        ulong[] values = [0, 1, 2, 4294967295, 4294967296,
            9223372036854775807, 9223372036854775808, ulong.MaxValue - 1,
            ulong.MaxValue];
        foreach (var left in values)
        {
            var leftLiteral = UInt64Literal(left);
            foreach (var right in values)
            {
                var rightLiteral = UInt64Literal(right);
                var sum = (BigInteger)left + right;
                var product = (BigInteger)left * right;
                checks.Add($"CanAddUInt64({leftLiteral}, {rightLiteral}) != "
                    + (sum <= ulong.MaxValue ? "true" : "false"));
                checks.Add($"CanSubtractUInt64({leftLiteral}, {rightLiteral}) != "
                    + (left >= right ? "true" : "false"));
                checks.Add($"CanMultiplyUInt64({leftLiteral}, {rightLiteral}) != "
                    + (product <= ulong.MaxValue ? "true" : "false"));
                checks.Add($"CanDivideUInt64({leftLiteral}, {rightLiteral}) != "
                    + (right != 0 ? "true" : "false"));
                checks.Add($"CanRemainderUInt64({leftLiteral}, {rightLiteral}) != "
                    + (right != 0 ? "true" : "false"));
            }
        }

        await AssertIntegerFactsAsync(checks, """
            static require !CanAddUInt64(uint64.MaxValue, 1);
            static require CanAddUInt64(0, uint64.MaxValue);
            static require !CanSubtractUInt64(0, 1);
            static require !CanMultiplyUInt64(uint64.MaxValue, 2);
            static require CanMultiplyUInt64(0, uint64.MaxValue);
            static require !CanDivideUInt64(1, 0);
            static require !CanRemainderUInt64(1, 0);
            """).ConfigureAwait(true);
    }

    private static async Task AssertIntegerFactsAsync(List<string> checks, string proofs)
    {
        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerFacts.terrum"))
            .ConfigureAwait(true);
        foreach (var batch in checks.Chunk(32))
        {
            var assertions = string.Join(Environment.NewLine, batch.Select((check, index) =>
                $"if ({check}) {{ return {index + 1}; }}"));
            var source = component + "\nint32 Main() {\n" + proofs + "\n"
                + assertions + "\nreturn 42;\n}";
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }
}
