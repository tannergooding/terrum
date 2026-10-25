// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmRegisterBodyMapValidator
{
    internal static void Validate(TrRmRegisterBodyReader body, int methodDefRow,
        int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmFieldDefTableReader fields, TrRmFieldPathTableReader fieldPaths,
        TrRmConstantSourceTableReader constantSources, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs)
    {
        ValidateHomeMap(body, methodDefRow, methods, signatures);
        ValidateTypeMap(body, ownerModule, types, typeSpecs, imports, moduleRefs);
        ValidateMethodMap(body, ownerModule, methods, imports, moduleRefs);
        ValidateFieldMap(body, ownerModule, types, fields, fieldPaths, imports, moduleRefs);
        ValidateConstantMap(body, typeSpecs, constantSources);
    }

    private static void ValidateHomeMap(TrRmRegisterBodyReader body, int methodDefRow,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures)
    {
        var signature = methods.GetSignature(methodDefRow);
        var parameterCount = signatures.GetParamRange(signature).Count;
        var hasReceiver = signatures.GetReceiverTypeSpec(signature) != 0;
        var formalOrdinals = new HashSet<int>();
        var hasReceiverHome = false;
        for (var home = 0; home < body.HomeCount; home++)
        {
            var data = body.HomeMap.Slice(home * 8, 8);
            var role = data[0];
            var flags = data[1];
            var typeMapIndex = ReadUInt16(data, 2);
            var formalOrdinal = ReadUInt16(data, 4);
            if (role is < 1 or > 3 || typeMapIndex >= body.TypeCount
                || ReadUInt16(data, 6) != 0)
            {
                throw InvalidMap();
            }

            switch (role)
            {
                case 1:
                    if (flags != 0 || formalOrdinal != 0 || !hasReceiver || hasReceiverHome)
                    {
                        throw InvalidMap();
                    }
                    hasReceiverHome = true;
                    break;
                case 2:
                    if (flags != 0 || formalOrdinal >= parameterCount
                        || !formalOrdinals.Add(formalOrdinal))
                    {
                        throw InvalidMap();
                    }
                    break;
                case 3:
                    if (flags > 1 || formalOrdinal != 0)
                    {
                        throw InvalidMap();
                    }
                    break;
            }
        }
    }

    private static void ValidateTypeMap(TrRmRegisterBodyReader body, int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmImportTableReader imports, TrRmModuleRefTableReader moduleRefs)
    {
        for (var index = 0; index < body.TypeCount; index++)
        {
            var token = ReadUInt32(body.TypeMap, index * sizeof(uint));
            var row = (int)(token & 0x00FF_FFFF);
            switch (token >> 24)
            {
                case 0x11:
                    _ = typeSpecs.GetTypeSpecRef((uint)row);
                    break;
                case 0x13:
                    if (row <= 0 || row > types.Count
                        || types.GetOwnerModule(row) != ownerModule)
                    {
                        throw InvalidMap();
                    }
                    break;
                case 0x1C:
                    ValidateImport(row, 0x13, ownerModule, imports, moduleRefs);
                    break;
                case 0xF0:
                case 0xF1:
                    ValidatePseudoType(token, body.TypeCount);
                    break;
                case 0xF2:
                case 0xF3:
                    _ = typeSpecs.GetTypeSpecRef(token);
                    break;
                default:
                    throw InvalidMap();
            }
        }

        ValidatePseudoTypeAcyclic(body);
    }

    private static void ValidateMethodMap(TrRmRegisterBodyReader body, int ownerModule,
        TrRmMethodDefTableReader methods, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs)
    {
        for (var index = 0; index < body.MethodCount; index++)
        {
            var token = ReadUInt32(body.MethodMap, index * sizeof(uint));
            var row = (int)(token & 0x00FF_FFFF);
            switch (token >> 24)
            {
                case 0x16:
                    if (row <= 0 || row > methods.Count
                        || methods.GetOwnerModule(row) != ownerModule)
                    {
                        throw InvalidMap();
                    }
                    break;
                case 0x1C:
                    ValidateImport(row, 0x16, ownerModule, imports, moduleRefs);
                    break;
                default:
                    throw InvalidMap();
            }
        }
    }

    private static void ValidateFieldMap(TrRmRegisterBodyReader body, int ownerModule,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmFieldPathTableReader fieldPaths, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs)
    {
        for (var index = 0; index < body.FieldCount; index++)
        {
            var token = ReadUInt32(body.FieldMap, index * sizeof(uint));
            var row = (int)(token & 0x00FF_FFFF);
            switch (token >> 24)
            {
                case 0x14:
                    if (row <= 0 || row > fields.Count
                        || types.GetOwnerModule(fields.GetOwnerTypeDef(row)) != ownerModule)
                    {
                        throw InvalidMap();
                    }
                    break;
                case 0x1C:
                    ValidateImport(row, 0x14, ownerModule, imports, moduleRefs);
                    break;
                case 0x1D:
                    if (row <= 0 || row > fieldPaths.Count)
                    {
                        throw InvalidMap();
                    }
                    ValidateFieldPathReferences(row, ownerModule, types, fields,
                        fieldPaths, imports, moduleRefs);
                    break;
                default:
                    throw InvalidMap();
            }
        }
    }

    private static void ValidateFieldPathReferences(int row, int ownerModule,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmFieldPathTableReader fieldPaths, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs)
    {
        var fieldCount = fieldPaths.GetFieldCount(row);
        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            var fieldReference = fieldPaths.GetFieldReference(row, ordinal);
            var fieldRow = (int)(fieldReference & 0x00FF_FFFF);
            if ((fieldReference >> 24) == 0x14)
            {
                if (types.GetOwnerModule(fields.GetOwnerTypeDef(fieldRow)) != ownerModule)
                {
                    throw InvalidMap();
                }
            }
            else
            {
                ValidateImport(fieldRow, 0x14, ownerModule, imports, moduleRefs);
            }
        }
    }

    private static void ValidateConstantMap(TrRmRegisterBodyReader body,
        TrRmTypeSpecTableReader typeSpecs, TrRmConstantSourceTableReader constantSources)
    {
        for (var index = 0; index < body.ConstantCount; index++)
        {
            var data = body.ConstantMap.Slice(index * 8, 8);
            _ = typeSpecs.GetTypeSpecRef(ReadUInt32(data, 0));
            var sourceRow = ReadUInt32(data, 4);
            if (sourceRow == 0 || sourceRow > (uint)constantSources.Count)
            {
                throw InvalidMap();
            }
        }
    }

    private static void ValidatePseudoType(uint token, int typeCount)
    {
        var baseTypeMapIndex = (byte)token;
        var genericFamily = (byte)(token >> 8);
        var wrapperModifier = (byte)(token >> 16);
        var wrapperCode = wrapperModifier & 0x07;
        if (baseTypeMapIndex >= typeCount
            || genericFamily is not (<= 5 or 8 or 9 or 10)
            || wrapperCode > 5)
        {
            throw InvalidMap();
        }
    }

    private static void ValidatePseudoTypeAcyclic(TrRmRegisterBodyReader body)
    {
        var states = new byte[body.TypeCount];
        var path = new int[body.TypeCount];
        for (var start = 0; start < body.TypeCount; start++)
        {
            if (states[start] != 0)
            {
                continue;
            }

            var pathCount = 0;
            var current = start;
            while (current >= 0 && states[current] == 0)
            {
                states[current] = 1;
                path[pathCount++] = current;
                var token = ReadUInt32(body.TypeMap, current * sizeof(uint));
                current = (token >> 24) is 0xF0 or 0xF1
                    ? (byte)token : -1;
            }

            if (current >= 0 && states[current] == 1)
            {
                throw InvalidMap();
            }

            for (var index = 0; index < pathCount; index++)
            {
                states[path[index]] = 2;
            }
        }
    }

    private static void ValidateImport(int importRow, byte expectedKind, int ownerModule,
        TrRmImportTableReader imports, TrRmModuleRefTableReader moduleRefs)
    {
        if (importRow <= 0 || importRow > imports.Count
            || (imports.GetExpectedDefinitionForImportRow(importRow) >> 24) != expectedKind
            || moduleRefs.GetOwnerModule(imports.GetModuleRefForImportRow(importRow)) != ownerModule)
        {
            throw InvalidMap();
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidMap()
        => new("Invalid TRRM register Body map.");
}
