// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Lexing;

/// <summary>Bounds the provisional, in-process scalar frontend result projection.</summary>
public static class ScalarFrontendHost
{
    public const int WordCount = 34;
    public const int BufferIndex = 9;

    public static ImmutableArray<HostBuffer> CreateBuffers(SourceText source)
    {
        var buffers = ScannerHost.CreateBuffers(source);
        if (buffers.Length != BufferIndex)
        {
            throw new InvalidOperationException("The scanner host buffer layout changed.");
        }
        return buffers.Add(new HostBuffer("Frontend", PrimitiveType.Int32,
            [.. new int[WordCount]], true));
    }

    public static ScalarFrontendResult ReadResult(SourceText source, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (bytes.Length != WordCount * sizeof(int))
        {
            throw InvalidResult();
        }

        var words = new int[WordCount];
        for (var index = 0; index < words.Length; index++)
        {
            words[index] = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(index * 4, 4));
        }

        if (words[0] != 1 || words[1] is not (1 or 2))
        {
            throw InvalidResult();
        }

        var errorSpan = Span(3);
        if (words[1] == 2)
        {
            if (words[2] is < 1 or > 4 || !ValidSpan(errorSpan)
                || !AllZero(5, WordCount))
            {
                throw InvalidResult();
            }
            return new ScalarFrontendResult(false, words[2], errorSpan,
                0, 0, 0, 0, default, default, default, false, default, default, default);
        }

        var name = Span(9);
        var firstName = Span(11);
        var secondName = Span(13);
        var count = words[6];
        if (!AllZero(2, 5) || !ScalarType(words[5]) || count is < 1 or > 2
            || !ScalarType(words[7]) || (count == 1 ? words[8] != 0 : !ScalarType(words[8]))
            || !ValidSpan(name, nonempty: true) || !ValidSpan(firstName, nonempty: true)
            || (count == 1 ? secondName != default : !ValidSpan(secondName, nonempty: true))
            || words[15] is not (0 or 1))
        {
            throw InvalidResult();
        }

        var condition = Expression(16);
        var whenTrue = Expression(22);
        var whenFalse = Expression(28);
        if (!ValidExpression(whenTrue, words[5], count)
            || (words[15] == 0
                ? !AllZero(16, 22) || !AllZero(28, WordCount)
                : !ValidExpression(condition, 5, count)
                    || !ValidExpression(whenFalse, words[5], count)))
        {
            throw InvalidResult();
        }

        return new ScalarFrontendResult(true, 0, default, words[5], count,
            words[7], words[8], name, firstName, secondName,
            words[15] == 1, condition, whenTrue, whenFalse);

        TextSpan Span(int offset)
        {
            var start = words[offset];
            var length = words[offset + 1];
            if (start < 0 || start > source.Length || length < 0
                || length > source.Length - start)
            {
                throw InvalidResult();
            }
            return new TextSpan(start, length);
        }

        ScalarFrontendExpression Expression(int offset) => new(
            words[offset], words[offset + 1], words[offset + 2], words[offset + 3],
            Span(offset + 4));

        bool ValidSpan(TextSpan span, bool nonempty = false)
            => span.Start >= 0 && span.Start <= source.Length && span.Length >= (nonempty ? 1 : 0)
                && span.Length <= source.Length - span.Start;

        bool ValidExpression(ScalarFrontendExpression expression, int type, int parameters)
        {
            var operation = expression.Operation;
            var left = expression.LeftParameter;
            var right = expression.RightParameter;
            var leftType = left == 1 ? words[7] : words[8];
            var rightType = right == 1 ? words[7] : words[8];
            return expression.Type == type && ValidSpan(expression.Span, nonempty: true)
                && left >= 1 && left <= parameters
                && (operation == 0 ? right == 0 && leftType == type
                    : (operation is 57 or 58 or >= 65 and <= 70)
                        && right >= 1 && right <= parameters && leftType == rightType
                        && (operation is 57 or 58
                            ? type == leftType && leftType is >= 1 and <= 4
                            : type == 5 && leftType is >= 1 and <= 4 or 6));
        }

        bool AllZero(int first, int end)
        {
            for (var index = first; index < end; index++)
            {
                if (words[index] != 0)
                {
                    return false;
                }
            }
            return true;
        }
    }

    private static bool ScalarType(int type) => type is >= 1 and <= 6;

    private static ArgumentException InvalidResult()
        => new("Invalid scalar frontend projection.");
}

public readonly record struct ScalarFrontendExpression(
    int Type, int Operation, int LeftParameter, int RightParameter, TextSpan Span);

public readonly record struct ScalarFrontendResult(
    bool IsValid, int ErrorCode, TextSpan ErrorSpan, int ReturnType, int ParameterCount,
    int FirstType, int SecondType, TextSpan Name, TextSpan FirstName, TextSpan SecondName,
    bool HasCondition, ScalarFrontendExpression Condition, ScalarFrontendExpression WhenTrue,
    ScalarFrontendExpression WhenFalse);
