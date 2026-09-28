// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Fact]
    public async Task WrappingIntegerConversionsMatchLowBitsAcrossAotAndJitAsync()
    {
        var checks = new List<string>();
        long[] signed = [
            long.MinValue, long.MinValue + 1, -4_294_967_297, -4_294_967_296,
            int.MinValue, -1, 0, 1, int.MaxValue, (long)uint.MaxValue,
            (long)uint.MaxValue + 1, long.MaxValue,
        ];
        foreach (var value in signed)
        {
            var literal = value == long.MinValue ? "int64.MinValue" : value.ToString(CultureInfo.InvariantCulture);
            checks.Add($"WrapInt64ToUInt32({literal}) != {unchecked((uint)value)}");
            checks.Add($"WrapInt64ToInt32({literal}) != {Int32Literal(unchecked((int)value))}");
            checks.Add($"WrapInt64ToUInt64({literal}) != {UInt64Literal(unchecked((ulong)value))}");
        }
        ulong[] unsigned = [
            0, 1, int.MaxValue, (ulong)int.MaxValue + 1, uint.MaxValue,
            (ulong)uint.MaxValue + 1, long.MaxValue, (ulong)long.MaxValue + 1,
            ulong.MaxValue,
        ];
        foreach (var value in unsigned)
        {
            var literal = UInt64Literal(value);
            checks.Add($"WrapUInt64ToUInt32({literal}) != {unchecked((uint)value)}");
            checks.Add($"WrapUInt64ToInt32({literal}) != {Int32Literal(unchecked((int)value))}");
            checks.Add($"WrapUInt64ToInt64({literal}) != {Int64Literal(unchecked((long)value))}");
        }
        int[] signed32 = [int.MinValue, -1, 0, 1, int.MaxValue];
        foreach (var value in signed32)
        {
            checks.Add($"WrapInt32ToUInt32({Int32Literal(value)}) != {unchecked((uint)value)}");
        }
        uint[] unsigned32 = [0, 1, int.MaxValue, (uint)int.MaxValue + 1, uint.MaxValue];
        foreach (var value in unsigned32)
        {
            checks.Add($"WrapUInt32ToInt32({value}) != {Int32Literal(unchecked((int)value))}");
        }

        var component = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "self-host", "IntegerWrapping.terrum")).ConfigureAwait(true);
        foreach (var batch in checks.Chunk(24))
        {
            var assertions = string.Join(Environment.NewLine, batch.Select((check, index) =>
                $"if ({check}) {{ return {index + 1}; }}"));
            var source = component + "\nint32 Main() {\n" + assertions + "\nreturn 42;\n}";
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }

    private static string Int32Literal(int value) =>
        value == int.MinValue ? "int32.MinValue" : value.ToString(CultureInfo.InvariantCulture);

    private static string Int64Literal(long value) =>
        value == long.MinValue ? "int64.MinValue" : value.ToString(CultureInfo.InvariantCulture);

    private static string UInt64Literal(ulong value) =>
        value == ulong.MaxValue ? "uint64.MaxValue" : value.ToString(CultureInfo.InvariantCulture);
}
