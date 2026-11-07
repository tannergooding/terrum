// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Text;
using System.Threading;

namespace Terrum.CodeAnalysis.Lexing;

internal static class StringLiteralDecoder
{
    internal enum RawTextErrorKind
    {
        None,
        Invalid,
        Unterminated,
    }

    internal enum RawTextHeaderKind
    {
        None,
        Valid,
        Invalid,
    }

    internal static bool TryDecode(ReadOnlySpan<char> text, out string value)
    {
        value = string.Empty;
        if (text.Length >= 3 && text[0] == '"' && text[1] == '"' && text[2] == '"')
        {
            if (TryReadRawText(text, true, default, out var consumedLength, out var rawValue,
                out _, out _, out _) && consumedLength == text.Length)
            {
                value = rawValue;
                return true;
            }
            return false;
        }

        if (text.Length < 2 || text[0] != '"' || text[^1] != '"')
        {
            return false;
        }

        var builder = new StringBuilder(text.Length - 2);
        var position = 1;
        var end = text.Length - 1;
        while (position < end)
        {
            var character = text[position++];
            if (character != '\\')
            {
                if (char.IsHighSurrogate(character))
                {
                    if (position >= end || !char.IsLowSurrogate(text[position]))
                    {
                        return false;
                    }
                    _ = builder.Append(character);
                    _ = builder.Append(text[position++]);
                }
                else if (char.IsLowSurrogate(character))
                {
                    return false;
                }
                else
                {
                    _ = builder.Append(character);
                }
                continue;
            }

            if (position >= end)
            {
                return false;
            }

            switch (text[position++])
            {
                case 'n':
                    _ = builder.Append('\n');
                    break;
                case 'r':
                    _ = builder.Append('\r');
                    break;
                case 't':
                    _ = builder.Append('\t');
                    break;
                case '\\':
                    _ = builder.Append('\\');
                    break;
                case '"':
                    _ = builder.Append('"');
                    break;
                case '0':
                    _ = builder.Append('\0');
                    break;
                case 'u':
                    if (!TryReadEscape(text, ref position, end, 4, out var shortScalar))
                    {
                        return false;
                    }
                    _ = builder.Append(char.ConvertFromUtf32(shortScalar));
                    break;
                case 'U':
                    if (!TryReadEscape(text, ref position, end, 8, out var longScalar))
                    {
                        return false;
                    }
                    _ = builder.Append(char.ConvertFromUtf32(longScalar));
                    break;
                default:
                    return false;
            }
        }

        value = builder.ToString();
        return true;
    }

    internal static bool TryReadRawText(ReadOnlySpan<char> source, bool decodeValue,
        CancellationToken cancellationToken, out int consumedLength, out string value,
        out RawTextErrorKind error, out int errorStart, out int errorLength)
    {
        consumedLength = 0;
        value = string.Empty;
        error = RawTextErrorKind.None;
        errorStart = -1;
        errorLength = 0;

        var fenceLength = CountQuotes(source, 0, source.Length);
        if (fenceLength < 3)
        {
            error = RawTextErrorKind.Invalid;
            errorStart = 0;
            errorLength = fenceLength;
            return false;
        }

        var headerKind = ReadOpeningHeader(source, fenceLength, out var bodyStart, out var newlineDirective,
            out var headerErrorStart, out var headerErrorLength);
        if (headerKind != RawTextHeaderKind.None)
        {
            return TryReadFramedRawText(source, fenceLength, bodyStart, newlineDirective,
                headerKind == RawTextHeaderKind.Invalid, headerErrorStart, headerErrorLength,
                decodeValue, cancellationToken, out consumedLength, out value, out error, out errorStart,
                out errorLength);
        }

        return TryReadSingleLineRawText(source, fenceLength, decodeValue, cancellationToken,
            out consumedLength, out value, out error, out errorStart, out errorLength);
    }

    private static bool TryReadSingleLineRawText(ReadOnlySpan<char> source, int fenceLength, bool decodeValue,
        CancellationToken cancellationToken, out int consumedLength, out string value,
        out RawTextErrorKind error, out int errorStart, out int errorLength)
    {
        var builder = decodeValue ? new StringBuilder(source.Length) : null;
        var position = fenceLength;
        var invalid = false;
        errorStart = -1;
        errorLength = 0;

        while (position < source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsNewline(source[position]))
            {
                consumedLength = position;
                value = string.Empty;
                error = invalid ? RawTextErrorKind.Invalid : RawTextErrorKind.Unterminated;
                if (!invalid)
                {
                    errorStart = position;
                    errorLength = 0;
                }
                return false;
            }

            if (source[position] == '"')
            {
                var quoteRun = CountQuotes(source, position, source.Length);
                if (quoteRun == fenceLength)
                {
                    consumedLength = position + quoteRun;
                    var content = source.Slice(fenceLength, position - fenceLength);
                    if (content.Length != 0 && (content[0] == '"' || content[^1] == '"'))
                    {
                        SetInvalid(ref invalid, ref errorStart, ref errorLength, fenceLength, content.Length);
                    }
                    var valid = AppendRawContent(content, fenceLength, builder,
                        ref invalid, ref errorStart, ref errorLength);
                    value = valid ? builder?.ToString() ?? string.Empty : string.Empty;
                    error = invalid ? RawTextErrorKind.Invalid : RawTextErrorKind.None;
                    return !invalid;
                }

                if (quoteRun > fenceLength)
                {
                    SetInvalid(ref invalid, ref errorStart, ref errorLength, position, quoteRun);
                }
                position += quoteRun;
                continue;
            }

            position++;
        }

        consumedLength = source.Length;
        value = string.Empty;
        error = invalid ? RawTextErrorKind.Invalid : RawTextErrorKind.Unterminated;
        if (!invalid)
        {
            errorStart = source.Length;
        }
        return false;
    }

    private static bool TryReadFramedRawText(ReadOnlySpan<char> source, int fenceLength, int bodyStart,
        string? newlineDirective, bool invalidHeader, int headerErrorStart, int headerErrorLength,
        bool decodeValue, CancellationToken cancellationToken,
        out int consumedLength, out string value, out RawTextErrorKind error, out int errorStart, out int errorLength)
    {
        var invalid = invalidHeader;
        errorStart = invalidHeader ? headerErrorStart : -1;
        errorLength = invalidHeader ? headerErrorLength : 0;
        var previousBreakStart = -1;
        var lineStart = bodyStart;

        while (lineStart <= source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indentEnd = lineStart;
            while (indentEnd < source.Length && IsHorizontalWhitespace(source[indentEnd]))
            {
                indentEnd++;
            }

            var quoteRun = CountQuotes(source, indentEnd, source.Length);
            if (quoteRun == fenceLength)
            {
                consumedLength = indentEnd + quoteRun;
                var bodyEnd = previousBreakStart < 0 ? bodyStart : previousBreakStart;
                if (TryBuildFramedValue(source, bodyStart, bodyEnd, source.Slice(lineStart, indentEnd - lineStart),
                    newlineDirective, decodeValue, cancellationToken, ref invalid, ref errorStart, ref errorLength,
                    out value) && !invalid)
                {
                    error = RawTextErrorKind.None;
                    return true;
                }

                value = string.Empty;
                error = RawTextErrorKind.Invalid;
                return false;
            }

            var lineEnd = FindLineBreak(source, lineStart);
            if (lineEnd == source.Length)
            {
                CheckQuoteRuns(source, lineStart, lineEnd, fenceLength, cancellationToken,
                    ref invalid, ref errorStart, ref errorLength);
                consumedLength = source.Length;
                value = string.Empty;
                error = invalid ? RawTextErrorKind.Invalid : RawTextErrorKind.Unterminated;
                if (!invalid)
                {
                    errorStart = source.Length;
                }
                return false;
            }

            CheckQuoteRuns(source, lineStart, lineEnd, fenceLength, cancellationToken,
                ref invalid, ref errorStart, ref errorLength);
            previousBreakStart = lineEnd;
            lineStart = lineEnd + LineBreakLength(source, lineEnd);
        }

        consumedLength = source.Length;
        value = string.Empty;
        error = invalid ? RawTextErrorKind.Invalid : RawTextErrorKind.Unterminated;
        if (!invalid)
        {
            errorStart = source.Length;
        }
        return false;
    }

    private static bool TryBuildFramedValue(ReadOnlySpan<char> source, int bodyStart, int bodyEnd,
        ReadOnlySpan<char> indentation, string? newlineDirective, bool decodeValue,
        CancellationToken cancellationToken, ref bool invalid, ref int errorStart, ref int errorLength,
        out string value)
    {
        var builder = decodeValue ? new StringBuilder(bodyEnd - bodyStart) : null;
        var lineStart = bodyStart;
        while (lineStart < bodyEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lineEnd = FindLineBreak(source, lineStart);
            if (lineEnd > bodyEnd)
            {
                lineEnd = bodyEnd;
            }

            var line = source.Slice(lineStart, lineEnd - lineStart);
            var prefixLength = indentation.Length;
            if (IsWhitespaceOnly(line))
            {
                if (!StartsWith(line, indentation))
                {
                    if (IsPrefixOf(line, indentation))
                    {
                        prefixLength = line.Length;
                    }
                    else
                    {
                        SetInvalid(ref invalid, ref errorStart, ref errorLength, lineStart, line.Length);
                    }
                }
            }
            else if (!StartsWith(line, indentation))
            {
                SetInvalid(ref invalid, ref errorStart, ref errorLength, lineStart, line.Length);
            }

            if (prefixLength <= line.Length && line.Slice(prefixLength) is var content)
            {
                _ = AppendRawContent(content, lineStart + prefixLength, builder,
                    ref invalid, ref errorStart, ref errorLength);
            }

            if (lineEnd == bodyEnd)
            {
                break;
            }

            var breakLength = LineBreakLength(source, lineEnd);
            if (lineEnd + breakLength > bodyEnd)
            {
                break;
            }
            _ = builder?.Append(newlineDirective ?? source.Slice(lineEnd, breakLength).ToString());
            lineStart = lineEnd + breakLength;
        }

        value = invalid ? string.Empty : builder?.ToString() ?? string.Empty;
        return !invalid;
    }

    internal static RawTextHeaderKind ReadOpeningHeader(ReadOnlySpan<char> source, int position,
        out int bodyStart, out string? newlineDirective, out int errorStart, out int errorLength)
    {
        bodyStart = 0;
        newlineDirective = null;
        errorStart = -1;
        errorLength = 0;

        var newlineStart = FindLineBreak(source, position);
        if (newlineStart == source.Length)
        {
            return RawTextHeaderKind.None;
        }

        var cursor = position;
        while (cursor < newlineStart && IsHorizontalWhitespace(source[cursor]))
        {
            cursor++;
        }

        if (cursor == newlineStart)
        {
            bodyStart = newlineStart + LineBreakLength(source, newlineStart);
            return RawTextHeaderKind.Valid;
        }

        if (source[cursor] != '\\')
        {
            return RawTextHeaderKind.None;
        }

        var directiveStart = cursor;
        if (!TryReadNewlineDirective(source, ref cursor, newlineStart, out newlineDirective))
        {
            bodyStart = newlineStart + LineBreakLength(source, newlineStart);
            errorStart = directiveStart;
            errorLength = newlineStart - directiveStart;
            return RawTextHeaderKind.Invalid;
        }

        while (cursor < newlineStart && IsHorizontalWhitespace(source[cursor]))
        {
            cursor++;
        }

        bodyStart = newlineStart + LineBreakLength(source, newlineStart);
        if (cursor != newlineStart)
        {
            newlineDirective = null;
            errorStart = directiveStart;
            errorLength = newlineStart - directiveStart;
            return RawTextHeaderKind.Invalid;
        }

        return RawTextHeaderKind.Valid;
    }

    private static bool TryReadNewlineDirective(ReadOnlySpan<char> source, ref int position, int end,
        out string? newlineDirective)
    {
        newlineDirective = null;
        if (!TryReadEscape(source, ref position, end, out var firstScalar))
        {
            return false;
        }

        if (position < end && source[position] == '\\')
        {
            if (!TryReadEscape(source, ref position, end, out var secondScalar)
                || firstScalar != '\r' || secondScalar != '\n')
            {
                return false;
            }
            newlineDirective = "\r\n";
            return true;
        }

        newlineDirective = firstScalar switch {
            '\n' => "\n",
            '\r' => "\r",
            '\v' => "\v",
            '\f' => "\f",
            '\u0085' => "\u0085",
            '\u2028' => "\u2028",
            '\u2029' => "\u2029",
            _ => null,
        };
        return newlineDirective is not null;
    }

    private static bool TryReadEscape(ReadOnlySpan<char> text, ref int position, int end, out int scalar)
    {
        scalar = 0;
        if (position >= end || text[position++] != '\\' || position >= end)
        {
            return false;
        }

        switch (text[position++])
        {
            case 'n':
                scalar = '\n';
                return true;
            case 'r':
                scalar = '\r';
                return true;
            case 't':
                scalar = '\t';
                return true;
            case '\\':
                scalar = '\\';
                return true;
            case '"':
                scalar = '"';
                return true;
            case '0':
                scalar = '\0';
                return true;
            case 'u':
                return TryReadEscape(text, ref position, end, 4, out scalar);
            case 'U':
                return TryReadEscape(text, ref position, end, 8, out scalar);
            default:
                return false;
        }
    }

    private static bool AppendRawContent(ReadOnlySpan<char> content, int sourceStart, StringBuilder? builder,
        ref bool invalid, ref int errorStart, ref int errorLength)
    {
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 < content.Length && char.IsLowSurrogate(content[index + 1]))
                {
                    index++;
                    continue;
                }
                SetInvalid(ref invalid, ref errorStart, ref errorLength, sourceStart + index, 1);
            }
            else if (char.IsLowSurrogate(character))
            {
                SetInvalid(ref invalid, ref errorStart, ref errorLength, sourceStart + index, 1);
            }
        }

        _ = builder?.Append(content);
        return !invalid;
    }

    private static void CheckQuoteRuns(ReadOnlySpan<char> source, int start, int end, int fenceLength,
        CancellationToken cancellationToken, ref bool invalid, ref int errorStart, ref int errorLength)
    {
        var position = start;
        while (position < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source[position] != '"')
            {
                position++;
                continue;
            }

            var quoteRun = CountQuotes(source, position, end);
            if (quoteRun >= fenceLength)
            {
                SetInvalid(ref invalid, ref errorStart, ref errorLength, position, quoteRun);
            }
            position += quoteRun;
        }
    }

    private static void SetInvalid(ref bool invalid, ref int errorStart, ref int errorLength, int start, int length)
    {
        if (!invalid)
        {
            invalid = true;
            errorStart = start;
            errorLength = length;
        }
    }

    private static bool IsWhitespaceOnly(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }
        return true;
    }

    private static bool StartsWith(ReadOnlySpan<char> text, ReadOnlySpan<char> prefix)
        => text.Length >= prefix.Length && text[..prefix.Length].SequenceEqual(prefix);

    private static bool IsPrefixOf(ReadOnlySpan<char> text, ReadOnlySpan<char> prefix)
        => text.Length <= prefix.Length && prefix[..text.Length].SequenceEqual(text);

    private static int FindLineBreak(ReadOnlySpan<char> text, int position)
    {
        while (position < text.Length && !IsNewline(text[position]))
        {
            position++;
        }
        return position;
    }

    private static int CountQuotes(ReadOnlySpan<char> text, int position, int end)
    {
        var start = position;
        while (position < end && text[position] == '"')
        {
            position++;
        }
        return position - start;
    }

    private static int LineBreakLength(ReadOnlySpan<char> text, int position)
        => text[position] == '\r' && position + 1 < text.Length && text[position + 1] == '\n' ? 2 : 1;

    private static bool IsNewline(char value) => value is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';

    internal static bool IsHorizontalWhitespace(char value) =>
        value is '\t' or ' ' or '\u00A0' or '\u1680'
            or (>= '\u2000' and <= '\u200A') or '\u202F' or '\u205F' or '\u3000';

    private static bool TryReadEscape(ReadOnlySpan<char> text, ref int position, int end, int digitCount,
        out int scalar)
    {
        scalar = 0;
        if (end - position < digitCount)
        {
            return false;
        }

        for (var digit = 0; digit < digitCount; digit++)
        {
            var character = text[position++];
            if (character is >= '0' and <= '9')
            {
                scalar = (scalar << 4) | (character - '0');
            }
            else if (character is >= 'a' and <= 'f')
            {
                scalar = (scalar << 4) | (character - 'a' + 10);
            }
            else if (character is >= 'A' and <= 'F')
            {
                scalar = (scalar << 4) | (character - 'A' + 10);
            }
            else
            {
                return false;
            }
        }

        return Rune.IsValid(scalar);
    }
}
