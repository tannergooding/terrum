// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmRegisterBodyMapValidator
{
    private const uint UnitTypeSpecRef = 0xF200_0000;

    internal static void Validate(TrRmRegisterBodyReader body, int methodDefRow,
        int ownerModule, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmFieldDefTableReader fields, TrRmFieldPathTableReader fieldPaths,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs,
        TrRmContractTableReader contracts, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs)
    {
        var visitedTypeSpecRows = new HashSet<int>();
        ValidateHomeMap(body, methodDefRow, types, methods, signatures, parameters,
            typeSpecs, genericParams, imports);
        ValidateTypeMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, imports, moduleRefs, visitedTypeSpecRows);
        ValidateMethodMap(body, ownerModule, methods, signatures, parameters,
            types, typeSpecs, genericParams, imports, moduleRefs, visitedTypeSpecRows);
        ValidateFieldMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, fields, fieldPaths, imports, moduleRefs, visitedTypeSpecRows);
        ValidateConstantMap(body, ownerModule, features, modules, types, typeSpecs,
            genericParams, methods, imports, moduleRefs, constantSources,
            frozenValueNodes, blobs, visitedTypeSpecRows);
        ValidateCallSiteEffects(body, typeSpecs, methods, signatures,
            contracts, imports);
    }

    private static void ValidateHomeMap(TrRmRegisterBodyReader body, int methodDefRow,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports)
    {
        var signature = methods.GetSignature(methodDefRow);
        var (firstParameter, parameterCount) = signatures.GetParamRange(signature);
        var receiverType = signatures.GetReceiverTypeSpec(signature);
        var hasReceiver = receiverType != 0;
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
                    if (flags != 0 || formalOrdinal != 0 || !hasReceiver || hasReceiverHome
                        || !MatchesTypeMapEntry(body, typeMapIndex, receiverType,
                            types, typeSpecs, genericParams, imports))
                    {
                        throw InvalidMap();
                    }
                    hasReceiverHome = true;
                    break;
                case 2:
                    if (flags != 0 || formalOrdinal >= parameterCount
                        || !formalOrdinals.Add(formalOrdinal)
                        || !MatchesTypeMapEntry(body, typeMapIndex,
                            parameters.GetTypeSpec(firstParameter + formalOrdinal),
                            types, typeSpecs, genericParams, imports))
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

    private static bool MatchesTypeMapEntry(TrRmRegisterBodyReader body,
        int typeMapIndex, uint expectedType, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports)
    {
        var token = ReadUInt32(body.TypeMap, typeMapIndex * sizeof(uint));
        var kind = (byte)(token >> 24);
        if (kind is 0x11 or 0xF2 or 0xF3)
        {
            var typeSpecRef = kind == 0x11 ? token & 0x00FF_FFFF : token;
            var mapType = typeSpecs.GetTypeSpecRef(typeSpecRef);
            var formalType = typeSpecs.GetTypeSpecRef(expectedType);
            if (mapType.Form == 0x0C && formalType.Form == 0x01)
            {
                return MatchesInlineFamilyTypeSpec(expectedType, typeSpecRef, typeSpecs);
            }
            if (formalType.Form == 0x0C && mapType.Form == 0x01)
            {
                return MatchesInlineFamilyTypeSpec(typeSpecRef, expectedType, typeSpecs);
            }

            return AreEquivalentSelectedTypeSpecRefs(typeSpecRef, expectedType,
                types, typeSpecs, genericParams, imports);
        }

        if (kind is not (0x13 or 0x1C))
        {
            return false;
        }

        var expected = typeSpecs.GetTypeSpecRef(expectedType);
        if (!expected.IsRow || expected.Qualifiers != 0)
        {
            return false;
        }

        var expectedRow = typeSpecs.GetRow(expected.Row);
        return kind switch
        {
            0x13 => expected.Form == 0x02
                && ReadUInt32(expectedRow, 2) == (token & 0x00FF_FFFF),
            0x1C => expected.Form == 0x03
                && ReadUInt32(expectedRow, 2) == (token & 0x00FF_FFFF),
            _ => false,
        };
    }

    private static bool AreEquivalentSelectedTypeSpecRefs(uint left, uint right,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, TrRmImportTableReader imports)
    {
        var pending = new Stack<(uint Left, uint Right)>();
        var visited = new HashSet<(uint Left, uint Right)>();
        pending.Push((left, right));
        while (pending.TryPop(out var pair))
        {
            if (pair.Left == pair.Right || !visited.Add(pair))
            {
                continue;
            }

            var leftReference = typeSpecs.GetTypeSpecRef(pair.Left);
            var rightReference = typeSpecs.GetTypeSpecRef(pair.Right);
            if (leftReference.Qualifiers != rightReference.Qualifiers)
            {
                return false;
            }
            if (leftReference.Form != rightReference.Form)
            {
                if (leftReference.Form == 0x0C && rightReference.Form == 0x01
                    && rightReference.TypeCode >= 0x80)
                {
                    if (!MatchesInlineFamilyTypeSpec(pair.Right, pair.Left, typeSpecs))
                    {
                        return false;
                    }
                    continue;
                }
                if (leftReference.Form == 0x01 && leftReference.TypeCode >= 0x80
                    && rightReference.Form == 0x0C)
                {
                    if (!MatchesInlineFamilyTypeSpec(pair.Left, pair.Right, typeSpecs))
                    {
                        return false;
                    }
                    continue;
                }
                return false;
            }

            var leftRow = leftReference.IsRow
                ? typeSpecs.GetRow(leftReference.Row) : default;
            var rightRow = rightReference.IsRow
                ? typeSpecs.GetRow(rightReference.Row) : default;
            switch (leftReference.Form)
            {
                case 0x01:
                    if (leftReference.TypeCode != rightReference.TypeCode
                        || leftReference.ArgumentCode != rightReference.ArgumentCode)
                    {
                        return false;
                    }
                    break;

                case 0x02:
                case 0x03:
                case 0x04:
                    if (!leftReference.IsRow || !rightReference.IsRow
                        || ReadUInt32(leftRow, 2) != ReadUInt32(rightRow, 2))
                    {
                        return false;
                    }
                    break;

                case 0x05:
                    if (!leftReference.IsRow || !rightReference.IsRow)
                    {
                        return false;
                    }
                    var leftDefinitionRef = ReadUInt32(leftRow, 2);
                    var rightDefinitionRef = ReadUInt32(rightRow, 2);
                    if (!typeSpecs.AreEquivalentTypeSpecRefs(
                        leftDefinitionRef, rightDefinitionRef))
                    {
                        return false;
                    }

                    var leftDefinition = typeSpecs.GetNominalTypeDefRow(
                        leftDefinitionRef, types, imports);
                    var rightDefinition = typeSpecs.GetNominalTypeDefRow(
                        rightDefinitionRef, types, imports);
                    if (leftDefinition != rightDefinition)
                    {
                        return false;
                    }

                    var (firstFormal, formalCount) = types.GetGenericRange(leftDefinition);
                    var leftArgumentCount = typeSpecs.GetConstructedArgumentCount(leftReference.Row);
                    var rightArgumentCount = typeSpecs.GetConstructedArgumentCount(rightReference.Row);
                    if (leftArgumentCount != formalCount || rightArgumentCount != formalCount)
                    {
                        return false;
                    }
                    for (var ordinal = 0; ordinal < formalCount; ordinal++)
                    {
                        var formalKind = genericParams.GetFormalKind(firstFormal + ordinal);
                        var leftArgument = typeSpecs.GetConstructedArgument(
                            leftReference.Row, ordinal);
                        var rightArgument = typeSpecs.GetConstructedArgument(
                            rightReference.Row, ordinal);
                        if (formalKind == 1)
                        {
                            pending.Push((leftArgument, rightArgument));
                        }
                        else if (formalKind != 2 || leftArgument != rightArgument)
                        {
                            return false;
                        }
                    }
                    break;

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                    if (!leftReference.IsRow || !rightReference.IsRow)
                    {
                        return false;
                    }
                    pending.Push((ReadUInt32(leftRow, 2), ReadUInt32(rightRow, 2)));
                    break;

                case 0x0C:
                    if (!leftReference.IsRow || !rightReference.IsRow
                        || leftRow[2] != rightRow[2])
                    {
                        return false;
                    }
                    pending.Push((ReadUInt32(leftRow, 3), ReadUInt32(rightRow, 3)));
                    break;

                default:
                    if (!typeSpecs.AreEquivalentTypeSpecRefs(pair.Left, pair.Right))
                    {
                        return false;
                    }
                    break;
            }
        }

        return true;
    }

    private static bool MatchesInlineFamilyTypeSpec(uint inlineTypeSpecRef,
        uint indexedTypeSpecRef, TrRmTypeSpecTableReader typeSpecs)
    {
        var inlineType = typeSpecs.GetTypeSpecRef(inlineTypeSpecRef);
        var indexedType = typeSpecs.GetTypeSpecRef(indexedTypeSpecRef);
        if (inlineType.Form != 0x01 || inlineType.TypeCode < 0x80
            || !indexedType.IsRow || indexedType.Form != 0x0C
            || inlineType.Qualifiers != indexedType.Qualifiers)
        {
            return false;
        }

        var indexedRow = typeSpecs.GetRow(indexedType.Row);
        if (indexedRow[2] != inlineType.TypeCode)
        {
            return false;
        }

        var argument = typeSpecs.GetTypeSpecRef(ReadUInt32(indexedRow, 3),
            allowInitQualifier: inlineType.TypeCode == 0x8A);
        return argument.Form == 0x01 && argument.Qualifiers == 0
            && argument.TypeCode < 0x80
            && argument.TypeCode == inlineType.ArgumentCode;
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
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, TrRmMethodDefTableReader methods,
        TrRmImportTableReader imports, TrRmModuleRefTableReader moduleRefs,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs,
        HashSet<int> visitedTypeSpecRows)
    {
        if (body.ConstantCount != 0
            && !TrRmFeatureManifestValidator.Requires(features, modules,
                ownerModule, TrRmFeatureTableReader.TypedFrozenValuesFeatureId))
        {
            throw new NotSupportedException(
                "A nonempty ConstantMap requires TypedFrozenValues revision 1.");
        }

        for (var index = 0; index < body.ConstantCount; index++)
        {
            var data = body.ConstantMap.Slice(index * 8, 8);
            var mapTypeSpecRef = ReadUInt32(data, 0);
            ValidateTypeSpecModuleContext(mapTypeSpecRef, ownerModule,
                types, typeSpecs, genericParams, methods, imports,
                moduleRefs, visitedTypeSpecRows);
            var sourceRow = ReadUInt32(data, 4);
            if (sourceRow == 0 || sourceRow > (uint)constantSources.Count)
            {
                throw InvalidMap();
            }

            if (constantSources.GetKind((int)sourceRow) != 1)
            {
                throw new NotSupportedException(
                    "Register Body ConstantMap currently supports only frozen scalar values.");
            }

            var nodeRow = constantSources.GetPayloadReference((int)sourceRow);
            if (frozenValueNodes.GetKind(checked((int)nodeRow)) != 1)
            {
                throw new NotSupportedException(
                    "Register Body ConstantMap currently supports only frozen scalar values.");
            }

            var mapType = typeSpecs.GetTypeSpecRef(mapTypeSpecRef);
            var nodeType = typeSpecs.GetTypeSpecRef(
                frozenValueNodes.GetTypeSpecRef(checked((int)nodeRow)));
            if (mapType.Form != 0x01 || mapType.Qualifiers != 0
                || nodeType.Form != 0x01 || nodeType.Qualifiers != 0
                || !TrRmFrozenValueNodeTableReader.IsRegisteredScalarTypeCode(mapType.TypeCode)
                || !TrRmFrozenValueNodeTableReader.IsRegisteredScalarTypeCode(nodeType.TypeCode))
            {
                throw new NotSupportedException(
                    "Register Body ConstantMap requires an unqualified registered scalar type.");
            }
            if (mapType.TypeCode != nodeType.TypeCode
                || mapType.ArgumentCode != nodeType.ArgumentCode)
            {
                throw InvalidMap();
            }

            frozenValueNodes.ValidateScalarValue(checked((int)nodeRow), blobs);
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

    private static void ValidateCallSiteEffects(TrRmRegisterBodyReader body,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmContractTableReader contracts,
        TrRmImportTableReader imports)
    {
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var position = 0;
            while (position < code.Length)
            {
                var opcode = code[position];
                var instructionLength = TrRmRegisterBodyReader.InstructionLength(opcode);
                var instruction = code.Slice(position, instructionLength);
                switch (opcode)
                {
                    case 0x80:
                    case 0x82:
                        RejectNonReturningMethodMapTarget(
                            body, instruction[1] >> 4, methods, contracts, imports);
                        break;
                    case 0x90:
                        ValidateCallSiteEffects(body, instruction[1], blockIndex,
                            typeSpecs, methods, signatures, contracts, imports);
                        break;
                    case 0xE0:
                        ValidateCallSiteEffects(body, ReadUInt16(instruction, 2),
                            blockIndex, typeSpecs, methods, signatures,
                            contracts, imports);
                        break;
                    case 0xE1:
                        var methodMapIndex = ReadUInt16(instruction, 2);
                        var targetMethod = GetMethodMapTarget(
                            body, methodMapIndex, methods, imports);
                        var signature = methods.GetSignature(targetMethod);
                        var resultType = signatures.GetResultTypeSpec(signature);
                        if ((instruction[1] >> 4) == 15
                            && !typeSpecs.AreEquivalentTypeSpecRefs(
                                resultType, UnitTypeSpecRef))
                        {
                            throw InvalidMap();
                        }
                        RejectNonReturningMethodMapTarget(
                            body, methodMapIndex, methods, contracts, imports);
                        break;
                }

                position += instructionLength;
            }
        }
    }

    private static void ValidateCallSiteEffects(TrRmRegisterBodyReader body,
        int recordIndex, int blockIndex, TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmContractTableReader contracts, TrRmImportTableReader imports)
    {
        var payload = body.GetRecord(recordIndex)[4..];
        if (payload[0] != 1)
        {
            return;
        }

        var targetMethod = GetMethodMapTarget(
            body, ReadUInt16(payload, 4), methods, imports);
        var signature = methods.GetSignature(targetMethod);
        var resultType = signatures.GetResultTypeSpec(signature);
        var unitResult = typeSpecs.AreEquivalentTypeSpecRefs(
            resultType, UnitTypeSpecRef);
        var doesNotReturn = (contracts.GetEffectsMask(targetMethod) & 2) != 0;
        if (doesNotReturn
            ? payload[1] != 0 || !body.IsCallSiteAtBlockEnd(blockIndex, recordIndex)
            : unitResult ? payload[1] != 0 : payload[1] == 0)
        {
            throw InvalidMap();
        }
    }

    private static void RejectNonReturningMethodMapTarget(
        TrRmRegisterBodyReader body, int methodMapIndex,
        TrRmMethodDefTableReader methods, TrRmContractTableReader contracts,
        TrRmImportTableReader imports)
    {
        var targetMethod = GetMethodMapTarget(
            body, methodMapIndex, methods, imports);
        if ((contracts.GetEffectsMask(targetMethod) & 2) != 0)
        {
            throw InvalidMap();
        }
    }

    private static int GetMethodMapTarget(TrRmRegisterBodyReader body,
        int methodMapIndex, TrRmMethodDefTableReader methods,
        TrRmImportTableReader imports)
    {
        if ((uint)methodMapIndex >= (uint)body.MethodCount)
        {
            throw InvalidMap();
        }

        var token = ReadUInt32(body.MethodMap, methodMapIndex * sizeof(uint));
        var row = checked((int)(token & 0x00FF_FFFF));
        return (byte)(token >> 24) switch
        {
            0x16 when row > 0 && row <= methods.Count => row,
            0x1C => GetImportedMethodTarget(row, methods, imports),
            _ => throw InvalidMap(),
        };
    }

    private static int GetImportedMethodTarget(int importRow,
        TrRmMethodDefTableReader methods, TrRmImportTableReader imports)
    {
        var expected = imports.GetExpectedDefinitionForImportRow(importRow);
        var targetMethod = checked((int)(expected & 0x00FF_FFFF));
        if ((expected >> 24) != 0x16
            || targetMethod <= 0 || targetMethod > methods.Count)
        {
            throw InvalidMap();
        }

        return targetMethod;
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
