// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Terrum.CodeAnalysis.Modules;

public static class TrRmStringTableWriter
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private static readonly UnicodeEncoding s_utf16 = new(false, false, true);
    private static readonly UTF32Encoding s_utf32 = new(false, false, true);

    public static byte[] EncodeNames(IReadOnlyList<(string Text, int Encoding)> names)
        => EncodeRows(names, allowEmpty: false, argumentName: nameof(names));

    public static byte[] EncodeTextRows(IReadOnlyList<(string Text, int Encoding)> rows)
        => EncodeRows(rows, allowEmpty: true, argumentName: nameof(rows));

    private static byte[] EncodeRows(IReadOnlyList<(string Text, int Encoding)> names,
        bool allowEmpty, string argumentName)
    {
        if (names is null)
        {
            throw new ArgumentNullException(argumentName);
        }
        if (names.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(argumentName);
        }

        var indexBytes = ((long)names.Count + 1) * sizeof(uint);
        var rows = new (byte[] Content, int Encoding, int Width, int Length)[names.Count];
        var dataBytes = 0L;
        for (var index = 0; index < names.Count; index++)
        {
            var (text, encoding) = names[index];
            ArgumentNullException.ThrowIfNull(text);
            if ((!allowEmpty && text.Length == 0) || text.Contains('\0'))
            {
                throw new ArgumentException("A TRRM text row must be NUL-free and required names nonempty.", argumentName);
            }

            byte[] content;
            int width;
            try
            {
                (content, width) = encoding switch
                {
                    0 => (EncodeAscii(text), 1),
                    1 => (s_utf8.GetBytes(text), 1),
                    2 => (s_utf16.GetBytes(text), 2),
                    3 => (s_utf32.GetBytes(text), 4),
                    _ => throw new ArgumentOutOfRangeException(argumentName, "Unassigned TRRM String encoding."),
                };
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException("A TRRM name must contain well-formed scalar text.", argumentName, exception);
            }

            if (content.Length / width > 0xFF_FFFF)
            {
                throw new ArgumentOutOfRangeException(argumentName, "TRRM String unit count exceeds u24.");
            }

            var unpaddedLength = (long)4 + content.Length + width;
            var rowLength = (unpaddedLength + 3) & ~3L;
            dataBytes += rowLength;
            if (indexBytes + dataBytes > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(argumentName, "TRRM String section exceeds host byte-array limits.");
            }

            rows[index] = (content, encoding, width, (int)rowLength);
        }

        var result = new byte[(int)(indexBytes + dataBytes)];
        var dataStart = (int)indexBytes;
        var offset = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            var (content, encoding, width, length) = rows[index];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * sizeof(uint)), (uint)offset);
            var units = content.Length / width;
            var row = result.AsSpan(dataStart + offset, length);
            row[0] = (byte)encoding;
            row[1] = (byte)(units & byte.MaxValue);
            row[2] = (byte)((units >> 8) & byte.MaxValue);
            row[3] = (byte)(units >> 16);
            content.CopyTo(row[4..]);
            offset += length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(rows.Length * sizeof(uint)), (uint)offset);
        return result;
    }

    private static byte[] EncodeAscii(string text)
    {
        foreach (var unit in text)
        {
            if (unit > 0x7F)
            {
                throw new ArgumentException("ASCII TRRM names cannot contain non-ASCII scalars.", nameof(text));
            }
        }

        return Encoding.ASCII.GetBytes(text);
    }
}
