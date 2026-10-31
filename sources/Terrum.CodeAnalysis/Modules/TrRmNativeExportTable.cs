// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>One assigned standard NativeExport declaration.</summary>
public readonly record struct TrRmNativeExportRow(
    byte OwnerKind, int OwnerRef, int EntryNameStringRow, int NativeCallableContractRow);

/// <summary>Emits assigned 16-byte standard NativeExport rows.</summary>
public static class TrRmNativeExportTableWriter
{
    /// <summary>Encodes standard NativeExport rows and validates their owners and references.</summary>
    /// <param name="rows">The standard NativeExport rows.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="fields">The validated FieldDef table.</param>
    /// <param name="typeDefs">The validated TypeDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <param name="modules">The validated Module table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="typeSpecs">The validated TypeSpec table.</param>
    /// <param name="contracts">The validated Contract table.</param>
    /// <param name="layouts">The validated Layout table.</param>
    /// <returns>The encoded 16-byte-row NativeExport section.</returns>
    public static byte[] Encode(IReadOnlyList<TrRmNativeExportRow> rows,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmFieldDefTableReader fields, TrRmTypeDefTableReader typeDefs,
        TrRmImportTableReader imports, TrRmModuleTableReader modules,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader typeSpecs,
        TrRmContractTableReader contracts, TrRmLayoutTableReader layouts)
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
            Write(result, offset + 4, row.OwnerRef);
            Write(result, offset + 8, row.EntryNameStringRow);
            Write(result, offset + 12, row.NativeCallableContractRow);
        }

        _ = new TrRmNativeExportTableReader(result, rows.Count, strings, methods,
            fields, typeDefs, imports, modules, signatures, typeSpecs, contracts,
            layouts);
        return result;
    }

    private static void Write(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset), unchecked((uint)value));
}

/// <summary>Validates standard NativeExport rows and their exact static owners.</summary>
/// <remarks>The borrowed NativeExport section must remain unchanged while this reader is in use.</remarks>
public readonly ref struct TrRmNativeExportTableReader
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

    /// <summary>Validates NativeExport rows, exact static owners and callable-clause references.</summary>
    /// <param name="section">The borrowed 16-byte-row section.</param>
    /// <param name="recordCount">The number of rows in the section.</param>
    /// <param name="strings">The validated String table.</param>
    /// <param name="methods">The validated MethodDef table.</param>
    /// <param name="fields">The validated FieldDef table.</param>
    /// <param name="typeDefs">The validated TypeDef table.</param>
    /// <param name="imports">The validated Import table.</param>
    /// <param name="modules">The validated Module table.</param>
    /// <param name="signatures">The validated MethodSig table.</param>
    /// <param name="typeSpecs">The validated TypeSpec table.</param>
    /// <param name="contracts">The validated Contract table.</param>
    /// <param name="layouts">The validated Layout table.</param>
    public TrRmNativeExportTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmStringTableReader strings, TrRmMethodDefTableReader methods,
        TrRmFieldDefTableReader fields, TrRmTypeDefTableReader typeDefs,
        TrRmImportTableReader imports, TrRmModuleTableReader modules,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader typeSpecs,
        TrRmContractTableReader contracts, TrRmLayoutTableReader layouts)
        : this(section, recordCount, strings, methods, fields, typeDefs,
            imports, modules, signatures, typeSpecs, contracts, layouts, null)
    {
    }

    internal TrRmNativeExportTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmMethodDefTableReader methods, TrRmFieldDefTableReader fields,
        TrRmTypeDefTableReader typeDefs, TrRmImportTableReader imports,
        TrRmModuleTableReader modules, TrRmMethodSigTableReader signatures,
        TrRmTypeSpecTableReader typeSpecs, TrRmContractTableReader contracts,
        TrRmLayoutTableReader layouts,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(3);
        if (recordCount is < 0 or > 0xFF_FFFF
            || (long)recordCount * 16 != section.Length
            || (recordCount == 0 && !section.IsEmpty))
        {
            throw InvalidNativeExport();
        }

        _rows = section;
        _strings = strings;
        _methods = methods;
        _fields = fields;
        _recordCount = recordCount;
        _resourceLedger = resourceLedger;
        _resourceLedger?.ConsumeWork(checked((ulong)methods.Count + 1UL
            + (ulong)fields.Count + 1UL + (ulong)recordCount));
        _rowByMethod = new int[methods.Count + 1];
        _rowByField = new int[fields.Count + 1];
        _resourceLedger?.ConsumeWork();
        var entryNames = new HashSet<string>(StringComparer.Ordinal);

        for (var row = 1; row <= recordCount; row++)
        {
            _resourceLedger?.ConsumeWork();
            var data = GetRowBytes(row);
            _resourceLedger?.ConsumeWork(4);
            var ownerKind = data[0];
            var flags = data[1];
            _resourceLedger?.ConsumeWork(3);
            var reserved = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            var ownerRef = Read(data, 4);
            var entryNameRef = Read(data, 8);
            var nativeCallableContractRef = Read(data, 12);
            _resourceLedger?.ConsumeWork(6);
            if (flags != 0 || reserved != 0
                || ownerRef is 0 or > 0xFF_FFFF
                || entryNameRef > 0xFF_FFFF || entryNameRef > strings.Count
                || nativeCallableContractRef > 0xFF_FFFF
                || nativeCallableContractRef > contracts.Count)
            {
                throw InvalidNativeExport();
            }

            var declarationName = ownerKind switch
            {
                MethodDefOwnerKind => ValidateMethod((int)ownerRef,
                    (int)nativeCallableContractRef, methods, signatures, contracts),
                FieldDefOwnerKind => ValidateField((int)ownerRef,
                    (int)nativeCallableContractRef, fields, typeDefs, imports, modules,
                    typeSpecs, contracts, layouts, _resourceLedger),
                _ => throw InvalidNativeExport(),
            };
            if (ownerKind == MethodDefOwnerKind)
            {
                _resourceLedger?.ConsumeWork(2);
                if (_rowByMethod[ownerRef] != 0)
                {
                    throw InvalidNativeExport();
                }

                _rowByMethod[ownerRef] = row;
            }
            else
            {
                _resourceLedger?.ConsumeWork(2);
                if (_rowByField[ownerRef] != 0)
                {
                    throw InvalidNativeExport();
                }

                _rowByField[ownerRef] = row;
            }

            var entryName = entryNameRef == 0
                ? declarationName : strings.DecodeName((int)entryNameRef);
            _resourceLedger?.ConsumeWork(checked(
                ((ulong)entryName.Length * sizeof(char)) + 2UL));
            if (entryName.Length == 0 || !entryNames.Add(entryName))
            {
                throw InvalidNativeExport();
            }
        }
    }

    /// <summary>Gets the number of validated NativeExport rows.</summary>
    public int Count => _recordCount;

    /// <summary>Gets a validated row by its one-based section row number.</summary>
    /// <param name="row">The one-based NativeExport row number.</param>
    /// <returns>The decoded fixed-row fields.</returns>
    public TrRmNativeExportRow GetRow(int row)
    {
        _resourceLedger?.ConsumeWork();
        var data = GetRowBytes(row);
        _resourceLedger?.ConsumeWork(4);
        return new TrRmNativeExportRow(data[0], (int)Read(data, 4),
            (int)Read(data, 8), (int)Read(data, 12));
    }

    /// <summary>Determines whether a MethodDef has a native export row.</summary>
    /// <param name="methodDefRow">The one-based MethodDef row.</param>
    /// <returns><see langword="true"/> if the method has a NativeExport row.</returns>
    public bool HasExportForMethod(int methodDefRow)
    {
        _resourceLedger?.ConsumeWork(2);
        return _rowByMethod is not null && methodDefRow > 0
            && methodDefRow < _rowByMethod.Length
            && _rowByMethod[methodDefRow] != 0;
    }

    /// <summary>Determines whether a FieldDef has a native export row.</summary>
    /// <param name="fieldDefRow">The one-based FieldDef row.</param>
    /// <returns><see langword="true"/> if the field has a NativeExport row.</returns>
    public bool HasExportForField(int fieldDefRow)
    {
        _resourceLedger?.ConsumeWork(2);
        return _rowByField is not null && fieldDefRow > 0
            && fieldDefRow < _rowByField.Length
            && _rowByField[fieldDefRow] != 0;
    }

    /// <summary>Gets the NativeExport row for a method.</summary>
    /// <param name="methodDefRow">The one-based MethodDef row.</param>
    /// <returns>The validated NativeExport row.</returns>
    /// <exception cref="InvalidDataException">The method has no NativeExport row.</exception>
    public TrRmNativeExportRow GetExportForMethod(int methodDefRow)
        => HasExportForMethod(methodDefRow)
            ? GetRow(_rowByMethod[methodDefRow]) : throw InvalidNativeExport();

    /// <summary>Gets the NativeExport row for a field.</summary>
    /// <param name="fieldDefRow">The one-based FieldDef row.</param>
    /// <returns>The validated NativeExport row.</returns>
    /// <exception cref="InvalidDataException">The field has no NativeExport row.</exception>
    public TrRmNativeExportRow GetExportForField(int fieldDefRow)
        => HasExportForField(fieldDefRow)
            ? GetRow(_rowByField[fieldDefRow]) : throw InvalidNativeExport();

    /// <summary>Gets the selected symbol name for a validated NativeExport row.</summary>
    /// <param name="row">The one-based NativeExport row number.</param>
    /// <returns>The exact declaration or explicit entry name.</returns>
    public string GetEntryName(int row)
    {
        _resourceLedger?.ConsumeWork();
        var export = GetRow(row);
        if (export.EntryNameStringRow != 0)
        {
            return _strings.DecodeName(export.EntryNameStringRow);
        }

        return export.OwnerKind switch
        {
            MethodDefOwnerKind => _methods.GetName(export.OwnerRef),
            FieldDefOwnerKind => _fields.GetName(export.OwnerRef),
            _ => throw InvalidNativeExport(),
        };
    }

    private static string ValidateMethod(int ownerRef, int nativeCallableContractRef,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmContractTableReader contracts)
    {
        if (ownerRef <= 0 || ownerRef > methods.Count
            || signatures.GetReceiverTypeSpec(methods.GetSignature(ownerRef)) != 0
            || !contracts.HasNativeCallableContract(ownerRef)
            || nativeCallableContractRef != contracts.GetNativeCallableContractRow(ownerRef))
        {
            throw InvalidNativeExport();
        }

        return methods.GetName(ownerRef);
    }

    private static string ValidateField(int ownerRef,
        int nativeCallableContractRef, TrRmFieldDefTableReader fields,
        TrRmTypeDefTableReader typeDefs, TrRmImportTableReader imports,
        TrRmModuleTableReader modules, TrRmTypeSpecTableReader typeSpecs,
        TrRmContractTableReader contracts, TrRmLayoutTableReader layouts,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(3);
        if (ownerRef <= 0 || ownerRef > fields.Count
            || nativeCallableContractRef != 0
            || (fields.GetFlags(ownerRef) & 0x08) == 0
            || !TrRmNativeImportTableReader.IsNativeAbiFieldType(typeSpecs,
                fields.GetTypeSpecRef(ownerRef),
                fields.GetOwnerTypeDef(ownerRef), typeDefs, imports,
                modules, contracts, layouts, resourceLedger))
        {
            throw InvalidNativeExport();
        }

        return fields.GetName(ownerRef);
    }

    private ReadOnlySpan<byte> GetRowBytes(int row)
    {
        _resourceLedger?.ConsumeWork(2);
        if (row <= 0 || row > _recordCount)
        {
            throw InvalidNativeExport();
        }

        return _rows.Slice((row - 1) * 16, 16);
    }

    private uint Read(ReadOnlySpan<byte> bytes, int offset)
    {
        _resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidNativeExport()
        => new("Invalid standard TRRM NativeExport table.");
}
