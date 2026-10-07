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
        ImmutableArray<BoundFunction>.Builder functions, uint ruleRevision)
    {
        var statements = ImmutableArray.CreateBuilder<BoundStatement>(
            body.Operations.Length + body.Places.Length + 1);
        var placeVariables = new int[body.Places.Length];
        for (var index = 0; index < body.Places.Length; index++)
        {
            var place = body.Places[index];
            var placeType = OutputType(body, types, place.TypeId);
            if (!IsOutputScalarType(placeType)
                || ruleRevision == TrRmFeatureTableReader.ScalarInitOutputInitialRuleRevision
                    && placeType != PrimitiveType.Int32)
            {
                throw Invalid("The executable place profile has an unsupported referent type.");
            }

            if (place.ParameterOrdinal >= 0)
            {
                if (place.ParameterOrdinal >= variables.Count
                    || !variables[place.ParameterOrdinal].IsOut
                    || !variables[place.ParameterOrdinal].IsInit
                    || variables[place.ParameterOrdinal].Type != placeType)
                {
                    throw Invalid("An output place does not match its formal parameter.");
                }
                placeVariables[index] = place.ParameterOrdinal;
            }
            else
            {
                placeVariables[index] = variables.Count;
                variables.Add(new BoundVariable($"place{index + 1}", placeType,
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
        var conditionalResult = body.Terminator.Opcode == 0x81
            ? body.Terminator.Operands[0] : 0;
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
                if (index + 1 == conditionalResult)
                {
                    continue;
                }
                valueVariables[index] = variables.Count;
                variables.Add(new BoundVariable($"value{index + 1}", type, false, default));
            }
        }

        var bodyTypes = new PrimitiveType[body.Types.Length];
        for (var index = 0; index < bodyTypes.Length; index++)
        {
            bodyTypes[index] = OutputType(body, types, index + 1);
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

        BoundCall? conditionalCall = null;
        void DecodeOperations(ImmutableArray<TrRmScalarInitOutputBodyReader.Instruction> operations)
        {
            foreach (var operation in operations)
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
                        var type = bodyTypes[operation.TypeId - 1];
                        if (!IsOutputScalarType(type) || operation.Opcode == 2 && !IsInteger(type))
                        {
                            throw Invalid("Constant type is outside the executable place profile.");
                        }

                        var constantValue = type switch
                        {
                            PrimitiveType.Int32 or PrimitiveType.UInt32
                                => unchecked((int)operation.Data),
                            PrimitiveType.Float32 => unchecked((uint)operation.Data),
                            PrimitiveType.Bool or PrimitiveType.Char16 => (long)operation.Data,
                            _ => unchecked((long)operation.Data),
                        };
                        DefineValue(operation.Result,
                            new BoundConstant(constantValue, type, default));
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
                            new BoundRead(place, variables[place].Type, default));
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
                    case 0x16:
                    case 0x1A:
                    case 0x1B:
                    case 0x1E:
                    case 0x1F:
                    {
                        if (operation.Operands.Length != 1)
                        {
                            throw Invalid("Convert requires one value.");
                        }

                        var operand = ReadValue(operation.Operands[0]);
                        var destination = bodyTypes[operation.TypeId - 1];
                        var valid = operation.Opcode switch
                        {
                            0x16 => IsOutputIntegerConversionType(operand.Type)
                                && IsOutputIntegerConversionType(destination),
                            0x1A => IsFloat(operand.Type) && IsFloat(destination)
                                && operand.Type != destination,
                            0x1B => IsInteger(operand.Type) && IsFloat(destination),
                            0x1E => operand.Type == PrimitiveType.Char16 && IsFloat(destination),
                            0x1F => IsFloat(operand.Type) && IsInteger(destination),
                            _ => false,
                        };
                        if (!valid)
                        {
                            throw Invalid("Convert is outside the executable scalar profile.");
                        }

                        DefineValue(operation.Result, new BoundCast(operand, destination, default));
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
                                if (!formal.IsOut || !formal.IsInit
                                    || !IsOutputScalarType(formal.Type))
                                {
                                    throw Invalid("The output formal type is outside the executable profile.");
                                }

                                var place = PlaceVariable(PlaceForHandle(argument));
                                if (variables[place].Type != formal.Type)
                                {
                                    throw Invalid("Output argument type does not match its formal.");
                                }
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
                        if (conditionalResult != 0 && operation.Result == conditionalResult)
                        {
                            conditionalCall = call;
                            valueExpressions[operation.Result - 1] = call;
                        }
                        else if (operation.Result == 0)
                        {
                            statements.Add(new BoundExpressionStatement(call, default));
                        }
                        else
                        {
                            DefineValue(operation.Result, call);
                        }
                        break;
                    }
                case 4:
                case 0x17:
                case 0x1C:
                {
                    if (operation.Operands.Length != 1)
                    {
                        throw Invalid("Unary requires one value.");
                    }

                    var operand = ReadValue(operation.Operands[0]);
                    var isFloatNegation = operation.Opcode == 0x1C;
                    var isCheckedIntegerNegation = operation.Opcode == 0x17;
                    var valid = isFloatNegation
                        ? IsFloat(operand.Type)
                        : isCheckedIntegerNegation
                            ? operand.Type is PrimitiveType.Int32 or PrimitiveType.Int64
                            : operation.Kind switch
                            {
                                1 => operand.Type == PrimitiveType.Bool,
                                2 or 3 => IsInteger(operand.Type),
                                _ => false,
                            };
                    if (!valid)
                    {
                        throw Invalid("Unary operand is outside the executable place profile.");
                    }

                    var token = isFloatNegation || isCheckedIntegerNegation
                        ? TokenKind.Minus
                        : operation.Kind switch
                        {
                            1 => TokenKind.Bang,
                            2 => TokenKind.Tilde,
                            _ => TokenKind.Plus,
                        };
                    DefineValue(operation.Result,
                        new BoundUnary(token, operand, operand.Type, default));
                    break;
                }
                case 0x1D:
                {
                    if (operation.Operands.Length != 2)
                    {
                        throw Invalid("Floating binary operation requires two values.");
                    }

                    var floatingLeftValue = ReadValue(operation.Operands[0]);
                    var floatingRightValue = ReadValue(operation.Operands[1]);
                    if (floatingLeftValue.Type != floatingRightValue.Type
                        || !IsFloat(floatingLeftValue.Type)
                        || operation.Kind is < 1 or > 4)
                    {
                        throw Invalid("Floating binary operation is outside the executable place profile.");
                    }

                    var token = operation.Kind switch
                    {
                        1 => TokenKind.Plus,
                        2 => TokenKind.Minus,
                        3 => TokenKind.Star,
                        _ => TokenKind.Slash,
                    };
                    DefineValue(operation.Result,
                        new BoundBinary(token, floatingLeftValue, floatingRightValue,
                            floatingLeftValue.Type, default));
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
                        1 or 2 or 3 => !isFloat && IsInteger(left.Type),
                        4 or 5 => left.Type is PrimitiveType.Bool or PrimitiveType.Char16
                            or PrimitiveType.Int32 or PrimitiveType.UInt32
                            or PrimitiveType.Int64 or PrimitiveType.UInt64,
                        6 or 7 or 8 or 9 => IsInteger(left.Type),
                        _ => false,
                    });
                    if (!valid)
                    {
                        throw Invalid("Comparison operands are outside the executable place profile.");
                    }

                    var token = (isFloat, operation.Kind) switch
                    {
                        (false, 1) => TokenKind.Ampersand,
                        (false, 2) => TokenKind.Pipe,
                        (false, 3) => TokenKind.Caret,
                        (true, 1) or (false, 4) => TokenKind.EqualsEquals,
                        (true, 2) or (false, 5) => TokenKind.BangEquals,
                        (true, 3) or (false, 6) => TokenKind.Less,
                        (true, 4) or (false, 7) => TokenKind.LessEquals,
                        (true, 5) or (false, 8) => TokenKind.Greater,
                        (true, 6) or (false, 9) => TokenKind.GreaterEquals,
                        _ => throw Invalid("Unsupported Binary kind."),
                    };
                    DefineValue(operation.Result,
                        new BoundBinary(token, left, right,
                            !isFloat && operation.Kind is >= 1 and <= 3
                                ? left.Type : PrimitiveType.Bool, default));
                    break;
                }
                case 0x15:
                {
                    if (operation.Operands.Length != 2)
                    {
                        throw Invalid("Binary requires two values.");
                    }

                    var integerLeft = ReadValue(operation.Operands[0]);
                    var integerRight = ReadValue(operation.Operands[1]);
                    var isShift = operation.Kind is 6 or 7;
                    if (!IsInteger(integerLeft.Type)
                        || (isShift
                            ? integerRight.Type != PrimitiveType.UInt32
                            : integerLeft.Type != integerRight.Type))
                    {
                        throw Invalid("Binary operands are outside the executable integer profile.");
                    }

                    var token = operation.Kind switch
                    {
                        1 => TokenKind.Plus,
                        2 => TokenKind.Minus,
                        3 => TokenKind.Star,
                        4 => TokenKind.Slash,
                        5 => TokenKind.Percent,
                        6 => TokenKind.ShiftLeft,
                        7 => TokenKind.ShiftRight,
                        _ => throw Invalid("The executable place profile has an unsupported Binary kind."),
                    };
                    BoundExpression rightValue = isShift
                        ? new BoundCast(integerRight, PrimitiveType.Int32, default)
                        : integerRight;
                    DefineValue(operation.Result, new BoundBinary(token,
                        integerLeft, rightValue, integerLeft.Type, default));
                    break;
                }
                default:
                    throw Invalid($"Operation 0x{operation.Opcode:X2} is outside the executable place profile.");
                }
            }
        }

        void DecodeTerminator(TrRmScalarInitOutputBodyReader.Instruction terminator)
        {
            if (terminator.Opcode == 0x83)
            {
                if (terminator.Operands.Length != 1)
                {
                    throw Invalid("Return requires one result slot.");
                }

                var result = terminator.Operands[0];
                statements.Add(new BoundReturn(result == 0 ? null : ReadValue(result), default));
            }
            else if (terminator.Opcode == 0x87)
            {
                statements.Add(new BoundRequire(new BoundConstant(0, PrimitiveType.Bool, default), default));
            }
            else
            {
                throw Invalid("Unsupported executable place terminator.");
            }
        }

        if (body.Blocks.Length == 1)
        {
            DecodeOperations(body.Operations);
            DecodeTerminator(body.Terminator);
        }
        else
        {
            if (body.Blocks.Length != 3 || body.Terminator.Opcode != 0x81)
            {
                throw Invalid("Conditional output Body is outside the executable profile.");
            }

            DecodeOperations(body.Blocks[0].Operations);
            if (conditionalCall is null)
            {
                throw Invalid("Conditional output Body has no direct Boolean call condition.");
            }
            var rootStatements = statements;
            statements = ImmutableArray.CreateBuilder<BoundStatement>();
            DecodeOperations(body.Blocks[1].Operations);
            DecodeTerminator(body.Blocks[1].Terminator);
            var whenTrue = new BoundBlock(statements.ToImmutable(), default);
            statements = ImmutableArray.CreateBuilder<BoundStatement>();
            DecodeOperations(body.Blocks[2].Operations);
            DecodeTerminator(body.Blocks[2].Terminator);
            var whenFalse = new BoundBlock(statements.ToImmutable(), default);
            statements = rootStatements;
            statements.Add(new BoundIf(conditionalCall, whenTrue, whenFalse, default));
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

    private static bool IsOutputIntegerConversionType(PrimitiveType type)
        => IsInteger(type) || type == PrimitiveType.Char16;

    private static bool IsOutputConversionType(PrimitiveType type)
        => IsOutputScalarType(type) && type != PrimitiveType.Bool;

    private static bool IsOutputIntegerBinary(BoundBinary binary)
        => binary.Type == binary.Left.Type && IsInteger(binary.Left.Type)
            && (binary.Operator switch
            {
                TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent => binary.Right.Type == binary.Left.Type,
                TokenKind.ShiftLeft or TokenKind.ShiftRight =>
                    binary.Right.Type == PrimitiveType.Int32,
                TokenKind.ShiftRightUnsigned => binary.Right.Type == PrimitiveType.Int32
                    && binary.Left.Type is PrimitiveType.UInt32 or PrimitiveType.UInt64,
                _ => false,
            });

    private static bool IsOutputCallResultType(PrimitiveType type)
        => type == PrimitiveType.Void || IsOutputScalarType(type);

    private static bool IsOutputReturnType(PrimitiveType type)
        => type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float32
            or PrimitiveType.Float64;

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
        private readonly List<OutputBlock> _blocks = [];
        private readonly List<int> _valueTypes = [];
        private readonly List<byte> _valueKinds = [];
        private readonly List<uint> _definitionOffsets = [];
        private readonly List<int> _definitionBlocks = [];
        private readonly List<int> _parameterValues = [];
        private readonly List<byte> _operations = [];
        private readonly Dictionary<int, int> _valueLastUses = [];
        private readonly Dictionary<int, int> _placeLastUses = [];
        private int _operationCount;
        private int _returnValue;
        private int _failureReason;
        private int _currentBlock;
        private bool _ended;

        public OutputBodyWriter(BoundFunction function, BoundProgram program)
        {
            _function = function;
            _program = program;
            _placeByVariable = new int[function.Variables.Length];
            _currentValues = new int[function.Variables.Length];
            if (function.Variables.Length > SemanticVerifier.MaximumVariables
                || function.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32)
                    && (!HasOutputParameters(function) || !IsOutputReturnType(function.ReturnType)))
            {
                throw Unsupported("Output Bodies support at most 256 variables and scalar results only for output functions.");
            }

            for (var variable = 0; variable < function.ParameterCount; variable++)
            {
                var parameter = function.Variables[variable];
                if (parameter.IsOut || parameter.IsInit)
                {
                    if (!parameter.IsOut || !parameter.IsInit
                        || !IsOutputScalarType(parameter.Type))
                    {
                        throw Unsupported("The output Body profile requires scalar init ref parameters.");
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
                    if (!IsOutputScalarType(local.Type) || local.IsMutable)
                    {
                        throw Unsupported("The output Body profile only supports vacant scalar local places.");
                    }
                    AddPlace(variable, isParameter: false);
                }
                else if (!IsOutputScalarType(local.Type))
                {
                    throw Unsupported("The output Body profile currently supports only scalar locals.");
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

            if (_blocks.Count == 0)
            {
                CaptureBlock();
                _ended = true;
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
                || _valueTypes.Count >= SemanticVerifier.MaximumVariables)
            {
                throw Unsupported("The output Body profile supports scalar values within the verifier limit.");
            }

            var typeRow = TypeRow(type);
            _ = BodyTypeId(typeRow);
            _valueTypes.Add(typeRow);
            _valueKinds.Add(kind);
            _definitionOffsets.Add(definitionOffset);
            _definitionBlocks.Add(_currentBlock);
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
            var type = _function.Variables[_places[place].Variable].Type;
            _ = AddValue(type, 1, (uint)offset);
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
                        throw Unsupported("Vacant locals must map to scalar local places.");
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
                        || !IsOutputScalarType(local.Initializer.Type)
                        || local.Initializer.Type != _function.Variables[local.Variable].Type)
                    {
                        throw Unsupported("The output Body profile supports initialized scalar value locals.");
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
                case BoundIf conditional:
                    EmitConditional(conditional);
                    break;
                default:
                    throw Unsupported($"Statement {statement.GetType().Name} is outside the output Body profile.");
            }
        }

        private void EmitConditional(BoundIf conditional)
        {
            if (conditional.Condition is not BoundCall { Type: PrimitiveType.Bool } call)
            {
                throw Unsupported("Output Body conditionals require a direct Boolean call.");
            }

            var condition = EmitCall(call);
            if (condition == 0)
            {
                throw Unsupported("Output Body conditionals require a Boolean call result.");
            }
            UseValue(condition, _operationCount);

            var outputPlaces = new List<int>();
            foreach (var argument in call.Arguments)
            {
                if (argument is BoundOutArgument { IsInit: true } output)
                {
                    if ((uint)output.Variable >= (uint)_placeByVariable.Length
                        || _placeByVariable[output.Variable] == 0)
                    {
                        throw Unsupported("Conditional output destinations must be scalar local places.");
                    }
                    outputPlaces.Add(_placeByVariable[output.Variable]);
                }
            }
            var falseState = _placeInitialized.ToArray();
            var trueState = (bool[])falseState.Clone();
            foreach (var place in outputPlaces)
            {
                trueState[place] = true;
            }

            CaptureBlock(condition);
            _currentBlock = 1;
            RestorePlaceState(trueState);
            var trueEntryState = _placeInitialized.ToArray();
            EmitBranchTerminal(conditional.Then);
            CaptureBlock(inheritedPlaces: trueEntryState);

            _currentBlock = 2;
            RestorePlaceState(falseState);
            if (conditional.Else is null)
            {
                throw Unsupported("Output Body conditionals require two terminal arms.");
            }
            var falseEntryState = _placeInitialized.ToArray();
            EmitBranchTerminal(conditional.Else);
            CaptureBlock(inheritedPlaces: falseEntryState);
            _ended = true;
        }

        private void EmitBranchTerminal(BoundStatement statement)
        {
            var terminal = statement switch
            {
                BoundBlock { RetiredManagedLocals.IsEmpty: true, Statements.Length: 1 } block
                    => block.Statements[0],
                _ => statement,
            };

            switch (terminal)
            {
                case BoundReturn returned:
                    EmitReturn(returned);
                    break;
                case BoundRequire
                {
                    Condition: BoundConstant { Type: PrimitiveType.Bool, Value: 0 },
                }:
                    for (var place = 1; place < _places.Count; place++)
                    {
                        UsePlace(place, _operationCount);
                    }
                    _failureReason = 1;
                    _ended = true;
                    break;
                default:
                    throw Unsupported("Conditional output arms require one terminal Return or Fail reason 1.");
            }
        }

        private void RestorePlaceState(bool[] initialized)
        {
            if (initialized.Length != _placeInitialized.Count)
            {
                throw Unsupported("Conditional output place inventory changed between arms.");
            }

            for (var place = 0; place < initialized.Length; place++)
            {
                _placeInitialized[place] = initialized[place];
            }
        }

        private void CaptureBlock(int condition = 0, bool[]? inheritedPlaces = null)
        {
            _blocks.Add(new OutputBlock(_operations.ToArray(), _operationCount, _returnValue,
                _failureReason, condition, new Dictionary<int, int>(_valueLastUses),
                new Dictionary<int, int>(_placeLastUses),
                _currentBlock == 0 ? null : inheritedPlaces ?? _placeInitialized.ToArray()));
            _operations.Clear();
            _valueLastUses.Clear();
            _placeLastUses.Clear();
            _operationCount = 0;
            _returnValue = 0;
            _failureReason = 0;
            _ended = false;
        }

        private void EmitAssignment(BoundAssignment assignment)
        {
            if ((uint)assignment.Variable >= (uint)_currentValues.Length)
            {
                throw Unsupported("Assignment target is outside the variable inventory.");
            }

            var place = _placeByVariable[assignment.Variable];
            var value = EmitValue(assignment.Value);
            var variableType = _function.Variables[assignment.Variable].Type;
            if (!IsOutputScalarType(variableType)
                || _valueTypes[value - 1] != TypeRow(variableType))
            {
                throw Unsupported("Output assignment requires an exactly typed scalar value.");
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
                        _ = AddValue(read.Type, 0, (uint)offset);
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
                case BoundConstant constant when IsOutputScalarType(constant.Type):
                    var constResult = _valueTypes.Count + 1;
                    var bits = constant.Type switch
                    {
                        PrimitiveType.Bool or PrimitiveType.Char16 => (ulong)constant.Value,
                        PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Float32
                            => unchecked((uint)constant.Value),
                        _ => unchecked((ulong)constant.Value),
                    };
                    var dataWidth = Width(bits);
                    var constantTypeId = BodyTypeId(TypeRow(constant.Type));
                    var referenceWidth = Width((ulong)Math.Max(constResult, constantTypeId));
                    var constOffset = _operations.Count;
                    WriteInstruction(_operations, 0x01, referenceWidth, dataWidth);
                    Write(_operations, (ulong)constResult, referenceWidth);
                    Write(_operations, (ulong)constantTypeId, referenceWidth);
                    Write(_operations, bits, dataWidth);
                    _ = AddValue(constant.Type, 0, (uint)constOffset);
                    _operationCount++;
                    return constResult;
                case BoundCast cast when IsOutputConversionType(cast.Operand.Type)
                    && IsOutputConversionType(cast.Type):
                {
                    if (cast.Operand.Type == cast.Type)
                    {
                        return EmitValue(cast.Operand);
                    }

                    var conversionOpcode = (cast.Operand.Type, cast.Type) switch
                    {
                        (PrimitiveType.Float32 or PrimitiveType.Float64,
                            PrimitiveType.Float32 or PrimitiveType.Float64) => (byte)0x1A,
                        (PrimitiveType.Int32 or PrimitiveType.UInt32
                            or PrimitiveType.Int64 or PrimitiveType.UInt64,
                            PrimitiveType.Float32 or PrimitiveType.Float64) => (byte)0x1B,
                        (PrimitiveType.Char16, PrimitiveType.Float32 or PrimitiveType.Float64) => (byte)0x1E,
                        (PrimitiveType.Float32 or PrimitiveType.Float64,
                            PrimitiveType.Int32 or PrimitiveType.UInt32
                                or PrimitiveType.Int64 or PrimitiveType.UInt64) => (byte)0x1F,
                        (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                            or PrimitiveType.UInt64 or PrimitiveType.Char16,
                            PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                                or PrimitiveType.UInt64 or PrimitiveType.Char16) => (byte)0x16,
                        _ => throw Unsupported("Conversion is outside the output Body scalar profile."),
                    };
                    var castOperand = EmitValue(cast.Operand);
                    var castResult = _valueTypes.Count + 1;
                    var castTypeId = BodyTypeId(TypeRow(cast.Type));
                    var castWidth = Width((ulong)Math.Max(castResult,
                        Math.Max(castTypeId, castOperand)));
                    var castOffset = _operations.Count;
                    WriteInstruction(_operations, conversionOpcode, castWidth, 0);
                    Write(_operations, (ulong)castResult, castWidth);
                    Write(_operations, (ulong)castTypeId, castWidth);
                    Write(_operations, (ulong)castOperand, castWidth);
                    _ = AddValue(cast.Type, 0, (uint)castOffset);
                    UseValue(castOperand, _operationCount);
                    _operationCount++;
                    return castResult;
                }
                case BoundBinary binary when binary.Type == binary.Left.Type
                    && binary.Left.Type == binary.Right.Type && IsFloat(binary.Left.Type)
                    && binary.Operator is TokenKind.Plus or TokenKind.Minus
                        or TokenKind.Star or TokenKind.Slash:
                {
                    var floatingLeft = EmitValue(binary.Left);
                    var floatingRight = EmitValue(binary.Right);
                    var floatingResult = _valueTypes.Count + 1;
                    var floatingKind = binary.Operator switch
                    {
                        TokenKind.Plus => 1,
                        TokenKind.Minus => 2,
                        TokenKind.Star => 3,
                        _ => 4,
                    };
                    var floatingWidth = Width((ulong)Math.Max(floatingResult,
                        Math.Max(floatingLeft, floatingRight)));
                    var floatingOffset = _operations.Count;
                    WriteInstruction(_operations, 0x1D, floatingWidth, 0);
                    Write(_operations, (ulong)floatingResult, floatingWidth);
                    _operations.Add((byte)floatingKind);
                    Write(_operations, (ulong)floatingLeft, floatingWidth);
                    Write(_operations, (ulong)floatingRight, floatingWidth);
                    _ = AddValue(binary.Type, 0, (uint)floatingOffset);
                    UseValue(floatingLeft, _operationCount);
                    UseValue(floatingRight, _operationCount);
                    _operationCount++;
                    return floatingResult;
                }
                case BoundUnary unary when unary.Type == unary.Operand.Type
                    && (unary.Operator switch
                    {
                        TokenKind.Bang => unary.Type == PrimitiveType.Bool,
                        TokenKind.Tilde or TokenKind.Plus => IsInteger(unary.Type),
                        TokenKind.Minus => IsFloat(unary.Type)
                            || unary.Type is PrimitiveType.Int32 or PrimitiveType.Int64,
                        _ => false,
                    }):
                {
                    var unaryOperand = EmitValue(unary.Operand);
                    var unaryResult = _valueTypes.Count + 1;
                    var unaryOpcode = unary.Operator == TokenKind.Minus
                        ? IsFloat(unary.Type) ? (byte)0x1C : (byte)0x17
                        : (byte)4;
                    var unaryKind = unary.Operator switch
                    {
                        TokenKind.Bang => 1,
                        TokenKind.Tilde => 2,
                        TokenKind.Plus => 3,
                        _ => 0,
                    };
                    var unaryWidth = Width((ulong)Math.Max(unaryResult, unaryOperand));
                    var unaryOffset = _operations.Count;
                    WriteInstruction(_operations, unaryOpcode, unaryWidth, 0);
                    Write(_operations, (ulong)unaryResult, unaryWidth);
                    if (unaryOpcode == 4)
                    {
                        _operations.Add((byte)unaryKind);
                    }
                    Write(_operations, (ulong)unaryOperand, unaryWidth);
                    _ = AddValue(unary.Type, 0, (uint)unaryOffset);
                    UseValue(unaryOperand, _operationCount);
                    _operationCount++;
                    return unaryResult;
                }
                case BoundBinary binary when binary.Type == binary.Left.Type
                    && binary.Left.Type == binary.Right.Type && IsInteger(binary.Left.Type)
                    && binary.Operator is TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret:
                {
                    var bitwiseLeft = EmitValue(binary.Left);
                    var bitwiseRight = EmitValue(binary.Right);
                    var bitwiseResult = _valueTypes.Count + 1;
                    var bitwiseKind = binary.Operator switch
                    {
                        TokenKind.Ampersand => 1,
                        TokenKind.Pipe => 2,
                        _ => 3,
                    };
                    var bitwiseWidth = Width((ulong)Math.Max(bitwiseResult,
                        Math.Max(bitwiseLeft, bitwiseRight)));
                    var bitwiseOffset = _operations.Count;
                    WriteInstruction(_operations, 5, bitwiseWidth, 0);
                    Write(_operations, (ulong)bitwiseResult, bitwiseWidth);
                    _operations.Add((byte)bitwiseKind);
                    Write(_operations, (ulong)bitwiseLeft, bitwiseWidth);
                    Write(_operations, (ulong)bitwiseRight, bitwiseWidth);
                    _ = AddValue(binary.Type, 0, (uint)bitwiseOffset);
                    UseValue(bitwiseLeft, _operationCount);
                    UseValue(bitwiseRight, _operationCount);
                    _operationCount++;
                    return bitwiseResult;
                }
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
                                or PrimitiveType.Int32 or PrimitiveType.UInt32
                                or PrimitiveType.Int64 or PrimitiveType.UInt64)
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
                case BoundBinary binary when IsOutputIntegerBinary(binary):
                {
                    var integerLeft = EmitValue(binary.Left);
                    var isShift = binary.Operator is TokenKind.ShiftLeft or TokenKind.ShiftRight
                        or TokenKind.ShiftRightUnsigned;
                    var integerRight = isShift
                        ? EmitValue(new BoundCast(binary.Right, PrimitiveType.UInt32, default))
                        : EmitValue(binary.Right);
                    var integerResult = _valueTypes.Count + 1;
                    var integerKind = binary.Operator switch
                    {
                        TokenKind.Plus => 1,
                        TokenKind.Minus => 2,
                        TokenKind.Star => 3,
                        TokenKind.Slash => 4,
                        TokenKind.Percent => 5,
                        TokenKind.ShiftLeft => 6,
                        _ => 7,
                    };
                    var binaryWidth = Width((ulong)Math.Max(integerResult,
                        Math.Max(integerLeft, integerRight)));
                    var binaryOffset = _operations.Count;
                    WriteInstruction(_operations, 0x15, binaryWidth, 0);
                    Write(_operations, (ulong)integerResult, binaryWidth);
                    _operations.Add((byte)integerKind);
                    Write(_operations, (ulong)integerLeft, binaryWidth);
                    Write(_operations, (ulong)integerRight, binaryWidth);
                    _ = AddValue(binary.Left.Type, 0, (uint)binaryOffset);
                    UseValue(integerLeft, _operationCount);
                    UseValue(integerRight, _operationCount);
                    _operationCount++;
                    return integerResult;
                }
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
                || target.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32)
                    && !IsOutputReturnType(target.ReturnType)))
            {
                throw Unsupported("Output targets must be non-exported and return void or a scalar.");
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
                    if (!parameter.IsOut || !parameter.IsInit || !IsOutputScalarType(parameter.Type)
                        || value is not BoundOutArgument
                        {
                            IsInit: true,
                            DeclaresVariable: false,
                            Type: var outputType,
                        } output
                        || outputType != parameter.Type
                        || (uint)output.Variable >= (uint)_placeByVariable.Length
                        || _placeByVariable[output.Variable] == 0
                        || _function.Variables[output.Variable].Type != parameter.Type
                        || output.Variable < _function.ParameterCount
                        || !_placeDeclared[_placeByVariable[output.Variable]]
                        || _placeInitialized[_placeByVariable[output.Variable]]
                        || !outputTargets.Add(output.Variable))
                    {
                        throw Unsupported("Mode 0x02 requires a distinct vacant local place of the formal type.");
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
                    _placeInitialized[place] = call.Type != PrimitiveType.Bool;
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
                    throw Unsupported("Non-void output functions must return a value.");
                }
                _returnValue = EmitValue(result.Value);
                if (_valueTypes[_returnValue - 1] != TypeRow(_function.ReturnType)
                    || _function.ReturnType != PrimitiveType.Int32
                        && (!HasOutputParameters(_function) || !IsOutputReturnType(_function.ReturnType)))
                {
                    throw Unsupported("Output-profile return type must match its supported output signature.");
                }
                UseValue(_returnValue, _operationCount);
            }

            for (var place = 1; place < _places.Count; place++)
            {
                UsePlace(place, _operationCount);
                if (_places[place].IsParameter && !_placeInitialized[place])
                {
                    throw Unsupported("Every init ref scalar formal must be initialized before normal return.");
                }
            }
            _ended = true;
        }

        private byte[] BuildBody()
        {
            if (_blocks.Count is not (1 or 3))
            {
                throw Unsupported("Output Bodies support one block or one terminal conditional split.");
            }

            var blockCount = _blocks.Count;
            var liveIns = new int[blockCount][];
            var liveOuts = new int[blockCount][];
            for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                liveIns[blockIndex] = blockIndex == 0 ? []
                    : _definitionBlocks
                        .Select((definitionBlock, index) => (definitionBlock, index))
                        .Where(pair => pair.definitionBlock < blockIndex
                            && _blocks[blockIndex].ValueLastUses.ContainsKey(pair.index + 1))
                        .Select(static pair => pair.index + 1)
                        .ToArray();
                liveOuts[blockIndex] = [];
            }
            if (blockCount == 3)
            {
                liveOuts[0] = liveIns[1].Concat(liveIns[2]).Distinct().Order().ToArray();
            }

            var maximumStructural = Math.Max(blockCount,
                Math.Max(_valueTypes.Count, Math.Max(_places.Count - 1, _parameterValues.Count)));
            foreach (var block in _blocks)
            {
                maximumStructural = Math.Max(maximumStructural,
                    Math.Max(block.OperationCount, Math.Max(block.ValueLastUses.Count,
                        Math.Max(block.PlaceLastUses.Count,
                            block.InheritedPlaces?.Length - 1 ?? 0))));
            }
            var localWidth = Width((ulong)maximumStructural);
            if (localWidth > 4)
            {
                throw Unsupported("Output Body exceeds its bounded local representation.");
            }

            var blocks = new List<byte>[blockCount];
            var operationsStarts = new int[blockCount];
            var terminatorOffsets = new int[blockCount];
            var blockOffsets = new int[blockCount + 1];
            for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                var current = _blocks[blockIndex];
                var block = new List<byte>();
                Write(block, 0, localWidth);
                var parameters = blockIndex == 0 ? _parameterValues : [];
                Write(block, (ulong)parameters.Count, localWidth);
                foreach (var value in parameters)
                {
                    Write(block, (ulong)value, localWidth);
                }
                Write(block, (ulong)liveIns[blockIndex].Length, localWidth);
                foreach (var value in liveIns[blockIndex])
                {
                    Write(block, (ulong)value, localWidth);
                }
                Write(block, (ulong)liveOuts[blockIndex].Length, localWidth);
                foreach (var value in liveOuts[blockIndex])
                {
                    Write(block, (ulong)value, localWidth);
                }

                var inheritedPlaces = current.InheritedPlaces;
                Write(block, (ulong)(inheritedPlaces is null ? 0 : inheritedPlaces.Length - 1),
                    localWidth);
                if (inheritedPlaces is not null)
                {
                    for (var place = 1; place < inheritedPlaces.Length; place++)
                    {
                        Write(block, (ulong)place, localWidth);
                        block.Add(inheritedPlaces[place] ? (byte)1 : (byte)0);
                    }
                }

                var startingPlaces = blockIndex == 0
                    ? Enumerable.Range(1, _places.Count - 1).ToArray() : [];
                Write(block, (ulong)startingPlaces.Length, localWidth);
                foreach (var place in startingPlaces)
                {
                    Write(block, (ulong)place, localWidth);
                }
                var endingPlaces = blockCount == 1 || blockIndex != 0
                    ? startingPlacesForEnd() : [];
                Write(block, (ulong)endingPlaces.Length, localWidth);
                foreach (var place in endingPlaces)
                {
                    Write(block, (ulong)place, localWidth);
                }
                Write(block, (ulong)current.ValueLastUses.Count, localWidth);
                foreach (var (value, ordinal) in current.ValueLastUses.OrderBy(static pair => pair.Key))
                {
                    Write(block, (ulong)value, localWidth);
                    Write(block, (ulong)ordinal, localWidth);
                }
                Write(block, (ulong)current.PlaceLastUses.Count, localWidth);
                foreach (var (place, ordinal) in current.PlaceLastUses.OrderBy(static pair => pair.Key))
                {
                    Write(block, (ulong)place, localWidth);
                    Write(block, (ulong)ordinal, localWidth);
                }
                Write(block, (ulong)current.OperationCount, localWidth);
                operationsStarts[blockIndex] = block.Count;
                block.AddRange(current.Operations);
                terminatorOffsets[blockIndex] = block.Count;
                if (current.Condition != 0)
                {
                    WriteConditionalTerminator(block, current.Condition,
                        trueTarget: 2, falseTarget: 3);
                }
                else if (current.FailureReason == 1)
                {
                    block.Add(0x87);
                    block.Add(1);
                }
                else if (current.FailureReason == 0)
                {
                    WriteInstruction(block, 0x83, Width((ulong)current.ReturnValue), 0);
                    Write(block, (ulong)current.ReturnValue, Width((ulong)current.ReturnValue));
                }
                else
                {
                    throw Unsupported("Output Body failure reason is outside the profile.");
                }
                blocks[blockIndex] = block;
                blockOffsets[blockIndex + 1] = checked(blockOffsets[blockIndex] + block.Count);
            }

            var body = new List<byte>
            {
                (byte)WidthCode(localWidth),
            };
            Write(body, (ulong)blockCount, localWidth);
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
                var type = _function.Variables[_places[place].Variable].Type;
                Write(body, (ulong)_bodyTypeIds[TypeRow(type)], localWidth);
                Write(body, 0, localWidth); // function-root region
                body.Add(0x04); // initially vacant, non-unique, non-rebindable
                var useBlockCount = _blocks.Count(block => block.PlaceLastUses.ContainsKey(place));
                Write(body, (ulong)useBlockCount, localWidth);
            }
            for (var value = 0; value < _valueTypes.Count; value++)
            {
                Write(body, (ulong)_bodyTypeIds[_valueTypes[value]], localWidth);
                body.Add(_valueKinds[value]);
                var useBlockCount = _blocks.Count(block => block.ValueLastUses.ContainsKey(value + 1));
                Write(body, (ulong)useBlockCount, localWidth);
            }
            var definitionStarts = new int[blockCount + 1];
            for (var value = 0; value < _definitionBlocks.Count; value++)
            {
                if (_definitionBlocks[value] < 0 || _definitionBlocks[value] >= blockCount)
                {
                    throw Unsupported("Output Body value definition has no owning block.");
                }
                definitionStarts[_definitionBlocks[value] + 1]++;
            }
            for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                definitionStarts[blockIndex + 1] += definitionStarts[blockIndex];
                Write(body, (ulong)definitionStarts[blockIndex], localWidth);
            }
            Write(body, (ulong)definitionStarts[^1], localWidth);
            for (var value = 0; value < _definitionOffsets.Count; value++)
            {
                var definition = _definitionOffsets[value];
                if (definition != uint.MaxValue)
                {
                    var blockIndex = _definitionBlocks[value];
                    definition = checked((uint)(operationsStarts[blockIndex] + definition));
                }
                Write(body, definition, 4);
            }
            foreach (var offset in blockOffsets)
            {
                Write(body, (ulong)offset, 4);
            }
            foreach (var offset in terminatorOffsets)
            {
                Write(body, (ulong)offset, 4);
            }
            foreach (var block in blocks)
            {
                body.AddRange(block);
            }
            return body.ToArray();

            int[] startingPlacesForEnd()
                => Enumerable.Range(1, _places.Count - 1).ToArray();
        }

        private static void WriteConditionalTerminator(List<byte> bytes,
            int condition, int trueTarget, int falseTarget)
        {
            var referenceWidth = Width((ulong)Math.Max(condition,
                Math.Max(trueTarget, falseTarget)));
            if (referenceWidth == 1)
            {
                bytes.Add(0x81);
            }
            else
            {
                bytes.Add(0xF0);
                bytes.Add((byte)(WidthCode(referenceWidth) | (WidthCode(1) << 2)));
                bytes.Add(0x81);
            }
            Write(bytes, (ulong)condition, referenceWidth);
            Write(bytes, (ulong)trueTarget, referenceWidth);
            Write(bytes, 0, 1);
            Write(bytes, (ulong)falseTarget, referenceWidth);
            Write(bytes, 0, 1);
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

    private sealed record OutputBlock(byte[] Operations, int OperationCount, int ReturnValue,
        int FailureReason, int Condition, Dictionary<int, int> ValueLastUses,
        Dictionary<int, int> PlaceLastUses, bool[]? InheritedPlaces);
}
