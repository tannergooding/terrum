// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmFrozenScalarValueReference(
    uint TypeSpecRef, uint ConstantSourceRow);

internal readonly record struct TrRmFrozenScalarValueTables(
    byte[] BlobSection,
    int BlobCount,
    byte[] FrozenValueNodeSection,
    int FrozenValueNodeCount,
    byte[] ConstantSourceSection,
    int ConstantSourceCount);

internal sealed class TrRmFrozenScalarValueBuilder
{
    private readonly Dictionary<(PrimitiveType Type, ScalarBits Bits),
        TrRmFrozenScalarValueReference> _values = [];
    private readonly Dictionary<BlobKey, uint> _blobReferences = [];
    private readonly List<ReadOnlyMemory<byte>> _blobs = [];
    private readonly List<TrRmFrozenValueNodeRow> _nodes = [];
    private readonly List<TrRmConstantSourceRow> _sources = [];

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

    private TrRmFrozenScalarValueReference Add(BoundConstant constant,
        uint typeSpecRef, PrimitiveType storageType)
    {
        var key = (constant.Type, constant.Bits);
        if (_values.TryGetValue(key, out var existing))
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
            var blobKey = new BlobKey(width, constant.Bits.Low, constant.Bits.High);
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
        var sourceRow = checked((uint)_sources.Count + 1);
        _nodes.Add(new TrRmFrozenValueNodeRow(typeSpecRef, 1, blobReference, 0, 0, 0));
        _sources.Add(new TrRmConstantSourceRow(1, nodeRow));
        var result = new TrRmFrozenScalarValueReference(typeSpecRef, sourceRow);
        _values.Add(key, result);
        return result;
    }

    internal TrRmFrozenScalarValueTables Encode(TrRmTypeSpecTableReader typeSpecs)
    {
        var blobSection = _blobs.Count == 0
            ? [] : TrRmVariableTableWriter.EncodeBlobs(_blobs);
        var blobs = _blobs.Count == 0
            ? default : new TrRmBlobTableReader(blobSection, _blobs.Count);
        var frozenValueNodeSection = TrRmFrozenValueNodeTableWriter.Encode(
            _nodes, typeSpecs, blobs, childCount: 0, unionCaseDefCount: 0);
        var constantSourceSection = TrRmConstantSourceTableWriter.Encode(
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
