// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmConstantSourceValueValidator
{
    internal static void ValidateConstructedTypeSpecValues(uint typeSpecRef,
        int ownerModule, TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs,
        TrRmEnumDefTableReader enumDefs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, HashSet<int> visitedTypeSpecRows,
        ICollection<TrRmConstantExecutionUse>? constantExecutionUses = null,
        TrRmA3LayoutResourceLedger? resourceLedger = null,
        ulong depth = 1,
        TrRmFieldDefTableReader fields = default,
        TrRmTransparentConstantDomainTableReader transparentConstantDomains = default,
        bool allowInitQualifier = false)
    {
        resourceLedger?.ReserveWorkAtDepth(1, depth);
        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef,
            allowInitQualifier: allowInitQualifier);
        if (!reference.IsRow)
        {
            return;
        }

        resourceLedger?.ConsumeWork(2);
        if (!visitedTypeSpecRows.Add(reference.Row))
        {
            return;
        }

        var data = typeSpecs.GetRow(reference.Row);
        switch (data[0])
        {
            case 0x05:
                var genericTypeSpecRow = checked((int)Read(data, 2,
                    resourceLedger));
                var genericTypeSpec = typeSpecs.GetRow(genericTypeSpecRow);
                var definition = genericTypeSpec[0] switch
                {
                    0x02 => 0x1300_0000u | Read(genericTypeSpec, 2,
                        resourceLedger),
                    0x03 => imports.GetExpectedDefinitionForImportRow(
                        checked((int)Read(genericTypeSpec, 2,
                            resourceLedger))),
                    _ => throw InvalidConstructedTypeSpec(),
                };
                var typeDefRow = checked((int)(definition & 0x00FF_FFFF));
                if ((definition >> 24) != 0x13
                    || typeDefRow <= 0 || typeDefRow > typeDefs.Count)
                {
                    throw InvalidConstructedTypeSpec();
                }

                var (formalFirst, formalCount) = typeDefs.GetGenericRange(typeDefRow);
                var argumentCount = typeSpecs.GetConstructedArgumentCount(reference.Row);
                if (argumentCount != formalCount)
                {
                    throw InvalidConstructedTypeSpec();
                }

                for (var ordinal = 0; ordinal < argumentCount; ordinal++)
                {
                    resourceLedger?.ConsumeWork();
                    var formalRow = formalFirst + ordinal;
                    if (genericParams.GetOwnerDefId(formalRow) != definition
                        || genericParams.GetOrdinal(formalRow) != (uint)ordinal)
                    {
                        throw InvalidConstructedTypeSpec();
                    }

                    var argument = typeSpecs.GetConstructedArgument(
                        reference.Row, ordinal);
                    switch (genericParams.GetFormalKind(formalRow))
                    {
                        case 1:
                            ValidateConstructedTypeSpecValues(argument,
                                ownerModule, typeSpecs, typeDefs, enumDefs,
                                genericParams, imports, constantSources,
                                frozenValueNodes, blobs, features, modules,
                                visitedTypeSpecRows, constantExecutionUses,
                                resourceLedger, checked(depth + 1), fields,
                                transparentConstantDomains);
                            break;

                        case 2:
                            var sourceRow = checked((int)argument);
                            if (typeSpecs.Schema.UsesGenericConstantMethods()
                                && constantSources.GetKind(sourceRow) is 2 or 3)
                            {
                                throw InvalidConstructedTypeSpec();
                            }
                            if (constantSources.GetKind(sourceRow) == 2
                                && constantExecutionUses is not null)
                            {
                                resourceLedger?.ConsumeWork();
                                constantExecutionUses.Add(
                                    new TrRmConstantExecutionUse(
                                        checked((int)constantSources
                                            .GetPayloadReference(sourceRow)),
                                        ownerModule,
                                        genericParams.GetConstantTypeSpecRef(
                                            formalRow)));
                            }
                            else if (constantSources.GetKind(sourceRow) == 1)
                            {
                                var valueNode = checked((int)
                                    constantSources.GetPayloadReference(
                                        sourceRow));
                                var valueTypeSpecRef =
                                    frozenValueNodes.GetTypeSpecRef(valueNode);
                                if (!AreTypeSpecsEquivalentAfterTypeArgumentSubstitution(
                                        genericParams.GetConstantTypeSpecRef(
                                            formalRow),
                                        valueTypeSpecRef, typeDefRow,
                                        reference.Row, typeSpecs, typeDefs,
                                        genericParams, imports,
                                        resourceLedger, checked(depth + 1)))
                                {
                                    throw InvalidConstructedTypeSpec();
                                }

                                ValidateFrozenScalar(sourceRow,
                                    valueTypeSpecRef, ownerModule,
                                    constantSources, frozenValueNodes, blobs,
                                    typeSpecs, features, modules, typeDefs,
                                    enumDefs, imports, resourceLedger, fields,
                                    transparentConstantDomains);
                            }
                            else
                            {
                                ValidateFrozenScalar(sourceRow,
                                    genericParams.GetConstantTypeSpecRef(
                                        formalRow),
                                    ownerModule, constantSources,
                                    frozenValueNodes, blobs, typeSpecs,
                                    features, modules, typeDefs, enumDefs,
                                    imports, resourceLedger, fields,
                                    transparentConstantDomains);
                            }
                            break;

                        default:
                            throw InvalidConstructedTypeSpec();
                    }
                }
                break;

            case >= 0x06 and <= 0x0B:
            case 0x0E:
                ValidateConstructedTypeSpecValues(Read(data, 2, resourceLedger),
                    ownerModule, typeSpecs, typeDefs, enumDefs, genericParams,
                    imports, constantSources, frozenValueNodes, blobs,
                    features, modules, visitedTypeSpecRows,
                    constantExecutionUses, resourceLedger,
                    checked(depth + 1), fields, transparentConstantDomains);
                break;

            case 0x0C:
                var innerTypeSpecRef = Read(data, 3, resourceLedger);
                var innerReference = typeSpecs.GetTypeSpecRef(innerTypeSpecRef,
                    allowInitQualifier: data[2] == 0x8A);
                if (innerReference.IsRow)
                {
                    ValidateConstructedTypeSpecValues(innerTypeSpecRef,
                        ownerModule, typeSpecs, typeDefs, enumDefs,
                        genericParams, imports, constantSources,
                        frozenValueNodes, blobs, features, modules,
                        visitedTypeSpecRows, constantExecutionUses,
                        resourceLedger, checked(depth + 1), fields,
                        transparentConstantDomains,
                        allowInitQualifier: data[2] == 0x8A);
                }
                break;

            case 0x11:
                if (!TrRmFeatureManifestValidator.Requires(features, modules,
                        ownerModule,
                        TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
                        typeSpecs.Schema))
                {
                    throw InvalidConstructedTypeSpec();
                }

                ValidateConstructedTypeSpecValues(
                    Read(data, 2, resourceLedger), ownerModule, typeSpecs,
                    typeDefs, enumDefs, genericParams, imports,
                    constantSources, frozenValueNodes, blobs, features,
                    modules, visitedTypeSpecRows, constantExecutionUses,
                    resourceLedger, checked(depth + 1), fields,
                    transparentConstantDomains);
                break;
        }
    }

    private static bool AreTypeSpecsEquivalentAfterTypeArgumentSubstitution(
        uint expectedTypeSpecRef, uint actualTypeSpecRef,
        int ownerTypeDefRow, int constructedTypeSpecRow,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger, ulong depth)
    {
        resourceLedger?.ReserveWorkAtDepth(1, depth);
        if (depth > SemanticVerifier.MaximumDepth)
        {
            return false;
        }

        var expected = typeSpecs.GetTypeSpecRef(expectedTypeSpecRef);
        var actual = typeSpecs.GetTypeSpecRef(actualTypeSpecRef);
        if (expected.Form == 0x04 && expected.IsRow
            && expected.Qualifiers == 0)
        {
            var genericParamRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    typeSpecs.GetRow(expected.Row)[2..]));
            var (firstFormal, formalCount)
                = typeDefs.GetGenericRange(ownerTypeDefRow);
            if (genericParamRow < firstFormal
                || genericParamRow >= firstFormal + formalCount
                || genericParams.GetOwnerDefId(genericParamRow)
                    != (0x1300_0000u | (uint)ownerTypeDefRow)
                || genericParams.GetFormalKind(genericParamRow) != 1)
            {
                return typeSpecs.AreEquivalentTypeSpecRefs(
                    expectedTypeSpecRef, actualTypeSpecRef);
            }

            var formalPosition = genericParamRow - firstFormal;
            if (formalPosition
                >= typeSpecs.GetConstructedArgumentCount(
                    constructedTypeSpecRow))
            {
                return false;
            }

            return typeSpecs.AreEquivalentTypeSpecRefs(
                typeSpecs.GetConstructedArgument(constructedTypeSpecRow,
                    formalPosition),
                actualTypeSpecRef);
        }

        if (expected.Form != 0x05 || actual.Form != 0x05
            || !expected.IsRow || !actual.IsRow
            || expected.Qualifiers != actual.Qualifiers)
        {
            return typeSpecs.AreEquivalentTypeSpecRefs(
                expectedTypeSpecRef, actualTypeSpecRef);
        }

        var expectedRow = typeSpecs.GetRow(expected.Row);
        var actualRow = typeSpecs.GetRow(actual.Row);
        var expectedGenericTypeSpecRef
            = BinaryPrimitives.ReadUInt32LittleEndian(expectedRow[2..]);
        var actualGenericTypeSpecRef
            = BinaryPrimitives.ReadUInt32LittleEndian(actualRow[2..]);
        if (!typeSpecs.AreEquivalentTypeSpecRefs(
                expectedGenericTypeSpecRef, actualGenericTypeSpecRef))
        {
            return false;
        }

        var genericTypeDefRow = typeSpecs.GetNominalTypeDefRow(
            expectedGenericTypeSpecRef, typeDefs, imports);
        var (firstGenericFormal, genericFormalCount)
            = typeDefs.GetGenericRange(genericTypeDefRow);
        if (typeSpecs.GetConstructedArgumentCount(expected.Row)
                != genericFormalCount
            || typeSpecs.GetConstructedArgumentCount(actual.Row)
                != genericFormalCount)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < genericFormalCount; ordinal++)
        {
            resourceLedger?.ConsumeWork();
            var formalRow = firstGenericFormal + ordinal;
            var expectedArgument = typeSpecs.GetConstructedArgument(
                expected.Row, ordinal);
            var actualArgument = typeSpecs.GetConstructedArgument(
                actual.Row, ordinal);
            if (genericParams.GetFormalKind(formalRow) == 1)
            {
                if (!AreTypeSpecsEquivalentAfterTypeArgumentSubstitution(
                    expectedArgument, actualArgument, ownerTypeDefRow,
                    constructedTypeSpecRow, typeSpecs, typeDefs,
                    genericParams, imports, resourceLedger,
                    checked(depth + 1)))
                {
                    return false;
                }
            }
            else if (expectedArgument != actualArgument)
            {
                return false;
            }
        }

        return true;
    }

    internal static BoundConstant MaterializeFrozenScalar(int constantSourceRow,
        uint expectedTypeSpecRef, int ownerModule, uint selectedTargetRequirement,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
        => MaterializeFrozenScalar(constantSourceRow, expectedTypeSpecRef,
            ownerModule, selectedTargetRequirement, constantSources,
            frozenValueNodes, blobs, typeSpecs, features, modules,
            default, default, default, resourceLedger);

    internal static BoundConstant MaterializeFrozenScalar(int constantSourceRow,
        uint expectedTypeSpecRef, int ownerModule, uint selectedTargetRequirement,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader typeDefs, TrRmEnumDefTableReader enumDefs,
        TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger = null,
        TrRmFieldDefTableReader fields = default,
        TrRmTransparentConstantDomainTableReader transparentConstantDomains = default,
        IReadOnlyList<BoundStruct>? structures = null)
    {
        ValidateFrozenScalar(constantSourceRow, expectedTypeSpecRef, ownerModule,
            constantSources, frozenValueNodes, blobs, typeSpecs, features, modules,
            typeDefs, enumDefs, imports, resourceLedger, fields,
            transparentConstantDomains);
        resourceLedger?.ConsumeWork();

        var (moduleTargetRequirement, _) = modules.GetRequirements(ownerModule);
        if (selectedTargetRequirement != moduleTargetRequirement
            || selectedTargetRequirement != FixedLayoutStorage.WindowsX64TargetRequirement)
        {
            throw new NotSupportedException(
                "The selected target has no admitted frozen-scalar materializer.");
        }

        var typeReference = typeSpecs.GetTypeSpecRef(expectedTypeSpecRef);
        PrimitiveType storageType;
        ReadOnlySpan<byte> payload;
        var isEnum = false;
        var isTransparentWrapper = false;
        var boundStructures = structures ?? Array.Empty<BoundStruct>();
        var nodeRow = checked((int)constantSources.GetPayloadReference(
            constantSourceRow));
        if (typeReference.Form == 0x01
            && TryGetBoundScalarType(typeReference.TypeCode, out var type))
        {
            storageType = type;
            payload = frozenValueNodes.GetScalarPayload(nodeRow, blobs);
        }
        else if (typeReference.IsRow
            && typeReference.Form is 0x02 or 0x03
            && typeReference.Qualifiers == 0)
        {
            var typeDefRow = typeSpecs.GetNominalTypeDefRow(
                expectedTypeSpecRef, typeDefs, imports);
            if (typeDefs.GetKind(typeDefRow) == 4)
            {
                var enumRow = enumDefs.GetRowForTypeDef(typeDefRow);
                var enumOrdinal = 0;
                for (var row = 1; row < typeDefRow; row++)
                {
                    resourceLedger?.ConsumeWork();
                    if (typeDefs.GetKind(row) == 4)
                    {
                        enumOrdinal++;
                    }
                }

                if (enumOrdinal >= BoundEnumTypes.MaximumCount)
                {
                    throw new NotSupportedException(
                        "The selected enum table exceeds the bound enum type domain.");
                }

                var underlying = typeSpecs.GetTypeSpecRef(
                    enumDefs.GetUnderlyingTypeSpecRef(enumRow));
                if (typeSpecs.Schema.SupportsNominalEnums())
                {
                    storageType = PrimitiveType.Int32;
                }
                else if (typeSpecs.Schema.UsesTypedEnumMemberValues()
                    && underlying.Form == 0x01
                    && underlying.Qualifiers == 0
                    && underlying.ArgumentCode == 0
                    && typeSpecs.Schema.SupportsEnumUnderlyingTypeCode(
                        underlying.TypeCode)
                    && TryGetBoundScalarType(underlying.TypeCode,
                        out var enumUnderlyingType)
                    && PrimitiveTypeFacts.IsIntegerOrWide(enumUnderlyingType))
                {
                    storageType = enumUnderlyingType;
                }
                else
                {
                    throw new NotSupportedException(
                        "The selected enum has no admitted integer carrier.");
                }

                type = BoundEnumTypes.Type(enumOrdinal);
                isEnum = true;
                payload = frozenValueNodes.GetNominalEnumScalarPayload(
                    nodeRow, blobs, expectedTypeSpecRef, typeDefRow, typeDefs,
                    enumDefs, imports);
            }
            else if (typeDefs.GetKind(typeDefRow) == 1
                && typeSpecs.Schema.UsesGenericConstantMethods()
                && transparentConstantDomains.TryGetForOwner(typeDefRow,
                    out var carrierTypeSpecRef, out var carrierFieldDefRow))
            {
                var carrierType = typeSpecs.GetTypeSpecRef(
                    carrierTypeSpecRef);
                if (carrierType.Form != 0x01
                    || carrierType.Qualifiers != 0
                    || carrierType.ArgumentCode != 0
                    || !TryGetBoundScalarType(carrierType.TypeCode,
                        out var carrierPrimitiveType)
                    || !(PrimitiveTypeFacts.IsIntegerOrWide(
                            carrierPrimitiveType)
                        || carrierPrimitiveType == PrimitiveType.Bool))
                {
                    throw new InvalidDataException(
                        "A transparent wrapper has no admitted primitive carrier.");
                }

                var definitionId = 0x1300_0000u | (uint)typeDefRow;
                var ownerIdentity = modules.GetIdentity(
                    typeDefs.GetOwnerModule(typeDefRow));
                var structureIndex = -1;
                for (var index = 0; index < boundStructures.Count; index++)
                {
                    var origin = boundStructures[index].StandardTypeOrigin;
                    if (origin?.DefinitionId == definitionId
                        && origin.Provider.ModuleIdentity == ownerIdentity)
                    {
                        structureIndex = index;
                        break;
                    }
                }

                if (structureIndex < 0
                    || boundStructures[structureIndex] is not BoundStruct {
                        IsTransparent: true,
                        IsUnique: false,
                        BlittableTargetRequirement:
                            FixedLayoutStorage.WindowsX64TargetRequirement,
                        Fields: [{ } carrierField],
                    }
                    || carrierField.Type != carrierPrimitiveType
                    || !StringComparer.Ordinal.Equals(carrierField.Name,
                        fields.GetName(carrierFieldDefRow))
                    || !(PrimitiveTypeFacts.IsIntegerOrWide(
                            carrierField.Type)
                        || carrierField.Type == PrimitiveType.Bool))
                {
                    throw new NotSupportedException(
                        "The selected wrapper has no matching checked bound structure.");
                }

                storageType = carrierPrimitiveType;
                type = (PrimitiveType)(100 + structureIndex);
                isTransparentWrapper = true;
                payload = frozenValueNodes.GetTransparentWrapperScalarPayload(
                    nodeRow, blobs, expectedTypeSpecRef, typeDefRow,
                    typeDefs, fields, transparentConstantDomains, imports);
            }
            else
            {
                throw new NotSupportedException(
                    "The selected nominal type has no admitted enum or transparent-wrapper scalar representation.");
            }
        }
        else
        {
            throw new NotSupportedException(
                "The selected execution profile has no direct bound representation for this frozen scalar.");
        }

        var size = ValueLayout.Size(storageType, boundStructures);
        var alignment = ValueLayout.Alignment(storageType, boundStructures);
        if (size != (ulong)payload.Length || alignment != payload.Length)
        {
            throw new NotSupportedException(
                "The selected target's scalar layout does not match the frozen representation.");
        }

        resourceLedger?.ConsumeWork(checked((ulong)payload.Length));
        var bits = ScalarBits.FromPayload(storageType, payload);
        return isEnum || isTransparentWrapper
            ? new BoundConstant(bits, type, storageType, default)
            : new BoundConstant(bits, type, default);
    }

    internal static bool TryGetBoundScalarType(byte typeCode, out PrimitiveType type)
    {
        type = typeCode switch
        {
            0x00 => PrimitiveType.Void,
            0x01 => PrimitiveType.Bool,
            0x02 => PrimitiveType.Char8,
            0x03 => PrimitiveType.Char16,
            0x04 => PrimitiveType.Char32,
            0x05 => PrimitiveType.Rune,
            0x06 => PrimitiveType.Int8,
            0x07 => PrimitiveType.Int16,
            0x08 => PrimitiveType.Int32,
            0x09 => PrimitiveType.Int64,
            0x0A => PrimitiveType.Int128,
            0x0B => PrimitiveType.UInt8,
            0x0C => PrimitiveType.UInt16,
            0x0D => PrimitiveType.UInt32,
            0x0E => PrimitiveType.UInt64,
            0x0F => PrimitiveType.UInt128,
            0x10 => PrimitiveType.NativeInt,
            0x11 => PrimitiveType.NativeUInt,
            0x12 => PrimitiveType.Float16,
            0x13 => PrimitiveType.BFloat16,
            0x14 => PrimitiveType.Float32,
            0x15 => PrimitiveType.Float64,
            0x16 => PrimitiveType.Float128,
            0x17 => PrimitiveType.Decimal32,
            0x18 => PrimitiveType.Decimal64,
            0x19 => PrimitiveType.Decimal128,
            _ => PrimitiveType.Void,
        };
        return typeCode is 0x00 or 0x01 or 0x02 or 0x03 or 0x04 or 0x05
            or 0x06 or 0x07 or 0x08 or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x0E
            or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x14 or 0x15 or 0x16
            or 0x17 or 0x18 or 0x19;
    }

    internal static bool TryGetIntegerTypeCode(PrimitiveType type,
        out byte typeCode)
    {
        typeCode = type switch {
            PrimitiveType.Int8 => 0x06,
            PrimitiveType.Int16 => 0x07,
            PrimitiveType.Int32 => 0x08,
            PrimitiveType.Int64 => 0x09,
            PrimitiveType.Int128 => 0x0A,
            PrimitiveType.UInt8 => 0x0B,
            PrimitiveType.UInt16 => 0x0C,
            PrimitiveType.UInt32 => 0x0D,
            PrimitiveType.UInt64 => 0x0E,
            PrimitiveType.UInt128 => 0x0F,
            PrimitiveType.NativeInt => 0x10,
            PrimitiveType.NativeUInt => 0x11,
            _ => 0,
        };
        return PrimitiveTypeFacts.IsIntegerOrWide(type);
    }

    internal static void ValidateFrozenScalar(int constantSourceRow,
        uint expectedTypeSpecRef, int ownerModule,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
        => ValidateFrozenScalar(constantSourceRow, expectedTypeSpecRef,
            ownerModule, constantSources, frozenValueNodes, blobs, typeSpecs,
            features, modules, default, default, default, resourceLedger);

    internal static void ValidateFrozenScalar(int constantSourceRow,
        uint expectedTypeSpecRef, int ownerModule,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader typeDefs, TrRmEnumDefTableReader enumDefs,
        TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger = null,
        TrRmFieldDefTableReader fields = default,
        TrRmTransparentConstantDomainTableReader transparentConstantDomains = default)
        => _ = GetFrozenScalarPayload(constantSourceRow, expectedTypeSpecRef,
            ownerModule, constantSources, frozenValueNodes, blobs, typeSpecs,
            features, modules, typeDefs, enumDefs, imports, resourceLedger,
            fields, transparentConstantDomains);

    internal static ReadOnlySpan<byte> GetFrozenScalarPayload(
        int constantSourceRow, uint expectedTypeSpecRef, int ownerModule,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader typeDefs, TrRmEnumDefTableReader enumDefs,
        TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger = null,
        TrRmFieldDefTableReader fields = default,
        TrRmTransparentConstantDomainTableReader transparentConstantDomains = default)
    {
        resourceLedger?.ConsumeWork(3);
        var sourceKind = constantSources.GetKind(constantSourceRow);
        if (sourceKind != 1)
        {
            throw new System.NotSupportedException(
                "This selected use supports only frozen scalar ConstantSource rows.");
        }
        if (!TrRmFeatureManifestValidator.Requires(features, modules, ownerModule,
            TrRmFeatureTableReader.TypedFrozenValuesFeatureId))
        {
            throw new System.NotSupportedException(
                "A selected frozen scalar value requires TypedFrozenValues revision 1.");
        }

        var nodeRow = constantSources.GetPayloadReference(constantSourceRow);
        if (frozenValueNodes.GetKind(checked((int)nodeRow)) != 1)
        {
            throw new System.NotSupportedException(
                "This selected use supports only frozen scalar values.");
        }

        var mapType = typeSpecs.GetTypeSpecRef(expectedTypeSpecRef);
        var nodeTypeSpecRef = frozenValueNodes.GetTypeSpecRef(
            checked((int)nodeRow));
        var nodeType = typeSpecs.GetTypeSpecRef(nodeTypeSpecRef);
        resourceLedger?.ConsumeWork(2);
        var mapIsBuiltinScalar = mapType.Form == 0x01
            && mapType.Qualifiers == 0
            && TrRmFrozenValueNodeTableReader.IsRegisteredScalarTypeCode(
                mapType.TypeCode);
        var nodeIsBuiltinScalar = nodeType.Form == 0x01
            && nodeType.Qualifiers == 0
            && TrRmFrozenValueNodeTableReader.IsRegisteredScalarTypeCode(
                nodeType.TypeCode);
        if (mapIsBuiltinScalar != nodeIsBuiltinScalar)
        {
            throw new InvalidDataException(
                "ConstantMap type does not match its frozen value.");
        }

        if (mapIsBuiltinScalar)
        {
            resourceLedger?.ConsumeWork();
            if (mapType.TypeCode != nodeType.TypeCode
                || mapType.ArgumentCode != nodeType.ArgumentCode)
            {
                throw new InvalidDataException(
                    "ConstantMap type does not match its frozen value.");
            }

            return frozenValueNodes.GetScalarPayload(
                checked((int)nodeRow), blobs);
        }

        if (mapType.IsRow
            && mapType.Form is 0x02 or 0x03 or 0x05
            && mapType.Qualifiers == 0 && nodeType.IsRow
            && nodeType.Form is 0x02 or 0x03 or 0x05
            && nodeType.Qualifiers == 0)
        {
            resourceLedger?.ConsumeWork();
            if (!typeSpecs.AreEquivalentTypeSpecRefs(
                    expectedTypeSpecRef, nodeTypeSpecRef))
            {
                throw new InvalidDataException(
                    "ConstantMap type does not match its frozen value.");
            }

            var typeDefRow = typeSpecs.GetNominalTypeDefRow(
                expectedTypeSpecRef, typeDefs, imports);
            var typeKind = typeDefs.GetKind(typeDefRow);
            if (typeKind == 4)
            {
                return frozenValueNodes.GetNominalEnumScalarPayload(
                    checked((int)nodeRow), blobs, expectedTypeSpecRef,
                    typeDefRow, typeDefs, enumDefs, imports);
            }

            if (typeKind == 1
                && typeSpecs.Schema.UsesGenericConstantMethods())
            {
                if (!TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule,
                    TrRmFeatureTableReader.ConstantGenericTermsFeatureId,
                    typeSpecs.Schema))
                {
                    throw new InvalidDataException(
                        "A transparent wrapper value requires Feature 24/1.");
                }

                return frozenValueNodes.GetTransparentWrapperScalarPayload(
                    checked((int)nodeRow), blobs, expectedTypeSpecRef,
                    typeDefRow, typeDefs, fields,
                    transparentConstantDomains, imports);
            }

            throw new System.NotSupportedException(
                "This selected use supports only registered scalar, enum and CG1 transparent-wrapper values.");
        }

        throw new System.NotSupportedException(
            "This selected use requires an unqualified registered scalar, enum or CG1 transparent-wrapper type.");
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset,
        TrRmA3LayoutResourceLedger? resourceLedger)
    {
        resourceLedger?.ConsumeWork(sizeof(uint) + 1);
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));
    }

    private static InvalidDataException InvalidConstructedTypeSpec()
        => new("Invalid TRRM constructed TypeSpec value binding.");
}
