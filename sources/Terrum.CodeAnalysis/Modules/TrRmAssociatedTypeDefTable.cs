// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>An associated-type requirement directly declared by one InterfaceDef.</summary>
/// <param name="OwnerInterfaceDefRow">The one-based owning InterfaceDef row.</param>
/// <param name="NameStringRow">The one-based nonempty String name row.</param>
/// <param name="DefaultTypeSpecRef">The optional complete default type, or zero when absent.</param>
public readonly record struct TrRmAssociatedTypeDefRow(
    int OwnerInterfaceDefRow, int NameStringRow, uint DefaultTypeSpecRef);

/// <summary>Validates AssociatedTypeDef rows and their InterfaceDef ownership.</summary>
/// <remarks>Associated-declaration DefId and Import identity is a separate binding obligation.</remarks>
public readonly ref struct TrRmAssociatedTypeDefTableReader
{
    private const int RowSize = 12;

    private readonly ReadOnlySpan<byte> _rows;

    /// <summary>Gets the number of validated AssociatedTypeDef rows.</summary>
    public int Count { get; }

    /// <summary>Validates associated declaration ownership, names and optional defaults.</summary>
    /// <param name="section">The borrowed 12-byte AssociatedTypeDef rows.</param>
    /// <param name="recordCount">The number of AssociatedTypeDef rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    public TrRmAssociatedTypeDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeSpecTableReader types,
        TrRmInterfaceDefTableReader interfaces)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != interfaces.AssociatedTypeCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidAssociatedTypeDef();
        }

        _rows = section;
        Count = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var ownerInterface = Read(data, 0);
            var name = Read(data, 4);
            var defaultType = Read(data, 8);
            if (ownerInterface is 0 or > 0xFF_FFFF
                || ownerInterface > interfaces.Count
                || interfaces.GetAssociatedTypeOwner(row) != ownerInterface
                || name is 0 or > 0xFF_FFFF)
            {
                throw InvalidAssociatedTypeDef();
            }

            _ = strings.DecodeName((int)name);
            if (defaultType != 0)
            {
                _ = types.GetTypeSpecRef(defaultType);
            }
        }
    }

    /// <summary>Gets the one-based owning InterfaceDef row.</summary>
    public int GetOwnerInterfaceDef(int row)
        => checked((int)ReadRow(row, 0));

    /// <summary>Gets the one-based name String row.</summary>
    public int GetNameStringRow(int row)
        => checked((int)ReadRow(row, 4));

    /// <summary>Gets the optional default TypeSpecRef, or zero when absent.</summary>
    public uint GetDefaultTypeSpecRef(int row)
        => ReadRow(row, 8);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > Count)
        {
            throw InvalidAssociatedTypeDef();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint ReadRow(int row, int offset)
        => Read(GetRow(row), offset);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidAssociatedTypeDef()
        => new("Invalid TRRM AssociatedTypeDef table.");
}

/// <summary>Encodes AssociatedTypeDef rows and validates their owner and type default.</summary>
public static class TrRmAssociatedTypeDefTableWriter
{
    private const int RowSize = 12;

    /// <summary>Encodes AssociatedTypeDef rows in one-based table order.</summary>
    /// <param name="rows">The directly-declared associated-type requirements.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <returns>The encoded AssociatedTypeDef section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmAssociatedTypeDefRow> rows,
        TrRmStringTableReader strings, TrRmTypeSpecTableReader types,
        TrRmInterfaceDefTableReader interfaces)
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
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset),
                unchecked((uint)row.OwnerInterfaceDefRow));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 4),
                unchecked((uint)row.NameStringRow));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 8),
                row.DefaultTypeSpecRef);
        }

        _ = new TrRmAssociatedTypeDefTableReader(result, rows.Count,
            strings, types, interfaces);
        return result;
    }
}
