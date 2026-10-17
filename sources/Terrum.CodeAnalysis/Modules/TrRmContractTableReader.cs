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

/// <summary>Validates Contract row framing, owner ranges and assigned clause shapes.</summary>
/// <remarks>Structural decoding does not prove the promises or make unassigned clause modes executable.</remarks>
public readonly ref struct TrRmContractTableReader
{
    private readonly TrRmVariableTableIndex _rows;
    private readonly TrRmMethodDefTableReader _methods;
    private readonly TrRmMethodSigTableReader _signatures;
    private readonly TrRmParamTableReader _parameters;
    private readonly TrRmTypeSpecTableReader _typeSpecs;
    private readonly ImmutableArray<TrRmNativeParameterContract>[] _nativeParameters;
    private readonly TrRmNativeAbiContract[] _nativeAbis;
    private readonly bool[] _hasNativeAbis;
    private readonly uint[] _nativeBlittableTargetRequirements;

    public TrRmContractTableReader(ReadOnlySpan<byte> section, int recordCount,
        TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader typeSpecs)
    {
        if (recordCount is < 0 or > 0xFF_FFFF
            || recordCount != types.ContractCount || recordCount != methods.ContractCount
            || (recordCount == 0 && !section.IsEmpty))
        {
            throw InvalidContract();
        }

        _rows = recordCount == 0 ? default : new TrRmVariableTableIndex(section, recordCount, 8, 1);
        _methods = methods;
        _signatures = signatures;
        _parameters = parameters;
        _typeSpecs = typeSpecs;
        _nativeParameters = new ImmutableArray<TrRmNativeParameterContract>[methods.Count + 1];
        _nativeAbis = new TrRmNativeAbiContract[methods.Count + 1];
        _hasNativeAbis = new bool[methods.Count + 1];
        _nativeBlittableTargetRequirements = new uint[types.Count + 1];
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
        var outputStates = new HashSet<(uint Owner, uint Ordinal)>();
        var noThrowMethods = new HashSet<uint>();
        var cleanupTypes = new HashSet<int>();
        var cleanupMethods = new List<uint>();
        var nativeParameterOrdinals = new HashSet<(uint Owner, uint Ordinal)>();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = _rows.GetRow(row);
            var owner = Read(data, 0);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            if (owners[row] == 0 || owner != owners[row]
                || BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0
                || (kind is 1 or 5 or 0x10 or 0x13
                    && !singletons.Add((owner, kind))))
            {
                throw InvalidContract();
            }

            switch (kind)
            {
                case 1:
                    if (data.Length != 12 || (Read(data, 8) & ~7u) != 0)
                    {
                        throw InvalidContract();
                    }

                    if (owner >> 24 == 0x16)
                    {
                        var method = owner & 0xFF_FFFF;
                        if ((Read(data, 8) & 1) != 0)
                        {
                            _ = noThrowMethods.Add(method);
                        }
                    }
                    break;
                case 2:
                    if (data.Length != 16 || data[12] > 1 || data[13] > 1
                        || data[14] > 1 || data[15] != 0 || owner >> 24 != 0x16)
                    {
                        throw InvalidContract();
                    }

                    var signature = methods.GetSignature((int)(owner & 0xFF_FFFF));
                    var result = typeSpecs.GetRow(signatures.GetResultTypeSpec(signature));
                    var (paramFirst, paramCount) = signatures.GetParamRange(signature);
                    var ordinal = Read(data, 8);
                    if (result.Length != 3 || result[0] != 1 || result[1] != 0
                        || result[2] != 1 || ordinal >= paramCount
                        || parameters.GetMode(paramFirst + (int)ordinal) is not (1 or 2)
                        || !outputStates.Add((owner, ordinal)))
                    {
                        throw InvalidContract();
                    }
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
                    break;
                case 5:
                    if (data.Length != 12 || owner >> 24 != 0x13
                        || Read(data, 8) is 0 or > 0xFF_FFFF
                        || Read(data, 8) > methods.Count
                        || methods.GetOwnerType((int)Read(data, 8)) != (owner & 0xFF_FFFF))
                    {
                        throw InvalidContract();
                    }

                    cleanupMethods.Add(Read(data, 8));
                    _ = cleanupTypes.Add((int)(owner & 0xFF_FFFF));
                    break;
                case 6:
                    throw InvalidContract();
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
                    if (data.Length != 20 || owner >> 24 != 0x13)
                    {
                        throw InvalidContract();
                    }

                    var blittableType = (int)(owner & 0xFF_FFFF);
                    var blittableTargetRequirement = Read(data, 8);
                    var blittableFlags = types.GetFlags(blittableType);
                    var blittableLayout = (blittableFlags >> 4) & 0x03;
                    if (((blittableFlags >> 2) & 0x03) != 1
                        || blittableLayout is not (1 or 3)
                        || types.GetLayoutRow(blittableType) == 0
                        || types.GetGenericRange(blittableType).Count != 0
                        || blittableTargetRequirement != 1 || Read(data, 12) != 0
                        || Read(data, 16) != 1)
                    {
                        throw InvalidContract();
                    }

                    _nativeBlittableTargetRequirements[blittableType] = blittableTargetRequirement;
                    break;
                default:
                    throw InvalidContract();
            }
        }

        foreach (var method in cleanupMethods)
        {
            if (!noThrowMethods.Contains(method))
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

        for (var type = 1; type < _nativeBlittableTargetRequirements.Length; type++)
        {
            if (_nativeBlittableTargetRequirements[type] != 0
                && cleanupTypes.Contains(type))
            {
                throw InvalidContract();
            }
        }
    }

    internal ReadOnlySpan<byte> GetRow(int row)
        => _rows.GetRow(row);

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

    internal TrRmNativeAbiContract GetNativeAbi(int methodRow)
        => HasNativeAbi(methodRow) ? _nativeAbis[methodRow] : throw InvalidContract();

    internal uint GetNativeBlittableTargetRequirement(int typeDefRow)
        => typeDefRow > 0 && typeDefRow < _nativeBlittableTargetRequirements.Length
            ? _nativeBlittableTargetRequirements[typeDefRow] : 0;

    internal ImmutableArray<TrRmNativeParameterContract> GetNativeParameters(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _nativeParameters.Length
            ? _nativeParameters[methodDefRow] : [];

    private static bool IsPointerTypeSpec(TrRmTypeSpecTableReader typeSpecs, int typeSpecRow)
    {
        var type = typeSpecs.GetRow(typeSpecRow);
        return (type.Length == 4 && type[0] == 1 && type[2] == 0x8C)
            || (type.Length == 7 && type[0] == 0x0C && type[2] == 0x8C);
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
