// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmNativeFunctionPointerSignatureRow(int MethodDefRow);

internal static class TrRmNativeFunctionPointerSignatureTableWriter
{
    internal static byte[] Encode(IReadOnlyList<TrRmNativeFunctionPointerSignatureRow> rows,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader types,
        TrRmContractTableReader contracts)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 8];
        for (var index = 0; index < rows.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * 8),
                (uint)rows[index].MethodDefRow);
        }

        _ = new TrRmNativeFunctionPointerSignatureTableReader(result, rows.Count,
            methods, signatures, parameters, types, contracts);
        return result;
    }
}

internal readonly ref struct TrRmNativeFunctionPointerSignatureTableReader
{
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmMethodDefTableReader _methods;
    private readonly TrRmMethodSigTableReader _signatures;
    private readonly TrRmParamTableReader _parameters;
    private readonly TrRmTypeSpecTableReader _types;
    private readonly int[] _rowByMethod;
    private readonly int[] _methodBySignature;
    private readonly int[] _canonicalSignatureIdByRow;
    private readonly byte[] _canonicalSignatureStates;
    private readonly Dictionary<string, int> _canonicalSignatureIds;
    private readonly int _recordCount;

    internal TrRmNativeFunctionPointerSignatureTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * 8 != section.Length
            || (recordCount == 0 && !section.IsEmpty)
            || recordCount != types.NativeFunctionPointerSignatureCount)
        {
            throw InvalidNativeFunctionPointerSignature();
        }

        _rows = section;
        _methods = methods;
        _signatures = signatures;
        _parameters = parameters;
        _types = types;
        _recordCount = recordCount;
        _rowByMethod = new int[methods.Count + 1];
        _methodBySignature = new int[recordCount + 1];
        _canonicalSignatureIdByRow = new int[recordCount + 1];
        _canonicalSignatureStates = new byte[recordCount + 1];
        _canonicalSignatureIds = new Dictionary<string, int>(recordCount, StringComparer.Ordinal);
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var method = Read(data, 0);
            if (method is 0 or > 0xFF_FFFF || method > methods.Count
                || Read(data, 4) != 0 || _rowByMethod[method] != 0
                || methods.GetOwnerType((int)method) != 0
                || methods.GetFlags((int)method) != 0
                || methods.GetBodyRow((int)method) != 0
                || signatures.GetReceiverTypeSpec(methods.GetSignature((int)method)) != 0
                || signatures.GetGenericCount(methods.GetSignature((int)method)) != 0
                || !contracts.HasNativeAbi((int)method)
                || contracts.GetNativeAbi((int)method) != new TrRmNativeAbiContract(1, 3, 0)
                || contracts.GetEffectsMask((int)method) != 5
                || methods.GetContractRange((int)method).Count != 2)
            {
                throw InvalidNativeFunctionPointerSignature();
            }

            _rowByMethod[method] = row;
            _methodBySignature[row] = (int)method;
        }

        for (var row = 1; row <= recordCount; row++)
        {
            ValidateSignature(_methodBySignature[row]);
        }

        var uniqueSignatures = new HashSet<int>();
        for (var row = 1; row <= recordCount; row++)
        {
            if (!uniqueSignatures.Add(GetCanonicalSignatureId(row, 0)))
            {
                throw InvalidNativeFunctionPointerSignature();
            }
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            var isRole3Method = contracts.HasNativeAbi(method)
                && contracts.GetNativeAbi(method).Role == 3;
            var hasSignature = _rowByMethod[method] != 0;
            if (isRole3Method != hasSignature)
            {
                throw InvalidNativeFunctionPointerSignature();
            }
        }
    }

    internal int Count => _recordCount;

    internal bool HasSignatureForMethod(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _rowByMethod.Length
            && _rowByMethod[methodDefRow] != 0;

    internal int GetMethodDefRow(int signatureRow)
    {
        if (signatureRow <= 0 || signatureRow > _recordCount)
        {
            throw InvalidNativeFunctionPointerSignature();
        }

        return checked((int)Read(GetRow(signatureRow), 0));
    }

    internal int FindMatchingSignatureRow(int methodDefRow)
    {
        var methodSignature = _methods.GetSignature(methodDefRow);
        var match = 0;
        for (var signatureRow = 1; signatureRow <= _recordCount; signatureRow++)
        {
            var anchorSignature = _methods.GetSignature(GetMethodDefRow(signatureRow));
            if (!SignatureEquals(methodSignature, anchorSignature))
            {
                continue;
            }

            if (match != 0)
            {
                throw InvalidNativeFunctionPointerSignature();
            }
            match = signatureRow;
        }
        return match;
    }

    private bool SignatureEquals(int leftSignature, int rightSignature)
    {
        if (CanonicalTypeIdentity(_signatures.GetResultTypeSpec(leftSignature),
                allowVoid: true, depth: 0)
            != CanonicalTypeIdentity(_signatures.GetResultTypeSpec(rightSignature),
                allowVoid: true, depth: 0))
        {
            return false;
        }

        var (leftFirst, leftCount) = _signatures.GetParamRange(leftSignature);
        var (rightFirst, rightCount) = _signatures.GetParamRange(rightSignature);
        if (leftCount != rightCount)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < leftCount; ordinal++)
        {
            if (_parameters.GetMode(leftFirst + ordinal) != _parameters.GetMode(rightFirst + ordinal)
                || CanonicalTypeIdentity(_parameters.GetTypeSpec(leftFirst + ordinal),
                    allowVoid: false, depth: 0)
                    != CanonicalTypeIdentity(_parameters.GetTypeSpec(rightFirst + ordinal),
                        allowVoid: false, depth: 0))
            {
                return false;
            }
        }
        return true;
    }

    private string TypeIdentity(int typeSpecRow, bool allowVoid, bool allowStruct)
    {
        var type = _types.GetRow(typeSpecRow);
        if (type.Length == 3 && type[0] == 0x01 && type[1] == 0
            && (type[2] is 0x01 or 0x03 or 0x08 or 0x09 or 0x0D or 0x0E or 0x14 or 0x15
                || (allowVoid && type[2] == 0)))
        {
            return $"scalar:{type[2]}";
        }

        if (type.Length == 6 && type[0] == 0x02 && type[1] == 0 && allowStruct)
        {
            return $"struct:{BinaryPrimitives.ReadUInt32LittleEndian(type[2..])}";
        }

        if (type.Length == 6 && type[0] == 0x0D && type[1] == 0)
        {
            var signatureRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(type[2..]));
            return $"fnptr:{GetCanonicalSignatureId(signatureRow, 1)}";
        }

        if (IsNativePointerType(type))
        {
            return $"ptr:{type[1]}:{NativePointerPointeeIdentity(type, depth: 1)}";
        }

        throw InvalidNativeFunctionPointerSignature();
    }

    private string CanonicalTypeIdentity(int typeSpecRow, bool allowVoid, int depth,
        bool allowStruct = false)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw InvalidNativeFunctionPointerSignature();
        }

        var type = _types.GetRow(typeSpecRow);
        if (type.Length == 6 && type[0] == 0x0D && type[1] == 0)
        {
            var signatureRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(type[2..]));
            return $"fnptr:{GetCanonicalSignatureId(signatureRow, depth + 1)}";
        }

        if (IsNativePointerType(type))
        {
            return $"ptr:{type[1]}:{NativePointerPointeeIdentity(type, depth + 1)}";
        }

        return TypeIdentity(typeSpecRow, allowVoid, allowStruct);
    }

    private string NativePointerPointeeIdentity(ReadOnlySpan<byte> type, int depth)
    {
        if (type.Length == 4)
        {
            var pointeeCode = type[3];
            if (pointeeCode is 0x01 or 0x03 or 0x08 or 0x09 or 0x0D or 0x0E or 0x14 or 0x15
                or 0)
            {
                return $"scalar:{pointeeCode}";
            }

            throw InvalidNativeFunctionPointerSignature();
        }

        var pointeeRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(type[3..]));
        return CanonicalTypeIdentity(pointeeRow, allowVoid: true, depth, allowStruct: true);
    }

    private static bool IsNativePointerType(ReadOnlySpan<byte> type)
        => (type.Length == 4 && type[0] == 0x01 && type[2] == 0x8C)
            || (type.Length == 7 && type[0] == 0x0C && type[2] == 0x8C);

    private int GetCanonicalSignatureId(int signatureRow, int depth)
    {
        if (signatureRow <= 0 || signatureRow > _recordCount
            || depth > SemanticVerifier.MaximumDepth)
        {
            throw InvalidNativeFunctionPointerSignature();
        }
        if (_canonicalSignatureStates[signatureRow] == 2)
        {
            return _canonicalSignatureIdByRow[signatureRow];
        }
        if (_canonicalSignatureStates[signatureRow] == 1)
        {
            throw InvalidNativeFunctionPointerSignature();
        }

        _canonicalSignatureStates[signatureRow] = 1;
        var method = _methodBySignature[signatureRow];
        var signature = _methods.GetSignature(method);
        var typeIdentities = new List<string>();
        typeIdentities.Add(CanonicalTypeIdentity(
            _signatures.GetResultTypeSpec(signature), allowVoid: true, depth + 1));
        var (first, count) = _signatures.GetParamRange(signature);
        for (var parameter = 0; parameter < count; parameter++)
        {
            if (_parameters.GetMode(first + parameter) != 0)
            {
                throw InvalidNativeFunctionPointerSignature();
            }
            typeIdentities.Add(CanonicalTypeIdentity(
                _parameters.GetTypeSpec(first + parameter), allowVoid: false, depth + 1));
        }

        var identity = string.Join(",", typeIdentities);
        if (!_canonicalSignatureIds.TryGetValue(identity, out var canonicalId))
        {
            canonicalId = _canonicalSignatureIds.Count + 1;
            _canonicalSignatureIds.Add(identity, canonicalId);
        }
        _canonicalSignatureIdByRow[signatureRow] = canonicalId;
        _canonicalSignatureStates[signatureRow] = 2;
        return canonicalId;
    }

    private void ValidateSignature(int method)
    {
        var signature = _methods.GetSignature(method);
        _ = CanonicalTypeIdentity(_signatures.GetResultTypeSpec(signature),
            allowVoid: true, depth: 0);
        var (first, count) = _signatures.GetParamRange(signature);
        for (var parameter = 0; parameter < count; parameter++)
        {
            if (_parameters.GetMode(first + parameter) != 0)
            {
                throw InvalidNativeFunctionPointerSignature();
            }
            _ = CanonicalTypeIdentity(_parameters.GetTypeSpec(first + parameter),
                allowVoid: false, depth: 0);
        }
    }

    private ReadOnlySpan<byte> GetRow(int row)
        => _rows.Slice((row - 1) * 8, 8);

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidNativeFunctionPointerSignature()
        => new("Invalid TRRM native function-pointer signature declaration.");
}
