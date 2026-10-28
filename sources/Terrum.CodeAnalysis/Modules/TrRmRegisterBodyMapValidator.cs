// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmRegisterBodyMapValidator
{
    private const uint UnitTypeSpecRef = 0xF200_0000;

    private readonly ref struct ConstantSourceValueContext(
        TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs)
    {
        internal TrRmFeatureTableReader Features { get; } = features;
        internal TrRmModuleTableReader Modules { get; } = modules;
        internal TrRmConstantSourceTableReader ConstantSources { get; } = constantSources;
        internal TrRmFrozenValueNodeTableReader FrozenValueNodes { get; } = frozenValueNodes;
        internal TrRmBlobTableReader Blobs { get; } = blobs;
    }

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
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors,
        TrRmNativeImportTableReader standardNativeImports,
        TrRmModuleRefTableReader moduleRefs)
    {
        var visitedTypeSpecRows = new HashSet<int>();
        var constantValues = new ConstantSourceValueContext(features, modules,
            constantSources, frozenValueNodes, blobs);
        ValidateHomeMap(body, methodDefRow, ownerModule, types, methods, signatures,
            parameters, typeSpecs, genericParams, imports, constantValues,
            nativeFunctionPointerAnchors);
        ValidateTypeMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, imports, moduleRefs, constantValues, visitedTypeSpecRows);
        ValidateMethodMap(body, ownerModule, methods, signatures, parameters,
            types, typeSpecs, genericParams, imports, moduleRefs, constantValues,
            visitedTypeSpecRows, nativeFunctionPointerAnchors);
        ValidateFieldMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, fields, fieldPaths, imports, moduleRefs, constantValues,
            visitedTypeSpecRows);
        ValidateConstantMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, imports, moduleRefs, constantValues, visitedTypeSpecRows);
        ValidateCallSiteEffects(body, typeSpecs, methods, signatures,
            parameters, contracts, imports, nativeFunctionPointerAnchors);
        ValidateCallSiteHomeTypes(body, ownerModule, types, typeSpecs,
            genericParams, methods, signatures, parameters, imports,
            constantValues, contracts, nativeFunctionPointerAnchors);
        ValidateNativeAddressRecords(body, ownerModule, features, modules,
            types, typeSpecs, genericParams, methods, signatures, parameters,
            contracts, imports, standardNativeImports,
            nativeFunctionPointerAnchors);
    }

    private static void ValidateHomeMap(TrRmRegisterBodyReader body, int methodDefRow,
        int ownerModule,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports, ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
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
                            ownerModule, types, typeSpecs, genericParams, imports,
                            constantValues, nativeFunctionPointerAnchors))
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
                            ownerModule, types, typeSpecs, genericParams, imports,
                            constantValues, nativeFunctionPointerAnchors))
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
        int typeMapIndex, uint expectedType, int ownerModule,
        TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports, ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
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
                ownerModule, types, typeSpecs, genericParams, imports, constantValues,
                nativeFunctionPointerAnchors);
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
        int ownerModule, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports, ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
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
                        else if (formalKind != 2
                            || !AreEquivalentFrozenScalarSources(leftArgument, rightArgument,
                                genericParams.GetConstantTypeSpecRef(firstFormal + ordinal),
                                ownerModule, typeSpecs, constantValues))
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

                case 0x0D:
                    if (!nativeFunctionPointerAnchors.AreEquivalentTypeSpecRefs(
                        pair.Left, pair.Right))
                    {
                        return false;
                    }
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

    private static bool AreEquivalentFrozenScalarSources(uint leftSourceRow,
        uint rightSourceRow, uint constantTypeSpecRef, int ownerModule,
        TrRmTypeSpecTableReader typeSpecs, ConstantSourceValueContext constantValues)
    {
        if (leftSourceRow == 0 || rightSourceRow == 0)
        {
            return false;
        }

        var leftRow = checked((int)leftSourceRow);
        var rightRow = checked((int)rightSourceRow);
        TrRmConstantSourceValueValidator.ValidateFrozenScalar(
            leftRow, constantTypeSpecRef, ownerModule,
            constantValues.ConstantSources, constantValues.FrozenValueNodes,
            constantValues.Blobs, typeSpecs, constantValues.Features,
            constantValues.Modules);
        TrRmConstantSourceValueValidator.ValidateFrozenScalar(
            rightRow, constantTypeSpecRef, ownerModule,
            constantValues.ConstantSources, constantValues.FrozenValueNodes,
            constantValues.Blobs, typeSpecs, constantValues.Features,
            constantValues.Modules);
        var leftNode = checked((int)constantValues.ConstantSources.GetPayloadReference(leftRow));
        var rightNode = checked((int)constantValues.ConstantSources.GetPayloadReference(rightRow));
        return constantValues.FrozenValueNodes.GetScalarPayload(leftNode,
                constantValues.Blobs)
            .SequenceEqual(constantValues.FrozenValueNodes.GetScalarPayload(rightNode,
                constantValues.Blobs));
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
        TrRmModuleRefTableReader moduleRefs,
        ConstantSourceValueContext constantValues,
        HashSet<int> visitedTypeSpecRows)
    {
        for (var index = 0; index < body.TypeCount; index++)
        {
            var token = ReadUInt32(body.TypeMap, index * sizeof(uint));
            var row = (int)(token & 0x00FF_FFFF);
            switch (token >> 24)
            {
                case 0x11:
                    var typeSpecRef = (uint)row;
                    ValidateTypeSpecModuleContext(typeSpecRef, ownerModule,
                        types, typeSpecs, genericParams, methods, imports,
                        moduleRefs, constantValues, visitedTypeSpecRows);
                    if (typeSpecs.ContainsNativeFunctionPointerValue(typeSpecRef,
                            types, genericParams, imports)
                        && !TrRmFeatureManifestValidator.Requires(
                            constantValues.Features, constantValues.Modules,
                            ownerModule,
                            TrRmFeatureTableReader.RootlessNativeValuesFeatureId))
                    {
                        throw InvalidMap();
                    }
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
                        moduleRefs, constantValues, visitedTypeSpecRows);
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
        ConstantSourceValueContext constantValues,
        HashSet<int> visitedTypeSpecRows,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        for (var index = 0; index < body.MethodCount; index++)
        {
            var token = ReadUInt32(body.MethodMap, index * sizeof(uint));
            var row = (int)(token & 0x00FF_FFFF);
            switch (token >> 24)
            {
                case 0x16:
                    if (row <= 0 || row > methods.Count
                        || nativeFunctionPointerAnchors.IsAnchorMethodDef(row)
                        || methods.GetOwnerModule(row) != ownerModule)
                    {
                        throw InvalidMap();
                    }
                    ValidateMethodSignatureTypeContexts(row, ownerModule,
                        methods, signatures, parameters, types, typeSpecs,
                        genericParams, imports, moduleRefs, constantValues,
                        visitedTypeSpecRows);
                    break;
                case 0x1C:
                    ValidateImport(row, 0x16, ownerModule, imports, moduleRefs);
                    var expectedMethod = imports.GetExpectedDefinitionForImportRow(row);
                    var expectedMethodRow = (int)(expectedMethod & 0x00FF_FFFF);
                    if (nativeFunctionPointerAnchors.IsAnchorMethodDef(expectedMethodRow))
                    {
                        throw InvalidMap();
                    }
                    ValidateMethodSignatureTypeContexts(
                        expectedMethodRow, ownerModule,
                        methods, signatures, parameters, types, typeSpecs,
                        genericParams, imports, moduleRefs, constantValues,
                        visitedTypeSpecRows);
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
        ConstantSourceValueContext constantValues,
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
                        imports, moduleRefs, constantValues, visitedTypeSpecRows);
                    break;
                case 0x1C:
                    ValidateImport(row, 0x14, ownerModule, imports, moduleRefs);
                    var expectedField = imports.GetExpectedDefinitionForImportRow(row);
                    var expectedFieldRow = (int)(expectedField & 0x00FF_FFFF);
                    ValidateTypeSpecModuleContext(fields.GetTypeSpecRef(expectedFieldRow),
                        ownerModule, types, typeSpecs, genericParams, methods, imports,
                        moduleRefs, constantValues, visitedTypeSpecRows);
                    break;
                case 0x1D:
                    if (row <= 0 || row > fieldPaths.Count)
                    {
                        throw InvalidMap();
                    }
                    ValidateFieldPathReferences(row, ownerModule, types, typeSpecs,
                        genericParams, methods, fields, fieldPaths, imports,
                        moduleRefs, constantValues, visitedTypeSpecRows);
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
        ConstantSourceValueContext constantValues,
        HashSet<int> visitedTypeSpecRows)
    {
        ValidateTypeSpecModuleContext(fieldPaths.GetRootTypeSpecRef(row), ownerModule,
            types, typeSpecs, genericParams, methods, imports,
            moduleRefs, constantValues, visitedTypeSpecRows);
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
                    imports, moduleRefs, constantValues, visitedTypeSpecRows);
            }
            else
            {
                ValidateImport(fieldRow, 0x14, ownerModule, imports, moduleRefs);
                var expectedField = imports.GetExpectedDefinitionForImportRow(fieldRow);
                var expectedFieldRow = (int)(expectedField & 0x00FF_FFFF);
                ValidateTypeSpecModuleContext(fields.GetTypeSpecRef(expectedFieldRow),
                    ownerModule, types, typeSpecs, genericParams, methods, imports,
                    moduleRefs, constantValues, visitedTypeSpecRows);
            }
        }
    }

    private static void ValidateConstantMap(TrRmRegisterBodyReader body, int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, TrRmMethodDefTableReader methods,
        TrRmImportTableReader imports, TrRmModuleRefTableReader moduleRefs,
        ConstantSourceValueContext constantValues,
        HashSet<int> visitedTypeSpecRows)
    {
        for (var index = 0; index < body.ConstantCount; index++)
        {
            var data = body.ConstantMap.Slice(index * 8, 8);
            var mapTypeSpecRef = ReadUInt32(data, 0);
            ValidateTypeSpecModuleContext(mapTypeSpecRef, ownerModule,
                types, typeSpecs, genericParams, methods, imports,
                moduleRefs, constantValues, visitedTypeSpecRows);
            var sourceRow = ReadUInt32(data, 4);
            if (sourceRow == 0
                || sourceRow > (uint)constantValues.ConstantSources.Count)
            {
                throw InvalidMap();
            }

            TrRmConstantSourceValueValidator.ValidateFrozenScalar(
                checked((int)sourceRow), mapTypeSpecRef, ownerModule,
                constantValues.ConstantSources, constantValues.FrozenValueNodes,
                constantValues.Blobs, typeSpecs, constantValues.Features,
                constantValues.Modules);
        }
    }

    private static void ValidateMethodSignatureTypeContexts(int methodRow,
        int ownerModule, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs,
        ConstantSourceValueContext constantValues,
        HashSet<int> visitedTypeSpecRows)
    {
        var signature = methods.GetSignature(methodRow);
        ValidateTypeSpecModuleContext(signatures.GetResultTypeSpec(signature),
            ownerModule, types, typeSpecs, genericParams, methods,
            imports, moduleRefs, constantValues, visitedTypeSpecRows);
        var receiver = signatures.GetReceiverTypeSpec(signature);
        if (receiver != 0)
        {
            ValidateTypeSpecModuleContext(receiver, ownerModule,
                types, typeSpecs, genericParams, methods,
                imports, moduleRefs, constantValues, visitedTypeSpecRows);
        }

        var (firstParameter, parameterCount) = signatures.GetParamRange(signature);
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            ValidateTypeSpecModuleContext(parameters.GetTypeSpec(firstParameter + ordinal),
                ownerModule, types, typeSpecs, genericParams, methods,
                imports, moduleRefs, constantValues, visitedTypeSpecRows);
        }
    }

    private static void ValidateTypeSpecModuleContext(uint typeSpecRef,
        int ownerModule, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmMethodDefTableReader methods, TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs,
        ConstantSourceValueContext constantValues,
        HashSet<int> visitedTypeSpecRows)
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
                        typeSpecs, genericParams, imports, ownerModule,
                        constantValues, pendingRows);
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
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmContractTableReader contracts, TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var registerTypeMapIndexes =
                CreateBlockEntryRegisterTypeMapIndexes(body, blockIndex);
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
                            registerTypeMapIndexes,
                            typeSpecs, methods, signatures, parameters, contracts, imports,
                            nativeFunctionPointerAnchors);
                        break;
                    case 0xE0:
                        ValidateCallSiteEffects(body, ReadUInt16(instruction, 2),
                            blockIndex, registerTypeMapIndexes,
                            typeSpecs, methods, signatures,
                            parameters, contracts, imports,
                            nativeFunctionPointerAnchors);
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

                UpdateRegisterTypeMapIndexes(instruction, registerTypeMapIndexes);
                position += instructionLength;
            }
        }
    }

    private static void ValidateCallSiteEffects(TrRmRegisterBodyReader body,
        int recordIndex, int blockIndex, int[] registerTypeMapIndexes,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmContractTableReader contracts,
        TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var payload = body.GetRecord(recordIndex)[4..];
        var targetMethod = GetCallSiteTargetMethod(body, payload,
            registerTypeMapIndexes, typeSpecs, methods, imports,
            nativeFunctionPointerAnchors);
        var signature = methods.GetSignature(targetMethod);
        var (firstParameter, parameterCount) = signatures.GetParamRange(signature);
        var inputCount = ReadUInt32(payload, 8);
        if (inputCount != (uint)parameterCount)
        {
            throw InvalidMap();
        }

        var inputOffset = 16;
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var selectorKind = payload[inputOffset];
            var isUnitInput = selectorKind == 0;
            var formalType = parameters.GetTypeSpec(firstParameter + ordinal);
            var isUnitFormal = typeSpecs.AreEquivalentTypeSpecRefs(
                formalType, UnitTypeSpecRef);
            if (isUnitInput != isUnitFormal)
            {
                throw InvalidMap();
            }

            inputOffset += 4;
        }

        var resultType = signatures.GetResultTypeSpec(signature);
        var unitResult = typeSpecs.AreEquivalentTypeSpecRefs(
            resultType, UnitTypeSpecRef);
        var doesNotReturn = (contracts.GetEffectsMask(targetMethod) & 2) != 0;
        if (payload[0] == 2 && !doesNotReturn && !unitResult)
        {
            throw InvalidMap();
        }

        if (doesNotReturn
            ? payload[1] != 0 || !body.IsCallSiteAtBlockEnd(blockIndex, recordIndex)
            : unitResult ? payload[1] != 0 : payload[1] == 0)
        {
            throw InvalidMap();
        }
    }

    private static int GetCallSiteTargetMethod(TrRmRegisterBodyReader body,
        ReadOnlySpan<byte> payload, int[] registerTypeMapIndexes,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var targetIndex = ReadUInt16(payload, 4);
        return payload[0] switch
        {
            1 => GetMethodMapTarget(body, targetIndex, methods, imports),
            2 => GetRegisterFunctionPointerAnchorMethod(
                body, registerTypeMapIndexes, targetIndex, typeSpecs,
                nativeFunctionPointerAnchors),
            3 => GetHomeFunctionPointerAnchorMethod(body, targetIndex,
                typeSpecs, nativeFunctionPointerAnchors),
            _ => throw InvalidMap(),
        };
    }

    private static int GetHomeFunctionPointerAnchorMethod(
        TrRmRegisterBodyReader body, int homeIndex,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var typeMapIndex = ReadUInt16(body.HomeMap, (homeIndex * 8) + 2);
        var typeSpec = typeSpecs.GetTypeSpecRef(
            GetTypeMapTypeSpecRef(body, typeMapIndex));
        if (!typeSpec.IsRow || typeSpec.Form != 0x0D || typeSpec.Qualifiers != 0
            || !nativeFunctionPointerAnchors.HasAnchorForTypeSpec(typeSpec.Row))
        {
            throw InvalidMap();
        }

        return nativeFunctionPointerAnchors.GetAnchorMethodDefRow(typeSpec.Row);
    }

    private static int GetRegisterFunctionPointerAnchorMethod(
        TrRmRegisterBodyReader body, int[] registerTypeMapIndexes, int register,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var typeMapIndex = GetRegisterTypeMapIndex(registerTypeMapIndexes, register);
        var typeSpec = typeSpecs.GetTypeSpecRef(
            GetTypeMapTypeSpecRef(body, typeMapIndex));
        if (!typeSpec.IsRow || typeSpec.Form != 0x0D || typeSpec.Qualifiers != 0
            || !nativeFunctionPointerAnchors.HasAnchorForTypeSpec(typeSpec.Row))
        {
            throw InvalidMap();
        }

        return nativeFunctionPointerAnchors.GetAnchorMethodDefRow(typeSpec.Row);
    }

    private static int[] CreateBlockEntryRegisterTypeMapIndexes(
        TrRmRegisterBodyReader body, int blockIndex)
    {
        var registerTypeMapIndexes = new int[16];
        Array.Fill(registerTypeMapIndexes, -1);
        var block = body.GetBlock(blockIndex);
        var entryState = body.GetRecord(ReadUInt16(block, 8));
        var registerCount = checked((int)ReadUInt32(entryState, 4));
        for (var entry = 0; entry < registerCount; entry++)
        {
            var offset = 8 + (entry * 5);
            var register = entryState[offset];
            if (entryState[offset + 1] != 0 || entryState[offset + 4] != 0)
            {
                continue;
            }

            registerTypeMapIndexes[register] = ReadUInt16(entryState, offset + 2);
        }

        return registerTypeMapIndexes;
    }

    private static void UpdateRegisterTypeMapIndexes(
        ReadOnlySpan<byte> instruction, int[] registerTypeMapIndexes)
    {
        if (instruction[0] == 0x10)
        {
            var destination = instruction[1] & 0x0F;
            var source = instruction[1] >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            registerTypeMapIndexes[destination] = source < 14
                ? registerTypeMapIndexes[source] : -1;
            return;
        }

        Array.Fill(registerTypeMapIndexes, -1);
    }

    private static int GetRegisterTypeMapIndex(
        int[] registerTypeMapIndexes, int register)
    {
        if ((uint)register >= (uint)registerTypeMapIndexes.Length
            || registerTypeMapIndexes[register] < 0)
        {
            throw InvalidMap();
        }

        return registerTypeMapIndexes[register];
    }

    private static void ValidateCallSiteHomeTypes(TrRmRegisterBodyReader body,
        int ownerModule, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmImportTableReader imports,
        ConstantSourceValueContext constantValues, TrRmContractTableReader contracts,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var registerTypeMapIndexes =
                CreateBlockEntryRegisterTypeMapIndexes(body, blockIndex);
            var position = 0;
            while (position < code.Length)
            {
                var instruction = code.Slice(position,
                    TrRmRegisterBodyReader.InstructionLength(code[position]));
                var recordIndex = instruction[0] switch
                {
                    0x90 => instruction[1],
                    0xE0 => ReadUInt16(instruction, 2),
                    _ => -1,
                };
                if (recordIndex >= 0)
                {
                    var payload = body.GetRecord(recordIndex)[4..];
                    if (payload[0] is 1 or 2 or 3)
                    {
                        var targetMethod = GetCallSiteTargetMethod(body, payload,
                            registerTypeMapIndexes, typeSpecs, methods, imports,
                            nativeFunctionPointerAnchors);
                        var signature = methods.GetSignature(targetMethod);
                        var (firstParameter, parameterCount) =
                            signatures.GetParamRange(signature);
                        var nativeParameters = contracts.GetNativeParameters(targetMethod);
                        var inputOffset = 16;
                        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
                        {
                            var selectorKind = payload[inputOffset];
                            if (selectorKind is 1 or 2)
                            {
                                var selectorIndex = ReadUInt16(payload,
                                    inputOffset + 2);
                                var typeMapIndex = selectorKind == 1
                                    ? GetRegisterTypeMapIndex(
                                        registerTypeMapIndexes, selectorIndex)
                                    : ReadUInt16(body.HomeMap,
                                        (selectorIndex * 8) + 2);
                                var formalType = parameters.GetTypeSpec(
                                    firstParameter + ordinal);
                                var nativeParameterAccess = (byte)0;
                                foreach (var nativeParameter in nativeParameters)
                                {
                                    if (nativeParameter.ParameterOrdinal == ordinal)
                                    {
                                        nativeParameterAccess = nativeParameter.Access;
                                        break;
                                    }
                                }

                                if (nativeParameterAccess != 0
                                    ? !TrRmNativeImportTypeContract
                                        .MatchesNativeParameterArgumentType(typeSpecs,
                                            GetTypeMapTypeSpecRef(body, typeMapIndex),
                                            formalType, nativeParameterAccess)
                                    : !MatchesTypeMapEntry(body, typeMapIndex,
                                        formalType, ownerModule, types, typeSpecs,
                                        genericParams, imports, constantValues,
                                        nativeFunctionPointerAnchors))
                                {
                                    throw InvalidMap();
                                }
                            }

                            inputOffset += 4;
                        }

                        if (payload[1] == 2)
                        {
                            var resultHome = ReadUInt16(payload, 6);
                            var typeMapIndex = ReadUInt16(body.HomeMap,
                                (resultHome * 8) + 2);
                            if (!MatchesTypeMapEntry(body, typeMapIndex,
                                signatures.GetResultTypeSpec(signature),
                                ownerModule, types, typeSpecs, genericParams,
                                imports, constantValues, nativeFunctionPointerAnchors))
                            {
                                throw InvalidMap();
                            }
                        }
                    }
                }

                UpdateRegisterTypeMapIndexes(instruction, registerTypeMapIndexes);
                position += instruction.Length;
            }
        }
    }

    private static uint GetTypeMapTypeSpecRef(
        TrRmRegisterBodyReader body, int typeMapIndex)
    {
        var token = ReadUInt32(body.TypeMap, typeMapIndex * sizeof(uint));
        return (byte)(token >> 24) switch
        {
            0x11 => token & 0x00FF_FFFF,
            0xF2 or 0xF3 => token,
            _ => throw InvalidMap(),
        };
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

    private static void ValidateNativeAddressRecords(
        TrRmRegisterBodyReader body, int ownerModule,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmContractTableReader contracts,
        TrRmImportTableReader imports,
        TrRmNativeImportTableReader standardNativeImports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        for (var recordIndex = 0; recordIndex < body.RecordCount; recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x0D)
            {
                continue;
            }

            if (!TrRmFeatureManifestValidator.Requires(features, modules,
                ownerModule, TrRmFeatureTableReader.NativeExportEntriesFeatureId))
            {
                throw InvalidMap();
            }

            var payload = record[4..];
            var pointerTypeMapIndex = ReadUInt16(payload, 6);
            var pointerTypeToken = ReadUInt32(body.TypeMap,
                pointerTypeMapIndex * sizeof(uint));
            if ((pointerTypeToken >> 24) != 0x11)
            {
                throw InvalidMap();
            }

            var pointerTypeSpecRow = checked((int)(pointerTypeToken & 0x00FF_FFFF));
            var pointerType = typeSpecs.GetTypeSpecRef((uint)pointerTypeSpecRow);
            if (!pointerType.IsRow || pointerType.Form != 0x0D
                || pointerType.Qualifiers != 0
                || !nativeFunctionPointerAnchors.HasAnchorForTypeSpec(
                    pointerTypeSpecRow))
            {
                throw InvalidMap();
            }

            var methodMapIndex = ReadUInt16(payload, 4);
            var methodMapToken = ReadUInt32(body.MethodMap,
                methodMapIndex * sizeof(uint));
            var methodKind = (byte)(methodMapToken >> 24);
            var targetMethod = GetMethodMapTarget(
                body, methodMapIndex, methods, imports);
            if (nativeFunctionPointerAnchors.IsAnchorMethodDef(targetMethod)
                || !HasSupportedNativeAddressContract(targetMethod,
                    pointerTypeSpecRow, methodKind, ownerModule,
                    types, methods, signatures, parameters,
                    contracts, standardNativeImports,
                    nativeFunctionPointerAnchors))
            {
                throw InvalidMap();
            }

            if (payload[0] == 2)
            {
                var homeIndex = ReadUInt16(payload, 2);
                var home = body.HomeMap.Slice(homeIndex * 8, 8);
                var homeTypeMapIndex = ReadUInt16(home, 2);
                var homeTypeToken = ReadUInt32(body.TypeMap,
                    homeTypeMapIndex * sizeof(uint));
                if (home[0] != 3 || (homeTypeToken >> 24) != 0x11)
                {
                    throw InvalidMap();
                }

                var homeTypeSpecRef = homeTypeToken & 0x00FF_FFFF;
                var homeType = typeSpecs.GetTypeSpecRef(homeTypeSpecRef);
                if (!homeType.IsRow || homeType.Form != 0x0D
                    || homeType.Qualifiers != 0
                    || !nativeFunctionPointerAnchors.AreEquivalentTypeSpecRefs(
                        (uint)pointerTypeSpecRow, homeTypeSpecRef))
                {
                    throw InvalidMap();
                }
            }
        }
    }

    private static bool HasSupportedNativeAddressContract(int targetMethod,
        int pointerTypeSpecRow, byte methodKind, int ownerModule,
        TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmContractTableReader contracts,
        TrRmNativeImportTableReader standardNativeImports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var targetSignature = methods.GetSignature(targetMethod);
        var nativeParameters = contracts.GetNativeParameters(targetMethod);
        if (signatures.GetReceiverTypeSpec(targetSignature) != 0
            || signatures.GetGenericCount(targetSignature) != 0
            || !contracts.GetNativeOutputs(targetMethod).IsDefaultOrEmpty
            || (!nativeParameters.IsDefaultOrEmpty
                && contracts.Schema != TrRmArtifactSchema.ForeignInvocationDraft))
        {
            return false;
        }

        var targetOwnerType = methods.GetOwnerType(targetMethod);
        if (targetOwnerType != 0
            && types.GetGenericRange(targetOwnerType).Count != 0)
        {
            return false;
        }

        var hasNativeCallable = contracts.HasNativeCallableContract(targetMethod);
        var hasNativeImport = standardNativeImports.HasImportForMethod(targetMethod);
        byte? targetConvention = null;
        if (hasNativeCallable)
        {
            targetConvention = contracts.GetNativeCallableConvention(targetMethod);
        }
        if (hasNativeImport)
        {
            var importConvention = standardNativeImports
                .GetImportForMethod(targetMethod).CallConv;
            if (targetConvention is byte existingConvention
                && existingConvention != importConvention)
            {
                return false;
            }
            targetConvention = importConvention;
        }

        if (methodKind == 0x16)
        {
            if (methods.GetOwnerModule(targetMethod) != ownerModule)
            {
                return false;
            }

            if (hasNativeCallable || hasNativeImport)
            {
                if (methods.GetBodyRow(targetMethod) == 0
                    && !hasNativeImport)
                {
                    return false;
                }
            }
            else
            {
                if (!IsOrdinaryAddressableMethod(targetMethod, types,
                    methods, signatures, parameters, contracts))
                {
                    return false;
                }
                targetConvention = 0;
            }
        }
        else if (methodKind != 0x1C)
        {
            return false;
        }

        return nativeFunctionPointerAnchors.MatchesAddressableMethod(
            targetMethod, pointerTypeSpecRow, targetConvention);
    }

    private static bool IsOrdinaryAddressableMethod(int methodDefRow,
        TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmContractTableReader contracts)
    {
        if (methods.GetBodyRow(methodDefRow) == 0
            || contracts.IsCleanupMethod(methodDefRow)
            || contracts.HasNativeAbi(methodDefRow))
        {
            return false;
        }

        var ownerType = methods.GetOwnerType(methodDefRow);

        var name = methods.GetName(methodDefRow);
        if (name.EndsWith(".#ctor", StringComparison.Ordinal)
            || name.EndsWith(".#cctor", StringComparison.Ordinal)
            || name.Contains(".#get:", StringComparison.Ordinal)
            || name.Contains(".#set:", StringComparison.Ordinal))
        {
            return false;
        }

        if (ownerType != 0)
        {
            var ownerName = types.GetNamespace(ownerType) is string @namespace
                && @namespace.Length != 0
                    ? @namespace + "." + types.GetName(ownerType)
                    : types.GetName(ownerType);
            if (Binder.TryGetStaticOperatorKey(name, ownerName, out _))
            {
                return false;
            }
        }

        var signature = methods.GetSignature(methodDefRow);
        var (firstParameter, parameterCount) = signatures.GetParamRange(signature);
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            if (parameters.GetMode(firstParameter + ordinal) != 0)
            {
                return false;
            }
        }

        return true;
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
        TrRmImportTableReader imports, int ownerModule,
        ConstantSourceValueContext constantValues, Stack<int> pendingRows)
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
                    TrRmConstantSourceValueValidator.ValidateFrozenScalar(
                        checked((int)typeSpecs.GetConstructedArgument(row, ordinal)),
                        genericParams.GetConstantTypeSpecRef(formalRow), ownerModule,
                        constantValues.ConstantSources,
                        constantValues.FrozenValueNodes, constantValues.Blobs,
                        typeSpecs, constantValues.Features, constantValues.Modules);
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
