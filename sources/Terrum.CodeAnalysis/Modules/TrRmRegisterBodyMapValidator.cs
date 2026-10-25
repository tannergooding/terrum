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
        TrRmGenericParamTableReader genericParams,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmFieldDefTableReader fields, TrRmFieldPathTableReader fieldPaths,
        TrRmConstantSourceTableReader constantSources, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs)
    {
        var visitedTypeSpecRows = new HashSet<int>();
        ValidateHomeMap(body, methodDefRow, methods, signatures);
        ValidateTypeMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, imports, moduleRefs, visitedTypeSpecRows);
        ValidateMethodMap(body, ownerModule, methods, signatures, parameters,
            types, typeSpecs, genericParams, imports, moduleRefs, visitedTypeSpecRows);
        ValidateFieldMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, fields, fieldPaths, imports, moduleRefs, visitedTypeSpecRows);
        ValidateConstantMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, imports, moduleRefs, constantSources, visitedTypeSpecRows);
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
        TrRmGenericParamTableReader genericParams,
        TrRmMethodDefTableReader methods, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs, HashSet<int> visitedTypeSpecRows)
    {
        for (var index = 0; index < body.TypeCount; index++)
        {
            var token = ReadUInt32(body.TypeMap, index * sizeof(uint));
            var row = (int)(token & 0x00FF_FFFF);
            switch (token >> 24)
            {
                case 0x11:
                    ValidateTypeSpecModuleContext((uint)row, ownerModule,
                        types, typeSpecs, genericParams, methods, imports,
                        moduleRefs, visitedTypeSpecRows);
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
                    throw InvalidMap();
                case 0xF2:
                case 0xF3:
                    ValidateTypeSpecModuleContext(token, ownerModule,
                        types, typeSpecs, genericParams, methods, imports,
                        moduleRefs, visitedTypeSpecRows);
                    break;
                default:
                    throw InvalidMap();
            }
        }
    }

    private static void ValidateMethodMap(TrRmRegisterBodyReader body, int ownerModule,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports, TrRmModuleRefTableReader moduleRefs,
        HashSet<int> visitedTypeSpecRows)
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
                    ValidateMethodSignatureTypeContexts(row, ownerModule,
                        methods, signatures, parameters, types, typeSpecs,
                        genericParams, imports, moduleRefs, visitedTypeSpecRows);
                    break;
                case 0x1C:
                    ValidateImport(row, 0x16, ownerModule, imports, moduleRefs);
                    var expectedMethod = imports.GetExpectedDefinitionForImportRow(row);
                    ValidateMethodSignatureTypeContexts(
                        (int)(expectedMethod & 0x00FF_FFFF), ownerModule,
                        methods, signatures, parameters, types, typeSpecs,
                        genericParams, imports, moduleRefs, visitedTypeSpecRows);
                    break;
                default:
                    throw InvalidMap();
            }
        }
    }

    private static void ValidateFieldMap(TrRmRegisterBodyReader body, int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, TrRmMethodDefTableReader methods,
        TrRmFieldDefTableReader fields, TrRmFieldPathTableReader fieldPaths,
        TrRmImportTableReader imports, TrRmModuleRefTableReader moduleRefs,
        HashSet<int> visitedTypeSpecRows)
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
                    ValidateTypeSpecModuleContext(fields.GetTypeSpecRef(row),
                        ownerModule, types, typeSpecs, genericParams, methods,
                        imports, moduleRefs, visitedTypeSpecRows);
                    break;
                case 0x1C:
                    ValidateImport(row, 0x14, ownerModule, imports, moduleRefs);
                    var expectedField = imports.GetExpectedDefinitionForImportRow(row);
                    var expectedFieldRow = (int)(expectedField & 0x00FF_FFFF);
                    ValidateTypeSpecModuleContext(fields.GetTypeSpecRef(expectedFieldRow),
                        ownerModule, types, typeSpecs, genericParams, methods, imports,
                        moduleRefs, visitedTypeSpecRows);
                    break;
                case 0x1D:
                    if (row <= 0 || row > fieldPaths.Count)
                    {
                        throw InvalidMap();
                    }
                    ValidateFieldPathReferences(row, ownerModule, types, typeSpecs,
                        genericParams, methods, fields, fieldPaths, imports,
                        moduleRefs, visitedTypeSpecRows);
                    break;
                default:
                    throw InvalidMap();
            }
        }
    }

    private static void ValidateFieldPathReferences(int row, int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, TrRmMethodDefTableReader methods,
        TrRmFieldDefTableReader fields, TrRmFieldPathTableReader fieldPaths,
        TrRmImportTableReader imports, TrRmModuleRefTableReader moduleRefs,
        HashSet<int> visitedTypeSpecRows)
    {
        ValidateTypeSpecModuleContext(fieldPaths.GetRootTypeSpecRef(row), ownerModule,
            types, typeSpecs, genericParams, methods, imports,
            moduleRefs, visitedTypeSpecRows);
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
                ValidateTypeSpecModuleContext(fields.GetTypeSpecRef(fieldRow),
                    ownerModule, types, typeSpecs, genericParams, methods,
                    imports, moduleRefs, visitedTypeSpecRows);
            }
            else
            {
                ValidateImport(fieldRow, 0x14, ownerModule, imports, moduleRefs);
                var expectedField = imports.GetExpectedDefinitionForImportRow(fieldRow);
                var expectedFieldRow = (int)(expectedField & 0x00FF_FFFF);
                ValidateTypeSpecModuleContext(fields.GetTypeSpecRef(expectedFieldRow),
                    ownerModule, types, typeSpecs, genericParams, methods, imports,
                    moduleRefs, visitedTypeSpecRows);
            }
        }
    }

    private static void ValidateConstantMap(TrRmRegisterBodyReader body, int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, TrRmMethodDefTableReader methods,
        TrRmImportTableReader imports, TrRmModuleRefTableReader moduleRefs,
        TrRmConstantSourceTableReader constantSources,
        HashSet<int> visitedTypeSpecRows)
    {
        for (var index = 0; index < body.ConstantCount; index++)
        {
            var data = body.ConstantMap.Slice(index * 8, 8);
            ValidateTypeSpecModuleContext(ReadUInt32(data, 0), ownerModule,
                types, typeSpecs, genericParams, methods, imports,
                moduleRefs, visitedTypeSpecRows);
            var sourceRow = ReadUInt32(data, 4);
            if (sourceRow == 0 || sourceRow > (uint)constantSources.Count)
            {
                throw InvalidMap();
            }
        }
    }

    private static void ValidateMethodSignatureTypeContexts(int methodRow,
        int ownerModule, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs, HashSet<int> visitedTypeSpecRows)
    {
        var signature = methods.GetSignature(methodRow);
        ValidateTypeSpecModuleContext(signatures.GetResultTypeSpec(signature),
            ownerModule, types, typeSpecs, genericParams, methods,
            imports, moduleRefs, visitedTypeSpecRows);
        var receiver = signatures.GetReceiverTypeSpec(signature);
        if (receiver != 0)
        {
            ValidateTypeSpecModuleContext(receiver, ownerModule,
                types, typeSpecs, genericParams, methods,
                imports, moduleRefs, visitedTypeSpecRows);
        }

        var (firstParameter, parameterCount) = signatures.GetParamRange(signature);
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            ValidateTypeSpecModuleContext(parameters.GetTypeSpec(firstParameter + ordinal),
                ownerModule, types, typeSpecs, genericParams, methods,
                imports, moduleRefs, visitedTypeSpecRows);
        }
    }

    private static void ValidateTypeSpecModuleContext(uint typeSpecRef,
        int ownerModule, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmMethodDefTableReader methods, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs, HashSet<int> visitedTypeSpecRows)
    {
        var typeSpec = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (!typeSpec.IsRow)
        {
            return;
        }

        var pendingRows = new Stack<int>();
        pendingRows.Push(typeSpec.Row);
        while (pendingRows.TryPop(out var row))
        {
            if (!visitedTypeSpecRows.Add(row))
            {
                continue;
            }

            var data = typeSpecs.GetRow(row);
            switch (data[0])
            {
                case 0x02:
                    var typeDefRow = checked((int)ReadUInt32(data, 2));
                    if (typeDefRow <= 0 || typeDefRow > types.Count
                        || types.GetOwnerModule(typeDefRow) != ownerModule)
                    {
                        throw InvalidMap();
                    }
                    break;
                case 0x03:
                    ValidateImport(checked((int)ReadUInt32(data, 2)),
                        0x13, ownerModule, imports, moduleRefs);
                    break;
                case 0x04:
                    ValidateGenericParamModuleContext(
                        checked((int)ReadUInt32(data, 2)), ownerModule,
                        types, genericParams, methods);
                    break;
                case 0x05:
                    AddConstructedTypeReferences(row, data, types,
                        typeSpecs, genericParams, imports, pendingRows);
                    break;
                case >= 0x06 and <= 0x0B:
                    AddNestedTypeSpecReference(ReadUInt32(data, 2), false,
                        typeSpecs, pendingRows);
                    break;
                case 0x0C:
                    AddNestedTypeSpecReference(ReadUInt32(data, 3),
                        data[2] == 0x8A, typeSpecs, pendingRows);
                    break;
                case 0x0E:
                    AddNestedTypeSpecReference(ReadUInt32(data, 2), false,
                        typeSpecs, pendingRows);
                    break;
                case 0x01:
                case 0x0D:
                case 0x0F:
                    break;
                default:
                    throw InvalidMap();
            }
        }
    }

    private static void AddConstructedTypeReferences(int row,
        ReadOnlySpan<byte> data, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports, Stack<int> pendingRows)
    {
        var genericTypeSpecRow = checked((int)ReadUInt32(data, 2));
        var genericTypeSpec = typeSpecs.GetRow(genericTypeSpecRow);
        var definition = genericTypeSpec[0] switch
        {
            0x02 => 0x1300_0000u | ReadUInt32(genericTypeSpec, 2),
            0x03 => imports.GetExpectedDefinitionForImportRow(
                checked((int)ReadUInt32(genericTypeSpec, 2))),
            _ => throw InvalidMap(),
        };
        var typeDefRow = (int)(definition & 0x00FF_FFFF);
        if ((definition >> 24) != 0x13 || typeDefRow <= 0 || typeDefRow > types.Count)
        {
            throw InvalidMap();
        }

        var (formalFirst, formalCount) = types.GetGenericRange(typeDefRow);
        var argumentCount = typeSpecs.GetConstructedArgumentCount(row);
        if (argumentCount != formalCount)
        {
            throw InvalidMap();
        }

        for (var ordinal = 0; ordinal < argumentCount; ordinal++)
        {
            var formalRow = formalFirst + ordinal;
            if (genericParams.GetOwnerDefId(formalRow) != definition
                || genericParams.GetOrdinal(formalRow) != (uint)ordinal)
            {
                throw InvalidMap();
            }

            switch (genericParams.GetFormalKind(formalRow))
            {
                case 1:
                    var argument = typeSpecs.GetTypeSpecRef(
                        typeSpecs.GetConstructedArgument(row, ordinal));
                    if (argument.IsRow)
                    {
                        pendingRows.Push(argument.Row);
                    }
                    break;
                case 2:
                    break;
                default:
                    throw InvalidMap();
            }
        }

        pendingRows.Push(genericTypeSpecRow);
    }

    private static void ValidateGenericParamModuleContext(int formalRow,
        int ownerModule, TrRmTypeDefTableReader types,
        TrRmGenericParamTableReader genericParams, TrRmMethodDefTableReader methods)
    {
        var owner = genericParams.GetOwnerDefId(formalRow);
        var ownerRow = (int)(owner & 0x00FF_FFFF);
        var module = (owner >> 24) switch
        {
            0x13 when ownerRow > 0 && ownerRow <= types.Count
                => types.GetOwnerModule(ownerRow),
            0x16 when ownerRow > 0 && ownerRow <= methods.Count
                => methods.GetOwnerModule(ownerRow),
            _ => throw InvalidMap(),
        };
        if (module != ownerModule)
        {
            throw InvalidMap();
        }
    }

    private static void AddNestedTypeSpecReference(uint value,
        bool allowInitQualifier, TrRmTypeSpecTableReader typeSpecs,
        Stack<int> pendingRows)
    {
        var reference = typeSpecs.GetTypeSpecRef(value,
            allowInitQualifier: allowInitQualifier);
        if (reference.IsRow)
        {
            pendingRows.Push(reference.Row);
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
