// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates TRRM (1, 0) framing and indexes present sections without binding them.</summary>
/// <remarks>The borrowed artifact bytes must remain unchanged while this index is in use.</remarks>
public readonly ref struct TrRmArtifactIndex
{
    private readonly ReadOnlySpan<byte> _artifact;
    private readonly ReadOnlySpan<byte> _directory;
    private readonly int _entryCount;

    public int SectionCount => _entryCount;

    public TrRmArtifactIndex(ReadOnlySpan<byte> artifact)
    {
        if (artifact.Length < 32 || !artifact[..4].SequenceEqual("TRRM"u8)
            || ReadU16(artifact, 4) != 1 || ReadU16(artifact, 6) != 0
            || ReadU32(artifact, 8) != 32 || ReadU32(artifact, 16) != artifact.Length
            || ReadU32(artifact, 20) != 32)
        {
            throw InvalidArtifact();
        }

        for (var offset = 24; offset < 32; offset++)
        {
            if (artifact[offset] != 0)
            {
                throw InvalidArtifact();
            }
        }

        var entryCount = ReadU32(artifact, 12);
        var directoryEnd = 32L + entryCount * 16L;
        if (entryCount is 0 or > 32790 || directoryEnd > artifact.Length)
        {
            throw InvalidArtifact();
        }

        _artifact = artifact;
        _directory = artifact.Slice(32, (int)directoryEnd - 32);
        _entryCount = (int)entryCount;
        // Reference slots target only kinds 1 through 0x20; NativeImport is not referenceable.
        Span<uint> sectionCounts = stackalloc uint[0x33 + 1];
        sectionCounts.Clear();
        var previousKind = 0;
        // Keep directory-ordered payloads allocation-free while retaining support for alternate layouts.
        var previousStart = 0u;
        var rangesOrdered = true;
        var moduleFound = false;
        for (var entry = 0; entry < _entryCount; entry++)
        {
            var kind = Kind(entry);
            var flags = ReadU16(_directory, entry * 16 + 2);
            var count = ReadU32(_directory, entry * 16 + 4);
            var start = ReadU32(_directory, entry * 16 + 8);
            var length = ReadU32(_directory, entry * 16 + 12);
            if (kind <= previousKind || count is 0 or > 0xFF_FFFF || length == 0
                || start < directoryEnd || (long)start + length > artifact.Length)
            {
                throw InvalidArtifact();
            }

            previousKind = kind;
            if (entry != 0 && start < previousStart)
            {
                rangesOrdered = false;
            }
            previousStart = start;
            if (kind == 0x0001)
            {
                moduleFound = true;
            }

            var stride = FixedStride(kind);
            if ((kind < 0x8000 && stride == -1)
                || (kind < 0x8000 && flags != (kind == 0x7F00 ? 2 : 0))
                || (kind >= 0x8000 && flags is not (1 or 2 or 3))
                || (stride > 0 && length != (long)count * stride)
                || (kind == 0x0002 && (start & 3) != 0))
            {
                throw InvalidArtifact();
            }

            if (kind <= 0x0033)
            {
                sectionCounts[kind] = count;
            }
        }

        if (!moduleFound)
        {
            throw InvalidArtifact();
        }

        var featureCount = (int)sectionCounts[0x0004];
        if (featureCount != 0)
        {
            if (!TryGetSection(0x0004, out var featureBytes, out var actualFeatureCount)
                || actualFeatureCount != featureCount)
            {
                throw InvalidArtifact();
            }

            _ = new TrRmFeatureTableReader(featureBytes, featureCount);
        }

        if (!TryGetSection(0x0001, out var moduleBytes, out var moduleCount))
        {
            throw InvalidArtifact();
        }

        for (var module = 0; module < moduleCount; module++)
        {
            var offset = module * 44;
            var featureFirst = ReadU32(moduleBytes, offset + 16);
            var moduleFeatureCount = ReadU32(moduleBytes, offset + 20);
            if (!ValidFeatureRange(featureFirst, moduleFeatureCount, featureCount))
            {
                throw InvalidArtifact();
            }
        }

        var cursor = (int)directoryEnd;
        if (rangesOrdered)
        {
            for (var entry = 0; entry < _entryCount; entry++)
            {
                var start = ReadU32(_directory, entry * 16 + 8);
                var length = ReadU32(_directory, entry * 16 + 12);
                if (start < cursor)
                {
                    throw InvalidArtifact();
                }

                if (artifact.Slice(cursor, (int)start - cursor).IndexOfAnyExcept((byte)0) >= 0)
                {
                    throw InvalidArtifact();
                }

                cursor = (int)(start + length);
            }
        }
        else
        {
            var extents = new (uint Start, uint End)[_entryCount];
            for (var entry = 0; entry < _entryCount; entry++)
            {
                var start = ReadU32(_directory, entry * 16 + 8);
                var length = ReadU32(_directory, entry * 16 + 12);
                extents[entry] = (start, start + length);
            }

            Array.Sort(extents, static (left, right) => left.Start.CompareTo(right.Start));
            foreach (var (start, end) in extents)
            {
                if (start < cursor)
                {
                    throw InvalidArtifact();
                }

                if (artifact.Slice(cursor, (int)start - cursor).IndexOfAnyExcept((byte)0) >= 0)
                {
                    throw InvalidArtifact();
                }

                cursor = (int)end;
            }
        }

        if (cursor != artifact.Length)
        {
            throw InvalidArtifact();
        }

        for (var entry = 0; entry < _entryCount; entry++)
        {
            if (ReadU16(_directory, entry * 16 + 2) == 2)
            {
                var start = ReadU32(_directory, entry * 16 + 8);
                var length = ReadU32(_directory, entry * 16 + 12);
                var count = ReadU32(_directory, entry * 16 + 4);
                ValidateSlots(artifact.Slice((int)start, (int)length), Kind(entry), count,
                    sectionCounts);
            }
        }
    }

    public bool TryGetSection(ushort kind, out ReadOnlySpan<byte> section, out int recordCount)
    {
        var low = 0;
        var high = _entryCount;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (Kind(middle) < kind)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low < _entryCount && Kind(low) == kind)
        {
            var start = ReadU32(_directory, low * 16 + 8);
            var length = ReadU32(_directory, low * 16 + 12);
            section = _artifact.Slice((int)start, (int)length);
            recordCount = (int)ReadU32(_directory, low * 16 + 4);
            return true;
        }

        section = default;
        recordCount = 0;
        return false;
    }

    public void GetSection(int entry, out ushort kind, out ushort flags,
        out ReadOnlySpan<byte> section, out int recordCount)
    {
        if (entry < 0 || entry >= _entryCount)
        {
            throw InvalidArtifact();
        }

        var directory = entry * 16;
        kind = (ushort)Kind(entry);
        flags = ReadU16(_directory, directory + 2);
        recordCount = (int)ReadU32(_directory, directory + 4);
        var start = (int)ReadU32(_directory, directory + 8);
        var length = (int)ReadU32(_directory, directory + 12);
        section = _artifact.Slice(start, length);
    }

    private int Kind(int entry)
        => ReadU16(_directory, entry * 16);

    private void ValidateSlots(ReadOnlySpan<byte> section, int kind, uint recordCount,
        ReadOnlySpan<uint> sectionCounts)
    {
        if (section.Length < 4)
        {
            throw InvalidArtifact();
        }

        var slotCount = ReadU32(section, 0);
        var preludeEnd = 4L + slotCount * 16L;
        if (preludeEnd > section.Length
            || (kind == 0x7F00 && preludeEnd + recordCount * 16L != section.Length))
        {
            throw InvalidArtifact();
        }

        for (var slot = 0; slot < slotCount; slot++)
        {
            var offset = 4 + (int)slot * 16;
            var targetKind = ReadU16(section, offset);
            var selector = ReadU16(section, offset + 2);
            var row = ReadU32(section, offset + 4);
            var block = ReadU32(section, offset + 8);
            var ordinal = ReadU32(section, offset + 12);
            if (row is 0 or > 0xFF_FFFF
                || targetKind is < 0x0001 or > 0x0020 or 0x0004
                || FixedStride(targetKind) == -1
                || sectionCounts[targetKind] == 0 || row > sectionCounts[targetKind]
                || (selector == 0 && (block != 0 || ordinal != 0))
                || (selector == 1 && (targetKind != 0x0020 || block == 0))
                || (kind == 0x7F00 && !((selector == 0 && targetKind == 0x001F)
                    || (selector == 1 && targetKind == 0x0020)))
                || selector > 1)
            {
                throw InvalidArtifact();
            }
        }
    }

    private static int FixedStride(int kind) => kind switch
    {
        0x0001 => 44, 0x0002 or 0x0003 or 0x0011 or 0x001B or 0x0020 or 0x7F00 => 0,
        0x0004 => 8, 0x0010 => 12, 0x0012 => 12, 0x0013 => 40,
        0x0014 => 20, 0x0015 => 24, 0x0016 => 28, 0x0017 => 24,
        0x0018 => 16, 0x0019 => 20, 0x001A => 16, 0x001C => 12,
        0x001D => 8, 0x001E => 20, 0x001F => 8, 0x0030 => 16, 0x0031 => 8,
        0x0032 => 16, 0x0033 => 8,
        _ => -1,
    };

    private static bool ValidFeatureRange(uint first, uint count, int featureCount)
    {
        if (first == 0 && count == 0)
        {
            return true;
        }

        return first != 0 && count != 0
            && first <= 0xFF_FFFF && count <= 0xFF_FFFF
            && ((ulong)first + count - 1) <= 0xFF_FFFF
            && ((ulong)first + count) <= ((ulong)featureCount + 1);
    }

    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));

    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidArtifact()
        => new("Invalid TRRM (1, 0) artifact framing.");
}
