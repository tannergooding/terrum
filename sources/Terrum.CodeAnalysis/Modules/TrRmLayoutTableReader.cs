// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Checks Layout row framing, target variants and complete field-list ranges.</summary>
/// <remarks>Physical offsets, size and final alignment are derived by the selected target ABI.</remarks>
public readonly ref struct TrRmLayoutTableReader
{
    private const int RowSize = 28;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmTypeDefTableReader _types;
    private readonly TrRmTargetProfileTableReader _targetProfiles;
    private readonly int _recordCount;
    private readonly int _layoutFieldCount;
    private readonly int[] _ownerTypeDefs;

    /// <summary>Gets the number of validated Layout rows.</summary>
    public int Count => _recordCount;

    /// <summary>Gets the number of LayoutField rows referenced by these variants.</summary>
    public int LayoutFieldCount => _layoutFieldCount;

    /// <summary>Validates Layout variants and their owner, target-profile and field ranges.</summary>
    /// <param name="section">The borrowed Layout section bytes.</param>
    /// <param name="recordCount">The number of 28-byte Layout rows.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="targetProfiles">The validated TargetProfile table.</param>
    /// <param name="layoutFieldCount">The number of artifact LayoutField rows.</param>
    public TrRmLayoutTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader types, TrRmTargetProfileTableReader targetProfiles,
        int layoutFieldCount)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != types.LayoutCount
            || layoutFieldCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length
            || (recordCount == 0 && layoutFieldCount != 0))
        {
            throw InvalidLayout();
        }

        _rows = section;
        _types = types;
        _targetProfiles = targetProfiles;
        _recordCount = recordCount;
        _layoutFieldCount = layoutFieldCount;
        _ownerTypeDefs = new int[recordCount + 1];

        for (var type = 1; type <= types.Count; type++)
        {
            var (first, count) = types.GetLayoutRange(type);
            if (count == 0)
            {
                throw InvalidLayout();
            }

            var hasDefault = false;
            var profiles = new HashSet<uint>();
            for (var row = first; row < first + count; row++)
            {
                if (row <= 0 || row > recordCount || _ownerTypeDefs[row] != 0)
                {
                    throw InvalidLayout();
                }

                var data = section.Slice((row - 1) * RowSize, RowSize);
                var owner = Read(data, 0);
                var targetProfile = Read(data, 4);
                var kind = data[8];
                var flags = data[9];
                var reserved = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
                var alignment = Read(data, 12);
                var pack = Read(data, 16);
                var fieldFirst = Read(data, 20);
                var fieldCount = Read(data, 24);
                if (owner != type || targetProfile > targetProfiles.Count
                    || (targetProfile != 0
                        && !targetProfiles.IsAssignedProfileReference(targetProfile))
                    || kind > 4 || (flags & ~1) != 0 || reserved != 0
                    || (alignment != 0 && (alignment & (alignment - 1)) != 0)
                    || pack is not (0 or 1 or 2 or 4 or 8 or 16)
                    || !ValidRange(fieldFirst, fieldCount, layoutFieldCount)
                    || ((flags & 1) != 0 && targetProfile == 0)
                    || (kind == 0 && (alignment != 0 || pack != 0))
                    || (kind == 3 && (alignment != 0 || pack != 0))
                    || !profiles.Add(targetProfile))
                {
                    throw InvalidLayout();
                }

                hasDefault |= targetProfile == 0;
                _ownerTypeDefs[row] = type;
            }

            if (!hasDefault)
            {
                throw InvalidLayout();
            }
        }

        for (var row = 1; row <= recordCount; row++)
        {
            if (_ownerTypeDefs[row] == 0)
            {
                throw InvalidLayout();
            }
        }
    }

    /// <summary>Gets the owning TypeDef row for a validated Layout row.</summary>
    public int GetOwnerTypeDef(int row)
    {
        CheckRow(row);
        return _ownerTypeDefs[row];
    }

    /// <summary>Gets the TargetProfile row, or zero for the default variant.</summary>
    public uint GetTargetProfileRef(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the assigned layout kind for a validated Layout row.</summary>
    public byte GetLayoutKind(int row)
        => GetRow(row)[8];

    /// <summary>Gets the Layout flags for a validated Layout row.</summary>
    public byte GetFlags(int row)
        => GetRow(row)[9];

    /// <summary>Gets the declared aggregate alignment, or zero when omitted.</summary>
    public uint GetAlignment(int row)
        => Read(GetRow(row), 12);

    /// <summary>Gets the declared packing limit, or zero when not applicable.</summary>
    public uint GetPack(int row)
        => Read(GetRow(row), 16);

    /// <summary>Gets the complete ordered LayoutField range for a validated Layout row.</summary>
    public (int First, int Count) GetLayoutFieldRange(int row)
    {
        var data = GetRow(row);
        return (checked((int)Read(data, 20)), checked((int)Read(data, 24)));
    }

    /// <summary>Gets the exact profile variant, or the default variant if absent.</summary>
    public int GetLayoutForProfile(int typeDefRow, uint targetProfileRow)
    {
        if (typeDefRow <= 0 || typeDefRow > _types.Count
            || targetProfileRow > _targetProfiles.Count)
        {
            throw InvalidLayout();
        }

        var (first, count) = _types.GetLayoutRange(typeDefRow);
        var defaultRow = 0;
        for (var row = first; row < first + count; row++)
        {
            var target = GetTargetProfileRef(row);
            if (target == targetProfileRow && targetProfileRow != 0)
            {
                return row;
            }
            if (target == 0)
            {
                defaultRow = row;
            }
        }

        return defaultRow != 0 ? defaultRow : throw InvalidLayout();
    }

    /// <summary>Reports whether the exact registered target profile has a Blittable variant.</summary>
    public bool HasBlittableLayout(int typeDefRow, uint profileId, uint ruleRevision)
    {
        if (!_targetProfiles.TryGetProfile(profileId, ruleRevision, out var profileRow))
        {
            return false;
        }

        var layout = GetLayoutForProfile(typeDefRow, (uint)profileRow);
        return GetTargetProfileRef(layout) == (uint)profileRow
            && (GetFlags(layout) & 1) != 0;
    }

    /// <summary>Reports whether any complete Layout variant has the Blittable flag.</summary>
    public bool HasBlittableVariant(int typeDefRow)
    {
        if (typeDefRow <= 0 || typeDefRow > _types.Count)
        {
            throw InvalidLayout();
        }

        var (first, count) = _types.GetLayoutRange(typeDefRow);
        for (var row = first; row < first + count; row++)
        {
            if ((GetFlags(row) & 1) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        CheckRow(row);
        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private void CheckRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidLayout();
        }
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static bool ValidRange(uint first, uint count, int total)
        => count == 0 ? first == 0
            : first != 0 && first <= total && count <= (uint)total - first + 1;

    private static InvalidDataException InvalidLayout()
        => new("Invalid TRRM Layout table.");
}
