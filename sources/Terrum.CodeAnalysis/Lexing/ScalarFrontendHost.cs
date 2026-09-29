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

        if (words[0] is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15)
            || words[1] is not (1 or 2))
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
                0, 0, 0, 0, default, default, default, false, false, default, default, default);
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
            || words[15] is not (0 or 1 or 2)
            || (words[0] < 4 && words[15] == 2)
            || (words[0] == 4 && words[15] != 2))
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
                    || !ValidExpression(whenFalse, words[5], count))
            || !ValidBody())
        {
            throw InvalidResult();
        }

        return new ScalarFrontendResult(true, isPublic, 0, default, words[5], count,
            words[7], words[8], name, firstName, secondName,
            words[15] != 0, words[15] == 2, condition, whenTrue, whenFalse);

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
            var isInternal = !isPublic && words[0] >= 3
                && tokens[0].Kind == TokenKind.InternalKeyword;
            var offset = isPublic || isInternal ? 1 : 0;
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

        bool ValidBody()
        {
            var headerEnd = count switch { 0 => name.End, 1 => firstName.End, _ => secondName.End };
            var lexical = Lexer.Lex(SourceText.From(source.Text[headerEnd..]));
            var tokens = lexical.Tokens;
            var at = 0;
            if (!lexical.Diagnostics.IsEmpty
                || !Take(count == 0 ? TokenKind.OpenParen : TokenKind.CloseParen)
                || (count == 0 && !Take(TokenKind.CloseParen))
                || !Take(TokenKind.OpenBrace))
            {
                return false;
            }

            if (words[15] != 0
                && (!Take(TokenKind.IfKeyword) || !Take(TokenKind.OpenParen)
                    || !TakeExpression(condition) || !Take(TokenKind.CloseParen)
                    || !Take(TokenKind.OpenBrace)))
            {
                return false;
            }
            if (!TakeReturn(whenTrue))
            {
                return false;
            }
            if (words[15] != 0)
            {
                if (!Take(TokenKind.CloseBrace)
                    || (words[15] == 1 && (!Take(TokenKind.ElseKeyword)
                        || !Take(TokenKind.OpenBrace)))
                    || !TakeReturn(whenFalse)
                    || (words[15] == 1 && !Take(TokenKind.CloseBrace)))
                {
                    return false;
                }
            }
            return Take(TokenKind.CloseBrace) && Take(TokenKind.EndOfFile)
                && at == tokens.Length;

            bool Take(TokenKind kind)
            {
                if (at >= tokens.Length || tokens[at].Kind != kind)
                {
                    return false;
                }
                at++;
                return true;
            }

            bool TakeReturn(ScalarFrontendExpression expression)
                => Take(TokenKind.ReturnKeyword) && TakeExpression(expression)
                    && Take(TokenKind.Semicolon);

            bool TakeExpression(ScalarFrontendExpression expression)
            {
                var groups = 0;
                while (at < tokens.Length && tokens[at].Kind == TokenKind.OpenParen
                    && headerEnd + tokens[at].Span.Start < expression.Span.Start)
                {
                    at++;
                    groups++;
                }
                if (at >= tokens.Length
                    || headerEnd + tokens[at].Span.Start != expression.Span.Start)
                {
                    return false;
                }
                while (at < tokens.Length && tokens[at].Kind != TokenKind.EndOfFile
                    && headerEnd + tokens[at].Span.End <= expression.Span.End)
                {
                    at++;
                }
                if (at == 0 || headerEnd + tokens[at - 1].Span.End != expression.Span.End)
                {
                    return false;
                }
                while (groups-- > 0)
                {
                    if (!Take(TokenKind.CloseParen))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        bool ValidExpression(ScalarFrontendExpression expression, int type, int parameters)
        {
            var operation = expression.Operation;
            var left = expression.LeftParameter;
            var right = expression.RightParameter;
            var leftType = left == 1 ? words[7] : words[8];
            var rightType = right == 1 ? words[7] : words[8];
            if (operation == 62)
            {
                if (words[0] < 3 || expression.Type != 5 || type != 5
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                if (!lexical.Diagnostics.IsEmpty
                    || !TryUnaryOperand(tokens, TokenKind.Bang, expression.Span.Length,
                        out var operandIndex))
                {
                    return false;
                }
                if (left == 0)
                {
                    return right is 20 or 21 && tokens[operandIndex].Kind == (TokenKind)right;
                }
                var name = left == 1 ? firstName : secondName;
                return right == 0 && left >= 1 && left <= parameters && leftType == 5
                    && tokens[operandIndex].Kind == TokenKind.Identifier
                    && tokens[operandIndex].Text == source.Text.Substring(name.Start, name.Length);
            }
            if (operation is 63 or 163)
            {
                if (words[0] < 3 || expression.Type != type || type is not (1 or 2 or 3 or 4)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                if (!lexical.Diagnostics.IsEmpty
                    || !TryUnaryOperand(tokens, TokenKind.Tilde, expression.Span.Length,
                        out var operandIndex))
                {
                    return false;
                }
                if (operation == 163)
                {
                    return tokens[operandIndex].Kind == TokenKind.NumericLiteral
                        && ValidMagnitude(tokens[operandIndex].Text.AsSpan(), type, false, left, right);
                }
                var name = left == 1 ? firstName : secondName;
                return right == 0 && left >= 1 && left <= parameters && leftType == type
                    && tokens[operandIndex].Kind == TokenKind.Identifier
                    && tokens[operandIndex].Text == source.Text.Substring(name.Start, name.Length);
            }
            if (operation is 57 or 58 && right == 0)
            {
                if (words[0] < 3 || expression.Type != type
                    || (operation == 58 ? type is not (1 or 3) : type is < 1 or > 4)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                var name = left == 1 ? firstName : secondName;
                if (!lexical.Diagnostics.IsEmpty
                    || !TryUnaryOperand(tokens, (TokenKind)operation, expression.Span.Length,
                        out var operandIndex))
                {
                    return false;
                }
                return tokens[operandIndex].Kind == TokenKind.Identifier
                    && right == 0 && left >= 1 && left <= parameters && leftType == type
                    && tokens[operandIndex].Text == source.Text.Substring(name.Start, name.Length);
            }
            if (operation is 657 or 658)
            {
                if (words[0] < (operation == 658 ? 7 : 3)
                    || expression.Type != type
                    || (operation == 658 ? type is not (1 or 3)
                        : type is not (1 or 2 or 3 or 4))
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                return lexical.Diagnostics.IsEmpty
                    && TryUnaryOperand(tokens, operation == 658 ? TokenKind.Minus
                        : TokenKind.Plus, expression.Span.Length,
                        out var operandIndex)
                    && tokens[operandIndex].Kind == TokenKind.NumericLiteral
                    && ValidMagnitude(tokens[operandIndex].Text.AsSpan(),
                        type, false, left, right);
            }
            if (IsNegatedBooleanBinary(operation))
            {
                return words[0] >= (operation <= 2075 ? 8
                        : operation is >= 2700 and < 3200 ? 9 : 10)
                    && expression.Type == 5 && type == 5
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        TokenKind.Bang, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(5, operation - 2000,
                        left, right, nestedSpan), 5, parameters);
            }
            if (IsComplementedIntegerBinary(operation))
            {
                return words[0] >= 11 && expression.Type == type
                    && type is >= 1 and <= 4
                    && ((operation - 4000 is not (>= 57 and <= 61 or 71 or 73 or 75))
                        || left >= 1 && right >= 1)
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        TokenKind.Tilde, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(type, operation - 4000,
                        left, right, nestedSpan), type, parameters);
            }
            if (IsSignedIntegerBinary(operation))
            {
                var minus = operation >= 8000;
                var offset = minus ? 8000 : 6000;
                var binary = operation - offset;
                return words[0] >= 12 && expression.Type == type
                    && (minus ? type is 1 or 3 : type is >= 1 and <= 4)
                    && ((binary is not (>= 57 and <= 61 or 71 or 73 or 75))
                        || left >= 1 && right >= 1)
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        minus ? TokenKind.Minus : TokenKind.Plus, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(type, binary,
                        left, right, nestedSpan), type, parameters);
            }
            if (operation is 10057 or 10058)
            {
                return words[0] >= 13 && expression.Type == type
                    && type is >= 1 and <= 4 && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        TokenKind.Tilde, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(type,
                        operation - 10000, left, right, nestedSpan), type, parameters);
            }
            if (operation is 12063 or 12163 or 14063 or 14163)
            {
                var minus = operation is 14063 or 14163;
                var nestedOperation = operation - (minus ? 14000 : 12000);
                return words[0] >= 14 && expression.Type == type
                    && (minus ? type is 1 or 3 : type is >= 1 and <= 4)
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        minus ? TokenKind.Minus : TokenKind.Plus, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(type, nestedOperation,
                        left, right, nestedSpan), type, parameters);
            }
            if (operation == 16062 || operation is >= 18000 and < 20000
                && IsNegatedBooleanBinary(operation - 16000))
            {
                return words[0] >= 15 && expression.Type == 5 && type == 5
                    && ValidSpan(expression.Span, nonempty: true)
                    && TryGroupedUnarySpan(source, expression.Span,
                        TokenKind.Bang, out var nestedSpan)
                    && ValidExpression(new ScalarFrontendExpression(5,
                        operation - 16000,
                        left, right, nestedSpan), 5, parameters);
            }
            if (operation / 100 is 7 or 9
                && operation % 100 is 65 or 66 or >= 71 and <= 75)
            {
                if (words[0] < 3 || expression.Type != 5 || type != 5
                    || left < 1 || left > parameters
                    || (left == 1 ? words[7] : words[8]) != 5
                    || right is not (20 or 21)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var literalFirst = operation >= 900;
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                var name = left == 1 ? firstName : secondName;
                return lexical.Diagnostics.IsEmpty
                    && TryBinaryOperandTokens(tokens, (TokenKind)(operation % 100),
                        expression.Span.Length, out var first, out var second)
                    && tokens[first].Kind == (literalFirst ? (TokenKind)right : TokenKind.Identifier)
                    && (literalFirst || tokens[first].Text
                        == source.Text.Substring(name.Start, name.Length))
                    && tokens[second].Kind == (literalFirst ? TokenKind.Identifier : (TokenKind)right)
                    && (!literalFirst || tokens[second].Text
                        == source.Text.Substring(name.Start, name.Length));
            }
            if (operation / 100 == 11
                && operation % 100 is 65 or 66 or >= 71 and <= 75)
            {
                if (words[0] < 3 || expression.Type != 5 || type != 5
                    || left is not (20 or 21) || right is not (20 or 21)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                return lexical.Diagnostics.IsEmpty
                    && TryBinaryOperandTokens(tokens, (TokenKind)(operation % 100),
                        expression.Span.Length, out var first, out var second)
                    && tokens[first].Kind == (TokenKind)left && tokens[second].Kind == (TokenKind)right;
            }
            if (operation / 100 == 5 && operation % 100 is >= 57 and <= 61 or 71 or 73 or 75)
            {
                if (words[0] < 3 || expression.Type != type || type is not (1 or 2)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var text = source.Text.Substring(expression.Span.Start, expression.Span.Length);
                var lexical = Lexer.Lex(SourceText.From(text));
                var tokens = lexical.Tokens;
                return lexical.Diagnostics.IsEmpty
                    && TryBinaryOperandTokens(tokens, (TokenKind)(operation % 100),
                        text.Length, out var first, out var second)
                    && tokens[first].Kind == TokenKind.NumericLiteral
                    && tokens[second].Kind == TokenKind.NumericLiteral
                    && ValidMagnitude(tokens[first].Text.AsSpan(), type, false, left, 0)
                    && ValidMagnitude(tokens[second].Text.AsSpan(), type, false, right, 0);
            }
            var positiveLiteral = operation >= 1600;
            var negativeLiteral = !positiveLiteral && operation >= 1200;
            var binaryOperation = operation - (positiveLiteral ? 1600 : negativeLiteral ? 1200 : 0);
            if (binaryOperation / 100 is >= 1 and <= 4
                && binaryOperation % 100 is >= 57 and <= 61 or >= 65 and <= 70 or 71 or 73 or 75)
            {
                var literalFirst = binaryOperation >= 300;
                var parameter = binaryOperation / 100 - (literalFirst ? 2 : 0);
                var parameterType = parameter == 1 ? words[7] : words[8];
                var comparison = binaryOperation % 100 is >= 65 and <= 70;
                if (words[0] < (positiveLiteral ? 6 : negativeLiteral ? 5 : 3)
                    || expression.Type != type
                    || (comparison ? type != 5 || parameterType is not (1 or 2 or 3 or 4 or 6)
                        : type != parameterType || parameterType is not (1 or 2 or 3 or 4))
                    || parameter > parameters || negativeLiteral && parameterType is not (1 or 3)
                    || !ValidSpan(expression.Span, nonempty: true))
                {
                    return false;
                }
                var text = source.Text.Substring(expression.Span.Start, expression.Span.Length);
                var lexical = Lexer.Lex(SourceText.From(text));
                var tokens = lexical.Tokens;
                var name = parameter == 1 ? firstName : secondName;
                return lexical.Diagnostics.IsEmpty
                    && TryBinaryOperandTokens(tokens, (TokenKind)(binaryOperation % 100),
                        text.Length, out var first, out var second, literalFirst,
                        positiveLiteral ? TokenKind.Plus
                            : negativeLiteral ? TokenKind.Minus : TokenKind.EndOfFile)
                    && tokens[literalFirst ? second : first].Kind == TokenKind.Identifier
                    && tokens[literalFirst ? second : first].Text
                        == source.Text.Substring(name.Start, name.Length)
                    && tokens[literalFirst ? first : second].Kind == TokenKind.NumericLiteral
                    && ValidMagnitude(tokens[literalFirst ? first : second].Text.AsSpan(),
                        parameterType, negativeLiteral, left, right);
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
                    : (operation is >= 57 and <= 61 or >= 65 and <= 75)
                        && right >= 1 && right <= parameters && leftType == rightType
                        && (operation is >= 57 and <= 61 or 71 or 73 or 75
                            ? type == leftType && (leftType is >= 1 and <= 4
                                || operation is 71 or 73 or 75 && leftType == 5)
                            : type == 5 && (operation is 72 or 74
                                ? leftType == 5 : leftType is >= 1 and <= 4 or 6
                                    || (operation is 65 or 66) && leftType == 5)))
                && MatchesSource(expression, left, right);
        }

        bool MatchesSource(ScalarFrontendExpression expression, int left, int right)
        {
            var leftName = left == 1 ? firstName : secondName;
            var span = expression.Span;
            var groupedLeft = source.Text[span.Start] == '(';
            if (span.Length < leftName.Length
                || (!groupedLeft && !source.Text.AsSpan(span.Start, leftName.Length)
                    .SequenceEqual(source.Text.AsSpan(leftName.Start, leftName.Length))))
            {
                return false;
            }
            if (expression.Operation == 0)
            {
                return !groupedLeft && span.Length == leftName.Length;
            }

            var rightName = right == 1 ? firstName : secondName;
            var middleLength = span.Length - leftName.Length - rightName.Length;
            var spelling = expression.Operation switch {
                57 => "+", 58 => "-", 59 => "*", 60 => "/", 61 => "%",
                65 => "==", 66 => "!=", 67 => "<",
                68 => "<=", 69 => ">", 70 => ">=", 71 => "&", 73 => "|",
                72 => "&&", 74 => "||", 75 => "^", _ => throw InvalidResult(),
            };
            if (!groupedLeft && middleLength >= 1 && source.Text.AsSpan(span.End - rightName.Length,
                    rightName.Length).SequenceEqual(source.Text.AsSpan(
                        rightName.Start, rightName.Length)))
            {
                var middle = source.Text.AsSpan(span.Start + leftName.Length, middleLength);
                if (middle.Trim().SequenceEqual(spelling.AsSpan()))
                {
                    return true;
                }
                var middleTokens = Lexer.Lex(SourceText.From(middle.ToString()));
                if (middleTokens.Diagnostics.IsEmpty && middleTokens.Tokens.Length == 2
                    && middleTokens.Tokens[0].Kind == (TokenKind)expression.Operation)
                {
                    return true;
                }
            }
            var lexical = Lexer.Lex(SourceText.From(
                source.Text.Substring(span.Start, span.Length)));
            var tokens = lexical.Tokens;
            if (!lexical.Diagnostics.IsEmpty || tokens.Length < 4
                || tokens[0].Span.Start != 0)
            {
                return false;
            }
            var leftIndex = 0;
            while (leftIndex < tokens.Length - 1
                && tokens[leftIndex].Kind == TokenKind.OpenParen)
            {
                leftIndex++;
            }
            var leftGroups = leftIndex;
            var operationIndex = leftIndex + leftGroups + 1;
            if (operationIndex >= tokens.Length - 1
                || tokens[leftIndex].Kind != TokenKind.Identifier
                || tokens[leftIndex].Text != source.Text.Substring(
                    leftName.Start, leftName.Length)
                || tokens[operationIndex].Kind != (TokenKind)expression.Operation)
            {
                return false;
            }
            for (var index = 0; index < leftGroups; index++)
            {
                if (tokens[leftIndex + 1 + index].Kind != TokenKind.CloseParen)
                {
                    return false;
                }
            }
            var operandIndex = operationIndex + 1;
            while (operandIndex < tokens.Length - 1
                && tokens[operandIndex].Kind == TokenKind.OpenParen)
            {
                operandIndex++;
            }
            var groups = operandIndex - operationIndex - 1;
            if (tokens.Length != operandIndex + groups + 2
                || tokens[operandIndex].Kind != TokenKind.Identifier
                || tokens[operandIndex].Text != source.Text.Substring(
                    rightName.Start, rightName.Length)
                || tokens[tokens.Length - 2].Span.End != span.Length)
            {
                return false;
            }
            for (var index = 0; index < groups; index++)
            {
                if (tokens[operandIndex + 1 + index].Kind != TokenKind.CloseParen)
                {
                    return false;
                }
            }
            return true;
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

    private static int UnaryOperandIndex(ImmutableArray<SyntaxToken> tokens)
    {
        var index = 1;
        while (index < tokens.Length - 1 && tokens[index].Kind == TokenKind.OpenParen)
        {
            index++;
        }
        return index;
    }

    private static bool TryBinaryOperandTokens(ImmutableArray<SyntaxToken> tokens,
        TokenKind operation, int length, out int left, out int right,
        bool literalFirst = false, TokenKind literalSign = TokenKind.EndOfFile)
    {
        var at = 0;
        left = -1;
        right = -1;
        if (tokens.Length < 4 || tokens[0].Span.Start != 0
            || !TakeOperand(literalFirst ? literalSign : TokenKind.EndOfFile, out left)
            || at >= tokens.Length - 1
            || tokens[at++].Kind != operation
            || !TakeOperand(literalFirst ? TokenKind.EndOfFile : literalSign, out right))
        {
            return false;
        }
        return at == tokens.Length - 1 && tokens[at - 1].Span.End == length;

        bool TakeOperand(TokenKind sign, out int operand)
        {
            operand = -1;
            var groups = 0;
            var foundSign = false;
            while (at < tokens.Length - 1)
            {
                if (tokens[at].Kind == TokenKind.OpenParen)
                {
                    at++;
                    groups++;
                }
                else if (!foundSign && sign != TokenKind.EndOfFile
                    && tokens[at].Kind == sign)
                {
                    at++;
                    foundSign = true;
                }
                else
                {
                    break;
                }
            }
            if (at >= tokens.Length - 1
                || (sign != TokenKind.EndOfFile && !foundSign))
            {
                return false;
            }
            operand = at++;
            for (var index = 0; index < groups; index++)
            {
                if (at >= tokens.Length - 1 || tokens[at++].Kind != TokenKind.CloseParen)
                {
                    return false;
                }
            }
            return true;
        }
    }

    private static bool TryUnaryOperand(ImmutableArray<SyntaxToken> tokens,
        TokenKind operation, int length, out int operandIndex)
    {
        operandIndex = UnaryOperandIndex(tokens);
        var groups = operandIndex - 1;
        if (tokens.Length != 3 + groups * 2 || tokens[0].Kind != operation
            || tokens[0].Span.Start != 0 || tokens[tokens.Length - 2].Span.End != length)
        {
            return false;
        }
        for (var index = 0; index < groups; index++)
        {
            if (tokens[operandIndex + 1 + index].Kind != TokenKind.CloseParen)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsNegatedBooleanBinary(int operation)
    {
        if (operation >= 4000)
        {
            return false;
        }
        if (operation is >= 2065 and <= 2075)
        {
            return true;
        }
        var nested = operation - 2000;
        if ((nested / 100 is 7 or 9 or 11)
            && (nested % 100 is 65 or 66 or >= 71 and <= 75))
        {
            return true;
        }
        var comparison = nested >= 1600 ? nested - 1600
            : nested >= 1200 ? nested - 1200 : nested;
        return (comparison / 100 is >= 1 and <= 4)
            && (comparison % 100 is >= 65 and <= 70);
    }

    private static bool IsComplementedIntegerBinary(int operation)
    {
        if (operation is < 4000 or >= 6000)
        {
            return false;
        }
        var nested = operation - 4000;
        var binary = nested >= 1600 ? nested - 1600
            : nested >= 1200 ? nested - 1200 : nested;
        return binary is >= 57 and <= 61 or 71 or 73 or 75
            || (binary / 100 is >= 1 and <= 5)
                && (binary % 100 is >= 57 and <= 61 or 71 or 73 or 75);
    }

    private static bool IsSignedIntegerBinary(int operation)
    {
        if (operation is < 6000 or >= 10000)
        {
            return false;
        }
        var offset = operation >= 8000 ? 8000 : 6000;
        return IsComplementedIntegerBinary(operation - offset + 4000);
    }

    private static bool TryGroupedUnarySpan(SourceText source, TextSpan span,
        TokenKind prefix, out TextSpan nested)
    {
        nested = default;
        var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(span.Start, span.Length)));
        var tokens = lexical.Tokens;
        if (!lexical.Diagnostics.IsEmpty || tokens.Length < 6
            || tokens[0].Kind != prefix || tokens[0].Span.Start != 0)
        {
            return false;
        }
        var last = tokens.Length - 2;
        if (tokens[1].Kind != TokenKind.OpenParen
            || tokens[last].Kind != TokenKind.CloseParen
            || tokens[last].Span.End != span.Length)
        {
            return false;
        }
        var openings = new int[tokens.Length];
        var closing = new int[tokens.Length];
        var depth = 0;
        for (var index = 1; index <= last; index++)
        {
            if (tokens[index].Kind == TokenKind.OpenParen)
            {
                openings[depth++] = index;
            }
            else if (tokens[index].Kind == TokenKind.CloseParen)
            {
                if (depth == 0)
                {
                    return false;
                }
                closing[openings[--depth]] = index;
            }
            if (depth == 0 && index < last)
            {
                return false;
            }
        }
        if (depth != 0)
        {
            return false;
        }
        var at = 2;
        last--;
        while (at < last && tokens[at].Kind == TokenKind.OpenParen
            && closing[at] == last)
        {
            at++;
            last--;
        }
        nested = new TextSpan(span.Start + tokens[at].Span.Start,
            tokens[last].Span.End - tokens[at].Span.Start);
        return true;
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
        ImmutableArray<BoundStatement> statements;
        if (result.HasCondition)
        {
            var conditional = new BoundIf(Expression(result.Condition),
                new BoundBlock([Return(result.WhenTrue)], result.WhenTrue.Span),
                result.HasFallthrough ? null
                    : new BoundBlock([Return(result.WhenFalse)], result.WhenFalse.Span),
                new TextSpan(result.Condition.Span.Start,
                    (result.HasFallthrough ? result.WhenTrue.Span.End : result.WhenFalse.Span.End)
                        - result.Condition.Span.Start));
            statements = result.HasFallthrough
                ? [conditional, Return(result.WhenFalse)] : [conditional];
        }
        else
        {
            statements = [Return(result.WhenTrue)];
        }

        var function = new BoundFunction(Name(result.Name), returnType, types, types.Length,
            new BoundBlock(statements, methodSpan), methodSpan) { IsInternal = !result.IsPublic };
        var program = new BoundProgram([function], -1) { RuntimePolicy = policy };
        SemanticVerifier.VerifyLibrary(program);
        return program;

        string Name(TextSpan span) => source.Text.Substring(span.Start, span.Length);

        BoundReturn Return(ScalarFrontendExpression expression)
            => new(Expression(expression), expression.Span);

        BoundExpression Expression(ScalarFrontendExpression expression)
        {
            if (expression.Operation == 62)
            {
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var token = lexical.Tokens[UnaryOperandIndex(lexical.Tokens)];
                var operandSpan = new TextSpan(expression.Span.Start + token.Span.Start, token.Span.Length);
                BoundExpression operand = expression.LeftParameter == 0
                    ? new BoundConstant(expression.RightParameter == 20 ? 1 : 0,
                        PrimitiveType.Bool, operandSpan)
                    : new BoundRead(expression.LeftParameter - 1, PrimitiveType.Bool, operandSpan);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Bang, operand,
                    PrimitiveType.Bool, expression.Span));
            }
            if (expression.Operation is 63 or 163)
            {
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var token = lexical.Tokens[UnaryOperandIndex(lexical.Tokens)];
                var operandSpan = new TextSpan(expression.Span.Start + token.Span.Start, token.Span.Length);
                var type = Type(expression.Type);
                BoundExpression operand;
                if (expression.Operation == 63)
                {
                    operand = new BoundRead(expression.LeftParameter - 1, type, operandSpan);
                }
                else
                {
                    var value = expression.Type is 1 or 2
                        ? expression.LeftParameter
                        : unchecked((long)((ulong)(uint)expression.RightParameter << 32
                            | (uint)expression.LeftParameter));
                    operand = new BoundConstant(value, type, operandSpan);
                }
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Tilde, operand,
                    type, expression.Span));
            }
            if (expression.Operation is 57 or 58 && expression.RightParameter == 0)
            {
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var token = lexical.Tokens[UnaryOperandIndex(lexical.Tokens)];
                var operandSpan = new TextSpan(expression.Span.Start + token.Span.Start, token.Span.Length);
                var type = Type(expression.Type);
                return new BoundUnary((TokenKind)expression.Operation,
                    new BoundRead(expression.LeftParameter - 1, type, operandSpan),
                    type, expression.Span);
            }
            if (expression.Operation is 657 or 658)
            {
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var token = lexical.Tokens[UnaryOperandIndex(lexical.Tokens)];
                var operandSpan = new TextSpan(expression.Span.Start + token.Span.Start, token.Span.Length);
                var type = Type(expression.Type);
                var value = expression.Type is 1 or 2
                    ? expression.LeftParameter
                    : unchecked((long)((ulong)(uint)expression.RightParameter << 32
                        | (uint)expression.LeftParameter));
                return Binder.FoldScalarConstant(new BoundUnary(expression.Operation == 658
                    ? TokenKind.Minus : TokenKind.Plus,
                    new BoundConstant(value, type, operandSpan), type, expression.Span));
            }
            if (IsNegatedBooleanBinary(expression.Operation))
            {
                if (!TryGroupedUnarySpan(source, expression.Span,
                    TokenKind.Bang, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var nested = new ScalarFrontendExpression(5, expression.Operation - 2000,
                    expression.LeftParameter, expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Bang,
                    Expression(nested), PrimitiveType.Bool, expression.Span));
            }
            if (IsComplementedIntegerBinary(expression.Operation))
            {
                if (!TryGroupedUnarySpan(source, expression.Span,
                    TokenKind.Tilde, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var nested = new ScalarFrontendExpression(expression.Type,
                    expression.Operation - 4000, expression.LeftParameter,
                    expression.RightParameter, nestedSpan);
                var type = Type(expression.Type);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Tilde,
                    Expression(nested), type, expression.Span));
            }
            if (IsSignedIntegerBinary(expression.Operation))
            {
                var minus = expression.Operation >= 8000;
                var offset = minus ? 8000 : 6000;
                if (!TryGroupedUnarySpan(source, expression.Span,
                    minus ? TokenKind.Minus : TokenKind.Plus, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var nested = new ScalarFrontendExpression(expression.Type,
                    expression.Operation - offset, expression.LeftParameter,
                    expression.RightParameter, nestedSpan);
                var type = Type(expression.Type);
                return Binder.FoldScalarConstant(new BoundUnary(minus
                    ? TokenKind.Minus : TokenKind.Plus, Expression(nested),
                    type, expression.Span));
            }
            if (expression.Operation is 10057 or 10058)
            {
                if (!TryGroupedUnarySpan(source, expression.Span,
                    TokenKind.Tilde, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var type = Type(expression.Type);
                var nested = new ScalarFrontendExpression(expression.Type,
                    expression.Operation - 10000, expression.LeftParameter,
                    expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Tilde,
                    Expression(nested), type, expression.Span));
            }
            if (expression.Operation is 12063 or 12163 or 14063 or 14163)
            {
                var minus = expression.Operation is 14063 or 14163;
                if (!TryGroupedUnarySpan(source, expression.Span,
                    minus ? TokenKind.Minus : TokenKind.Plus, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var type = Type(expression.Type);
                var nested = new ScalarFrontendExpression(expression.Type,
                    expression.Operation - (minus ? 14000 : 12000),
                    expression.LeftParameter, expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(minus
                    ? TokenKind.Minus : TokenKind.Plus,
                    Expression(nested), type, expression.Span));
            }
            if (expression.Operation == 16062
                || expression.Operation is >= 18000 and < 20000
                    && IsNegatedBooleanBinary(expression.Operation - 16000))
            {
                if (!TryGroupedUnarySpan(source, expression.Span,
                    TokenKind.Bang, out var nestedSpan))
                {
                    throw InvalidResult();
                }
                var nested = new ScalarFrontendExpression(5,
                    expression.Operation - 16000,
                    expression.LeftParameter, expression.RightParameter, nestedSpan);
                return Binder.FoldScalarConstant(new BoundUnary(TokenKind.Bang,
                    Expression(nested), PrimitiveType.Bool, expression.Span));
            }
            if (expression.Operation / 100 is 7 or 9
                && expression.Operation % 100 is 65 or 66 or >= 71 and <= 75)
            {
                var literalFirst = expression.Operation >= 900;
                var tokens = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length))).Tokens;
                if (!TryBinaryOperandTokens(tokens, (TokenKind)(expression.Operation % 100),
                    expression.Span.Length, out var firstIndex, out var secondIndex))
                {
                    throw InvalidResult();
                }
                var first = new TextSpan(expression.Span.Start + tokens[firstIndex].Span.Start,
                    tokens[firstIndex].Span.Length);
                var second = new TextSpan(expression.Span.Start + tokens[secondIndex].Span.Start,
                    tokens[secondIndex].Span.Length);
                var parameter = new BoundRead(expression.LeftParameter - 1,
                    PrimitiveType.Bool, literalFirst ? second : first);
                var literal = new BoundConstant(expression.RightParameter == 20 ? 1 : 0,
                    PrimitiveType.Bool, literalFirst ? first : second);
                return new BoundBinary((TokenKind)(expression.Operation % 100),
                    literalFirst ? literal : parameter, literalFirst ? parameter : literal,
                    PrimitiveType.Bool, expression.Span);
            }
            if (expression.Operation / 100 == 11
                && expression.Operation % 100 is 65 or 66 or >= 71 and <= 75)
            {
                var tokens = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length))).Tokens;
                if (!TryBinaryOperandTokens(tokens, (TokenKind)(expression.Operation % 100),
                    expression.Span.Length, out var firstIndex, out var secondIndex))
                {
                    throw InvalidResult();
                }
                var first = new BoundConstant(expression.LeftParameter == 20 ? 1 : 0,
                    PrimitiveType.Bool, new TextSpan(expression.Span.Start + tokens[firstIndex].Span.Start,
                        tokens[firstIndex].Span.Length));
                var second = new BoundConstant(expression.RightParameter == 20 ? 1 : 0,
                    PrimitiveType.Bool, new TextSpan(expression.Span.Start + tokens[secondIndex].Span.Start,
                        tokens[secondIndex].Span.Length));
                return Binder.FoldScalarConstant(new BoundBinary(
                    (TokenKind)(expression.Operation % 100), first, second,
                    PrimitiveType.Bool, expression.Span));
            }
            if (expression.Operation / 100 == 5
                && expression.Operation % 100 is >= 57 and <= 61 or 71 or 73 or 75)
            {
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                var type = Type(expression.Type);
                var first = new BoundConstant(expression.LeftParameter, type,
                    new TextSpan(expression.Span.Start + tokens[0].Span.Start, tokens[0].Span.Length));
                var second = new BoundConstant(expression.RightParameter, type,
                    new TextSpan(expression.Span.Start + tokens[2].Span.Start, tokens[2].Span.Length));
                return Binder.FoldScalarConstant(new BoundBinary(
                    (TokenKind)(expression.Operation % 100), first, second, type,
                    expression.Span));
            }
            var positiveLiteral = expression.Operation >= 1600;
            var negativeLiteral = !positiveLiteral && expression.Operation >= 1200;
            var binaryOperation = expression.Operation
                - (positiveLiteral ? 1600 : negativeLiteral ? 1200 : 0);
            if (binaryOperation / 100 is >= 1 and <= 4
                && binaryOperation % 100 is >= 57 and <= 61 or >= 65 and <= 70
                    or 71 or 73 or 75)
            {
                var literalFirst = binaryOperation >= 300;
                var parameter = binaryOperation / 100 - (literalFirst ? 2 : 0);
                var lexical = Lexer.Lex(SourceText.From(source.Text.Substring(
                    expression.Span.Start, expression.Span.Length)));
                var tokens = lexical.Tokens;
                if (!TryBinaryOperandTokens(tokens, (TokenKind)(binaryOperation % 100),
                    expression.Span.Length, out var first, out var second, literalFirst,
                    positiveLiteral ? TokenKind.Plus
                        : negativeLiteral ? TokenKind.Minus : TokenKind.EndOfFile))
                {
                    throw InvalidResult();
                }
                var numberIndex = literalFirst ? first : second;
                var nameIndex = literalFirst ? second : first;
                var parameterType = types[parameter - 1].Type;
                var value = parameterType is PrimitiveType.Int32 or PrimitiveType.UInt32
                    ? expression.LeftParameter
                    : unchecked((long)((ulong)(uint)expression.RightParameter << 32
                        | (uint)expression.LeftParameter));
                var leftOperand = new BoundRead(parameter - 1, parameterType,
                    new TextSpan(expression.Span.Start + tokens[nameIndex].Span.Start, tokens[nameIndex].Span.Length));
                var numberSpan = new TextSpan(expression.Span.Start + tokens[numberIndex].Span.Start,
                    tokens[numberIndex].Span.Length);
                BoundExpression literal = new BoundConstant(value, parameterType, numberSpan);
                if (negativeLiteral || positiveLiteral)
                {
                    var signedSpan = new TextSpan(expression.Span.Start + tokens[numberIndex - 1].Span.Start,
                        tokens[numberIndex].Span.End - tokens[numberIndex - 1].Span.Start);
                    var magnitude = (ulong)(uint)expression.RightParameter << 32
                        | (uint)expression.LeftParameter;
                    var minimum = parameterType == PrimitiveType.Int32
                        ? (ulong)int.MaxValue + 1 : (ulong)long.MaxValue + 1;
                    literal = negativeLiteral
                        && ScannerHost.IntegerRadix(tokens[numberIndex].Text.AsSpan()) == 10
                        && magnitude == minimum
                        ? new BoundConstant(parameterType == PrimitiveType.Int32
                            ? int.MinValue : long.MinValue, parameterType, signedSpan)
                        : Binder.FoldScalarConstant(new BoundUnary(
                            negativeLiteral ? TokenKind.Minus : TokenKind.Plus, literal,
                            parameterType, signedSpan));
                }
                return new BoundBinary((TokenKind)(binaryOperation % 100),
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
    bool HasCondition, bool HasFallthrough, ScalarFrontendExpression Condition, ScalarFrontendExpression WhenTrue,
    ScalarFrontendExpression WhenFalse);
