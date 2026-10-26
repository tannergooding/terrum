// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Globalization;
using System.Numerics;

namespace Terrum.CodeAnalysis.Semantics;

internal static partial class FloatingLiteralParser
{
    private const double Log10OfTwo = 0.30102999566398119521;

    internal static bool TryParseWide(ReadOnlySpan<char> text, PrimitiveType type, out ScalarBits bits)
    {
        bits = default;
        if (type is not (PrimitiveType.Float16 or PrimitiveType.BFloat16 or PrimitiveType.Float128
            or PrimitiveType.Decimal32 or PrimitiveType.Decimal64 or PrimitiveType.Decimal128))
        {
            return false;
        }

        var spelling = text.ToString().Replace("_", "", StringComparison.Ordinal);
        if (spelling.Length == 0)
        {
            return false;
        }

        var negative = false;
        if (spelling[0] is '+' or '-')
        {
            negative = spelling[0] == '-';
            spelling = spelling[1..];
            if (spelling.Length == 0)
            {
                return false;
            }
        }

        if (TryGetBinaryFormat(type, out _, out _, out _, out _, out _, out _))
        {
            if (TryParseHexFloatParts(spelling, out var significand, out var binaryShift))
            {
                return TryEncodeBinaryPower(significand, binaryShift, negative, type, out bits);
            }
            if (TryParseBasedIntegerSpelling(spelling, out var basedMagnitude))
            {
                return TryEncodeBinaryRational(basedMagnitude, BigInteger.One,
                    negative, type, out bits);
            }
            if (!TryParseDecimalParts(spelling, out var coefficient, out var exponent))
            {
                return false;
            }
            return TryEncodeBinaryDecimal(coefficient, exponent, negative, type, out bits);
        }

        if (!TryGetDecimalFormat(type, out _, out _, out _, out _, out _, out _, out _, out _))
        {
            return false;
        }

        BigInteger decimalCoefficient;
        long decimalExponent;
        if (TryParseHexFloatParts(spelling, out var hexSignificand, out var hexShift))
        {
            if (!TryConvertHexFloatToDecimalParts(
                hexSignificand, hexShift, out decimalCoefficient, out decimalExponent))
            {
                return false;
            }
            return TryEncodeDecimal(decimalCoefficient, decimalExponent, decimalExponent,
                negative, type, out bits);
        }
        else if (TryParseBasedIntegerSpelling(spelling, out decimalCoefficient))
        {
            decimalExponent = 0;
        }
        else
        {
            if (!TryParseDecimalParts(spelling, out decimalCoefficient, out decimalExponent))
            {
                return false;
            }
        }

        return TryEncodeDecimal(decimalCoefficient, decimalExponent, decimalExponent,
            negative, type, out bits);
    }

    private static bool TryParseHexFloatParts(
        string spelling, out BigInteger significand, out long binaryShift)
    {
        significand = BigInteger.Zero;
        binaryShift = 0;
        if (!spelling.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var exponentIndex = spelling.IndexOfAny(['p', 'P']);
        if (exponentIndex < 0
            || spelling.IndexOfAny(['p', 'P'], exponentIndex + 1) >= 0
            || !TryParseExponent(spelling.AsSpan(exponentIndex + 1), out var exponent))
        {
            return false;
        }

        var mantissa = spelling.AsSpan(2, exponentIndex - 2);
        var pointIndex = mantissa.IndexOf('.');
        var fractionalDigits = pointIndex < 0 ? 0 : mantissa.Length - pointIndex - 1;
        var digits = pointIndex < 0
            ? mantissa.ToString()
            : string.Concat(mantissa[..pointIndex], mantissa[(pointIndex + 1)..]);
        if (digits.Length == 0
            || !BigInteger.TryParse(string.Concat("0", digits), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out significand))
        {
            return false;
        }

        binaryShift = exponent - (4L * fractionalDigits);
        return true;
    }

    private static bool TryParseBasedIntegerSpelling(string spelling, out BigInteger magnitude)
    {
        if (spelling.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseBasedInteger(spelling.AsSpan(2), 16, out magnitude);
        }
        if (spelling.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseBasedInteger(spelling.AsSpan(2), 2, out magnitude);
        }
        magnitude = BigInteger.Zero;
        return false;
    }

    private static bool TryParseDecimalParts(
        string spelling, out BigInteger coefficient, out long exponent)
    {
        coefficient = BigInteger.Zero;
        exponent = 0;
        var exponentIndex = spelling.IndexOfAny(['e', 'E']);
        if (exponentIndex >= 0)
        {
            if (spelling.IndexOfAny(['e', 'E'], exponentIndex + 1) >= 0
                || !TryParseExponent(spelling.AsSpan(exponentIndex + 1), out exponent))
            {
                return false;
            }
        }

        var mantissa = exponentIndex < 0 ? spelling.AsSpan() : spelling.AsSpan(0, exponentIndex);
        var pointIndex = mantissa.IndexOf('.');
        if (pointIndex >= 0 && mantissa[(pointIndex + 1)..].Contains('.'))
        {
            return false;
        }
        var fractionalDigits = pointIndex < 0 ? 0 : mantissa.Length - pointIndex - 1;
        var digits = pointIndex < 0
            ? mantissa.ToString()
            : string.Concat(mantissa[..pointIndex], mantissa[(pointIndex + 1)..]);
        if (digits.Length == 0)
        {
            return false;
        }
        foreach (var character in digits)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }
        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out coefficient))
        {
            return false;
        }

        exponent -= fractionalDigits;
        return true;
    }

    private static bool TryEncodeBinaryDecimal(
        BigInteger coefficient, long exponent, bool negative, PrimitiveType type, out ScalarBits bits)
    {
        bits = default;
        if (!TryGetBinaryFormat(type, out _, out var fractionBits, out var minimumExponent,
            out var maximumExponent, out _, out _))
        {
            return false;
        }
        if (coefficient.IsZero)
        {
            return TryEncodeBinaryRational(coefficient, BigInteger.One, negative, type, out bits);
        }

        var adjustedExponent = checked((long)coefficient.ToString(CultureInfo.InvariantCulture).Length - 1 + exponent);
        var maximumDecimalOrder = (long)Math.Ceiling((maximumExponent + 1) * Log10OfTwo) + 2;
        var minimumSubnormalExponent = minimumExponent - fractionBits;
        var minimumDecimalOrder = (long)Math.Floor(minimumSubnormalExponent * Log10OfTwo) - 2;
        if (adjustedExponent > maximumDecimalOrder)
        {
            return TryEncodeBinaryInfinity(negative, type, out bits);
        }
        if (adjustedExponent < minimumDecimalOrder)
        {
            return TryEncodeBinaryRational(BigInteger.Zero, BigInteger.One, negative, type, out bits);
        }

        var numerator = coefficient;
        var denominator = BigInteger.One;
        if (exponent >= 0)
        {
            numerator *= PowerOfTen(exponent);
        }
        else
        {
            denominator = PowerOfTen(-exponent);
        }
        return TryEncodeBinaryRational(numerator, denominator, negative, type, out bits);
    }

    private static bool TryEncodeBinaryPower(
        BigInteger significand, long shift, bool negative, PrimitiveType type, out ScalarBits bits)
    {
        if (shift >= 0)
        {
            return TryEncodeBinaryRational(significand << checked((int)shift),
                BigInteger.One, negative, type, out bits);
        }
        return TryEncodeBinaryRational(significand,
            BigInteger.One << checked((int)-shift), negative, type, out bits);
    }

    private static bool TryEncodeBinaryRational(
        BigInteger numerator, BigInteger denominator, bool negative,
        PrimitiveType type, out ScalarBits bits)
    {
        bits = default;
        if (!TryGetBinaryFormat(type, out var totalBits, out var fractionBits,
            out var minimumExponent, out var maximumExponent, out _, out var bias)
            || numerator.Sign < 0 || denominator.Sign <= 0)
        {
            return false;
        }

        var sign = negative ? BigInteger.One << (totalBits - 1) : BigInteger.Zero;
        if (numerator.IsZero)
        {
            bits = FromRaw(sign);
            return true;
        }

        var exponent = FloorLog2(numerator, denominator);
        if (exponent > maximumExponent)
        {
            return TryEncodeBinaryInfinity(negative, type, out bits);
        }

        var minimumSubnormalExponent = minimumExponent - fractionBits;
        if (exponent < minimumSubnormalExponent - 1)
        {
            bits = FromRaw(sign);
            return true;
        }

        var hiddenBit = BigInteger.One << fractionBits;
        BigInteger exponentField;
        BigInteger fraction;
        if (exponent >= minimumExponent)
        {
            var roundedSignificand = RoundScaled(numerator, denominator, fractionBits - exponent);
            if (roundedSignificand >= (hiddenBit << 1))
            {
                roundedSignificand >>= 1;
                exponent++;
            }
            if (exponent > maximumExponent)
            {
                return TryEncodeBinaryInfinity(negative, type, out bits);
            }

            exponentField = exponent + bias;
            fraction = roundedSignificand - hiddenBit;
        }
        else
        {
            var subnormal = RoundScaled(numerator, denominator, -minimumSubnormalExponent);
            if (subnormal >= hiddenBit)
            {
                exponentField = BigInteger.One;
                fraction = BigInteger.Zero;
            }
            else
            {
                exponentField = BigInteger.Zero;
                fraction = subnormal;
            }
        }

        var raw = sign | (exponentField << fractionBits) | fraction;
        bits = FromRaw(raw);
        return true;
    }

    private static bool TryEncodeBinaryInfinity(bool negative, PrimitiveType type, out ScalarBits bits)
    {
        bits = default;
        if (!TryGetBinaryFormat(type, out var totalBits, out var fractionBits,
            out _, out _, out var exponentBits, out _))
        {
            return false;
        }
        var sign = negative ? BigInteger.One << (totalBits - 1) : BigInteger.Zero;
        var exponentMask = (BigInteger.One << exponentBits) - BigInteger.One;
        bits = FromRaw(sign | (exponentMask << fractionBits));
        return true;
    }

    private static long FloorLog2(BigInteger numerator, BigInteger denominator)
    {
        var exponent = numerator.GetBitLength() - denominator.GetBitLength();
        if (exponent >= 0)
        {
            if (numerator < (denominator << checked((int)exponent)))
            {
                exponent--;
            }
        }
        else if ((numerator << checked((int)-exponent)) < denominator)
        {
            exponent--;
        }
        return exponent;
    }

    private static BigInteger RoundScaled(BigInteger numerator, BigInteger denominator, long shift)
    {
        if (shift >= 0)
        {
            numerator <<= checked((int)shift);
        }
        else
        {
            denominator <<= checked((int)-shift);
        }
        return RoundToEven(numerator, denominator);
    }

    private static BigInteger RoundToEven(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        var comparison = (remainder << 1).CompareTo(denominator);
        return comparison > 0 || (comparison == 0 && !quotient.IsEven)
            ? quotient + BigInteger.One : quotient;
    }

    private static bool TryConvertHexFloatToDecimalParts(
        BigInteger significand, long binaryShift, out BigInteger coefficient, out long exponent)
    {
        coefficient = BigInteger.Zero;
        exponent = 0;
        if (significand.IsZero)
        {
            return true;
        }

        if (binaryShift >= 0)
        {
            coefficient = significand << checked((int)binaryShift);
        }
        else
        {
            var denominatorPower = -binaryShift;
            var cancelledPowersOfTwo = Math.Min(
                CountTrailingZeroBits(significand), denominatorPower);
            var remainingPower = denominatorPower - cancelledPowersOfTwo;
            coefficient = (significand >> checked((int)cancelledPowersOfTwo))
                * BigInteger.Pow(5, checked((int)remainingPower));
            exponent = -remainingPower;
        }
        StripDecimalTrailingZeros(ref coefficient, ref exponent);
        return true;
    }

    private static long CountTrailingZeroBits(BigInteger value)
    {
        long count = 0;
        while (!value.IsZero)
        {
            var low = (ulong)(value & ulong.MaxValue);
            if (low != 0)
            {
                return count + BitOperations.TrailingZeroCount(low);
            }
            value >>= 64;
            count += 64;
        }
        return count;
    }

    private static void StripDecimalTrailingZeros(ref BigInteger coefficient, ref long exponent)
    {
        while (!coefficient.IsZero)
        {
            var quotient = BigInteger.DivRem(coefficient, 10, out var remainder);
            if (!remainder.IsZero)
            {
                return;
            }
            coefficient = quotient;
            exponent++;
        }
    }

    private static bool TryEncodeDecimal(
        BigInteger coefficient, long exponent, long preferredExponent,
        bool negative, PrimitiveType type, out ScalarBits bits)
    {
        bits = default;
        if (!TryGetDecimalFormat(type, out var totalBits, out var precision,
            out var coefficientBits, out _, out var bias,
            out var minimumQuantumExponent, out var maximumQuantumExponent,
            out var maximumAdjustedExponent)
            || coefficient.Sign < 0)
        {
            return false;
        }

        long outputExponent;
        if (coefficient.IsZero)
        {
            outputExponent = Math.Clamp(preferredExponent,
                minimumQuantumExponent, maximumQuantumExponent);
        }
        else
        {
            var digitCount = coefficient.ToString(CultureInfo.InvariantCulture).Length;
            var adjustedExponent = checked((long)digitCount - 1 + exponent);
            if (adjustedExponent > maximumAdjustedExponent)
            {
                return TryEncodeDecimalInfinity(negative, type, out bits);
            }
            if (adjustedExponent < minimumQuantumExponent - 1)
            {
                return TryEncodeDecimal(BigInteger.Zero, minimumQuantumExponent,
                    minimumQuantumExponent, negative, type, out bits);
            }

            outputExponent = Math.Clamp(Math.Max(preferredExponent,
                    adjustedExponent - precision + 1),
                minimumQuantumExponent, maximumQuantumExponent);
            var shift = exponent - outputExponent;
            var rounded = shift >= 0
                ? coefficient * PowerOfTen(shift)
                : RoundToEven(coefficient, PowerOfTen(-shift));
            var precisionLimit = PowerOfTen(precision);
            if (rounded >= precisionLimit)
            {
                rounded /= 10;
                outputExponent++;
                if (outputExponent > maximumQuantumExponent)
                {
                    return TryEncodeDecimalInfinity(negative, type, out bits);
                }
            }

            coefficient = rounded;
            if (coefficient >= precisionLimit)
            {
                return false;
            }
        }

        var sign = negative ? BigInteger.One << (totalBits - 1) : BigInteger.Zero;
        var exponentField = outputExponent + bias;
        var shortCoefficientLimit = BigInteger.One << coefficientBits;
        BigInteger raw;
        if (coefficient < shortCoefficientLimit)
        {
            raw = sign | (new BigInteger(exponentField) << coefficientBits) | coefficient;
        }
        else
        {
            var steering = new BigInteger(3) << (totalBits - 3);
            var encodedCoefficient = coefficient - shortCoefficientLimit;
            raw = sign | steering
                | (new BigInteger(exponentField) << (coefficientBits - 2))
                | encodedCoefficient;
        }
        bits = FromRaw(raw);
        return true;
    }

    private static bool TryEncodeDecimalInfinity(
        bool negative, PrimitiveType type, out ScalarBits bits)
    {
        bits = default;
        if (!TryGetDecimalFormat(type, out var totalBits, out _, out _, out _, out _,
            out _, out _, out _))
        {
            return false;
        }
        var sign = negative ? BigInteger.One << (totalBits - 1) : BigInteger.Zero;
        bits = FromRaw(sign | (new BigInteger(0b11110) << (totalBits - 6)));
        return true;
    }

    private static BigInteger PowerOfTen(long exponent) =>
        BigInteger.Pow(10, checked((int)exponent));

    private static ScalarBits FromRaw(BigInteger raw) =>
        new((ulong)(raw & ulong.MaxValue), (ulong)(raw >> 64));

    private static bool TryGetBinaryFormat(
        PrimitiveType type, out int totalBits, out int fractionBits, out int minimumExponent,
        out int maximumExponent, out int exponentBits, out int bias)
    {
        var (width, precision, minimumNormal, maximumNormal, exponentWidth, exponentBias) = type switch
        {
            PrimitiveType.Float16 => (16, 11, -14, 15, 5, 15),
            PrimitiveType.BFloat16 => (16, 8, -126, 127, 8, 127),
            PrimitiveType.Float32 => (32, 24, -126, 127, 8, 127),
            PrimitiveType.Float64 => (64, 53, -1022, 1023, 11, 1023),
            PrimitiveType.Float128 => (128, 113, -16382, 16383, 15, 16383),
            _ => (0, 0, 0, 0, 0, 0),
        };
        totalBits = width;
        fractionBits = precision - 1;
        minimumExponent = minimumNormal;
        maximumExponent = maximumNormal;
        exponentBits = exponentWidth;
        bias = exponentBias;
        return width != 0;
    }

    private static bool TryGetDecimalFormat(
        PrimitiveType type, out int totalBits, out int precision, out int coefficientBits,
        out int exponentBits, out int bias, out int minimumQuantumExponent,
        out int maximumQuantumExponent, out int maximumAdjustedExponent)
    {
        var format = type switch
        {
            PrimitiveType.Decimal32 => (32, 7, 23, 8, 101, -101, 90, 96),
            PrimitiveType.Decimal64 => (64, 16, 53, 10, 398, -398, 369, 384),
            PrimitiveType.Decimal128 => (128, 34, 113, 14, 6176, -6176, 6111, 6144),
            _ => (0, 0, 0, 0, 0, 0, 0, 0),
        };
        totalBits = format.Item1;
        precision = format.Item2;
        coefficientBits = format.Item3;
        exponentBits = format.Item4;
        bias = format.Item5;
        minimumQuantumExponent = format.Item6;
        maximumQuantumExponent = format.Item7;
        maximumAdjustedExponent = format.Item8;
        return totalBits != 0;
    }
}
