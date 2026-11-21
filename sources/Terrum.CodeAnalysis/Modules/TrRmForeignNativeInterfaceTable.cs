// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A foreign interface's exact owner, target profile and vtable offset.</summary>
/// <param name="OwnerInterfaceDefRow">The one-based owning InterfaceDef row.</param>
/// <param name="TargetProfileRow">The one-based target-profile row.</param>
/// <param name="VTableByteOffset">The aligned full-width vtable pointer offset.</param>
public readonly record struct TrRmForeignNativeInterfaceRow(
    uint OwnerInterfaceDefRow, uint TargetProfileRow, uint VTableByteOffset);

/// <summary>Validates provisional ForeignNativeInterface rows under the exact F+I profiles.</summary>
/// <remarks>The borrowed section must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmForeignNativeInterfaceTableReader
{
    private const int RowSize = 12;
    private const uint WindowsX64PointerSize = 8;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated ForeignNativeInterface rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates fixed-width ForeignNativeInterface rows and their typed references.</summary>
    /// <param name="section">The borrowed 12-byte rows.</param>
    /// <param name="recordCount">The nonzero number of rows.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <param name="targetProfiles">The validated TargetProfile table.</param>
    public TrRmForeignNativeInterfaceTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmInterfaceDefTableReader interfaces,
        TrRmTargetProfileTableReader targetProfiles)
        : this(section, recordCount, interfaces, targetProfiles,
            TrRmArtifactSchema.ForeignNativeInterfaceDraft)
    {
    }

    internal TrRmForeignNativeInterfaceTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmInterfaceDefTableReader interfaces,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmArtifactSchema schema)
    {
        if (!schema.UsesForeignNativeInterface()
            || recordCount is <= 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidForeignNativeInterface();
        }

        _rows = section;
        _recordCount = recordCount;
        var owners = new HashSet<uint>();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var ownerInterfaceDef = Read(data, 0);
            var targetProfile = Read(data, 4);
            var vtableByteOffset = Read(data, 8);
            if (ownerInterfaceDef is 0 or > 0xFF_FFFF
                || ownerInterfaceDef > (uint)interfaces.Count
                || targetProfile is 0 or > 0xFF_FFFF
                || !targetProfiles.IsAssignedProfileReference(targetProfile)
                || vtableByteOffset % WindowsX64PointerSize != 0
                || !owners.Add(ownerInterfaceDef))
            {
                throw InvalidForeignNativeInterface();
            }
        }
    }

    /// <summary>Gets the one-based owning InterfaceDef row.</summary>
    public uint GetOwnerInterfaceDef(int row)
        => ReadRow(row, 0);

    /// <summary>Gets the one-based target-profile row.</summary>
    public uint GetTargetProfile(int row)
        => ReadRow(row, 4);

    /// <summary>Gets the full-width vtable byte offset.</summary>
    public uint GetVTableByteOffset(int row)
        => ReadRow(row, 8);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidForeignNativeInterface();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint ReadRow(int row, int offset)
        => Read(GetRow(row), offset);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidForeignNativeInterface()
        => new("Invalid TRRM ForeignNativeInterface table.");
}

/// <summary>Encodes canonical provisional ForeignNativeInterface rows.</summary>
public static class TrRmForeignNativeInterfaceTableWriter
{
    /// <summary>Encodes rows ordered by their owning InterfaceDef row.</summary>
    /// <param name="rows">The nonempty ForeignNativeInterface rows.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <param name="targetProfiles">The validated TargetProfile table.</param>
    /// <returns>The encoded ForeignNativeInterface section.</returns>
    public static byte[] Encode(
        IReadOnlyList<TrRmForeignNativeInterfaceRow> rows,
        TrRmInterfaceDefTableReader interfaces,
        TrRmTargetProfileTableReader targetProfiles)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var orderedRows = new TrRmForeignNativeInterfaceRow[rows.Count];
        for (var index = 0; index < orderedRows.Length; index++)
        {
            orderedRows[index] = rows[index];
        }

        Array.Sort(orderedRows, static (left, right) =>
            left.OwnerInterfaceDefRow.CompareTo(right.OwnerInterfaceDefRow));
        var result = new byte[checked(orderedRows.Length * RowSize)];
        for (var index = 0; index < orderedRows.Length; index++)
        {
            var row = orderedRows[index];
            var offset = index * RowSize;
            Write(result, offset, row.OwnerInterfaceDefRow);
            Write(result, offset + sizeof(uint), row.TargetProfileRow);
            Write(result, offset + (2 * sizeof(uint)), row.VTableByteOffset);
        }

        _ = new TrRmForeignNativeInterfaceTableReader(result,
            orderedRows.Length, interfaces, targetProfiles);
        return result;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
