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
        int TypeId, byte Kind, ulong Data, int MethodRow, ImmutableArray<int> Operands);
    internal sealed record Body(ImmutableArray<TypeInfo> Types, ImmutableArray<PlaceInfo> Places,
        ImmutableArray<ValueInfo> Values, ImmutableArray<Instruction> Operations,
        Instruction Terminator, ImmutableArray<int> StartingPlaces, ImmutableArray<int> EndingPlaces,
        uint Effects);

    internal static Body Read(TrRmBodyTableReader bodies, int methodRow,
        TrRmModuleTableReader modules, TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, ReadOnlySpan<byte> contractSection = default)
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
            types, contracts, out var effects);
        var parser = new Parser(bodies.GetRow(methods.GetBodyRow(methodRow)), types,
            bodies, methods, signatures, parameters, contracts);
        return parser.Read(signature, effects);
    }

    private static int ValidateMethod(int method, TrRmBodyTableReader bodies,
        TrRmMethodDefTableReader methods,
        TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
        TrRmTypeSpecTableReader types, TrRmVariableTableIndex contracts, out uint effects)
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
            if (mode == 2 && type == 8)
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
        if (hasOutput && (methods.GetFlags(method) == 2 || body.IsEmpty || body[0] > 2
            || ResultType(types, signatures.GetResultTypeSpec(signature)) is not (0 or 8)))
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
        private int _position;
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
        private int[] _observedValues;
        private int[] _observedPlaces;
        private int[] _handlePlaces;
        private int[] _valueParameters;
        private int[] _placeParameters;
        private bool[] _active;
        private bool[] _initialized;

        internal Parser(ReadOnlySpan<byte> body, TrRmTypeSpecTableReader types,
            TrRmBodyTableReader bodies, TrRmMethodDefTableReader methods,
            TrRmMethodSigTableReader signatures, TrRmParamTableReader parameters,
            TrRmVariableTableIndex contracts)
        {
            _body = body;
            _types = types;
            _bodies = bodies;
            _methods = methods;
            _signatures = signatures;
            _parameters = parameters;
            _contracts = contracts;
            _position = 0;
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
            _observedValues = [];
            _observedPlaces = [];
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
            if (Local() != 1)
            {
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
            _placeParameters = Filled(placeCount, -1);
            _active = new bool[placeCount];
            _initialized = new bool[placeCount];
            var placeTypes = new int[placeCount];
            for (var index = 0; index < placeCount; index++)
            {
                placeTypes[index] = Local();
                if (Type(placeTypes[index]) != 8 || Local() != 0 || Byte() != 4
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
                    || _valueKinds[index] == 1 && Type(_valueTypes[index]) != 8)
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
                    if (formalPlace >= placeCount)
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
                places.Add(new PlaceInfo(placeTypes[index], _placeParameters[index],
                    _observedPlaces[index], _initialized[index]));
            }

            return new Body(ImmutableArray.CreateRange(_map), places.MoveToImmutable(),
                values.MoveToImmutable(), operations.MoveToImmutable(), terminator,
                starting, ending, effects);
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
                resultType = 8;
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

                    resultType = 8;
                }
                else
                {
                    if (_initialized[placeId - 1] || Operand(referenceWidth, operands) != 8)
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
                    _parameters, _types, _contracts, out _);
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
                    _initialized[destination - 1] = true;
                }
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
                methodRow, operands.ToImmutable());
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
            Place(place);
            _observedValues[value - 1] = _ordinal;
            return place;
        }

        private void Place(int place)
        {
            if (place == 0 || place > _active.Length || !_active[place - 1])
            {
                throw InvalidBody();
            }

            _observedPlaces[place - 1] = Math.Max(_observedPlaces[place - 1], _ordinal);
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
        private int Remaining => _body.Length - _position;

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

    private static InvalidDataException InvalidBody()
        => new("Invalid or unsupported provisional TRRM ScalarInitOutput Body.");
}
