// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers;
using System.Text;

namespace Terrum.CodeAnalysis.Lexing;

internal enum CharacterLiteralError
{
    InvalidEscape,
    InvalidScalar,
    WrongScalarCount,
}

internal static class CharacterLiteralDecoder
{
    internal static bool TryDecode(ReadOnlySpan<char> text, out int scalar)
        => TryDecode(text, out scalar, out _, out _, out _);

    internal static bool TryDecode(ReadOnlySpan<char> text, out int scalar,
        out CharacterLiteralError error, out int errorStart, out int errorLength)
    {
        scalar = 0;
        error = CharacterLiteralError.WrongScalarCount;
        errorStart = 1;
        errorLength = 0;
        if (text.Length < 2 || text[0] != '\'' || text[^1] != '\'')
        {
            return false;
        }

        var content = text[1..^1];
        var position = 0;
        var count = 0;
        while (position < content.Length)
        {
            var scalarStart = position;
            int value;
            if (content[position] == '\\')
            {
                if (!TryDecodeEscape(content, ref position, out value))
                {
                    error = CharacterLiteralError.InvalidEscape;
                    errorStart = scalarStart + 1;
                    errorLength = Math.Max(1, position - scalarStart);
                    return false;
                }
            }
            else
            {
                var codeUnit = content[position];
                if (!char.IsSurrogate(codeUnit))
                {
                    value = codeUnit;
                    position++;
                }
                else
                {
                    var status = Rune.DecodeFromUtf16(content[position..], out var rune, out var width);
                    if (status != OperationStatus.Done)
                    {
                        error = CharacterLiteralError.InvalidScalar;
                        errorStart = scalarStart + 1;
                        errorLength = 1;
                        return false;
                    }
                    value = rune.Value;
                    position += width;
                }
            }

            if (count++ != 0)
            {
                error = CharacterLiteralError.WrongScalarCount;
                errorStart = scalarStart + 1;
                errorLength = position - scalarStart;
                return false;
            }
            scalar = value;
        }

        if (count == 0)
        {
            errorStart = 0;
            errorLength = text.Length;
            return false;
        }
        return true;
    }

    private static bool TryDecodeEscape(ReadOnlySpan<char> content, ref int position, out int scalar)
    {
        position++;
        if (position == content.Length)
        {
            scalar = 0;
            return false;
        }

        var escape = content[position++];
        scalar = escape switch {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            '\\' => '\\',
            '"' => '"',
            '\'' => '\'',
            '0' => '\0',
            _ => -1,
        };
        if (scalar >= 0)
        {
            return true;
        }

        if (escape is not ('u' or 'U'))
        {
            return false;
        }

        var digits = escape == 'u' ? 4 : 8;
        uint value = 0;
        for (var count = 0; count < digits; count++)
        {
            if (position == content.Length || !TryHexValue(content[position], out var digit))
            {
                return false;
            }
            value = (value << 4) | digit;
            position++;
        }

        if (value > 0x10FFFF || !Rune.IsValid((int)value))
        {
            return false;
        }

        scalar = (int)value;
        return true;
    }

    private static bool TryHexValue(char value, out uint digit)
    {
        if (value is >= '0' and <= '9')
        {
            digit = (uint)(value - '0');
            return true;
        }
        if (value is >= 'a' and <= 'f')
        {
            digit = (uint)(value - 'a' + 10);
            return true;
        }
        if (value is >= 'A' and <= 'F')
        {
            digit = (uint)(value - 'A' + 10);
            return true;
        }
        digit = 0;
        return false;
    }
}
