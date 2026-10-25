// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A registry-shaped interface descriptor and its owned declaration ranges.</summary>
/// <param name="OwnerTypeDefRow">The one-based interface TypeDef row.</param>
/// <param name="SelfGenericParamRow">The optional one-based self Type formal row.</param>
/// <param name="BaseFirst">The first directly-declared InterfaceBase row, or zero when empty.</param>
/// <param name="BaseCount">The number of directly-declared InterfaceBase rows.</param>
/// <param name="MethodFirst">The first directly-declared InterfaceMethod row, or zero when empty.</param>
/// <param name="MethodCount">The number of directly-declared InterfaceMethod rows.</param>
/// <param name="AssociatedTypeFirst">The first AssociatedTypeDef row, or zero when empty.</param>
/// <param name="AssociatedTypeCount">The number of AssociatedTypeDef rows.</param>
/// <param name="AssociatedConstFirst">The first AssociatedConstDef row, or zero when empty.</param>
/// <param name="AssociatedConstCount">The number of AssociatedConstDef rows.</param>
/// <param name="InterfaceConstFirst">The first InterfaceConstDef row, or zero when empty.</param>
/// <param name="InterfaceConstCount">The number of InterfaceConstDef rows.</param>
public readonly record struct TrRmInterfaceDefRow(
    int OwnerTypeDefRow, int SelfGenericParamRow,
    int BaseFirst, int BaseCount, int MethodFirst, int MethodCount,
    int AssociatedTypeFirst, int AssociatedTypeCount,
    int AssociatedConstFirst, int AssociatedConstCount,
    int InterfaceConstFirst, int InterfaceConstCount);

/// <summary>Validates InterfaceDef ownership, formal identity and declaration-range extents.</summary>
/// <remarks>AssociatedTypeDef ranges are partitioned here; other declaration ranges still need their row-owner validators.</remarks>
public readonly ref struct TrRmInterfaceDefTableReader
{
    private const int RowSize = 48;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int[] _associatedTypeOwners;
    private readonly int _recordCount;
    private readonly int _baseCount;
    private readonly int _methodCount;
    private readonly int _associatedConstCount;
    private readonly int _interfaceConstCount;

    /// <summary>Gets the number of validated InterfaceDef rows.</summary>
    public int Count => _recordCount;

    /// <summary>Gets the number of AssociatedTypeDef rows in the owning ranges.</summary>
    public int AssociatedTypeCount => _associatedTypeOwners.Length - 1;

    /// <summary>Gets the number of InterfaceBase rows.</summary>
    public int InterfaceBaseCount => _baseCount;

    /// <summary>Gets the number of InterfaceMethod rows.</summary>
    public int InterfaceMethodCount => _methodCount;

    /// <summary>Gets the number of AssociatedConstDef rows.</summary>
    public int AssociatedConstCount => _associatedConstCount;

    /// <summary>Gets the number of InterfaceConstDef rows.</summary>
    public int InterfaceConstCount => _interfaceConstCount;

    /// <summary>Validates InterfaceDef rows and their associated-type ranges.</summary>
    /// <param name="section">The borrowed 48-byte InterfaceDef rows.</param>
    /// <param name="recordCount">The number of InterfaceDef rows.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="formals">The validated GenericParam table.</param>
    /// <param name="interfaceBaseCount">The number of InterfaceBase rows.</param>
    /// <param name="interfaceMethodCount">The number of InterfaceMethod rows.</param>
    /// <param name="associatedTypeCount">The number of AssociatedTypeDef rows.</param>
    /// <param name="associatedConstCount">The number of AssociatedConstDef rows.</param>
    /// <param name="interfaceConstCount">The number of InterfaceConstDef rows.</param>
    public TrRmInterfaceDefTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader types, TrRmGenericParamTableReader formals,
        int interfaceBaseCount, int interfaceMethodCount, int associatedTypeCount,
        int associatedConstCount, int interfaceConstCount)
    {
        if (!ValidCount(recordCount) || !ValidCount(interfaceBaseCount)
            || !ValidCount(interfaceMethodCount) || !ValidCount(associatedTypeCount)
            || !ValidCount(associatedConstCount) || !ValidCount(interfaceConstCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidInterfaceDef();
        }

        _rows = section;
        _recordCount = recordCount;
        _baseCount = interfaceBaseCount;
        _methodCount = interfaceMethodCount;
        _associatedConstCount = associatedConstCount;
        _interfaceConstCount = interfaceConstCount;
        _associatedTypeOwners = new int[associatedTypeCount + 1];
        var interfaceOwners = new int[types.Count + 1];

        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var ownerTypeDef = Read(data, 0);
            var selfFormal = Read(data, 4);
            if (ownerTypeDef is 0 or > 0xFF_FFFF || ownerTypeDef > types.Count
                || ((types.GetFlags((int)ownerTypeDef) >> 2) & 3) != 3
                || interfaceOwners[ownerTypeDef] != 0)
            {
                throw InvalidInterfaceDef();
            }

            interfaceOwners[ownerTypeDef] = row;
            if (selfFormal != 0)
            {
                var (firstFormal, formalCount) = types.GetGenericRange((int)ownerTypeDef);
                if (selfFormal > (uint)formals.Count || firstFormal == 0
                    || selfFormal < firstFormal
                    || selfFormal >= (uint)firstFormal + (uint)formalCount
                    || formals.GetFormalKind((int)selfFormal) != 1
                    || formals.GetOwnerDefId((int)selfFormal)
                        != (0x1300_0000u | ownerTypeDef))
                {
                    throw InvalidInterfaceDef();
                }
            }

            var baseFirst = Read(data, 8);
            var baseCount = Read(data, 12);
            var methodFirst = Read(data, 16);
            var methodCount = Read(data, 20);
            var associatedTypeFirst = Read(data, 24);
            var associatedTypeRangeCount = Read(data, 28);
            var associatedConstFirst = Read(data, 32);
            var associatedConstRangeCount = Read(data, 36);
            var interfaceConstFirst = Read(data, 40);
            var interfaceConstRangeCount = Read(data, 44);
            if (!ValidRange(baseFirst, baseCount, interfaceBaseCount)
                || !ValidRange(methodFirst, methodCount, interfaceMethodCount)
                || !ValidRange(associatedTypeFirst, associatedTypeRangeCount,
                    associatedTypeCount)
                || !ValidRange(associatedConstFirst, associatedConstRangeCount,
                    associatedConstCount)
                || !ValidRange(interfaceConstFirst, interfaceConstRangeCount,
                    interfaceConstCount))
            {
                throw InvalidInterfaceDef();
            }

            for (var associatedRow = associatedTypeFirst;
                associatedRow < associatedTypeFirst + associatedTypeRangeCount;
                associatedRow++)
            {
                if (_associatedTypeOwners[associatedRow] != 0)
                {
                    throw InvalidInterfaceDef();
                }

                _associatedTypeOwners[associatedRow] = row;
            }
        }

        for (var typeDef = 1; typeDef <= types.Count; typeDef++)
        {
            var isInterface = ((types.GetFlags(typeDef) >> 2) & 3) == 3;
            if (isInterface != (interfaceOwners[typeDef] != 0))
            {
                throw InvalidInterfaceDef();
            }
        }

        for (var associatedType = 1; associatedType <= associatedTypeCount; associatedType++)
        {
            if (_associatedTypeOwners[associatedType] == 0)
            {
                throw InvalidInterfaceDef();
            }
        }
    }

    /// <summary>Gets the one-based owning TypeDef row.</summary>
    public int GetOwnerTypeDef(int row)
        => checked((int)ReadRow(row, 0));

    /// <summary>Gets the optional one-based self GenericParam row.</summary>
    public int GetSelfGenericParam(int row)
        => checked((int)ReadRow(row, 4));

    /// <summary>Gets the directly-declared AssociatedTypeDef range.</summary>
    public (int First, int Count) GetAssociatedTypeRange(int row)
        => (checked((int)ReadRow(row, 24)), checked((int)ReadRow(row, 28)));

    /// <summary>Gets the directly-declared InterfaceBase range.</summary>
    public (int First, int Count) GetBaseRange(int row)
        => (checked((int)ReadRow(row, 8)), checked((int)ReadRow(row, 12)));

    /// <summary>Gets the directly-declared InterfaceMethod range.</summary>
    public (int First, int Count) GetMethodRange(int row)
        => (checked((int)ReadRow(row, 16)), checked((int)ReadRow(row, 20)));

    /// <summary>Gets the directly-declared AssociatedConstDef range.</summary>
    public (int First, int Count) GetAssociatedConstRange(int row)
        => (checked((int)ReadRow(row, 32)), checked((int)ReadRow(row, 36)));

    /// <summary>Gets the directly-declared InterfaceConstDef range.</summary>
    public (int First, int Count) GetInterfaceConstRange(int row)
        => (checked((int)ReadRow(row, 40)), checked((int)ReadRow(row, 44)));

    /// <summary>Gets the InterfaceDef row that owns an AssociatedTypeDef row.</summary>
    public int GetAssociatedTypeOwner(int row)
    {
        if (row <= 0 || row >= _associatedTypeOwners.Length)
        {
            throw InvalidInterfaceDef();
        }

        return _associatedTypeOwners[row];
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidInterfaceDef();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint ReadRow(int row, int offset)
        => Read(GetRow(row), offset);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    private static bool ValidRange(uint first, uint count, int tableCount)
        => count == 0 ? first == 0
            : first != 0 && first <= (uint)tableCount
                && count <= (uint)tableCount - first + 1;

    private static InvalidDataException InvalidInterfaceDef()
        => new("Invalid TRRM InterfaceDef table.");
}

/// <summary>Encodes InterfaceDef rows and validates their owners and declaration ranges.</summary>
public static class TrRmInterfaceDefTableWriter
{
    /// <summary>Encodes InterfaceDef rows in one-based table order.</summary>
    /// <param name="rows">The interface descriptors.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <param name="formals">The validated GenericParam table.</param>
    /// <param name="interfaceBaseCount">The number of InterfaceBase rows.</param>
    /// <param name="interfaceMethodCount">The number of InterfaceMethod rows.</param>
    /// <param name="associatedTypeCount">The number of AssociatedTypeDef rows.</param>
    /// <param name="associatedConstCount">The number of AssociatedConstDef rows.</param>
    /// <param name="interfaceConstCount">The number of InterfaceConstDef rows.</param>
    /// <returns>The encoded InterfaceDef section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmInterfaceDefRow> rows,
        TrRmTypeDefTableReader types, TrRmGenericParamTableReader formals,
        int interfaceBaseCount, int interfaceMethodCount, int associatedTypeCount,
        int associatedConstCount, int interfaceConstCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 48];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 48;
            Write(result, offset, row.OwnerTypeDefRow);
            Write(result, offset + 4, row.SelfGenericParamRow);
            Write(result, offset + 8, row.BaseFirst);
            Write(result, offset + 12, row.BaseCount);
            Write(result, offset + 16, row.MethodFirst);
            Write(result, offset + 20, row.MethodCount);
            Write(result, offset + 24, row.AssociatedTypeFirst);
            Write(result, offset + 28, row.AssociatedTypeCount);
            Write(result, offset + 32, row.AssociatedConstFirst);
            Write(result, offset + 36, row.AssociatedConstCount);
            Write(result, offset + 40, row.InterfaceConstFirst);
            Write(result, offset + 44, row.InterfaceConstCount);
        }

        _ = new TrRmInterfaceDefTableReader(result, rows.Count, types, formals,
            interfaceBaseCount, interfaceMethodCount, associatedTypeCount,
            associatedConstCount, interfaceConstCount);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset), unchecked((uint)value));
}
