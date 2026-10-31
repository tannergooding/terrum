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
        TrRmTypeDefTableReader typeDefs,
        TrRmEnumDefTableReader enumDefs,
        TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs,
        TrRmFixedArrayLayoutTableReader fixedArrayLayouts,
        TrRmTargetProfileTableReader targetProfiles)
    {
        internal TrRmFeatureTableReader Features { get; } = features;
        internal TrRmModuleTableReader Modules { get; } = modules;
        internal TrRmTypeDefTableReader TypeDefs { get; } = typeDefs;
        internal TrRmEnumDefTableReader EnumDefs { get; } = enumDefs;
        internal TrRmImportTableReader Imports { get; } = imports;
        internal TrRmConstantSourceTableReader ConstantSources { get; } = constantSources;
        internal TrRmFrozenValueNodeTableReader FrozenValueNodes { get; } = frozenValueNodes;
        internal TrRmBlobTableReader Blobs { get; } = blobs;
        internal TrRmFixedArrayLayoutTableReader FixedArrayLayouts { get; }
            = fixedArrayLayouts;
        internal TrRmTargetProfileTableReader TargetProfiles { get; }
            = targetProfiles;
    }

    private readonly record struct RegisterTypeState(int TypeMapIndex,
        uint TypeSpecRef, int PlaceSelectionRecordIndex, int RootHomeIndex,
        byte AccessKind)
    {
        internal static RegisterTypeState Unknown => new(-1, 0, -1, -1, 0);

        internal static RegisterTypeState FromTypeMap(int typeMapIndex)
            => new(typeMapIndex, 0, -1, -1, 0);

        internal static RegisterTypeState FromTypeSpecRef(uint typeSpecRef)
            => new(-1, typeSpecRef, -1, -1, 0);

        internal static RegisterTypeState FromPlaceTypeMap(int typeMapIndex,
            byte accessKind)
            => new(typeMapIndex, 0, -2, -1, accessKind);

        internal static RegisterTypeState FromPlace(uint typeSpecRef,
            int placeSelectionRecordIndex, int rootHomeIndex, byte accessKind)
            => new(-1, typeSpecRef, placeSelectionRecordIndex,
                rootHomeIndex, accessKind);

        internal bool IsKnown => TypeMapIndex >= 0 || TypeSpecRef != 0;
        internal bool IsPlaceHandle => PlaceSelectionRecordIndex != -1;
    }

    internal static void Validate(TrRmRegisterBodyReader body, int methodDefRow,
        int ownerModule, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmEnumDefTableReader enumDefs,
        TrRmTypeSpecTableReader typeSpecs,
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
        TrRmModuleRefTableReader moduleRefs,
        TrRmLayoutTableReader layouts,
        TrRmFixedArrayLayoutTableReader fixedArrayLayouts,
        TrRmTargetProfileTableReader targetProfiles)
    {
        if (body.HasRootPinOperations
            && !TrRmFeatureManifestValidator.Requires(features, modules,
                ownerModule, TrRmFeatureTableReader.RootPinRuntimeFeatureId,
                body.Schema))
        {
            throw new NotSupportedException(
                "A RootPin Body operation requires RootPinRuntime revision 1 in its using Module.");
        }

        var visitedTypeSpecRows = new HashSet<int>();
        var constantValues = new ConstantSourceValueContext(features, modules,
            types, enumDefs, imports, constantSources, frozenValueNodes, blobs,
            fixedArrayLayouts, targetProfiles);
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
        ValidateCallSiteEffects(body, methodDefRow, typeSpecs, methods, signatures,
            parameters, contracts, imports, nativeFunctionPointerAnchors,
            constantValues);
        ValidateCallSiteHomeTypes(body, methodDefRow, ownerModule, types, typeSpecs,
            genericParams, methods, signatures, parameters, imports,
            constantValues, contracts, nativeFunctionPointerAnchors);
        ValidateNativeAddressRecords(body, ownerModule, features, modules,
            types, typeSpecs, genericParams, methods, signatures, parameters,
            contracts, imports, standardNativeImports,
            nativeFunctionPointerAnchors);
        ValidateStorageAddressRecords(body, ownerModule, features, modules,
            typeSpecs, layouts, constantValues.FixedArrayLayouts);
        ValidateFixedArrayPlaceSelections(body, ownerModule, features,
            modules, typeSpecs, constantValues);
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

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var position = 0;
            while (position < code.Length)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(code[position],
                        body.Schema);
                var instruction = code.Slice(position, instructionLength);
                var opcode = instruction[0];
                var selectionIndex = opcode switch
                {
                    0xD8 => ReadUInt16(instruction, 2),
                    0x91 or 0x61 => instruction[1] >> 4,
                    _ => -1,
                };
                if (selectionIndex >= 0)
                {
                    var selection = body.GetRecord(selectionIndex);
                    if (selection[0] == 0x04)
                    {
                        var payload = selection[4..];
                        if (payload[9] == 3
                            && (opcode is not (0xD8 or 0x91)
                                || payload[2] != 1
                                || ReadUInt16(payload, 4)
                                    != (opcode == 0xD8
                                        ? instruction[1] & 0x0F
                                        : instruction[1] & 0x0F)))
                        {
                            throw InvalidMap();
                        }
                    }
                }

                position += instructionLength;
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
                    var leftDefinitionType
                        = typeSpecs.GetTypeSpecRef(leftDefinitionRef);
                    var rightDefinitionType
                        = typeSpecs.GetTypeSpecRef(rightDefinitionRef);
                    if (!leftDefinitionType.IsRow
                        || !rightDefinitionType.IsRow
                        || leftDefinitionType.Form is not (0x02 or 0x03)
                        || rightDefinitionType.Form is not (0x02 or 0x03)
                        || leftDefinitionType.Qualifiers
                            != rightDefinitionType.Qualifiers)
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
        var leftPayload = TrRmConstantSourceValueValidator
            .GetFrozenScalarPayload(leftRow, constantTypeSpecRef, ownerModule,
                constantValues.ConstantSources, constantValues.FrozenValueNodes,
                constantValues.Blobs, typeSpecs, constantValues.Features,
                constantValues.Modules, constantValues.TypeDefs,
                constantValues.EnumDefs, constantValues.Imports);
        var rightPayload = TrRmConstantSourceValueValidator
            .GetFrozenScalarPayload(rightRow, constantTypeSpecRef, ownerModule,
                constantValues.ConstantSources, constantValues.FrozenValueNodes,
                constantValues.Blobs, typeSpecs, constantValues.Features,
                constantValues.Modules, constantValues.TypeDefs,
                constantValues.EnumDefs, constantValues.Imports);
        return leftPayload.SequenceEqual(rightPayload);
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
                constantValues.Modules, constantValues.TypeDefs,
                constantValues.EnumDefs, constantValues.Imports);
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
                case 0x11:
                    if (!TrRmFeatureManifestValidator.Requires(
                            constantValues.Features, constantValues.Modules,
                            ownerModule,
                            TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
                            typeSpecs.Schema)
                        || !constantValues.TargetProfiles.TryGetProfile(
                            TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                            TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                            out var profileRow)
                        || constantValues.FixedArrayLayouts.FindRow(ownerModule,
                            (uint)row, (uint)profileRow) == 0)
                    {
                        throw InvalidMap();
                    }

                    AddNestedTypeSpecReference(ReadUInt32(data, 2), false,
                        typeSpecs, pendingRows);
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
        int methodDefRow,
        TrRmTypeSpecTableReader typeSpecs, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmContractTableReader contracts, TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors,
        ConstantSourceValueContext constantValues)
    {
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var registerTypes = CreateBlockEntryRegisterTypes(body, blockIndex);
            var position = 0;
            while (position < code.Length)
            {
                var opcode = code[position];
                var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                    opcode, body.Schema);
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
                            registerTypes,
                            typeSpecs, methods, signatures, parameters, contracts, imports,
                            nativeFunctionPointerAnchors);
                        UpdateCallSiteRegisterTypes(body, instruction[1], registerTypes,
                            typeSpecs, methods, signatures, imports,
                            nativeFunctionPointerAnchors);
                        break;
                    case 0xE0:
                        ValidateCallSiteEffects(body, ReadUInt16(instruction, 2),
                            blockIndex, registerTypes,
                            typeSpecs, methods, signatures,
                            parameters, contracts, imports,
                            nativeFunctionPointerAnchors);
                        UpdateCallSiteRegisterTypes(body,
                            ReadUInt16(instruction, 2), registerTypes,
                            typeSpecs, methods, signatures, imports,
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

                if (opcode is not (0x90 or 0xE0))
                {
                    UpdateRegisterTypeStates(body, instruction, registerTypes,
                        methodDefRow, methods, signatures, parameters, typeSpecs,
                        constantValues);
                }
                position += instructionLength;
            }
        }
    }

    private static void UpdateCallSiteRegisterTypes(
        TrRmRegisterBodyReader body, int recordIndex,
        RegisterTypeState[] registerTypes,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var payload = body.GetRecord(recordIndex)[4..];
        var targetMethod = GetCallSiteTargetMethod(body, payload,
            registerTypes, typeSpecs, methods, imports,
            nativeFunctionPointerAnchors);
        var signature = methods.GetSignature(targetMethod);
        var inputCount = checked((int)ReadUInt32(payload, 8));
        var inputOffset = 16;
        for (var input = 0; input < inputCount; input++)
        {
            if (payload[inputOffset] == 1
                && payload[inputOffset + 1] == 2)
            {
                var register = ReadUInt16(payload, inputOffset + 2);
                if (register < 14)
                {
                    registerTypes[register] = RegisterTypeState.Unknown;
                }
            }

            inputOffset += 4;
        }

        if (payload[1] == 1)
        {
            var resultRegister = ReadUInt16(payload, 6);
            registerTypes[resultRegister] =
                RegisterTypeState.FromTypeSpecRef(
                    signatures.GetResultTypeSpec(signature));
        }
    }

    private static void ValidateCallSiteEffects(TrRmRegisterBodyReader body,
        int recordIndex, int blockIndex, RegisterTypeState[] registerTypes,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmContractTableReader contracts,
        TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var payload = body.GetRecord(recordIndex)[4..];
        var targetMethod = GetCallSiteTargetMethod(body, payload,
            registerTypes, typeSpecs, methods, imports,
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
        if (doesNotReturn
            ? payload[1] != 0 || !body.IsCallSiteAtBlockEnd(blockIndex, recordIndex)
            : unitResult ? payload[1] != 0 : payload[1] == 0)
        {
            throw InvalidMap();
        }
    }

    private static int GetCallSiteTargetMethod(TrRmRegisterBodyReader body,
        ReadOnlySpan<byte> payload, RegisterTypeState[] registerTypes,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var targetIndex = ReadUInt16(payload, 4);
        return payload[0] switch
        {
            1 => GetMethodMapTarget(body, targetIndex, methods, imports),
            2 => GetRegisterFunctionPointerAnchorMethod(
                body, registerTypes, targetIndex, typeSpecs,
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
        TrRmRegisterBodyReader body, RegisterTypeState[] registerTypes, int register,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var typeState = GetRegisterTypeState(registerTypes, register);
        if (typeState.IsPlaceHandle)
        {
            throw InvalidMap();
        }

        var typeSpec = typeSpecs.GetTypeSpecRef(
            GetRegisterTypeSpecRef(body, typeState));
        if (!typeSpec.IsRow || typeSpec.Form != 0x0D || typeSpec.Qualifiers != 0
            || !nativeFunctionPointerAnchors.HasAnchorForTypeSpec(typeSpec.Row))
        {
            throw InvalidMap();
        }

        return nativeFunctionPointerAnchors.GetAnchorMethodDefRow(typeSpec.Row);
    }

    private static RegisterTypeState[] CreateBlockEntryRegisterTypes(
        TrRmRegisterBodyReader body, int blockIndex)
    {
        var registerTypes = new RegisterTypeState[16];
        Array.Fill(registerTypes, RegisterTypeState.Unknown);
        var block = body.GetBlock(blockIndex);
        var entryState = body.GetRecord(ReadUInt16(block, 8));
        var registerCount = checked((int)ReadUInt32(entryState, 4));
        for (var entry = 0; entry < registerCount; entry++)
        {
            var offset = 8 + (entry * 5);
            var register = entryState[offset];
            var carrierKind = entryState[offset + 1];
            var accessKind = entryState[offset + 4];
            var typeMapIndex = ReadUInt16(entryState, offset + 2);
            if (carrierKind == 0 && accessKind == 0)
            {
                registerTypes[register]
                    = RegisterTypeState.FromTypeMap(typeMapIndex);
            }
            else if (carrierKind == 1 && accessKind is >= 1 and <= 5)
            {
                registerTypes[register]
                    = RegisterTypeState.FromPlaceTypeMap(typeMapIndex,
                        accessKind);
            }
            else
            {
                continue;
            }
        }

        return registerTypes;
    }

    private static void UpdateRegisterTypeStates(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        RegisterTypeState[] registerTypes, int methodDefRow,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues)
    {
        if (instruction[0] is 0xD8 or 0x91)
        {
            UpdateFixedArrayPlaceRegister(body, instruction, registerTypes,
                methods.GetOwnerModule(methodDefRow), typeSpecs,
                constantValues);
            return;
        }

        if (instruction[0] == 0xEC)
        {
            UpdateStorageAddressResult(body, instruction, registerTypes,
                methods.GetOwnerModule(methodDefRow), typeSpecs,
                constantValues);
            return;
        }

        if (instruction[0] == 0x10)
        {
            var destination = instruction[1] & 0x0F;
            var source = instruction[1] >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            registerTypes[destination] = source < 14
                ? registerTypes[source] : RegisterTypeState.Unknown;
            return;
        }

        if (instruction[0] == 0x13)
        {
            var destination = instruction[1] & 0x0F;
            var homeIndex = instruction[1] >> 4;
            if (destination >= 14 || homeIndex >= body.HomeCount)
            {
                throw InvalidMap();
            }

            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            if (home[0] != 2 || home[1] != 0)
            {
                throw InvalidMap();
            }

            var typeMapIndex = ReadUInt16(home, 2);
            if (!IsCopyableRegisterValueType(typeSpecs,
                GetTypeMapTypeSpecRef(body, typeMapIndex), constantValues))
            {
                throw InvalidMap();
            }

            registerTypes[destination] = RegisterTypeState.FromTypeMap(typeMapIndex);
            return;
        }

        if (instruction[0] == 0x11)
        {
            var destination = instruction[1] & 0x0F;
            var formalOrdinal = instruction[1] >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var signature = methods.GetSignature(methodDefRow);
            var (firstParameter, parameterCount) =
                signatures.GetParamRange(signature);
            if (formalOrdinal >= parameterCount)
            {
                throw InvalidMap();
            }

            var parameterRow = firstParameter + formalOrdinal;
            var typeSpecRef = parameters.GetTypeSpec(parameterRow);
            if (parameters.GetMode(parameterRow) != 0
                || parameters.GetFlags(parameterRow) != 0
                || !IsCopyableRegisterValueType(typeSpecs, typeSpecRef,
                    constantValues))
            {
                throw InvalidMap();
            }

            registerTypes[destination] =
                RegisterTypeState.FromTypeSpecRef(typeSpecRef);
            return;
        }

        if (instruction[0] == 0x12)
        {
            var destination = instruction[1] & 0x0F;
            var firstFormal = instruction[1] >> 4;
            if (destination > 12)
            {
                throw InvalidMap();
            }

            var signature = methods.GetSignature(methodDefRow);
            var (firstParameter, parameterCount) =
                signatures.GetParamRange(signature);
            if (firstFormal + 1 >= parameterCount)
            {
                throw InvalidMap();
            }

            for (var pair = 0; pair < 2; pair++)
            {
                var parameterRow = firstParameter + firstFormal + pair;
                var typeSpecRef = parameters.GetTypeSpec(parameterRow);
                if (parameters.GetMode(parameterRow) != 0
                    || parameters.GetFlags(parameterRow) != 0
                    || !IsCopyableRegisterValueType(typeSpecs, typeSpecRef,
                        constantValues))
                {
                    throw InvalidMap();
                }

                registerTypes[destination + pair] =
                    RegisterTypeState.FromTypeSpecRef(typeSpecRef);
            }
            return;
        }

        if (instruction[0] == 0x16)
        {
            var destination = instruction[1] & 0x0F;
            var constantIndex = instruction[1] >> 4;
            if (destination >= 14 || constantIndex >= body.ConstantCount)
            {
                throw InvalidMap();
            }

            var constant = body.ConstantMap.Slice(constantIndex * 8, 8);
            var typeSpecRef = ReadUInt32(constant, 0);
            if (!IsCopyableRegisterValueType(typeSpecs, typeSpecRef,
                constantValues))
            {
                throw InvalidMap();
            }

            registerTypes[destination] =
                RegisterTypeState.FromTypeSpecRef(typeSpecRef);
            return;
        }

        if (instruction[0] == 0xD5)
        {
            var destination = instruction[1] & 0x0F;
            var homeIndex = ReadUInt16(instruction, 2);
            if (destination >= 14 || homeIndex >= body.HomeCount)
            {
                throw InvalidMap();
            }

            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var typeMapIndex = ReadUInt16(home, 2);
            if (!IsCopyableRegisterValueType(typeSpecs,
                    GetTypeMapTypeSpecRef(body, typeMapIndex), constantValues))
            {
                throw InvalidMap();
            }

            registerTypes[destination] = RegisterTypeState.FromTypeMap(
                typeMapIndex);
            return;
        }

        if (instruction[0] is 0x20 or 0x26)
        {
            var destination = instruction[1] & 0x0F;
            var source = instruction[1] >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var sourceType = source < 14 && registerTypes[source].IsKnown
                && !registerTypes[source].IsPlaceHandle
                ? GetRegisterTypeSpecRef(body, registerTypes[source])
                : 0;
            var isFloating = instruction[0] == 0x26;
            registerTypes[destination] = sourceType != 0
                && IsRegisterArithmeticType(typeSpecs, sourceType, isFloating)
                    ? registerTypes[source]
                    : RegisterTypeState.Unknown;
            return;
        }

        if (instruction[0] is (>= 0x21 and <= 0x25)
            or (>= 0x27 and <= 0x2B))
        {
            var destination = instruction[1] & 0x0F;
            var right = instruction[1] >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var destinationState = registerTypes[destination];
            var rightState = right < 14
                ? registerTypes[right] : RegisterTypeState.Unknown;
            if (destinationState.IsKnown && rightState.IsKnown
                && !destinationState.IsPlaceHandle && !rightState.IsPlaceHandle)
            {
                var destinationType = GetRegisterTypeSpecRef(
                    body, destinationState);
                var rightType = GetRegisterTypeSpecRef(body, rightState);
                var isFloating = instruction[0] >= 0x27;
                if (typeSpecs.AreEquivalentTypeSpecRefs(
                        destinationType, rightType)
                    && IsRegisterArithmeticType(
                        typeSpecs, destinationType, isFloating))
                {
                    return;
                }
            }

            registerTypes[destination] = RegisterTypeState.Unknown;
            return;
        }

        if (instruction[0] is >= 0x30 and <= 0x32)
        {
            var destination = instruction[1] & 0x0F;
            var right = instruction[1] >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var destinationState = registerTypes[destination];
            var rightState = right < 14
                ? registerTypes[right] : RegisterTypeState.Unknown;
            if (destinationState.IsKnown && rightState.IsKnown
                && !destinationState.IsPlaceHandle && !rightState.IsPlaceHandle)
            {
                var destinationType = GetRegisterTypeSpecRef(
                    body, destinationState);
                var rightType = GetRegisterTypeSpecRef(body, rightState);
                if (typeSpecs.AreEquivalentTypeSpecRefs(
                        destinationType, rightType)
                    && IsRegisterBitwiseType(typeSpecs, destinationType))
                {
                    return;
                }
            }

            registerTypes[destination] = RegisterTypeState.Unknown;
            return;
        }

        if (instruction[0] == 0x33)
        {
            var destination = instruction[1] & 0x0F;
            var source = instruction[1] >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var sourceState = source < 14
                ? registerTypes[source] : RegisterTypeState.Unknown;
            registerTypes[destination] = sourceState.IsKnown
                && !sourceState.IsPlaceHandle
                && IsRegisterBitwiseType(typeSpecs,
                    GetRegisterTypeSpecRef(body, sourceState))
                    ? sourceState : RegisterTypeState.Unknown;
            return;
        }

        if (instruction[0] is >= 0x34 and <= 0x36)
        {
            var destination = instruction[1] & 0x0F;
            var countRegister = instruction[1] >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var destinationState = registerTypes[destination];
            var countState = countRegister < 14
                ? registerTypes[countRegister] : RegisterTypeState.Unknown;
            if (destinationState.IsKnown && countState.IsKnown
                && !destinationState.IsPlaceHandle && !countState.IsPlaceHandle
                && IsRegisterShiftIntegerType(typeSpecs,
                    GetRegisterTypeSpecRef(body, destinationState))
                && IsRegisterShiftIntegerType(typeSpecs,
                    GetRegisterTypeSpecRef(body, countState)))
            {
                return;
            }

            registerTypes[destination] = RegisterTypeState.Unknown;
            return;
        }

        if (instruction[0] == 0x40)
        {
            var destination = instruction[1] & 0x0F;
            var right = instruction[1] >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var destinationState = registerTypes[destination];
            var rightState = right < 14
                ? registerTypes[right] : RegisterTypeState.Unknown;
            registerTypes[destination] =
                destinationState.IsKnown && rightState.IsKnown
                    && !destinationState.IsPlaceHandle
                    && !rightState.IsPlaceHandle
                    && typeSpecs.AreEquivalentTypeSpecRefs(
                        GetRegisterTypeSpecRef(body, destinationState),
                        GetRegisterTypeSpecRef(body, rightState))
                    ? RegisterTypeState.FromTypeSpecRef(0xF200_000B)
                    : RegisterTypeState.Unknown;
            return;
        }

        if (instruction[0] == 0x41)
        {
            var destination = instruction[1] & 0x0F;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var sourceState = registerTypes[destination];
            registerTypes[destination] = sourceState.IsKnown
                && IsRegisterType(typeSpecs,
                    GetRegisterTypeSpecRef(body, sourceState), 0x0B)
                    ? RegisterTypeState.FromTypeSpecRef(0xF200_0001)
                    : RegisterTypeState.Unknown;
            return;
        }

        if (instruction[0] is >= 0x50 and <= 0x52)
        {
            var destination = instruction[1] & 0x0F;
            var typeMapIndex = instruction[1] >> 4;
            if (destination >= 14 || typeMapIndex >= body.TypeCount)
            {
                throw InvalidMap();
            }

            registerTypes[destination] = registerTypes[destination].IsKnown
                && !registerTypes[destination].IsPlaceHandle
                ? RegisterTypeState.FromTypeMap(typeMapIndex)
                : RegisterTypeState.Unknown;
            return;
        }

        Array.Fill(registerTypes, RegisterTypeState.Unknown);
    }

    private static void UpdateFixedArrayPlaceRegister(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        RegisterTypeState[] registerTypes, int ownerModule,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues)
    {
        var opcode = instruction[0];
        var destination = instruction[1] & 0x0F;
        var recordIndex = opcode == 0xD8
            ? ReadUInt16(instruction, 2)
            : instruction[1] >> 4;
        var record = body.GetRecord(recordIndex);
        if (record[0] != 0x04)
        {
            throw InvalidMap();
        }

        var payload = record[4..];
        if (payload[9] != 3)
        {
            Array.Fill(registerTypes, RegisterTypeState.Unknown);
            return;
        }

        var layoutRow = ReadUInt32(payload, 12);
        var arrayLayout = constantValues.FixedArrayLayouts.GetRow(
            checked((int)layoutRow));
        if (arrayLayout.OwnerModuleRow != ownerModule
            || payload[2] != 1
            || ReadUInt16(payload, 4) != destination
            || !typeSpecs.TryGetFixedInlineArray(
                (uint)arrayLayout.ArrayTypeSpecRow,
                out var elementTypeSpecRef, out _))
        {
            throw InvalidMap();
        }

        var indexIsValid = payload[8] switch
        {
            1 => IsValueRegisterIndex(body, registerTypes,
                ReadUInt16(payload, 10), typeSpecs),
            2 => IsHomeIndex(body, ReadUInt16(payload, 10), typeSpecs),
            _ => false,
        };
        if (!indexIsValid)
        {
            throw InvalidMap();
        }

        int rootHome;
        bool parentAllowsWrite;
        if (payload[3] == 2)
        {
            var homeIndex = ReadUInt16(payload, 6);
            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            if (home[0] != 3
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    GetTypeMapTypeSpecRef(body, ReadUInt16(home, 2)),
                    (uint)arrayLayout.ArrayTypeSpecRow))
            {
                throw InvalidMap();
            }

            rootHome = homeIndex;
            parentAllowsWrite = (home[1] & 1) != 0;
        }
        else if (payload[3] == 1)
        {
            var baseRegister = ReadUInt16(payload, 6);
            var baseState = GetRegisterTypeState(registerTypes, baseRegister);
            if (!baseState.IsPlaceHandle
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    GetRegisterTypeSpecRef(body, baseState),
                    (uint)arrayLayout.ArrayTypeSpecRow))
            {
                throw InvalidMap();
            }

            rootHome = baseState.RootHomeIndex;
            parentAllowsWrite = AllowsWriteAuthority(
                baseState.AccessKind,
                GetRegisterTypeSpecRef(body, baseState), typeSpecs);
        }
        else
        {
            throw InvalidMap();
        }

        var elementType = typeSpecs.GetTypeSpecRef(elementTypeSpecRef);
        var requiresWrite = payload[1] is 2 or 3 or 4
            || (payload[1] == 5 && (elementType.Qualifiers & 1) != 0);
        if (requiresWrite && !parentAllowsWrite)
        {
            throw InvalidMap();
        }

        registerTypes[destination] = RegisterTypeState.FromPlace(
            elementTypeSpecRef, recordIndex, rootHome, payload[1]);
    }

    private static void UpdateStorageAddressResult(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        RegisterTypeState[] registerTypes, int ownerModule,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues)
    {
        var storage = body.GetRecord(ReadUInt16(instruction, 2))[4..];
        var sourceKind = storage[0];
        var sourceIndex = ReadUInt16(storage, 2);
        var resultKind = storage[1];
        var resultIndex = ReadUInt16(storage, 4);
        var pointerTypeMapIndex = ReadUInt16(storage, 6);
        if (sourceKind == 1)
        {
            if (!body.Schema.UsesForeignInvocationStorageFixedArrays())
            {
                throw InvalidMap();
            }

            var source = GetRegisterTypeState(registerTypes, sourceIndex);
            if (!source.IsPlaceHandle || source.RootHomeIndex < 0
                || source.PlaceSelectionRecordIndex < 0
                || (uint)source.RootHomeIndex >= (uint)body.HomeCount)
            {
                throw InvalidMap();
            }

            var rootHome = body.HomeMap.Slice(source.RootHomeIndex * 8, 8);
            if (rootHome[0] != 3)
            {
                throw InvalidMap();
            }

            var selection = body.GetRecord(
                source.PlaceSelectionRecordIndex)[4..];
            if (selection[0] != 3 || selection[9] != 3)
            {
                throw InvalidMap();
            }

            var layoutRow = ReadUInt32(selection, 12);
            var arrayLayout = constantValues.FixedArrayLayouts.GetRow(
                checked((int)layoutRow));
            if (arrayLayout.OwnerModuleRow != ownerModule
                || !typeSpecs.TryGetFixedInlineArray(
                    (uint)arrayLayout.ArrayTypeSpecRow,
                    out var elementTypeSpecRef, out var elementCount)
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    source.TypeSpecRef, elementTypeSpecRef)
                || ReadUInt32(storage, 8) > elementCount)
            {
                throw InvalidMap();
            }

            var pointerTypeSpecRef = GetTypeMapTypeSpecRef(body,
                pointerTypeMapIndex);
            var pointerType = typeSpecs.GetTypeSpecBytes(
                pointerTypeSpecRef);
            var pointeeType = pointerType.Length == 4
                ? 0xF200_0000u | pointerType[3]
                : pointerType.Length == 7
                    ? ReadUInt32(pointerType, 3)
                    : throw InvalidMap();
            var isOpaqueVoid = typeSpecs.AreEquivalentTypeSpecRefs(
                pointeeType, 0xF200_0000);
            if ((!isOpaqueVoid
                    && !typeSpecs.AreEquivalentTypeSpecRefs(
                        source.TypeSpecRef, pointeeType))
                || (isOpaqueVoid && pointerType[1] != 0)
                || (pointerType[1] == 1
                    && (!AllowsWriteAuthority(source.AccessKind,
                            source.TypeSpecRef, typeSpecs)
                        || (rootHome[1] & 1) == 0)))
            {
                throw InvalidMap();
            }
        }

        if (resultKind == 1 && resultIndex < 14)
        {
            registerTypes[resultIndex]
                = RegisterTypeState.FromTypeMap(pointerTypeMapIndex);
        }
    }

    private static bool IsValueRegisterIndex(TrRmRegisterBodyReader body,
        RegisterTypeState[] registerTypes, int register,
        TrRmTypeSpecTableReader typeSpecs)
    {
        if ((uint)register >= (uint)registerTypes.Length
            || !registerTypes[register].IsKnown
            || registerTypes[register].IsPlaceHandle)
        {
            return false;
        }

        return IsFixedArrayIndexType(typeSpecs,
            GetRegisterTypeSpecRef(body, registerTypes[register]));
    }

    private static bool IsHomeIndex(TrRmRegisterBodyReader body,
        int homeIndex, TrRmTypeSpecTableReader typeSpecs)
    {
        var home = body.HomeMap.Slice(homeIndex * 8, 8);
        return IsFixedArrayIndexType(typeSpecs,
            GetTypeMapTypeSpecRef(body, ReadUInt16(home, 2)));
    }

    private static bool AllowsWriteAuthority(byte accessKind,
        uint typeSpecRef, TrRmTypeSpecTableReader typeSpecs)
        => accessKind is 2 or 3 or 4
            || (accessKind == 5
                && (typeSpecs.GetTypeSpecRef(typeSpecRef).Qualifiers & 1) != 0);

    private static bool IsRegisterBitwiseType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        return type.Form == 0x01 && type.Qualifiers == 0
            && (type.TypeCode == 0x01
                || type.TypeCode is >= 0x06 and <= 0x0F);
    }

    private static bool IsRegisterShiftIntegerType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        return type.Form == 0x01 && type.Qualifiers == 0
            && type.TypeCode is >= 0x06 and <= 0x11;
    }

    private static bool IsRegisterType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef, byte typeCode)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        return type.Form == 0x01 && type.Qualifiers == 0
            && type.TypeCode == typeCode;
    }

    private static bool IsRegisterArithmeticType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef, bool isFloating)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        return type.Form == 0x01 && type.Qualifiers == 0
            && (isFloating
                ? type.TypeCode is 0x14 or 0x15
                : type.TypeCode is >= 0x06 and <= 0x0F);
    }

    private static RegisterTypeState GetRegisterTypeState(
        RegisterTypeState[] registerTypes, int register)
    {
        if ((uint)register >= (uint)registerTypes.Length
            || !registerTypes[register].IsKnown)
        {
            throw InvalidMap();
        }

        return registerTypes[register];
    }

    private static uint GetRegisterTypeSpecRef(TrRmRegisterBodyReader body,
        RegisterTypeState type)
        => type.TypeSpecRef != 0
            ? type.TypeSpecRef
            : type.TypeMapIndex >= 0
                ? GetTypeMapTypeSpecRef(body, type.TypeMapIndex)
                : throw InvalidMap();

    private static bool MatchesRegisterType(TrRmRegisterBodyReader body,
        RegisterTypeState actual, uint expectedTypeSpecRef, int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, TrRmImportTableReader imports,
        ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        if (actual.IsPlaceHandle)
        {
            return false;
        }

        if (actual.TypeSpecRef != 0)
        {
            return AreEquivalentSelectedTypeSpecRefs(actual.TypeSpecRef,
                expectedTypeSpecRef, ownerModule, types, typeSpecs,
                genericParams, imports, constantValues,
                nativeFunctionPointerAnchors);
        }

        return actual.TypeMapIndex >= 0
            && MatchesTypeMapEntry(body, actual.TypeMapIndex,
                expectedTypeSpecRef, ownerModule, types, typeSpecs,
                genericParams, imports, constantValues,
                nativeFunctionPointerAnchors);
    }

    private static bool IsCopyableRegisterValueType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef,
        ConstantSourceValueContext constantValues)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        var isAtomicValueType = type.TypeCode is > 0 and < 0x80;
        var isPointerFamily = type.TypeCode == 0x8C;
        if ((type.Form is 0x02 or 0x03) && type.Qualifiers == 0)
        {
            var typeDefRow = typeSpecs.GetNominalTypeDefRow(typeSpecRef,
                constantValues.TypeDefs, constantValues.Imports);
            return constantValues.TypeDefs.GetKind(typeDefRow) == 4;
        }

        return type.Form == 0x0D
            || (type.Form == 0x01 && (isAtomicValueType || isPointerFamily))
            || (type.Form == 0x0C && isPointerFamily);
    }

    private static void ValidateCallSiteHomeTypes(TrRmRegisterBodyReader body,
        int methodDefRow, int ownerModule, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmImportTableReader imports,
        ConstantSourceValueContext constantValues, TrRmContractTableReader contracts,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var registerTypes = CreateBlockEntryRegisterTypes(body, blockIndex);
            var position = 0;
            while (position < code.Length)
            {
                var instruction = code.Slice(position,
                    TrRmRegisterBodyReader.InstructionLength(code[position],
                        body.Schema));
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
                            registerTypes, typeSpecs, methods, imports,
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
                                var typeState = selectorKind == 1
                                    ? GetRegisterTypeState(registerTypes, selectorIndex)
                                    : RegisterTypeState.FromTypeMap(
                                        ReadUInt16(body.HomeMap,
                                            (selectorIndex * 8) + 2));
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
                                            GetRegisterTypeSpecRef(body, typeState),
                                            formalType, nativeParameterAccess)
                                    : !MatchesRegisterType(body, typeState,
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

                if (recordIndex >= 0)
                {
                    UpdateCallSiteRegisterTypes(body, recordIndex,
                        registerTypes, typeSpecs, methods, signatures,
                        imports, nativeFunctionPointerAnchors);
                }
                else
                {
                    UpdateRegisterTypeStates(body, instruction, registerTypes,
                        methodDefRow, methods, signatures, parameters, typeSpecs,
                        constantValues);
                }
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
        if (!body.Schema.UsesForeignInvocationContracts())
        {
            return;
        }

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

    private static void ValidateStorageAddressRecords(
        TrRmRegisterBodyReader body, int ownerModule,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmLayoutTableReader layouts,
        TrRmFixedArrayLayoutTableReader fixedArrayLayouts)
    {
        if (!body.Schema.UsesForeignInvocationStorageAddresses())
        {
            return;
        }

        for (var recordIndex = 0; recordIndex < body.RecordCount; recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x0E)
            {
                continue;
            }

            if (!TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule, TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                    body.Schema))
            {
                throw InvalidMap();
            }

            var payload = record[4..];
            var sourceKind = payload[0];
            var resultKind = payload[1];
            var sourceIndex = ReadUInt16(payload, 2);
            var pointerTypeMapIndex = ReadUInt16(payload, 6);
            var requiredElementCount = ReadUInt32(payload, 8);
            if (sourceKind is not (1 or 2) || resultKind is not (1 or 2)
                || requiredElementCount == 0
                || (sourceKind == 2 && requiredElementCount != 1)
                || (sourceKind == 1
                    && (!body.Schema.UsesForeignInvocationStorageFixedArrays()
                        || sourceIndex > 13 || fixedArrayLayouts.Count == 0)))
            {
                throw InvalidMap();
            }

            var sourceType = 0u;
            var sourceHomeMutable = false;
            if (sourceKind == 2)
            {
                var sourceHome = body.HomeMap.Slice(sourceIndex * 8, 8);
                if (sourceHome[0] != 3)
                {
                    throw InvalidMap();
                }

                sourceType = GetTypeMapTypeSpecRef(body,
                    ReadUInt16(sourceHome, 2));
                sourceHomeMutable = (sourceHome[1] & 1) != 0;
                if (!TrRmNativeImportTypeContract.TryGetScalarType(
                        typeSpecs, sourceType, out var sourceScalar)
                    || !DirectNativeImportContract.IsScalar(sourceScalar))
                {
                    throw InvalidMap();
                }
            }

            var pointerType = GetTypeMapTypeSpecRef(body, pointerTypeMapIndex);
            var pointerBytes = typeSpecs.GetTypeSpecBytes(pointerType);
            if (pointerBytes.Length is not (4 or 7)
                || pointerBytes[2] != 0x8C
                || pointerBytes[1] is not (0 or 1)
                || (pointerBytes.Length == 4 && pointerBytes[0] != 0x01)
                || (pointerBytes.Length == 7 && pointerBytes[0] != 0x0C))
            {
                throw InvalidMap();
            }

            var pointeeType = pointerBytes.Length == 4
                ? 0xF200_0000u | pointerBytes[3]
                : ReadUInt32(pointerBytes, 3);
            var isOpaqueVoid = typeSpecs.AreEquivalentTypeSpecRefs(
                pointeeType, 0xF200_0000);
            if ((sourceKind == 2 && pointerBytes[1] == 1
                    && !sourceHomeMutable)
                || (isOpaqueVoid && pointerBytes[1] != 0)
                || (!isOpaqueVoid
                    && sourceKind == 2
                    && !typeSpecs.AreEquivalentTypeSpecRefs(
                        sourceType, pointeeType))
                || !TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
                    typeSpecs, pointerType,
                    pointerBytes[1] == 1 ? (byte)2 : (byte)1,
                    layouts, out _))
            {
                throw InvalidMap();
            }

            if (resultKind == 2)
            {
                var resultHome = body.HomeMap.Slice(
                    ReadUInt16(payload, 4) * 8, 8);
                if (resultHome[0] != 3
                    || !typeSpecs.AreEquivalentTypeSpecRefs(
                        GetTypeMapTypeSpecRef(body,
                            ReadUInt16(resultHome, 2)), pointerType))
                {
                    throw InvalidMap();
                }
            }
        }
    }

    private static void ValidateFixedArrayPlaceSelections(
        TrRmRegisterBodyReader body, int ownerModule,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues)
    {
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x04)
            {
                continue;
            }

            var payload = record[4..];
            if (payload[9] != 3)
            {
                continue;
            }

            if (!body.Schema.UsesForeignInvocationStorageFixedArrays()
                || payload[0] != 3 || payload[2] != 1
                || payload[3] is not (1 or 2)
                || payload[8] is not (1 or 2)
                || !TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule,
                    TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
                    body.Schema))
            {
                throw InvalidMap();
            }

            var layoutRow = ReadUInt32(payload, 12);
            if (layoutRow == 0
                || layoutRow > (uint)constantValues.FixedArrayLayouts.Count)
            {
                throw InvalidMap();
            }

            var arrayLayout = constantValues.FixedArrayLayouts
                .GetRow(checked((int)layoutRow));
            if (arrayLayout.OwnerModuleRow != ownerModule
                || !constantValues.TargetProfiles.TryGetProfile(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                    out var profileRow)
                || arrayLayout.TargetProfileRef != (uint)profileRow
                || !typeSpecs.TryGetFixedInlineArray(
                    (uint)arrayLayout.ArrayTypeSpecRow,
                    out var elementTypeSpecRef, out _)
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    GetTypeMapTypeSpecRef(body,
                        ReadUInt16(payload, 16)), elementTypeSpecRef))
            {
                throw InvalidMap();
            }

            if (payload[3] == 2)
            {
                var baseHome = ReadUInt16(payload, 6);
                var home = body.HomeMap.Slice(baseHome * 8, 8);
                if (home[0] != 3
                    || !typeSpecs.AreEquivalentTypeSpecRefs(
                        GetTypeMapTypeSpecRef(body, ReadUInt16(home, 2)),
                        (uint)arrayLayout.ArrayTypeSpecRow))
                {
                    throw InvalidMap();
                }

                var referent = typeSpecs.GetTypeSpecRef(elementTypeSpecRef);
                var requiresWrite = payload[1] is 2 or 3 or 4
                    || (payload[1] == 5
                        && (referent.Qualifiers & 1) != 0);
                if (requiresWrite && (home[1] & 1) == 0)
                {
                    throw InvalidMap();
                }
            }

            if (payload[8] == 2)
            {
                var indexHome = ReadUInt16(payload, 10);
                var home = body.HomeMap.Slice(indexHome * 8, 8);
                if (!IsFixedArrayIndexType(typeSpecs,
                        GetTypeMapTypeSpecRef(body, ReadUInt16(home, 2))))
                {
                    throw InvalidMap();
                }
            }
        }
    }

    private static bool IsFixedArrayIndexType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        return type.Form == 0x01 && type.Qualifiers == 0
            && type.TypeCode is >= 0x06 and <= 0x11;
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
                && !contracts.Schema.UsesForeignInvocationContracts()))
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
                        typeSpecs, constantValues.Features, constantValues.Modules,
                        constantValues.TypeDefs, constantValues.EnumDefs,
                        constantValues.Imports);
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
