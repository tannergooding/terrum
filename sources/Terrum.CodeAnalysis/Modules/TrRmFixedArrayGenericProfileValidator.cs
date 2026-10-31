// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmFixedArrayGenericProfileValidator
{
    internal static void ValidateA3(
        TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs,
        TrRmGenericParamTableReader genericParams,
        TrRmConstraintTableReader constraints,
        TrRmImportTableReader imports,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmBodyTableReader bodies,
        TrRmContractTableReader contracts,
        TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmFixedArrayLayoutTableReader arrayLayouts,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        for (var formal = 1; formal <= genericParams.Count; formal++)
        {
            resourceLedger?.ConsumeWork();
            resourceLedger?.ConsumeWork(sizeof(uint) + 1);
            var ownerKind = genericParams.GetOwnerDefId(formal) >> 24;
            resourceLedger?.ConsumeWork();
            if (ownerKind == 0x16)
            {
                throw UnsupportedGenericMetadata();
            }
            if (ownerKind != 0x13)
            {
                throw InvalidGenericMetadata();
            }
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            resourceLedger?.ConsumeWork();
            resourceLedger?.ConsumeWork(sizeof(uint) + 1);
            var ownerTypeDef = methods.GetOwnerType(method);
            resourceLedger?.ConsumeWork(11);
            if (ownerTypeDef != 0
                && typeDefs.GetGenericRange(ownerTypeDef).Count != 0)
            {
                throw UnsupportedGenericMetadata();
            }

            resourceLedger?.ConsumeWork(sizeof(uint) + 1);
            var signature = methods.GetSignature(method);
            resourceLedger?.ConsumeWork(2UL * (sizeof(uint) + 1));
            var resultType = signatures.GetResultTypeSpec(signature);
            var receiverType = signatures.GetReceiverTypeSpec(signature);
            if (ContainsA3GenericUse(resultType, typeSpecs, typeDefs, imports,
                    resourceLedger)
                || (receiverType != 0
                    && ContainsA3GenericUse(receiverType, typeSpecs, typeDefs,
                        imports, resourceLedger)))
            {
                throw UnsupportedGenericMetadata();
            }

            resourceLedger?.ConsumeWork(
                (2UL * (sizeof(uint) + 1)) + 1);
            var (firstParameter, parameterCount)
                = signatures.GetParamRange(signature);
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                resourceLedger?.ConsumeWork();
                resourceLedger?.ConsumeWork(sizeof(uint) + 1);
                if (ContainsA3GenericUse(
                        parameters.GetTypeSpec(firstParameter + ordinal),
                        typeSpecs, typeDefs, imports, resourceLedger))
                {
                    throw UnsupportedGenericMetadata();
                }
            }
        }

        for (var typeDef = 1; typeDef <= typeDefs.Count; typeDef++)
        {
            resourceLedger?.ConsumeWork();
            resourceLedger?.ConsumeWork(11);
            var (firstFormal, formalCount) = typeDefs.GetGenericRange(typeDef);
            if (formalCount == 0)
            {
                continue;
            }

            var ownerDefId = 0x1300_0000u | (uint)typeDef;
            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                resourceLedger?.ConsumeWork();
                var formal = firstFormal + ordinal;
                resourceLedger?.ConsumeWork(sizeof(uint) + 1);
                if (genericParams.GetOwnerDefId(formal) != ownerDefId)
                {
                    throw InvalidGenericMetadata();
                }
                resourceLedger?.ConsumeWork(sizeof(uint) + 1);
                if (genericParams.GetOrdinal(formal) != (uint)ordinal)
                {
                    throw InvalidGenericMetadata();
                }
                resourceLedger?.ConsumeWork(2);
                if (genericParams.GetFormalKind(formal) != 1)
                {
                    throw UnsupportedGenericMetadata();
                }

                resourceLedger?.ConsumeWork(sizeof(uint) + 1);
                var defaultType = genericParams.GetDefaultArgumentRef(formal);
                if (defaultType != 0)
                {
                    ValidateEarlierFormalReferences(defaultType, ownerDefId,
                        (uint)ordinal, typeSpecs, genericParams,
                        resourceLedger);
                }

                resourceLedger?.ConsumeWork(
                    (2UL * (sizeof(uint) + 1)) + 1);
                var (firstConstraint, constraintCount)
                    = genericParams.GetConstraintRange(formal);
                for (var offset = 0; offset < constraintCount; offset++)
                {
                    resourceLedger?.ConsumeWork();
                    var constraint = firstConstraint + offset;
                    resourceLedger?.ConsumeWork(sizeof(uint) + 1);
                    if (constraints.GetOwnerGenericParam(constraint) != formal)
                    {
                        throw InvalidGenericMetadata();
                    }
                    resourceLedger?.ConsumeWork(sizeof(ushort) + 1);
                    if (constraints.GetKind(constraint) != 2)
                    {
                        throw UnsupportedGenericMetadata();
                    }
                    resourceLedger?.ConsumeWork(sizeof(uint) + 1);
                    if (constraints.GetPayload(constraint) != 3)
                    {
                        throw UnsupportedGenericMetadata();
                    }
                }
            }
        }

        for (var row = 1; row <= typeSpecs.Count; row++)
        {
            resourceLedger?.ConsumeWork();
            var bytes = typeSpecs.GetRow(row);
            resourceLedger?.ConsumeWork(2);
            if (bytes[0] != 0x05)
            {
                continue;
            }

            var typeDef = typeSpecs.GetNominalTypeDefRow((uint)row,
                typeDefs, imports);
            resourceLedger?.ConsumeWork(11);
            var (firstFormal, formalCount) = typeDefs.GetGenericRange(typeDef);
            var argumentCount = typeSpecs.GetConstructedArgumentCount(row);
            if (formalCount == 0 || argumentCount != formalCount)
            {
                throw InvalidGenericMetadata();
            }

            for (var ordinal = 0; ordinal < formalCount; ordinal++)
            {
                resourceLedger?.ConsumeWork();
                var formal = firstFormal + ordinal;
                resourceLedger?.ConsumeWork(sizeof(uint) + 1);
                var argument = typeSpecs.GetTypeSpecRef(
                    typeSpecs.GetConstructedArgument(row, ordinal));
                if (genericParams.GetFormalKind(formal) != 1
                    || argument.Form == 0x0F)
                {
                    throw UnsupportedGenericMetadata();
                }
            }
        }

        ValidateBodyTypeMaps(typeSpecs, typeDefs, imports, methods, bodies,
            contracts, features, modules, arrayLayouts, targetProfiles,
            resourceLedger);
    }

    private static void ValidateBodyTypeMaps(
        TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs,
        TrRmImportTableReader imports,
        TrRmMethodDefTableReader methods,
        TrRmBodyTableReader bodies,
        TrRmContractTableReader contracts,
        TrRmFeatureTableReader features,
        TrRmModuleTableReader modules,
        TrRmFixedArrayLayoutTableReader arrayLayouts,
        TrRmTargetProfileTableReader targetProfiles,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        for (var method = 1; method <= methods.Count; method++)
        {
            resourceLedger?.ConsumeWork();
            resourceLedger?.ConsumeWork(sizeof(uint) + 1);
            var bodyRow = methods.GetBodyRow(method);
            if (bodyRow == 0)
            {
                continue;
            }

            resourceLedger?.ConsumeWork();
            var body = bodies.GetRegisterBody(bodyRow, contracts.Count);
            for (var index = 0; index < body.TypeCount; index++)
            {
                resourceLedger?.ConsumeWork();
                resourceLedger?.ConsumeWork(sizeof(uint) + 1);
                var token = BinaryPrimitives.ReadUInt32LittleEndian(
                    body.TypeMap.Slice(index * sizeof(uint), sizeof(uint)));
                var row = checked((int)(token & 0x00FF_FFFF));
                switch (token >> 24)
                {
                    case 0x11:
                        var typeSpec = typeSpecs.GetTypeSpecRef((uint)row);
                        if (typeSpec.Form == 0x11)
                        {
                            resourceLedger?.ConsumeWork(sizeof(uint) + 1);
                            var ownerModule = methods.GetOwnerModule(method);
                            resourceLedger?.ConsumeWork();
                            if (!TrRmFeatureManifestValidator.Requires(
                                    features, modules, ownerModule,
                                    TrRmFeatureTableReader
                                        .FixedInlineArrayStorageFeatureId,
                                    typeSpecs.Schema))
                            {
                                throw InvalidGenericMetadata();
                            }

                            resourceLedger?.ConsumeWork();
                            if (!targetProfiles.TryGetProfile(
                                    TrRmTargetProfileTableReader
                                        .WindowsX64NativeProfileId,
                                    TrRmTargetProfileTableReader
                                        .WindowsX64NativeProfileRuleRevision,
                                    out var profileRow))
                            {
                                throw InvalidGenericMetadata();
                            }

                            resourceLedger?.ConsumeWork();
                            if (arrayLayouts.FindRow(ownerModule, (uint)row,
                                    (uint)profileRow) == 0)
                            {
                                throw InvalidGenericMetadata();
                            }
                        }
                        else if (ContainsA3GenericUse((uint)row, typeSpecs,
                                     typeDefs, imports, resourceLedger))
                        {
                            throw UnsupportedGenericMetadata();
                        }
                        break;

                    case 0x13:
                        if (row > 0 && row <= typeDefs.Count)
                        {
                            resourceLedger?.ConsumeWork(11);
                            if (typeDefs.GetGenericRange(row).Count != 0)
                            {
                                throw UnsupportedGenericMetadata();
                            }
                        }
                        break;

                    case 0x1C:
                        resourceLedger?.ConsumeWork(sizeof(uint) + 1);
                        var definition
                            = imports.GetExpectedDefinitionForImportRow(row);
                        var typeDefRow = checked((int)(definition
                            & 0x00FF_FFFF));
                        if ((definition >> 24) == 0x13
                            && typeDefRow > 0
                            && typeDefRow <= typeDefs.Count)
                        {
                            resourceLedger?.ConsumeWork(11);
                            if (typeDefs.GetGenericRange(typeDefRow).Count != 0)
                            {
                                throw UnsupportedGenericMetadata();
                            }
                        }
                        break;

                    case 0xF2:
                    case 0xF3:
                        if (ContainsA3GenericUse(token, typeSpecs,
                                typeDefs, imports, resourceLedger))
                        {
                            throw UnsupportedGenericMetadata();
                        }
                        break;
                }
            }
        }
    }

    private static bool ContainsA3GenericUse(uint typeSpecRef,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs,
        TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        var pending = new Stack<(uint Reference, bool AllowInitQualifier,
            ulong Depth)>();
        var visited = new Dictionary<(int Row, bool AllowInitQualifier),
            ulong>();
        resourceLedger?.ReserveWorkAtDepth(1, 1);
        pending.Push((typeSpecRef, false, 1));
        while (pending.TryPop(out var current))
        {
            resourceLedger?.ConsumeWork();
            resourceLedger?.CheckDepth(current.Depth);
            resourceLedger?.ConsumeWork();
            var reference = typeSpecs.GetTypeSpecRef(current.Reference,
                allowInitQualifier: current.AllowInitQualifier);
            if (!reference.IsRow)
            {
                if (reference.Form == 0x0F)
                {
                    return true;
                }

                continue;
            }

            var visitKey = (reference.Row, current.AllowInitQualifier);
            if (visited.TryGetValue(visitKey, out var previousDepth)
                && current.Depth <= previousDepth)
            {
                continue;
            }
            visited[visitKey] = current.Depth;

            if (reference.Form is 0x04 or 0x05)
            {
                return true;
            }

            resourceLedger?.ConsumeWork();
            var row = typeSpecs.GetRow(reference.Row);
            if (reference.Form is 0x02 or 0x03)
            {
                var definition = reference.Form == 0x02
                    ? 0x1300_0000u | Read(row, 2, resourceLedger)
                    : imports.GetExpectedDefinitionForImportRow(
                        checked((int)Read(row, 2, resourceLedger)));
                var typeDef = checked((int)(definition & 0x00FF_FFFF));
                if ((definition >> 24) == 0x13 && typeDef > 0
                    && typeDef <= typeDefs.Count
                    && typeDefs.GetGenericRange(typeDef).Count != 0)
                {
                    return true;
                }
            }

            switch (reference.Form)
            {
                case >= 0x06 and <= 0x0B:
                case 0x0E:
                case 0x11:
                    var unaryDepth = checked(current.Depth + 1);
                    resourceLedger?.ReserveWorkAtDepth(1, unaryDepth);
                    pending.Push((Read(row, 2, resourceLedger), false,
                        unaryDepth));
                    break;

                case 0x0C:
                    var indexedDepth = checked(current.Depth + 1);
                    resourceLedger?.ReserveWorkAtDepth(1, indexedDepth);
                    pending.Push((Read(row, 3, resourceLedger),
                        reference.TypeCode == 0x8A, indexedDepth));
                    break;
            }
        }

        return false;
    }

    private static void ValidateEarlierFormalReferences(uint typeSpecRef,
        uint ownerDefId, uint currentOrdinal,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        var pending = new Stack<(uint Reference, ulong Depth)>();
        var visited = new Dictionary<int, ulong>();
        resourceLedger?.ReserveWorkAtDepth(1, 1);
        pending.Push((typeSpecRef, 1));
        while (pending.TryPop(out var current))
        {
            resourceLedger?.ConsumeWork();
            resourceLedger?.CheckDepth(current.Depth);
            var reference = typeSpecs.GetTypeSpecRef(current.Reference);
            if (!reference.IsRow)
            {
                continue;
            }
            if (visited.TryGetValue(reference.Row, out var previousDepth)
                && current.Depth <= previousDepth)
            {
                continue;
            }
            visited[reference.Row] = current.Depth;

            resourceLedger?.ConsumeWork();
            var row = typeSpecs.GetRow(reference.Row);
            switch (reference.Form)
            {
                case 0x04:
                    var formal = checked((int)Read(row, 2, resourceLedger));
                    if (genericParams.GetOwnerDefId(formal) != ownerDefId
                        || genericParams.GetOrdinal(formal) >= currentOrdinal)
                    {
                        throw InvalidGenericMetadata();
                    }
                    break;

                case 0x05:
                    for (var ordinal = 0;
                        ordinal < typeSpecs.GetConstructedArgumentCount(
                            reference.Row);
                        ordinal++)
                    {
                        var childDepth = checked(current.Depth + 1);
                        resourceLedger?.ReserveWorkAtDepth(1, childDepth);
                        pending.Push((typeSpecs.GetConstructedArgument(
                            reference.Row, ordinal), childDepth));
                    }
                    break;

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                case 0x11:
                    var unaryDepth = checked(current.Depth + 1);
                    resourceLedger?.ReserveWorkAtDepth(1, unaryDepth);
                    pending.Push((Read(row, 2, resourceLedger), unaryDepth));
                    break;

                case 0x0C:
                    var indexedDepth = checked(current.Depth + 1);
                    resourceLedger?.ReserveWorkAtDepth(1, indexedDepth);
                    pending.Push((Read(row, 3, resourceLedger), indexedDepth));
                    break;

                case 0x0F:
                    throw UnsupportedGenericMetadata();
            }
        }
    }

    private static uint Read(ReadOnlySpan<byte> row, int offset,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            row.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidGenericMetadata()
        => new("Invalid A3 generic fixed-array metadata.");

    private static NotSupportedException UnsupportedGenericMetadata()
        => new("The selected A3 generic fixed-array profile does not support this metadata use.");
}
