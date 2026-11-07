// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

internal sealed partial class JitTests
{
    [Test]
    public async Task Float128ArithmeticMatchesExactOracleAcrossRandomOperandsAsync()
    {
        var random = new Random(0x128113);
        var operations = new (TokenKind Operator, string Name)[] {
            (TokenKind.Plus, "Add"),
            (TokenKind.Minus, "Subtract"),
            (TokenKind.Star, "Multiply"),
            (TokenKind.Slash, "Divide"),
            (TokenKind.Percent, "Remainder"),
        };
        var checks = new StringBuilder();
        var checkCount = 0;
        foreach (var (operation, name) in operations)
        {
            for (var index = 0; index < 3; index++)
            {
                var left = RandomFiniteFloat128(random);
                var right = RandomFiniteFloat128(random);
                Assert.That(FloatingLiteralParser.TryFoldBinary(
                    operation, PrimitiveType.Float128, left, right, out var expected), Is.True);
                Assert.That((expected.High >> 48) & 0x7FFF, Is.Not.EqualTo(0x7FFF));
                checkCount++;
                _ = checks.Append("if (").Append(name).Append('(')
                    .Append(Float128Literal(left)).Append(", ")
                    .Append(Float128Literal(right)).Append(") != ")
                    .Append(Float128Literal(expected)).Append(") { return ")
                    .Append(checkCount).AppendLine("; }");
            }
        }

        var source = $$"""
            float128 Add(float128 left, float128 right) { return left + right; }
            float128 Subtract(float128 left, float128 right) { return left - right; }
            float128 Multiply(float128 left, float128 right) { return left * right; }
            float128 Divide(float128 left, float128 right) { return left / right; }
            float128 Remainder(float128 left, float128 right) { return left % right; }
            int32 Main() {
            {{checks}}
                return 42;
            }
            """;

        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Test]
    public async Task Float128OperatorsExecuteAcrossAotAndJitUnderBothRuntimePoliciesAsync()
    {
        const string source = """
            float128 Identity(float128 value) { return value; }
            float128 Add(float128 left, float128 right) { return left + right; }
            float128 Subtract(float128 left, float128 right) { return left - right; }
            float128 Multiply(float128 left, float128 right) { return left * right; }
            float128 Divide(float128 left, float128 right) { return left / right; }
            float128 Remainder(float128 left, float128 right) { return left % right; }
            bool Equal(float128 left, float128 right) { return left == right; }
            bool NotEqual(float128 left, float128 right) { return left != right; }
            bool Less(float128 left, float128 right) { return left < right; }
            bool LessOrEqual(float128 left, float128 right) { return left <= right; }
            bool Greater(float128 left, float128 right) { return left > right; }
            bool GreaterOrEqual(float128 left, float128 right) { return left >= right; }
            float128 PowerOfTwo(int32 exponent) {
                mut float128 value = float128(1);
                for (mut int32 index = 0; index < exponent; index++) {
                    value *= float128(2);
                }
                return value;
            }
            int32 Main() {
                float128 one = Identity(float128(1));
                float128 two = Identity(float128(2));
                float128 positiveValue = +Identity(float128(-1.5));
                float128 negativeZero = -Identity(float128(0));
                float128 nan = Divide(float128(0), float128(0));
                float128 infinity = Divide(one, float128(0));
                float128 minimumSubnormal = float128(0x1p-16494);
                mut float128 update = float128(5.5);
                float128 previous = update++;
                float128 prefix = --update;
                update += float128(0.5);
                update -= float128(0.5);
                update *= two;
                update /= two;
                update %= float128(2);
                if (Add(Identity(float128(1.5)), Identity(float128(0.25)))
                    != float128(1.75)) { return 1; }
                if (Subtract(float128(1.5), float128(0.5)) != float128(1)) { return 2; }
                if (Multiply(float128(1.5), two) != float128(3)) { return 3; }
                if (Divide(one, float128(3)) != float128(0.3333333333333333333333333333333333)) {
                    return 4;
                }
                if (Add(float128(0x1p+112), float128(0x1p-1)) != float128(0x1p+112)) {
                    return 5;
                }
                if (Add(Add(float128(0x1p+112), one), float128(0x1p-1))
                    != Add(float128(0x1p+112), float128(2))) { return 6; }
                if (Add(PowerOfTwo(1000), one) != PowerOfTwo(1000)) { return 7; }
                if (Remainder(float128(0x1p+256), float128(3)) != one) { return 8; }
                if (Remainder(float128(-5.5), two) != float128(-1.5)) { return 9; }
                if (Remainder(float128(0x1p+1000), float128(0x1p-1000)) != float128(0)) {
                    return 10;
                }
                if (Add(minimumSubnormal, minimumSubnormal) != float128(0x1p-16493)) {
                    return 18;
                }
                if (-Identity(float128(1.5)) != float128(-1.5)
                    || positiveValue != float128(-1.5)) { return 19; }
                if (Multiply(float128(0x1p+16383), two) != infinity) { return 11; }
                if (Divide(float128(0x1p-16494), two) != float128(0)) { return 12; }
                if (Add(infinity, -infinity) == Add(infinity, -infinity)) { return 13; }
                if (nan == nan || !NotEqual(nan, nan) || Less(nan, one)
                    || LessOrEqual(nan, one) || Greater(nan, one)
                    || GreaterOrEqual(nan, one)) { return 14; }
                if (!Equal(negativeZero, float128(0))
                    || !(Divide(one, negativeZero) < float128(0))) { return 15; }
                if (!Equal(float128(-2), float128(-2))
                    || !NotEqual(float128(-2), one)
                    || !Less(float128(-2), float128(-1))
                    || !LessOrEqual(float128(-1), float128(-1))
                    || !Greater(float128(2), one)
                    || !GreaterOrEqual(one, one)) { return 16; }
                if (previous != float128(5.5) || prefix != float128(5.5)
                    || update != float128(1.5)) { return 17; }
                return 42;
            }
            """;

        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    private static ScalarBits RandomFiniteFloat128(Random random)
    {
        var bytes = new byte[2 * sizeof(ulong)];
        random.NextBytes(bytes);
        var low = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        var high = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(sizeof(ulong)));
        var exponent = (ulong)random.Next(0x3F00, 0x4100);
        high = (high & 0x8000_FFFF_FFFF_FFFF) | (exponent << 48);
        return new ScalarBits(low, high);
    }

    private static string Float128Literal(ScalarBits bits)
    {
        var negative = (bits.High & 0x8000_0000_0000_0000) != 0;
        var encodedExponent = (int)((bits.High >> 48) & 0x7FFF);
        var fraction = $"{bits.High & 0x0000_FFFF_FFFF_FFFF:X12}{bits.Low:X16}";
        if (encodedExponent == 0 && bits.Low == 0
            && (bits.High & 0x0000_FFFF_FFFF_FFFF) == 0)
        {
            return negative ? "float128(-0)" : "float128(0)";
        }
        var exponent = encodedExponent == 0 ? -16382 : encodedExponent - 16383;
        var integralDigit = encodedExponent == 0 ? "0" : "1";
        var exponentText = exponent >= 0 ? $"+{exponent}"
            : exponent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"float128({(negative ? "-" : "")}0x{integralDigit}.{fraction}p{exponentText})";
    }
}
