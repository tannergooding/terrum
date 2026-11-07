// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>An initialized interface-owned constant declaration.</summary>
/// <param name="OwnerInterfaceDefRow">The one-based owning InterfaceDef row.</param>
/// <param name="NameStringRow">The one-based nonempty declaration name.</param>
/// <param name="ConstantTypeSpecRef">The complete constant type.</param>
/// <param name="ConstantSourceRow">The one-based ConstantSource row.</param>
public readonly record struct TrRmInterfaceConstDefRow(
    int OwnerInterfaceDefRow, int NameStringRow,
    uint ConstantTypeSpecRef, int ConstantSourceRow);

/// <summary>An associated-constant requirement declared by one interface.</summary>
/// <param name="OwnerInterfaceDefRow">The one-based owning InterfaceDef row.</param>
/// <param name="NameStringRow">The one-based nonempty declaration name.</param>
/// <param name="ConstantTypeSpecRef">The complete constant type.</param>
public readonly record struct TrRmAssociatedConstDefRow(
    int OwnerInterfaceDefRow, int NameStringRow, uint ConstantTypeSpecRef);

/// <summary>Validates InterfaceConstDef ownership, names, types and source rows.</summary>
public readonly ref struct TrRmInterfaceConstDefTableReader
{
    private const int RowSize = 16;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated InterfaceConstDef rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates every InterfaceConstDef row against its interface range.</summary>
    /// <param name="section">The borrowed 16-byte InterfaceConstDef rows.</param>
    /// <param name="recordCount">The number of InterfaceConstDef rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <param name="constantSourceCount">The number of ConstantSource rows.</param>
    public TrRmInterfaceConstDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeSpecTableReader types,
        TrRmInterfaceDefTableReader interfaces, int constantSourceCount)
        : this(section, recordCount, strings, types, interfaces,
            constantSourceCount, null)
    {
    }

    internal TrRmInterfaceConstDefTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmTypeSpecTableReader types, TrRmInterfaceDefTableReader interfaces,
        int constantSourceCount,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != interfaces.InterfaceConstCount
            || constantSourceCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidInterfaceConstDef();
        }

        _rows = section;
        _recordCount = recordCount;
        resourceLedger?.ConsumeWork(checked((ulong)recordCount + 2UL));
        var rowOwners = new int[recordCount + 1];
        _resourceLedger = resourceLedger;
        for (var interfaceDef = 1; interfaceDef <= interfaces.Count; interfaceDef++)
        {
            _resourceLedger?.ConsumeWork();
            var (first, count) = interfaces.GetInterfaceConstRange(interfaceDef);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var row = first + ordinal;
                _resourceLedger?.ConsumeWork(2);
                if (rowOwners[row] != 0)
                {
                    throw InvalidInterfaceConstDef();
                }

                rowOwners[row] = interfaceDef;
                var data = GetRow(row);
                var owner = Read(data, 0);
                var name = Read(data, 4);
                var type = Read(data, 8);
                var source = Read(data, 12);
                if (owner != (uint)interfaceDef || name is 0 or > 0xFF_FFFF
                    || type == 0 || source is 0 or > 0xFF_FFFF
                    || source > (uint)constantSourceCount)
                {
                    throw InvalidInterfaceConstDef();
                }

                _ = strings.DecodeName((int)name);
                _ = types.GetTypeSpecRef(type);
            }
        }

        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            if (rowOwners[row] == 0)
            {
                throw InvalidInterfaceConstDef();
            }
        }
    }

    /// <summary>Gets the one-based owning InterfaceDef row.</summary>
    public int GetOwnerInterfaceDef(int row)
        => checked((int)ReadRow(row, 0));

    /// <summary>Gets the one-based name String row.</summary>
    public int GetNameStringRow(int row)
        => checked((int)ReadRow(row, 4));

    /// <summary>Gets the complete constant TypeSpecRef.</summary>
    public uint GetConstantTypeSpecRef(int row)
        => ReadRow(row, 8);

    /// <summary>Gets the one-based ConstantSource row.</summary>
    public int GetConstantSourceRow(int row)
        => checked((int)ReadRow(row, 12));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidInterfaceConstDef();
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

    private static InvalidDataException InvalidInterfaceConstDef()
        => new("Invalid TRRM InterfaceConstDef table.");
}

/// <summary>Encodes InterfaceConstDef rows with checked type and ConstantSource references.</summary>
public static class TrRmInterfaceConstDefTableWriter
{
    private const int RowSize = 16;

    /// <summary>Encodes InterfaceConstDef rows in one-based table order.</summary>
    /// <param name="rows">The initialized interface-owned constants.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <param name="constantSourceCount">The number of ConstantSource rows.</param>
    /// <returns>The encoded InterfaceConstDef section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmInterfaceConstDefRow> rows,
        TrRmStringTableReader strings, TrRmTypeSpecTableReader types,
        TrRmInterfaceDefTableReader interfaces, int constantSourceCount)
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
            Write(result, offset + 4, row.NameStringRow);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 8),
                row.ConstantTypeSpecRef);
            Write(result, offset + 12, row.ConstantSourceRow);
        }

        _ = new TrRmInterfaceConstDefTableReader(result, rows.Count,
            strings, types, interfaces, constantSourceCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset), unchecked((uint)value));
}

/// <summary>Validates AssociatedConstDef ownership, names and complete type references.</summary>
public readonly ref struct TrRmAssociatedConstDefTableReader
{
    private const int RowSize = 12;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated AssociatedConstDef rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates every AssociatedConstDef row against its interface range.</summary>
    /// <param name="section">The borrowed 12-byte AssociatedConstDef rows.</param>
    /// <param name="recordCount">The number of AssociatedConstDef rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    public TrRmAssociatedConstDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmTypeSpecTableReader types,
        TrRmInterfaceDefTableReader interfaces)
        : this(section, recordCount, strings, types, interfaces, null)
    {
    }

    internal TrRmAssociatedConstDefTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmTypeSpecTableReader types, TrRmInterfaceDefTableReader interfaces,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != interfaces.AssociatedConstCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidAssociatedConstDef();
        }

        _rows = section;
        _recordCount = recordCount;
        resourceLedger?.ConsumeWork(checked((ulong)recordCount + 2UL));
        var rowOwners = new int[recordCount + 1];
        _resourceLedger = resourceLedger;
        for (var interfaceDef = 1; interfaceDef <= interfaces.Count; interfaceDef++)
        {
            _resourceLedger?.ConsumeWork();
            var (first, count) = interfaces.GetAssociatedConstRange(interfaceDef);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var row = first + ordinal;
                _resourceLedger?.ConsumeWork(2);
                if (rowOwners[row] != 0)
                {
                    throw InvalidAssociatedConstDef();
                }

                rowOwners[row] = interfaceDef;
                var data = GetRow(row);
                var owner = Read(data, 0);
                var name = Read(data, 4);
                var type = Read(data, 8);
                if (owner != (uint)interfaceDef || name is 0 or > 0xFF_FFFF
                    || type == 0)
                {
                    throw InvalidAssociatedConstDef();
                }

                _ = strings.DecodeName((int)name);
                _ = types.GetTypeSpecRef(type);
            }
        }

        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            if (rowOwners[row] == 0)
            {
                throw InvalidAssociatedConstDef();
            }
        }
    }

    /// <summary>Gets the one-based owning InterfaceDef row.</summary>
    public int GetOwnerInterfaceDef(int row)
        => checked((int)ReadRow(row, 0));

    /// <summary>Gets the one-based name String row.</summary>
    public int GetNameStringRow(int row)
        => checked((int)ReadRow(row, 4));

    /// <summary>Gets the complete constant TypeSpecRef.</summary>
    public uint GetConstantTypeSpecRef(int row)
        => ReadRow(row, 8);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidAssociatedConstDef();
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

    private static InvalidDataException InvalidAssociatedConstDef()
        => new("Invalid TRRM AssociatedConstDef table.");
}

/// <summary>Encodes AssociatedConstDef rows and validates their owners and types.</summary>
public static class TrRmAssociatedConstDefTableWriter
{
    private const int RowSize = 12;

    /// <summary>Encodes AssociatedConstDef rows in one-based table order.</summary>
    /// <param name="rows">The associated-constant requirements.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <returns>The encoded AssociatedConstDef section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmAssociatedConstDefRow> rows,
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
            Write(result, offset, row.OwnerInterfaceDefRow);
            Write(result, offset + 4, row.NameStringRow);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 8),
                row.ConstantTypeSpecRef);
        }

        _ = new TrRmAssociatedConstDefTableReader(result, rows.Count,
            strings, types, interfaces);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset), unchecked((uint)value));
}
