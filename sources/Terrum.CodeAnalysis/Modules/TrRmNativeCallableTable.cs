// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmNativeCallableRow(int MethodDefRow, int EntryNameStringRow);

internal static class TrRmNativeCallableTableWriter
{
    internal static byte[] Encode(IReadOnlyList<TrRmNativeCallableRow> rows,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts,
        TrRmNativeFunctionPointerSignatureTableReader nativeFunctionPointerSignatures,
        TrRmImportTableReader imports)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 8];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * 8), (uint)row.MethodDefRow);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan((index * 8) + 4), (uint)row.EntryNameStringRow);
        }

        _ = new TrRmNativeCallableTableReader(result, rows.Count, strings, methods,
            signatures, parameters, types, contracts, nativeFunctionPointerSignatures,
            imports);
        return result;
    }
}

internal readonly ref struct TrRmNativeCallableTableReader
{
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly int[] _rowByMethod;
    private readonly int[] _signatureRowByMethod;
    private readonly int _recordCount;

    internal TrRmNativeCallableTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts,
        TrRmNativeFunctionPointerSignatureTableReader nativeFunctionPointerSignatures,
        TrRmImportTableReader imports)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * 8 != section.Length
            || (recordCount == 0 && !section.IsEmpty))
        {
            throw InvalidNativeCallable();
        }

        _rows = section;
        _strings = strings;
        _recordCount = recordCount;
        _rowByMethod = new int[methods.Count + 1];
        _signatureRowByMethod = new int[methods.Count + 1];
        var entryNames = new HashSet<string>(StringComparer.Ordinal);
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var method = Read(data, 0);
            var entryName = Read(data, 4);
            if (method is 0 or > 0xFF_FFFF || method > methods.Count
                || entryName > 0xFF_FFFF || entryName > strings.Count
                || _rowByMethod[method] != 0
                || methods.GetOwnerType((int)method) != 0
                || methods.GetBodyRow((int)method) == 0
                || imports.IsExpectedDefinition(0x1600_0000u | method)
                || signatures.GetReceiverTypeSpec(methods.GetSignature((int)method)) != 0
                || signatures.GetGenericCount(methods.GetSignature((int)method)) != 0
                || !contracts.HasNativeAbi((int)method)
                || contracts.GetNativeAbi((int)method) != new TrRmNativeAbiContract(1, 2, 0)
                || (contracts.GetEffectsMask((int)method) & 5) != 5
                || (contracts.GetEffectsMask((int)method) & 2) != 0)
            {
                throw InvalidNativeCallable();
            }

            if (entryName != 0)
            {
                var name = strings.DecodeName((int)entryName);
                if (!DirectNativeImportContract.IsValidExportName(name) || !entryNames.Add(name))
                {
                    throw InvalidNativeCallable();
                }
            }

            ValidateSignature((int)method, methods, signatures, parameters, types);
            var signatureRow = nativeFunctionPointerSignatures
                .FindMatchingSignatureRow((int)method);
            if (signatureRow == 0)
            {
                throw InvalidNativeCallable();
            }

            _rowByMethod[method] = row;
            _signatureRowByMethod[method] = signatureRow;
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            if (!contracts.HasNativeAbi(method)
                || contracts.GetNativeAbi(method).Role != 2)
            {
                continue;
            }

            var hasImplementation = methods.GetBodyRow(method) != 0;
            var hasImport = imports.IsExpectedDefinition(0x1600_0000u | (uint)method);
            if (hasImplementation != (_rowByMethod[method] != 0)
                || (!hasImplementation && !hasImport)
                || (hasImplementation && hasImport))
            {
                throw InvalidNativeCallable();
            }

            if (hasImplementation || hasImport)
            {
                ValidateSignature(method, methods, signatures, parameters, types);
                var signatureRow = nativeFunctionPointerSignatures.FindMatchingSignatureRow(method);
                if (signatureRow == 0)
                {
                    throw InvalidNativeCallable();
                }
                _signatureRowByMethod[method] = signatureRow;
            }
        }
    }

    internal int Count => _recordCount;

    internal int GetFunctionPointerSignatureIndex(int methodDefRow)
        => HasCallableForMethod(methodDefRow)
            ? _signatureRowByMethod[methodDefRow] - 1
            : throw InvalidNativeCallable();

    internal bool HasCallableForMethod(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _signatureRowByMethod.Length
            && _signatureRowByMethod[methodDefRow] != 0;

    internal bool HasCallableEntryForMethod(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _rowByMethod.Length
            && _rowByMethod[methodDefRow] != 0;

    internal string? GetEntryName(int methodDefRow)
    {
        if (!HasCallableEntryForMethod(methodDefRow))
        {
            throw InvalidNativeCallable();
        }

        var entryName = Read(GetRow(_rowByMethod[methodDefRow]), 4);
        return entryName == 0 ? null : _strings.DecodeName((int)entryName);
    }

    private ReadOnlySpan<byte> GetRow(int row)
        => _rows.Slice((row - 1) * 8, 8);

    private static void ValidateSignature(int method, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types)
    {
        var signature = methods.GetSignature(method);
        if (!IsCallableType(types, signatures.GetResultTypeSpec(signature), allowVoid: true))
        {
            throw InvalidNativeCallable();
        }

        var (first, count) = signatures.GetParamRange(signature);
        for (var parameter = 0; parameter < count; parameter++)
        {
            if (parameters.GetMode(first + parameter) != 0
                || !IsCallableType(types, parameters.GetTypeSpec(first + parameter), allowVoid: false))
            {
                throw InvalidNativeCallable();
            }
        }
    }

    private static bool IsCallableType(TrRmTypeSpecTableReader types, int row, bool allowVoid)
    {
        var type = types.GetRow(row);
        return (type.Length == 3 && type[0] == 1 && type[1] == 0
                && (type[2] is 1 or 3 or 8 or 9 or 0x0D or 0x0E or 0x14 or 0x15
                    || (allowVoid && type[2] == 0)))
            || (type.Length == 4 && type[0] == 1 && type[2] == 0x8C)
            || (type.Length == 7 && type[0] == 0x0C && type[2] == 0x8C)
            || (type.Length == 6 && type[0] == 0x0D && type[1] == 0);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidNativeCallable()
        => new("Invalid TRRM native callable declaration.");
}
