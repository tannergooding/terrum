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
    private readonly int[] _comOwnerInterfaces;
    private readonly ImmutableArray<TrRmNativeParameterContract>[] _nativeParameters;
    private readonly ImmutableArray<TrRmNativeComAcquisitionContract>[] _comAcquisitions;

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
        _comOwnerInterfaces = new int[types.Count + 1];
        _nativeParameters = new ImmutableArray<TrRmNativeParameterContract>[methods.Count + 1];
        _comAcquisitions = new ImmutableArray<TrRmNativeComAcquisitionContract>[methods.Count + 1];
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
        var outputFailureDispositions = new Dictionary<(uint Owner, uint Ordinal), uint>();
        var outputStates = new Dictionary<(uint Owner, uint Ordinal),
            (byte OnTrue, byte OnFalse, byte OnExceptional, byte ParameterMode)>();
        var noThrowMethods = new HashSet<uint>();
        var effectMasks = new uint[methods.Count + 1];
        var cleanupTypes = new HashSet<int>();
        var cleanupMethods = new List<uint>();
        var interfaceIids = new HashSet<Guid>();
        var interfaceMethods = new Dictionary<int, List<(uint Slot, int Method)>>();
        var nativeParameterOrdinals = new HashSet<(uint Owner, uint Ordinal)>();
        var acquisitionOrdinals = new HashSet<(uint Owner, uint Ordinal)>();
        var acquisitions = new List<TrRmNativeComAcquisitionContract>();
        for (var row = 1; row <= recordCount; row++)
        {
            var data = _rows.GetRow(row);
            var owner = Read(data, 0);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            if (owners[row] == 0 || owner != owners[row]
                || BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0
                || (kind is 1 or 5 or 7 or 8 or 9 && !singletons.Add((owner, kind))))
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
                        || !outputStates.TryAdd((owner, ordinal),
                            (data[12], data[13], data[14],
                                parameters.GetMode(paramFirst + (int)ordinal))))
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
                    if (outputOrdinal >= outputCount
                        || !outputs.Add((owner, outputOrdinal))
                        || outputCondition != (outputResult == 8 ? 1u
                            : outputResult == 0x0D ? 2u : 0u)
                        || outputCondition == 0 || failureDisposition != 1
                        || Read(data, 20) != 0
                        || parameters.GetMode(outputFirst + (int)outputOrdinal) != 0)
                    {
                        throw InvalidContract();
                    }
                    outputFailureDispositions.Add((owner, outputOrdinal), failureDisposition);

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
                    if (!interfaceMethods.TryGetValue(comMethodOwner, out var declaredMethods))
                    {
                        declaredMethods = [];
                        interfaceMethods.Add(comMethodOwner, declaredMethods);
                    }
                    declaredMethods.Add((vtableSlot, comMethod));
                    break;
                case 9:
                    if (data.Length != 12 || owner >> 24 != 0x13)
                    {
                        throw InvalidContract();
                    }

                    var ownerType = (int)(owner & 0xFF_FFFF);
                    var ownerInterface = Read(data, 8);
                    if (ownerInterface is 0 or > 0xFF_FFFF || ownerInterface > types.Count
                        || ownerType == ownerInterface
                        || ((types.GetFlags(ownerType) >> 2) & 0x03) != 1
                        || ((types.GetFlags((int)ownerInterface) >> 2) & 0x03) != 3)
                    {
                        throw InvalidContract();
                    }
                    _comOwnerInterfaces[ownerType] = (int)ownerInterface;
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
                    break;
                case 11:
                    if (data.Length != 28 || owner >> 24 != 0x16)
                    {
                        throw InvalidContract();
                    }

                    var acquisitionMethod = (int)(owner & 0xFF_FFFF);
                    var acquisitionOutput = Read(data, 8);
                    var acquisitionOwner = Read(data, 12);
                    var sourceMethod = Read(data, 16);
                    var sourceOutput = Read(data, 20);
                    var acquisitionFailureDisposition = Read(data, 24);
                    if (acquisitionOutput >= GetParameterCount(methods, signatures, acquisitionMethod)
                        || acquisitionOwner is 0 or > 0xFF_FFFF || acquisitionOwner > types.Count
                        || sourceMethod is 0 or > 0xFF_FFFF || sourceMethod > methods.Count
                        || sourceOutput >= GetParameterCount(methods, signatures, (int)sourceMethod)
                        || acquisitionFailureDisposition > 1
                        || methods.GetBodyRow(acquisitionMethod) == 0
                        || !HasResultType(methods, signatures, typeSpecs, acquisitionMethod, 1)
                        || !IsTypeDefTypeSpec(typeSpecs, parameters, signatures, methods,
                            acquisitionMethod, (int)acquisitionOutput, (int)acquisitionOwner)
                        || !outputFailureDispositions.TryGetValue(
                            (0x1600_0000u | sourceMethod, sourceOutput), out var sourceFailureDisposition)
                        || acquisitionFailureDisposition != sourceFailureDisposition
                        || !acquisitionOrdinals.Add((owner, acquisitionOutput)))
                    {
                        throw InvalidContract();
                    }

                    acquisitions.Add(new TrRmNativeComAcquisitionContract(
                        acquisitionMethod, (int)acquisitionOutput, (int)acquisitionOwner,
                        (int)sourceMethod, (int)sourceOutput, (byte)acquisitionFailureDisposition));
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

            var inheritedMethodCount = 0;
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
                    if (localMethods.Any(method => method.Slot < inheritedMethodCount)
                        || !localMethods.Select(static method => method.Slot).Order()
                            .SequenceEqual(Enumerable.Range(inheritedMethodCount, localMethods.Count)
                                .Select(static slot => (uint)slot)))
                    {
                        throw InvalidContract();
                    }
                    inheritedMethodCount += localMethods.Count;
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

        foreach (var (ownerType, interfaceType) in Enumerate(_comOwnerInterfaces))
        {
            if (_comInterfaces[interfaceType] is null || !cleanupTypes.Contains(ownerType))
            {
                throw InvalidContract();
            }
        }

        foreach (var acquisition in acquisitions)
        {
            if (_comOwnerInterfaces[acquisition.OwnerTypeDefRow] == 0
                || ((types.GetFlags(acquisition.OwnerTypeDefRow) >> 2) & 0x03) != 1)
            {
                throw InvalidContract();
            }
            var key = (0x1600_0000u | (uint)acquisition.MethodDefRow,
                (uint)acquisition.OutputParameterOrdinal);
            if (!outputStates.TryGetValue(key, out var state)
                || state.ParameterMode != 0 || state.OnTrue != 1
                || state.OnFalse != 0 || state.OnExceptional != 0)
            {
                throw InvalidContract();
            }
            AddComAcquisition(acquisition.MethodDefRow, acquisition);
        }

        foreach (var (key, state) in outputStates)
        {
            if (state.ParameterMode == 0
                && !acquisitionOrdinals.Contains(key))
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

    internal int GetNativeComOwnerInterface(int typeDefRow)
        => typeDefRow > 0 && typeDefRow < _comOwnerInterfaces.Length
            ? _comOwnerInterfaces[typeDefRow] : 0;

    internal ImmutableArray<TrRmNativeParameterContract> GetNativeParameters(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _nativeParameters.Length
            ? _nativeParameters[methodDefRow] : [];

    internal ImmutableArray<TrRmNativeComAcquisitionContract> GetNativeComAcquisitions(int methodDefRow)
        => methodDefRow > 0 && methodDefRow < _comAcquisitions.Length
            ? _comAcquisitions[methodDefRow] : [];

    private static int GetParameterCount(TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, int method)
    {
        var (_, count) = signatures.GetParamRange(methods.GetSignature(method));
        return count;
    }

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

    private static bool IsTypeDefTypeSpec(TrRmTypeSpecTableReader typeSpecs,
        TrRmParamTableReader parameters, TrRmMethodSigTableReader signatures,
        TrRmMethodDefTableReader methods, int method, int ordinal, int typeDefRow)
    {
        var (first, _) = signatures.GetParamRange(methods.GetSignature(method));
        var type = typeSpecs.GetRow(parameters.GetTypeSpec(first + ordinal));
        return type.Length == 6 && type[0] == 0x02 && type[1] == 0
            && Read(type, 2) == (uint)typeDefRow;
    }

    private void AddNativeParameter(int method, TrRmNativeParameterContract parameter)
    {
        var parameters = _nativeParameters[method];
        _nativeParameters[method] = parameters.IsDefault
            ? [parameter] : parameters.Add(parameter);
    }

    private void AddComAcquisition(int acquisitionMethod,
        TrRmNativeComAcquisitionContract acquisition)
    {
        var acquisitions = _comAcquisitions[acquisitionMethod];
        _comAcquisitions[acquisitionMethod] = acquisitions.IsDefault
            ? [acquisition] : acquisitions.Add(acquisition);
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
