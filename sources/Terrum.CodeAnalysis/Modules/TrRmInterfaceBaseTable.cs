// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>An ordered complete base-interface instance.</summary>
/// <param name="OwnerInterfaceDefRow">The one-based owning InterfaceDef row.</param>
/// <param name="Ordinal">The zero-based declaration ordinal.</param>
/// <param name="BaseTypeSpecRef">The complete base-interface TypeSpecRef.</param>
public readonly record struct TrRmInterfaceBaseRow(
    int OwnerInterfaceDefRow, int Ordinal, uint BaseTypeSpecRef);

/// <summary>Validates InterfaceBase ownership, ordinals and complete type references.</summary>
public readonly ref struct TrRmInterfaceBaseTableReader
{
    private const int RowSize = 12;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int[] _rowOwners;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated InterfaceBase rows.</summary>
    public int Count { get; }

    /// <summary>Validates every InterfaceBase row against its InterfaceDef range.</summary>
    /// <param name="section">The borrowed 12-byte InterfaceBase rows.</param>
    /// <param name="recordCount">The number of InterfaceBase rows.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    public TrRmInterfaceBaseTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeSpecTableReader types, TrRmInterfaceDefTableReader interfaces)
        : this(section, recordCount, types, interfaces, null)
    {
    }

    internal TrRmInterfaceBaseTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeSpecTableReader types,
        TrRmInterfaceDefTableReader interfaces,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != interfaces.InterfaceBaseCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidInterfaceBase();
        }

        _rows = section;
        Count = recordCount;
        resourceLedger?.ConsumeWork(checked((ulong)recordCount + 2UL));
        _rowOwners = new int[recordCount + 1];
        _resourceLedger = resourceLedger;
        for (var interfaceDef = 1; interfaceDef <= interfaces.Count; interfaceDef++)
        {
            _resourceLedger?.ConsumeWork();
            var (first, count) = interfaces.GetBaseRange(interfaceDef);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var row = first + ordinal;
                _resourceLedger?.ConsumeWork(2);
                if (_rowOwners[row] != 0)
                {
                    throw InvalidInterfaceBase();
                }

                _rowOwners[row] = interfaceDef;
                var data = GetRow(row);
                var owner = Read(data, 0);
                var rowOrdinal = Read(data, 4);
                var baseTypeSpec = Read(data, 8);
                if (owner != (uint)interfaceDef || rowOrdinal != (uint)ordinal
                    || baseTypeSpec == 0)
                {
                    throw InvalidInterfaceBase();
                }

                _ = types.GetTypeSpecRef(baseTypeSpec);
            }
        }

        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            if (_rowOwners[row] == 0)
            {
                throw InvalidInterfaceBase();
            }
        }
    }

    /// <summary>Gets the one-based owning InterfaceDef row.</summary>
    public int GetOwnerInterfaceDef(int row)
        => checked((int)ReadRow(row, 0));

    /// <summary>Gets the zero-based declaration ordinal.</summary>
    public int GetOrdinal(int row)
        => checked((int)ReadRow(row, 4));

    /// <summary>Gets the complete base TypeSpecRef.</summary>
    public uint GetBaseTypeSpecRef(int row)
        => ReadRow(row, 8);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > Count)
        {
            throw InvalidInterfaceBase();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint ReadRow(int row, int offset)
        => Read(GetRow(row), offset);

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidInterfaceBase()
        => new("Invalid TRRM InterfaceBase table.");
}

/// <summary>Encodes InterfaceBase rows and validates their ranges and type references.</summary>
public static class TrRmInterfaceBaseTableWriter
{
    /// <summary>Encodes ordered InterfaceBase rows in one-based table order.</summary>
    /// <param name="rows">The base-interface rows.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <returns>The encoded InterfaceBase section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmInterfaceBaseRow> rows,
        TrRmTypeSpecTableReader types, TrRmInterfaceDefTableReader interfaces)
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
            Write(result, offset, row.OwnerInterfaceDefRow);
            Write(result, offset + 4, row.Ordinal);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 8),
                row.BaseTypeSpecRef);
        }

        _ = new TrRmInterfaceBaseTableReader(result, rows.Count, types, interfaces);
        return result;
    }

    private const int RowSize = 12;

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset), unchecked((uint)value));
}
