// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

public readonly record struct TrRmContractRow(uint OwnerDefId, ushort Kind, ReadOnlyMemory<byte> Payload);

/// <summary>Emits assigned clause shapes without establishing their executable promises.</summary>
public static class TrRmContractTableWriter
{
    public static byte[] Encode(IReadOnlyList<TrRmContractRow> rows,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs, bool developmentProfile = false)
        => EncodeCore(rows, types, methods, signatures, parameters, typeSpecs,
            fieldPathCount: 0, predicateGraphCount: 0, default, default,
            developmentProfile ? TrRmArtifactSchema.Development
                : TrRmArtifactSchema.Standard, profileAware: false);

    internal static byte[] EncodeCopyOriginDraft(IReadOnlyList<TrRmContractRow> rows,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs, TrRmFieldPathTableReader fieldPaths,
        int fieldPathCount,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        return EncodeCore(CanonicalizeCopyOriginRows(rows, fieldPaths),
            types, methods, signatures, parameters, typeSpecs,
            fieldPathCount, predicateGraphCount: 0, features, modules,
            TrRmArtifactSchema.CopyOriginDraft, profileAware: true);
    }

    internal static byte[] EncodeWithPredicateGraphs(
        IReadOnlyList<TrRmContractRow> rows, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        int predicateGraphCount)
        => EncodeCore(rows, types, methods, signatures, parameters, typeSpecs,
            fieldPathCount: 0, predicateGraphCount, features, modules,
            TrRmArtifactSchema.Standard, profileAware: true);

    internal static byte[] EncodeForeignInvocationDraft(
        IReadOnlyList<TrRmContractRow> rows, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        int predicateGraphCount,
        TrRmArtifactSchema schema = TrRmArtifactSchema.ForeignInvocationDraft,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (schema is not (TrRmArtifactSchema.ForeignInvocationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
            or TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft))
        {
            throw new ArgumentOutOfRangeException(nameof(schema));
        }

        return EncodeCore(rows, types, methods, signatures, parameters, typeSpecs,
            fieldPathCount: 0, predicateGraphCount, features, modules,
            schema, profileAware: true, resourceLedger: resourceLedger);
    }

    internal static byte[] EncodeGeneralPredicateGraphsDraft(
        IReadOnlyList<TrRmContractRow> rows, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs,
        int fieldPathCount, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, int predicateGraphCount,
        TrRmEagerGroupTableReader eagerGroups = default,
        TrRmStaticFieldTableReader staticFields = default,
        TrRmStaticFieldContractTableReader staticFieldContracts = default,
        TrRmEagerInitializerTableReader eagerInitializers = default,
        TrRmLazyUnitTableReader lazyUnits = default)
        => EncodeCore(CanonicalizeEvaluationFormRows(rows), types, methods,
            signatures, parameters, typeSpecs, fieldPathCount,
            predicateGraphCount, features, modules,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft, profileAware: true,
            eagerGroups: eagerGroups, staticFields: staticFields,
            staticFieldContracts: staticFieldContracts,
            eagerInitializers: eagerInitializers, lazyUnits: lazyUnits);

    internal static byte[] EncodeDevelopmentFunctionPointerEffectsDraft(
        IReadOnlyList<TrRmContractRow> rows, TrRmTypeDefTableReader types,
        TrRmMethodDefTableReader methods, TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters, TrRmTypeSpecTableReader typeSpecs)
        => EncodeCore(rows, types, methods, signatures, parameters, typeSpecs,
            fieldPathCount: 0, predicateGraphCount: 0, default, default,
            TrRmArtifactSchema.DevelopmentFunctionPointerEffectsDraft,
            profileAware: false);

    private static TrRmContractRow[] CanonicalizeCopyOriginRows(
        IReadOnlyList<TrRmContractRow> rows,
        TrRmFieldPathTableReader fieldPaths)
    {
        var result = new TrRmContractRow[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            result[index] = row.Kind == 0x0D
                ? row with
                {
                    Payload = CanonicalizeCopyOriginPayload(
                        row.Payload, fieldPaths),
                }
                : row;
        }

        return result;
    }

    private static TrRmContractRow[] CanonicalizeEvaluationFormRows(
        IReadOnlyList<TrRmContractRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var result = new TrRmContractRow[rows.Count];
        var evaluationIndexes = new List<int>();
        var evaluationRows = new List<TrRmContractRow>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            result[index] = row;
            if (row.Kind == 0x0A)
            {
                if (row.Payload.Length != 4 || row.Payload.Span[0] is not (1 or 2))
                {
                    throw InvalidContract();
                }

                evaluationIndexes.Add(index);
                evaluationRows.Add(row);
            }
        }

        evaluationRows.Sort(static (left, right) =>
        {
            var ownerOrder = left.OwnerDefId.CompareTo(right.OwnerDefId);
            return ownerOrder != 0 ? ownerOrder
                : left.Payload.Span[0].CompareTo(right.Payload.Span[0]);
        });
        for (var index = 0; index < evaluationIndexes.Count; index++)
        {
            result[evaluationIndexes[index]] = evaluationRows[index];
        }

        return result;
    }

    private static byte[] CanonicalizeCopyOriginPayload(
        ReadOnlyMemory<byte> payload, TrRmFieldPathTableReader fieldPaths)
    {
        var data = payload.Span;
        if (data.Length < 12)
        {
            throw InvalidCopyOrigin();
        }

        var factCount = Read(data, 8);
        if (factCount == 0 || factCount > (uint)((data.Length - 12) / 8))
        {
            throw InvalidCopyOrigin();
        }

        var facts = new CopyOriginFact[checked((int)factCount)];
        var pathSequences = new Dictionary<uint, uint[]>();
        var offset = 12;
        for (var fact = 0; fact < facts.Length; fact++)
        {
            if (offset > data.Length - 8)
            {
                throw InvalidCopyOrigin();
            }

            var destinationPath = Read(data, offset);
            var originCount = Read(data, offset + 4);
            var remainingBytes = data.Length - offset - 8;
            if (originCount == 0 || originCount > (uint)(remainingBytes / 12))
            {
                throw InvalidCopyOrigin();
            }

            var origins = new CopyOriginOrigin[checked((int)originCount)];
            offset += 8;
            for (var origin = 0; origin < origins.Length; origin++)
            {
                var sourcePath = Read(data, offset + 8);
                origins[origin] = new CopyOriginOrigin(data[offset],
                    data[offset + 1], Read(data, offset + 4), sourcePath,
                    GetPathSequence(sourcePath, fieldPaths, pathSequences), offset);
                offset += 12;
            }

            Array.Sort(origins, (left, right) =>
            {
                var comparison = left.SourceKind.CompareTo(right.SourceKind);
                if (comparison == 0)
                {
                    comparison = left.SourceOrdinal.CompareTo(
                        right.SourceOrdinal);
                }
                if (comparison == 0)
                {
                    comparison = left.SourceView.CompareTo(right.SourceView);
                }

                return comparison != 0 ? comparison
                    : CompareFieldPathSequences(left.SourcePathReferences,
                        right.SourcePathReferences);
            });
            facts[fact] = new CopyOriginFact(destinationPath,
                GetPathSequence(destinationPath, fieldPaths, pathSequences),
                origins);
        }

        if (offset != data.Length)
        {
            throw InvalidCopyOrigin();
        }

        Array.Sort(facts, (left, right) =>
            CompareFieldPathSequences(left.DestinationPathReferences,
                right.DestinationPathReferences));

        var result = new byte[data.Length];
        data[..12].CopyTo(result.AsSpan());
        offset = 12;
        foreach (var fact in facts)
        {
            Write(result, offset, fact.DestinationPath);
            Write(result, offset + 4, (uint)fact.Origins.Length);
            offset += 8;
            foreach (var origin in fact.Origins)
            {
                data.Slice(origin.Offset, 12).CopyTo(result.AsSpan(offset));
                offset += 12;
            }
        }

        return result;
    }

    private static uint[] GetPathSequence(uint pathRow,
        TrRmFieldPathTableReader fieldPaths,
        Dictionary<uint, uint[]> pathSequences)
    {
        if (pathSequences.TryGetValue(pathRow, out var fields))
        {
            return fields;
        }

        var count = pathRow == 0 ? 0
            : fieldPaths.GetFieldCount(checked((int)pathRow));
        fields = new uint[count];
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            fields[ordinal] = fieldPaths.GetFieldReference(
                checked((int)pathRow), ordinal);
        }

        pathSequences.Add(pathRow, fields);
        return fields;
    }

    private static int CompareFieldPathSequences(uint[] left, uint[] right)
    {
        var commonCount = Math.Min(left.Length, right.Length);
        for (var ordinal = 0; ordinal < commonCount; ordinal++)
        {
            var comparison = left[ordinal].CompareTo(right[ordinal]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(offset, sizeof(uint)));

    private static void Write(byte[] data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

    private static InvalidDataException InvalidCopyOrigin()
        => new("Invalid TRRM CopyOrigin contract.");

    private static InvalidDataException InvalidContract()
        => new("Invalid TRRM Contract table.");

    private readonly record struct CopyOriginFact(
        uint DestinationPath, uint[] DestinationPathReferences,
        CopyOriginOrigin[] Origins);

    private readonly record struct CopyOriginOrigin(
        byte SourceKind, byte SourceView, uint SourceOrdinal,
        uint SourcePath, uint[] SourcePathReferences, int Offset);

    private static byte[] EncodeCore(IReadOnlyList<TrRmContractRow> rows,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs, int fieldPathCount,
        int predicateGraphCount,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema, bool profileAware,
        TrRmEagerGroupTableReader eagerGroups = default,
        TrRmStaticFieldTableReader staticFields = default,
        TrRmStaticFieldContractTableReader staticFieldContracts = default,
        TrRmEagerInitializerTableReader eagerInitializers = default,
        TrRmLazyUnitTableReader lazyUnits = default,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var rowCount = rows.Count;
        if (rowCount is <= 0 or > 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        resourceLedger?.ConsumeWork(3);
        var sectionLength = ((long)rowCount + 1) * sizeof(uint);
        resourceLedger?.ConsumeWork(checked((ulong)rowCount));
        var validatedRows = new TrRmContractRow[rowCount];
        for (var index = 0; index < rowCount; index++)
        {
            resourceLedger?.ConsumeWork();
            var row = rows[index];
            resourceLedger?.ConsumeWork(2);
            sectionLength += 8L + row.Payload.Length;
            resourceLedger?.ConsumeWork();
            if (sectionLength > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(rows));
            }

            validatedRows[index] = row;
        }

        resourceLedger?.ConsumeWork(checked((ulong)sectionLength + 1UL
            + ((ulong)rowCount * 3UL) + (ulong)rowCount + 1UL));
        var result = new byte[(int)sectionLength];
        var indexLength = (rowCount + 1) * sizeof(uint);
        var dataOffset = indexLength;
        for (var index = 0; index < validatedRows.Length; index++)
        {
            resourceLedger?.ConsumeWork();
            var row = validatedRows[index];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * sizeof(uint)),
                (uint)(dataOffset - indexLength));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(dataOffset), row.OwnerDefId);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(dataOffset + 4), row.Kind);
            row.Payload.Span.CopyTo(result.AsSpan(dataOffset + 8));
            dataOffset += 8 + row.Payload.Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(rowCount * sizeof(uint)),
            (uint)(dataOffset - indexLength));
        if (profileAware)
        {
            _ = new TrRmContractTableReader(result, rowCount, types, methods,
                signatures, parameters, typeSpecs, fieldPathCount, features,
                modules, schema, predicateGraphCount, eagerGroups,
                staticFields, staticFieldContracts, eagerInitializers, lazyUnits,
                resourceLedger);
        }
        else
        {
            _ = new TrRmContractTableReader(result, rowCount,
                types, methods, signatures, parameters, typeSpecs,
                schema);
        }
        return result;
    }
}
