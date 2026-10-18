// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task StructuredGenericPowerSampleAgreesBetweenAotAndJitAsync(bool checkedFailures)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(),
            "samples", "LanguageFeatures", "StaticProtocols", "static-protocol-power.terrum")).ConfigureAwait(true);
        if (checkedFailures)
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }
    [TestCase("Int32Value","0","0","1")]
    [TestCase("Int32Value","0","7","0")]
    [TestCase("Int32Value","-2","5","-32")]
    [TestCase("Int32Value","2147483647","1","2147483647")]
    [TestCase("Int64Value","9223372036854775807","1","9223372036854775807")]
    [TestCase("Int64Value","3","20","3486784401")]
    [TestCase("Int64Value","1","4294967295","1")]
    public async Task StructuredGenericPowerPreservesArithmeticBoundariesAsync(
        string type, string value, string exponent, string expected)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(),
            "samples", "LanguageFeatures", "StaticProtocols", "static-protocol-power.terrum")).ConfigureAwait(true);
        var entry = source.IndexOf("int32 Main()", StringComparison.Ordinal);
        Assert.That(entry >= 0, Is.True);
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
