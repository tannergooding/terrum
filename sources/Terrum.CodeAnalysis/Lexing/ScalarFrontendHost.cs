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

        if (words[0] is not (1 or 2 or 3) || words[1] is not (1 or 2))
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
            return new ScalarFrontendResult(false, false, words[2], errorSpan,
                0, 0, 0, 0, default, default, default, false, default, default, default);
        }

        var name = Span(9);
        var firstName = Span(11);
        var secondName = Span(13);
        var count = words[6];
        var isPublic = words[0] >= 2 && words[2] == 1;
        if ((words[0] == 1 ? words[2] != 0 : words[2] is not (0 or 1))
            || !AllZero(3, 5) || !ScalarType(words[5]) || count is < 0 or > 2
            || (count == 0 ? words[7] != 0 : !ScalarType(words[7]))
            || (count <= 1 ? words[8] != 0 : !ScalarType(words[8]))
            || !ValidSpan(name, nonempty: true)
            || (count == 0 ? firstName != default : !ValidSpan(firstName, nonempty: true))
            || (count < 2 ? secondName != default : !ValidSpan(secondName, nonempty: true))
            || !ValidHeader()
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

        return new ScalarFrontendResult(true, isPublic, 0, default, words[5], count,
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

        bool ValidHeader()
        {
            var end = count switch { 0 => name.End, 1 => firstName.End, _ => secondName.End };
            var lexical = Lexer.Lex(SourceText.From(source.Text[..end]));
            var tokens = lexical.Tokens;
            var offset = isPublic ? 1 : 0;
            return lexical.Diagnostics.IsEmpty && tokens.Length == (count switch {
                0 => 3, 1 => 6, _ => 9,
            }) + offset
                && (!isPublic || tokens[0].Kind == TokenKind.PublicKeyword)
                && tokens[offset].Kind == TokenKind.Identifier
                && tokens[offset].Text == TypeName(words[5])
                && tokens[offset + 1].Kind == TokenKind.Identifier
                && tokens[offset + 1].Span == name
                && (count == 0 || (tokens[offset + 2].Kind == TokenKind.OpenParen
                && tokens[offset + 3].Kind == TokenKind.Identifier
                && tokens[offset + 3].Text == TypeName(words[7])
                && tokens[offset + 4].Kind == TokenKind.Identifier
                && tokens[offset + 4].Span == firstName
                && (count == 1 || (tokens[offset + 5].Kind == TokenKind.Comma
                    && tokens[offset + 6].Kind == TokenKind.Identifier
                    && tokens[offset + 6].Text == TypeName(words[8])
                    && tokens[offset + 7].Kind == TokenKind.Identifier
                    && tokens[offset + 7].Span == secondName
                    && tokens[offset + 4].Text != tokens[offset + 7].Text))));
        }

        bool ValidExpression(ScalarFrontendExpression expression, int type, int parameters)
        {
            var operation = expression.Operation;
            var left = expression.LeftParameter;
            var right = expression.RightParameter;
            var leftType = left == 1 ? words[7] : words[8];
            var rightType = right == 1 ? words[7] : words[8];
            if (operation is 157 or 158 or 257 or 258 or 357 or 358 or 457 or 458)
            {
                var literalFirst = operation >= 300;
                var parameter = operation / 100 - (literalFirst ? 2 : 0);
                var numberIndex = literalFirst ? 0 : 2;
                var nameIndex = literalFirst ? 2 : 0;
                if (words[0] != 3 || expression.Type != type || type is not (1 or 2 or 3 or 4)
                    || parameter > parameters || (parameter == 1 ? words[7] : words[8]) != type
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var text = source.Text.Substring(expression.Span.Start, expression.Span.Length);
                var lexical = Lexer.Lex(SourceText.From(text));
                var tokens = lexical.Tokens;
                var name = parameter == 1 ? firstName : secondName;
                return lexical.Diagnostics.IsEmpty && tokens.Length == 4
                    && tokens[0].Span.Start == 0
                    && tokens[nameIndex].Kind == TokenKind.Identifier
                    && tokens[nameIndex].Text == source.Text.Substring(name.Start, name.Length)
                    && tokens[1].Kind == (TokenKind)(operation % 100)
                    && tokens[numberIndex].Kind == TokenKind.NumericLiteral
                    && tokens[2].Span.End == text.Length
                    && ValidMagnitude(tokens[numberIndex].Text.AsSpan(), type, false, left, right);
            }
            if (operation == 3)
            {
                if (expression.Type != type || type is not (1 or 2 or 3 or 4 or 6)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var negative = source.Text[expression.Span.Start] == '-';
                var numberSpan = NumericSpan(source, expression.Span);
                var digits = source.Text.AsSpan(numberSpan.Start, numberSpan.Length);
                return ValidMagnitude(digits, type, negative, left, right);
            }
            if (operation is 20 or 21)
            {
                return type == 5 && expression.Type == 5 && left == 0 && right == 0
                    && ValidSpan(expression.Span, nonempty: true)
                    && source.Text.AsSpan(expression.Span.Start, expression.Span.Length)
                        .SequenceEqual((operation == 20 ? "true" : "false").AsSpan());
            }
            return expression.Type == type && ValidSpan(expression.Span, nonempty: true)
                && left >= 1 && left <= parameters
                && (operation == 0 ? right == 0 && leftType == type
                    : (operation is 57 or 58 or >= 65 and <= 70)
                        && right >= 1 && right <= parameters && leftType == rightType
                        && (operation is 57 or 58
                            ? type == leftType && leftType is >= 1 and <= 4
                            : type == 5 && leftType is >= 1 and <= 4 or 6))
                && MatchesSource(expression, left, right);
        }

        bool MatchesSource(ScalarFrontendExpression expression, int left, int right)
        {
            var leftName = left == 1 ? firstName : secondName;
            var span = expression.Span;
            if (span.Length < leftName.Length
                || !source.Text.AsSpan(span.Start, leftName.Length)
                    .SequenceEqual(source.Text.AsSpan(leftName.Start, leftName.Length)))
            {
                return false;
            }
            if (expression.Operation == 0)
            {
                return span.Length == leftName.Length;
            }

            var rightName = right == 1 ? firstName : secondName;
            var middleLength = span.Length - leftName.Length - rightName.Length;
            if (middleLength < 1 || !source.Text.AsSpan(span.End - rightName.Length, rightName.Length)
                .SequenceEqual(source.Text.AsSpan(rightName.Start, rightName.Length)))
            {
                return false;
            }
            var spelling = expression.Operation switch {
                57 => "+", 58 => "-", 65 => "==", 66 => "!=", 67 => "<",
                68 => "<=", 69 => ">", 70 => ">=", _ => throw InvalidResult(),
            };
            var middle = source.Text.AsSpan(span.Start + leftName.Length, middleLength);
            if (middle.Trim().SequenceEqual(spelling.AsSpan()))
            {
                return true;
            }
            var tokens = Lexer.Lex(SourceText.From(middle.ToString()));
            return tokens.Diagnostics.IsEmpty && tokens.Tokens.Length == 2
                && tokens.Tokens[0].Kind == (TokenKind)expression.Operation;
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

    private static bool ValidMagnitude(ReadOnlySpan<char> digits, int type, bool negative,
        int low, int high)
    {
        if (digits.IsEmpty || type is not (1 or 2 or 3 or 4 or 6)
            || (negative && type is not (1 or 3))
            || ScannerHost.IntegerDigitCount(digits) != digits.Length
            || !ScannerHost.TryParseIntegerMagnitude(digits, out var magnitude))
        {
            return false;
        }
        var based = ScannerHost.IntegerRadix(digits) != 10;
        ulong max = type switch {
            1 when based => uint.MaxValue,
            1 when negative => (ulong)int.MaxValue + 1,
            1 => int.MaxValue,
            2 => uint.MaxValue,
            3 when based => ulong.MaxValue,
            3 when negative => (ulong)long.MaxValue + 1,
            3 => long.MaxValue,
            4 => ulong.MaxValue,
            _ => ushort.MaxValue,
        };
        return magnitude <= max && magnitude == ((ulong)(uint)high << 32 | (uint)low);
    }

    private static TextSpan NumericSpan(SourceText source, TextSpan expression)
    {
        if (source.Text[expression.Start] != '-')
        {
            return expression;
        }
        var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(expression.Start, expression.Length)));
        var tokens = lexical.Tokens;
        if (!lexical.Diagnostics.IsEmpty || tokens.Length != 3
            || tokens[0].Kind != TokenKind.Minus || tokens[0].Span.Start != 0
            || tokens[1].Kind != TokenKind.NumericLiteral || tokens[1].Span.End != expression.Length)
        {
            throw InvalidResult();
        }
        return new TextSpan(expression.Start + tokens[1].Span.Start, tokens[1].Span.Length);
    }

    private static string TypeName(int code) => code switch {
        1 => "int32", 2 => "uint32", 3 => "int64", 4 => "uint64",
        5 => "bool", 6 => "char16", _ => throw InvalidResult(),
    };

    /// <summary>Builds a verifier-checked scalar library with source-declared visibility from native frontend facts.</summary>
    public static BoundProgram BindLibrary(SourceText source, ReadOnlySpan<byte> bytes,
        BoundRuntimePolicy policy)
    {
        var result = ReadResult(source, bytes);
        if (!result.IsValid)
        {
            throw InvalidResult();
        }
        var variables = ImmutableArray.CreateBuilder<BoundVariable>(result.ParameterCount);
        if (result.ParameterCount > 0)
        {
            variables.Add(new BoundVariable(Name(result.FirstName), Type(result.FirstType),
                false, result.FirstName));
        }
        if (result.ParameterCount == 2)
        {
            variables.Add(new BoundVariable(Name(result.SecondName), Type(result.SecondType),
                false, result.SecondName));
        }
        var types = variables.ToImmutable();
        var methodSpan = new TextSpan(0, source.Length);
        var returnType = Type(result.ReturnType);
        BoundStatement body;
        if (result.HasCondition)
        {
            body = new BoundIf(Expression(result.Condition),
                new BoundBlock([Return(result.WhenTrue)], result.WhenTrue.Span),
                new BoundBlock([Return(result.WhenFalse)], result.WhenFalse.Span),
                new TextSpan(result.Condition.Span.Start,
                    result.WhenFalse.Span.End - result.Condition.Span.Start));
        }
        else
        {
            body = Return(result.WhenTrue);
        }

        var function = new BoundFunction(Name(result.Name), returnType, types, types.Length,
            new BoundBlock([body], methodSpan), methodSpan) { IsInternal = !result.IsPublic };
        var program = new BoundProgram([function], -1) { RuntimePolicy = policy };
        SemanticVerifier.VerifyLibrary(program);
        return program;

        string Name(TextSpan span) => source.Text.Substring(span.Start, span.Length);

        BoundReturn Return(ScalarFrontendExpression expression)
            => new(Expression(expression), expression.Span);

        BoundExpression Expression(ScalarFrontendExpression expression)
        {
            if (expression.Operation is 157 or 158 or 257 or 258 or 357 or 358 or 457 or 458)
            {
                var literalFirst = expression.Operation >= 300;
                var parameter = expression.Operation / 100 - (literalFirst ? 2 : 0);
                var numberIndex = literalFirst ? 0 : 2;
                var nameIndex = literalFirst ? 2 : 0;
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                var value = expression.Type is 1 or 2
                    ? expression.LeftParameter
                    : unchecked((long)((ulong)(uint)expression.RightParameter << 32
                        | (uint)expression.LeftParameter));
                var leftOperand = new BoundRead(parameter - 1, types[parameter - 1].Type,
                    new TextSpan(expression.Span.Start + tokens[nameIndex].Span.Start, tokens[nameIndex].Span.Length));
                var literal = new BoundConstant(value, Type(expression.Type),
                    new TextSpan(expression.Span.Start + tokens[numberIndex].Span.Start,
                        tokens[numberIndex].Span.Length));
                return new BoundBinary((TokenKind)(expression.Operation % 100),
                    literalFirst ? literal : leftOperand, literalFirst ? leftOperand : literal,
                    Type(expression.Type), expression.Span);
            }
            if (expression.Operation == 3)
            {
                var negative = source.Text[expression.Span.Start] == '-';
                var value = expression.Type is 1 or 2
                    ? expression.LeftParameter
                    : unchecked((long)((ulong)(uint)expression.RightParameter << 32
                        | (uint)expression.LeftParameter));
                if (!negative)
                {
                    return new BoundConstant(value, Type(expression.Type), expression.Span);
                }
                var minimum = expression.Type == 1 ? (ulong)int.MaxValue + 1
                    : (ulong)long.MaxValue + 1;
                var magnitude = (ulong)(uint)expression.RightParameter << 32
                    | (uint)expression.LeftParameter;
                var operandSpan = NumericSpan(source, expression.Span);
                var digits = source.Text.AsSpan(operandSpan.Start, operandSpan.Length);
                if (ScannerHost.IntegerRadix(digits) == 10 && magnitude == minimum)
                {
                    return new BoundConstant(expression.Type == 1 ? int.MinValue : long.MinValue,
                        Type(expression.Type), expression.Span);
                }
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Minus,
                    new BoundConstant(value, Type(expression.Type), operandSpan),
                    Type(expression.Type), expression.Span));
            }
            if (expression.Operation is 20 or 21)
            {
                return new BoundConstant(expression.Operation == 20 ? 1 : 0,
                    PrimitiveType.Bool, expression.Span);
            }
            var leftName = expression.LeftParameter == 1 ? result.FirstName : result.SecondName;
            var left = new BoundRead(expression.LeftParameter - 1,
                types[expression.LeftParameter - 1].Type,
                new TextSpan(expression.Span.Start, leftName.Length));
            if (expression.Operation == 0)
            {
                return left;
            }
            var rightName = expression.RightParameter == 1 ? result.FirstName : result.SecondName;
            var right = new BoundRead(expression.RightParameter - 1,
                types[expression.RightParameter - 1].Type,
                new TextSpan(expression.Span.End - rightName.Length, rightName.Length));
            return new BoundBinary((TokenKind)expression.Operation, left, right,
                Type(expression.Type), expression.Span);
        }
    }

    private static PrimitiveType Type(int code) => code switch {
        1 => PrimitiveType.Int32, 2 => PrimitiveType.UInt32,
        3 => PrimitiveType.Int64, 4 => PrimitiveType.UInt64,
        5 => PrimitiveType.Bool, 6 => PrimitiveType.Char16,
        _ => throw InvalidResult(),
    };

    private static ArgumentException InvalidResult()
        => new("Invalid scalar frontend projection.");
}

public readonly record struct ScalarFrontendExpression(
    int Type, int Operation, int LeftParameter, int RightParameter, TextSpan Span);

public readonly record struct ScalarFrontendResult(
    bool IsValid, bool IsPublic, int ErrorCode, TextSpan ErrorSpan, int ReturnType, int ParameterCount,
    int FirstType, int SecondType, TextSpan Name, TextSpan FirstName, TextSpan SecondName,
    bool HasCondition, ScalarFrontendExpression Condition, ScalarFrontendExpression WhenTrue,
    ScalarFrontendExpression WhenFalse);
