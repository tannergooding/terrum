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
    public static TrRmSection Create(ushort kind, uint recordCount,
        IReadOnlyList<TrRmReferenceSlot> slots, ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var size = 4L + slots.Count * 16L + payload.Length;
        if (kind < 0x8000 || recordCount is 0 or > 0xFF_FFFF || size > int.MaxValue)
        {
            throw InvalidSlots();
        }

        var result = new byte[(int)size];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)slots.Count);
        for (var index = 0; index < slots.Count; index++)
        {
            WriteSlot(result, index, slots[index]);
        }
        payload.Span.CopyTo(result.AsSpan(4 + slots.Count * 16));
        return new TrRmSection(kind, 2, recordCount, result);
    }

    public static byte[] InsertInArtifact(ReadOnlySpan<byte> artifact, TrRmSection extension)
    {
        if (extension.Kind < 0x8000 || extension.Flags != 2)
        {
            throw InvalidSlots();
        }

        var index = new TrRmArtifactIndex(artifact);
        var sections = CopySections(index);
        sections.Add(extension);
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections);
    }

    public static byte[] ReplaceInArtifact(ReadOnlySpan<byte> artifact, ushort kind,
        IReadOnlyList<TrRmReferenceSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (kind < 0x8000)
        {
            throw InvalidSlots();
        }

        var index = new TrRmArtifactIndex(artifact);
        var sections = CopySections(index);
        var found = false;
        for (var entry = 0; entry < sections.Count; entry++)
        {
            if (sections[entry].Kind == kind)
            {
                sections[entry] = Replace(sections[entry], slots);
                found = true;
            }
        }

        if (!found)
        {
            throw InvalidSlots();
        }

        return TrRmArtifactWriter.Encode(sections);
    }

    private static List<TrRmSection> CopySections(TrRmArtifactIndex index)
    {
        var sections = new List<TrRmSection>(index.SectionCount + 1);
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags, out var bytes, out var count);
            if (kind >= 0x8000 && flags == 3)
            {
                continue;
            }
            sections.Add(new TrRmSection(kind, flags, (uint)count, bytes.ToArray()));
        }
        return sections;
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
            WriteSlot(result, index, slots[index]);
        }

        return section with { Bytes = result };
    }

    private static void WriteSlot(byte[] bytes, int index, TrRmReferenceSlot slot)
    {
        var offset = 4 + index * 16;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), slot.TargetKind);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2), slot.Selector);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), slot.TargetRow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8), slot.Block);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 12), slot.Ordinal);
    }

    private static InvalidDataException InvalidSlots()
        => new("Invalid TRRM optional-section reference slots.");
}
