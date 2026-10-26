// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Globalization;
using System.Numerics;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

internal static partial class FloatingLiteralParser
{
    private enum FloatingValueClass
    {
        Finite,
        Infinity,
        NaN,
    }

    private readonly record struct DecodedFloatingValue(
        FloatingValueClass Class, bool Negative, BigInteger Coefficient, long Exponent);

    internal static bool TryFoldBinary(TokenKind operation, PrimitiveType type,
        ScalarBits leftBits, ScalarBits rightBits, out ScalarBits result)
    {
        result = default;
        if (!PrimitiveTypeFacts.IsFloating(type)
            || !TryDecodeFloating(type, leftBits, out var left)
            || !TryDecodeFloating(type, rightBits, out var right))
        {
            return false;
        }

        if (operation is TokenKind.EqualsEquals or TokenKind.BangEquals
            or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals)
        {
            var comparison = CompareFloating(left, right, type, out var unordered);
            var value = operation switch {
                TokenKind.EqualsEquals => !unordered && comparison == 0,
                TokenKind.BangEquals => unordered || comparison != 0,
                TokenKind.Less => !unordered && comparison < 0,
                TokenKind.LessEquals => !unordered && comparison <= 0,
                TokenKind.Greater => !unordered && comparison > 0,
                TokenKind.GreaterEquals => !unordered && comparison >= 0,
                _ => false,
            };
            result = ScalarBits.FromLegacyValue(value ? 1 : 0, PrimitiveType.Bool);
            return true;
        }

        return operation switch {
            TokenKind.Plus or TokenKind.Minus => TryAddOrSubtract(
                operation, type, left, right, out result),
            TokenKind.Star => TryMultiply(type, left, right, out result),
            TokenKind.Slash => TryDivide(type, left, right, out result),
            TokenKind.Percent => TryRemainder(type, leftBits, rightBits, left, right, out result),
            _ => false,
        };
    }

    private static bool TryDecodeFloating(
        PrimitiveType type, ScalarBits bits, out DecodedFloatingValue value)
    {
        if (TryGetBinaryFormat(type, out var totalBits, out var fractionBits,
            out var minimumExponent, out _, out var exponentBits, out var bias))
        {
            var raw = (new BigInteger(bits.High) << 64) | bits.Low;
            var negative = ((raw >> (totalBits - 1)) & BigInteger.One) != 0;
            var exponentMask = (BigInteger.One << exponentBits) - BigInteger.One;
            var exponentField = (int)((raw >> fractionBits) & exponentMask);
            var fractionMask = (BigInteger.One << fractionBits) - BigInteger.One;
            var fraction = raw & fractionMask;
            if (exponentField == (int)exponentMask)
            {
                value = new DecodedFloatingValue(
                    fraction.IsZero ? FloatingValueClass.Infinity : FloatingValueClass.NaN,
                    negative, BigInteger.Zero, 0);
                return true;
            }
            if (exponentField == 0)
            {
                value = new DecodedFloatingValue(FloatingValueClass.Finite,
                    negative, fraction, minimumExponent - fractionBits);
                return true;
            }

            value = new DecodedFloatingValue(FloatingValueClass.Finite, negative,
                (BigInteger.One << fractionBits) | fraction,
                exponentField - bias - fractionBits);
            return true;
        }

        if (!TryGetDecimalFormat(type, out var decimalWidth, out var precision,
            out var coefficientBits, out var decimalExponentBits, out var decimalBias,
            out _, out _, out _))
        {
            value = default;
            return false;
        }

        var decimalRaw = (new BigInteger(bits.High) << 64) | bits.Low;
        var decimalNegative = ((decimalRaw >> (decimalWidth - 1)) & BigInteger.One) != 0;
        var special = (int)((decimalRaw >> (decimalWidth - 6)) & 0b1_1111);
        if (special is 0b1_1110 or 0b1_1111)
        {
            value = new DecodedFloatingValue(
                special == 0b1_1110 ? FloatingValueClass.Infinity : FloatingValueClass.NaN,
                decimalNegative, BigInteger.Zero, 0);
            return true;
        }

        var exponentMaskDecimal = (BigInteger.One << decimalExponentBits) - BigInteger.One;
        BigInteger coefficient;
        BigInteger exponentFieldDecimal;
        var steering = ((decimalRaw >> (decimalWidth - 3)) & 0b11) == 0b11;
        if (steering)
        {
            coefficient = (BigInteger.One << coefficientBits)
                | (decimalRaw & ((BigInteger.One << (coefficientBits - 2)) - BigInteger.One));
            exponentFieldDecimal = (decimalRaw >> (coefficientBits - 2)) & exponentMaskDecimal;
        }
        else
        {
            coefficient = decimalRaw & ((BigInteger.One << coefficientBits) - BigInteger.One);
            exponentFieldDecimal = (decimalRaw >> coefficientBits) & exponentMaskDecimal;
        }
        if (coefficient >= PowerOfTen(precision))
        {
            coefficient = BigInteger.Zero;
        }
        value = new DecodedFloatingValue(FloatingValueClass.Finite, decimalNegative,
            coefficient, (long)exponentFieldDecimal - decimalBias);
        return true;
    }

    private static int CompareFloating(DecodedFloatingValue left, DecodedFloatingValue right,
        PrimitiveType type, out bool unordered)
    {
        unordered = left.Class == FloatingValueClass.NaN || right.Class == FloatingValueClass.NaN;
        if (unordered)
        {
            return 0;
        }

        if (left.Class == FloatingValueClass.Infinity || right.Class == FloatingValueClass.Infinity)
        {
            if (left.Class == right.Class)
            {
                return left.Negative == right.Negative ? 0 : left.Negative ? -1 : 1;
            }
            if (left.Class == FloatingValueClass.Infinity)
            {
                return left.Negative ? -1 : 1;
            }
            return right.Negative ? 1 : -1;
        }

        if (left.Coefficient.IsZero && right.Coefficient.IsZero)
        {
            return 0;
        }
        if (left.Negative != right.Negative)
        {
            return left.Negative ? -1 : 1;
        }

        var magnitude = IsDecimal(type)
            ? CompareDecimalMagnitude(left, right)
            : CompareBinaryMagnitude(left, right);
        return left.Negative ? -magnitude : magnitude;
    }

    private static int CompareBinaryMagnitude(DecodedFloatingValue left, DecodedFloatingValue right)
    {
        var exponent = Math.Min(left.Exponent, right.Exponent);
        var leftValue = left.Coefficient << checked((int)(left.Exponent - exponent));
        var rightValue = right.Coefficient << checked((int)(right.Exponent - exponent));
        return leftValue.CompareTo(rightValue);
    }

    private static int CompareDecimalMagnitude(DecodedFloatingValue left, DecodedFloatingValue right)
    {
        var exponent = Math.Min(left.Exponent, right.Exponent);
        var leftValue = left.Coefficient * PowerOfTen(left.Exponent - exponent);
        var rightValue = right.Coefficient * PowerOfTen(right.Exponent - exponent);
        return leftValue.CompareTo(rightValue);
    }

    private static bool TryAddOrSubtract(TokenKind operation, PrimitiveType type,
        DecodedFloatingValue left, DecodedFloatingValue right, out ScalarBits bits)
    {
        bits = default;
        if (left.Class == FloatingValueClass.NaN || right.Class == FloatingValueClass.NaN)
        {
            return TryEncodeQuietNaN(type, out bits);
        }

        var rightNegative = right.Negative ^ (operation == TokenKind.Minus);
        if (left.Class == FloatingValueClass.Infinity || right.Class == FloatingValueClass.Infinity)
        {
            if (left.Class == FloatingValueClass.Infinity
                && right.Class == FloatingValueClass.Infinity
                && left.Negative != rightNegative)
            {
                return TryEncodeQuietNaN(type, out bits);
            }
            var negative = left.Class == FloatingValueClass.Infinity ? left.Negative : rightNegative;
            return TryEncodeInfinity(negative, type, out bits);
        }

        if (IsDecimal(type))
        {
            var exponent = Math.Min(left.Exponent, right.Exponent);
            var leftCoefficient = left.Coefficient * PowerOfTen(left.Exponent - exponent);
            var rightCoefficient = right.Coefficient * PowerOfTen(right.Exponent - exponent);
            if (left.Negative)
            {
                leftCoefficient = -leftCoefficient;
            }
            if (rightNegative)
            {
                rightCoefficient = -rightCoefficient;
            }
            var sum = leftCoefficient + rightCoefficient;
            var negativeResult = sum.Sign < 0
                || (sum.IsZero && left.Coefficient.IsZero && right.Coefficient.IsZero
                    && left.Negative && rightNegative);
            return TryEncodeDecimal(BigInteger.Abs(sum), exponent, exponent,
                negativeResult, type, out bits);
        }

        var binaryExponent = Math.Min(left.Exponent, right.Exponent);
        var leftValue = left.Coefficient << checked((int)(left.Exponent - binaryExponent));
        var rightValue = right.Coefficient << checked((int)(right.Exponent - binaryExponent));
        if (left.Negative)
        {
            leftValue = -leftValue;
        }
        if (rightNegative)
        {
            rightValue = -rightValue;
        }
        var binarySum = leftValue + rightValue;
        var binaryNegative = binarySum.Sign < 0
            || (binarySum.IsZero && left.Coefficient.IsZero && right.Coefficient.IsZero
                && left.Negative && rightNegative);
        return TryEncodeBinaryPower(BigInteger.Abs(binarySum), binaryExponent,
            binaryNegative, type, out bits);
    }

    private static bool TryMultiply(PrimitiveType type,
        DecodedFloatingValue left, DecodedFloatingValue right, out ScalarBits bits)
    {
        bits = default;
        if (left.Class == FloatingValueClass.NaN || right.Class == FloatingValueClass.NaN
            || (left.Class == FloatingValueClass.Infinity && right.Coefficient.IsZero)
            || (right.Class == FloatingValueClass.Infinity && left.Coefficient.IsZero))
        {
            return TryEncodeQuietNaN(type, out bits);
        }

        var negative = left.Negative ^ right.Negative;
        if (left.Class == FloatingValueClass.Infinity || right.Class == FloatingValueClass.Infinity)
        {
            return TryEncodeInfinity(negative, type, out bits);
        }

        var exponent = left.Exponent + right.Exponent;
        if (left.Coefficient.IsZero || right.Coefficient.IsZero)
        {
            return TryEncodeZero(negative, IsDecimal(type) ? exponent : 0, type, out bits);
        }

        var coefficient = left.Coefficient * right.Coefficient;
        return IsDecimal(type)
            ? TryEncodeDecimal(coefficient, exponent, exponent, negative, type, out bits)
            : TryEncodeBinaryPower(coefficient, exponent, negative, type, out bits);
    }

    private static bool TryDivide(PrimitiveType type,
        DecodedFloatingValue left, DecodedFloatingValue right, out ScalarBits bits)
    {
        bits = default;
        if (left.Class == FloatingValueClass.NaN || right.Class == FloatingValueClass.NaN
            || (left.Class == FloatingValueClass.Infinity && right.Class == FloatingValueClass.Infinity)
            || (left.Coefficient.IsZero && right.Coefficient.IsZero
                && left.Class == FloatingValueClass.Finite && right.Class == FloatingValueClass.Finite))
        {
            return TryEncodeQuietNaN(type, out bits);
        }

        var negative = left.Negative ^ right.Negative;
        if (left.Class == FloatingValueClass.Infinity)
        {
            return TryEncodeInfinity(negative, type, out bits);
        }
        if (right.Class == FloatingValueClass.Infinity)
        {
            var zeroExponent = 0;
            if (IsDecimal(type) && !TryGetDecimalFormat(type,
                out _, out _, out _, out _, out _, out zeroExponent, out _, out _))
            {
                return false;
            }
            return TryEncodeZero(negative, zeroExponent, type, out bits);
        }
        if (right.Coefficient.IsZero)
        {
            return TryEncodeInfinity(negative, type, out bits);
        }
        if (left.Coefficient.IsZero)
        {
            var preferredExponent = IsDecimal(type) ? left.Exponent - right.Exponent : 0;
            return TryEncodeZero(negative, preferredExponent, type, out bits);
        }

        var exponent = left.Exponent - right.Exponent;
        if (IsDecimal(type))
        {
            return TryEncodeDecimalRational(left.Coefficient, right.Coefficient,
                exponent, exponent, negative, type, out bits);
        }

        var numerator = left.Coefficient;
        var denominator = right.Coefficient;
        if (exponent >= 0)
        {
            numerator <<= checked((int)exponent);
        }
        else
        {
            denominator <<= checked((int)-exponent);
        }
        return TryEncodeBinaryRational(numerator, denominator, negative, type, out bits);
    }

    private static bool TryRemainder(PrimitiveType type, ScalarBits leftBits, ScalarBits rightBits,
        DecodedFloatingValue left, DecodedFloatingValue right, out ScalarBits bits)
    {
        bits = default;
        if (left.Class == FloatingValueClass.NaN || right.Class == FloatingValueClass.NaN
            || left.Class == FloatingValueClass.Infinity
            || (right.Class == FloatingValueClass.Finite && right.Coefficient.IsZero))
        {
            return TryEncodeQuietNaN(type, out bits);
        }
        if (right.Class == FloatingValueClass.Infinity)
        {
            bits = leftBits;
            return true;
        }

        if (left.Coefficient.IsZero)
        {
            var exponent = IsDecimal(type) ? Math.Min(left.Exponent, right.Exponent) : 0;
            return TryEncodeZero(left.Negative, exponent, type, out bits);
        }

        if (IsDecimal(type))
        {
            var exponent = Math.Min(left.Exponent, right.Exponent);
            var dividend = left.Coefficient * PowerOfTen(left.Exponent - exponent);
            var divisor = right.Coefficient * PowerOfTen(right.Exponent - exponent);
            if (left.Negative)
            {
                dividend = -dividend;
            }
            if (right.Negative)
            {
                divisor = -divisor;
            }
            var remainder = dividend % divisor;
            var negative = remainder.IsZero ? left.Negative : remainder.Sign < 0;
            return TryEncodeDecimal(BigInteger.Abs(remainder), exponent, exponent,
                negative, type, out bits);
        }

        var binaryExponent = Math.Min(left.Exponent, right.Exponent);
        var binaryDividend = left.Coefficient << checked((int)(left.Exponent - binaryExponent));
        var binaryDivisor = right.Coefficient << checked((int)(right.Exponent - binaryExponent));
        if (left.Negative)
        {
            binaryDividend = -binaryDividend;
        }
        if (right.Negative)
        {
            binaryDivisor = -binaryDivisor;
        }
        var binaryRemainder = binaryDividend % binaryDivisor;
        var binaryNegative = binaryRemainder.IsZero ? left.Negative : binaryRemainder.Sign < 0;
        return TryEncodeBinaryPower(BigInteger.Abs(binaryRemainder), binaryExponent,
            binaryNegative, type, out bits);
    }

    private static bool TryEncodeDecimalRational(BigInteger numerator, BigInteger denominator,
        long exponent, long preferredExponent, bool negative, PrimitiveType type, out ScalarBits bits)
    {
        bits = default;
        if (numerator.Sign < 0 || denominator.Sign <= 0
            || !TryGetDecimalFormat(type, out _, out var precision, out _, out _,
                out _, out var minimumQuantumExponent, out var maximumQuantumExponent,
                out var maximumAdjustedExponent))
        {
            return false;
        }
        if (numerator.IsZero)
        {
            return TryEncodeDecimal(BigInteger.Zero, preferredExponent, preferredExponent,
                negative, type, out bits);
        }

        var adjustedExponent = checked(FloorLog10Rational(numerator, denominator) + exponent);
        if (adjustedExponent > maximumAdjustedExponent)
        {
            return TryEncodeDecimalInfinity(negative, type, out bits);
        }
        var precisionExponent = adjustedExponent - precision + 1;
        if (preferredExponent >= minimumQuantumExponent
            && preferredExponent <= maximumQuantumExponent
            && TryGetExactDecimalCoefficient(numerator, denominator, exponent,
                preferredExponent, out var preferredCoefficient)
            && preferredCoefficient < PowerOfTen(precision)
            && TryEncodeDecimal(preferredCoefficient, preferredExponent, preferredExponent,
                negative, type, out bits))
        {
            return true;
        }

        var exactExponent = precisionExponent;
        if (TryGetTerminatingDecimal(numerator, denominator, exponent,
            out var exactCoefficient, out var terminatingExponent))
        {
            var digitCount = exactCoefficient.ToString(CultureInfo.InvariantCulture).Length;
            if (digitCount <= precision
                && terminatingExponent >= minimumQuantumExponent
                && terminatingExponent <= maximumQuantumExponent)
            {
                return TryEncodeDecimal(exactCoefficient, terminatingExponent,
                    terminatingExponent, negative, type, out bits);
            }
            exactExponent = Math.Max(terminatingExponent, precisionExponent);
        }

        var outputExponent = Math.Clamp(exactExponent,
            minimumQuantumExponent, maximumQuantumExponent);
        var rounded = RoundDecimalRational(numerator, denominator, exponent, outputExponent);
        if (rounded >= PowerOfTen(precision))
        {
            rounded /= 10;
            outputExponent++;
            if (outputExponent > maximumQuantumExponent)
            {
                return TryEncodeDecimalInfinity(negative, type, out bits);
            }
        }
        return TryEncodeDecimal(rounded, outputExponent, outputExponent,
            negative, type, out bits);
    }

    private static bool TryGetExactDecimalCoefficient(BigInteger numerator, BigInteger denominator,
        long exponent, long quantum, out BigInteger coefficient)
    {
        var scaledNumerator = numerator;
        var scaledDenominator = denominator;
        var shift = exponent - quantum;
        if (shift >= 0)
        {
            scaledNumerator *= PowerOfTen(shift);
        }
        else
        {
            scaledDenominator *= PowerOfTen(-shift);
        }
        coefficient = BigInteger.DivRem(scaledNumerator, scaledDenominator, out var remainder);
        return remainder.IsZero;
    }

    private static bool TryGetTerminatingDecimal(BigInteger numerator, BigInteger denominator,
        long exponent, out BigInteger coefficient, out long quantum)
    {
        var divisor = denominator / BigInteger.GreatestCommonDivisor(numerator, denominator);
        var twos = 0;
        var fives = 0;
        while (divisor.IsEven)
        {
            divisor >>= 1;
            twos++;
        }
        while (BigInteger.Remainder(divisor, 5).IsZero)
        {
            divisor /= 5;
            fives++;
        }
        if (divisor != BigInteger.One)
        {
            coefficient = BigInteger.Zero;
            quantum = 0;
            return false;
        }

        var commonScale = Math.Max(twos, fives);
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        coefficient = numerator / gcd
            * BigInteger.Pow(2, commonScale - twos)
            * BigInteger.Pow(5, commonScale - fives);
        quantum = exponent - commonScale;
        while (BigInteger.Remainder(coefficient, 10).IsZero)
        {
            coefficient /= 10;
            quantum++;
        }
        return true;
    }

    private static BigInteger RoundDecimalRational(BigInteger numerator, BigInteger denominator,
        long exponent, long quantum)
    {
        var shift = exponent - quantum;
        return shift >= 0
            ? RoundToEven(numerator * PowerOfTen(shift), denominator)
            : RoundToEven(numerator, denominator * PowerOfTen(-shift));
    }

    private static long FloorLog10Rational(BigInteger numerator, BigInteger denominator)
    {
        var exponent = (long)numerator.ToString(CultureInfo.InvariantCulture).Length
            - denominator.ToString(CultureInfo.InvariantCulture).Length;
        if (exponent >= 0)
        {
            if (numerator < denominator * PowerOfTen(exponent))
            {
                exponent--;
            }
        }
        else if (numerator * PowerOfTen(-exponent) < denominator)
        {
            exponent--;
        }
        return exponent;
    }

    private static bool TryEncodeZero(bool negative, long preferredExponent,
        PrimitiveType type, out ScalarBits bits)
    {
        if (IsDecimal(type))
        {
            return TryEncodeDecimal(BigInteger.Zero, preferredExponent, preferredExponent,
                negative, type, out bits);
        }
        return TryEncodeBinaryRational(BigInteger.Zero, BigInteger.One, negative, type, out bits);
    }

    private static bool TryEncodeInfinity(bool negative, PrimitiveType type, out ScalarBits bits)
    {
        return IsDecimal(type)
            ? TryEncodeDecimalInfinity(negative, type, out bits)
            : TryEncodeBinaryInfinity(negative, type, out bits);
    }

    private static bool TryEncodeQuietNaN(PrimitiveType type, out ScalarBits bits)
    {
        if (TryGetBinaryFormat(type, out _, out var fractionBits,
            out _, out _, out var exponentBits, out _))
        {
            var exponentMask = (BigInteger.One << exponentBits) - BigInteger.One;
            return SetRaw((exponentMask << fractionBits)
                | (BigInteger.One << (fractionBits - 1)), out bits);
        }
        if (TryGetDecimalFormat(type, out var decimalWidth, out _, out _, out _,
            out _, out _, out _, out _))
        {
            return SetRaw(new BigInteger(0b1_1111) << (decimalWidth - 6), out bits);
        }
        bits = default;
        return false;
    }

    private static bool SetRaw(BigInteger raw, out ScalarBits bits)
    {
        bits = FromRaw(raw);
        return true;
    }

    private static bool IsDecimal(PrimitiveType type) =>
        type is PrimitiveType.Decimal32 or PrimitiveType.Decimal64 or PrimitiveType.Decimal128;
}
