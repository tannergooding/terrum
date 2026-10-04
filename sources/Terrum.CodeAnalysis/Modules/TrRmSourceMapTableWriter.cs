// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmSourceMapEntry(
    int BodyRow, int Block, int Ordinal, int SourceFileRow, uint StartUtf16, uint LengthUtf16);

/// <summary>Emits structurally checked SourceMap slots and ordered mapping rows.</summary>
/// <remarks>Instruction block and ordinal existence still require a Body-aware verifier.</remarks>
public static class TrRmSourceMapTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmSourceMapEntry> mappings,
        TrRmMethodDefTableReader methods, TrRmSourceFileTableReader files)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        if (mappings.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(mappings));
        }

        var seen = new HashSet<(int Body, int Block, int Ordinal)>();
        var fileSlots = new Dictionary<int, int>();
        var fileRows = new List<int>();
        foreach (var mapping in mappings)
        {
            if (mapping.Block <= 0 || mapping.Ordinal < 0
                || (ulong)mapping.StartUtf16 + mapping.LengthUtf16 > uint.MaxValue
                || methods.GetOwnerModule(methods.GetBodyOwnerMethod(mapping.BodyRow))
                    != files.GetOwnerModule(mapping.SourceFileRow)
                || !seen.Add((mapping.BodyRow, mapping.Block, mapping.Ordinal)))
            {
                throw new InvalidDataException("Invalid TRRM SourceMap mapping.");
            }

            if (fileSlots.TryAdd(mapping.SourceFileRow, fileRows.Count))
            {
                fileRows.Add(mapping.SourceFileRow);
            }
        }

        var slotCount = mappings.Count + fileRows.Count;
        var sectionBytes = 4L + slotCount * 16L + mappings.Count * 16L;
        if (sectionBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(mappings));
        }

        var result = new byte[(int)sectionBytes];
        Write(result, 0, (uint)slotCount);
        var rowsStart = 4 + slotCount * 16;
        for (var index = 0; index < mappings.Count; index++)
        {
            var mapping = mappings[index];
            var bodySlot = 4 + index * 16;
            WriteU16(result, bodySlot, 0x0020);
            WriteU16(result, bodySlot + 2, 1);
            Write(result, bodySlot + 4, (uint)mapping.BodyRow);
            Write(result, bodySlot + 8, (uint)mapping.Block);
            Write(result, bodySlot + 12, (uint)mapping.Ordinal);
            var row = rowsStart + index * 16;
            Write(result, row, (uint)index + 1);
            Write(result, row + 4, (uint)(mappings.Count + fileSlots[mapping.SourceFileRow] + 1));
            Write(result, row + 8, mapping.StartUtf16);
            Write(result, row + 12, mapping.LengthUtf16);
        }

        for (var index = 0; index < fileRows.Count; index++)
        {
            var fileSlot = 4 + (mappings.Count + index) * 16;
            WriteU16(result, fileSlot, 0x001F);
            Write(result, fileSlot + 4, (uint)fileRows[index]);
        }

        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteU16(byte[] bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
