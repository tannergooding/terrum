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
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, HashSet<int> visitedTypeSpecRows)
    {
        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (!reference.IsRow || !visitedTypeSpecRows.Add(reference.Row))
        {
            return;
        }

        var data = typeSpecs.GetRow(reference.Row);
        switch (data[0])
        {
            case 0x05:
                var genericTypeSpecRow = checked((int)Read(data, 2));
                var genericTypeSpec = typeSpecs.GetRow(genericTypeSpecRow);
                var definition = genericTypeSpec[0] switch
                {
                    0x02 => 0x1300_0000u | Read(genericTypeSpec, 2),
                    0x03 => imports.GetExpectedDefinitionForImportRow(
                        checked((int)Read(genericTypeSpec, 2))),
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
                                typeSpecs, typeDefs, genericParams, imports,
                                constantSources, frozenValueNodes, blobs, features,
                                modules, visitedTypeSpecRows);
                            break;
                        case 2:
                            ValidateFrozenScalar(checked((int)argument),
                                genericParams.GetConstantTypeSpecRef(formalRow),
                                ownerModule, constantSources, frozenValueNodes,
                                blobs, typeSpecs, features, modules);
                            break;
                        default:
                            throw InvalidConstructedTypeSpec();
                    }
                }
                break;

            case >= 0x06 and <= 0x0B:
            case 0x0E:
                ValidateConstructedTypeSpecValues(Read(data, 2), ownerModule,
                    typeSpecs, typeDefs, genericParams, imports, constantSources,
                    frozenValueNodes, blobs, features, modules, visitedTypeSpecRows);
                break;

            case 0x0C:
                var innerTypeSpecRef = Read(data, 3);
                var innerReference = typeSpecs.GetTypeSpecRef(innerTypeSpecRef,
                    allowInitQualifier: data[2] == 0x8A);
                if (innerReference.IsRow)
                {
                    ValidateConstructedTypeSpecValues(innerTypeSpecRef, ownerModule,
                        typeSpecs, typeDefs, genericParams, imports, constantSources,
                        frozenValueNodes, blobs, features, modules, visitedTypeSpecRows);
                }
                break;
        }
    }

    internal static BoundConstant MaterializeFrozenScalar(int constantSourceRow,
        uint expectedTypeSpecRef, int ownerModule, uint selectedTargetRequirement,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules)
    {
        ValidateFrozenScalar(constantSourceRow, expectedTypeSpecRef, ownerModule,
            constantSources, frozenValueNodes, blobs, typeSpecs, features, modules);

        var (moduleTargetRequirement, _) = modules.GetRequirements(ownerModule);
        if (selectedTargetRequirement != moduleTargetRequirement
            || selectedTargetRequirement != FixedLayoutStorage.WindowsX64TargetRequirement)
        {
            throw new NotSupportedException(
                "The selected target has no admitted frozen-scalar materializer.");
        }

        var typeCode = typeSpecs.GetTypeSpecRef(expectedTypeSpecRef).TypeCode;
        var type = typeCode switch
        {
            0x00 => PrimitiveType.Void,
            0x01 => PrimitiveType.Bool,
            0x03 => PrimitiveType.Char16,
            0x08 => PrimitiveType.Int32,
            0x09 => PrimitiveType.Int64,
            0x0D => PrimitiveType.UInt32,
            0x0E => PrimitiveType.UInt64,
            0x14 => PrimitiveType.Float32,
            0x15 => PrimitiveType.Float64,
            _ => throw new NotSupportedException(
                "The selected execution profile has no direct bound representation for this frozen scalar."),
        };
        var payload = frozenValueNodes.GetScalarPayload(
            checked((int)constantSources.GetPayloadReference(constantSourceRow)), blobs);
        var structures = Array.Empty<BoundStruct>();
        var size = ValueLayout.Size(type, structures);
        var alignment = ValueLayout.Alignment(type, structures);
        if (size != payload.Length || alignment != payload.Length)
        {
            throw new NotSupportedException(
                "The selected target's scalar layout does not match the frozen representation.");
        }

        var value = typeCode switch
        {
            0x00 => 0,
            0x01 => payload[0],
            0x03 => BinaryPrimitives.ReadUInt16LittleEndian(payload),
            0x08 => BinaryPrimitives.ReadInt32LittleEndian(payload),
            0x09 => BinaryPrimitives.ReadInt64LittleEndian(payload),
            0x0D => BinaryPrimitives.ReadUInt32LittleEndian(payload),
            0x0E or 0x15 => unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(payload)),
            0x14 => BinaryPrimitives.ReadUInt32LittleEndian(payload),
            _ => throw new InvalidOperationException("The materialized scalar type is not mapped."),
        };
        return new BoundConstant(value, type, default);
    }

    internal static void ValidateFrozenScalar(int constantSourceRow,
        uint expectedTypeSpecRef, int ownerModule,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules)
    {
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
        var nodeType = typeSpecs.GetTypeSpecRef(
            frozenValueNodes.GetTypeSpecRef(checked((int)nodeRow)));
        if (mapType.Form != 0x01 || mapType.Qualifiers != 0
            || nodeType.Form != 0x01 || nodeType.Qualifiers != 0
            || !TrRmFrozenValueNodeTableReader.IsRegisteredScalarTypeCode(mapType.TypeCode)
            || !TrRmFrozenValueNodeTableReader.IsRegisteredScalarTypeCode(nodeType.TypeCode))
        {
            throw new System.NotSupportedException(
                "This selected use requires an unqualified registered scalar type.");
        }
        if (mapType.TypeCode != nodeType.TypeCode
            || mapType.ArgumentCode != nodeType.ArgumentCode)
        {
            throw new InvalidDataException("ConstantMap type does not match its frozen value.");
        }

        frozenValueNodes.ValidateScalarValue(checked((int)nodeRow), blobs);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidConstructedTypeSpec()
        => new("Invalid TRRM constructed TypeSpec value binding.");
}
