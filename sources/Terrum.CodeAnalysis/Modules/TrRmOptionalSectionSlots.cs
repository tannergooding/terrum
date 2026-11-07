// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A standard TRRM optional-section row or Body-instruction reference.</summary>
/// <param name="TargetKind">The assigned section kind, or zero for a Body instruction.</param>
/// <param name="Selector">Zero for a table row or one for a Body instruction.</param>
/// <param name="TargetRow">The one-based row in the selected table.</param>
/// <param name="Block">The zero-based u16 Body block index, or zero for a table row.</param>
/// <param name="Ordinal">The zero-based instruction ordinal, or zero for a table row.</param>
public readonly record struct TrRmReferenceSlot(
    ushort TargetKind, ushort Selector, uint TargetRow, uint Block, uint Ordinal);

/// <summary>Replaces optional-section reference slots without interpreting or changing opaque payload bytes.</summary>
/// <remarks>The recomposed artifact must be validated to check target rows. A tool must separately establish
/// that relocated Body instruction references retain their meaning.</remarks>
public static class TrRmOptionalSectionSlots
{
    /// <summary>Creates an optional section from reference slots and opaque payload bytes.</summary>
    /// <param name="kind">The assigned optional-section kind.</param>
    /// <param name="recordCount">The nonzero row count recorded in the artifact directory.</param>
    /// <param name="slots">The references preceding the opaque payload.</param>
    /// <param name="payload">The payload bytes, which are not interpreted.</param>
    /// <returns>The optional section with reference-slot metadata.</returns>
    public static TrRmSection Create(ushort kind, uint recordCount,
        IReadOnlyList<TrRmReferenceSlot> slots, ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var size = 4L + (slots.Count * 16L) + payload.Length;
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
        payload.Span.CopyTo(result.AsSpan(4 + (slots.Count * 16)));
        return new TrRmSection(kind, 2, recordCount, result);
    }

    /// <summary>Inserts an optional section into a standard TRRM artifact.</summary>
    /// <param name="artifact">The artifact to extend.</param>
    /// <param name="extension">The optional section to insert.</param>
    /// <returns>The recomposed artifact; validate it to check referenced target rows.</returns>
    public static byte[] InsertInArtifact(ReadOnlySpan<byte> artifact, TrRmSection extension)
        => InsertInArtifact(artifact, extension, TrRmArtifactSchema.Standard);

    internal static byte[] InsertInDevelopmentArtifact(ReadOnlySpan<byte> artifact,
        TrRmSection extension)
        => InsertInArtifact(artifact, extension, TrRmArtifactSchema.Development);

    internal static byte[] InsertInArtifact(ReadOnlySpan<byte> artifact,
        TrRmSection extension, TrRmArtifactSchema schema)
    {
        if (extension.Kind < 0x8000 || extension.Flags != 2)
        {
            throw InvalidSlots();
        }

        var index = new TrRmArtifactIndex(artifact, schema);
        var sections = CopySections(index);
        sections.Add(extension);
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    /// <summary>Replaces the reference slots of an existing optional section.</summary>
    /// <param name="artifact">The artifact containing the optional section.</param>
    /// <param name="kind">The optional-section kind to replace.</param>
    /// <param name="slots">The replacement slots, with the same count as the existing slots.</param>
    /// <returns>The recomposed artifact; validate it to check referenced target rows.</returns>
    public static byte[] ReplaceInArtifact(ReadOnlySpan<byte> artifact, ushort kind,
        IReadOnlyList<TrRmReferenceSlot> slots)
        => ReplaceInArtifact(artifact, kind, slots, TrRmArtifactSchema.Standard);

    internal static byte[] ReplaceInDevelopmentArtifact(ReadOnlySpan<byte> artifact,
        ushort kind, IReadOnlyList<TrRmReferenceSlot> slots)
        => ReplaceInArtifact(artifact, kind, slots, TrRmArtifactSchema.Development);

    internal static byte[] ReplaceInArtifact(ReadOnlySpan<byte> artifact, ushort kind,
        IReadOnlyList<TrRmReferenceSlot> slots, TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (kind < 0x8000)
        {
            throw InvalidSlots();
        }

        var index = new TrRmArtifactIndex(artifact, schema);
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

        return TrRmArtifactWriter.Encode(sections, schema);
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

    /// <summary>Replaces the slots in an optional section while preserving its payload.</summary>
    /// <param name="section">The optional section to update.</param>
    /// <param name="slots">The replacement slots, with the same count as the existing slots.</param>
    /// <returns>The updated section.</returns>
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
        if (slotCount != slots.Count || 4L + (slotCount * 16L) > source.Length)
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
        if (slot.Block > ushort.MaxValue)
        {
            throw InvalidSlots();
        }

        var offset = 4 + (index * 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), slot.TargetKind);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2), slot.Selector);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), slot.TargetRow);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 8), (ushort)slot.Block);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 10), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 12), slot.Ordinal);
    }

    private static InvalidDataException InvalidSlots()
        => new("Invalid TRRM optional-section reference slots.");
}
