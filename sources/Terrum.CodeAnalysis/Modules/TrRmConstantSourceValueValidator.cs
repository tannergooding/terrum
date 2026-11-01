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
        ulong depth = 1)
    {
        resourceLedger?.ReserveWorkAtDepth(1, depth);
        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef);
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
                            ValidateConstructedTypeSpecValues(argument, ownerModule,
                                typeSpecs, typeDefs, enumDefs, genericParams, imports,
                                constantSources, frozenValueNodes, blobs, features,
                                modules, visitedTypeSpecRows, constantExecutionUses,
                                resourceLedger, checked(depth + 1));
                            break;
                        case 2:
                            var sourceRow = checked((int)argument);
                            if (constantSources.GetKind(sourceRow) == 2
                                && constantExecutionUses is not null)
                            {
                                resourceLedger?.ConsumeWork();
                                constantExecutionUses.Add(
                                    new TrRmConstantExecutionUse(
                                        checked((int)constantSources
                                            .GetPayloadReference(sourceRow)),
                                        ownerModule,
                                        genericParams.GetConstantTypeSpecRef(formalRow)));
                            }
                            else
                            {
                                ValidateFrozenScalar(sourceRow,
                                    genericParams.GetConstantTypeSpecRef(formalRow),
                                    ownerModule, constantSources, frozenValueNodes,
                                    blobs, typeSpecs, features, modules, typeDefs,
                                    enumDefs, imports, resourceLedger);
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
                    ownerModule,
                    typeSpecs, typeDefs, enumDefs, genericParams, imports, constantSources,
                    frozenValueNodes, blobs, features, modules, visitedTypeSpecRows,
                    constantExecutionUses, resourceLedger,
                    checked(depth + 1));
                break;

            case 0x0C:
                var innerTypeSpecRef = Read(data, 3, resourceLedger);
                var innerReference = typeSpecs.GetTypeSpecRef(innerTypeSpecRef,
                    allowInitQualifier: data[2] == 0x8A);
                if (innerReference.IsRow)
                {
                    ValidateConstructedTypeSpecValues(innerTypeSpecRef, ownerModule,
                        typeSpecs, typeDefs, enumDefs, genericParams, imports, constantSources,
                        frozenValueNodes, blobs, features, modules, visitedTypeSpecRows,
                        constantExecutionUses, resourceLedger,
                        checked(depth + 1));
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

                ValidateConstructedTypeSpecValues(Read(data, 2, resourceLedger),
                    ownerModule,
                    typeSpecs, typeDefs, enumDefs, genericParams, imports,
                    constantSources, frozenValueNodes, blobs, features, modules,
                    visitedTypeSpecRows, constantExecutionUses, resourceLedger,
                    checked(depth + 1));
                break;
        }
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
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        ValidateFrozenScalar(constantSourceRow, expectedTypeSpecRef, ownerModule,
            constantSources, frozenValueNodes, blobs, typeSpecs, features, modules,
            typeDefs, enumDefs, imports, resourceLedger);
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
            _ = enumDefs.GetRowForTypeDef(typeDefRow);
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

            type = BoundEnumTypes.Type(enumOrdinal);
            storageType = PrimitiveType.Int32;
            payload = frozenValueNodes.GetNominalEnumScalarPayload(nodeRow,
                blobs, expectedTypeSpecRef, typeDefRow, typeDefs, enumDefs,
                imports);
        }
        else
        {
            throw new NotSupportedException(
                "The selected execution profile has no direct bound representation for this frozen scalar.");
        }

        var structures = Array.Empty<BoundStruct>();
        var size = ValueLayout.Size(type, structures);
        var alignment = ValueLayout.Alignment(type, structures);
        if (size != payload.Length || alignment != payload.Length)
        {
            throw new NotSupportedException(
                "The selected target's scalar layout does not match the frozen representation.");
        }

        resourceLedger?.ConsumeWork(checked((ulong)payload.Length));
        return new BoundConstant(ScalarBits.FromPayload(storageType, payload),
            type, default);
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
            or 0x0F or 0x12 or 0x13 or 0x14 or 0x15 or 0x16 or 0x17 or 0x18 or 0x19;
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
        TrRmA3LayoutResourceLedger? resourceLedger = null)
        => _ = GetFrozenScalarPayload(constantSourceRow, expectedTypeSpecRef,
            ownerModule, constantSources, frozenValueNodes, blobs, typeSpecs,
            features, modules, typeDefs, enumDefs, imports, resourceLedger);

    internal static ReadOnlySpan<byte> GetFrozenScalarPayload(
        int constantSourceRow, uint expectedTypeSpecRef, int ownerModule,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader typeDefs, TrRmEnumDefTableReader enumDefs,
        TrRmImportTableReader imports,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
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

        if (mapType.IsRow && mapType.Form is 0x02 or 0x03
            && mapType.Qualifiers == 0 && nodeType.IsRow
            && nodeType.Form is 0x02 or 0x03
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
            if (typeDefs.GetKind(typeDefRow) != 4)
            {
                throw new System.NotSupportedException(
                    "This selected use supports only registered scalar and nominal enum values.");
            }

            return frozenValueNodes.GetNominalEnumScalarPayload(
                checked((int)nodeRow), blobs, expectedTypeSpecRef,
                typeDefRow, typeDefs, enumDefs, imports);
        }

        throw new System.NotSupportedException(
            "This selected use requires an unqualified registered scalar or nominal enum type.");
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
