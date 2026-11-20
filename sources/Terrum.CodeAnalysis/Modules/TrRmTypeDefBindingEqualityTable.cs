// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Retains one consumer-owned equality edge between TypeDef views.</summary>
/// <param name="OwnerModule">The Module that owns the equality obligation.</param>
/// <param name="LeftTypeDef">The first artifact-wide TypeDef row.</param>
/// <param name="RightTypeDef">The second artifact-wide TypeDef row.</param>
public readonly record struct TrRmTypeDefBindingEqualityRow(
    uint OwnerModule, uint LeftTypeDef, uint RightTypeDef);

/// <summary>Validates retained TypeDef binding-equality references.</summary>
public readonly ref struct TrRmTypeDefBindingEqualityTableReader
{
    private const int RowSize = 12;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated TypeDef binding-equality rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates Module and struct TypeDef row references.</summary>
    /// <param name="section">The borrowed 12-byte equality rows.</param>
    /// <param name="recordCount">The number of equality rows.</param>
    /// <param name="modules">The validated Module table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    public TrRmTypeDefBindingEqualityTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types)
        : this(section, recordCount, modules, types,
            allowClassTypeDefs: false, resourceLedger: null)
    {
    }

    internal TrRmTypeDefBindingEqualityTableReader(
        ReadOnlySpan<byte> section, int recordCount,
        TrRmModuleTableReader modules, TrRmTypeDefTableReader types,
        TrRmA3LayoutResourceLedger? resourceLedger)
        : this(section, recordCount, modules, types,
            allowClassTypeDefs: false, resourceLedger: resourceLedger)
    {
    }

    internal TrRmTypeDefBindingEqualityTableReader(
        ReadOnlySpan<byte> section, int recordCount,
        TrRmModuleTableReader modules, TrRmTypeDefTableReader types,
        bool allowClassTypeDefs,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (recordCount is <= 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidEquality();
        }

        _rows = section;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            resourceLedger?.ConsumeWork();
            var data = GetRow(row);
            var ownerModule = Read(data, 0);
            var leftTypeDef = Read(data, 4);
            var rightTypeDef = Read(data, 8);
            if (ownerModule is 0 or > 0xFF_FFFF
                || ownerModule > modules.Count
                || !IsSupportedTypeDef(leftTypeDef, types,
                    allowClassTypeDefs)
                || !IsSupportedTypeDef(rightTypeDef, types,
                    allowClassTypeDefs))
            {
                throw InvalidEquality();
            }
        }
    }

    /// <summary>Gets the owning Module row.</summary>
    public uint GetOwnerModule(int row)
        => Read(GetRow(row), 0);

    /// <summary>Gets the first endpoint TypeDef row.</summary>
    public uint GetLeftTypeDef(int row)
        => Read(GetRow(row), 4);

    /// <summary>Gets the second endpoint TypeDef row.</summary>
    public uint GetRightTypeDef(int row)
        => Read(GetRow(row), 8);

    private static bool IsSupportedTypeDef(uint reference,
        TrRmTypeDefTableReader types, bool allowClassTypeDefs)
    {
        if (reference is 0 or > 0x00FF_FFFF
            || reference > (uint)types.Count)
        {
            return false;
        }

        var kind = types.GetKind(checked((int)reference));
        return kind == 1 || allowClassTypeDefs && kind == 2;
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidEquality();
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidEquality()
        => new("Invalid TRRM TypeDefBindingEquality table.");
}

/// <summary>Encodes consumer-owned equality edges between TypeDef views.</summary>
public static class TrRmTypeDefBindingEqualityTableWriter
{
    private const int RowSize = 12;

    /// <summary>Encodes TypeDef binding-equality rows in one-based table order.</summary>
    /// <param name="rows">The consumer-owned equality edges.</param>
    /// <param name="modules">The validated Module table.</param>
    /// <param name="types">The validated TypeDef table.</param>
    /// <returns>The encoded section payload.</returns>
    public static byte[] Encode(
        IReadOnlyList<TrRmTypeDefBindingEqualityRow> rows,
        TrRmModuleTableReader modules, TrRmTypeDefTableReader types)
        => Encode(rows, modules, types, allowClassTypeDefs: false);

    internal static byte[] Encode(
        IReadOnlyList<TrRmTypeDefBindingEqualityRow> rows,
        TrRmModuleTableReader modules, TrRmTypeDefTableReader types,
        bool allowClassTypeDefs)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[checked(rows.Count * RowSize)];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * RowSize;
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(offset), row.OwnerModule);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(offset + sizeof(uint)), row.LeftTypeDef);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(offset + (2 * sizeof(uint))), row.RightTypeDef);
        }

        _ = new TrRmTypeDefBindingEqualityTableReader(result, rows.Count,
            modules, types, allowClassTypeDefs,
            resourceLedger: null);
        return result;
    }
}
