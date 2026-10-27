// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct TrRmNativeOutputContract(
    int ParameterOrdinal, PrimitiveType PointeeType, uint ResultCondition, uint FailureDisposition);

internal readonly record struct TrRmNativeParameterContract(
    int ParameterOrdinal, byte Access, byte Retention, byte ExtentKind, uint Extent,
    uint PointerDepth, uint RequiredAlignment);

internal readonly record struct TrRmNativeAbiContract(uint AbiId, byte Role, byte ExceptionMode);

internal readonly record struct TrRmPredicateGraphContract(
    byte PhaseTag, uint PhaseLocalOrdinal, int PredicateGraphRow);
internal readonly record struct TrRmOutputStateContract(
    int ParameterOrdinal, int PredicateGraphRow, byte State);
internal readonly record struct TrRmDependencyContract(
    byte DestinationKind, byte SourceKind, byte Relationship, byte Phase,
    uint DestinationOrdinal, uint SourceOrdinal);

/// <summary>Validates Contract row framing, owner ranges and assigned clause shapes.</summary>
/// <remarks>Structural decoding does not prove the promises or make unassigned clause modes executable.</remarks>
public readonly ref struct TrRmContractTableReader
{
    private readonly TrRmVariableTableIndex _rows;
    private readonly TrRmMethodDefTableReader _methods;
    private readonly TrRmMethodSigTableReader _signatures;
    private readonly TrRmParamTableReader _parameters;
    private readonly TrRmTypeSpecTableReader _typeSpecs;
    private readonly TrRmArtifactSchema _schema;
    private readonly ImmutableArray<TrRmNativeParameterContract>[] _nativeParameters;
    private readonly TrRmNativeAbiContract[] _nativeAbis;
    private readonly bool[] _hasNativeAbis;
    private readonly ImmutableArray<TrRmPredicateGraphContract>[] _predicateGraphContracts;
    private readonly ImmutableArray<TrRmOutputStateContract>[] _outputStateContracts;
    private readonly ImmutableArray<TrRmDependencyContract>[] _dependencyContracts;
    private readonly byte[] _nativeCallableConventions;
    private readonly int[] _nativeCallableContractRows;
    private readonly HashSet<int> _cleanupMethodRows;
    private readonly HashSet<uint> _predicateGraphReferences;

    internal int Count => _rows.Count;
    internal int PredicateGraphAttachmentCount => _predicateGraphReferences.Count;
    internal TrRmArtifactSchema Schema => _schema;

    public TrRmContractTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs, bool developmentProfile = false)
        : this(section, recordCount, types, methods, signatures, parameters,
            typeSpecs, fieldPathCount: 0, default, default,
            developmentProfile ? TrRmArtifactSchema.Development
                : TrRmArtifactSchema.Standard, profileAware: false,
            predicateGraphCount: 0)
    {
    }

    internal TrRmContractTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs, int fieldPathCount,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema, int predicateGraphCount = 0)
        : this(section, recordCount, types, methods, signatures, parameters,
            typeSpecs, fieldPathCount, features, modules, schema, profileAware: true,
            predicateGraphCount)
    {
    }

    private TrRmContractTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs, int fieldPathCount,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules,
        TrRmArtifactSchema schema, bool profileAware, int predicateGraphCount)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != types.ContractCount || recordCount != methods.ContractCount
            || (recordCount == 0 && !section.IsEmpty)
            || fieldPathCount is < 0 or > 0xFF_FFFF
            || predicateGraphCount is < 0 or > 0xFF_FFFF
            || schema is not (TrRmArtifactSchema.Standard or TrRmArtifactSchema.Development
                or TrRmArtifactSchema.CopyOriginDraft
                or TrRmArtifactSchema.ForeignInvocationDraft)
            || (schema == TrRmArtifactSchema.CopyOriginDraft && !profileAware))
        {
            throw InvalidContract();
        }
        var developmentProfile = schema == TrRmArtifactSchema.Development;

        _rows = recordCount == 0 ? default : new TrRmVariableTableIndex(section, recordCount, 8, 1);
        _methods = methods;
        _signatures = signatures;
        _parameters = parameters;
        _typeSpecs = typeSpecs;
        _schema = schema;
        _nativeParameters = new ImmutableArray<TrRmNativeParameterContract>[methods.Count + 1];
        _nativeAbis = new TrRmNativeAbiContract[methods.Count + 1];
        _hasNativeAbis = new bool[methods.Count + 1];
        _predicateGraphContracts =
            new ImmutableArray<TrRmPredicateGraphContract>[methods.Count + 1];
        _outputStateContracts =
            new ImmutableArray<TrRmOutputStateContract>[methods.Count + 1];
        _dependencyContracts =
            new ImmutableArray<TrRmDependencyContract>[methods.Count + 1];
        _nativeCallableConventions = new byte[methods.Count + 1];
        _nativeCallableContractRows = new int[methods.Count + 1];
        _cleanupMethodRows = new HashSet<int>();
        _predicateGraphReferences = new HashSet<uint>();
        var owners = new uint[recordCount + 1];
        for (var type = 1; type <= types.Count; type++)
        {
            var (start, count) = types.GetContractRange(type);
            Fill(owners, start, count, 0x1300_0000u | (uint)type);
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            var (start, count) = methods.GetContractRange(method);
            Fill(owners, start, count, 0x1600_0000u | (uint)method);
        }

        var singletons = new HashSet<(uint Owner, ushort Kind)>();
        var outputs = new HashSet<(uint Owner, uint Ordinal)>();
        var outputStates = new HashSet<(uint Owner, uint Ordinal, uint Predicate)>();
        var dependencies = new HashSet<(uint Owner, byte DestinationKind,
            uint DestinationOrdinal, byte Phase, byte SourceKind, uint SourceOrdinal)>();
        var emptyDependencyDestinations = new HashSet<(uint Owner, byte DestinationKind,
            uint DestinationOrdinal, byte Phase)>();
        var sourcedDependencyDestinations = new HashSet<(uint Owner, byte DestinationKind,
            uint DestinationOrdinal, byte Phase)>();
        var cleanupTypes = new HashSet<int>();
        var predicateOrdinals = new Dictionary<(uint Owner, byte Phase), HashSet<uint>>();
        var nativeParameterOrdinals = new HashSet<(uint Owner, uint Ordinal)>();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = _rows.GetRow(row);
            var owner = Read(data, 0);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            if (owners[row] == 0 || owner != owners[row]
                || BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0
                || (!developmentProfile && kind is 0x10 or 0x11 or 0x12)
                || (kind is 1 or 5 or 0x0C or 0x0D or 0x10
                    && !singletons.Add((owner, kind))))
            {
                throw InvalidContract();
            }

            switch (kind)
            {
                case 1:
                    const uint allowedEffects = 6;
                    if (data.Length != 12 || (Read(data, 8) & ~allowedEffects) != 0)
                    {
                        throw InvalidContract();
                    }
                    break;
                case 2:
                    if (owner >> 24 != 0x16)
                    {
                        throw InvalidContract();
                    }

                    var outputMethod = checked((int)(owner & 0xFF_FFFF));
                    var signature = methods.GetSignature(outputMethod);
                    var (paramFirst, paramCount) = signatures.GetParamRange(signature);
                    var ordinal = Read(data, 8);
                    if (developmentProfile)
                    {
                        if (data.Length != 16 || data[12] > 1 || data[13] > 1
                            || data[14] > 1 || data[15] != 0
                            || typeSpecs.GetTypeSpecBytes(
                                signatures.GetResultTypeSpec(signature)) is not
                                    [1, 0, 1]
                            || ordinal >= paramCount
                            || parameters.GetMode(paramFirst + (int)ordinal) is not (1 or 2)
                            || !outputStates.Add((owner, ordinal, 0)))
                        {
                            throw InvalidContract();
                        }

                        break;
                    }

                    if (data.Length != 20 || data[16] > 1 || data[17] != 0
                        || data[18] != 0 || data[19] != 0)
                    {
                        throw InvalidContract();
                    }

                    var outputPredicateGraph = Read(data, 12);
                    if (ordinal >= paramCount
                        || outputPredicateGraph is 0 or > 0xFF_FFFF
                        || outputPredicateGraph > (uint)predicateGraphCount
                        || parameters.GetMode(paramFirst + (int)ordinal) != 0
                        || !IsInitializationDestinationType(typeSpecs,
                            parameters.GetTypeSpec(paramFirst + (int)ordinal))
                        || !outputStates.Add((owner, ordinal, outputPredicateGraph)))
                    {
                        throw InvalidContract();
                    }

                    var outputState = new TrRmOutputStateContract((int)ordinal,
                        checked((int)outputPredicateGraph), data[16]);
                    var methodOutputStates = _outputStateContracts[outputMethod];
                    _outputStateContracts[outputMethod] =
                        methodOutputStates.IsDefault
                            ? [outputState]
                            : methodOutputStates.Add(outputState);
                    break;
                case 3:
                    if (data.Length != 16 || data[14] != 0 || data[15] != 0)
                    {
                        throw InvalidContract();
                    }
                    break;
                case 4:
                    if (data.Length != 20)
                    {
                        throw InvalidContract();
                    }
                    if (developmentProfile)
                    {
                        break;
                    }
                    if (owner >> 24 != 0x16)
                    {
                        throw InvalidContract();
                    }

                    var dependencyMethod = checked((int)(owner & 0xFF_FFFF));
                    var dependencySignature = methods.GetSignature(dependencyMethod);
                    var dependencyParamCount =
                        signatures.GetParamRange(dependencySignature).Count;
                    var destinationKind = data[8];
                    var sourceKind = data[9];
                    var relationship = data[10];
                    var phase = data[11];
                    var destinationOrdinal = Read(data, 12);
                    var sourceOrdinal = Read(data, 16);
                    var destinationValid = destinationKind switch
                    {
                        1 => destinationOrdinal == 0,
                        2 or 3 => destinationOrdinal < dependencyParamCount,
                        4 => destinationOrdinal == 0
                            && signatures.GetReceiverTypeSpec(dependencySignature) != 0,
                        _ => false,
                    };
                    var sourceValid = sourceKind switch
                    {
                        0 => relationship == 0 && sourceOrdinal == 0,
                        1 => relationship is >= 1 and <= 3
                            && sourceOrdinal < dependencyParamCount,
                        2 => relationship is >= 1 and <= 3 && sourceOrdinal == 0
                            && signatures.GetReceiverTypeSpec(dependencySignature) != 0,
                        _ => false,
                    };
                    var dependencyKey = (owner, destinationKind,
                        destinationOrdinal, phase, sourceKind, sourceOrdinal);
                    var destinationKey = (owner, destinationKind,
                        destinationOrdinal, phase);
                    if (phase is < 1 or > 3 || !destinationValid || !sourceValid
                        || !dependencies.Add(dependencyKey))
                    {
                        throw InvalidContract();
                    }

                    if (sourceKind == 0)
                    {
                        _ = emptyDependencyDestinations.Add(destinationKey);
                    }
                    else
                    {
                        _ = sourcedDependencyDestinations.Add(destinationKey);
                    }

                    var dependency = new TrRmDependencyContract(destinationKind,
                        sourceKind, relationship, phase, destinationOrdinal,
                        sourceOrdinal);
                    var methodDependencies = _dependencyContracts[dependencyMethod];
                    _dependencyContracts[dependencyMethod] =
                        methodDependencies.IsDefault
                            ? [dependency] : methodDependencies.Add(dependency);
                    break;
                case 5:
                    if (data.Length != 12 || owner >> 24 != 0x13
                        || Read(data, 8) is 0 or > 0xFF_FFFF
                        || Read(data, 8) > methods.Count
                        || methods.GetOwnerType((int)Read(data, 8)) != (owner & 0xFF_FFFF))
                    {
                        throw InvalidContract();
                    }

                    _ = _cleanupMethodRows.Add(checked((int)Read(data, 8)));
                    _ = cleanupTypes.Add((int)(owner & 0xFF_FFFF));
                    break;
                case 6:
                    throw InvalidContract();
                case 0x0B:
                    if (developmentProfile || !profileAware || data.Length != 20
                        || owner >> 24 != 0x16 || data[8] is not (1 or 2 or 3)
                        || data[9] != 0
                        || BinaryPrimitives.ReadUInt16LittleEndian(data[10..]) != 0)
                    {
                        throw InvalidContract();
                    }

                    var predicateOrdinal = Read(data, 12);
                    var predicateGraph = Read(data, 16);
                    var predicateMethod = checked((int)(owner & 0xFF_FFFF));
                    if (predicateGraph == 0 || predicateGraph > (uint)predicateGraphCount
                        || !_predicateGraphReferences.Add(predicateGraph))
                    {
                        throw InvalidContract();
                    }

                    if (!predicateOrdinals.TryGetValue((owner, data[8]),
                        out var phaseOrdinals))
                    {
                        phaseOrdinals = new HashSet<uint>();
                        predicateOrdinals.Add((owner, data[8]), phaseOrdinals);
                    }
                    if (!phaseOrdinals.Add(predicateOrdinal)
                        || !TrRmFeatureManifestValidator.Requires(features, modules,
                            methods.GetOwnerModule(predicateMethod),
                            TrRmFeatureTableReader.PublishedPredicatesFeatureId, schema))
                    {
                        throw InvalidContract();
                    }
                    var methodPredicates = _predicateGraphContracts[predicateMethod];
                    _predicateGraphContracts[predicateMethod] =
                        methodPredicates.IsDefault
                            ? [new TrRmPredicateGraphContract(data[8],
                                predicateOrdinal, checked((int)predicateGraph))]
                            : methodPredicates.Add(new TrRmPredicateGraphContract(
                                data[8], predicateOrdinal, checked((int)predicateGraph)));
                    break;
                case 0x0C:
                    if (developmentProfile || data.Length != 12
                        || owner >> 24 != 0x16 || data[8] is < 1 or > 4
                        || data[9] != 0
                        || BinaryPrimitives.ReadUInt16LittleEndian(data[10..]) != 0)
                    {
                        throw InvalidContract();
                    }

                    var nativeCallableMethod = checked((int)(owner & 0xFF_FFFF));
                    _nativeCallableConventions[nativeCallableMethod] = data[8];
                    _nativeCallableContractRows[nativeCallableMethod] = row;
                    break;
                case 0x0D:
                    if (schema != TrRmArtifactSchema.CopyOriginDraft
                        || !profileAware || owner >> 24 != 0x16)
                    {
                        throw InvalidContract();
                    }
                    ValidateCopyOriginClause(data, (int)(owner & 0xFF_FFFF),
                        methods, signatures, parameters, typeSpecs, fieldPathCount);
                    var copyOriginModule = methods.GetOwnerModule(
                        (int)(owner & 0xFF_FFFF));
                    if (!TrRmFeatureManifestValidator.Requires(features, modules,
                        copyOriginModule,
                        TrRmFeatureTableReader.CopyOriginContractsCandidateFeatureId,
                        schema))
                    {
                        throw InvalidContract();
                    }
                    break;
                case 0x0E:
                    if (schema != TrRmArtifactSchema.ForeignInvocationDraft
                        || !profileAware || data.Length != 20 || owner >> 24 != 0x16
                        || data[12] is < 1 or > 3 || data[13] != 1
                        || data[14] is not (0 or 1) || data[15] != 0
                        || (data[14] == 0 ? Read(data, 16) != 0 : Read(data, 16) == 0))
                    {
                        throw InvalidContract();
                    }

                    var nativeParameterMethod = checked((int)(owner & 0xFF_FFFF));
                    var nativeParameterOrdinal = Read(data, 8);
                    var nativeParameterSignature = methods.GetSignature(nativeParameterMethod);
                    var (pointerParameterFirst, pointerParameterCount) =
                        signatures.GetParamRange(nativeParameterSignature);
                    if (nativeParameterOrdinal >= pointerParameterCount)
                    {
                        throw InvalidContract();
                    }

                    var pointerParameterRow = pointerParameterFirst
                        + checked((int)nativeParameterOrdinal);
                    var nativeParameterTypeSpec = parameters.GetTypeSpec(pointerParameterRow);
                    if (parameters.GetMode(pointerParameterRow) != 0
                        || parameters.GetFlags(pointerParameterRow) != 0
                        || !IsNativeParameterPointerTypeSpec(typeSpecs,
                            nativeParameterTypeSpec, data[12]))
                    {
                        throw new InvalidDataException(
                            "Invalid TRRM NativeParameter formal type.");
                    }

                    var nativeParameterType = typeSpecs.GetTypeSpecRef(
                        nativeParameterTypeSpec);
                    if (IsVoidNativeParameterTypeSpec(typeSpecs, nativeParameterTypeSpec)
                        && (data[12] != 1 || data[14] != 0
                            || Read(data, 16) != 0
                            || nativeParameterType.Qualifiers != 0))
                    {
                        throw new InvalidDataException(
                            "Opaque TRRM NativeParameter pointers are read-only and unbounded.");
                    }
                    if (!nativeParameterOrdinals.Add((owner, nativeParameterOrdinal)))
                    {
                        throw new InvalidDataException(
                            "Duplicate TRRM NativeParameter formal contract.");
                    }
                    if (!TrRmFeatureManifestValidator.Requires(features, modules,
                        methods.GetOwnerModule(nativeParameterMethod),
                        TrRmFeatureTableReader.ForeignInvocationContractsCandidateFeatureId,
                        schema))
                    {
                        throw new InvalidDataException(
                            "TRRM NativeParameter requires Feature 16/1.");
                    }

                    var pointerParameter = new TrRmNativeParameterContract(
                        checked((int)nativeParameterOrdinal), data[12], data[13],
                        data[14], Read(data, 16), 1, 0);
                    AddNativeParameter(nativeParameterMethod, pointerParameter);
                    break;
                case 0x10:
                    if (data.Length != 16 || owner >> 24 != 0x16
                        || Read(data, 8) != 1 || data[12] is < 1 or > 4
                        || data[13] != 0 || data[14] != 0 || data[15] != 0)
                    {
                        throw InvalidContract();
                    }

                    var abiMethod = (int)(owner & 0xFF_FFFF);
                    _nativeAbis[abiMethod] = new TrRmNativeAbiContract(
                        Read(data, 8), data[12], data[13]);
                    _hasNativeAbis[abiMethod] = true;
                    break;
                case 0x11:
                    if (data.Length != 28 || owner >> 24 != 0x16
                        || data[19] != 0 || Read(data, 12) != 1
                        || data[16] is < 1 or > 3 || data[17] != 1
                        || data[18] is not (0 or 1)
                        || (data[18] == 0 && Read(data, 20) != 0)
                        || Read(data, 24) != 0)
                    {
                        throw InvalidContract();
                    }

                    var parameterMethod = (int)(owner & 0xFF_FFFF);
                    var parameterOrdinal = Read(data, 8);
                    var parameterSignature = methods.GetSignature(parameterMethod);
                    var (nativeParameterFirst, nativeParameterCount) =
                        signatures.GetParamRange(parameterSignature);
                    if (parameterOrdinal >= nativeParameterCount
                        || !IsPointerTypeSpec(typeSpecs,
                            parameters.GetTypeSpec(nativeParameterFirst + (int)parameterOrdinal))
                        || !nativeParameterOrdinals.Add((owner, parameterOrdinal)))
                    {
                        throw InvalidContract();
                    }

                    var nativeParameter = new TrRmNativeParameterContract(
                        (int)parameterOrdinal, data[16], data[17], data[18],
                        Read(data, 20), Read(data, 12), Read(data, 24));
                    AddNativeParameter(parameterMethod, nativeParameter);
                    break;
                case 0x12:
                    if (data.Length != 28 || owner >> 24 != 0x16
                        || data[19] != 0 || Read(data, 12) != 1
                        || data[16] is not (2 or 4)
                        || data[17] != 2 || data[18] != 0
                        || ReadUInt64(data, 20) != 0)
                    {
                        throw InvalidContract();
                    }

                    var methodRow = (int)(owner & 0xFF_FFFF);
                    var outputSignature = methods.GetSignature(methodRow);
                    var outputOrdinal = Read(data, 8);
                    var outputCondition = data[16];
                    var outputResult = TrRmCompactLinearBodyReader.ResolveType(typeSpecs,
                        signatures.GetResultTypeSpec(outputSignature));
                    var (outputFirst, outputCount) = signatures.GetParamRange(outputSignature);
                    var validOutputCondition = outputCondition == 2
                        ? outputResult is 8 or 0x0D
                        : outputResult == 8;
                    if (outputOrdinal >= outputCount
                        || !outputs.Add((owner, outputOrdinal))
                        || !validOutputCondition
                        || parameters.GetMode(outputFirst + (int)outputOrdinal) != 0)
                    {
                        throw InvalidContract();
                    }
                    var parameterTypeSpec = parameters.GetTypeSpec(outputFirst + (int)outputOrdinal);
                    if (!TrRmNativeImportTypeContract.TryGetWritableOutputPointee(
                        typeSpecs, parameterTypeSpec, out _))
                    {
                        throw InvalidContract();
                    }
                    break;
                case 0x13:
                    throw InvalidContract();
                default:
                    throw InvalidContract();
            }
        }

        if (emptyDependencyDestinations.Overlaps(sourcedDependencyDestinations))
        {
            throw InvalidContract();
        }
        foreach (var nativeParameter in nativeParameterOrdinals)
        {
            if (outputs.Contains(nativeParameter)
                || outputStates.Any(outputState =>
                    outputState.Owner == nativeParameter.Owner
                    && outputState.Ordinal == nativeParameter.Ordinal))
            {
                throw InvalidContract();
            }
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            if (_hasNativeAbis[method] && (GetEffectsMask(method) & 4) == 0)
            {
                throw InvalidContract();
            }
        }

        foreach (var phase in predicateOrdinals.Values)
        {
            for (uint ordinal = 0; ordinal < phase.Count; ordinal++)
            {
                if (!phase.Contains(ordinal))
                {
                    throw InvalidContract();
                }
            }
        }

        for (var method = 1; method <= methods.Count; method++)
        {
            foreach (var outputState in GetOutputStateContracts(method))
            {
                var matchingPredicate = false;
                foreach (var predicate in GetPredicateGraphContracts(method))
                {
                    if (predicate.PhaseTag == 3
                        && predicate.PredicateGraphRow == outputState.PredicateGraphRow)
                    {
                        matchingPredicate = true;
                        break;
                    }
                }

                if (!matchingPredicate)
                {
                    throw InvalidContract();
                }
            }
        }
    }

    internal ReadOnlySpan<byte> GetRow(int row)
        => _rows.GetRow(row);

    internal ReadOnlySpan<byte> GetCopyOriginClauseRow(int methodDefRow)
    {
        if (methodDefRow <= 0 || methodDefRow > _methods.Count)
        {
            throw InvalidContract();
        }

        var (first, count) = _methods.GetContractRange(methodDefRow);
        for (var index = 0; index < count; index++)
        {
            var row = _rows.GetRow(first + index);
            if (BinaryPrimitives.ReadUInt16LittleEndian(row[4..]) == 0x0D)
            {
                return row;
            }
        }

        return default;
    }

    internal uint GetEffectsMask(int methodRow)
    {
        if (methodRow <= 0 || methodRow > _methods.Count)
        {
            throw InvalidContract();
        }

        var (first, count) = _methods.GetContractRange(methodRow);
        for (var index = 0; index < count; index++)
        {
            var clause = _rows.GetRow(first + index);
            if (BinaryPrimitives.ReadUInt16LittleEndian(clause[4..]) == 1)
            {
                return Read(clause, 8);
            }
        }

        return 0;
    }

    internal ImmutableArray<TrRmNativeOutputContract> GetNativeOutputs(int methodRow)
    {
        if (methodRow <= 0 || methodRow > _methods.Count)
        {
            throw InvalidContract();
        }

        var (first, count) = _methods.GetContractRange(methodRow);
        var results = ImmutableArray.CreateBuilder<TrRmNativeOutputContract>();
        for (var index = 0; index < count; index++)
        {
            var clause = _rows.GetRow(first + index);
            if (BinaryPrimitives.ReadUInt16LittleEndian(clause[4..]) == 0x12)
            {
                var ordinal = Read(clause, 8);
                var signature = _methods.GetSignature(methodRow);
                var (paramFirst, _) = _signatures.GetParamRange(signature);
                var parameterTypeSpec = _parameters.GetTypeSpec(
                    paramFirst + checked((int)ordinal));
                if (!TrRmNativeImportTypeContract.TryGetWritableOutputPointee(
                    _typeSpecs, parameterTypeSpec, out var pointeeType))
                {
                    throw InvalidContract();
                }

                var resultType = TrRmCompactLinearBodyReader.ResolveType(_typeSpecs,
                    _signatures.GetResultTypeSpec(signature));
                var condition = clause[16] == 4
                    ? 3u : resultType == 8 ? 1u : 2u;
                results.Add(new TrRmNativeOutputContract(checked((int)ordinal),
                    pointeeType, condition, 1));
            }
        }

        return results.ToImmutable();
    }

    internal bool HasNativeAbi(int methodRow)
        => methodRow > 0 && methodRow < _hasNativeAbis.Length && _hasNativeAbis[methodRow];

    internal bool HasNativeCallableContract(int methodRow)
        => methodRow > 0 && methodRow < _nativeCallableContractRows.Length
            && _nativeCallableContractRows[methodRow] != 0;

    internal byte GetNativeCallableConvention(int methodRow)
        => HasNativeCallableContract(methodRow)
            ? _nativeCallableConventions[methodRow] : throw InvalidContract();

    internal int GetNativeCallableContractRow(int methodRow)
        => HasNativeCallableContract(methodRow)
            ? _nativeCallableContractRows[methodRow] : throw InvalidContract();

    internal bool IsCleanupMethod(int methodRow)
        => _cleanupMethodRows.Contains(methodRow);

    internal ImmutableArray<TrRmPredicateGraphContract> GetPredicateGraphContracts(
        int methodRow)
        => methodRow > 0 && methodRow < _predicateGraphContracts.Length
            ? _predicateGraphContracts[methodRow].IsDefault
                ? [] : _predicateGraphContracts[methodRow]
            : throw InvalidContract();

    internal ImmutableArray<TrRmOutputStateContract> GetOutputStateContracts(
        int methodRow)
        => methodRow > 0 && methodRow < _outputStateContracts.Length
            ? _outputStateContracts[methodRow].IsDefault
                ? [] : _outputStateContracts[methodRow]
            : throw InvalidContract();

    internal ImmutableArray<TrRmDependencyContract> GetDependencyContracts(
        int methodRow)
        => methodRow > 0 && methodRow < _dependencyContracts.Length
            ? _dependencyContracts[methodRow].IsDefault
                ? [] : _dependencyContracts[methodRow]
            : throw InvalidContract();

    internal TrRmNativeAbiContract GetNativeAbi(int methodRow)
        => HasNativeAbi(methodRow) ? _nativeAbis[methodRow] : throw InvalidContract();

    internal ImmutableArray<TrRmNativeParameterContract> GetNativeParameters(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _nativeParameters.Length
            ? _nativeParameters[methodDefRow].IsDefault
                ? [] : _nativeParameters[methodDefRow]
            : throw InvalidContract();

    private static bool IsPointerTypeSpec(TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var type = typeSpecs.GetTypeSpecBytes(typeSpecRef);
        return (type.Length == 4 && type[0] == 1 && type[2] == 0x8C)
            || (type.Length == 7 && type[0] == 0x0C && type[2] == 0x8C);
    }

    private static bool IsNativeParameterPointerTypeSpec(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef, byte access)
    {
        var type = typeSpecs.GetTypeSpecBytes(typeSpecRef);
        if (type[1] is not (0 or 1)
            || (access is 2 or 3 && type[1] != 1)
            || (type.Length == 4 && (type[0] != 1 || type[2] != 0x8C))
            || (type.Length == 7 && (type[0] != 0x0C || type[2] != 0x8C))
            || type.Length is not (4 or 7))
        {
            return false;
        }

        if (type.Length == 4)
        {
            return type[3] is 0 or (>= 1 and <= 9) or (>= 0x0B and <= 0x0E)
                or 0x14 or 0x15;
        }

        _ = typeSpecs.GetTypeSpecRef(Read(type, 3));
        return true;
    }

    private static bool IsVoidNativeParameterTypeSpec(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var type = typeSpecs.GetTypeSpecBytes(typeSpecRef);
        if (type.Length == 4 && type[0] == 1 && type[2] == 0x8C)
        {
            return type[3] == 0;
        }
        if (type.Length == 7 && type[0] == 0x0C && type[2] == 0x8C)
        {
            var innerType = typeSpecs.GetTypeSpecBytes(Read(type, 3));
            return innerType is [1, 0, 0];
        }

        return false;
    }

    private static bool IsInitializationDestinationType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var type = typeSpecs.GetTypeSpecBytes(typeSpecRef);
        if (type.Length == 6 && type[0] == 0x0E)
        {
            return true;
        }

        if (type.Length == 7 && type[0] == 0x0C && type[2] == 0x8A)
        {
            var argument = typeSpecs.GetTypeSpecRef(Read(type, 3),
                allowInitQualifier: true);
            return (argument.Qualifiers & 0x04) != 0;
        }

        return false;
    }

    private static void ValidateCopyOriginClause(ReadOnlySpan<byte> data,
        int methodDefRow, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs, int fieldPathCount)
    {
        if (data.Length < 20 || data[8] is not (1 or 2) || data[9] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(data[10..]) != 0
            || Read(data, 12) == 0)
        {
            throw InvalidContract();
        }
        _ = typeSpecs.GetTypeSpecRef(Read(data, 12));

        var signature = methods.GetSignature(methodDefRow);
        var (paramFirst, paramCount) = signatures.GetParamRange(signature);
        var receiverTypeSpec = signatures.GetReceiverTypeSpec(signature);
        var factCount = Read(data, 16);
        if (factCount == 0 || factCount > (uint)((data.Length - 20) / 8))
        {
            throw InvalidContract();
        }

        var offset = 20;
        for (uint fact = 0; fact < factCount; fact++)
        {
            if (offset > data.Length - 8)
            {
                throw InvalidContract();
            }

            var destinationPath = Read(data, offset);
            var originCount = Read(data, offset + 4);
            var remainingOriginBytes = data.Length - offset - 8;
            if ((destinationPath != 0 && destinationPath > (uint)fieldPathCount)
                || originCount == 0
                || originCount > (uint)(remainingOriginBytes / 12))
            {
                throw InvalidContract();
            }

            offset += 8;
            for (uint origin = 0; origin < originCount; origin++)
            {
                var sourceKind = data[offset];
                var sourceView = data[offset + 1];
                var sourceOrdinal = Read(data, offset + 4);
                var sourcePath = Read(data, offset + 8);
                if (sourceKind is not (1 or 2) || sourceView is not (1 or 2)
                    || BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]) != 0
                    || (sourcePath != 0 && sourcePath > (uint)fieldPathCount)
                    || (sourceKind == 2 && (sourceView != 2 || sourceOrdinal != 0
                        || receiverTypeSpec == 0 || data[8] == 2))
                    || (sourceKind == 1 && sourceOrdinal >= (uint)paramCount))
                {
                    throw InvalidContract();
                }

                if (sourceKind == 1)
                {
                    var sourceMode = parameters.GetMode(paramFirst
                        + checked((int)sourceOrdinal));
                    if (sourceMode != 0)
                    {
                        throw InvalidContract();
                    }

                    var sourceType = typeSpecs.GetTypeSpecRef(
                        parameters.GetTypeSpec(paramFirst
                            + checked((int)sourceOrdinal)));
                    if (sourceType.Form == 0x0E)
                    {
                        throw InvalidContract();
                    }
                    if (sourceView == 2)
                    {
                        _ = TrRmCopyOriginContractValidator.GetReferentTypeSpec(
                            sourceType, typeSpecs);
                    }
                }
                offset += 12;
            }
        }

        if (offset != data.Length)
        {
            throw InvalidContract();
        }
    }

    private void AddNativeParameter(int method, TrRmNativeParameterContract parameter)
    {
        var parameters = _nativeParameters[method];
        _nativeParameters[method] = parameters.IsDefault
            ? [parameter] : parameters.Add(parameter);
    }

    private static void Fill(uint[] owners, int start, int count, uint owner)
    {
        if (count == 0 ? start != 0
            : start <= 0 || count < 0 || start + count > owners.Length)
        {
            throw InvalidContract();
        }

        for (var row = start; row < start + count; row++)
        {
            if (owners[row] != 0)
            {
                throw InvalidContract();
            }

            owners[row] = owner;
        }
    }

    private static uint Read(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, sizeof(ulong)));

    private static InvalidDataException InvalidContract()
        => new("Invalid TRRM Contract table.");
}
