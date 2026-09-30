// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                """ + assertions + "\nreturn 42;\n}";
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }
}
