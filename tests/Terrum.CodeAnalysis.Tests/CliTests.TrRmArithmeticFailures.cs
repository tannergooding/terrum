// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("Signed32One() / Signed32Zero() == Signed32Zero()",unchecked((int)0xE0540002))]
    [TestCase("Signed32One() % Signed32Zero() == Signed32Zero()",unchecked((int)0xE0540002))]
    [TestCase("Unsigned32One() / Unsigned32Zero() == Unsigned32Zero()",unchecked((int)0xE0540002))]
    [TestCase("Unsigned32One() % Unsigned32Zero() == Unsigned32Zero()",unchecked((int)0xE0540002))]
    [TestCase("Signed64One() / Signed64Zero() == Signed64Zero()",unchecked((int)0xE0540002))]
    [TestCase("Signed64One() % Signed64Zero() == Signed64Zero()",unchecked((int)0xE0540002))]
    [TestCase("Unsigned64One() / Unsigned64Zero() == Unsigned64Zero()",unchecked((int)0xE0540002))]
    [TestCase("Unsigned64One() % Unsigned64Zero() == Unsigned64Zero()",unchecked((int)0xE0540002))]
    [TestCase("Signed32Minimum() / Signed32NegativeOne() == Signed32Zero()",unchecked((int)0xE0540001))]
    [TestCase("Signed64Minimum() / Signed64NegativeOne() == Signed64Zero()",unchecked((int)0xE0540001))]
    [TestCase("Signed32Maximum() + Signed32One() == Signed32Zero()",unchecked((int)0xE0540001))]
    [TestCase("Unsigned32Maximum() + Unsigned32One() == Unsigned32Zero()",unchecked((int)0xE0540001))]
    [TestCase("Signed64Maximum() + Signed64One() == Signed64Zero()",unchecked((int)0xE0540001))]
    [TestCase("Unsigned64Maximum() + Unsigned64One() == Unsigned64Zero()",unchecked((int)0xE0540001))]
    [TestCase("Signed32Minimum() - Signed32One() == Signed32Zero()",unchecked((int)0xE0540001))]
    [TestCase("Unsigned32Zero() - Unsigned32One() == Unsigned32Zero()",unchecked((int)0xE0540001))]
    [TestCase("Signed64Minimum() - Signed64One() == Signed64Zero()",unchecked((int)0xE0540001))]
    [TestCase("Unsigned64Zero() - Unsigned64One() == Unsigned64Zero()",unchecked((int)0xE0540001))]
    [TestCase("Signed32One() << Signed32NegativeOne() == Signed32Zero()",unchecked((int)0xE0540001))]
    [TestCase("Unsigned32One() << Signed32ThirtyTwo() == Unsigned32Zero()",unchecked((int)0xE0540001))]
    [TestCase("Signed32One() >> Signed32ThirtyTwo() == Signed32Zero()",unchecked((int)0xE0540001))]
    [TestCase("Signed64One() >> Signed32SixtyFour() == Signed64Zero()",unchecked((int)0xE0540001))]
    [TestCase("Unsigned64One() >>> Signed32SixtyFour() == Unsigned64Zero()",unchecked((int)0xE0540001))]
    [TestCase("Unsigned64One() >>> Signed32NegativeOne() == Unsigned64Zero()",unchecked((int)0xE0540001))]
    [TestCase("Signed32Maximum() * Signed32Two() == Signed32Zero()",unchecked((int)0xE0540001))]
    [TestCase("Unsigned32Maximum() * Unsigned32Two() == Unsigned32Zero()",unchecked((int)0xE0540001))]
    [TestCase("Signed64Maximum() * Signed64Two() == Signed64Zero()",unchecked((int)0xE0540001))]
    [TestCase("Unsigned64Maximum() * Unsigned64Two() == Unsigned64Zero()",unchecked((int)0xE0540001))]
    public async Task PersistedInitOutputIntegerArithmeticPreservesFailureStatusesAsync(
        string expression, int expected)
    {
        var source = $$"""
            void Fill(int32 value, init ref<int32> result) { result = value; }
            int32 Observe(bool value) { return value ? 42 : 0; }
            int32 Signed32One() { return 1; }
            int32 Signed32Zero() { return 0; }
            int32 Signed32Minimum() { return int32.MinValue; }
            int32 Signed32Maximum() { return int32.MaxValue; }
            int32 Signed32NegativeOne() { return -1; }
            int64 Signed64One() { return int64(1); }
            int64 Signed64Zero() { return int64(0); }
            int64 Signed64Minimum() { return int64.MinValue; }
            int64 Signed64Maximum() { return int64.MaxValue; }
            int64 Signed64NegativeOne() { return int64(-1); }
            int32 Signed32Two() { return 2; }
            int32 Signed32ThirtyTwo() { return 32; }
            int32 Signed32SixtyFour() { return 64; }
            int64 Signed64Two() { return int64(2); }
            uint32 Unsigned32One() { return uint32(1); }
            uint32 Unsigned32Zero() { return uint32(0); }
            uint32 Unsigned32Maximum() { return uint32.MaxValue; }
            uint32 Unsigned32Two() { return uint32(2); }
            uint64 Unsigned64One() { return uint64(1); }
            uint64 Unsigned64Zero() { return uint64(0); }
            uint64 Unsigned64Maximum() { return uint64.MaxValue; }
            uint64 Unsigned64Two() { return uint64(2); }
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
            Assert.That(compiled == 0, Is.True, $"{compileOutput}{Environment.NewLine}{compileError}");
            Assert.That(compileError, Is.Empty);

            var (ran, runOutput, runError) = await RunAsync([], "run-trrm", artifact,
                "--runtime", "fail-fast", "--json");
            Assert.That(ran, Is.EqualTo(expected));
            Assert.That(runOutput, Is.Empty);
            Assert.That(runError, Is.Empty);
        }
        finally
        {
            File.Delete(artifact);
        }
    }
}
