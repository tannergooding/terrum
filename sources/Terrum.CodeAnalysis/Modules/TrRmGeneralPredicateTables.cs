// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmPredicateNodeRow(
    uint NodeKind, uint ResultTypeSpecRef, uint OperandFirstRow, uint OperandCount);

internal readonly record struct TrRmPredicateNodeOperandRow(
    byte OperandKind, uint OperandRef);

internal readonly record struct TrRmPredicateReadRootRow(
    byte RootKind, uint RootIdentity);

internal readonly record struct TrRmPredicateReadDependencyRow(
    byte DependencyKind, uint DependencyRef);

internal readonly record struct TrRmPredicateOperationRow(
    ushort OperationCode, byte Policy);

internal readonly record struct TrRmPredicateTypeUseRow(uint TypeSpecRef);

internal readonly record struct TrRmPredicateControlRow(
    ushort ControlKind, uint Value);

internal readonly record struct TrRmEvaluationReadBoundRow(
    int OwnerMethodDefRow, int ReadFootprintRow);

internal readonly record struct TrRmPredicateCallBindingRow(
    byte BindingKind, byte SourceUse, uint FormalOrdinal, uint ChildOrdinal);

internal readonly record struct TrRmPredicateTableRowCounts(
    int ReadRootCount, int FrozenValueNodeCount, int CaptureCount,
    int OperationCount, int CallCount, int TypeUseCount, int FieldPathCount,
    int PropertyCount, int UnionCaseCount, int ControlCount,
    int TypePatternCount);

internal static class TrRmPredicateTableValidation
{
    internal static bool ValidCount(int count)
        => count is >= 0 and <= 0xFF_FFFF;

    internal static bool IsRowReference(uint row, int count)
        => row is > 0 and <= 0xFF_FFFF && row <= (uint)count;

    internal static uint Read(ReadOnlySpan<byte> data, int offset,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    internal static ushort ReadU16(ReadOnlySpan<byte> data, int offset,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork(sizeof(ushort) + 1);
        return BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(offset, sizeof(ushort)));
    }

    internal static byte ReadByte(ReadOnlySpan<byte> data, int offset,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork(sizeof(byte) + 1);
        return data[offset];
    }

    internal static void Write(byte[] data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

    internal static void WriteU16(byte[] data, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);

    internal static void ValidateOwnedRange(
        uint first, uint count, int targetCount, byte[] ownership, string table,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork();
        if (count == 0)
        {
            if (first != 0)
            {
                throw Invalid(table);
            }

            return;
        }

        var last = (ulong)first + count - 1;
        if (first is 0 or > 0xFF_FFFF || last > (uint)targetCount)
        {
            throw Invalid(table);
        }

        for (var index = checked((int)first - 1); index <= (int)last - 1; index++)
        {
            resourceLedger?.ConsumeWork(2);
            var byteIndex = index >> 3;
            var mask = (byte)(1 << (index & 7));
            if ((ownership[byteIndex] & mask) != 0)
            {
                throw Invalid(table);
            }

            ownership[byteIndex] |= mask;
        }
    }

    internal static void ValidateAllOwned(byte[] ownership, int count,
        string table, TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        for (var row = 0; row < count; row++)
        {
            resourceLedger?.ConsumeWork();
            if ((ownership[row >> 3] & (1 << (row & 7))) == 0)
            {
                throw Invalid(table);
            }
        }
    }

    internal static InvalidDataException Invalid(string table)
        => new($"Invalid TRRM {table} table.");
}

internal readonly ref struct TrRmPredicateNodeTableReader
{
    private const int RowSize = 16;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    internal int Count
    {
        get
        {
            _resourceLedger?.ConsumeWork();
            return _recordCount;
        }
    }

    internal TrRmPredicateNodeTableReader(ReadOnlySpan<byte> section,
        int recordCount, int operandCount, TrRmTypeSpecTableReader typeSpecs,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (!TrRmPredicateTableValidation.ValidCount(recordCount)
            || !TrRmPredicateTableValidation.ValidCount(operandCount)
            || (long)recordCount * RowSize != section.Length
            || schema.IsDevelopmentProfile()
            || schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.CopyOriginDraft
                or TrRmArtifactSchema.ForeignInvocationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                or TrRmArtifactSchema.ScalarFrozenValuesDraft
                or TrRmArtifactSchema.NominalEnumsDraft
                or TrRmArtifactSchema.RootPinRuntimeDraft
                or TrRmArtifactSchema.GeneralPredicateGraphsDraft
                or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateNode");
        }

        _rows = section;
        _recordCount = recordCount;
        resourceLedger?.ConsumeWork(checked((ulong)operandCount + 1UL));
        var ownership = new byte[(operandCount + 7) / 8];
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var nodeKind = TrRmPredicateTableValidation.Read(data, 0,
                _resourceLedger);
            var resultType = TrRmPredicateTableValidation.Read(data, 4,
                _resourceLedger);
            var operandFirst = TrRmPredicateTableValidation.Read(data, 8,
                _resourceLedger);
            var operandCountForNode = TrRmPredicateTableValidation.Read(data, 12,
                _resourceLedger);
            _resourceLedger?.ConsumeWork();
            if (!IsNodeKind(nodeKind, schema) || resultType == 0)
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateNode");
            }

            _ = typeSpecs.GetTypeSpecRef(resultType, allowInitQualifier: true);
            TrRmPredicateTableValidation.ValidateOwnedRange(
                operandFirst, operandCountForNode, operandCount,
                ownership, "PredicateNode", _resourceLedger);
        }
        TrRmPredicateTableValidation.ValidateAllOwned(
            ownership, operandCount, "PredicateNode", _resourceLedger);
    }

    internal uint GetNodeKind(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 0, _resourceLedger);

    internal uint GetResultTypeSpecRef(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 4, _resourceLedger);

    internal uint GetOperandFirstRow(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 8, _resourceLedger);

    internal uint GetOperandCount(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 12, _resourceLedger);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateNode");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool IsNodeKind(uint nodeKind, TrRmArtifactSchema schema)
    {
        var isSelectorKind = nodeKind is >= 1 and <= 6;
        var isGeneralKind = nodeKind is (>= 0x10 and <= 0x36)
            or (>= 0x40 and <= 0x4F);
        return isSelectorKind || (schema.UsesGeneralPredicateGraphs() && isGeneralKind);
    }
}

internal static class TrRmPredicateNodeTableWriter
{
    private const int RowSize = 16;

    internal static byte[] Encode(IReadOnlyList<TrRmPredicateNodeRow> rows,
        int operandCount, TrRmTypeSpecTableReader typeSpecs,
        TrRmArtifactSchema schema)
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
            TrRmPredicateTableValidation.Write(result, offset, row.NodeKind);
            TrRmPredicateTableValidation.Write(result, offset + 4, row.ResultTypeSpecRef);
            TrRmPredicateTableValidation.Write(result, offset + 8, row.OperandFirstRow);
            TrRmPredicateTableValidation.Write(result, offset + 12, row.OperandCount);
        }

        _ = new TrRmPredicateNodeTableReader(
            result, rows.Count, operandCount, typeSpecs, schema);
        return result;
    }
}

internal readonly ref struct TrRmPredicateNodeOperandTableReader
{
    private const int RowSize = 8;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    internal int Count
    {
        get
        {
            _resourceLedger?.ConsumeWork();
            return _recordCount;
        }
    }

    internal TrRmPredicateNodeOperandTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmPredicateTableRowCounts counts,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (!TrRmPredicateTableValidation.ValidCount(recordCount)
            || (long)recordCount * RowSize != section.Length
            || schema.IsDevelopmentProfile()
            || schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.CopyOriginDraft
                or TrRmArtifactSchema.ForeignInvocationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                or TrRmArtifactSchema.ScalarFrozenValuesDraft
                or TrRmArtifactSchema.NominalEnumsDraft
                or TrRmArtifactSchema.RootPinRuntimeDraft
                or TrRmArtifactSchema.GeneralPredicateGraphsDraft
                or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateNodeOperand");
        }

        _rows = section;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var operandKind = TrRmPredicateTableValidation.ReadByte(data, 0,
                _resourceLedger);
            var target = TrRmPredicateTableValidation.Read(data, 4,
                _resourceLedger);
            var targetCount = GetTargetCount(operandKind, counts, schema);
            _resourceLedger?.ConsumeWork();
            if (TrRmPredicateTableValidation.ReadByte(data, 1,
                    _resourceLedger) != 0
                || TrRmPredicateTableValidation.ReadU16(data, 2,
                    _resourceLedger) != 0
                || !TrRmPredicateTableValidation.IsRowReference(target, targetCount))
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateNodeOperand");
            }
        }
    }

    internal byte GetOperandKind(int row)
        => TrRmPredicateTableValidation.ReadByte(GetRow(row), 0,
            _resourceLedger);

    internal uint GetOperandRef(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 4, _resourceLedger);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateNodeOperand");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static int GetTargetCount(byte kind,
        TrRmPredicateTableRowCounts counts, TrRmArtifactSchema schema)
        => kind switch
        {
            1 => counts.ReadRootCount,
            2 => counts.FrozenValueNodeCount,
            3 => counts.CaptureCount,
            4 when schema.UsesGeneralPredicateGraphs() => counts.OperationCount,
            5 when schema.UsesGeneralPredicateGraphs() => counts.CallCount,
            6 when schema.UsesGeneralPredicateGraphs() => counts.TypeUseCount,
            7 when schema.UsesGeneralPredicateGraphs() => counts.FieldPathCount,
            8 when schema.UsesGeneralPredicateGraphs() => counts.PropertyCount,
            9 when schema.UsesGeneralPredicateGraphs() => counts.UnionCaseCount,
            10 when schema.UsesGeneralPredicateGraphs() => counts.ControlCount,
            11 when schema.UsesGeneralPredicateGraphs() => counts.TypePatternCount,
            _ => throw TrRmPredicateTableValidation.Invalid("PredicateNodeOperand"),
        };
}

internal static class TrRmPredicateNodeOperandTableWriter
{
    private const int RowSize = 8;

    internal static byte[] Encode(IReadOnlyList<TrRmPredicateNodeOperandRow> rows,
        TrRmPredicateTableRowCounts counts, TrRmArtifactSchema schema)
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
            result[offset] = row.OperandKind;
            TrRmPredicateTableValidation.Write(result, offset + 4, row.OperandRef);
        }

        _ = new TrRmPredicateNodeOperandTableReader(
            result, rows.Count, counts, schema);
        return result;
    }
}

internal readonly ref struct TrRmPredicateReadRootTableReader
{
    private const int RowSize = 8;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    internal int Count
    {
        get
        {
            _resourceLedger?.ConsumeWork();
            return _recordCount;
        }
    }

    internal TrRmPredicateReadRootTableReader(ReadOnlySpan<byte> section,
        int recordCount, int immutableDataCount, int captureCount,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (!TrRmPredicateTableValidation.ValidCount(recordCount)
            || !TrRmPredicateTableValidation.ValidCount(immutableDataCount)
            || !TrRmPredicateTableValidation.ValidCount(captureCount)
            || (long)recordCount * RowSize != section.Length
            || schema.IsDevelopmentProfile()
            || schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.CopyOriginDraft
                or TrRmArtifactSchema.ForeignInvocationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft
                or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                or TrRmArtifactSchema.ScalarFrozenValuesDraft
                or TrRmArtifactSchema.NominalEnumsDraft
                or TrRmArtifactSchema.RootPinRuntimeDraft
                or TrRmArtifactSchema.GeneralPredicateGraphsDraft
                or TrRmArtifactSchema.RegisterExceptionRegionsDraft))
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateReadRoot");
        }

        _rows = section;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var kind = TrRmPredicateTableValidation.ReadByte(data, 0,
                _resourceLedger);
            var identity = TrRmPredicateTableValidation.Read(data, 4,
                _resourceLedger);
            _resourceLedger?.ConsumeWork();
            var validIdentity = kind switch
            {
                1 or 5 or 7 => identity == 0,
                6 => TrRmPredicateTableValidation.IsRowReference(
                    identity, immutableDataCount),
                8 when schema.UsesGeneralPredicateGraphs()
                    => TrRmPredicateTableValidation.IsRowReference(identity, captureCount),
                >= 2 and <= 4 => true,
                _ => false,
            };
            if (!validIdentity
                || TrRmPredicateTableValidation.ReadByte(data, 1,
                    _resourceLedger) != 0
                || TrRmPredicateTableValidation.ReadU16(data, 2,
                    _resourceLedger) != 0)
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateReadRoot");
            }
        }
    }

    internal byte GetRootKind(int row)
        => TrRmPredicateTableValidation.ReadByte(GetRow(row), 0,
            _resourceLedger);

    internal uint GetRootIdentity(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 4, _resourceLedger);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateReadRoot");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal static class TrRmPredicateReadRootTableWriter
{
    private const int RowSize = 8;

    internal static byte[] Encode(IReadOnlyList<TrRmPredicateReadRootRow> rows,
        int immutableDataCount, int captureCount, TrRmArtifactSchema schema)
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
            result[offset] = row.RootKind;
            TrRmPredicateTableValidation.Write(result, offset + 4, row.RootIdentity);
        }

        _ = new TrRmPredicateReadRootTableReader(
            result, rows.Count, immutableDataCount, captureCount, schema);
        return result;
    }
}

internal readonly ref struct TrRmPredicateReadDependencyTableReader
{
    private const int RowSize = 8;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    internal int Count
    {
        get
        {
            _resourceLedger?.ConsumeWork();
            return _recordCount;
        }
    }

    internal TrRmPredicateReadDependencyTableReader(ReadOnlySpan<byte> section,
        int recordCount, int methodDefCount, int constraintCount,
        int witnessCount, int witnessProjectionCount,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (!TrRmPredicateTableValidation.ValidCount(recordCount)
            || !TrRmPredicateTableValidation.ValidCount(methodDefCount)
            || !TrRmPredicateTableValidation.ValidCount(constraintCount)
            || !TrRmPredicateTableValidation.ValidCount(witnessCount)
            || !TrRmPredicateTableValidation.ValidCount(witnessProjectionCount)
            || (long)recordCount * RowSize != section.Length)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateReadDependency");
        }

        _rows = section;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var kind = TrRmPredicateTableValidation.ReadByte(data, 0,
                _resourceLedger);
            var targetCount = kind switch
            {
                1 => methodDefCount,
                2 => constraintCount,
                3 => witnessCount,
                4 => witnessProjectionCount,
                _ => throw TrRmPredicateTableValidation.Invalid("PredicateReadDependency"),
            };
            _resourceLedger?.ConsumeWork();
            if (TrRmPredicateTableValidation.ReadByte(data, 1,
                    _resourceLedger) != 0
                || TrRmPredicateTableValidation.ReadU16(data, 2,
                    _resourceLedger) != 0
                || !TrRmPredicateTableValidation.IsRowReference(
                    TrRmPredicateTableValidation.Read(data, 4,
                        _resourceLedger), targetCount))
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateReadDependency");
            }
        }
    }

    internal byte GetDependencyKind(int row)
        => TrRmPredicateTableValidation.ReadByte(GetRow(row), 0,
            _resourceLedger);

    internal uint GetDependencyRef(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 4, _resourceLedger);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateReadDependency");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal static class TrRmPredicateReadDependencyTableWriter
{
    private const int RowSize = 8;

    internal static byte[] Encode(
        IReadOnlyList<TrRmPredicateReadDependencyRow> rows,
        int methodDefCount, int constraintCount, int witnessCount,
        int witnessProjectionCount)
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
            result[offset] = row.DependencyKind;
            TrRmPredicateTableValidation.Write(result, offset + 4, row.DependencyRef);
        }

        _ = new TrRmPredicateReadDependencyTableReader(result, rows.Count,
            methodDefCount, constraintCount, witnessCount, witnessProjectionCount);
        return result;
    }
}

internal readonly ref struct TrRmPredicateOperationTableReader
{
    private const int RowSize = 4;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmPredicateOperationTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmArtifactSchema schema)
    {
        if (!TrRmPredicateTableValidation.ValidCount(recordCount)
            || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateOperation");
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var operation = TrRmPredicateTableValidation.ReadU16(data, 0);
            var policy = data[2];
            if (data[3] != 0 || !IsSupportedPolicy(operation, policy))
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateOperation");
            }
        }
    }

    internal ushort GetOperationCode(int row)
        => TrRmPredicateTableValidation.ReadU16(GetRow(row), 0);

    internal byte GetPolicy(int row) => GetRow(row)[2];

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateOperation");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }

    private static bool IsSupportedPolicy(ushort operation, byte policy)
        => operation switch
        {
            0x0001 or 0x0003 or (>= 0x0007 and <= 0x0014) or 0x0016
                => policy == 0,
            0x0002 or (>= 0x0004 and <= 0x0006) => policy <= 2,
            0x0015 => policy <= 3,
            _ => false,
        };
}

internal static class TrRmPredicateOperationTableWriter
{
    private const int RowSize = 4;

    internal static byte[] Encode(IReadOnlyList<TrRmPredicateOperationRow> rows,
        TrRmArtifactSchema schema)
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
            TrRmPredicateTableValidation.WriteU16(result, offset, row.OperationCode);
            result[offset + 2] = row.Policy;
        }

        _ = new TrRmPredicateOperationTableReader(result, rows.Count, schema);
        return result;
    }
}

internal readonly ref struct TrRmPredicateTypeUseTableReader
{
    private const int RowSize = 4;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmPredicateTypeUseTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmTypeSpecTableReader typeSpecs,
        TrRmArtifactSchema schema)
    {
        if (!TrRmPredicateTableValidation.ValidCount(recordCount)
            || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateTypeUse");
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var typeSpec = TrRmPredicateTableValidation.Read(GetRow(row), 0);
            if (typeSpec == 0)
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateTypeUse");
            }

            _ = typeSpecs.GetTypeSpecRef(typeSpec);
        }
    }

    internal uint GetTypeSpecRef(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 0);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateTypeUse");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal static class TrRmPredicateTypeUseTableWriter
{
    private const int RowSize = 4;

    internal static byte[] Encode(IReadOnlyList<TrRmPredicateTypeUseRow> rows,
        TrRmTypeSpecTableReader typeSpecs, TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * RowSize];
        for (var index = 0; index < rows.Count; index++)
        {
            TrRmPredicateTableValidation.Write(
                result, index * RowSize, rows[index].TypeSpecRef);
        }

        _ = new TrRmPredicateTypeUseTableReader(
            result, rows.Count, typeSpecs, schema);
        return result;
    }
}

internal readonly ref struct TrRmPredicateControlTableReader
{
    private const int RowSize = 8;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmPredicateControlTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmArtifactSchema schema)
    {
        if (!TrRmPredicateTableValidation.ValidCount(recordCount)
            || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateControl");
        }

        _rows = section;
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var kind = TrRmPredicateTableValidation.ReadU16(data, 0);
            var value = TrRmPredicateTableValidation.Read(data, 4);
            if (TrRmPredicateTableValidation.ReadU16(data, 2) != 0
                || kind is < 1 or > 3
                || (kind == 2 && value is < 1 or > 3))
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateControl");
            }
        }
    }

    internal ushort GetControlKind(int row)
        => TrRmPredicateTableValidation.ReadU16(GetRow(row), 0);

    internal uint GetValue(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 4);

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateControl");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal static class TrRmPredicateControlTableWriter
{
    private const int RowSize = 8;

    internal static byte[] Encode(IReadOnlyList<TrRmPredicateControlRow> rows,
        TrRmArtifactSchema schema)
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
            TrRmPredicateTableValidation.WriteU16(result, offset, row.ControlKind);
            TrRmPredicateTableValidation.Write(result, offset + 4, row.Value);
        }

        _ = new TrRmPredicateControlTableReader(result, rows.Count, schema);
        return result;
    }
}

internal readonly ref struct TrRmEvaluationReadBoundTableReader
{
    private const int RowSize = 12;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmEvaluationReadBoundTableReader(ReadOnlySpan<byte> section,
        int recordCount, int methodDefCount, int readFootprintCount,
        TrRmArtifactSchema schema)
    {
        if (!TrRmPredicateTableValidation.ValidCount(recordCount)
            || !TrRmPredicateTableValidation.ValidCount(methodDefCount)
            || !TrRmPredicateTableValidation.ValidCount(readFootprintCount)
            || (long)recordCount * RowSize != section.Length
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw TrRmPredicateTableValidation.Invalid("EvaluationReadBound");
        }

        _rows = section;
        _recordCount = recordCount;
        var owners = new HashSet<uint>();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var owner = TrRmPredicateTableValidation.Read(data, 0);
            var footprint = TrRmPredicateTableValidation.Read(data, 4);
            if (!TrRmPredicateTableValidation.IsRowReference(owner, methodDefCount)
                || !TrRmPredicateTableValidation.IsRowReference(
                    footprint, readFootprintCount)
                || TrRmPredicateTableValidation.Read(data, 8) != 0
                || !owners.Add(owner))
            {
                throw TrRmPredicateTableValidation.Invalid("EvaluationReadBound");
            }
        }
    }

    internal int GetOwnerMethodDefRow(int row)
        => checked((int)TrRmPredicateTableValidation.Read(GetRow(row), 0));

    internal int GetReadFootprintRow(int row)
        => checked((int)TrRmPredicateTableValidation.Read(GetRow(row), 4));

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmPredicateTableValidation.Invalid("EvaluationReadBound");
        }

        return _rows.Slice((row - 1) * RowSize, RowSize);
    }
}

internal static class TrRmEvaluationReadBoundTableWriter
{
    private const int RowSize = 12;

    internal static byte[] Encode(
        IReadOnlyList<TrRmEvaluationReadBoundRow> rows, int methodDefCount,
        int readFootprintCount, TrRmArtifactSchema schema)
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
            TrRmPredicateTableValidation.Write(
                result, offset, unchecked((uint)row.OwnerMethodDefRow));
            TrRmPredicateTableValidation.Write(
                result, offset + 4, unchecked((uint)row.ReadFootprintRow));
        }

        _ = new TrRmEvaluationReadBoundTableReader(result, rows.Count,
            methodDefCount, readFootprintCount, schema);
        return result;
    }
}

internal readonly ref struct TrRmPredicateCallTableReader
{
    private const int PrefixSize = 24;
    private const int GenericArgumentSize = 8;
    private const int BindingSize = 12;
    private readonly TrRmVariableTableIndex _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmPredicateCallTableReader(ReadOnlySpan<byte> section,
        int recordCount, int readDependencyCount, int methodDefCount,
        int constantSourceCount,
        TrRmPredicateReadDependencyTableReader dependencies,
        TrRmTypeSpecTableReader typeSpecs, TrRmArtifactSchema schema)
    {
        if (!TrRmPredicateTableValidation.ValidCount(recordCount)
            || !TrRmPredicateTableValidation.ValidCount(readDependencyCount)
            || !TrRmPredicateTableValidation.ValidCount(methodDefCount)
            || !TrRmPredicateTableValidation.ValidCount(constantSourceCount)
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateCall");
        }
        if (recordCount == 0)
        {
            if (!section.IsEmpty)
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateCall");
            }

            _rows = default;
            _recordCount = 0;
            return;
        }

        _rows = new TrRmVariableTableIndex(section, recordCount, PrefixSize, 1);
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var targetRole = data[0];
            var callKind = data[1];
            var dependencyRef = TrRmPredicateTableValidation.Read(data, 4);
            var requirementMethod = TrRmPredicateTableValidation.Read(data, 8);
            var ownerType = TrRmPredicateTableValidation.Read(data, 12);
            var genericArgumentCount = TrRmPredicateTableValidation.Read(data, 16);
            var bindingCount = TrRmPredicateTableValidation.Read(data, 20);
            var expectedLength = (ulong)PrefixSize
                + ((ulong)genericArgumentCount * GenericArgumentSize)
                + ((ulong)bindingCount * BindingSize);
            if (targetRole is not (1 or 2) || callKind is not (1 or 2)
                || TrRmPredicateTableValidation.ReadU16(data, 2) != 0
                || !TrRmPredicateTableValidation.IsRowReference(
                    dependencyRef, readDependencyCount)
                || expectedLength != (ulong)data.Length
                || (ownerType != 0 && !IsCompleteTypeReference(ownerType, typeSpecs))
                || (targetRole == 1 && requirementMethod != 0)
                || (targetRole == 2
                    && (!TrRmPredicateTableValidation.IsRowReference(
                        requirementMethod, methodDefCount)
                        || ownerType == 0))
                || (callKind == 2 && targetRole != 1))
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateCall");
            }
            if ((targetRole == 1 && dependencies.GetDependencyKind(
                    checked((int)dependencyRef)) != 1)
                || (targetRole == 2 && dependencies.GetDependencyKind(
                    checked((int)dependencyRef)) is not (2 or 3 or 4)))
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateCall");
            }

            var genericCount = checked((int)genericArgumentCount);
            var bindingStart = checked(PrefixSize
                + (genericCount * GenericArgumentSize));
            for (var argument = 0; argument < genericCount; argument++)
            {
                var offset = PrefixSize + (argument * GenericArgumentSize);
                var kind = data[offset];
                var reference = TrRmPredicateTableValidation.Read(data, offset + 4);
                if (kind == 1)
                {
                    if (reference == 0)
                    {
                        throw TrRmPredicateTableValidation.Invalid("PredicateCall");
                    }

                    _ = typeSpecs.GetTypeSpecRef(reference);
                }
                else if (kind != 2
                    || !TrRmPredicateTableValidation.IsRowReference(
                        reference, constantSourceCount))
                {
                    throw TrRmPredicateTableValidation.Invalid("PredicateCall");
                }
                if (data[offset + 1] != 0
                    || TrRmPredicateTableValidation.ReadU16(data, offset + 2) != 0)
                {
                    throw TrRmPredicateTableValidation.Invalid("PredicateCall");
                }
            }

            var bindingCountInt = checked((int)bindingCount);
            for (var binding = 0; binding < bindingCountInt; binding++)
            {
                var offset = bindingStart + (binding * BindingSize);
                var kind = data[offset];
                var sourceUse = data[offset + 1];
                var formalOrdinal = TrRmPredicateTableValidation.Read(data, offset + 4);
                var childOrdinal = TrRmPredicateTableValidation.Read(data, offset + 8);
                var validShape = kind switch
                {
                    1 => formalOrdinal == 0,
                    2 => true,
                    3 => sourceUse == 5 && childOrdinal == 0,
                    4 => callKind == 2 && sourceUse == 5
                        && formalOrdinal == 0 && childOrdinal == 0,
                    _ => false,
                };
                if (!validShape || sourceUse is < 1 or > 5
                    || TrRmPredicateTableValidation.ReadU16(data, offset + 2) != 0)
                {
                    throw TrRmPredicateTableValidation.Invalid("PredicateCall");
                }
            }
        }
    }

    internal byte GetTargetRole(int row) => GetRow(row)[0];
    internal byte GetCallKind(int row) => GetRow(row)[1];
    internal uint GetDependencyRef(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 4);
    internal uint GetRequirementMethodDefRef(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 8);
    internal uint GetOwnerTypeSpecRef(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 12);
    internal uint GetGenericArgumentCount(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 16);
    internal uint GetBindingCount(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 20);

    internal byte GetGenericArgumentKind(int row, int ordinal)
    {
        var data = GetGenericArgument(row, ordinal);
        return data[0];
    }

    internal uint GetGenericArgumentRef(int row, int ordinal)
        => TrRmPredicateTableValidation.Read(GetGenericArgument(row, ordinal), 4);

    internal TrRmPredicateCallBindingRow GetBinding(int row, int ordinal)
    {
        var data = GetBindingRow(row, ordinal);
        return new TrRmPredicateCallBindingRow(data[0], data[1],
            TrRmPredicateTableValidation.Read(data, 4),
            TrRmPredicateTableValidation.Read(data, 8));
    }

    private ReadOnlySpan<byte> GetGenericArgument(int row, int ordinal)
    {
        var data = GetRow(row);
        var count = checked((int)TrRmPredicateTableValidation.Read(data, 16));
        if ((uint)ordinal >= (uint)count)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateCall");
        }

        return data.Slice(PrefixSize + (ordinal * GenericArgumentSize),
            GenericArgumentSize);
    }

    private ReadOnlySpan<byte> GetBindingRow(int row, int ordinal)
    {
        var data = GetRow(row);
        var genericCount = checked((int)TrRmPredicateTableValidation.Read(data, 16));
        var count = checked((int)TrRmPredicateTableValidation.Read(data, 20));
        if ((uint)ordinal >= (uint)count)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateCall");
        }

        var start = PrefixSize + (genericCount * GenericArgumentSize)
            + (ordinal * BindingSize);
        return data.Slice(start, BindingSize);
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateCall");
        }

        return _rows.GetRow(row);
    }

    private static bool IsCompleteTypeReference(uint reference,
        TrRmTypeSpecTableReader typeSpecs)
    {
        try
        {
            _ = typeSpecs.GetTypeSpecRef(reference);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}

internal static class TrRmPredicateCallTableWriter
{
    internal static byte[] Encode(IReadOnlyList<ReadOnlyMemory<byte>> rows,
        int readDependencyCount, int methodDefCount, int constantSourceCount,
        TrRmPredicateReadDependencyTableReader dependencies,
        TrRmTypeSpecTableReader typeSpecs, TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var result = TrRmVariableTableWriter.EncodeBlobs(rows);
        _ = new TrRmPredicateCallTableReader(result, rows.Count,
            readDependencyCount, methodDefCount, constantSourceCount,
            dependencies, typeSpecs, schema);
        return result;
    }
}

internal readonly ref struct TrRmPredicateTypePatternTableReader
{
    private const int PrefixSize = 12;
    private readonly TrRmVariableTableIndex _rows;
    private readonly int _recordCount;

    internal int Count => _recordCount;

    internal TrRmPredicateTypePatternTableReader(ReadOnlySpan<byte> section,
        int recordCount, int captureCount, int constantSourceCount,
        int typeUseCount, TrRmArtifactSchema schema)
    {
        if (!TrRmPredicateTableValidation.ValidCount(recordCount)
            || !TrRmPredicateTableValidation.ValidCount(captureCount)
            || !TrRmPredicateTableValidation.ValidCount(constantSourceCount)
            || !TrRmPredicateTableValidation.ValidCount(typeUseCount)
            || !schema.UsesGeneralPredicateGraphs())
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateTypePattern");
        }
        if (recordCount == 0)
        {
            if (!section.IsEmpty)
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateTypePattern");
            }

            _rows = default;
            _recordCount = 0;
            return;
        }

        _rows = new TrRmVariableTableIndex(section, recordCount, PrefixSize, 1);
        _recordCount = recordCount;
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var kind = data[0];
            var typeFlags = data[1];
            var target = TrRmPredicateTableValidation.Read(data, 4);
            var argumentCount = TrRmPredicateTableValidation.Read(data, 8);
            var expectedLength = (ulong)PrefixSize + ((ulong)argumentCount * sizeof(uint));
            if (expectedLength != (ulong)data.Length
                || TrRmPredicateTableValidation.ReadU16(data, 2) != 0
                || !IsValidTarget(kind, typeFlags, target, argumentCount,
                    captureCount, constantSourceCount, typeUseCount))
            {
                throw TrRmPredicateTableValidation.Invalid("PredicateTypePattern");
            }

            for (var ordinal = 0; ordinal < checked((int)argumentCount); ordinal++)
            {
                var child = TrRmPredicateTableValidation.Read(
                    data, PrefixSize + (ordinal * sizeof(uint)));
                if (!TrRmPredicateTableValidation.IsRowReference(child, recordCount))
                {
                    throw TrRmPredicateTableValidation.Invalid("PredicateTypePattern");
                }
            }
        }
        ValidateAcyclic();
    }

    internal byte GetKind(int row) => GetRow(row)[0];
    internal byte GetTypeFlags(int row) => GetRow(row)[1];
    internal uint GetTargetRef(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 4);
    internal uint GetArgumentCount(int row)
        => TrRmPredicateTableValidation.Read(GetRow(row), 8);

    internal uint GetArgumentRef(int row, int ordinal)
    {
        var data = GetRow(row);
        var count = checked((int)TrRmPredicateTableValidation.Read(data, 8));
        if ((uint)ordinal >= (uint)count)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateTypePattern");
        }

        return TrRmPredicateTableValidation.Read(
            data, PrefixSize + (ordinal * sizeof(uint)));
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw TrRmPredicateTableValidation.Invalid("PredicateTypePattern");
        }

        return _rows.GetRow(row);
    }

    private void ValidateAcyclic()
    {
        var state = new byte[_recordCount + 1];
        var path = new int[_recordCount];
        var nextArgument = new int[_recordCount];
        for (var start = 1; start <= _recordCount; start++)
        {
            if (state[start] != 0)
            {
                continue;
            }

            var depth = 0;
            path[depth] = start;
            state[start] = 1;
            while (depth >= 0)
            {
                var row = path[depth];
                var data = GetRow(row);
                var argumentCount = checked((int)TrRmPredicateTableValidation.Read(data, 8));
                if (nextArgument[depth] == argumentCount)
                {
                    state[row] = 2;
                    nextArgument[depth] = 0;
                    depth--;
                    continue;
                }

                var child = checked((int)TrRmPredicateTableValidation.Read(
                    data, PrefixSize + (nextArgument[depth] * sizeof(uint))));
                nextArgument[depth]++;
                if (state[child] == 1)
                {
                    throw TrRmPredicateTableValidation.Invalid("PredicateTypePattern");
                }
                if (state[child] == 0)
                {
                    if (depth + 1 == path.Length)
                    {
                        throw TrRmPredicateTableValidation.Invalid("PredicateTypePattern");
                    }

                    depth++;
                    path[depth] = child;
                    state[child] = 1;
                }
            }
        }
    }

    private static bool IsValidTarget(byte kind, byte typeFlags, uint target,
        uint argumentCount, int captureCount, int constantSourceCount,
        int typeUseCount)
        => kind switch
        {
            1 => typeFlags == 0 && argumentCount == 0
                && TrRmPredicateTableValidation.IsRowReference(target, typeUseCount),
            2 or 3 or 4 => typeFlags == 0 && target == 0 && argumentCount == 0,
            5 or 6 => typeFlags == 0 && argumentCount == 0
                && TrRmPredicateTableValidation.IsRowReference(target, captureCount),
            7 => (typeFlags & ~3) == 0
                && TrRmPredicateTableValidation.IsRowReference(target, typeUseCount),
            8 => typeFlags == 0 && argumentCount == 0
                && TrRmPredicateTableValidation.IsRowReference(target, constantSourceCount),
            9 or 10 => typeFlags == 0 && target == 0 && argumentCount == 1,
            _ => false,
        };
}

internal static class TrRmPredicateTypePatternTableWriter
{
    internal static byte[] Encode(IReadOnlyList<ReadOnlyMemory<byte>> rows,
        int captureCount, int constantSourceCount, int typeUseCount,
        TrRmArtifactSchema schema)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var result = TrRmVariableTableWriter.EncodeBlobs(rows);
        _ = new TrRmPredicateTypePatternTableReader(result, rows.Count,
            captureCount, constantSourceCount, typeUseCount, schema);
        return result;
    }
}
