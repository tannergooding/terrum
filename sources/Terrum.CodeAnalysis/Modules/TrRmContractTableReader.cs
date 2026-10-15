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

/// <summary>Validates Contract row framing, owner ranges and assigned clause shapes.</summary>
/// <remarks>Structural decoding does not prove the promises or make unassigned clause modes executable.</remarks>
public readonly ref struct TrRmContractTableReader
{
    private readonly TrRmVariableTableIndex _rows;
    private readonly TrRmMethodDefTableReader _methods;
    private readonly TrRmMethodSigTableReader _signatures;
    private readonly TrRmParamTableReader _parameters;
    private readonly TrRmTypeSpecTableReader _typeSpecs;
    private readonly TrRmNativeComInterfaceContract?[] _comInterfaces;
    private readonly TrRmNativeComMethodContract?[] _comMethods;
    private readonly bool[] _typeHasNativeComContracts;
    private readonly bool[] _methodHasNativeComContracts;
    private readonly ImmutableArray<TrRmNativeParameterContract>[] _nativeParameters;
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
        _comInterfaces = new TrRmNativeComInterfaceContract?[types.Count + 1];
        _comMethods = new TrRmNativeComMethodContract?[methods.Count + 1];
        _typeHasNativeComContracts = new bool[types.Count + 1];
        _methodHasNativeComContracts = new bool[methods.Count + 1];
        _nativeParameters = new ImmutableArray<TrRmNativeParameterContract>[methods.Count + 1];
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
        var effectMasks = new uint[methods.Count + 1];
        var cleanupTypes = new HashSet<int>();
        var cleanupMethods = new List<uint>();
        var interfaceIids = new HashSet<Guid>();
        var interfaceMethods = new Dictionary<int, List<(uint Slot, int Method)>>();
        var nativeParameterOrdinals = new HashSet<(uint Owner, uint Ordinal)>();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = _rows.GetRow(row);
            var owner = Read(data, 0);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            if (owners[row] == 0 || owner != owners[row]
                || BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0
                || (kind is 1 or 5 or 7 or 8 or 12 && !singletons.Add((owner, kind))))
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
                        effectMasks[method] = Read(data, 8);
                        if ((effectMasks[method] & 1) != 0)
                        {
                            noThrowMethods.Add(method);
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
                        || parameters.GetMode(paramFirst + (int)ordinal) > 2
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
                    cleanupTypes.Add((int)(owner & 0xFF_FFFF));
                    break;
                case 6:
                    if (data.Length != 24 || owner >> 24 != 0x16)
                    {
                        throw InvalidContract();
                    }

                    var methodRow = (int)(owner & 0xFF_FFFF);
                    var outputSignature = methods.GetSignature(methodRow);
                    var outputOrdinal = Read(data, 8);
                    var outputCondition = Read(data, 12);
                    var failureDisposition = Read(data, 16);
                    var outputResult = TrRmCompactLinearBodyReader.ResolveType(typeSpecs,
                        signatures.GetResultTypeSpec(outputSignature));
                    var (outputFirst, outputCount) = signatures.GetParamRange(outputSignature);
                    var validOutputCondition = outputResult == 8
                        ? outputCondition is 1 or 3
                        : outputResult == 0x0D && outputCondition == 2;
                    if (outputOrdinal >= outputCount
                        || !outputs.Add((owner, outputOrdinal))
                        || !validOutputCondition || failureDisposition != 1
                        || Read(data, 20) != 0
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
                case 7:
                    if (data.Length != 28 || owner >> 24 != 0x13)
                    {
                        throw InvalidContract();
                    }

                    var interfaceType = (int)(owner & 0xFF_FFFF);
                    var interfaceIid = new Guid(data.Slice(8, 16), bigEndian: true);
                    var baseInterface = Read(data, 24);
                    if (((types.GetFlags(interfaceType) >> 2) & 0x03) != 3
                        || interfaceIid == Guid.Empty || !interfaceIids.Add(interfaceIid)
                        || baseInterface > types.Count || baseInterface == (uint)interfaceType)
                    {
                        throw InvalidContract();
                    }
                    _comInterfaces[interfaceType] = new TrRmNativeComInterfaceContract(
                        interfaceIid, (int)baseInterface);
                    _typeHasNativeComContracts[interfaceType] = true;
                    break;
                case 8:
                    if (data.Length != 16 || owner >> 24 != 0x16)
                    {
                        throw InvalidContract();
                    }

                    var comMethod = (int)(owner & 0xFF_FFFF);
                    var comMethodOwner = methods.GetOwnerType(comMethod);
                    var vtableSlot = Read(data, 8);
                    var comEffects = Read(data, 12);
                    if (comMethodOwner == 0 || comEffects > 0x0F
                        || (comEffects & 1) != 0
                            && !HasResultType(methods, signatures, typeSpecs, comMethod, 8)
                        || (comEffects & 4) != 0 && (comEffects & 1) == 0)
                    {
                        throw InvalidContract();
                    }
                    _comMethods[comMethod] = new TrRmNativeComMethodContract(vtableSlot, comEffects);
                    _methodHasNativeComContracts[comMethod] = true;
                    if (!interfaceMethods.TryGetValue(comMethodOwner, out var declaredMethods))
                    {
                        declaredMethods = [];
                        interfaceMethods.Add(comMethodOwner, declaredMethods);
                    }
                    declaredMethods.Add((vtableSlot, comMethod));
                    break;
                case 10:
                    if (data.Length != 20 || owner >> 24 != 0x16)
                    {
                        throw InvalidContract();
                    }

                    var parameterMethod = (int)(owner & 0xFF_FFFF);
                    var parameterOrdinal = Read(data, 8);
                    var parameterSignature = methods.GetSignature(parameterMethod);
                    var (nativeParameterFirst, nativeParameterCount) =
                        signatures.GetParamRange(parameterSignature);
                    if (parameterOrdinal >= nativeParameterCount
                        || data[12] is < 1 or > 3 || data[13] > 1
                        || data[14] != 0 || data[15] != 0
                        || !IsPointerTypeSpec(typeSpecs,
                            parameters.GetTypeSpec(nativeParameterFirst + (int)parameterOrdinal))
                        || !nativeParameterOrdinals.Add((owner, parameterOrdinal)))
                    {
                        throw InvalidContract();
                    }

                    var nativeParameter = new TrRmNativeParameterContract(
                        (int)parameterOrdinal, data[12], data[13], Read(data, 16));
                    AddNativeParameter(parameterMethod, nativeParameter);
                    _methodHasNativeComContracts[parameterMethod] = true;
                    break;
                case 12:
                    if (data.Length != 12 || owner >> 24 != 0x13)
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
                        || blittableTargetRequirement != 1)
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

        for (var type = 1; type < _nativeBlittableTargetRequirements.Length; type++)
        {
            if (_nativeBlittableTargetRequirements[type] != 0
                && cleanupTypes.Contains(type))
            {
                throw InvalidContract();
            }
        }

        foreach (var (interfaceType, definition) in Enumerate(_comInterfaces))
        {
            if (definition.BaseTypeDefRow != 0
                && _comInterfaces[definition.BaseTypeDefRow] is null)
            {
                throw InvalidContract();
            }

            var visited = new HashSet<int>();
            var inheritance = new List<int>();
            var current = interfaceType;
            while (_comInterfaces[current] is TrRmNativeComInterfaceContract currentInterface)
            {
                if (!visited.Add(current))
                {
                    throw InvalidContract();
                }
                inheritance.Add(current);
                if (currentInterface.BaseTypeDefRow == 0)
                {
                    break;
                }
                current = currentInterface.BaseTypeDefRow;
            }

            var inheritedSlotCount = 0u;
            for (var index = inheritance.Count - 1; index >= 0; index--)
            {
                var declaringInterface = inheritance[index];
                if (interfaceMethods.TryGetValue(declaringInterface, out var localMethods))
                {
                    var slots = new HashSet<uint>();
                    foreach (var (slot, method) in localMethods)
                    {
                        if (!slots.Add(slot)
                            || methods.GetOwnerType(method) != declaringInterface
                            || (effectMasks[method] & 4) == 0)
                        {
                            throw InvalidContract();
                        }
                    }
                    if (localMethods.Any(method => method.Slot < inheritedSlotCount
                            || method.Slot > (uint)(int.MaxValue / NativeTargetProfile.PointerSize)))
                    {
                        throw InvalidContract();
                    }
                    if (localMethods.Count != 0)
                    {
                        inheritedSlotCount = Math.Max(inheritedSlotCount,
                            localMethods.Max(static method => method.Slot) + 1);
                    }
                }
            }
        }

        foreach (var (method, _) in Enumerate(_comMethods))
        {
            var interfaceType = methods.GetOwnerType(method);
            if (interfaceType == 0 || _comInterfaces[interfaceType] is null)
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
            if (BinaryPrimitives.ReadUInt16LittleEndian(clause[4..]) == 6)
            {
                var ordinal = Read(clause, 8);
                var signature = _methods.GetSignature(methodRow);
                var (paramFirst, _) = _signatures.GetParamRange(signature);
                var parameterTypeSpec = _parameters.GetTypeSpec(paramFirst + checked((int)ordinal));
                if (!TrRmNativeImportTypeContract.TryGetWritableOutputPointee(
                    _typeSpecs, parameterTypeSpec, out var pointeeType))
                {
                    throw InvalidContract();
                }
                results.Add(new TrRmNativeOutputContract(
                    checked((int)ordinal), pointeeType,
                    Read(clause, 12), Read(clause, 16)));
            }
        }

        return results.ToImmutable();
    }

    internal TrRmNativeComInterfaceContract? GetNativeComInterface(int typeDefRow)
        => typeDefRow > 0 && typeDefRow < _comInterfaces.Length
            ? _comInterfaces[typeDefRow] : null;

    internal TrRmNativeComMethodContract? GetNativeComMethod(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _comMethods.Length
            ? _comMethods[methodDefRow] : null;

    internal uint GetNativeBlittableTargetRequirement(int typeDefRow)
        => typeDefRow > 0 && typeDefRow < _nativeBlittableTargetRequirements.Length
            ? _nativeBlittableTargetRequirements[typeDefRow] : 0;

    internal bool HasNativeComContractsForTypeDef(int typeDefRow)
        => typeDefRow > 0 && typeDefRow < _typeHasNativeComContracts.Length
            && _typeHasNativeComContracts[typeDefRow];

    internal bool HasNativeComContractsForMethodDef(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _methodHasNativeComContracts.Length
            && _methodHasNativeComContracts[methodDefRow];

    internal ImmutableArray<TrRmNativeParameterContract> GetNativeParameters(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _nativeParameters.Length
            ? _nativeParameters[methodDefRow] : [];

    private static bool HasResultType(TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmTypeSpecTableReader typeSpecs,
        int method, byte typeCode)
    {
        var result = typeSpecs.GetRow(signatures.GetResultTypeSpec(methods.GetSignature(method)));
        return result.Length == 3 && result[0] == 1 && result[1] == 0 && result[2] == typeCode;
    }

    private static bool IsPointerTypeSpec(TrRmTypeSpecTableReader typeSpecs, int typeSpecRow)
    {
        var type = typeSpecs.GetRow(typeSpecRow);
        return type.Length == 4 && type[0] == 1 && type[2] == 0x8C
            || type.Length == 7 && type[0] == 0x0C && type[2] == 0x8C;
    }

    private void AddNativeParameter(int method, TrRmNativeParameterContract parameter)
    {
        var parameters = _nativeParameters[method];
        _nativeParameters[method] = parameters.IsDefault
            ? [parameter] : parameters.Add(parameter);
    }

    private static IEnumerable<(int Row, T Value)> Enumerate<T>(T?[] rows) where T : struct
    {
        for (var row = 1; row < rows.Length; row++)
        {
            if (rows[row] is T value)
            {
                yield return (row, value);
            }
        }
    }

    private static IEnumerable<(int Row, int Value)> Enumerate(int[] rows)
    {
        for (var row = 1; row < rows.Length; row++)
        {
            if (rows[row] != 0)
            {
                yield return (row, rows[row]);
            }
        }
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

    private static InvalidDataException InvalidContract()
        => new("Invalid TRRM Contract table.");
}
