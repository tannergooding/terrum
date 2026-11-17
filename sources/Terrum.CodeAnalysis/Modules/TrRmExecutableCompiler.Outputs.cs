using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static bool HasOutputFeature(BoundFunction function, BoundProgram program)
    {
        if (HasOutputParameters(function))
        {
            return true;
        }

        return ContainsOutputFeature(function.Body, program);
    }

    private static bool HasRootlessNativeValues(BoundFunction function)
    {
        var found = function.Variables.Take(function.ParameterCount)
            .Any(static variable => variable.Type == PrimitiveType.NativeRawPointer
                || NativePointerTypes.IsType(variable.Type));
        _ = BoundTreeTraversal.RewriteExpressions(function, expression => {
            found |= expression.Type == PrimitiveType.NativeRawPointer
                || NativePointerTypes.IsType(expression.Type)
                || expression is BoundNativeStorageAddress;
            return expression;
        });
        return found;
    }

    private static bool HasOutputParameters(BoundFunction function)
        => function.Variables.Take(function.ParameterCount)
            .Any(static variable => variable.IsOut || variable.IsInit);

    private static bool ContainsOutputFeature(BoundStatement statement, BoundProgram program)
    {
        return statement switch {
            BoundBlock block => block.Statements.Any(child =>
                ContainsOutputFeature(child, program)),
            BoundVacantLocal => true,
            BoundLocal local => ContainsOutputFeature(local.Initializer, program),
            BoundAssignment assignment => ContainsOutputFeature(assignment.Value, program),
            BoundExpressionStatement expression => ContainsOutputFeature(expression.Expression, program),
            BoundReturn { Value: not null } result => ContainsOutputFeature(result.Value, program),
            BoundIf conditional => ContainsOutputFeature(conditional.Condition, program)
                || ContainsOutputFeature(conditional.Then, program)
                || (conditional.Else is not null
                    && ContainsOutputFeature(conditional.Else, program)),
            BoundWhile loop => ContainsOutputFeature(loop.Condition, program)
                || ContainsOutputFeature(loop.Body, program)
                || (loop.Iterator is not null
                    && ContainsOutputFeature(loop.Iterator, program)),
            BoundUnsafe unsafeBlock => ContainsOutputFeature(unsafeBlock.Body, program),
            BoundRequire require => ContainsOutputFeature(require.Condition, program),
            _ => false,
        };
    }

    private static bool ContainsOutputFeature(BoundExpression expression, BoundProgram program)
    {
        return expression switch {
            BoundOutArgument or BoundNativeStorageAddress => true,
            BoundBinary binary => ContainsOutputFeature(binary.Left, program)
                || ContainsOutputFeature(binary.Right, program),
            BoundUnary unary => ContainsOutputFeature(unary.Operand, program),
            BoundCast cast => ContainsOutputFeature(cast.Operand, program),
            BoundConditional conditional => ContainsOutputFeature(conditional.Condition, program)
                || ContainsOutputFeature(conditional.WhenTrue, program)
                || ContainsOutputFeature(conditional.WhenFalse, program),
            BoundCall call => HasNativeParameterArguments(call, program)
                || call.Arguments.Any(argument => ContainsOutputFeature(argument, program)),
            BoundIndirectCall call => HasNativeParameterArguments(call, program)
                || ContainsOutputFeature(call.Target, program)
                || call.Arguments.Any(argument => ContainsOutputFeature(argument, program)),
            _ => false,
        };
    }

    private static bool HasNativeParameterArguments(BoundCall call, BoundProgram program)
    {
        if ((uint)call.Function >= (uint)program.Functions.Length)
        {
            return false;
        }

        var target = program.Functions[call.Function];
        for (var index = 0; index < call.Arguments.Length; index++)
        {
            var parameterIndex = call.ParameterIndex(index);
            if (DirectNativeImportContract.ParameterForParameter(target, parameterIndex) is not null
                || NativeVTableCallContract.ParameterForParameter(target, parameterIndex) is not null)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasNativeParameterArguments(BoundIndirectCall call, BoundProgram program)
    {
        if (!NativeFunctionPointerTypes.IsType(call.Target.Type))
        {
            return false;
        }

        var signatureIndex = NativeFunctionPointerTypes.Index(call.Target.Type);
        return (uint)signatureIndex < (uint)program.NativeFunctionPointerSignatures.Length
            && !program.NativeFunctionPointerSignatures[signatureIndex].NativeParameters.IsEmpty;
    }

    private static byte[] EncodeOutputBody(BoundFunction function, BoundProgram program,
        List<ReadOnlyMemory<byte>> typeRows,
        Dictionary<PrimitiveType, int> nativeParameterTypeRows,
        Dictionary<PrimitiveType, int> nativeOutputTypeRows,
        Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows,
        Dictionary<PrimitiveType, int> nativePointerTypeRows,
        Dictionary<int, int> structureTypeDefRows,
        Dictionary<int, int> typeDefTypeSpecRows,
        int nativeRawPointerTypeRow)
        => new OutputBodyWriter(function, program, typeRows,
            nativeParameterTypeRows, nativeOutputTypeRows, nativeFunctionPointerTypeRows,
            nativePointerTypeRows, structureTypeDefRows, typeDefTypeSpecRows,
            nativeRawPointerTypeRow).Encode();

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
        ImmutableArray<BoundFunction>.Builder functions,
        int[] functionIndexByMethodRow,
        ImmutableArray<BoundStruct> structures,
        List<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] structureTypeByTypeDef, uint ruleRevision)
    {
        var statements = ImmutableArray.CreateBuilder<BoundStatement>(
            body.Operations.Length + body.Places.Length + 1);
        var placeVariables = new int[body.Places.Length];
        for (var index = 0; index < body.Places.Length; index++)
        {
            var place = body.Places[index];
            var placeType = OutputType(body, types, place.TypeId,
                nativePointerTypeDefinitions, structureTypeByTypeDef);
            if (!IsOutputScalarType(placeType)
                || (ruleRevision == TrRmProvisionalFeatureRevisions.ScalarInitOutputInitialRuleRevision
                    && placeType != PrimitiveType.Int32))
            {
                throw Invalid("The executable place profile has an unsupported referent type.");
            }

            if (place.ParameterOrdinal >= 0)
            {
                if (place.ParameterOrdinal >= variables.Count
                    || !TryGetScalarInitOutputReferentType(
                        variables[place.ParameterOrdinal],
                        checkedReferenceTypes, out var outputType)
                    || outputType != placeType)
                {
                    throw Invalid("An output place does not match its formal parameter.");
                }
                placeVariables[index] = place.ParameterOrdinal;
            }
            else
            {
                placeVariables[index] = variables.Count;
                var addressableLocal = place.Flags is 5 or 7;
                variables.Add(new BoundVariable($"place{index + 1}", placeType,
                    (place.Flags & 2) != 0, default) {
                    IsInitializationLocal = !addressableLocal,
                });
                if (!addressableLocal)
                {
                    statements.Add(new BoundVacantLocal(placeVariables[index], default));
                }
            }
        }

        var valueVariables = new int[body.Values.Length];
        Array.Fill(valueVariables, -1);
        var valueExpressions = new BoundExpression?[body.Values.Length];
        var valueConstants = new BoundConstant?[body.Values.Length];
        var conditionalResult = body.Terminator.Opcode == 0x81
            ? body.Terminator.Operands[0] : 0;
        var conditionalResultIsCall = conditionalResult != 0
            && body.Blocks[0].Operations.Any(operation =>
                operation.Result == conditionalResult && operation.Opcode == 0x12);
        for (var index = 0; index < body.Values.Length; index++)
        {
            var value = body.Values[index];
            var type = OutputType(body, types, value.TypeId,
                nativePointerTypeDefinitions, structureTypeByTypeDef);
            if (!IsOutputBodyValueType(type))
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
                if (index + 1 == conditionalResult && conditionalResultIsCall)
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
            bodyTypes[index] = OutputType(body, types, index + 1,
                nativePointerTypeDefinitions, structureTypeByTypeDef);
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
            if (expression is BoundConstant constant)
            {
                valueConstants[valueId - 1] = constant;
            }
            AddStatement(new BoundLocal(variable, expression, default));
        }

        var unsafeStatements = ImmutableArray.CreateBuilder<BoundStatement>();
        var inUnsafeScope = false;
        var collectingConditionalArm = false;
        void AddStatement(BoundStatement statement)
        {
            (inUnsafeScope && !collectingConditionalArm ? unsafeStatements : statements).Add(statement);
        }

        BoundExpression? conditionalCondition = null;
        void DecodeOperations(ImmutableArray<TrRmScalarInitOutputBodyReader.Instruction> operations,
            TrRmTypeSpecTableReader typeReader)
        {
            foreach (var operation in operations)
            {
                switch (operation.Opcode)
                {
                    case 0x2E:
                        if (inUnsafeScope)
                        {
                            throw Invalid("A place Body contains a duplicate unsafe-scope marker.");
                        }
                        inUnsafeScope = true;
                        break;
                    case 0x2A:
                    {
                        if (operation.Result <= 0 || operation.Result > body.Values.Length
                            || operation.Operands.Length != 1)
                        {
                            throw Invalid("AddressOfPlace requires a result and one place handle.");
                        }

                        var valueInfo = body.Values[operation.Result - 1];
                        if (valueInfo.Kind != 2 || valueInfo.PlaceId <= 0
                            || valueInfo.PlaceId > body.Places.Length)
                        {
                            throw Invalid("AddressOfPlace result has no addressable backing.");
                        }
                        var variable = PlaceVariable(valueInfo.PlaceId);
                        var type = variables[variable].Type;
                        var extent = ValueLayout.Size(type, structures);
                        var pointerTypeRow = body.Types[valueInfo.TypeId - 1].TypeSpecRow;
                        var isOutputPointer = TrRmNativeImportTypeContract.TryGetWritableOutputPointee(
                            typeReader, checked((uint)pointerTypeRow), out var outputPointee)
                            && outputPointee == type;
                        if (!IsOutputScalarType(type) || extent <= 0)
                        {
                            throw Invalid("AddressOfPlace backing is outside the scalar storage profile.");
                        }

                        valueExpressions[operation.Result - 1] = new BoundNativeStorageAddress(
                            variable, new BoundConstant(0, PrimitiveType.Int32, default),
                            type, type, PrimitiveType.NativeStorageAddress, 1,
                            (ulong)extent, (ulong)extent,
                            IsReadonly: !isOutputPointer, default);
                        break;
                    }
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
                        var isNullPointer = operation.Data == 0
                            && (type == PrimitiveType.NativeRawPointer
                                || NativePointerTypes.IsType(type));
                        if ((!IsOutputScalarType(type) && !isNullPointer)
                            || (operation.Opcode == 2 && !IsInteger(type)))
                        {
                            throw Invalid("Constant type is outside the executable place profile.");
                        }

                        var constantValue = isNullPointer ? 0 : type switch {
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
                        var placeId = PlaceForHandle(operation.Operands[0]);
                        var place = PlaceVariable(placeId);
                        var initialValue = ReadValue(operation.Operands[1]);
                        var placeInfo = body.Places[placeId - 1];
                        if (placeInfo.ParameterOrdinal >= 0)
                        {
                            var parameter = variables[place];
                            if (!TryGetScalarInitOutputReferentType(
                                    parameter, checkedReferenceTypes,
                                    out var referentType)
                                || referentType != initialValue.Type)
                            {
                                throw Invalid("An initialization write does not match its reference formal.");
                            }
                            AddStatement(new BoundExpressionStatement(
                                new BoundReferenceWrite(
                                    new BoundRead(place, parameter.Type, default),
                                    initialValue, default), default));
                        }
                        else if (placeInfo.Flags is 5 or 7)
                        {
                            AddStatement(new BoundLocal(place, initialValue, default));
                        }
                        else
                        {
                            AddStatement(new BoundAssignment(place, initialValue, default));
                        }
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
                        var valid = operation.Opcode switch {
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
                        var functionIndex = FunctionIndexForMethodRow(
                            operation.MethodRow, functionIndexByMethodRow);
                        if (functionIndex < 0)
                        {
                            throw Invalid("Call target is outside the module.");
                        }
                        var target = functions[functionIndex];
                        var isNativeImport = target.DirectNativeImport is not null
                            && target.IsExternal;
                        var isNativeVTable = target.NativeVTableMethod is not null
                            && target.Intrinsic == BoundIntrinsic.NativeVTableCall
                            && !target.IsExternal;
                        if ((target.IsExternal && !isNativeImport)
                            || (target.Intrinsic != BoundIntrinsic.None && !isNativeVTable)
                            || target.IsConstructor || target.IsClassConstructor
                            || target.ClassConstructor != -1 || target.DeclaringStruct is not null
                            || target.IsStatic || target.IsCleanup || target.PropertyName is not null
                            || !IsOutputCallResultType(target.ReturnType)
                            || operation.Operands.Length != target.ParameterCount)
                        {
                            throw Invalid("Call target is outside the executable place profile.");
                        }
                        var arguments = ImmutableArray.CreateBuilder<BoundExpression>(target.ParameterCount);
                        for (var parameter = 0; parameter < target.ParameterCount; parameter++)
                        {
                            var formal = target.Variables[parameter];
                            var argument = operation.Operands[parameter];
                            var nativeOutput = isNativeVTable
                                ? NativeVTableCallContract.OutputForParameter(target, parameter)
                                : isNativeImport
                                    ? DirectNativeImportContract.OutputForParameter(target, parameter)
                                    : null;
                            if (nativeOutput is not null)
                            {
                                var value = ReadValue(argument);
                                if (formal.Type != PrimitiveType.NativeStorageAddress
                                    || value is not BoundNativeStorageAddress address
                                    || address.IsReadonly
                                    || address.CarrierType != nativeOutput.PointeeType)
                                {
                                    throw Invalid("Native output address does not match its formal contract.");
                                }
                                arguments.Add(value);
                            }
                            else if (formal.IsOut || formal.IsInit)
                            {
                                if (!TryGetScalarInitOutputReferentType(
                                        formal, checkedReferenceTypes,
                                        out var outputType))
                                {
                                    throw Invalid("The output formal type is outside the executable profile.");
                                }

                                var place = PlaceVariable(PlaceForHandle(argument));
                                var outputVariable = variables[place];
                                if (outputVariable.IsInit && !outputVariable.IsOut)
                                {
                                    if (!CanForwardInitializationReference(
                                            outputVariable, formal,
                                            checkedReferenceTypes, outputType))
                                    {
                                        throw Invalid("An initialization output argument is incompatible with its destination.");
                                    }
                                    arguments.Add(new BoundReferenceReborrow(
                                        new BoundRead(place, outputVariable.Type,
                                            default),
                                        formal.Type, default));
                                }
                                else
                                {
                                    if (outputVariable.Type != outputType
                                        || !outputVariable.IsInitializationLocal)
                                    {
                                        throw Invalid("An initialization output argument must name a vacant local place.");
                                    }
                                    arguments.Add(new BoundReferenceFormation(
                                        new BoundRead(place, outputType, default),
                                        formal.Type, default));
                                }
                            }
                            else
                            {
                                var value = ReadValue(argument);
                                var nativeParameter = isNativeVTable
                                    ? NativeVTableCallContract.ParameterForParameter(target, parameter)
                                    : isNativeImport
                                        ? DirectNativeImportContract.ParameterForParameter(target, parameter)
                                        : null;
                                if (value is BoundNativeStorageAddress)
                                {
                                    if (nativeParameter is not DirectNativeParameter parameterContract
                                        || parameterContract.Retention != NativeParameterRetention.NoEscape
                                        || value is not BoundNativeStorageAddress address
                                        || parameterContract.Extent > address.ElementCount
                                        || !IsOutputScalarType(parameterContract.PointeeType)
                                        || !DirectNativeImportContract.TryGetNativeParameterArgumentType(
                                            formal.Type, parameterContract,
                                            [.. nativePointerTypeDefinitions],
                                            out var pointerArgumentType)
                                        || address.CarrierType != parameterContract.PointeeType
                                        || address.IsReadonly
                                            != (parameterContract.Access == NativeParameterAccess.Read)
                                        || body.Values[argument - 1].TypeId <= 0
                                        || body.Values[argument - 1].TypeId > bodyTypes.Length
                                        || bodyTypes[body.Values[argument - 1].TypeId - 1]
                                            != pointerArgumentType)
                                    {
                                        throw Invalid("Address argument has no matching NativeParameter contract.");
                                    }
                                }
                                else if (nativeParameter is DirectNativeParameter valueContract
                                    ? !DirectNativeImportContract.MatchesNativeParameterArgumentType(
                                        value.Type, formal.Type, valueContract,
                                        [.. nativePointerTypeDefinitions],
                                        DirectNativeImportContract.IsNullNativePointerValue(value))
                                        && !DirectNativeImportContract.IsReadonlyStringParameter(
                                            value.Type, valueContract)
                                    : formal.Type != value.Type)
                                {
                                    throw Invalid("Call argument type does not match its formal.");
                                }
                                arguments.Add(value);
                            }
                        }

                        var call = new BoundCall(functionIndex,
                            arguments.MoveToImmutable(), target.ReturnType, default) {
                            IsUnsafeAuthorized = inUnsafeScope,
                        };
                        if (conditionalResult != 0 && operation.Result == conditionalResult)
                        {
                            conditionalCondition = call;
                            valueExpressions[operation.Result - 1] = call;
                        }
                        else if (operation.Result == 0)
                        {
                            AddStatement(new BoundExpressionStatement(call, default));
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
                                : operation.Kind switch {
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
                            : operation.Kind switch {
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

                        var token = operation.Kind switch {
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
                        if (!isFloat && operation.Kind is >= 10 and <= 12)
                        {
                            if (!IsInteger(left.Type) || left.Type != right.Type)
                            {
                                throw Invalid("Wrapping Binary requires equal integer operands.");
                            }

                            var wrappingToken = operation.Kind switch {
                                10 => TokenKind.Plus,
                                11 => TokenKind.Minus,
                                12 => TokenKind.Star,
                                _ => throw Invalid("Unsupported wrapping Binary kind."),
                            };
                            DefineValue(operation.Result,
                                new BoundBinary(wrappingToken, left, right, left.Type, default) {
                                    OverflowBehavior = BoundIntegerOverflowBehavior.Wrapping,
                                });
                            break;
                        }
                        var valid = left.Type == right.Type && (operation.Kind switch {
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

                        var token = (isFloat, operation.Kind) switch {
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
                                ? integerRight.Type != PrimitiveType.Int32
                                : integerLeft.Type != integerRight.Type))
                        {
                            throw Invalid("Binary operands are outside the executable integer profile.");
                        }

                        var token = operation.Kind switch {
                            1 => TokenKind.Plus,
                            2 => TokenKind.Minus,
                            3 => TokenKind.Star,
                            4 => TokenKind.Slash,
                            5 => TokenKind.Percent,
                            6 => TokenKind.ShiftLeft,
                            7 => TokenKind.ShiftRight,
                            _ => throw Invalid("The executable place profile has an unsupported Binary kind."),
                        };
                        DefineValue(operation.Result, new BoundBinary(token,
                            integerLeft, integerRight, integerLeft.Type, default));
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
                var returnValue = result == 0 ? null
                    : valueConstants[result - 1] ?? ReadValue(result);
                AddStatement(new BoundReturn(returnValue, default));
            }
            else if (terminator.Opcode == 0x87)
            {
                AddStatement(new BoundRequire(new BoundConstant(0, PrimitiveType.Bool, default), default));
            }
            else
            {
                throw Invalid("Unsupported executable place terminator.");
            }
        }

        if (body.Blocks.Length == 1)
        {
            DecodeOperations(body.Operations, types);
            DecodeTerminator(body.Terminator);
            if (inUnsafeScope)
            {
                statements.Add(new BoundUnsafe(new BoundBlock(unsafeStatements.ToImmutable(), default),
                    default));
            }
        }
        else
        {
            if (body.Blocks.Length != 3 || body.Terminator.Opcode != 0x81)
            {
                throw Invalid("Conditional output Body is outside the executable profile.");
            }

            DecodeOperations(body.Blocks[0].Operations, types);
            var conditionValue = body.Terminator.Operands[0];
            conditionalCondition ??= valueConstants[conditionValue - 1]
                ?? ReadValue(conditionValue);
            if (conditionalCondition.Type != PrimitiveType.Bool)
            {
                throw Invalid("Conditional output Body condition is not Boolean.");
            }
            var rootStatements = statements;
            statements = ImmutableArray.CreateBuilder<BoundStatement>();
            collectingConditionalArm = true;
            DecodeOperations(body.Blocks[1].Operations, types);
            DecodeTerminator(body.Blocks[1].Terminator);
            var whenTrue = new BoundBlock(statements.ToImmutable(), default);
            statements = ImmutableArray.CreateBuilder<BoundStatement>();
            DecodeOperations(body.Blocks[2].Operations, types);
            DecodeTerminator(body.Blocks[2].Terminator);
            var whenFalse = new BoundBlock(statements.ToImmutable(), default);
            collectingConditionalArm = false;
            statements = rootStatements;
            AddStatement(new BoundIf(conditionalCondition, whenTrue, whenFalse, default));
            if (inUnsafeScope)
            {
                statements.Add(new BoundUnsafe(
                    new BoundBlock(unsafeStatements.ToImmutable(), default), default));
            }
        }

        return statements.ToImmutable();
    }

    private static PrimitiveType OutputType(TrRmScalarInitOutputBodyReader.Body body,
        TrRmTypeSpecTableReader types, int typeId,
        List<NativePointerTypeDefinition> nativePointerTypeDefinitions,
        int[] structureTypeByTypeDef)
    {
        if (typeId <= 0 || typeId > body.Types.Length)
        {
            throw Invalid("A place Body type ID is outside its map.");
        }

        var typeSpecRow = body.Types[typeId - 1].TypeSpecRow;
        return BodyTypeFromRow(types, typeSpecRow, nativePointerTypeDefinitions,
            structureTypeByTypeDef);
    }

    private static bool IsOutputScalarType(PrimitiveType type)
        => type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32
            or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.NativeInt or PrimitiveType.NativeUInt
            or PrimitiveType.Float32 or PrimitiveType.Float64;

    private static bool TryGetScalarInitOutputReferentType(
        BoundVariable parameter,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes,
        out PrimitiveType referentType)
    {
        referentType = default;
        if (!parameter.IsInit || parameter.IsOut
            || !CheckedReferenceTypeContract.IsValidType(parameter.Type,
                checkedReferenceTypes))
        {
            return false;
        }

        var reference = checkedReferenceTypes[
            CheckedReferenceTypes.Index(parameter.Type)];
        if (!reference.IsInitialization
            || reference.IsGarbageCollected || reference.IsOptional
            || !IsOutputScalarType(reference.ReferentType))
        {
            return false;
        }

        referentType = reference.ReferentType;
        return true;
    }

    private static bool CanForwardInitializationReference(
        BoundVariable source, BoundVariable target,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes,
        PrimitiveType referentType)
    {
        if (!source.IsInit || source.IsOut || !target.IsInit || target.IsOut
            || !IsCompatibleInitializationReferenceType(source.Type,
                target.Type, referentType, checkedReferenceTypes))
        {
            return false;
        }

        return true;
    }

    private static bool IsCompatibleInitializationReferenceType(
        PrimitiveType sourceType, PrimitiveType targetType,
        PrimitiveType referentType,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes)
    {
        if (!CheckedReferenceTypeContract.IsValidType(sourceType,
                checkedReferenceTypes)
            || !CheckedReferenceTypeContract.IsValidType(targetType,
                checkedReferenceTypes))
        {
            return false;
        }

        var source = checkedReferenceTypes[CheckedReferenceTypes.Index(sourceType)];
        var target = checkedReferenceTypes[CheckedReferenceTypes.Index(targetType)];
        return source.IsInitialization && target.IsInitialization
            && !source.IsGarbageCollected && !target.IsGarbageCollected
            && !source.IsOptional && !target.IsOptional
            && source.ReferentType == referentType
            && target.ReferentType == referentType
            && (!target.IsWritable || source.IsWritable);
    }

    private static bool IsOutputBodyValueType(PrimitiveType type)
        => IsOutputScalarType(type) || type == PrimitiveType.NativeRawPointer
            || NativePointerTypes.IsType(type);

    private static bool IsOutputIntegerConversionType(PrimitiveType type)
        => IsInteger(type) || type == PrimitiveType.Char16;

    private static bool IsOutputConversionType(PrimitiveType type)
        => IsOutputScalarType(type) && type != PrimitiveType.Bool;

    private static bool IsOutputIntegerBinary(BoundBinary binary)
        => binary.Type == binary.Left.Type && IsInteger(binary.Left.Type)
            && (binary.Operator switch {
                TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                    or TokenKind.Slash or TokenKind.Percent => binary.Right.Type == binary.Left.Type,
                TokenKind.ShiftLeft or TokenKind.ShiftRight =>
                    binary.Right.Type == PrimitiveType.Int32,
                TokenKind.ShiftRightUnsigned => binary.Right.Type == PrimitiveType.Int32
                    && binary.Left.Type is PrimitiveType.UInt32 or PrimitiveType.UInt64
                        or PrimitiveType.NativeUInt,
                _ => false,
            });

    private static bool IsOutputCallResultType(PrimitiveType type)
        => type == PrimitiveType.Void || IsOutputScalarType(type);

    private static bool IsOutputReturnType(PrimitiveType type)
        => type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.NativeInt or PrimitiveType.NativeUInt or PrimitiveType.Float32
            or PrimitiveType.Float64;

    private sealed class OutputBodyWriter
    {
        private readonly BoundFunction _function;
        private readonly BoundProgram _program;
        private readonly List<ReadOnlyMemory<byte>> _typeRows;
        private readonly Dictionary<PrimitiveType, int> _nativeParameterTypeRows;
        private readonly Dictionary<PrimitiveType, int> _nativeOutputTypeRows;
        private readonly Dictionary<PrimitiveType, int> _nativeFunctionPointerTypeRows;
        private readonly Dictionary<PrimitiveType, int> _nativePointerTypeRows;
        private readonly Dictionary<int, int> _structureTypeDefRows;
        private readonly Dictionary<int, int> _typeDefTypeSpecRows;
        private readonly int _nativeRawPointerTypeRow;
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
        private bool[]? _fallthroughInheritedPlaces;
        private bool _ended;
        private bool _unsafeScope;

        public OutputBodyWriter(BoundFunction function, BoundProgram program,
            List<ReadOnlyMemory<byte>> typeRows,
            Dictionary<PrimitiveType, int> nativeParameterTypeRows,
            Dictionary<PrimitiveType, int> nativeOutputTypeRows,
            Dictionary<PrimitiveType, int> nativeFunctionPointerTypeRows,
            Dictionary<PrimitiveType, int> nativePointerTypeRows,
            Dictionary<int, int> structureTypeDefRows,
            Dictionary<int, int> typeDefTypeSpecRows,
            int nativeRawPointerTypeRow)
        {
            _function = function;
            _program = program;
            _typeRows = typeRows;
            _nativeParameterTypeRows = nativeParameterTypeRows;
            _nativeOutputTypeRows = nativeOutputTypeRows;
            _nativeFunctionPointerTypeRows = nativeFunctionPointerTypeRows;
            _nativePointerTypeRows = nativePointerTypeRows;
            _structureTypeDefRows = structureTypeDefRows;
            _typeDefTypeSpecRows = typeDefTypeSpecRows;
            _nativeRawPointerTypeRow = nativeRawPointerTypeRow;
            _placeByVariable = new int[function.Variables.Length];
            _currentValues = new int[function.Variables.Length];
            var addressableVariables = new HashSet<int>();
            _ = BoundTreeTraversal.RewriteExpressions(function, expression => {
                if (expression is BoundNativeStorageAddress address)
                {
                    _ = addressableVariables.Add(address.Variable);
                }
                return expression;
            });
            if (function.Variables.Length > SemanticVerifier.MaximumVariables
                || (function.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32)
                    && (!HasOutputParameters(function) || !IsOutputReturnType(function.ReturnType))))
            {
                throw Unsupported("Output Bodies support at most 256 variables and scalar results only for output functions.");
            }

            for (var variable = 0; variable < function.ParameterCount; variable++)
            {
                var parameter = function.Variables[variable];
                if (parameter.IsOut || parameter.IsInit)
                {
                    if (parameter.IsOut || !parameter.IsInit
                        || !IsOutputScalarType(
                            InitializationOutputReferentType(parameter)))
                    {
                        throw Unsupported("The output Body profile requires scalar init ref parameters.");
                    }
                    AddPlace(variable, isParameter: true);
                }
                else
                {
                    if (!IsOutputBodyValueType(parameter.Type))
                    {
                        throw Unsupported("The output Body profile requires scalar or raw-pointer by-value parameters.");
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
                else if (addressableVariables.Contains(variable))
                {
                    if (local.IsOut || local.IsInit || !IsOutputScalarType(local.Type))
                    {
                        throw Unsupported("Native addresses require initialized scalar local backing.");
                    }
                    AddPlace(variable, isParameter: false, isAddressableLocal: true);
                }
                else if (!IsOutputScalarType(local.Type))
                {
                    throw Unsupported("The output Body profile currently supports only scalar locals.");
                }
            }
            if (addressableVariables.Any(variable => variable < function.ParameterCount
                    || (uint)variable >= (uint)function.Variables.Length
                    || _placeByVariable[variable] == 0))
            {
                throw Unsupported("Native addresses require addressable scalar local variables.");
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

            for (var statementIndex = 0;
                statementIndex < _function.Body.Statements.Length; statementIndex++)
            {
                var statement = _function.Body.Statements[statementIndex];
                if (statement is BoundUnsafe unsafeBlock)
                {
                    if (statementIndex != _function.Body.Statements.Length - 1 || _unsafeScope)
                    {
                        throw Unsupported("The output Body profile supports one terminal unsafe scope.");
                    }
                    EmitUnsafeTail(unsafeBlock);
                }
                else
                {
                    EmitStatement(statement);
                }
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
            else if (_blocks.Count == 2
                && _fallthroughInheritedPlaces is not null)
            {
                CaptureBlock(inheritedPlaces: _fallthroughInheritedPlaces);
                _ended = true;
            }
            return BuildBody();
        }

        private void EmitUnsafeTail(BoundUnsafe unsafeBlock)
        {
            if (unsafeBlock.Body.Statements.IsDefaultOrEmpty)
            {
                throw Unsupported("The output Body unsafe scope must contain a terminal statement.");
            }

            _operations.Add(0x2E);
            _operationCount++;
            _unsafeScope = true;
            foreach (var statement in unsafeBlock.Body.Statements)
            {
                EmitStatement(statement);
            }
            _unsafeScope = false;
            if (!_ended)
            {
                throw Unsupported("The output Body unsafe scope must extend through a terminal return.");
            }
        }

        private void AddPlace(int variable, bool isParameter, bool isAddressableLocal = false)
        {
            var place = _places.Count;
            _places.Add(new OutputPlace(variable, isParameter, isAddressableLocal));
            _placeInitialized.Add(false);
            _placeDeclared.Add(isParameter || isAddressableLocal);
            _placeHandles.Add(0);
            _placeByVariable[variable] = place;
        }

        private PrimitiveType PlaceValueType(int variable)
        {
            var value = _function.Variables[variable];
            return value.IsInit && !value.IsOut
                ? InitializationOutputReferentType(value)
                : value.Type;
        }

        private PrimitiveType InitializationOutputReferentType(
            BoundVariable parameter)
        {
            if (!parameter.IsInit || parameter.IsOut
                || !CheckedReferenceTypeContract.IsValidType(parameter.Type,
                    _program.CheckedReferenceTypes))
            {
                throw Unsupported(
                    "The output Body profile requires canonical ref<init T> formals.");
            }

            var reference = _program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(parameter.Type)];
            if (!reference.IsInitialization
                || reference.IsGarbageCollected || reference.IsOptional
                || !IsOutputScalarType(reference.ReferentType))
            {
                throw Unsupported(
                    "The output Body profile requires scalar ref<init T> referents.");
            }

            return reference.ReferentType;
        }

        private int AddValue(PrimitiveType type, byte kind, uint definitionOffset)
            => AddValue(type, kind, definitionOffset, null);

        private int AddValue(PrimitiveType type, byte kind, uint definitionOffset,
            int? typeRowOverride)
        {
            if (!IsOutputBodyValueType(type) || kind > 2
                || (kind == 1 && !IsOutputScalarType(type))
                || (kind == 2 && type != PrimitiveType.NativeRawPointer
                    && !NativePointerTypes.IsType(type))
                || _valueTypes.Count >= SemanticVerifier.MaximumVariables)
            {
                throw Unsupported("The output Body profile supports scalar and typed pointer values within the verifier limit.");
            }

            var typeRow = typeRowOverride ?? BodyTypeRow(type);
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
            var type = PlaceValueType(_places[place].Variable);
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
                        || !IsOutputScalarType(local.Initializer.Type)
                        || local.Initializer.Type != _function.Variables[local.Variable].Type)
                    {
                        throw Unsupported("The output Body profile supports initialized scalar value locals.");
                    }
                    var localPlace = _placeByVariable[local.Variable];
                    if (localPlace != 0)
                    {
                        if (!_places[localPlace].IsAddressableLocal
                            || !_placeDeclared[localPlace] || _placeInitialized[localPlace])
                        {
                            throw Unsupported("Addressable locals require one initialized declaration.");
                        }
                        EmitAssignment(new BoundAssignment(local.Variable, local.Initializer, local.Span));
                    }
                    else
                    {
                        _currentValues[local.Variable] = EmitValue(local.Initializer);
                    }
                    break;
                case BoundAssignment assignment:
                    EmitAssignment(assignment);
                    break;
                case BoundExpressionStatement {
                    Expression: BoundReferenceWrite referenceWrite,
                }:
                    EmitInitializationReferenceWrite(referenceWrite);
                    break;
                case BoundExpressionStatement { Expression: BoundCall call }:
                    if (EmitCall(call) != 0)
                    {
                        throw Unsupported("Only void calls may be used as output-profile statements.");
                    }
                    break;
                case BoundIf {
                    Condition: BoundConstant { Type: PrimitiveType.Bool } condition,
                } conditional:
                    EmitInlineStatements(condition.Value != 0
                        ? conditional.Then : conditional.Else);
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

        private void EmitInlineStatements(BoundStatement? statement)
        {
            if (statement is BoundBlock block)
            {
                foreach (var child in block.Statements)
                {
                    EmitStatement(child);
                }
            }
            else if (statement is not null)
            {
                EmitStatement(statement);
            }
        }

        private void EmitConditional(BoundIf conditional)
        {
            if (conditional.Condition.Type != PrimitiveType.Bool)
            {
                throw Unsupported("Output Body conditionals require a Boolean condition.");
            }

            var call = conditional.Condition as BoundCall;
            var condition = EmitValue(conditional.Condition);
            UseValue(condition, _operationCount);

            var outputPlaces = new List<int>();
            if (call is not null)
            {
                var target = _program.Functions[call.Function];
                for (var index = 0; index < call.Arguments.Length; index++)
                {
                    var parameter = target.Variables[call.ParameterIndex(index)];
                    if (!parameter.IsInit || parameter.IsOut)
                    {
                        continue;
                    }

                    var referentType = InitializationOutputReferentType(parameter);
                    if (!TryGetOutputDestination(call.Arguments[index], parameter,
                            referentType, out var outputVariable)
                        || _placeByVariable[outputVariable] == 0)
                    {
                        throw Unsupported(
                            "Conditional output destinations must be local places or compatible init-reference formals.");
                    }
                    outputPlaces.Add(_placeByVariable[outputVariable]);
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
            if (!EmitBranchTerminal(conditional.Then))
            {
                throw Unsupported(
                    "Output Body true arms must terminate before the false-arm continuation.");
            }
            CaptureBlock(inheritedPlaces: trueEntryState);

            _currentBlock = 2;
            RestorePlaceState(falseState);
            if (conditional.Else is null)
            {
                _fallthroughInheritedPlaces = _placeInitialized.ToArray();
                return;
            }
            var falseEntryState = _placeInitialized.ToArray();
            if (!EmitBranchTerminal(conditional.Else))
            {
                throw Unsupported(
                    "Output Body false arms must terminate.");
            }
            CaptureBlock(inheritedPlaces: falseEntryState);
            _ended = true;
        }

        private bool EmitBranchTerminal(BoundStatement statement)
        {
            var terminal = statement switch {
                BoundBlock { RetiredManagedLocals.IsEmpty: true, Statements.Length: 1 } block
                    => block.Statements[0],
                _ => statement,
            };

            if (terminal is BoundBlock branchBlock)
            {
                for (var index = 0; index < branchBlock.Statements.Length; index++)
                {
                    EmitStatement(branchBlock.Statements[index]);
                    if (_ended && index != branchBlock.Statements.Length - 1)
                    {
                        throw Unsupported(
                            "Output Body branch statements cannot follow a terminal operation.");
                    }
                }
                return _ended;
            }

            switch (terminal)
            {
                case BoundReturn returned:
                    EmitReturn(returned);
                    return true;
                case BoundRequire {
                    Condition: BoundConstant { Type: PrimitiveType.Bool, Value: 0 },
                }:
                    for (var place = 1; place < _places.Count; place++)
                    {
                        UsePlace(place, _operationCount);
                    }
                    _failureReason = 1;
                    _ended = true;
                    return true;
                default:
                    return false;
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
            _blocks.Add(new OutputBlock([.. _operations], _operationCount, _returnValue,
                _failureReason, condition, new Dictionary<int, int>(_valueLastUses),
                new Dictionary<int, int>(_placeLastUses),
                _currentBlock == 0 ? null : inheritedPlaces ?? [.. _placeInitialized]));
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
                || _valueTypes[value - 1] != BodyTypeRow(variableType))
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
            EmitPlaceInitialization(place, variableType, assignment.Value);
        }

        private void EmitInitializationReferenceWrite(
            BoundReferenceWrite referenceWrite)
        {
            if (referenceWrite.Descriptor is not BoundRead descriptor
                || (uint)descriptor.Variable >= (uint)_function.ParameterCount)
            {
                throw Unsupported(
                    "The output Body profile supports writes through direct init-reference formals.");
            }

            var parameter = _function.Variables[descriptor.Variable];
            var referentType = InitializationOutputReferentType(parameter);
            if (descriptor.Type != parameter.Type
                || referenceWrite.Value.Type != referentType)
            {
                throw Unsupported(
                    "An initialization-reference write must preserve its formal and referent types.");
            }

            var place = _placeByVariable[descriptor.Variable];
            if (place == 0 || !_places[place].IsParameter)
            {
                throw Unsupported(
                    "An initialization-reference write requires its formal place.");
            }

            EmitPlaceInitialization(place, referentType, referenceWrite.Value);
        }

        private void EmitPlaceInitialization(int place, PrimitiveType type,
            BoundExpression valueExpression)
        {
            if (place <= 0 || place >= _places.Count
                || !_placeDeclared[place] || _placeInitialized[place])
            {
                throw Unsupported("Initialize requires an active vacant place.");
            }

            var value = EmitValue(valueExpression);
            if (!IsOutputScalarType(type)
                || _valueTypes[value - 1] != BodyTypeRow(type))
            {
                throw Unsupported("Output initialization requires an exactly typed scalar value.");
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
                case BoundConstant { Value: 0 } constant
                when constant.Type == PrimitiveType.NativeRawPointer
                    || NativePointerTypes.IsType(constant.Type):
                    var pointerResult = _valueTypes.Count + 1;
                    var pointerTypeId = BodyTypeId(BodyTypeRow(constant.Type));
                    var pointerReferenceWidth = Width((ulong)Math.Max(pointerResult, pointerTypeId));
                    var pointerOffset = _operations.Count;
                    WriteInstruction(_operations, 0x01, pointerReferenceWidth, 1);
                    Write(_operations, (ulong)pointerResult, pointerReferenceWidth);
                    Write(_operations, (ulong)pointerTypeId, pointerReferenceWidth);
                    Write(_operations, 0, 1);
                    _ = AddValue(constant.Type, 0, (uint)pointerOffset);
                    _operationCount++;
                    return pointerResult;
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
                    if (current == 0 || _valueTypes[current - 1] != BodyTypeRow(read.Type))
                    {
                        throw Unsupported("Read of an uninitialized or mismatched scalar value.");
                    }
                    return current;
                case BoundConstant constant when IsOutputScalarType(constant.Type):
                    var constResult = _valueTypes.Count + 1;
                    var bits = constant.Type switch {
                        PrimitiveType.Bool or PrimitiveType.Char16 => (ulong)constant.Value,
                        PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Float32
                            => unchecked((uint)constant.Value),
                        _ => unchecked((ulong)constant.Value),
                    };
                    var dataWidth = Width(bits);
                    var constantTypeId = BodyTypeId(BodyTypeRow(constant.Type));
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

                    var conversionOpcode = (cast.Operand.Type, cast.Type) switch {
                        (PrimitiveType.Float32 or PrimitiveType.Float64,
                            PrimitiveType.Float32 or PrimitiveType.Float64) => (byte)0x1A,
                        (PrimitiveType.Int32 or PrimitiveType.UInt32
                            or PrimitiveType.Int64 or PrimitiveType.UInt64
                            or PrimitiveType.NativeInt or PrimitiveType.NativeUInt,
                            PrimitiveType.Float32 or PrimitiveType.Float64) => (byte)0x1B,
                        (PrimitiveType.Char16, PrimitiveType.Float32 or PrimitiveType.Float64) => (byte)0x1E,
                        (PrimitiveType.Float32 or PrimitiveType.Float64,
                            PrimitiveType.Int32 or PrimitiveType.UInt32
                                or PrimitiveType.Int64 or PrimitiveType.UInt64
                                or PrimitiveType.NativeInt or PrimitiveType.NativeUInt) => (byte)0x1F,
                        (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                            or PrimitiveType.UInt64 or PrimitiveType.NativeInt
                            or PrimitiveType.NativeUInt or PrimitiveType.Char16,
                            PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                                or PrimitiveType.UInt64 or PrimitiveType.NativeInt
                                or PrimitiveType.NativeUInt or PrimitiveType.Char16) => (byte)0x16,
                        _ => throw Unsupported("Conversion is outside the output Body scalar profile."),
                    };
                    var castOperand = EmitValue(cast.Operand);
                    var castResult = _valueTypes.Count + 1;
                    var castTypeId = BodyTypeId(BodyTypeRow(cast.Type));
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
                    var floatingKind = binary.Operator switch {
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
                    && (unary.Operator switch {
                        TokenKind.Bang => unary.Type == PrimitiveType.Bool,
                        TokenKind.Tilde or TokenKind.Plus => IsInteger(unary.Type),
                        TokenKind.Minus => IsFloat(unary.Type) || IsInteger(unary.Type),
                        _ => false,
                    }):
                {
                    var unaryOperand = EmitValue(unary.Operand);
                    if (unary.Operator == TokenKind.Minus && IsInteger(unary.Type)
                        && (unary.Type is PrimitiveType.UInt32 or PrimitiveType.UInt64
                            or PrimitiveType.NativeUInt
                            || unary.OverflowBehavior == BoundIntegerOverflowBehavior.Wrapping))
                    {
                        var zero = EmitValue(new BoundConstant(0, unary.Type, default));
                        return EmitIntegerArithmetic(TokenKind.Minus, unary.OverflowBehavior,
                            zero, unaryOperand, unary.Type);
                    }
                    var unaryResult = _valueTypes.Count + 1;
                    var unaryOpcode = unary.Operator == TokenKind.Minus
                        ? IsFloat(unary.Type) ? (byte)0x1C : (byte)0x17
                        : (byte)4;
                    var unaryKind = unary.Operator switch {
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
                    var bitwiseKind = binary.Operator switch {
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
                        || (!isFloat && (isEquality
                            ? binary.Left.Type is not (PrimitiveType.Bool or PrimitiveType.Char16
                                or PrimitiveType.Int32 or PrimitiveType.UInt32
                                or PrimitiveType.Int64 or PrimitiveType.UInt64
                                or PrimitiveType.NativeInt or PrimitiveType.NativeUInt)
                            : !IsInteger(binary.Left.Type))))
                    {
                        throw Unsupported("Comparison operands are outside the output Body profile.");
                    }

                    var comparisonLeft = EmitValue(binary.Left);
                    var comparisonRight = EmitValue(binary.Right);
                    var compareResult = _valueTypes.Count + 1;
                    var compareKind = binary.Operator switch {
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
                    var integerRight = EmitValue(binary.Right);
                    var integerResult = _valueTypes.Count + 1;
                    var integerKind = binary.Operator switch {
                        TokenKind.Plus => 1,
                        TokenKind.Minus => 2,
                        TokenKind.Star => 3,
                        TokenKind.Slash => 4,
                        TokenKind.Percent => 5,
                        TokenKind.ShiftLeft => 6,
                        _ => 7,
                    };
                    var integerOpcode = 0x15;
                    if (binary.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star)
                    {
                        (integerOpcode, integerKind) = IntegerArithmeticEncoding(
                            binary.Operator, binary.OverflowBehavior);
                    }
                    var binaryWidth = Width((ulong)Math.Max(integerResult,
                        Math.Max(integerLeft, integerRight)));
                    var binaryOffset = _operations.Count;
                    WriteInstruction(_operations, integerOpcode, binaryWidth, 0);
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

        private int EmitIntegerArithmetic(TokenKind operation,
            BoundIntegerOverflowBehavior overflowBehavior, int left, int right, PrimitiveType type)
        {
            if (!IsInteger(type))
            {
                throw Unsupported("Integer arithmetic requires an admitted integer type.");
            }

            var (opcode, kind) = IntegerArithmeticEncoding(operation, overflowBehavior);
            var result = _valueTypes.Count + 1;
            var width = Width((ulong)Math.Max(result, Math.Max(left, right)));
            var offset = _operations.Count;
            WriteInstruction(_operations, opcode, width, 0);
            Write(_operations, (ulong)result, width);
            _operations.Add((byte)kind);
            Write(_operations, (ulong)left, width);
            Write(_operations, (ulong)right, width);
            _ = AddValue(type, 0, (uint)offset);
            UseValue(left, _operationCount);
            UseValue(right, _operationCount);
            _operationCount++;
            return result;
        }

        private int EmitCall(BoundCall call)
        {
            if ((uint)call.Function >= (uint)_program.Functions.Length
                || (call.ArgumentParameterIndices.IsDefault == false
                    && call.ArgumentParameterIndices.Length != call.Arguments.Length))
            {
                throw Unsupported("Call target or argument mapping is invalid.");
            }

            var target = _program.Functions[call.Function];
            var isNativeImport = target.IsExternal && target.DirectNativeImport is not null
                && DirectNativeImportContract.IsValid(target, _program.Structures,
                    _program.NativeFunctionPointerSignatures,
                    _program.NativePointerTypes, _program.EnumDefinitions);
            var isNativeVTable = !target.IsExternal
                && target.NativeVTableMethod is not null
                && target.Intrinsic == BoundIntrinsic.NativeVTableCall
                && NativeVTableCallContract.IsValid(target, _program);
            if ((target.IsExternal && !isNativeImport)
                || target.IsConstructor || target.IsClassConstructor
                || target.ClassConstructor != -1 || target.DeclaringStruct is not null
                || target.IsStatic || target.IsCleanup || target.PropertyName is not null
                || (target.Intrinsic != BoundIntrinsic.None && !isNativeVTable)
                || target.ParameterCount != call.Arguments.Length
                || target.ReturnType != call.Type
                || !IsOutputCallResultType(target.ReturnType))
            {
                throw Unsupported("Output calls require a local scalar function with an exact signature.");
            }
            if (target.RequiresUnsafeCaller && !_unsafeScope)
            {
                throw Unsupported("Caller-unsafe calls require an encoded unsafe scope.");
            }

            var outputTarget = false;
            for (var parameterIndex = 0; parameterIndex < target.ParameterCount; parameterIndex++)
            {
                var parameter = target.Variables[parameterIndex];
                if ((parameter.IsOut || parameter.IsInit)
                    && !(isNativeVTable
                        && NativeVTableCallContract.OutputForParameter(target, parameterIndex) is not null))
                {
                    outputTarget = true;
                }
            }
            if (outputTarget && ((!target.IsPrivate && !target.IsInternal)
                || (target.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32)
                    && !IsOutputReturnType(target.ReturnType))))
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
                var nativeOutput = isNativeVTable
                    ? NativeVTableCallContract.OutputForParameter(target, argument)
                    : isNativeImport
                        ? DirectNativeImportContract.OutputForParameter(target, argument)
                        : null;
                if (nativeOutput is not null)
                {
                    if (!_unsafeScope || value is not BoundNativeStorageAddress address
                        || !DirectNativeImportContract.MatchesAddress(
                            address, nativeOutput, _function, _program)
                        || !IsOutputScalarType(nativeOutput.PointeeType)
                        || parameter.Type != PrimitiveType.NativeStorageAddress)
                    {
                        throw Unsupported("Native output requires an exact writable scalar address.");
                    }

                    var pointerTypeRow = NativeOutputTypeRow(_typeRows,
                        _nativeOutputTypeRows, nativeOutput.PointeeType);
                    arguments[argument] = EmitNativeStorageAddress(address, pointerTypeRow,
                        PrimitiveType.NativeRawPointer);
                }
                else if (parameter.IsOut || parameter.IsInit)
                {
                    var parameterType
                        = InitializationOutputReferentType(parameter);
                    if (!IsOutputScalarType(parameterType)
                        || !TryGetOutputDestination(value, parameter,
                            parameterType, out var outputVariable))
                    {
                        throw Unsupported(
                            "Mode 0x02 requires a vacant local formation or compatible init-reference reborrow.");
                    }
                    if ((uint)outputVariable >= (uint)_placeByVariable.Length
                        || _placeByVariable[outputVariable] == 0)
                    {
                        throw Unsupported(
                            "An initialization destination has no Body place.");
                    }

                    var outputPlace = _placeByVariable[outputVariable];
                    if (!_placeDeclared[outputPlace]
                        || _placeInitialized[outputPlace]
                        || !outputTargets.Add(outputVariable))
                    {
                        throw Unsupported(
                            "Mode 0x02 requires a distinct vacant destination.");
                    }
                    arguments[argument] = _placeHandles[outputPlace];
                    outputPlaces[argument] = outputPlace;
                }
                else
                {
                    var nativeParameter = isNativeVTable
                        ? NativeVTableCallContract.ParameterForParameter(target, argument)
                        : isNativeImport
                            ? DirectNativeImportContract.ParameterForParameter(target, argument)
                            : null;
                    if (value is BoundNativeStorageAddress address)
                    {
                        if (!_unsafeScope || nativeParameter is not DirectNativeParameter parameterContract
                            || parameterContract.Retention != NativeParameterRetention.NoEscape
                            || parameterContract.Extent > address.ElementCount
                            || !IsOutputScalarType(parameterContract.PointeeType)
                            || !DirectNativeImportContract.TryGetNativeParameterArgumentType(
                                parameter.Type, parameterContract, _program.NativePointerTypes,
                                out var pointerValueType)
                            || !DirectNativeImportContract.MatchesAddress(
                                address, parameterContract, _function, _program))
                        {
                            throw Unsupported("Address arguments require an exact scalar NativeParameter contract.");
                        }

                        var pointerTypeRow = BodyTypeRow(pointerValueType);
                        arguments[argument] = EmitNativeStorageAddress(address, pointerTypeRow,
                            pointerValueType);
                    }
                    else
                    {
                        if (value is BoundOutArgument || !IsOutputBodyValueType(parameter.Type))
                        {
                            throw Unsupported("By-value output-profile arguments must be scalar or raw-pointer values.");
                        }
                        arguments[argument] = EmitValue(value);
                        var matchesParameter = nativeParameter is DirectNativeParameter parameterContract
                            ? DirectNativeImportContract.MatchesNativeParameterArgumentType(
                                value.Type, parameter.Type, parameterContract,
                                _program.NativePointerTypes,
                                DirectNativeImportContract.IsNullNativePointerValue(value))
                                || DirectNativeImportContract.IsReadonlyStringParameter(
                                    value.Type, parameterContract)
                            : value.Type == parameter.Type;
                        if (_valueTypes[arguments[argument] - 1] != BodyTypeRow(value.Type)
                            || !matchesParameter)
                        {
                            throw Unsupported("Call argument type does not match its parameter.");
                        }
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

        private bool TryGetOutputDestination(BoundExpression argument,
            BoundVariable target, PrimitiveType referentType,
            out int variable)
        {
            switch (argument)
            {
                case BoundReferenceFormation {
                    Place: BoundRead place,
                } formation
                    when (uint)place.Variable < (uint)_function.Variables.Length:
                {
                    var local = _function.Variables[place.Variable];
                    if (!local.IsInitializationLocal
                        || local.Type != referentType
                        || place.Type != local.Type
                        || !IsCompatibleInitializationReferenceType(
                            formation.Type, target.Type, referentType,
                            _program.CheckedReferenceTypes))
                    {
                        break;
                    }

                    variable = place.Variable;
                    return true;
                }
                case BoundReferenceReborrow {
                    Descriptor: BoundRead descriptor,
                } reborrow
                    when (uint)descriptor.Variable < (uint)_function.ParameterCount:
                {
                    var source = _function.Variables[descriptor.Variable];
                    if (descriptor.Type != source.Type
                        || !CanForwardInitializationReference(source, target,
                            _program.CheckedReferenceTypes, referentType)
                        || !IsCompatibleInitializationReferenceType(
                            reborrow.Type, target.Type, referentType,
                            _program.CheckedReferenceTypes))
                    {
                        break;
                    }

                    variable = descriptor.Variable;
                    return true;
                }
                case BoundOutArgument output
                    when output.IsInit && !output.DeclaresVariable
                        && (uint)output.Variable < (uint)_function.Variables.Length:
                {
                    var source = _function.Variables[output.Variable];
                    if (output.Variable < _function.ParameterCount
                        ? !CanForwardInitializationReference(source, target,
                            _program.CheckedReferenceTypes, referentType)
                            || output.Type != source.Type
                        : !source.IsInitializationLocal
                            || source.Type != referentType
                            || output.Type != referentType)
                    {
                        break;
                    }

                    variable = output.Variable;
                    return true;
                }
            }

            variable = -1;
            return false;
        }

        private int EmitNativeStorageAddress(BoundNativeStorageAddress address,
            int pointerTypeRow, PrimitiveType pointerValueType)
        {
            if ((uint)address.Variable >= (uint)_placeByVariable.Length
                || _placeByVariable[address.Variable] == 0)
            {
                throw Unsupported("Native address backing is not an addressable local place.");
            }

            var place = _placeByVariable[address.Variable];
            if (!_placeDeclared[place] || !_placeInitialized[place]
                || !_places[place].IsAddressableLocal)
            {
                throw Unsupported("Native address backing must be a declared initialized local.");
            }

            var pointerTypeId = BodyTypeId(pointerTypeRow);
            var handle = _placeHandles[place];
            var result = _valueTypes.Count + 1;
            var width = Width((ulong)Math.Max(result, Math.Max(pointerTypeId, handle)));
            var offset = _operations.Count;
            WriteInstruction(_operations, 0x2A, width, 0);
            Write(_operations, (ulong)result, width);
            Write(_operations, (ulong)pointerTypeId, width);
            Write(_operations, (ulong)handle, width);
            _ = AddValue(pointerValueType, 2, (uint)offset, pointerTypeRow);
            UseValue(handle, _operationCount);
            UsePlace(place, _operationCount);
            _operationCount++;
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
                if (_valueTypes[_returnValue - 1] != BodyTypeRow(_function.ReturnType)
                    || (_function.ReturnType != PrimitiveType.Int32
                        && (!HasOutputParameters(_function) || !IsOutputReturnType(_function.ReturnType))))
                {
                    throw Unsupported("Output-profile return type must match its supported output signature.");
                }
                UseValue(_returnValue, _operationCount);
            }

            for (var place = 1; place < _places.Count; place++)
            {
                UsePlace(place, _operationCount);
                if (_places[place].IsParameter
                    && _function.ReturnType != PrimitiveType.Bool
                    && !_placeInitialized[place])
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
                    : [.. _definitionBlocks
                        .Select((definitionBlock, index) => (definitionBlock, index))
                        .Where(pair => pair.definitionBlock < blockIndex
                            && _blocks[blockIndex].ValueLastUses.ContainsKey(pair.index + 1))
                        .Select(static pair => pair.index + 1)];
                liveOuts[blockIndex] = [];
            }
            if (blockCount == 3)
            {
                liveOuts[0] = [.. liveIns[1].Concat(liveIns[2]).Distinct().Order()];
            }

            var maximumStructural = Math.Max(blockCount,
                Math.Max(_valueTypes.Count, Math.Max(_places.Count - 1, _parameterValues.Count)));
            foreach (var block in _blocks)
            {
                maximumStructural = Math.Max(maximumStructural,
                    Math.Max(block.OperationCount, Math.Max(block.ValueLastUses.Count,
                        Math.Max(block.PlaceLastUses.Count,
                            (block.InheritedPlaces?.Length - 1) ?? 0))));
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
                var type = PlaceValueType(_places[place].Variable);
                Write(body, (ulong)_bodyTypeIds[BodyTypeRow(type)], localWidth);
                Write(body, 0, localWidth); // function-root region
                body.Add(_places[place].IsAddressableLocal
                    ? _function.Variables[_places[place].Variable].IsMutable ? (byte)7 : (byte)5
                    : (byte)4);
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
            return [.. body];

            int[] startingPlacesForEnd()
            {
                return [.. Enumerable.Range(1, _places.Count - 1)];
            }
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

        private int BodyTypeRow(PrimitiveType type)
        {
            if (type == PrimitiveType.NativeRawPointer)
            {
                return _nativeRawPointerTypeRow != 0
                    ? _nativeRawPointerTypeRow
                    : throw Unsupported("A raw-pointer Body value has no opaque pointer TypeSpec row.");
            }

            if (NativePointerTypes.IsType(type))
            {
                return NativePointerTypeRow(type, _nativeRawPointerTypeRow,
                    _nativeFunctionPointerTypeRows, _typeRows, _nativePointerTypeRows,
                    _program.NativePointerTypes, _structureTypeDefRows,
                    _typeDefTypeSpecRows);
            }

            return TypeRow(type);
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

    private readonly record struct OutputPlace(
        int Variable, bool IsParameter, bool IsAddressableLocal);

    private sealed record OutputBlock(byte[] Operations, int OperationCount, int ReturnValue,
        int FailureReason, int Condition, Dictionary<int, int> ValueLastUses,
        Dictionary<int, int> PlaceLastUses, bool[]? InheritedPlaces);
}
