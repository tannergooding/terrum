// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmFrozenScalarValueReference(
    uint TypeSpecRef, uint ConstantSourceRow);

internal readonly record struct TrRmFrozenScalarValueNodeReference(
    uint TypeSpecRef, uint FrozenValueNodeRow);

internal readonly record struct TrRmFrozenScalarValueTables(
    byte[] BlobSection,
    int BlobCount,
    byte[] FrozenValueNodeSection,
    int FrozenValueNodeCount,
    byte[] ConstantSourceSection,
    int ConstantSourceCount);

internal sealed class TrRmFrozenScalarValueBuilder
{
    private readonly Dictionary<(PrimitiveType Type, ScalarBits Bits,
        uint TypeSpecRef, PrimitiveType StorageType),
        TrRmFrozenScalarValueReference> _values = [];
    private readonly Dictionary<(PrimitiveType Type, ScalarBits Bits,
        uint TypeSpecRef, PrimitiveType StorageType), uint> _nodeRows = [];
    private readonly Dictionary<BlobKey, uint> _blobReferences = [];
    private readonly List<ReadOnlyMemory<byte>> _blobs = [];
    private readonly List<TrRmFrozenValueNodeRow> _nodes = [];
    private readonly List<TrRmConstantSourceRow> _sources = [];

    internal int BlobCount => _blobs.Count;

    internal int FrozenValueNodeCount => _nodes.Count;

    internal int ConstantSourceCount => _sources.Count;

    internal TrRmFrozenScalarValueNodeReference AddFrozenValueNode(
        BoundConstant constant)
    {
        if (!TryGetTypeCode(constant.Type, out var typeCode))
        {
            throw new NotSupportedException(
                "The selected frozen-value profile supports only fixed-width built-in scalars.");
        }

        var typeSpecRef = 0xF200_0000u | typeCode;
        var nodeRow = GetOrAddNode(constant, typeSpecRef, constant.Type);
        return new TrRmFrozenScalarValueNodeReference(typeSpecRef, nodeRow);
    }

    internal TrRmFrozenScalarValueReference Add(BoundConstant constant)
    {
        if (!TryGetTypeCode(constant.Type, out var typeCode))
        {
            throw new NotSupportedException(
                "The selected frozen-value profile supports only fixed-width built-in scalars.");
        }

        return Add(constant, 0xF200_0000u | typeCode, constant.Type);
    }

    internal TrRmFrozenScalarValueReference AddNominalEnum(
        BoundConstant constant, uint typeSpecRef)
    {
        if (!BoundEnumTypes.IsType(constant.Type)
            || typeSpecRef is 0 or > 0x00FF_FFFF)
        {
            throw new NotSupportedException(
                "Nominal enum frozen values require a complete TypeSpec row reference.");
        }

        return Add(constant, typeSpecRef, PrimitiveType.Int32);
    }

    internal TrRmFrozenScalarValueReference AddGenericNominalEnum(
        BoundConstant constant, uint typeSpecRef,
        PrimitiveType underlyingType)
    {
        if (!BoundEnumTypes.IsType(constant.Type)
            || typeSpecRef is 0 or > 0x00FF_FFFF
            || !PrimitiveTypeFacts.IsIntegerOrWide(underlyingType)
            || (constant.RepresentationType != underlyingType
                && !(underlyingType == PrimitiveType.Int32
                    && constant.RepresentationType == constant.Type))
            || !constant.Bits.IsCanonicalFor(underlyingType))
        {
            throw new NotSupportedException(
                "CG1 enum frozen values require the exact enum domain and integer carrier.");
        }

        return Add(constant, typeSpecRef, underlyingType);
    }

    internal TrRmFrozenScalarValueReference AddC1EnumClosureMember(
        BoundConstant constant, uint typeSpecRef,
        PrimitiveType underlyingType, TrRmArtifactSchema schema)
    {
        if (!schema.UsesC1EnumClosure()
            || !BoundEnumTypes.IsType(constant.Type)
            || typeSpecRef is 0 or > 0x00FF_FFFF
            || !TrRmConstantSourceValueValidator.TryGetIntegerTypeCode(
                underlyingType, out var typeCode)
            || !schema.SupportsEnumUnderlyingTypeCode(typeCode)
            || (constant.RepresentationType != underlyingType
                && !(underlyingType == PrimitiveType.Int32
                    && constant.RepresentationType == constant.Type))
            || !constant.Bits.IsCanonicalFor(underlyingType))
        {
            throw new NotSupportedException(
                "EN1 enum frozen values require the exact enum domain, selected integer base and target binding.");
        }

        return Add(constant, typeSpecRef, underlyingType);
    }

    internal TrRmFrozenScalarValueReference AddGenericNominalWrapper(
        BoundConstant constant, uint typeSpecRef, BoundStruct wrapper)
    {
        ArgumentNullException.ThrowIfNull(wrapper);
        if (typeSpecRef is 0 or > 0x00FF_FFFF
            || wrapper.IsUnique || !wrapper.IsTransparent
            || wrapper.BlittableTargetRequirement
                != FixedLayoutStorage.WindowsX64TargetRequirement
            || wrapper.Fields is not [{ } field]
            || (!PrimitiveTypeFacts.IsIntegerOrWide(field.Type)
                && field.Type != PrimitiveType.Bool)
            || constant.RepresentationType != field.Type
            || !constant.Bits.IsCanonicalFor(field.Type))
        {
            throw new NotSupportedException(
                "CG1 wrapper frozen values require the exact transparent domain and primitive carrier.");
        }

        return Add(constant, typeSpecRef, field.Type);
    }

    private TrRmFrozenScalarValueReference Add(BoundConstant constant,
        uint typeSpecRef, PrimitiveType storageType)
    {
        var key = (constant.Type, constant.Bits, typeSpecRef, storageType);
        if (_values.TryGetValue(key, out var existing))
        {
            return existing;
        }
        var nodeRow = GetOrAddNode(constant, typeSpecRef, storageType);
        if (_sources.Count >= 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(constant),
                "The ConstantSource table exceeds its row limit.");
        }
        var sourceRow = checked((uint)_sources.Count + 1);
        _sources.Add(new TrRmConstantSourceRow(1, nodeRow));
        var result = new TrRmFrozenScalarValueReference(typeSpecRef, sourceRow);
        _values.Add(key, result);
        return result;
    }

    private uint GetOrAddNode(BoundConstant constant, uint typeSpecRef,
        PrimitiveType storageType)
    {
        var key = (constant.Type, constant.Bits, typeSpecRef, storageType);
        if (_nodeRows.TryGetValue(key, out var existing))
        {
            return existing;
        }
        if (_nodes.Count >= 0xFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(constant),
                "The frozen scalar value table exceeds its row limit.");
        }

        var width = PrimitiveTypeFacts.ScalarWidth(storageType);
        if (width < 0)
        {
            throw new NotSupportedException(
                "The selected frozen-value profile has no fixed scalar representation.");
        }

        var blobReference = 0u;
        if (width != 0)
        {
            var payload = new byte[width];
            constant.Bits.WritePayload(storageType, payload);
            var blobKey = new BlobKey(width, constant.Bits.Low,
                constant.Bits.High);
            if (!_blobReferences.TryGetValue(blobKey, out blobReference))
            {
                if (_blobs.Count >= 0xFF_FFFF)
                {
                    throw new ArgumentOutOfRangeException(nameof(constant),
                        "The Blob table exceeds its row limit.");
                }

                _blobs.Add(payload);
                blobReference = checked((uint)_blobs.Count);
                _blobReferences.Add(blobKey, blobReference);
            }
        }

        var nodeRow = checked((uint)_nodes.Count + 1);
        _nodes.Add(new TrRmFrozenValueNodeRow(typeSpecRef, 1,
            blobReference, 0, 0, 0));
        _nodeRows.Add(key, nodeRow);
        return nodeRow;
    }

    internal TrRmFrozenScalarValueTables Encode(TrRmTypeSpecTableReader typeSpecs)
    {
        var blobSection = _blobs.Count == 0
            ? [] : TrRmVariableTableWriter.EncodeBlobs(_blobs);
        var blobs = _blobs.Count == 0
            ? default : new TrRmBlobTableReader(blobSection, _blobs.Count);
        var frozenValueNodeSection = TrRmFrozenValueNodeTableWriter.Encode(
            _nodes, typeSpecs, blobs, childCount: 0, unionCaseDefCount: 0);
        var constantSourceSection = _sources.Count == 0 ? []
            : TrRmConstantSourceTableWriter.Encode(
                _sources, _nodes.Count, constantExecutionCount: 0,
                targetValueBindingCount: 0);
        return new TrRmFrozenScalarValueTables(blobSection, _blobs.Count,
            frozenValueNodeSection, _nodes.Count, constantSourceSection,
            _sources.Count);
    }

    internal TrRmFrozenScalarValueTables EncodeNominalEnumsDraft(
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmEnumDefTableReader enumDefs, TrRmImportTableReader imports)
    {
        var blobSection = _blobs.Count == 0
            ? [] : TrRmVariableTableWriter.EncodeBlobs(_blobs);
        var blobs = _blobs.Count == 0
            ? default : new TrRmBlobTableReader(blobSection, _blobs.Count);
        var frozenValueNodeSection
            = TrRmFrozenValueNodeTableWriter.EncodeNominalEnumsDraft(
                _nodes, typeSpecs, blobs, childCount: 0,
                unionCaseDefCount: 0, typeDefs, enumDefs, imports);
        var constantSourceSection = TrRmConstantSourceTableWriter.Encode(
            _sources, _nodes.Count, constantExecutionCount: 0,
            targetValueBindingCount: 0);
        return new TrRmFrozenScalarValueTables(blobSection, _blobs.Count,
            frozenValueNodeSection, _nodes.Count, constantSourceSection,
            _sources.Count);
    }

    internal TrRmFrozenScalarValueTables EncodeGenericConstantMethodsDraft(
        TrRmTypeSpecTableReader typeSpecs, TrRmTypeDefTableReader typeDefs,
        TrRmEnumDefTableReader enumDefs, TrRmFieldDefTableReader fields,
        TrRmTransparentConstantDomainTableReader transparentConstantDomains,
        TrRmImportTableReader imports)
    {
        var blobSection = _blobs.Count == 0
            ? [] : TrRmVariableTableWriter.EncodeBlobs(_blobs);
        var blobs = _blobs.Count == 0
            ? default : new TrRmBlobTableReader(blobSection, _blobs.Count);
        var frozenValueNodeSection
            = TrRmFrozenValueNodeTableWriter.EncodeGenericConstantMethodsDraft(
                _nodes, typeSpecs, blobs, childCount: 0,
                unionCaseDefCount: 0, typeDefs, enumDefs, fields,
                transparentConstantDomains, imports);
        var constantSourceSection = TrRmConstantSourceTableWriter.Encode(
            _sources, _nodes.Count, constantExecutionCount: 0,
            targetValueBindingCount: 0, genericParamCount: 0,
            constantTemplateCount: 0,
            schema: TrRmArtifactSchema.GenericConstantMethodsDraft);
        return new TrRmFrozenScalarValueTables(blobSection, _blobs.Count,
            frozenValueNodeSection, _nodes.Count, constantSourceSection,
            _sources.Count);
    }

    internal TrRmFrozenScalarValueTables EncodeC1EnumClosure(
        TrRmTypeSpecTableReader typeSpecs,
        TrRmTypeDefTableReader typeDefs,
        TrRmEnumDefTableReader enumDefs,
        TrRmImportTableReader imports)
    {
        if (!typeSpecs.Schema.UsesC1EnumClosure())
        {
            throw new ArgumentOutOfRangeException(nameof(typeSpecs));
        }

        var blobSection = _blobs.Count == 0
            ? [] : TrRmVariableTableWriter.EncodeBlobs(_blobs);
        var blobs = _blobs.Count == 0
            ? default : new TrRmBlobTableReader(blobSection, _blobs.Count);
        var frozenValueNodeSection
            = TrRmFrozenValueNodeTableWriter.EncodeTypedEnumMemberValues(
                _nodes, typeSpecs, blobs, childCount: 0,
                unionCaseDefCount: 0, typeDefs, enumDefs, imports);
        var constantSourceSection = _sources.Count == 0 ? []
            : TrRmConstantSourceTableWriter.Encode(
                _sources, _nodes.Count, constantExecutionCount: 0,
                targetValueBindingCount: 0, genericParamCount: 0,
                constantTemplateCount: 0, schema: typeSpecs.Schema);
        return new TrRmFrozenScalarValueTables(blobSection, _blobs.Count,
            frozenValueNodeSection, _nodes.Count, constantSourceSection,
            _sources.Count);
    }

    private static bool TryGetTypeCode(PrimitiveType type, out byte typeCode)
    {
        for (var candidate = 0; candidate <= 0x19; candidate++)
        {
            if (TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                (byte)candidate, out var candidateType)
                && candidateType == type)
            {
                typeCode = (byte)candidate;
                return true;
            }
        }

        typeCode = 0;
        return false;
    }

    private readonly record struct BlobKey(int Width, ulong Low, ulong High);
}
