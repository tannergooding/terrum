// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed partial class Binder
{
    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only the supported built-in operators can be folded.")]
    internal static BoundExpression FoldScalarConstant(BoundExpression expression)
    {
        BigInteger value;
        switch (expression)
        {
            case BoundUnary { Operator: TokenKind.Bang, Operand: BoundConstant { Type: PrimitiveType.Bool } boolean, Type: PrimitiveType.Bool }:
                return new BoundConstant(boolean.Value == 0 ? 1 : 0, PrimitiveType.Bool, expression.Span);

            case BoundBinary { Left: BoundConstant boolLeft, Right: BoundConstant boolRight, Type: PrimitiveType.Bool } comparison:
                if (boolLeft.Type != boolRight.Type)
                {
                    return expression;
                }
                BigInteger firstValue;
                BigInteger secondValue;
                if (boolLeft.Type == PrimitiveType.Bool)
                {
                    firstValue = boolLeft.Value;
                    secondValue = boolRight.Value;
                }
                else if (PrimitiveTypeFacts.IsFloating(boolLeft.Type)
                    && FloatingLiteralParser.TryFoldBinary(comparison.Operator, boolLeft.Type,
                        boolLeft.Bits, boolRight.Bits, out var floatingComparison))
                {
                    return new BoundConstant(floatingComparison, PrimitiveType.Bool, expression.Span);
                }
                else if (!TryScalarValue(boolLeft, out firstValue) || !TryScalarValue(boolRight, out secondValue))
                {
                    return expression;
                }
                bool result;
                switch (comparison.Operator)
                {
                    case TokenKind.EqualsEquals:
                        result = firstValue == secondValue;
                        break;
                    case TokenKind.BangEquals:
                        result = firstValue != secondValue;
                        break;
                    case TokenKind.AmpersandAmpersand or TokenKind.Ampersand when boolLeft.Type == PrimitiveType.Bool:
                        result = firstValue != 0 && secondValue != 0;
                        break;
                    case TokenKind.PipePipe or TokenKind.Pipe when boolLeft.Type == PrimitiveType.Bool:
                        result = firstValue != 0 || secondValue != 0;
                        break;
                    case TokenKind.Caret when boolLeft.Type == PrimitiveType.Bool:
                        result = firstValue != secondValue;
                        break;
                    case TokenKind.Less when boolLeft.Type != PrimitiveType.Bool:
                        result = firstValue < secondValue;
                        break;
                    case TokenKind.LessEquals when boolLeft.Type != PrimitiveType.Bool:
                        result = firstValue <= secondValue;
                        break;
                    case TokenKind.Greater when boolLeft.Type != PrimitiveType.Bool:
                        result = firstValue > secondValue;
                        break;
                    case TokenKind.GreaterEquals when boolLeft.Type != PrimitiveType.Bool:
                        result = firstValue >= secondValue;
                        break;
                    default:
                        return expression;
                }
                return new BoundConstant(result ? 1 : 0, PrimitiveType.Bool, expression.Span);

            case BoundBinary { Left: BoundConstant left, Right: BoundConstant right } binary
                when left.Type == right.Type && binary.Type == left.Type
                    && PrimitiveTypeFacts.IsFloating(left.Type)
                    && FloatingLiteralParser.TryFoldBinary(binary.Operator, left.Type,
                        left.Bits, right.Bits, out var binaryFloatingBits):
                return new BoundConstant(binaryFloatingBits, binary.Type, expression.Span);

            case BoundCast { Operand: BoundConstant operand } cast
                when TryConvertToFloatingConstant(operand, cast.Type, out var floatingBits):
                return new BoundConstant(floatingBits, cast.Type, expression.Span);

            case BoundCast { Operand: BoundConstant operand } cast
                when TryConvertFloatingToIntegerConstant(operand, cast.Type, out var integerBits):
                return new BoundConstant(integerBits, cast.Type, expression.Span);

            case BoundCast { Operand: BoundConstant operand, Type: PrimitiveType.Int128 or PrimitiveType.UInt128 } cast
                when (PrimitiveTypeFacts.IsInteger(operand.Type) || operand.Type == PrimitiveType.Char16)
                    && TryScalarValue(operand, out var wideInput)
                    && ScalarBits.TryFromIntegerValue(wideInput, cast.Type, out var wideBits):
                return new BoundConstant(wideBits, cast.Type, expression.Span);

            case BoundCast { Operand: BoundConstant operand }
                when TryScalarValue(operand, out var input):
                value = input;
                break;

            case BoundUnary { Operand: BoundConstant operand } unary
                when unary.Type == operand.Type && PrimitiveTypeFacts.IsFloating(unary.Type)
                    && unary.Operator is TokenKind.Plus or TokenKind.Minus:
                return new BoundConstant(unary.Operator == TokenKind.Minus
                    ? operand.Bits.ToggleSign(unary.Type) : operand.Bits, unary.Type, expression.Span);

            case BoundUnary { Operand: BoundConstant operand } unary
                when unary.Type == operand.Type && TryScalarValue(operand, out var input):
                switch (unary.Operator)
                {
                    case TokenKind.Plus:
                        value = input;
                        break;
                    case TokenKind.Minus when PrimitiveTypeFacts.IsIntegerOrWide(unary.Type):
                        value = -input;
                        break;
                    case TokenKind.Tilde when PrimitiveTypeFacts.IsIntegerOrWide(unary.Type):
                        var width = PrimitiveTypeFacts.IntegerWidthOrWide(unary.Type);
                        var modulus = BigInteger.One << width;
                        value = (~input) & (modulus - BigInteger.One);
                        if (PrimitiveTypeFacts.IsSignedIntegerOrWide(unary.Type)
                            && value >= (BigInteger.One << (width - 1)))
                        {
                            value -= modulus;
                        }
                        break;
                    default:
                        return expression;
                }
                break;

            case BoundBinary { Left: BoundConstant left, Right: BoundConstant count } shift
                when shift.Operator is TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned
                    && shift.Type == left.Type && PrimitiveTypeFacts.IsIntegerOrWide(count.Type)
                    && TryScalarValue(left, out var shifted) && TryScalarValue(count, out var shiftCount):
            {
                var width = PrimitiveTypeFacts.IntegerWidthOrWide(shift.Type);
                if (shiftCount < BigInteger.Zero || shiftCount >= width)
                {
                    return expression;
                }
                value = shift.Operator switch {
                    TokenKind.ShiftLeft => (shifted << (int)shiftCount) & ((BigInteger.One << width) - 1),
                    TokenKind.ShiftRightUnsigned => (shifted & ((BigInteger.One << width) - 1)) >> (int)shiftCount,
                    _ => shifted >> (int)shiftCount,
                };
                if (PrimitiveTypeFacts.IsSignedIntegerOrWide(shift.Type)
                    && value >= (BigInteger.One << (width - 1)))
                {
                    value -= BigInteger.One << width;
                }
                break;
            }

            case BoundBinary { Left: BoundConstant left, Right: BoundConstant right } binary
                when left.Type == right.Type && binary.Type == left.Type
                    && TryScalarValue(left, out var first) && TryScalarValue(right, out var second):
                switch (binary.Operator)
                {
                    case TokenKind.Plus:
                        value = first + second;
                        break;
                    case TokenKind.Minus:
                        value = first - second;
                        break;
                    case TokenKind.Star:
                        value = first * second;
                        break;
                    case TokenKind.Slash when second != 0:
                        value = first / second;
                        break;
                    case TokenKind.Percent when second != 0:
                        value = first % second;
                        break;
                    case TokenKind.Ampersand:
                        value = first & second;
                        break;
                    case TokenKind.Pipe:
                        value = first | second;
                        break;
                    case TokenKind.Caret:
                        value = first ^ second;
                        break;
                    default:
                        return expression;
                }
                break;

            default:
                return expression;
        }

        var overflowBehavior = expression switch {
            BoundUnary unary => unary.OverflowBehavior,
            BoundBinary binary => binary.OverflowBehavior,
            _ => BoundIntegerOverflowBehavior.Checked,
        };
        var encoded = overflowBehavior == BoundIntegerOverflowBehavior.Wrapping
            ? TryWrapScalar(value, expression.Type, out var bits)
            : TryEncodeScalar(value, expression.Type, out bits);
        return encoded
            ? new BoundConstant(bits, expression.Type, expression.Span) : expression;
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Only integral scalar constants can be folded.")]
    private static bool TryScalarValue(BoundConstant constant, out BigInteger value)
    {
        if (PrimitiveTypeFacts.IsWideInteger(constant.Type))
        {
            return constant.Bits.TryGetIntegerValue(constant.Type, out value);
        }

        value = constant.Type switch {
            PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32 or PrimitiveType.Int64
                or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32 or PrimitiveType.Rune => constant.Value,
            PrimitiveType.UInt8 => unchecked((byte)constant.Value),
            PrimitiveType.UInt16 => unchecked((ushort)constant.Value),
            PrimitiveType.UInt32 => unchecked((uint)(int)constant.Value),
            PrimitiveType.UInt64 => unchecked((ulong)constant.Value),
            _ => default,
        };
        return PrimitiveTypeFacts.IsIntegerOrWide(constant.Type)
            || PrimitiveTypeFacts.IsCodeUnitOrRune(constant.Type);
    }

    private static bool TryConvertToFloatingConstant(BoundConstant source, PrimitiveType destination, out long value)
    {
        value = 0;
        if (destination is PrimitiveType.Float32 or PrimitiveType.Float64
            && TryScalarValue(source, out var integer)
            && (PrimitiveTypeFacts.IsInteger(source.Type) || source.Type == PrimitiveType.Char16))
        {
            value = destination == PrimitiveType.Float32
                ? unchecked((uint)BitConverter.SingleToInt32Bits((float)integer))
                : BitConverter.DoubleToInt64Bits((double)integer);
            return true;
        }
        if (source.Type == PrimitiveType.Float32 && destination == PrimitiveType.Float64)
        {
            var single = BitConverter.Int32BitsToSingle(unchecked((int)source.Value));
            value = BitConverter.DoubleToInt64Bits(single);
            return true;
        }
        if (source.Type == PrimitiveType.Float64 && destination == PrimitiveType.Float32)
        {
            var number = BitConverter.Int64BitsToDouble(source.Value);
            value = unchecked((uint)BitConverter.SingleToInt32Bits((float)number));
            return true;
        }
        return false;
    }

    private static bool TryConvertFloatingToIntegerConstant(BoundConstant source, PrimitiveType destination,
        out long value)
    {
        value = 0;
        if (destination is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64))
        {
            return false;
        }
        var floatingValue = source.Type switch {
            PrimitiveType.Float32 => BitConverter.Int32BitsToSingle(unchecked((int)source.Value)),
            PrimitiveType.Float64 => BitConverter.Int64BitsToDouble(source.Value),
            _ => double.NaN,
        };
        if (!double.IsFinite(floatingValue))
        {
            return false;
        }
        if (!TryEncodeScalar(new BigInteger(Math.Truncate(floatingValue)), destination, out var bits))
        {
            return false;
        }
        return bits.TryGetLegacyValue(destination, out value);
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Other types cannot be folded.")]
    [SuppressMessage("Style", "IDE0078:Use pattern matching", Justification = "BigInteger range comparisons use its overloaded operators.")]
    private static bool TryEncodeScalar(BigInteger value, PrimitiveType type, out ScalarBits bits)
    {
        bits = default;
        if (PrimitiveTypeFacts.IsWideInteger(type))
        {
            return ScalarBits.TryFromIntegerValue(value, type, out bits);
        }
        if (type switch {
            PrimitiveType.Int8 => value < sbyte.MinValue || value > sbyte.MaxValue,
            PrimitiveType.Int16 => value < short.MinValue || value > short.MaxValue,
            PrimitiveType.Int32 => value < int.MinValue || value > int.MaxValue,
            PrimitiveType.UInt8 => value < byte.MinValue || value > byte.MaxValue,
            PrimitiveType.UInt16 => value < ushort.MinValue || value > ushort.MaxValue,
            PrimitiveType.UInt32 => value < 0 || value > uint.MaxValue,
            PrimitiveType.Int64 => value < long.MinValue || value > long.MaxValue,
            PrimitiveType.UInt64 => value < 0 || value > ulong.MaxValue,
            PrimitiveType.Char8 => value < byte.MinValue || value > byte.MaxValue,
            PrimitiveType.Char16 => value < 0 || value > ushort.MaxValue,
            PrimitiveType.Char32 => value < 0 || value > uint.MaxValue,
            PrimitiveType.Rune => value < 0 || value > 0x10_FFFF
                || (value >= 0xD800 && value <= 0xDFFF),
            _ => true,
        })
        {
            return false;
        }
        var legacyValue = type switch {
            PrimitiveType.UInt8 or PrimitiveType.Char8 => (byte)value,
            PrimitiveType.UInt16 or PrimitiveType.Char16 => (ushort)value,
            PrimitiveType.UInt32 => unchecked((int)(uint)value),
            PrimitiveType.UInt64 => unchecked((long)(ulong)value),
            PrimitiveType.Char32 or PrimitiveType.Rune => (long)(uint)value,
            _ => (long)value,
        };
        bits = ScalarBits.FromLegacyValue(legacyValue, type);
        return true;
    }

    private static bool TryWrapScalar(BigInteger value, PrimitiveType type, out ScalarBits bits)
    {
        bits = default;
        var width = PrimitiveTypeFacts.IntegerWidthOrWide(type);
        if (width == 0)
        {
            return false;
        }
        var modulus = BigInteger.One << width;
        var wrapped = value % modulus;
        if (wrapped.Sign < 0)
        {
            wrapped += modulus;
        }
        if (PrimitiveTypeFacts.IsSignedIntegerOrWide(type)
            && wrapped >= (BigInteger.One << (width - 1)))
        {
            wrapped -= modulus;
        }
        if (PrimitiveTypeFacts.IsWideInteger(type))
        {
            return ScalarBits.TryFromIntegerValue(wrapped, type, out bits);
        }
        var legacyValue = type switch {
            PrimitiveType.UInt8 => (byte)wrapped,
            PrimitiveType.UInt16 => (ushort)wrapped,
            PrimitiveType.UInt32 => unchecked((int)(uint)wrapped),
            PrimitiveType.UInt64 => unchecked((long)(ulong)wrapped),
            PrimitiveType.Int8 => (sbyte)wrapped,
            PrimitiveType.Int16 => (short)wrapped,
            _ => (long)wrapped,
        };
        bits = ScalarBits.FromLegacyValue(legacyValue, type);
        return true;
    }
}
