// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Encodes TRRM String sections from text and selected encodings.</summary>
/// <remarks>Encoding values 0 through 3 select ASCII, UTF-8, UTF-16LE and UTF-32LE, respectively.</remarks>
public static class TrRmStringTableWriter
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private static readonly UnicodeEncoding s_utf16 = new(false, false, true);
    private static readonly UTF32Encoding s_utf32 = new(false, false, true);

    /// <summary>Encodes nonempty name rows using ASCII, UTF-8, UTF-16LE or UTF-32LE.</summary>
    /// <param name="names">The text and encoding for each row, in table order.</param>
    /// <returns>The encoded String section.</returns>
    public static byte[] EncodeNames(IReadOnlyList<(string Text, int Encoding)> names)
        => EncodeRows(names, allowEmpty: false, argumentName: nameof(names),
            resourceLedger: null);

    /// <summary>Encodes text rows, allowing empty text and the selected ASCII or Unicode encoding.</summary>
    /// <param name="rows">The text and encoding for each row, in table order.</param>
    /// <returns>The encoded String section.</returns>
    public static byte[] EncodeTextRows(IReadOnlyList<(string Text, int Encoding)> rows)
        => EncodeRows(rows, allowEmpty: true, argumentName: nameof(rows),
            resourceLedger: null);

    internal static byte[] EncodeTextRows(
        IReadOnlyList<(string Text, int Encoding)> rows,
        TrRmA3LayoutResourceLedger resourceLedger)
        => EncodeRows(rows, allowEmpty: true, argumentName: nameof(rows),
            resourceLedger);

    private static byte[] EncodeRows(IReadOnlyList<(string Text, int Encoding)> names,
        bool allowEmpty, string argumentName,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (names is null)
        {
            throw new ArgumentNullException(argumentName);
        }
        if (names.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(argumentName);
        }

        resourceLedger?.ConsumeWork(2);
        var indexBytes = ((long)names.Count + 1) * sizeof(uint);
        resourceLedger?.ConsumeWork(checked((ulong)names.Count + 1UL));
        var rows = new (string Text, int Encoding, int Width, int ContentLength, int Length)[names.Count];
        var dataBytes = 0L;
        for (var index = 0; index < names.Count; index++)
        {
            resourceLedger?.ConsumeWork();
            var (text, encoding) = names[index];
            ArgumentNullException.ThrowIfNull(text);
            resourceLedger?.ConsumeWork();
            if (!allowEmpty && text.Length == 0)
            {
                throw new ArgumentException("A TRRM text row must be NUL-free and required names nonempty.", argumentName);
            }

            resourceLedger?.ConsumeWork();
            if (encoding != 0)
            {
                resourceLedger?.ConsumeWork(checked((ulong)text.Length));
                if (text.Contains('\0', StringComparison.Ordinal))
                {
                    throw new ArgumentException("A TRRM text row must be NUL-free and required names nonempty.", argumentName);
                }
            }

            int contentLength;
            int width;
            try
            {
                (contentLength, width) = encoding switch
                {
                    0 => (ValidateAscii(text, argumentName, resourceLedger), 1),
                    1 => (GetByteCount(s_utf8, text, resourceLedger), 1),
                    2 => (GetByteCount(s_utf16, text, resourceLedger), 2),
                    3 => (GetByteCount(s_utf32, text, resourceLedger), 4),
                    _ => throw new ArgumentOutOfRangeException(argumentName, "Unassigned TRRM String encoding."),
                };
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException("A TRRM name must contain well-formed scalar text.", argumentName, exception);
            }

            if (contentLength / width > 0xFF_FFFF)
            {
                throw new ArgumentOutOfRangeException(argumentName, "TRRM String unit count exceeds u24.");
            }

            resourceLedger?.ConsumeWork(3);
            var unpaddedLength = (long)4 + contentLength + width;
            var rowLength = (unpaddedLength + 3) & ~3L;
            resourceLedger?.ConsumeWork(2);
            dataBytes += rowLength;
            resourceLedger?.ConsumeWork();
            if (indexBytes + dataBytes > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(argumentName, "TRRM String section exceeds host byte-array limits.");
            }

            rows[index] = (text, encoding, width, contentLength, (int)rowLength);
        }

        var resultLength = checked((ulong)(indexBytes + dataBytes));
        resourceLedger?.ConsumeWork(checked(resultLength + 1UL
            + (ulong)names.Count + 1UL
            + ((ulong)names.Count * 4UL)));
        var result = new byte[(int)resultLength];
        var dataStart = (int)indexBytes;
        var offset = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            resourceLedger?.ConsumeWork(2);
            var (text, encoding, width, contentLength, length) = rows[index];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * sizeof(uint)), (uint)offset);
            var units = contentLength / width;
            var row = result.AsSpan(dataStart + offset, length);
            row[0] = (byte)encoding;
            row[1] = (byte)(units & byte.MaxValue);
            row[2] = (byte)((units >> 8) & byte.MaxValue);
            row[3] = (byte)(units >> 16);
            resourceLedger?.ConsumeWork(checked((ulong)text.Length));
            _ = encoding switch
            {
                0 => Encoding.ASCII.GetBytes(text.AsSpan(), row.Slice(4, contentLength)),
                1 => s_utf8.GetBytes(text.AsSpan(), row.Slice(4, contentLength)),
                2 => s_utf16.GetBytes(text.AsSpan(), row.Slice(4, contentLength)),
                3 => s_utf32.GetBytes(text.AsSpan(), row.Slice(4, contentLength)),
                _ => throw new InvalidOperationException("The validated TRRM String encoding changed."),
            };
            resourceLedger?.ConsumeWork();
            offset += length;
        }

        resourceLedger?.ConsumeWork();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(rows.Length * sizeof(uint)), (uint)offset);
        return result;
    }

    private static int ValidateAscii(string text, string argumentName,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        var containsZero = false;
        var containsNonAscii = false;
        foreach (var unit in text)
        {
            resourceLedger?.ConsumeWork();
            containsZero |= unit == '\0';
            containsNonAscii |= unit > 0x7F;
        }

        if (containsZero)
        {
            throw new ArgumentException("A TRRM text row must be NUL-free and required names nonempty.", argumentName);
        }
        if (containsNonAscii)
        {
            throw new ArgumentException("ASCII TRRM names cannot contain non-ASCII scalars.", nameof(text));
        }

        return text.Length;
    }

    private static int GetByteCount(Encoding encoding, string text,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(checked((ulong)text.Length));
        return encoding.GetByteCount(text);
    }
}
