// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmConstantExecutionArgumentRow(
    byte Kind, uint SourceRef);

internal readonly record struct TrRmConstantExecutionDependencyRow(
    byte Kind, uint TargetRef);

internal readonly record struct TrRmLogicalInputViewRow(
    byte BackingKind, uint BackingRef, uint ReferentTypeSpecRef,
    int PathFirst, int PathCount);

internal readonly record struct TrRmLogicalViewPathEntryRow(
    byte SelectorKind, uint SelectorRef, ulong IndexOrRangeStart,
    ulong RangeCount);

internal static class TrRmConstantExecutionTableValidation
{
    internal static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    internal static bool IsRowReference(uint row, int count)
        => row is > 0 and <= 0xFF_FFFF && row <= (uint)count;

    internal static bool IsValidRange(uint first, uint count, int total)
        => count == 0 ? first == 0
            : first != 0 && first <= (uint)total
                && count <= (uint)total - first + 1;

    internal static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    internal static ushort ReadU16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));

    internal static ulong ReadU64(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, sizeof(ulong)));

    internal static void Write(byte[] data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

    internal static InvalidDataException InvalidTable(string name)
        => new($"Invalid TRRM {name} table.");
}

internal readonly ref struct TrRmConstantExecutionArgumentTableReader
{
    private const int RowSize = 8;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmConstantExecutionArgumentTableReader(ReadOnlySpan<byte> section,
        int recordCount, int frozenValueNodeCount, int logicalInputViewCount,
        int constantExecutionCount)
    {
        if (!TrRmConstantExecutionTableValidation.ValidCount(recordCount)
            || !TrRmConstantExecutionTableValidation.ValidCount(frozenValueNodeCount)
            || !TrRmConstantExecutionTableValidation.ValidCount(logicalInputViewCount)
            || !TrRmConstantExecutionTableValidation.ValidCount(constantExecutionCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecutionArgument");
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var kind = data[0];
            var source = TrRmConstantExecutionTableValidation.Read(data, 4);
            var targetCount = kind switch
            {
                1 => frozenValueNodeCount,
                2 => logicalInputViewCount,
                3 => 0,
                4 => constantExecutionCount,
                _ => -1,
            };
            if (data[1] != 0
                || TrRmConstantExecutionTableValidation.ReadU16(data, 2) != 0
                || (kind == 3
                    ? source != 0
                    : targetCount <= 0
                        || !TrRmConstantExecutionTableValidation.IsRowReference(
                            source, targetCount)))
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "ConstantExecutionArgument");
            }
        }
    }

    internal byte GetKind(int row) => GetRow(row)[0];
    internal uint GetSourceRef(int row)
        => TrRmConstantExecutionTableValidation.Read(GetRow(row), 4);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecutionArgument");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal readonly ref struct TrRmConstantExecutionDependencyTableReader
{
    private const int RowSize = 8;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmConstantExecutionDependencyTableReader(ReadOnlySpan<byte> section,
        int recordCount, int methodDefCount, int constraintCount,
        int interfaceWitnessCount, int witnessProjectionCount,
        int intrinsicRegistrationCount, int immutableDataCount,
        int fieldDefCount, int propertyDefCount, int unionCaseDefCount)
    {
        var targetCounts = new[] {
            methodDefCount, constraintCount, interfaceWitnessCount,
            witnessProjectionCount, intrinsicRegistrationCount,
            immutableDataCount, fieldDefCount, propertyDefCount, unionCaseDefCount,
        };
        var countsValid = true;
        foreach (var targetCount in targetCounts)
        {
            countsValid &= TrRmConstantExecutionTableValidation.ValidCount(
                targetCount);
        }
        if (!TrRmConstantExecutionTableValidation.ValidCount(recordCount)
            || !countsValid
            || (long)recordCount * RowSize != section.Length)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecutionDependency");
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var kind = data[0];
            var target = TrRmConstantExecutionTableValidation.Read(data, 4);
            var targetCount = kind is >= 1 and <= 9
                ? targetCounts[kind - 1] : -1;
            if (data[1] != 0
                || TrRmConstantExecutionTableValidation.ReadU16(data, 2) != 0
                || targetCount <= 0
                || !TrRmConstantExecutionTableValidation.IsRowReference(
                    target, targetCount))
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "ConstantExecutionDependency");
            }
        }
    }

    internal byte GetKind(int row) => GetRow(row)[0];
    internal uint GetTargetRef(int row)
        => TrRmConstantExecutionTableValidation.Read(GetRow(row), 4);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "ConstantExecutionDependency");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal readonly ref struct TrRmLogicalViewPathEntryTableReader
{
    private const int RowSize = 24;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmLogicalViewPathEntryTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmFieldDefTableReader fields,
        TrRmUnionCaseFieldTableReader unionCaseFields,
        TrRmImportTableReader imports)
    {
        if (!TrRmConstantExecutionTableValidation.ValidCount(recordCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "LogicalViewPathEntry");
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var kind = data[0];
            var selector = TrRmConstantExecutionTableValidation.Read(data, 4);
            var start = TrRmConstantExecutionTableValidation.ReadU64(data, 8);
            var count = TrRmConstantExecutionTableValidation.ReadU64(data, 16);
            var validSelector = kind switch
            {
                1 => count == 0 && start == 0,
                2 => TrRmConstantExecutionTableValidation.IsRowReference(
                        selector, unionCaseFields.Count)
                    && count == 0 && start == 0,
                3 or 4 => selector == 0 && count == 0,
                5 => selector == 0 && count != 0,
                _ => false,
            };
            if (data[1] != 0
                || TrRmConstantExecutionTableValidation.ReadU16(data, 2) != 0
                || !validSelector)
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "LogicalViewPathEntry");
            }

            if (kind == 1)
            {
                TrRmFieldReferenceValidator.Validate(selector, fields, imports);
            }
        }
    }

    internal byte GetSelectorKind(int row) => GetRow(row)[0];
    internal uint GetSelectorRef(int row)
        => TrRmConstantExecutionTableValidation.Read(GetRow(row), 4);
    internal ulong GetIndexOrRangeStart(int row)
        => TrRmConstantExecutionTableValidation.ReadU64(GetRow(row), 8);
    internal ulong GetRangeCount(int row)
        => TrRmConstantExecutionTableValidation.ReadU64(GetRow(row), 16);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "LogicalViewPathEntry");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal readonly ref struct TrRmLogicalInputViewTableReader
{
    private const int RowSize = 20;

    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmLogicalInputViewTableReader(ReadOnlySpan<byte> section,
        int recordCount, int frozenValueNodeCount, int immutableDataCount,
        int constantExecutionCount, TrRmTypeSpecTableReader typeSpecs,
        TrRmLogicalViewPathEntryTableReader pathEntries)
    {
        if (!TrRmConstantExecutionTableValidation.ValidCount(recordCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "LogicalInputView");
        }

        _rows = section;
        _recordCount = recordCount;
        var pathRowsOwned = new byte[pathEntries.Count + 1];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var backingKind = data[0];
            var backing = TrRmConstantExecutionTableValidation.Read(data, 4);
            var referentType = TrRmConstantExecutionTableValidation.Read(data, 8);
            var pathFirst = TrRmConstantExecutionTableValidation.Read(data, 12);
            var pathCount = TrRmConstantExecutionTableValidation.Read(data, 16);
            var backingCount = backingKind switch
            {
                1 => frozenValueNodeCount,
                2 => immutableDataCount,
                3 => constantExecutionCount,
                _ => -1,
            };
            if (data[1] != 0
                || TrRmConstantExecutionTableValidation.ReadU16(data, 2) != 0
                || backingCount <= 0
                || !TrRmConstantExecutionTableValidation.IsRowReference(
                    backing, backingCount)
                || referentType == 0
                || !TrRmConstantExecutionTableValidation.IsValidRange(
                    pathFirst, pathCount, pathEntries.Count))
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "LogicalInputView");
            }

            _ = typeSpecs.GetTypeSpecRef(referentType);
            var hasTerminalRange = false;
            for (var offset = 0; offset < pathCount; offset++)
            {
                var pathRow = checked((int)(pathFirst + (uint)offset));
                if (pathRowsOwned[pathRow] != 0 || hasTerminalRange)
                {
                    throw TrRmConstantExecutionTableValidation.InvalidTable(
                        "LogicalInputView");
                }

                pathRowsOwned[pathRow] = 1;
                hasTerminalRange = pathEntries.GetSelectorKind(pathRow) == 5;
                if (hasTerminalRange && offset + 1 != pathCount)
                {
                    throw TrRmConstantExecutionTableValidation.InvalidTable(
                        "LogicalInputView");
                }
            }
        }

        for (var row = 1; row <= pathEntries.Count; row++)
        {
            if (pathRowsOwned[row] == 0)
            {
                throw TrRmConstantExecutionTableValidation.InvalidTable(
                    "LogicalInputView");
            }
        }
    }

    internal byte GetBackingKind(int row) => GetRow(row)[0];
    internal uint GetBackingRef(int row)
        => TrRmConstantExecutionTableValidation.Read(GetRow(row), 4);
    internal uint GetReferentTypeSpecRef(int row)
        => TrRmConstantExecutionTableValidation.Read(GetRow(row), 8);
    internal (int First, int Count) GetPathRange(int row)
    {
        var data = GetRow(row);
        return (
            checked((int)TrRmConstantExecutionTableValidation.Read(data, 12)),
            checked((int)TrRmConstantExecutionTableValidation.Read(data, 16)));
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmConstantExecutionTableValidation.InvalidTable(
                "LogicalInputView");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal static class TrRmConstantExecutionArgumentTableWriter
{
    private const int RowSize = 8;

    internal static byte[] Encode(
        IReadOnlyList<TrRmConstantExecutionArgumentRow> rows,
        int frozenValueNodeCount, int logicalInputViewCount,
        int constantExecutionCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            result[index * RowSize] = rows[index].Kind;
            TrRmConstantExecutionTableValidation.Write(
                result, (index * RowSize) + 4, rows[index].SourceRef);
        }

        _ = new TrRmConstantExecutionArgumentTableReader(result, rows.Count,
            frozenValueNodeCount, logicalInputViewCount, constantExecutionCount);
        return result;
    }
}

internal static class TrRmConstantExecutionDependencyTableWriter
{
    private const int RowSize = 8;

    internal static byte[] Encode(
        IReadOnlyList<TrRmConstantExecutionDependencyRow> rows,
        int methodDefCount, int constraintCount, int interfaceWitnessCount,
        int witnessProjectionCount, int intrinsicRegistrationCount,
        int immutableDataCount, int fieldDefCount, int propertyDefCount,
        int unionCaseDefCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            result[index * RowSize] = rows[index].Kind;
            TrRmConstantExecutionTableValidation.Write(
                result, (index * RowSize) + 4, rows[index].TargetRef);
        }

        _ = new TrRmConstantExecutionDependencyTableReader(result, rows.Count,
            methodDefCount, constraintCount, interfaceWitnessCount,
            witnessProjectionCount, intrinsicRegistrationCount,
            immutableDataCount, fieldDefCount, propertyDefCount,
            unionCaseDefCount);
        return result;
    }
}

internal static class TrRmLogicalViewPathEntryTableWriter
{
    private const int RowSize = 24;

    internal static byte[] Encode(
        IReadOnlyList<TrRmLogicalViewPathEntryRow> rows,
        TrRmFieldDefTableReader fields,
        TrRmUnionCaseFieldTableReader unionCaseFields,
        TrRmImportTableReader imports)
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
            result[offset] = row.SelectorKind;
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 4, row.SelectorRef);
            BinaryPrimitives.WriteUInt64LittleEndian(
                result.AsSpan(offset + 8), row.IndexOrRangeStart);
            BinaryPrimitives.WriteUInt64LittleEndian(
                result.AsSpan(offset + 16), row.RangeCount);
        }

        _ = new TrRmLogicalViewPathEntryTableReader(result, rows.Count,
            fields, unionCaseFields, imports);
        return result;
    }
}

internal static class TrRmLogicalInputViewTableWriter
{
    private const int RowSize = 20;

    internal static byte[] Encode(IReadOnlyList<TrRmLogicalInputViewRow> rows,
        int frozenValueNodeCount, int immutableDataCount,
        int constantExecutionCount, TrRmTypeSpecTableReader typeSpecs,
        TrRmLogicalViewPathEntryTableReader pathEntries)
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
            result[offset] = row.BackingKind;
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 4, row.BackingRef);
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 8, row.ReferentTypeSpecRef);
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 12, unchecked((uint)row.PathFirst));
            TrRmConstantExecutionTableValidation.Write(
                result, offset + 16, unchecked((uint)row.PathCount));
        }

        _ = new TrRmLogicalInputViewTableReader(result, rows.Count,
            frozenValueNodeCount, immutableDataCount, constantExecutionCount,
            typeSpecs, pathEntries);
        return result;
    }
}
