// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmConstantTemplateActual(byte Kind, uint Reference);

internal sealed record TrRmConstantTemplateRow(
    uint SelectedMethodReference,
    uint ResultTypeSpecRef,
    uint TargetProfileRef,
    IReadOnlyList<TrRmConstantTemplateActual> Actuals);

/// <summary>Validates CG1 ConstantTemplate framing and local structural references.</summary>
/// <remarks>
/// Borrowed sections and referenced table inputs must remain unchanged while
/// this reader is in use. Imported helper contracts require provider binding.
/// </remarks>
internal readonly ref struct TrRmConstantTemplateTableReader
{
    private const int MinimumRowSize = 16;
    private const int ActualSize = 8;

    private readonly TrRmVariableTableIndex _rows;
    private readonly TrRmMethodDefTableReader _methods;
    private readonly TrRmImportTableReader _imports;
    private readonly TrRmMethodSigTableReader _signatures;
    private readonly TrRmGenericParamTableReader _genericParams;
    private readonly TrRmTypeSpecTableReader _typeSpecs;
    private readonly TrRmConstantSourceTableReader _constantSources;
    private readonly TrRmTargetProfileTableReader _targetProfiles;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    public int Count => _rows.Count;

    public TrRmConstantTemplateTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmMethodDefTableReader methods,
        TrRmImportTableReader imports, TrRmMethodSigTableReader signatures,
        TrRmGenericParamTableReader genericParams,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmConstantSourceTableReader constantSources,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (!schema.UsesGenericConstantMethods())
        {
            throw InvalidTemplate();
        }

        _resourceLedger = resourceLedger;
        _rows = new TrRmVariableTableIndex(section, recordCount,
            MinimumRowSize, rowAlignment: 1, resourceLedger);
        _methods = methods;
        _imports = imports;
        _signatures = signatures;
        _genericParams = genericParams;
        _typeSpecs = typeSpecs;
        _constantSources = constantSources;
        _targetProfiles = targetProfiles;

        for (var row = 1; row <= recordCount; row++)
        {
            ValidateRow(row);
        }
    }

    public uint GetSelectedMethodReference(int row)
        => Read(GetRow(row), 0);

    public uint GetResultTypeSpecRef(int row)
        => Read(GetRow(row), 4);

    public uint GetTargetProfileRef(int row)
        => Read(GetRow(row), 8);

    public int GetArgumentCount(int row)
        => checked((int)Read(GetRow(row), 12));

    public TrRmConstantTemplateActual GetActual(int row, int ordinal)
    {
        var data = GetRow(row);
        var count = checked((int)Read(data, 12));
        if (ordinal < 0 || ordinal >= count)
        {
            throw InvalidTemplate();
        }

        var offset = checked(MinimumRowSize + (ordinal * ActualSize));
        return new TrRmConstantTemplateActual(data[offset],
            Read(data, offset + 4));
    }

    private void ValidateRow(int row)
    {
        var data = GetRow(row);
        var selectedMethod = Read(data, 0);
        var resultType = Read(data, 4);
        var targetProfile = Read(data, 8);
        var argumentCount = Read(data, 12);
        _resourceLedger?.ConsumeWork(4);
        if (argumentCount > 0xFF_FFFF
            || targetProfile > 0xFF_FFFF
            || (ulong)MinimumRowSize + ((ulong)argumentCount * ActualSize)
                != (ulong)data.Length)
        {
            throw InvalidTemplate();
        }

        _ = _typeSpecs.GetTypeSpecRef(resultType);
        if (targetProfile != 0)
        {
            _ = _targetProfiles.GetProfile(checked((int)targetProfile));
        }

        var (methodRow, isImport) = ResolveMethod(selectedMethod);
        if (_methods.GetOwnerType(methodRow) != 0)
        {
            throw InvalidTemplate();
        }

        if ((_methods.GetBodyRow(methodRow) == 0) != isImport)
        {
            throw InvalidTemplate();
        }

        var signature = _methods.GetSignature(methodRow);
        var (_, parameterCount) = _signatures.GetParamRange(signature);
        if (_signatures.GetReceiverTypeSpec(signature) != 0
            || parameterCount != 0)
        {
            throw InvalidTemplate();
        }

        var formalRange = _signatures.GetGenericRange(signature);
        if (argumentCount != (uint)formalRange.Count)
        {
            throw InvalidTemplate();
        }

        for (var ordinal = 0; ordinal < argumentCount; ordinal++)
        {
            _resourceLedger?.ConsumeWork();
            var offset = checked(MinimumRowSize + (int)(ordinal * ActualSize));
            var kind = data[offset];
            var flags = data[offset + 1];
            var reserved = BinaryPrimitives.ReadUInt16LittleEndian(
                data.Slice(offset + 2, sizeof(ushort)));
            var reference = Read(data, offset + 4);
            _resourceLedger?.ConsumeWork(4);
            if (kind is not (1 or 2) || flags != 0 || reserved != 0)
            {
                throw InvalidTemplate();
            }

            if (kind == 1)
            {
                _ = _typeSpecs.GetTypeSpecRef(reference);
            }
            else
            {
                if (reference is 0 or > 0xFF_FFFF)
                {
                    throw InvalidTemplate();
                }

                var sourceRow = checked((int)reference);
                var sourceKind = _constantSources.GetKind(sourceRow);
                if (sourceKind == 4)
                {
                    var formal = checked((int)
                        _constantSources.GetPayloadReference(sourceRow));
                    if (_genericParams.GetFormalKind(formal) != 2)
                    {
                        throw InvalidTemplate();
                    }
                }
            }

            if (_genericParams.GetFormalKind(formalRange.Start + (int)ordinal) != kind)
            {
                throw InvalidTemplate();
            }
        }
    }

    private (int MethodRow, bool IsImport) ResolveMethod(uint reference)
    {
        _resourceLedger?.ConsumeWork(2);
        var kind = reference >> 24;
        var row = checked((int)(reference & 0x00FF_FFFF));
        if (row == 0)
        {
            throw InvalidTemplate();
        }

        if (kind == 0x16 && row <= _methods.Count)
        {
            return (row, false);
        }

        if (kind == 0x1C && row <= _imports.Count)
        {
            var expected = _imports.GetExpectedDefinitionForImportRow(row);
            var expectedRow = checked((int)(expected & 0x00FF_FFFF));
            if ((expected >> 24) == 0x16
                && expectedRow > 0 && expectedRow <= _methods.Count)
            {
                return (expectedRow, true);
            }
        }

        throw InvalidTemplate();
    }

    private ReadOnlySpan<byte> GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        return _rows.GetRow(row);
    }

    private uint Read(ReadOnlySpan<byte> data, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidTemplate()
        => new("Invalid TRRM ConstantTemplate table.");
}

internal static class TrRmConstantTemplateTableWriter
{
    private const int MinimumRowSize = 16;
    private const int ActualSize = 8;

    public static byte[] Encode(
        IReadOnlyList<TrRmConstantTemplateRow> rows,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!schema.UsesGenericConstantMethods())
        {
            throw new ArgumentOutOfRangeException(nameof(schema));
        }

        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var encodedRows = new ReadOnlyMemory<byte>[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            resourceLedger?.ConsumeWork();
            var row = rows[index];
            ArgumentNullException.ThrowIfNull(row.Actuals);
            if (row.Actuals.Count > 0xFF_FFFF)
            {
                throw new ArgumentOutOfRangeException(nameof(rows));
            }

            var rowLength = checked((long)MinimumRowSize
                + ((long)row.Actuals.Count * ActualSize));
            if (rowLength > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(rows));
            }

            resourceLedger?.ConsumeWork(checked((ulong)rowLength + 1UL));
            var bytes = new byte[(int)rowLength];
            Write(bytes, 0, row.SelectedMethodReference);
            Write(bytes, 4, row.ResultTypeSpecRef);
            Write(bytes, 8, row.TargetProfileRef);
            Write(bytes, 12, (uint)row.Actuals.Count);
            for (var ordinal = 0; ordinal < row.Actuals.Count; ordinal++)
            {
                resourceLedger?.ConsumeWork();
                var actual = row.Actuals[ordinal];
                var offset = MinimumRowSize + (ordinal * ActualSize);
                bytes[offset] = actual.Kind;
                Write(bytes, offset + 4, actual.Reference);
            }

            encodedRows[index] = bytes;
        }

        return resourceLedger is null
            ? TrRmVariableTableWriter.EncodeBlobs(encodedRows)
            : TrRmVariableTableWriter.EncodeBlobs(encodedRows, resourceLedger);
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
