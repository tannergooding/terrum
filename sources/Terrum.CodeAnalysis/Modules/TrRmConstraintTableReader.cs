// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Validates Constraint row framing, ownership, flags and assigned payload forms.</summary>
/// <remarks>Associated-relation payload semantics and generic execution remain unsupported. Borrowed sections must remain unchanged.</remarks>
public readonly ref struct TrRmConstraintTableReader
{
    private const int RowSize = 12;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated Constraint rows.</summary>
    public int Count { get; }

    /// <summary>Validates Constraint row framing, ownership, flags and assigned payload forms.</summary>
    /// <param name="section">The borrowed Constraint section bytes.</param>
    /// <param name="recordCount">The number of 12-byte Constraint rows.</param>
    /// <param name="formals">The validated GenericParam table.</param>
    /// <param name="types">The validated TypeSpec table for type-valued payloads.</param>
    public TrRmConstraintTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmGenericParamTableReader formals, TrRmTypeSpecTableReader types)
        : this(section, recordCount, formals, types, null)
    {
    }

    internal TrRmConstraintTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmGenericParamTableReader formals,
        TrRmTypeSpecTableReader types,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(3);
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != formals.ConstraintCount
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidConstraint();
        }

        _rows = section;
        Count = recordCount;
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var data = section.Slice((row - 1) * RowSize, RowSize);
            var owner = Read(data, 0);
            _resourceLedger?.ConsumeWork(sizeof(ushort) + 1);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            _resourceLedger?.ConsumeWork(sizeof(ushort) + 1);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            var payload = Read(data, 8);
            _resourceLedger?.ConsumeWork(5);
            if (owner != formals.GetConstraintOwner(row)
                || flags != 0 || payload == 0
                || (kind == 2 && payload > 3)
                || (kind is not (1 or 2 or 3)))
            {
                throw InvalidConstraint();
            }

            if (kind == 1)
            {
                _ = types.GetTypeSpecRef(payload);
            }
        }
    }

    /// <summary>Gets the one-based GenericParam owner row.</summary>
    public int GetOwnerGenericParam(int row)
        => checked((int)ReadRow(row, 0));

    /// <summary>Gets the assigned Constraint kind.</summary>
    public ushort GetKind(int row)
    {
        CheckRow(row);
        _resourceLedger?.ConsumeWork(sizeof(ushort) + 1);
        return BinaryPrimitives.ReadUInt16LittleEndian(
            _rows.Slice(((row - 1) * RowSize) + 4, sizeof(ushort)));
    }

    /// <summary>Gets the kind-specific payload word.</summary>
    public uint GetPayload(int row)
        => ReadRow(row, 8);

    private uint ReadRow(int row, int offset)
    {
        CheckRow(row);
        return Read(_rows.Slice((row - 1) * RowSize, RowSize), offset);
    }

    private void CheckRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > Count)
        {
            throw InvalidConstraint();
        }
    }

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidConstraint()
        => new("Invalid TRRM Constraint table.");
}
