// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmNativeImportTypeContract
{
    internal static bool TryGetDirectNativeImportResultType(TrRmTypeSpecTableReader types,
        uint typeSpecRef, out PrimitiveType resultType)
    {
        var type = types.GetTypeSpecBytes(typeSpecRef);
        if (type.Length == 3 && type[0] == 1 && type[1] == 0 && type[2] == 0)
        {
            resultType = PrimitiveType.Void;
            return true;
        }

        if (TryGetScalarType(types, typeSpecRef, out resultType))
        {
            return DirectNativeImportContract.IsScalar(resultType);
        }

        if (IsNativeAbiPointerOrFunctionPointerType(types, typeSpecRef))
        {
            resultType = PrimitiveType.NativeRawPointer;
            return true;
        }

        resultType = PrimitiveType.Void;
        return false;
    }

    internal static bool TryGetScalarType(TrRmTypeSpecTableReader types, uint typeSpecRef,
        out PrimitiveType scalarType)
    {
        var type = types.GetTypeSpecBytes(typeSpecRef);
        scalarType = type.Length == 3 && type[0] == 1 && type[1] == 0
            ? FromAtomicCode(type[2]) : PrimitiveType.Void;
        return scalarType != PrimitiveType.Void;
    }

    internal static bool TryGetWritableOutputPointee(TrRmTypeSpecTableReader types,
        uint typeSpecRef, out PrimitiveType pointeeType)
    {
        var type = types.GetTypeSpecBytes(typeSpecRef);
        pointeeType = type.Length == 4 && type[0] == 1 && type[1] == 1 && type[2] == 0x8C
            ? FromAtomicCode(type[3]) : PrimitiveType.Void;
        if (pointeeType != PrimitiveType.Void)
        {
            return DirectNativeImportContract.IsOutputPointee(pointeeType);
        }

        if (type.Length == 7 && type[0] == 0x0C && type[1] == 1 && type[2] == 0x8C)
        {
            var innerRef = BinaryPrimitives.ReadUInt32LittleEndian(type[3..]);
            var inner = types.GetTypeSpecBytes(innerRef);
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
        uint typeSpecRef, out PrimitiveType pointeeType)
    {
        var type = types.GetTypeSpecBytes(typeSpecRef);
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
            var innerRef = BinaryPrimitives.ReadUInt32LittleEndian(type[3..]);
            var inner = types.GetTypeSpecBytes(innerRef);
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
        uint typeSpecRef, byte access, TrRmLayoutTableReader layouts,
        out PrimitiveType pointeeType)
    {
        pointeeType = PrimitiveType.Void;
        if (access is < 1 or > 3)
        {
            return false;
        }

        var type = types.GetTypeSpecBytes(typeSpecRef);
        if (type.Length == 4 && type[0] == 1 && type[2] == 0x8C)
        {
            if (type[1] is not (0 or 1) || (access is 2 or 3 && type[1] != 1))
            {
                return false;
            }

            if (type[3] == 0)
            {
                return access == 1 && type[1] == 0;
            }

            pointeeType = FromAtomicCode(type[3]);
            return DirectNativeImportContract.IsScalar(pointeeType);
        }

        if (type.Length != 7 || type[0] != 0x0C || type[2] != 0x8C
            || type[1] is not (0 or 1) || (access is 2 or 3 && type[1] != 1))
        {
            return false;
        }

        var pointeeRef = BinaryPrimitives.ReadUInt32LittleEndian(type[3..]);
        var pointee = types.GetTypeSpecBytes(pointeeRef);
        if (pointee is [1, 0, 0])
        {
            return access == 1 && type[1] == 0;
        }

        if (TryGetScalarType(types, pointeeRef, out pointeeType)
            && DirectNativeImportContract.IsScalar(pointeeType))
        {
            return true;
        }

        if (TryGetBlittableAggregateParameterTypeDef(types, typeSpecRef,
            layouts, access is 2 or 3, out _)
            || IsNativeAbiValueType(types, pointeeRef))
        {
            pointeeType = PrimitiveType.NativeRawPointer;
            return true;
        }

        pointeeType = PrimitiveType.Void;
        return false;
    }

    internal static bool MatchesNativeParameterArgumentType(
        TrRmTypeSpecTableReader types, uint argumentTypeSpecRef,
        uint formalTypeSpecRef, byte access)
    {
        if (access is < 1 or > 3
            || !TryGetNativePointerPointeeShape(types, argumentTypeSpecRef,
                out var argumentQualifier, out var argumentInlinePointee,
                out var argumentPointeeRef)
            || !TryGetNativePointerPointeeShape(types, formalTypeSpecRef,
                out var formalQualifier, out var formalInlinePointee,
                out var formalPointeeRef)
            || (access is 2 or 3 && (argumentQualifier != 1 || formalQualifier != 1)))
        {
            return false;
        }

        var argumentIsVoid = IsVoidPointee(types, argumentInlinePointee, argumentPointeeRef);
        var formalIsVoid = IsVoidPointee(types, formalInlinePointee, formalPointeeRef);
        if (argumentIsVoid || formalIsVoid)
        {
            return argumentIsVoid && formalIsVoid && access == 1
                && argumentQualifier == 0 && formalQualifier == 0;
        }

        if (argumentPointeeRef == 0 && formalPointeeRef == 0)
        {
            return argumentInlinePointee == formalInlinePointee;
        }
        if (argumentPointeeRef == 0
            && TryGetScalarType(types, formalPointeeRef, out var formalScalar))
        {
            return FromAtomicCode(argumentInlinePointee) == formalScalar;
        }
        if (formalPointeeRef == 0
            && TryGetScalarType(types, argumentPointeeRef, out var argumentScalar))
        {
            return FromAtomicCode(formalInlinePointee) == argumentScalar;
        }

        return types.GetTypeSpecBytes(argumentPointeeRef)
            .SequenceEqual(types.GetTypeSpecBytes(formalPointeeRef));
    }

    private static bool TryGetNativePointerPointeeShape(TrRmTypeSpecTableReader types,
        uint typeSpecRef, out byte qualifier, out byte inlinePointeeCode,
        out uint pointeeTypeSpecRef)
    {
        var type = types.GetTypeSpecBytes(typeSpecRef);
        if (type.Length == 4 && type[0] == 0x01 && type[2] == 0x8C)
        {
            qualifier = type[1];
            inlinePointeeCode = type[3];
            pointeeTypeSpecRef = 0;
            return qualifier is 0 or 1;
        }

        if (type.Length == 7 && type[0] == 0x0C && type[2] == 0x8C)
        {
            qualifier = type[1];
            inlinePointeeCode = 0;
            pointeeTypeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(type[3..]);
            return qualifier is 0 or 1 && pointeeTypeSpecRef != 0;
        }

        qualifier = 0;
        inlinePointeeCode = 0;
        pointeeTypeSpecRef = 0;
        return false;
    }

    private static bool IsVoidPointee(TrRmTypeSpecTableReader types,
        byte inlinePointeeCode, uint pointeeTypeSpecRef)
        => pointeeTypeSpecRef == 0
            ? inlinePointeeCode == 0
            : types.GetTypeSpecBytes(pointeeTypeSpecRef) is [0x01, 0x00, 0x00];

    internal static bool IsNativeAbiValueType(TrRmTypeSpecTableReader types, uint typeSpecRef)
        => (TryGetScalarType(types, typeSpecRef, out var scalarType)
                && DirectNativeImportContract.IsScalar(scalarType))
            || IsNativeAbiPointerOrFunctionPointerType(types, typeSpecRef);

    private static bool IsNativeAbiPointerOrFunctionPointerType(
        TrRmTypeSpecTableReader types, uint typeSpecRef)
    {
        var type = types.GetTypeSpecBytes(typeSpecRef);
        if ((type.Length == 4 && type[0] == 0x01 && type[2] == 0x8C)
            || (type.Length == 7 && type[0] == 0x0C && type[2] == 0x8C))
        {
            return true;
        }

        if (type.Length == 7 && type[0] == 0x0D && type[1] == 0)
        {
            var typeSpec = types.GetTypeSpecRef(typeSpecRef);
            return types.TryGetNativeFunctionPointerAnchor(typeSpec.Row,
                out _, out _);
        }

        if (type.Length != 6 || type[0] != 0x0D || type[1] != 0)
        {
            return false;
        }

        var signatureRow = BinaryPrimitives.ReadUInt32LittleEndian(type[2..]);
        return signatureRow != 0 && signatureRow <= types.NativeFunctionPointerSignatureCount;
    }

    internal static bool TryGetBlittableAggregateParameterTypeDef(
        TrRmTypeSpecTableReader types, uint typeSpecRef,
        TrRmLayoutTableReader layouts, out int typeDefRow)
        => TryGetBlittableAggregateParameterTypeDef(types, typeSpecRef,
            layouts, writableRequired: false, out typeDefRow);

    internal static bool TryGetBlittableAggregateParameterTypeDef(
        TrRmTypeSpecTableReader types, uint typeSpecRef,
        TrRmLayoutTableReader layouts, bool writableRequired, out int typeDefRow)
    {
        var type = types.GetTypeSpecBytes(typeSpecRef);
        if (type.Length == 7 && type[0] == 0x0C
            && type[1] is 0 or 1 && (!writableRequired || type[1] == 1)
            && type[2] == 0x8C)
        {
            var innerRef = BinaryPrimitives.ReadUInt32LittleEndian(type[3..]);
            var inner = types.GetTypeSpecBytes(innerRef);
            if (inner.Length == 6 && inner[0] == 0x02 && inner[1] == 0)
            {
                typeDefRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner[2..]));
                return layouts.HasBlittableLayout(typeDefRow, 1, 1);
            }
        }

        typeDefRow = 0;
        return false;
    }

    private static PrimitiveType FromAtomicCode(byte typeCode) => typeCode switch
    {
        1 => PrimitiveType.Bool,
        2 => PrimitiveType.Char8,
        3 => PrimitiveType.Char16,
        4 => PrimitiveType.Char32,
        5 => PrimitiveType.Rune,
        6 => PrimitiveType.Int8,
        7 => PrimitiveType.Int16,
        8 => PrimitiveType.Int32,
        9 => PrimitiveType.Int64,
        0x0B => PrimitiveType.UInt8,
        0x0C => PrimitiveType.UInt16,
        0x0D => PrimitiveType.UInt32,
        0x0E => PrimitiveType.UInt64,
        0x14 => PrimitiveType.Float32,
        0x15 => PrimitiveType.Float64,
        _ => PrimitiveType.Void,
    };
}

/// <summary>One native import declaration in the development-only TRRM prototype.</summary>
/// <param name="MethodDefRow">The 1-based method-definition row.</param>
/// <param name="LibraryNameStringRow">The 1-based library-name string row.</param>
/// <param name="ExportNameStringRow">The 1-based export-name string row.</param>
/// <param name="ResolutionPolicy">The declared native-library resolution policy.</param>
public readonly record struct TrRmDevelopmentNativeImportRow(
    int MethodDefRow, int LibraryNameStringRow, int ExportNameStringRow, uint ResolutionPolicy);

/// <summary>Emits rows for the development-only four-word NativeImport prototype.</summary>
public static class TrRmDevelopmentNativeImportTableWriter
{
    /// <summary>Encodes and validates development-prototype native import rows.</summary>
    /// <param name="rows">The rows to encode.</param>
    /// <param name="strings">The string table used to validate library and export names.</param>
    /// <param name="methods">The method definitions associated with the imports.</param>
    /// <param name="signatures">The method signatures associated with the imports.</param>
    /// <param name="parameters">The method parameter declarations associated with the imports.</param>
    /// <param name="types">The type specifications used by the signatures.</param>
    /// <param name="contracts">The method contracts used by the imports.</param>
    /// <param name="layouts">The selected target layouts used by aggregate pointees.</param>
    /// <returns>The encoded native-import section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmDevelopmentNativeImportRow> rows,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts,
        TrRmLayoutTableReader layouts)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (contracts.Schema.UsesDevelopmentNativeConventions())
        {
            throw new ArgumentException(
                "The selected Development schema requires explicit resolution rows.",
                nameof(contracts));
        }
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

        _ = new TrRmDevelopmentNativeImportTableReader(result, rows.Count, strings, methods,
            signatures, parameters, types, contracts, layouts);
        return result;
    }

    internal static byte[] EncodeNativeConventionsDraft(
        IReadOnlyList<TrRmDevelopmentNativeImportResolutionRow> rows,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts,
        TrRmLayoutTableReader layouts)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!contracts.Schema.UsesDevelopmentNativeConventions())
        {
            throw new ArgumentException(
                "The selected Development schema does not use resolution rows.",
                nameof(contracts));
        }
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var result = new byte[rows.Count * 16];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!TrRmDevelopmentNativeConventions.TryEncodeResolution(
                row.ResolutionMode, row.ResolutionPolicy,
                out var mode, out var policy))
            {
                throw new ArgumentException(
                    "A native import has an invalid resolution pair.", nameof(rows));
            }

            var offset = index * 16;
            Write(result, offset, row.MethodDefRow);
            Write(result, offset + 4, row.LibraryNameStringRow);
            Write(result, offset + 8, row.ExportNameStringRow);
            result[offset + 12] = mode;
            result[offset + 13] = policy;
        }

        _ = new TrRmDevelopmentNativeImportTableReader(result, rows.Count, strings, methods,
            signatures, parameters, types, contracts, layouts);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => Write(bytes, offset, unchecked((uint)value));

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}

/// <summary>Validates NativeImport rows in the selected Development TRRM schema.</summary>
/// <remarks>The borrowed NativeImport section must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmDevelopmentNativeImportTableReader
{
    private const uint WindowsSystem32ResolutionPolicy = 1;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly int[] _rowByMethod;
    private readonly NativeImportResolutionMode[] _resolutionModeByMethod;
    private readonly NativeImportResolutionPolicy[] _resolutionPolicyByMethod;
    private readonly bool _explicitResolution;
    private readonly int _recordCount;

    /// <summary>Validates the native import section against its declarations.</summary>
    /// <param name="section">The borrowed section bytes.</param>
    /// <param name="recordCount">The number of fixed-size rows in the section.</param>
    /// <param name="strings">The associated string table.</param>
    /// <param name="methods">The associated method-definition table.</param>
    /// <param name="signatures">The associated method-signature table.</param>
    /// <param name="parameters">The associated parameter table.</param>
    /// <param name="types">The associated type-specification table.</param>
    /// <param name="contracts">The associated contract table.</param>
    /// <param name="layouts">The validated Layout table.</param>
    public TrRmDevelopmentNativeImportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts,
        TrRmLayoutTableReader layouts)
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
        _resolutionModeByMethod = new NativeImportResolutionMode[methods.Count + 1];
        _resolutionPolicyByMethod = new NativeImportResolutionPolicy[methods.Count + 1];
        _explicitResolution = contracts.Schema.UsesDevelopmentNativeConventions();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = GetRow(row);
            var method = Read(data, 0);
            var library = Read(data, 4);
            var export = Read(data, 8);
            var resolutionMode = NativeImportResolutionMode.Link;
            var resolutionPolicy = NativeImportResolutionPolicy.NotApplicable;
            var validResolution = _explicitResolution
                ? data[14] == 0 && data[15] == 0
                    && TrRmDevelopmentNativeConventions.TryDecodeResolution(
                        data[12], data[13], out resolutionMode, out resolutionPolicy)
                : Read(data, 12) == WindowsSystem32ResolutionPolicy;
            var effectsMask = method > 0 && method <= methods.Count
                ? contracts.GetEffectsMask((int)method) : 0;
            if (method is 0 or > 0xFF_FFFF || method > methods.Count
                || library is 0 or > 0xFF_FFFF || export is 0 or > 0xFF_FFFF
                || policy != WindowsSystem32ResolutionPolicy
                || _rowByMethod[method] != 0
                || methods.GetOwnerType((int)method) != 0
                || methods.GetBodyRow((int)method) != 0
                || signatures.GetReceiverTypeSpec(methods.GetSignature((int)method)) != 0
                || signatures.GetGenericCount(methods.GetSignature((int)method)) != 0
                || (!contracts.Schema.UsesDevelopmentFunctionPointerEffects()
                    && (effectsMask & 4) == 0)
                || !contracts.HasNativeAbi((int)method)
                || !HasNativeAbiRole(contracts.GetNativeAbi((int)method), 1))
            {
                throw InvalidNativeImport();
            }

            var libraryName = strings.DecodeName((int)library);
            var exportName = strings.DecodeName((int)export);
            if (!DirectNativeImportContract.IsValidPrivateWindowsLibraryName(libraryName)
                || !DirectNativeImportContract.IsValidExportName(exportName))
            {
                throw InvalidNativeImport();
            }

            _rowByMethod[method] = row;
            TrRmNativeImportSignatureValidator.Validate(method, methods, signatures, parameters, types,
                contracts.GetNativeOutputs((int)method), contracts.GetNativeParameters((int)method),
                layouts, validateNativeParameterAccess: true);
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            var outputs = contracts.GetNativeOutputs(method);
            if (_rowByMethod[method] == 0)
            {
                var abiRole = contracts.HasNativeAbi(method)
                    ? contracts.GetNativeAbi(method).Role : (byte)0;
                if (abiRole == 1 || (abiRole != 4 && !outputs.IsEmpty)
                    || (abiRole != 4 && !contracts.GetNativeParameters(method).IsDefaultOrEmpty))
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

    /// <summary>Gets the number of import rows.</summary>
    public int Count => _recordCount;

    /// <summary>Determines whether a method has a native import declaration.</summary>
    /// <param name="methodDefRow">The 1-based method-definition row.</param>
    /// <returns><see langword="true"/> when the method has an import row.</returns>
    public bool HasImportForMethod(int methodDefRow)
        => _rowByMethod is not null && methodDefRow > 0 && methodDefRow < _rowByMethod.Length
            && _rowByMethod[methodDefRow] != 0;

    /// <summary>Gets the import row for a method.</summary>
    /// <param name="methodDefRow">The 1-based method-definition row.</param>
    /// <returns>The import declaration for the method.</returns>
    /// <exception cref="InvalidDataException">The method has no native import declaration.</exception>
    public TrRmDevelopmentNativeImportRow GetImportForMethod(int methodDefRow)
    {
        if (_explicitResolution || !HasImportForMethod(methodDefRow))
        {
            throw InvalidNativeImport();
        }

        var data = GetRow(_rowByMethod[methodDefRow]);
        return new TrRmDevelopmentNativeImportRow(methodDefRow, (int)Read(data, 4),
            (int)Read(data, 8), _explicitResolution ? data[13] : Read(data, 12));
    }

    internal TrRmDevelopmentNativeImportResolutionRow
        GetResolutionImportForMethod(int methodDefRow)
    {
        if (!_explicitResolution || !HasImportForMethod(methodDefRow))
        {
            throw InvalidNativeImport();
        }

        var data = GetRow(_rowByMethod[methodDefRow]);
        return new TrRmDevelopmentNativeImportResolutionRow(methodDefRow,
            (int)Read(data, 4), (int)Read(data, 8),
            _resolutionModeByMethod[methodDefRow],
            _resolutionPolicyByMethod[methodDefRow]);
    }

    internal NativeImportResolutionMode GetResolutionMode(int methodDefRow)
        => HasImportForMethod(methodDefRow)
            ? _resolutionModeByMethod[methodDefRow] : throw InvalidNativeImport();

    internal NativeImportResolutionPolicy GetResolutionPolicy(int methodDefRow)
        => HasImportForMethod(methodDefRow)
            ? _resolutionPolicyByMethod[methodDefRow] : throw InvalidNativeImport();

    /// <summary>Gets the library name associated with a method's import.</summary>
    /// <param name="methodDefRow">The 1-based method-definition row.</param>
    /// <returns>The decoded library name.</returns>
    public string GetLibraryName(int methodDefRow)
        => _strings.DecodeName(GetImportForMethod(methodDefRow).LibraryNameStringRow);

    /// <summary>Gets the export name associated with a method's import.</summary>
    /// <param name="methodDefRow">The 1-based method-definition row.</param>
    /// <returns>The decoded export name.</returns>
    public string GetExportName(int methodDefRow)
        => _strings.DecodeName(GetImportForMethod(methodDefRow).ExportNameStringRow);

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

    private static bool HasNativeAbiRole(TrRmNativeAbiContract abi, byte role)
        => abi.AbiId == 1 && abi.Role == role && abi.ExceptionMode == 0;

    private static InvalidDataException InvalidNativeImport()
        => new("Invalid TRRM NativeImport table.");
}

internal static class TrRmNativeImportSignatureValidator
{
    internal static void Validate(uint method, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types,
        System.Collections.Immutable.ImmutableArray<TrRmNativeOutputContract> outputs,
        System.Collections.Immutable.ImmutableArray<TrRmNativeParameterContract> nativeParameters,
        TrRmLayoutTableReader layouts, bool validateNativeParameterAccess = false)
    {
        var signature = methods.GetSignature((int)method);
        if (!TrRmNativeImportTypeContract.TryGetDirectNativeImportResultType(
                types, signatures.GetResultTypeSpec(signature), out var resultType))
        {
            throw InvalidNativeImport();
        }

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
            var hasNativeParameter = TryGetNativeParameter(
                nativeParameters, parameter, out var nativeParameter);
            var nativePointeeType = PrimitiveType.Void;
            var hasNativePointeeType = hasNativeParameter
                && (validateNativeParameterAccess
                    ? TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
                        types, typeRow, nativeParameter.Access, layouts,
                        out nativePointeeType)
                    : TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
                        types, typeRow, out nativePointeeType));
            if (TrRmNativeImportTypeContract.TryGetWritableOutputPointee(
                    types, typeRow, out var pointeeType)
                && seenOutputs[parameter])
            {
                if (parameters.GetMode(first + parameter) != 0 || hasNativeParameter)
                {
                    throw InvalidNativeImport();
                }

                var output = FindOutput(outputs, parameter);
                if (output.PointeeType != pointeeType)
                {
                    throw InvalidNativeImport();
                }
            }
            else if (hasNativePointeeType)
            {
                if (parameters.GetMode(first + parameter) != 0 || seenOutputs[parameter]
                    || !ValidNativeParameter(nativeParameter, nativePointeeType))
                {
                    throw InvalidNativeImport();
                }

                seenNativeParameters[parameter] = true;
            }
            else if (TrRmNativeImportTypeContract.TryGetBlittableAggregateParameterTypeDef(
                        types, typeRow, layouts, out _)
                    && hasNativeParameter)
            {
                if (parameters.GetMode(first + parameter) != 0 || seenOutputs[parameter]
                    || !ValidNativeParameter(nativeParameter, PrimitiveType.NativeRawPointer))
                {
                    throw InvalidNativeImport();
                }

                seenNativeParameters[parameter] = true;
            }
            else if (parameters.GetMode(first + parameter) != 0 || seenOutputs[parameter]
                || hasNativeParameter
                || !TrRmNativeImportTypeContract.IsNativeAbiValueType(types, typeRow))
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

    private static bool ValidNativeParameter(TrRmNativeParameterContract parameter,
        PrimitiveType pointeeType)
        => parameter.PointerDepth == 1 && parameter.Retention == 1
            && parameter.RequiredAlignment == 0
            && parameter.ExtentKind is 0 or 1
            && (parameter.ExtentKind != 0 || parameter.Extent == 0)
            && (pointeeType == PrimitiveType.Void
                ? parameter.Access == 1 && parameter.Extent == 0
                : parameter.Access is >= 1 and <= 3);

    private static TrRmNativeOutputContract FindOutput(
        System.Collections.Immutable.ImmutableArray<TrRmNativeOutputContract> outputs,
        int parameter)
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

    private static InvalidDataException InvalidNativeImport()
        => new("Invalid TRRM NativeImport signature.");
}
