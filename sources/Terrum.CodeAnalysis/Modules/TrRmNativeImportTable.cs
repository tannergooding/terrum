// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmNativeImportTypeContract
{
    internal static bool TryGetScalarType(TrRmTypeSpecTableReader types, int typeSpecRow,
        out PrimitiveType scalarType)
    {
        var type = types.GetRow(typeSpecRow);
        scalarType = type.Length == 3 && type[0] == 1 && type[1] == 0
            ? FromAtomicCode(type[2]) : PrimitiveType.Void;
        return scalarType != PrimitiveType.Void;
    }

    internal static bool TryGetWritableOutputPointee(TrRmTypeSpecTableReader types,
        int typeSpecRow, out PrimitiveType pointeeType)
    {
        var type = types.GetRow(typeSpecRow);
        pointeeType = type.Length == 4 && type[0] == 1 && type[1] == 1 && type[2] == 0x8C
            ? FromAtomicCode(type[3]) : PrimitiveType.Void;
        if (pointeeType != PrimitiveType.Void)
        {
            return DirectNativeImportContract.IsOutputPointee(pointeeType);
        }

        if (type.Length == 7 && type[0] == 0x0C && type[1] == 1 && type[2] == 0x8C)
        {
            var innerRow = BinaryPrimitives.ReadUInt32LittleEndian(type[3..]);
            var inner = types.GetRow(checked((int)innerRow));
            if (inner.Length == 4 && inner[0] == 1 && inner[1] == 0
                && inner[2] == 0x8C && inner[3] == 0)
            {
                pointeeType = PrimitiveType.NativeRawPointer;
                return true;
            }
        }

        pointeeType = PrimitiveType.Void;
        return false;
    }

    internal static bool TryGetNativeParameterPointee(TrRmTypeSpecTableReader types,
        int typeSpecRow, out PrimitiveType pointeeType)
    {
        var type = types.GetRow(typeSpecRow);
        if (type.Length == 4 && type[0] == 1 && type[1] == 0 && type[2] == 0x8C)
        {
            if (type[3] == 0)
            {
                pointeeType = PrimitiveType.Void;
                return true;
            }

            pointeeType = FromAtomicCode(type[3]);
            return DirectNativeImportContract.IsScalar(pointeeType);
        }

        if (type.Length == 7 && type[0] == 0x0C && type[1] == 0 && type[2] == 0x8C)
        {
            var innerRow = BinaryPrimitives.ReadUInt32LittleEndian(type[3..]);
            var inner = types.GetRow(checked((int)innerRow));
            if (inner.Length == 4 && inner[0] == 1 && inner[1] == 0
                && inner[2] == 0x8C && inner[3] == 0)
            {
                pointeeType = PrimitiveType.NativeRawPointer;
                return true;
            }
        }

        pointeeType = PrimitiveType.Void;
        return false;
    }

    private static PrimitiveType FromAtomicCode(byte typeCode) => typeCode switch
    {
        1 => PrimitiveType.Bool,
        3 => PrimitiveType.Char16,
        8 => PrimitiveType.Int32,
        9 => PrimitiveType.Int64,
        0x0D => PrimitiveType.UInt32,
        0x0E => PrimitiveType.UInt64,
        0x14 => PrimitiveType.Float32,
        0x15 => PrimitiveType.Float64,
        _ => PrimitiveType.Void,
    };
}

public readonly record struct TrRmNativeImportRow(
    int MethodDefRow, int LibraryNameStringRow, int ExportNameStringRow, uint ResolutionPolicy);

/// <summary>Emits provisional TRRM Windows named native import rows.</summary>
public static class TrRmNativeImportTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmNativeImportRow> rows,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 16];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var offset = index * 16;
            Write(result, offset, row.MethodDefRow);
            Write(result, offset + 4, row.LibraryNameStringRow);
            Write(result, offset + 8, row.ExportNameStringRow);
            Write(result, offset + 12, row.ResolutionPolicy);
        }

        _ = new TrRmNativeImportTableReader(result, rows.Count, strings, methods,
            signatures, parameters, types, contracts);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}

/// <summary>Validates provisional TRRM Windows named native import declarations.</summary>
/// <remarks>The borrowed NativeImport section must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmNativeImportTableReader
{
    private const uint WindowsSystem32ResolutionPolicy = 1;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly int[] _rowByMethod;
    private readonly int _recordCount;

    public TrRmNativeImportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * 16 != section.Length
            || (recordCount == 0 && !section.IsEmpty))
        {
            throw InvalidNativeImport();
        }

        _rows = section;
        _strings = strings;
        _recordCount = recordCount;
        _rowByMethod = new int[methods.Count + 1];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var method = Read(data, 0);
            var library = Read(data, 4);
            var export = Read(data, 8);
            var policy = Read(data, 12);
            if (method is 0 or > 0xFF_FFFF || method > methods.Count
                || library is 0 or > 0xFF_FFFF || export is 0 or > 0xFF_FFFF
                || policy != WindowsSystem32ResolutionPolicy
                || _rowByMethod[method] != 0
                || methods.GetOwnerType((int)method) != 0
                || methods.GetBodyRow((int)method) != 0
                || signatures.GetReceiverTypeSpec(methods.GetSignature((int)method)) != 0
                || signatures.GetGenericCount(methods.GetSignature((int)method)) != 0
                || (contracts.GetEffectsMask((int)method) & 4) == 0)
            {
                throw InvalidNativeImport();
            }

            var libraryName = strings.DecodeName((int)library);
            var exportName = strings.DecodeName((int)export);
            if (!DirectNativeImportContract.IsValidLibraryName(libraryName)
                || !DirectNativeImportContract.IsValidExportName(exportName))
            {
                throw InvalidNativeImport();
            }

            _rowByMethod[method] = row;
            ValidateSignature((int)method, methods, signatures, parameters, types,
                contracts.GetNativeOutputs((int)method), contracts.GetNativeParameters((int)method));
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            var outputs = contracts.GetNativeOutputs(method);
            if (_rowByMethod[method] == 0)
            {
                if (!outputs.IsEmpty
                    || !contracts.GetNativeParameters(method).IsDefaultOrEmpty
                        && contracts.GetNativeComMethod(method) is null)
                {
                    throw InvalidNativeImport();
                }
            }
            else if (methods.GetBodyRow(method) != 0)
            {
                throw InvalidNativeImport();
            }
        }
    }

    public int Count => _recordCount;

    public bool HasImportForMethod(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _rowByMethod.Length
            && _rowByMethod[methodDefRow] != 0;

    public TrRmNativeImportRow GetImportForMethod(int methodDefRow)
    {
        if (!HasImportForMethod(methodDefRow))
        {
            throw InvalidNativeImport();
        }

        var data = GetRow(_rowByMethod[methodDefRow]);
        return new TrRmNativeImportRow(methodDefRow, (int)Read(data, 4),
            (int)Read(data, 8), Read(data, 12));
    }

    public string GetLibraryName(int methodDefRow)
        => _strings.DecodeName(GetImportForMethod(methodDefRow).LibraryNameStringRow);

    public string GetExportName(int methodDefRow)
        => _strings.DecodeName(GetImportForMethod(methodDefRow).ExportNameStringRow);

    private static void ValidateSignature(int method, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types,
        System.Collections.Immutable.ImmutableArray<TrRmNativeOutputContract> outputs,
        System.Collections.Immutable.ImmutableArray<TrRmNativeParameterContract> nativeParameters)
    {
        var signature = methods.GetSignature(method);
        var resultType = ScalarType(types, signatures.GetResultTypeSpec(signature));
        var (first, count) = signatures.GetParamRange(signature);
        var seenOutputs = new bool[count];
        var seenNativeParameters = new bool[count];
        foreach (var output in outputs)
        {
            if (output.ParameterOrdinal >= count || seenOutputs[output.ParameterOrdinal]
                || output.FailureDisposition != 1
                || !DirectNativeImportContract.IsValidResultCondition(
                    resultType, output.ResultCondition))
            {
                throw InvalidNativeImport();
            }

            seenOutputs[output.ParameterOrdinal] = true;
        }

        for (var parameter = 0; parameter < count; parameter++)
        {
            var typeRow = parameters.GetTypeSpec(first + parameter);
            if (TrRmNativeImportTypeContract.TryGetWritableOutputPointee(
                types, typeRow, out var pointeeType))
            {
                if (parameters.GetMode(first + parameter) != 0
                    || HasNativeParameter(nativeParameters, parameter)
                    || !seenOutputs[parameter])
                {
                    throw InvalidNativeImport();
                }

                var output = FindOutput(outputs, parameter);
                if (output.PointeeType != pointeeType)
                {
                    throw InvalidNativeImport();
                }
            }
            else if (TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
                    types, typeRow, out var nativePointeeType))
            {
                if (parameters.GetMode(first + parameter) != 0 || seenOutputs[parameter]
                    || !TryGetNativeParameter(nativeParameters, parameter, out var nativeParameter)
                    || !ValidNativeParameter(nativeParameter, nativePointeeType))
                {
                    throw InvalidNativeImport();
                }

                seenNativeParameters[parameter] = true;
            }
            else if (seenOutputs[parameter] || HasNativeParameter(nativeParameters, parameter)
                || !IsScalar(types, typeRow))
            {
                throw InvalidNativeImport();
            }
        }

        if (!nativeParameters.IsDefaultOrEmpty)
        {
            foreach (var nativeParameter in nativeParameters)
            {
                if (nativeParameter.ParameterOrdinal < 0
                    || nativeParameter.ParameterOrdinal >= count
                    || !seenNativeParameters[nativeParameter.ParameterOrdinal])
                {
                    throw InvalidNativeImport();
                }
            }
        }
    }

    private static bool HasNativeParameter(
        System.Collections.Immutable.ImmutableArray<TrRmNativeParameterContract> parameters, int ordinal)
        => TryGetNativeParameter(parameters, ordinal, out _);

    private static bool TryGetNativeParameter(
        System.Collections.Immutable.ImmutableArray<TrRmNativeParameterContract> parameters,
        int ordinal, out TrRmNativeParameterContract result)
    {
        if (parameters.IsDefaultOrEmpty)
        {
            result = default;
            return false;
        }

        foreach (var parameter in parameters)
        {
            if (parameter.ParameterOrdinal == ordinal)
            {
                result = parameter;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static bool ValidNativeParameter(TrRmNativeParameterContract parameter, PrimitiveType pointeeType)
    {
        var access = parameter.Access - 1;
        return parameter.Retention == (byte)NativeParameterRetention.NoEscape
            && (pointeeType == PrimitiveType.Void
                ? access == (byte)NativeParameterAccess.Read && parameter.Extent == 0
                : parameter.Extent is 0 or 1
                    && access is >= (byte)NativeParameterAccess.Read
                        and <= (byte)NativeParameterAccess.ReadWrite);
    }

    private static TrRmNativeOutputContract FindOutput(
        System.Collections.Immutable.ImmutableArray<TrRmNativeOutputContract> outputs, int parameter)
    {
        foreach (var output in outputs)
        {
            if (output.ParameterOrdinal == parameter)
            {
                return output;
            }
        }

        throw InvalidNativeImport();
    }

    private static bool IsScalar(TrRmTypeSpecTableReader types, int row)
        => TrRmNativeImportTypeContract.TryGetScalarType(types, row, out _);

    private static PrimitiveType ScalarType(TrRmTypeSpecTableReader types, int row)
        => TrRmNativeImportTypeContract.TryGetScalarType(types, row, out var scalarType)
            ? scalarType : PrimitiveType.Void;

    private ReadOnlySpan<byte> GetRow(int row)
    {
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidNativeImport();
        }

        return _rows.Slice((row - 1) * 16, 16);
    }

    private static uint Read(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidNativeImport()
        => new("Invalid TRRM NativeImport table.");
}
