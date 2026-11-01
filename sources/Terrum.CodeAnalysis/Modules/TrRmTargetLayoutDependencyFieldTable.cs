// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmFieldReferenceValidator
{
    public static void Validate(uint reference, TrRmFieldDefTableReader fields,
        TrRmImportTableReader imports)
    {
        var row = (int)(reference & 0xFF_FFFF);
        if (row <= 0)
        {
            throw InvalidFieldReference();
        }

        switch (reference >> 24)
        {
            case 0x14 when row <= fields.Count:
                return;

            case 0x1C when row <= imports.Count:
                if ((imports.GetExpectedDefinitionForImportRow(row) >> 24) == 0x14)
                {
                    return;
                }
                break;
        }

        throw InvalidFieldReference();
    }

    private static InvalidDataException InvalidFieldReference()
        => new("Invalid TRRM FieldReference.");
}

/// <summary>Validates TargetLayoutDependencyField references in their assigned namespace.</summary>
public readonly ref struct TrRmTargetLayoutDependencyFieldTableReader
{
    private const int RowSize = 4;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Gets the number of validated field-reference rows.</summary>
    public int Count => _recordCount;

    /// <summary>Validates FieldDef DefIds and Import tokens expecting FieldDef.</summary>
    /// <param name="section">The borrowed four-byte FieldReference rows.</param>
    /// <param name="recordCount">The number of FieldReference rows.</param>
    /// <param name="fields">The validated FieldDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    public TrRmTargetLayoutDependencyFieldTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmFieldDefTableReader fields, TrRmImportTableReader imports)
        : this(section, recordCount, fields, imports, null)
    {
    }

    internal TrRmTargetLayoutDependencyFieldTableReader(
        ReadOnlySpan<byte> section, int recordCount,
        TrRmFieldDefTableReader fields, TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidFieldReferenceTable();
        }

        _rows = section;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            TrRmFieldReferenceValidator.Validate(ReadRow(row), fields, imports);
        }
    }

    /// <summary>Gets the FieldDef DefId or FieldDef Import token.</summary>
    public uint GetFieldReference(int row)
        => ReadRow(row);

    private uint ReadRow(int row)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidFieldReferenceTable();
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(
            _rows.Slice((row - 1) * RowSize, RowSize));
    }

    private static InvalidDataException InvalidFieldReferenceTable()
        => new("Invalid TRRM TargetLayoutDependencyField table.");
}

/// <summary>Encodes TargetLayoutDependencyField references in one-based table order.</summary>
public static class TrRmTargetLayoutDependencyFieldTableWriter
{
    /// <summary>Encodes FieldDef DefIds or FieldDef Import tokens.</summary>
    /// <param name="rows">The ordered FieldReference words.</param>
    /// <param name="fields">The validated FieldDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <returns>The encoded TargetLayoutDependencyField section.</returns>
    public static byte[] Encode(IReadOnlyList<uint> rows,
        TrRmFieldDefTableReader fields, TrRmImportTableReader imports)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * sizeof(uint)];
        for (var index = 0; index < rows.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(index * sizeof(uint)), rows[index]);
        }

        _ = new TrRmTargetLayoutDependencyFieldTableReader(
            result, rows.Count, fields, imports);
        return result;
    }
}
