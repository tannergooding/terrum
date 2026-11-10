// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmGenericConstantFeatureUseValidator
{
    internal static void ValidateConstantSourceRows(
        TrRmConstantSourceTableReader constantSources,
        TrRmGenericParamTableReader genericParams,
        TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (!schema.UsesGenericConstantMethods())
        {
            return;
        }

        for (var row = 1; row <= constantSources.Count; row++)
        {
            resourceLedger?.ConsumeWork();
            if (constantSources.GetKind(row) == 4
                && genericParams.GetFormalKind(checked((int)
                    constantSources.GetPayloadReference(row))) != 2)
            {
                throw InvalidTypeSpec();
            }
        }
    }

    internal static void ValidateTypeSpec(uint typeSpecRef, int ownerModule,
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmGenericParamTableReader genericParams, TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema, HashSet<int> visitedRows)
    {
        if (!schema.UsesGenericConstantMethods() || typeSpecRef == 0)
        {
            return;
        }

        var root = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (!root.IsRow)
        {
            return;
        }

        var resourceLedger = typeSpecs.ResourceLedger;
        resourceLedger?.ReserveWorkAtDepth(1, 1);
        var pendingRows = new Stack<(int Row, ulong Depth)>();
        pendingRows.Push((root.Row, 1));
        while (pendingRows.TryPop(out var pendingRow))
        {
            resourceLedger?.ConsumeWork();
            resourceLedger?.CheckDepthFromCurrent(pendingRow.Depth);
            if (!visitedRows.Add(pendingRow.Row))
            {
                continue;
            }

            var data = typeSpecs.GetRow(pendingRow.Row);
            switch (data[0])
            {
                case 0x05:
                    var definition = typeSpecs.GetNominalTypeDefRow(
                        (uint)pendingRow.Row, typeDefs, imports);
                    var (formalFirst, formalCount)
                        = typeDefs.GetGenericRange(definition);
                    var argumentCount
                        = typeSpecs.GetConstructedArgumentCount(pendingRow.Row);
                    if (argumentCount != formalCount)
                    {
                        throw InvalidTypeSpec();
                    }

                    for (var ordinal = 0; ordinal < argumentCount; ordinal++)
                    {
                        resourceLedger?.ConsumeWork();
                        var formal = formalFirst + ordinal;
                        var actual = typeSpecs.GetConstructedArgument(
                            pendingRow.Row, ordinal);
                        switch (genericParams.GetFormalKind(formal))
                        {
                            case 1:
                                var argument = typeSpecs.GetTypeSpecRef(actual);
                                if (argument.IsRow)
                                {
                                    var childDepth
                                        = checked(pendingRow.Depth + 1);
                                    resourceLedger?.ReserveWorkAtDepth(
                                        1, childDepth);
                                    pendingRows.Push((argument.Row, childDepth));
                                }
                                break;
                            case 2:
                                if (actual is 0 or > 0xFF_FFFF)
                                {
                                    throw InvalidTypeSpec();
                                }

                                ValidateConstantSourceUse(
                                    checked((int)actual), ownerModule,
                                    constantSources, genericParams, features,
                                    modules, schema);
                                RequireConstantGenericFeature(features,
                                    modules, ownerModule, schema);
                                break;
                            default:
                                throw InvalidTypeSpec();
                        }
                    }
                    break;

                case >= 0x06 and <= 0x0B:
                case 0x0E:
                    QueueTypeSpec(Read(data, 2), pendingRow.Depth,
                        typeSpecs, resourceLedger, pendingRows);
                    break;

                case 0x0C:
                    QueueTypeSpec(Read(data, 3), pendingRow.Depth,
                        typeSpecs, resourceLedger, pendingRows);
                    break;

                case 0x11:
                    QueueTypeSpec(Read(data, 2), pendingRow.Depth,
                        typeSpecs, resourceLedger, pendingRows);
                    break;
            }
        }
    }

    internal static void ValidateConstantSourceUse(int constantSourceRow,
        int ownerModule, TrRmConstantSourceTableReader constantSources,
        TrRmGenericParamTableReader genericParams,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema)
    {
        if (!schema.UsesGenericConstantMethods())
        {
            return;
        }

        var kind = constantSources.GetKind(constantSourceRow);
        if (kind == 4)
        {
            var formal = checked((int)constantSources
                .GetPayloadReference(constantSourceRow));
            if (genericParams.GetFormalKind(formal) != 2)
            {
                throw InvalidTypeSpec();
            }
        }

        if (kind is 4 or 5)
        {
            RequireConstantGenericFeature(features, modules, ownerModule,
                schema);
        }
    }

    internal static void ValidateGenericCall(ReadOnlySpan<byte> payload,
        int ownerModule, TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema, HashSet<int> visitedTypeSpecRows)
    {
        if (!schema.UsesGenericConstantMethods())
        {
            return;
        }

        if (payload.Length < 16)
        {
            throw InvalidTypeSpec();
        }

        var ownerTypeSpec = Read(payload, 4);
        var argumentCount = Read(payload, 12);
        if (argumentCount > 0xFF_FFFF
            || (ulong)payload.Length
                != 16UL + ((ulong)argumentCount * 8UL))
        {
            throw InvalidTypeSpec();
        }

        if (ownerTypeSpec != 0)
        {
            ValidateTypeSpec(ownerTypeSpec, ownerModule, typeSpecs,
                typeDefs, genericParams, imports, constantSources, features,
                modules, schema, visitedTypeSpecRows);
        }

        for (var ordinal = 0; ordinal < argumentCount; ordinal++)
        {
            var offset = checked(16 + (checked((int)ordinal) * 8));
            var kind = payload[offset];
            var flags = payload[offset + 1];
            var reserved = BinaryPrimitives.ReadUInt16LittleEndian(
                payload.Slice(offset + 2, sizeof(ushort)));
            var reference = Read(payload, offset + 4);
            if (flags != 0 || reserved != 0)
            {
                throw InvalidTypeSpec();
            }

            if (kind == 1)
            {
                ValidateTypeSpec(reference, ownerModule, typeSpecs,
                    typeDefs, genericParams, imports, constantSources,
                    features, modules, schema, visitedTypeSpecRows);
            }
            else if (kind == 2)
            {
                if (reference is 0 or > 0xFF_FFFF)
                {
                    throw InvalidTypeSpec();
                }

                ValidateConstantSourceUse(checked((int)reference),
                    ownerModule, constantSources, genericParams, features,
                    modules, schema);
                RequireConstantGenericFeature(features, modules,
                    ownerModule, schema);
            }
            else
            {
                throw InvalidTypeSpec();
            }
        }
    }

    internal static void ValidateBody(TrRmRegisterBodyReader body,
        int ownerModule, TrRmTypeDefTableReader typeDefs,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmGenericParamTableReader genericParams,
        TrRmImportTableReader imports,
        TrRmConstantSourceTableReader constantSources,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        HashSet<int> visitedTypeSpecRows)
    {
        if (!body.Schema.UsesGenericConstantMethods())
        {
            return;
        }

        if (body.HasGenericCallTargets
            && !TrRmFeatureManifestValidator.Requires(features, modules,
                ownerModule,
                TrRmFeatureTableReader.GenericMethodCallsFeatureId,
                body.Schema))
        {
            throw InvalidTypeSpec();
        }

        for (var index = 0; index < body.TypeCount; index++)
        {
            body.ResourceLedger?.ConsumeWork(sizeof(uint) + 1);
            var token = Read(body.TypeMap, index * sizeof(uint));
            if ((token >> 24) == 0x11)
            {
                ValidateTypeSpec(token & 0x00FF_FFFF, ownerModule,
                    typeSpecs, typeDefs, genericParams, imports,
                    constantSources, features, modules, body.Schema,
                    visitedTypeSpecRows);
            }
        }

        for (var index = 0; index < body.ConstantCount; index++)
        {
            body.ResourceLedger?.ConsumeWork(1UL + (2UL * sizeof(uint)));
            var data = body.ConstantMap.Slice(index * 8, 8);
            var typeSpecRef = Read(data, 0);
            ValidateTypeSpec(typeSpecRef, ownerModule, typeSpecs,
                typeDefs, genericParams, imports, constantSources,
                features, modules, body.Schema, visitedTypeSpecRows);
            var sourceRow = Read(data, sizeof(uint));
            if (sourceRow is 0 or > 0xFF_FFFF)
            {
                throw InvalidTypeSpec();
            }

            ValidateConstantSourceUse(checked((int)sourceRow), ownerModule,
                constantSources, genericParams, features, modules,
                body.Schema);
        }

        for (var record = 0; record < body.RecordCount; record++)
        {
            body.ResourceLedger?.ConsumeWork();
            var data = body.GetRecord(record);
            if (data[0] == 0x10)
            {
                ValidateGenericCall(data[4..], ownerModule, typeSpecs,
                    typeDefs, genericParams, imports, constantSources,
                    features, modules, body.Schema, visitedTypeSpecRows);
            }
        }
    }

    internal static void RequireConstantGenericFeature(
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        int ownerModule, TrRmArtifactSchema schema)
    {
        if (!TrRmFeatureManifestValidator.Requires(features, modules,
            ownerModule, TrRmFeatureTableReader.ConstantGenericTermsFeatureId,
            schema))
        {
            throw InvalidTypeSpec();
        }
    }

    private static void QueueTypeSpec(uint typeSpecRef, ulong depth,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmA3LayoutResourceLedger? resourceLedger,
        Stack<(int Row, ulong Depth)> pendingRows)
    {
        var child = typeSpecs.GetTypeSpecRef(typeSpecRef);
        if (child.IsRow)
        {
            var childDepth = checked(depth + 1);
            resourceLedger?.ReserveWorkAtDepth(1, childDepth);
            pendingRows.Push((child.Row, childDepth));
        }
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));

    private static InvalidDataException InvalidTypeSpec()
        => new("Invalid TRRM generic-constant feature use.");
}
