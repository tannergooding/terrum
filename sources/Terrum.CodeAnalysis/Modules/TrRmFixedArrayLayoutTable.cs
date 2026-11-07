// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmFixedArrayLayoutRow(
    int OwnerModuleRow, int ArrayTypeSpecRow, uint TargetProfileRef,
    ulong ElementStride, ulong ByteExtent, ulong Alignment);

internal readonly ref struct TrRmFixedArrayLayoutTableReader
{
    private const int RowSize = 40;
    private readonly TrRmTypeSpecTableReader _typeSpecs;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;
    private readonly int _recordCount;
    private readonly TrRmFixedArrayLayoutRow[] _layoutRows;
    private readonly Dictionary<(int OwnerModule, int ArrayTypeSpecRow,
        uint TargetProfile), int> _exactRows;
    private readonly Dictionary<(int OwnerModule, uint TargetProfile,
        int TypeHash), List<int>> _normalizedRows;

    public int Count => _recordCount;

    internal TrRmFixedArrayLayoutTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeSpecTableReader typeSpecs,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork(3);
        if (recordCount is < 0 or > 0xFF_FFFF)
        {
            throw new InvalidDataException(
                "TRRM FixedArrayLayout row count exceeds its 24-bit table limit.");
        }
        var hasWindowsProfile = targetProfiles.TryGetProfile(
            TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
            TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
            out var expectedProfile);
        resourceLedger?.ConsumeWork(3);
        if (!schema.UsesForeignInvocationStorageFixedArrays()
            || (long)recordCount * RowSize != section.Length
            || (recordCount == 0 && !section.IsEmpty)
            || (recordCount != 0 && !hasWindowsProfile))
        {
            throw InvalidLayout();
        }

        _resourceLedger = resourceLedger;
        var layoutEntryCount = checked((ulong)recordCount + 1);
        var moduleEntryCount = checked((ulong)modules.Count + 1);
        _resourceLedger?.ConsumeWork(checked(1UL
            + (layoutEntryCount * 37UL)
            + (moduleEntryCount * 4UL) + 3UL));
        _typeSpecs = typeSpecs;
        _recordCount = recordCount;
        _layoutRows = new TrRmFixedArrayLayoutRow[recordCount + 1];
        _resourceLedger?.ConsumeWork(4);
        _exactRows = new Dictionary<(int OwnerModule, int ArrayTypeSpecRow,
            uint TargetProfile), int>();
        _normalizedRows = new Dictionary<(int OwnerModule, uint TargetProfile,
            int TypeHash), List<int>>();
        var featureRequirements = new bool[modules.Count + 1];
        var featureRequirementsChecked = new bool[modules.Count + 1];
        var keys = new HashSet<(int OwnerModule, int ArrayTypeSpecRow,
            uint TargetProfile)>();
        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var data = section.Slice((row - 1) * RowSize, RowSize);
            var ownerModule = ReadInt(data, 0);
            var arrayTypeSpecRow = ReadInt(data, 4);
            var targetProfile = ReadUInt32(data, 8);
            var elementStride = ReadUInt64(data, 16);
            var byteExtent = ReadUInt64(data, 24);
            var alignment = ReadUInt64(data, 32);
            _resourceLedger?.ConsumeWork();
            if (ownerModule <= 0)
            {
                throw InvalidLayout();
            }
            _resourceLedger?.ConsumeWork();
            if (ownerModule > modules.Count)
            {
                throw InvalidLayout();
            }
            _resourceLedger?.ConsumeWork();
            if (arrayTypeSpecRow <= 0)
            {
                throw InvalidLayout();
            }
            if (!_typeSpecs.TryGetFixedInlineArray(
                    (uint)arrayTypeSpecRow, out _, out var elementCount))
            {
                throw InvalidLayout();
            }
            _resourceLedger?.ConsumeWork();
            if (targetProfile == 0)
            {
                throw InvalidLayout();
            }
            _resourceLedger?.ConsumeWork();
            if (targetProfile != (uint)expectedProfile)
            {
                throw InvalidLayout();
            }
            var reserved = ReadUInt32(data, 12);
            _resourceLedger?.ConsumeWork();
            if (reserved != 0)
            {
                throw InvalidLayout();
            }
            _resourceLedger?.ConsumeWork();
            if (alignment == 0)
            {
                throw InvalidLayout();
            }
            _resourceLedger?.ConsumeWork();
            if ((alignment & (alignment - 1)) != 0)
            {
                throw InvalidLayout();
            }
            _resourceLedger?.ConsumeWork();
            if (elementStride % alignment != 0)
            {
                throw InvalidLayout();
            }
            if (!HasExtent(elementCount, elementStride, byteExtent))
            {
                throw InvalidLayout();
            }

            var key = (ownerModule, arrayTypeSpecRow, targetProfile);
            _resourceLedger?.ConsumeWork(4);
            if (keys.Contains(key))
            {
                throw InvalidLayout();
            }
            _resourceLedger?.ConsumeWork(13);
            _ = keys.Add(key);

            _resourceLedger?.ConsumeWork(2);
            if (!featureRequirementsChecked[ownerModule])
            {
                _resourceLedger?.ConsumeWork();
                featureRequirements[ownerModule]
                    = TrRmFeatureManifestValidator.Requires(features, modules,
                        ownerModule,
                        TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
                        schema);
                _resourceLedger?.ConsumeWork(2);
                featureRequirementsChecked[ownerModule] = true;
            }
            _resourceLedger?.ConsumeWork(2);
            if (!featureRequirements[ownerModule])
            {
                throw InvalidLayout();
            }

            _resourceLedger?.ConsumeWork(37);
            _layoutRows[row] = new TrRmFixedArrayLayoutRow(ownerModule,
                arrayTypeSpecRow, targetProfile, elementStride, byteExtent,
                alignment);
            _resourceLedger?.ConsumeWork(20);
            _exactRows.Add((ownerModule, arrayTypeSpecRow, targetProfile), row);
            _resourceLedger?.ConsumeWork();
            var normalizedKey = (ownerModule, targetProfile,
                _typeSpecs.GetTypeSpecStructuralHash(
                    (uint)arrayTypeSpecRow));
            _resourceLedger?.ConsumeWork(4);
            if (!_normalizedRows.TryGetValue(normalizedKey,
                    out var equivalentRows))
            {
                _resourceLedger?.ConsumeWork(1);
                equivalentRows = [];
                _resourceLedger?.ConsumeWork(21);
                _normalizedRows.Add(normalizedKey, equivalentRows);
            }
            else
            {
                foreach (var existingArrayTypeSpecRow in equivalentRows)
                {
                    _resourceLedger?.ConsumeWork(5);
                    if (_typeSpecs.AreEquivalentTypeSpecRefs(
                            (uint)existingArrayTypeSpecRow,
                            (uint)arrayTypeSpecRow))
                    {
                        throw InvalidLayout();
                    }
                }
            }
            _resourceLedger?.ConsumeWork(5);
            equivalentRows.Add(arrayTypeSpecRow);
        }

        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var layout = _layoutRows[row];
            _ = _typeSpecs.TryGetFixedInlineArray(
                (uint)layout.ArrayTypeSpecRow, out var elementTypeSpecRef, out _);
            var elementType = _typeSpecs.GetTypeSpecRef(elementTypeSpecRef);
            if (elementType.IsRow && elementType.Form == 0x11)
            {
                _resourceLedger?.ConsumeWork(4);
                if (!keys.Contains((layout.OwnerModuleRow, elementType.Row,
                        layout.TargetProfileRef)))
                {
                    throw InvalidLayout();
                }
            }
        }
    }

    public TrRmFixedArrayLayoutRow GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidLayout();
        }

        return _layoutRows[row];
    }

    internal int FindRow(int ownerModuleRow, uint arrayTypeSpecRef,
        uint targetProfileRef)
    {
        _resourceLedger?.ConsumeWork();
        var reference = _typeSpecs.GetTypeSpecRef(arrayTypeSpecRef);
        _resourceLedger?.ConsumeWork(4);
        if (reference.IsRow
            && _exactRows.TryGetValue((ownerModuleRow, reference.Row,
                targetProfileRef), out var exactRow))
        {
            return exactRow;
        }

        _resourceLedger?.ConsumeWork();
        var normalizedKey = (ownerModuleRow, targetProfileRef,
            _typeSpecs.GetTypeSpecStructuralHash(arrayTypeSpecRef));
        _resourceLedger?.ConsumeWork(4);
        if (_normalizedRows.TryGetValue(normalizedKey, out var equivalentRows))
        {
            foreach (var arrayTypeSpecRow in equivalentRows)
            {
                _resourceLedger?.ConsumeWork(5);
                var candidate = _layoutRows[_exactRows[
                    (ownerModuleRow, arrayTypeSpecRow, targetProfileRef)]];
                if (_typeSpecs.AreEquivalentTypeSpecRefs(
                    (uint)candidate.ArrayTypeSpecRow, arrayTypeSpecRef))
                {
                    return _exactRows[(ownerModuleRow,
                        candidate.ArrayTypeSpecRow, targetProfileRef)];
                }
            }
        }

        return 0;
    }

    private bool HasExtent(ulong count, ulong stride, ulong extent)
    {
        _resourceLedger?.ConsumeWork();
        if (count == 0)
        {
            _resourceLedger?.ConsumeWork();
            return extent == 0;
        }

        _resourceLedger?.ConsumeWork(2);
        if (stride > ulong.MaxValue / count)
        {
            return false;
        }

        _resourceLedger?.ConsumeWork(2);
        return count * stride == extent;
    }

    private int ReadInt(ReadOnlySpan<byte> data, int offset)
    {
        var value = ReadUInt32(data, offset);
        _resourceLedger?.ConsumeWork(2);
        return value is > 0 and <= 0xFF_FFFF
            ? checked((int)value)
            : 0;
    }

    private uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private ulong ReadUInt64(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(ulong) + 1);
        return BinaryPrimitives.ReadUInt64LittleEndian(
            data.Slice(offset, sizeof(ulong)));
    }

    private static InvalidDataException InvalidLayout()
        => new("Invalid TRRM FixedArrayLayout table.");
}

internal static class TrRmFixedArrayLayoutTableWriter
{
    internal static byte[] Encode(IReadOnlyList<TrRmFixedArrayLayoutRow> rows,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[checked(rows.Count * 40)];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 40;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset),
                checked((uint)row.OwnerModuleRow));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 4),
                checked((uint)row.ArrayTypeSpecRow));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 8),
                row.TargetProfileRef);
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(offset + 16),
                row.ElementStride);
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(offset + 24),
                row.ByteExtent);
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(offset + 32),
                row.Alignment);
        }

        _ = new TrRmFixedArrayLayoutTableReader(result, rows.Count, typeSpecs,
            targetProfiles, features, modules, schema);
        return result;
    }
}
