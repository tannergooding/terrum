// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A nominal enum declaration and its contiguous member range.</summary>
public readonly record struct TrRmEnumDefRow(
    int OwnerTypeDefRow, uint UnderlyingTypeSpecRef, uint Flags,
    int MemberFirst, int MemberCount);

/// <summary>A source member declaration owned by one enum.</summary>
public readonly record struct TrRmEnumMemberDefRow(
    int OwnerTypeDefRow, int Ordinal, int DeclarationKeyStringRow,
    int NameStringRow, int Value);

/// <summary>Validates enum member names, keys, owners and row framing.</summary>
/// <remarks>The borrowed EnumMemberDef, TypeDef and String sections must remain unchanged.</remarks>
public readonly ref struct TrRmEnumMemberDefTableReader
{
    private const int RowSize = 20;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated EnumMemberDef rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates fixed EnumMemberDef rows against their enum TypeDefs.</summary>
    public TrRmEnumMemberDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types)
    {
        if (!types.Schema.SupportsNominalEnums()
            || recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidMembers();
        }

        _rows = section;
        _strings = strings;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var owner = Read(data, 0);
            var ordinal = Read(data, 4);
            var declarationKey = Read(data, 8);
            var name = Read(data, 12);
            if (owner == 0 || owner > types.Count
                || types.GetKind((int)owner) != 4
                || (types.GetFlags((int)owner) & 3) != 2
                || ordinal > 0xFF_FFFF
                || declarationKey is 0 or > 0xFF_FFFF
                || name is 0 or > 0xFF_FFFF)
            {
                throw InvalidMembers();
            }

            _ = strings.DecodeName((int)declarationKey);
            _ = strings.DecodeName((int)name);
        }
    }

    /// <summary>Gets the owning enum TypeDef row.</summary>
    public int GetOwnerTypeDef(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the declaration ordinal within the owning enum.</summary>
    public int GetOrdinal(int row)
        => checked((int)Read(GetRow(row), 4));

    /// <summary>Gets the producer-assigned declaration key.</summary>
    public string GetDeclarationKey(int row)
        => _strings.DecodeName(checked((int)Read(GetRow(row), 8)));

    /// <summary>Gets the source member name.</summary>
    public string GetName(int row)
        => _strings.DecodeName(checked((int)Read(GetRow(row), 12)));

    /// <summary>Gets the checked int32 member value.</summary>
    public int GetValue(int row)
        => unchecked((int)Read(GetRow(row), 16));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidMembers();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static uint Read(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidMembers()
        => new("Invalid TRRM EnumMemberDef table.");
}

/// <summary>Validates enum declarations, underlying types and complete member ownership.</summary>
/// <remarks>The borrowed EnumDef, TypeDef, TypeSpec, Layout and member sections must remain unchanged.</remarks>
public readonly ref struct TrRmEnumDefTableReader
{
    private const int RowSize = 20;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly int _memberCount;
    private readonly int[] _enumRowsByTypeDef;

    /// <summary>Gets the number of validated EnumDef rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates EnumDef rows and the exact enum/member/target-layout relationships.</summary>
    public TrRmEnumDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmLayoutTableReader layouts, TrRmTargetProfileTableReader targetProfiles,
        TrRmEnumMemberDefTableReader members)
    {
        if (!types.Schema.SupportsNominalEnums()
            || (typeSpecs.Count != 0 && typeSpecs.Schema != types.Schema)
            || recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidEnums();
        }

        _rows = section;
        _recordCount = recordCount;
        _memberCount = members.Count;
        var enumByType = new int[types.Count + 1];
        var memberOwners = new int[_memberCount + 1];
        var enumTypeCount = 0;
        for (var type = 1; type <= types.Count; type++)
        {
            if (types.GetKind(type) == 4)
            {
                enumTypeCount++;
            }
        }

        if (enumTypeCount != recordCount)
        {
            throw InvalidEnums();
        }

        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var owner = Read(data, 0);
            var underlyingTypeSpecRef = Read(data, 4);
            var flags = Read(data, 8);
            var memberFirst = Read(data, 12);
            var memberCount = Read(data, 16);
            if (owner == 0 || owner > types.Count
                || types.GetKind((int)owner) != 4
                || (types.GetFlags((int)owner) & 3) != 2
                || enumByType[(int)owner] != 0
                || flags > 1
                || !IsInt32Type(typeSpecs, underlyingTypeSpecRef)
                || !ValidRange(memberFirst, memberCount, members.Count))
            {
                throw InvalidEnums();
            }

            var ownerRow = (int)owner;
            enumByType[ownerRow] = row;
            if (types.GetFieldRange(ownerRow).Start != 0
                || types.GetFieldRange(ownerRow).End != 0
                || types.GetGenericRange(ownerRow).Count != 0
                || types.GetContractRange(ownerRow).Count != 0)
            {
                throw InvalidEnums();
            }

            ValidateLayouts(ownerRow, types, layouts, targetProfiles);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var ordinal = 0; ordinal < memberCount; ordinal++)
            {
                var memberRow = checked((int)memberFirst + ordinal);
                if (memberOwners[memberRow] != 0
                    || members.GetOwnerTypeDef(memberRow) != ownerRow
                    || members.GetOrdinal(memberRow) != ordinal
                    || !keys.Add(members.GetDeclarationKey(memberRow))
                    || !names.Add(members.GetName(memberRow)))
                {
                    throw InvalidEnums();
                }

                memberOwners[memberRow] = row;
            }
        }

        for (var type = 1; type <= types.Count; type++)
        {
            if (types.GetKind(type) == 4 != (enumByType[type] != 0))
            {
                throw InvalidEnums();
            }
        }

        for (var row = 1; row <= _memberCount; row++)
        {
            if (memberOwners[row] == 0)
            {
                throw InvalidEnums();
            }
        }

        _enumRowsByTypeDef = enumByType;
    }

    internal int GetRowForTypeDef(int typeDefRow)
    {
        if (typeDefRow <= 0 || typeDefRow >= _enumRowsByTypeDef.Length
            || _enumRowsByTypeDef[typeDefRow] == 0)
        {
            throw InvalidEnums();
        }

        return _enumRowsByTypeDef[typeDefRow];
    }

    /// <summary>Gets the owning enum TypeDef row.</summary>
    public int GetOwnerTypeDef(int row)
        => checked((int)Read(GetRow(row), 0));

    /// <summary>Gets the exact underlying TypeSpecRef.</summary>
    public uint GetUnderlyingTypeSpecRef(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the enum declaration flags; bit zero denotes Flags.</summary>
    public uint GetFlags(int row)
        => Read(GetRow(row), 8);

    /// <summary>Gets the complete one-based EnumMemberDef range.</summary>
    public (int First, int Count) GetMemberRange(int row)
    {
        var data = GetRow(row);
        return (checked((int)Read(data, 12)), checked((int)Read(data, 16)));
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidEnums();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool IsInt32Type(TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        return type.Form == 0x01 && type.Qualifiers == 0
            && type.TypeCode == 0x08 && type.ArgumentCode == 0;
    }

    private static void ValidateLayouts(int typeDefRow,
        TrRmTypeDefTableReader types, TrRmLayoutTableReader layouts,
        TrRmTargetProfileTableReader targetProfiles)
    {
        var (first, count) = types.GetLayoutRange(typeDefRow);
        var hasWindowsProfile = targetProfiles.TryGetProfile(
            TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
            TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
            out var windowsProfileRow);
        for (var row = first; row < first + count; row++)
        {
            var (fieldFirst, fieldCount) = layouts.GetLayoutFieldRange(row);
            var targetProfile = layouts.GetTargetProfileRef(row);
            var flags = layouts.GetFlags(row);
            if (layouts.GetLayoutKind(row) != 0
                || layouts.GetAlignment(row) != 0 || layouts.GetPack(row) != 0
                || fieldFirst != 0 || fieldCount != 0
                || (targetProfile != 0
                    && (!hasWindowsProfile || targetProfile != (uint)windowsProfileRow))
                || ((flags & 1) != 0
                    && (!hasWindowsProfile || targetProfile != (uint)windowsProfileRow)))
            {
                throw InvalidEnums();
            }
        }
    }

    private static bool ValidRange(uint first, uint count, int total)
        => count == 0 ? first == 0
            : first != 0 && first <= total && count <= (uint)total - first + 1;

    private static uint Read(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidEnums()
        => new("Invalid TRRM EnumDef table.");
}

/// <summary>Encodes enum declaration and member rows with checked ownership.</summary>
public static class TrRmEnumTablesWriter
{
    private const int EnumDefRowSize = 20;
    private const int EnumMemberDefRowSize = 20;

    /// <summary>Encodes EnumMemberDef rows in their physical table order.</summary>
    public static byte[] EncodeMembers(IReadOnlyList<TrRmEnumMemberDefRow> rows,
        TrRmStringTableReader strings, TrRmTypeDefTableReader types)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[checked(rows.Count * EnumMemberDefRowSize)];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * EnumMemberDefRowSize;
            Write(result, offset, row.OwnerTypeDefRow);
            Write(result, offset + 4, row.Ordinal);
            Write(result, offset + 8, row.DeclarationKeyStringRow);
            Write(result, offset + 12, row.NameStringRow);
            Write(result, offset + 16, row.Value);
        }

        _ = new TrRmEnumMemberDefTableReader(result, rows.Count, strings, types);
        return result;
    }

    /// <summary>Encodes EnumDef rows and validates all owned member and layout facts.</summary>
    public static byte[] Encode(IReadOnlyList<TrRmEnumDefRow> rows,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmLayoutTableReader layouts, TrRmTargetProfileTableReader targetProfiles,
        TrRmEnumMemberDefTableReader members)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[checked(rows.Count * EnumDefRowSize)];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * EnumDefRowSize;
            Write(result, offset, row.OwnerTypeDefRow);
            Write(result, offset + 4, row.UnderlyingTypeSpecRef);
            Write(result, offset + 8, row.Flags);
            Write(result, offset + 12, row.MemberFirst);
            Write(result, offset + 16, row.MemberCount);
        }

        _ = new TrRmEnumDefTableReader(result, rows.Count, types, typeSpecs,
            layouts, targetProfiles, members);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
