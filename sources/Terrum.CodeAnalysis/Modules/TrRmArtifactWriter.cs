// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmSection(
    ushort Kind, ushort Flags, uint RecordCount, ReadOnlyMemory<byte> Bytes);

/// <summary>Frames independently authored sections in a standard TRRM (1, 0) artifact.</summary>
/// <remarks>Does not replace the semantic-module writer or establish executable metadata validity.</remarks>
public static class TrRmArtifactWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmSection> sections)
        => Encode(sections, TrRmArtifactSchema.Standard);

    internal static byte[] EncodeDevelopment(IReadOnlyList<TrRmSection> sections)
        => Encode(sections, TrRmArtifactSchema.Development);

    internal static byte[] Encode(IReadOnlyList<TrRmSection> sections,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        ArgumentNullException.ThrowIfNull(sections);
        resourceLedger?.ConsumeWork();
        if (sections.Count is <= 0 or > 32790)
        {
            throw new ArgumentOutOfRangeException(nameof(sections));
        }

        resourceLedger?.ConsumeWork(2);
        var size = 32L + sections.Count * 16L;
        var previousKind = 0;
        for (var index = 0; index < sections.Count; index++)
        {
            resourceLedger?.ConsumeWork();
            var section = sections[index];
            resourceLedger?.ConsumeWork();
            if (section.Kind <= previousKind)
            {
                throw new ArgumentException("TRRM sections require ordered unique kinds and nonempty payloads.", nameof(sections));
            }

            previousKind = section.Kind;
            resourceLedger?.ConsumeWork();
            if (section.RecordCount is 0 or > 0xFF_FFFF)
            {
                throw new ArgumentException("TRRM sections require ordered unique kinds and nonempty payloads.", nameof(sections));
            }

            resourceLedger?.ConsumeWork();
            if (section.Bytes.IsEmpty)
            {
                throw new ArgumentException("TRRM sections require ordered unique kinds and nonempty payloads.", nameof(sections));
            }

            resourceLedger?.ConsumeWork();
            if (section.Kind == 0x0002)
            {
                resourceLedger?.ConsumeWork(2);
                size = (size + 3) & ~3L;
            }

            resourceLedger?.ConsumeWork(2);
            var nextSize = size + section.Bytes.Length;
            if (nextSize > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(sections), "TRRM artifact exceeds host byte-array limits.");
            }

            size = nextSize;
        }

        resourceLedger?.ConsumeWork(checked((ulong)size + 1UL));
        var result = new byte[(int)size];
        resourceLedger?.ConsumeWork();
        "TRRM"u8.CopyTo(result);
        resourceLedger?.ConsumeWork();
        WriteU16(result, 4, 1);
        resourceLedger?.ConsumeWork();
        Write(result, 8, 32);
        resourceLedger?.ConsumeWork();
        Write(result, 12, (uint)sections.Count);
        resourceLedger?.ConsumeWork();
        Write(result, 16, (uint)size);
        resourceLedger?.ConsumeWork();
        Write(result, 20, 32);
        resourceLedger?.ConsumeWork(2);
        var offset = 32L + sections.Count * 16L;
        for (var index = 0; index < sections.Count; index++)
        {
            resourceLedger?.ConsumeWork();
            var section = sections[index];
            resourceLedger?.ConsumeWork();
            if (section.Kind == 0x0002)
            {
                resourceLedger?.ConsumeWork(2);
                offset = (offset + 3) & ~3L;
            }

            resourceLedger?.ConsumeWork(2);
            var directory = 32 + index * 16;
            resourceLedger?.ConsumeWork();
            WriteU16(result, directory, section.Kind);
            resourceLedger?.ConsumeWork();
            WriteU16(result, directory + 2, section.Flags);
            resourceLedger?.ConsumeWork();
            Write(result, directory + 4, section.RecordCount);
            resourceLedger?.ConsumeWork();
            Write(result, directory + 8, (uint)offset);
            resourceLedger?.ConsumeWork();
            Write(result, directory + 12, (uint)section.Bytes.Length);
            resourceLedger?.ConsumeWork();
            section.Bytes.Span.CopyTo(result.AsSpan((int)offset));
            resourceLedger?.ConsumeWork();
            offset += section.Bytes.Length;
        }
        resourceLedger?.ConsumeWork();
        if (offset != size)
        {
            throw new InvalidOperationException("TRRM section offsets did not match the computed artifact size.");
        }

        _ = new TrRmArtifactIndex(result, schema, resourceLedger);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteU16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
