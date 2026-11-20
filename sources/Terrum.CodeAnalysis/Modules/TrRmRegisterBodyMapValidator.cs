// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmRegisterBodyMapValidator
{
    private const uint UnitTypeSpecRef = 0xF200_0000;
    private const uint BooleanTypeSpecRef = 0xF200_0001;

    private static void ConsumeWork(TrRmRegisterBodyReader body,
        ulong units = 1)
        => body.ResourceLedger?.ConsumeWork(units);

    private static void ConsumeWork(TrRmTypeSpecTableReader typeSpecs,
        ulong units = 1)
        => typeSpecs.ResourceLedger?.ConsumeWork(units);

    private static ReadOnlySpan<byte> GetHomeMapEntry(
        TrRmRegisterBodyReader body, int homeIndex)
    {
        ConsumeWork(body, 9);
        if ((uint)homeIndex >= (uint)body.HomeCount)
        {
            throw InvalidMap();
        }

        return body.HomeMap.Slice(homeIndex * 8, 8);
    }

    private static int GetHomeTypeMapIndex(
        TrRmRegisterBodyReader body, int homeIndex)
    {
        var home = GetHomeMapEntry(body, homeIndex);
        return ReadUInt16(body, home, 2);
    }

    private static uint GetTypeMapToken(TrRmRegisterBodyReader body,
        int typeMapIndex)
    {
        ConsumeWork(body, 6);
        if ((uint)typeMapIndex >= (uint)body.TypeCount)
        {
            throw InvalidMap();
        }

        return ReadUInt32(body.TypeMap, typeMapIndex * sizeof(uint));
    }

    private static uint GetMethodMapToken(TrRmRegisterBodyReader body,
        int methodMapIndex)
    {
        ConsumeWork(body, 6);
        if ((uint)methodMapIndex >= (uint)body.MethodCount)
        {
            throw InvalidMap();
        }

        return ReadUInt32(body.MethodMap, methodMapIndex * sizeof(uint));
    }

    private static ReadOnlySpan<byte> GetRecordPayload(
        TrRmRegisterBodyReader body, int recordIndex)
    {
        var record = body.GetRecord(recordIndex);
        ConsumeWork(body, checked((ulong)(record.Length - 2)));
        return record[4..];
    }

    private static void SetRegisterTypeState(
        TrRmRegisterBodyReader body, RegisterTypeState[] registerTypes,
        int register, RegisterTypeState state)
    {
        ConsumeWork(body);
        registerTypes[register] = state;
    }

    private static RegisterTypeState ReadRegisterTypeState(
        TrRmRegisterBodyReader body, RegisterTypeState[] registerTypes,
        int register)
    {
        ConsumeWork(body);
        return (uint)register < (uint)registerTypes.Length
            ? registerTypes[register]
            : RegisterTypeState.Unknown;
    }

    private static void ClearRegisterTypeStates(
        TrRmRegisterBodyReader body, RegisterTypeState[] registerTypes)
    {
        ConsumeWork(body, checked((ulong)registerTypes.Length));
        Array.Fill(registerTypes, RegisterTypeState.Unknown);
    }

    private static byte ReadByte(TrRmRegisterBodyReader body,
        ReadOnlySpan<byte> data, int offset)
    {
        ConsumeWork(body);
        return data[offset];
    }

    private static ushort ReadUInt16(TrRmRegisterBodyReader body,
        ReadOnlySpan<byte> data, int offset)
    {
        ConsumeWork(body);
        return ReadUInt16(data, offset);
    }

    private static uint ReadUInt32(TrRmRegisterBodyReader body,
        ReadOnlySpan<byte> data, int offset)
    {
        ConsumeWork(body);
        return ReadUInt32(data, offset);
    }

    private readonly ref struct ConstantSourceValueContext(
        TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmTypeDefTableReader typeDefs,
        TrRmFieldDefTableReader fields,
        TrRmEnumDefTableReader enumDefs,
        TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs,
        TrRmFixedArrayLayoutTableReader fixedArrayLayouts,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmTransparentConstantDomainTableReader transparentConstantDomains,
        HashSet<int> genericConstantFeatureTypeSpecRows)
    {
        internal TrRmFeatureTableReader Features { get; } = features;
        internal TrRmModuleTableReader Modules { get; } = modules;
        internal TrRmTypeDefTableReader TypeDefs { get; } = typeDefs;
        internal TrRmFieldDefTableReader Fields { get; } = fields;
        internal TrRmEnumDefTableReader EnumDefs { get; } = enumDefs;
        internal TrRmImportTableReader Imports { get; } = imports;
        internal TrRmConstantSourceTableReader ConstantSources { get; } = constantSources;
        internal TrRmFrozenValueNodeTableReader FrozenValueNodes { get; } = frozenValueNodes;
        internal TrRmBlobTableReader Blobs { get; } = blobs;
        internal TrRmFixedArrayLayoutTableReader FixedArrayLayouts { get; }
            = fixedArrayLayouts;
        internal TrRmTargetProfileTableReader TargetProfiles { get; }
            = targetProfiles;
        internal TrRmTransparentConstantDomainTableReader
            TransparentConstantDomains { get; } = transparentConstantDomains;
        internal HashSet<int> GenericConstantFeatureTypeSpecRows { get; }
            = genericConstantFeatureTypeSpecRows;
    }

    private readonly record struct PlaceHandleOrigin(
        int PlaceSelectionRecordIndex, int RootHomeIndex,
        int DefinitionCodeOffset);

    private readonly record struct ConstructionContextState(
        byte Phase, int InitializerMethodDefRow);

    private readonly record struct RegisterTypeState(int TypeMapIndex,
        uint TypeSpecRef, int PlaceSelectionRecordIndex, int RootHomeIndex,
        byte AccessKind, PlaceHandleOrigin[] PlaceOrigins)
    {
        internal static RegisterTypeState Unknown
            => new(-1, 0, -1, -1, 0, []);

        internal static RegisterTypeState FromTypeMap(int typeMapIndex)
            => new(typeMapIndex, 0, -1, -1, 0,
                []);

        internal static RegisterTypeState FromTypeSpecRef(uint typeSpecRef)
            => new(-1, typeSpecRef, -1, -1, 0,
                []);

        internal static RegisterTypeState FromPlaceTypeMap(int typeMapIndex,
            byte accessKind)
            => new(typeMapIndex, 0, -2, -1, accessKind,
                []);

        internal static RegisterTypeState FromPlace(uint typeSpecRef,
            int placeSelectionRecordIndex, byte accessKind,
            PlaceHandleOrigin[] origins)
        {
            var rootHomeIndex = origins.Length == 0
                ? -1 : origins[0].RootHomeIndex;
            var selectionIndex = origins.Length == 0
                ? -2 : origins[0].PlaceSelectionRecordIndex;
            foreach (var origin in origins)
            {
                if (origin.RootHomeIndex != rootHomeIndex)
                {
                    rootHomeIndex = -2;
                }
                if (origin.PlaceSelectionRecordIndex != selectionIndex)
                {
                    selectionIndex = -2;
                }
            }

            return new RegisterTypeState(-1, typeSpecRef, selectionIndex,
                rootHomeIndex, accessKind, origins);
        }

        internal RegisterTypeState WithPlaceOrigins(
            PlaceHandleOrigin[] origins)
        {
            var rootHomeIndex = origins.Length == 0
                ? -1 : origins[0].RootHomeIndex;
            var selectionIndex = origins.Length == 0
                ? -2 : origins[0].PlaceSelectionRecordIndex;
            foreach (var origin in origins)
            {
                if (origin.RootHomeIndex != rootHomeIndex)
                {
                    rootHomeIndex = -2;
                }
                if (origin.PlaceSelectionRecordIndex != selectionIndex)
                {
                    selectionIndex = -2;
                }
            }

            return this with {
                PlaceSelectionRecordIndex = selectionIndex,
                RootHomeIndex = rootHomeIndex,
                PlaceOrigins = origins,
            };
        }

        internal bool IsKnown => TypeMapIndex >= 0 || TypeSpecRef != 0;
        internal bool IsPlaceHandle => PlaceSelectionRecordIndex != -1;
    }

    private sealed class InitializationReferenceTypeMap
    {
        private readonly Dictionary<(int Hash, bool Writable),
            List<(uint ReferentTypeSpecRef, uint TypeSpecRef)>> _typesByReferent
                = [];
        private bool _isInitialized;

        internal bool TryGetTypeSpecRef(TrRmRegisterBodyReader body,
            TrRmTypeSpecTableReader typeSpecs, uint referentTypeSpecRef,
            bool writable, out uint typeSpecRef)
        {
            if (!_isInitialized)
            {
                for (var typeMapIndex = 0;
                    typeMapIndex < body.TypeCount; typeMapIndex++)
                {
                    ConsumeWork(body);
                    var candidateTypeSpecRef = GetTypeMapTypeSpecRef(body,
                        typeMapIndex);
                    var candidateType = typeSpecs.GetTypeSpecRef(
                        candidateTypeSpecRef);
                    if (candidateType.Form != 0x0E
                        || !candidateType.IsRow
                        || ((candidateType.Qualifiers & 1) != 0) != writable)
                    {
                        continue;
                    }

                    var candidateTypeBytes = typeSpecs.GetRow(
                        candidateType.Row);
                    var candidateReferentTypeSpecRef
                        = System.Buffers.Binary.BinaryPrimitives
                            .ReadUInt32LittleEndian(
                                candidateTypeBytes[2..]);
                    var key = (
                        typeSpecs.GetTypeSpecStructuralHash(
                            candidateReferentTypeSpecRef),
                        writable);
                    if (!_typesByReferent.TryGetValue(key,
                            out var candidates))
                    {
                        ConsumeWork(body);
                        candidates = [];
                        _typesByReferent.Add(key, candidates);
                    }

                    var isDuplicate = false;
                    foreach (var existing in candidates)
                    {
                        ConsumeWork(body);
                        if (typeSpecs.AreEquivalentTypeSpecRefs(
                            existing.ReferentTypeSpecRef,
                            candidateReferentTypeSpecRef))
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                    if (!isDuplicate)
                    {
                        ConsumeWork(body);
                        candidates.Add((candidateReferentTypeSpecRef,
                            candidateTypeSpecRef));
                    }
                }

                _isInitialized = true;
            }

            var lookupKey = (
                typeSpecs.GetTypeSpecStructuralHash(referentTypeSpecRef),
                writable);
            if (_typesByReferent.TryGetValue(lookupKey,
                    out var matchingCandidates))
            {
                foreach (var candidate in matchingCandidates)
                {
                    ConsumeWork(body);
                    if (typeSpecs.AreEquivalentTypeSpecRefs(
                        candidate.ReferentTypeSpecRef,
                        referentTypeSpecRef))
                    {
                        typeSpecRef = candidate.TypeSpecRef;
                        return true;
                    }
                }
            }

            typeSpecRef = 0;
            return false;
        }
    }

    internal static void Validate(TrRmRegisterBodyReader body, int methodDefRow,
        int ownerModule, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmEnumDefTableReader enumDefs,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmFieldDefTableReader fields, TrRmFieldPathTableReader fieldPaths,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceMethodTableReader interfaceMethods,
        TrRmInterfaceWitnessTableReader interfaceWitnesses,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs,
        TrRmContractTableReader contracts, TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors,
        TrRmNativeImportTableReader standardNativeImports,
        TrRmModuleRefTableReader moduleRefs,
        TrRmLayoutTableReader layouts,
        TrRmFixedArrayLayoutTableReader fixedArrayLayouts,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmTransparentConstantDomainTableReader transparentConstantDomains)
    {
        ConsumeWork(body);
        if (!body.Schema.IsDevelopmentProfile()
            && (body.FieldCount != 0 || body.HasTypeIdentityCarrier
                || body.HasLayoutQueryOperation)
            && !TrRmFeatureManifestValidator.Requires(features, modules,
                ownerModule, TrRmFeatureTableReader.LayoutQueriesFeatureId,
                body.Schema))
        {
            throw InvalidMap();
        }

        if (body.HasExceptionDirectory
            && !TrRmFeatureManifestValidator.Requires(features, modules,
                ownerModule,
                TrRmFeatureTableReader.RegisterExceptionRegionsFeatureId,
                body.Schema))
        {
            throw InvalidMap();
        }

        if (body.HasGenericCallTargets
            && !TrRmFeatureManifestValidator.Requires(features, modules,
                ownerModule,
                TrRmFeatureTableReader.GenericMethodCallsFeatureId,
                body.Schema))
        {
            throw InvalidMap();
        }

        if (body.HasRootPinOperations
            && !TrRmFeatureManifestValidator.Requires(features, modules,
                ownerModule, TrRmFeatureTableReader.RootPinRuntimeFeatureId,
                body.Schema))
        {
            throw new NotSupportedException(
                "A RootPin Body operation requires RootPinRuntime revision 1 in its using Module.");
        }

        var constructionRole = body.Schema.UsesObjectConstruction()
            && contracts.HasConstructionClause(methodDefRow)
            ? contracts.GetConstructionClause(methodDefRow).RoleTag
            : (byte)0;
        if ((body.HasConstructionContextHome
                || body.HasObjectConstructionRecords
                || constructionRole is 1 or 2)
            && (!body.Schema.UsesObjectConstruction()
                || !TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule,
                    TrRmFeatureTableReader
                        .ClassConstructionBodiesFeatureId,
                    body.Schema)))
        {
            throw InvalidMap();
        }

        ConsumeWork(body);
        var visitedTypeSpecRows = new HashSet<int>();
        ConsumeWork(body);
        var genericConstantFeatureTypeSpecRows = new HashSet<int>();
        var constantValues = new ConstantSourceValueContext(features, modules,
            types, fields, enumDefs, imports, constantSources, frozenValueNodes, blobs,
            fixedArrayLayouts, targetProfiles,
            transparentConstantDomains,
            genericConstantFeatureTypeSpecRows);
        ValidateGenericConstantCallFeatures(body, ownerModule, types,
            typeSpecs, genericParams, imports, constantValues);
        ConsumeWork(body);
        ValidateHomeMap(body, methodDefRow, ownerModule, types, methods, signatures,
            parameters, typeSpecs, genericParams, imports, constantValues,
            contracts, nativeFunctionPointerAnchors);
        ConsumeWork(body);
        ValidateObjectConstructionRecords(body, typeSpecs);
        ConsumeWork(body);
        ValidateGenericDirectReturnHomeBody(body, methodDefRow, methods,
            signatures, parameters, typeSpecs, genericParams, contracts);
        ConsumeWork(body);
        ValidateTypeMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, imports, moduleRefs, constantValues, visitedTypeSpecRows);
        ConsumeWork(body);
        ValidateExceptionDirectoryBindings(body, ownerModule, types, typeSpecs,
            genericParams, imports, constantValues, nativeFunctionPointerAnchors,
            interfaceWitnesses);
        ConsumeWork(body);
        ValidateMethodMap(body, ownerModule, methods, signatures, parameters,
            types, typeSpecs, genericParams, imports, moduleRefs, constantValues,
            visitedTypeSpecRows, nativeFunctionPointerAnchors);
        ConsumeWork(body);
        ValidateGenericCallTargets(body, methodDefRow, ownerModule, types,
            interfaces, interfaceMethods, typeSpecs, genericParams,
            constraints, methods, signatures, parameters, imports,
            moduleRefs, contracts, constantValues, visitedTypeSpecRows);
        ConsumeWork(body);
        ValidateFieldMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, fields, fieldPaths, imports, moduleRefs, constantValues,
            visitedTypeSpecRows);
        ConsumeWork(body);
        ValidateConstantMap(body, ownerModule, types, typeSpecs, genericParams,
            methods, imports, moduleRefs, constantValues, visitedTypeSpecRows);
        ConsumeWork(body);
        var initializationReferenceTypes
            = new InitializationReferenceTypeMap();
        var registerTypeEntries = ValidateCallSiteEffects(body, methodDefRow,
            types, fields, ref typeSpecs, methods, signatures,
            parameters, genericParams, constraints, contracts, imports,
            nativeFunctionPointerAnchors,
            constantValues, initializationReferenceTypes);
        ConsumeWork(body);
        ValidateCallSiteHomeTypes(body, methodDefRow, ownerModule, types, typeSpecs,
            fields, fieldPaths, layouts, genericParams, constraints,
            methods, signatures,
            parameters, imports,
            constantValues, contracts, features, modules, standardNativeImports,
            nativeFunctionPointerAnchors,
            registerTypeEntries, initializationReferenceTypes);
        ConsumeWork(body);
        ValidateObjectConstructionLifecycle(body, methodDefRow, ownerModule,
            types, fields, methods, signatures, parameters, genericParams,
            constraints, imports, ref typeSpecs, constantValues,
            initializationReferenceTypes,
            nativeFunctionPointerAnchors, registerTypeEntries, contracts);
        ConsumeWork(body);
        ValidateNativeOutputReservationFlow(body);
        ConsumeWork(body);
        ValidateNativeAddressRecords(body, ownerModule, features, modules,
            types, typeSpecs, genericParams, methods, signatures, parameters,
            contracts, imports, standardNativeImports,
            nativeFunctionPointerAnchors);
        ConsumeWork(body);
        ValidateStorageAddressRecords(body, ownerModule, features, modules,
            typeSpecs, layouts, constantValues.FixedArrayLayouts);
        ConsumeWork(body);
        ValidateFixedArrayPlaceSelections(body, ownerModule, features,
            modules, types, fields, methods, methodDefRow, imports,
            typeSpecs, constantValues);
    }

    private static void ValidateExceptionDirectoryBindings(
        TrRmRegisterBodyReader body, int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors,
        TrRmInterfaceWitnessTableReader interfaceWitnesses)
    {
        if (!body.HasExceptionDirectory)
        {
            return;
        }

        var directory = body.ExceptionDirectory;
        for (var handlerIndex = 0;
            handlerIndex < directory.HandlerCount; handlerIndex++)
        {
            ConsumeWork(body);
            var handler = directory.GetHandler(handlerIndex);
            if (handler.HasBinding == 0)
            {
                continue;
            }

            var home = GetHomeMapEntry(body, handler.BindingHomeIndex);
            if (home[0] != 3
                || !AreEquivalentTypeMapEntries(body,
                    GetHomeTypeMapIndex(body, handler.BindingHomeIndex),
                    handler.CatchTypeMapIndex, ownerModule, types, typeSpecs,
                    genericParams, imports, constantValues,
                    nativeFunctionPointerAnchors))
            {
                throw InvalidMap();
            }
        }

        for (var handlerIndex = 0;
            handlerIndex < directory.HandlerCount; handlerIndex++)
        {
            ConsumeWork(body);
            var handler = directory.GetHandler(handlerIndex);
            if (handler.CatchKind != 1)
            {
                continue;
            }

            var witnessRow = checked((int)handler.ExceptionWitnessRow);
            if (witnessRow > interfaceWitnesses.Count
                || !MatchesTypeMapEntry(body, handler.CatchTypeMapIndex,
                    interfaceWitnesses.GetImplementingTypeSpecRef(witnessRow),
                    ownerModule, types, typeSpecs, genericParams, imports,
                    constantValues, nativeFunctionPointerAnchors))
            {
                throw InvalidMap();
            }
        }
    }

    private static bool AreEquivalentTypeMapEntries(
        TrRmRegisterBodyReader body, int leftTypeMapIndex,
        int rightTypeMapIndex, int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var leftToken = GetTypeMapToken(body, leftTypeMapIndex);
        var rightToken = GetTypeMapToken(body, rightTypeMapIndex);
        if (leftToken == rightToken)
        {
            return true;
        }

        var leftKind = (byte)(leftToken >> 24);
        if (leftKind is 0x11 or 0xF2 or 0xF3)
        {
            return MatchesTypeMapEntry(body, rightTypeMapIndex,
                GetTypeMapTypeSpecRef(body, leftTypeMapIndex), ownerModule,
                types, typeSpecs, genericParams, imports, constantValues,
                nativeFunctionPointerAnchors);
        }

        var rightKind = (byte)(rightToken >> 24);
        return rightKind is 0x11 or 0xF2 or 0xF3
            && MatchesTypeMapEntry(body, leftTypeMapIndex,
                GetTypeMapTypeSpecRef(body, rightTypeMapIndex), ownerModule,
                types, typeSpecs, genericParams, imports, constantValues,
                nativeFunctionPointerAnchors);
    }

    private static void ValidateHomeMap(TrRmRegisterBodyReader body, int methodDefRow,
        int ownerModule,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports, ConstantSourceValueContext constantValues,
        TrRmContractTableReader contracts,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var signature = methods.GetSignature(methodDefRow);
        var (firstParameter, parameterCount) = signatures.GetParamRange(signature);
        var receiverType = signatures.GetReceiverTypeSpec(signature);
        var hasReceiver = receiverType != 0;
        var isConstructionInitializer
            = body.Schema.UsesConstructionContracts()
                && contracts.HasConstructionClause(methodDefRow)
                && contracts.GetConstructionClause(methodDefRow).RoleTag
                    == 1;
        ConsumeWork(body);
        var formalOrdinals = new HashSet<int>();
        var hasReceiverHome = false;
        for (var home = 0; home < body.HomeCount; home++)
        {
            ConsumeWork(body, 1UL + 8UL);
            var data = body.HomeMap.Slice(home * 8, 8);
            var role = ReadByte(body, data, 0);
            var flags = ReadByte(body, data, 1);
            var typeMapIndex = ReadUInt16(body, data, 2);
            var formalOrdinal = ReadUInt16(body, data, 4);
            if (role is < 1 or > 4 || typeMapIndex >= body.TypeCount
                || ReadUInt16(body, data, 6) != 0)
            {
                throw InvalidMap();
            }

            switch (role)
            {
                case 1:
                    ConsumeWork(body);
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
                    if (flags != 0 || formalOrdinal >= parameterCount)
                    {
                        throw InvalidMap();
                    }
                    ConsumeWork(body);
                    if (!formalOrdinals.Add(formalOrdinal))
                    {
                        throw InvalidMap();
                    }
                    ConsumeWork(body);
                    var parameterRow = firstParameter + formalOrdinal;
                    var parameterTypeSpec = parameters.GetTypeSpec(
                        parameterRow);
                    if ((parameters.GetFlags(parameterRow) & 1) != 0
                        && !typeSpecs
                            .TryGetImplicitReadonlyReferenceReferentTypeSpecRef(
                                parameterTypeSpec,
                                out parameterTypeSpec))
                    {
                        throw InvalidMap();
                    }

                    if (!MatchesTypeMapEntry(body, typeMapIndex,
                        parameterTypeSpec, ownerModule, types, typeSpecs,
                        genericParams, imports, constantValues,
                        nativeFunctionPointerAnchors))
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
                case 4:
                    if (flags != 0 || formalOrdinal != 0)
                    {
                        throw InvalidMap();
                    }
                    ValidateConstructionContextHome(body, methodDefRow,
                        ownerModule, typeMapIndex, types, methods,
                        typeSpecs, imports, contracts);
                    break;
            }
        }

        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            ConsumeWork(body);
            var code = body.GetBlockCode(blockIndex);
            var position = 0;
            while (position < code.Length)
            {
                ConsumeWork(body, 2);
                var opcode = code[position];
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        body.Schema, body.UsesNativePointerLoads);
                ConsumeWork(body);
                ConsumeWork(body, checked((ulong)instructionLength));
                var instruction = code.Slice(position, instructionLength);
                var directHomeIndex = opcode switch
                {
                    0x05 or 0x07 => 0,
                    0x06 or 0x08 => 1,
                    0x13 or 0x15 or 0x60
                        => ReadByte(body, instruction, 1) >> 4,
                    0x14 => ReadByte(body, instruction, 1) & 0x0F,
                    0xB8 => ReadByte(body, instruction, 1),
                    0xD5 or 0xD6 or 0xD7
                        => ReadUInt16(body, instruction, 2),
                    0xEB => ReadUInt16(body, instruction, 2),
                    _ => -1,
                };
                if (directHomeIndex >= 0
                    && (ReadByte(body,
                            GetHomeMapEntry(body, directHomeIndex), 0) == 4
                        || (isConstructionInitializer
                            && ReadByte(body, GetHomeMapEntry(body,
                                directHomeIndex), 0) == 1)))
                {
                    throw InvalidMap();
                }

                var switchRecordIndex = opcode switch
                {
                    0xB2 => ReadByte(body, instruction, 1),
                    0xE6 => ReadUInt16(body, instruction, 2),
                    _ => -1,
                };
                if (switchRecordIndex >= 0)
                {
                    var switchPayload = GetRecordPayload(body,
                        switchRecordIndex);
                    if (ReadByte(body, switchPayload, 0) == 2)
                    {
                        var switchHome = GetHomeMapEntry(body,
                            ReadUInt16(body, switchPayload, 2));
                        var switchHomeRole = ReadByte(body, switchHome, 0);
                        if (switchHomeRole == 4
                            || (isConstructionInitializer
                                && switchHomeRole == 1))
                        {
                            throw InvalidMap();
                        }
                    }
                }

                var selectionIndex = opcode switch
                {
                    0xD8 => ReadUInt16(body, instruction, 2),
                    0x91 or 0x61 => ReadByte(body, instruction, 1) >> 4,
                    _ => -1,
                };
                if (selectionIndex >= 0)
                {
                    var selection = body.GetRecord(selectionIndex);
                    ConsumeWork(body, 2);
                    if (selection[0] == 0x04)
                    {
                        var payload = selection[4..];
                        var instructionOperand
                            = ReadByte(body, instruction, 1);
                        ConsumeWork(body,
                            checked((ulong)payload.Length));
                        if (ReadByte(body, payload, 9) == 3
                            && (opcode is not (0xD8 or 0x91)
                                || ReadByte(body, payload, 2) != 1
                                || ReadUInt16(body, payload, 4)
                                    != (instructionOperand & 0x0F)))
                        {
                            throw InvalidMap();
                        }
                    }

                }

                if (opcode is 0x14 or 0xD6)
                {
                    var sourceRegister = opcode == 0x14
                        ? ReadByte(body, instruction, 1) >> 4
                        : ReadByte(body, instruction, 1) & 0x0F;
                    var homeIndex = opcode == 0x14
                        ? ReadByte(body, instruction, 1) & 0x0F
                        : ReadUInt16(body, instruction, 2);
                    var home = GetHomeMapEntry(body, homeIndex);
                    var homeType = GetTypeMapTypeSpecRef(body,
                        ReadUInt16(body, home, 2));
                    if (IsUnitTypeSpecRef(typeSpecs, homeType)
                        && (!body.Schema
                                .UsesForeignInvocationStorageFixedArrayUnitInitialization()
                            || sourceRegister != 14
                            || ReadByte(body, home, 0) != 3))
                    {
                        throw InvalidMap();
                    }
                }

                position += instructionLength;
            }
        }
    }

    private static void ValidateConstructionContextHome(
        TrRmRegisterBodyReader body, int methodDefRow, int ownerModule,
        int typeMapIndex, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmImportTableReader imports,
        TrRmContractTableReader contracts)
    {
        var ownerType = methods.GetOwnerType(methodDefRow);
        var callerRole = contracts.HasConstructionClause(methodDefRow)
            ? contracts.GetConstructionClause(methodDefRow).RoleTag
            : (byte)0;
        var allowsCallerEntry = body.Schema.UsesPropertyInitializedState()
            && body.Schema.UsesObjectConstruction();
        if (!body.Schema.UsesObjectConstruction()
            || methods.GetOwnerModule(methodDefRow) != ownerModule
            || (!allowsCallerEntry && callerRole != 2)
            || (callerRole == 2 && ownerType <= 0)
            || (callerRole == 2
                && types.GetOwnerModule(ownerType) != ownerModule))
        {
            throw InvalidMap();
        }

        if (!TryGetNormalClassHandleTypeSpec(
                GetTypeMapTypeSpecRef(body, typeMapIndex), types, typeSpecs,
                imports, out var contextTypeDef,
                out var contextQualifiers))
        {
            throw InvalidMap();
        }

        if (contextTypeDef <= 0 || contextTypeDef > types.Count
            || types.GetKind(contextTypeDef) != 2
            || types.GetOwnerModule(contextTypeDef) != ownerModule
            || (callerRole == 2 && contextTypeDef != ownerType)
            || !types.TryGetDeclaredClassMutability(contextTypeDef,
                out var isMutable)
            || contextQualifiers != (isMutable ? (byte)0x01 : (byte)0))
        {
            throw InvalidMap();
        }
    }

    private static void ValidateObjectConstructionRecords(
        TrRmRegisterBodyReader body, TrRmTypeSpecTableReader typeSpecs)
    {
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            ConsumeWork(body);
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x11 || record[4] != 2 || record[5] != 2)
            {
                continue;
            }

            var payload = record[4..];
            var contextHome = GetHomeMapEntry(body,
                ReadUInt16(body, payload, 2));
            var destinationHome = GetHomeMapEntry(body,
                ReadUInt16(body, payload, 4));
            var contextTypeSpecRef = GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, contextHome, 2));
            var destinationTypeSpecRef = GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, destinationHome, 2));
            if (!typeSpecs.AreEquivalentTypeSpecRefs(contextTypeSpecRef,
                    destinationTypeSpecRef))
            {
                throw InvalidMap();
            }
        }
    }

    private static void ValidateObjectConstructionLifecycle(
        TrRmRegisterBodyReader body, int methodDefRow, int ownerModule,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmImportTableReader imports,
        ref TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues,
        InitializationReferenceTypeMap initializationReferenceTypes,
        TrRmNativeFunctionPointerAnchorTableReader
            nativeFunctionPointerAnchors,
        RegisterTypeState[][] registerTypeEntries,
        TrRmContractTableReader contracts)
    {
        if (!body.Schema.UsesObjectConstruction())
        {
            return;
        }

        var construction = contracts.HasConstructionClause(methodDefRow)
            ? contracts.GetConstructionClause(methodDefRow) : default;
        var isProjection = construction.RoleTag == 2;
        var hasConstructionFacts = isProjection
            || body.HasConstructionContextHome
            || body.HasObjectConstructionRecords;
        if (!hasConstructionFacts)
        {
            return;
        }

        var requiresPublishedResult = isProjection
            && (contracts.GetEffectsMask(methodDefRow) & 2) == 0;
        var blockCount = body.BlockCount;
        var successors = BuildBlockFlowSuccessors(body);
        var predecessors = new List<int>?[blockCount];
        for (var source = 0; source < blockCount; source++)
        {
            ConsumeWork(body);
            if (successors[source] is not { } targets)
            {
                continue;
            }

            foreach (var target in targets)
            {
                ConsumeWork(body);
                var targetPredecessors = predecessors[target];
                if (targetPredecessors is null)
                {
                    targetPredecessors = [];
                    predecessors[target] = targetPredecessors;
                }
                targetPredecessors.Add(source);
            }
        }

        ConsumeWork(body, checked((ulong)blockCount * 4UL + 1UL));
        var entryStates = new ImmutableDictionary<int,
            ConstructionContextState>[blockCount];
        var entryPublished = new bool[blockCount];
        var hasEntry = new bool[blockCount];
        var outputStates = new ImmutableDictionary<int,
            ConstructionContextState>[blockCount];
        var outputPublished = new bool[blockCount];
        var hasOutput = new bool[blockCount];
        var queued = new bool[blockCount];
        Array.Fill(entryStates,
            ImmutableDictionary<int, ConstructionContextState>.Empty);
        Array.Fill(outputStates,
            ImmutableDictionary<int, ConstructionContextState>.Empty);
        var worklist = new Queue<int>(blockCount);

        hasEntry[0] = true;
        entryStates[0]
            = ImmutableDictionary<int, ConstructionContextState>.Empty;
        EnqueueConstructionLifecycleBlock(body, worklist, queued, 0);
        ProcessConstructionLifecycleWorklist(body, successors, worklist,
            queued, entryStates, entryPublished, hasEntry,
            outputStates, outputPublished, hasOutput,
            requiresPublishedResult,
            methodDefRow, ownerModule, types, fields, methods, signatures,
            contracts, parameters, genericParams, constraints, imports,
            ref typeSpecs,
            constantValues,
            initializationReferenceTypes, nativeFunctionPointerAnchors,
            registerTypeEntries);

        while (true)
        {
            var seed = -1;
            for (var block = 0; block < blockCount; block++)
            {
                ConsumeWork(body);
                if (hasEntry[block])
                {
                    continue;
                }

                var hasUnvisitedPredecessor = false;
                if (predecessors[block] is { } blockPredecessors)
                {
                    foreach (var predecessor in blockPredecessors)
                    {
                        ConsumeWork(body);
                        if (!hasEntry[predecessor])
                        {
                            hasUnvisitedPredecessor = true;
                            break;
                        }
                    }
                }

                if (!hasUnvisitedPredecessor)
                {
                    seed = block;
                    break;
                }
            }

            if (seed < 0)
            {
                for (var block = 0; block < blockCount; block++)
                {
                    ConsumeWork(body);
                    if (!hasEntry[block])
                    {
                        seed = block;
                        break;
                    }
                }
            }

            if (seed < 0)
            {
                break;
            }

            hasEntry[seed] = true;
            entryStates[seed]
                = ImmutableDictionary<int, ConstructionContextState>.Empty;
            entryPublished[seed] = false;
            EnqueueConstructionLifecycleBlock(body, worklist, queued, seed);
            ProcessConstructionLifecycleWorklist(body, successors, worklist,
                queued, entryStates, entryPublished, hasEntry,
                outputStates, outputPublished, hasOutput,
                requiresPublishedResult,
                methodDefRow, ownerModule, types, fields, methods, signatures,
                contracts, parameters, genericParams, constraints, imports,
                ref typeSpecs,
                constantValues,
                initializationReferenceTypes, nativeFunctionPointerAnchors,
                registerTypeEntries);
        }
    }

    private static void EnqueueConstructionLifecycleBlock(
        TrRmRegisterBodyReader body, Queue<int> worklist,
        bool[] queued, int block)
    {
        if (queued[block])
        {
            return;
        }

        ConsumeWork(body);
        queued[block] = true;
        worklist.Enqueue(block);
    }

    private static void ValidateConstructionContextPlaceAccess(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        byte opcode,
        ImmutableDictionary<int, ConstructionContextState> state)
    {
        var selectionRecordIndex = opcode switch
        {
            0xD8 or 0xDA => ReadUInt16(body, instruction, 2),
            0x61 or 0x91 => ReadByte(body, instruction, 1) >> 4,
            _ => throw InvalidMap(),
        };
        var selection = GetRecordPayload(body, selectionRecordIndex);
        var targetKind = ReadByte(body, selection, 0);
        var baseKind = ReadByte(body, selection, 3);
        if (targetKind == 1
            && ReadByte(body,
                GetHomeMapEntry(body,
                    checked((int)ReadUInt32(body, selection, 12))), 0)
                is 1 or 4)
        {
            throw InvalidMap();
        }

        if (baseKind != 2)
        {
            return;
        }

        var contextHomeIndex = ReadUInt16(body, selection, 6);
        if (ReadByte(body, GetHomeMapEntry(body, contextHomeIndex), 0) != 4)
        {
            return;
        }

        var accessKind = ReadByte(body, selection, 1);
        var validContextAccess = accessKind == 1
            || (body.Schema.UsesPropertyInitializedState()
                && accessKind == 3);
        if (targetKind != 2 || !validContextAccess
            || !state.TryGetValue(contextHomeIndex,
                out var contextState)
            || contextState.Phase != 2
            || contextState.InitializerMethodDefRow <= 0)
        {
            throw InvalidMap();
        }
    }

    private static bool HasLiveConstructionPlace(
        TrRmRegisterBodyReader body,
        RegisterTypeState[] registerTypes, int contextHomeIndex)
    {
        for (var register = 0; register < registerTypes.Length; register++)
        {
            ConsumeWork(body);
            var state = ReadRegisterTypeState(body, registerTypes, register);
            if (!state.IsPlaceHandle)
            {
                continue;
            }

            foreach (var origin in state.PlaceOrigins)
            {
                ConsumeWork(body);
                if (origin.RootHomeIndex == contextHomeIndex)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ProcessConstructionLifecycleWorklist(
        TrRmRegisterBodyReader body, List<int>?[] successors,
        Queue<int> worklist, bool[] queued,
        ImmutableDictionary<int, ConstructionContextState>[] entryStates,
        bool[] entryPublished, bool[] hasEntry,
        ImmutableDictionary<int, ConstructionContextState>[] outputStates,
        bool[] outputPublished, bool[] hasOutput,
        bool requiresPublishedResult, int methodDefRow, int ownerModule,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmContractTableReader contracts,
        TrRmParamTableReader parameters,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmImportTableReader imports,
        ref TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues,
        InitializationReferenceTypeMap initializationReferenceTypes,
        TrRmNativeFunctionPointerAnchorTableReader
            nativeFunctionPointerAnchors,
        RegisterTypeState[][] registerTypeEntries)
    {
        while (worklist.TryDequeue(out var blockIndex))
        {
            ConsumeWork(body);
            queued[blockIndex] = false;
            var state = entryStates[blockIndex];
            var published = entryPublished[blockIndex];
            var registerTypes = CopyRegisterTypeStates(body,
                registerTypeEntries[blockIndex]);
            var block = body.GetBlock(blockIndex);
            var codeOffset = checked((int)ReadUInt32(body, block, 0));
            var code = body.GetBlockCode(blockIndex);
            var position = 0;
            while (position < code.Length)
            {
                ConsumeWork(body, 2);
                var opcode = ReadByte(body, code, position);
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        body.Schema, body.UsesNativePointerLoads);
                ConsumeWork(body,
                    checked((ulong)instructionLength));
                var instruction = code.Slice(position,
                    instructionLength);
                if (opcode == 0xED)
                {
                    var record = GetRecordPayload(body,
                        ReadUInt16(body, instruction, 2));
                    var contextHome = ReadUInt16(body, record, 2);
                    if (HasLiveConstructionPlace(body, registerTypes,
                            contextHome))
                    {
                        throw InvalidMap();
                    }
                    state.TryGetValue(contextHome, out var contextState);
                    switch (ReadByte(body, record, 0))
                    {
                        case 1:
                            if (contextState.Phase != 0)
                            {
                                throw InvalidMap();
                            }
                            state = state.SetItem(contextHome,
                                new ConstructionContextState(1, 0));
                            break;
                        case 2:
                            if (contextState.Phase != 2
                                || contextState.InitializerMethodDefRow <= 0)
                            {
                                throw InvalidMap();
                            }
                            state = state.Remove(contextHome);
                            published = true;
                            break;
                        case 3:
                            if (contextState.Phase != 1
                                && (!body.Schema
                                        .UsesPropertyInitializedState()
                                    || contextState.Phase != 2
                                    || contextState
                                        .InitializerMethodDefRow <= 0))
                            {
                                throw InvalidMap();
                            }
                            state = state.Remove(contextHome);
                            break;
                        default:
                            throw InvalidMap();
                    }
                }
                else if (opcode is 0xD8 or 0x61 or 0x91 or 0xDA)
                {
                    ValidateConstructionContextPlaceAccess(body,
                        instruction, opcode, state);
                }
                else if (opcode is 0x90 or 0xE0)
                {
                    var callSiteRecord = opcode == 0x90
                        ? ReadByte(body, instruction, 1)
                        : ReadUInt16(body, instruction, 2);
                    var callSite = GetRecordPayload(body, callSiteRecord);
                    if (ReadUInt32(body, callSite, 8) != 0
                        && ReadByte(body, callSite, 16) == 2
                        && ReadByte(body, callSite, 17) == 5)
                    {
                        var contextHome = ReadUInt16(body, callSite, 18);
                        if (ReadByte(body, GetHomeMapEntry(body,
                                contextHome), 0) == 4)
                        {
                            var targetMethod = GetCallSiteTargetMethod(body,
                                callSite, registerTypes, typeSpecs, methods,
                                imports, nativeFunctionPointerAnchors);
                            var targetRole = contracts.HasConstructionClause(
                                    targetMethod)
                                ? contracts.GetConstructionClause(
                                    targetMethod).RoleTag : (byte)0;
                            if (!state.TryGetValue(contextHome,
                                    out var contextState))
                            {
                                throw InvalidMap();
                            }

                            if (targetRole == 1)
                            {
                                if (contextState.Phase != 1
                                    || contextState.InitializerMethodDefRow != 0)
                                {
                                    throw InvalidMap();
                                }
                                state = state.SetItem(contextHome,
                                    new ConstructionContextState(2,
                                        targetMethod));
                            }
                            else if (targetRole == 3
                                && body.Schema.UsesPropertyInitializedState())
                            {
                                if (contextState.Phase != 2
                                    || contextState.InitializerMethodDefRow <= 0)
                                {
                                    throw InvalidMap();
                                }
                            }
                            else
                            {
                                throw InvalidMap();
                            }
                        }
                    }
                }
                if (opcode is 0x90 or 0xE0)
                {
                    var callSiteRecord = opcode == 0x90
                        ? ReadByte(body, instruction, 1)
                        : ReadUInt16(body, instruction, 2);
                    UpdateCallSiteRegisterTypes(body, callSiteRecord,
                        registerTypes, ref typeSpecs, methods, signatures,
                        genericParams, constraints, imports,
                        nativeFunctionPointerAnchors);
                }
                else
                {
                    UpdateRegisterTypeStates(body, instruction,
                        registerTypes, methodDefRow, methods, contracts,
                        types, fields,
                        imports, signatures, parameters, genericParams,
                        typeSpecs, constantValues,
                        initializationReferenceTypes,
                        nativeFunctionPointerAnchors,
                        checked(codeOffset + position),
                        allowIncompletePlaceFlow: false);
                }

                position += instructionLength;
            }

            if (hasOutput[blockIndex]
                && ConstructionStatesEqual(body,
                    outputStates[blockIndex], state)
                && outputPublished[blockIndex] == published)
            {
                continue;
            }

            ConsumeWork(body);
            outputStates[blockIndex] = state;
            outputPublished[blockIndex] = published;
            hasOutput[blockIndex] = true;
            if (successors[blockIndex] is not { } blockSuccessors)
            {
                var terminalOpcode = GetTerminalOpcode(body, blockIndex);
                if ((terminalOpcode is 0x17 or 0x18 or 0xB8)
                    && (state.Count != 0
                        || (requiresPublishedResult && !published)))
                {
                    throw InvalidMap();
                }
                continue;
            }

            foreach (var successor in blockSuccessors)
            {
                ConsumeWork(body);
                if (!hasEntry[successor])
                {
                    entryStates[successor] = state;
                    entryPublished[successor] = published;
                    hasEntry[successor] = true;
                    EnqueueConstructionLifecycleBlock(body, worklist,
                        queued, successor);
                    continue;
                }

                if (!ConstructionStatesEqual(body,
                        entryStates[successor], state))
                {
                    throw InvalidMap();
                }

                var mergedPublished
                    = entryPublished[successor] && published;
                if (mergedPublished != entryPublished[successor])
                {
                    entryPublished[successor] = mergedPublished;
                    EnqueueConstructionLifecycleBlock(body, worklist,
                        queued, successor);
                }

            }
        }
    }

    private static bool ConstructionStatesEqual(
        TrRmRegisterBodyReader body,
        ImmutableDictionary<int, ConstructionContextState> left,
        ImmutableDictionary<int, ConstructionContextState> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        ConsumeWork(body, checked((ulong)left.Count + 1UL));
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (contextHome, state) in left)
        {
            ConsumeWork(body);
            if (!right.TryGetValue(contextHome, out var otherState)
                || state != otherState)
            {
                return false;
            }
        }

        return true;
    }

    private static byte GetTerminalOpcode(
        TrRmRegisterBodyReader body, int blockIndex)
    {
        var code = body.GetBlockCode(blockIndex);
        var opcode = (byte)0;
        for (var position = 0; position < code.Length;)
        {
            ConsumeWork(body);
            opcode = ReadByte(body, code, position);
            position += TrRmRegisterBodyReader.InstructionLength(opcode,
                body.Schema, body.UsesNativePointerLoads);
        }

        return opcode;
    }

    private static bool MatchesTypeMapEntry(TrRmRegisterBodyReader body,
        int typeMapIndex, uint expectedType, int ownerModule,
        TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports, ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var token = GetTypeMapToken(body, typeMapIndex);
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
        typeSpecs.ResourceLedger?.ReserveWorkAtDepth(1, 1);
        var pending = new Stack<(uint Left, uint Right, ulong Depth)>();
        pending.Push((left, right, 1));
        ConsumeWork(typeSpecs);
        var visited = new HashSet<(uint Left, uint Right)>();
        while (pending.TryPop(out var pair))
        {
            ConsumeWork(typeSpecs);
            typeSpecs.ResourceLedger?.CheckDepthFromCurrent(pair.Depth);
            ConsumeWork(typeSpecs);
            if (pair.Left == pair.Right)
            {
                continue;
            }
            ConsumeWork(typeSpecs);
            if (visited.Contains((pair.Left, pair.Right)))
            {
                continue;
            }
            ConsumeWork(typeSpecs);
            _ = visited.Add((pair.Left, pair.Right));

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
                            var childDepth = checked(pair.Depth + 1);
                            typeSpecs.ResourceLedger?.ReserveWorkAtDepth(1,
                                childDepth);
                            pending.Push((leftArgument, rightArgument,
                                childDepth));
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
                    var unaryDepth = checked(pair.Depth + 1);
                    typeSpecs.ResourceLedger?.ReserveWorkAtDepth(1,
                        unaryDepth);
                    pending.Push((ReadUInt32(leftRow, 2),
                        ReadUInt32(rightRow, 2), unaryDepth));
                    break;

                case 0x0C:
                    if (!leftReference.IsRow || !rightReference.IsRow
                        || leftRow[2] != rightRow[2])
                    {
                        return false;
                    }
                    var indexedDepth = checked(pair.Depth + 1);
                    typeSpecs.ResourceLedger?.ReserveWorkAtDepth(1,
                        indexedDepth);
                    pending.Push((ReadUInt32(leftRow, 3),
                        ReadUInt32(rightRow, 3), indexedDepth));
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
                constantValues.EnumDefs, constantValues.Imports,
                typeSpecs.ResourceLedger, constantValues.Fields,
                constantValues.TransparentConstantDomains);
        var rightPayload = TrRmConstantSourceValueValidator
            .GetFrozenScalarPayload(rightRow, constantTypeSpecRef, ownerModule,
                constantValues.ConstantSources, constantValues.FrozenValueNodes,
                constantValues.Blobs, typeSpecs, constantValues.Features,
                constantValues.Modules, constantValues.TypeDefs,
                constantValues.EnumDefs, constantValues.Imports, null,
                constantValues.Fields,
                constantValues.TransparentConstantDomains);
        ConsumeWork(typeSpecs, checked((ulong)leftPayload.Length
            + (ulong)rightPayload.Length + 1UL));
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
            ConsumeWork(body, sizeof(uint) + 1);
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
            ConsumeWork(body, sizeof(uint) + 1);
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
            ConsumeWork(body, sizeof(uint) + 1);
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
        ConsumeWork(typeSpecs);
        ValidateTypeSpecModuleContext(fieldPaths.GetRootTypeSpecRef(row), ownerModule,
            types, typeSpecs, genericParams, methods, imports,
            moduleRefs, constantValues, visitedTypeSpecRows);
        ConsumeWork(typeSpecs);
        var fieldCount = fieldPaths.GetFieldCount(row);
        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            ConsumeWork(typeSpecs, sizeof(uint) + 1);
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
            ConsumeWork(body, 1UL + 8UL);
            var data = body.ConstantMap.Slice(index * 8, 8);
            var mapTypeSpecRef = ReadUInt32(body, data, 0);
            ValidateTypeSpecModuleContext(mapTypeSpecRef, ownerModule,
                types, typeSpecs, genericParams, methods, imports,
                moduleRefs, constantValues, visitedTypeSpecRows);
            var sourceRow = ReadUInt32(body, data, 4);
            if (sourceRow == 0
                || sourceRow > (uint)constantValues.ConstantSources.Count)
            {
                throw InvalidMap();
            }

            TrRmGenericConstantFeatureUseValidator.ValidateConstantSourceUse(
                checked((int)sourceRow), ownerModule,
                constantValues.ConstantSources, genericParams,
                constantValues.Features, constantValues.Modules, body.Schema);
            TrRmConstantSourceValueValidator.ValidateFrozenScalar(
                checked((int)sourceRow), mapTypeSpecRef, ownerModule,
                constantValues.ConstantSources, constantValues.FrozenValueNodes,
                constantValues.Blobs, typeSpecs, constantValues.Features,
                constantValues.Modules, constantValues.TypeDefs,
                constantValues.EnumDefs, constantValues.Imports, null,
                constantValues.Fields,
                constantValues.TransparentConstantDomains);
        }
    }

    private static void ValidateGenericConstantCallFeatures(
        TrRmRegisterBodyReader body, int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        ConstantSourceValueContext constantValues)
    {
        if (!body.Schema.UsesGenericConstantMethods()
            || !body.HasGenericCallTargets)
        {
            return;
        }

        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            ConsumeWork(body);
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x10)
            {
                continue;
            }

            TrRmGenericConstantFeatureUseValidator.ValidateGenericCall(
                record[4..], ownerModule, typeSpecs, types, genericParams,
                imports, constantValues.ConstantSources,
                constantValues.Features, constantValues.Modules, body.Schema,
                constantValues.GenericConstantFeatureTypeSpecRows);
        }
    }

    private static void ValidateGenericCallTargets(
        TrRmRegisterBodyReader body, int callerMethodRow, int ownerModule,
        TrRmTypeDefTableReader types,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceMethodTableReader interfaceMethods,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs,
        TrRmContractTableReader contracts,
        ConstantSourceValueContext constantValues,
        HashSet<int> visitedTypeSpecRows)
    {
        if (!body.Schema.UsesGenericMethodCalls())
        {
            return;
        }

        var callerSignature = methods.GetSignature(callerMethodRow);
        var (callerFormalFirst, callerFormalCount)
            = signatures.GetGenericRange(callerSignature);
        var callerDefinitionId = 0x1600_0000u
            | checked((uint)callerMethodRow);
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            ConsumeWork(body);
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x10)
            {
                continue;
            }

            var payload = record[4..];
            var targetRole = ReadByte(body, payload, 0);
            var evidenceKind = ReadByte(body, payload, 1);
            var methodMapIndex = ReadUInt16(body, payload, 2);
            var ownerTypeSpec = ReadUInt32(body, payload, 4);
            var evidenceReference = ReadUInt32(body, payload, 8);
            var argumentCount = ReadUInt32(body, payload, 12);
            if (targetRole == 2)
            {
                if (evidenceKind is 2 or 3)
                {
                    throw new NotSupportedException(
                        "The G1 consumer currently supports open Constraint evidence for interface-requirement GenericCalls only.");
                }
                if (evidenceKind != 1
                    || ownerTypeSpec == 0 || evidenceReference == 0)
                {
                    throw InvalidMap();
                }

                ValidateGenericInterfaceRequirementTarget(body,
                    callerMethodRow, ownerModule, methodMapIndex,
                    ownerTypeSpec, evidenceReference, argumentCount,
                    types, interfaces, interfaceMethods, typeSpecs,
                    genericParams, constraints, methods, signatures,
                    parameters, imports, contracts);
                continue;
            }

            if (targetRole != 1 || evidenceKind != 0
                || ownerTypeSpec != 0 || evidenceReference != 0)
            {
                throw new NotSupportedException(
                    "The G1 consumer currently supports direct module-level GenericCall targets without dispatch evidence.");
            }

            var targetMethodRow = GetMethodMapTarget(body, methodMapIndex,
                methods, imports);
            if (methods.GetOwnerModule(targetMethodRow) != ownerModule
                || methods.GetOwnerType(targetMethodRow) != 0)
            {
                throw new NotSupportedException(
                    "The G1 consumer currently supports module-level GenericCall targets in the owning Module only.");
            }

            if ((contracts.GetEffectsMask(targetMethodRow) & 4) != 0
                && (contracts.GetEffectsMask(callerMethodRow) & 4) == 0)
            {
                throw InvalidMap();
            }

            var targetSignature = methods.GetSignature(targetMethodRow);
            var (targetFormalFirst, targetFormalCount)
                = signatures.GetGenericRange(targetSignature);
            if (targetFormalCount == 0
                || argumentCount != (uint)targetFormalCount)
            {
                throw InvalidMap();
            }

            var targetDefinitionId = 0x1600_0000u
                | checked((uint)targetMethodRow);
            var argumentTypeSpecRefs = new uint[targetFormalCount];
            for (var ordinal = 0; ordinal < targetFormalCount; ordinal++)
            {
                ConsumeWork(body);
                var targetFormal = targetFormalFirst + ordinal;
                if (genericParams.GetOwnerDefId(targetFormal)
                        != targetDefinitionId
                    || genericParams.GetOrdinal(targetFormal)
                        != (uint)ordinal
                    || genericParams.GetFormalKind(targetFormal) != 1)
                {
                    throw InvalidMap();
                }

                var (constraintFirst, constraintCount)
                    = genericParams.GetConstraintRange(targetFormal);
                var argumentOffset = checked(16 + (ordinal * 8));
                if (ReadByte(body, payload, argumentOffset) != 1)
                {
                    throw new NotSupportedException(
                        "The G1 consumer currently supports TypeActual GenericCall arguments only.");
                }

                var argumentTypeSpec = ReadUInt32(body, payload,
                    argumentOffset + 4);
                argumentTypeSpecRefs[ordinal] = argumentTypeSpec;
                var actualFormal = ValidateGenericCallTypeActual(
                    argumentTypeSpec,
                    targetFormal, callerDefinitionId,
                    callerFormalFirst, callerFormalCount, ownerModule,
                    types, typeSpecs, genericParams, constraints, methods,
                    imports, moduleRefs, constantValues,
                    visitedTypeSpecRows);
                for (var constraintOffset = 0;
                    constraintOffset < constraintCount; constraintOffset++)
                {
                    var constraintRow = constraintFirst + constraintOffset;
                    var constraintKind = constraints.GetKind(constraintRow);
                    var constraintPayload
                        = constraints.GetPayload(constraintRow);
                    if (constraintKind == 2
                        && constraintPayload is 2 or 3)
                    {
                        continue;
                    }
                    if (constraintKind != 1)
                    {
                        throw new NotSupportedException(
                            "The G1 consumer currently supports interface-instance, Write, and Blittable constraints on direct GenericCall targets.");
                    }
                    if (actualFormal == 0)
                    {
                        throw new NotSupportedException(
                            "The G1 consumer requires a closed witness provider for a constrained direct GenericCall TypeActual.");
                    }

                    var satisfied = false;
                    var (firstActualConstraint, actualConstraintCount)
                        = genericParams.GetConstraintRange(actualFormal);
                    for (var actualConstraintOffset = 0;
                        actualConstraintOffset < actualConstraintCount;
                        actualConstraintOffset++)
                    {
                        var actualConstraintRow
                            = firstActualConstraint + actualConstraintOffset;
                        if (constraints.GetKind(actualConstraintRow) == 1
                            && GenericCallConstraintTypeSpecsMatch(
                                body, constraintPayload,
                                constraints.GetPayload(actualConstraintRow),
                                targetMethodRow, argumentTypeSpecRefs,
                                typeSpecs, genericParams))
                        {
                            satisfied = true;
                            break;
                        }
                    }
                    if (!satisfied)
                    {
                        throw InvalidMap();
                    }
                }
            }

            var (firstParameter, parameterCount)
                = signatures.GetParamRange(targetSignature);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                ConsumeWork(body);
                var parameter = firstParameter + ordinal;
                if (parameters.GetMode(parameter) != 0
                    || parameters.GetFlags(parameter) != 0)
                {
                    throw new NotSupportedException(
                        "The G1 consumer currently supports ordinary value parameters on direct GenericCall targets only.");
                }

                ValidateGenericCallSignatureType(
                    parameters.GetTypeSpec(parameter), targetDefinitionId,
                    targetFormalFirst, targetFormalCount,
                    typeSpecs, genericParams, constraints);
            }

            ValidateGenericCallSignatureType(
                signatures.GetResultTypeSpec(targetSignature),
                targetDefinitionId, targetFormalFirst,
                targetFormalCount, typeSpecs, genericParams,
                constraints,
                allowUnit: true);
        }
    }

    private static void ValidateGenericInterfaceRequirementTarget(
        TrRmRegisterBodyReader body, int callerMethodRow, int ownerModule,
        ushort methodMapIndex, uint ownerTypeSpec, uint evidenceReference,
        uint argumentCount, TrRmTypeDefTableReader types,
        TrRmInterfaceDefTableReader interfaces,
        TrRmInterfaceMethodTableReader interfaceMethods,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmImportTableReader imports,
        TrRmContractTableReader contracts)
    {
        if (!body.Schema.UsesMethodParameterLabels()
            || methods.GetOwnerType(callerMethodRow) != 0
            || argumentCount != 0)
        {
            throw new NotSupportedException(
                "Open interface-requirement GenericCall targets require G1L1 module-level methods with no requirement method formals.");
        }

        var callerDefinitionId = 0x1600_0000u
            | checked((uint)callerMethodRow);
        var callerSignature = methods.GetSignature(callerMethodRow);
        var (callerFormalFirst, callerFormalCount)
            = signatures.GetGenericRange(callerSignature);
        var evidenceRow = checked((int)evidenceReference);
        var evidenceOwner
            = constraints.GetOwnerGenericParam(evidenceRow);
        var (firstEvidenceConstraint, evidenceConstraintCount)
            = genericParams.GetConstraintRange(evidenceOwner);
        var evidenceOrdinal = genericParams.GetOrdinal(evidenceOwner);
        if (genericParams.GetOwnerDefId(evidenceOwner)
                != callerDefinitionId
            || genericParams.GetFormalKind(evidenceOwner) != 1
            || evidenceOrdinal >= (uint)callerFormalCount
            || evidenceOwner < callerFormalFirst
            || evidenceOwner >= callerFormalFirst + callerFormalCount
            || evidenceRow < firstEvidenceConstraint
            || evidenceRow >= firstEvidenceConstraint
                + evidenceConstraintCount
            || constraints.GetKind(evidenceRow) != 1)
        {
            throw InvalidMap();
        }

        var methodToken = ReadUInt32(body, body.MethodMap,
            checked(methodMapIndex * sizeof(uint)));
        var targetMethodRow = GetMethodMapTarget(body, methodMapIndex,
            methods, imports);
        var targetDefinitionId = 0x1600_0000u
            | checked((uint)targetMethodRow);
        if (((methodToken >> 24) == 0x16
                && imports.IsExpectedDefinition(targetDefinitionId))
            || methods.GetOwnerModule(targetMethodRow) != ownerModule)
        {
            throw InvalidMap();
        }

        var ownerTypeDef = methods.GetOwnerType(targetMethodRow);
        if (ownerTypeDef == 0 || types.GetKind(ownerTypeDef) != 3
            || types.GetOwnerModule(ownerTypeDef) != ownerModule)
        {
            throw new NotSupportedException(
                "The G1 interface-requirement target must be an interface MethodDef in the owning Module.");
        }

        var interfaceDef
            = interfaces.GetInterfaceDefForTypeDef(ownerTypeDef);
        if (interfaceDef == 0
            || interfaceMethods.GetOwnerInterfaceDefForMethod(
                targetMethodRow) != interfaceDef)
        {
            throw InvalidMap();
        }
        if (interfaces.GetBaseRange(interfaceDef).Count != 0
            || interfaces.GetAssociatedTypeRange(interfaceDef).Count != 0
            || interfaces.GetAssociatedConstRange(interfaceDef).Count != 0
            || interfaces.GetInterfaceConstRange(interfaceDef).Count != 0)
        {
            throw new NotSupportedException(
                "The G1 interface-requirement target has an unsupported interface closure.");
        }

        var ownerType = typeSpecs.GetTypeSpecRef(ownerTypeSpec);
        if (!ownerType.IsRow || ownerType.Qualifiers != 0
            || ownerType.Form is not (0x02 or 0x03 or 0x05)
            || typeSpecs.GetNominalTypeDefRow(ownerTypeSpec, types,
                imports) != ownerTypeDef)
        {
            throw InvalidMap();
        }

        var (firstOwnerFormal, ownerFormalCount)
            = types.GetGenericRange(ownerTypeDef);
        if (ownerFormalCount == 0
            ? ownerType.Form is not (0x02 or 0x03)
            : ownerType.Form != 0x05)
        {
            throw InvalidMap();
        }
        if (ownerFormalCount != 0)
        {
            var ownerTypeRow = typeSpecs.GetRow(ownerType.Row);
            if ((ownerTypeRow.Length - 6) / sizeof(uint)
                != ownerFormalCount)
            {
                throw InvalidMap();
            }
        }
        var ownerDefinitionId = 0x1300_0000u
            | checked((uint)ownerTypeDef);
        for (var ordinal = 0; ordinal < ownerFormalCount; ordinal++)
        {
            var formal = firstOwnerFormal + ordinal;
            if (genericParams.GetOwnerDefId(formal)
                    != ownerDefinitionId
                || genericParams.GetFormalKind(formal) != 1
                || genericParams.GetOrdinal(formal) != (uint)ordinal
                || genericParams.GetOwnershipDomain(formal) != 1
                || genericParams.GetNullabilityDomain(formal) != 1
                || genericParams.GetConstraintRange(formal).Count != 0
                || genericParams.GetDefaultArgumentRef(formal) != 0)
            {
                throw new NotSupportedException(
                    "The G1 interface-requirement target has an unsupported owner formal contract.");
            }
        }

        var evidenceTypeSpec = constraints.GetPayload(evidenceRow);
        if (!typeSpecs.AreEquivalentTypeSpecRefs(ownerTypeSpec,
                evidenceTypeSpec))
        {
            throw InvalidMap();
        }

        var targetSignature = methods.GetSignature(targetMethodRow);
        var (_, targetFormalCount)
            = signatures.GetGenericRange(targetSignature);
        var (firstParameter, parameterCount)
            = signatures.GetParamRange(targetSignature);
        var (_, contractCount)
            = methods.GetContractRange(targetMethodRow);
        if (targetFormalCount != 0
            || signatures.GetReceiverTypeSpec(targetSignature) == 0
            || methods.GetBodyRow(targetMethodRow) != 0
            || (methods.GetFlags(targetMethodRow) & 3) != 2
            || contractCount != 0)
        {
            throw new NotSupportedException(
                "The G1 interface-requirement MethodDef is outside the bodyless value-callable subset.");
        }
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var parameter = firstParameter + ordinal;
            if (parameters.GetMode(parameter) != 0
                || parameters.GetFlags(parameter) != 0)
            {
                throw new NotSupportedException(
                    "The G1 interface-requirement MethodDef has an unsupported parameter mode.");
            }
            ValidateGenericInterfaceRequirementSignatureType(
                parameters.GetTypeSpec(parameter), ownerDefinitionId,
                firstOwnerFormal, ownerFormalCount, allowVoid: false,
                typeSpecs, genericParams);
        }
        ValidateGenericInterfaceRequirementSignatureType(
            signatures.GetResultTypeSpec(targetSignature),
            ownerDefinitionId, firstOwnerFormal, ownerFormalCount,
            allowVoid: true, typeSpecs, genericParams);
    }

    private static void ValidateGenericCallInterfaceReceiverInput(
        TrRmRegisterBodyReader body, int callerMethodRow,
        int targetMethodRow, ReadOnlySpan<byte> callSitePayload,
        int inputOffset, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters)
    {
        var targetRecordIndex = ReadUInt16(body, callSitePayload, 4);
        var target = GetRecordPayload(body, targetRecordIndex);
        var evidenceRow = checked((int)ReadUInt32(body, target, 8));
        if (ReadByte(body, target, 0) != 2
            || ReadByte(body, target, 1) != 1
            || constraints.GetKind(evidenceRow) != 1
            || ReadByte(body, callSitePayload, 0) != 4
            || methods.GetOwnerType(targetMethodRow) == 0
            || signatures.GetReceiverTypeSpec(
                methods.GetSignature(targetMethodRow)) == 0)
        {
            throw InvalidMap();
        }

        var evidenceOwner = constraints.GetOwnerGenericParam(evidenceRow);
        var callerDefinitionId = 0x1600_0000u
            | checked((uint)callerMethodRow);
        var (callerFormalFirst, callerFormalCount)
            = signatures.GetGenericRange(
                methods.GetSignature(callerMethodRow));
        if (genericParams.GetOwnerDefId(evidenceOwner)
                != callerDefinitionId
            || genericParams.GetFormalKind(evidenceOwner) != 1
            || genericParams.GetOwnershipDomain(evidenceOwner) != 1
            || evidenceOwner < callerFormalFirst
            || evidenceOwner >= callerFormalFirst + callerFormalCount)
        {
            throw InvalidMap();
        }

        var requiresWritableReceiver
            = (methods.GetFlags(targetMethodRow) & 4) != 0;
        if (requiresWritableReceiver
            && !HasGenericMethodPropertyConstraint(evidenceOwner, 2,
                genericParams, constraints))
        {
            throw InvalidMap();
        }

        var expectedReceiverSourceUse = (byte)1;
        if (body.Schema.UsesMethodReceiverAccess())
        {
            expectedReceiverSourceUse = requiresWritableReceiver
                ? (byte)4 : (byte)3;
        }
        if (ReadByte(body, callSitePayload, inputOffset) != 2
            || ReadByte(body, callSitePayload, inputOffset + 1)
                != expectedReceiverSourceUse)
        {
            throw InvalidMap();
        }

        var homeIndex = ReadUInt16(body, callSitePayload, inputOffset + 2);
        var home = GetHomeMapEntry(body, homeIndex);
        var parameterOrdinal = ReadUInt16(body, home, 4);
        if (ReadByte(body, home, 0) != 2)
        {
            throw InvalidMap();
        }

        var homeTypeSpecRef = GetTypeMapTypeSpecRef(body,
            GetHomeTypeMapIndex(body, homeIndex));
        var homeType = typeSpecs.GetTypeSpecRef(homeTypeSpecRef);
        if (!homeType.IsRow || homeType.Form != 0x04
            || homeType.Qualifiers != 0)
        {
            throw new NotSupportedException(
                "The G1 interface-requirement receiver must be an in-scope type formal.");
        }

        var homeFormal = checked((int)ReadUInt32(
            typeSpecs.GetRow(homeType.Row), 2));
        var signature = methods.GetSignature(callerMethodRow);
        var (firstParameter, parameterCount)
            = signatures.GetParamRange(signature);
        if (homeFormal != evidenceOwner
            || parameterOrdinal >= parameterCount
            || parameters.GetMode(firstParameter + parameterOrdinal) != 0
            || !typeSpecs.AreEquivalentTypeSpecRefs(homeTypeSpecRef,
                parameters.GetTypeSpec(firstParameter + parameterOrdinal)))
        {
            throw InvalidMap();
        }
    }

    private static void ValidateGenericInterfaceRequirementSignatureType(
        uint typeSpecRef, uint ownerDefinitionId, int firstOwnerFormal,
        int ownerFormalCount, bool allowVoid,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (type.Qualifiers != 0)
        {
            throw new NotSupportedException(
                "The G1 interface-requirement signature has qualified types.");
        }

        if (type.Form == 0x01
            && (type.IsRow
                ? typeSpecs.GetRow(type.Row).Length == 3
                : (type.Value & 0xFF00_0000) == 0xF200_0000)
            && (allowVoid || type.TypeCode != 0))
        {
            return;
        }

        if (type.Form == 0x04 && type.IsRow
            && typeSpecs.GetRow(type.Row).Length == 6)
        {
            var formalRow = checked((int)ReadUInt32(
                typeSpecs.GetRow(type.Row), 2));
            var ordinal = genericParams.GetOrdinal(formalRow);
            if (genericParams.GetOwnerDefId(formalRow)
                    == ownerDefinitionId
                && genericParams.GetFormalKind(formalRow) == 1
                && ordinal < (uint)ownerFormalCount
                && formalRow >= firstOwnerFormal
                && formalRow < firstOwnerFormal + ownerFormalCount
                && ordinal == checked((uint)(formalRow
                    - firstOwnerFormal)))
            {
                return;
            }
        }

        throw new NotSupportedException(
            "The G1 interface-requirement signature is outside the scalar/owner-formal subset.");
    }

    private static void ValidateGenericDirectReturnHomeBody(
        TrRmRegisterBodyReader body, int methodDefRow,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmContractTableReader contracts)
    {
        if (!body.Schema.UsesGenericMethodCalls()
            || body.BlockCount != 1 || body.RecordCount != 1)
        {
            return;
        }

        var code = body.GetBlockCode(0);
        if (code.Length != 2 || code[0] != 0xB8)
        {
            return;
        }

        var home = GetHomeMapEntry(body, code[1]);
        var formalOrdinal = ReadUInt16(body, home, 4);
        var signature = methods.GetSignature(methodDefRow);
        var (firstParameter, parameterCount)
            = signatures.GetParamRange(signature);
        if (home[0] != 2 || formalOrdinal >= parameterCount
            || (contracts.GetEffectsMask(methodDefRow) & 2) != 0)
        {
            throw InvalidMap();
        }

        var parameterType = parameters.GetTypeSpec(
            firstParameter + formalOrdinal);
        if (!typeSpecs.AreEquivalentTypeSpecRefs(parameterType,
                signatures.GetResultTypeSpec(signature)))
        {
            throw InvalidMap();
        }

        var reference = typeSpecs.GetTypeSpecRef(parameterType);
        if (reference.Form == 0x04)
        {
            var genericParamRow = checked((int)ReadUInt32(
                typeSpecs.GetRow(reference.Row), 2));
            if (genericParams.GetOwnerDefId(genericParamRow)
                    != (0x1600_0000u | checked((uint)methodDefRow))
                || genericParams.GetFormalKind(genericParamRow) != 1
                || genericParams.GetOwnershipDomain(genericParamRow)
                    is not (1 or 2))
            {
                throw InvalidMap();
            }
        }
        else if (reference.Form != 0x01 || reference.TypeCode == 0)
        {
            throw new NotSupportedException(
                "The G1 consumer currently supports scalar or direct-formal ReturnHome Bodies only.");
        }
    }

    private readonly record struct GenericCallTypeActualFacts(
        byte? OwnershipDomain, byte NullabilityDomain,
        bool RequiresWritable, bool RequiresBlittable, int FormalRow,
        bool IsWritableQualifier, bool IsInitializationQualifier);

    private static int ValidateGenericCallTypeActual(
        uint typeSpecRef, int targetFormal, uint callerDefinitionId,
        int callerFormalFirst, int callerFormalCount, int ownerModule,
        TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmMethodDefTableReader methods,
        TrRmImportTableReader imports,
        TrRmModuleRefTableReader moduleRefs,
        ConstantSourceValueContext constantValues,
        HashSet<int> visitedTypeSpecRows)
    {
        var actual = ResolveGenericCallTypeActualFacts(typeSpecRef,
            callerDefinitionId, callerFormalFirst, callerFormalCount,
            typeSpecs, genericParams, constraints, allowInitQualifier: false,
            depth: 0);
        if (actual.OwnershipDomain is not byte actualOwnership)
        {
            throw InvalidMap();
        }

        if (!SemanticVerifier.IsGenericMethodFormalContractSatisfied(
                genericParams.GetOwnershipDomain(targetFormal),
                genericParams.GetNullabilityDomain(targetFormal),
                HasGenericMethodPropertyConstraint(targetFormal, 2,
                    genericParams, constraints),
                HasGenericMethodPropertyConstraint(targetFormal, 3,
                    genericParams, constraints),
                actualOwnership, actual.NullabilityDomain,
                actual.RequiresWritable, actual.RequiresBlittable))
        {
            throw InvalidMap();
        }

        ValidateTypeSpecModuleContext(typeSpecRef, ownerModule, types,
            typeSpecs, genericParams, methods, imports, moduleRefs,
            constantValues, visitedTypeSpecRows);
        return actual.FormalRow;
    }

    private static GenericCallTypeActualFacts ResolveGenericCallTypeActualFacts(
        uint typeSpecRef, uint callerDefinitionId,
        int callerFormalFirst, int callerFormalCount,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints, bool allowInitQualifier,
        int depth)
    {
        ConsumeWork(typeSpecs);
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw new NotSupportedException(
                "The G1 GenericCall TypeActual exceeds the supported depth.");
        }

        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: allowInitQualifier);
        var isWritable = (reference.Qualifiers & 0x01) != 0;
        var isGarbageCollected = (reference.Qualifiers & 0x02) != 0;
        var isInitialization = (reference.Qualifiers & 0x04) != 0;
        if (isGarbageCollected)
        {
            throw new NotSupportedException(
                "G1 GenericCall TypeActuals do not support GC-backed types.");
        }

        if (reference.Form == 0x01 && !reference.IsRow)
        {
            if (reference.TypeCode == 0 || isInitialization
                || isGarbageCollected)
            {
                throw InvalidMap();
            }
            if (reference.TypeCode == 0x8A
                && reference.ArgumentCode != 0)
            {
                return new GenericCallTypeActualFacts(1, 1, isWritable,
                    false, 0, false, false);
            }
            if (reference.TypeCode >= 0x80 || isWritable)
            {
                throw new NotSupportedException(
                    "G1 GenericCall TypeActuals use an unsupported inline family.");
            }
            return new GenericCallTypeActualFacts(1, 1, false, true, 0,
                false, false);
        }

        if (!reference.IsRow)
        {
            throw new NotSupportedException(
                "G1 GenericCall TypeActuals use an unsupported inline TypeSpec.");
        }

        var row = typeSpecs.GetRow(reference.Row);
        if (reference.Form == 0x01)
        {
            if (reference.TypeCode == 0 || reference.TypeCode >= 0x80
                || row.Length != 3 || isWritable || isInitialization)
            {
                throw new NotSupportedException(
                    "G1 GenericCall TypeActuals use an unsupported built-in TypeSpec.");
            }
            return new GenericCallTypeActualFacts(1, 1, false, true, 0,
                false, false);
        }

        if (reference.Form == 0x04 && row.Length == 6)
        {
            var formalRow = checked((int)ReadUInt32(row, 2));
            if (genericParams.GetOwnerDefId(formalRow)
                    != callerDefinitionId
                || formalRow < callerFormalFirst
                || formalRow >= callerFormalFirst + callerFormalCount
                || genericParams.GetFormalKind(formalRow) != 1)
            {
                throw InvalidMap();
            }

            var requiresWritable = HasGenericMethodPropertyConstraint(
                formalRow, 2, genericParams, constraints);
            if (isWritable && !requiresWritable)
            {
                throw InvalidMap();
            }
            if (isInitialization && !allowInitQualifier)
            {
                throw InvalidMap();
            }

            return new GenericCallTypeActualFacts(
                genericParams.GetOwnershipDomain(formalRow),
                genericParams.GetNullabilityDomain(formalRow),
                requiresWritable,
                HasGenericMethodPropertyConstraint(formalRow, 3,
                    genericParams, constraints),
                formalRow, isWritable
                    && (allowInitQualifier || !requiresWritable),
                isInitialization);
        }

        if ((reference.Form is 0x07 or 0x0E) && row.Length == 6)
        {
            if (isInitialization)
            {
                throw InvalidMap();
            }

            var referentTypeSpecRef = ReadUInt32(row, 2);
            var referent = ResolveGenericCallTypeActualFacts(
                referentTypeSpecRef, callerDefinitionId,
                callerFormalFirst, callerFormalCount, typeSpecs,
                genericParams, constraints, allowInitQualifier: false,
                depth + 1);
            var ownershipDomain = referent.OwnershipDomain == 1
                ? (byte?)1 : null;
            return new GenericCallTypeActualFacts(ownershipDomain, 1,
                isWritable, false, 0, false, false);
        }

        if (reference.Form == 0x0C && row.Length == 7
            && reference.TypeCode == 0x8A)
        {
            if (isInitialization)
            {
                throw InvalidMap();
            }

            var elementTypeSpecRef = ReadUInt32(row, 3);
            var element = ResolveGenericCallTypeActualFacts(
                elementTypeSpecRef, callerDefinitionId,
                callerFormalFirst, callerFormalCount, typeSpecs,
                genericParams, constraints, allowInitQualifier: true,
                depth + 1);
            if (element.IsWritableQualifier && element.FormalRow == 0)
            {
                throw InvalidMap();
            }

            return new GenericCallTypeActualFacts(
                element.IsInitializationQualifier ? null : (byte)1,
                1, isWritable, false, 0, false, false);
        }

        throw new NotSupportedException(
            "G1 GenericCall TypeActuals use an unsupported TypeSpec form.");
    }

    private static bool GenericCallConstraintTypeSpecsMatch(
        TrRmRegisterBodyReader body, uint targetConstraintTypeSpecRef,
        uint callerConstraintTypeSpecRef, int targetMethodRow,
        uint[] targetArgumentTypeSpecRefs,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, int depth = 0)
    {
        ConsumeWork(body);
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw new NotSupportedException(
                "The G1 GenericCall interface constraint exceeds the supported depth.");
        }

        var target = typeSpecs.GetTypeSpecRef(
            targetConstraintTypeSpecRef);
        if (target.Form == 0x04 && target.IsRow
            && target.Qualifiers == 0)
        {
            var formalRow = checked((int)ReadUInt32(
                typeSpecs.GetRow(target.Row), 2));
            var targetDefinitionId = 0x1600_0000u
                | checked((uint)targetMethodRow);
            if (genericParams.GetOwnerDefId(formalRow)
                    == targetDefinitionId
                && genericParams.GetFormalKind(formalRow) == 1)
            {
                var ordinal = genericParams.GetOrdinal(formalRow);
                return ordinal < (uint)targetArgumentTypeSpecRefs.Length
                    && typeSpecs.AreEquivalentTypeSpecRefs(
                        targetArgumentTypeSpecRefs[checked((int)ordinal)],
                        callerConstraintTypeSpecRef);
            }
        }

        if (target.Form == 0x05 && target.IsRow)
        {
            var caller = typeSpecs.GetTypeSpecRef(
                callerConstraintTypeSpecRef);
            if (!caller.IsRow || caller.Form != 0x05
                || caller.Qualifiers != target.Qualifiers)
            {
                return false;
            }

            var targetRow = typeSpecs.GetRow(target.Row);
            var callerRow = typeSpecs.GetRow(caller.Row);
            if (targetRow.Length != callerRow.Length
                || targetRow.Length < 6
                || (targetRow.Length - 6) % sizeof(uint) != 0)
            {
                return false;
            }

            if (!GenericCallConstraintTypeSpecsMatch(body,
                    ReadUInt32(targetRow, 2), ReadUInt32(callerRow, 2),
                    targetMethodRow, targetArgumentTypeSpecRefs,
                    typeSpecs, genericParams, depth + 1))
            {
                return false;
            }

            for (var offset = 6; offset < targetRow.Length;
                offset += sizeof(uint))
            {
                if (!GenericCallConstraintTypeSpecsMatch(body,
                        ReadUInt32(targetRow, offset),
                        ReadUInt32(callerRow, offset), targetMethodRow,
                        targetArgumentTypeSpecRefs, typeSpecs,
                        genericParams, depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        return typeSpecs.AreEquivalentTypeSpecRefs(
            targetConstraintTypeSpecRef, callerConstraintTypeSpecRef);
    }

    private static bool HasGenericMethodPropertyConstraint(int formalRow,
        uint payload, TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints)
    {
        var (first, count) = genericParams.GetConstraintRange(formalRow);
        for (var offset = 0; offset < count; offset++)
        {
            var row = first + offset;
            if (constraints.GetKind(row) == 2
                && constraints.GetPayload(row) == payload)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateGenericCallSignatureType(
        uint typeSpecRef, uint ownerDefinitionId, int formalFirst,
        int formalCount, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints, bool allowUnit = false)
    {
        ValidateGenericCallSignatureTypeCore(typeSpecRef,
            ownerDefinitionId, formalFirst, formalCount, typeSpecs,
            genericParams, constraints, allowUnit,
            allowInitQualifier: false, depth: 0);
    }

    private static void ValidateGenericCallSignatureTypeCore(
        uint typeSpecRef, uint ownerDefinitionId, int formalFirst,
        int formalCount, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints, bool allowUnit,
        bool allowInitQualifier, int depth)
    {
        ConsumeWork(typeSpecs);
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw new NotSupportedException(
                "The G1 GenericCall signature TypeSpec exceeds the supported depth.");
        }

        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: allowInitQualifier);
        var isWritable = (reference.Qualifiers & 0x01) != 0;
        var isGarbageCollected = (reference.Qualifiers & 0x02) != 0;
        var isInitialization = (reference.Qualifiers & 0x04) != 0;
        if (isGarbageCollected)
        {
            throw new NotSupportedException(
                "G1 GenericCall signatures do not support GC-backed types.");
        }

        if (!reference.IsRow)
        {
            if (reference.Form == 0x01
                && reference.TypeCode is > 0 and < 0x80
                && !isWritable && !isInitialization)
            {
                return;
            }
            if (allowUnit && reference.Form == 0x01
                && reference.TypeCode == 0
                && !isWritable && !isInitialization)
            {
                return;
            }
            if (reference.Form == 0x01
                && reference.TypeCode == 0x8A
                && reference.ArgumentCode != 0
                && !isInitialization)
            {
                return;
            }

            throw new NotSupportedException(
                "The G1 consumer uses an unsupported inline GenericCall signature TypeSpec.");
        }

        var row = typeSpecs.GetRow(reference.Row);
        if (reference.Form == 0x01 && row.Length is 3 or 4)
        {
            if (reference.TypeCode == 0 && allowUnit
                && !isWritable && !isInitialization)
            {
                return;
            }
            if (reference.TypeCode is > 0 and < 0x80
                && !isWritable && !isInitialization)
            {
                return;
            }
            throw InvalidMap();
        }

        if (reference.Form == 0x04 && row.Length == 6)
        {
            var formalRow = checked((int)ReadUInt32(row, 2));
            if (genericParams.GetOwnerDefId(formalRow)
                    != ownerDefinitionId
                || formalRow < formalFirst
                || formalRow >= formalFirst + formalCount
                || genericParams.GetFormalKind(formalRow) != 1)
            {
                throw InvalidMap();
            }
            if (isWritable
                && !HasGenericMethodPropertyConstraint(formalRow, 2,
                    genericParams, constraints))
            {
                throw InvalidMap();
            }
            if (isInitialization && !allowInitQualifier)
            {
                throw InvalidMap();
            }
            return;
        }

        if ((reference.Form is >= 0x06 and <= 0x0B
                || reference.Form == 0x0E)
            && row.Length == 6)
        {
            if (reference.Form == 0x06)
            {
                throw new NotSupportedException(
                    "G1 GenericCall signatures do not support optional TypeSpecs.");
            }
            if ((reference.Form is 0x07 or 0x0E) && isInitialization)
            {
                throw InvalidMap();
            }
            ValidateGenericCallSignatureTypeCore(
                ReadUInt32(row, 2), ownerDefinitionId, formalFirst,
                formalCount, typeSpecs, genericParams, constraints,
                allowUnit: false, allowInitQualifier: false,
                depth + 1);
            return;
        }

        if (reference.Form == 0x0C && row.Length == 7
            && reference.TypeCode == 0x8A)
        {
            if (isInitialization)
            {
                throw InvalidMap();
            }
            ValidateGenericCallSignatureTypeCore(
                ReadUInt32(row, 3), ownerDefinitionId, formalFirst,
                formalCount, typeSpecs, genericParams, constraints,
                allowUnit: false, allowInitQualifier: true,
                depth + 1);
            return;
        }

        throw new NotSupportedException(
            "The G1 consumer uses an unsupported GenericCall signature TypeSpec.");
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
            ConsumeWork(typeSpecs, sizeof(uint) + 1);
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
        TrRmGenericConstantFeatureUseValidator.ValidateTypeSpec(typeSpecRef,
            ownerModule, typeSpecs, types, genericParams, imports,
            constantValues.ConstantSources, constantValues.Features,
            constantValues.Modules, typeSpecs.Schema,
            constantValues.GenericConstantFeatureTypeSpecRows);
        var typeSpec = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (!typeSpec.IsRow)
        {
            return;
        }

        typeSpecs.ResourceLedger?.ReserveWorkAtDepth(1, 1);
        var pendingRows = new Stack<(int Row, ulong Depth)>();
        pendingRows.Push((typeSpec.Row, 1));
        while (pendingRows.TryPop(out var pendingRow))
        {
            ConsumeWork(typeSpecs);
            typeSpecs.ResourceLedger?.CheckDepthFromCurrent(
                pendingRow.Depth);
            var row = pendingRow.Row;
            ConsumeWork(typeSpecs);
            if (visitedTypeSpecRows.Contains(row))
            {
                continue;
            }
            ConsumeWork(typeSpecs);
            _ = visitedTypeSpecRows.Add(row);

            var data = typeSpecs.GetRow(row);
            ConsumeWork(typeSpecs, 2);
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
                        constantValues, pendingRow.Depth, pendingRows);
                    break;
                case >= 0x06 and <= 0x0B:
                    AddNestedTypeSpecReference(ReadUInt32(data, 2), false,
                        typeSpecs, pendingRow.Depth, pendingRows);
                    break;
                case 0x0C:
                    AddNestedTypeSpecReference(ReadUInt32(data, 3),
                        data[2] == 0x8A, typeSpecs, pendingRow.Depth,
                        pendingRows);
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
                        typeSpecs, pendingRow.Depth, pendingRows);
                    break;
                case 0x0E:
                    AddNestedTypeSpecReference(ReadUInt32(data, 2), false,
                        typeSpecs, pendingRow.Depth, pendingRows);
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

    private static RegisterTypeState[][] ValidateCallSiteEffects(
        TrRmRegisterBodyReader body,
        int methodDefRow,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        ref TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmContractTableReader contracts, TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors,
        ConstantSourceValueContext constantValues,
        InitializationReferenceTypeMap initializationReferenceTypes)
    {
        var registerTypeEntries = ComputeBlockRegisterTypeStates(body,
            methodDefRow, types, fields, ref typeSpecs, methods, signatures,
            parameters, genericParams, constraints, contracts, imports,
            nativeFunctionPointerAnchors,
            constantValues, initializationReferenceTypes);
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            ConsumeWork(body);
            var block = body.GetBlock(blockIndex);
            var codeOffset = checked((int)ReadUInt32(body, block, 0));
            var code = body.GetBlockCode(blockIndex);
            var registerTypes = CopyRegisterTypeStates(body,
                registerTypeEntries[blockIndex]);
            var position = 0;
            while (position < code.Length)
            {
                ConsumeWork(body, 2);
                var opcode = code[position];
                ConsumeWork(body);
                var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                    opcode, body.Schema, body.UsesNativePointerLoads);
                ConsumeWork(body, checked((ulong)instructionLength));
                var instruction = code.Slice(position, instructionLength);
                switch (opcode)
                {
                    case 0x80:
                    case 0x82:
                        var shortMethodMapIndex
                            = ReadByte(body, instruction, 1) >> 4;
                        RejectNonReturningMethodMapTarget(body,
                            shortMethodMapIndex, methods, contracts,
                            imports);
                        RejectCompactConstructionInitializerCall(body,
                            shortMethodMapIndex, methods, contracts,
                            imports);
                        break;
                    case 0x90:
                        var callSiteRecordIndex
                            = ReadByte(body, instruction, 1);
                        ValidateCallSiteEffects(body, methodDefRow,
                            callSiteRecordIndex, blockIndex,
                            registerTypes,
                            ref typeSpecs, methods, signatures, parameters,
                            genericParams, constraints, contracts, imports,
                            nativeFunctionPointerAnchors);
                        UpdateCallSiteRegisterTypes(body, callSiteRecordIndex,
                            registerTypes, ref typeSpecs, methods,
                            signatures, genericParams, constraints, imports,
                            nativeFunctionPointerAnchors);
                        break;
                    case 0xE0:
                        ValidateCallSiteEffects(body,
                            methodDefRow,
                            ReadUInt16(body, instruction, 2),
                            blockIndex, registerTypes,
                            ref typeSpecs, methods, signatures,
                            parameters, genericParams, constraints, contracts, imports,
                            nativeFunctionPointerAnchors);
                        UpdateCallSiteRegisterTypes(body,
                            ReadUInt16(body, instruction, 2), registerTypes,
                            ref typeSpecs, methods, signatures,
                            genericParams, constraints, imports,
                            nativeFunctionPointerAnchors);
                        break;
                    case 0xE1:
                        var methodMapIndex = ReadUInt16(body, instruction, 2);
                        var targetMethod = GetMethodMapTarget(
                            body, methodMapIndex, methods, imports);
                        var signature = methods.GetSignature(targetMethod);
                        var resultType
                            = signatures.GetResultTypeSpec(signature);
                        if ((ReadByte(body, instruction, 1) >> 4) == 15
                            && !typeSpecs.AreEquivalentTypeSpecRefs(
                                resultType, UnitTypeSpecRef))
                        {
                            throw InvalidMap();
                        }
                        RejectNonReturningMethodMapTarget(
                            body, methodMapIndex, methods, contracts, imports);
                        RejectCompactConstructionInitializerCall(body,
                            methodMapIndex, methods, contracts, imports);
                        break;
                    case 0xB6:
                    case 0xEA:
                        ValidateExceptionFilterResult(body, instruction,
                            registerTypes, typeSpecs);
                        break;
                }

                if (opcode is not (0x90 or 0xE0))
                {
                    UpdateRegisterTypeStates(body, instruction, registerTypes,
                        methodDefRow, methods, contracts, types, fields,
                        imports,
                        signatures, parameters, genericParams, typeSpecs,
                        constantValues, initializationReferenceTypes,
                        nativeFunctionPointerAnchors,
                        checked(codeOffset + position),
                        allowIncompletePlaceFlow: false);
                }
                position += instructionLength;
            }
        }

        return registerTypeEntries;
    }

    private static void ValidateExceptionFilterResult(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        RegisterTypeState[] registerTypes,
        TrRmTypeSpecTableReader typeSpecs)
    {
        if (!body.HasExceptionDirectory)
        {
            return;
        }

        var opcode = ReadByte(body, instruction, 0);
        var recordIndex = opcode == 0xB6
            ? ReadByte(body, instruction, 1)
            : ReadUInt16(body, instruction, 2);
        var payload = GetRecordPayload(body, recordIndex);
        if (ReadByte(body, payload, 0) != 3)
        {
            return;
        }

        var valueKind = ReadByte(body, payload, 4);
        var valueIndex = ReadUInt16(body, payload, 6);
        uint resultTypeSpecRef;
        if (valueKind == 1)
        {
            var registerType = GetRegisterTypeState(body, registerTypes,
                valueIndex);
            if (registerType.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            resultTypeSpecRef = GetRegisterTypeSpecRef(body, registerType);
        }
        else if (valueKind == 2)
        {
            resultTypeSpecRef = GetTypeMapTypeSpecRef(body,
                GetHomeTypeMapIndex(body, valueIndex));
        }
        else
        {
            throw InvalidMap();
        }

        if (!typeSpecs.AreEquivalentTypeSpecRefs(resultTypeSpecRef,
                BooleanTypeSpecRef))
        {
            throw InvalidMap();
        }
    }

    private static void UpdateCallSiteRegisterTypes(
        TrRmRegisterBodyReader body, int recordIndex,
        RegisterTypeState[] registerTypes,
        ref TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var payload = GetRecordPayload(body, recordIndex);
        var targetMethod = GetCallSiteTargetMethod(body, payload,
            registerTypes, typeSpecs, methods, imports,
            nativeFunctionPointerAnchors);
        var signature = methods.GetSignature(targetMethod);
        var inputCount = checked((int)ReadUInt32(body, payload, 8));
        var inputOffset = 16;
        for (var input = 0; input < inputCount; input++)
        {
            ConsumeWork(body);
            if (ReadByte(body, payload, inputOffset) == 1
                && ReadByte(body, payload, inputOffset + 1) == 2)
            {
                var register = ReadUInt16(body, payload, inputOffset + 2);
                if (register < 14)
                {
                    SetRegisterTypeState(body, registerTypes, register,
                        RegisterTypeState.Unknown);
                }
            }

            inputOffset += 4;
        }

        if (ReadByte(body, payload, 1) == 1)
        {
            var resultRegister = ReadUInt16(body, payload, 6);
            SetRegisterTypeState(body, registerTypes, resultRegister,
                RegisterTypeState.FromTypeSpecRef(
                    GetGenericCallTypeSpecRef(body, payload,
                        signatures.GetResultTypeSpec(signature),
                    targetMethod, methods, ref typeSpecs, genericParams,
                    constraints)));
        }
    }

    private static void ValidateCallSiteEffects(TrRmRegisterBodyReader body,
        int callerMethodDefRow, int recordIndex, int blockIndex,
        RegisterTypeState[] registerTypes,
        ref TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmContractTableReader contracts,
        TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var payload = GetRecordPayload(body, recordIndex);
        var targetMethod = GetCallSiteTargetMethod(body, payload,
            registerTypes, typeSpecs, methods, imports,
            nativeFunctionPointerAnchors);
        var signature = methods.GetSignature(targetMethod);
        var (firstParameter, parameterCount) = signatures.GetParamRange(signature);
        var inputCount = ReadUInt32(body, payload, 8);
        var receiverTypeSpec = signatures.GetReceiverTypeSpec(signature);
        var isGenericInterfaceRequirement
            = ReadByte(body, payload, 0) == 4
                && ReadByte(body, GetRecordPayload(body,
                    ReadUInt16(body, payload, 4)), 0) == 2;
        var targetConstructionRole = contracts.HasConstructionClause(targetMethod)
            ? contracts.GetConstructionClause(targetMethod).RoleTag : (byte)0;

        var hasReceiver = receiverTypeSpec != 0
            && (body.Schema.UsesObjectConstruction()
                || (body.Schema.UsesConstructionContracts()
                    && targetConstructionRole == 1)
                || body.Schema.UsesMethodReceiverAccess()
                || isGenericInterfaceRequirement);
        if (inputCount != (uint)(parameterCount
                + (hasReceiver ? 1 : 0)))
        {
            throw InvalidMap();
        }

        var inputOffset = 16;
        if (hasReceiver)
        {
            if (body.Schema.UsesPropertyInitializedState()
                && targetConstructionRole is 1 or 3)
            {
                ValidatePropertyInitializerReceiverInput(body,
                    callerMethodDefRow, targetMethod, payload,
                    receiverTypeSpec, ref typeSpecs, methods, signatures,
                    contracts);
            }
            else if (targetConstructionRole == 3)
            {
                throw InvalidMap();
            }
            else if (isGenericInterfaceRequirement)
            {
                ValidateGenericCallInterfaceReceiverInput(body,
                    callerMethodDefRow, targetMethod, payload,
                    inputOffset, typeSpecs, genericParams, constraints,
                    methods, signatures, parameters);
            }
            else if (targetConstructionRole == 1)
            {
                ValidateConstructionReceiverInput(body,
                    callerMethodDefRow, targetMethod, payload,
                    receiverTypeSpec, methods, typeSpecs, contracts);
            }
            inputOffset += 4;
        }

        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            ConsumeWork(body);
            var selectorKind = ReadByte(body, payload, inputOffset);
            var isUnitInput = selectorKind == 0;
            var formalType = GetGenericCallTypeSpecRef(body, payload,
                parameters.GetTypeSpec(firstParameter + ordinal),
                targetMethod, methods, ref typeSpecs, genericParams,
                constraints);
            var isUnitFormal = typeSpecs.AreEquivalentTypeSpecRefs(
                formalType, UnitTypeSpecRef);
            if (isUnitInput != isUnitFormal)
            {
                throw InvalidMap();
            }

            inputOffset += 4;
        }

        var resultType = GetGenericCallTypeSpecRef(body, payload,
            signatures.GetResultTypeSpec(signature), targetMethod,
            methods, ref typeSpecs, genericParams, constraints);
        var unitResult = typeSpecs.AreEquivalentTypeSpecRefs(
            resultType, UnitTypeSpecRef);
        var doesNotReturn = (contracts.GetEffectsMask(targetMethod) & 2) != 0;
        var resultKind = ReadByte(body, payload, 1);
        if (doesNotReturn
            ? resultKind != 0 || !body.IsCallSiteAtBlockEnd(blockIndex, recordIndex)
            : unitResult ? resultKind != 0 : resultKind == 0)
        {
            throw InvalidMap();
        }
    }

    private static void ValidatePropertyInitializerReceiverInput(
        TrRmRegisterBodyReader body, int callerMethod, int targetMethod,
        ReadOnlySpan<byte> callSitePayload, uint receiverTypeSpec,
        ref TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmContractTableReader contracts)
    {
        var targetConstruction = contracts.GetConstructionClause(targetMethod);
        var targetSignature = methods.GetSignature(targetMethod);
        var (_, parameterCount) = signatures.GetParamRange(targetSignature);
        var expectedInputCount = parameterCount + 1;
        if (!body.Schema.UsesPropertyInitializedState()
            || ReadByte(body, callSitePayload, 0) != 1
            || ReadByte(body, callSitePayload, 1) != 0
            || ReadUInt16(body, callSitePayload, 2) != 0
            || ReadUInt16(body, callSitePayload, 6) != 0
            || ReadUInt32(body, callSitePayload, 8)
                != (uint)expectedInputCount
            || ReadByte(body, callSitePayload, 16) != 2
            || ReadByte(body, callSitePayload, 17) != 5
            || receiverTypeSpec == 0
            || (methods.GetFlags(targetMethod) & 4) != 0
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                receiverTypeSpec,
                signatures.GetReceiverTypeSpec(targetSignature))
            || targetConstruction.RoleTag is not (1 or 3))
        {
            throw InvalidMap();
        }

        if (targetConstruction.RoleTag == 3
            && (parameterCount != 1 || expectedInputCount != 2))
        {
            throw InvalidMap();
        }

        var homeIndex = ReadUInt16(body, callSitePayload, 18);
        if (homeIndex >= body.HomeCount)
        {
            throw InvalidMap();
        }

        var home = GetHomeMapEntry(body, homeIndex);
        var homeRole = ReadByte(body, home, 0);
        if (ReadByte(body, home, 1) != 0
            || ReadUInt16(body, home, 4) != 0
            || ReadUInt16(body, home, 6) != 0
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                GetTypeMapTypeSpecRef(body, ReadUInt16(body, home, 2)),
                receiverTypeSpec))
        {
            throw InvalidMap();
        }

        var callerRole = contracts.HasConstructionClause(callerMethod)
            ? contracts.GetConstructionClause(callerMethod).RoleTag
            : (byte)0;
        if (homeRole == 4)
        {
            if (!body.Schema.UsesObjectConstruction())
            {
                throw InvalidMap();
            }

            if (callerRole == 2 && targetConstruction.RoleTag == 1)
            {
                var callerConstruction
                    = contracts.GetConstructionClause(callerMethod);
                if (callerConstruction.InitializerMethodDefRow
                    != (uint)targetMethod
                    || methods.GetOwnerType(callerMethod)
                        != methods.GetOwnerType(targetMethod)
                    || methods.GetOwnerModule(callerMethod)
                        != methods.GetOwnerModule(targetMethod))
                {
                    throw InvalidMap();
                }
            }

            return;
        }

        if (homeRole != 1 || callerRole is not (1 or 3)
            || methods.GetOwnerType(callerMethod)
                != methods.GetOwnerType(targetMethod)
            || methods.GetOwnerModule(callerMethod)
                != methods.GetOwnerModule(targetMethod)
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                signatures.GetReceiverTypeSpec(
                    methods.GetSignature(callerMethod)),
                receiverTypeSpec)
            || targetConstruction.RoleTag != 3)
        {
            throw InvalidMap();
        }

        if (callerRole == 3
            && contracts.GetConstructionClause(callerMethod).PropertyDefRow
                != targetConstruction.PropertyDefRow)
        {
            throw InvalidMap();
        }
    }

    private static void ValidateConstructionReceiverInput(
        TrRmRegisterBodyReader body, int callerMethodDefRow,
        int targetMethod, ReadOnlySpan<byte> callSitePayload,
        uint receiverTypeSpec, TrRmMethodDefTableReader methods,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmContractTableReader contracts)
    {
        var selectorKind = ReadByte(body, callSitePayload, 16);
        var sourceUse = ReadByte(body, callSitePayload, 17);
        var contextHomeIndex = ReadUInt16(body, callSitePayload, 18);
        var callerConstruction
            = contracts.GetConstructionClause(callerMethodDefRow);
        var targetConstruction
            = contracts.GetConstructionClause(targetMethod);
        if (!body.Schema.UsesObjectConstruction()
            || ReadByte(body, callSitePayload, 0) != 1
            || selectorKind != 2 || sourceUse != 5
            || ReadByte(body, GetHomeMapEntry(body,
                contextHomeIndex), 0) != 4
            || callerConstruction.RoleTag != 2
            || callerConstruction.InitializerMethodDefRow
                != (uint)targetMethod
            || targetConstruction.RoleTag != 1
            || methods.GetOwnerType(callerMethodDefRow)
                != methods.GetOwnerType(targetMethod)
            || methods.GetOwnerModule(callerMethodDefRow)
                != methods.GetOwnerModule(targetMethod)
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                GetTypeMapTypeSpecRef(body,
                    GetHomeTypeMapIndex(body, contextHomeIndex)),
                receiverTypeSpec))
        {
            throw InvalidMap();
        }
    }

    private static void ValidateOrdinaryClassReceiverInput(
        TrRmRegisterBodyReader body, int callerMethodDefRow,
        int targetMethod, ReadOnlySpan<byte> callSitePayload,
        uint receiverTypeSpec, RegisterTypeState[] registerTypes,
        int ownerModule, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmImportTableReader imports,
        TrRmMethodDefTableReader methods,
        TrRmContractTableReader contracts)
    {
        ValidateOrdinaryClassReceiverInput(body, callerMethodDefRow,
            targetMethod, ReadByte(body, callSitePayload, 16),
            ReadByte(body, callSitePayload, 17),
            ReadUInt16(body, callSitePayload, 18), receiverTypeSpec,
            registerTypes, ownerModule, types, typeSpecs, imports, methods,
            contracts);
    }

    private static void ValidateOrdinaryClassReceiverInput(
        TrRmRegisterBodyReader body, int callerMethodDefRow,
        int targetMethod, byte selectorKind, byte sourceUse,
        int selectorIndex, uint receiverTypeSpec,
        RegisterTypeState[] registerTypes, int ownerModule,
        TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmImportTableReader imports,
        TrRmMethodDefTableReader methods,
        TrRmContractTableReader contracts)
    {
        if (!TryGetNormalClassHandleTypeSpec(receiverTypeSpec, types,
                typeSpecs, imports, out var receiverOwnerType,
                out var receiverQualifiers))
        {
            throw InvalidMap();
        }

        var typeRequiresWritable = (receiverQualifiers & 1) != 0;
        var expectedWritable = body.Schema.UsesMethodReceiverAccess()
            ? (methods.GetFlags(targetMethod) & 4) != 0
            : typeRequiresWritable;
        var hasOwnerMutability = types.TryGetDeclaredClassMutability(
            receiverOwnerType, out var ownerIsMutable);
        var requiresDeclaredClassMutability
            = !body.Schema.UsesMethodReceiverAccess();
        if ((!body.Schema.UsesObjectConstruction()
                && !body.Schema.UsesMethodReceiverAccess())
            || types.GetGenericRange(receiverOwnerType).Count != 0
            || (requiresDeclaredClassMutability
                && (!hasOwnerMutability
                    || (expectedWritable && !ownerIsMutable)))
            || (body.Schema.UsesMethodReceiverAccess()
                && expectedWritable != typeRequiresWritable)
            || methods.GetOwnerType(targetMethod) != receiverOwnerType
            || methods.GetOwnerModule(targetMethod) != ownerModule
            || sourceUse != (expectedWritable ? 4 : 3)
            || selectorKind is not (1 or 2))
        {
            throw InvalidMap();
        }

        uint actualTypeSpecRef;
        if (selectorKind == 1)
        {
            var actual = GetRegisterTypeState(body, registerTypes,
                selectorIndex);
            if (actual.IsPlaceHandle)
            {
                throw InvalidMap();
            }
            actualTypeSpecRef = GetRegisterTypeSpecRef(body, actual);
        }
        else
        {
            var receiverHome = GetHomeMapEntry(body, selectorIndex);
            var receiverHomeRole = ReadByte(body, receiverHome, 0);
            var callerIsInitializer
                = contracts.HasConstructionClause(callerMethodDefRow)
                    && contracts.GetConstructionClause(
                        callerMethodDefRow).RoleTag == 1;
            if (receiverHomeRole is not (1 or 2 or 3)
                || receiverHomeRole == 4
                || (callerIsInitializer && receiverHomeRole == 1))
            {
                throw InvalidMap();
            }

            actualTypeSpecRef = GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, receiverHome, 2));
        }

        if (!TryGetNormalClassHandleTypeSpec(actualTypeSpecRef, types,
                typeSpecs, imports, out var actualOwnerType,
                out var actualQualifiers))
        {
            throw InvalidMap();
        }

        var actualWritable = (actualQualifiers & 1) != 0;
        if (actualOwnerType != receiverOwnerType
            || (expectedWritable && !actualWritable)
            || (requiresDeclaredClassMutability
                && actualWritable && !ownerIsMutable))
        {
            throw InvalidMap();
        }
    }

    private static bool TryGetNormalClassHandleTypeSpec(
        uint typeSpecRef, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs, TrRmImportTableReader imports,
        out int classTypeDefRow, out byte qualifiers)
    {
        var typeSpec = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (!typeSpec.IsRow || typeSpec.Form is not (0x02 or 0x03)
            || (typeSpec.Qualifiers & ~0x01) != 0)
        {
            classTypeDefRow = 0;
            qualifiers = 0;
            return false;
        }

        classTypeDefRow = typeSpecs.GetNominalTypeDefRow(
            typeSpecRef, types, imports);
        qualifiers = typeSpec.Qualifiers;
        return types.GetKind(classTypeDefRow) == 2;
    }

    private static void ValidateCompactOrdinaryClassCall(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        byte opcode, RegisterTypeState[] registerTypes,
        int callerMethodDefRow, int ownerModule,
        TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmGenericParamTableReader genericParams,
        TrRmContractTableReader contracts,
        TrRmImportTableReader imports,
        ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader
            nativeFunctionPointerAnchors)
    {
        var control = ReadByte(body, instruction, 1);
        var methodMapIndex = opcode == 0xE1
            ? ReadUInt16(body, instruction, 2) : control >> 4;
        var argumentBase = control & 0x0F;
        var targetMethod = GetMethodMapTarget(body, methodMapIndex,
            methods, imports);
        var constructionRole
            = contracts.HasConstructionClause(targetMethod)
                ? contracts.GetConstructionClause(targetMethod).RoleTag
                : (byte)0;
        if (constructionRole == 1)
        {
            throw InvalidMap();
        }

        var signature = methods.GetSignature(targetMethod);
        var receiverTypeSpec = signatures.GetReceiverTypeSpec(signature);
        if (receiverTypeSpec == 0)
        {
            return;
        }
        if (constructionRole == 2
            || signatures.GetGenericCount(signature) != 0)
        {
            throw InvalidMap();
        }

        var receiverType = typeSpecs.GetTypeSpecRef(receiverTypeSpec);
        var receiverSourceUse = (receiverType.Qualifiers & 1) != 0
            ? (byte)4 : (byte)3;
        ValidateOrdinaryClassReceiverInput(body, callerMethodDefRow,
            targetMethod, selectorKind: 1, receiverSourceUse, argumentBase,
            receiverTypeSpec, registerTypes, ownerModule, types, typeSpecs,
            imports, methods, contracts);

        var (firstParameter, parameterCount)
            = signatures.GetParamRange(signature);
        var inputCount = checked(parameterCount + 1);
        if (argumentBase + inputCount > 14)
        {
            throw InvalidMap();
        }

        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            ConsumeWork(body);
            var parameterRow = firstParameter + ordinal;
            var formalTypeSpecRef = parameters.GetTypeSpec(parameterRow);
            if (parameters.GetMode(parameterRow) != 0
                || parameters.GetFlags(parameterRow) != 0
                || typeSpecs.IsInitializationDestinationType(
                    formalTypeSpecRef))
            {
                throw InvalidMap();
            }

            var sourceRegister = argumentBase + ordinal + 1;
            var source = GetRegisterTypeState(body, registerTypes,
                sourceRegister);
            if (source.IsPlaceHandle
                || !MatchesRegisterType(body, source, formalTypeSpecRef,
                    ownerModule, types, typeSpecs, genericParams, imports,
                    constantValues, nativeFunctionPointerAnchors)
                || (!typeSpecs.AreEquivalentTypeSpecRefs(
                        formalTypeSpecRef, UnitTypeSpecRef)
                    && !IsCopyableRegisterValueType(typeSpecs,
                        formalTypeSpecRef, constantValues, body.Schema)))
            {
                throw InvalidMap();
            }
        }

        var resultTypeSpecRef
            = signatures.GetResultTypeSpec(signature);
        var isUnitResult = typeSpecs.AreEquivalentTypeSpecRefs(
            resultTypeSpecRef, UnitTypeSpecRef);
        var resultRegister = opcode switch
        {
            0x80 when !isUnitResult => argumentBase,
            0x82 when isUnitResult => -1,
            0xE1 when isUnitResult
                && (control >> 4) == 15 => -1,
            0xE1 when !isUnitResult && (control >> 4) < 14
                => control >> 4,
            _ => throw InvalidMap(),
        };
        if (resultRegister >= 0)
        {
            SetRegisterTypeState(body, registerTypes, resultRegister,
                RegisterTypeState.FromTypeSpecRef(resultTypeSpecRef));
        }
    }

    private static byte GetGenericCallSourceUse(uint formalTypeSpecRef,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams)
    {
        if (typeSpecs.IsInitializationDestinationType(formalTypeSpecRef))
        {
            return 5;
        }

        var type = typeSpecs.GetTypeSpecRef(formalTypeSpecRef);
        if (type.Form == 0x01 && type.TypeCode != 0)
        {
            return 1;
        }
        if (type.Form == 0x04)
        {
            var formalRow = checked((int)ReadUInt32(
                typeSpecs.GetRow(type.Row), 2));
            return genericParams.GetOwnershipDomain(formalRow) switch
            {
                1 => 1,
                2 => 2,
                _ => throw new NotSupportedException(
                    "The G1 consumer currently requires a proven Copy or unique source-use domain for GenericCall inputs."),
            };
        }

        throw new NotSupportedException(
            "The G1 consumer currently supports scalar and direct-formal GenericCall input types only.");
    }

    private static bool IsTerminalGenericCallTransfer(
        TrRmRegisterBodyReader body, int blockIndex,
        ReadOnlySpan<byte> code, int callPosition, int callSiteRecordIndex,
        ReadOnlySpan<byte> callSitePayload)
    {
        if (blockIndex != 0 || body.BlockCount != 1 || callPosition != 0
            || ReadByte(body, callSitePayload, 1) != 2)
        {
            return false;
        }

        var opcode = ReadByte(body, code, callPosition);
        var instructionLength = opcode switch
        {
            0x90 when ReadByte(body, code, callPosition + 1)
                    == callSiteRecordIndex => 2,
            0xE0 when ReadUInt16(body, code, callPosition + 2)
                    == callSiteRecordIndex => 4,
            _ => 0,
        };
        return instructionLength != 0
            && code.Length == instructionLength + 2
            && code[instructionLength] == 0xB8
            && ReadByte(body, code, instructionLength + 1)
                == ReadUInt16(body, callSitePayload, 6);
    }

    private static void ValidateNativeOutputReservationFlow(
        TrRmRegisterBodyReader body)
    {
        if (!body.Schema.UsesForeignInvocationContracts())
        {
            return;
        }

        var reservationRowsByCallSite
            = new List<int>?[body.RecordCount];
        var hasReservations = false;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            ConsumeWork(body);
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x0C)
            {
                continue;
            }

            var payload = record[4..];
            var callSiteRecordIndex = ReadUInt16(body, payload, 0);
            ConsumeWork(body);
            var reservations = reservationRowsByCallSite[callSiteRecordIndex];
            if (reservations is null)
            {
                ConsumeWork(body);
                reservations = [];
                reservationRowsByCallSite[callSiteRecordIndex] = reservations;
            }

            ConsumeWork(body);
            reservations.Add(recordIndex);
            hasReservations = true;
        }

        if (!hasReservations)
        {
            return;
        }

        var referencedCallSites = new HashSet<int>();
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            ConsumeWork(body);
            var code = body.GetBlockCode(blockIndex);
            var position = 0;
            while (position < code.Length)
            {
                ConsumeWork(body);
                var instruction = code[position..];
                var opcode = ReadByte(body, instruction, 0);
                var length = TrRmRegisterBodyReader.InstructionLength(
                    opcode, body.Schema, body.UsesNativePointerLoads);
                if (opcode == 0x90)
                {
                    _ = referencedCallSites.Add(
                        ReadByte(body, instruction, 1));
                }
                else if (opcode == 0xE0)
                {
                    _ = referencedCallSites.Add(
                        ReadUInt16(body, instruction, 2));
                }

                position += length;
            }
        }

        for (var callSite = 0; callSite < reservationRowsByCallSite.Length;
            callSite++)
        {
            if (reservationRowsByCallSite[callSite] is not null
                && !referencedCallSites.Contains(callSite))
            {
                throw InvalidMap();
            }
        }

        var successors = BuildBlockFlowSuccessors(body);
        var entryStates = new HashSet<int>?[body.BlockCount];
        var queued = new bool[body.BlockCount];
        var worklist = new Queue<int>();
        entryStates[0] = [];
        queued[0] = true;
        worklist.Enqueue(0);
        while (worklist.Count != 0)
        {
            ConsumeWork(body);
            var blockIndex = worklist.Dequeue();
            queued[blockIndex] = false;
            var active = new HashSet<int>(entryStates[blockIndex]!);
            var code = body.GetBlockCode(blockIndex);
            var position = 0;
            while (position < code.Length)
            {
                ConsumeWork(body, 2);
                var instruction = code[position..];
                var opcode = ReadByte(body, instruction, 0);
                var length = TrRmRegisterBodyReader.InstructionLength(
                    opcode, body.Schema, body.UsesNativePointerLoads);
                if (opcode == 0xDF)
                {
                    var action = ReadByte(body, instruction, 1);
                    var reservationIndex = ReadUInt16(body, instruction, 2);
                    ConsumeWork(body);
                    if (action == 0)
                    {
                        if (!active.Add(reservationIndex))
                        {
                            throw InvalidMap();
                        }
                    }
                    else if (action != 1
                        || !active.Remove(reservationIndex))
                    {
                        throw InvalidMap();
                    }
                }
                else if (opcode is 0x90 or 0xE0)
                {
                    var callSiteRecordIndex = opcode == 0x90
                        ? ReadByte(body, instruction, 1)
                        : ReadUInt16(body, instruction, 2);
                    if (reservationRowsByCallSite[callSiteRecordIndex]
                        is { } reservations)
                    {
                        foreach (var reservation in reservations)
                        {
                            ConsumeWork(body);
                            if (!active.Remove(reservation))
                            {
                                throw InvalidMap();
                            }
                        }
                    }
                }

                position += length;
            }

            if (successors[blockIndex] is not { } blockSuccessors)
            {
                if (active.Count != 0)
                {
                    throw InvalidMap();
                }

                continue;
            }

            foreach (var successor in blockSuccessors)
            {
                ConsumeWork(body);
                var successorState = entryStates[successor];
                if (successorState is null)
                {
                    entryStates[successor] = [.. active];
                    if (!queued[successor])
                    {
                        worklist.Enqueue(successor);
                        queued[successor] = true;
                    }
                }
                else if (!successorState.SetEquals(active))
                {
                    throw InvalidMap();
                }
            }
        }
    }

    private static int GetCallSiteTargetMethod(TrRmRegisterBodyReader body,
        ReadOnlySpan<byte> payload, RegisterTypeState[] registerTypes,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var targetKind = ReadByte(body, payload, 0);
        var targetIndex = ReadUInt16(body, payload, 4);
        return targetKind switch
        {
            1 => GetMethodMapTarget(body, targetIndex, methods, imports),
            2 => GetRegisterFunctionPointerAnchorMethod(
                body, registerTypes, targetIndex, typeSpecs,
                nativeFunctionPointerAnchors),
            3 => GetHomeFunctionPointerAnchorMethod(body, targetIndex,
                typeSpecs, nativeFunctionPointerAnchors),
            4 => GetGenericCallTargetMethod(body, targetIndex, methods,
                imports),
            _ => throw InvalidMap(),
        };
    }

    private static int GetGenericCallTargetMethod(
        TrRmRegisterBodyReader body, int targetRecordIndex,
        TrRmMethodDefTableReader methods, TrRmImportTableReader imports)
    {
        var target = GetRecordPayload(body, targetRecordIndex);
        if (ReadByte(body, target, 0) is not (1 or 2))
        {
            throw InvalidMap();
        }

        return GetMethodMapTarget(body, ReadUInt16(body, target, 2),
            methods, imports);
    }

    private static uint GetGenericCallTypeSpecRef(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> callSitePayload,
        uint signatureTypeSpecRef, int targetMethodRow,
        TrRmMethodDefTableReader methods,
        ref TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints)
    {
        if (ReadByte(body, callSitePayload, 0) != 4)
        {
            return signatureTypeSpecRef;
        }

        var targetRecordIndex = ReadUInt16(body, callSitePayload, 4);
        var target = GetRecordPayload(body, targetRecordIndex);
        if (ReadByte(body, target, 0) == 1)
        {
            var actualCount = checked((int)ReadUInt32(body, target, 12));
            var actualTypeSpecRefs = new uint[actualCount];
            for (var ordinal = 0; ordinal < actualCount; ordinal++)
            {
                var argumentOffset = checked(16 + (ordinal * 8));
                if (ReadByte(body, target, argumentOffset) != 1)
                {
                    throw new NotSupportedException(
                        "G1 GenericCall type substitution requires TypeActual arguments.");
                }
                actualTypeSpecRefs[ordinal] = ReadUInt32(body, target,
                    argumentOffset + 4);
            }

            var actualAdditions = new List<ReadOnlyMemory<byte>>();
            for (var ordinal = 0; ordinal < actualTypeSpecRefs.Length;
                ordinal++)
            {
                actualTypeSpecRefs[ordinal]
                    = NormalizeGenericCallTypeSpecRef(body,
                        actualTypeSpecRefs[ordinal], typeSpecs,
                        genericParams, constraints, actualAdditions, 0);
            }
            if (actualAdditions.Count != 0)
            {
                typeSpecs = typeSpecs.WithAdditionalRows(actualAdditions);
            }

            var substitutionAdditions
                = new List<ReadOnlyMemory<byte>>();
            var substitutedTypeSpecRef
                = SubstituteGenericCallTypeSpecRef(body,
                    signatureTypeSpecRef, targetMethodRow,
                    actualTypeSpecRefs, typeSpecs, genericParams,
                    substitutionAdditions, 0);
            if (substitutionAdditions.Count != 0)
            {
                typeSpecs = typeSpecs.WithAdditionalRows(
                    substitutionAdditions);
            }

            var normalizedAdditions
                = new List<ReadOnlyMemory<byte>>();
            var normalizedTypeSpecRef
                = NormalizeGenericCallTypeSpecRef(body,
                    substitutedTypeSpecRef, typeSpecs, genericParams,
                    constraints, normalizedAdditions, 0);
            if (normalizedAdditions.Count != 0)
            {
                typeSpecs = typeSpecs.WithAdditionalRows(
                    normalizedAdditions);
            }
            return normalizedTypeSpecRef;
        }

        var signatureType = typeSpecs.GetTypeSpecRef(signatureTypeSpecRef);
        if (signatureType.Form != 0x04)
        {
            return signatureTypeSpecRef;
        }

        var formalRow = checked((int)ReadUInt32(
            typeSpecs.GetRow(signatureType.Row), 2));
        var formalOwner = genericParams.GetOwnerDefId(formalRow);
        if (ReadByte(body, target, 0) == 2)
        {
            var ownerTypeDef = methods.GetOwnerType(targetMethodRow);
            var ownerDefinitionId = 0x1300_0000u
                | checked((uint)ownerTypeDef);
            if (ownerTypeDef == 0
                || formalOwner != ownerDefinitionId
                || genericParams.GetFormalKind(formalRow) != 1)
            {
                throw InvalidMap();
            }

            var ownerTypeSpecRef = ReadUInt32(body, target, 4);
            var ownerType = typeSpecs.GetTypeSpecRef(ownerTypeSpecRef);
            if (!ownerType.IsRow || ownerType.Form != 0x05
                || ownerType.Qualifiers != 0)
            {
                throw InvalidMap();
            }
            var ownerTypeBytes = typeSpecs.GetRow(ownerType.Row);
            var ownerOrdinal = genericParams.GetOrdinal(formalRow);
            var ownerArgumentCount
                = (ownerTypeBytes.Length - 6) / sizeof(uint);
            if (ownerOrdinal >= (uint)ownerArgumentCount)
            {
                throw InvalidMap();
            }

            var ownerArgumentOffset = checked(
                6 + (checked((int)ownerOrdinal) * sizeof(uint)));
            var ownerArgumentTypeSpecRef = ReadUInt32(body,
                ownerTypeBytes, ownerArgumentOffset);
            var additions = new List<ReadOnlyMemory<byte>>();
            var normalizedTypeSpecRef
                = NormalizeGenericCallTypeSpecRef(body,
                    ownerArgumentTypeSpecRef, typeSpecs, genericParams,
                    constraints, additions, 0);
            if (additions.Count != 0)
            {
                typeSpecs = typeSpecs.WithAdditionalRows(additions);
            }
            return normalizedTypeSpecRef;
        }

        var targetDefinitionId = 0x1600_0000u
            | checked((uint)targetMethodRow);
        if (formalOwner != targetDefinitionId
            || genericParams.GetFormalKind(formalRow) != 1)
        {
            throw InvalidMap();
        }

        var ordinal = checked((int)genericParams.GetOrdinal(formalRow));
        var argumentCount = ReadUInt32(body, target, 12);
        if ((uint)ordinal >= argumentCount)
        {
            throw InvalidMap();
        }

        var argumentOffset = checked(16 + (ordinal * 8));
        if (ReadByte(body, target, argumentOffset) != 1)
        {
            throw new NotSupportedException(
                "The G1 consumer currently supports TypeActual substitution for GenericCall signatures only.");
        }

        var typeActual = ReadUInt32(body, target, argumentOffset + 4);
        var normalizedAdditions
            = new List<ReadOnlyMemory<byte>>();
        var normalizedActual = NormalizeGenericCallTypeSpecRef(body,
            typeActual, typeSpecs, genericParams, constraints,
            normalizedAdditions, 0);
        if (normalizedAdditions.Count != 0)
        {
            typeSpecs = typeSpecs.WithAdditionalRows(normalizedAdditions);
        }
        return normalizedActual;
    }

    private static uint NormalizeGenericCallTypeSpecRef(
        TrRmRegisterBodyReader body, uint typeSpecRef,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        List<ReadOnlyMemory<byte>> additions, int depth,
        bool isSpanElement = false)
    {
        ConsumeWork(body);
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw new NotSupportedException(
                "The G1 GenericCall TypeSpec normalization exceeds the supported depth.");
        }

        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: true);
        if (!reference.IsRow)
        {
            return typeSpecRef;
        }

        var row = typeSpecs.GetRow(reference.Row);
        if (reference.Form == 0x04 && row.Length == 6
            && (reference.Qualifiers & 0x01) != 0)
        {
            var formalRow = checked((int)ReadUInt32(row, 2));
            if (!HasGenericMethodPropertyConstraint(formalRow, 2,
                    genericParams, constraints))
            {
                throw InvalidMap();
            }
            if (isSpanElement)
            {
                return typeSpecRef;
            }

            var normalizedFormal = row.ToArray();
            normalizedFormal[1] &= 0xFE;
            return AddGenericCallTypeSpecRow(typeSpecs, additions,
                normalizedFormal);
        }

        var childOffset = reference.Form switch {
            0x07 or 0x0E => 2,
            0x0C when row.Length == 7
                && reference.TypeCode == 0x8A => 3,
            _ => -1,
        };
        if (childOffset < 0)
        {
            return typeSpecRef;
        }
        if (row.Length < childOffset + sizeof(uint))
        {
            throw InvalidMap();
        }

        var childTypeSpecRef = ReadUInt32(row, childOffset);
        var normalizedChild = NormalizeGenericCallTypeSpecRef(body,
            childTypeSpecRef, typeSpecs, genericParams, constraints,
            additions, depth + 1, reference.Form == 0x0C);
        if (normalizedChild == childTypeSpecRef)
        {
            return typeSpecRef;
        }

        var normalizedRow = row.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            normalizedRow.AsSpan(childOffset, sizeof(uint)),
            normalizedChild);
        return AddGenericCallTypeSpecRow(typeSpecs, additions,
            normalizedRow);
    }

    private static uint SubstituteGenericCallTypeSpecRef(
        TrRmRegisterBodyReader body, uint typeSpecRef, int targetMethodRow,
        uint[] actualTypeSpecRefs, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        List<ReadOnlyMemory<byte>> additions, int depth)
    {
        ConsumeWork(body);
        if (depth > SemanticVerifier.MaximumDepth)
        {
            throw new NotSupportedException(
                "The G1 GenericCall signature substitution exceeds the supported depth.");
        }

        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: true);
        if (reference.IsRow && reference.Form == 0x04)
        {
            var formalRow = checked((int)ReadUInt32(
                typeSpecs.GetRow(reference.Row), 2));
            var targetDefinitionId = 0x1600_0000u
                | checked((uint)targetMethodRow);
            if (genericParams.GetOwnerDefId(formalRow)
                    == targetDefinitionId
                && genericParams.GetFormalKind(formalRow) == 1)
            {
                var ordinal = checked((int)genericParams.GetOrdinal(
                    formalRow));
                if ((uint)ordinal >= (uint)actualTypeSpecRefs.Length)
                {
                    throw InvalidMap();
                }

                return AddGenericCallTypeSpecQualifiers(body,
                    actualTypeSpecRefs[ordinal], reference.Qualifiers,
                    typeSpecs, additions);
            }
        }

        if (!reference.IsRow)
        {
            return typeSpecRef;
        }

        var row = typeSpecs.GetRow(reference.Row);
        var childOffset = reference.Form switch {
            >= 0x06 and <= 0x0B => 2,
            0x0E => 2,
            0x0C => 3,
            _ => -1,
        };
        if (childOffset < 0 || row.Length < childOffset + sizeof(uint))
        {
            return typeSpecRef;
        }

        var childTypeSpecRef = ReadUInt32(row, childOffset);
        var substitutedChild = SubstituteGenericCallTypeSpecRef(body,
            childTypeSpecRef, targetMethodRow, actualTypeSpecRefs,
            typeSpecs, genericParams, additions, depth + 1);
        if (substitutedChild == childTypeSpecRef)
        {
            return typeSpecRef;
        }

        var substitutedRow = row.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            substitutedRow.AsSpan(childOffset, sizeof(uint)),
            substitutedChild);
        return AddGenericCallTypeSpecRow(typeSpecs, additions,
            substitutedRow);
    }

    private static uint AddGenericCallTypeSpecQualifiers(
        TrRmRegisterBodyReader body, uint typeSpecRef, byte qualifiers,
        TrRmTypeSpecTableReader typeSpecs,
        List<ReadOnlyMemory<byte>> additions)
    {
        if (qualifiers == 0)
        {
            return typeSpecRef;
        }

        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: true);
        if ((reference.Qualifiers & qualifiers) == qualifiers)
        {
            return typeSpecRef;
        }

        byte[] qualifiedRow;
        if (reference.IsRow)
        {
            qualifiedRow = typeSpecs.GetRow(reference.Row).ToArray();
            qualifiedRow[1] |= qualifiers;
        }
        else if (reference.Form == 0x01)
        {
            qualifiedRow = reference.TypeCode >= 0x80
                ? TrRmTypeSpecTableWriter.InlineFamily(
                    reference.TypeCode, reference.ArgumentCode,
                    (byte)(reference.Qualifiers | qualifiers))
                : TrRmTypeSpecTableWriter.Builtin(reference.TypeCode,
                    (byte)(reference.Qualifiers | qualifiers));
        }
        else
        {
            throw InvalidMap();
        }

        return AddGenericCallTypeSpecRow(typeSpecs, additions,
            qualifiedRow);
    }

    private static uint AddGenericCallTypeSpecRow(
        TrRmTypeSpecTableReader typeSpecs,
        List<ReadOnlyMemory<byte>> additions, byte[] row)
    {
        if (typeSpecs.TryFindAppendedRow(row, out var existingRow))
        {
            return existingRow;
        }
        for (var index = 0; index < additions.Count; index++)
        {
            if (additions[index].Span.SequenceEqual(row))
            {
                return checked((uint)typeSpecs.Count
                    + (uint)index + 1);
            }
        }

        var rowRef = checked((uint)typeSpecs.Count
            + (uint)additions.Count + 1);
        additions.Add(row);
        return rowRef;
    }

    private static int GetHomeFunctionPointerAnchorMethod(
        TrRmRegisterBodyReader body, int homeIndex,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        var home = GetHomeMapEntry(body, homeIndex);
        var typeMapIndex = ReadUInt16(body, home, 2);
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
        var typeState = GetRegisterTypeState(body, registerTypes, register);
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
        ConsumeWork(body, 1UL + (16UL * 18UL));
        var registerTypes = new RegisterTypeState[16];
        Array.Fill(registerTypes, RegisterTypeState.Unknown);
        var block = body.GetBlock(blockIndex);
        var entryState = GetRecordPayload(body,
            ReadUInt16(body, block, 8));
        var registerCount = checked((int)ReadUInt32(entryState, 0));
        for (var entry = 0; entry < registerCount; entry++)
        {
            ConsumeWork(body, 6);
            var offset = 4 + (entry * 5);
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

    private static RegisterTypeState[][] ComputeBlockRegisterTypeStates(
        TrRmRegisterBodyReader body, int methodDefRow,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        ref TrRmTypeSpecTableReader typeSpecs,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmContractTableReader contracts,
        TrRmImportTableReader imports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors,
        ConstantSourceValueContext constantValues,
        InitializationReferenceTypeMap initializationReferenceTypes)
    {
        var blockCount = body.BlockCount;
        ConsumeWork(body, checked(((ulong)blockCount * 4UL) + 4UL));
        var entryStates = new RegisterTypeState[blockCount][];
        var outputStates = new RegisterTypeState[blockCount][];
        var hasOutput = new bool[blockCount];
        var queued = new bool[blockCount];
        var successors = BuildBlockFlowSuccessors(body);
        ConsumeWork(body, checked((ulong)blockCount + 1UL));
        var worklist = new Queue<int>(blockCount);

        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            ConsumeWork(body);
            entryStates[blockIndex]
                = CreateBlockEntryRegisterTypes(body, blockIndex);
            ConsumeWork(body);
            worklist.Enqueue(blockIndex);
            queued[blockIndex] = true;
        }

        while (worklist.TryDequeue(out var blockIndex))
        {
            ConsumeWork(body);
            queued[blockIndex] = false;
            var registerTypes = CopyRegisterTypeStates(body,
                entryStates[blockIndex]);
            var block = body.GetBlock(blockIndex);
            var codeOffset = checked((int)ReadUInt32(body, block, 0));
            var code = body.GetBlockCode(blockIndex);
            var position = 0;
            while (position < code.Length)
            {
                ConsumeWork(body, 2);
                var opcode = ReadByte(body, code, position);
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        body.Schema, body.UsesNativePointerLoads);
                ConsumeWork(body,
                    checked((ulong)instructionLength));
                var instruction = code.Slice(position, instructionLength);
                if (opcode == 0x90)
                {
                    UpdateCallSiteRegisterTypes(body,
                        ReadByte(body, instruction, 1), registerTypes,
                        ref typeSpecs, methods, signatures, genericParams,
                        constraints,
                        imports,
                        nativeFunctionPointerAnchors);
                }
                else if (opcode == 0xE0)
                {
                    UpdateCallSiteRegisterTypes(body,
                        ReadUInt16(body, instruction, 2), registerTypes,
                        ref typeSpecs, methods, signatures, genericParams,
                        constraints,
                        imports,
                        nativeFunctionPointerAnchors);
                }
                else
                {
                    UpdateRegisterTypeStates(body, instruction,
                        registerTypes, methodDefRow, methods, contracts,
                        types,
                        fields, imports, signatures, parameters, genericParams,
                        typeSpecs,
                        constantValues, initializationReferenceTypes,
                        nativeFunctionPointerAnchors,
                        checked(codeOffset + position),
                        allowIncompletePlaceFlow: true);
                }

                position += instructionLength;
            }

            if (hasOutput[blockIndex]
                && RegisterTypeStatesEqual(body,
                    outputStates[blockIndex], registerTypes))
            {
                continue;
            }

            ConsumeWork(body);
            outputStates[blockIndex] = registerTypes;
            hasOutput[blockIndex] = true;
            if (successors[blockIndex] is not { } blockSuccessors)
            {
                continue;
            }

            foreach (var successor in blockSuccessors)
            {
                ConsumeWork(body);
                var successorEntryStates = entryStates[successor];
                var changed = false;
                for (var register = 0;
                    register < successorEntryStates.Length; register++)
                {
                    ConsumeWork(body);
                    var expected = successorEntryStates[register];
                    if (!expected.IsPlaceHandle)
                    {
                        if (expected.IsKnown
                            && registerTypes[register].IsPlaceHandle)
                        {
                            throw InvalidMap();
                        }

                        continue;
                    }

                    var incoming = registerTypes[register];
                    if (!incoming.IsPlaceHandle
                        || incoming.PlaceOrigins.Length == 0)
                    {
                        continue;
                    }

                    if (expected.AccessKind != incoming.AccessKind
                        || !typeSpecs.AreEquivalentTypeSpecRefs(
                            GetRegisterTypeSpecRef(body, expected),
                            GetRegisterTypeSpecRef(body, incoming)))
                    {
                        throw InvalidMap();
                    }

                    var mergedOrigins = MergePlaceHandleOrigins(body,
                        expected.PlaceOrigins, incoming.PlaceOrigins);
                    if (PlaceHandleOriginsEqual(body,
                            expected.PlaceOrigins, mergedOrigins))
                    {
                        continue;
                    }

                    ConsumeWork(body,
                        checked((ulong)mergedOrigins.Length + 1UL));
                    successorEntryStates[register]
                        = expected.WithPlaceOrigins(mergedOrigins);
                    changed = true;
                }

                if (changed && !queued[successor])
                {
                    ConsumeWork(body);
                    worklist.Enqueue(successor);
                    queued[successor] = true;
                }
            }
        }

        for (var sourceBlock = 0; sourceBlock < blockCount; sourceBlock++)
        {
            ConsumeWork(body);
            if (successors[sourceBlock] is not { } blockSuccessors)
            {
                continue;
            }

            foreach (var successor in blockSuccessors)
            {
                ConsumeWork(body);
                var successorEntryStates = entryStates[successor];
                for (var register = 0;
                    register < successorEntryStates.Length; register++)
                {
                    ConsumeWork(body);
                    var expected = successorEntryStates[register];
                    if (!expected.IsKnown)
                    {
                        continue;
                    }

                    if (!hasOutput[sourceBlock])
                    {
                        throw InvalidMap();
                    }

                    var incoming = outputStates[sourceBlock][register];
                    if (!incoming.IsKnown
                        || expected.IsPlaceHandle != incoming.IsPlaceHandle
                        || !typeSpecs.AreEquivalentTypeSpecRefs(
                            GetRegisterTypeSpecRef(body, expected),
                            GetRegisterTypeSpecRef(body, incoming)))
                    {
                        throw InvalidMap();
                    }

                    if (expected.IsPlaceHandle
                        && (incoming.PlaceOrigins.Length == 0
                            || expected.AccessKind != incoming.AccessKind))
                    {
                        throw InvalidMap();
                    }
                }
            }
        }

        return entryStates;
    }

    private static List<int>?[] BuildBlockFlowSuccessors(
        TrRmRegisterBodyReader body)
    {
        var blockCount = body.BlockCount;
        ConsumeWork(body, checked((ulong)blockCount + 1UL));
        var successors = new List<int>?[blockCount];
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            ConsumeWork(body);
            var code = body.GetBlockCode(blockIndex);
            ReadOnlySpan<byte> terminalInstruction = default;
            var position = 0;
            while (position < code.Length)
            {
                ConsumeWork(body);
                var opcode = ReadByte(body, code, position);
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        body.Schema, body.UsesNativePointerLoads);
                ConsumeWork(body);
                terminalInstruction = code.Slice(position,
                    instructionLength);
                position += instructionLength;
            }

            if (terminalInstruction.IsEmpty)
            {
                throw InvalidMap();
            }

            var terminalOpcode = ReadByte(body, terminalInstruction, 0);
            switch (terminalOpcode)
            {
                case 0xB0:
                    AddBlockFlowSuccessor(body, successors, blockIndex,
                        ReadByte(body, terminalInstruction, 1), blockCount);
                    break;
                case 0xE3:
                    AddBlockFlowSuccessor(body, successors, blockIndex,
                        ReadUInt16(body, terminalInstruction, 2), blockCount);
                    break;
                case >= 0xA0 and <= 0xAF:
                case 0xB1:
                    AddConditionalBlockFlowSuccessors(body, successors,
                        blockIndex,
                        ReadByte(body, terminalInstruction, 1) >> 4,
                        blockCount);
                    break;
                case 0xE4:
                case 0xE5:
                    AddConditionalBlockFlowSuccessors(body, successors,
                        blockIndex,
                        ReadUInt16(body, terminalInstruction, 2),
                        blockCount);
                    break;
                case 0xB2:
                    AddSwitchBlockFlowSuccessors(body, successors,
                        blockIndex, ReadByte(body, terminalInstruction, 1),
                        blockCount);
                    break;
                case 0xE6:
                    AddSwitchBlockFlowSuccessors(body, successors,
                        blockIndex, ReadUInt16(body, terminalInstruction, 2),
                        blockCount);
                    break;
            }
        }

        return successors;
    }

    private static void AddBlockFlowSuccessor(TrRmRegisterBodyReader body,
        List<int>?[] successors, int sourceBlock, int targetBlock,
        int blockCount)
    {
        ConsumeWork(body, 2);
        if ((uint)targetBlock >= (uint)blockCount)
        {
            throw InvalidMap();
        }

        var targets = successors[sourceBlock];
        if (targets is null)
        {
            ConsumeWork(body);
            targets = new List<int>();
            successors[sourceBlock] = targets;
        }

        targets.Add(targetBlock);
    }

    private static void AddConditionalBlockFlowSuccessors(
        TrRmRegisterBodyReader body, List<int>?[] successors,
        int sourceBlock, int recordIndex, int blockCount)
    {
        var payload = GetRecordPayload(body, recordIndex);
        if (payload.Length != 4)
        {
            throw InvalidMap();
        }

        AddBlockFlowSuccessor(body, successors, sourceBlock,
            ReadUInt16(body, payload, 0), blockCount);
        AddBlockFlowSuccessor(body, successors, sourceBlock,
            ReadUInt16(body, payload, 2), blockCount);
    }

    private static void AddSwitchBlockFlowSuccessors(
        TrRmRegisterBodyReader body, List<int>?[] successors,
        int sourceBlock, int recordIndex, int blockCount)
    {
        var payload = GetRecordPayload(body, recordIndex);
        var caseCount = ReadUInt32(body, payload, 8);
        if (12L + ((long)caseCount * 4) != payload.Length)
        {
            throw InvalidMap();
        }

        AddBlockFlowSuccessor(body, successors, sourceBlock,
            ReadUInt16(body, payload, 6), blockCount);
        for (var caseIndex = 0; caseIndex < caseCount; caseIndex++)
        {
            ConsumeWork(body);
            AddBlockFlowSuccessor(body, successors, sourceBlock,
                ReadUInt16(body, payload,
                    checked(14 + (checked((int)caseIndex) * 4))),
                blockCount);
        }
    }

    private static RegisterTypeState[] CopyRegisterTypeStates(
        TrRmRegisterBodyReader body, RegisterTypeState[] source)
    {
        ConsumeWork(body, checked((ulong)source.Length + 1UL));
        var result = new RegisterTypeState[source.Length];
        Array.Copy(source, result, source.Length);
        return result;
    }

    private static bool RegisterTypeStatesEqual(
        TrRmRegisterBodyReader body, RegisterTypeState[] left,
        RegisterTypeState[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            ConsumeWork(body);
            if (left[index].TypeMapIndex != right[index].TypeMapIndex
                || left[index].TypeSpecRef != right[index].TypeSpecRef
                || left[index].PlaceSelectionRecordIndex
                    != right[index].PlaceSelectionRecordIndex
                || left[index].RootHomeIndex != right[index].RootHomeIndex
                || left[index].AccessKind != right[index].AccessKind
                || !PlaceHandleOriginsEqual(body,
                    left[index].PlaceOrigins, right[index].PlaceOrigins))
            {
                return false;
            }
        }

        return true;
    }

    private static PlaceHandleOrigin[] MergePlaceHandleOrigins(
        TrRmRegisterBodyReader body, PlaceHandleOrigin[] left,
        PlaceHandleOrigin[] right)
    {
        if (right.Length == 0)
        {
            return left;
        }
        if (left.Length == 0)
        {
            ConsumeWork(body, checked((ulong)right.Length + 1UL));
            return right;
        }

        var count = left.Length;
        for (var rightIndex = 0; rightIndex < right.Length; rightIndex++)
        {
            var found = false;
            for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
            {
                ConsumeWork(body);
                if (left[leftIndex] == right[rightIndex])
                {
                    found = true;
                    break;
                }
            }
            for (var priorRightIndex = 0;
                !found && priorRightIndex < rightIndex; priorRightIndex++)
            {
                ConsumeWork(body);
                found = right[priorRightIndex] == right[rightIndex];
            }

            if (!found)
            {
                count++;
            }
        }

        if (count == left.Length)
        {
            return left;
        }

        ConsumeWork(body, checked((2UL * (ulong)count) + 1UL));
        var merged = new PlaceHandleOrigin[count];
        Array.Copy(left, merged, left.Length);
        var mergedCount = left.Length;
        for (var rightIndex = 0; rightIndex < right.Length; rightIndex++)
        {
            var found = false;
            for (var existingIndex = 0; existingIndex < mergedCount;
                existingIndex++)
            {
                ConsumeWork(body);
                if (merged[existingIndex] == right[rightIndex])
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                merged[mergedCount++] = right[rightIndex];
            }
        }

        return merged;
    }

    private static bool PlaceHandleOriginsEqual(
        TrRmRegisterBodyReader body, PlaceHandleOrigin[] left,
        PlaceHandleOrigin[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            ConsumeWork(body);
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void UpdateRegisterTypeStates(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        RegisterTypeState[] registerTypes, int methodDefRow,
        TrRmMethodDefTableReader methods,
        TrRmContractTableReader contracts,
        TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields, TrRmImportTableReader imports,
        TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmGenericParamTableReader genericParams,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues,
        InitializationReferenceTypeMap initializationReferenceTypes,
        TrRmNativeFunctionPointerAnchorTableReader
            nativeFunctionPointerAnchors,
        int codeOffset,
        bool allowIncompletePlaceFlow)
    {
        var opcode = ReadByte(body, instruction, 0);
        var operand = instruction.Length > 1
            ? ReadByte(body, instruction, 1)
            : (byte)0;
        if (opcode == 0xED && body.Schema.UsesObjectConstruction())
        {
            var construction = GetRecordPayload(body,
                ReadUInt16(body, instruction, 2));
            if (ReadByte(body, construction, 0) == 2
                && ReadByte(body, construction, 1) == 1)
            {
                var destination = ReadUInt16(body, construction, 4);
                var contextHomeIndex = ReadUInt16(body, construction, 2);
                var callerRole = contracts.HasConstructionClause(
                        methodDefRow)
                    ? contracts.GetConstructionClause(methodDefRow).RoleTag
                    : (byte)0;
                var callerEntry = body.Schema
                        .UsesPropertyInitializedState()
                    && callerRole != 2;
                if (destination >= 14
                    || (callerRole != 2 && !callerEntry))
                {
                    throw InvalidMap();
                }

                var contextTypeMapIndex
                    = GetHomeTypeMapIndex(body, contextHomeIndex);
                if (!callerEntry && !MatchesTypeMapEntry(body,
                        contextTypeMapIndex,
                        signatures.GetResultTypeSpec(
                            methods.GetSignature(methodDefRow)),
                        methods.GetOwnerModule(methodDefRow), types,
                        typeSpecs, genericParams, imports, constantValues,
                        nativeFunctionPointerAnchors))
                {
                    throw InvalidMap();
                }

                SetRegisterTypeState(body, registerTypes, destination,
                    RegisterTypeState.FromTypeMap(contextTypeMapIndex));
            }
            return;
        }

        if (opcode == 0xDD)
        {
            UpdateInitializationReferenceRegister(body, instruction,
                registerTypes, methodDefRow, ownerModule, methods, types,
                fields, fieldPaths, layouts, imports, typeSpecs,
                constantValues, initializationReferenceTypes);
            return;
        }

        if (opcode is 0xD8 or 0x61 or 0x91)
        {
            UpdateFixedArrayPlaceRegister(body, instruction, registerTypes,
                methodDefRow, methods, contracts, types, fields, imports,
                methods.GetOwnerModule(methodDefRow), typeSpecs, constantValues,
                codeOffset, allowIncompletePlaceFlow);
            return;
        }

        if (opcode == 0xDA)
        {
            UpdateFixedArrayPlaceLoadRegister(body, instruction,
                registerTypes, methodDefRow, methods, contracts, types, fields,
                fieldPaths, layouts, imports,
                methods.GetOwnerModule(methodDefRow), typeSpecs, constantValues,
                codeOffset,
                allowIncompletePlaceFlow);
            return;
        }

        if (opcode == 0x63 && body.Schema.UsesObjectConstruction())
        {
            var destination = operand & 0x0F;
            var placeRegister = operand >> 4;
            if (destination >= 14 || placeRegister >= 14)
            {
                throw InvalidMap();
            }

            var place = GetRegisterTypeState(body, registerTypes,
                placeRegister);
            var placeTypeSpecRef = GetRegisterTypeSpecRef(body, place);
            if (!place.IsPlaceHandle
                || place.AccessKind != 1
                || place.PlaceOrigins.Length == 0
                || !IsCopyableRegisterValueType(typeSpecs,
                    placeTypeSpecRef, constantValues, body.Schema))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromTypeSpecRef(placeTypeSpecRef));
            return;
        }

        if (opcode == 0xEC)
        {
            UpdateStorageAddressResult(body, instruction, registerTypes,
                methods.GetOwnerModule(methodDefRow), typeSpecs,
                constantValues, allowIncompletePlaceFlow);
            return;
        }

        if (opcode is 0xD9 or 0x92)
        {
            var store = GetRecordPayload(body,
                opcode == 0xD9
                    ? ReadUInt16(body, instruction, 2)
                    : ReadByte(body, instruction, 1));
            if (ValidateStandardInitializationReferenceStore(body, store,
                    registerTypes, methodDefRow, methods, signatures,
                    parameters, types, genericParams, imports, typeSpecs,
                    constantValues, nativeFunctionPointerAnchors))
            {
                return;
            }

            if (ValidateConstructionClassFieldStore(body, store,
                    registerTypes, methodDefRow, methods, contracts, types,
                    fields, imports, genericParams, typeSpecs,
                    constantValues, nativeFunctionPointerAnchors))
            {
                return;
            }

            if (body.Schema.UsesObjectConstruction())
            {
                var constructionRole
                    = contracts.HasConstructionClause(methodDefRow)
                        ? contracts.GetConstructionClause(methodDefRow).RoleTag
                        : (byte)0;
                var placeKind = ReadByte(body, store, 0);
                var sourceKind = ReadByte(body, store, 1);
                if (placeKind == 2)
                {
                    var placeHome = GetHomeMapEntry(body,
                        ReadUInt16(body, store, 4));
                    var role = ReadByte(body, placeHome, 0);
                    if (role == 4
                        || (constructionRole == 1 && role == 1))
                    {
                        throw InvalidMap();
                    }
                }

                if (sourceKind == 2)
                {
                    var sourceHome = GetHomeMapEntry(body,
                        ReadUInt16(body, store, 6));
                    var role = ReadByte(body, sourceHome, 0);
                    if (role == 4
                        || (constructionRole == 1 && role == 1))
                    {
                        throw InvalidMap();
                    }
                }
            }

            if (ReadByte(body, store, 1) == 0)
            {
                ValidateEmptyArrayInitializationStore(body, store,
                    registerTypes, methods.GetOwnerModule(methodDefRow),
                    typeSpecs, constantValues);
            }
            else if (body.Schema
                    .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
                && ReadByte(body, store, 3) == 6)
            {
                ValidateRepeatCopyStore(body, store, registerTypes,
                    typeSpecs);
            }
            else if (body.Schema
                    .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
                && ReadByte(body, store, 2) == 2
                && ReadByte(body, store, 3) == 1
                && IsFieldReplacementPlaceSelection(body, store,
                    registerTypes))
            {
                ValidateArrayFieldReplacementStore(body, store,
                    registerTypes, fields, typeSpecs, 2, 2);
            }
            else if (body.Schema
                    .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
                && ReadByte(body, store, 2) == 1
                && ReadByte(body, store, 3) == 1
                && IsFieldReplacementPlaceSelection(body, store,
                    registerTypes))
            {
                ValidateArrayFieldReplacementStore(body, store,
                    registerTypes, fields, typeSpecs, 1, 3);
            }
            else if (IsUnitTypeSpecRef(typeSpecs,
                         GetRegisterTypeSpecRef(body, GetRegisterTypeState(
                             body, registerTypes,
                             ReadUInt16(body, store, 4)))))
            {
                ValidateUnitArrayElementStore(body, store, registerTypes,
                    methods.GetOwnerModule(methodDefRow), typeSpecs,
                    constantValues);
            }
            else if (body.Schema
                .UsesForeignInvocationStorageFixedArrays()
                && ReadByte(body, store, 1) is 1 or 2
                && (ReadByte(body, store, 1) == 2
                    || ReadUInt16(body, store, 6) < 14))
            {
                var place = GetRegisterTypeState(body, registerTypes,
                    ReadUInt16(body, store, 4));
                if (!allowIncompletePlaceFlow
                    || !place.IsPlaceHandle
                    || place.PlaceOrigins.Length != 0)
                {
                    ValidateArrayElementValueStore(body, store,
                        registerTypes, typeSpecs, constantValues);
                }
            }
            return;
        }

        if (opcode == 0x64 && body.Schema.UsesObjectConstruction())
        {
            var placeRegister = operand & 0x0F;
            var sourceRegister = operand >> 4;
            var place = ReadRegisterTypeState(body, registerTypes,
                placeRegister);
            var hasInitializerPlaceOrigin = false;
            if (place.IsPlaceHandle)
            {
                foreach (var origin in place.PlaceOrigins)
                {
                    ConsumeWork(body);
                    hasInitializerPlaceOrigin |= ReadByte(body,
                        GetHomeMapEntry(body, origin.RootHomeIndex), 0) == 1;
                }
            }

            if (hasInitializerPlaceOrigin)
            {
                if (!ValidateConstructionClassFieldCopyStore(body,
                        registerTypes, placeRegister, sourceRegister,
                        methodDefRow, methods, contracts, types, fields,
                        imports, genericParams, typeSpecs, constantValues,
                        nativeFunctionPointerAnchors))
                {
                    throw InvalidMap();
                }
                return;
            }
        }

        if (opcode == 0x10)
        {
            var destination = operand & 0x0F;
            var source = operand >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                source < 14
                    ? ReadRegisterTypeState(body, registerTypes, source)
                    : source == 14
                        && body.Schema
                            .UsesForeignInvocationStorageFixedArrayUnitInitialization()
                            ? RegisterTypeState.FromTypeSpecRef(0xF200_0008)
                    : RegisterTypeState.Unknown);
            return;
        }

        if (opcode == 0x13)
        {
            var destination = operand & 0x0F;
            var homeIndex = operand >> 4;
            if (destination >= 14 || homeIndex >= body.HomeCount)
            {
                throw InvalidMap();
            }

            var home = GetHomeMapEntry(body, homeIndex);
            if (ReadByte(body, home, 0) != 2
                || ReadByte(body, home, 1) != 0)
            {
                throw InvalidMap();
            }

            var typeMapIndex = ReadUInt16(body, home, 2);
            if (!IsCopyableRegisterValueType(typeSpecs,
                    GetTypeMapTypeSpecRef(body, typeMapIndex),
                    constantValues, body.Schema))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromTypeMap(typeMapIndex));
            return;
        }

        if (opcode == 0x11)
        {
            var destination = operand & 0x0F;
            var formalOrdinal = operand >> 4;
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
                    constantValues, body.Schema))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromTypeSpecRef(typeSpecRef));
            return;
        }

        if (opcode == 0x1B)
        {
            var destination = operand & 0x0F;
            var source = operand >> 4;
            if (!body.UsesNativePointerLoads || destination >= 14
                || source >= 14)
            {
                throw InvalidMap();
            }

            var sourceState = GetRegisterTypeState(body, registerTypes,
                source);
            if (sourceState.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            var pointerTypeSpecRef = GetRegisterTypeSpecRef(body,
                sourceState);
            if (!typeSpecs.TryGetNativePointerPointeeTypeSpecRef(
                    pointerTypeSpecRef, out var pointeeTypeSpecRef)
                || !IsCopyableRegisterValueType(typeSpecs,
                    pointeeTypeSpecRef, constantValues, body.Schema)
                || !TrRmFeatureManifestValidator.Requires(
                    constantValues.Features, constantValues.Modules,
                    methods.GetOwnerModule(methodDefRow),
                    TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                    body.Schema))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromTypeSpecRef(pointeeTypeSpecRef));
            return;
        }

        if (opcode == 0x12)
        {
            var destination = operand & 0x0F;
            var firstFormal = operand >> 4;
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
                        constantValues, body.Schema))
                {
                    throw InvalidMap();
                }

                SetRegisterTypeState(body, registerTypes, destination + pair,
                    RegisterTypeState.FromTypeSpecRef(typeSpecRef));
            }
            return;
        }

        if (opcode == 0x16)
        {
            var destination = operand & 0x0F;
            var constantIndex = operand >> 4;
            if (destination >= 14 || constantIndex >= body.ConstantCount)
            {
                throw InvalidMap();
            }

            ConsumeWork(body, 9);
            var constant = body.ConstantMap.Slice(constantIndex * 8, 8);
            var typeSpecRef = ReadUInt32(body, constant, 0);
            if (!IsCopyableRegisterValueType(typeSpecs, typeSpecRef,
                    constantValues, body.Schema))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromTypeSpecRef(typeSpecRef));
            return;
        }

        if (opcode == 0xD4)
        {
            var destination = operand & 0x0F;
            var constantIndex = ReadUInt16(body, instruction, 2);
            if ((operand & 0xF0) != 0 || destination >= 14
                || constantIndex >= body.ConstantCount)
            {
                throw InvalidMap();
            }

            ConsumeWork(body, 9);
            var constant = body.ConstantMap.Slice(constantIndex * 8, 8);
            var typeSpecRef = ReadUInt32(body, constant, 0);
            if (!IsCopyableRegisterValueType(typeSpecs, typeSpecRef,
                    constantValues, body.Schema))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromTypeSpecRef(typeSpecRef));
            return;
        }

        if (opcode == 0xD5)
        {
            var destination = operand & 0x0F;
            var homeIndex = ReadUInt16(body, instruction, 2);
            if (destination >= 14 || homeIndex >= body.HomeCount)
            {
                throw InvalidMap();
            }

            var home = GetHomeMapEntry(body, homeIndex);
            var typeMapIndex = ReadUInt16(body, home, 2);
            if (!IsCopyableRegisterValueType(typeSpecs,
                    GetTypeMapTypeSpecRef(body, typeMapIndex),
                    constantValues, body.Schema))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromTypeMap(typeMapIndex));
            return;
        }

        if (opcode == 0xD6 && operand >> 4 == 1)
        {
            var homeIndex = ReadUInt16(body, instruction, 2);
            for (var register = 0; register < registerTypes.Length;
                register++)
            {
                ConsumeWork(body);
                var state = ReadRegisterTypeState(body, registerTypes,
                    register);
                if (!state.IsPlaceHandle)
                {
                    continue;
                }

                foreach (var origin in state.PlaceOrigins)
                {
                    ConsumeWork(body);
                    if (origin.RootHomeIndex == homeIndex)
                    {
                        SetRegisterTypeState(body, registerTypes, register,
                            RegisterTypeState.Unknown);
                        break;
                    }
                }
            }

            return;
        }

        if (opcode is 0x20 or 0x26)
        {
            var destination = operand & 0x0F;
            var source = operand >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var sourceState = source < 14
                ? ReadRegisterTypeState(body, registerTypes, source)
                : RegisterTypeState.Unknown;
            var sourceType = sourceState.IsKnown
                && !sourceState.IsPlaceHandle
                ? GetRegisterTypeSpecRef(body, sourceState)
                : 0;
            var isFloating = opcode == 0x26;
            SetRegisterTypeState(body, registerTypes, destination,
                sourceType != 0
                    && IsRegisterArithmeticType(typeSpecs, sourceType, isFloating)
                        ? sourceState
                        : RegisterTypeState.Unknown);
            return;
        }

        if (opcode is (>= 0x21 and <= 0x25)
            or (>= 0x27 and <= 0x2B))
        {
            var destination = operand & 0x0F;
            var right = operand >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var destinationState = ReadRegisterTypeState(
                body, registerTypes, destination);
            var rightState = right < 14
                ? ReadRegisterTypeState(body, registerTypes, right)
                : RegisterTypeState.Unknown;
            if (destinationState.IsKnown && rightState.IsKnown
                && !destinationState.IsPlaceHandle && !rightState.IsPlaceHandle)
            {
                var destinationType = GetRegisterTypeSpecRef(
                    body, destinationState);
                var rightType = GetRegisterTypeSpecRef(body, rightState);
                var isFloating = opcode >= 0x27;
                if (typeSpecs.AreEquivalentTypeSpecRefs(
                        destinationType, rightType)
                    && IsRegisterArithmeticType(
                        typeSpecs, destinationType, isFloating))
                {
                    return;
                }
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.Unknown);
            return;
        }

        if (opcode is >= 0x30 and <= 0x32)
        {
            var destination = operand & 0x0F;
            var right = operand >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var destinationState = ReadRegisterTypeState(
                body, registerTypes, destination);
            var rightState = right < 14
                ? ReadRegisterTypeState(body, registerTypes, right)
                : RegisterTypeState.Unknown;
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

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.Unknown);
            return;
        }

        if (opcode == 0x33)
        {
            var destination = operand & 0x0F;
            var source = operand >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var sourceState = source < 14
                ? ReadRegisterTypeState(body, registerTypes, source)
                : RegisterTypeState.Unknown;
            SetRegisterTypeState(body, registerTypes, destination,
                sourceState.IsKnown
                    && !sourceState.IsPlaceHandle
                    && IsRegisterBitwiseType(typeSpecs,
                        GetRegisterTypeSpecRef(body, sourceState))
                        ? sourceState : RegisterTypeState.Unknown);
            return;
        }

        if (opcode is >= 0x34 and <= 0x36)
        {
            var destination = operand & 0x0F;
            var countRegister = operand >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var destinationState = ReadRegisterTypeState(
                body, registerTypes, destination);
            var countState = countRegister < 14
                ? ReadRegisterTypeState(body, registerTypes, countRegister)
                : RegisterTypeState.Unknown;
            if (destinationState.IsKnown && countState.IsKnown
                && !destinationState.IsPlaceHandle && !countState.IsPlaceHandle
                && IsRegisterShiftIntegerType(typeSpecs,
                    GetRegisterTypeSpecRef(body, destinationState))
                && IsRegisterShiftIntegerType(typeSpecs,
                    GetRegisterTypeSpecRef(body, countState)))
            {
                return;
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.Unknown);
            return;
        }

        if (opcode == 0x40)
        {
            var destination = operand & 0x0F;
            var right = operand >> 4;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var destinationState = ReadRegisterTypeState(
                body, registerTypes, destination);
            var rightState = right < 14
                ? ReadRegisterTypeState(body, registerTypes, right)
                : RegisterTypeState.Unknown;
            SetRegisterTypeState(body, registerTypes, destination,
                destinationState.IsKnown && rightState.IsKnown
                    && !destinationState.IsPlaceHandle
                    && !rightState.IsPlaceHandle
                    && typeSpecs.AreEquivalentTypeSpecRefs(
                        GetRegisterTypeSpecRef(body, destinationState),
                        GetRegisterTypeSpecRef(body, rightState))
                    ? RegisterTypeState.FromTypeSpecRef(0xF200_000B)
                    : RegisterTypeState.Unknown);
            return;
        }

        if (opcode == 0x41)
        {
            var destination = operand & 0x0F;
            if (destination >= 14)
            {
                throw InvalidMap();
            }

            var sourceState = ReadRegisterTypeState(
                body, registerTypes, destination);
            SetRegisterTypeState(body, registerTypes, destination,
                sourceState.IsKnown
                    && IsRegisterType(typeSpecs,
                        GetRegisterTypeSpecRef(body, sourceState), 0x0B)
                        ? RegisterTypeState.FromTypeSpecRef(0xF200_0001)
                        : RegisterTypeState.Unknown);
            return;
        }

        if (opcode is >= 0x50 and <= 0x52)
        {
            var destination = operand & 0x0F;
            var typeMapIndex = operand >> 4;
            if (destination >= 14 || typeMapIndex >= body.TypeCount)
            {
                throw InvalidMap();
            }

            var destinationState = ReadRegisterTypeState(
                body, registerTypes, destination);
            SetRegisterTypeState(body, registerTypes, destination,
                destinationState.IsKnown
                    && !destinationState.IsPlaceHandle
                    ? RegisterTypeState.FromTypeMap(typeMapIndex)
                    : RegisterTypeState.Unknown);
            return;
        }

        if (opcode == 0xD0
            && body.Schema == TrRmArtifactSchema.LocalAggregateLayoutDraft)
        {
            var control = operand;
            var sourceRegister = control & 0x0F;
            var destination = control >> 4;
            var typeMapIndex = ReadUInt16(body, instruction, 2);
            if (sourceRegister >= 14 || destination >= 14
                || typeMapIndex >= body.TypeCount)
            {
                throw InvalidMap();
            }

            var sourceState = GetRegisterTypeState(body, registerTypes,
                sourceRegister);
            if (sourceState.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            var destinationTypeSpecRef = GetTypeMapTypeSpecRef(body,
                typeMapIndex);
            if (!IsCopyableRegisterValueType(typeSpecs,
                    destinationTypeSpecRef, constantValues, body.Schema))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromTypeMap(typeMapIndex));
            return;
        }

        if (opcode == 0xC0
            && body.Schema == TrRmArtifactSchema.LocalAggregateLayoutDraft)
        {
            var payload = ReadUInt16(body, instruction, 2);
            var destination = payload & 0x0F;
            var result1 = (payload >> 4) & 0x0F;
            var source0 = (payload >> 8) & 0x0F;
            var source1 = payload >> 12;
            var isUnary = operand is 0x04 or 0x08 or 0x25;
            var isFloating = operand is >= 0x20 and <= 0x25;
            var isSupportedOperation = operand is (>= 0x01 and <= 0x08)
                or 0x0C or 0x0D or (>= 0x20 and <= 0x25);
            if (!isSupportedOperation || destination >= 14
                || result1 != 15 || source0 >= 14
                || (isUnary ? source1 != 15 : source1 >= 14))
            {
                throw InvalidMap();
            }

            var sourceState = GetRegisterTypeState(
                body, registerTypes, source0);
            if (sourceState.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            var sourceType = GetRegisterTypeSpecRef(body, sourceState);
            if (!IsRegisterArithmeticType(typeSpecs, sourceType,
                    isFloating))
            {
                throw InvalidMap();
            }
            if (!isUnary)
            {
                var rightState = GetRegisterTypeState(
                    body, registerTypes, source1);
                if (rightState.IsPlaceHandle
                    || !typeSpecs.AreEquivalentTypeSpecRefs(sourceType,
                        GetRegisterTypeSpecRef(body, rightState)))
                {
                    throw InvalidMap();
                }
            }

            SetRegisterTypeState(body, registerTypes, destination,
                sourceState);
            return;
        }

        if (opcode == 0xC1
            && body.Schema == TrRmArtifactSchema.LocalAggregateLayoutDraft)
        {
            var payload = ReadUInt16(body, instruction, 2);
            var destination = payload & 0x0F;
            var result1 = (payload >> 4) & 0x0F;
            var source0 = (payload >> 8) & 0x0F;
            var source1 = payload >> 12;
            if (operand is not (0x01 or 0x02 or 0x03)
                || destination >= 14 || result1 != 15
                || source0 >= 14 || source1 >= 14)
            {
                throw InvalidMap();
            }

            var sourceState = GetRegisterTypeState(
                body, registerTypes, source0);
            var rightState = GetRegisterTypeState(
                body, registerTypes, source1);
            var sourceType = GetRegisterTypeSpecRef(body, sourceState);
            if (sourceState.IsPlaceHandle || rightState.IsPlaceHandle
                || !IsRegisterArithmeticType(typeSpecs, sourceType,
                    isFloating: false)
                || !typeSpecs.AreEquivalentTypeSpecRefs(sourceType,
                    GetRegisterTypeSpecRef(body, rightState)))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                sourceState);
            return;
        }

        if (opcode == 0xC2)
        {
            var payload = ReadUInt16(body, instruction, 2);
            var destination = payload & 0x0F;
            var secondResult = (payload >> 4) & 0x0F;
            var source0 = (payload >> 8) & 0x0F;
            var source1 = payload >> 12;
            if (destination >= 14 || secondResult != 15
                || (source0 >= 14 && source1 >= 14))
            {
                throw InvalidMap();
            }

            var generalRegister = source0 < 14 ? source0 : source1;
            var generalState = GetRegisterTypeState(body, registerTypes,
                generalRegister);
            if (generalState.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            var valueType = GetRegisterTypeSpecRef(body, generalState);
            var specialRegister = source0 >= 14 ? source0 : source1 >= 14
                ? source1 : -1;
            if (specialRegister < 0)
            {
                var secondState = GetRegisterTypeState(body,
                    registerTypes, source1);
                if (secondState.IsPlaceHandle
                    || !typeSpecs.AreEquivalentTypeSpecRefs(valueType,
                        GetRegisterTypeSpecRef(body, secondState)))
                {
                    throw InvalidMap();
                }
            }

            if (specialRegister >= 0
                && !(IsRegisterArithmeticType(typeSpecs, valueType,
                        isFloating: false)
                    || IsRegisterArithmeticType(typeSpecs, valueType,
                        isFloating: true)
                    || typeSpecs.AreEquivalentTypeSpecRefs(valueType,
                        BooleanTypeSpecRef)))
            {
                throw InvalidMap();
            }

            if (operand == 0)
            {
                if (!IsRegisterArithmeticType(typeSpecs, valueType,
                        isFloating: false)
                    && !IsRegisterArithmeticType(typeSpecs, valueType,
                        isFloating: true))
                {
                    throw InvalidMap();
                }
            }
            else if (operand is 1 or 2)
            {
                if (!typeSpecs.AreEquivalentTypeSpecRefs(valueType,
                        BooleanTypeSpecRef))
                {
                    throw InvalidMap();
                }

                SetRegisterTypeState(body, registerTypes, destination,
                    RegisterTypeState.FromTypeSpecRef(
                        BooleanTypeSpecRef));
            }
            else
            {
                throw InvalidMap();
            }

            return;
        }

        if (opcode == 0xC3)
        {
            var payload = ReadUInt16(body, instruction, 2);
            var destination = payload & 0x0F;
            var conditionRegister = (payload >> 4) & 0x0F;
            var unused0 = (payload >> 8) & 0x0F;
            var unused1 = payload >> 12;
            if (operand is not (1 or 2 or 3 or 4 or 6 or 13)
                || destination >= 14 || conditionRegister >= 14
                || unused0 != 15 || unused1 != 15)
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromTypeSpecRef(BooleanTypeSpecRef));
            return;
        }

        if (opcode == 0xC0 && operand is 0x04 or 0x08)
        {
            var payload = ReadUInt16(body, instruction, 2);
            var destination = payload & 0x0F;
            var result1 = (payload >> 4) & 0x0F;
            var source0 = (payload >> 8) & 0x0F;
            var source1 = payload >> 12;
            if (destination >= 14 || result1 != 15
                || source0 >= 14 || source1 != 15)
            {
                throw InvalidMap();
            }

            var sourceState = GetRegisterTypeState(
                body, registerTypes, source0);
            if (sourceState.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            var sourceType = GetRegisterTypeSpecRef(body, sourceState);
            if (!IsRegisterArithmeticType(typeSpecs, sourceType,
                    isFloating: false))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                sourceState);
            return;
        }

        if (opcode == 0xC0 && operand == 0x25)
        {
            var payload = ReadUInt16(body, instruction, 2);
            var destination = payload & 0x0F;
            var result1 = (payload >> 4) & 0x0F;
            var source0 = (payload >> 8) & 0x0F;
            var source1 = payload >> 12;
            if (destination >= 14 || result1 != 15
                || source0 >= 14 || source1 != 15)
            {
                throw InvalidMap();
            }

            var sourceState = GetRegisterTypeState(
                body, registerTypes, source0);
            if (sourceState.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            var sourceType = GetRegisterTypeSpecRef(body, sourceState);
            if (!IsRegisterArithmeticType(typeSpecs, sourceType,
                    isFloating: true))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                sourceState);
            return;
        }

        if (opcode == 0xC0
            && operand is 0x01 or 0x02 or 0x03 or 0x05 or 0x06 or 0x07
                or 0x0C or 0x0D or (>= 0x20 and <= 0x24))
        {
            var payload = ReadUInt16(body, instruction, 2);
            var destination = payload & 0x0F;
            var result1 = (payload >> 4) & 0x0F;
            var source0 = (payload >> 8) & 0x0F;
            var source1 = payload >> 12;
            if (destination >= 14 || result1 != 15 || source0 >= 14)
            {
                throw InvalidMap();
            }

            var sourceState = GetRegisterTypeState(
                body, registerTypes, source0);
            if (sourceState.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            var sourceType = GetRegisterTypeSpecRef(body, sourceState);
            var isFloating = operand is >= 0x20 and <= 0x24;
            if (!IsRegisterArithmeticType(typeSpecs, sourceType,
                    isFloating))
            {
                throw InvalidMap();
            }

            if (source1 < 14)
            {
                var rightState = GetRegisterTypeState(
                    body, registerTypes, source1);
                if (rightState.IsPlaceHandle
                    || !typeSpecs.AreEquivalentTypeSpecRefs(sourceType,
                        GetRegisterTypeSpecRef(body, rightState)))
                {
                    throw InvalidMap();
                }
            }
            else if (source1 is not (14 or 15))
            {
                throw InvalidMap();
            }

            SetRegisterTypeState(body, registerTypes, destination,
                sourceState);
            return;
        }

        if (opcode is 0x00 or 0x17 or 0x18
            or (>= 0xA0 and <= 0xAF)
            or 0xB0 or 0xB1 or 0xB2 or 0xB3 or 0xB4 or 0xB5
            or 0xB7 or 0xB8
            or (>= 0xE3 and <= 0xE8) or 0xEB)
        {
            return;
        }

        ClearRegisterTypeStates(body, registerTypes);
    }

    private static bool ValidateStandardInitializationReferenceStore(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> store,
        RegisterTypeState[] registerTypes, int methodDefRow,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeDefTableReader types,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader
            nativeFunctionPointerAnchors)
    {
        if (body.Schema is not (TrRmArtifactSchema.Standard
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft)
            || ReadByte(body, store, 0) is not (1 or 2))
        {
            return false;
        }

        var placeKind = ReadByte(body, store, 0);
        var placeIndex = ReadUInt16(body, store, 4);
        int placeHomeIndex = -1;
        uint placeTypeSpecRef;
        if (placeKind == 1)
        {
            var place = GetRegisterTypeState(body, registerTypes,
                placeIndex);
            if (place.IsPlaceHandle)
            {
                return false;
            }

            placeTypeSpecRef = GetRegisterTypeSpecRef(body, place);
        }
        else
        {
            placeHomeIndex = placeIndex;
            var placeHome = GetHomeMapEntry(body, placeHomeIndex);
            if (ReadByte(body, placeHome, 0) is not (2 or 3))
            {
                return false;
            }

            placeTypeSpecRef = GetTypeMapTypeSpecRef(body,
                GetHomeTypeMapIndex(body, placeHomeIndex));
        }

        if (!typeSpecs.TryGetInitializationReferenceReferentTypeSpecRef(
                placeTypeSpecRef, out var referentTypeSpecRef,
                out var isWritable))
        {
            return false;
        }

        var transition = ReadByte(body, store, 2);
        var sourceUse = ReadByte(body, store, 3);
        if (transition is not (1 or 2) || sourceUse is not (1 or 2)
            || (transition == 2 && !isWritable)
            || ReadByte(body, store, 12) != 0
            || ReadUInt32(body, store, 8) != 0)
        {
            throw InvalidMap();
        }

        var ownerModule = methods.GetOwnerModule(methodDefRow);
        if (placeHomeIndex >= 0)
        {
            var placeHome = GetHomeMapEntry(body, placeHomeIndex);
            if (ReadByte(body, placeHome, 0) == 2)
            {
                var formalOrdinal = ReadUInt16(body, placeHome, 4);
                var signature = methods.GetSignature(methodDefRow);
                var (firstParameter, parameterCount)
                    = signatures.GetParamRange(signature);
                if (formalOrdinal >= parameterCount
                    || parameters.GetMode(firstParameter + formalOrdinal) != 0
                    || parameters.GetFlags(firstParameter + formalOrdinal) != 0
                    || !MatchesTypeMapEntry(body,
                        GetHomeTypeMapIndex(body, placeHomeIndex),
                        parameters.GetTypeSpec(firstParameter + formalOrdinal),
                        ownerModule, types, typeSpecs, genericParams, imports,
                        constantValues, nativeFunctionPointerAnchors))
                {
                    throw InvalidMap();
                }
            }
        }

        var sourceKind = ReadByte(body, store, 1);
        var sourceIndex = ReadUInt16(body, store, 6);
        RegisterTypeState sourceState = RegisterTypeState.Unknown;
        uint sourceTypeSpecRef;
        if (sourceKind == 1)
        {
            sourceState = GetRegisterTypeState(body, registerTypes,
                sourceIndex);
            if (sourceState.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            sourceTypeSpecRef = GetRegisterTypeSpecRef(body, sourceState);
        }
        else
        {
            sourceTypeSpecRef = GetTypeMapTypeSpecRef(body,
                GetHomeTypeMapIndex(body, sourceIndex));
        }

        if (!AreEquivalentSelectedTypeSpecRefs(sourceTypeSpecRef,
                referentTypeSpecRef, ownerModule, types, typeSpecs,
                genericParams, imports, constantValues,
                nativeFunctionPointerAnchors)
            || (sourceUse == 1
                && !IsCopyableRegisterValueType(typeSpecs,
                    sourceTypeSpecRef, constantValues, body.Schema)))
        {
            throw InvalidMap();
        }

        return true;
    }

    private static bool ValidateConstructionClassFieldStore(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> store,
        RegisterTypeState[] registerTypes, int methodDefRow,
        TrRmMethodDefTableReader methods,
        TrRmContractTableReader contracts,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmImportTableReader imports,
        TrRmGenericParamTableReader genericParams,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader
            nativeFunctionPointerAnchors)
    {
        var constructionRole
            = contracts.HasConstructionClause(methodDefRow)
                ? contracts.GetConstructionClause(methodDefRow).RoleTag
                : (byte)0;
        if ((!body.Schema.UsesObjectConstruction()
                && !(body.Schema.UsesConstructionContracts()
                    && constructionRole == 1))
            || store.Length != 16
            || ReadByte(body, store, 0) != 1)
        {
            return false;
        }

        var placeIndex = ReadUInt16(body, store, 4);
        if (placeIndex >= 14)
        {
            throw InvalidMap();
        }

        var place = GetRegisterTypeState(body, registerTypes, placeIndex);
        if (!place.IsPlaceHandle || place.PlaceOrigins.Length == 0)
        {
            return false;
        }

        var selectionRecordIndex
            = place.PlaceOrigins[0].PlaceSelectionRecordIndex;
        var selectionRecord = body.GetRecord(selectionRecordIndex);
        if (selectionRecord[0] != 0x04)
        {
            return false;
        }

        var selection = selectionRecord[4..];
        if (ReadByte(body, selection, 0) != 2
            || ReadByte(body, selection, 3) != 2
            || ReadByte(body, selection, 9) != 1)
        {
            return false;
        }

        var fieldRowValue = ReadUInt32(body, selection, 12);
        if (fieldRowValue is 0 or > 0x00FF_FFFF
            || fieldRowValue > (uint)fields.Count)
        {
            throw InvalidMap();
        }

        var fieldRow = checked((int)fieldRowValue);
        var contextHomeIndex = ReadUInt16(body, selection, 6);
        var contextHome = GetHomeMapEntry(body, contextHomeIndex);
        var construction = contracts.HasConstructionClause(methodDefRow)
            ? contracts.GetConstructionClause(methodDefRow) : default;
        var isPropertyInitializedState
            = body.Schema.UsesPropertyInitializedState();
        var fieldTypeSpecRef = fields.GetTypeSpecRef(fieldRow);
        var sourceKind = ReadByte(body, store, 1);
        var sourceIndex = ReadUInt16(body, store, 6);
        uint sourceTypeSpecRef;
        if (sourceKind == 1)
        {
            if (sourceIndex >= 14)
            {
                throw InvalidMap();
            }

            var source = GetRegisterTypeState(body, registerTypes,
                sourceIndex);
            if (source.IsPlaceHandle)
            {
                throw InvalidMap();
            }
            sourceTypeSpecRef = GetRegisterTypeSpecRef(body, source);
        }
        else if (sourceKind == 2)
        {
            var sourceHome = GetHomeMapEntry(body, sourceIndex);
            if (ReadByte(body, sourceHome, 0) is not (2 or 3))
            {
                throw InvalidMap();
            }
            sourceTypeSpecRef = GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, sourceHome, 2));
        }
        else
        {
            throw InvalidMap();
        }

        var fieldOwnerType = fields.GetOwnerTypeDef(fieldRow);
        var fieldFlags = fields.GetFlags(fieldRow);
        var memberDefId = 0x1400_0000u | checked((uint)fieldRow);
        var isInitializedMember = false;
        if (!construction.InitializedMemberDefIds.IsDefaultOrEmpty)
        {
            foreach (var initializedMember
                in construction.InitializedMemberDefIds)
            {
                ConsumeWork(body);
                isInitializedMember |= initializedMember == memberDefId;
            }
        }

        var originsMatch = true;
        foreach (var origin in place.PlaceOrigins)
        {
            ConsumeWork(body);
            originsMatch &= origin.RootHomeIndex == contextHomeIndex
                && origin.PlaceSelectionRecordIndex
                    == selectionRecordIndex;
        }

        if (ReadByte(body, selection, 1) == 2
            && ReadByte(body, contextHome, 0) == 2
            && construction.RoleTag == 0)
        {
            var contextTypeSpecRef = GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, contextHome, 2));
            if (place.AccessKind != 2
                || !originsMatch
                || ReadByte(body, store, 2) != 2
                || ReadByte(body, store, 3) != 1
                || ReadUInt32(body, store, 8) != 0
                || ReadByte(body, store, 12) != 0
                || ReadByte(body, store, 13) != 0
                || ReadUInt16(body, store, 14) != 0
                || ReadByte(body, contextHome, 1) != 0
                || ReadUInt16(body, contextHome, 6) != 0
                || methods.GetOwnerModule(methodDefRow)
                    != types.GetOwnerModule(fieldOwnerType)
                || types.GetKind(fieldOwnerType) != 2
                || (fieldFlags & 0x08) != 0
                || (fieldFlags & 3) != 2
                || (fieldFlags & 0x04) == 0
                || !TryGetNormalClassHandleTypeSpec(contextTypeSpecRef,
                    types, typeSpecs, imports, out var contextClassType,
                    out var contextClassQualifiers)
                || contextClassType != fieldOwnerType
                || (contextClassQualifiers & 0x01) == 0
                || (types.GetFlags(contextClassType) & 0x80) == 0
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    sourceTypeSpecRef, fieldTypeSpecRef)
                || !IsCopyableRegisterValueType(typeSpecs,
                    sourceTypeSpecRef, constantValues, body.Schema)
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    fieldTypeSpecRef,
                    GetTypeMapTypeSpecRef(body,
                        ReadUInt16(body, selection, 16))))
            {
                throw InvalidMap();
            }

            return true;
        }

        if (ReadByte(body, selection, 1) != 3
            || ReadByte(body, selection, 2) != 1
            || ReadByte(body, contextHome, 0) != 1
            || construction.RoleTag != 1
            || place.AccessKind != 3
            || methods.GetOwnerType(methodDefRow) != fieldOwnerType
            || types.GetKind(fieldOwnerType) != 2
            || types.GetOwnerModule(fieldOwnerType)
                != methods.GetOwnerModule(methodDefRow)
            || (fieldFlags & 0x08) != 0
            || !isInitializedMember
            || ReadByte(body, store, 2) != 1
            || ReadByte(body, store, 3) != 1
            || ReadUInt32(body, store, 8) != 0
            || ReadByte(body, store, 12) != 0
            || ReadByte(body, store, 13) != 0
            || ReadUInt16(body, store, 14) != 0
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                sourceTypeSpecRef, fieldTypeSpecRef)
            || !IsConstructionInitializerCopyValueType(typeSpecs,
                sourceTypeSpecRef, types, imports, constantValues,
                body.Schema)
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                fieldTypeSpecRef,
                GetTypeMapTypeSpecRef(body,
                    ReadUInt16(body, selection, 16)))
            || !originsMatch)
        {
            throw InvalidMap();
        }

        for (var register = 0; register < registerTypes.Length; register++)
        {
            var current = ReadRegisterTypeState(body, registerTypes,
                register);
            var matchesSelection = false;
            if (current.IsPlaceHandle)
            {
                foreach (var origin in current.PlaceOrigins)
                {
                    ConsumeWork(body);
                    matchesSelection |= origin.PlaceSelectionRecordIndex
                        == selectionRecordIndex;
                }
            }
            if (matchesSelection)
            {
                SetRegisterTypeState(body, registerTypes, register,
                    RegisterTypeState.Unknown);
            }
        }

        return true;
    }

    private static bool IsConstructionInitializerCopyValueType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef,
        TrRmTypeDefTableReader types, TrRmImportTableReader imports,
        ConstantSourceValueContext constantValues,
        TrRmArtifactSchema schema)
    {
        if (IsCopyableRegisterValueType(typeSpecs, typeSpecRef,
                constantValues, schema))
        {
            return true;
        }

        if (!schema.UsesConstructionInitializerCopyEvidence()
            || !schema.SupportsDeclaredCopyEvidence()
            || !schema.SupportsDeclaredUniqueEvidence())
        {
            return false;
        }

        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (!type.IsRow || type.Form is not (0x02 or 0x03))
        {
            return false;
        }

        var typeDefRow = typeSpecs.GetNominalTypeDefRow(typeSpecRef,
            types, imports);
        if (types.GetKind(typeDefRow) == 1)
        {
            return type.Qualifiers == 0
                && types.HasDeclaredCopy(typeDefRow);
        }

        return types.GetKind(typeDefRow) == 2
            && TryGetNormalClassHandleTypeSpec(typeSpecRef, types,
                typeSpecs, imports, out var classTypeDefRow, out _)
            && classTypeDefRow == typeDefRow
            && !types.HasDeclaredUnique(typeDefRow);
    }

    private static bool ValidateConstructionClassFieldCopyStore(
        TrRmRegisterBodyReader body, RegisterTypeState[] registerTypes,
        int placeRegister, int sourceRegister, int methodDefRow,
        TrRmMethodDefTableReader methods,
        TrRmContractTableReader contracts,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmImportTableReader imports,
        TrRmGenericParamTableReader genericParams,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader
            nativeFunctionPointerAnchors)
    {
        Span<byte> store = stackalloc byte[16];
        store.Clear();
        store[0] = 1;
        store[1] = 1;
        store[2] = 1;
        store[3] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(store[4..],
            checked((ushort)placeRegister));
        BinaryPrimitives.WriteUInt16LittleEndian(store[6..],
            checked((ushort)sourceRegister));
        return ValidateConstructionClassFieldStore(body, store,
            registerTypes, methodDefRow, methods, contracts, types, fields,
            imports, genericParams, typeSpecs, constantValues,
            nativeFunctionPointerAnchors);
    }

    private static void ValidateStandardInitializationFieldSelection(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> selection,
        int destination, byte accessKind, bool allowInitializationFlags,
        int methodDefRow, TrRmMethodDefTableReader methods,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmImportTableReader imports, TrRmTypeSpecTableReader typeSpecs,
        out int baseHomeIndex, out int fieldRow,
        out uint fieldTypeSpecRef, out uint fieldFlags,
        out byte baseHomeFlags)
    {
        baseHomeIndex = -1;
        fieldRow = -1;
        fieldTypeSpecRef = 0;
        fieldFlags = 0;
        baseHomeFlags = 0;
        var flags = ReadUInt16(body, selection, 18);
        var fieldRowValue = ReadUInt32(body, selection, 12);
        var referentTypeMapIndex = ReadUInt16(body, selection, 16);
        if (selection.Length != 20 || destination >= 14
            || ReadByte(body, selection, 0) != 2
            || ReadByte(body, selection, 1) != accessKind
            || ReadByte(body, selection, 2) != 1
            || ReadUInt16(body, selection, 4) != destination
            || ReadByte(body, selection, 3) != 2
            || ReadByte(body, selection, 8) != 0
            || ReadByte(body, selection, 9) != 1
            || ReadUInt16(body, selection, 10) != 0
            || (allowInitializationFlags
                ? flags is not (1 or 3) : flags != 0)
            || fieldRowValue is 0 or > 0x00FF_FFFF
            || fieldRowValue > (uint)fields.Count
            || referentTypeMapIndex >= body.TypeCount)
        {
            throw InvalidMap();
        }

        baseHomeIndex = ReadUInt16(body, selection, 6);
        if (baseHomeIndex >= body.HomeCount)
        {
            throw InvalidMap();
        }

        var baseHome = GetHomeMapEntry(body, baseHomeIndex);
        baseHomeFlags = ReadByte(body, baseHome, 1);
        var baseTypeSpecRef = GetTypeMapTypeSpecRef(body,
            ReadUInt16(body, baseHome, 2));
        var baseType = typeSpecs.GetTypeSpecRef(baseTypeSpecRef);
        fieldRow = checked((int)fieldRowValue);
        fieldTypeSpecRef = fields.GetTypeSpecRef(fieldRow);
        fieldFlags = fields.GetFlags(fieldRow);
        var fieldOwnerTypeDef = fields.GetOwnerTypeDef(fieldRow);
        var fieldVisibility = fieldFlags & 3;
        var fieldOwnerModule = types.GetOwnerModule(fieldOwnerTypeDef);
        var methodOwnerModule = methods.GetOwnerModule(methodDefRow);
        if (ReadByte(body, baseHome, 0) != 3
            || !baseType.IsRow || baseType.Form != 0x02
            || baseType.Qualifiers != 0
            || typeSpecs.GetNominalTypeDefRow(baseTypeSpecRef, types,
                imports) != fieldOwnerTypeDef
            || types.GetKind(fieldOwnerTypeDef) != 1
            || fieldOwnerModule != methodOwnerModule
            || types.GetGenericRange(fieldOwnerTypeDef).Count != 0
            || (fieldFlags & 0x08) != 0
            || (fieldVisibility == 0
                && methods.GetOwnerType(methodDefRow) != fieldOwnerTypeDef)
            || (fieldVisibility == 1
                && fieldOwnerModule != methodOwnerModule)
            || !typeSpecs.AreEquivalentTypeSpecRefs(fieldTypeSpecRef,
                GetTypeMapTypeSpecRef(body, referentTypeMapIndex)))
        {
            throw InvalidMap();
        }
    }

    private static bool ValidateStandardInitializationFieldPathSelection(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> selection,
        int destination, byte accessKind, bool allowInitializationFlags,
        int methodDefRow, int ownerModule,
        TrRmMethodDefTableReader methods, TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields,
        TrRmFieldPathTableReader fieldPaths,
        TrRmLayoutTableReader layouts, TrRmImportTableReader imports,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues,
        uint referentTypeSpecRef, out byte baseHomeFlags)
    {
        baseHomeFlags = 0;
        var targetRef = ReadUInt32(body, selection, 12);
        var referentTypeMapIndex = ReadUInt16(body, selection, 16);
        var flags = ReadUInt16(body, selection, 18);
        if (!body.Schema
                .UsesNestedInitializationReferenceFieldDestinations()
            || selection.Length != 20
            || ReadByte(body, selection, 0) != 2
            || ReadByte(body, selection, 1) != accessKind
            || ReadByte(body, selection, 2) != 1
            || ReadUInt16(body, selection, 4) != destination
            || ReadByte(body, selection, 3) != 2
            || ReadByte(body, selection, 8) != 0
            || ReadByte(body, selection, 9) != 4
            || ReadUInt16(body, selection, 10) != 0
            || targetRef is 0 or > 0x00FF_FFFF
            || targetRef > (uint)fieldPaths.Count
            || (allowInitializationFlags
                ? flags is not (1 or 3) : flags != 0)
            || referentTypeMapIndex >= body.TypeCount
            || !TrRmFeatureManifestValidator.Requires(
                constantValues.Features, constantValues.Modules,
                ownerModule, TrRmFeatureTableReader.LayoutQueriesFeatureId,
                body.Schema))
        {
            throw InvalidMap();
        }

        var baseHomeIndex = ReadUInt16(body, selection, 6);
        if (baseHomeIndex >= body.HomeCount)
        {
            throw InvalidMap();
        }

        var baseHome = GetHomeMapEntry(body, baseHomeIndex);
        baseHomeFlags = ReadByte(body, baseHome, 1);
        var baseTypeSpecRef = GetTypeMapTypeSpecRef(body,
            ReadUInt16(body, baseHome, 2));
        var rootTypeSpecRef
            = fieldPaths.GetRootTypeSpecRef(checked((int)targetRef));
        if (ReadByte(body, baseHome, 0) != 3
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                rootTypeSpecRef, baseTypeSpecRef)
            || !TryGetSupportedInlineOwner(rootTypeSpecRef,
                out var currentOwnerTypeDef))
        {
            throw InvalidMap();
        }

        var fieldCount = fieldPaths.GetFieldCount(
            checked((int)targetRef));
        var fieldWritable = true;
        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            ConsumeWork(body);
            var fieldReference = fieldPaths.GetFieldReference(
                checked((int)targetRef), ordinal);
            var fieldReferenceKind = fieldReference >> 24;
            var fieldReferenceRow
                = checked((int)(fieldReference & 0x00FF_FFFF));
            int fieldRow;
            if (fieldReferenceKind == 0x14)
            {
                fieldRow = fieldReferenceRow;
            }
            else if (fieldReferenceKind == 0x1C)
            {
                var expectedDefinition
                    = imports.GetExpectedDefinitionForImportRow(
                        fieldReferenceRow);
                if (expectedDefinition >> 24 != 0x14)
                {
                    throw InvalidMap();
                }
                fieldRow = checked((int)(
                    expectedDefinition & 0x00FF_FFFF));
            }
            else
            {
                throw InvalidMap();
            }

            var fieldOwnerTypeDef = fields.GetOwnerTypeDef(fieldRow);
            var fieldFlags = fields.GetFlags(fieldRow);
            var fieldVisibility = fieldFlags & 3;
            if (fieldOwnerTypeDef != currentOwnerTypeDef
                || (fieldFlags & 0x08) != 0
                || fieldVisibility > 2
                || (fieldReferenceKind == 0x1C
                    && fieldVisibility != 2)
                || (fieldVisibility == 0
                    && methods.GetOwnerType(methodDefRow)
                        != fieldOwnerTypeDef)
                || (fieldVisibility == 1
                    && types.GetOwnerModule(fieldOwnerTypeDef)
                        != ownerModule))
            {
                throw InvalidMap();
            }

            fieldWritable &= (fieldFlags & 4) != 0;
            var fieldTypeSpecRef = fields.GetTypeSpecRef(fieldRow);
            if (ordinal + 1 == fieldCount)
            {
                if (!typeSpecs.AreEquivalentTypeSpecRefs(
                    fieldTypeSpecRef, referentTypeSpecRef))
                {
                    throw InvalidMap();
                }
                continue;
            }

            if (!TryGetSupportedInlineOwner(fieldTypeSpecRef,
                out currentOwnerTypeDef))
            {
                throw InvalidMap();
            }
        }

        return fieldWritable;

        bool TryGetSupportedInlineOwner(uint typeSpecRef,
            out int typeDefRow)
        {
            ConsumeWork(body, 4);
            var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
            if (!type.IsRow || type.Form is not (0x02 or 0x03)
                || type.Qualifiers != 0)
            {
                typeDefRow = 0;
                return false;
            }

            typeDefRow = typeSpecs.GetNominalTypeDefRow(typeSpecRef,
                types, imports);
            if (types.GetKind(typeDefRow) != 1
                || types.GetGenericRange(typeDefRow).Count != 0
                || !types.HasDeclaredCopy(typeDefRow)
                || !constantValues.TargetProfiles.TryGetProfile(
                    TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                    TrRmTargetProfileTableReader
                        .WindowsX64NativeProfileRuleRevision,
                    out var profileRow))
            {
                return false;
            }

            if (!layouts.HasBlittableLayout(typeDefRow,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision))
            {
                return false;
            }

            var layoutRow = layouts.GetLayoutForProfile(typeDefRow,
                checked((uint)profileRow));
            return layouts.GetTargetProfileRef(layoutRow)
                    == checked((uint)profileRow)
                && layouts.GetLayoutKind(layoutRow) is 1 or 3;
        }
    }

    private static void UpdateInitializationReferenceRegister(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        RegisterTypeState[] registerTypes, int methodDefRow, int ownerModule,
        TrRmMethodDefTableReader methods, TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields,
        TrRmFieldPathTableReader fieldPaths,
        TrRmLayoutTableReader layouts,
        TrRmImportTableReader imports,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues,
        InitializationReferenceTypeMap initializationReferenceTypes)
    {
        var control = ReadByte(body, instruction, 1);
        var destination = control & 0x0F;
        var recordIndex = ReadUInt16(body, instruction, 2);
        if ((control & 0xF0) != 0 || destination >= 14)
        {
            throw InvalidMap();
        }

        var selectionRecord = body.GetRecord(recordIndex);
        if (ReadByte(body, selectionRecord, 0) != 0x04)
        {
            throw InvalidMap();
        }

        var selection = selectionRecord[4..];
        var flags = ReadUInt16(body, selection, 18);
        if (ReadByte(body, selection, 9) == 4
            && (ReadByte(body, selection, 0) != 2 || flags == 0))
        {
            throw InvalidMap();
        }
        if (flags == 0)
        {
            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.Unknown);
            return;
        }

        if (flags is not (1 or 3)
            || ReadByte(body, selection, 1) != 3
            || ReadByte(body, selection, 2) != 1
            || ReadUInt16(body, selection, 4) != destination)
        {
            throw InvalidMap();
        }

        var referentTypeMapIndex = ReadUInt16(body, selection, 16);
        var referentTypeSpecRef = GetTypeMapTypeSpecRef(body,
            referentTypeMapIndex);
        var targetKind = ReadByte(body, selection, 0);
        var fieldWritable = true;
        if (targetKind == 1)
        {
            var homeIndex = ReadUInt32(body, selection, 12);
            if (ReadByte(body, selection, 3) != 0
                || ReadUInt16(body, selection, 6) != 0
                || ReadByte(body, selection, 8) != 0
                || ReadByte(body, selection, 9) != 0
                || ReadUInt16(body, selection, 10) != 0
                || homeIndex >= (uint)body.HomeCount)
            {
                throw InvalidMap();
            }

            var home = GetHomeMapEntry(body, checked((int)homeIndex));
            var homeTypeSpecRef = GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, home, 2));
            if (ReadByte(body, home, 0) != 3
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    referentTypeSpecRef, homeTypeSpecRef))
            {
                throw InvalidMap();
            }

            fieldWritable = (ReadByte(body, home, 1) & 1) != 0;
        }
        else if (targetKind == 2
            && ReadByte(body, selection, 9) == 1)
        {
            ValidateStandardInitializationFieldSelection(body, selection,
                destination, 3, allowInitializationFlags: true,
                methodDefRow, methods, types, fields, imports, typeSpecs,
                out _, out _, out _, out var fieldFlags,
                out var baseHomeFlags);
            fieldWritable = (baseHomeFlags & 1) != 0
                && (fieldFlags & 4) != 0;
        }
        else if (targetKind == 2
            && ReadByte(body, selection, 9) == 4)
        {
            var pathWritable
                = ValidateStandardInitializationFieldPathSelection(body,
                    selection, destination, 3, true, methodDefRow, ownerModule,
                    methods, types, fields, fieldPaths, layouts, imports,
                    typeSpecs, constantValues, referentTypeSpecRef,
                    out var baseHomeFlags);
            fieldWritable = (baseHomeFlags & 1) != 0 && pathWritable;
        }
        else
        {
            throw InvalidMap();
        }

        if ((flags == 3 && !fieldWritable)
            || !initializationReferenceTypes.TryGetTypeSpecRef(body,
                typeSpecs, referentTypeSpecRef, flags == 3,
                out var initializationReferenceTypeSpecRef))
        {
            throw InvalidMap();
        }

        SetRegisterTypeState(body, registerTypes, destination,
            RegisterTypeState.FromTypeSpecRef(
                initializationReferenceTypeSpecRef));
    }

    private static void UpdateFixedArrayPlaceLoadRegister(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        RegisterTypeState[] registerTypes, int methodDefRow,
        TrRmMethodDefTableReader methods,
        TrRmContractTableReader contracts,
        TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields,
        TrRmFieldPathTableReader fieldPaths,
        TrRmLayoutTableReader layouts, TrRmImportTableReader imports,
        int ownerModule,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues, int codeOffset,
        bool allowIncompletePlaceFlow)
    {
        var operand = ReadByte(body, instruction, 1);
        var destination = operand & 0x0F;
        var selectionIndex = ReadUInt16(body, instruction, 2);
        var selectionRecord = body.GetRecord(selectionIndex);
        if (selectionRecord[0] != 0x04)
        {
            throw InvalidMap();
        }

        var selection = selectionRecord[4..];
        if (ReadByte(body, selection, 9) == 4)
        {
            var referentTypeSpecRef = GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, selection, 16));
            _ = ValidateStandardInitializationFieldPathSelection(body,
                selection, destination, 1, false, methodDefRow, ownerModule,
                methods, types, fields, fieldPaths, layouts, imports,
                typeSpecs, constantValues, referentTypeSpecRef, out _);
            var baseHomeIndex = ReadUInt16(body, selection, 6);
            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromPlace(referentTypeSpecRef,
                    selectionIndex, 1, [
                        new PlaceHandleOrigin(selectionIndex,
                            baseHomeIndex, codeOffset),
                    ]));
        }
        else
        {
            Span<byte> placeInstruction = stackalloc byte[4];
            instruction.CopyTo(placeInstruction);
            placeInstruction[0] = 0xD8;
            UpdateFixedArrayPlaceRegister(body, placeInstruction,
                registerTypes, methodDefRow, methods, contracts, types,
                fields, imports,
                ownerModule, typeSpecs, constantValues, codeOffset,
                allowIncompletePlaceFlow);
        }
        var place = GetRegisterTypeState(body, registerTypes, destination);
        var matchesSelection = false;
        foreach (var origin in place.PlaceOrigins)
        {
            ConsumeWork(body);
            matchesSelection |= origin.PlaceSelectionRecordIndex
                == selectionIndex;
        }
        if ((operand & 0xF0) != 0 || destination >= 14
            || !place.IsPlaceHandle
            || place.AccessKind != 1
            || place.PlaceOrigins.Length == 0
            || !matchesSelection)
        {
            throw InvalidMap();
        }

        var typeMapIndex = ReadUInt16(body, selection, 16);
        var selectedTypeSpecRef = GetTypeMapTypeSpecRef(body, typeMapIndex);
        var placeTypeSpecRef = GetRegisterTypeSpecRef(body, place);
        if (ReadByte(body, selection, 0) is not (2 or 3)
            || ReadByte(body, selection, 1) != 1
            || ReadByte(body, selection, 2) != 1
            || ReadUInt16(body, selection, 4) != destination
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                selectedTypeSpecRef, placeTypeSpecRef)
            || !IsCopyableRegisterValueType(typeSpecs,
                selectedTypeSpecRef, constantValues, body.Schema))
        {
            throw InvalidMap();
        }

        SetRegisterTypeState(body, registerTypes, destination,
            RegisterTypeState.FromTypeMap(typeMapIndex));
    }

    private static void UpdateFixedArrayPlaceRegister(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        RegisterTypeState[] registerTypes, int methodDefRow,
        TrRmMethodDefTableReader methods,
        TrRmContractTableReader contracts,
        TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields, TrRmImportTableReader imports,
        int ownerModule,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues, int codeOffset,
        bool allowIncompletePlaceFlow)
    {
        var opcode = ReadByte(body, instruction, 0);
        var instructionOperand = ReadByte(body, instruction, 1);
        var destination = instructionOperand & 0x0F;
        var recordIndex = opcode == 0xD8
            ? ReadUInt16(body, instruction, 2)
            : instructionOperand >> 4;
        var record = body.GetRecord(recordIndex);
        ConsumeWork(body, checked((ulong)(record.Length - 4)));
        if (ReadByte(body, record, 0) != 0x04)
        {
            throw InvalidMap();
        }

        var payload = record[4..];
        if (ReadByte(body, payload, 9) != 3)
        {
            if (body.Schema.UsesObjectConstruction()
                && ReadByte(body, payload, 0) == 2
                && ReadByte(body, payload, 9) == 1)
            {
                UpdateConstructionClassFieldPlaceRegister(body, payload,
                    registerTypes, methodDefRow, methods, contracts, types,
                    fields, imports, ownerModule, typeSpecs, recordIndex,
                    destination, codeOffset);
                return;
            }

            if (body.Schema.UsesForeignInvocationStorageFixedArrays()
                && ReadByte(body, payload, 0) == 2
                && ReadByte(body, payload, 9) == 1)
            {
                UpdateFixedArrayFieldPlaceRegister(body, payload,
                    registerTypes, methodDefRow, methods, types, fields,
                    imports, ownerModule, typeSpecs, recordIndex,
                    destination, codeOffset, allowIncompletePlaceFlow);
                return;
            }

            if ((body.Schema is TrRmArtifactSchema.Standard
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutDraft
                    or TrRmArtifactSchema.InitializationReferenceLocalFieldLayoutNestedFieldDestinationsDraft)
                && ReadByte(body, payload, 0) == 2
                && ReadByte(body, payload, 1) == 1
                && ReadByte(body, payload, 9) == 1
                && TrRmFeatureManifestValidator.Requires(
                    constantValues.Features, constantValues.Modules,
                    ownerModule,
                    TrRmFeatureTableReader.NativeBoundaryContractsFeatureId,
                    body.Schema))
            {
                UpdateStandardInitializationFieldPlaceRegister(body,
                    payload, registerTypes, methodDefRow, methods, types,
                    fields, imports, ownerModule, typeSpecs, recordIndex,
                    destination, codeOffset);
                return;
            }

            if (body.Schema
                    .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
                && ReadByte(body, payload, 0) == 1)
            {
                UpdateEmptyArrayHomePlaceRegister(body, payload,
                    registerTypes, ownerModule, typeSpecs, constantValues,
                    recordIndex, destination, codeOffset);
                return;
            }

            ClearRegisterTypeStates(body, registerTypes);
            return;
        }

        var layoutRow = ReadUInt32(body, payload, 12);
        var arrayLayout = constantValues.FixedArrayLayouts.GetRow(
            checked((int)layoutRow));
        if (arrayLayout.OwnerModuleRow != ownerModule
            || ReadByte(body, payload, 2) != 1
            || ReadUInt16(body, payload, 4) != destination
            || !typeSpecs.TryGetFixedInlineArray(
                (uint)arrayLayout.ArrayTypeSpecRow,
                out var elementTypeSpecRef, out _))
        {
            throw InvalidMap();
        }

        var indexKind = ReadByte(body, payload, 8);
        var index = ReadUInt16(body, payload, 10);
        var indexIsValid = indexKind switch
        {
            1 => IsValueRegisterIndex(body, registerTypes,
                index, typeSpecs),
            2 => IsHomeIndex(body, index, typeSpecs),
            _ => false,
        };
        if (!indexIsValid)
        {
            throw InvalidMap();
        }

        var placeOrigins = Array.Empty<PlaceHandleOrigin>();
        bool parentAllowsWrite;
        var baseKind = ReadByte(body, payload, 3);
        if (baseKind == 2)
        {
            var homeIndex = ReadUInt16(body, payload, 6);
            var home = GetHomeMapEntry(body, homeIndex);
            if (ReadByte(body, home, 0) != 3
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    GetTypeMapTypeSpecRef(body, ReadUInt16(body, home, 2)),
                    (uint)arrayLayout.ArrayTypeSpecRow))
            {
                throw InvalidMap();
            }

            ConsumeWork(body, 2);
            placeOrigins = [
                new PlaceHandleOrigin(recordIndex, homeIndex, codeOffset),
            ];
            parentAllowsWrite = (ReadByte(body, home, 1) & 1) != 0;
        }
        else if (baseKind == 1)
        {
            var baseRegister = ReadUInt16(body, payload, 6);
            var baseState = GetRegisterTypeState(
                body, registerTypes, baseRegister);
            if (!baseState.IsPlaceHandle
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    GetRegisterTypeSpecRef(body, baseState),
                    (uint)arrayLayout.ArrayTypeSpecRow))
            {
                throw InvalidMap();
            }

            if (baseState.PlaceOrigins.Length == 0)
            {
                if (!allowIncompletePlaceFlow)
                {
                    throw InvalidMap();
                }
            }
            else
            {
                ConsumeWork(body,
                    checked((ulong)baseState.PlaceOrigins.Length + 1UL));
                var origins = new PlaceHandleOrigin[
                    baseState.PlaceOrigins.Length];
                for (var originIndex = 0;
                    originIndex < baseState.PlaceOrigins.Length;
                    originIndex++)
                {
                    ConsumeWork(body);
                    var origin = baseState.PlaceOrigins[originIndex];
                    if (origin.RootHomeIndex < 0
                        || origin.RootHomeIndex >= body.HomeCount)
                    {
                        throw InvalidMap();
                    }

                    origins[originIndex] = new PlaceHandleOrigin(recordIndex,
                        origin.RootHomeIndex, codeOffset);
                }

                placeOrigins = origins;
            }
            parentAllowsWrite = AllowsWriteAuthority(
                baseState.AccessKind,
                GetRegisterTypeSpecRef(body, baseState), typeSpecs);
        }
        else
        {
            throw InvalidMap();
        }

        var elementType = typeSpecs.GetTypeSpecRef(elementTypeSpecRef);
        var accessKind = ReadByte(body, payload, 1);
        var requiresWrite = accessKind is 2 or 3 or 4
            || (accessKind == 5 && (elementType.Qualifiers & 1) != 0);
        var initializesHomeArray = baseKind == 2 && accessKind == 3;
        if (requiresWrite && !parentAllowsWrite && !initializesHomeArray)
        {
            throw InvalidMap();
        }

        ConsumeWork(body, checked((ulong)placeOrigins.Length + 1UL));
        SetRegisterTypeState(body, registerTypes, destination,
            RegisterTypeState.FromPlace(elementTypeSpecRef, recordIndex,
                accessKind, placeOrigins));
    }

    private static void UpdateStandardInitializationFieldPlaceRegister(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        RegisterTypeState[] registerTypes, int methodDefRow,
        TrRmMethodDefTableReader methods, TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields, TrRmImportTableReader imports,
        int ownerModule, TrRmTypeSpecTableReader typeSpecs,
        int recordIndex, int destination, int codeOffset)
    {
        ValidateStandardInitializationFieldSelection(body, payload,
            destination, 1, allowInitializationFlags: false, methodDefRow,
            methods, types, fields, imports, typeSpecs,
            out var baseHomeIndex, out _, out var fieldTypeSpecRef,
            out _, out _);
        SetRegisterTypeState(body, registerTypes, destination,
            RegisterTypeState.FromPlace(fieldTypeSpecRef, recordIndex, 1,
                [new PlaceHandleOrigin(recordIndex, baseHomeIndex,
                    codeOffset)]));
    }

    private static void UpdateFixedArrayFieldPlaceRegister(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        RegisterTypeState[] registerTypes, int methodDefRow,
        TrRmMethodDefTableReader methods, TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields, TrRmImportTableReader imports,
        int ownerModule, TrRmTypeSpecTableReader typeSpecs,
        int recordIndex, int destination, int codeOffset,
        bool allowIncompletePlaceFlow)
    {
        ValidateFixedArrayFieldPlaceSelection(body, payload, methodDefRow,
            ownerModule, methods, types, fields, imports, typeSpecs);
        if (ReadUInt16(body, payload, 4) != destination)
        {
            throw InvalidMap();
        }

        var fieldRow = checked((int)ReadUInt32(body, payload, 12));
        var fieldTypeSpecRef = fields.GetTypeSpecRef(fieldRow);
        var fieldOwnerTypeDef = fields.GetOwnerTypeDef(fieldRow);
        var baseKind = ReadByte(body, payload, 3);
        var baseIndex = ReadUInt16(body, payload, 6);
        uint baseTypeSpecRef;
        bool parentAllowsWrite;
        PlaceHandleOrigin[] placeOrigins;
        if (baseKind == 2)
        {
            var home = GetHomeMapEntry(body, baseIndex);
            if (ReadByte(body, home, 0) is not (2 or 3))
            {
                throw InvalidMap();
            }

            baseTypeSpecRef = GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, home, 2));
            placeOrigins = [
                new PlaceHandleOrigin(recordIndex, baseIndex, codeOffset),
            ];
            parentAllowsWrite = (ReadByte(body, home, 1) & 1) != 0;
        }
        else
        {
            var baseState = GetRegisterTypeState(body, registerTypes,
                baseIndex);
            if (!baseState.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            baseTypeSpecRef = GetRegisterTypeSpecRef(body, baseState);
            if (baseState.PlaceOrigins.Length == 0)
            {
                if (!allowIncompletePlaceFlow)
                {
                    throw InvalidMap();
                }

                placeOrigins = [];
            }
            else
            {
                ConsumeWork(body,
                    checked((ulong)baseState.PlaceOrigins.Length + 1UL));
                placeOrigins = new PlaceHandleOrigin[
                    baseState.PlaceOrigins.Length];
                for (var index = 0; index < baseState.PlaceOrigins.Length;
                    index++)
                {
                    ConsumeWork(body);
                    var origin = baseState.PlaceOrigins[index];
                    if (origin.RootHomeIndex < 0
                        || origin.RootHomeIndex >= body.HomeCount)
                    {
                        throw InvalidMap();
                    }

                    placeOrigins[index] = new PlaceHandleOrigin(recordIndex,
                        origin.RootHomeIndex, codeOffset);
                }
            }

            parentAllowsWrite = AllowsWriteAuthority(baseState.AccessKind,
                baseTypeSpecRef, typeSpecs);
        }

        var baseType = typeSpecs.GetTypeSpecRef(baseTypeSpecRef);
        if (!baseType.IsRow || baseType.Form is not (0x02 or 0x03)
            || typeSpecs.GetNominalTypeDefRow(baseTypeSpecRef, types,
                imports) != fieldOwnerTypeDef)
        {
            throw InvalidMap();
        }

        var accessKind = ReadByte(body, payload, 1);
        var fieldFlags = fields.GetFlags(fieldRow);
        var fieldType = typeSpecs.GetTypeSpecRef(fieldTypeSpecRef);
        var requiresWrite = accessKind is 2 or 3 or 4
            || (accessKind == 5 && (fieldType.Qualifiers & 1) != 0);
        if (requiresWrite
            && (!parentAllowsWrite || (fieldFlags & 4) == 0))
        {
            throw InvalidMap();
        }

        ConsumeWork(body, checked((ulong)placeOrigins.Length + 1UL));
        SetRegisterTypeState(body, registerTypes, destination,
            RegisterTypeState.FromPlace(fieldTypeSpecRef, recordIndex,
                accessKind, placeOrigins));
    }

    private static void UpdateConstructionClassFieldPlaceRegister(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        RegisterTypeState[] registerTypes, int methodDefRow,
        TrRmMethodDefTableReader methods,
        TrRmContractTableReader contracts,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmImportTableReader imports,
        int ownerModule, TrRmTypeSpecTableReader typeSpecs, int recordIndex,
        int destination, int codeOffset)
    {
        var accessKind = ReadByte(body, payload, 1);
        var destinationKind = ReadByte(body, payload, 2);
        var baseKind = ReadByte(body, payload, 3);
        var destinationIndex = ReadUInt16(body, payload, 4);
        var contextHomeIndex = ReadUInt16(body, payload, 6);
        var indexKind = ReadByte(body, payload, 8);
        var targetRefKind = ReadByte(body, payload, 9);
        var indexIndex = ReadUInt16(body, payload, 10);
        var fieldRow = ReadUInt32(body, payload, 12);
        var referentTypeMapIndex = ReadUInt16(body, payload, 16);
        var construction = contracts.HasConstructionClause(methodDefRow)
            ? contracts.GetConstructionClause(methodDefRow) : default;
        var isPropertyInitializedState
            = body.Schema.UsesPropertyInitializedState();
        var isConstructionInitializer
            = body.Schema.UsesConstructionContracts()
                && construction.RoleTag == 1;
        if ((!body.Schema.UsesObjectConstruction()
                && !body.Schema.UsesPropertyInitializedState()
                && !isConstructionInitializer)
            || payload.Length != 20 || accessKind is not (1 or 2 or 3)
            || destinationKind != 1 || destinationIndex != destination
            || destination >= 14 || baseKind is not (1 or 2)
            || (baseKind == 1 && accessKind is not (1 or 2))
            || (baseKind == 1 ? contextHomeIndex >= 14
                : contextHomeIndex >= body.HomeCount)
            || indexKind != 0
            || targetRefKind != 1 || indexIndex != 0
            || fieldRow is 0 or > 0x00FF_FFFF
            || fieldRow > (uint)fields.Count
            || referentTypeMapIndex >= body.TypeCount
            || ReadUInt16(body, payload, 18) != 0)
        {
            throw InvalidMap();
        }

        var fieldDefRow = checked((int)fieldRow);
        var fieldOwnerType = fields.GetOwnerTypeDef(fieldDefRow);
        var fieldFlags = fields.GetFlags(fieldDefRow);
        var fieldTypeSpecRef = fields.GetTypeSpecRef(fieldDefRow);
        var contextTypeSpecRef = GetTypeMapTypeSpecRef(body,
            baseKind == 2
                ? ReadUInt16(body, GetHomeMapEntry(body,
                    contextHomeIndex), 2)
                : referentTypeMapIndex);
        if (!typeSpecs.AreEquivalentTypeSpecRefs(fieldTypeSpecRef,
                GetTypeMapTypeSpecRef(body, referentTypeMapIndex)))
        {
            throw InvalidMap();
        }

        if (baseKind == 1)
        {
            var baseState = GetRegisterTypeState(body, registerTypes,
                contextHomeIndex);
            var baseTypeSpecRef = GetRegisterTypeSpecRef(body, baseState);
            var baseType = typeSpecs.GetTypeSpecRef(baseTypeSpecRef);
            if (construction.RoleTag != 0 || accessKind is not (1 or 2)
                || !baseState.IsPlaceHandle
                || baseState.AccessKind != accessKind
                || baseState.PlaceOrigins.IsDefaultOrEmpty
                || !baseType.IsRow || baseType.Form != 0x02
                || baseType.Qualifiers != 0
                || types.GetKind(fieldOwnerType) != 1
                || types.GetOwnerModule(fieldOwnerType) != ownerModule
                || typeSpecs.GetNominalTypeDefRow(baseTypeSpecRef,
                    types, imports) != fieldOwnerType
                || (fieldFlags & 0x08) != 0
                || (accessKind == 2 && (fieldFlags & 0x04) == 0)
                || (fieldFlags & 3) > 2
                || ((fieldFlags & 3) == 0
                    && methods.GetOwnerType(methodDefRow)
                        != fieldOwnerType)
                || ((fieldFlags & 3) == 1
                    && types.GetOwnerModule(fieldOwnerType)
                        != ownerModule))
            {
                throw InvalidMap();
            }

            var placeOrigins
                = ImmutableArray.CreateBuilder<PlaceHandleOrigin>(
                    baseState.PlaceOrigins.Length);
            foreach (var origin in baseState.PlaceOrigins)
            {
                ConsumeWork(body);
                if ((uint)origin.RootHomeIndex
                    >= (uint)body.HomeCount)
                {
                    throw InvalidMap();
                }

                placeOrigins.Add(new PlaceHandleOrigin(recordIndex,
                    origin.RootHomeIndex, codeOffset));
            }

            ConsumeWork(body,
                checked((ulong)placeOrigins.Count + 1UL));
            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromPlace(fieldTypeSpecRef, recordIndex,
                    accessKind, placeOrigins.MoveToImmutable()));
            return;
        }

        var contextHome = GetHomeMapEntry(body, contextHomeIndex);
        var contextRole = ReadByte(body, contextHome, 0);
        var ownerType = methods.GetOwnerType(methodDefRow);
        var contextTypeSpec = typeSpecs.GetTypeSpecBytes(
            contextTypeSpecRef);
        if (contextRole == 2 && construction.RoleTag == 0
            && accessKind is 1 or 2)
        {
            if (ReadByte(body, contextHome, 1) != 0
                || ReadUInt16(body, contextHome, 6) != 0
                || methods.GetOwnerModule(methodDefRow) != ownerModule
                || types.GetKind(fieldOwnerType) != 2
                || types.GetOwnerModule(fieldOwnerType) != ownerModule
                || (fieldFlags & 0x08) != 0
                || (fieldFlags & 3) != 2
                || !TryGetNormalClassHandleTypeSpec(contextTypeSpecRef,
                    types, typeSpecs, imports, out var contextClassType,
                    out var contextClassQualifiers)
                || contextClassType != fieldOwnerType
                || (accessKind == 2
                    && ((contextClassQualifiers & 0x01) == 0
                        || (types.GetFlags(contextClassType) & 0x80) == 0
                        || (fieldFlags & 0x04) == 0)))
            {
                throw InvalidMap();
            }

            ConsumeWork(body);
            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromPlace(fieldTypeSpecRef, recordIndex,
                    accessKind, [
                        new PlaceHandleOrigin(recordIndex,
                            contextHomeIndex, codeOffset),
                    ]));
            return;
        }

        if (contextRole == 1 && construction.RoleTag == 0
            && accessKind is 1 or 2)
        {
            var contextReceiverType
                = typeSpecs.GetTypeSpecRef(contextTypeSpecRef);
            var isWrite = accessKind == 2;
            var expectedReceiverQualifiers = isWrite ? (byte)0x01 : (byte)0;
            if (ReadByte(body, contextHome, 1) != 0
                || ReadUInt16(body, contextHome, 4) != 0
                || ReadUInt16(body, contextHome, 6) != 0
                || (((methods.GetFlags(methodDefRow) & 4) != 0)
                    != isWrite)
                || methods.GetOwnerModule(methodDefRow) != ownerModule
                || types.GetKind(ownerType) != 2
                || ownerType != fieldOwnerType
                || types.GetOwnerModule(fieldOwnerType) != ownerModule
                || (fieldFlags & 0x08) != 0
                || (isWrite && (fieldFlags & 0x04) == 0)
                || (fieldFlags & 3) > 2
                || ((fieldFlags & 3) == 0
                    && ownerType != fieldOwnerType)
                || ((fieldFlags & 3) == 1
                    && types.GetOwnerModule(fieldOwnerType)
                        != ownerModule)
                || !TryGetNormalClassHandleTypeSpec(contextTypeSpecRef,
                    types, typeSpecs, imports, out var contextClassType,
                    out var contextQualifiers)
                || contextClassType != ownerType
                || !contextReceiverType.IsRow
                || contextReceiverType.Form != 0x02
                || contextQualifiers != expectedReceiverQualifiers)
            {
                throw InvalidMap();
            }

            ConsumeWork(body);
            SetRegisterTypeState(body, registerTypes, destination,
                RegisterTypeState.FromPlace(fieldTypeSpecRef, recordIndex,
                    accessKind, [
                        new PlaceHandleOrigin(recordIndex,
                            contextHomeIndex, codeOffset),
                    ]));
            return;
        }

        if (contextRole is not (1 or 4)
            || accessKind == 2
            || (contextRole == 4
                && (!body.Schema.UsesObjectConstruction()
                    || (!isPropertyInitializedState
                        && construction.RoleTag != 2)))
            || (accessKind == 3
                && (contextRole != 1
                    && !(contextRole == 4 && isPropertyInitializedState)))
            || ReadByte(body, contextHome, 1) != 0
            || ReadUInt16(body, contextHome, 4) != 0
            || ReadUInt16(body, contextHome, 6) != 0
            || types.GetKind(fieldOwnerType) != 2
            || types.GetOwnerModule(fieldOwnerType) != ownerModule
            || (fieldFlags & 0x08) != 0
            || (fieldFlags & 3) > 2
            || ((fieldFlags & 3) == 1
                && types.GetOwnerModule(fieldOwnerType) != ownerModule)
            || (contextRole == 4
                && ((fieldFlags & 0x10) != 0
                    || ((fieldFlags & 3) == 0
                        && ownerType != fieldOwnerType)))
            || contextTypeSpec.Length != 7
            || contextTypeSpec[0] != 0x0C
            || contextTypeSpec[2] != 0x8A)
        {
            throw InvalidMap();
        }

        var classTypeRef = ReadUInt32(body, contextTypeSpec, 3);
        var classType = typeSpecs.GetTypeSpecRef(classTypeRef);
        if (!classType.IsRow || classType.Form != 0x02
            || classType.Qualifiers != 0
            || typeSpecs.GetNominalTypeDefRow(classTypeRef, types, imports)
                != fieldOwnerType
            || (contextRole == 1
                && (ownerType != fieldOwnerType
                    || construction.RoleTag is not (1 or 3))))
        {
            throw InvalidMap();
        }

        if (contextRole == 1 && construction.RoleTag == 3)
        {
            if (!isPropertyInitializedState
                || !fields.IsPropertyBackingField(fieldDefRow)
                || contracts.GetConstructionPropertyBackingFieldDef(
                    methodDefRow) != fieldDefRow)
            {
                throw InvalidMap();
            }
        }
        else if (contextRole == 1 && construction.RoleTag == 1
            && accessKind == 3)
        {
            if (isPropertyInitializedState
                && fields.IsPropertyBackingField(fieldDefRow))
            {
                _ = contracts.GetPropertyDefForBackingField(fieldDefRow);
            }
            else
            {
                var initializedFieldId = 0x1400_0000u
                    | checked((uint)fieldDefRow);
                var isInitializedMember = false;
                foreach (var memberDefId
                    in construction.InitializedMemberDefIds)
                {
                    ConsumeWork(body);
                    isInitializedMember |= memberDefId == initializedFieldId;
                }
                if (!isInitializedMember)
                {
                    throw InvalidMap();
                }
            }
        }
        else if (contextRole == 1 && construction.RoleTag != 1
            || contextRole == 4 && !isPropertyInitializedState)
        {
            var initializerMethodDefRow
                = checked((int)construction.InitializerMethodDefRow);
            var initializerConstruction
                = contracts.GetConstructionClause(
                    initializerMethodDefRow);
            var initializedFieldId = 0x1400_0000u
                | checked((uint)fieldDefRow);
            var isInitializedMember = false;
            if (initializerConstruction.RoleTag != 1)
            {
                throw InvalidMap();
            }

            foreach (var memberDefId
                in initializerConstruction.InitializedMemberDefIds)
            {
                ConsumeWork(body);
                isInitializedMember |= memberDefId == initializedFieldId;
            }
            if (!isInitializedMember)
            {
                throw InvalidMap();
            }
        }

        ConsumeWork(body);
        SetRegisterTypeState(body, registerTypes, destination,
            RegisterTypeState.FromPlace(fieldTypeSpecRef, recordIndex,
                accessKind,
                [new PlaceHandleOrigin(recordIndex, contextHomeIndex,
                    codeOffset)]));
    }

    private static void ValidateFixedArrayFieldPlaceSelection(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        int methodDefRow, int ownerModule,
        TrRmMethodDefTableReader methods, TrRmTypeDefTableReader types,
        TrRmFieldDefTableReader fields, TrRmImportTableReader imports,
        TrRmTypeSpecTableReader typeSpecs)
    {
        var accessKind = ReadByte(body, payload, 1);
        var destinationKind = ReadByte(body, payload, 2);
        var baseKind = ReadByte(body, payload, 3);
        var destination = ReadUInt16(body, payload, 4);
        var baseIndex = ReadUInt16(body, payload, 6);
        var indexKind = ReadByte(body, payload, 8);
        var targetRefKind = ReadByte(body, payload, 9);
        var indexIndex = ReadUInt16(body, payload, 10);
        var fieldRow = ReadUInt32(body, payload, 12);
        var referentTypeMapIndex = ReadUInt16(body, payload, 16);
        if (!body.Schema.UsesForeignInvocationStorageFixedArrays()
            || accessKind is < 1 or > 5
            || destinationKind != 1 || destination >= 14
            || baseKind is not (1 or 2)
            || (baseKind == 1 ? baseIndex > 13
                : baseIndex >= body.HomeCount)
            || indexKind != 0 || targetRefKind != 1 || indexIndex != 0
            || fieldRow is 0 or > 0xFF_FFFF
            || fieldRow > (uint)fields.Count
            || referentTypeMapIndex >= body.TypeCount
            || ReadUInt16(body, payload, 18) != 0)
        {
            throw InvalidMap();
        }

        var fieldDefRow = checked((int)fieldRow);
        var fieldOwnerTypeDef = fields.GetOwnerTypeDef(fieldDefRow);
        if (baseKind == 2)
        {
            var home = GetHomeMapEntry(body, baseIndex);
            var homeTypeSpecRef = GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, home, 2));
            var homeType = typeSpecs.GetTypeSpecRef(homeTypeSpecRef);
            if (ReadByte(body, home, 0) is not (2 or 3)
                || !homeType.IsRow
                || homeType.Form is not (0x02 or 0x03)
                || typeSpecs.GetNominalTypeDefRow(homeTypeSpecRef, types,
                    imports) != fieldOwnerTypeDef)
            {
                throw InvalidMap();
            }
        }

        var fieldOwnerModule = types.GetOwnerModule(fieldOwnerTypeDef);
        var fieldFlags = fields.GetFlags(fieldDefRow);
        var methodOwnerType = methods.GetOwnerType(methodDefRow);
        var fieldVisibility = fieldFlags & 3;
        if (types.GetKind(fieldOwnerTypeDef) != 1
            || !types.HasDeclaredCopy(fieldOwnerTypeDef)
            || (fieldFlags & 8) != 0
            || (fieldVisibility == 0
                && methodOwnerType != fieldOwnerTypeDef)
            || (fieldVisibility == 1 && fieldOwnerModule != ownerModule)
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                fields.GetTypeSpecRef(fieldDefRow),
                GetTypeMapTypeSpecRef(body, referentTypeMapIndex)))
        {
            throw InvalidMap();
        }
    }

    private static void UpdateEmptyArrayHomePlaceRegister(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        RegisterTypeState[] registerTypes, int ownerModule,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues, int recordIndex,
        int destination, int codeOffset)
    {
        var targetRef = ReadUInt32(body, payload, 12);
        var referentTypeMapIndex = ReadUInt16(body, payload, 16);
        if (ReadByte(body, payload, 1) != 3
            || ReadByte(body, payload, 2) != 1
            || ReadByte(body, payload, 3) != 0
            || ReadUInt16(body, payload, 4) != destination
            || ReadUInt16(body, payload, 6) != 0
            || ReadByte(body, payload, 8) != 0
            || ReadByte(body, payload, 9) != 0
            || ReadUInt16(body, payload, 10) != 0
            || ReadUInt16(body, payload, 18) != 0
            || targetRef >= (uint)body.HomeCount)
        {
            throw InvalidMap();
        }

        var homeIndex = checked((int)targetRef);
        var home = GetHomeMapEntry(body, homeIndex);
        var typeSpecRef = GetTypeMapTypeSpecRef(body,
            ReadUInt16(body, home, 2));
        var referentTypeSpecRef = GetTypeMapTypeSpecRef(body,
            referentTypeMapIndex);
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (ReadByte(body, home, 0) != 3
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                typeSpecRef, referentTypeSpecRef)
            || !type.IsRow || type.Form != 0x11
            || !typeSpecs.TryGetFixedInlineArray(typeSpecRef, out _, out var count)
            || (!body.Schema
                    .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
                && count != 0)
            || !TrRmFeatureManifestValidator.Requires(
                constantValues.Features, constantValues.Modules,
                ownerModule,
                TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
                body.Schema)
            || !constantValues.TargetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            throw InvalidMap();
        }

        var layoutRow = constantValues.FixedArrayLayouts.FindRow(ownerModule,
            (uint)type.Row, (uint)profileRow);
        if (layoutRow == 0)
        {
            throw InvalidMap();
        }

        var layout = constantValues.FixedArrayLayouts.GetRow(layoutRow);
        if (layout.OwnerModuleRow != ownerModule
            || layout.ArrayTypeSpecRow != type.Row
            || (count == 0 && layout.ByteExtent != 0)
            || layout.Alignment == 0)
        {
            throw InvalidMap();
        }

        SetRegisterTypeState(body, registerTypes, destination,
            RegisterTypeState.FromPlace(referentTypeSpecRef, recordIndex, 3,
                [new PlaceHandleOrigin(recordIndex, homeIndex, codeOffset)]));
    }

    private static void ValidateEmptyArrayInitializationStore(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        RegisterTypeState[] registerTypes, int ownerModule,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues)
    {
        var placeIndex = ReadUInt16(body, payload, 4);
        if (!body.Schema
                .UsesForeignInvocationStorageFixedArrayEmptyInitialization()
            || payload.Length != 16 || ReadByte(body, payload, 0) != 1
            || ReadByte(body, payload, 1) != 0
            || ReadByte(body, payload, 2) != 1
            || ReadByte(body, payload, 3) != 0
            || placeIndex > 13
            || ReadUInt16(body, payload, 6) != 0
            || ReadUInt32(body, payload, 8) != 0
            || ReadByte(body, payload, 12) != 0
            || ReadByte(body, payload, 13) != 0
            || ReadUInt16(body, payload, 14) != 0)
        {
            throw InvalidMap();
        }

        var place = GetRegisterTypeState(body, registerTypes, placeIndex);
        if (!place.IsPlaceHandle || place.AccessKind != 3
            || place.PlaceOrigins.Length == 0)
        {
            throw InvalidMap();
        }

        var emptyArrayTypeSpecRef = GetRegisterTypeSpecRef(body, place);
        var emptyArrayType = typeSpecs.GetTypeSpecRef(
            emptyArrayTypeSpecRef);
        if (!emptyArrayType.IsRow || emptyArrayType.Form != 0x11
            || !typeSpecs.TryGetFixedInlineArray(
                emptyArrayTypeSpecRef, out _, out var elementCount)
            || elementCount != 0
            || !TrRmFeatureManifestValidator.Requires(
                constantValues.Features, constantValues.Modules,
                ownerModule,
                TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
                body.Schema)
            || !constantValues.TargetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            throw InvalidMap();
        }

        var emptyArrayLayoutRow = constantValues.FixedArrayLayouts.FindRow(
            ownerModule, (uint)emptyArrayType.Row, (uint)profileRow);
        if (emptyArrayLayoutRow == 0
            || constantValues.FixedArrayLayouts.GetRow(
                emptyArrayLayoutRow).ByteExtent != 0)
        {
            throw InvalidMap();
        }

        foreach (var origin in place.PlaceOrigins)
        {
            ConsumeWork(body);
            if (origin.RootHomeIndex < 0
                || origin.RootHomeIndex >= body.HomeCount
                || origin.PlaceSelectionRecordIndex < 0)
            {
                throw InvalidMap();
            }

            var rootHome = GetHomeMapEntry(body, origin.RootHomeIndex);
            var selection = GetRecordPayload(body,
                origin.PlaceSelectionRecordIndex);
            if (ReadByte(body, rootHome, 0) != 3
                || ReadByte(body, selection, 1) != 3
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    GetTypeMapTypeSpecRef(body,
                        ReadUInt16(body, selection, 16)),
                    emptyArrayTypeSpecRef))
            {
                throw InvalidMap();
            }

            var targetKind = ReadByte(body, selection, 0);
            var targetRefKind = ReadByte(body, selection, 9);
            var targetRef = ReadUInt32(body, selection, 12);
            if (targetKind == 1)
            {
                if (targetRefKind != 0
                    || targetRef != (uint)origin.RootHomeIndex)
                {
                    throw InvalidMap();
                }
            }
            else if (targetKind == 3)
            {
                if (targetRefKind != 3 || targetRef == 0
                    || targetRef
                        > (uint)constantValues.FixedArrayLayouts.Count)
                {
                    throw InvalidMap();
                }

                var parentLayout = constantValues.FixedArrayLayouts.GetRow(
                    checked((int)targetRef));
                if (parentLayout.OwnerModuleRow != ownerModule
                    || !typeSpecs.TryGetFixedInlineArray(
                        (uint)parentLayout.ArrayTypeSpecRow,
                        out var childTypeSpecRef, out _)
                    || !typeSpecs.AreEquivalentTypeSpecRefs(
                        childTypeSpecRef, emptyArrayTypeSpecRef))
                {
                    throw InvalidMap();
                }
            }
            else
            {
                throw InvalidMap();
            }
        }
    }

    private static void ValidateUnitArrayElementStore(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        RegisterTypeState[] registerTypes, int ownerModule,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues)
    {
        var placeIndex = ReadUInt16(body, payload, 4);
        if (!body.Schema
                .UsesForeignInvocationStorageFixedArrayUnitInitialization()
            || payload.Length != 16 || ReadByte(body, payload, 0) != 1
            || ReadByte(body, payload, 1) != 1
            || ReadByte(body, payload, 2) != 1
            || ReadByte(body, payload, 3) != 1
            || placeIndex > 13 || ReadUInt16(body, payload, 6) != 14
            || ReadUInt32(body, payload, 8) != 0
            || ReadByte(body, payload, 12) != 0
            || ReadByte(body, payload, 13) != 0
            || ReadUInt16(body, payload, 14) != 0)
        {
            throw InvalidMap();
        }

        var place = GetRegisterTypeState(body, registerTypes, placeIndex);
        if (!place.IsPlaceHandle || place.AccessKind != 3
            || place.PlaceOrigins.Length == 0
            || !IsUnitTypeSpecRef(typeSpecs,
                GetRegisterTypeSpecRef(body, place)))
        {
            throw InvalidMap();
        }

        if (!TrRmFeatureManifestValidator.Requires(
                constantValues.Features, constantValues.Modules,
                ownerModule,
                TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
                body.Schema)
            || !constantValues.TargetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            throw InvalidMap();
        }

        foreach (var origin in place.PlaceOrigins)
        {
            ConsumeWork(body);
            if (origin.RootHomeIndex < 0
                || origin.RootHomeIndex >= body.HomeCount
                || origin.PlaceSelectionRecordIndex < 0)
            {
                throw InvalidMap();
            }

            var rootHome = GetHomeMapEntry(body, origin.RootHomeIndex);
            var selection = GetRecordPayload(body,
                origin.PlaceSelectionRecordIndex);
            var layoutRowIndex = ReadUInt32(body, selection, 12);
            if (ReadByte(body, rootHome, 0) != 3
                || ReadByte(body, selection, 0) != 3
                || ReadByte(body, selection, 1) != 3
                || ReadByte(body, selection, 2) != 1
                || ReadByte(body, selection, 9) != 3
                || layoutRowIndex == 0
                || layoutRowIndex
                    > (uint)constantValues.FixedArrayLayouts.Count
                || !IsUnitTypeSpecRef(typeSpecs,
                    GetTypeMapTypeSpecRef(body,
                        ReadUInt16(body, selection, 16)))
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    GetTypeMapTypeSpecRef(body,
                        ReadUInt16(body, selection, 16)),
                    GetRegisterTypeSpecRef(body, place)))
            {
                throw InvalidMap();
            }

            var layout = constantValues.FixedArrayLayouts.GetRow(
                checked((int)layoutRowIndex));
            if (layout.OwnerModuleRow != ownerModule
                || layout.TargetProfileRef != (uint)profileRow
                || layout.ElementStride != 0 || layout.ByteExtent != 0
                || layout.Alignment != 1
                || !typeSpecs.TryGetFixedInlineArray(
                    (uint)layout.ArrayTypeSpecRow,
                    out var elementTypeSpecRef, out var elementCount)
                || elementCount == 0
                || !IsUnitTypeSpecRef(typeSpecs, elementTypeSpecRef))
            {
                throw InvalidMap();
            }
        }
    }

    private static void ValidateArrayElementValueStore(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        RegisterTypeState[] registerTypes,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues)
    {
        var placeIndex = ReadUInt16(body, payload, 4);
        var sourceIndex = ReadUInt16(body, payload, 6);
        var sourceKind = ReadByte(body, payload, 1);
        var transition = ReadByte(body, payload, 2);
        var expectedAccessKind = transition == 1 ? (byte)3 : (byte)2;
        if (payload.Length != 16
            || ReadByte(body, payload, 0) != 1
            || sourceKind is not (1 or 2)
            || transition is not (1 or 2)
            || ReadByte(body, payload, 3) != 1
            || placeIndex > 13
            || (sourceKind == 1
                ? sourceIndex > 13 : sourceIndex >= body.HomeCount)
            || ReadUInt32(body, payload, 8) != 0
            || ReadByte(body, payload, 12) != 0
            || ReadByte(body, payload, 13) != 0
            || ReadUInt16(body, payload, 14) != 0)
        {
            throw InvalidMap();
        }

        var place = GetRegisterTypeState(body, registerTypes, placeIndex);
        if (!place.IsPlaceHandle || place.AccessKind != expectedAccessKind
            || place.PlaceOrigins.Length == 0
            || place.PlaceSelectionRecordIndex < 0)
        {
            throw InvalidMap();
        }

        var placeTypeSpecRef = GetRegisterTypeSpecRef(body, place);
        var selection = GetRecordPayload(body,
            place.PlaceSelectionRecordIndex);
        var selectedTypeSpecRef = GetTypeMapTypeSpecRef(body,
            ReadUInt16(body, selection, 16));
        uint sourceTypeSpecRef;
        if (sourceKind == 2)
        {
            var sourceHome = GetHomeMapEntry(body, sourceIndex);
            var sourceHomeTypeSpecRef = GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, sourceHome, 2));
            var sourceType = typeSpecs.GetTypeSpecRef(
                sourceHomeTypeSpecRef);
            if (ReadByte(body, selection, 0) == 2)
            {
                if (transition != 1
                    || ReadByte(body, selection, 1) != 3
                    || ReadByte(body, selection, 2) != 1
                    || ReadByte(body, selection, 9) != 1
                    || ReadByte(body, sourceHome, 0) != 3
                    || !sourceType.IsRow || sourceType.Form != 0x11
                    || !typeSpecs.TryGetFixedInlineArray(
                        checked((uint)sourceType.Row), out _, out _)
                    || !typeSpecs.AreEquivalentTypeSpecRefs(
                        selectedTypeSpecRef, placeTypeSpecRef)
                    || !typeSpecs.AreEquivalentTypeSpecRefs(
                        sourceHomeTypeSpecRef, placeTypeSpecRef))
                {
                    throw InvalidMap();
                }

                return;
            }

            if (!body.Schema.UsesForeignInvocationStorageFixedArrays()
                || ReadByte(body, selection, 0) != 3
                || ReadByte(body, selection, 9) != 3
                || ReadByte(body, sourceHome, 0) != 3
                || !typeSpecs.AreEquivalentTypeSpecRefs(
                    sourceHomeTypeSpecRef, placeTypeSpecRef))
            {
                throw InvalidMap();
            }

            sourceTypeSpecRef = sourceHomeTypeSpecRef;
        }
        else
        {
            var source = GetRegisterTypeState(body, registerTypes,
                sourceIndex);
            if (source.IsPlaceHandle)
            {
                throw InvalidMap();
            }

            sourceTypeSpecRef = GetRegisterTypeSpecRef(body, source);
        }
        var isScalar = TrRmNativeImportTypeContract.TryGetScalarType(
                typeSpecs, placeTypeSpecRef, out var elementType)
            && DirectNativeImportContract.IsScalar(elementType);
        var isPointer = IsNativeDataPointerTypeSpecRef(typeSpecs,
            placeTypeSpecRef);
        if (ReadByte(body, selection, 0) != 3
            || ReadByte(body, selection, 1) != expectedAccessKind
            || ReadByte(body, selection, 2) != 1
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                selectedTypeSpecRef, placeTypeSpecRef)
            || !typeSpecs.AreEquivalentTypeSpecRefs(
                sourceTypeSpecRef, placeTypeSpecRef)
            || (!isScalar && !isPointer
                && (body.Schema
                        != TrRmArtifactSchema.LocalAggregateLayoutDraft
                    || !IsCopyableRegisterValueType(typeSpecs,
                        placeTypeSpecRef, constantValues, body.Schema))))
        {
            throw InvalidMap();
        }
    }

    private static void ValidateRepeatCopyStore(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        RegisterTypeState[] registerTypes,
        TrRmTypeSpecTableReader typeSpecs)
    {
        var placeIndex = ReadUInt16(body, payload, 4);
        var sourceIndex = ReadUInt16(body, payload, 6);
        var sourceKind = ReadByte(body, payload, 1);
        if (!body.Schema
                .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
            || payload.Length != 16
            || ReadByte(body, payload, 0) != 1
            || sourceKind is not (1 or 2)
            || ReadByte(body, payload, 2) != 1
            || ReadByte(body, payload, 3) != 6
            || placeIndex > 13
            || (sourceKind == 1
                ? sourceIndex > 15 : sourceIndex >= body.HomeCount)
            || ReadUInt32(body, payload, 8) != 0
            || ReadByte(body, payload, 12) != 0
            || ReadByte(body, payload, 13) != 0
            || ReadUInt16(body, payload, 14) != 0)
        {
            throw InvalidMap();
        }

        var place = GetRegisterTypeState(body, registerTypes, placeIndex);
        if (!place.IsPlaceHandle || place.AccessKind != 3
            || place.PlaceOrigins.Length == 0
            || place.PlaceSelectionRecordIndex < 0)
        {
            throw InvalidMap();
        }

        var arrayTypeSpecRef = GetRegisterTypeSpecRef(body, place);
        var arrayType = typeSpecs.GetTypeSpecRef(arrayTypeSpecRef);
        if (!arrayType.IsRow || arrayType.Form != 0x11
            || !typeSpecs.TryGetFixedInlineArray((uint)arrayType.Row,
                out var elementTypeSpecRef, out _))
        {
            throw InvalidMap();
        }

        var selection = GetRecordPayload(body,
            place.PlaceSelectionRecordIndex);
        if (ReadByte(body, selection, 0) != 1
            || ReadByte(body, selection, 1) != 3
            || ReadByte(body, selection, 2) != 1
            || ReadByte(body, selection, 3) != 0
            || ReadUInt16(body, selection, 4) != placeIndex
            || ReadByte(body, selection, 8) != 0
            || ReadByte(body, selection, 9) != 0
            || ReadUInt16(body, selection, 10) != 0
            || ReadUInt32(body, selection, 12) >= (uint)body.HomeCount
            || !typeSpecs.AreEquivalentTypeSpecRefs(arrayTypeSpecRef,
                GetTypeMapTypeSpecRef(body,
                    ReadUInt16(body, selection, 16))))
        {
            throw InvalidMap();
        }

        if (sourceKind == 1 && sourceIndex >= 14)
        {
            return;
        }

        var sourceTypeSpecRef = sourceKind == 1
            ? GetRegisterTypeSpecRef(body,
                GetRegisterTypeState(body, registerTypes, sourceIndex))
            : GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, GetHomeMapEntry(body, sourceIndex), 2));
        if (sourceKind == 1
            && GetRegisterTypeState(body, registerTypes, sourceIndex)
                .IsPlaceHandle)
        {
            throw InvalidMap();
        }

        if (!typeSpecs.AreEquivalentTypeSpecRefs(sourceTypeSpecRef,
                elementTypeSpecRef))
        {
            throw InvalidMap();
        }
    }

    private static void ValidateArrayFieldReplacementStore(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        RegisterTypeState[] registerTypes,
        TrRmFieldDefTableReader fields,
        TrRmTypeSpecTableReader typeSpecs, byte transition,
        byte accessKind)
    {
        var placeIndex = ReadUInt16(body, payload, 4);
        var sourceIndex = ReadUInt16(body, payload, 6);
        var sourceKind = ReadByte(body, payload, 1);
        if (!body.Schema
                .UsesForeignInvocationStorageFixedArrayRepeatInitialization()
            || payload.Length != 16
            || ReadByte(body, payload, 0) != 1
            || sourceKind is not (1 or 2)
            || ReadByte(body, payload, 2) != transition
            || ReadByte(body, payload, 3) != 1
            || placeIndex > 13
            || (sourceKind == 1
                ? sourceIndex > 15 : sourceIndex >= body.HomeCount)
            || ReadUInt32(body, payload, 8) != 0
            || ReadByte(body, payload, 12) != 0
            || ReadByte(body, payload, 13) != 0
            || ReadUInt16(body, payload, 14) != 0)
        {
            throw InvalidMap();
        }

        var place = GetRegisterTypeState(body, registerTypes, placeIndex);
        if (!place.IsPlaceHandle || place.AccessKind != accessKind
            || place.PlaceOrigins.Length == 0
            || place.PlaceSelectionRecordIndex < 0)
        {
            throw InvalidMap();
        }

        var selection = GetRecordPayload(body,
            place.PlaceSelectionRecordIndex);
        var fieldRow = ReadUInt32(body, selection, 12);
        if (ReadByte(body, selection, 0) != 2
            || ReadByte(body, selection, 1) != accessKind
            || ReadByte(body, selection, 2) != 1
            || fieldRow is 0 or > 0xFF_FFFF
            || fieldRow > (uint)fields.Count)
        {
            throw InvalidMap();
        }

        var placeTypeSpecRef = GetRegisterTypeSpecRef(body, place);
        var fieldTypeSpecRef = fields.GetTypeSpecRef(checked((int)fieldRow));
        if (!typeSpecs.AreEquivalentTypeSpecRefs(placeTypeSpecRef,
                GetTypeMapTypeSpecRef(body,
                    ReadUInt16(body, selection, 16)))
            || !typeSpecs.AreEquivalentTypeSpecRefs(placeTypeSpecRef,
                fieldTypeSpecRef))
        {
            throw InvalidMap();
        }

        if (sourceKind == 1 && sourceIndex >= 14)
        {
            if (!TrRmNativeImportTypeContract.TryGetScalarType(
                    typeSpecs, placeTypeSpecRef, out var scalarType)
                || !DirectNativeImportContract.IsScalar(scalarType))
            {
                throw InvalidMap();
            }
            return;
        }

        var sourceTypeSpecRef = sourceKind == 1
            ? GetRegisterTypeSpecRef(body,
                GetRegisterTypeState(body, registerTypes, sourceIndex))
            : GetTypeMapTypeSpecRef(body,
                ReadUInt16(body, GetHomeMapEntry(body, sourceIndex), 2));
        if (sourceKind == 1
            && GetRegisterTypeState(body, registerTypes, sourceIndex)
                .IsPlaceHandle)
        {
            throw InvalidMap();
        }

        if (!typeSpecs.AreEquivalentTypeSpecRefs(sourceTypeSpecRef,
                placeTypeSpecRef))
        {
            throw InvalidMap();
        }
    }

    private static bool IsFieldReplacementPlaceSelection(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> payload,
        RegisterTypeState[] registerTypes)
    {
        var placeIndex = ReadUInt16(body, payload, 4);
        if (placeIndex > 13)
        {
            return false;
        }

        var place = GetRegisterTypeState(body, registerTypes, placeIndex);
        if (!place.IsPlaceHandle || place.PlaceSelectionRecordIndex < 0)
        {
            return false;
        }

        var selection = GetRecordPayload(body,
            place.PlaceSelectionRecordIndex);
        return ReadByte(body, selection, 0) == 2
            && ReadByte(body, selection, 9) == 1;
    }

    private static bool IsNativeDataPointerTypeSpecRef(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        return type.TypeCode == 0x8C
            && type.Form is 0x01 or 0x0C;
    }

    private static bool IsUnitTypeSpecRef(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        return !type.IsRow && type.Form == 0x01
            && type.Qualifiers == 0 && type.TypeCode == 0;
    }

    private static void UpdateStorageAddressResult(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        RegisterTypeState[] registerTypes, int ownerModule,
        TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues,
        bool allowIncompletePlaceFlow)
    {
        var storageRecordIndex = ReadUInt16(body, instruction, 2);
        var storage = GetRecordPayload(body, storageRecordIndex);
        var sourceKind = ReadByte(body, storage, 0);
        var sourceIndex = ReadUInt16(body, storage, 2);
        var resultKind = ReadByte(body, storage, 1);
        var resultIndex = ReadUInt16(body, storage, 4);
        var pointerTypeMapIndex = ReadUInt16(body, storage, 6);
        if (sourceKind == 1)
        {
            if (!body.Schema.UsesForeignInvocationStorageFixedArrays())
            {
                throw InvalidMap();
            }

            var source = GetRegisterTypeState(body, registerTypes, sourceIndex);
            if (!source.IsPlaceHandle)
            {
                throw InvalidMap();
            }
            var sourceTypeSpecRef = GetRegisterTypeSpecRef(body, source);

            if (source.PlaceOrigins.Length == 0)
            {
                if (!allowIncompletePlaceFlow)
                {
                    throw InvalidMap();
                }
            }
            else
            {
                var requiredElementCount = ReadUInt32(body, storage, 8);
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
                if (requiredElementCount == 0
                    || (!isOpaqueVoid
                        && !typeSpecs.AreEquivalentTypeSpecRefs(
                            sourceTypeSpecRef, pointeeType))
                    || (isOpaqueVoid && pointerType[1] != 0))
                {
                    throw InvalidMap();
                }

                foreach (var origin in source.PlaceOrigins)
                {
                    ConsumeWork(body);
                    if (origin.RootHomeIndex < 0
                        || origin.RootHomeIndex >= body.HomeCount
                        || origin.PlaceSelectionRecordIndex < 0)
                    {
                        throw InvalidMap();
                    }

                    var rootHome = GetHomeMapEntry(body,
                        origin.RootHomeIndex);
                    var selection = GetRecordPayload(body,
                        origin.PlaceSelectionRecordIndex);
                    if (ReadByte(body, rootHome, 0) != 3
                        || ReadByte(body, selection, 0) != 3
                        || ReadByte(body, selection, 9) != 3)
                    {
                        throw InvalidMap();
                    }

                    var layoutRow = ReadUInt32(body, selection, 12);
                    var arrayLayout = constantValues.FixedArrayLayouts
                        .GetRow(checked((int)layoutRow));
                    if (arrayLayout.OwnerModuleRow != ownerModule
                        || !typeSpecs.TryGetFixedInlineArray(
                            (uint)arrayLayout.ArrayTypeSpecRow,
                            out var elementTypeSpecRef,
                            out var elementCount)
                        || !typeSpecs.AreEquivalentTypeSpecRefs(
                            sourceTypeSpecRef, elementTypeSpecRef)
                        || requiredElementCount > elementCount
                        || (pointerType[1] == 1
                            && (!AllowsWriteAuthority(source.AccessKind,
                                    sourceTypeSpecRef, typeSpecs)
                                || (ReadByte(body, rootHome, 1) & 1) == 0)))
                    {
                        throw InvalidMap();
                    }
                }
            }
        }

        if (resultKind == 1 && resultIndex < 14)
        {
            SetRegisterTypeState(body, registerTypes, resultIndex,
                RegisterTypeState.FromTypeMap(pointerTypeMapIndex));
        }
    }

    private static bool IsValueRegisterIndex(TrRmRegisterBodyReader body,
        RegisterTypeState[] registerTypes, int register,
        TrRmTypeSpecTableReader typeSpecs)
    {
        var type = ReadRegisterTypeState(body, registerTypes, register);
        if (!type.IsKnown || type.IsPlaceHandle)
        {
            return false;
        }

        return IsFixedArrayIndexType(typeSpecs,
            GetRegisterTypeSpecRef(body, type));
    }

    private static bool IsHomeIndex(TrRmRegisterBodyReader body,
        int homeIndex, TrRmTypeSpecTableReader typeSpecs)
    {
        var home = GetHomeMapEntry(body, homeIndex);
        return IsFixedArrayIndexType(typeSpecs,
            GetTypeMapTypeSpecRef(body, ReadUInt16(body, home, 2)));
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
                ? type.TypeCode is >= 0x12 and <= 0x19
                : type.TypeCode is >= 0x06 and <= 0x0F);
    }

    private static RegisterTypeState GetRegisterTypeState(
        TrRmRegisterBodyReader body, RegisterTypeState[] registerTypes,
        int register)
    {
        var type = ReadRegisterTypeState(body, registerTypes, register);
        if (!type.IsKnown)
        {
            throw InvalidMap();
        }

        return type;
    }

    private static uint GetRegisterTypeSpecRef(TrRmRegisterBodyReader body,
        RegisterTypeState type)
    {
        ConsumeWork(body);
        return type.TypeSpecRef != 0
            ? type.TypeSpecRef
            : type.TypeMapIndex >= 0
                ? GetTypeMapTypeSpecRef(body, type.TypeMapIndex)
                : throw InvalidMap();
    }

    private static bool MatchesRegisterType(TrRmRegisterBodyReader body,
        RegisterTypeState actual, uint expectedTypeSpecRef, int ownerModule,
        TrRmTypeDefTableReader types, TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams, TrRmImportTableReader imports,
        ConstantSourceValueContext constantValues,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors)
    {
        ConsumeWork(body);
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
        ConstantSourceValueContext constantValues,
        TrRmArtifactSchema schema)
    {
        var type = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (type.Form == 0x11 && type.Qualifiers == 0)
        {
            return typeSpecs.TryGetFixedInlineArray(typeSpecRef,
                out _, out _);
        }

        var isAtomicValueType = type.TypeCode is > 0 and < 0x80;
        var isPointerFamily = typeSpecs
            .TryGetNativePointerPointeeTypeSpecRef(typeSpecRef, out _);
        if ((type.Form is 0x02 or 0x03) && type.Qualifiers == 0)
        {
            var typeDefRow = typeSpecs.GetNominalTypeDefRow(typeSpecRef,
                constantValues.TypeDefs, constantValues.Imports);
            var typeKind = constantValues.TypeDefs.GetKind(typeDefRow);
            return typeKind == 4
                || (schema == TrRmArtifactSchema.LocalAggregateLayoutDraft
                    && typeKind == 1
                    && constantValues.TypeDefs.HasDeclaredCopy(
                        typeDefRow));
        }

        return type.Form == 0x0D
            || (type.Form == 0x01 && (isAtomicValueType || isPointerFamily))
            || (type.Form is 0x08 or 0x0C && isPointerFamily);
    }

    private static void ValidateCallSiteHomeTypes(TrRmRegisterBodyReader body,
        int methodDefRow, int ownerModule, TrRmTypeDefTableReader types,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmFieldDefTableReader fields,
        TrRmFieldPathTableReader fieldPaths,
        TrRmLayoutTableReader layouts,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmImportTableReader imports,
        ConstantSourceValueContext constantValues, TrRmContractTableReader contracts,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmNativeImportTableReader standardNativeImports,
        TrRmNativeFunctionPointerAnchorTableReader nativeFunctionPointerAnchors,
        RegisterTypeState[][] registerTypeEntries,
        InitializationReferenceTypeMap initializationReferenceTypes)
    {
        var isConstructionInitializer
            = body.Schema.UsesConstructionContracts()
                && contracts.HasConstructionClause(methodDefRow)
                && contracts.GetConstructionClause(methodDefRow).RoleTag
                    == 1;
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            ConsumeWork(body);
            var block = body.GetBlock(blockIndex);
            var codeOffset = checked((int)ReadUInt32(body, block, 0));
            var code = body.GetBlockCode(blockIndex);
            var registerTypes = CopyRegisterTypeStates(body,
                registerTypeEntries[blockIndex]);
            var position = 0;
            while (position < code.Length)
            {
                ConsumeWork(body, 2);
                var opcode = code[position];
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        body.Schema, body.UsesNativePointerLoads);
                ConsumeWork(body);
                var instruction = code.Slice(position,
                    instructionLength);
                ConsumeWork(body, checked((ulong)instructionLength));
                if (opcode is 0x80 or 0x82 or 0xE1)
                {
                    ValidateCompactOrdinaryClassCall(body, instruction,
                        opcode, registerTypes, methodDefRow, ownerModule,
                        types, typeSpecs, methods, signatures, parameters,
                        genericParams, contracts, imports, constantValues,
                        nativeFunctionPointerAnchors);
                }
                ValidateConstructionContextPlaceUse(body, instruction,
                    opcode, registerTypes);
                var recordIndex = opcode switch
                {
                    0x90 => ReadByte(body, instruction, 1),
                    0xE0 => ReadUInt16(body, instruction, 2),
                    _ => -1,
                };
                if (recordIndex >= 0)
                {
                    var payload = GetRecordPayload(body, recordIndex);
                    ConsumeWork(body);
                    if (ReadByte(body, payload, 0) is 1 or 2 or 3 or 4)
                    {
                        var targetMethod = GetCallSiteTargetMethod(body, payload,
                            registerTypes, typeSpecs, methods, imports,
                            nativeFunctionPointerAnchors);
                        var signature = methods.GetSignature(targetMethod);
                        var (firstParameter, parameterCount) =
                            signatures.GetParamRange(signature);
                        var nativeParameters = contracts.GetNativeParameters(targetMethod);
                        var receiverTypeSpec
                            = signatures.GetReceiverTypeSpec(signature);
                        var isGenericInterfaceRequirement
                            = ReadByte(body, payload, 0) == 4
                                && ReadByte(body, GetRecordPayload(body,
                                    ReadUInt16(body, payload, 4)), 0) == 2;
                        var targetConstructionRole
                            = contracts.HasConstructionClause(targetMethod)
                                ? contracts.GetConstructionClause(
                                    targetMethod).RoleTag : (byte)0;
                        if (targetConstructionRole == 3
                            && !body.Schema.UsesPropertyInitializedState())
                        {
                            throw InvalidMap();
                        }

                        var hasReceiver = receiverTypeSpec != 0
                            && (body.Schema.UsesObjectConstruction()
                                || (body.Schema.UsesConstructionContracts()
                                    && targetConstructionRole == 1)
                                || body.Schema.UsesMethodReceiverAccess()
                                || isGenericInterfaceRequirement);
                        if (ReadUInt32(body, payload, 8)
                            != (uint)(parameterCount
                                + (hasReceiver ? 1 : 0)))
                        {
                            throw InvalidMap();
                        }

                        var inputOffset = 16;
                        if (hasReceiver)
                        {
                            if (body.Schema.UsesPropertyInitializedState()
                                && targetConstructionRole is 1 or 3)
                            {
                                ValidatePropertyInitializerReceiverInput(
                                    body, methodDefRow, targetMethod, payload,
                                    receiverTypeSpec, ref typeSpecs, methods,
                                    signatures, contracts);
                            }
                            else if (isGenericInterfaceRequirement)
                            {
                                ValidateGenericCallInterfaceReceiverInput(
                                    body, methodDefRow, targetMethod,
                                    payload, inputOffset,
                                    typeSpecs, genericParams, constraints,
                                    methods, signatures, parameters);
                            }
                            else if (targetConstructionRole == 1)
                            {
                                ValidateConstructionReceiverInput(body,
                                    methodDefRow, targetMethod, payload,
                                    receiverTypeSpec, methods, typeSpecs,
                                    contracts);
                            }
                            else if (targetConstructionRole == 2)
                            {
                                throw InvalidMap();
                            }
                            else
                            {
                                ValidateOrdinaryClassReceiverInput(body,
                                    methodDefRow, targetMethod, payload,
                                    receiverTypeSpec, registerTypes,
                                    ownerModule, types, typeSpecs, imports,
                                    methods, contracts);
                            }
                            inputOffset += 4;
                        }

                        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
                        {
                            ConsumeWork(body);
                            var selectorKind = ReadByte(body, payload,
                                inputOffset);
                            if (selectorKind is 1 or 2)
                            {
                                var sourceUse = ReadByte(body, payload,
                                    inputOffset + 1);
                                var isNativeOutputCell
                                    = contracts.IsNativeOutputCellParameter(
                                        targetMethod, ordinal);
                                if (isNativeOutputCell != (sourceUse == 6))
                                {
                                    throw InvalidMap();
                                }

                                var selectorIndex = ReadUInt16(body, payload,
                                    inputOffset + 2);
                                if (selectorKind == 2)
                                {
                                    var selectedHomeRole = ReadByte(body,
                                        GetHomeMapEntry(body, selectorIndex),
                                        0);
                                    if (selectedHomeRole == 4
                                        || (isConstructionInitializer
                                            && selectedHomeRole == 1))
                                    {
                                        throw InvalidMap();
                                    }
                                }
                                var typeState = selectorKind == 1
                                    ? GetRegisterTypeState(body, registerTypes,
                                        selectorIndex)
                                    : RegisterTypeState.FromTypeMap(
                                        GetHomeTypeMapIndex(body,
                                            selectorIndex));
                                ConsumeWork(body);
                                var formalType = GetGenericCallTypeSpecRef(
                                    body, payload,
                                    parameters.GetTypeSpec(
                                        firstParameter + ordinal),
                                    targetMethod, methods, ref typeSpecs,
                                    genericParams, constraints);
                                var isInitializationDestination
                                    = typeSpecs.IsInitializationDestinationType(
                                        formalType);
                                if (isInitializationDestination
                                        != (sourceUse == 5)
                                    || (isInitializationDestination
                                        && parameters.GetMode(
                                            firstParameter + ordinal) != 0))
                                {
                                    throw InvalidMap();
                                }

                                var isImplicitReference
                                    = (parameters.GetFlags(firstParameter
                                        + ordinal) & 1) != 0;
                                var nativeParameterAccess = (byte)0;
                                foreach (var nativeParameter in nativeParameters)
                                {
                                    ConsumeWork(body);
                                    if (nativeParameter.ParameterOrdinal == ordinal)
                                    {
                                        nativeParameterAccess = nativeParameter.Access;
                                        break;
                                    }
                                }

                                if (sourceUse == 6)
                                {
                                    if (selectorKind != 2
                                        || nativeParameterAccess != 0
                                        || !standardNativeImports
                                            .HasImportForMethod(targetMethod)
                                        || !contracts
                                            .IsNativeOutputCellParameter(
                                                targetMethod, ordinal)
                                        || !TrRmFeatureManifestValidator
                                            .Requires(features, modules,
                                                methods.GetOwnerModule(
                                                    targetMethod),
                                                TrRmFeatureTableReader
                                                    .NativeOutputCellsCandidateFeatureId,
                                                body.Schema)
                                        || !TrRmNativeImportTypeContract
                                            .TryGetNativeOutputCellPointeeTypeSpecRef(
                                                typeSpecs, formalType,
                                                out var outputTypeSpec)
                                        || !typeSpecs.AreEquivalentTypeSpecRefs(
                                            GetTypeMapTypeSpecRef(body,
                                                typeState.TypeMapIndex),
                                            outputTypeSpec))
                                    {
                                        throw InvalidMap();
                                    }
                                }
                                else if (sourceUse == 5)
                                {
                                    if (isImplicitReference
                                        || nativeParameterAccess != 0
                                        || !MatchesRegisterType(body,
                                            typeState, formalType, ownerModule,
                                            types, typeSpecs, genericParams,
                                            imports, constantValues,
                                            nativeFunctionPointerAnchors))
                                    {
                                        throw InvalidMap();
                                    }
                                }
                                else if (isImplicitReference)
                                {
                                    if (sourceUse != 3
                                        || nativeParameterAccess != 0
                                        || !typeSpecs
                                            .TryGetImplicitReadonlyReferenceReferentTypeSpecRef(
                                                formalType,
                                                out var referentType)
                                        || (selectorKind == 1
                                            ? !typeState.IsPlaceHandle
                                                || typeState.AccessKind
                                                    is not (1 or 2)
                                            : typeState.IsPlaceHandle)
                                        || !AreEquivalentSelectedTypeSpecRefs(
                                            GetRegisterTypeSpecRef(body,
                                                typeState),
                                            referentType, ownerModule,
                                            types, typeSpecs, genericParams,
                                            imports, constantValues,
                                            nativeFunctionPointerAnchors))
                                    {
                                        throw InvalidMap();
                                    }
                                }
                                else if (sourceUse == 3
                                    || (nativeParameterAccess != 0
                                        ? !TrRmNativeImportTypeContract
                                            .MatchesNativeParameterArgumentType(
                                                typeSpecs,
                                                GetRegisterTypeSpecRef(body,
                                                    typeState),
                                                formalType,
                                                nativeParameterAccess)
                                        : !MatchesRegisterType(body, typeState,
                                            formalType, ownerModule, types,
                                            typeSpecs, genericParams, imports,
                                            constantValues,
                                            nativeFunctionPointerAnchors)))
                                {
                                    throw InvalidMap();
                                }

                                if (ReadByte(body, payload, 0) == 4)
                                {
                                    var expectedSourceUse
                                        = GetGenericCallSourceUse(formalType,
                                            typeSpecs, genericParams);
                                    if (sourceUse != expectedSourceUse
                                        || (sourceUse == 2
                                            && selectorKind != 2)
                                        || (sourceUse == 2
                                            && !IsTerminalGenericCallTransfer(
                                                body, blockIndex, code,
                                                position, recordIndex,
                                                payload)))
                                    {
                                        throw InvalidMap();
                                    }
                                }
                            }

                            inputOffset += 4;
                        }

                        if (ReadByte(body, payload, 1) == 2)
                        {
                            var resultHome = ReadUInt16(body, payload, 6);
                            var resultHomeEntry = GetHomeMapEntry(body,
                                resultHome);
                            var resultHomeRole
                                = ReadByte(body, resultHomeEntry, 0);
                            if (resultHomeRole == 4
                                || (isConstructionInitializer
                                    && resultHomeRole == 1)
                                || (body.Schema.UsesObjectConstruction()
                                    && resultHomeRole != 3))
                            {
                                throw InvalidMap();
                            }

                            if (ReadByte(body, payload, 0) == 4)
                            {
                                if (ReadByte(body, resultHomeEntry, 0) != 3
                                    || ReadByte(body, resultHomeEntry, 1) != 1)
                                {
                                    throw InvalidMap();
                                }
                            }

                            var typeMapIndex = GetHomeTypeMapIndex(body,
                                resultHome);
                            var resultType = GetGenericCallTypeSpecRef(body,
                                payload,
                                signatures.GetResultTypeSpec(signature),
                                targetMethod, methods, ref typeSpecs,
                                genericParams, constraints);
                            if (!MatchesTypeMapEntry(body, typeMapIndex,
                                resultType,
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
                        registerTypes, ref typeSpecs, methods, signatures,
                        genericParams, constraints, imports,
                        nativeFunctionPointerAnchors);
                }
                else
                {
                    UpdateRegisterTypeStates(body, instruction, registerTypes,
                        methodDefRow, methods, contracts, types, fields,
                        imports,
                        signatures, parameters, genericParams, typeSpecs,
                        constantValues,
                        initializationReferenceTypes,
                        nativeFunctionPointerAnchors,
                        checked(codeOffset + position),
                        allowIncompletePlaceFlow: false);
                }
                position += instruction.Length;
            }
        }
    }

    private static void ValidateConstructionContextPlaceUse(
        TrRmRegisterBodyReader body, ReadOnlySpan<byte> instruction,
        byte opcode, RegisterTypeState[] registerTypes)
    {
        if (!body.Schema.UsesObjectConstruction()
            && !body.Schema.UsesPropertyInitializedState())
        {
            return;
        }

        var selectionRecordIndex = opcode switch
        {
            0xD8 or 0xDA or 0xDB or 0xDC or 0xDD
                => ReadUInt16(body, instruction, 2),
            0x61 or 0x91 => ReadByte(body, instruction, 1) >> 4,
            _ => -1,
        };
        if (selectionRecordIndex >= 0)
        {
            var selection = GetRecordPayload(body,
                selectionRecordIndex);
            var targetKind = ReadByte(body, selection, 0);
            var accessKind = ReadByte(body, selection, 1);
            var baseKind = ReadByte(body, selection, 3);
            if (targetKind == 1)
            {
                var targetHome = ReadUInt32(body, selection, 12);
                if (targetHome > int.MaxValue
                    || ReadByte(body, GetHomeMapEntry(body,
                        checked((int)targetHome)), 0) is 1 or 4)
                {
                    throw InvalidMap();
                }
            }

            if (baseKind == 2)
            {
                var baseHome = ReadUInt16(body, selection, 6);
                var baseHomeRole = ReadByte(body,
                    GetHomeMapEntry(body, baseHome), 0);
                var placeOpcode = opcode is 0xD8 or 0xDA or 0x61 or 0x91;
                var validContextRead = placeOpcode
                    && targetKind == 2 && accessKind == 1;
                var validPropertyContextAccess = placeOpcode
                    && targetKind == 2
                    && body.Schema.UsesPropertyInitializedState()
                    && accessKind == 3;
                var validInitializerPlace = placeOpcode
                    && targetKind == 2 && accessKind is 1 or 3;
                if ((baseHomeRole == 4
                        && !validContextRead && !validPropertyContextAccess)
                    || (baseHomeRole == 1 && !validInitializerPlace))
                {
                    throw InvalidMap();
                }
            }
        }

        var placeRegister = opcode switch
        {
            0x63 or 0x65 or 0x67 => ReadByte(body, instruction, 1) >> 4,
            0x62 => ReadByte(body, instruction, 1) & 0x0F,
            0x64 or 0x66 => ReadByte(body, instruction, 1) & 0x0F,
            _ => -1,
        };
        if (placeRegister < 0)
        {
            return;
        }

        var place = GetRegisterTypeState(body, registerTypes,
            placeRegister);
        foreach (var origin in place.PlaceOrigins)
        {
            ConsumeWork(body);
            var originRole = ReadByte(body, GetHomeMapEntry(body,
                origin.RootHomeIndex), 0);
            if ((originRole == 4 && opcode != 0x63)
                || (originRole == 1
                    && opcode is 0x62 or 0x65 or 0x66 or 0x67))
            {
                throw InvalidMap();
            }
        }
    }

    private static uint GetTypeMapTypeSpecRef(
        TrRmRegisterBodyReader body, int typeMapIndex)
    {
        var token = GetTypeMapToken(body, typeMapIndex);
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

    private static void RejectCompactConstructionInitializerCall(
        TrRmRegisterBodyReader body, int methodMapIndex,
        TrRmMethodDefTableReader methods, TrRmContractTableReader contracts,
        TrRmImportTableReader imports)
    {
        if (!body.Schema.UsesObjectConstruction())
        {
            return;
        }

        var targetMethod = GetMethodMapTarget(body, methodMapIndex,
            methods, imports);
        if (contracts.HasConstructionClause(targetMethod)
            && contracts.GetConstructionClause(targetMethod).RoleTag == 1)
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
            ConsumeWork(body);
            var record = body.GetRecord(recordIndex);
            ConsumeWork(body, 2);
            if (record[0] != 0x0D)
            {
                continue;
            }
            ConsumeWork(body, checked((ulong)(record.Length - 4)));

            if (!TrRmFeatureManifestValidator.Requires(features, modules,
                ownerModule, TrRmFeatureTableReader.NativeExportEntriesFeatureId))
            {
                throw InvalidMap();
            }

            var payload = record[4..];
            var pointerTypeMapIndex = ReadUInt16(body, payload, 6);
            var pointerTypeToken = GetTypeMapToken(body,
                pointerTypeMapIndex);
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

            var methodMapIndex = ReadUInt16(body, payload, 4);
            var methodMapToken = GetMethodMapToken(body, methodMapIndex);
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

            if (ReadByte(body, payload, 0) == 2)
            {
                var homeIndex = ReadUInt16(body, payload, 2);
                var home = GetHomeMapEntry(body, homeIndex);
                var homeTypeMapIndex = ReadUInt16(body, home, 2);
                var homeTypeToken = GetTypeMapToken(body,
                    homeTypeMapIndex);
                if (ReadByte(body, home, 0) != 3
                    || (homeTypeToken >> 24) != 0x11)
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
            ConsumeWork(body);
            var record = body.GetRecord(recordIndex);
            ConsumeWork(body, 2);
            if (record[0] != 0x0E)
            {
                continue;
            }
            ConsumeWork(body, checked((ulong)(record.Length - 4)));

            if (!TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule, TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
                    body.Schema))
            {
                throw InvalidMap();
            }

            var payload = record[4..];
            var sourceKind = ReadByte(body, payload, 0);
            var resultKind = ReadByte(body, payload, 1);
            var sourceIndex = ReadUInt16(body, payload, 2);
            var pointerTypeMapIndex = ReadUInt16(body, payload, 6);
            var requiredElementCount = ReadUInt32(body, payload, 8);
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
                var sourceHome = GetHomeMapEntry(body, sourceIndex);
                if (ReadByte(body, sourceHome, 0) != 3)
                {
                    throw InvalidMap();
                }

                sourceType = GetTypeMapTypeSpecRef(body,
                    ReadUInt16(body, sourceHome, 2));
                sourceHomeMutable = (ReadByte(body, sourceHome, 1) & 1) != 0;
                var sourceIsScalar
                    = TrRmNativeImportTypeContract.TryGetScalarType(
                        typeSpecs, sourceType, out var sourceScalar)
                    && DirectNativeImportContract.IsScalar(sourceScalar);
                if (!sourceIsScalar
                    && !IsNativeDataPointerTypeSpecRef(typeSpecs,
                        sourceType))
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
                var resultHome = GetHomeMapEntry(body,
                    ReadUInt16(body, payload, 4));
                if (ReadByte(body, resultHome, 0) != 3
                    || !typeSpecs.AreEquivalentTypeSpecRefs(
                        GetTypeMapTypeSpecRef(body,
                            ReadUInt16(body, resultHome, 2)), pointerType))
                {
                    throw InvalidMap();
                }
            }
        }
    }

    private static void ValidateFixedArrayPlaceSelections(
        TrRmRegisterBodyReader body, int ownerModule,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmMethodDefTableReader methods, int methodDefRow,
        TrRmImportTableReader imports, TrRmTypeSpecTableReader typeSpecs,
        ConstantSourceValueContext constantValues)
    {
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            ConsumeWork(body);
            var record = body.GetRecord(recordIndex);
            ConsumeWork(body, 2);
            if (record[0] != 0x04)
            {
                continue;
            }
            ConsumeWork(body, checked((ulong)(record.Length - 4)));

            var payload = record[4..];
            var targetKind = ReadByte(body, payload, 0);
            var targetRefKind = ReadByte(body, payload, 9);
            if (targetRefKind == 1 && targetKind == 2
                && body.Schema.UsesForeignInvocationStorageFixedArrays())
            {
                ValidateFixedArrayFieldPlaceSelection(body, payload,
                    methodDefRow, ownerModule, methods, types, fields,
                    imports, typeSpecs);
                continue;
            }

            if (targetRefKind != 3)
            {
                continue;
            }

            if (!body.Schema.UsesForeignInvocationStorageFixedArrays()
                || targetKind != 3
                || ReadByte(body, payload, 2) != 1
                || ReadByte(body, payload, 3) is not (1 or 2)
                || ReadByte(body, payload, 8) is not (1 or 2)
                || !TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule,
                    TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
                    body.Schema))
            {
                throw InvalidMap();
            }

            var layoutRow = ReadUInt32(body, payload, 12);
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
                        ReadUInt16(body, payload, 16)), elementTypeSpecRef))
            {
                throw InvalidMap();
            }

            if (ReadByte(body, payload, 3) == 2)
            {
                var baseHome = ReadUInt16(body, payload, 6);
                var home = GetHomeMapEntry(body, baseHome);
                if (ReadByte(body, home, 0) != 3
                    || !typeSpecs.AreEquivalentTypeSpecRefs(
                        GetTypeMapTypeSpecRef(body,
                            ReadUInt16(body, home, 2)),
                        (uint)arrayLayout.ArrayTypeSpecRow))
                {
                    throw InvalidMap();
                }

                var referent = typeSpecs.GetTypeSpecRef(elementTypeSpecRef);
                var accessKind = ReadByte(body, payload, 1);
                var requiresWrite = accessKind is 2 or 3 or 4
                    || (accessKind == 5
                        && (referent.Qualifiers & 1) != 0);
                var initializesHomeArray = accessKind == 3;
                if (requiresWrite && !initializesHomeArray
                    && (ReadByte(body, home, 1) & 1) == 0)
                {
                    throw InvalidMap();
                }
            }

            if (ReadByte(body, payload, 8) == 2)
            {
                var indexHome = ReadUInt16(body, payload, 10);
                var home = GetHomeMapEntry(body, indexHome);
                if (!IsFixedArrayIndexType(typeSpecs,
                        GetTypeMapTypeSpecRef(body,
                            ReadUInt16(body, home, 2))))
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

        var token = GetMethodMapToken(body, methodMapIndex);
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
        ConstantSourceValueContext constantValues, ulong depth,
        Stack<(int Row, ulong Depth)> pendingRows)
    {
        ConsumeWork(typeSpecs, sizeof(uint) + 1);
        var genericTypeSpecRow = checked((int)ReadUInt32(data, 2));
        var genericTypeSpec = typeSpecs.GetRow(genericTypeSpecRow);
        ConsumeWork(typeSpecs);
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

        ConsumeWork(typeSpecs, 11);
        var (formalFirst, formalCount) = types.GetGenericRange(typeDefRow);
        ConsumeWork(typeSpecs);
        var argumentCount = typeSpecs.GetConstructedArgumentCount(row);
        if (argumentCount != formalCount)
        {
            throw InvalidMap();
        }

        for (var ordinal = 0; ordinal < argumentCount; ordinal++)
        {
            ConsumeWork(typeSpecs);
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
                        var childDepth = checked(depth + 1);
                        typeSpecs.ResourceLedger?.ReserveWorkAtDepth(1,
                            childDepth);
                        pendingRows.Push((argument.Row, childDepth));
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
                        constantValues.Imports, typeSpecs.ResourceLedger,
                        constantValues.Fields,
                        constantValues.TransparentConstantDomains);
                    break;
                default:
                    throw InvalidMap();
            }
        }

        var definitionDepth = checked(depth + 1);
        typeSpecs.ResourceLedger?.ReserveWorkAtDepth(1, definitionDepth);
        pendingRows.Push((genericTypeSpecRow, definitionDepth));
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
        ulong depth, Stack<(int Row, ulong Depth)> pendingRows)
    {
        ConsumeWork(typeSpecs);
        var reference = typeSpecs.GetTypeSpecRef(value,
            allowInitQualifier: allowInitQualifier);
        if (reference.IsRow)
        {
            var childDepth = checked(depth + 1);
            typeSpecs.ResourceLedger?.ReserveWorkAtDepth(1, childDepth);
            pendingRows.Push((reference.Row, childDepth));
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
