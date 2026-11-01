// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    internal static BoundProgram LoadStandardScalarReturn(ReadOnlySpan<byte> artifact)
        => LoadScalarReturn(artifact, TrRmArtifactSchema.Standard);

    internal static BoundProgram LoadScalarFrozenValuesDraft(ReadOnlySpan<byte> artifact)
        => LoadScalarReturn(artifact, TrRmArtifactSchema.ScalarFrozenValuesDraft);

    internal static BoundProgram LoadNominalEnumsDraftScalarReturns(
        ReadOnlySpan<byte> artifact)
        => LoadScalarReturn(artifact, TrRmArtifactSchema.NominalEnumsDraft);

    internal static BoundProgram LoadNominalEnumsDraftIdentityLibrary(
        ReadOnlySpan<byte> artifact)
        => LoadScalarReturn(artifact, TrRmArtifactSchema.NominalEnumsDraft,
            allowNominalEnumsDraftIdentity: true);

    private static BoundProgram LoadScalarReturn(ReadOnlySpan<byte> artifact,
        TrRmArtifactSchema schema,
        bool allowNominalEnumsDraftIdentity = false)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = schema switch
        {
            TrRmArtifactSchema.Standard =>
                TrRmStructuralMetadataValidator
                    .ValidateAndGetExecutionTables(index),
            TrRmArtifactSchema.ScalarFrozenValuesDraft =>
                TrRmStructuralMetadataValidator
                    .ValidateScalarFrozenValuesDraftAndGetExecutionTables(index),
            TrRmArtifactSchema.NominalEnumsDraft =>
                TrRmStructuralMetadataValidator
                    .ValidateNominalEnumsDraftAndGetExecutionTables(index),
            _ => throw Unsupported(
                "The selected scalar-return loader requires an explicit supported schema."),
        };
        var nominalEnumsDraft
            = schema == TrRmArtifactSchema.NominalEnumsDraft;
        _ = index.TryGetSection(0x0004, out _, out var featureCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            var supported = nominalEnumsDraft
                ? kind is 0x0001 or 0x0002 or 0x0003 or 0x0004 or 0x0010
                    or 0x0011 or 0x0013 or 0x0015 or 0x0016 or 0x0017
                    or 0x0018 or 0x001C or 0x001D or 0x0020 or 0x0030 or 0x003F
                    or 0x0048 or 0x0053 or 0x0054
                : kind is 0x0001 or 0x0002 or 0x0003 or 0x0004 or 0x0011
                    or 0x0016 or 0x0017 or 0x001D or 0x0020 or 0x0030
                    or 0x003F;
            if (!supported)
            {
                throw Unsupported($"Section 0x{kind:X4} is outside the standard scalar-return profile.");
            }
        }

        var methodCount = tables.Methods.Count;
        var enumDefinitions = nominalEnumsDraft
            ? LoadNominalEnumsDraftDefinitions(tables)
            : [];
        var featureRange = tables.Modules.GetFeatureRange(1);
        var hasRequiredFeatures = nominalEnumsDraft
            ? (allowNominalEnumsDraftIdentity
                ? HasNominalEnumsDraftIdentityFeatureSet(tables.Features,
                    featureCount)
                : HasNominalEnumsDraftFeatureSet(tables.Features, featureCount)
                    && TrRmFeatureManifestValidator.Requires(tables.Features,
                        tables.Modules, 1,
                        TrRmFeatureTableReader.TypedFrozenValuesFeatureId,
                        schema))
                && (tables.EnumDefs.Count == 0
                    || TrRmFeatureManifestValidator.Requires(tables.Features,
                        tables.Modules, 1, 18, schema))
            : featureCount <= 1
                && (featureCount == 0
                    || tables.Features.GetFeature(1)
                        == TrRmFeatureRow.TypedFrozenValues);
        if (tables.Modules.Count != 1 || methodCount == 0
            || methodCount > SemanticVerifier.MaximumFunctions
            || tables.Signatures.Count != methodCount || tables.Bodies.Count != methodCount
            || tables.Contracts.Count != 0
            || (!nominalEnumsDraft
                && (tables.Imports.Count != 0 || tables.TypeDefs.Count != 0))
            || (nominalEnumsDraft
                && (tables.TypeDefs.Count != tables.EnumDefs.Count
                    || tables.TypeSpecs.Count != tables.EnumDefs.Count))
            || tables.Modules.GetRequirements(1) != (1u, 0u)
            || tables.Modules.GetGoverningRuleEdition(1) != 1
            || !hasRequiredFeatures
            || (nominalEnumsDraft
                ? featureRange != (1, featureCount)
                : featureRange.Count != featureCount))
        {
            throw Unsupported("Scalar-return execution requires one Windows x64 module and local scalar callables.");
        }
        if (nominalEnumsDraft)
        {
            ValidateNominalEnumsDraftScalarReturnReferences(
                tables, index, methodCount);
        }

        if (!index.TryGetSection(0x0001, out var moduleBytes, out _))
        {
            throw Unsupported("Scalar-return execution requires a Module row.");
        }
        var moduleEntryPoint = BinaryPrimitives.ReadUInt32LittleEndian(
            moduleBytes.Slice(16, sizeof(uint)));
        if (moduleEntryPoint > (uint)methodCount)
        {
            throw Unsupported("Scalar-return execution requires an absent or local entry point.");
        }

        var functions = ImmutableArray.CreateBuilder<BoundFunction>(methodCount);
        var usedBodies = new bool[methodCount];
        var hasConstantBody = false;
        var hasIdentityBody = false;
        for (var methodRow = 1; methodRow <= methodCount; methodRow++)
        {
            if (tables.Methods.GetOwnerType(methodRow) != 0
                || tables.Methods.GetFlags(methodRow) > 2)
            {
                throw Unsupported("Scalar-return execution requires top-level methods with assigned visibility.");
            }

            var signature = tables.Methods.GetSignature(methodRow);
            var (parameterStart, parameterCount)
                = tables.Signatures.GetParamRange(signature);
            if (tables.Signatures.GetReceiverTypeSpec(signature) != 0
                || parameterCount > (allowNominalEnumsDraftIdentity ? 1 : 0)
                || tables.Signatures.GetGenericCount(signature) != 0)
            {
                throw Unsupported(allowNominalEnumsDraftIdentity
                    ? "NominalEnumsDraft identity execution requires top-level non-generic methods with at most one value parameter."
                    : "Scalar-return execution requires top-level parameterless non-generic methods.");
            }

            var resultTypeInfo = tables.TypeSpecs.GetTypeSpecRef(
                tables.Signatures.GetResultTypeSpec(signature));
            var resultType = ResolveScalarReturnType(
                tables.Signatures.GetResultTypeSpec(signature),
                resultTypeInfo, tables, nominalEnumsDraft);
            if (resultType == PrimitiveType.Void
                || (moduleEntryPoint == (uint)methodRow
                    && resultType != PrimitiveType.Int32))
            {
                throw Unsupported(
                    "Scalar-return execution requires non-unit scalar results and an int32 entry point.");
            }

            var identityMethod = parameterCount == 1;
            uint parameterTypeSpec = 0;
            if (identityMethod)
            {
                parameterTypeSpec = tables.Parameters.GetTypeSpec(parameterStart);
                var parameterTypeInfo = tables.TypeSpecs.GetTypeSpecRef(
                    parameterTypeSpec);
                var parameterType = ResolveScalarReturnType(
                    parameterTypeSpec, parameterTypeInfo, tables,
                    nominalEnumsDraft);
                if (!allowNominalEnumsDraftIdentity
                    || !BoundEnumTypes.IsValidType(parameterType,
                        enumDefinitions)
                    || parameterType != resultType
                    || tables.Parameters.GetMode(parameterStart) != 0
                    || tables.Parameters.GetFlags(parameterStart) != 0
                    || !tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                        parameterTypeSpec,
                        tables.Signatures.GetResultTypeSpec(signature)))
                {
                    throw Unsupported(
                        "NominalEnumsDraft identity methods require one value parameter identical to the nominal enum result type.");
                }
            }

            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow <= 0 || bodyRow > methodCount || usedBodies[bodyRow - 1])
            {
                throw Unsupported("Scalar-return execution does not admit shared MethodDef Body rows.");
            }
            usedBodies[bodyRow - 1] = true;
            tables.Bodies.ValidateSelectedImplementation(bodyRow, tables.Contracts.Count);
            tables.ValidateRegisterBodyMapsForMethod(methodRow);
            var body = tables.Bodies.GetRegisterBody(bodyRow, tables.Contracts.Count);
            if ((identityMethod
                    ? body.MapMask != 0x03
                    : body.MapMask is not (0 or 0x10))
                || body.BlockCount != 1 || body.RecordCount != 1)
            {
                throw Unsupported(identityMethod
                    ? "NominalEnumsDraft identity execution requires one bounded Block, HomeMap and TypeMap."
                    : "Scalar-return execution requires one bounded Block, result record and optional ConstantMap.");
            }

            var entryState = body.GetRecord(0);
            if (entryState.Length != 8 || entryState[0] != 0x01
                || BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
            {
                throw Unsupported("Scalar-return execution requires an empty BlockEntryState.");
            }

            var code = body.GetBlockCode(0);
            BoundExpression result;
            var variables = ImmutableArray<BoundVariable>.Empty;
            if (identityMethod)
            {
                var homeMap = body.HomeMap;
                var typeMap = body.TypeMap;
                if (body.ConstantCount != 0 || homeMap.Length != 8
                    || homeMap[0] != 0x02 || homeMap[1] != 0
                    || BinaryPrimitives.ReadUInt16LittleEndian(homeMap[2..]) != 0
                    || BinaryPrimitives.ReadUInt16LittleEndian(homeMap[4..]) != 0
                    || BinaryPrimitives.ReadUInt16LittleEndian(homeMap[6..]) != 0
                    || typeMap.Length != sizeof(uint))
                {
                    throw Unsupported(
                        "NominalEnumsDraft identity execution requires one parameter home and TypeMap row.");
                }

                var mappedType = BinaryPrimitives.ReadUInt32LittleEndian(typeMap);
                if (mappedType >> 24 != 0x11
                    || !tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                        mappedType & 0x00FF_FFFF, parameterTypeSpec)
                    || code.Length != 4 || code[0] != 0x13 || code[1] != 0
                    || code[2] != 0x17 || code[3] != 0)
                {
                    throw Unsupported(
                        "NominalEnumsDraft identity execution requires the exact home load and result sequence.");
                }

                hasIdentityBody = true;
                result = new BoundRead(0, resultType, default);
                variables = [new BoundVariable("value", resultType, false, default)];
            }
            else if (body.MapMask == 0x10)
            {
                hasConstantBody = true;
                if (body.ConstantCount != 1 || code.Length != 2
                    || code[0] != 0x09 || code[1] != 0)
                {
                    throw Unsupported("Scalar-return execution supports one exact ConstantMap load.");
                }

                var typeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(body.ConstantMap);
                var constantSourceRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    body.ConstantMap[sizeof(uint)..]));
                result = TrRmConstantSourceValueValidator.MaterializeFrozenScalar(
                    constantSourceRow, typeSpecRef, 1,
                    tables.Modules.GetRequirements(1).TargetRequirement,
                    tables.ConstantSources, tables.FrozenValueNodes, tables.Blobs,
                    tables.TypeSpecs, tables.Features, tables.Modules,
                    tables.TypeDefs, tables.EnumDefs, tables.Imports,
                    tables.A3LayoutResourceLedger);
                if (result.Type != resultType)
                {
                    throw Unsupported("ConstantMap result does not match the selected MethodSig type.");
                }
            }
            else
            {
                if (body.ConstantCount != 0 || code.Length != 2
                    || code[0] != 0x17 || code[1] is not (0x0E or 0x0F))
                {
                    throw Unsupported("Scalar-return execution supports only Return R14 or Return R15 without a ConstantMap.");
                }

                result = CreateSpecialRegisterConstant(resultType, code[1]);
            }

            var flags = tables.Methods.GetFlags(methodRow);
            functions.Add(new BoundFunction(tables.Methods.GetName(methodRow),
                resultType, variables, identityMethod ? 1 : 0,
                new BoundBlock([new BoundReturn(result, default)], default), default)
            {
                IsPrivate = flags == 0,
                IsInternal = flags == 1,
            });
        }

        if (Array.Exists(usedBodies, static used => !used))
        {
            throw Unsupported("Scalar-return execution does not admit unowned Body rows.");
        }
        if (nominalEnumsDraft
            ? (!hasConstantBody && !hasIdentityBody)
                || (hasConstantBody
                    && !TrRmFeatureManifestValidator.Requires(tables.Features,
                        tables.Modules, 1,
                        TrRmFeatureTableReader.TypedFrozenValuesFeatureId,
                        schema))
                || (tables.EnumDefs.Count != 0
                    && !TrRmFeatureManifestValidator.Requires(tables.Features,
                        tables.Modules, 1, 18, schema))
            : hasConstantBody
            ? featureCount != 1 || featureRange != (1, 1)
            : featureCount != 0 || featureRange != (0, 0))
        {
            throw Unsupported("Scalar-return execution requires the exact Feature manifest for its selected Bodies.");
        }

        var entryPoint = moduleEntryPoint == 0
            ? -1 : checked((int)moduleEntryPoint - 1);
        var program = new BoundProgram(functions.MoveToImmutable(), entryPoint)
        {
            EnumDefinitions = enumDefinitions,
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };
        if (moduleEntryPoint == 0)
        {
            SemanticVerifier.VerifyLibrary(program);
        }
        else
        {
            SemanticVerifier.Verify(program);
        }
        return program;
    }

    internal static BoundProgram LoadStandardFloatingScalarFunction(ReadOnlySpan<byte> artifact)
        => LoadStandardScalarOperationFunction(artifact, floating: true);

    internal static BoundProgram LoadStandardIntegerScalarFunction(ReadOnlySpan<byte> artifact)
        => LoadStandardScalarOperationFunction(artifact, floating: false);

    private static BoundProgram LoadStandardScalarOperationFunction(
        ReadOnlySpan<byte> artifact, bool floating)
    {
        var index = new TrRmArtifactIndex(artifact, TrRmArtifactSchema.Standard);
        var tables = TrRmStructuralMetadataValidator.ValidateAndGetExecutionTables(index);
        if (!index.TryGetSection(0x0001, out var moduleBytes, out _)
            || BinaryPrimitives.ReadUInt32LittleEndian(moduleBytes.Slice(16, sizeof(uint))) != 0)
        {
            throw Unsupported("Standard scalar-operation execution requires a library module without an entry point.");
        }
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind is not (0x0001 or 0x0002 or 0x0011 or 0x0016
                or 0x0017 or 0x0018 or 0x001D or 0x0020))
            {
                throw Unsupported($"Section 0x{kind:X4} is outside the standard scalar-operation profile.");
            }
        }

        if (tables.Modules.Count != 1 || tables.Methods.Count != 1
            || tables.Signatures.Count != 1 || tables.Signatures.ParamCount != 2
            || tables.Bodies.Count != 1 || tables.Contracts.Count != 0
            || tables.Imports.Count != 0 || tables.TypeDefs.Count != 0
            || tables.Modules.GetRequirements(1) != (1u, 0u)
            || tables.Modules.GetGoverningRuleEdition(1) != 1
            || tables.Modules.GetFeatureRange(1).Count != 0
            || tables.Methods.GetOwnerType(1) != 0
            || tables.Methods.GetBodyRow(1) != 1)
        {
            throw Unsupported("Standard scalar-operation execution requires one feature-free Windows x64 library method.");
        }
        var methodFlags = tables.Methods.GetFlags(1);
        if (methodFlags > 2)
        {
            throw Unsupported("Standard scalar-operation execution does not admit additional MethodDef flags.");
        }

        var signature = tables.Methods.GetSignature(1);
        var (parameterStart, parameterCount) = tables.Signatures.GetParamRange(signature);
        if (tables.Signatures.GetReceiverTypeSpec(signature) != 0
            || parameterCount != 2
            || tables.Signatures.GetGenericCount(signature) != 0)
        {
            throw Unsupported("Standard scalar-operation execution requires a top-level non-generic two-parameter method.");
        }

        var resultType = StandardScalarType(
            tables.TypeSpecs, tables.Signatures.GetResultTypeSpec(signature));
        var parameterTypes = new PrimitiveType[parameterCount];
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var parameterRow = parameterStart + ordinal;
            if (tables.Parameters.GetMode(parameterRow) != 0)
            {
                throw Unsupported("Standard scalar-operation execution requires value parameters.");
            }
            parameterTypes[ordinal] = StandardScalarType(
                tables.TypeSpecs, tables.Parameters.GetTypeSpec(parameterRow));
        }

        var supportedType = floating
            ? IsFloat(resultType) : PrimitiveTypeFacts.IsIntegerOrWide(resultType);
        if (!supportedType || parameterTypes[0] != resultType
            || parameterTypes[1] != resultType)
        {
            throw Invalid("Scalar-operation signature types do not match the selected opcode family.");
        }

        var bodyRow = tables.Methods.GetBodyRow(1);
        tables.Bodies.ValidateSelectedImplementation(bodyRow, tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(1);
        var body = tables.Bodies.GetRegisterBody(bodyRow, tables.Contracts.Count);
        if (body.MapMask != 0 || body.HomeCount != 0 || body.TypeCount != 0
            || body.MethodCount != 0 || body.FieldCount != 0 || body.ConstantCount != 0
            || body.BlockCount != 1 || body.RecordCount != 1)
        {
            throw Unsupported("Standard scalar-operation execution requires one map-free block and entry-state record.");
        }

        var entryState = body.GetRecord(0);
        if (entryState[0] != 0x01 || entryState.Length != 8
            || BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
        {
            throw Unsupported("Standard scalar-operation execution requires an empty BlockEntryState.");
        }

        var variables = ImmutableArray.CreateBuilder<BoundVariable>();
        variables.Add(new BoundVariable("left", parameterTypes[0], false, default));
        variables.Add(new BoundVariable("right", parameterTypes[1], false, default));
        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        var registerVariables = new int[14];
        Array.Fill(registerVariables, -1);
        var code = body.GetBlockCode(0);
        BoundReturn? result = null;
        var position = 0;
        while (position < code.Length)
        {
            var opcode = code[position];
            var operands = code[position + 1];
            var destination = operands & 0x0F;
            var source = operands >> 4;
            switch (opcode)
            {
                case 0x11:
                    if (destination >= registerVariables.Length
                        || source >= parameterTypes.Length)
                    {
                        throw Invalid("Formal load references an unavailable register or formal.");
                    }
                    var parameter = variables[source];
                    var formalRead = new BoundRead(source, parameter.Type, default);
                    var formalVariable = variables.Count;
                    variables.Add(new BoundVariable(
                        $"r{destination}_{formalVariable}", parameter.Type, false, default));
                    statements.Add(new BoundLocal(formalVariable, formalRead, default));
                    registerVariables[destination] = formalVariable;
                    break;
                case 0x20:
                    if (floating || !PrimitiveTypeFacts.IsIntegerOrWide(resultType)
                        || destination >= registerVariables.Length)
                    {
                        throw Invalid("Integer negation requires an integer operation profile and writable register.");
                    }
                    var integerOperand = GetStandardScalarRegisterValue(
                        source, resultType, variables, registerVariables);
                    var integerNegationVariable = variables.Count;
                    variables.Add(new BoundVariable(
                        $"r{destination}_{integerNegationVariable}", resultType, false, default));
                    statements.Add(new BoundLocal(integerNegationVariable,
                        new BoundUnary(TokenKind.Minus, integerOperand, resultType, default)
                        {
                            OverflowBehavior = BoundIntegerOverflowBehavior.Wrapping,
                        }, default));
                    registerVariables[destination] = integerNegationVariable;
                    break;
                case >= 0x21 and <= 0x25:
                    if (floating || !PrimitiveTypeFacts.IsIntegerOrWide(resultType)
                        || destination >= registerVariables.Length)
                    {
                        throw Invalid("Integer arithmetic requires an integer operation profile and writable register.");
                    }
                    var integerLeftVariable = registerVariables[destination];
                    if (integerLeftVariable < 0
                        || variables[integerLeftVariable].Type != resultType)
                    {
                        throw Invalid("Integer arithmetic left operand is not an initialized register of the selected type.");
                    }
                    var integerRight = GetStandardScalarRegisterValue(
                        source, resultType, variables, registerVariables);
                    var integerOperation = opcode switch
                    {
                        0x21 => TokenKind.Plus,
                        0x22 => TokenKind.Minus,
                        0x23 => TokenKind.Star,
                        0x24 => TokenKind.Slash,
                        _ => TokenKind.Percent,
                    };
                    var integerResultVariable = variables.Count;
                    variables.Add(new BoundVariable(
                        $"r{destination}_{integerResultVariable}", resultType, false, default));
                    statements.Add(new BoundLocal(integerResultVariable,
                        new BoundBinary(integerOperation,
                            new BoundRead(integerLeftVariable, resultType, default),
                            integerRight, resultType, default)
                        {
                            OverflowBehavior = opcode is >= 0x21 and <= 0x23
                                ? BoundIntegerOverflowBehavior.Wrapping
                                : BoundIntegerOverflowBehavior.Checked,
                        }, default));
                    registerVariables[destination] = integerResultVariable;
                    break;
                case 0x26:
                    if (!floating || destination >= registerVariables.Length)
                    {
                        throw Invalid("Floating negation destination is not a writable register.");
                    }
                    var floatingOperand = GetStandardScalarRegisterValue(
                        source, resultType, variables, registerVariables);
                    if (!IsFloat(floatingOperand.Type))
                    {
                        throw Invalid("Floating negation operand is not a float.");
                    }
                    var negationVariable = variables.Count;
                    variables.Add(new BoundVariable(
                        $"r{destination}_{negationVariable}", resultType, false, default));
                    statements.Add(new BoundLocal(negationVariable,
                        new BoundUnary(TokenKind.Minus, floatingOperand, resultType, default), default));
                    registerVariables[destination] = negationVariable;
                    break;
                case >= 0x27 and <= 0x2B:
                    if (!floating || destination >= registerVariables.Length)
                    {
                        throw Invalid("Floating arithmetic destination is not a writable register.");
                    }
                    var leftVariable = registerVariables[destination];
                    if (leftVariable < 0 || !IsFloat(variables[leftVariable].Type))
                    {
                        throw Invalid("Floating arithmetic left operand is not an initialized float register.");
                    }
                    var valueType = variables[leftVariable].Type;
                    var rightValue = GetStandardScalarRegisterValue(
                        source, valueType, variables, registerVariables);
                    var remainderVariable = variables.Count;
                    var operation = opcode switch
                    {
                        0x27 => TokenKind.Plus,
                        0x28 => TokenKind.Minus,
                        0x29 => TokenKind.Star,
                        0x2A => TokenKind.Slash,
                        _ => TokenKind.Percent,
                    };
                    var value = new BoundBinary(operation,
                        new BoundRead(leftVariable, valueType, default),
                        rightValue,
                        valueType, default);
                    variables.Add(new BoundVariable(
                        $"r{destination}_{remainderVariable}", valueType, false, default));
                    statements.Add(new BoundLocal(remainderVariable, value, default));
                    registerVariables[destination] = remainderVariable;
                    break;
                case 0x17:
                    if (source != 0)
                    {
                        throw Invalid("Return register selector has a nonzero reserved operand.");
                    }
                    if (destination < registerVariables.Length)
                    {
                        var returnVariable = registerVariables[destination];
                        if (returnVariable < 0 || variables[returnVariable].Type != resultType)
                        {
                            throw Invalid("Return register does not match the selected MethodSig.");
                        }
                        result = new BoundReturn(new BoundRead(
                            returnVariable, resultType, default), default);
                    }
                    else
                    {
                        result = new BoundReturn(
                            CreateSpecialRegisterConstant(resultType, (byte)destination), default);
                    }
                    break;
                default:
                    throw Unsupported($"Register opcode 0x{opcode:X2} is outside the scalar floating-operation execution slice.");
            }

            position += TrRmRegisterBodyReader.InstructionLength(opcode, body.Schema);
            if (result is not null && position != code.Length)
            {
                throw Invalid("A terminal register instruction is followed by code.");
            }
        }
        if (result is null)
        {
            throw Invalid("Standard scalar Body has no return.");
        }
        statements.Add(result);

        var function = new BoundFunction(tables.Methods.GetName(1),
            resultType, variables.ToImmutable(), parameterCount,
            new BoundBlock(statements.ToImmutable(), default), default)
        {
            IsPrivate = methodFlags == 0,
            IsInternal = methodFlags == 1,
        };
        var program = new BoundProgram([function], -1)
        {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        };
        SemanticVerifier.VerifyLibrary(program);
        return program;
    }

    private static PrimitiveType StandardScalarType(
        TrRmTypeSpecTableReader typeSpecs, uint typeSpecRef)
    {
        var reference = typeSpecs.GetTypeSpecRef(typeSpecRef);
        return reference.Form == 0x01 && reference.Qualifiers == 0
            && TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                reference.TypeCode, out var type)
                ? type : throw Unsupported("Standard scalar method has an unsupported TypeSpec.");
    }

    private static BoundExpression GetStandardScalarRegisterValue(int register,
        PrimitiveType type, ImmutableArray<BoundVariable>.Builder variables,
        int[] registerVariables)
    {
        if (register < registerVariables.Length)
        {
            var variable = registerVariables[register];
            if (variable < 0 || variables[variable].Type != type)
            {
                throw Invalid("Scalar operation source register is uninitialized or has the wrong type.");
            }

            return new BoundRead(variable, type, default);
        }

        return CreateSpecialRegisterConstant(type, checked((byte)register));
    }

    private static BoundConstant CreateSpecialRegisterConstant(PrimitiveType type,
        byte register)
    {
        if (type is PrimitiveType.Int128 or PrimitiveType.UInt128)
        {
            return register switch
            {
                0x0E => new BoundConstant(default(ScalarBits), type, default),
                0x0F => new BoundConstant(
                    new ScalarBits(ulong.MaxValue, ulong.MaxValue), type, default),
                _ => throw Unsupported($"R{register} is not a valid scalar source register."),
            };
        }
        if (register == 0x0E)
        {
            return new BoundConstant(0, type, default);
        }

        long value = type switch
        {
            PrimitiveType.Bool => 1,
            PrimitiveType.Char8 or PrimitiveType.UInt8 => byte.MaxValue,
            PrimitiveType.Char16 or PrimitiveType.UInt16 => ushort.MaxValue,
            PrimitiveType.Char32 => uint.MaxValue,
            PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
                or PrimitiveType.Int64 or PrimitiveType.UInt32 or PrimitiveType.UInt64
                or PrimitiveType.Int128 or PrimitiveType.UInt128 or PrimitiveType.Float64 => -1,
            PrimitiveType.Float32 => uint.MaxValue,
            _ => throw Unsupported($"R15 is not a valid result constant for {type}."),
        };
        return new BoundConstant(value, type, default);
    }
}
