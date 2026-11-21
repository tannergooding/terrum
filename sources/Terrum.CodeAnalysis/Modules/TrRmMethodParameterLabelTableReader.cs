// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Modules;

internal readonly ref struct TrRmMethodParameterLabelTableReader
{
    private const int RowSize = 12;
    private readonly Dictionary<ulong, uint>? _nameRows;
    private readonly Dictionary<ulong, uint>? _defaultConstantSourceRows;
    private readonly TrRmA3LayoutResourceLedger? _resourceLedger;
    private readonly bool _usesParameterDefaults;

    internal TrRmMethodParameterLabelTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        if (!schema.UsesMethodParameterLabels()
            || schema.UsesClassParameterDefaults()
            || schema.UsesForeignNativeInterfaceParameterSurface()
            || recordCount is < 1 or > 0xFF_FFFF
            || (long)recordCount * RowSize != section.Length)
        {
            throw InvalidTable();
        }

        _resourceLedger = resourceLedger;
        var nameRows = new Dictionary<ulong, uint>(recordCount);
        for (var row = 0; row < recordCount; row++)
        {
            resourceLedger?.ConsumeWork();
            var data = section.Slice(row * RowSize, RowSize);
            var owner = Read(data, 0);
            var ordinal = Read(data, 4);
            var nameRow = Read(data, 8);
            if (owner is 0 or > 0x00FF_FFFF
                || owner > (uint)methods.Count
                || nameRow is 0 or > 0x00FF_FFFF
                || nameRow > (uint)strings.Count)
            {
                throw InvalidTable();
            }

            var signature = methods.GetSignature(checked((int)owner));
            var (_, parameterCount) = signatures.GetParamRange(signature);
            if (ordinal >= (uint)parameterCount)
            {
                throw InvalidTable();
            }

            var name = strings.DecodeName(checked((int)nameRow));
            if (!IsIdentifier(name))
            {
                throw InvalidTable();
            }

            var key = ((ulong)owner << 32) | ordinal;
            if (!nameRows.TryAdd(key, nameRow))
            {
                throw InvalidTable();
            }
        }

        Count = recordCount;
        _nameRows = nameRows;
        _defaultConstantSourceRows = null;
        _usesParameterDefaults = false;
    }

    internal TrRmMethodParameterLabelTableReader(ReadOnlySpan<byte> section,
        int recordCount, TrRmStringTableReader strings,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures,
        TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmFeatureTableReader features,
        TrRmModuleTableReader modules, TrRmArtifactSchema schema,
        TrRmA3LayoutResourceLedger? resourceLedger = null)
    {
        const int surfaceRowSize = 16;
        if (!(schema.UsesClassParameterDefaults()
                || schema.UsesForeignNativeInterfaceParameterSurface())
            || recordCount is < 1 or > 0xFF_FFFF
            || (long)recordCount * surfaceRowSize != section.Length)
        {
            throw InvalidTable();
        }

        _resourceLedger = resourceLedger;
        var nameRows = new Dictionary<ulong, uint>(recordCount);
        var defaultRows = new Dictionary<ulong, uint>(recordCount);
        var namesByOwner = new HashSet<(uint Owner, string Name)>();
        for (var row = 0; row < recordCount; row++)
        {
            resourceLedger?.ConsumeWork();
            var data = section.Slice(row * surfaceRowSize,
                surfaceRowSize);
            var owner = Read(data, 0);
            var ordinal = Read(data, 4);
            var nameRow = Read(data, 8);
            var defaultValueRow = Read(data, 12);
            if (owner is 0 or > 0x00FF_FFFF
                || owner > (uint)methods.Count
                || nameRow is 0 or > 0x00FF_FFFF
                || nameRow > (uint)strings.Count
                || defaultValueRow > 0x00FF_FFFF
                || defaultValueRow > (uint)constantSources.Count)
            {
                throw InvalidTable();
            }

            var ownerModule = methods.GetOwnerModule(
                checked((int)owner));
            if (!TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule,
                    TrRmFeatureTableReader.MethodParameterLabelsFeatureId,
                    schema))
            {
                throw InvalidTable();
            }

            var signature = methods.GetSignature(checked((int)owner));
            var (parameterFirst, parameterCount)
                = signatures.GetParamRange(signature);
            if (ordinal >= (uint)parameterCount)
            {
                throw InvalidTable();
            }

            var name = strings.DecodeName(checked((int)nameRow));
            if (!IsIdentifier(name)
                || !namesByOwner.Add((owner, name)))
            {
                throw InvalidTable();
            }

            var key = ((ulong)owner << 32) | ordinal;
            if (!nameRows.TryAdd(key, nameRow)
                || !defaultRows.TryAdd(key, defaultValueRow))
            {
                throw InvalidTable();
            }

            if (defaultValueRow == 0)
            {
                continue;
            }

            if (!TrRmFeatureManifestValidator.Requires(features, modules,
                    ownerModule,
                    TrRmFeatureTableReader.TypedFrozenValuesFeatureId,
                    schema))
            {
                throw InvalidTable();
            }

            var parameterRow = checked(parameterFirst
                + (int)ordinal);
            var parameterTypeSpecRef
                = parameters.GetTypeSpec(parameterRow);
            var parameterType
                = typeSpecs.GetTypeSpecRef(parameterTypeSpecRef);
            var parameterTypeEligible
                = parameterType.Form == 0x01
                    && parameterType.Qualifiers == 0
                    && (schema.UsesForeignNativeInterfaceParameterSurface()
                        ? TrRmFrozenValueNodeTableReader
                            .IsRegisteredScalarTypeCode(
                                parameterType.TypeCode)
                        : parameterType.Row == 0
                            && parameterType.TypeCode is not (0x10 or 0x11));
            if (parameters.GetMode(parameterRow) != 0
                || parameters.GetFlags(parameterRow) != 0
                || !parameterTypeEligible)
            {
                throw InvalidTable();
            }

            var constantSourceRow = checked((int)defaultValueRow);
            if (constantSources.GetKind(constantSourceRow) != 1)
            {
                throw InvalidTable();
            }

            var frozenValueNodeRow = checked((int)
                constantSources.GetPayloadReference(constantSourceRow));
            var frozenValueTypeSpecRef
                = frozenValueNodes.GetTypeSpecRef(frozenValueNodeRow);
            if (frozenValueNodes.GetKind(frozenValueNodeRow) != 1
                || (schema.UsesForeignNativeInterfaceParameterSurface()
                    ? !typeSpecs.AreEquivalentTypeSpecRefs(
                        frozenValueTypeSpecRef, parameterTypeSpecRef)
                    : frozenValueTypeSpecRef != parameterTypeSpecRef))
            {
                throw InvalidTable();
            }

            _ = frozenValueNodes.GetScalarPayload(frozenValueNodeRow,
                blobs);
        }

        Count = recordCount;
        _nameRows = nameRows;
        _defaultConstantSourceRows = defaultRows;
        _usesParameterDefaults = true;
    }

    internal int Count { get; }

    internal bool TryGetNameStringRow(int methodDefRow, uint ordinal,
        out int nameStringRow)
    {
        _resourceLedger?.ConsumeWork();
        if (methodDefRow <= 0 || (uint)methodDefRow > 0x00FF_FFFF
            || _nameRows is null
            || !_nameRows.TryGetValue(
                ((ulong)(uint)methodDefRow << 32) | ordinal,
                out var row))
        {
            nameStringRow = 0;
            return false;
        }

        nameStringRow = checked((int)row);
        return true;
    }

    internal bool TryGetDefaultConstantSourceRow(int methodDefRow,
        uint ordinal, out uint constantSourceRow)
    {
        _resourceLedger?.ConsumeWork();
        if (!_usesParameterDefaults
            || methodDefRow <= 0 || (uint)methodDefRow > 0x00FF_FFFF
            || _defaultConstantSourceRows is null
            || !_defaultConstantSourceRows.TryGetValue(
                ((ulong)(uint)methodDefRow << 32) | ordinal,
                out constantSourceRow))
        {
            constantSourceRow = 0;
            return false;
        }

        return true;
    }

    private static uint Read(ReadOnlySpan<byte> row, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(offset, 4));

    private static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || name == "_")
        {
            return false;
        }

        var first = true;
        foreach (var rune in name.EnumerateRunes())
        {
            if (rune.Value != '_' && !(first
                    ? UnicodeXid.IsStart(rune.Value)
                    : UnicodeXid.IsContinue(rune.Value)))
            {
                return false;
            }

            first = false;
        }

        return !first;
    }

    private static InvalidDataException InvalidTable()
        => new("Invalid TRRM MethodParameterLabel table.");
}
