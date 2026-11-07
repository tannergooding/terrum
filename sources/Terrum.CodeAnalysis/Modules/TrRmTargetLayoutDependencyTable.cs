// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A retained type/layout query and its ordered FieldReference range.</summary>
/// <param name="QueryKind">The assigned SizeOf, AlignOf or offset query kind.</param>
/// <param name="Flags">The reserved-zero flags byte.</param>
/// <param name="Reserved">The reserved-zero word.</param>
/// <param name="SubjectTypeSpecRef">The complete queried type.</param>
/// <param name="SelectedTargetProfileRef">The one-based selected TargetProfile row.</param>
/// <param name="FieldFirst">The first owned TargetLayoutDependencyField row.</param>
/// <param name="FieldCount">The number of owned TargetLayoutDependencyField rows.</param>
public readonly record struct TrRmTargetLayoutDependencyRow(
    byte QueryKind, byte Flags, ushort Reserved,
    uint SubjectTypeSpecRef, uint SelectedTargetProfileRef,
    int FieldFirst, int FieldCount);

/// <summary>Validates retained query types, selected profiles and exclusive field ranges.</summary>
public readonly ref struct TrRmTargetLayoutDependencyTableReader
{
    private const int RowSize = 20;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly bool[] _fieldOwned;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated TargetLayoutDependency rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates each query and partitions offset field-reference ranges.</summary>
    /// <param name="section">The borrowed 20-byte TargetLayoutDependency rows.</param>
    /// <param name="recordCount">The number of TargetLayoutDependency rows.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="targetProfiles">The validated TargetProfile table.</param>
    /// <param name="fieldReferenceCount">The number of TargetLayoutDependencyField rows.</param>
    public TrRmTargetLayoutDependencyTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeSpecTableReader types,
        TrRmTargetProfileTableReader targetProfiles, int fieldReferenceCount)
        : this(section, recordCount, types, targetProfiles,
            fieldReferenceCount, null)
    {
    }

    internal TrRmTargetLayoutDependencyTableReader(
        ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeSpecTableReader types,
        TrRmTargetProfileTableReader targetProfiles, int fieldReferenceCount,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (!ValidCount(recordCount) || !ValidCount(fieldReferenceCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidTargetLayoutDependency();
        }

        _rows = section;
        _recordCount = recordCount;
        resourceLedger?.ConsumeWork(checked(
            (ulong)fieldReferenceCount + 1UL));
        _fieldOwned = new bool[fieldReferenceCount + 1];
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var queryKind = ReadByte(data, 0);
            var flags = ReadByte(data, 1);
            var reserved = ReadUInt16(data, 2);
            var subjectType = Read(data, 4);
            var targetProfile = Read(data, 8);
            var first = Read(data, 12);
            var count = Read(data, 16);
            if (queryKind is < 1 or > 4 || flags != 0 || reserved != 0
                || subjectType == 0 || targetProfile is 0 or > 0xFF_FFFF
                || targetProfile > (uint)targetProfiles.Count
                || !targetProfiles.IsAssignedProfileReference(targetProfile)
                || !ValidRange(first, count, fieldReferenceCount)
                || (queryKind is 1 or 2 && count != 0)
                || (queryKind is 3 or 4 && count == 0))
            {
                throw InvalidTargetLayoutDependency();
            }

            _ = types.GetTypeSpecRef(subjectType);
            for (var field = first; field < first + count; field++)
            {
                _resourceLedger?.ConsumeWork(2);
                if (_fieldOwned[field])
                {
                    throw InvalidTargetLayoutDependency();
                }

                _fieldOwned[field] = true;
            }
        }

        for (var field = 1; field <= fieldReferenceCount; field++)
        {
            _resourceLedger?.ConsumeWork();
            if (!_fieldOwned[field])
            {
                throw InvalidTargetLayoutDependency();
            }
        }
    }

    /// <summary>Gets the assigned query kind.</summary>
    public byte GetQueryKind(int row)
        => ReadByte(GetRow(row), 0);

    /// <summary>Gets the complete queried TypeSpecRef.</summary>
    public uint GetSubjectTypeSpecRef(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the one-based selected TargetProfile row.</summary>
    public int GetTargetProfileRow(int row)
        => checked((int)Read(GetRow(row), 8));

    /// <summary>Gets the ordered TargetLayoutDependencyField range.</summary>
    public (int First, int Count) GetFieldRange(int row)
    {
        var data = GetRow(row);
        return (checked((int)Read(data, 12)), checked((int)Read(data, 16)));
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidTargetLayoutDependency();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private byte ReadByte(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(byte) + 1);
        return data[offset];
    }

    private ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(ushort) + 1);
        return BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(offset, sizeof(ushort)));
    }

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static bool ValidRange(uint first, uint count, int total)
        => count == 0 ? first == 0
            : first != 0 && first <= (uint)total
                && count <= (uint)total - first + 1;

    private static InvalidDataException InvalidTargetLayoutDependency()
        => new("Invalid TRRM TargetLayoutDependency table.");
}

/// <summary>Encodes target-layout queries and validates their field ranges.</summary>
public static class TrRmTargetLayoutDependencyTableWriter
{
    private const int RowSize = 20;

    /// <summary>Encodes TargetLayoutDependency rows in one-based table order.</summary>
    /// <param name="rows">The retained layout observations.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="targetProfiles">The validated TargetProfile table.</param>
    /// <param name="fieldReferenceCount">The number of TargetLayoutDependencyField rows.</param>
    /// <returns>The encoded TargetLayoutDependency section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmTargetLayoutDependencyRow> rows,
        TrRmTypeSpecTableReader types, TrRmTargetProfileTableReader targetProfiles,
        int fieldReferenceCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * RowSize;
            result[offset] = row.QueryKind;
            result[offset + 1] = row.Flags;
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(offset + 2), row.Reserved);
            Write(result, offset + 4, row.SubjectTypeSpecRef);
            Write(result, offset + 8, row.SelectedTargetProfileRef);
            Write(result, offset + 12, unchecked((uint)row.FieldFirst));
            Write(result, offset + 16, unchecked((uint)row.FieldCount));
        }

        _ = new TrRmTargetLayoutDependencyTableReader(result, rows.Count,
            types, targetProfiles, fieldReferenceCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
