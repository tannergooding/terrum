// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>A directly-declared instance method of one interface.</summary>
/// <param name="OwnerInterfaceDefRow">The one-based owning InterfaceDef row.</param>
/// <param name="Ordinal">The zero-based declaration ordinal.</param>
/// <param name="MethodDefRow">The one-based directly-declared MethodDef row.</param>
public readonly record struct TrRmInterfaceMethodRow(
    int OwnerInterfaceDefRow, int Ordinal, int MethodDefRow);

/// <summary>Validates InterfaceMethod ranges and directly-declared instance methods.</summary>
public readonly ref struct TrRmInterfaceMethodTableReader
{
    private const int RowSize = 12;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int[] _methodOwners;
    private readonly int[] _rowOwners;
    private readonly int _recordCount;

    /// <summary>Gets the number of validated InterfaceMethod rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates every InterfaceMethod row against its InterfaceDef and MethodDef.</summary>
    /// <param name="section">The borrowed 12-byte InterfaceMethod rows.</param>
    /// <param name="recordCount">The number of InterfaceMethod rows.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    public TrRmInterfaceMethodTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmInterfaceDefTableReader interfaces, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != interfaces.InterfaceMethodCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidInterfaceMethod();
        }

        _rows = section;
        _recordCount = recordCount;
        _methodOwners = new int[methods.Count + 1];
        _rowOwners = new int[recordCount + 1];
        for (var interfaceDef = 1; interfaceDef <= interfaces.Count; interfaceDef++)
        {
            var (first, count) = interfaces.GetMethodRange(interfaceDef);
            var ownerTypeDef = interfaces.GetOwnerTypeDef(interfaceDef);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var row = first + ordinal;
                if (_rowOwners[row] != 0)
                {
                    throw InvalidInterfaceMethod();
                }

                _rowOwners[row] = interfaceDef;
                var data = GetRow(row);
                var owner = Read(data, 0);
                var rowOrdinal = Read(data, 4);
                var methodDef = Read(data, 8);
                if (owner != (uint)interfaceDef || rowOrdinal != (uint)ordinal
                    || methodDef is 0 or > 0xFF_FFFF || methodDef > methods.Count
                    || methods.GetOwnerType((int)methodDef) != ownerTypeDef
                    || signatures.GetReceiverTypeSpec(methods.GetSignature((int)methodDef)) == 0
                    || _methodOwners[methodDef] != 0)
                {
                    throw InvalidInterfaceMethod();
                }

                _methodOwners[methodDef] = interfaceDef;
            }
        }

        for (var row = 1; row <= recordCount; row++)
        {
            if (_rowOwners[row] == 0)
            {
                throw InvalidInterfaceMethod();
            }
        }
    }

    /// <summary>Gets the one-based owning InterfaceDef row.</summary>
    public int GetOwnerInterfaceDef(int row)
        => checked((int)ReadRow(row, 0));

    /// <summary>Gets the zero-based declaration ordinal.</summary>
    public int GetOrdinal(int row)
        => checked((int)ReadRow(row, 4));

    /// <summary>Gets the one-based directly-declared MethodDef row.</summary>
    public int GetMethodDefRow(int row)
        => checked((int)ReadRow(row, 8));

    internal int GetOwnerInterfaceDefForMethod(int methodDef)
    {
        if (methodDef <= 0 || methodDef >= _methodOwners.Length)
        {
            throw InvalidInterfaceMethod();
        }

        return _methodOwners[methodDef];
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidInterfaceMethod();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint ReadRow(int row, int offset)
        => Read(GetRow(row), offset);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidInterfaceMethod()
        => new("Invalid TRRM InterfaceMethod table.");
}

/// <summary>Encodes InterfaceMethod rows and validates ownership and instance signatures.</summary>
public static class TrRmInterfaceMethodTableWriter
{
    /// <summary>Encodes InterfaceMethod rows in one-based table order.</summary>
    /// <param name="rows">The directly-declared interface methods.</param>
    /// <param name="interfaces">The validated InterfaceDef table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <returns>The encoded InterfaceMethod section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmInterfaceMethodRow> rows,
        TrRmInterfaceDefTableReader interfaces, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures)
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
            Write(result, offset + 8, row.MethodDefRow);
        }

        _ = new TrRmInterfaceMethodTableReader(result, rows.Count,
            interfaces, methods, signatures);
        return result;
    }

    private const int RowSize = 12;

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset), unchecked((uint)value));
}
