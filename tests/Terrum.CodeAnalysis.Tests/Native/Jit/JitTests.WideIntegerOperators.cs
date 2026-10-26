// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [Test]
    public async Task WideIntegerCoreOperatorsExecuteAcrossAotAndJitAsync()
    {
        const string source = """
            int128 Add(int128 left, int128 right) { return left + right; }
            int128 Multiply(int128 left, int128 right) { return left * right; }
            int128 Divide(int128 left, int128 right) { return left / right; }
            int128 Remainder(int128 left, int128 right) { return left % right; }
            uint128 Subtract(uint128 left, uint128 right) { return left - right; }
            uint128 MultiplyUnsigned(uint128 left, uint128 right) { return left * right; }
            uint128 DivideUnsigned(uint128 left, uint128 right) { return left / right; }
            uint128 RemainderUnsigned(uint128 left, uint128 right) { return left % right; }
            int128 Negate(int128 value) { return -value; }
            int128 Complement(int128 value) { return ~value; }
            uint128 ComplementUnsigned(uint128 value) { return ~value; }
            uint128 And(uint128 left, uint128 right) { return left & right; }
            uint128 Or(uint128 left, uint128 right) { return left | right; }
            uint128 Xor(uint128 left, uint128 right) { return left ^ right; }
            uint128 ShiftLeft(uint128 value, int8 count) { return value << count; }
            uint128 ShiftLeftWideCount(uint128 value, uint128 count) { return value << count; }
            int128 ShiftRight(int128 value, int8 count) { return value >> count; }
            int128 ShiftRightUnsigned(int128 value, int8 count) { return value >>> count; }
            bool SignedLess(int128 left, int128 right) { return left < right; }
            bool UnsignedGreater(uint128 left, uint128 right) { return left > right; }
            int32 Main() {
                int128 sum = Add(int128(40), int128(2));
                int128 signedProduct = Multiply(int128(-3), int128(7));
                int128 signedQuotient = Divide(int128(-11), int128(3));
                int128 signedRemainder = Remainder(int128(-11), int128(3));
                uint128 difference = Subtract(uint128(10), uint128(3));
                uint128 unsignedProduct = MultiplyUnsigned(uint128(3), uint128(7));
                uint128 unsignedQuotient = DivideUnsigned(
                    uint128(340282366920938463463374607431768211455), uint128(3));
                uint128 unsignedRemainder = RemainderUnsigned(
                    uint128(340282366920938463463374607431768211455), uint128(3));
                int128 negated = Negate(int128(7));
                int128 complemented = Complement(int128(0));
                uint128 unsignedComplement = ComplementUnsigned(uint128(0));
                uint128 andResult = And(
                    uint128(340282366920938463463374607431768211455), uint128(18446744073709551621));
                uint128 orResult = Or(uint128(1),
                    uint128(170141183460469231731687303715884105728));
                uint128 xorResult = Xor(
                    uint128(340282366920938463463374607431768211455),
                    uint128(340282366920938463463374607431768211454));
                uint128 highBit = ShiftLeft(uint128(1), int8(127));
                uint128 highBitWideCount = ShiftLeftWideCount(uint128(1), uint128(127));
                int128 arithmetic = ShiftRight(int128(-7), int8(1));
                int128 logical = ShiftRightUnsigned(int128(-1), int8(1));
                mut int128 current = int128(10);
                int128 previous = current++;
                current += int128(2);
                mut int128 decrement = int128(10);
                int128 oldDecrement = decrement--;
                decrement -= int128(2);
                mut uint128 compound = uint128(10);
                compound *= uint128(3);
                compound /= uint128(2);
                compound %= uint128(7);
                mut uint128 bitwise = uint128(340282366920938463463374607431768211455);
                bitwise &= uint128(3);
                bitwise |= uint128(4);
                bitwise ^= uint128(1);
                mut uint128 compoundShift = uint128(1);
                compoundShift <<= int128(127);
                compoundShift >>>= int8(127);
                if (sum == int128(42) && signedProduct == int128(-21)
                    && difference == uint128(7) && unsignedProduct == uint128(21)
                    && signedQuotient == int128(-3) && signedRemainder == int128(-2)
                    && unsignedQuotient == uint128(113427455640312821154458202477256070485)
                    && unsignedRemainder == uint128(0)
                    && negated == int128(-7) && complemented == int128(-1)
                    && unsignedComplement == uint128(340282366920938463463374607431768211455)
                    && andResult == uint128(18446744073709551621)
                    && orResult == uint128(170141183460469231731687303715884105729)
                    && xorResult == uint128(1)
                    && highBit == uint128(170141183460469231731687303715884105728)
                    && highBitWideCount == uint128(170141183460469231731687303715884105728)
                    && arithmetic == int128(-4)
                    && logical == int128(170141183460469231731687303715884105727)
                    && previous == int128(10) && current == int128(13)
                    && oldDecrement == int128(10) && decrement == int128(7)
                    && compound == uint128(1) && bitwise == uint128(6)
                    && compoundShift == uint128(1)
                    && SignedLess(int128(-1), int128(0))
                    && UnsignedGreater(uint128(340282366920938463463374607431768211455), uint128(1))) {
                    return 42;
                }
                return 0;
            }
            """;

        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Test]
    public async Task CheckedWideIntegerFailuresPreserveValuesAndValidateCountsAsync()
    {
        const string source = """
            int128 Multiply(int128 left, int128 right) { return left * right; }
            uint128 MultiplyUnsigned(uint128 left, uint128 right) { return left * right; }
            int128 Divide(int128 left, int128 right) { return left / right; }
            int128 Remainder(int128 left, int128 right) { return left % right; }
            uint128 DivideUnsigned(uint128 left, uint128 right) { return left / right; }
            uint128 ShiftLeft(uint128 value, int16 count) { return value << count; }
            uint128 ShiftLeftWideCount(uint128 value, uint128 count) { return value << count; }
            int32 Main() {
                mut int128 value = int128(170141183460469231731687303715884105727);
                mut int32 failures = 0;
                int128 minimumRemainder = Remainder(
                    int128(-170141183460469231731687303715884105728), int128(-1));
                unsafe {
                    try {
                        value++;
                    } catch {
                        if (value == int128(170141183460469231731687303715884105727)) {
                            failures += 1;
                        }
                    }
                    try {
                        value = -int128(-170141183460469231731687303715884105728);
                    } catch {
                        if (value == int128(170141183460469231731687303715884105727)) {
                            failures += 1;
                        }
                    }
                    try {
                        uint128 invalid = ShiftLeft(uint128(1), int16(128));
                    } catch {
                        failures += 1;
                    }
                    try {
                        uint128 invalid = ShiftLeftWideCount(uint128(1), uint128(128));
                    } catch {
                        failures += 1;
                    }
                    try {
                        int128 overflow = Multiply(
                            int128(170141183460469231731687303715884105727), int128(2));
                    } catch {
                        failures += 1;
                    }
                    try {
                        uint128 overflow = MultiplyUnsigned(
                            uint128(340282366920938463463374607431768211455), uint128(2));
                    } catch {
                        failures += 1;
                    }
                    try {
                        int128 overflow = Multiply(
                            int128(-170141183460469231731687303715884105728), int128(-1));
                    } catch {
                        failures += 1;
                    }
                    try {
                        int128 overflow = Divide(
                            int128(-170141183460469231731687303715884105728), int128(-1));
                    } catch {
                        failures += 1;
                    }
                    try {
                        uint128 invalid = DivideUnsigned(uint128(1), uint128(0));
                    } catch {
                        failures += 1;
                    }
                    try {
                        value /= int128(0);
                    } catch {
                        if (value == int128(170141183460469231731687303715884105727)) {
                            failures += 1;
                        }
                    }
                }
                return failures == 10 && minimumRemainder == int128(0) ? 42 : 0;
            }
            """;

        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [TestCase("""
        int128 Multiply(int128 left, int128 right) { return left * right; }
        int32 Main() {
            int128 overflow = Multiply(
                int128(170141183460469231731687303715884105727), int128(2));
            return 42;
        }
        """, unchecked((int)0xE0540001))]
    [TestCase("""
        int128 Divide(int128 left, int128 right) { return left / right; }
        int32 Main() {
            int128 overflow = Divide(
                int128(-170141183460469231731687303715884105728), int128(-1));
            return 42;
        }
        """, unchecked((int)0xE0540001))]
    [TestCase("""
        uint128 Divide(uint128 left, uint128 right) { return left / right; }
        int32 Main() { uint128 invalid = Divide(uint128(1), uint128(0)); return 42; }
        """, unchecked((int)0xE0540002))]
    [TestCase("""
        uint128 Shift(uint128 value, int16 count) { return value << count; }
        int32 Main() { uint128 invalid = Shift(uint128(1), int16(128)); return 42; }
        """, unchecked((int)0xE0540003))]
    public Task FailFastWideIntegerFailuresUseExactRuntimeStatusesAsync(string source, int expected) =>
        AssertFailFastExecutionAsync(source, expected);

    [Test]
    public async Task WideIntegerDivisionAndRemainderMatchExactReferenceAsync()
    {
        var random = new Random(0x128_64);
        var signedMinimum = -(BigInteger.One << 127);
        var signedMaximum = (BigInteger.One << 127) - BigInteger.One;
        var unsignedMaximum = (BigInteger.One << 128) - BigInteger.One;
        var signedCases = new List<(BigInteger Dividend, BigInteger Divisor)> {
            (-11, 3),
            (11, -3),
            (-11, -3),
            (signedMinimum, 1),
            (signedMaximum, -7),
        };
        for (var index = 0; index < 6; index++)
        {
            signedCases.Add((RandomSignedInteger128(random), Nonzero(RandomSignedInteger128(random))));
        }

        var unsignedCases = new List<(BigInteger Dividend, BigInteger Divisor)> {
            (unsignedMaximum, 3),
            (BigInteger.One << 127, 7),
            (123456789, 123),
            (unsignedMaximum, unsignedMaximum - 1),
        };
        for (var index = 0; index < 6; index++)
        {
            unsignedCases.Add((RandomUnsignedInteger128(random), Nonzero(RandomUnsignedInteger128(random))));
        }

        var source = new StringBuilder("""
            int128 SignedQuotient(int128 dividend, int128 divisor) { return dividend / divisor; }
            int128 SignedRemainder(int128 dividend, int128 divisor) { return dividend % divisor; }
            uint128 UnsignedQuotient(uint128 dividend, uint128 divisor) { return dividend / divisor; }
            uint128 UnsignedRemainder(uint128 dividend, uint128 divisor) { return dividend % divisor; }
            int32 Main() {
            """);
        foreach (var (dividend, divisor) in signedCases)
        {
            AppendDivisionCheck(source, "int128", "SignedQuotient", "SignedRemainder",
                dividend, divisor, signed: true);
        }
        foreach (var (dividend, divisor) in unsignedCases)
        {
            AppendDivisionCheck(source, "uint128", "UnsignedQuotient", "UnsignedRemainder",
                dividend, divisor, signed: false);
        }
        source.AppendLine("return 42;");
        source.AppendLine("}");

        await AssertFailFastExecutionAsync(source.ToString(), 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source.ToString(), 42).ConfigureAwait(true);
    }

    [TestCase(BoundRuntimePolicy.FailFast, NativeRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions, NativeRuntimePolicy.CheckedExceptions)]
    public async Task ReleaseWideIntegerMultiplicationWrapsAcrossAotAndJitAsync(
        BoundRuntimePolicy runtimePolicy, NativeRuntimePolicy nativePolicy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var random = new Random(0x128_65);
        var signedMask = (BigInteger.One << 128) - BigInteger.One;
        var signedMinimum = -(BigInteger.One << 127);
        var signedMaximum = (BigInteger.One << 127) - BigInteger.One;
        var unsignedMaximum = signedMask;
        var source = new StringBuilder("""
            int128 SignedProduct(int128 left, int128 right) { return left * right; }
            uint128 UnsignedProduct(uint128 left, uint128 right) { return left * right; }
            int32 Main() {
            """);
        AppendMultiplicationCheck(source, "int128", "SignedProduct",
            signedMaximum, 2, SignedProduct(signedMaximum, 2));
        AppendMultiplicationCheck(source, "int128", "SignedProduct",
            signedMinimum, -1, signedMinimum);
        AppendMultiplicationCheck(source, "uint128", "UnsignedProduct",
            unsignedMaximum, 2, unsignedMaximum - 1);
        AppendMultiplicationCheck(source, "uint128", "UnsignedProduct",
            BigInteger.Parse("18446744073709551619", CultureInfo.InvariantCulture),
            BigInteger.Parse("18446744073709551621", CultureInfo.InvariantCulture),
            BigInteger.Parse("147573952589676412943", CultureInfo.InvariantCulture));

        for (var index = 0; index < 6; index++)
        {
            var left = RandomSignedInteger128(random);
            var right = RandomSignedInteger128(random);
            AppendMultiplicationCheck(source, "int128", "SignedProduct", left, right,
                SignedProduct(left, right));
            var unsignedLeft = RandomUnsignedInteger128(random);
            var unsignedRight = RandomUnsignedInteger128(random);
            AppendMultiplicationCheck(source, "uint128", "UnsignedProduct",
                unsignedLeft, unsignedRight, unsignedLeft * unsignedRight & signedMask);
        }
        source.AppendLine("return 42;");
        source.AppendLine("}");

        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()), new CompilationOptions {
            SourceArithmeticProfile = SourceArithmeticProfile.Release,
            RuntimePolicy = runtimePolicy,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.Execute(program, nativePolicy), Is.EqualTo(42));

        var executable = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"wide-int128-wrap-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(executable, NativeEmitter.Emit(program, nativePolicy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start the native worker.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                await process.WaitForExitAsync().ConfigureAwait(true);
                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [TestCase(BoundRuntimePolicy.FailFast, NativeRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions, NativeRuntimePolicy.CheckedExceptions)]
    public async Task ReleaseWideIntegerArithmeticWrapsAcrossAotAndJitAsync(
        BoundRuntimePolicy runtimePolicy, NativeRuntimePolicy nativePolicy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var signedMinimum = -(BigInteger.One << 127);
        var signedMaximum = (BigInteger.One << 127) - BigInteger.One;
        var unsignedMaximum = (BigInteger.One << 128) - BigInteger.One;
        var source = new StringBuilder("""
            int128 AddSigned(int128 left, int128 right) { return left + right; }
            int128 SubtractSigned(int128 left, int128 right) { return left - right; }
            uint128 AddUnsigned(uint128 left, uint128 right) { return left + right; }
            uint128 SubtractUnsigned(uint128 left, uint128 right) { return left - right; }
            int32 Main() {
            """);
        AppendArithmeticCheck(source, "AddSigned", "int128", signedMaximum, 1, signedMinimum);
        AppendArithmeticCheck(source, "SubtractSigned", "int128", signedMinimum, 1, signedMaximum);
        AppendArithmeticCheck(source, "AddUnsigned", "uint128", unsignedMaximum, 1, 0);
        AppendArithmeticCheck(source, "SubtractUnsigned", "uint128", 0, 1, unsignedMaximum);

        var random = new Random(0x128_66);
        var mask = (BigInteger.One << 128) - BigInteger.One;
        for (var index = 0; index < 6; index++)
        {
            var left = RandomSignedInteger128(random);
            var right = RandomSignedInteger128(random);
            AppendArithmeticCheck(source, "AddSigned", "int128", left, right,
                SignedProduct(left + right, BigInteger.One));
            AppendArithmeticCheck(source, "SubtractSigned", "int128", left, right,
                SignedProduct(left - right, BigInteger.One));
            var unsignedLeft = RandomUnsignedInteger128(random);
            var unsignedRight = RandomUnsignedInteger128(random);
            AppendArithmeticCheck(source, "AddUnsigned", "uint128", unsignedLeft, unsignedRight,
                (unsignedLeft + unsignedRight) & mask);
            AppendArithmeticCheck(source, "SubtractUnsigned", "uint128", unsignedLeft, unsignedRight,
                (unsignedLeft - unsignedRight) & mask);
        }
        source.AppendLine("return 42;");
        source.AppendLine("}");

        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()), new CompilationOptions {
            SourceArithmeticProfile = SourceArithmeticProfile.Release,
            RuntimePolicy = runtimePolicy,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.Execute(program, nativePolicy), Is.EqualTo(42));

        var executable = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"wide-int128-arithmetic-wrap-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(executable, NativeEmitter.Emit(program, nativePolicy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start the native worker.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                await process.WaitForExitAsync().ConfigureAwait(true);
                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    private static BigInteger RandomSignedInteger128(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        return new BigInteger(bytes, isUnsigned: false, isBigEndian: false);
    }

    private static BigInteger RandomUnsignedInteger128(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private static BigInteger Nonzero(BigInteger value) => value.IsZero ? BigInteger.One : value;

    private static BigInteger SignedProduct(BigInteger left, BigInteger right)
    {
        var raw = left * right & ((BigInteger.One << 128) - BigInteger.One);
        return raw >= (BigInteger.One << 127) ? raw - (BigInteger.One << 128) : raw;
    }

    private static void AppendMultiplicationCheck(StringBuilder source, string type,
        string function, BigInteger left, BigInteger right, BigInteger expected)
    {
        var leftText = left.ToString(CultureInfo.InvariantCulture);
        var rightText = right.ToString(CultureInfo.InvariantCulture);
        var expectedText = expected.ToString(CultureInfo.InvariantCulture);
        source.Append("if (").Append(function).Append('(').Append(type).Append('(')
            .Append(leftText).Append("), ").Append(type).Append('(').Append(rightText).Append(")) != ")
            .Append(type).Append('(').Append(expectedText).AppendLine(")) { return 1; }");
    }

    private static void AppendArithmeticCheck(StringBuilder source, string function, string type,
        BigInteger left, BigInteger right, BigInteger expected)
    {
        var leftText = left.ToString(CultureInfo.InvariantCulture);
        var rightText = right.ToString(CultureInfo.InvariantCulture);
        var expectedText = expected.ToString(CultureInfo.InvariantCulture);
        source.Append("if (").Append(function).Append('(').Append(type).Append('(')
            .Append(leftText).Append("), ").Append(type).Append('(').Append(rightText).Append(")) != ")
            .Append(type).Append('(').Append(expectedText).AppendLine(")) { return 1; }");
    }

    private static void AppendDivisionCheck(StringBuilder source, string type, string quotientFunction,
        string remainderFunction, BigInteger dividend, BigInteger divisor, bool signed)
    {
        var dividendText = dividend.ToString(CultureInfo.InvariantCulture);
        var divisorText = divisor.ToString(CultureInfo.InvariantCulture);
        var quotient = dividend / divisor;
        var remainder = dividend % divisor;
        var quotientText = quotient.ToString(CultureInfo.InvariantCulture);
        var remainderText = remainder.ToString(CultureInfo.InvariantCulture);
        source.Append("if (").Append(quotientFunction).Append('(').Append(type).Append('(')
            .Append(dividendText).Append("), ").Append(type).Append('(').Append(divisorText).Append(")) != ")
            .Append(type).Append('(').Append(quotientText).Append(") || ")
            .Append(remainderFunction).Append('(').Append(type).Append('(').Append(dividendText)
            .Append("), ").Append(type).Append('(').Append(divisorText).Append(")) != ")
            .Append(type).Append('(').Append(remainderText).Append(")) { return ")
            .Append(signed ? "1" : "2").AppendLine("; }");
    }
}
