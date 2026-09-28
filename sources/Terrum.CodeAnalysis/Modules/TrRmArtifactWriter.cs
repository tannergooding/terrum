// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmSection(
    ushort Kind, ushort Flags, uint RecordCount, ReadOnlyMemory<byte> Bytes);

/// <summary>Frames independently authored sections in a provisional TRRM (1, 0) artifact.</summary>
/// <remarks>Does not replace the semantic-module writer or establish executable metadata validity.</remarks>
public static class TrRmArtifactWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Count is <= 0 or > 32790)
        {
            throw new ArgumentOutOfRangeException(nameof(sections));
        }

        var offsets = new int[sections.Count];
        var size = 32L + sections.Count * 16L;
        var previousKind = 0;
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            if (section.Kind <= previousKind
                || section.RecordCount is 0 or > 0xFF_FFFF
                || section.Bytes.IsEmpty)
            {
                throw new ArgumentException("TRRM sections require ordered unique kinds and nonempty payloads.", nameof(sections));
            }

            previousKind = section.Kind;
            if (section.Kind == 0x0002)
            {
                size = (size + 3) & ~3L;
            }

            if (size + section.Bytes.Length > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(sections), "TRRM artifact exceeds host byte-array limits.");
            }

            offsets[index] = (int)size;
            size += section.Bytes.Length;
        }

        var result = new byte[(int)size];
        "TRRM"u8.CopyTo(result);
        WriteU16(result, 4, 1);
        Write(result, 8, 32);
        Write(result, 12, (uint)sections.Count);
        Write(result, 16, (uint)size);
        Write(result, 20, 32);
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            var directory = 32 + index * 16;
            WriteU16(result, directory, section.Kind);
            WriteU16(result, directory + 2, section.Flags);
            Write(result, directory + 4, section.RecordCount);
            Write(result, directory + 8, (uint)offsets[index]);
            Write(result, directory + 12, (uint)section.Bytes.Length);
            section.Bytes.Span.CopyTo(result.AsSpan(offsets[index]));
        }

        _ = new TrRmArtifactIndex(result);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteU16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
