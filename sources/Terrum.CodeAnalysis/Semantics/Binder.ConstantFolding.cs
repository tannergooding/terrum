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

            case BoundCast { Operand: BoundConstant operand } cast
                when TryConvertToFloatingConstant(operand, cast.Type, out var floatingBits):
                return new BoundConstant(floatingBits, cast.Type, expression.Span);

            case BoundCast { Operand: BoundConstant operand } cast
                when TryConvertFloatingToIntegerConstant(operand, cast.Type, out var integerBits):
                return new BoundConstant(integerBits, cast.Type, expression.Span);

            case BoundCast { Operand: BoundConstant operand }
                when TryScalarValue(operand, out var input):
                value = input;
                break;

            case BoundUnary { Operand: BoundConstant operand } unary
                when unary.Type == operand.Type && unary.Type is PrimitiveType.Float32 or PrimitiveType.Float64
                    && unary.Operator is TokenKind.Plus or TokenKind.Minus:
            {
                var signMask = unary.Type == PrimitiveType.Float32 ? 0x8000_0000L : long.MinValue;
                var resultBits = unary.Operator == TokenKind.Minus ? operand.Value ^ signMask : operand.Value;
                return new BoundConstant(resultBits, unary.Type, expression.Span);
            }

            case BoundUnary { Operand: BoundConstant operand } unary
                when unary.Type == operand.Type && TryScalarValue(operand, out var input):
                switch (unary.Operator)
                {
                    case TokenKind.Plus:
                        value = input;
                        break;
                    case TokenKind.Minus when unary.Type is PrimitiveType.Int32 or PrimitiveType.Int64:
                        value = -input;
                        break;
                    case TokenKind.Tilde when unary.Type is PrimitiveType.Int32 or PrimitiveType.Int64:
                        value = ~input;
                        break;
                    case TokenKind.Tilde when unary.Type is PrimitiveType.UInt32 or PrimitiveType.UInt64:
                        value = (unary.Type == PrimitiveType.UInt32 ? uint.MaxValue : (BigInteger)ulong.MaxValue) ^ input;
                        break;
                    default:
                        return expression;
                }
                break;

            case BoundBinary { Left: BoundConstant left, Right: BoundConstant { Type: PrimitiveType.Int32 } count } shift
                when shift.Operator is TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned
                    && shift.Type == left.Type && TryScalarValue(left, out var shifted):
            {
                var width = shift.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 ? 32 : 64;
                if (count.Value < 0 || count.Value >= width)
                {
                    return expression;
                }
                value = shift.Operator switch {
                    TokenKind.ShiftLeft => (shifted << (int)count.Value) & ((BigInteger.One << width) - 1),
                    TokenKind.ShiftRightUnsigned => (shifted & ((BigInteger.One << width) - 1)) >> (int)count.Value,
                    _ => shifted >> (int)count.Value,
                };
                if (shift.Type is PrimitiveType.Int32 or PrimitiveType.Int64
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

        return TryEncodeScalar(value, expression.Type, out var bits)
            ? new BoundConstant(bits, expression.Type, expression.Span) : expression;
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Only integral scalar constants can be folded.")]
    private static bool TryScalarValue(BoundConstant constant, out BigInteger value)
    {
        value = constant.Type switch {
            PrimitiveType.Int32 or PrimitiveType.Int64 => constant.Value,
            PrimitiveType.UInt32 => unchecked((uint)(int)constant.Value),
            PrimitiveType.UInt64 => unchecked((ulong)constant.Value),
            PrimitiveType.Char16 => constant.Value,
            _ => default,
        };
        return constant.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
            or PrimitiveType.UInt64 or PrimitiveType.Char16;
    }

    private static bool TryConvertToFloatingConstant(BoundConstant source, PrimitiveType destination, out long value)
    {
        value = 0;
        if (destination is PrimitiveType.Float32 or PrimitiveType.Float64
            && TryScalarValue(source, out var integer)
            && (source.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                or PrimitiveType.UInt64 or PrimitiveType.Char16))
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
        return TryEncodeScalar(new BigInteger(Math.Truncate(floatingValue)), destination, out value);
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Other types cannot be folded.")]
    [SuppressMessage("Style", "IDE0078:Use pattern matching", Justification = "BigInteger range comparisons use its overloaded operators.")]
    private static bool TryEncodeScalar(BigInteger value, PrimitiveType type, out long bits)
    {
        bits = default;
        if (type switch {
            PrimitiveType.Int32 => value < int.MinValue || value > int.MaxValue,
            PrimitiveType.UInt32 => value < 0 || value > uint.MaxValue,
            PrimitiveType.Int64 => value < long.MinValue || value > long.MaxValue,
            PrimitiveType.UInt64 => value < 0 || value > ulong.MaxValue,
            PrimitiveType.Char16 => value < 0 || value > ushort.MaxValue,
            _ => true,
        })
        {
            return false;
        }
        bits = type switch {
            PrimitiveType.UInt32 => unchecked((int)(uint)value),
            PrimitiveType.UInt64 => unchecked((long)(ulong)value),
            _ => (long)value,
        };
        return true;
    }
}
