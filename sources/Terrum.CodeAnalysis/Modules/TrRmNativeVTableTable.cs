// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmNativeVTableRow(
    int MethodDefRow, int ReceiverOrdinal, int VTableByteOffset, int Slot);

internal static class TrRmNativeVTableTableWriter
{
    internal static byte[] Encode(IReadOnlyList<TrRmNativeVTableRow> rows,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader types,
        TrRmContractTableReader contracts)
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
            var data = result.AsSpan(index * 16);
            BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)row.MethodDefRow);
            BinaryPrimitives.WriteUInt32LittleEndian(data[4..], (uint)row.ReceiverOrdinal);
            BinaryPrimitives.WriteUInt32LittleEndian(data[8..], (uint)row.VTableByteOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(data[12..], (uint)row.Slot);
        }

        _ = new TrRmNativeVTableTableReader(result, rows.Count, methods, signatures,
            parameters, types, contracts);
        return result;
    }
}

internal readonly ref struct TrRmNativeVTableTableReader
{
    private readonly ReadOnlySpan<byte> _rows;
    private readonly int[] _rowByMethod;
    private readonly bool[][] _nativeParametersByMethod;
    private readonly int _recordCount;

    internal TrRmNativeVTableTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader types,
        TrRmContractTableReader contracts)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * 16 != section.Length
            || (recordCount == 0 && !section.IsEmpty))
        {
            throw InvalidNativeVTable();
        }

        _rows = section;
        _recordCount = recordCount;
        _rowByMethod = new int[methods.Count + 1];
        _nativeParametersByMethod = new bool[methods.Count + 1][];
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var method = Read(data, 0);
            if (method is 0 or > 0xFF_FFFF || method > methods.Count
                || _rowByMethod[method] != 0
                || methods.GetOwnerType((int)method) != 0
                || methods.GetBodyRow((int)method) != 0
                || signatures.GetReceiverTypeSpec(methods.GetSignature((int)method)) != 0
                || signatures.GetGenericCount(methods.GetSignature((int)method)) != 0
                || !contracts.HasNativeAbi((int)method)
                || contracts.GetNativeAbi((int)method) != new TrRmNativeAbiContract(1, 4, 0)
                || (contracts.GetEffectsMask((int)method) & 4) == 0
                || (contracts.GetEffectsMask((int)method) & 2) != 0
                || Read(data, 4) != 0
                || Read(data, 8) % NativeTargetProfile.PointerSize != 0
                || Read(data, 8) > (uint)(int.MaxValue - NativeTargetProfile.PointerSize)
                || Read(data, 12) > int.MaxValue / NativeTargetProfile.PointerSize)
            {
                throw InvalidNativeVTable();
            }

            ValidateSignature((int)method, methods, signatures, parameters, types, contracts);
            var parameterCount = signatures.GetParamRange(
                methods.GetSignature((int)method)).Count;
            var nativeParameters = new bool[parameterCount];
            var contractsForParameters = contracts.GetNativeParameters((int)method);
            if (!contractsForParameters.IsDefaultOrEmpty)
            {
                foreach (var nativeParameter in contractsForParameters)
                {
                    nativeParameters[nativeParameter.ParameterOrdinal] = true;
                }
            }
            _nativeParametersByMethod[method] = nativeParameters;
            _rowByMethod[method] = row;
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            if (contracts.HasNativeAbi(method)
                && contracts.GetNativeAbi(method).Role == 4
                && _rowByMethod[method] == 0)
            {
                throw InvalidNativeVTable();
            }
        }
    }

    internal int Count => _recordCount;

    internal bool HasVTableForMethod(int methodDefRow)
        => _rowByMethod is not null && methodDefRow > 0 && methodDefRow < _rowByMethod.Length
            && _rowByMethod[methodDefRow] != 0;

    internal bool HasNativeParameter(int methodDefRow, int parameterOrdinal)
        => _nativeParametersByMethod is not null
            && methodDefRow > 0 && methodDefRow < _nativeParametersByMethod.Length
            && parameterOrdinal >= 0
            && _nativeParametersByMethod[methodDefRow] is { } parameters
            && parameterOrdinal < parameters.Length && parameters[parameterOrdinal];

    internal int GetSlot(int methodDefRow)
        => checked((int)Read(GetMethodRow(methodDefRow), 12));

    internal int GetVTableByteOffset(int methodDefRow)
        => checked((int)Read(GetMethodRow(methodDefRow), 8));

    private ReadOnlySpan<byte> GetMethodRow(int methodDefRow)
    {
        if (!HasVTableForMethod(methodDefRow))
        {
            throw InvalidNativeVTable();
        }

        return GetRow(_rowByMethod[methodDefRow]);
    }

    private ReadOnlySpan<byte> GetRow(int row)
        => _rows.Slice((row - 1) * 16, 16);

    private static void ValidateSignature(int method, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts)
    {
        var signature = methods.GetSignature(method);
        if (!IsNativeValueType(types, signatures.GetResultTypeSpec(signature), allowVoid: true))
        {
            throw InvalidNativeVTable();
        }

        var nativeOutputs = contracts.GetNativeOutputs(method);
        var nativeParameters = contracts.GetNativeParameters(method);
        var seenNativeOutputs = new bool[signatures.GetParamRange(signature).Count];
        var seenNativeParameters = new bool[signatures.GetParamRange(signature).Count];
        var (first, count) = signatures.GetParamRange(signature);
        if (count == 0 || !IsNativePointerType(types, parameters.GetTypeSpec(first)))
        {
            throw InvalidNativeVTable();
        }

        var resultType = PrimitiveType.Void;
        if (!nativeOutputs.IsDefaultOrEmpty)
        {
            resultType = TrRmCompactLinearBodyReader.ResolveType(
                types, signatures.GetResultTypeSpec(signature)) switch
            {
                8 => PrimitiveType.Int32,
                0x0D => PrimitiveType.UInt32,
                _ => PrimitiveType.Void,
            };
        }

        foreach (var output in nativeOutputs)
        {
            if (output.ParameterOrdinal <= 0 || output.ParameterOrdinal >= count
                || seenNativeOutputs[output.ParameterOrdinal]
                || FindNativeParameter(nativeParameters, output.ParameterOrdinal) is not null
                || output.FailureDisposition != 1
                || !DirectNativeImportContract.IsValidResultCondition(
                    resultType, output.ResultCondition))
            {
                throw InvalidNativeVTable();
            }
            seenNativeOutputs[output.ParameterOrdinal] = true;
        }

        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var typeSpec = parameters.GetTypeSpec(first + ordinal);
            if (parameters.GetMode(first + ordinal) != 0)
            {
                throw InvalidNativeVTable();
            }

            var nativeParameter = FindNativeParameter(nativeParameters, ordinal);
            var nativeOutput = FindNativeOutput(nativeOutputs, ordinal);
            if (nativeOutput is TrRmNativeOutputContract output)
            {
                if (nativeParameter is not null
                    || !TrRmNativeImportTypeContract.TryGetWritableOutputPointee(
                        types, typeSpec, out var pointeeType)
                    || pointeeType != output.PointeeType)
                {
                    throw InvalidNativeVTable();
                }
            }
            else if (nativeParameter is TrRmNativeParameterContract annotated)
            {
                if (ordinal == 0 || !IsSupportedNativeParameter(types, typeSpec, annotated))
                {
                    throw InvalidNativeVTable();
                }

                seenNativeParameters[ordinal] = true;
            }
            else if (!IsNativeValueType(types, typeSpec, allowVoid: false))
            {
                throw InvalidNativeVTable();
            }
        }
        if (!nativeParameters.IsDefaultOrEmpty)
        {
            foreach (var parameter in nativeParameters)
            {
                if (parameter.ParameterOrdinal < 0
                    || parameter.ParameterOrdinal >= seenNativeParameters.Length
                    || !seenNativeParameters[parameter.ParameterOrdinal])
                {
                    throw InvalidNativeVTable();
                }
            }
        }
    }

    private static TrRmNativeParameterContract? FindNativeParameter(
        System.Collections.Immutable.ImmutableArray<TrRmNativeParameterContract> parameters,
        int ordinal)
    {
        if (parameters.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var parameter in parameters)
        {
            if (parameter.ParameterOrdinal == ordinal)
            {
                return parameter;
            }
        }

        return null;
    }

    private static TrRmNativeOutputContract? FindNativeOutput(
        System.Collections.Immutable.ImmutableArray<TrRmNativeOutputContract> outputs,
        int ordinal)
    {
        if (outputs.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var output in outputs)
        {
            if (output.ParameterOrdinal == ordinal)
            {
                return output;
            }
        }

        return null;
    }

    private static bool IsSupportedNativeParameter(TrRmTypeSpecTableReader types,
        int typeSpec, TrRmNativeParameterContract parameter)
        => parameter.Access is >= 1 and <= 3
            && parameter.Retention == 1
            && parameter.ExtentKind is 0 or 1
            && parameter.PointerDepth == 1
            && parameter.RequiredAlignment == 0
            && parameter.Extent <= int.MaxValue
            && TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
                types, typeSpec, out _);

    private static bool IsNativeValueType(TrRmTypeSpecTableReader types,
        int typeSpec, bool allowVoid)
    {
        var row = types.GetRow(typeSpec);
        if (row.Length == 3 && row[0] == 1 && row[1] == 0)
        {
            return row[2] is 1 or 3 or 8 or 9 or 0x0D or 0x0E or 0x14 or 0x15
                || (allowVoid && row[2] == 0);
        }

        return IsNativePointerType(types, typeSpec)
            || (row.Length == 6 && row[0] == 0x0D && row[1] == 0);
    }

    private static bool IsNativePointerType(TrRmTypeSpecTableReader types, int typeSpec)
    {
        var row = types.GetRow(typeSpec);
        return (row.Length == 4 && row[0] == 1 && row[2] == 0x8C)
            || (row.Length == 7 && row[0] == 0x0C && row[2] == 0x8C);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidNativeVTable()
        => new("Invalid TRRM native vtable declaration.");
}
