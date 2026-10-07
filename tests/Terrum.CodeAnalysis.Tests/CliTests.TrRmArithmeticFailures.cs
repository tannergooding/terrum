// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("Signed32One() / Signed32Zero() == Signed32Zero()", unchecked((int)0xE0540002))]
    [InlineData("Signed32One() % Signed32Zero() == Signed32Zero()", unchecked((int)0xE0540002))]
    [InlineData("Unsigned32One() / Unsigned32Zero() == Unsigned32Zero()", unchecked((int)0xE0540002))]
    [InlineData("Unsigned32One() % Unsigned32Zero() == Unsigned32Zero()", unchecked((int)0xE0540002))]
    [InlineData("Signed64One() / Signed64Zero() == Signed64Zero()", unchecked((int)0xE0540002))]
    [InlineData("Signed64One() % Signed64Zero() == Signed64Zero()", unchecked((int)0xE0540002))]
    [InlineData("Unsigned64One() / Unsigned64Zero() == Unsigned64Zero()", unchecked((int)0xE0540002))]
    [InlineData("Unsigned64One() % Unsigned64Zero() == Unsigned64Zero()", unchecked((int)0xE0540002))]
    [InlineData("Signed32Minimum() / Signed32NegativeOne() == Signed32Zero()", unchecked((int)0xE0540001))]
    [InlineData("Signed64Minimum() / Signed64NegativeOne() == Signed64Zero()", unchecked((int)0xE0540001))]
    public async Task PersistedInitOutputIntegerDivisionPreservesFailureStatusesAsync(
        string expression, int expected)
    {
        var source = $$"""
            void Fill(int32 value, init ref<int32> result) { result = value; }
            int32 Observe(bool value) { return value ? 42 : 0; }
            int32 Signed32One() { return 1; }
            int32 Signed32Zero() { return 0; }
            int32 Signed32Minimum() { return int32.MinValue; }
            int32 Signed32NegativeOne() { return -1; }
            int64 Signed64One() { return int64(1); }
            int64 Signed64Zero() { return int64(0); }
            int64 Signed64Minimum() { return int64.MinValue; }
            int64 Signed64NegativeOne() { return int64(-1); }
            uint32 Unsigned32One() { return uint32(1); }
            uint32 Unsigned32Zero() { return uint32(0); }
            uint64 Unsigned64One() { return uint64(1); }
            uint64 Unsigned64Zero() { return uint64(0); }
            int32 Main() {
                init int32 result;
                Fill(Observe({{expression}}), init result);
                return result;
            }
            """;
        var artifact = Path.Combine(Directory.GetCurrentDirectory(),
            $"trrm-init-arithmetic-failure-{Guid.NewGuid():N}.trrm");
        try
        {
            var (compiled, compileOutput, compileError) = await RunAsync(
                Encoding.UTF8.GetBytes(source), "compile-trrm", "-", "--identity",
                "init-output-arithmetic-failure", "-o", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.True(compiled == 0, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.Empty(compileError);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.Equal(expected, ran);
            Assert.Empty(runOutput);
            Assert.Empty(runError);
        }
        finally
        {
            File.Delete(artifact);
        }
    }
}
