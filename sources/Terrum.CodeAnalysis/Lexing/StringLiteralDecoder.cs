// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Text;

namespace Terrum.CodeAnalysis.Lexing;

internal static class StringLiteralDecoder
{
    internal static bool TryDecode(ReadOnlySpan<char> text, out string value)
    {
        value = string.Empty;
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
                    builder.Append(character);
                    builder.Append(text[position++]);
                }
                else if (char.IsLowSurrogate(character))
                {
                    return false;
                }
                else
                {
                    builder.Append(character);
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
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case '"':
                    builder.Append('"');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                case 'u':
                    if (!TryReadEscape(text, ref position, end, 4, out var shortScalar))
                    {
                        return false;
                    }
                    builder.Append(char.ConvertFromUtf32(shortScalar));
                    break;
                case 'U':
                    if (!TryReadEscape(text, ref position, end, 8, out var longScalar))
                    {
                        return false;
                    }
                    builder.Append(char.ConvertFromUtf32(longScalar));
                    break;
                default:
                    return false;
            }
        }

        value = builder.ToString();
        return true;
    }

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
