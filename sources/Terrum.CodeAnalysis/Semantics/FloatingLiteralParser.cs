// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Globalization;
using System.Numerics;

namespace Terrum.CodeAnalysis.Semantics;

internal static class FloatingLiteralParser
{
    internal static bool TryParse(ReadOnlySpan<char> text, PrimitiveType type, out long value)
    {
        value = 0;
        if (type is not (PrimitiveType.Float32 or PrimitiveType.Float64))
        {
            return false;
        }

        var spelling = text.ToString().Replace("_", "", StringComparison.Ordinal);
        if (type == PrimitiveType.Float32)
        {
            if (spelling.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && spelling.IndexOfAny(['p', 'P']) is var exponentIndex && exponentIndex >= 0)
            {
                if (!TryParseHexFloat(spelling, exponentIndex, precision: 24, minimumExponent: -126,
                    maximumExponent: 127, minimumSubnormalExponent: -149, exponentBias: 127, out var bits))
                {
                    return false;
                }
                value = (uint)bits;
                return true;
            }

            if (spelling.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseBasedInteger(spelling.AsSpan(2), radix: 16, out var magnitude)
                    && StoreSingle((float)magnitude, out value);
            }
            if (spelling.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseBasedInteger(spelling.AsSpan(2), radix: 2, out var magnitude)
                    && StoreSingle((float)magnitude, out value);
            }
            return float.TryParse(spelling, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && StoreSingle(parsed, out value);
        }

        if (spelling.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && spelling.IndexOfAny(['p', 'P']) is var doubleExponentIndex && doubleExponentIndex >= 0)
        {
            if (!TryParseHexFloat(spelling, doubleExponentIndex, precision: 53, minimumExponent: -1022,
                maximumExponent: 1023, minimumSubnormalExponent: -1074, exponentBias: 1023, out var bits))
            {
                return false;
            }
            value = unchecked((long)bits);
            return true;
        }

        if (spelling.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseBasedInteger(spelling.AsSpan(2), radix: 16, out var magnitude)
                && StoreDouble((double)magnitude, out value);
        }
        if (spelling.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseBasedInteger(spelling.AsSpan(2), radix: 2, out var magnitude)
                && StoreDouble((double)magnitude, out value);
        }
        return double.TryParse(spelling, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
            && StoreDouble(doubleValue, out value);
    }

    private static bool StoreSingle(float number, out long value)
    {
        value = unchecked((uint)BitConverter.SingleToInt32Bits(number));
        return true;
    }

    private static bool StoreDouble(double number, out long value)
    {
        value = BitConverter.DoubleToInt64Bits(number);
        return true;
    }

    private static bool TryParseBasedInteger(ReadOnlySpan<char> digits, int radix, out BigInteger magnitude)
    {
        magnitude = BigInteger.Zero;
        if (digits.IsEmpty)
        {
            return false;
        }

        var format = radix == 16 ? NumberStyles.AllowHexSpecifier : NumberStyles.AllowBinarySpecifier;
        var spelling = string.Concat("0", digits);
        return BigInteger.TryParse(spelling, format, CultureInfo.InvariantCulture, out magnitude);
    }

    private static bool TryParseHexFloat(
        string spelling,
        int exponentIndex,
        int precision,
        int minimumExponent,
        int maximumExponent,
        int minimumSubnormalExponent,
        int exponentBias,
        out ulong bits)
    {
        bits = 0;
        var exponentText = spelling.AsSpan(exponentIndex + 1);
        if (!TryParseExponent(exponentText, out var exponent))
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
                CultureInfo.InvariantCulture, out var significand))
        {
            return false;
        }

        if (significand.IsZero)
        {
            return true;
        }

        var binaryShift = exponent - (4L * fractionalDigits);
        var bitLength = significand.GetBitLength();
        var unbiasedExponent = bitLength - 1 + binaryShift;
        if (unbiasedExponent > maximumExponent)
        {
            bits = ExponentMask(precision, exponentBias);
            return true;
        }

        var fractionBits = precision - 1;
        if (unbiasedExponent >= minimumExponent)
        {
            var roundedSignificand = RoundShiftRight(significand, bitLength - precision);
            if (roundedSignificand.GetBitLength() > precision)
            {
                roundedSignificand >>= 1;
                unbiasedExponent++;
            }
            if (unbiasedExponent > maximumExponent)
            {
                bits = ExponentMask(precision, exponentBias);
                return true;
            }

            var exponentField = (ulong)(unbiasedExponent + exponentBias);
            var significandBits = (ulong)roundedSignificand;
            var fractionMask = (1UL << fractionBits) - 1;
            bits = (exponentField << fractionBits) | (significandBits & fractionMask);
            return true;
        }

        var subnormalShift = minimumSubnormalExponent - binaryShift;
        var subnormal = RoundShiftRight(significand, subnormalShift);
        var minimumNormalSignificand = BigInteger.One << fractionBits;
        if (subnormal >= minimumNormalSignificand)
        {
            bits = 1UL << fractionBits;
            return true;
        }

        bits = (ulong)subnormal;
        return true;
    }

    private static bool TryParseExponent(ReadOnlySpan<char> text, out long value)
    {
        value = 0;
        if (text.IsEmpty)
        {
            return false;
        }

        var index = 0;
        var negative = false;
        if (text[0] is '+' or '-')
        {
            negative = text[0] == '-';
            index++;
        }
        if (index == text.Length)
        {
            return false;
        }

        const long Limit = 1_000_000;
        for (; index < text.Length; index++)
        {
            var character = text[index];
            if (character is < '0' or > '9')
            {
                return false;
            }
            value = Math.Min(Limit, value * 10 + (character - '0'));
        }
        if (negative)
        {
            value = -value;
        }
        return true;
    }

    private static BigInteger RoundShiftRight(BigInteger value, long shift)
    {
        if (shift <= 0)
        {
            return value << checked((int)-shift);
        }
        if (shift > value.GetBitLength())
        {
            return BigInteger.Zero;
        }

        var whole = checked((int)shift);
        var quotient = value >> whole;
        var remainder = value - (quotient << whole);
        var halfway = BigInteger.One << (whole - 1);
        if (remainder > halfway || (remainder == halfway && !quotient.IsEven))
        {
            quotient++;
        }
        return quotient;
    }

    private static ulong ExponentMask(int precision, int exponentBias)
    {
        var fractionBits = precision - 1;
        return (ulong)(exponentBias * 2 + 1) << fractionBits;
    }
}
