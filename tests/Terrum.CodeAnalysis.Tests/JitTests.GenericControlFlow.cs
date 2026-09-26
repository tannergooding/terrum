// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class JitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredGenericPowerSampleAgreesBetweenAotAndJitAsync(bool checkedFailures)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "static-protocol-power.terrum")).ConfigureAwait(true);
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
    [InlineData("Int32Value", "0", "0u32", "1")]
    [InlineData("Int32Value", "0", "7u32", "0")]
    [InlineData("Int32Value", "-2", "5u32", "-32")]
    [InlineData("Int32Value", "2147483647", "1u32", "2147483647")]
    [InlineData("Int64Value", "9223372036854775807i64", "1u32", "9223372036854775807i64")]
    [InlineData("Int64Value", "3i64", "20u32", "3486784401i64")]
    [InlineData("Int64Value", "1i64", "4294967295u32", "1i64")]
    public async Task StructuredGenericPowerPreservesArithmeticBoundariesAsync(
        string type, string value, string exponent, string expected)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "static-protocol-power.terrum")).ConfigureAwait(true);
        var entry = source.IndexOf("int32 Main()", StringComparison.Ordinal);
        Assert.True(entry >= 0);
        source = source[..entry] + $$"""
            int32 Main() {
                var result = Power<{{type}}>(new {{type}}({{value}}), new {{type}}(1), {{exponent}});
                if (result.Value != {{expected}}) { return 1; }
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
}
