// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>One assigned standard NativeImport declaration.</summary>
public readonly record struct TrRmNativeImportRow(
    byte OwnerKind, byte ResolutionMode, byte ResolutionPolicy, byte CallConv,
    int OwnerRef, int LibraryNameStringRow, int EntryNameStringRow);

/// <summary>Emits assigned 16-byte standard NativeImport rows.</summary>
public static class TrRmNativeImportTableWriter
{
    /// <summary>Encodes assigned NativeImport rows and validates their owners and contracts.</summary>
    /// <param name="rows">The standard NativeImport rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="fields">The validated FieldDef table.</param>
    /// <param name="typeDefs">The validated TypeDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <param name="modules">The validated Module table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="parameters">The validated Param table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="contracts">The validated Contract table.</param>
    /// <param name="layouts">The validated Layout table.</param>
    /// <returns>The encoded 16-byte-row NativeImport section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmNativeImportRow> rows,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmFieldDefTableReader fields, TrRmTypeDefTableReader typeDefs,
        TrRmImportTableReader imports, TrRmModuleTableReader modules,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts,
        TrRmLayoutTableReader layouts)
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
            result[offset] = row.OwnerKind;
            result[offset + 1] = row.ResolutionMode;
            result[offset + 2] = row.ResolutionPolicy;
            result[offset + 3] = row.CallConv;
            Write(result, offset + 4, row.OwnerRef);
            Write(result, offset + 8, row.LibraryNameStringRow);
            Write(result, offset + 12, row.EntryNameStringRow);
        }

        _ = new TrRmNativeImportTableReader(result, rows.Count, strings, methods,
            fields, typeDefs, imports, modules, signatures, parameters, types,
            contracts, layouts);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), unchecked((uint)value));
}

/// <summary>Validates assigned standard NativeImport rows and their exact static owners.</summary>
/// <remarks>The borrowed NativeImport section must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmNativeImportTableReader
{
    private const byte MethodDefOwnerKind = 1;
    private const byte FieldDefOwnerKind = 2;
    private readonly ReadOnlySpan<byte> _rows;
    private readonly TrRmStringTableReader _strings;
    private readonly TrRmMethodDefTableReader _methods;
    private readonly TrRmFieldDefTableReader _fields;
    private readonly int[] _rowByMethod;
    private readonly int[] _rowByField;
    private readonly int _recordCount;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;

    /// <summary>Validates standard NativeImport rows and their exact static owners.</summary>
    /// <param name="section">The borrowed 16-byte-row section.</param>
    /// <param name="recordCount">The number of rows in the section.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="fields">The validated FieldDef table.</param>
    /// <param name="typeDefs">The validated TypeDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <param name="modules">The validated Module table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="parameters">The validated Param table.</param>
    /// <param name="types">The validated TypeSpec table.</param>
    /// <param name="contracts">The validated Contract table.</param>
    /// <param name="layouts">The validated Layout table.</param>
    public TrRmNativeImportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmFieldDefTableReader fields, TrRmTypeDefTableReader typeDefs,
        TrRmImportTableReader imports, TrRmModuleTableReader modules,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmContractTableReader contracts,
        TrRmLayoutTableReader layouts)
        : this(section, recordCount, strings, methods, fields, typeDefs,
            imports, modules, signatures, parameters, types, contracts,
            layouts, null)
    {
    }

    internal TrRmNativeImportTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmMethodDefTableReader methods, TrRmFieldDefTableReader fields,
        TrRmTypeDefTableReader typeDefs, TrRmImportTableReader imports,
        TrRmModuleTableReader modules, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader types,
        TrRmContractTableReader contracts, TrRmLayoutTableReader layouts,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(4);
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * 16 != section.Length
            || types.NativeFunctionPointerSignatureCount != 0
            || (recordCount == 0 && !section.IsEmpty))
        {
            throw InvalidNativeImport();
        }

        _rows = section;
        _strings = strings;
        _methods = methods;
        _fields = fields;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        _resourceLedger?.ConsumeWork(checked((ulong)methods.Count + 1UL
            + (ulong)fields.Count + 1UL));
        _rowByMethod = new int[methods.Count + 1];
        _rowByField = new int[fields.Count + 1];

        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var data = GetRowBytes(row);
            _resourceLedger?.ConsumeWork(4);
            var ownerKind = data[0];
            var resolutionMode = data[1];
            var resolutionPolicy = data[2];
            var callConv = data[3];
            var ownerRef = Read(data, 4);
            var libraryNameRef = Read(data, 8);
            var entryNameRef = Read(data, 12);
            if (ownerRef is 0 or > 0xFF_FFFF
                || libraryNameRef is 0 or > 0xFF_FFFF
                || libraryNameRef > strings.Count
                || entryNameRef > 0xFF_FFFF || entryNameRef > strings.Count
                || !ValidResolution(resolutionMode, resolutionPolicy))
            {
                throw InvalidNativeImport();
            }

            var libraryName = strings.DecodeName((int)libraryNameRef);
            if (!DirectNativeImportContract.IsValidSourceLibraryName(libraryName))
            {
                throw InvalidNativeImport();
            }

            if (entryNameRef != 0
                && !IsAscii(strings.DecodeName((int)entryNameRef)))
            {
                throw InvalidNativeImport();
            }

            switch (ownerKind)
            {
                case MethodDefOwnerKind:
                    ValidateMethod((int)ownerRef, callConv, methods, signatures,
                        parameters, types, contracts, layouts,
                        _resourceLedger);
                    _resourceLedger?.ConsumeWork(2);
                    if (_rowByMethod[ownerRef] != 0)
                    {
                        throw InvalidNativeImport();
                    }

                    _resourceLedger?.ConsumeWork();
                    _rowByMethod[ownerRef] = row;
                    break;
                case FieldDefOwnerKind:
                    if (callConv != 0 || ownerRef > fields.Count
                        || (fields.GetFlags((int)ownerRef) & 0x08) == 0
                        || !IsNativeAbiFieldType(types, (uint)fields.GetTypeSpecRef((int)ownerRef),
                        fields.GetOwnerTypeDef((int)ownerRef), typeDefs, imports,
                        modules, contracts, layouts, _resourceLedger)
                        || _rowByField[ownerRef] != 0)
                    {
                        throw InvalidNativeImport();
                    }

                    _resourceLedger?.ConsumeWork();
                    _rowByField[ownerRef] = row;
                    break;
                default:
                    throw InvalidNativeImport();
            }
        }
    }

    /// <summary>Gets the number of validated NativeImport rows.</summary>
    public int Count => _recordCount;

    /// <summary>Gets a validated row by its one-based section row number.</summary>
    /// <param name="row">The one-based NativeImport row number.</param>
    /// <returns>The decoded fixed-row fields.</returns>
    public TrRmNativeImportRow GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        var data = GetRowBytes(row);
        _resourceLedger?.ConsumeWork(4);
        return new TrRmNativeImportRow(data[0], data[1], data[2], data[3],
            (int)Read(data, 4), (int)Read(data, 8), (int)Read(data, 12));
    }

    /// <summary>Determines whether a MethodDef has a callable import row.</summary>
    /// <param name="methodDefRow">The one-based MethodDef row.</param>
    /// <returns><see langword="true"/> if the method has an import.</returns>
    public bool HasImportForMethod(int methodDefRow)
    {
        _resourceLedger?.ConsumeWork(2);
        return _rowByMethod is not null && methodDefRow > 0
            && methodDefRow < _rowByMethod.Length
            && _rowByMethod[methodDefRow] != 0;
    }

    /// <summary>Determines whether a FieldDef has a data import row.</summary>
    /// <param name="fieldDefRow">The one-based FieldDef row.</param>
    /// <returns><see langword="true"/> if the field has an import.</returns>
    public bool HasImportForField(int fieldDefRow)
    {
        _resourceLedger?.ConsumeWork(2);
        return _rowByField is not null && fieldDefRow > 0
            && fieldDefRow < _rowByField.Length
            && _rowByField[fieldDefRow] != 0;
    }

    /// <summary>Gets the import row for a method.</summary>
    /// <param name="methodDefRow">The one-based MethodDef row.</param>
    /// <returns>The validated callable import row.</returns>
    /// <exception cref="InvalidDataException">The method has no import row.</exception>
    public TrRmNativeImportRow GetImportForMethod(int methodDefRow)
        => HasImportForMethod(methodDefRow)
            ? GetRow(_rowByMethod[methodDefRow]) : throw InvalidNativeImport();

    /// <summary>Gets the import row for a field.</summary>
    /// <param name="fieldDefRow">The one-based FieldDef row.</param>
    /// <returns>The validated data import row.</returns>
    /// <exception cref="InvalidDataException">The field has no import row.</exception>
    public TrRmNativeImportRow GetImportForField(int fieldDefRow)
        => HasImportForField(fieldDefRow)
            ? GetRow(_rowByField[fieldDefRow]) : throw InvalidNativeImport();

    /// <summary>Decodes the library name referenced by an import row.</summary>
    /// <param name="row">A validated import row.</param>
    /// <returns>The exact decoded library name.</returns>
    public string GetLibraryName(TrRmNativeImportRow row)
    {
        _resourceLedger?.ConsumeWork();
        return _strings.DecodeName(row.LibraryNameStringRow);
    }

    /// <summary>Gets the explicit entry name or exact owner declaration name.</summary>
    /// <param name="row">A validated import row.</param>
    /// <returns>The exact decoded entry name.</returns>
    public string GetEntryName(TrRmNativeImportRow row)
    {
        _resourceLedger?.ConsumeWork();
        return row.EntryNameStringRow != 0
            ? _strings.DecodeName(row.EntryNameStringRow)
            : row.OwnerKind switch
            {
                MethodDefOwnerKind => _methods.GetName(row.OwnerRef),
                FieldDefOwnerKind => _fields.GetName(row.OwnerRef),
                _ => throw InvalidNativeImport(),
            };
    }

    private static void ValidateMethod(int ownerRef, byte callConv,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader types,
        TrRmContractTableReader contracts, TrRmLayoutTableReader layouts,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(3);
        if (ownerRef <= 0 || ownerRef > methods.Count
            || callConv is < 1 or > 4
            || methods.GetBodyRow(ownerRef) != 0)
        {
            throw InvalidNativeImport();
        }

        var signature = methods.GetSignature(ownerRef);
        if (signatures.GetReceiverTypeSpec(signature) != 0
            || signatures.GetGenericCount(signature) != 0
            || (contracts.GetEffectsMask(ownerRef) & 4) == 0)
        {
            throw InvalidNativeImport();
        }

        TrRmNativeImportSignatureValidator.Validate((uint)ownerRef, methods,
            signatures, parameters, types, contracts.GetNativeOutputs(ownerRef),
            contracts.GetNativeParameters(ownerRef), layouts,
            validateNativeParameterAccess: true);
    }

    internal static bool IsNativeAbiFieldType(TrRmTypeSpecTableReader types,
        uint typeSpecRef, int fieldOwnerTypeDef, TrRmTypeDefTableReader typeDefs,
        TrRmImportTableReader imports, TrRmModuleTableReader modules,
        TrRmContractTableReader contracts, TrRmLayoutTableReader layouts,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        resourceLedger?.ConsumeWork();
        if (TrRmNativeImportTypeContract.IsNativeAbiValueType(types, typeSpecRef))
        {
            return true;
        }

        var type = types.GetTypeSpecBytes(typeSpecRef);
        if (type.Length != 6 || type[0] is not (0x02 or 0x03) || type[1] != 0)
        {
            return false;
        }

        var typeDefRow = types.GetNominalTypeDefRow(typeSpecRef, typeDefs, imports);
        if ((typeDefs.GetFlags(typeDefRow) & 0x0C) != 0x04)
        {
            return false;
        }

        var (contractFirst, contractCount) = typeDefs.GetContractRange(typeDefRow);
        for (var contract = contractFirst; contract < contractFirst + contractCount; contract++)
        {
            resourceLedger?.ConsumeWork(3);
            if (BinaryPrimitives.ReadUInt16LittleEndian(contracts.GetRow(contract)[4..]) == 5)
            {
                return false;
            }
        }

        var module = typeDefs.GetOwnerModule(fieldOwnerTypeDef);
        var (targetRequirement, _) = modules.GetRequirements(module);
        return targetRequirement == TrRmModuleTableReader.WindowsX64NativeTargetRequirement
            && layouts.HasBlittableLayout(typeDefRow,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision);
    }

    private ReadOnlySpan<byte> GetRowBytes(int row)
    {
        _resourceLedger?.ConsumeWork();
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidNativeImport();
        }

        return _rows.Slice((row - 1) * 16, 16);
    }

    private static bool ValidResolution(byte mode, byte policy)
        => (mode, policy) is (1, 0) or (2, 1) or (2, 2);

    private static bool IsAscii(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        foreach (var character in name)
        {
            if (character > '\u007F')
            {
                return false;
            }
        }

        return true;
    }

    private uint Read(ReadOnlySpan<byte> bytes, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidNativeImport()
        => new("Invalid standard TRRM NativeImport table.");
}
