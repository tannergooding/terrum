using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static bool HasOutputFeature(BoundFunction function)
    {
        if (HasOutputParameters(function))
        {
            return true;
        }

        return ContainsOutputFeature(function.Body);
    }

    private static bool HasOutputParameters(BoundFunction function)
        => function.Variables.Take(function.ParameterCount)
            .Any(static variable => variable.IsOut || variable.IsInit);

    private static bool ContainsOutputFeature(BoundStatement statement)
    {
        return statement switch
        {
            BoundBlock block => block.Statements.Any(ContainsOutputFeature),
            BoundVacantLocal => true,
            BoundLocal local => ContainsOutputFeature(local.Initializer),
            BoundAssignment assignment => ContainsOutputFeature(assignment.Value),
            BoundExpressionStatement expression => ContainsOutputFeature(expression.Expression),
            BoundReturn { Value: not null } result => ContainsOutputFeature(result.Value),
            BoundIf conditional => ContainsOutputFeature(conditional.Condition)
                || ContainsOutputFeature(conditional.Then)
                || conditional.Else is not null && ContainsOutputFeature(conditional.Else),
            BoundWhile loop => ContainsOutputFeature(loop.Condition)
                || ContainsOutputFeature(loop.Body)
                || loop.Iterator is not null && ContainsOutputFeature(loop.Iterator),
            BoundUnsafe unsafeBlock => ContainsOutputFeature(unsafeBlock.Body),
            BoundRequire require => ContainsOutputFeature(require.Condition),
            _ => false,
        };
    }

    private static bool ContainsOutputFeature(BoundExpression expression)
    {
        return expression switch
        {
            BoundOutArgument => true,
            BoundBinary binary => ContainsOutputFeature(binary.Left)
                || ContainsOutputFeature(binary.Right),
            BoundUnary unary => ContainsOutputFeature(unary.Operand),
            BoundCast cast => ContainsOutputFeature(cast.Operand),
            BoundConditional conditional => ContainsOutputFeature(conditional.Condition)
                || ContainsOutputFeature(conditional.WhenTrue)
                || ContainsOutputFeature(conditional.WhenFalse),
            BoundCall call => call.Arguments.Any(ContainsOutputFeature),
            _ => false,
        };
    }

    private static byte[] EncodeOutputBody(BoundFunction function, BoundProgram program)
        => new OutputBodyWriter(function, program).Encode();

    private static void WriteCallInstruction(List<byte> bytes, int referenceWidth, int countWidth)
    {
        if (referenceWidth == 1 && countWidth == 1)
        {
            bytes.Add(0x12);
            return;
        }

        bytes.Add(0xF0);
        bytes.Add((byte)(WidthCode(referenceWidth) | (WidthCode(countWidth) << 2)));
        bytes.Add(0x12);
    }

    private static ImmutableArray<BoundStatement> DecodeOutputBody(
        TrRmScalarInitOutputBodyReader.Body body, TrRmTypeSpecTableReader types,
        ImmutableArray<BoundVariable>.Builder variables,
        ImmutableArray<BoundFunction>.Builder functions)
    {
        var statements = ImmutableArray.CreateBuilder<BoundStatement>(
            body.Operations.Length + body.Places.Length + 1);
        var placeVariables = new int[body.Places.Length];
        for (var index = 0; index < body.Places.Length; index++)
        {
            var place = body.Places[index];
            if (OutputType(body, types, place.TypeId) != PrimitiveType.Int32)
            {
                throw Invalid("The executable place profile requires int32 referents.");
            }

            if (place.ParameterOrdinal >= 0)
            {
                if (place.ParameterOrdinal >= variables.Count
                    || !variables[place.ParameterOrdinal].IsOut
                    || !variables[place.ParameterOrdinal].IsInit)
                {
                    throw Invalid("An output place does not match its formal parameter.");
                }
                placeVariables[index] = place.ParameterOrdinal;
            }
            else
            {
                placeVariables[index] = variables.Count;
                variables.Add(new BoundVariable($"place{index + 1}", PrimitiveType.Int32,
                    false, default)
                {
                    IsInitializationLocal = true,
                });
                statements.Add(new BoundVacantLocal(placeVariables[index], default));
            }
        }

        var valueVariables = new int[body.Values.Length];
        Array.Fill(valueVariables, -1);
        var valueExpressions = new BoundExpression?[body.Values.Length];
        for (var index = 0; index < body.Values.Length; index++)
        {
            var value = body.Values[index];
            var type = OutputType(body, types, value.TypeId);
            if (!IsOutputScalarType(type))
            {
                throw Invalid("The executable place profile requires scalar values.");
            }

            if (value.Kind != 0)
            {
                continue;
            }

            if (value.ParameterOrdinal >= 0)
            {
                if (value.ParameterOrdinal >= variables.Count
                    || variables[value.ParameterOrdinal].Type != type
                    || variables[value.ParameterOrdinal].IsOut
                    || variables[value.ParameterOrdinal].IsInit)
                {
                    throw Invalid("A scalar entry value does not match its by-value formal.");
                }
                valueExpressions[index] = new BoundRead(value.ParameterOrdinal, type, default);
            }
            else
            {
                valueVariables[index] = variables.Count;
                variables.Add(new BoundVariable($"value{index + 1}", type, false, default));
            }
        }

        BoundExpression ReadValue(int valueId)
        {
            if (valueId <= 0 || valueId > valueExpressions.Length
                || valueExpressions[valueId - 1] is not { } expression)
            {
                throw Invalid("An executable place operation uses a non-scalar value.");
            }

            return expression;
        }

        int PlaceVariable(int placeId)
        {
            if (placeId <= 0 || placeId > placeVariables.Length)
            {
                throw Invalid("An executable place operation uses an invalid place.");
            }

            return placeVariables[placeId - 1];
        }

        int PlaceForHandle(int valueId)
        {
            if (valueId <= 0 || valueId > body.Values.Length
                || body.Values[valueId - 1].Kind != 1
                || body.Values[valueId - 1].PlaceId == 0)
            {
                throw Invalid("An output argument is not a place handle.");
            }

            return body.Values[valueId - 1].PlaceId;
        }

        void DefineValue(int valueId, BoundExpression expression)
        {
            if (valueId <= 0 || valueId > valueVariables.Length
                || valueVariables[valueId - 1] < 0
                || variables[valueVariables[valueId - 1]].Type != expression.Type)
            {
                throw Invalid("An executable place result is not a scalar definition.");
            }

            var variable = valueVariables[valueId - 1];
            valueExpressions[valueId - 1] = new BoundRead(variable, expression.Type, default);
            statements.Add(new BoundLocal(variable, expression, default));
        }

        foreach (var operation in body.Operations)
        {
            switch (operation.Opcode)
            {
                case 0x0B:
                    if (operation.Result <= 0 || operation.Result > body.Values.Length
                        || body.Values[operation.Result - 1].Kind != 1)
                    {
                        throw Invalid("PlaceLocal must define a place handle.");
                    }
                    break;
                case 1:
                case 2:
                {
                    var type = OutputType(body, types, operation.TypeId);
                    if (type != PrimitiveType.Int32)
                    {
                        throw Invalid("The executable place profile currently supports int32 constants.");
                    }

                    DefineValue(operation.Result,
                        new BoundConstant(unchecked((int)operation.Data), type, default));
                    break;
                }
                case 0x0D:
                {
                    if (operation.Operands.Length != 1)
                    {
                        throw Invalid("Load requires one place handle.");
                    }
                    var place = PlaceVariable(PlaceForHandle(operation.Operands[0]));
                    DefineValue(operation.Result,
                        new BoundRead(place, PrimitiveType.Int32, default));
                    break;
                }
                case 0x0E:
                {
                    if (operation.Operands.Length != 2)
                    {
                        throw Invalid("Initialize requires a place handle and a value.");
                    }
                    var place = PlaceVariable(PlaceForHandle(operation.Operands[0]));
                    statements.Add(new BoundAssignment(place, ReadValue(operation.Operands[1]), default));
                    break;
                }
                case 0x12:
                {
                    if (operation.MethodRow <= 0 || operation.MethodRow > functions.Count)
                    {
                        throw Invalid("Call target is outside the module.");
                    }
                    var target = functions[operation.MethodRow - 1];
                    if (!IsOutputCallResultType(target.ReturnType)
                        || operation.Operands.Length != target.ParameterCount)
                    {
                        throw Invalid("Call target is outside the executable place profile.");
                    }
                    var arguments = ImmutableArray.CreateBuilder<BoundExpression>(target.ParameterCount);
                    for (var parameter = 0; parameter < target.ParameterCount; parameter++)
                    {
                        var formal = target.Variables[parameter];
                        var argument = operation.Operands[parameter];
                        if (formal.IsOut || formal.IsInit)
                        {
                            if (!formal.IsOut || !formal.IsInit || formal.Type != PrimitiveType.Int32)
                            {
                                throw Invalid("Only init ref<int32> output arguments are supported.");
                            }

                            var place = PlaceVariable(PlaceForHandle(argument));
                            arguments.Add(new BoundOutArgument(place, false, formal.Type, default)
                            {
                                IsInit = true,
                            });
                        }
                        else
                        {
                            var value = ReadValue(argument);
                            if (formal.Type != value.Type)
                            {
                                throw Invalid("Call argument type does not match its formal.");
                            }
                            arguments.Add(value);
                        }
                    }

                    var call = new BoundCall(operation.MethodRow - 1,
                        arguments.MoveToImmutable(), target.ReturnType, default);
                    if (operation.Result == 0)
                    {
                        statements.Add(new BoundExpressionStatement(call, default));
                    }
                    else
                    {
                        DefineValue(operation.Result, call);
                    }
                    break;
                }
                case 5:
                case 0x19:
                {
                    if (operation.Operands.Length != 2)
                    {
                        throw Invalid("Comparison requires two values.");
                    }

                    var left = ReadValue(operation.Operands[0]);
                    var right = ReadValue(operation.Operands[1]);
                    var isFloat = operation.Opcode == 0x19;
                    var valid = left.Type == right.Type && (operation.Kind switch
                    {
                        1 or 2 when isFloat => IsFloat(left.Type),
                        3 or 4 or 5 or 6 when isFloat => IsFloat(left.Type),
                        4 or 5 => left.Type is PrimitiveType.Bool or PrimitiveType.Char16
                            or PrimitiveType.Int32 or PrimitiveType.Int64,
                        6 or 7 or 8 or 9 => IsInteger(left.Type),
                        _ => false,
                    });
                    if (!valid)
                    {
                        throw Invalid("Comparison operands are outside the executable place profile.");
                    }

                    var token = (isFloat, operation.Kind) switch
                    {
                        (true, 1) or (false, 4) => TokenKind.EqualsEquals,
                        (true, 2) or (false, 5) => TokenKind.BangEquals,
                        (true, 3) or (false, 6) => TokenKind.Less,
                        (true, 4) or (false, 7) => TokenKind.LessEquals,
                        (true, 5) or (false, 8) => TokenKind.Greater,
                        (true, 6) or (false, 9) => TokenKind.GreaterEquals,
                        _ => throw Invalid("Unsupported comparison kind."),
                    };
                    DefineValue(operation.Result,
                        new BoundBinary(token, left, right, PrimitiveType.Bool, default));
                    break;
                }
                case 0x15:
                {
                    if (operation.Operands.Length != 2)
                    {
                        throw Invalid("Binary requires two values.");
                    }
                    var token = operation.Kind switch
                    {
                        1 => TokenKind.Plus,
                        2 => TokenKind.Minus,
                        3 => TokenKind.Star,
                        _ => throw Invalid("The executable place profile has an unsupported Binary kind."),
                    };
                    DefineValue(operation.Result, new BoundBinary(token,
                        ReadValue(operation.Operands[0]), ReadValue(operation.Operands[1]),
                        PrimitiveType.Int32, default));
                    break;
                }
                default:
                    throw Invalid($"Operation 0x{operation.Opcode:X2} is outside the executable place profile.");
            }
        }

        if (body.Terminator.Opcode == 0x83)
        {
            if (body.Terminator.Operands.Length != 1)
            {
                throw Invalid("Return requires one result slot.");
            }

            var result = body.Terminator.Operands[0];
            statements.Add(new BoundReturn(result == 0 ? null : ReadValue(result), default));
        }
        else if (body.Terminator.Opcode == 0x87)
        {
            statements.Add(new BoundRequire(new BoundConstant(0, PrimitiveType.Bool, default), default));
        }
        else
        {
            throw Invalid("Unsupported executable place terminator.");
        }

        return statements.ToImmutable();
    }

    private static PrimitiveType OutputType(TrRmScalarInitOutputBodyReader.Body body,
        TrRmTypeSpecTableReader types, int typeId)
    {
        if (typeId <= 0 || typeId > body.Types.Length)
        {
            throw Invalid("A place Body type ID is outside its map.");
        }

        return BodyTypeFromRow(types, body.Types[typeId - 1].TypeSpecRow);
    }

    private static bool IsOutputScalarType(PrimitiveType type)
        => type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32
            or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Float32 or PrimitiveType.Float64;

    private static bool IsOutputCallResultType(PrimitiveType type)
        => type == PrimitiveType.Void || IsOutputScalarType(type);

    private sealed class OutputBodyWriter
    {
        private readonly BoundFunction _function;
        private readonly BoundProgram _program;
        private readonly int[] _placeByVariable;
        private readonly int[] _currentValues;
        private readonly List<OutputPlace> _places = [default];
        private readonly List<bool> _placeInitialized = [false];
        private readonly List<bool> _placeDeclared = [false];
        private readonly List<int> _placeHandles = [0];
        private readonly List<int> _bodyTypeRows = [];
        private readonly Dictionary<int, int> _bodyTypeIds = [];
        private readonly List<int> _valueTypes = [];
        private readonly List<byte> _valueKinds = [];
        private readonly List<uint> _definitionOffsets = [];
        private readonly List<int> _parameterValues = [];
        private readonly List<byte> _operations = [];
        private readonly Dictionary<int, int> _valueLastUses = [];
        private readonly Dictionary<int, int> _placeLastUses = [];
        private int _operationCount;
        private int _returnValue;
        private bool _ended;

        public OutputBodyWriter(BoundFunction function, BoundProgram program)
        {
            _function = function;
            _program = program;
            _placeByVariable = new int[function.Variables.Length];
            _currentValues = new int[function.Variables.Length];
            if (function.Variables.Length > SemanticVerifier.MaximumVariables
                || function.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32))
            {
                throw Unsupported("The output Body profile supports at most 256 variables and void/int32 results.");
            }

            for (var variable = 0; variable < function.ParameterCount; variable++)
            {
                var parameter = function.Variables[variable];
                if (parameter.IsOut || parameter.IsInit)
                {
                    if (!parameter.IsOut || !parameter.IsInit || parameter.Type != PrimitiveType.Int32)
                    {
                        throw Unsupported("The output Body profile only supports init ref<int32> parameters.");
                    }
                    AddPlace(variable, isParameter: true);
                }
                else
                {
                    if (!IsOutputScalarType(parameter.Type))
                    {
                        throw Unsupported("The output Body profile requires scalar by-value parameters.");
                    }
                    _currentValues[variable] = AddValue(parameter.Type, 0, uint.MaxValue);
                    _parameterValues.Add(_currentValues[variable]);
                }
            }

            for (var variable = function.ParameterCount; variable < function.Variables.Length; variable++)
            {
                var local = function.Variables[variable];
                if (local.IsOut || local.IsInit)
                {
                    throw Unsupported("Output modes are valid only on formal parameters.");
                }
                if (local.IsInitializationLocal)
                {
                    if (local.Type != PrimitiveType.Int32 || local.IsMutable)
                    {
                        throw Unsupported("The output Body profile only supports vacant int32 local places.");
                    }
                    AddPlace(variable, isParameter: false);
                }
                else if (local.Type != PrimitiveType.Int32)
                {
                    throw Unsupported("The output Body profile currently supports only int32 locals.");
                }
            }

            for (var place = 1; place < _places.Count; place++)
            {
                EmitPlaceLocal(place);
            }
        }

        public byte[] Encode()
        {
            if (_function.IsExternal || _function.IsConstructor || _function.IsClassConstructor
                || _function.ClassConstructor != -1 || _function.DeclaringStruct is not null
                || _function.IsStatic || _function.IsCleanup || _function.PropertyName is not null
                || _function.Intrinsic != BoundIntrinsic.None)
            {
                throw Unsupported("The output Body profile requires a module-level ordinary function.");
            }

            foreach (var statement in _function.Body.Statements)
            {
                EmitStatement(statement);
            }
            if (!_ended)
            {
                throw Unsupported("The output Body profile requires one terminal Return.");
            }

            return BuildBody();
        }

        private void AddPlace(int variable, bool isParameter)
        {
            var place = _places.Count;
            _places.Add(new OutputPlace(variable, isParameter));
            _placeInitialized.Add(false);
            _placeDeclared.Add(isParameter);
            _placeHandles.Add(0);
            _placeByVariable[variable] = place;
        }

        private int AddValue(PrimitiveType type, byte kind, uint definitionOffset)
        {
            if (!IsOutputScalarType(type) || kind > 1
                || kind == 1 && type != PrimitiveType.Int32
                || _valueTypes.Count >= SemanticVerifier.MaximumVariables)
            {
                throw Unsupported("The output Body profile supports scalar values within the verifier limit.");
            }

            var typeRow = TypeRow(type);
            _ = BodyTypeId(typeRow);
            _valueTypes.Add(typeRow);
            _valueKinds.Add(kind);
            _definitionOffsets.Add(definitionOffset);
            return _valueTypes.Count;
        }

        private void EmitPlaceLocal(int place)
        {
            var result = _valueTypes.Count + 1;
            var width = Width((ulong)Math.Max(result, place));
            var offset = _operations.Count;
            WriteInstruction(_operations, 0x0B, width, 0);
            Write(_operations, (ulong)result, width);
            Write(_operations, (ulong)place, width);
            _ = AddValue(PrimitiveType.Int32, 1, (uint)offset);
            _placeHandles[place] = result;
            UsePlace(place, _operationCount);
            _operationCount++;
        }

        private void EmitStatement(BoundStatement statement)
        {
            if (_ended)
            {
                throw Unsupported("Statements after the terminal return are not supported.");
            }

            switch (statement)
            {
                case BoundVacantLocal vacant:
                    if ((uint)vacant.Variable >= (uint)_placeByVariable.Length
                        || _placeByVariable[vacant.Variable] == 0)
                    {
                        throw Unsupported("Vacant locals must map to int32 local places.");
                    }
                    var vacantPlace = _placeByVariable[vacant.Variable];
                    if (_placeDeclared[vacantPlace])
                    {
                        throw Unsupported("A local place is declared more than once.");
                    }
                    _placeDeclared[vacantPlace] = true;
                    break;
                case BoundLocal local:
                    if ((uint)local.Variable >= (uint)_currentValues.Length
                        || _placeByVariable[local.Variable] != 0
                        || local.Initializer.Type != PrimitiveType.Int32)
                    {
                        throw Unsupported("The output Body profile supports initialized int32 value locals.");
                    }
                    _currentValues[local.Variable] = EmitValue(local.Initializer);
                    break;
                case BoundAssignment assignment:
                    EmitAssignment(assignment);
                    break;
                case BoundExpressionStatement { Expression: BoundCall call }:
                    if (EmitCall(call) != 0)
                    {
                        throw Unsupported("Only void calls may be used as output-profile statements.");
                    }
                    break;
                case BoundReturn result:
                    EmitReturn(result);
                    break;
                default:
                    throw Unsupported($"Statement {statement.GetType().Name} is outside the output Body profile.");
            }
        }

        private void EmitAssignment(BoundAssignment assignment)
        {
            if ((uint)assignment.Variable >= (uint)_currentValues.Length)
            {
                throw Unsupported("Assignment target is outside the variable inventory.");
            }

            var place = _placeByVariable[assignment.Variable];
            var value = EmitValue(assignment.Value);
            if (_valueTypes[value - 1] != TypeRow(PrimitiveType.Int32))
            {
                throw Unsupported("Output assignment requires an int32 value.");
            }
            if (place == 0)
            {
                if (assignment.Variable < _function.ParameterCount)
                {
                    throw Unsupported("The output Body profile cannot assign by-value parameters.");
                }
                _currentValues[assignment.Variable] = value;
                return;
            }
            if (!_placeDeclared[place] || _placeInitialized[place])
            {
                throw Unsupported("Initialize requires an active vacant place.");
            }

            var handle = _placeHandles[place];
            var width = Width((ulong)Math.Max(handle, value));
            WriteInstruction(_operations, 0x0E, width, 0);
            Write(_operations, (ulong)handle, width);
            Write(_operations, (ulong)value, width);
            UseValue(handle, _operationCount);
            UseValue(value, _operationCount);
            UsePlace(place, _operationCount);
            _operationCount++;
            _placeInitialized[place] = true;
        }

        private int EmitValue(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundRead read:
                    if ((uint)read.Variable >= (uint)_currentValues.Length)
                    {
                        throw Unsupported("Read is outside the variable inventory.");
                    }
                    var place = _placeByVariable[read.Variable];
                    if (place != 0)
                    {
                        if (!_placeDeclared[place] || !_placeInitialized[place])
                        {
                            throw Unsupported("Load requires an initialized active place.");
                        }
                        var handle = _placeHandles[place];
                        var loadResult = _valueTypes.Count + 1;
                        var width = Width((ulong)Math.Max(loadResult, handle));
                        var offset = _operations.Count;
                        WriteInstruction(_operations, 0x0D, width, 0);
                        Write(_operations, (ulong)loadResult, width);
                        Write(_operations, (ulong)handle, width);
                        _ = AddValue(PrimitiveType.Int32, 0, (uint)offset);
                        UseValue(handle, _operationCount);
                        UsePlace(place, _operationCount);
                        _operationCount++;
                        return loadResult;
                    }

                    var current = _currentValues[read.Variable];
                    if (current == 0 || _valueTypes[current - 1] != TypeRow(read.Type))
                    {
                        throw Unsupported("Read of an uninitialized or mismatched scalar value.");
                    }
                    return current;
                case BoundConstant constant when constant.Type == PrimitiveType.Int32:
                    var constResult = _valueTypes.Count + 1;
                    var bits = unchecked((uint)constant.Value);
                    var dataWidth = Width(bits);
                    var int32TypeId = BodyTypeId(TypeRow(PrimitiveType.Int32));
                    var referenceWidth = Width((ulong)Math.Max(constResult, int32TypeId));
                    var constOffset = _operations.Count;
                    WriteInstruction(_operations, 0x01, referenceWidth, dataWidth);
                    Write(_operations, (ulong)constResult, referenceWidth);
                    Write(_operations, (ulong)int32TypeId, referenceWidth);
                    Write(_operations, bits, dataWidth);
                    _ = AddValue(PrimitiveType.Int32, 0, (uint)constOffset);
                    _operationCount++;
                    return constResult;
                case BoundBinary binary when binary.Type == PrimitiveType.Bool
                    && binary.Left.Type == binary.Right.Type
                    && binary.Operator is TokenKind.EqualsEquals or TokenKind.BangEquals
                        or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater
                        or TokenKind.GreaterEquals:
                {
                    var isFloat = IsFloat(binary.Left.Type);
                    var isEquality = binary.Operator is TokenKind.EqualsEquals or TokenKind.BangEquals;
                    if (!IsOutputScalarType(binary.Left.Type)
                        || !isFloat && (isEquality
                            ? binary.Left.Type is not (PrimitiveType.Bool or PrimitiveType.Char16
                                or PrimitiveType.Int32 or PrimitiveType.Int64)
                            : !IsInteger(binary.Left.Type)))
                    {
                        throw Unsupported("Comparison operands are outside the output Body profile.");
                    }

                    var comparisonLeft = EmitValue(binary.Left);
                    var comparisonRight = EmitValue(binary.Right);
                    var compareResult = _valueTypes.Count + 1;
                    var compareKind = binary.Operator switch
                    {
                        TokenKind.EqualsEquals => isFloat ? 1 : 4,
                        TokenKind.BangEquals => isFloat ? 2 : 5,
                        TokenKind.Less => isFloat ? 3 : 6,
                        TokenKind.LessEquals => isFloat ? 4 : 7,
                        TokenKind.Greater => isFloat ? 5 : 8,
                        _ => isFloat ? 6 : 9,
                    };
                    var compareWidth = Width((ulong)Math.Max(compareResult,
                        Math.Max(comparisonLeft, comparisonRight)));
                    var compareOffset = _operations.Count;
                    WriteInstruction(_operations, isFloat ? (byte)0x19 : (byte)5, compareWidth, 0);
                    Write(_operations, (ulong)compareResult, compareWidth);
                    _operations.Add((byte)compareKind);
                    Write(_operations, (ulong)comparisonLeft, compareWidth);
                    Write(_operations, (ulong)comparisonRight, compareWidth);
                    _ = AddValue(PrimitiveType.Bool, 0, (uint)compareOffset);
                    UseValue(comparisonLeft, _operationCount);
                    UseValue(comparisonRight, _operationCount);
                    _operationCount++;
                    return compareResult;
                }
                case BoundBinary binary when binary.Type == PrimitiveType.Int32
                    && binary.Left.Type == PrimitiveType.Int32 && binary.Right.Type == PrimitiveType.Int32
                    && binary.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star:
                    var left = EmitValue(binary.Left);
                    var right = EmitValue(binary.Right);
                    var result = _valueTypes.Count + 1;
                    var kind = binary.Operator switch
                    {
                        TokenKind.Plus => 1,
                        TokenKind.Minus => 2,
                        _ => 3,
                    };
                    var binaryWidth = Width((ulong)Math.Max(result, Math.Max(left, right)));
                    var binaryOffset = _operations.Count;
                    WriteInstruction(_operations, 0x15, binaryWidth, 0);
                    Write(_operations, (ulong)result, binaryWidth);
                    _operations.Add((byte)kind);
                    Write(_operations, (ulong)left, binaryWidth);
                    Write(_operations, (ulong)right, binaryWidth);
                    _ = AddValue(PrimitiveType.Int32, 0, (uint)binaryOffset);
                    UseValue(left, _operationCount);
                    UseValue(right, _operationCount);
                    _operationCount++;
                    return result;
                case BoundCall call:
                    return EmitCall(call);
                default:
                    throw Unsupported($"Expression {expression.GetType().Name} is outside the output Body profile.");
            }
        }

        private int EmitCall(BoundCall call)
        {
            if ((uint)call.Function >= (uint)_program.Functions.Length
                || call.ArgumentParameterIndices.IsDefault == false
                    && call.ArgumentParameterIndices.Length != call.Arguments.Length)
            {
                throw Unsupported("Call target or argument mapping is invalid.");
            }

            var target = _program.Functions[call.Function];
            if (target.IsExternal || target.IsConstructor || target.IsClassConstructor
                || target.ClassConstructor != -1 || target.DeclaringStruct is not null
                || target.IsStatic || target.IsCleanup || target.PropertyName is not null
                || target.Intrinsic != BoundIntrinsic.None
                || target.ParameterCount != call.Arguments.Length
                || target.ReturnType != call.Type
                || !IsOutputCallResultType(target.ReturnType))
            {
                throw Unsupported("Output calls require a local scalar function with an exact signature.");
            }

            var outputTarget = target.Variables.Take(target.ParameterCount)
                .Any(static parameter => parameter.IsOut || parameter.IsInit);
            if (outputTarget && (!target.IsPrivate && !target.IsInternal
                || target.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32)))
            {
                throw Unsupported("Output targets must be non-exported and return void or int32.");
            }

            var arguments = new int[call.Arguments.Length];
            var outputPlaces = new int[call.Arguments.Length];
            var outputTargets = new HashSet<int>();
            for (var argument = 0; argument < call.Arguments.Length; argument++)
            {
                if (!call.ArgumentParameterIndices.IsDefault
                    && call.ArgumentParameterIndices[argument] != argument)
                {
                    throw Unsupported("Output-profile calls require parameter-order arguments.");
                }

                var parameter = target.Variables[argument];
                var value = call.Arguments[argument];
                if (parameter.IsOut || parameter.IsInit)
                {
                    if (!parameter.IsOut || !parameter.IsInit || parameter.Type != PrimitiveType.Int32
                        || value is not BoundOutArgument
                        {
                            IsInit: true,
                            DeclaresVariable: false,
                            Type: PrimitiveType.Int32,
                        } output
                        || (uint)output.Variable >= (uint)_placeByVariable.Length
                        || _placeByVariable[output.Variable] == 0
                        || output.Variable < _function.ParameterCount
                        || !_placeDeclared[_placeByVariable[output.Variable]]
                        || _placeInitialized[_placeByVariable[output.Variable]]
                        || !outputTargets.Add(output.Variable))
                    {
                        throw Unsupported("Mode 0x02 requires a distinct vacant local int32 place.");
                    }
                    var place = _placeByVariable[output.Variable];
                    arguments[argument] = _placeHandles[place];
                    outputPlaces[argument] = place;
                }
                else
                {
                    if (value is BoundOutArgument || !IsOutputScalarType(parameter.Type))
                    {
                        throw Unsupported("By-value output-profile arguments must be scalar values.");
                    }
                    arguments[argument] = EmitValue(value);
                    if (_valueTypes[arguments[argument] - 1] != TypeRow(parameter.Type))
                    {
                        throw Unsupported("Call argument type does not match its parameter.");
                    }
                }
            }

            foreach (var place in outputPlaces)
            {
                if (place != 0 && _placeInitialized[place])
                {
                    throw Unsupported("An output destination changed during argument evaluation.");
                }
            }

            var result = call.Type == PrimitiveType.Void ? 0 : _valueTypes.Count + 1;
            var maximumOperand = Math.Max(result, call.Function + 1);
            for (var argument = 0; argument < arguments.Length; argument++)
            {
                maximumOperand = Math.Max(maximumOperand, arguments[argument]);
            }
            var width = Width((ulong)maximumOperand);
            var countWidth = Width((ulong)arguments.Length);
            var offset = _operations.Count;
            WriteCallInstruction(_operations, width, countWidth);
            Write(_operations, (ulong)result, width);
            Write(_operations, (ulong)(call.Function + 1), width);
            Write(_operations, (ulong)arguments.Length, countWidth);
            for (var argument = 0; argument < arguments.Length; argument++)
            {
                Write(_operations, (ulong)arguments[argument], width);
                UseValue(arguments[argument], _operationCount);
                if (outputPlaces[argument] != 0)
                {
                    UsePlace(outputPlaces[argument], _operationCount);
                }
            }
            if (result != 0)
            {
                _ = AddValue(call.Type, 0, (uint)offset);
            }
            _operationCount++;
            foreach (var place in outputPlaces)
            {
                if (place != 0)
                {
                    _placeInitialized[place] = true;
                }
            }
            return result;
        }

        private void EmitReturn(BoundReturn result)
        {
            if (_function.ReturnType == PrimitiveType.Void)
            {
                if (result.Value is not null)
                {
                    throw Unsupported("Void output functions cannot return a value.");
                }
                _returnValue = 0;
            }
            else
            {
                if (result.Value is null)
                {
                    throw Unsupported("int32 output functions must return a value.");
                }
                _returnValue = EmitValue(result.Value);
                if (_valueTypes[_returnValue - 1] != TypeRow(PrimitiveType.Int32))
                {
                    throw Unsupported("Output-profile return type must be int32.");
                }
                UseValue(_returnValue, _operationCount);
            }

            for (var place = 1; place < _places.Count; place++)
            {
                UsePlace(place, _operationCount);
                if (_places[place].IsParameter && !_placeInitialized[place])
                {
                    throw Unsupported("Every init ref<int32> formal must be initialized before normal return.");
                }
            }
            _ended = true;
        }

        private byte[] BuildBody()
        {
            var int32TypeId = BodyTypeId(TypeRow(PrimitiveType.Int32));
            var localWidth = Width((ulong)Math.Max(
                Math.Max(_valueTypes.Count, _places.Count - 1),
                Math.Max(_operationCount, _parameterValues.Count)));
            if (localWidth > 4 || _operations.Count > int.MaxValue)
            {
                throw Unsupported("Output Body exceeds its bounded local representation.");
            }

            var valueUseCount = _valueLastUses.Count;
            var placeUseCount = _placeLastUses.Count;
            var startPlaces = Enumerable.Range(1, _places.Count - 1).ToArray();
            var block = new List<byte>();
            Write(block, 0, localWidth); // function-root region
            Write(block, (ulong)_parameterValues.Count, localWidth);
            foreach (var value in _parameterValues)
            {
                Write(block, (ulong)value, localWidth);
            }
            Write(block, 0, localWidth); // live-ins
            Write(block, 0, localWidth); // live-outs
            Write(block, 0, localWidth); // inherited places
            Write(block, (ulong)startPlaces.Length, localWidth);
            foreach (var place in startPlaces)
            {
                Write(block, (ulong)place, localWidth);
            }
            Write(block, (ulong)(_places.Count - 1), localWidth);
            for (var place = 1; place < _places.Count; place++)
            {
                Write(block, (ulong)place, localWidth);
            }
            Write(block, (ulong)valueUseCount, localWidth);
            foreach (var (value, ordinal) in _valueLastUses.OrderBy(static pair => pair.Key))
            {
                Write(block, (ulong)value, localWidth);
                Write(block, (ulong)ordinal, localWidth);
            }
            Write(block, (ulong)placeUseCount, localWidth);
            foreach (var (place, ordinal) in _placeLastUses.OrderBy(static pair => pair.Key))
            {
                Write(block, (ulong)place, localWidth);
                Write(block, (ulong)ordinal, localWidth);
            }
            Write(block, (ulong)_operationCount, localWidth);
            var operationsStart = block.Count;
            block.AddRange(_operations);
            var terminatorOffset = block.Count;
            var returnWidth = Width((ulong)_returnValue);
            WriteInstruction(block, 0x83, returnWidth, 0);
            Write(block, (ulong)_returnValue, returnWidth);

            var body = new List<byte>
            {
                (byte)WidthCode(localWidth),
            };
            Write(body, 1, localWidth); // block count
            Write(body, (ulong)_valueTypes.Count, localWidth);
            Write(body, (ulong)(_places.Count - 1), localWidth);
            Write(body, 0, localWidth); // region count
            Write(body, (ulong)_bodyTypeRows.Count, localWidth); // type count
            foreach (var row in _bodyTypeRows)
            {
                Write(body, (uint)row, 4);
            }
            for (var place = 1; place < _places.Count; place++)
            {
                Write(body, (ulong)int32TypeId, localWidth);
                Write(body, 0, localWidth); // function-root region
                body.Add(0x04); // initially vacant, non-unique, non-rebindable
                Write(body, 1, localWidth); // one block uses the place
            }
            for (var value = 0; value < _valueTypes.Count; value++)
            {
                Write(body, (ulong)_bodyTypeIds[_valueTypes[value]], localWidth);
                body.Add(_valueKinds[value]);
                Write(body, (ulong)(_valueLastUses.ContainsKey(value + 1) ? 1 : 0), localWidth);
            }
            Write(body, 0, localWidth); // valueDefStarts[0]
            Write(body, (ulong)_valueTypes.Count, localWidth); // valueDefStarts[1]
            for (var value = 0; value < _definitionOffsets.Count; value++)
            {
                var definition = _definitionOffsets[value];
                if (definition != uint.MaxValue)
                {
                    definition = checked((uint)(operationsStart + definition));
                }
                Write(body, definition, 4);
            }
            Write(body, 0, 4); // blockOffsets[0]
            Write(body, (ulong)block.Count, 4); // blockOffsets[1]
            Write(body, (ulong)terminatorOffset, 4);
            body.AddRange(block);
            return body.ToArray();
        }

        private int BodyTypeId(int typeRow)
        {
            if (_bodyTypeIds.TryGetValue(typeRow, out var typeId))
            {
                return typeId;
            }

            typeId = _bodyTypeRows.Count + 1;
            _bodyTypeRows.Add(typeRow);
            _bodyTypeIds.Add(typeRow, typeId);
            return typeId;
        }

        private void UseValue(int value, int ordinal)
        {
            if (value <= 0 || value > _valueTypes.Count)
            {
                throw Unsupported("Body value use is outside the definition inventory.");
            }
            if (!_valueLastUses.TryGetValue(value, out var lastUse) || ordinal > lastUse)
            {
                _valueLastUses[value] = ordinal;
            }
        }

        private void UsePlace(int place, int ordinal)
        {
            if (place <= 0 || place >= _places.Count)
            {
                throw Unsupported("Body place use is outside the descriptor inventory.");
            }
            if (!_placeLastUses.TryGetValue(place, out var lastUse) || ordinal > lastUse)
            {
                _placeLastUses[place] = ordinal;
            }
        }
    }

    private readonly record struct OutputPlace(int Variable, bool IsParameter);
}
