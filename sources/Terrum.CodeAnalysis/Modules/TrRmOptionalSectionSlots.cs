// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmReferenceSlot(
    ushort TargetKind, ushort Selector, uint TargetRow, uint Block, uint Ordinal);

/// <summary>Replaces optional-section reference slots without interpreting or changing opaque payload bytes.</summary>
/// <remarks>The recomposed artifact must be validated to check target rows. A tool must separately establish
/// that relocated Body instruction references retain their meaning.</remarks>
public static class TrRmOptionalSectionSlots
{
    public static byte[] ReplaceInArtifact(ReadOnlySpan<byte> artifact, ushort kind,
        IReadOnlyList<TrRmReferenceSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (kind < 0x8000)
        {
            throw InvalidSlots();
        }

        var index = new TrRmArtifactIndex(artifact);
        var sections = new TrRmSection[index.SectionCount];
        var found = false;
        for (var entry = 0; entry < sections.Length; entry++)
        {
            index.GetSection(entry, out var currentKind, out var flags, out var bytes, out var count);
            var section = new TrRmSection(currentKind, flags, (uint)count, bytes.ToArray());
            if (currentKind == kind)
            {
                section = Replace(section, slots);
                found = true;
            }
            sections[entry] = section;
        }

        if (!found)
        {
            throw InvalidSlots();
        }

        return TrRmArtifactWriter.Encode(sections);
    }

    public static TrRmSection Replace(TrRmSection section, IReadOnlyList<TrRmReferenceSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (section.Kind < 0x8000 || section.Flags != 2
            || section.RecordCount is 0 or > 0xFF_FFFF
            || section.Bytes.Length < sizeof(uint))
        {
            throw InvalidSlots();
        }

        var source = section.Bytes.Span;
        var slotCount = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if (slotCount != slots.Count || 4L + slotCount * 16L > source.Length)
        {
            throw InvalidSlots();
        }

        var result = section.Bytes.ToArray();
        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            var offset = 4 + index * 16;
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset), slot.TargetKind);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset + 2), slot.Selector);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 4), slot.TargetRow);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 8), slot.Block);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 12), slot.Ordinal);
        }

        return section with { Bytes = result };
    }

    private static InvalidDataException InvalidSlots()
        => new("Invalid TRRM optional-section reference slots.");
}
