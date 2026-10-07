// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace Terrum.CodeAnalysis.Modules;

/// <summary>Audits the isolated, provisional ScalarInitOutput general Body subset.</summary>
/// <remarks>
/// This does not activate a feature or load an executable. The caller must validate artifact-wide
/// feature/section policy and audit every callee before binding and running SemanticVerifier.
/// Entry starts use the profile's provisional ordinal-zero rule, not general TRRM semantics.
/// </remarks>
internal static class TrRmScalarInitOutputBodyReader
{
    // Operand IDs are one-based; inventory arrays are zero-based. ParameterOrdinal is -1
    // for non-parameters, and PlaceId is zero for scalar values. Offsets are block-relative.
    // Initialized records the successful operation path, not a publication promise on Fail.
    internal sealed record TypeInfo(int TypeSpecRow, ushort TypeKey);
    internal sealed record PlaceInfo(int TypeId, int ParameterOrdinal, int LastUseOrdinal,
        bool Initialized);
    internal sealed record ValueInfo(int TypeId, byte Kind, int DefinitionOffset,
        int LastUseOrdinal, int PlaceId, int ParameterOrdinal);
    // ConstSigned Data is sign-extended to 64 bits; other constant Data is zero-extended.
    // Call Operands are always value IDs, including kind-1 handles. Only PlaceLocal's
    // operand names a place descriptor; ValueInfo.PlaceId resolves its resulting handle.
    internal sealed record Instruction(byte Opcode, int Offset, int Result, int SecondResult,
        int TypeId, byte Kind, ulong Data, int MethodRow, ImmutableArray<int> Operands,
        int TrueTarget = 0, int FalseTarget = 0,
        ImmutableArray<int> OutputPlaces = default);
    internal sealed record Block(ImmutableArray<int> Parameters, ImmutableArray<int> LiveIns,
        ImmutableArray<int> LiveOuts, ImmutableArray<(int Place, bool Initialized)> InheritedPlaces,
        ImmutableArray<int> StartingPlaces, ImmutableArray<int> EndingPlaces,
        ImmutableArray<Instruction> Operations, Instruction Terminator);
    internal sealed record Body(ImmutableArray<TypeInfo> Types, ImmutableArray<PlaceInfo> Places,
        ImmutableArray<ValueInfo> Values, ImmutableArray<Instruction> Operations,
        Instruction Terminator, ImmutableArray<int> StartingPlaces, ImmutableArray<int> EndingPlaces,
        uint Effects, ImmutableArray<Block> Blocks);

    internal static Body Read(TrRmBodyTableReader bodies, int methodRow,
        TrRmModuleTableReader modules, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, ReadOnlySpan<byte> contractSection,
        uint ruleRevision)
    {
        if (modules.Count != 1 || modules.ModuleRefCount != 0
            || modules.GetRequirements(1).RuntimePolicy != 0)
        {
            throw InvalidBody();
        }

        if (methods.ContractCount == 0 && !contractSection.IsEmpty)
        {
            throw InvalidBody();
        }

        var contracts = methods.ContractCount == 0 ? default
            : new TrRmVariableTableIndex(contractSection, methods.ContractCount, 8, 1);
        var signature = ValidateMethod(methodRow, bodies, methods, signatures, parameters,
            types, contracts, ruleRevision, out var effects);
        var parser = new Parser(bodies.GetRow(methods.GetBodyRow(methodRow)), types,
            bodies, methods, signatures, parameters, contracts, ruleRevision);
        return parser.Read(signature, effects);
    }

    private static int ValidateMethod(int method, TrRmBodyTableReader bodies,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmVariableTableIndex contracts,
        uint ruleRevision, out uint effects)
    {
        if (methods.GetOwnerModule(method) != 1 || methods.GetOwnerType(method) != 0
            || methods.GetBodyRow(method) == 0)
        {
            throw InvalidBody();
        }

        effects = 0;
        var (contractFirst, contractCount) = methods.GetContractRange(method);
        if (contractCount != 0)
        {
            if (contractCount != 1)
            {
                throw InvalidBody();
            }

            var clause = contracts.GetRow(contractFirst);
            if (clause.Length != 12
                || BinaryPrimitives.ReadUInt32LittleEndian(clause) != (0x1600_0000u | (uint)method)
                || BinaryPrimitives.ReadUInt16LittleEndian(clause[4..]) != 1
                || BinaryPrimitives.ReadUInt16LittleEndian(clause[6..]) != 0
                || (effects = BinaryPrimitives.ReadUInt32LittleEndian(clause[8..])) is < 1 or > 7)
            {
                throw InvalidBody();
            }
        }

        var signature = methods.GetSignature(method);
        if (signatures.GetReceiverTypeSpec(signature) != 0
            || signatures.GetGenericCount(signature) != 0)
        {
            throw InvalidBody();
        }

        var hasOutput = false;
        var (first, count) = signatures.GetParamRange(signature);
        for (var index = 0; index < count; index++)
        {
            var mode = parameters.GetMode(first + index);
            var type = TrRmCompactLinearBodyReader.ResolveType(types,
                parameters.GetTypeSpec(first + index));
            if (mode == 2 && Scalar(type))
            {
                hasOutput = true;
            }
            else if (mode != 0)
            {
                throw InvalidBody();
            }
        }

        // Public MethodDefs are the only legal Export targets. Rejecting public output
        // declarations also prevents a later Export row from exposing this private slice.
        var body = bodies.GetRow(methods.GetBodyRow(method));
        var hasGeneralPlaceBody = !body.IsEmpty && body[0] <= 2;
        var resultType = ResultType(types, signatures.GetResultTypeSpec(signature));
        var widenedOutputResult = hasOutput
            && (ruleRevision >= 3 && NonBooleanScalar(resultType)
                || ruleRevision >= 4 && resultType == 1);
        if ((hasGeneralPlaceBody && resultType is not (0 or 8) && !widenedOutputResult)
            || (hasOutput && (methods.GetFlags(method) == 2 || !hasGeneralPlaceBody
                || resultType is not (0 or 8) && !widenedOutputResult)))
        {
            throw InvalidBody();
        }

        return signature;
    }

    private static ushort ResultType(TrRmTypeSpecTableReader types, int row)
    {
        var bytes = types.GetRow(row);
        return bytes.Length == 3 && bytes[0] == 1 && bytes[1] == 0 && bytes[2] == 0
            ? (ushort)0 : TrRmCompactLinearBodyReader.ResolveType(types, row);
    }

    private ref struct Parser
    {
        private readonly ReadOnlySpan<byte> _body;
        private readonly TrRmTypeSpecTableReader _types;
        private readonly TrRmBodyTableReader _bodies;
        private readonly TrRmMethodDefTableReader _methods;
        private readonly TrRmMethodSigTableReader _signatures;
        private readonly TrRmParamTableReader _parameters;
        private readonly TrRmVariableTableIndex _contracts;
        private readonly uint _ruleRevision;
        private int _position;
        private int _blockEnd;
        private int _width;
        private int _maximumStructural;
        private int _blockStart;
        private int _defined;
        private int _ordinal;
        private TypeInfo[] _map;
        private bool[] _usedTypes;
        private int[] _valueTypes;
        private byte[] _valueKinds;
        private uint[] _definitions;
        private int[] _valueUses;
        private int[] _placeUses;
        private int[] _observedValueBlocks;
        private int[] _observedPlaceBlocks;
        private int[] _valueLastSeenBlock;
        private int[] _placeLastSeenBlock;
        private int _currentBlock;
        private int[] _placeTypes;
        private int[] _observedValues;
        private int[] _observedPlaces;
        private int[] _valueOccurrences;
        private int[] _handlePlaces;
        private int[] _valueParameters;
        private int[] _placeParameters;
        private bool[] _active;
        private bool[] _initialized;

        internal Parser(ReadOnlySpan<byte> body, TrRmTypeSpecTableReader types,
            TrRmBodyTableReader bodies, TrRmMethodDefTableReader methods,
            TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
            TrRmVariableTableIndex contracts, uint ruleRevision)
        {
            _body = body;
            _types = types;
            _bodies = bodies;
            _methods = methods;
            _signatures = signatures;
            _parameters = parameters;
            _contracts = contracts;
            _ruleRevision = ruleRevision;
            _position = 0;
            _blockEnd = body.Length;
            _width = 1;
            _maximumStructural = 0;
            _blockStart = 0;
            _defined = 0;
            _ordinal = 0;
            _map = [];
            _usedTypes = [];
            _valueTypes = [];
            _valueKinds = [];
            _definitions = [];
            _valueUses = [];
            _placeUses = [];
            _observedValueBlocks = [];
            _observedPlaceBlocks = [];
            _valueLastSeenBlock = [];
            _placeLastSeenBlock = [];
            _currentBlock = 0;
            _placeTypes = [];
            _observedValues = [];
            _observedPlaces = [];
            _valueOccurrences = [];
            _handlePlaces = [];
            _valueParameters = [];
            _placeParameters = [];
            _active = [];
            _initialized = [];
        }

        internal Body Read(int signature, uint effects)
        {
            var layout = Byte();
            if (layout > 2)
            {
                throw InvalidBody();
            }

            _width = 1 << layout;
            var blockCount = Local();
            if (blockCount != 1)
            {
                if (blockCount == 3 && _ruleRevision >= 5)
                {
                    return ReadConditional(signature, effects);
                }

                throw InvalidBody();
            }

            var valueCount = Local();
            var placeCount = Local();
            var regionCount = Local();
            var typeCount = Local();
            if (regionCount != 0 || typeCount > _types.Count
                || typeCount > Remaining / 4 || valueCount > Remaining / (2 * _width + 1)
                || placeCount > Remaining / (3 * _width + 1)
                || (long)typeCount * 4 + (long)valueCount * (2 * _width + 5)
                    + (long)placeCount * (3 * _width + 1) + 2 * _width + 12 > Remaining)
            {
                throw InvalidBody();
            }

            _map = new TypeInfo[typeCount];
            _usedTypes = new bool[typeCount];
            var mapped = new HashSet<ushort>();
            for (var index = 0; index < typeCount; index++)
            {
                var row = Number(4);
                if (row == 0 || row > _types.Count)
                {
                    throw InvalidBody();
                }

                var key = TrRmCompactLinearBodyReader.ResolveType(_types, row);
                if (!mapped.Add(key))
                {
                    throw InvalidBody();
                }

                _map[index] = new TypeInfo(row, key);
            }

            _placeUses = new int[placeCount];
            _placeTypes = new int[placeCount];
            _placeParameters = Filled(placeCount, -1);
            _active = new bool[placeCount];
            _initialized = new bool[placeCount];
            for (var index = 0; index < placeCount; index++)
            {
                _placeTypes[index] = Local();
                if (!Scalar(Type(_placeTypes[index])) || Local() != 0 || Byte() != 4
                    || (_placeUses[index] = Local()) > 1)
                {
                    throw InvalidBody();
                }
            }

            _valueTypes = new int[valueCount];
            _valueKinds = new byte[valueCount];
            _valueUses = new int[valueCount];
            _handlePlaces = new int[valueCount];
            _valueParameters = Filled(valueCount, -1);
            for (var index = 0; index < valueCount; index++)
            {
                _valueTypes[index] = Local();
                _ = Type(_valueTypes[index]);
                _valueKinds[index] = Byte();
                _valueUses[index] = Local();
                if (_valueKinds[index] > 1 || _valueUses[index] > 1
                    || _valueKinds[index] == 1 && !Scalar(Type(_valueTypes[index])))
                {
                    throw InvalidBody();
                }
            }

            if (Local() != 0 || Local() != valueCount)
            {
                throw InvalidBody();
            }

            _definitions = new uint[valueCount];
            for (var index = 0; index < valueCount; index++)
            {
                _definitions[index] = (uint)Unsigned(4);
            }

            var blockOffset = Unsigned(4);
            var blockLength = Unsigned(4);
            var terminatorOffset = Unsigned(4);
            if (blockOffset != 0 || blockLength != (ulong)Remaining
                || terminatorOffset >= blockLength || Local() != 0)
            {
                throw InvalidBody();
            }

            _blockStart = _position - _width;
            _observedValues = Filled(valueCount, -1);
            _observedPlaces = Filled(placeCount, -1);
            var (first, parameterCount) = _signatures.GetParamRange(signature);
            var entryCount = Local();
            if (entryCount > valueCount || entryCount > Remaining / _width)
            {
                throw InvalidBody();
            }

            var entry = 0;
            var formalPlace = 0;
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var row = first + ordinal;
                var type = TrRmCompactLinearBodyReader.ResolveType(_types, _parameters.GetTypeSpec(row));
                if (_parameters.GetMode(row) == 2)
                {
                    if (formalPlace >= placeCount || Type(_placeTypes[formalPlace]) != type)
                    {
                        throw InvalidBody();
                    }

                    _placeParameters[formalPlace++] = ordinal;
                }
                else
                {
                    if (entry >= entryCount || Local() != entry + 1
                        || _valueKinds[entry] != 0 || Type(_valueTypes[entry]) != type
                        || _definitions[entry] != uint.MaxValue)
                    {
                        throw InvalidBody();
                    }

                    _valueParameters[entry++] = ordinal;
                }
            }

            if (entry != entryCount || Local() != 0 || Local() != 0 || Local() != 0)
            {
                throw InvalidBody();
            }

            _defined = entry;
            var starting = PlaceList();
            foreach (var place in starting)
            {
                _active[place - 1] = true;
                _observedPlaces[place - 1] = Math.Max(_observedPlaces[place - 1], 0);
            }

            // With no predecessors or other blocks, each declared place starts here.
            if (starting.Length != placeCount)
            {
                throw InvalidBody();
            }

            var ending = PlaceList();
            var lastValues = LastUses(valueCount);
            var lastPlaces = LastUses(placeCount);
            var operationCount = Local();
            if (operationCount > Remaining / 2)
            {
                throw InvalidBody();
            }

            var operations = ImmutableArray.CreateBuilder<Instruction>(operationCount);
            for (_ordinal = 0; _ordinal < operationCount; _ordinal++)
            {
                if ((ulong)(_position - _blockStart) >= terminatorOffset)
                {
                    throw InvalidBody();
                }

                operations.Add(Operation());
                if ((ulong)(_position - _blockStart) > terminatorOffset)
                {
                    throw InvalidBody();
                }
            }

            if (_defined != valueCount || (ulong)(_position - _blockStart) != terminatorOffset)
            {
                throw InvalidBody();
            }

            foreach (var place in ending)
            {
                _active[place - 1] = false;
                _observedPlaces[place - 1] = Math.Max(_observedPlaces[place - 1], operationCount);
            }

            var terminator = Terminator(signature);
            if (Remaining != 0)
            {
                throw InvalidBody();
            }

            var expectedWidth = _maximumStructural <= byte.MaxValue ? 1
                : _maximumStructural <= ushort.MaxValue ? 2 : 4;
            if (_width != expectedWidth)
            {
                throw InvalidBody();
            }

            foreach (var used in _usedTypes)
            {
                if (!used)
                {
                    throw InvalidBody();
                }
            }

            AuditUses(lastValues, _observedValues, _valueUses);
            AuditUses(lastPlaces, _observedPlaces, _placeUses);
            var values = ImmutableArray.CreateBuilder<ValueInfo>(valueCount);
            for (var index = 0; index < valueCount; index++)
            {
                values.Add(new ValueInfo(_valueTypes[index], _valueKinds[index],
                    _definitions[index] == uint.MaxValue ? -1 : (int)_definitions[index],
                    _observedValues[index], _handlePlaces[index], _valueParameters[index]));
            }

            var places = ImmutableArray.CreateBuilder<PlaceInfo>(placeCount);
            for (var index = 0; index < placeCount; index++)
            {
                places.Add(new PlaceInfo(_placeTypes[index], _placeParameters[index],
                    _observedPlaces[index], _initialized[index]));
            }

            var operationList = operations.MoveToImmutable();
            return new Body(ImmutableArray.CreateRange(_map), places.MoveToImmutable(),
                values.MoveToImmutable(), operationList, terminator,
                starting, ending, effects,
                [new Block(ImmutableArray<int>.Empty, ImmutableArray<int>.Empty,
                    ImmutableArray<int>.Empty, ImmutableArray<(int, bool)>.Empty,
                    starting, ending, operationList, terminator)]);
        }

        private Body ReadConditional(int signature, uint effects)
        {
            var valueCount = Local();
            var placeCount = Local();
            var regionCount = Local();
            var typeCount = Local();
            if (regionCount != 0 || typeCount > _types.Count
                || typeCount > Remaining / 4 || valueCount > Remaining / (2 * _width + 1)
                || placeCount > Remaining / (3 * _width + 1))
            {
                throw InvalidBody();
            }

            _map = new TypeInfo[typeCount];
            _usedTypes = new bool[typeCount];
            var mapped = new HashSet<ushort>();
            for (var index = 0; index < typeCount; index++)
            {
                var row = Number(4);
                if (row == 0 || row > _types.Count)
                {
                    throw InvalidBody();
                }

                var key = TrRmCompactLinearBodyReader.ResolveType(_types, row);
                if (!mapped.Add(key))
                {
                    throw InvalidBody();
                }

                _map[index] = new TypeInfo(row, key);
            }

            _placeUses = new int[placeCount];
            _placeTypes = new int[placeCount];
            _placeParameters = Filled(placeCount, -1);
            _active = new bool[placeCount];
            _initialized = new bool[placeCount];
            for (var index = 0; index < placeCount; index++)
            {
                _placeTypes[index] = Local();
                if (!Scalar(Type(_placeTypes[index])) || Local() != 0 || Byte() != 4
                    || (_placeUses[index] = Local()) > 3)
                {
                    throw InvalidBody();
                }
            }

            _valueTypes = new int[valueCount];
            _valueKinds = new byte[valueCount];
            _valueUses = new int[valueCount];
            _handlePlaces = new int[valueCount];
            _valueParameters = Filled(valueCount, -1);
            for (var index = 0; index < valueCount; index++)
            {
                _valueTypes[index] = Local();
                _ = Type(_valueTypes[index]);
                _valueKinds[index] = Byte();
                _valueUses[index] = Local();
                if (_valueKinds[index] > 1 || _valueUses[index] > 3
                    || _valueKinds[index] == 1 && !Scalar(Type(_valueTypes[index])))
                {
                    throw InvalidBody();
                }
            }

            _valueOccurrences = new int[valueCount];
            _observedValueBlocks = new int[valueCount];
            _observedPlaceBlocks = new int[placeCount];
            _valueLastSeenBlock = Filled(valueCount, -1);
            _placeLastSeenBlock = Filled(placeCount, -1);
            _definitions = new uint[valueCount];
            var definitionStarts = new int[4];
            for (var index = 0; index < definitionStarts.Length; index++)
            {
                definitionStarts[index] = Local();
                if (index == 0 && definitionStarts[index] != 0
                    || index != 0 && definitionStarts[index] < definitionStarts[index - 1])
                {
                    throw InvalidBody();
                }
            }
            if (definitionStarts[^1] != valueCount)
            {
                throw InvalidBody();
            }

            for (var index = 0; index < valueCount; index++)
            {
                _definitions[index] = (uint)Unsigned(4);
                var block = 0;
                while (block < 3 && index >= definitionStarts[block + 1])
                {
                    block++;
                }
                if (block >= 3 || block != 0 && _definitions[index] == uint.MaxValue)
                {
                    throw InvalidBody();
                }
            }

            var blockOffsets = new uint[4];
            for (var index = 0; index < blockOffsets.Length; index++)
            {
                blockOffsets[index] = (uint)Unsigned(4);
                if (index == 0 && blockOffsets[index] != 0
                    || index != 0 && blockOffsets[index] <= blockOffsets[index - 1])
                {
                    throw InvalidBody();
                }
            }
            var terminatorOffsets = new uint[3];
            for (var index = 0; index < terminatorOffsets.Length; index++)
            {
                terminatorOffsets[index] = (uint)Unsigned(4);
            }

            var blockDataStart = _position;
            if (blockOffsets[^1] != Remaining)
            {
                throw InvalidBody();
            }

            var (first, parameterCount) = _signatures.GetParamRange(signature);
            var outputParameterCount = 0;
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                outputParameterCount += _parameters.GetMode(first + ordinal) == 2 ? 1 : 0;
            }
            var entryCount = parameterCount - outputParameterCount;
            if (entryCount > definitionStarts[1] || entryCount > valueCount)
            {
                throw InvalidBody();
            }
            for (var index = 0; index < valueCount; index++)
            {
                if ((index < entryCount) != (_definitions[index] == uint.MaxValue))
                {
                    throw InvalidBody();
                }
            }

            var entryValues = ImmutableArray.CreateBuilder<int>(entryCount);
            var formalPlace = 0;
            var entryValue = 0;
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var row = first + ordinal;
                var type = TrRmCompactLinearBodyReader.ResolveType(_types,
                    _parameters.GetTypeSpec(row));
                if (_parameters.GetMode(row) == 2)
                {
                    if (formalPlace >= placeCount || Type(_placeTypes[formalPlace]) != type)
                    {
                        throw InvalidBody();
                    }

                    _placeParameters[formalPlace++] = ordinal;
                }
                else
                {
                    if (entryValue >= entryCount || _valueKinds[entryValue] != 0
                        || Type(_valueTypes[entryValue]) != type
                        || _definitions[entryValue] != uint.MaxValue)
                    {
                        throw InvalidBody();
                    }

                    _valueParameters[entryValue] = ordinal;
                    entryValues.Add(++entryValue);
                }
            }
            if (entryValue != entryCount)
            {
                throw InvalidBody();
            }

            var blocks = ImmutableArray.CreateBuilder<Block>(3);
            var observedValuesByBlock = new int[3][];
            var declaredLiveOuts = new ImmutableArray<int>[3];
            var entryOperations = ImmutableArray<Instruction>.Empty;
            var entryTerminator = default(Instruction);
            bool[]? entryPlaceState = null;
            for (var blockIndex = 0; blockIndex < 3; blockIndex++)
            {
                var start = checked(blockDataStart + (int)blockOffsets[blockIndex]);
                var end = checked(blockDataStart + (int)blockOffsets[blockIndex + 1]);
                if (start >= end || terminatorOffsets[blockIndex] >= end - start)
                {
                    throw InvalidBody();
                }

                _position = start;
                _blockEnd = end;
                _blockStart = start;
                _currentBlock = blockIndex;
                _ordinal = 0;
                _observedValues = Filled(valueCount, -1);
                _observedPlaces = Filled(placeCount, -1);
                _maximumStructural = Math.Max(_maximumStructural,
                    Math.Max(blockIndex + 1, definitionStarts[blockIndex + 1]));

                if (Local() != 0)
                {
                    throw InvalidBody();
                }

                var parameterValues = ReadIndexList(valueCount);
                if (blockIndex == 0)
                {
                    if (!parameterValues.AsSpan().SequenceEqual(entryValues.ToArray()))
                    {
                        throw InvalidBody();
                    }
                }
                else if (!parameterValues.IsEmpty)
                {
                    throw InvalidBody();
                }

                var liveIns = ReadIndexList(valueCount);
                var liveOuts = ReadIndexList(valueCount);
                declaredLiveOuts[blockIndex] = liveOuts;
                var inherited = ReadInheritedPlaces();
                var starting = PlaceList();
                var ending = PlaceList();
                var declaredValueLastUses = LastUses(valueCount);
                var declaredPlaceLastUses = LastUses(placeCount);
                var operationCount = Local();
                if (operationCount > Remaining / 2
                    || (blockIndex == 0
                        ? !liveIns.IsEmpty || !inherited.IsEmpty
                            || !IsSequential(starting, placeCount)
                            || !ending.IsEmpty
                        : !starting.IsEmpty
                            || !HasEveryPlace(inherited, placeCount)))
                {
                    throw InvalidBody();
                }

                foreach (var place in starting)
                {
                    _active[place - 1] = true;
                    Place(place);
                }
                foreach (var (place, initialized) in inherited)
                {
                    _active[place - 1] = true;
                    _initialized[place - 1] = initialized;
                    Place(place);
                }

                _defined = definitionStarts[blockIndex];
                if (blockIndex == 0)
                {
                    _defined = entryCount;
                }

                var operations = ImmutableArray.CreateBuilder<Instruction>(operationCount);
                for (_ordinal = 0; _ordinal < operationCount; _ordinal++)
                {
                    if ((ulong)(_position - _blockStart) >= terminatorOffsets[blockIndex])
                    {
                        throw InvalidBody();
                    }
                    operations.Add(Operation());
                    if ((ulong)(_position - _blockStart) > terminatorOffsets[blockIndex])
                    {
                        throw InvalidBody();
                    }
                }

                if (_defined != definitionStarts[blockIndex + 1]
                    || (ulong)(_position - _blockStart) != terminatorOffsets[blockIndex])
                {
                    throw InvalidBody();
                }

                foreach (var place in ending)
                {
                    Place(place, operationCount);
                    _active[place - 1] = false;
                }

                _ordinal = operationCount;
                var terminator = blockIndex == 0
                    ? BranchTerminator()
                    : Terminator(signature);
                if (_position != _blockEnd)
                {
                    throw InvalidBody();
                }

                if (blockIndex == 0)
                {
                    if (!ending.IsEmpty
                        || terminator.TrueTarget != 2 || terminator.FalseTarget != 3)
                    {
                        throw InvalidBody();
                    }
                    entryOperations = operations.MoveToImmutable();
                    entryTerminator = terminator;
                    entryPlaceState = (bool[])_initialized.Clone();
                }
                else if (!IsSequential(ending, placeCount))
                {
                    throw InvalidBody();
                }

                AuditLastUses(declaredValueLastUses, _observedValues);
                AuditLastUses(declaredPlaceLastUses, _observedPlaces);
                observedValuesByBlock[blockIndex] = (int[])_observedValues.Clone();
                blocks.Add(new Block(parameterValues, liveIns, liveOuts, inherited,
                    starting, ending, blockIndex == 0 ? entryOperations : operations.MoveToImmutable(),
                    terminator));
            }

            var expectedWidth = _maximumStructural <= byte.MaxValue ? 1
                : _maximumStructural <= ushort.MaxValue ? 2 : 4;
            if (_position != _body.Length || _width != expectedWidth)
            {
                throw InvalidBody();
            }

            if (entryTerminator is null || entryTerminator.Operands.Length != 1)
            {
                throw InvalidBody();
            }

            var trueCondition = entryTerminator.Operands[0];
            Instruction? conditionCall = null;
            foreach (var operation in entryOperations)
            {
                if (operation.Result == trueCondition)
                {
                    conditionCall = operation;
                }
            }
            if (conditionCall is null || entryPlaceState is null || conditionCall.Opcode != 0x12
                || conditionCall.MethodRow == 0
                || Type(_valueTypes[trueCondition - 1]) != 1
                || (conditionCall.OutputPlaces.IsDefaultOrEmpty && _ruleRevision < 6)
                || entryOperations.IsEmpty || entryOperations[^1].Result != trueCondition
                || _valueOccurrences[trueCondition - 1] != 1)
            {
                throw InvalidBody();
            }

            var expectedTrue = (bool[])entryPlaceState.Clone();
            foreach (var place in conditionCall.OutputPlaces)
            {
                expectedTrue[place - 1] = true;
            }
            foreach (var place in blocks[1].InheritedPlaces)
            {
                if (place.Initialized != expectedTrue[place.Place - 1])
                {
                    throw InvalidBody();
                }
            }
            foreach (var place in blocks[2].InheritedPlaces)
            {
                if (place.Initialized != entryPlaceState[place.Place - 1])
                {
                    throw InvalidBody();
                }
            }

            for (var blockIndex = 1; blockIndex < 3; blockIndex++)
            {
                var expectedLiveIns = ImmutableArray.CreateBuilder<int>();
                for (var value = 1; value <= valueCount; value++)
                {
                    if (value - 1 < definitionStarts[1]
                        && observedValuesByBlock[blockIndex][value - 1] >= 0)
                    {
                        expectedLiveIns.Add(value);
                    }
                }
                if (!expectedLiveIns.ToImmutable().AsSpan()
                    .SequenceEqual(blocks[blockIndex].LiveIns.AsSpan())
                    || !blocks[blockIndex].LiveOuts.IsEmpty)
                {
                    throw InvalidBody();
                }
            }
            var expectedLiveOuts = new bool[valueCount + 1];
            foreach (var value in blocks[1].LiveIns)
            {
                expectedLiveOuts[value] = true;
            }
            foreach (var value in blocks[2].LiveIns)
            {
                expectedLiveOuts[value] = true;
            }
            var liveOutIndex = 0;
            for (var value = 1; value <= valueCount; value++)
            {
                if (expectedLiveOuts[value]
                    && (liveOutIndex >= declaredLiveOuts[0].Length
                        || declaredLiveOuts[0][liveOutIndex++] != value))
                {
                    throw InvalidBody();
                }
            }
            if (liveOutIndex != declaredLiveOuts[0].Length)
            {
                throw InvalidBody();
            }

            for (var value = 0; value < valueCount; value++)
            {
                if (_observedValueBlocks[value] != _valueUses[value])
                {
                    throw InvalidBody();
                }
            }
            for (var place = 0; place < placeCount; place++)
            {
                if (_observedPlaceBlocks[place] != _placeUses[place])
                {
                    throw InvalidBody();
                }
            }
            foreach (var used in _usedTypes)
            {
                if (!used)
                {
                    throw InvalidBody();
                }
            }

            var values = ImmutableArray.CreateBuilder<ValueInfo>(valueCount);
            for (var index = 0; index < valueCount; index++)
            {
                values.Add(new ValueInfo(_valueTypes[index], _valueKinds[index],
                    _definitions[index] == uint.MaxValue ? -1 : (int)_definitions[index],
                    _observedValues[index], _handlePlaces[index], _valueParameters[index]));
            }
            var places = ImmutableArray.CreateBuilder<PlaceInfo>(placeCount);
            for (var index = 0; index < placeCount; index++)
            {
                places.Add(new PlaceInfo(_placeTypes[index], _placeParameters[index],
                    _observedPlaces[index], _initialized[index]));
            }

            return new Body(ImmutableArray.CreateRange(_map), places.MoveToImmutable(),
                values.MoveToImmutable(), entryOperations, entryTerminator,
                blocks[0].StartingPlaces, blocks[0].EndingPlaces, effects,
                blocks.MoveToImmutable());
        }

        private static bool IsSequential(ImmutableArray<int> values, int count)
        {
            if (values.Length != count)
            {
                return false;
            }
            for (var index = 0; index < count; index++)
            {
                if (values[index] != index + 1)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasEveryPlace(
            ImmutableArray<(int Place, bool Initialized)> places, int count)
        {
            if (places.Length != count)
            {
                return false;
            }
            for (var index = 0; index < count; index++)
            {
                if (places[index].Place != index + 1)
                {
                    return false;
                }
            }
            return true;
        }

        private Instruction BranchTerminator()
        {
            var offset = _position - _blockStart;
            var (opcode, referenceWidth, countWidth, dataWidth) = Envelope();
            if (opcode != 0x81 || countWidth != 1 || dataWidth != 0)
            {
                throw InvalidBody();
            }

            var condition = Number(referenceWidth);
            if (UseValue(condition) != 1)
            {
                throw InvalidBody();
            }

            var trueTarget = ReadBranchTarget(referenceWidth, countWidth);
            var falseTarget = ReadBranchTarget(referenceWidth, countWidth);
            return new Instruction(opcode, offset, 0, 0, 0, 0, 0, 0, [condition],
                trueTarget, falseTarget);
        }

        private int ReadBranchTarget(int referenceWidth, int countWidth)
        {
            var target = Number(referenceWidth);
            if (Number(countWidth) != 0)
            {
                throw InvalidBody();
            }
            return target;
        }

        private ImmutableArray<int> ReadIndexList(int maximum)
        {
            var count = Local();
            if (count > maximum || count > Remaining / _width)
            {
                throw InvalidBody();
            }

            var values = ImmutableArray.CreateBuilder<int>(count);
            var previous = 0;
            for (var index = 0; index < count; index++)
            {
                var value = Local();
                if (value <= previous || value > maximum)
                {
                    throw InvalidBody();
                }
                values.Add(value);
                previous = value;
            }
            return values.MoveToImmutable();
        }

        private ImmutableArray<(int Place, bool Initialized)> ReadInheritedPlaces()
        {
            var count = Local();
            if (count > _active.Length || count > Remaining / ( _width + 1))
            {
                throw InvalidBody();
            }

            var places = ImmutableArray.CreateBuilder<(int, bool)>(count);
            var previous = 0;
            for (var index = 0; index < count; index++)
            {
                var place = Local();
                var state = Byte();
                if (place <= previous || place > _active.Length || state > 1)
                {
                    throw InvalidBody();
                }
                places.Add((place, state != 0));
                previous = place;
            }
            return places.MoveToImmutable();
        }

        private Instruction Operation()
        {
            var offset = _position - _blockStart;
            var (opcode, referenceWidth, countWidth, dataWidth) = Envelope();
            if (opcode is not (>= 1 and <= 0x0B or 0x0D or 0x0E or 0x12
                or >= 0x15 and <= 0x1F))
            {
                throw InvalidBody();
            }

            var operands = ImmutableArray.CreateBuilder<int>();
            var result = opcode == 0x0E ? 0 : Number(referenceWidth);
            var secondResult = 0;
            var typeId = 0;
            byte kind = 0;
            ulong data = 0;
            var methodRow = 0;
            ushort resultType = 0;
            byte resultKind = 0;
            var placeId = 0;
            var outputPlaces = ImmutableArray<int>.Empty;

            // Results are reserved only after operands have been checked, preventing self-use.
            if (opcode is 1 or 2)
            {
                typeId = Number(referenceWidth);
                resultType = Type(typeId);
                if (resultType >= 0x100)
                {
                    throw InvalidBody();
                }

                data = Unsigned(dataWidth);
                if (opcode == 1)
                {
                    if (BitWidth(resultType) < 64 && data >> BitWidth(resultType) != 0)
                    {
                        throw InvalidBody();
                    }
                }
                else
                {
                    var shift = 64 - dataWidth * 8;
                    var signed = unchecked((long)(data << shift)) >> shift;
                    if (!Integer(resultType) || !(resultType switch
                    {
                        8 => signed is >= int.MinValue and <= int.MaxValue,
                        9 => true,
                        0x0D => signed is >= 0 and <= uint.MaxValue,
                        _ => signed >= 0,
                    }))
                    {
                        throw InvalidBody();
                    }

                    data = unchecked((ulong)signed);
                }
            }
            else if (opcode == 0x0B)
            {
                placeId = Number(referenceWidth);
                Place(placeId);
                operands.Add(placeId);
                resultType = Type(_placeTypes[placeId - 1]);
                resultKind = 1;
            }
            else if (opcode is 0x0D or 0x0E)
            {
                var handle = Number(referenceWidth);
                placeId = UseHandle(handle);
                operands.Add(handle);
                if (opcode == 0x0D)
                {
                    if (!_initialized[placeId - 1])
                    {
                        throw InvalidBody();
                    }

                    resultType = Type(_placeTypes[placeId - 1]);
                }
                else
                {
                    if (_initialized[placeId - 1]
                        || Operand(referenceWidth, operands) != Type(_placeTypes[placeId - 1]))
                    {
                        throw InvalidBody();
                    }

                    _initialized[placeId - 1] = true;
                }
            }
            else if (opcode == 0x12)
            {
                var localMethod = Number(referenceWidth);
                // A single self-contained module has identical local and table method ordinals.
                if (localMethod == 0 || localMethod > _methods.Count)
                {
                    throw InvalidBody();
                }

                methodRow = localMethod;
                var signature = ValidateMethod(methodRow, _bodies, _methods, _signatures,
                    _parameters, _types, _contracts, _ruleRevision, out _);
                resultType = ResultType(_types, _signatures.GetResultTypeSpec(signature));
                if ((result == 0) != (resultType == 0))
                {
                    throw InvalidBody();
                }

                var (first, count) = _signatures.GetParamRange(signature);
                if (Number(countWidth) != count || count > Remaining / referenceWidth)
                {
                    throw InvalidBody();
                }

                var destinations = new HashSet<int>();
                var outputPlaceIds = ImmutableArray.CreateBuilder<int>();
                for (var argument = 0; argument < count; argument++)
                {
                    var valueId = Number(referenceWidth);
                    operands.Add(valueId);
                    var type = TrRmCompactLinearBodyReader.ResolveType(_types,
                        _parameters.GetTypeSpec(first + argument));
                    switch (_parameters.GetMode(first + argument))
                    {
                        case 0:
                        {
                            if (UseValue(valueId) != type)
                            {
                                throw InvalidBody();
                            }

                            break;
                        }
                        case 2:
                        {
                            var place = UseHandle(valueId);
                            if (Type(_valueTypes[valueId - 1]) != type
                                || _placeParameters[place - 1] != -1 || _initialized[place - 1]
                                || !destinations.Add(place))
                            {
                                throw InvalidBody();
                            }
                            outputPlaceIds.Add(place);

                            break;
                        }
                        default:
                        {
                            throw InvalidBody();
                        }
                    }
                }

                // Arguments are existing scalar snapshots or handles. No admitted argument
                // expression can access an alias during the call's initialization authority.
                foreach (var destination in destinations)
                {
                    _initialized[destination - 1] = resultType != 1;
                }
                outputPlaces = outputPlaceIds.ToImmutable();
            }
            else if (opcode is 8 or 9)
            {
                typeId = Number(referenceWidth);
                resultType = Type(typeId);
                if (resultType < 0x100
                    || opcode == 8 && Operand(referenceWidth, operands) != (resultType & 0xFF))
                {
                    throw InvalidBody();
                }
            }
            else if (opcode == 0x0A)
            {
                if (Operand(referenceWidth, operands) < 0x100)
                {
                    throw InvalidBody();
                }

                resultType = 1;
            }
            else if (opcode == 3)
            {
                resultType = Operand(referenceWidth, operands);
            }
            else if (opcode is 6 or 0x16 or 0x1A or 0x1B or 0x1E or 0x1F)
            {
                typeId = Number(referenceWidth);
                resultType = Type(typeId);
                kind = opcode == 6 ? Byte() : (byte)0;
                var source = Operand(referenceWidth, operands);
                var valid = opcode switch
                {
                    0x1A => Float(source) && Float(resultType) && source != resultType,
                    0x1B or 0x1F => opcode == 0x1B
                        ? Integer(source) && Float(resultType)
                        : Float(source) && Integer(resultType),
                    0x1E => source == 3 && Float(resultType),
                    0x16 => (Integer(source) || source == 3)
                        && (Integer(resultType) || resultType == 3),
                    _ => Integer(source) && Integer(resultType) && kind switch
                    {
                        1 or 2 => true,
                        3 => BitWidth(source) == BitWidth(resultType),
                        _ => false,
                    },
                };
                if (!valid)
                {
                    throw InvalidBody();
                }
            }
            else if (opcode is 4 or 0x17 or 0x1C)
            {
                kind = opcode == 4 ? Byte() : (byte)0;
                resultType = Operand(referenceWidth, operands);
                var valid = opcode switch
                {
                    0x17 => resultType is 8 or 9,
                    0x1C => Float(resultType),
                    _ => kind switch
                    {
                        1 => resultType == 1,
                        2 or 3 => Integer(resultType),
                        _ => false,
                    },
                };
                if (!valid)
                {
                    throw InvalidBody();
                }
            }
            else if (opcode == 7)
            {
                var condition = Operand(referenceWidth, operands);
                resultType = Operand(referenceWidth, operands);
                if (condition != 1 || Operand(referenceWidth, operands) != resultType)
                {
                    throw InvalidBody();
                }
            }
            else
            {
                if (opcode == 0x18)
                {
                    secondResult = Number(referenceWidth);
                }
                else
                {
                    kind = Byte();
                }

                var left = Operand(referenceWidth, operands);
                var right = Operand(referenceWidth, operands);
                var valid = opcode switch
                {
                    0x18 => left == right && Integer(left),
                    0x19 => kind is >= 1 and <= 6 && left == right && Float(left),
                    0x1D => kind is >= 1 and <= 4 && left == right && Float(left),
                    0x15 => kind is >= 1 and <= 7 && (kind is 6 or 7
                        ? Integer(left) && right == 0x0D : Integer(left) && left == right),
                    _ => kind is >= 1 and <= 12 && left == right
                        && (kind is 4 or 5
                            ? left is 1 or 3 or 8 or 9 or 0x0D or 0x0E or 0x103
                            : Integer(left)),
                };
                if (!valid)
                {
                    throw InvalidBody();
                }

                resultType = opcode == 0x19 || opcode == 5 && kind is >= 4 and <= 9
                    ? (ushort)1 : left;
            }

            if (opcode != 0x0E && (opcode != 0x12 || result != 0))
            {
                Define(result, offset, resultType, resultKind, opcode == 0x0B ? placeId : 0);
                if (opcode == 0x18)
                {
                    Define(secondResult, offset, resultType, 0, 0);
                }
            }

            return new Instruction(opcode, offset, result, secondResult, typeId, kind, data,
                methodRow, operands.ToImmutable(), OutputPlaces: outputPlaces);
        }

        private Instruction Terminator(int signature)
        {
            var offset = _position - _blockStart;
            var (opcode, referenceWidth, countWidth, dataWidth) = Envelope();
            if (countWidth != 1 || dataWidth != 0)
            {
                throw InvalidBody();
            }

            var operands = ImmutableArray<int>.Empty;
            byte reason = 0;
            if (opcode == 0x83)
            {
                var value = Number(referenceWidth);
                var resultType = ResultType(_types, _signatures.GetResultTypeSpec(signature));
                if (value == 0 ? resultType != 0 : UseValue(value) != resultType)
                {
                    throw InvalidBody();
                }

                operands = [value];
                for (var place = 0; place < _active.Length; place++)
                {
                    // Retirement ends access, not the independent formal publication fact.
                    if (_active[place] || _placeParameters[place] >= 0 && !_initialized[place])
                    {
                        throw InvalidBody();
                    }
                }
            }
            else if (opcode == 0x87 && referenceWidth == 1)
            {
                reason = Byte();
                if (reason != 1)
                {
                    throw InvalidBody();
                }
            }
            else
            {
                throw InvalidBody();
            }

            return new Instruction(opcode, offset, 0, 0, 0, reason, 0, 0, operands);
        }

        private void Define(int value, int offset, ushort type, byte kind, int place)
        {
            if (_defined >= _valueTypes.Length || value != _defined + 1
                || _definitions[_defined] != (uint)offset
                || _valueKinds[_defined] != kind || Type(_valueTypes[_defined]) != type)
            {
                throw InvalidBody();
            }

            _handlePlaces[_defined++] = place;
        }

        private ushort Operand(int width, ImmutableArray<int>.Builder operands)
        {
            var value = Number(width);
            operands.Add(value);
            return UseValue(value);
        }

        private ushort UseValue(int value)
        {
            if (value == 0 || value > _defined || _valueKinds[value - 1] != 0)
            {
                throw InvalidBody();
            }

            ObserveValue(value);
            _observedValues[value - 1] = _ordinal;
            return Type(_valueTypes[value - 1]);
        }

        private int UseHandle(int value)
        {
            if (value == 0 || value > _defined || _valueKinds[value - 1] != 1)
            {
                throw InvalidBody();
            }

            var place = _handlePlaces[value - 1];
            if (place <= 0 || place > _placeTypes.Length
                || Type(_valueTypes[value - 1]) != Type(_placeTypes[place - 1]))
            {
                throw InvalidBody();
            }
            Place(place);
            ObserveValue(value);
            _observedValues[value - 1] = _ordinal;
            return place;
        }

        private void ObserveValue(int value)
        {
            if (_valueOccurrences.Length != 0)
            {
                _valueOccurrences[value - 1]++;
                if (_valueLastSeenBlock[value - 1] != _currentBlock)
                {
                    _valueLastSeenBlock[value - 1] = _currentBlock;
                    _observedValueBlocks[value - 1]++;
                }
            }
        }

        private void Place(int place, int? ordinal = null)
        {
            if (place == 0 || place > _active.Length || !_active[place - 1])
            {
                throw InvalidBody();
            }

            var observedAt = ordinal ?? _ordinal;
            _observedPlaces[place - 1] = Math.Max(_observedPlaces[place - 1], observedAt);
            if (_observedPlaceBlocks.Length != 0
                && _placeLastSeenBlock[place - 1] != _currentBlock)
            {
                _placeLastSeenBlock[place - 1] = _currentBlock;
                _observedPlaceBlocks[place - 1]++;
            }
        }

        private ushort Type(int id)
        {
            if (id <= 0 || id > _map.Length)
            {
                throw InvalidBody();
            }

            _usedTypes[id - 1] = true;
            return _map[id - 1].TypeKey;
        }

        private ImmutableArray<int> PlaceList()
        {
            var count = Local();
            if (count > _active.Length || count > Remaining / _width)
            {
                throw InvalidBody();
            }

            var places = ImmutableArray.CreateBuilder<int>(count);
            var previous = 0;
            for (var index = 0; index < count; index++)
            {
                var place = Local();
                if (place <= previous || place > _active.Length)
                {
                    throw InvalidBody();
                }

                places.Add(place);
                previous = place;
            }

            return places.MoveToImmutable();
        }

        private int[] LastUses(int maximum)
        {
            var count = Local();
            if (count > maximum || count > Remaining / (2 * _width))
            {
                throw InvalidBody();
            }

            var uses = Filled(maximum, -1);
            var previous = 0;
            for (var index = 0; index < count; index++)
            {
                var id = Local();
                if (id <= previous || id > maximum)
                {
                    throw InvalidBody();
                }

                uses[id - 1] = Local();
                previous = id;
            }

            return uses;
        }

        private static void AuditUses(int[] declared, int[] observed, int[] blocks)
        {
            for (var index = 0; index < declared.Length; index++)
            {
                if (declared[index] != observed[index]
                    || blocks[index] != (observed[index] == -1 ? 0 : 1))
                {
                    throw InvalidBody();
                }
            }
        }

        private static void AuditLastUses(int[] declared, int[] observed)
        {
            if (!declared.AsSpan().SequenceEqual(observed))
            {
                throw InvalidBody();
            }
        }

        private (byte Opcode, int References, int Counts, int Data) Envelope()
        {
            var opcode = Byte();
            if (opcode != 0xF0)
            {
                return (opcode, 1, 1, opcode is 1 or 2 ? 1 : 0);
            }

            var layout = Byte();
            opcode = Byte();
            if (layout == 0 || (layout & 0xC0) != 0 || (layout & 3) == 3
                || ((layout >> 2) & 3) == 3 || opcode != 0x12 && (layout & 0x0C) != 0
                || opcode is not (1 or 2) && (layout & 0x30) != 0)
            {
                throw InvalidBody();
            }

            return (opcode, 1 << (layout & 3), 1 << ((layout >> 2) & 3),
                opcode is 1 or 2 ? 1 << ((layout >> 4) & 3) : 0);
        }

        private int Local()
        {
            var value = Number(_width);
            _maximumStructural = Math.Max(_maximumStructural, value);
            return value;
        }

        private int Number(int width)
        {
            var value = Unsigned(width);
            return value <= int.MaxValue ? (int)value : throw InvalidBody();
        }

        private byte Byte() => (byte)Unsigned(1);
        private int Remaining => Math.Min(_body.Length, _blockEnd) - _position;

        private ulong Unsigned(int width)
        {
            if (width > Remaining)
            {
                throw InvalidBody();
            }

            var bytes = _body.Slice(_position, width);
            _position += width;
            return width switch
            {
                1 => bytes[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
                _ => throw InvalidBody(),
            };
        }

        private static int[] Filled(int count, int value)
        {
            var values = new int[count];
            Array.Fill(values, value);
            return values;
        }
    }

    private static int BitWidth(ushort type)
        => type switch { 1 => 1, 3 => 16, 8 or 0x0D or 0x14 => 32, _ => 64 };

    private static bool Integer(ushort type) => type is 8 or 9 or 0x0D or 0x0E;
    private static bool Float(ushort type) => type is 0x14 or 0x15;
    private static bool Scalar(ushort type)
        => type is 1 or 3 or 8 or 9 or 0x0D or 0x0E or 0x14 or 0x15;
    private static bool NonBooleanScalar(ushort type) => Scalar(type) && type != 1;

    private static InvalidDataException InvalidBody()
        => new("Invalid or unsupported provisional TRRM ScalarInitOutput Body.");
}
