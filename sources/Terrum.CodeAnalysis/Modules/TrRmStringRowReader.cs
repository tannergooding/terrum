// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;

namespace Terrum.CodeAnalysis.Modules;

public static class TrRmStringRowReader
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private static readonly UnicodeEncoding s_utf16 = new(false, false, true);
    private static readonly UTF32Encoding s_utf32 = new(false, false, true);

    public static string DecodeName(ReadOnlySpan<byte> row)
    {
        if (row.Length < 8 || (row[0] & 0xFC) != 0)
        {
            throw InvalidRow();
        }

        var width = row[0] switch
        {
            0 or 1 => 1,
            2 => 2,
            _ => 4,
        };
        var units = row[1] | (row[2] << 8) | (row[3] << 16);
        if (units == 0)
        {
            throw InvalidRow();
        }

        var contentBytes = (long)units * width;
        var unpaddedLength = 4 + contentBytes + width;
        var paddedLength = (unpaddedLength + 3) & ~3L;
        if (paddedLength != row.Length)
        {
            throw InvalidRow();
        }

        var content = row.Slice(4, (int)contentBytes);
        foreach (var trailing in row.Slice(4 + (int)contentBytes))
        {
            if (trailing != 0)
            {
                throw InvalidRow();
            }
        }

        string name;
        try
        {
            name = row[0] switch
            {
                0 when IsAscii(content) => Encoding.ASCII.GetString(content),
                1 => s_utf8.GetString(content),
                2 => s_utf16.GetString(content),
                3 => s_utf32.GetString(content),
                _ => throw InvalidRow(),
            };
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Invalid TRRM String name encoding.", exception);
        }

        if (name.IndexOf('\0') >= 0)
        {
            throw InvalidRow();
        }

        return name;
    }

    private static bool IsAscii(ReadOnlySpan<byte> content)
    {
        foreach (var value in content)
        {
            if (value >= 0x80)
            {
                return false;
            }
        }

        return true;
    }

    private static InvalidDataException InvalidRow()
        => new("Invalid TRRM String name row.");
}
