// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates SourceMap slots, row order, UTF-16 spans and module association.</summary>
/// <remarks>Requires a Body-aware verifier to establish instruction block and ordinal validity.</remarks>
public readonly ref struct TrRmSourceMapTableReader
{
    public TrRmSourceMapTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmMethodDefTableReader methods, TrRmSourceFileTableReader files)
    {
        if (recordCount <= 0 || recordCount > 0xFF_FFFF || section.Length < 4)
        {
            throw InvalidMap();
        }

        var slotCount = Read(section, 0);
        var rowStart = 4L + slotCount * 16L;
        if (slotCount == 0 || rowStart + (long)recordCount * 16 != section.Length)
        {
            throw InvalidMap();
        }

        var slots = section.Slice(4, (int)rowStart - 4);
        for (var slot = 0; slot < slotCount; slot++)
        {
            var data = slots.Slice((int)slot * 16, 16);
            var kind = ReadU16(data, 0);
            var selector = ReadU16(data, 2);
            var target = Read(data, 4);
            if (kind == 0x0020 && selector == 1)
            {
                if (target == 0 || Read(data, 8) == 0)
                {
                    throw InvalidMap();
                }

                _ = methods.GetBodyOwnerMethod((int)target);
            }
            else if (kind == 0x001F && selector == 0)
            {
                if (Read(data, 8) != 0 || Read(data, 12) != 0)
                {
                    throw InvalidMap();
                }

                _ = files.GetOwnerModule((int)target);
            }
            else
            {
                throw InvalidMap();
            }
        }

        var previousInstruction = 0u;
        for (var row = 0; row < recordCount; row++)
        {
            var data = section.Slice((int)rowStart + row * 16, 16);
            var instructionSlot = Read(data, 0);
            var fileSlot = Read(data, 4);
            if (instructionSlot <= previousInstruction || instructionSlot > slotCount
                || fileSlot == 0 || fileSlot > slotCount
                || (ulong)Read(data, 8) + Read(data, 12) > uint.MaxValue)
            {
                throw InvalidMap();
            }

            var instruction = slots.Slice(((int)instructionSlot - 1) * 16, 16);
            var file = slots.Slice(((int)fileSlot - 1) * 16, 16);
            if (ReadU16(instruction, 0) != 0x0020 || ReadU16(instruction, 2) != 1
                || ReadU16(file, 0) != 0x001F || ReadU16(file, 2) != 0
                || methods.GetOwnerModule(methods.GetBodyOwnerMethod((int)Read(instruction, 4)))
                    != files.GetOwnerModule((int)Read(file, 4)))
            {
                throw InvalidMap();
            }

            previousInstruction = instructionSlot;
        }
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidMap()
        => new("Invalid TRRM SourceMap table.");
}
