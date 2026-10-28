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
    private readonly TrRmContractTableReader _contracts;
    private readonly TrRmArtifactSchema _schema;
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
        _contracts = contracts;
        _schema = contracts.Schema;
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
                || !ValidAnchorContractRange((int)method, methods, contracts))
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
            var anchorMethod = GetMethodDefRow(signatureRow);
            var anchorSignature = _methods.GetSignature(anchorMethod);
            if ((_schema.UsesDevelopmentFunctionPointerEffects()
                    && _contracts.GetEffectsMask(methodDefRow)
                        != _contracts.GetEffectsMask(anchorMethod))
                || !SignatureEquals(methodSignature, anchorSignature))
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

    internal uint GetDeclaredEffectsMask(int signatureRow)
    {
        if (signatureRow <= 0 || signatureRow > _recordCount)
        {
            throw InvalidNativeFunctionPointerSignature();
        }

        return _contracts.GetEffectsMask(_methodBySignature[signatureRow]);
    }

    private bool ValidAnchorContractRange(int method,
        TrRmMethodDefTableReader methods, TrRmContractTableReader contracts)
    {
        var (first, count) = methods.GetContractRange(method);
        if (!_schema.UsesDevelopmentFunctionPointerEffects())
        {
            return count == 2 && contracts.GetEffectsMask(method) == 4;
        }

        var hasEffects = false;
        var hasNativeAbi = false;
        for (var index = 0; index < count; index++)
        {
            var clause = contracts.GetRow(first + index);
            var owner = BinaryPrimitives.ReadUInt32LittleEndian(clause);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(clause[4..]);
            if (owner != (0x1600_0000u | (uint)method))
            {
                return false;
            }

            if (kind == 1)
            {
                if (hasEffects)
                {
                    return false;
                }
                hasEffects = true;
            }
            else if (kind == 0x10)
            {
                if (hasNativeAbi)
                {
                    return false;
                }
                hasNativeAbi = true;
            }
            else
            {
                return false;
            }
        }

        var effectsMask = contracts.GetEffectsMask(method);
        return hasNativeAbi && (count == 1 || count == 2)
            && hasEffects == (count == 2)
            && (effectsMask & ~6u) == 0
            && contracts.GetNativeParameters(method).IsEmpty
            && contracts.GetNativeOutputs(method).IsEmpty;
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

    private string TypeIdentity(uint typeSpecRef, bool allowVoid, bool allowStruct)
    {
        var type = _types.GetTypeSpecBytes(typeSpecRef);
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

    private string CanonicalTypeIdentity(uint typeSpecRef, bool allowVoid, int depth,
        bool allowStruct = false)
    {
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw InvalidNativeFunctionPointerSignature();
        }

        var type = _types.GetTypeSpecBytes(typeSpecRef);
        if (type.Length == 6 && type[0] == 0x0D && type[1] == 0)
        {
            var signatureRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(type[2..]));
            return $"fnptr:{GetCanonicalSignatureId(signatureRow, depth + 1)}";
        }

        if (IsNativePointerType(type))
        {
            return $"ptr:{type[1]}:{NativePointerPointeeIdentity(type, depth + 1)}";
        }

        return TypeIdentity(typeSpecRef, allowVoid, allowStruct);
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

        var pointeeRef = BinaryPrimitives.ReadUInt32LittleEndian(type[3..]);
        return CanonicalTypeIdentity(pointeeRef, allowVoid: true, depth, allowStruct: true);
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
        if (_schema.UsesDevelopmentFunctionPointerEffects())
        {
            typeIdentities.Add($"effects:{_contracts.GetEffectsMask(method)}");
        }
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
