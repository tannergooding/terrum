using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private readonly record struct StandardOutputStateFact(
        uint FormalOrdinal, byte PhaseTag, uint PhaseLocalOrdinal,
        byte NodeKind, byte State);

    private readonly record struct StandardOutputStateMetadata(
        ImmutableArray<NativeFunctionPointerSelector> Selectors,
        ImmutableArray<StandardOutputStateFact> Facts,
        ImmutableArray<BoundOutputStatePredicate> GeneralPredicates);

    private readonly record struct StandardInitializationOutputStateVectors(
        ImmutableArray<NativeFunctionPointerSelector> Selectors,
        ImmutableArray<NativeFunctionPointerOutputState> OutputStates,
        ImmutableArray<BoundOutputStatePredicate> GeneralPredicates);

    private readonly record struct StandardInitializationReferenceStore(
        int DescriptorVariable, int SourceVariable, byte Transition);

    private static bool TryGetGeneralPredicateUnaryOperation(
        BoundUnary unary, out ushort operationCode, out byte policy)
    {
        operationCode = unary.Operator switch {
            TokenKind.Plus => 0x0001,
            TokenKind.Minus => 0x0002,
            TokenKind.Tilde => 0x0003,
            _ => 0,
        };
        policy = 0;
        if (operationCode == 0 || unary.Type != unary.Operand.Type
            || !PrimitiveTypeFacts.IsBuiltInScalar(unary.Type))
        {
            return false;
        }

        var integer = PrimitiveTypeFacts.IsIntegerOrWide(unary.Type);
        var floating = PrimitiveTypeFacts.IsFloating(unary.Type);
        var validType = unary.Operator switch {
            TokenKind.Plus or TokenKind.Minus => integer || floating,
            TokenKind.Tilde => integer,
            _ => false,
        };
        if (!validType
            || unary.OverflowBehavior
                is not (BoundIntegerOverflowBehavior.Checked
                    or BoundIntegerOverflowBehavior.Wrapping)
            || (unary.OverflowBehavior == BoundIntegerOverflowBehavior.Wrapping
                && !(unary.Operator == TokenKind.Minus && integer)))
        {
            return false;
        }

        if (unary.Operator == TokenKind.Minus && integer)
        {
            policy = unary.OverflowBehavior
                == BoundIntegerOverflowBehavior.Wrapping ? (byte)2 : (byte)1;
        }

        return true;
    }

    private static bool TryGetGeneralPredicateBinaryOperation(
        BoundBinary binary, out ushort operationCode, out byte policy)
    {
        operationCode = binary.Operator switch {
            TokenKind.Plus => 0x0004,
            TokenKind.Minus => 0x0005,
            TokenKind.Star => 0x0006,
            TokenKind.Slash => 0x0007,
            TokenKind.Percent => 0x0008,
            TokenKind.Ampersand => 0x0009,
            TokenKind.Pipe => 0x000A,
            TokenKind.Caret => 0x000B,
            TokenKind.ShiftLeft => 0x000C,
            TokenKind.ShiftRight => 0x000D,
            TokenKind.ShiftRightUnsigned => 0x000E,
            TokenKind.EqualsEquals => 0x000F,
            TokenKind.BangEquals => 0x0010,
            TokenKind.Less => 0x0011,
            TokenKind.LessEquals => 0x0012,
            TokenKind.Greater => 0x0013,
            TokenKind.GreaterEquals => 0x0014,
            _ => 0,
        };
        policy = 0;
        if (operationCode == 0
            || binary.OverflowBehavior
                is not (BoundIntegerOverflowBehavior.Checked
                    or BoundIntegerOverflowBehavior.Wrapping))
        {
            return false;
        }

        var leftType = binary.Left.Type;
        var rightType = binary.Right.Type;
        var integer = PrimitiveTypeFacts.IsIntegerOrWide(leftType);
        var floating = PrimitiveTypeFacts.IsFloating(leftType);
        var sameOperands = leftType == rightType;
        switch (operationCode)
        {
            case >= 0x0004 and <= 0x0006:
                if (!sameOperands || binary.Type != leftType
                    || !(integer || floating))
                {
                    return false;
                }

                if (integer)
                {
                    policy = binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Wrapping
                            ? (byte)2 : (byte)1;
                    return true;
                }

                return binary.OverflowBehavior
                    == BoundIntegerOverflowBehavior.Checked;
            case 0x0007 or 0x0008:
                return sameOperands && binary.Type == leftType
                    && (integer || floating)
                    && binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked;
            case 0x0009 or 0x000A or 0x000B:
                return sameOperands && binary.Type == leftType && integer
                    && binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked;
            case 0x000C or 0x000D or 0x000E:
                return integer
                    && PrimitiveTypeFacts.IsIntegerOrWide(rightType)
                    && binary.Type == leftType
                    && binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked;
            case 0x000F or 0x0010:
                return sameOperands && binary.Type == PrimitiveType.Bool
                    && PrimitiveTypeFacts.IsBuiltInScalar(leftType)
                    && binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked;
            case >= 0x0011 and <= 0x0014:
                return sameOperands && binary.Type == PrimitiveType.Bool
                    && (integer
                        || PrimitiveTypeFacts.IsCodeUnitOrRune(leftType)
                        || floating)
                    && binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked;
            default:
                return false;
        }
    }

    private static bool TryGetGeneralPredicateConversion(BoundCast cast,
        out ushort operationCode, out byte policy)
    {
        operationCode = 0x0015;
        policy = 0;
        return PrimitiveTypeFacts.IsBuiltInScalar(cast.Operand.Type)
            && PrimitiveTypeFacts.IsBuiltInScalar(cast.Type)
            && SemanticVerifier.IsTotalConversion(cast.Operand.Type,
                cast.Type);
    }

    private static bool IsForeignInterfaceCheckedNumericConversion(
        BoundCast cast)
        => SemanticVerifier.IsSupportedCheckedNumericConversion(
            cast.Operand.Type, cast.Type);

    private static bool IsStandardInitializationReferenceParameter(
        BoundVariable variable, BoundProgram program)
        => IsStandardInitializationReferenceParameter(variable,
            program.CheckedReferenceTypes);

    private static bool IsStandardInitializationReferenceParameter(
        BoundVariable variable,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes)
        => variable.IsInit && !variable.IsOut
            && TryGetStandardInitializationReferenceReferentType(
                variable.Type, checkedReferenceTypes, out _);

    private static bool TryGetStandardInitializationReferenceReferentType(
        PrimitiveType type,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes,
        out PrimitiveType referentType)
    {
        referentType = PrimitiveType.Void;
        if (!CheckedReferenceTypes.IsType(type))
        {
            return false;
        }

        var index = CheckedReferenceTypes.Index(type);
        if ((uint)index >= (uint)checkedReferenceTypes.Count)
        {
            return false;
        }

        var definition = checkedReferenceTypes[index];
        if (!definition.IsInitialization || definition.IsOptional
            || definition.IsGarbageCollected)
        {
            return false;
        }

        referentType = definition.ReferentType;
        return IsOutputScalarType(referentType)
            || NativePointerTypes.IsType(referentType);
    }

    private static (
        ImmutableArray<NativeFunctionPointerSelector> Selectors,
        ImmutableArray<NativeFunctionPointerOutputState> OutputStates)
        GetStandardInitializationOutputStateVectors(
            BoundFunction function, BoundProgram program)
    {
        var selectors = function.CheckedOutputSelectors.IsDefault
            ? [] : function.CheckedOutputSelectors;
        var outputStates = function.CheckedOutputStates.IsDefault
            ? [] : function.CheckedOutputStates;
        if (function.DirectNativeImport is not null
            || function.NativeVTableMethod is not null)
        {
            if (!selectors.IsEmpty || !outputStates.IsEmpty)
            {
                throw Unsupported(
                    "Standard initialization Bodies do not combine init-reference OutputState with NativeOutput contracts.");
            }

            return ([], []);
        }

        var outputFormals = ImmutableArray.CreateBuilder<uint>();
        for (var ordinal = 0; ordinal < function.ParameterCount; ordinal++)
        {
            var formal = function.Variables[ordinal];
            if (formal.IsOut || (formal.IsInit
                    && !IsStandardInitializationReferenceParameter(
                        formal, program)))
            {
                throw Unsupported(
                    "Standard initialization Bodies support only scalar ref<init T> output formals.");
            }
            if (IsStandardInitializationReferenceParameter(formal, program))
            {
                outputFormals.Add(checked((uint)ordinal));
            }
        }

        var explicitlyReplacedOutputFormals
            = function.HasStandardCheckedOutputStateMetadata
                && !function.DeclarationContractClauses.IsDefaultOrEmpty
            ? CheckedOutputStateProjection.GetExplicitOutputFormals(function,
                program.CheckedReferenceTypes)
            : new HashSet<int>();
        if (function.HasStandardCheckedOutputStateMetadata
            && function.DeclarationContractClauses.IsDefaultOrEmpty)
        {
            explicitlyReplacedOutputFormals.UnionWith(outputFormals
                .Select(static formal => checked((int)formal)));
        }
        var defaultOutputFormals
            = ImmutableArray.CreateBuilder<uint>(
                function.ReturnType == PrimitiveType.Bool
                    ? outputFormals.Count : 0);
        foreach (var formal in outputFormals)
        {
            if (function.ReturnType == PrimitiveType.Bool
                && !explicitlyReplacedOutputFormals.Contains(
                    checked((int)formal)))
            {
                defaultOutputFormals.Add(formal);
            }
        }
        if (!selectors.IsEmpty || !outputStates.IsEmpty)
        {
            var normalizedSelectors = selectors.OrderBy(
                static selector => selector.PhaseLocalOrdinal)
                .ToImmutableArray();
            var normalizedStates = outputStates.OrderBy(
                static state => state.FormalOrdinal)
                .ThenBy(static state => state.SelectorOrdinal)
                .ToImmutableArray();
            if (outputFormals.Count == 0
                || !IsValidBoundedOutputStateVectors(function.ReturnType,
                    outputFormals, normalizedSelectors, normalizedStates)
                || CreateDefaultOutputStates(defaultOutputFormals).Any(
                    expected => !normalizedStates.Contains(expected)))
            {
                throw Unsupported(
                    "The Standard initialization Body has unsupported bounded OutputState selector relations.");
            }

            return (normalizedSelectors, normalizedStates);
        }

        if (function.HasPrivateCheckedOutputStateMetadata
            || function.HasStandardCheckedOutputStateMetadata
            || function.IsExternal
            || function.ReturnType != PrimitiveType.Bool
            || outputFormals.Count == 0)
        {
            return ([], []);
        }

        var defaultSelectors = ImmutableArray.Create(
            new NativeFunctionPointerSelector(3, 0, 1),
            new NativeFunctionPointerSelector(3, 1, 2));
        return (defaultSelectors,
            CreateDefaultOutputStates(defaultOutputFormals));

        static ImmutableArray<NativeFunctionPointerOutputState>
            CreateDefaultOutputStates(
                ImmutableArray<uint>.Builder outputFormals)
        {
            var states = ImmutableArray.CreateBuilder<
                NativeFunctionPointerOutputState>(
                    checked(outputFormals.Count * 2));
            foreach (var formal in outputFormals)
            {
                states.Add(new NativeFunctionPointerOutputState(
                    formal, 0, 1));
                states.Add(new NativeFunctionPointerOutputState(
                    formal, 1, 0));
            }

            return states.MoveToImmutable();
        }
    }

    private static bool IsValidBoundedOutputStateVectors(
        PrimitiveType returnType,
        ImmutableArray<uint>.Builder outputFormals,
        ImmutableArray<NativeFunctionPointerSelector> selectors,
        ImmutableArray<NativeFunctionPointerOutputState> outputStates)
    {
        if (selectors.IsEmpty || selectors.Length > 6)
        {
            return false;
        }

        for (var index = 0; index < selectors.Length; index++)
        {
            var selector = selectors[index];
            if (selector.PhaseTag != 3
                || selector.PhaseLocalOrdinal != (uint)index
                || !SelectorSupportsResultType(selector.NodeKind,
                    returnType)
                || (index > 0
                    && selectors[index - 1].NodeKind
                        >= selector.NodeKind))
            {
                return false;
            }
        }

        var outputSet = outputFormals.ToHashSet();
        var usedSelectors = new bool[selectors.Length];
        var outputKeys = new HashSet<(uint Formal, uint Selector)>();
        foreach (var output in outputStates)
        {
            if (!outputSet.Contains(output.FormalOrdinal)
                || output.State is not (0 or 1)
                || output.SelectorOrdinal >= (uint)selectors.Length
                || !outputKeys.Add((output.FormalOrdinal,
                    output.SelectorOrdinal)))
            {
                return false;
            }

            usedSelectors[(int)output.SelectorOrdinal] = true;
        }

        return usedSelectors.All(static used => used);

        static bool SelectorSupportsResultType(byte nodeKind,
            PrimitiveType resultType)
            => nodeKind switch {
                1 or 2 => resultType == PrimitiveType.Bool,
                3 or 4 => resultType is PrimitiveType.Int32
                    or PrimitiveType.UInt32,
                5 or 6 => resultType == PrimitiveType.Int32,
                _ => false,
            };
    }

    private static StandardInitializationOutputStateVectors
        ReadStandardInitializationOutputStateVectors(
            int methodRow, BoundFunction function,
            IReadOnlyList<CheckedReferenceTypeDefinition>
                checkedReferenceTypes,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        if (tables.Schema.UsesGeneralPredicateGraphs())
        {
            return ReadGeneralInitializationOutputStateVectors(methodRow,
                function, checkedReferenceTypes, tables);
        }

        var metadata = GetStandardOutputStateMetadata(methodRow, tables);
        if (metadata.Selectors.IsEmpty)
        {
            return new([], [], []);
        }

        var outputFormals = ImmutableArray.CreateBuilder<uint>();
        for (var ordinal = 0; ordinal < function.ParameterCount; ordinal++)
        {
            var formal = function.Variables[ordinal];
            if (formal.IsOut || (formal.IsInit
                    && !IsStandardInitializationReferenceParameter(
                        formal, checkedReferenceTypes)))
            {
                throw Invalid(
                    "The Standard initialization-reference OutputState owner has an unsupported output formal.");
            }
            if (IsStandardInitializationReferenceParameter(
                    formal, checkedReferenceTypes))
            {
                outputFormals.Add(checked((uint)ordinal));
            }
        }

        var outputStates = metadata.Facts.Select(static fact =>
            new NativeFunctionPointerOutputState(fact.FormalOrdinal,
                fact.PhaseLocalOrdinal, fact.State)).ToImmutableArray();
        if (outputFormals.Count == 0
            || !IsValidBoundedOutputStateVectors(function.ReturnType,
                outputFormals, metadata.Selectors, outputStates))
        {
            throw Invalid(
                "The Standard initialization-reference OutputState relation is malformed or unsupported.");
        }

        if (function.HasStandardCheckedOutputStateMetadata
            && !function.DeclarationContractClauses.IsDefaultOrEmpty
            && function.ReturnType == PrimitiveType.Bool)
        {
            var explicitlyReplaced
                = CheckedOutputStateProjection.GetExplicitOutputFormals(
                    function, checkedReferenceTypes);
            foreach (var formal in outputFormals)
            {
                if (explicitlyReplaced.Contains(checked((int)formal)))
                {
                    continue;
                }

                if (!outputStates.Contains(
                        new NativeFunctionPointerOutputState(formal, 0, 1))
                    || !outputStates.Contains(
                        new NativeFunctionPointerOutputState(formal, 1, 0)))
                {
                    throw Invalid(
                        "The Standard initialization-reference OutputState relation dropped an unchanged Boolean default.");
                }
            }
        }

        return new(metadata.Selectors, outputStates, []);
    }

    private static StandardInitializationOutputStateVectors
        ReadGeneralInitializationOutputStateVectors(
            int methodRow, BoundFunction function,
            IReadOnlyList<CheckedReferenceTypeDefinition>
                checkedReferenceTypes,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        var metadata = GetGeneralOutputStateMetadata(methodRow, tables);
        var outputFormals = ImmutableArray.CreateBuilder<uint>();
        for (var ordinal = 0; ordinal < function.ParameterCount; ordinal++)
        {
            var formal = function.Variables[ordinal];
            if (formal.IsOut || (formal.IsInit
                    && !IsStandardInitializationReferenceParameter(
                        formal, checkedReferenceTypes)))
            {
                throw Invalid(
                    "The GeneralPredicateGraphsDraft OutputState owner has an unsupported output formal.");
            }
            if (IsStandardInitializationReferenceParameter(
                    formal, checkedReferenceTypes))
            {
                outputFormals.Add(checked((uint)ordinal));
            }
        }

        if (metadata.Facts.Any(fact =>
                !outputFormals.Contains(fact.FormalOrdinal))
            || metadata.GeneralPredicates.Any(predicate =>
                !outputFormals.Contains(
                    checked((uint)predicate.FormalOrdinal))))
        {
            throw Invalid(
                "The GeneralPredicateGraphsDraft OutputState does not match the loaded signature.");
        }
        var outputStates = metadata.Facts.Select(static fact =>
            new NativeFunctionPointerOutputState(
                fact.FormalOrdinal, fact.PhaseLocalOrdinal,
                fact.State)).ToImmutableArray();
        if (!metadata.Selectors.IsEmpty
            && !IsValidBoundedOutputStateVectors(function.ReturnType,
                outputFormals, metadata.Selectors, outputStates))
        {
            throw Invalid(
                "The GeneralPredicateGraphsDraft bounded OutputState selectors do not match the owner result.");
        }

        if (function.HasStandardCheckedOutputStateMetadata
            && !function.DeclarationContractClauses.IsDefaultOrEmpty
            && function.ReturnType == PrimitiveType.Bool)
        {
            var explicitlyReplaced
                = CheckedOutputStateProjection.GetExplicitOutputFormals(
                    function, checkedReferenceTypes);
            foreach (var formal in outputFormals)
            {
                if (explicitlyReplaced.Contains(checked((int)formal)))
                {
                    continue;
                }

                if (!outputStates.Contains(
                        new NativeFunctionPointerOutputState(formal, 0, 1))
                    || !outputStates.Contains(
                        new NativeFunctionPointerOutputState(formal, 1, 0)))
                {
                    throw Invalid(
                        "The GeneralPredicateGraphsDraft OutputState relation dropped an unchanged Boolean default.");
                }
            }
        }

        return new(metadata.Selectors, outputStates,
            metadata.GeneralPredicates);
    }

    private static BoundExpression DecodeGeneralOutputStatePredicate(
        int methodRow, int graphRow,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        var footprint = tables.PredicateGraphs.GetReadFootprintRow(graphRow);
        var footprints = tables.PredicateReadFootprints;
        var rootFirst = footprints.GetRootFirstRow(footprint);
        var rootCount = footprints.GetRootRowCount(footprint);
        if (footprints.GetDependencyRowCount(footprint) != 0)
        {
            throw Unsupported(
                "The GeneralPredicateGraphsDraft Boolean selector has an unsupported read footprint.");
        }

        var signature = tables.Methods.GetSignature(methodRow);
        var (parameterFirst, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        var readExpressions = new Dictionary<int, BoundExpression>(rootCount);
        var rootKeys = new HashSet<(byte Kind, uint Identity)>();
        for (var offset = 0; offset < rootCount; offset++)
        {
            var readRoot = rootFirst + offset;
            var rootKind = tables.PredicateReadRoots.GetRootKind(readRoot);
            var rootIdentity
                = tables.PredicateReadRoots.GetRootIdentity(readRoot);
            if (!rootKeys.Add((rootKind, rootIdentity)))
            {
                throw Invalid(
                    "The GeneralPredicateGraphsDraft selector has a duplicate read root.");
            }

            if (rootKind == 5)
            {
                if (rootIdentity != 0)
                {
                    throw Invalid(
                        "A GeneralPredicateGraphsDraft saved-result root has a nonzero identity.");
                }

                var resultType = FromNativeValueRow(tables.TypeSpecs,
                    tables.Signatures.GetResultTypeSpec(signature));
                if (!PrimitiveTypeFacts.IsBuiltInScalar(resultType))
                {
                    throw Unsupported(
                        "The GeneralPredicateGraphsDraft selector reads only built-in scalar saved results in this consumer.");
                }

                readExpressions.Add(readRoot,
                    new BoundContractReturnValue(resultType, default));
                continue;
            }

            if (rootKind != 2)
            {
                throw Unsupported(
                    "The GeneralPredicateGraphsDraft selector reads only input formals and saved results in this consumer.");
            }
            var formalOrdinal = tables.PredicateReadRoots
                .GetRootIdentity(readRoot);
            if (formalOrdinal >= (uint)parameterCount)
            {
                throw Invalid(
                    "The GeneralPredicateGraphsDraft selector has an invalid input root.");
            }

            var parameterRow = parameterFirst
                + checked((int)formalOrdinal);
            var parameterTypeSpecRef
                = tables.Parameters.GetTypeSpec(parameterRow);
            var parameterType = FromNativeValueRow(tables.TypeSpecs,
                parameterTypeSpecRef);
            if (tables.Parameters.GetMode(parameterRow) != 0
                || !PrimitiveTypeFacts.IsBuiltInScalar(parameterType))
            {
                throw Unsupported(
                    "The GeneralPredicateGraphsDraft selector root is not a scalar input formal.");
            }

            readExpressions.Add(readRoot, new BoundRead(
                checked((int)formalOrdinal), parameterType, default));
        }

        var rootNode = tables.PredicateGraphs.GetRootNodeRow(graphRow);
        var pending = new Stack<(int NodeRow, bool PostOrder)>();
        var values = new Stack<BoundExpression>();
        var usedRootRows = new HashSet<int>();
        pending.Push((rootNode, false));
        while (pending.Count != 0)
        {
            var (nodeRow, postOrder) = pending.Pop();
            var nodeKind = tables.PredicateNodes.GetNodeKind(nodeRow);
            var childCount = tables.PredicateChildEdges
                .GetChildCount(nodeRow);
            if (!postOrder)
            {
                var resultType = FromNativeValueRow(tables.TypeSpecs,
                    tables.PredicateNodes.GetResultTypeSpecRef(nodeRow));
                if (!PrimitiveTypeFacts.IsBuiltInScalar(resultType))
                {
                    if (nodeKind is 0x20 or 0x21)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft scalar operation has a non-scalar result type.");
                    }

                    throw Unsupported(
                        "A GeneralPredicateGraphsDraft selector node has an unsupported result type.");
                }

                if (nodeKind == 0x11)
                {
                    if (childCount != 0
                        || tables.PredicateNodes.GetOperandCount(nodeRow) != 1)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft SubjectValue node has an invalid shape.");
                    }

                    var operandRow = checked((int)tables.PredicateNodes
                        .GetOperandFirstRow(nodeRow));
                    if (tables.PredicateNodeOperands.GetOperandKind(
                            operandRow) != 1)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft SubjectValue node does not reference a ReadRoot.");
                    }

                    var readRoot = checked((int)tables.PredicateNodeOperands
                        .GetOperandRef(operandRow));
                    if (readRoot < rootFirst
                        || readRoot >= rootFirst + rootCount
                        || !readExpressions.TryGetValue(readRoot,
                            out var read))
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft SubjectValue node references a root outside its footprint.");
                    }
                    if (read.Type != resultType)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft SubjectValue node differs from its input root type.");
                    }

                    _ = usedRootRows.Add(readRoot);
                    values.Push(read);
                    continue;
                }

                if (nodeKind == 0x14)
                {
                    if (childCount != 0
                        || tables.PredicateNodes.GetOperandCount(nodeRow) != 1)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft scalar literal node has an invalid shape.");
                    }

                    var operandRow = checked((int)tables.PredicateNodes
                        .GetOperandFirstRow(nodeRow));
                    if (tables.PredicateNodeOperands.GetOperandKind(
                            operandRow) != 2)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft scalar literal does not select a FrozenValueNode.");
                    }

                    var frozenValueNodeRow = checked((int)tables
                        .PredicateNodeOperands.GetOperandRef(operandRow));
                    var nodeTypeSpecRef = tables.PredicateNodes
                        .GetResultTypeSpecRef(nodeRow);
                    var valueTypeSpecRef = tables.FrozenValueNodes
                        .GetTypeSpecRef(frozenValueNodeRow);
                    if (!tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                            nodeTypeSpecRef, valueTypeSpecRef))
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft scalar literal has a mismatched complete type.");
                    }

                    var payload = tables.FrozenValueNodes.GetScalarPayload(
                        frozenValueNodeRow, tables.Blobs);
                    values.Push(new BoundConstant(
                        ScalarBits.FromPayload(resultType, payload),
                        resultType, default));
                    continue;
                }

                if (nodeKind is 0x20 or 0x21)
                {
                    var expectedChildren = nodeKind == 0x20 ? 1 : 2;
                    if (childCount != expectedChildren
                        || tables.PredicateNodes.GetOperandCount(nodeRow) != 1)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft scalar operation node has an invalid shape.");
                    }
                    var operandRow = checked((int)tables.PredicateNodes
                        .GetOperandFirstRow(nodeRow));
                    if (tables.PredicateNodeOperands.GetOperandKind(
                            operandRow) != 4)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft scalar comparison does not select an operation.");
                    }
                    var operationRow = checked((int)tables
                        .PredicateNodeOperands.GetOperandRef(operandRow));
                    var operationCode = tables.PredicateOperations
                        .GetOperationCode(operationRow);
                    var isOperationInNodeDomain = nodeKind == 0x20
                        ? operationCode >= 0x0001
                            && operationCode <= 0x0003
                        : operationCode >= 0x0004
                            && operationCode <= 0x0014;
                    if (!isOperationInNodeDomain)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft scalar operation selects a code outside its node domain.");
                    }
                }
                else if (nodeKind == 0x22)
                {
                    if (childCount != 1
                        || tables.PredicateNodes.GetOperandCount(nodeRow) != 1)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft conversion node has an invalid shape.");
                    }

                    var operandRow = checked((int)tables.PredicateNodes
                        .GetOperandFirstRow(nodeRow));
                    if (tables.PredicateNodeOperands.GetOperandKind(
                            operandRow) != 4)
                    {
                        throw Invalid(
                            "A GeneralPredicateGraphsDraft conversion does not select an operation.");
                    }
                    var operationRow = checked((int)tables
                        .PredicateNodeOperands.GetOperandRef(operandRow));
                    if (tables.PredicateOperations.GetOperationCode(
                            operationRow) != 0x0015)
                    {
                        throw Unsupported(
                            "A GeneralPredicateGraphsDraft conversion is outside the current OutputState selector consumer subset.");
                    }
                    if (tables.PredicateOperations.GetPolicy(operationRow)
                        != 0)
                    {
                        throw Unsupported(
                            "Only total scalar conversions are supported in this OutputState selector subset.");
                    }
                }

                var requiredChildren = nodeKind switch {
                    0x20 => 1,
                    0x22 => 1,
                    0x23 => 1,
                    0x21 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 => 2,
                    0x29 => 3,
                    _ => -1,
                };
                if (requiredChildren < 0)
                {
                    throw Unsupported(
                        $"GeneralPredicateGraphsDraft output selector node 0x{nodeKind:X} is outside the current consumer subset.");
                }
                if (nodeKind is >= 0x23 and <= 0x28
                    && resultType != PrimitiveType.Bool)
                {
                    throw Invalid(
                        "A GeneralPredicateGraphsDraft Boolean operation node has a non-Boolean result.");
                }
                if (childCount != requiredChildren
                    || (nodeKind is 0x20 or 0x21 or 0x22
                        ? tables.PredicateNodes.GetOperandCount(nodeRow) != 1
                        : tables.PredicateNodes.GetOperandCount(nodeRow) != 0))
                {
                    throw Invalid(
                        "A GeneralPredicateGraphsDraft Boolean operation node has an invalid shape.");
                }

                pending.Push((nodeRow, true));
                for (var childOrdinal = childCount - 1;
                    childOrdinal >= 0; childOrdinal--)
                {
                    pending.Push((
                        tables.PredicateChildEdges.GetChildNodeRowByOrdinal(
                            nodeRow, checked((uint)childOrdinal)), false));
                }
                continue;
            }

            var children = new BoundExpression[childCount];
            for (var child = childCount - 1; child >= 0; child--)
            {
                children[child] = values.Pop();
            }

            values.Push(nodeKind switch {
                0x20 => DecodeScalarUnary(nodeRow, children),
                0x21 => DecodeScalarBinary(nodeRow, children),
                0x22 => DecodeScalarConversion(nodeRow, children),
                0x23 => new BoundUnary(TokenKind.Bang, children[0],
                    PrimitiveType.Bool, default),
                0x24 => new BoundBinary(TokenKind.Ampersand,
                    children[0], children[1], PrimitiveType.Bool, default),
                0x25 => new BoundBinary(TokenKind.Pipe,
                    children[0], children[1], PrimitiveType.Bool, default),
                0x26 => new BoundBinary(TokenKind.Caret,
                    children[0], children[1], PrimitiveType.Bool, default),
                0x27 => new BoundBinary(TokenKind.AmpersandAmpersand,
                    children[0], children[1], PrimitiveType.Bool, default),
                0x28 => new BoundBinary(TokenKind.PipePipe,
                    children[0], children[1], PrimitiveType.Bool, default),
                0x29 => DecodePredicateConditional(nodeRow, children),
                _ => throw new InvalidOperationException(),
            });
        }

        BoundExpression DecodeScalarUnary(int nodeRow,
            BoundExpression[] children)
        {
            if (children.Length != 1)
            {
                throw Invalid(
                    "A GeneralPredicateGraphsDraft unary operation has an invalid child count.");
            }

            var operandRow = checked((int)tables.PredicateNodes
                .GetOperandFirstRow(nodeRow));
            var operationRow = checked((int)tables.PredicateNodeOperands
                .GetOperandRef(operandRow));
            var operationCode = tables.PredicateOperations
                .GetOperationCode(operationRow);
            var policy = tables.PredicateOperations.GetPolicy(operationRow);
            var operation = operationCode switch {
                0x0001 => TokenKind.Plus,
                0x0002 => TokenKind.Minus,
                0x0003 => TokenKind.Tilde,
                _ => throw Invalid(
                    "A GeneralPredicateGraphsDraft unary operation has an unknown operation."),
            };
            var resultType = FromNativeValueRow(tables.TypeSpecs,
                tables.PredicateNodes.GetResultTypeSpecRef(nodeRow));
            var unary = new BoundUnary(operation, children[0], resultType,
                default) {
                    OverflowBehavior = policy == 2
                        ? BoundIntegerOverflowBehavior.Wrapping
                        : BoundIntegerOverflowBehavior.Checked,
                };
            if (!TryGetGeneralPredicateUnaryOperation(unary,
                    out var expectedOperationCode, out var expectedPolicy)
                || expectedOperationCode != operationCode
                || expectedPolicy != policy)
            {
                throw Invalid(
                    "A GeneralPredicateGraphsDraft unary operation has incompatible types or policy.");
            }

            return unary;
        }

        BoundExpression DecodeScalarBinary(int nodeRow,
            BoundExpression[] children)
        {
            if (children.Length != 2)
            {
                throw Invalid(
                    "A GeneralPredicateGraphsDraft binary operation has an invalid child count.");
            }

            var operandRow = checked((int)tables.PredicateNodes
                .GetOperandFirstRow(nodeRow));
            var operationRow = checked((int)tables.PredicateNodeOperands
                .GetOperandRef(operandRow));
            var operationCode = tables.PredicateOperations
                .GetOperationCode(operationRow);
            var policy = tables.PredicateOperations.GetPolicy(operationRow);
            var operation = operationCode switch {
                0x0004 => TokenKind.Plus,
                0x0005 => TokenKind.Minus,
                0x0006 => TokenKind.Star,
                0x0007 => TokenKind.Slash,
                0x0008 => TokenKind.Percent,
                0x0009 => TokenKind.Ampersand,
                0x000A => TokenKind.Pipe,
                0x000B => TokenKind.Caret,
                0x000C => TokenKind.ShiftLeft,
                0x000D => TokenKind.ShiftRight,
                0x000E => TokenKind.ShiftRightUnsigned,
                0x000F => TokenKind.EqualsEquals,
                0x0010 => TokenKind.BangEquals,
                0x0011 => TokenKind.Less,
                0x0012 => TokenKind.LessEquals,
                0x0013 => TokenKind.Greater,
                0x0014 => TokenKind.GreaterEquals,
                _ => throw Invalid(
                    "A GeneralPredicateGraphsDraft binary operation has an unknown operation."),
            };
            var resultType = FromNativeValueRow(tables.TypeSpecs,
                tables.PredicateNodes.GetResultTypeSpecRef(nodeRow));
            var binary = new BoundBinary(operation, children[0], children[1],
                resultType, default) {
                    OverflowBehavior = policy == 2
                        ? BoundIntegerOverflowBehavior.Wrapping
                        : BoundIntegerOverflowBehavior.Checked,
                };
            if (!TryGetGeneralPredicateBinaryOperation(binary,
                    out var expectedOperationCode, out var expectedPolicy)
                || expectedOperationCode != operationCode
                || expectedPolicy != policy)
            {
                throw Invalid(
                    "A GeneralPredicateGraphsDraft binary operation has incompatible types or policy.");
            }

            return binary;
        }

        BoundExpression DecodeScalarConversion(int nodeRow,
            BoundExpression[] children)
        {
            if (children.Length != 1)
            {
                throw Invalid(
                    "A GeneralPredicateGraphsDraft conversion has an invalid child count.");
            }

            var resultType = FromNativeValueRow(tables.TypeSpecs,
                tables.PredicateNodes.GetResultTypeSpecRef(nodeRow));
            var conversion = new BoundCast(children[0], resultType,
                default);
            var operandRow = checked((int)tables.PredicateNodes
                .GetOperandFirstRow(nodeRow));
            var operationRow = checked((int)tables.PredicateNodeOperands
                .GetOperandRef(operandRow));
            var operationCode = tables.PredicateOperations
                .GetOperationCode(operationRow);
            var policy = tables.PredicateOperations.GetPolicy(operationRow);
            if (!TryGetGeneralPredicateConversion(conversion,
                    out var expectedOperationCode, out var expectedPolicy)
                || expectedOperationCode != operationCode
                || expectedPolicy != policy)
            {
                throw Invalid(
                    "A GeneralPredicateGraphsDraft total conversion has an invalid type pair.");
            }

            return conversion;
        }

        BoundExpression DecodePredicateConditional(int nodeRow,
            BoundExpression[] children)
        {
            var resultType = FromNativeValueRow(tables.TypeSpecs,
                tables.PredicateNodes.GetResultTypeSpecRef(nodeRow));
            if (children.Length != 3
                || children[0].Type != PrimitiveType.Bool
                || children[1].Type != resultType
                || children[2].Type != resultType)
            {
                throw Invalid(
                    "A GeneralPredicateGraphsDraft conditional has incompatible child types.");
            }

            return new BoundConditional(children[0], children[1],
                children[2], resultType, default);
        }

        if (values.Count != 1 || usedRootRows.Count != rootCount)
        {
            throw Invalid(
                "The GeneralPredicateGraphsDraft Boolean selector did not consume its complete read footprint.");
        }

        var result = values.Pop();
        if (result.Type != PrimitiveType.Bool)
        {
            throw Invalid(
                "The GeneralPredicateGraphsDraft OutputState root is not Boolean.");
        }

        return result;
    }

    private static StandardOutputStateMetadata
        GetGeneralOutputStateMetadata(
            int methodRow,
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        if (!tables.Schema.UsesGeneralPredicateGraphs())
        {
            throw new ArgumentException(
                "General OutputState metadata requires its exact graph schema.",
                nameof(tables));
        }

        var predicateContracts
            = tables.Contracts.GetPredicateGraphContracts(methodRow);
        var outputContracts
            = tables.Contracts.GetOutputStateContracts(methodRow);
        if (predicateContracts.IsEmpty && outputContracts.IsEmpty)
        {
            return new([], [], []);
        }
        if (predicateContracts.IsEmpty || outputContracts.IsEmpty)
        {
            throw Invalid(
                "The GeneralPredicateGraphsDraft OutputState clauses lack their predicate map.");
        }

        var signature = tables.Methods.GetSignature(methodRow);
        var (parameterFirst, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        var outputFormals = ImmutableArray.CreateBuilder<uint>();
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var parameterRow = parameterFirst + ordinal;
            if (tables.Parameters.GetMode(parameterRow) == 0
                && TryGetInitializationReferenceReferent(
                    tables.TypeSpecs,
                    tables.Parameters.GetTypeSpec(parameterRow), out _))
            {
                outputFormals.Add(checked((uint)ordinal));
            }
        }

        var graphFacts = new Dictionary<int,
            (uint PhaseLocalOrdinal, uint NodeKind)>(
                predicateContracts.Length);
        var selectors = ImmutableArray.CreateBuilder<
            NativeFunctionPointerSelector>(predicateContracts.Length);
        var generalGraphs = new Dictionary<int,
            (uint PhaseLocalOrdinal, BoundExpression Predicate)>();

        foreach (var predicate in predicateContracts.OrderBy(
            static item => item.PhaseLocalOrdinal))
        {
            var rootNode = tables.PredicateGraphs.GetRootNodeRow(
                predicate.PredicateGraphRow);
            var nodeKind = tables.PredicateNodes.GetNodeKind(rootNode);
            if (predicate.PhaseTag != 3
                || !graphFacts.TryAdd(predicate.PredicateGraphRow,
                    (predicate.PhaseLocalOrdinal, nodeKind)))
            {
                throw Invalid(
                    "The GeneralPredicateGraphsDraft OutputState predicate map is not canonical.");
            }

            if (nodeKind is >= 1 and <= 6)
            {
                selectors.Add(new NativeFunctionPointerSelector(
                    predicate.PhaseTag, predicate.PhaseLocalOrdinal,
                    checked((byte)nodeKind)));
            }
            else
            {
                generalGraphs.Add(predicate.PredicateGraphRow,
                    (predicate.PhaseLocalOrdinal,
                        DecodeGeneralOutputStatePredicate(methodRow,
                            predicate.PredicateGraphRow, tables)));
            }
        }

        var boundedFacts = ImmutableArray.CreateBuilder<
            StandardOutputStateFact>(outputContracts.Length);
        var generalPredicates = ImmutableArray.CreateBuilder<
            BoundOutputStatePredicate>(outputContracts.Length);
        var usedGraphRows = new HashSet<int>();
        foreach (var output in outputContracts)
        {
            if (!graphFacts.TryGetValue(output.PredicateGraphRow,
                    out var graph)
                || output.ParameterOrdinal < 0
                || output.ParameterOrdinal >= parameterCount
                || !outputFormals.Contains(
                    checked((uint)output.ParameterOrdinal))
                || output.State is not (0 or 1))
            {
                throw Invalid(
                    "The GeneralPredicateGraphsDraft OutputState row has an invalid owner, graph or state.");
            }

            _ = usedGraphRows.Add(output.PredicateGraphRow);
            if (graph.NodeKind is >= 1 and <= 6)
            {
                boundedFacts.Add(new StandardOutputStateFact(
                    checked((uint)output.ParameterOrdinal), 3,
                    graph.PhaseLocalOrdinal,
                    checked((byte)graph.NodeKind), output.State));
            }
            else
            {
                var generalGraph = generalGraphs[output.PredicateGraphRow];
                generalPredicates.Add(new BoundOutputStatePredicate(
                    output.ParameterOrdinal, generalGraph.PhaseLocalOrdinal,
                    output.State, generalGraph.Predicate));
            }
        }

        if (usedGraphRows.Count != graphFacts.Count)
        {
            throw Invalid(
                "The GeneralPredicateGraphsDraft OutputState map contains an unused selector.");
        }

        var normalizedSelectors = selectors.ToImmutable();
        var normalizedFacts = boundedFacts
            .OrderBy(static fact => fact.FormalOrdinal)
            .ThenBy(static fact => fact.PhaseLocalOrdinal)
            .ToImmutableArray();
        var expectedGeneralOrdinal = checked((uint)normalizedSelectors.Length);
        foreach (var ordinal in generalGraphs.Values
            .Select(static graph => graph.PhaseLocalOrdinal)
            .Order())
        {
            if (ordinal != expectedGeneralOrdinal++)
            {
                throw Invalid(
                    "General OutputState selector ordinals must follow the bounded selector group contiguously.");
            }
        }

        return new StandardOutputStateMetadata(normalizedSelectors,
            normalizedFacts, generalPredicates
                .OrderBy(static predicate => predicate.PhaseLocalOrdinal)
                .ThenBy(static predicate => predicate.FormalOrdinal)
                .ThenBy(static predicate => predicate.State)
                .ToImmutableArray());
    }

    private static StandardOutputStateMetadata GetStandardOutputStateMetadata(
        int methodRow,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
    {
        if (tables.Schema.UsesGeneralPredicateGraphs())
        {
            return GetGeneralOutputStateMetadata(methodRow, tables);
        }

        var predicateContracts
            = tables.Contracts.GetPredicateGraphContracts(methodRow);
        var outputContracts
            = tables.Contracts.GetOutputStateContracts(methodRow);
        if (predicateContracts.IsEmpty && outputContracts.IsEmpty)
        {
            return new([], [], []);
        }
        if (predicateContracts.IsEmpty || outputContracts.IsEmpty)
        {
            throw Invalid(
                "The Standard initialization-reference OutputState clauses lack their predicate map.");
        }

        var signature = tables.Methods.GetSignature(methodRow);
        var (parameterFirst, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        var resultType = tables.Signatures.GetResultTypeSpec(signature);
        var outputFormals = ImmutableArray.CreateBuilder<uint>();
        for (var ordinal = 0; ordinal < parameterCount; ordinal++)
        {
            var parameterRow = parameterFirst + ordinal;
            if (tables.Parameters.GetMode(parameterRow) == 0
                && TryGetInitializationReferenceReferent(
                    tables.TypeSpecs,
                    tables.Parameters.GetTypeSpec(parameterRow), out _))
            {
                outputFormals.Add(checked((uint)ordinal));
            }
        }

        var graphFacts = new Dictionary<int,
            (uint PhaseLocalOrdinal, byte NodeKind)>(
                predicateContracts.Length);
        var selectors = ImmutableArray.CreateBuilder<
            NativeFunctionPointerSelector>(predicateContracts.Length);
        foreach (var predicate in predicateContracts.OrderBy(
            static item => item.PhaseLocalOrdinal))
        {
            var rawNodeKind = tables.PredicateNodes.GetNodeKind(
                tables.PredicateGraphs.GetRootNodeRow(
                    predicate.PredicateGraphRow));
            if (rawNodeKind is < 1 or > 6)
            {
                throw Invalid(
                    "The Standard initialization-reference OutputState selector is not one of the six bounded result tests.");
            }
            var nodeKind = checked((byte)rawNodeKind);
            if (predicate.PhaseTag != 3
                || !SelectorSupportsResultType(nodeKind, resultType,
                    tables.TypeSpecs)
                || !graphFacts.TryAdd(predicate.PredicateGraphRow,
                    (predicate.PhaseLocalOrdinal, nodeKind)))
            {
                throw Invalid(
                    "The Standard initialization-reference predicate map is not canonical.");
            }

            selectors.Add(new NativeFunctionPointerSelector(
                predicate.PhaseTag, predicate.PhaseLocalOrdinal,
                nodeKind));
        }

        var normalizedSelectors = selectors.MoveToImmutable();

        var facts = ImmutableArray.CreateBuilder<
            StandardOutputStateFact>(outputContracts.Length);
        var usedGraphRows = new HashSet<int>();
        foreach (var output in outputContracts)
        {
            if (!graphFacts.TryGetValue(output.PredicateGraphRow,
                    out var predicate)
                || output.ParameterOrdinal < 0
                || output.ParameterOrdinal >= parameterCount
                || !outputFormals.Contains(
                    checked((uint)output.ParameterOrdinal))
                || output.State is not (0 or 1))
            {
                throw Invalid(
                    "The Standard initialization-reference OutputState contradicts its predicate.");
            }

            _ = usedGraphRows.Add(output.PredicateGraphRow);
            facts.Add(new StandardOutputStateFact(
                checked((uint)output.ParameterOrdinal), 3,
                predicate.PhaseLocalOrdinal, predicate.NodeKind,
                output.State));
        }

        if (usedGraphRows.Count != graphFacts.Count)
        {
            throw Invalid(
                "The Standard initialization-reference predicate map contains an unused selector.");
        }

        var normalizedFacts = facts
            .OrderBy(static fact => fact.FormalOrdinal)
            .ThenBy(static fact => fact.PhaseLocalOrdinal)
            .ToImmutableArray();

        return new StandardOutputStateMetadata(normalizedSelectors,
            normalizedFacts, []);

        static bool SelectorSupportsResultType(byte nodeKind,
            uint resultTypeSpec, TrRmTypeSpecTableReader typeSpecs)
            => nodeKind switch {
                1 or 2 => typeSpecs.AreEquivalentTypeSpecRefs(
                    resultTypeSpec, 0xF200_0001),
                3 or 4 => typeSpecs.AreEquivalentTypeSpecRefs(
                        resultTypeSpec, 0xF200_0008)
                    || typeSpecs.AreEquivalentTypeSpecRefs(resultTypeSpec,
                        0xF200_000D),
                5 or 6 => typeSpecs.AreEquivalentTypeSpecRefs(
                    resultTypeSpec, 0xF200_0008),
                _ => false,
            };
    }

    private static bool IsSupportedForeignNativeInterfaceValueType(
        PrimitiveType type, BoundProgram program,
        TrRmArtifactSchema schema)
        => BoundEnumTypes.IsType(type)
            ? schema.UsesEN1EnumClosure()
                && BoundEnumTypes.IsValidType(type,
                    program.EnumDefinitions)
            : DirectNativeImportContract.IsScalar(type);

    private static bool IsSupportedForeignNativeInterfaceConstant(
        BoundConstant constant, PrimitiveType expectedType,
        BoundProgram program, TrRmArtifactSchema schema)
    {
        if (constant.Type != expectedType)
        {
            return false;
        }

        if (BoundEnumTypes.IsType(constant.Type))
        {
            if (!schema.UsesEN1EnumClosure()
                || !BoundEnumTypes.IsValidType(constant.Type,
                    program.EnumDefinitions))
            {
                return false;
            }

            var underlyingType = program.EnumDefinitions[
                BoundEnumTypes.Index(constant.Type)].UnderlyingType;
            return constant.RepresentationType == underlyingType
                && constant.Bits.IsCanonicalFor(underlyingType);
        }

        return IsSupportedForeignNativeInterfaceValueType(constant.Type,
                program, schema)
            && constant.RepresentationType == constant.Type
            && constant.Bits.IsCanonicalFor(constant.Type);
    }

    private static TrRmFrozenScalarValueReference
        AddForeignNativeInterfaceConstantMapValue(
        BoundConstant constant, BoundProgram program,
        Func<PrimitiveType, int> typeSpecRowFor,
        TrRmFrozenScalarValueBuilder scalarValues,
        TrRmArtifactSchema schema)
    {
        if (!IsSupportedForeignNativeInterfaceConstant(constant,
                constant.Type, program, schema))
        {
            throw Unsupported(
                "A Standard F+I ConstantMap value has no supported exact type and representation.");
        }

        if (BoundEnumTypes.IsType(constant.Type))
        {
            var underlyingType = program.EnumDefinitions[
                BoundEnumTypes.Index(constant.Type)].UnderlyingType;
            var typeSpecRef = checked((uint)typeSpecRowFor(constant.Type));
            if (typeSpecRef is 0 or > 0x00FF_FFFF)
            {
                throw Unsupported(
                    "A Standard F+I enum ConstantMap value has no complete TypeSpec reference.");
            }

            return scalarValues.AddEN1EnumMember(constant, typeSpecRef,
                underlyingType, schema);
        }

        return scalarValues.Add(constant);
    }

    private static byte[] EncodeStandardInitializationReferenceBody(
        BoundFunction function, BoundProgram program,
        Func<PrimitiveType, int> typeSpecRowFor,
        Func<int, uint> methodMapTokenForFunction,
        Func<BoundFieldRead, uint> fieldDefRowFor,
        Func<BoundFieldRead, uint> fieldPathRowFor,
        Func<BoundInterfaceMemberIdentity, uint>?
            foreignInterfaceMethodMapTokenForCall,
        TrRmFrozenScalarValueBuilder scalarValues,
        TrRmArtifactSchema schema)
    {
        if (TryGetForeignInterfaceConditionalOutputCall(function, program,
                out var conditionalCall, out var conditionalOutputVariable,
                out var conditionalStatusVariable,
                out var conditionalSuccessOnTrue))
        {
            return EncodeForeignInterfaceConditionalOutputCallBody(
                function, program, conditionalCall, null, null, [],
                [conditionalOutputVariable],
                conditionalOutputVariable, conditionalStatusVariable, -1,
                conditionalSuccessOnTrue, typeSpecRowFor,
                foreignInterfaceMethodMapTokenForCall
                    ?? throw Unsupported(
                        "A Standard F+I caller Body has no InterfaceMethod MethodMap selection."),
                scalarValues, schema);
        }

        if (TryGetForeignInterfaceConditionalOutputCallWithFallback(
                function, program, schema,
                out var conditionalFallbackCall,
                out var conditionalPreludeCalls,
                out var conditionalOutputVariables,
                out var fallbackOutputVariable, out var fallbackStatusVariable,
                out var fallbackVariable, out var fallbackSuccessOnTrue,
                out var fallbackSuccessCall,
                out var fallbackSuccessNestedCall))
        {
            return EncodeForeignInterfaceConditionalOutputCallBody(
                function, program, conditionalFallbackCall,
                fallbackSuccessCall,
                fallbackSuccessNestedCall,
                conditionalPreludeCalls,
                conditionalOutputVariables,
                fallbackOutputVariable, fallbackStatusVariable,
                fallbackVariable, fallbackSuccessOnTrue, typeSpecRowFor,
                foreignInterfaceMethodMapTokenForCall
                    ?? throw Unsupported(
                        "A Standard F+I caller Body has no InterfaceMethod MethodMap selection."),
                scalarValues, schema);
        }

        if (TryGetForeignInterfaceDirectReturnCall(function, program,
                out var foreignInterfaceCall, out var stagedLocals))
        {
            return EncodeForeignInterfaceDirectReturnCallBody(function,
                program, foreignInterfaceCall, stagedLocals, typeSpecRowFor,
                foreignInterfaceMethodMapTokenForCall
                    ?? throw Unsupported(
                        "A Standard F+I caller Body has no InterfaceMethod MethodMap selection."),
                scalarValues, schema);
        }

        if (TryGetForeignInterfaceCallSequence(function, program,
                out var foreignInterfaceCallsInSequence,
                out var foreignInterfaceCallSequenceHasValueResult,
                out var foreignInterfaceCallSequenceNestedInputs,
                out var foreignInterfaceCallSequenceIsTerminal))
        {
            return EncodeForeignInterfaceCallSequenceBody(function, program,
                foreignInterfaceCallsInSequence,
                foreignInterfaceCallSequenceHasValueResult,
                foreignInterfaceCallSequenceIsTerminal, typeSpecRowFor,
                foreignInterfaceCallSequenceNestedInputs,
                methodMapTokenForFunction,
                foreignInterfaceMethodMapTokenForCall
                    ?? throw Unsupported(
                        "A Standard F+I caller Body has no InterfaceMethod MethodMap selection."),
                scalarValues, schema);
        }

        if (TryGetStandardInitializationReferenceConditionalStore(
                function, program, out var condition,
                out var conditionalStores))
        {
            return EncodeInitializationReferenceConditionalStoreBody(
                function, typeSpecRowFor, condition, conditionalStores,
                schema);
        }

        if (TryGetInitializationReferenceConditionalCall(function, program,
                out var conditionalCall, out var conditionalOutput,
                out var conditionalFallback))
        {
            return EncodeInitializationReferenceConditionalCallBody(
                function, conditionalCall, conditionalOutput,
                conditionalFallback, typeSpecRowFor,
                methodMapTokenForFunction, schema);
        }

        if (TryGetInitializationReferenceStore(function, program,
                out var stores))
        {
            return EncodeInitializationReferenceStoreBody(function,
                typeSpecRowFor, stores, schema);
        }

        if (TryGetInitializationReferenceForwardingCall(function, program,
                out var forwardingCall))
        {
            return EncodeInitializationReferenceForwardingCallBody(
                function, forwardingCall, typeSpecRowFor,
                methodMapTokenForFunction, schema);
        }

        if (TryGetInitializationReferenceCall(function, program,
                out var call, out var outputPlace, out var outputVariable,
                out _))
        {
            return EncodeInitializationReferenceCallBody(function, program,
                call, outputPlace, outputVariable, typeSpecRowFor,
            methodMapTokenForFunction, fieldDefRowFor,
            fieldPathRowFor, schema);
        }

        throw Unsupported(
            $"Function '{function.Name}' is outside the Standard initialization-reference Body subset.");
    }

    private static bool TryGetForeignInterfaceConditionalOutputCall(
        BoundFunction function, BoundProgram program, out BoundCall call,
        out int outputVariable, out int statusVariable,
        out bool successOnTrue)
    {
        call = null!;
        outputVariable = -1;
        statusVariable = -1;
        successOnTrue = false;
        if (function.IsExternal || function.ParameterCount != 2
            || function.Variables.Length != 4
            || function.DoesNotReturn
            || function.ReturnType is not (PrimitiveType.Int32
                or PrimitiveType.UInt32)
            || function.Body.RetiredManagedLocals.Length != 0
            || function.Variables[0].Type != PrimitiveType.NativeRawPointer
            || function.Variables[0].IsOut || function.Variables[0].IsInit
            || function.Variables[0].IsImplicitRef
            || function.Variables[0].IsImplicitReferenceTemporary
            || function.Variables[1].IsOut || function.Variables[1].IsInit
            || function.Variables[1].IsImplicitRef
            || function.Variables[1].IsImplicitReferenceTemporary
            || function.Variables[2].Type != function.ReturnType
            || function.Variables[2].IsOut || function.Variables[2].IsInit
            || !function.Variables[2].IsInitializationLocal
            || function.Variables[3].Type != function.ReturnType
            || function.Variables[3].IsOut || function.Variables[3].IsInit
            || function.Variables[3].IsImplicitRef
            || function.Variables[3].IsImplicitReferenceTemporary
            || function.Variables[3].IsInitializationLocal)
        {
            return false;
        }

        var statements = function.Body.Statements;
        if (statements.Length == 1
            && statements[0] is BoundUnsafe unsafeBlock)
        {
            if (unsafeBlock.Body.RetiredManagedLocals.Length != 0)
            {
                return false;
            }
            statements = unsafeBlock.Body.Statements;
        }
        else if (statements.Length != 0
            && statements[^1] is BoundUnsafe unsafeBody)
        {
            if (unsafeBody.Body.RetiredManagedLocals.Length != 0
                || unsafeBody.Body.Statements.IsEmpty)
            {
                return false;
            }

            var statementBuilder
                = ImmutableArray.CreateBuilder<BoundStatement>(
                    statements.Length - 1
                    + unsafeBody.Body.Statements.Length);
            for (var index = 0; index < statements.Length - 1; index++)
            {
                statementBuilder.Add(statements[index]);
            }
            statementBuilder.AddRange(unsafeBody.Body.Statements);
            statements = statementBuilder.ToImmutable();
        }

        if (statements.Length != 4
            || statements[0] is not BoundVacantLocal {
                Variable: 2,
            }
            || statements[1] is not BoundLocal {
                Variable: 3,
            } statusLocal
            || statusLocal.Initializer.Type != function.ReturnType
            || statements[2] is not BoundIf {
                Condition: BoundExpression condition,
                Then: BoundBlock { Statements.Length: 1 } thenBlock,
                Else: null,
            }
            || thenBlock.Statements[0] is not BoundReturn {
                Value: BoundRead thenRead,
            }
            || thenRead.Type != function.ReturnType
            || statements[3] is not BoundReturn {
                Value: BoundRead finalRead,
            }
            || finalRead.Type != function.ReturnType)
        {
            return false;
        }

        if (!TryGetForeignNativeOutputComparison(condition, 3,
                function.ReturnType, out var conditionOperator))
        {
            return false;
        }

        var callExpression = statusLocal.Initializer;
        if (callExpression is BoundUnsafeExpression unsafeCall)
        {
            callExpression = unsafeCall.Expression;
        }

        if (callExpression is not BoundCall boundCall
            || boundCall.Function != BoundCall.ForeignInterfaceFunction
            || boundCall.ForeignInterfaceCall is not
                BoundForeignInterfaceCall foreignCall
            || !boundCall.IsUnsafeAuthorized
            || foreignCall.DoesNotReturn
            || boundCall.Type != function.ReturnType
            || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                foreignCall, program, out var target)
            || target.ReturnType != function.ReturnType
            || target.ParameterCount != 2
            || target.NativeOutputContracts.Length != 1
            || target.NativeOutputContracts[0].FormalOrdinal != 1
            || target.NativeOutputContracts[0].PointeeType
                != function.ReturnType
            || target.NativeOutputContracts[0].SuccessState
                != NativeOutputState.Live
            || target.NativeOutputContracts[0].ComplementState
                != NativeOutputState.Vacant
            || boundCall.Arguments.IsDefault
            || boundCall.Arguments.Length != 2
            || boundCall.ParameterIndex(0) != 0
            || boundCall.ParameterIndex(1) != 1
            || foreignCall.Receiver is not BoundRead {
                Variable: 0,
                Type: PrimitiveType.NativeRawPointer,
            }
            || boundCall.Arguments[0] is not BoundRead sourceRead
            || (uint)sourceRead.Variable >= (uint)function.ParameterCount
            || sourceRead.Variable != 1
            || sourceRead.Type != target.Variables[0].Type
            || boundCall.Arguments[1] is not
                BoundNativeOutputReservation reservation
            || reservation.Variable != 2
            || reservation.PointeeType != function.ReturnType)
        {
            return false;
        }

        var outputContract = target.NativeOutputContracts[0];
        var expectedOperator = GetForeignNativeOutputSuccessOperator(
            outputContract.SuccessSelector,
            outputContract.ComplementSelector, function.ReturnType);
        var complementOperator = GetForeignNativeOutputComplementOperator(
            outputContract.SuccessSelector,
            outputContract.ComplementSelector, function.ReturnType);
        if (expectedOperator is not TokenKind successOperator
            || complementOperator is not TokenKind failureOperator)
        {
            return false;
        }

        successOnTrue = conditionOperator == successOperator;
        if ((!successOnTrue && conditionOperator != failureOperator)
            || thenRead.Variable != (successOnTrue ? 2 : 3)
            || finalRead.Variable != (successOnTrue ? 3 : 2))
        {
            return false;
        }

        call = boundCall;
        outputVariable = 2;
        statusVariable = 3;
        return true;
    }

    private static TokenKind? GetForeignNativeOutputSuccessOperator(
        NativeOutputSelector successSelector,
        NativeOutputSelector complementSelector, PrimitiveType resultType)
        => (successSelector, complementSelector, resultType) switch {
            (NativeOutputSelector.NonZero, NativeOutputSelector.Zero,
                PrimitiveType.Int32 or PrimitiveType.UInt32)
                => TokenKind.BangEquals,
            (NativeOutputSelector.NonNegative,
                NativeOutputSelector.Negative, PrimitiveType.Int32)
                => TokenKind.GreaterEquals,
            _ => null,
        };

    private static TokenKind? GetForeignNativeOutputComplementOperator(
        NativeOutputSelector successSelector,
        NativeOutputSelector complementSelector, PrimitiveType resultType)
        => (successSelector, complementSelector, resultType) switch {
            (NativeOutputSelector.NonZero, NativeOutputSelector.Zero,
                PrimitiveType.Int32 or PrimitiveType.UInt32)
                => TokenKind.EqualsEquals,
            (NativeOutputSelector.NonNegative,
                NativeOutputSelector.Negative, PrimitiveType.Int32)
                => TokenKind.Less,
            _ => null,
        };

    private static byte? GetForeignNativeOutputComparisonOpcode(
        TokenKind? comparison)
        => comparison switch {
            TokenKind.BangEquals => 13,
            TokenKind.GreaterEquals => 6,
            TokenKind.EqualsEquals => 2,
            TokenKind.Less => 1,
            _ => null,
        };

    private static bool TryGetForeignNativeOutputComparison(
        BoundExpression expression, int statusVariable,
        PrimitiveType statusType, out TokenKind comparison)
    {
        comparison = default;
        var negated = false;
        while (expression is BoundUnary {
                   Operator: TokenKind.Bang,
                   Type: PrimitiveType.Bool,
               } unary)
        {
            expression = unary.Operand;
            negated = !negated;
        }

        if (expression is not BoundBinary binary
            || binary.Type != PrimitiveType.Bool)
        {
            return false;
        }

        if (binary.Left is BoundRead {
                Variable: var leftVariable,
            } leftRead
            && leftVariable == statusVariable
            && leftRead.Type == statusType
            && IsZeroNativeOutputResult(binary.Right, statusType))
        {
            comparison = binary.Operator;
        }
        else if (binary.Right is BoundRead {
                     Variable: var rightVariable,
                 } rightRead
                 && rightVariable == statusVariable
                 && rightRead.Type == statusType
                 && IsZeroNativeOutputResult(binary.Left, statusType))
        {
            if (!TryReverseNativeOutputComparison(binary.Operator,
                    out comparison))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        if (negated)
        {
            if (!TryNegateNativeOutputComparison(comparison,
                    out var negatedComparison))
            {
                return false;
            }

            comparison = negatedComparison;
        }

        return true;
    }

    private static bool IsZeroNativeOutputResult(BoundExpression expression,
        PrimitiveType resultType)
        => expression is BoundConstant {
            Bits.Low: 0,
            Bits.High: 0,
        } constant && constant.Type == resultType;

    private static bool TryReverseNativeOutputComparison(TokenKind comparison,
        out TokenKind reversed)
    {
        reversed = comparison switch {
            TokenKind.BangEquals => TokenKind.BangEquals,
            TokenKind.EqualsEquals => TokenKind.EqualsEquals,
            TokenKind.Less => TokenKind.Greater,
            TokenKind.LessEquals => TokenKind.GreaterEquals,
            TokenKind.Greater => TokenKind.Less,
            TokenKind.GreaterEquals => TokenKind.LessEquals,
            _ => default,
        };
        return comparison is TokenKind.BangEquals or TokenKind.EqualsEquals
            or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater
            or TokenKind.GreaterEquals;
    }

    private static bool TryNegateNativeOutputComparison(TokenKind comparison,
        out TokenKind negated)
    {
        negated = comparison switch {
            TokenKind.BangEquals => TokenKind.EqualsEquals,
            TokenKind.EqualsEquals => TokenKind.BangEquals,
            TokenKind.Less => TokenKind.GreaterEquals,
            TokenKind.LessEquals => TokenKind.Greater,
            TokenKind.Greater => TokenKind.LessEquals,
            TokenKind.GreaterEquals => TokenKind.Less,
            _ => default,
        };
        return comparison is TokenKind.BangEquals or TokenKind.EqualsEquals
            or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater
            or TokenKind.GreaterEquals;
    }

    private static bool TryGetForeignInterfaceConditionalOutputCallWithFallback(
        BoundFunction function, BoundProgram program,
        TrRmArtifactSchema schema, out BoundCall call,
        out ImmutableArray<BoundCall> preludeCalls,
        out ImmutableArray<int> outputVariables,
        out int outputVariable, out int statusVariable,
        out int fallbackVariable, out bool successOnTrue,
        out BoundCall? successCall, out BoundCall? successNestedCall)
    {
        call = null!;
        preludeCalls = [];
        outputVariables = [];
        outputVariable = -1;
        statusVariable = -1;
        fallbackVariable = -1;
        successOnTrue = false;
        successCall = null;
        successNestedCall = null;
        if (function.IsExternal || function.ParameterCount < 3
            || function.Variables.Length < function.ParameterCount + 2
            || function.DoesNotReturn
            || (!DirectNativeImportContract.IsOutputPointee(
                    function.ReturnType)
                && !schema
                    .UsesForeignNativeInterfaceAggregateOutputConsumption())
            || function.Body.RetiredManagedLocals.Length != 0
            || function.Variables[0].Type != PrimitiveType.NativeRawPointer
            || function.Variables[0].IsOut || function.Variables[0].IsInit
            || function.Variables[0].IsImplicitRef
            || function.Variables[0].IsImplicitReferenceTemporary
            || function.Variables[0].IsInitializationLocal)
        {
            return false;
        }

        var fallbackLocal = function.ParameterCount - 1;
        for (var parameter = 1; parameter < function.ParameterCount;
            parameter++)
        {
            var variable = function.Variables[parameter];
            if (variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary
                || variable.IsInitializationLocal)
            {
                return false;
            }
        }

        var fallback = function.Variables[fallbackLocal];
        if (fallback.Type != function.ReturnType
            || fallback.IsOut || fallback.IsInit
            || fallback.IsImplicitRef
            || fallback.IsImplicitReferenceTemporary
            || fallback.IsInitializationLocal)
        {
            return false;
        }

        var statements = function.Body.Statements;
        if (statements.Length == 1
            && statements[0] is BoundUnsafe unsafeBlock)
        {
            if (unsafeBlock.Body.RetiredManagedLocals.Length != 0)
            {
                return false;
            }
            statements = unsafeBlock.Body.Statements;
        }
        else if (statements.Length != 0
            && statements[^1] is BoundUnsafe unsafeBody)
        {
            if (unsafeBody.Body.RetiredManagedLocals.Length != 0
                || unsafeBody.Body.Statements.IsEmpty)
            {
                return false;
            }

            var statementBuilder
                = ImmutableArray.CreateBuilder<BoundStatement>(
                    statements.Length - 1
                    + unsafeBody.Body.Statements.Length);
            for (var index = 0; index < statements.Length - 1; index++)
            {
                statementBuilder.Add(statements[index]);
            }
            statementBuilder.AddRange(unsafeBody.Body.Statements);
            statements = statementBuilder.ToImmutable();
        }

        var inputHomeVariables = new HashSet<int>();
        var preludeCallBuilder
            = ImmutableArray.CreateBuilder<BoundCall>();
        var conditionalStatement = 0;
        while (conditionalStatement < statements.Length
            && statements[conditionalStatement]
                is BoundExpressionStatement {
                    Expression: BoundExpression preludeExpression,
                })
        {
            if (GetUnwrappedBoundCall(preludeExpression)
                    is not BoundCall preludeCall
                || preludeCall.Function
                    != BoundCall.ForeignInterfaceFunction
                || preludeCall.ForeignInterfaceCall is not
                    BoundForeignInterfaceCall preludeForeignCall
                || !preludeCall.IsUnsafeAuthorized
                || preludeCall.Type != PrimitiveType.Void
                || preludeForeignCall.DoesNotReturn
                || preludeForeignCall.NativeOutputContracts.Length != 0
                || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                    preludeForeignCall, program, out var preludeTarget)
                || preludeTarget.ReturnType != PrimitiveType.Void
                || preludeTarget.ParameterCount
                    != preludeCall.Arguments.Length
                || !preludeTarget.NativeOutputContracts.IsEmpty
                || preludeCall.Arguments.IsDefault
                || preludeForeignCall.Receiver is not BoundRead {
                    Variable: 0,
                    Type: PrimitiveType.NativeRawPointer,
                })
            {
                return false;
            }

            var preludeSeenParameters
                = new bool[preludeTarget.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < preludeCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = preludeCall.ParameterIndex(argumentIndex);
                var argument = preludeCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)preludeTarget.ParameterCount
                    || preludeSeenParameters[formal]
                    || argument is not BoundRead read
                    || read.Variable <= 0
                    || read.Variable >= fallbackLocal
                    || read.Type != preludeTarget.Variables[formal].Type
                    || function.Variables[read.Variable].Type != read.Type
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit
                    || !(IsSupportedForeignNativeInterfaceValueType(
                            read.Type, program, schema)
                        || read.Type == PrimitiveType.NativeRawPointer))
                {
                    return false;
                }

                preludeSeenParameters[formal] = true;
                _ = inputHomeVariables.Add(read.Variable);
            }

            if (preludeSeenParameters.Any(static seen => !seen))
            {
                return false;
            }

            preludeCallBuilder.Add(preludeCall);
            conditionalStatement++;
        }

        var outputVariableBuilder = ImmutableArray.CreateBuilder<int>();
        while (conditionalStatement < statements.Length
            && statements[conditionalStatement] is BoundVacantLocal vacantLocal
            && vacantLocal.Variable
                == function.ParameterCount + outputVariableBuilder.Count)
        {
            var output = function.Variables[vacantLocal.Variable];
            if (output.IsOut || output.IsInit || output.IsImplicitRef
                || output.IsImplicitReferenceTemporary
                || !output.IsInitializationLocal)
            {
                return false;
            }

            outputVariableBuilder.Add(vacantLocal.Variable);
            conditionalStatement++;
        }

        if (outputVariableBuilder.Count == 0
            || function.Variables.Length
                != function.ParameterCount + outputVariableBuilder.Count + 1)
        {
            return false;
        }

        var outputVariablesLocal = outputVariableBuilder.ToImmutable();
        var statusLocalIndex
            = function.ParameterCount + outputVariablesLocal.Length;
        var status = function.Variables[statusLocalIndex];
        if (status.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32)
            || status.IsOut || status.IsInit || status.IsImplicitRef
            || status.IsImplicitReferenceTemporary
            || status.IsInitializationLocal
            || statements.Length - conditionalStatement != 3
            || statements[conditionalStatement] is not BoundLocal statusDeclaration
            || statusDeclaration.Variable != statusLocalIndex
            || statusDeclaration.Initializer.Type != status.Type
            || statements[conditionalStatement + 1] is not BoundIf {
                Condition: BoundExpression condition,
                Then: BoundBlock { Statements.Length: 1 } thenBlock,
                Else: null,
            }
            || thenBlock.Statements[0] is not BoundReturn {
                Value: BoundExpression thenValue,
            }
            || thenValue.Type != function.ReturnType
            || statements[conditionalStatement + 2] is not BoundReturn {
                Value: BoundRead finalRead,
            }
            || finalRead.Type != function.ReturnType)
        {
            return false;
        }

        var statusType = statusDeclaration.Initializer.Type;
        if (!TryGetForeignNativeOutputComparison(condition,
                statusLocalIndex, statusType, out var conditionOperator))
        {
            return false;
        }

        var callExpression = statusDeclaration.Initializer;
        if (callExpression is BoundUnsafeExpression unsafeCall)
        {
            callExpression = unsafeCall.Expression;
        }

        if (callExpression is not BoundCall boundCall
            || boundCall.Function != BoundCall.ForeignInterfaceFunction
            || boundCall.ForeignInterfaceCall is not
                BoundForeignInterfaceCall foreignCall
            || !boundCall.IsUnsafeAuthorized
            || foreignCall.DoesNotReturn
            || boundCall.Type != statusType
            || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                foreignCall, program, out var target)
            || target.ReturnType != statusType
            || boundCall.Arguments.IsDefault
            || target.ParameterCount != boundCall.Arguments.Length
            || target.NativeOutputContracts.IsEmpty
            || target.NativeOutputContracts.Length
                != outputVariablesLocal.Length
            || foreignCall.Receiver is not BoundRead receiver
            || receiver.Variable != 0
            || receiver.Type != PrimitiveType.NativeRawPointer)
        {
            return false;
        }

        var outputContract = target.NativeOutputContracts[0];
        var outputContractsByFormal
            = new Dictionary<int, NativeOutputContract>(
                target.NativeOutputContracts.Length);
        foreach (var contract in target.NativeOutputContracts)
        {
            if ((uint)contract.FormalOrdinal >= (uint)target.ParameterCount
                || !outputContractsByFormal.TryAdd(
                    contract.FormalOrdinal, contract)
                || contract.SuccessSelector
                    != outputContract.SuccessSelector
                || contract.ComplementSelector
                    != outputContract.ComplementSelector
                || contract.SuccessState != NativeOutputState.Live
                || contract.ComplementState != NativeOutputState.Vacant
                || (outputVariablesLocal.Length > 1
                    && !DirectNativeImportContract.IsOutputPointee(
                        contract.PointeeType)))
            {
                return false;
            }
        }

        var seenParameters = new bool[target.ParameterCount];
        var scalarExpressionRegisterCount = 0;
        var hasThrowingScalarExpression = false;
        var constantInputs
            = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
        var nestedInputCalls = new List<BoundCall>();
        var outputVariableByFormal = new Dictionary<int, int>(
            outputVariablesLocal.Length);
        var reservedOutputVariables = new HashSet<int>();
        for (var sourceArgument = 0;
            sourceArgument < boundCall.Arguments.Length;
            sourceArgument++)
        {
            var formal = boundCall.ParameterIndex(sourceArgument);
            if ((uint)formal >= (uint)target.ParameterCount
                || seenParameters[formal])
            {
                return false;
            }

            var argument = boundCall.Arguments[sourceArgument];
            if (outputContractsByFormal.TryGetValue(formal,
                    out var formalOutputContract))
            {
                if (argument is not BoundNativeOutputReservation reservation
                    || !outputVariablesLocal.Contains(
                        reservation.Variable)
                    || reservation.PointeeType
                        != formalOutputContract.PointeeType
                    || function.Variables[reservation.Variable].Type
                        != formalOutputContract.PointeeType
                    || !outputVariableByFormal.TryAdd(
                        formal, reservation.Variable)
                    || !reservedOutputVariables.Add(reservation.Variable))
                {
                    return false;
                }
            }
            else if (argument is BoundRead read)
            {
                if (read.Variable <= 0
                    || read.Variable >= fallbackLocal
                    || read.Type != target.Variables[formal].Type
                    || function.Variables[read.Variable].Type
                        != target.Variables[formal].Type
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit)
                {
                    return false;
                }

                _ = inputHomeVariables.Add(read.Variable);
            }
            else if (argument is BoundConstant constant
                && constant.Type == target.Variables[formal].Type
                && IsSupportedForeignNativeInterfaceConstant(constant,
                    target.Variables[formal].Type, program, schema))
            {
                constantInputs.Add((constant.Type, constant.Bits));
                if (constantInputs.Count + nestedInputCalls.Count
                    + scalarExpressionRegisterCount > 13)
                {
                    return false;
                }
            }
            else if (argument is BoundUnary or BoundBinary or BoundCast
                or BoundUnsafeExpression)
            {
                var expressionConstants
                    = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
                if (!TryAnalyzeForeignInterfaceScalarInputExpression(
                        argument, target.Variables[formal].Type, function,
                        fallbackLocal, expressionConstants,
                        out var expressionRegisterCount))
                {
                    return false;
                }

                scalarExpressionRegisterCount = checked(
                    scalarExpressionRegisterCount
                    + expressionRegisterCount);
                hasThrowingScalarExpression |=
                    ForeignInterfaceScalarInputExpressionMayThrow(argument);
                CollectForeignInterfaceScalarInputHomeVariables(argument,
                    inputHomeVariables);
                constantInputs.UnionWith(expressionConstants);
                if (constantInputs.Count + nestedInputCalls.Count
                        + scalarExpressionRegisterCount > 13)
                {
                    return false;
                }
            }
            else if (constantInputs.Count + nestedInputCalls.Count
                    + scalarExpressionRegisterCount < 13
                && GetNestedForeignInterfaceCall(argument)
                    is BoundCall nestedCall
                && !nestedInputCalls.Any(candidate =>
                    ReferenceEquals(candidate, nestedCall))
                && nestedCall.Type == target.Variables[formal].Type
                && IsValidNestedForeignInterfaceCall(nestedCall,
                    nestedCall.Type))
            {
                nestedInputCalls.Add(nestedCall);
            }
            else
            {
                return false;
            }

            seenParameters[formal] = true;
        }

        if (seenParameters.Any(static seen => !seen)
            || outputVariableByFormal.Count
                != outputContractsByFormal.Count
            || reservedOutputVariables.Count
                != outputVariablesLocal.Length
            || preludeCallBuilder.Count + nestedInputCalls.Count > 13
            || (hasThrowingScalarExpression
                && nestedInputCalls.Count != 0
                && !CanMixCheckedForeignInterfaceExpressionsWithNestedCalls(
                    boundCall, program)))
        {
            return false;
        }

        var expectedOperator = GetForeignNativeOutputSuccessOperator(
            outputContract.SuccessSelector,
            outputContract.ComplementSelector, target.ReturnType);
        var complementOperator = GetForeignNativeOutputComplementOperator(
            outputContract.SuccessSelector,
            outputContract.ComplementSelector, target.ReturnType);
        if (expectedOperator is not TokenKind successOperator
            || complementOperator is not TokenKind failureOperator)
        {
            return false;
        }

        successOnTrue = conditionOperator == successOperator;
        var successReturnValue = successOnTrue
            ? thenValue
            : (BoundExpression)finalRead;
        var fallbackReturnValue = successOnTrue
            ? finalRead
            : thenValue;
        if ((!successOnTrue && conditionOperator != failureOperator)
            || fallbackReturnValue is not BoundRead {
                Variable: var fallbackReturnVariable,
            }
            || fallbackReturnVariable != fallbackLocal)
        {
            return false;
        }

        int successOutputVariable;
        var successCallCandidate = GetUnwrappedBoundCall(
            successReturnValue);
        if (successCallCandidate is not null)
        {
            if (!successOnTrue
                || !TryGetOutputConsumerVariable(successCallCandidate,
                    outputVariablesLocal,
                    out successOutputVariable, out successNestedCall))
            {
                return false;
            }

            successCall = successCallCandidate;
        }
        else if (successReturnValue is BoundRead successRead
            && outputVariablesLocal.Contains(successRead.Variable)
            && successRead.Type == function.ReturnType
            && function.Variables[successRead.Variable].Type
                == function.ReturnType
            && outputVariableByFormal.Any(pair =>
                pair.Value == successRead.Variable
                && outputContractsByFormal[pair.Key].PointeeType
                    == function.ReturnType))
        {
            successOutputVariable = successRead.Variable;
        }
        else
        {
            return false;
        }

        if (inputHomeVariables.Count != fallbackLocal - 1)
        {
            return false;
        }
        if (preludeCallBuilder.Count + nestedInputCalls.Count
                + (successNestedCall is null ? 0 : 1) + 2 > 13)
        {
            return false;
        }

        call = boundCall;
        preludeCalls = preludeCallBuilder.ToImmutable();
        outputVariables = outputVariablesLocal;
        outputVariable = successOutputVariable;
        statusVariable = statusLocalIndex;
        fallbackVariable = fallbackLocal;
        return true;

        bool TryGetOutputConsumerVariable(BoundCall consumerCall,
            ImmutableArray<int> reservedOutputVariables,
            out int consumedOutputVariable, out BoundCall? nestedInputCall)
        {
            consumedOutputVariable = -1;
            nestedInputCall = null;
            var reservedOutputHomes = reservedOutputVariables.ToHashSet();
            if (consumerCall.Function
                    != BoundCall.ForeignInterfaceFunction
                || consumerCall.ForeignInterfaceCall is not
                    BoundForeignInterfaceCall consumerForeignCall
                || !consumerCall.IsUnsafeAuthorized
                || consumerForeignCall.DoesNotReturn
                || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                    consumerForeignCall, program, out var consumerTarget)
                || consumerTarget.ReturnType != function.ReturnType
                || consumerCall.Type != function.ReturnType
                || !consumerTarget.NativeOutputContracts.IsEmpty
                || consumerTarget.ParameterCount
                    != consumerCall.Arguments.Length
                || consumerCall.Arguments.IsDefault
                || consumerForeignCall.Receiver is not BoundRead receiver
                || receiver.Type != PrimitiveType.NativeRawPointer
                || (receiver.Variable != 0
                    && !reservedOutputHomes.Contains(receiver.Variable))
                || (reservedOutputHomes.Contains(receiver.Variable)
                    && function.Variables[receiver.Variable].Type
                        != PrimitiveType.NativeRawPointer)
                || reservedOutputVariables.IsEmpty)
            {
                return false;
            }

            var seenParameters = new bool[consumerTarget.ParameterCount];
            var consumedOutputHomes = new HashSet<int>();
            var hasPointerOutputUse = false;
            if (reservedOutputHomes.Contains(receiver.Variable))
            {
                _ = consumedOutputHomes.Add(receiver.Variable);
                hasPointerOutputUse = true;
            }
            for (var argumentIndex = 0;
                argumentIndex < consumerCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = consumerCall.ParameterIndex(argumentIndex);
                var argument = consumerCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)consumerTarget.ParameterCount
                    || seenParameters[formal]
                    || !(IsSupportedForeignNativeInterfaceValueType(
                            consumerTarget.Variables[formal].Type,
                            program, schema)
                        || consumerTarget.Variables[formal].Type
                            == PrimitiveType.NativeRawPointer))
                {
                    return false;
                }

                if (consumerCall.Arguments[argumentIndex]
                        is BoundRead read)
                {
                    if ((uint)read.Variable
                            >= (uint)function.Variables.Length
                        || read.Type
                            != consumerTarget.Variables[formal].Type
                        || function.Variables[read.Variable].Type
                            != read.Type
                        || function.Variables[read.Variable].IsOut
                        || function.Variables[read.Variable].IsInit)
                    {
                        return false;
                    }

                    if (reservedOutputHomes.Contains(read.Variable))
                    {
                        if (!consumedOutputHomes.Add(read.Variable))
                        {
                            return false;
                        }

                        hasPointerOutputUse |=
                            IsForeignNativeInterfacePointerValueType(
                                read.Type);
                    }
                    else if (read.Variable <= 0
                        || read.Variable >= fallbackLocal)
                    {
                        return false;
                    }
                    else
                    {
                        _ = inputHomeVariables.Add(read.Variable);
                    }
                }
                else if (GetNestedForeignInterfaceCall(
                        consumerCall.Arguments[argumentIndex])
                    is BoundCall candidate
                    && nestedInputCall is null
                    && candidate.Type
                        == consumerTarget.Variables[formal].Type
                    && IsValidNestedInputCall(candidate,
                        consumerTarget.Variables[formal].Type))
                {
                    nestedInputCall = candidate;
                }
                else
                {
                    return false;
                }

                seenParameters[formal] = true;
            }

            if (seenParameters.Any(static seen => !seen)
                || consumedOutputHomes.Count == 0
                || !hasPointerOutputUse
                || !reservedOutputHomes.SetEquals(
                    outputVariableByFormal.Values)
                || !consumedOutputHomes.All(outputHome =>
                    outputVariableByFormal.Any(pair =>
                        pair.Value == outputHome
                        && function.Variables[outputHome].Type
                            == outputContractsByFormal[pair.Key].PointeeType)))
            {
                return false;
            }

            consumedOutputVariable = reservedOutputVariables.First(
                outputHome =>
                consumedOutputHomes.Contains(outputHome)
                && IsForeignNativeInterfacePointerValueType(
                    function.Variables[outputHome].Type));
            return true;

            bool IsValidNestedInputCall(BoundCall nestedCall,
                PrimitiveType expectedType)
            {
                if (nestedCall.Function
                        != BoundCall.ForeignInterfaceFunction
                    || nestedCall.ForeignInterfaceCall is not
                        BoundForeignInterfaceCall nestedForeignCall
                    || !nestedCall.IsUnsafeAuthorized
                    || nestedForeignCall.DoesNotReturn
                    || !DirectNativeImportContract.IsScalar(expectedType)
                    || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                        nestedForeignCall, program, out var nestedTarget)
                    || nestedTarget.ReturnType != expectedType
                    || !DirectNativeImportContract.IsScalar(
                        nestedTarget.ReturnType)
                    || !nestedTarget.NativeOutputContracts.IsEmpty
                    || nestedTarget.ParameterCount
                        != nestedCall.Arguments.Length
                    || nestedCall.Arguments.IsDefault
                    || nestedForeignCall.Receiver is not BoundRead {
                        Variable: 0,
                        Type: PrimitiveType.NativeRawPointer,
                    })
                {
                    return false;
                }

                var nestedFormals = new bool[nestedTarget.ParameterCount];
                for (var nestedArgumentIndex = 0;
                    nestedArgumentIndex < nestedCall.Arguments.Length;
                    nestedArgumentIndex++)
                {
                    var nestedFormal
                        = nestedCall.ParameterIndex(nestedArgumentIndex);
                    var nestedArgument
                        = nestedCall.Arguments[nestedArgumentIndex];
                    if ((uint)nestedFormal
                            >= (uint)nestedTarget.ParameterCount
                        || nestedFormals[nestedFormal]
                        || nestedArgument is not BoundRead nestedRead
                        || nestedRead.Variable <= 0
                        || nestedRead.Variable >= fallbackLocal
                        || nestedRead.Type
                            != nestedTarget.Variables[nestedFormal].Type
                        || function.Variables[nestedRead.Variable].Type
                            != nestedRead.Type
                        || function.Variables[nestedRead.Variable].IsOut
                        || function.Variables[nestedRead.Variable].IsInit
                        || !(IsSupportedForeignNativeInterfaceValueType(
                                nestedRead.Type, program, schema)
                            || nestedRead.Type
                                == PrimitiveType.NativeRawPointer))
                    {
                        return false;
                    }

                    nestedFormals[nestedFormal] = true;
                    _ = inputHomeVariables.Add(nestedRead.Variable);
                }

                return nestedFormals.All(static seen => seen);
            }
        }

        bool IsValidNestedForeignInterfaceCall(BoundCall nestedCall,
            PrimitiveType expectedType)
        {
            if (nestedCall.ForeignInterfaceCall is not
                    BoundForeignInterfaceCall nestedForeignCall
                || !nestedCall.IsUnsafeAuthorized
                || nestedForeignCall.DoesNotReturn
                || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                    nestedForeignCall, program, out var nestedTarget)
                || nestedTarget.ReturnType != expectedType
                || !IsSupportedForeignNativeInterfaceValueType(
                    nestedTarget.ReturnType, program, schema)
                || !nestedTarget.NativeOutputContracts.IsEmpty
                || nestedTarget.ParameterCount
                    != nestedCall.Arguments.Length
                || nestedForeignCall.Receiver is not BoundRead receiver
                || receiver.Variable != 0
                || (uint)receiver.Variable >= (uint)fallbackLocal
                || receiver.Type != PrimitiveType.NativeRawPointer
                || function.Variables[receiver.Variable].Type
                    != receiver.Type
                || function.Variables[receiver.Variable].IsOut
                || function.Variables[receiver.Variable].IsInit)
            {
                return false;
            }
            if (receiver.Variable != 0)
            {
                _ = inputHomeVariables.Add(receiver.Variable);
            }

            var seenNestedParameters
                = new bool[nestedTarget.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < nestedCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = nestedCall.ParameterIndex(argumentIndex);
                var argument = nestedCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)nestedTarget.ParameterCount
                    || seenNestedParameters[formal]
                    || !(IsSupportedForeignNativeInterfaceValueType(
                            nestedTarget.Variables[formal].Type,
                            program, schema)
                        || nestedTarget.Variables[formal].Type
                            == PrimitiveType.NativeRawPointer))
                {
                    return false;
                }

                var formalType = nestedTarget.Variables[formal].Type;
                if (argument is BoundRead nestedRead)
                {
                    if ((uint)nestedRead.Variable >= (uint)fallbackLocal
                        || function.Variables[nestedRead.Variable].Type
                            != formalType
                        || nestedRead.Type != formalType
                        || function.Variables[nestedRead.Variable].IsOut
                        || function.Variables[nestedRead.Variable].IsInit)
                    {
                        return false;
                    }

                    if (nestedRead.Variable != 0)
                    {
                        _ = inputHomeVariables.Add(nestedRead.Variable);
                    }
                }
                else if (argument is BoundConstant nestedConstant
                    && nestedConstant.Type == formalType
                    && IsSupportedForeignNativeInterfaceConstant(
                        nestedConstant, formalType, program, schema))
                {
                    constantInputs.Add((nestedConstant.Type,
                        nestedConstant.Bits));
                    if (constantInputs.Count + nestedInputCalls.Count + 1
                            + scalarExpressionRegisterCount > 13)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                seenNestedParameters[formal] = true;
            }

            return seenNestedParameters.All(static seen => seen)
                && constantInputs.Count + nestedInputCalls.Count + 1
                    + scalarExpressionRegisterCount <= 13;
        }
    }

    private static BoundCall? GetNestedForeignInterfaceCall(
        BoundExpression expression)
    {
        var call = GetUnwrappedBoundCall(expression);
        return call is {
            Function: BoundCall.ForeignInterfaceFunction,
            ForeignInterfaceCall: not null,
        } ? call : null;
    }

    private static bool IsForeignNativeInterfacePointerValueType(
        PrimitiveType type)
        => type == PrimitiveType.NativeRawPointer
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type);

    private static BoundCall? GetUnwrappedBoundCall(
        BoundExpression expression)
    {
        while (expression is BoundUnsafeExpression unsafeCall)
        {
            expression = unsafeCall.Expression;
        }

        return expression as BoundCall;
    }

    private static bool TryGetForeignInterfaceScalarUnaryEncoding(
        BoundUnary unary, out byte opcode, out byte control)
    {
        opcode = 0;
        control = 0;
        if (unary.Operand.Type != unary.Type
            || !DirectNativeImportContract.IsScalar(unary.Type))
        {
            return false;
        }

        if ((PrimitiveTypeFacts.IsIntegerOrWide(unary.Type)
                && unary.Operator == TokenKind.Tilde)
            || (unary.Type == PrimitiveType.Bool
                && unary.Operator == TokenKind.Bang))
        {
            opcode = 0x33;
            return true;
        }

        if (unary.Operator == TokenKind.Plus)
        {
            return PrimitiveTypeFacts.IsIntegerOrWide(unary.Type)
                || PrimitiveTypeFacts.IsFloating(unary.Type);
        }

        if (unary.Operator != TokenKind.Minus)
        {
            return false;
        }

        if (PrimitiveTypeFacts.IsIntegerOrWide(unary.Type)
            && unary.OverflowBehavior
                == BoundIntegerOverflowBehavior.Wrapping)
        {
            opcode = 0x20;
            return true;
        }

        if (PrimitiveTypeFacts.IsIntegerOrWide(unary.Type)
            && unary.OverflowBehavior
                == BoundIntegerOverflowBehavior.Checked)
        {
            opcode = 0xC0;
            control = 0x04;
            return true;
        }

        if (PrimitiveTypeFacts.IsFloating(unary.Type)
            && unary.OverflowBehavior
                == BoundIntegerOverflowBehavior.Checked)
        {
            opcode = 0x26;
            return true;
        }

        return false;
    }

    private static bool TryGetForeignInterfaceScalarBinaryEncoding(
        BoundBinary binary, out byte opcode, out byte control)
    {
        opcode = 0;
        control = 0;
        if (!DirectNativeImportContract.IsScalar(binary.Type))
        {
            return false;
        }

        // The Body bitwise operations are equivalent only when a skipped
        // logical right operand has no effects or failure.
        if (binary.Type == PrimitiveType.Bool
            && binary.Left.Type == PrimitiveType.Bool
            && binary.Right.Type == PrimitiveType.Bool
            && (binary.Operator is TokenKind.EqualsEquals
                or TokenKind.BangEquals))
        {
            opcode = 0xC2;
            control = binary.Operator == TokenKind.EqualsEquals ? (byte)0x01
                : (byte)0x02;
            return true;
        }

        if (binary.Type == PrimitiveType.Bool
            && binary.Left.Type == binary.Right.Type
            && TryGetForeignInterfaceScalarComparisonPredicateMask(
                binary.Left.Type, binary.Operator, out _))
        {
            opcode = 0xC2;
            return true;
        }

        if (binary.Type == PrimitiveType.Bool
            && binary.Left.Type == PrimitiveType.Bool
            && binary.Right.Type == PrimitiveType.Bool
            && (binary.Operator is TokenKind.AmpersandAmpersand
                or TokenKind.PipePipe)
            && !ForeignInterfaceScalarInputExpressionMayThrow(
                binary.Right))
        {
            opcode = binary.Operator == TokenKind.AmpersandAmpersand
                ? (byte)0x30 : (byte)0x31;
            return true;
        }

        if (binary.Left.Type != binary.Type)
        {
            return false;
        }

        if (binary.Operator is TokenKind.ShiftLeft
                or TokenKind.ShiftRight
                or TokenKind.ShiftRightUnsigned)
        {
            if (!PrimitiveTypeFacts.IsIntegerOrWide(binary.Type)
                || !PrimitiveTypeFacts.IsIntegerOrWide(binary.Right.Type)
                || binary.OverflowBehavior
                    != BoundIntegerOverflowBehavior.Checked)
            {
                return false;
            }

            opcode = binary.Operator switch {
                TokenKind.ShiftLeft => 0x34,
                TokenKind.ShiftRight => 0x35,
                TokenKind.ShiftRightUnsigned => 0x36,
                _ => 0,
            };
            return opcode != 0;
        }

        if (binary.Right.Type != binary.Type)
        {
            return false;
        }

        if ((PrimitiveTypeFacts.IsIntegerOrWide(binary.Type)
                || binary.Type == PrimitiveType.Bool)
            && binary.Operator is TokenKind.Ampersand or TokenKind.Pipe
                or TokenKind.Caret)
        {
            opcode = binary.Operator switch {
                TokenKind.Ampersand => 0x30,
                TokenKind.Pipe => 0x31,
                TokenKind.Caret => 0x32,
                _ => 0,
            };
            return opcode != 0;
        }

        if (PrimitiveTypeFacts.IsIntegerOrWide(binary.Type)
            && binary.OverflowBehavior
                == BoundIntegerOverflowBehavior.Wrapping)
        {
            opcode = binary.Operator switch {
                TokenKind.Plus => 0x21,
                TokenKind.Minus => 0x22,
                TokenKind.Star => 0x23,
                _ => 0,
            };
            return opcode != 0;
        }

        if (PrimitiveTypeFacts.IsIntegerOrWide(binary.Type)
            && binary.OverflowBehavior
                == BoundIntegerOverflowBehavior.Checked)
        {
            control = binary.Operator switch {
                TokenKind.Plus => 0x01,
                TokenKind.Minus => 0x02,
                TokenKind.Star => 0x03,
                TokenKind.Slash => 0x0C,
                TokenKind.Percent => 0x0D,
                _ => 0,
            };
            if (control != 0)
            {
                opcode = 0xC0;
                return true;
            }
        }

        if (PrimitiveTypeFacts.IsFloating(binary.Type)
            && binary.OverflowBehavior
                == BoundIntegerOverflowBehavior.Checked)
        {
            opcode = binary.Operator switch {
                TokenKind.Plus => 0x27,
                TokenKind.Minus => 0x28,
                TokenKind.Star => 0x29,
                TokenKind.Slash => 0x2A,
                TokenKind.Percent => 0x2B,
                _ => 0,
            };
            return opcode != 0;
        }

        return false;
    }

    private static bool TryGetForeignInterfaceScalarUpdateEncoding(
        BoundUpdate update, out byte opcode, out byte control)
    {
        opcode = 0;
        control = 0;
        if (update.Place is not BoundRead place
            || place.Type != update.Type
            || !PrimitiveTypeFacts.IsIntegerOrWide(update.Type)
            || !DirectNativeImportContract.IsScalar(update.Type)
            || update.Operator is not (TokenKind.PlusPlus
                or TokenKind.MinusMinus))
        {
            return false;
        }

        var binary = new BoundBinary(
            update.Operator == TokenKind.PlusPlus
                ? TokenKind.Plus : TokenKind.Minus,
            place, new BoundConstant(1, update.Type, update.Span),
            update.Type, update.Span) {
            OverflowBehavior = update.OverflowBehavior,
        };
        return TryGetForeignInterfaceScalarBinaryEncoding(binary,
            out opcode, out control);
    }

    private static bool TryGetForeignInterfaceScalarUpdateFromBinary(
        BoundBinary binary, out BoundUpdate update)
    {
        update = null!;
        if (binary.Left is not BoundRead place
            || binary.Right is not BoundConstant constant
            || binary.Operator is not (TokenKind.Plus or TokenKind.Minus)
            || constant.Type != binary.Type
            || constant.RepresentationType != constant.Type
            || constant.Bits.Low != 1 || constant.Bits.High != 0)
        {
            return false;
        }

        update = new BoundUpdate(
            binary.Operator == TokenKind.Plus
                ? TokenKind.PlusPlus : TokenKind.MinusMinus,
            place, true, binary.Type, binary.Span) {
            OverflowBehavior = binary.OverflowBehavior,
        };
        return TryGetForeignInterfaceScalarUpdateEncoding(update,
            out _, out _);
    }

    private static bool TryGetForeignInterfaceScalarComparisonPredicateMask(
        PrimitiveType operandType, TokenKind operation, out byte predicateMask)
    {
        predicateMask = operation switch {
            TokenKind.Less => 0x01,
            TokenKind.EqualsEquals => 0x02,
            TokenKind.LessEquals => 0x03,
            TokenKind.Greater => 0x04,
            TokenKind.GreaterEquals => 0x06,
            TokenKind.BangEquals => 0x0D,
            _ => 0,
        };
        return predicateMask != 0
            && (PrimitiveTypeFacts.IsIntegerOrWide(operandType)
                || (PrimitiveTypeFacts.IsFloating(operandType)
                    && DirectNativeImportContract.IsScalar(operandType)));
    }

    private static bool TryGetForeignInterfaceScalarComparisonOperator(
        PrimitiveType operandType, byte predicateMask,
        out TokenKind operation)
    {
        operation = predicateMask switch {
            0x01 => TokenKind.Less,
            0x02 => TokenKind.EqualsEquals,
            0x03 => TokenKind.LessEquals,
            0x04 => TokenKind.Greater,
            0x05 or 0x0D => TokenKind.BangEquals,
            0x06 => TokenKind.GreaterEquals,
            _ => default,
        };
        return TryGetForeignInterfaceScalarComparisonPredicateMask(
            operandType, operation, out var expectedPredicateMask)
            && expectedPredicateMask == predicateMask;
    }

    private static bool TryAnalyzeForeignInterfaceScalarInputExpression(
        BoundExpression expression, PrimitiveType expectedType,
        BoundFunction function, int parameterLimit,
        HashSet<(PrimitiveType Type, ScalarBits Bits)> expressionConstantInputs,
        out int temporaryRegisterCount,
        HashSet<int>? stagedLocalVariables = null,
        IReadOnlyList<BoundVariable>? variableDefinitions = null)
    {
        temporaryRegisterCount = 0;
        if ((uint)parameterLimit > (uint)function.ParameterCount)
        {
            return false;
        }

        IReadOnlyList<BoundVariable> variableFacts
            = variableDefinitions ?? function.Variables;
        while (expression is BoundUnsafeExpression unsafeExpression)
        {
            expression = unsafeExpression.Expression;
        }

        if (expression is not (BoundRead or BoundUnary or BoundBinary
                or BoundCast or BoundUpdate)
            || !DirectNativeImportContract.IsScalar(expectedType))
        {
            return false;
        }

        var analysis = Visit(expression, expectedType);
        if (!analysis.IsValid || !analysis.OwnsRegister)
        {
            return false;
        }

        temporaryRegisterCount = analysis.RegisterCount;
        return true;

        (bool IsValid, int RegisterCount, bool OwnsRegister) Visit(
            BoundExpression candidate, PrimitiveType type)
        {
            while (candidate is BoundUnsafeExpression unsafeCandidate)
            {
                candidate = unsafeCandidate.Expression;
            }

            if (candidate is BoundRead read)
            {
                if (read.Type != type
                    || (uint)read.Variable
                        >= (uint)variableFacts.Count
                    || variableFacts[read.Variable].Type != type
                    || variableFacts[read.Variable].IsOut
                    || variableFacts[read.Variable].IsInit)
                {
                    return (false, 0, false);
                }

                if (read.Variable < parameterLimit
                    && read.Variable < 16)
                {
                    return (true, 1, true);
                }

                if (read.Variable >= function.ParameterCount
                    && stagedLocalVariables?.Contains(read.Variable) == true
                    && !variableFacts[read.Variable].IsInitializationLocal)
                {
                    return (true, 1, true);
                }

                return (false, 0, false);
            }

            if (candidate is BoundConstant constant)
            {
                if (constant.Type != type
                    || !DirectNativeImportContract.IsScalar(type)
                    || constant.RepresentationType != constant.Type
                    || !constant.Bits.IsCanonicalFor(constant.Type))
                {
                    return (false, 0, false);
                }

                _ = expressionConstantInputs.Add((constant.Type,
                    constant.Bits));
                return (true, 0, false);
            }

            if (candidate is BoundUpdate update)
            {
                if (update.Type != type
                    || update.Place is not BoundRead place
                    || place.Type != update.Type
                    || (uint)place.Variable
                        >= (uint)variableFacts.Count
                    || stagedLocalVariables is null
                    || !stagedLocalVariables.Contains(place.Variable)
                    || !variableFacts[place.Variable].IsMutable
                    || variableFacts[place.Variable].IsOut
                    || variableFacts[place.Variable].IsInit
                    || variableFacts[place.Variable].IsInitializationLocal
                    || variableFacts[place.Variable].Type != update.Type
                    || !TryGetForeignInterfaceScalarUpdateEncoding(update,
                        out _, out _))
                {
                    return (false, 0, false);
                }

                var one = new BoundConstant(1, update.Type, update.Span);
                _ = expressionConstantInputs.Add((one.Type, one.Bits));
                return (true, update.IsPrefix ? 1 : 2, true);
            }

            if (candidate is BoundCast cast)
            {
                var isTotalConversion
                    = TryGetGeneralPredicateConversion(cast, out _, out _);
                var isCheckedNumericConversion
                    = IsForeignInterfaceCheckedNumericConversion(cast);
                if (cast.Type != type
                    || (!isTotalConversion
                        && !isCheckedNumericConversion))
                {
                    return (false, 0, false);
                }

                var operand = Visit(cast.Operand, cast.Operand.Type);
                if (!operand.IsValid
                    || (isTotalConversion && !operand.OwnsRegister))
                {
                    return (false, 0, false);
                }

                return (true, checked(operand.RegisterCount
                    + (operand.OwnsRegister ? 0 : 1)), true);
            }

            if (candidate is BoundUnary unary)
            {
                if (unary.Type != type
                    || !TryGetForeignInterfaceScalarUnaryEncoding(unary,
                        out _, out _))
                {
                    return (false, 0, false);
                }

                var operand = Visit(unary.Operand, type);
                return operand.IsValid && operand.OwnsRegister
                    ? (true, operand.RegisterCount, true)
                    : (false, 0, false);
            }

            if (candidate is BoundBinary binary)
            {
                if (binary.Type != type
                    || !TryGetForeignInterfaceScalarBinaryEncoding(binary,
                        out _, out _))
                {
                    return (false, 0, false);
                }

                var relationComparison = binary.Type == PrimitiveType.Bool
                    && binary.Left.Type != PrimitiveType.Bool;
                var leftType = relationComparison
                    ? binary.Left.Type : type;
                var rightType = relationComparison
                    || (binary.Operator is TokenKind.ShiftLeft
                        or TokenKind.ShiftRight
                        or TokenKind.ShiftRightUnsigned)
                    ? binary.Right.Type : type;
                var left = Visit(binary.Left, leftType);
                var right = Visit(binary.Right, rightType);
                return left.IsValid && left.OwnsRegister && right.IsValid
                    ? (true, checked(left.RegisterCount
                        + right.RegisterCount), true)
                    : (false, 0, false);
            }

            return (false, 0, false);
        }
    }

    private static void CollectForeignInterfaceScalarInputConstants(
        BoundExpression expression, Action<BoundConstant> addConstant)
    {
        while (expression is BoundUnsafeExpression unsafeExpression)
        {
            expression = unsafeExpression.Expression;
        }

        if (expression is BoundConstant constant)
        {
            addConstant(constant);
        }
        else if (expression is BoundUnary unary)
        {
            CollectForeignInterfaceScalarInputConstants(unary.Operand,
                addConstant);
        }
        else if (expression is BoundCast cast)
        {
            CollectForeignInterfaceScalarInputConstants(cast.Operand,
                addConstant);
        }
        else if (expression is BoundUpdate update)
        {
            addConstant(new BoundConstant(1, update.Type, update.Span));
        }
        else if (expression is BoundBinary binary)
        {
            CollectForeignInterfaceScalarInputConstants(binary.Left,
                addConstant);
            CollectForeignInterfaceScalarInputConstants(binary.Right,
                addConstant);
        }
    }

    private static void CollectForeignInterfaceScalarInputHomeVariables(
        BoundExpression expression, HashSet<int> homeVariables)
    {
        while (expression is BoundUnsafeExpression unsafeExpression)
        {
            expression = unsafeExpression.Expression;
        }

        if (expression is BoundRead read)
        {
            _ = homeVariables.Add(read.Variable);
        }
        else if (expression is BoundUnary unary)
        {
            CollectForeignInterfaceScalarInputHomeVariables(unary.Operand,
                homeVariables);
        }
        else if (expression is BoundCast cast)
        {
            CollectForeignInterfaceScalarInputHomeVariables(cast.Operand,
                homeVariables);
        }
        else if (expression is BoundUpdate { Place: BoundRead read })
        {
            _ = homeVariables.Add(read.Variable);
        }
        else if (expression is BoundBinary binary)
        {
            CollectForeignInterfaceScalarInputHomeVariables(binary.Left,
                homeVariables);
            CollectForeignInterfaceScalarInputHomeVariables(binary.Right,
                homeVariables);
        }
    }

    private static void CollectForeignInterfaceScalarInputConversionTypes(
        BoundExpression expression, Action<PrimitiveType> addType)
    {
        while (expression is BoundUnsafeExpression unsafeExpression)
        {
            expression = unsafeExpression.Expression;
        }

        if (expression is BoundCast cast)
        {
            addType(cast.Type);
            CollectForeignInterfaceScalarInputConversionTypes(cast.Operand,
                addType);
        }
        else if (expression is BoundUnary unary)
        {
            CollectForeignInterfaceScalarInputConversionTypes(unary.Operand,
                addType);
        }
        else if (expression is BoundBinary binary)
        {
            CollectForeignInterfaceScalarInputConversionTypes(binary.Left,
                addType);
            CollectForeignInterfaceScalarInputConversionTypes(binary.Right,
                addType);
        }
    }

    private static bool IsSupportedForeignInterfaceScalarLocalInitializer(
        BoundExpression expression)
    {
        var pending = new Stack<(BoundExpression Expression, int Depth)>();
        pending.Push((expression, 0));
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var item))
        {
            if (item.Depth > SemanticVerifier.MaximumDepth
                || --remaining < 0
                || !item.Expression.HasValidNestedExpressions)
            {
                return false;
            }

            var depth = item.Depth + 1;
            switch (item.Expression)
            {
                case BoundRead or BoundConstant:
                    break;
                case BoundUnsafeExpression unsafeExpression:
                    pending.Push((unsafeExpression.Expression, depth));
                    break;
                case BoundUnary unary:
                    pending.Push((unary.Operand, depth));
                    break;
                case BoundBinary {
                    PatternObservation: null,
                    IsPatternComposition: false,
                    IsConstantPatternTest: false,
                } binary:
                    pending.Push((binary.Left, depth));
                    pending.Push((binary.Right, depth));
                    break;
                case BoundCast cast:
                    pending.Push((cast.Operand, depth));
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static uint[] GetStandardForeignInterfaceBodyTypeTokens(
        BoundFunction function, IEnumerable<BoundExpression> expressions,
        Func<PrimitiveType, int> typeSpecRowFor)
    {
        var typeTokens = function.Variables.Select(variable =>
            GetStandardBodyTypeMapToken(variable.Type, typeSpecRowFor))
            .ToList();
        var pending = new Stack<(BoundExpression Expression, int Depth)>();
        foreach (var expression in expressions)
        {
            if (expression.Type is not (PrimitiveType.Void
                or PrimitiveType.TypeIdentity))
            {
                typeTokens.Add(GetStandardBodyTypeMapToken(expression.Type,
                    typeSpecRowFor));
            }
            CollectForeignInterfaceScalarInputConversionTypes(expression,
                type => typeTokens.Add(
                    GetStandardBodyTypeMapToken(type, typeSpecRowFor)));
            pending.Push((expression, 0));
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        while (pending.TryPop(out var item))
        {
            if (item.Depth > SemanticVerifier.MaximumDepth
                || --remaining < 0
                || !item.Expression.HasValidNestedExpressions)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an invalid or oversized F+I input expression.");
            }

            var children = item.Expression.NestedExpressions;
            if (children.IsDefault)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an uninitialized F+I input expression.");
            }

            foreach (var child in children)
            {
                if (child.Type is not (PrimitiveType.Void
                    or PrimitiveType.TypeIdentity))
                {
                    typeTokens.Add(GetStandardBodyTypeMapToken(child.Type,
                        typeSpecRowFor));
                }
                pending.Push((child, item.Depth + 1));
            }
        }

        return [.. typeTokens];
    }

    private static bool ForeignInterfaceScalarInputExpressionMayThrow(
        BoundExpression expression)
    {
        while (expression is BoundUnsafeExpression unsafeExpression)
        {
            expression = unsafeExpression.Expression;
        }

        return expression switch {
            BoundCast cast =>
                !SemanticVerifier.IsTotalConversion(cast.Operand.Type,
                    cast.Type)
                || ForeignInterfaceScalarInputExpressionMayThrow(
                    cast.Operand),
            BoundUpdate update =>
                update.OverflowBehavior
                    == BoundIntegerOverflowBehavior.Checked
                && PrimitiveTypeFacts.IsIntegerOrWide(update.Type),
            BoundUnary unary =>
                (unary.Operator == TokenKind.Minus
                    && PrimitiveTypeFacts.IsIntegerOrWide(unary.Type)
                    && unary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked)
                || ForeignInterfaceScalarInputExpressionMayThrow(
                    unary.Operand),
            BoundBinary binary =>
                ((binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent
                    or TokenKind.ShiftLeft or TokenKind.ShiftRight
                    or TokenKind.ShiftRightUnsigned)
                    && PrimitiveTypeFacts.IsIntegerOrWide(binary.Type)
                    && binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked)
                || ForeignInterfaceScalarInputExpressionMayThrow(
                    binary.Left)
                || ForeignInterfaceScalarInputExpressionMayThrow(
                    binary.Right),
            _ => false,
        };
    }

    private static bool CanMixCheckedForeignInterfaceExpressionsWithNestedCalls(
        BoundCall call, BoundProgram program)
    {
        if (call.ForeignInterfaceCall is not
                BoundForeignNativeInterfaceCall foreignCall
            || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                foreignCall, program, out var target))
        {
            return false;
        }

        var outputFormals = target.NativeOutputContracts
            .Select(static output => output.FormalOrdinal)
            .ToHashSet();
        for (var argumentIndex = 0;
            argumentIndex < call.Arguments.Length;
            argumentIndex++)
        {
            var formal = call.ParameterIndex(argumentIndex);
            if ((uint)formal >= (uint)target.ParameterCount)
            {
                return false;
            }

            if (outputFormals.Contains(formal)
                || GetNestedForeignInterfaceCall(
                    call.Arguments[argumentIndex]) is not null)
            {
                continue;
            }

            if (!DirectNativeImportContract.IsScalar(
                    target.Variables[formal].Type))
            {
                return false;
            }
        }

        return true;
    }

    private static int EmitForeignInterfaceScalarInputExpression(
        BoundFunction function, BoundExpression expression,
        PrimitiveType expectedType,
        Func<int> allocateRegister,
        Func<BoundConstant, byte> getConstantRegister,
        Func<PrimitiveType, int> getTypeMapIndex, List<byte> code,
        HashSet<int>? stagedLocalVariables = null)
    {
        var result = Emit(expression, expectedType);
        if (!result.OwnsRegister)
        {
            throw Invalid(
                "A Standard F+I scalar expression has no result register.");
        }

        return result.Register;

        (int Register, bool OwnsRegister) Emit(
            BoundExpression candidate, PrimitiveType type)
        {
            while (candidate is BoundUnsafeExpression unsafeExpression)
            {
                candidate = unsafeExpression.Expression;
            }

            if (candidate is BoundRead read)
            {
                if (read.Type != type
                    || (uint)read.Variable
                        >= (uint)function.Variables.Length
                    || function.Variables[read.Variable].Type != type
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit)
                {
                    throw Unsupported(
                        "A Standard F+I scalar expression reads an unsupported Home.");
                }

                var register = allocateRegister();
                if (read.Variable < function.ParameterCount)
                {
                    if (read.Variable >= 16)
                    {
                        throw Unsupported(
                            "A Standard F+I scalar expression reads a parameter outside the formal-load range.");
                    }

                    code.Add(0x11);
                    code.Add(checked((byte)((read.Variable << 4)
                        | register)));
                }
                else if (stagedLocalVariables?.Contains(read.Variable)
                    == true
                    && !function.Variables[read.Variable]
                        .IsInitializationLocal
                    && (uint)read.Variable <= ushort.MaxValue)
                {
                    code.Add(0xD5);
                    code.Add(checked((byte)register));
                    code.Add(checked((byte)read.Variable));
                    code.Add(checked((byte)(read.Variable >> 8)));
                }
                else
                {
                    throw Unsupported(
                        "A Standard F+I scalar expression reads an unstaged local Home.");
                }

                return (register, true);
            }

            if (candidate is BoundConstant constant)
            {
                if (constant.Type != type
                    || !DirectNativeImportContract.IsScalar(type)
                    || constant.RepresentationType != constant.Type
                    || !constant.Bits.IsCanonicalFor(constant.Type))
                {
                    throw Unsupported(
                        "A Standard F+I scalar expression contains an unsupported ConstantMap value.");
                }

                return (getConstantRegister(constant), false);
            }

            if (candidate is BoundUpdate update
                && update.Type == type
                && update.Place is BoundRead place
                && (uint)place.Variable
                    < (uint)function.Variables.Length
                && stagedLocalVariables?.Contains(place.Variable) == true
                && function.Variables[place.Variable].IsMutable
                && !function.Variables[place.Variable].IsOut
                && !function.Variables[place.Variable].IsInit
                && !function.Variables[place.Variable]
                    .IsInitializationLocal
                && function.Variables[place.Variable].Type == update.Type
                && DirectNativeImportContract.IsScalar(update.Type)
                && TryGetForeignInterfaceScalarUpdateEncoding(update,
                    out var updateOpcode, out var updateControl))
            {
                if ((uint)place.Variable > ushort.MaxValue)
                {
                    throw Unsupported(
                        "A Standard F+I scalar update exceeds the Home index range.");
                }

                var previousRegister = allocateRegister();
                code.Add(0xD5);
                code.Add(checked((byte)previousRegister));
                code.Add(checked((byte)place.Variable));
                code.Add(checked((byte)(place.Variable >> 8)));
                var resultRegister = previousRegister;
                if (!update.IsPrefix)
                {
                    resultRegister = allocateRegister();
                    code.Add(0x10);
                    code.Add(checked((byte)((previousRegister << 4)
                        | resultRegister)));
                }

                var oneRegister = getConstantRegister(
                    new BoundConstant(1, update.Type, update.Span));
                if (updateOpcode == 0xC0)
                {
                    var payload = checked((ushort)(
                        resultRegister | (0x0F << 4)
                        | (resultRegister << 8)
                        | (oneRegister << 12)));
                    code.Add(updateOpcode);
                    code.Add(updateControl);
                    code.Add((byte)payload);
                    code.Add((byte)(payload >> 8));
                }
                else if (updateControl == 0
                    && updateOpcode is 0x21 or 0x22)
                {
                    code.Add(updateOpcode);
                    code.Add(checked((byte)((oneRegister << 4)
                        | resultRegister)));
                }
                else
                {
                    throw Invalid(
                        "A Standard F+I scalar update has no selected Body operation.");
                }

                code.Add(0xD6);
                code.Add(checked((byte)(0x10 | resultRegister)));
                code.Add(checked((byte)place.Variable));
                code.Add(checked((byte)(place.Variable >> 8)));
                return (update.IsPrefix ? resultRegister
                    : previousRegister, true);
            }

            if (candidate is BoundCast cast
                && cast.Type == type
                && TryGetGeneralPredicateConversion(cast, out _, out _))
            {
                var operand = Emit(cast.Operand, cast.Operand.Type);
                if (!operand.OwnsRegister)
                {
                    throw Invalid(
                        "A Standard F+I total conversion would overwrite a ConstantMap register.");
                }

                var typeMapIndex = getTypeMapIndex(cast.Type);
                if (typeMapIndex > 15)
                {
                    throw Unsupported(
                        "A Standard F+I total conversion exceeds the short TypeMap index range.");
                }

                code.Add(0x50);
                code.Add(checked((byte)((typeMapIndex << 4)
                    | operand.Register)));
                return operand;
            }

            if (candidate is BoundCast checkedCast
                && checkedCast.Type == type
                && IsForeignInterfaceCheckedNumericConversion(checkedCast))
            {
                var operand = Emit(checkedCast.Operand,
                    checkedCast.Operand.Type);
                var typeMapIndex = getTypeMapIndex(checkedCast.Type);
                if (typeMapIndex is < 0 or > ushort.MaxValue)
                {
                    throw Unsupported(
                        "A Standard F+I checked numeric conversion exceeds the TypeMap index range.");
                }

                var destinationRegister = operand.OwnsRegister
                    ? operand.Register : allocateRegister();
                code.Add(0xD0);
                code.Add(checked((byte)(operand.Register
                    | (destinationRegister << 4))));
                code.Add((byte)typeMapIndex);
                code.Add((byte)(typeMapIndex >> 8));
                return (destinationRegister, true);
            }

            if (candidate is BoundUnary unary
                && unary.Type == type
                && TryGetForeignInterfaceScalarUnaryEncoding(unary,
                    out var unaryOpcode, out var unaryControl))
            {
                var operand = Emit(unary.Operand, type);
                if (unary.Operator == TokenKind.Plus)
                {
                    return operand;
                }

                if (!operand.OwnsRegister)
                {
                    throw Invalid(
                        "A Standard F+I unary operation would overwrite a ConstantMap register.");
                }

                if (unaryOpcode == 0xC0)
                {
                    var payload = checked((ushort)(
                        operand.Register | (0x0F << 4)
                        | (operand.Register << 8) | (0x0F << 12)));
                    code.Add(unaryOpcode);
                    code.Add(unaryControl);
                    code.Add((byte)payload);
                    code.Add((byte)(payload >> 8));
                }
                else
                {
                    code.Add(unaryOpcode);
                    code.Add(checked((byte)((operand.Register << 4)
                        | operand.Register)));
                }
                return operand;
            }

            if (candidate is BoundBinary binary
                && binary.Type == type
                && TryGetForeignInterfaceScalarBinaryEncoding(binary,
                    out var binaryOpcode, out var binaryControl))
            {
                var relationComparison = binaryOpcode == 0xC2
                    && binaryControl == 0;
                var leftType = relationComparison
                    ? binary.Left.Type : type;
                var rightType = relationComparison
                    || (binary.Operator is TokenKind.ShiftLeft
                        or TokenKind.ShiftRight
                        or TokenKind.ShiftRightUnsigned)
                    ? binary.Right.Type : type;
                var left = Emit(binary.Left, leftType);
                var right = Emit(binary.Right, rightType);
                if (!left.OwnsRegister)
                {
                    throw Invalid(
                        "A Standard F+I binary operation would overwrite a ConstantMap register.");
                }

                if (relationComparison)
                {
                    if (!TryGetForeignInterfaceScalarComparisonPredicateMask(
                            binary.Left.Type, binary.Operator,
                            out var predicateMask))
                    {
                        throw Invalid(
                            "A Standard F+I relation comparison has no supported predicate mask.");
                    }

                    var comparisonPayload = checked((ushort)(
                        left.Register | (0x0F << 4)
                        | (left.Register << 8)
                        | (right.Register << 12)));
                    code.Add(0xC2);
                    code.Add(0);
                    code.Add((byte)comparisonPayload);
                    code.Add((byte)(comparisonPayload >> 8));

                    var conditionPayload = checked((ushort)(
                        left.Register | (left.Register << 4)
                        | (0x0F << 8) | (0x0F << 12)));
                    code.Add(0xC3);
                    code.Add(predicateMask);
                    code.Add((byte)conditionPayload);
                    code.Add((byte)(conditionPayload >> 8));
                }
                else if (binaryOpcode is 0xC0 or 0xC2)
                {
                    var payload = checked((ushort)(
                        left.Register | (0x0F << 4)
                        | (left.Register << 8)
                        | (right.Register << 12)));
                    code.Add(binaryOpcode);
                    code.Add(binaryControl);
                    code.Add((byte)payload);
                    code.Add((byte)(payload >> 8));
                }
                else
                {
                    code.Add(binaryOpcode);
                    code.Add(checked((byte)((right.Register << 4)
                        | left.Register)));
                }
                return left;
            }

            throw Unsupported(
                "A Standard F+I scalar expression is outside the supported input-expression subset.");
        }
    }

    private static bool TryGetForeignInterfaceDirectReturnCall(
        BoundFunction function, BoundProgram program, out BoundCall call,
        out ImmutableArray<BoundLocal> stagedLocals)
    {
        call = null!;
        stagedLocals = [];
        if (function.IsExternal
            || function.Variables.Length < function.ParameterCount
            || function.Body.RetiredManagedLocals.Length != 0
            || function.Variables.Take(function.ParameterCount).Any(
                static variable =>
                variable.IsOut || variable.IsInit
                || variable.IsInitializationLocal
                || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary)
            || function.Variables.Skip(function.ParameterCount).Any(
                static variable =>
                    variable.IsOut || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary))
        {
            return false;
        }

        var statements = function.Body.Statements;
        if (statements.Length == 1
            && statements[0] is BoundUnsafe unsafeBlock)
        {
            if (unsafeBlock.Body.RetiredManagedLocals.Length != 0)
            {
                return false;
            }
            statements = unsafeBlock.Body.Statements;
        }
        else if (statements.Length != 0
            && statements[^1] is BoundUnsafe unsafeReturn)
        {
            if (unsafeReturn.Body.RetiredManagedLocals.Length != 0
                || unsafeReturn.Body.Statements.IsEmpty)
            {
                return false;
            }

            var statementBuilder
                = ImmutableArray.CreateBuilder<BoundStatement>(
                    statements.Length - 1
                    + unsafeReturn.Body.Statements.Length);
            for (var index = 0; index < statements.Length - 1; index++)
            {
                statementBuilder.Add(statements[index]);
            }
            statementBuilder.AddRange(unsafeReturn.Body.Statements);
            statements = statementBuilder.ToImmutable();
        }

        var vacantLocals = new HashSet<int>();
        var stagedLocalBuilder
            = ImmutableArray.CreateBuilder<BoundLocal>();
        var stagedLocalVariables = new HashSet<int>();
        var stagedLocalInitializerConstants
            = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
        var stagedLocalInitializerRegisterCount = 0;

        bool TryAddStagedLocal(BoundStatement statement)
        {
            if (statement is not BoundLocal local
                || local.Variable < function.ParameterCount
                || (uint)local.Variable
                    >= (uint)function.Variables.Length
                || function.Variables[local.Variable].IsOut
                || function.Variables[local.Variable].IsInit
                || function.Variables[local.Variable]
                    .IsInitializationLocal
                || local.Initializer.Type
                    != function.Variables[local.Variable].Type
                || !DirectNativeImportContract.IsScalar(
                    local.Initializer.Type)
                || stagedLocalVariables.Contains(local.Variable))
            {
                return false;
            }

            var isDirectParameterRead
                = local.Initializer is BoundRead parameterRead
                && (uint)parameterRead.Variable
                    < (uint)function.ParameterCount
                && parameterRead.Variable < 16
                && parameterRead.Type
                    == function.Variables[parameterRead.Variable].Type
                && !function.Variables[parameterRead.Variable].IsOut
                && !function.Variables[parameterRead.Variable].IsInit;
            if (!isDirectParameterRead)
            {
                if (local.Initializer is BoundConstant constant
                    && constant.Type == local.Initializer.Type
                    && IsSupportedForeignNativeInterfaceConstant(
                        constant, local.Initializer.Type, program, schema))
                {
                    _ = stagedLocalInitializerConstants.Add(
                        (constant.Type, constant.Bits));
                }
                else if (IsSupportedForeignInterfaceScalarLocalInitializer(
                            local.Initializer))
                {
                    var expressionConstants
                        = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
                    if (!TryAnalyzeForeignInterfaceScalarInputExpression(
                            local.Initializer, local.Initializer.Type,
                            function, function.ParameterCount,
                            expressionConstants,
                            out var expressionRegisterCount,
                            stagedLocalVariables))
                    {
                        return false;
                    }

                    stagedLocalInitializerRegisterCount = Math.Max(
                        stagedLocalInitializerRegisterCount,
                        expressionRegisterCount);
                    stagedLocalInitializerConstants.UnionWith(
                        expressionConstants);
                }
                else
                {
                    return false;
                }
            }

            if (!stagedLocalVariables.Add(local.Variable))
            {
                return false;
            }

            stagedLocalBuilder.Add(local);
            return true;
        }

        BoundExpression? callExpression;
        var terminalExpressionCall = statements.Length != 0
            && statements[^1] is BoundExpressionStatement;
        var prefixStatementCount = 0;
        if (terminalExpressionCall)
        {
            callExpression
                = ((BoundExpressionStatement)statements[^1]).Expression;
            prefixStatementCount = statements.Length - 1;
        }
        else if (function.ReturnType == PrimitiveType.Void)
        {
            if (statements.Length < 2
                || statements[^2] is not
                    BoundExpressionStatement callStatement
                || statements[^1] is not BoundReturn { Value: null })
            {
                return false;
            }

            callExpression = callStatement.Expression;
            prefixStatementCount = statements.Length - 2;
        }
        else
        {
            if (statements.IsEmpty
                || statements[^1] is not BoundReturn returned)
            {
                return false;
            }

            callExpression = returned.Value;
            prefixStatementCount = statements.Length - 1;
        }

        for (var index = 0; index < prefixStatementCount; index++)
        {
            if (statements[index] is BoundVacantLocal vacant)
            {
                if (function.ReturnType == PrimitiveType.Void
                    && !terminalExpressionCall)
                {
                    return false;
                }

                if (vacant.Variable < function.ParameterCount
                    || (uint)vacant.Variable
                        >= (uint)function.Variables.Length
                    || !function.Variables[vacant.Variable]
                        .IsInitializationLocal
                    || !vacantLocals.Add(vacant.Variable))
                {
                    return false;
                }
            }
            else if (!TryAddStagedLocal(statements[index]))
            {
                return false;
            }
        }

        if (callExpression is BoundUnsafeExpression unsafeExpression)
        {
            callExpression = unsafeExpression.Expression;
        }

        if (callExpression is not BoundCall boundCall
            || boundCall.Function != BoundCall.ForeignInterfaceFunction
            || boundCall.ForeignInterfaceCall is not
                BoundForeignInterfaceCall foreignCall
            || !boundCall.IsUnsafeAuthorized
            || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                foreignCall, program, out var target)
            || target.ReturnType != boundCall.Type
            || terminalExpressionCall != foreignCall.DoesNotReturn
            || (terminalExpressionCall && !function.DoesNotReturn)
            || (!terminalExpressionCall
                && target.ReturnType != function.ReturnType)
            || target.ParameterCount != boundCall.Arguments.Length
            || boundCall.Arguments.IsDefault
            || foreignCall.Receiver is not BoundRead receiver
            || (uint)receiver.Variable >= (uint)function.ParameterCount
            || function.Variables[receiver.Variable].Type
                != receiver.Type
            || function.Variables[receiver.Variable].IsOut
            || function.Variables[receiver.Variable].IsInit)
        {
            return false;
        }

        var outputContractsByFormal = target.NativeOutputContracts
            .ToDictionary(static output => output.FormalOrdinal);
        if (function.ReturnType == PrimitiveType.Void
            && outputContractsByFormal.Count != 0)
        {
            return false;
        }

        var outputVariables = new HashSet<int>();
        var seenParameters = new bool[target.ParameterCount];
        var nestedForeignInterfaceCalls = new List<BoundCall>();
        var constantInputs
            = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
        var scalarExpressionRegisterCount = 0;
        var hasThrowingScalarExpression = false;
        var availableInputRegisters
            = 13 - (stagedLocalVariables.Count == 0 ? 0 : 1);
        constantInputs.UnionWith(stagedLocalInitializerConstants);

        if (constantInputs.Count
                + stagedLocalInitializerRegisterCount
            > availableInputRegisters)
        {
            return false;
        }

        for (var sourceArgument = 0;
            sourceArgument < boundCall.Arguments.Length;
            sourceArgument++)
        {
            var formal = boundCall.ParameterIndex(sourceArgument);
            if ((uint)formal >= (uint)target.ParameterCount
                || seenParameters[formal])
            {
                return false;
            }

            var argument = boundCall.Arguments[sourceArgument];
            if (outputContractsByFormal.TryGetValue(formal,
                    out var output))
            {
                if (argument is not BoundNativeOutputReservation reservation
                    || reservation.PointeeType != output.PointeeType
                    || (uint)reservation.Variable
                        < (uint)function.ParameterCount
                    || (uint)reservation.Variable
                        >= (uint)function.Variables.Length
                    || function.Variables[reservation.Variable].Type
                        != output.PointeeType
                    || !function.Variables[reservation.Variable]
                        .IsInitializationLocal
                    || !outputVariables.Add(reservation.Variable))
                {
                    return false;
                }
            }
            else if (argument is BoundRead read)
            {
                if (!IsAvailableStagedInput(read.Variable,
                        function.ParameterCount, function.Variables.Length,
                        stagedLocalVariables)
                    || function.Variables[read.Variable].Type
                        != target.Variables[formal].Type
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit)
                {
                    return false;
                }

                if (stagedLocalVariables.Contains(read.Variable)
                    && DirectNativeImportContract.IsScalar(read.Type))
                {
                    var expressionConstants
                        = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
                    if (!TryAnalyzeForeignInterfaceScalarInputExpression(
                            read, target.Variables[formal].Type, function,
                            function.ParameterCount, expressionConstants,
                            out var expressionRegisterCount,
                            stagedLocalVariables))
                    {
                        return false;
                    }

                    scalarExpressionRegisterCount = checked(
                        scalarExpressionRegisterCount
                        + expressionRegisterCount);
                    constantInputs.UnionWith(expressionConstants);
                    if (constantInputs.Count
                            + nestedForeignInterfaceCalls.Count
                            + scalarExpressionRegisterCount
                        > availableInputRegisters)
                    {
                        return false;
                    }
                }
            }
            else if (argument is BoundConstant constant
                && DirectNativeImportContract.IsScalar(constant.Type)
                && constant.Type == target.Variables[formal].Type
                && constant.RepresentationType == constant.Type
                && constant.Bits.IsCanonicalFor(constant.Type))
            {
                constantInputs.Add((constant.Type, constant.Bits));
                if (constantInputs.Count
                        + nestedForeignInterfaceCalls.Count
                        + scalarExpressionRegisterCount
                    > availableInputRegisters)
                {
                    return false;
                }
            }
            else if (argument is BoundUnary or BoundBinary or BoundCast
                or BoundUpdate or BoundUnsafeExpression)
            {
                var expressionConstants
                    = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
                if (!TryAnalyzeForeignInterfaceScalarInputExpression(
                        argument, target.Variables[formal].Type, function,
                        function.ParameterCount, expressionConstants,
                        out var expressionRegisterCount,
                        stagedLocalVariables))
                {
                    return false;
                }

                scalarExpressionRegisterCount = checked(
                    scalarExpressionRegisterCount
                    + expressionRegisterCount);
                hasThrowingScalarExpression |=
                    ForeignInterfaceScalarInputExpressionMayThrow(argument);
                constantInputs.UnionWith(expressionConstants);
                if (constantInputs.Count
                        + nestedForeignInterfaceCalls.Count
                        + scalarExpressionRegisterCount
                    > availableInputRegisters)
                {
                    return false;
                }
            }
            else if (nestedForeignInterfaceCalls.Count
                    + constantInputs.Count < availableInputRegisters
                && GetNestedForeignInterfaceCall(argument)
                    is BoundCall nestedCall
                && !nestedForeignInterfaceCalls.Any(candidate =>
                    ReferenceEquals(candidate, nestedCall))
                && nestedCall.Type == target.Variables[formal].Type
                && IsValidNestedForeignInterfaceCall(nestedCall,
                    nestedCall.Type))
            {
                nestedForeignInterfaceCalls.Add(nestedCall);
            }
            else
            {
                return false;
            }

            seenParameters[formal] = true;
        }

        if (seenParameters.Any(static seen => !seen)
            || outputVariables.Count != target.NativeOutputContracts.Length
            || !outputVariables.SetEquals(vacantLocals)
            || !stagedLocalVariables.SetEquals(
                stagedLocalBuilder.Select(static local => local.Variable))
            || constantInputs.Count
                + nestedForeignInterfaceCalls.Count
                + Math.Max(stagedLocalInitializerRegisterCount,
                    scalarExpressionRegisterCount)
                > availableInputRegisters
            || (hasThrowingScalarExpression
                && nestedForeignInterfaceCalls.Count != 0
                && !CanMixCheckedForeignInterfaceExpressionsWithNestedCalls(
                    boundCall, program))
            || function.Variables.Length != function.ParameterCount
                + outputVariables.Count + stagedLocalVariables.Count)
        {
            return false;
        }

        call = boundCall;
        stagedLocals = stagedLocalBuilder.ToImmutable();
        return true;

        bool IsValidNestedForeignInterfaceCall(BoundCall nestedCall,
            PrimitiveType expectedType)
        {
            if (nestedCall.ForeignInterfaceCall is not
                    BoundForeignInterfaceCall nestedForeignCall
                || !nestedCall.IsUnsafeAuthorized
                || nestedForeignCall.DoesNotReturn
                || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                    nestedForeignCall, program, out var nestedTarget)
                || nestedTarget.ReturnType != expectedType
                || !DirectNativeImportContract.IsScalar(
                    nestedTarget.ReturnType)
                || !nestedTarget.NativeOutputContracts.IsEmpty
                || nestedTarget.ParameterCount
                    != nestedCall.Arguments.Length
                || nestedForeignCall.Receiver is not BoundRead receiver
                || !IsAvailableStagedInput(receiver.Variable,
                    function.ParameterCount, function.Variables.Length,
                    stagedLocalVariables)
                || receiver.Type
                    != function.Variables[receiver.Variable].Type
                || receiver.Type != PrimitiveType.NativeRawPointer
                || function.Variables[receiver.Variable].IsOut
                || function.Variables[receiver.Variable].IsInit)
            {
                return false;
            }

            var seenNestedParameters
                = new bool[nestedTarget.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < nestedCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = nestedCall.ParameterIndex(argumentIndex);
                var argument = nestedCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)nestedTarget.ParameterCount
                    || seenNestedParameters[formal]
                    || !(DirectNativeImportContract.IsScalar(
                            nestedTarget.Variables[formal].Type)
                        || nestedTarget.Variables[formal].Type
                            == PrimitiveType.NativeRawPointer))
                {
                    return false;
                }

                var formalType = nestedTarget.Variables[formal].Type;
                if (argument is BoundRead nestedRead)
                {
                    if (!IsAvailableStagedInput(nestedRead.Variable,
                            function.ParameterCount,
                            function.Variables.Length,
                            stagedLocalVariables)
                        || function.Variables[nestedRead.Variable].Type
                            != formalType
                        || nestedRead.Type != formalType
                        || function.Variables[nestedRead.Variable].IsOut
                        || function.Variables[nestedRead.Variable].IsInit)
                    {
                        return false;
                    }
                }
                else if (argument is BoundConstant nestedConstant
                    && DirectNativeImportContract.IsScalar(
                        nestedConstant.Type)
                    && nestedConstant.Type == formalType
                    && nestedConstant.RepresentationType
                        == nestedConstant.Type
                    && nestedConstant.Bits.IsCanonicalFor(
                        nestedConstant.Type))
                {
                    constantInputs.Add((nestedConstant.Type,
                        nestedConstant.Bits));
                    if (constantInputs.Count
                            + nestedForeignInterfaceCalls.Count + 1
                        > availableInputRegisters)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                seenNestedParameters[formal] = true;
            }

            return seenNestedParameters.All(static seen => seen)
                && constantInputs.Count
                    + nestedForeignInterfaceCalls.Count + 1
                    + scalarExpressionRegisterCount
                    <= availableInputRegisters;
        }

        static bool IsAvailableStagedInput(int variable,
            int parameterCount, int variableCount,
            HashSet<int> stagedLocalVariables)
            => (uint)variable < (uint)variableCount
                && ((uint)variable < (uint)parameterCount
                    || stagedLocalVariables.Contains(variable));
    }

    private static bool TryGetForeignInterfaceCallSequence(
        BoundFunction function, BoundProgram program,
        out ImmutableArray<BoundCall> calls, out bool hasValueResult,
        out ImmutableArray<BoundCall> nestedInputs, out bool isTerminal)
    {
        calls = [];
        hasValueResult = false;
        nestedInputs = [];
        isTerminal = false;
        if (function.IsExternal
            || function.Body.RetiredManagedLocals.Length != 0
            || function.Variables.Length != function.ParameterCount
            || function.Variables.Any(static variable =>
                variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary
                || variable.IsInitializationLocal))
        {
            return false;
        }

        var statements = function.Body.Statements;
        if (statements.Length == 1
            && statements[0] is BoundUnsafe unsafeBlock)
        {
            if (unsafeBlock.Body.RetiredManagedLocals.Length != 0)
            {
                return false;
            }

            statements = unsafeBlock.Body.Statements;
        }
        else if (statements.Length != 0
            && statements[^1] is BoundUnsafe unsafeBody)
        {
            if (unsafeBody.Body.RetiredManagedLocals.Length != 0
                || unsafeBody.Body.Statements.IsEmpty)
            {
                return false;
            }

            var statementBuilder
                = ImmutableArray.CreateBuilder<BoundStatement>(
                    statements.Length - 1
                    + unsafeBody.Body.Statements.Length);
            for (var index = 0; index < statements.Length - 1; index++)
            {
                statementBuilder.Add(statements[index]);
            }

            statementBuilder.AddRange(unsafeBody.Body.Statements);
            statements = statementBuilder.ToImmutable();
        }

        var terminal = function.DoesNotReturn;
        if (terminal)
        {
            if (statements.Length < 2
                || statements[^1] is not BoundExpressionStatement)
            {
                return false;
            }
        }
        else if (statements.Length < (function.ReturnType == PrimitiveType.Void
                    ? 3 : 2)
            || statements[^1] is not BoundReturn returned
            || (function.ReturnType == PrimitiveType.Void)
                != (returned.Value is null))
        {
            return false;
        }

        var callBuilder = ImmutableArray.CreateBuilder<BoundCall>(
            terminal || function.ReturnType != PrimitiveType.Void
                ? statements.Length : statements.Length - 1);
        var nestedInputBuilder = ImmutableArray.CreateBuilder<BoundCall>();
        var constantInputs
            = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
        var scalarExpressionRegisterCount = 0;
        for (var statementIndex = 0;
            statementIndex < statements.Length - 1;
            statementIndex++)
        {
            if (statements[statementIndex]
                    is not BoundExpressionStatement expressionStatement)
            {
                return false;
            }

            var expression = expressionStatement.Expression;
            while (expression is BoundUnsafeExpression unsafeCall)
            {
                expression = unsafeCall.Expression;
            }

            if (expression is not BoundCall boundCall
                || !TryValidateCall(boundCall, PrimitiveType.Void,
                    allowNestedInputs: false, isTerminalCall: false))
            {
                return false;
            }

            callBuilder.Add(boundCall);
        }

        if (terminal)
        {
            var terminalExpression
                = ((BoundExpressionStatement)statements[^1]).Expression;
            while (terminalExpression is BoundUnsafeExpression unsafeTerminal)
            {
                terminalExpression = unsafeTerminal.Expression;
            }

            if (terminalExpression is not BoundCall terminalCall
                || !TryValidateCall(terminalCall, terminalCall.Type,
                    allowNestedInputs: true, isTerminalCall: true))
            {
                return false;
            }

            callBuilder.Add(terminalCall);
        }
        else if (statements[^1] is BoundReturn {
                Value: BoundExpression returnExpression,
            })
        {
            while (returnExpression is BoundUnsafeExpression unsafeReturn)
            {
                returnExpression = unsafeReturn.Expression;
            }

            if (returnExpression is not BoundCall returnCall
                || !TryValidateCall(returnCall, function.ReturnType,
                    allowNestedInputs: true, isTerminalCall: false))
            {
                return false;
            }

            callBuilder.Add(returnCall);
        }

        if (callBuilder.Count is < 2 or > 13
            || callBuilder.Count + nestedInputBuilder.Count > 13
            || constantInputs.Count + nestedInputBuilder.Count
                + scalarExpressionRegisterCount > 13)
        {
            return false;
        }

        calls = callBuilder.MoveToImmutable();
        hasValueResult = !terminal
            && function.ReturnType != PrimitiveType.Void;
        nestedInputs = nestedInputBuilder.ToImmutable();
        isTerminal = terminal;
        return true;

        bool TryValidateCall(BoundCall boundCall,
            PrimitiveType expectedReturnType, bool allowNestedInputs,
            bool isTerminalCall)
        {
            if (boundCall.ForeignInterfaceCall is not
                    BoundForeignInterfaceCall foreignCall)
            {
                return expectedReturnType == PrimitiveType.Void
                    && !allowNestedInputs
                    && !isTerminalCall
                    && (function.ReturnType != PrimitiveType.Void
                        || function.DoesNotReturn)
                    && TryValidateLocalVoidCall(boundCall);
            }

            if (boundCall.Function != BoundCall.ForeignInterfaceFunction
                || !boundCall.IsUnsafeAuthorized
                || foreignCall.DoesNotReturn != isTerminalCall
                || boundCall.Type != expectedReturnType
                || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                    foreignCall, program, out var target)
                || target.ReturnType != expectedReturnType
                || !target.NativeOutputContracts.IsEmpty
                || target.ParameterCount != boundCall.Arguments.Length
                || boundCall.Arguments.IsDefault
                || foreignCall.Receiver is not BoundRead receiver
                || (uint)receiver.Variable >= (uint)function.ParameterCount
                || receiver.Type != PrimitiveType.NativeRawPointer
                || function.Variables[receiver.Variable].Type
                    != PrimitiveType.NativeRawPointer)
            {
                return false;
            }

            var seenParameters = new bool[target.ParameterCount];
            var nestedInputFormals = new HashSet<int>();
            var hasThrowingScalarExpression = false;
            for (var argumentIndex = 0;
                argumentIndex < boundCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = boundCall.ParameterIndex(argumentIndex);
                var argument = boundCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)target.ParameterCount
                    || seenParameters[formal])
                {
                    return false;
                }

                var formalType = target.Variables[formal].Type;
                if (argument is BoundRead read)
                {
                    if ((uint)read.Variable >= (uint)function.ParameterCount
                        || read.Type != formalType
                        || function.Variables[read.Variable].Type
                            != formalType)
                    {
                        return false;
                    }
                }
                else if (argument is BoundConstant constant
                    && constant.Type == formalType
                    && IsSupportedForeignNativeInterfaceConstant(constant,
                        formalType, program, schema))
                {
                    constantInputs.Add((constant.Type, constant.Bits));
                    if (constantInputs.Count
                            + nestedInputBuilder.Count
                            + scalarExpressionRegisterCount > 13)
                    {
                        return false;
                    }
                }
                else if (allowNestedInputs
                    && argument is (BoundUnary or BoundBinary or BoundCast
                        or BoundUnsafeExpression))
                {
                    var expressionConstants
                        = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
                    if (!TryAnalyzeForeignInterfaceScalarInputExpression(
                            argument, formalType, function,
                            function.ParameterCount, expressionConstants,
                            out var expressionRegisterCount))
                    {
                        return false;
                    }

                    scalarExpressionRegisterCount = checked(
                        scalarExpressionRegisterCount
                        + expressionRegisterCount);
                    hasThrowingScalarExpression |=
                        ForeignInterfaceScalarInputExpressionMayThrow(
                            argument);
                    constantInputs.UnionWith(expressionConstants);
                    if (constantInputs.Count
                            + nestedInputBuilder.Count
                            + scalarExpressionRegisterCount > 13)
                    {
                        return false;
                    }
                }
                else if (allowNestedInputs
                    && GetUnwrappedBoundCall(argument)
                        is BoundCall nestedCall
                    && nestedCall.Type == formalType
                    && !nestedInputBuilder.Any(existing =>
                        ReferenceEquals(existing, nestedCall))
                    && TryValidateNestedCall(nestedCall, formalType,
                        foreignCall.Member))
                {
                    nestedInputBuilder.Add(nestedCall);
                    nestedInputFormals.Add(formal);
                    if (constantInputs.Count
                            + nestedInputBuilder.Count
                            + scalarExpressionRegisterCount > 13)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                seenParameters[formal] = true;
            }

            return seenParameters.All(static seen => seen)
                && !(hasThrowingScalarExpression
                    && nestedInputBuilder.Count != 0
                    && Enumerable.Range(0, target.ParameterCount).Any(
                        formal => !nestedInputFormals.Contains(formal)
                            && !DirectNativeImportContract.IsScalar(
                                target.Variables[formal].Type)));
        }

        bool TryValidateLocalVoidCall(BoundCall boundCall)
        {
            if (boundCall.ForeignInterfaceCall is not null
                || (uint)boundCall.Function >= (uint)program.Functions.Length
                || boundCall.InterfaceCallIdentity is not null
                || boundCall.GenericMethodCall is not null
                || boundCall.ImportedGenericMethodCall is not null
                || !boundCall.ImportedGenericMethodTypeArguments
                    .IsDefaultOrEmpty
                || boundCall.Type != PrimitiveType.Void
                || boundCall.Arguments.IsDefault)
            {
                return false;
            }

            var target = program.Functions[boundCall.Function];
            if (target.IsExternal
                || target.ModuleImport is not null
                || target.DeclaringStruct is not null
                || target.DirectNativeImport is not null
                || target.NativeVTableMethod is not null
                || target.NativeFunctionPointerType is not null
                || target.GenericMethodInstantiation is not null
                || target.ImportedGenericMethod is not null
                || target.InterfaceRequirement is not null
                || target.IsConstEval
                || target.RequiresUnsafeCaller
                || target.DoesNotReturn
                || target.IsConstructor
                || target.IsCleanup
                || target.ReturnType != PrimitiveType.Void
                || !target.NativeOutputContracts.IsEmpty
                || target.ParameterCount != boundCall.Arguments.Length
                || target.Variables.Length != target.ParameterCount
                || target.Variables.Any(static variable =>
                    variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary
                    || variable.IsInitializationLocal
                    || !(IsSupportedForeignNativeInterfaceValueType(
                            variable.Type, program, schema)
                        || variable.Type
                            == PrimitiveType.NativeRawPointer)))
            {
                return false;
            }

            var seenParameters = new bool[target.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < boundCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = boundCall.ParameterIndex(argumentIndex);
                var argument = boundCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)target.ParameterCount
                    || seenParameters[formal]
                    || argument is not BoundRead read
                    || (uint)read.Variable >= (uint)function.ParameterCount
                    || read.Type != target.Variables[formal].Type
                    || function.Variables[read.Variable].Type
                        != read.Type
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit)
                {
                    return false;
                }

                seenParameters[formal] = true;
            }

            return seenParameters.All(static seen => seen);
        }

        bool TryValidateNestedCall(BoundCall nestedCall,
            PrimitiveType expectedType,
            BoundInterfaceMemberIdentity outerMember)
        {
            if (nestedCall.Type != expectedType
                || !DirectNativeImportContract.IsScalar(nestedCall.Type))
            {
                return false;
            }

            if (nestedCall.ForeignInterfaceCall is null)
            {
                return TryValidateLocalScalarInputCall(nestedCall,
                    expectedType, outerMember);
            }

            if (nestedCall.Function != BoundCall.ForeignInterfaceFunction
                || nestedCall.ForeignInterfaceCall is not
                    BoundForeignInterfaceCall nestedForeignCall
                || !nestedCall.IsUnsafeAuthorized
                || nestedForeignCall.DoesNotReturn
                || nestedForeignCall.Member == outerMember
                || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                    nestedForeignCall, program, out var nestedTarget)
                || nestedTarget.ReturnType != expectedType
                || !nestedTarget.NativeOutputContracts.IsEmpty
                || nestedTarget.ParameterCount != nestedCall.Arguments.Length
                || nestedCall.Arguments.IsDefault
                || nestedForeignCall.Receiver is not BoundRead receiver
                || (uint)receiver.Variable >= (uint)function.ParameterCount
                || receiver.Type != PrimitiveType.NativeRawPointer
                || function.Variables[receiver.Variable].Type
                    != PrimitiveType.NativeRawPointer)
            {
                return false;
            }

            var seenParameters = new bool[nestedTarget.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < nestedCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = nestedCall.ParameterIndex(argumentIndex);
                var argument = nestedCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)nestedTarget.ParameterCount
                    || seenParameters[formal]
                    || !(DirectNativeImportContract.IsScalar(
                            nestedTarget.Variables[formal].Type)
                        || nestedTarget.Variables[formal].Type
                            == PrimitiveType.NativeRawPointer))
                {
                    return false;
                }

                var formalType = nestedTarget.Variables[formal].Type;
                if (argument is BoundRead read)
                {
                    if ((uint)read.Variable >= (uint)function.ParameterCount
                        || read.Type != formalType
                        || function.Variables[read.Variable].Type
                            != formalType)
                    {
                        return false;
                    }
                }
                else if (argument is BoundConstant constant
                    && DirectNativeImportContract.IsScalar(
                        constant.Type)
                    && constant.Type == formalType
                    && constant.RepresentationType == constant.Type
                    && constant.Bits.IsCanonicalFor(constant.Type))
                {
                    constantInputs.Add((constant.Type, constant.Bits));
                    if (constantInputs.Count
                            + nestedInputBuilder.Count
                            + scalarExpressionRegisterCount > 13)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                seenParameters[formal] = true;
            }

            return seenParameters.All(static seen => seen);
        }

        bool TryValidateLocalScalarInputCall(BoundCall nestedCall,
            PrimitiveType expectedType,
            BoundInterfaceMemberIdentity outerMember)
        {
            if (nestedCall.ForeignInterfaceCall is not null
                || nestedCall.Function == BoundCall.ForeignInterfaceFunction
                || (uint)nestedCall.Function >= (uint)program.Functions.Length
                || nestedCall.InterfaceCallIdentity is not null
                || nestedCall.GenericMethodCall is not null
                || nestedCall.ImportedGenericMethodCall is not null
                || !nestedCall.ImportedGenericMethodTypeArguments
                    .IsDefaultOrEmpty
                || nestedCall.Type != expectedType
                || nestedCall.Arguments.IsDefault)
            {
                return false;
            }

            var target = program.Functions[nestedCall.Function];
            if (target.IsExternal
                || target.ModuleImport is not null
                || target.DeclaringStruct is not null
                || target.DirectNativeImport is not null
                || target.NativeVTableMethod is not null
                || target.NativeFunctionPointerType is not null
                || target.GenericMethodInstantiation is not null
                || target.ImportedGenericMethod is not null
                || target.InterfaceRequirement is not null
                || target.IsConstEval
                || target.RequiresUnsafeCaller
                || target.DoesNotReturn
                || target.IsConstructor
                || target.IsCleanup
                || target.ReturnType != expectedType
                || !target.NativeOutputContracts.IsEmpty
                || target.ParameterCount != nestedCall.Arguments.Length
                || target.Variables.Length != target.ParameterCount
                || target.Variables.Any(static variable =>
                    variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary
                    || variable.IsInitializationLocal
                    || !(DirectNativeImportContract.IsScalar(
                            variable.Type)
                        || variable.Type
                            == PrimitiveType.NativeRawPointer
                        || NativePointerTypes.IsType(variable.Type))))
            {
                return false;
            }

            var seenParameters = new bool[target.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < nestedCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = nestedCall.ParameterIndex(argumentIndex);
                var argument = nestedCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)target.ParameterCount
                    || seenParameters[formal])
                {
                    return false;
                }

                var formalType = target.Variables[formal].Type;
                if (argument is BoundRead read)
                {
                    if ((uint)read.Variable >= (uint)function.ParameterCount
                        || read.Type != formalType
                        || function.Variables[read.Variable].Type
                            != formalType
                        || function.Variables[read.Variable].IsOut
                        || function.Variables[read.Variable].IsInit)
                    {
                        return false;
                    }
                }
                else if (argument is BoundConstant constant
                    && constant.Type == formalType
                    && IsSupportedForeignNativeInterfaceConstant(constant,
                        formalType, program, schema))
                {
                    constantInputs.Add((constant.Type, constant.Bits));
                    if (constantInputs.Count
                        + nestedInputBuilder.Count > 13)
                    {
                        return false;
                    }
                }
                else if (GetNestedForeignInterfaceCall(argument)
                        is BoundCall nestedInputCall
                    && nestedInputCall.Type == formalType
                    && !nestedInputBuilder.Any(existing =>
                        ReferenceEquals(existing, nestedInputCall))
                    && TryValidateNestedCall(nestedInputCall, formalType,
                        outerMember))
                {
                    nestedInputBuilder.Add(nestedInputCall);
                    if (constantInputs.Count
                        + nestedInputBuilder.Count > 13)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                seenParameters[formal] = true;
            }

            return seenParameters.All(static seen => seen);
        }
    }

    private static byte[] EncodeForeignInterfaceCallSequenceBody(
        BoundFunction function, BoundProgram program,
        ImmutableArray<BoundCall> calls, bool hasValueResult,
        bool isTerminal,
        Func<PrimitiveType, int> typeSpecRowFor,
        ImmutableArray<BoundCall> nestedCalls,
        Func<int, uint> methodMapTokenForFunction,
        Func<BoundInterfaceMemberIdentity, uint>
            methodMapTokenForForeignInterface,
        TrRmFrozenScalarValueBuilder scalarValues,
        TrRmArtifactSchema schema)
    {
        if (isTerminal != function.DoesNotReturn
            || (hasValueResult
                && (isTerminal || function.ReturnType == PrimitiveType.Void))
            || (!hasValueResult && !isTerminal
                && function.ReturnType != PrimitiveType.Void))
        {
            throw Invalid(
                "A Standard F+I call sequence has an inconsistent return form.");
        }

        var homeTypeTokens = function.Variables.Select(variable =>
            GetStandardBodyTypeMapToken(variable.Type, typeSpecRowFor))
            .ToArray();
        var typeTokens = GetStandardForeignInterfaceBodyTypeTokens(function,
            calls.SelectMany(static candidate => candidate.Arguments)
                .Concat(nestedCalls.SelectMany(
                    static candidate => candidate.Arguments)),
            typeSpecRowFor);
        var bodyConstantMapValues
            = new List<TrRmFrozenScalarValueReference>();
        var bodyConstantMapIndexByValue
            = new Dictionary<(PrimitiveType Type, ScalarBits Bits), ushort>();
        var constantRegisterByValue
            = new Dictionary<(PrimitiveType Type, ScalarBits Bits), byte>();
        var constantLoads = new List<(ushort MapIndex, byte Register)>();
        var finalInputPreparations
            = new List<(BoundCall? NestedCall, byte[] ExpressionCode)>();
        var hasFinalCall = hasValueResult || isTerminal;
        var prefixCallCount = calls.Length - (hasFinalCall ? 1 : 0);
        var prefixMethodMapTokens = new List<uint>(prefixCallCount);
        var nestedMethodMapTokens = new List<uint>(nestedCalls.Length);
        var methodMapTokens = new List<uint>(
            calls.Length + nestedCalls.Length);
        var outerMethodMapToken = 0u;
        var outerForeignCall = hasFinalCall
            ? calls[^1].ForeignInterfaceCall
                ?? throw Invalid(
                    "A Standard F+I call sequence has no final InterfaceMethod identity.")
            : null;
        for (var callIndex = 0; callIndex < prefixCallCount; callIndex++)
        {
            var call = calls[callIndex];
            var methodMapToken = 0u;
            if (call.ForeignInterfaceCall is
                BoundForeignInterfaceCall foreignCall)
            {
                if (!ForeignNativeInterfaceContract.TryCreateCallTarget(
                        foreignCall, program, out var target)
                    || !call.IsUnsafeAuthorized
                    || foreignCall.DoesNotReturn
                    || call.Type != PrimitiveType.Void
                    || target.ReturnType != PrimitiveType.Void
                    || !target.NativeOutputContracts.IsEmpty
                    || target.ParameterCount != call.Arguments.Length
                    || foreignCall.Receiver is not BoundRead {
                        Type: PrimitiveType.NativeRawPointer,
                    } receiver
                    || (uint)receiver.Variable
                        >= (uint)function.ParameterCount
                    || function.Variables[receiver.Variable].IsOut
                    || function.Variables[receiver.Variable].IsInit)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has an F+I call sequence target outside the direct-call subset.");
                }

                methodMapToken = methodMapTokenForForeignInterface(
                    foreignCall.Member);
                foreach (var argument in call.Arguments)
                {
                    if (argument is BoundConstant constant)
                    {
                        _ = GetConstantRegister(constant);
                    }
                }
            }
            else if (GetLocalVoidCallTarget(call) is not null)
            {
                methodMapToken
                    = methodMapTokenForFunction(call.Function);
            }
            else
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a sequence prefix call outside the local or F+I void-call subset.");
            }

            prefixMethodMapTokens.Add(methodMapToken);
            methodMapTokens.Add(methodMapToken);
        }

        if (outerForeignCall is not null)
        {
            outerMethodMapToken = methodMapTokenForForeignInterface(
                outerForeignCall.Member);
        }

        for (var nestedIndex = 0; nestedIndex < nestedCalls.Length;
            nestedIndex++)
        {
            var nestedCall = nestedCalls[nestedIndex];
            var nestedMethodMapToken = 0u;
            if (nestedCall.ForeignInterfaceCall is
                BoundForeignInterfaceCall nestedForeignCall)
            {
                if (!ForeignNativeInterfaceContract.TryCreateCallTarget(
                        nestedForeignCall, program, out var nestedTarget)
                    || !nestedCall.IsUnsafeAuthorized
                    || nestedForeignCall.DoesNotReturn
                    || nestedForeignCall.Member == outerForeignCall?.Member
                    || nestedCall.Type != nestedTarget.ReturnType
                    || !DirectNativeImportContract.IsScalar(
                        nestedTarget.ReturnType)
                    || !nestedTarget.NativeOutputContracts.IsEmpty
                    || nestedTarget.ParameterCount
                        != nestedCall.Arguments.Length
                    || nestedCall.Arguments.IsDefault
                    || nestedForeignCall.Receiver is not BoundRead receiver
                    || (uint)receiver.Variable
                        >= (uint)function.ParameterCount
                    || receiver.Type != PrimitiveType.NativeRawPointer
                    || function.Variables[receiver.Variable].Type
                        != PrimitiveType.NativeRawPointer)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a nested F+I sequence input outside the scalar no-output subset.");
                }

                nestedMethodMapToken
                    = methodMapTokenForForeignInterface(
                        nestedForeignCall.Member);
            }
            else if (GetLocalScalarCallTarget(nestedCall) is not null)
            {
                nestedMethodMapToken
                    = methodMapTokenForFunction(nestedCall.Function);
            }
            else
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a nested F+I sequence input outside the scalar local or F+I no-output subset.");
            }

            if (hasFinalCall
                && nestedMethodMapToken == outerMethodMapToken)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has nested and outer F+I calls that share one MethodMap target.");
            }

            nestedMethodMapTokens.Add(nestedMethodMapToken);
            methodMapTokens.Add(nestedMethodMapToken);
            foreach (var argument in nestedCall.Arguments)
            {
                if (argument is BoundConstant constant)
                {
                    _ = GetConstantRegister(constant);
                }
            }
        }

        if (hasFinalCall)
        {
            var call = calls[^1];
            var foreignCall = outerForeignCall
                ?? throw Invalid(
                    "A Standard F+I call sequence has no final InterfaceMethod identity.");
            if (!ForeignNativeInterfaceContract.TryCreateCallTarget(
                    foreignCall, program, out var target)
                || !call.IsUnsafeAuthorized
                || foreignCall.DoesNotReturn != isTerminal
                || call.Type != target.ReturnType
                || (hasValueResult && call.Type != function.ReturnType)
                || !target.NativeOutputContracts.IsEmpty
                || target.ParameterCount != call.Arguments.Length
                || foreignCall.Receiver is not BoundRead {
                    Type: PrimitiveType.NativeRawPointer,
                } receiver
                || (uint)receiver.Variable >= (uint)function.ParameterCount
                || function.Variables[receiver.Variable].IsOut
                || function.Variables[receiver.Variable].IsInit)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has an F+I call sequence value target outside the direct-call subset.");
            }

            foreach (var argument in call.Arguments)
            {
                if (argument is BoundConstant constant)
                {
                    _ = GetConstantRegister(constant);
                }
                else if (GetNestedForeignInterfaceCall(argument)
                        is BoundCall nestedCall
                    && !nestedCalls.Any(nested =>
                        ReferenceEquals(nested, nestedCall)))
                {
                    throw Invalid(
                        "A Standard F+I call sequence omits a nested input CallSite.");
                }
                else
                {
                    CollectForeignInterfaceScalarInputConstants(argument,
                        constant => _ = GetConstantRegister(constant));
                }
            }

            methodMapTokens.Add(outerMethodMapToken);
        }

        if (calls.Length + nestedCalls.Length > 13
            || nestedCalls.Length + constantLoads.Count > 13
            || (!hasFinalCall && !nestedCalls.IsEmpty))
        {
            throw Unsupported(
                $"Function '{function.Name}' has too many F+I call sequence CallSites or input registers.");
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            methodMapTokens);
        var homeMap = EncodeStandardInitializationHomeMap(function,
            homeTypeTokens, maps);
        var nestedRegisterByCall
            = new Dictionary<BoundCall, byte>(
                ReferenceEqualityComparer.Instance);
        for (var nestedIndex = 0; nestedIndex < nestedCalls.Length;
            nestedIndex++)
        {
            var resultRegister = checked((byte)(nestedIndex
                + constantLoads.Count + 1));
            if (!nestedRegisterByCall.TryAdd(nestedCalls[nestedIndex],
                    resultRegister))
            {
                throw Invalid(
                    "A nested F+I call sequence input was assigned twice.");
            }
        }

        var nextExpressionRegister
            = constantLoads.Count + nestedCalls.Length + 1;

        int AllocateExpressionRegister()
        {
            if (nextExpressionRegister >= 14)
            {
                throw Unsupported(
                    $"Function '{function.Name}' uses too many F+I call sequence input registers.");
            }

            return nextExpressionRegister++;
        }

        var records = new List<ReadOnlyMemory<byte>> {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        var prefixRecordIndexes = new List<ushort>(prefixCallCount);
        for (var callIndex = 0; callIndex < prefixCallCount; callIndex++)
        {
            var call = calls[callIndex];
            var callSite = EncodeCallSite(call,
                prefixMethodMapTokens[callIndex], 0, 0);
            prefixRecordIndexes.Add(checked((ushort)records.Count));
            records.Add(new(callSite));
        }

        var nestedRecordIndexByCall
            = new Dictionary<BoundCall, ushort>(
                ReferenceEqualityComparer.Instance);
        for (var nestedIndex = 0; nestedIndex < nestedCalls.Length;
            nestedIndex++)
        {
            var callSite = EncodeCallSite(nestedCalls[nestedIndex],
                nestedMethodMapTokens[nestedIndex], 1,
                nestedRegisterByCall[nestedCalls[nestedIndex]]);
            if (!nestedRecordIndexByCall.TryAdd(nestedCalls[nestedIndex],
                    checked((ushort)records.Count)))
            {
                throw Invalid(
                    "A nested F+I call sequence input has duplicate CallSite records.");
            }
            records.Add(new(callSite));
        }

        ushort? outerRecordIndex = null;
        if (hasFinalCall)
        {
            var outerCallSite = EncodeCallSite(calls[^1],
                outerMethodMapToken, hasValueResult ? (byte)1 : (byte)0, 0);
            outerRecordIndex = checked((ushort)records.Count);
            records.Add(new(outerCallSite));
        }

        var expressionCodeLength = finalInputPreparations.Sum(
            static preparation => preparation.ExpressionCode.Length);
        var code = new List<byte>(checked(
            (constantLoads.Count * 2)
            + ((calls.Length + nestedCalls.Length) * 2)
            + expressionCodeLength
            + (isTerminal ? 0 : 2)));
        foreach (var (constantMapIndex, register) in constantLoads)
        {
            if (constantMapIndex >= 16)
            {
                throw Unsupported(
                    "The Standard F+I call sequence Body requires short ConstantMap loads.");
            }

            code.Add(0x16);
            code.Add(checked((byte)((constantMapIndex << 4) | register)));
        }

        foreach (var recordIndex in prefixRecordIndexes)
        {
            if (recordIndex >= 256)
            {
                throw Unsupported(
                    "The Standard F+I call sequence requires short CallSite indexes.");
            }

            code.Add(0x90);
            code.Add(checked((byte)recordIndex));
        }

        var emittedNestedCalls
            = new HashSet<BoundCall>(ReferenceEqualityComparer.Instance);
        foreach (var preparation in finalInputPreparations)
        {
            if (preparation.NestedCall is BoundCall nestedCall)
            {
                EmitNestedCallInstructions(nestedCall);
            }
            else
            {
                code.AddRange(preparation.ExpressionCode);
            }
        }

        if (emittedNestedCalls.Count != nestedCalls.Length)
        {
            throw Invalid(
                "A Standard F+I call sequence leaves a nested CallSite unordered.");
        }

        if (outerRecordIndex is ushort valueRecordIndex)
        {
            if (valueRecordIndex >= 256)
            {
                throw Unsupported(
                    "The Standard F+I call sequence requires short CallSite indexes.");
            }

            code.Add(0x90);
            code.Add(checked((byte)valueRecordIndex));
        }

        if (!isTerminal)
        {
            code.Add(hasValueResult ? (byte)0x17 : (byte)0x18);
            code.Add(0);
        }
        var constantMap = EncodeConstantMap();
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, constantMap,
            [new TrRmRegisterBodyBlock(0, (uint)code.Count, 0)],
            records, [.. code], schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        TrRmRegisterBodyCallInput CreateInput(
            BoundExpression expression, PrimitiveType expectedType,
            bool allowScalarExpression = false)
        {
            if (GetUnwrappedBoundCall(expression)
                    is BoundCall nestedCall
                && nestedCall.Type == expectedType
                && nestedRegisterByCall.TryGetValue(nestedCall,
                    out var nestedRegister))
            {
                if (allowScalarExpression)
                {
                    finalInputPreparations.Add((nestedCall, []));
                }

                return new TrRmRegisterBodyCallInput(1, 1,
                    nestedRegister);
            }

            if (expression is BoundRead read
                && (uint)read.Variable < (uint)function.ParameterCount
                && read.Type == expectedType
                && function.Variables[read.Variable].Type == read.Type
                && !function.Variables[read.Variable].IsOut
                && !function.Variables[read.Variable].IsInit)
            {
                return new TrRmRegisterBodyCallInput(2, 1,
                    checked((ushort)read.Variable));
            }

            if (expression is BoundConstant constant
                && constant.Type == expectedType
                && IsSupportedForeignNativeInterfaceConstant(constant,
                    expectedType, program, schema))
            {
                return new TrRmRegisterBodyCallInput(1, 1,
                    GetConstantRegister(constant));
            }

            if (allowScalarExpression)
            {
                var expressionCode = new List<byte>();
                var resultRegister
                    = EmitForeignInterfaceScalarInputExpression(
                        function, expression, expectedType,
                        AllocateExpressionRegister, GetConstantRegister,
                        type => maps.GetTypeMapIndex(
                            GetStandardBodyTypeMapToken(type,
                                typeSpecRowFor)),
                        expressionCode);
                finalInputPreparations.Add((null,
                    expressionCode.ToArray()));
                return new TrRmRegisterBodyCallInput(1, 1,
                    checked((ushort)resultRegister));
            }

            throw Unsupported(
                $"Function '{function.Name}' has an F+I call sequence input outside the direct-parameter or scalar ConstantMap subset.");
        }

        byte[] EncodeCallSite(BoundCall call, uint methodMapToken,
            byte resultKind, ushort resultIndex)
        {
            if (call.ForeignInterfaceCall is null)
            {
                var localTarget = resultKind switch {
                    0 => GetLocalVoidCallTarget(call),
                    1 => GetLocalScalarCallTarget(call),
                    _ => null,
                };
                if (localTarget is null
                    || (resultKind == 0 && resultIndex != 0)
                    || (resultKind == 1
                        && resultIndex is 0 or >= 14))
                {
                    throw Invalid(
                        "A Standard F+I call sequence local CallSite has an invalid target or result form.");
                }

                var localInputs
                    = new TrRmRegisterBodyCallInput[
                        localTarget.ParameterCount];
                var localArgumentsByFormal
                    = new BoundExpression?[localTarget.ParameterCount];
                for (var argumentIndex = 0;
                    argumentIndex < call.Arguments.Length;
                    argumentIndex++)
                {
                    var formal = call.ParameterIndex(argumentIndex);
                    if ((uint)formal >= (uint)localTarget.ParameterCount
                        || localArgumentsByFormal[formal] is not null)
                    {
                        throw Invalid(
                            "A Standard F+I call sequence local CallSite does not map each formal exactly once.");
                    }

                    localArgumentsByFormal[formal]
                        = call.Arguments[argumentIndex];
                }

                for (var formal = 0;
                    formal < localTarget.ParameterCount; formal++)
                {
                    localInputs[formal] = CreateInput(
                        localArgumentsByFormal[formal]
                        ?? throw Invalid(
                            "A Standard F+I call sequence local CallSite omits a formal input."),
                        localTarget.Variables[formal].Type);
                }

                return TrRmRegisterBodyRecordWriter.EncodeCallSite(1,
                    resultKind,
                    maps.GetMethodMapIndex(methodMapToken), resultIndex,
                    localInputs, [], schema: schema);
            }

            var foreignCall = call.ForeignInterfaceCall
                ?? throw Invalid(
                    "A Standard F+I call sequence CallSite has no InterfaceMethod identity.");
            if (!ForeignNativeInterfaceContract.TryCreateCallTarget(
                    foreignCall, program, out var target))
            {
                throw Invalid(
                    "A Standard F+I call sequence CallSite has an invalid InterfaceMethod target.");
            }

            var inputs = new TrRmRegisterBodyCallInput[
                target.ParameterCount + 1];
            inputs[0] = CreateInput(foreignCall.Receiver,
                PrimitiveType.NativeRawPointer);
            var seenParameters = new bool[target.ParameterCount];
            var allowScalarExpression = hasFinalCall
                && ReferenceEquals(call, calls[^1]);
            for (var argumentIndex = 0;
                argumentIndex < call.Arguments.Length;
                argumentIndex++)
            {
                var formal = call.ParameterIndex(argumentIndex);
                if ((uint)formal >= (uint)target.ParameterCount
                    || seenParameters[formal])
                {
                    throw Invalid(
                        "A Standard F+I call sequence CallSite does not map each formal exactly once.");
                }

                inputs[formal + 1] = CreateInput(
                    call.Arguments[argumentIndex],
                    target.Variables[formal].Type,
                    allowScalarExpression);
                seenParameters[formal] = true;
            }

            if (!seenParameters.All(static seen => seen))
            {
                throw Invalid(
                    "A Standard F+I call sequence CallSite omits a formal input.");
            }

            return TrRmRegisterBodyRecordWriter.EncodeCallSite(1, resultKind,
                maps.GetMethodMapIndex(methodMapToken), resultIndex, inputs,
                [], schema: schema);
        }

        void EmitNestedCallInstructions(BoundCall nestedCall)
        {
            if (emittedNestedCalls.Contains(nestedCall))
            {
                return;
            }

            if (!nestedRecordIndexByCall.TryGetValue(nestedCall,
                    out var recordIndex))
            {
                throw Invalid(
                    "A Standard F+I call sequence omits a nested input CallSite.");
            }

            foreach (var argument in nestedCall.Arguments)
            {
                if (GetNestedForeignInterfaceCall(argument)
                        is not BoundCall childCall)
                {
                    continue;
                }

                if (!nestedRegisterByCall.ContainsKey(childCall))
                {
                    throw Invalid(
                        "A Standard F+I call sequence nested input has no result register.");
                }

                EmitNestedCallInstructions(childCall);
            }

            _ = emittedNestedCalls.Add(nestedCall);
            if (recordIndex >= 256)
            {
                throw Unsupported(
                    "The Standard F+I call sequence requires short CallSite indexes.");
            }

            code.Add(0x90);
            code.Add(checked((byte)recordIndex));
        }

        BoundFunction? GetLocalVoidCallTarget(BoundCall call)
        {
            if (call.ForeignInterfaceCall is not null
                || call.InterfaceCallIdentity is not null
                || call.GenericMethodCall is not null
                || call.ImportedGenericMethodCall is not null
                || !call.ImportedGenericMethodTypeArguments.IsDefaultOrEmpty
                || (uint)call.Function >= (uint)program.Functions.Length
                || call.Type != PrimitiveType.Void
                || call.Arguments.IsDefault)
            {
                return null;
            }

            var target = program.Functions[call.Function];
            if (target.IsExternal
                || target.ModuleImport is not null
                || target.DeclaringStruct is not null
                || target.DirectNativeImport is not null
                || target.NativeVTableMethod is not null
                || target.NativeFunctionPointerType is not null
                || target.GenericMethodInstantiation is not null
                || target.ImportedGenericMethod is not null
                || target.InterfaceRequirement is not null
                || target.IsConstEval
                || target.RequiresUnsafeCaller
                || target.DoesNotReturn
                || target.IsConstructor
                || target.IsCleanup
                || target.ReturnType != PrimitiveType.Void
                || !target.NativeOutputContracts.IsEmpty
                || target.ParameterCount != call.Arguments.Length
                || target.Variables.Length != target.ParameterCount
                || target.Variables.Any(static variable =>
                    variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary
                    || variable.IsInitializationLocal
                    || !(DirectNativeImportContract.IsScalar(
                            variable.Type)
                        || variable.Type
                            == PrimitiveType.NativeRawPointer)))
            {
                return null;
            }

            var seenParameters = new bool[target.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < call.Arguments.Length;
                argumentIndex++)
            {
                var formal = call.ParameterIndex(argumentIndex);
                var argument = call.Arguments[argumentIndex];
                if ((uint)formal >= (uint)target.ParameterCount
                    || seenParameters[formal]
                    || argument is not BoundRead read
                    || (uint)read.Variable >= (uint)function.ParameterCount
                    || read.Type != target.Variables[formal].Type
                    || function.Variables[read.Variable].Type != read.Type
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit)
                {
                    return null;
                }

                seenParameters[formal] = true;
            }

            return seenParameters.All(static seen => seen) ? target : null;
        }

        BoundFunction? GetLocalScalarCallTarget(BoundCall call)
        {
            if (call.ForeignInterfaceCall is not null
                || call.Function == BoundCall.ForeignInterfaceFunction
                || call.InterfaceCallIdentity is not null
                || call.GenericMethodCall is not null
                || call.ImportedGenericMethodCall is not null
                || !call.ImportedGenericMethodTypeArguments.IsDefaultOrEmpty
                || (uint)call.Function >= (uint)program.Functions.Length
                || call.Arguments.IsDefault
                || !IsSupportedForeignNativeInterfaceValueType(call.Type,
                    program, schema))
            {
                return null;
            }

            var target = program.Functions[call.Function];
            if (target.IsExternal
                || target.ModuleImport is not null
                || target.DeclaringStruct is not null
                || target.DirectNativeImport is not null
                || target.NativeVTableMethod is not null
                || target.NativeFunctionPointerType is not null
                || target.GenericMethodInstantiation is not null
                || target.ImportedGenericMethod is not null
                || target.InterfaceRequirement is not null
                || target.IsConstEval
                || target.RequiresUnsafeCaller
                || target.DoesNotReturn
                || target.IsConstructor
                || target.IsCleanup
                || target.ReturnType != call.Type
                || !target.NativeOutputContracts.IsEmpty
                || target.ParameterCount != call.Arguments.Length
                || target.Variables.Length != target.ParameterCount
                || target.Variables.Any(static variable =>
                    variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary
                    || variable.IsInitializationLocal
                    || !(IsSupportedForeignNativeInterfaceValueType(
                            variable.Type, program, schema)
                        || variable.Type
                            == PrimitiveType.NativeRawPointer
                        || NativePointerTypes.IsType(variable.Type))))
            {
                return null;
            }

            var seenParameters = new bool[target.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < call.Arguments.Length;
                argumentIndex++)
            {
                var formal = call.ParameterIndex(argumentIndex);
                var argument = call.Arguments[argumentIndex];
                if ((uint)formal >= (uint)target.ParameterCount
                    || seenParameters[formal])
                {
                    return null;
                }

                var formalType = target.Variables[formal].Type;
                if (argument is BoundRead read)
                {
                    if ((uint)read.Variable >= (uint)function.ParameterCount
                        || read.Type != formalType
                        || function.Variables[read.Variable].Type
                            != formalType
                        || function.Variables[read.Variable].IsOut
                        || function.Variables[read.Variable].IsInit)
                    {
                        return null;
                    }
                }
                else if (argument is not BoundConstant constant
                    || !IsSupportedForeignNativeInterfaceConstant(constant,
                        formalType, program, schema))
                {
                    if (GetNestedForeignInterfaceCall(argument)
                            is not BoundCall nestedInputCall
                        || nestedInputCall.Type != formalType
                        || !nestedCalls.Any(nested =>
                            ReferenceEquals(nested, nestedInputCall)))
                    {
                        return null;
                    }
                }

                seenParameters[formal] = true;
            }

            return seenParameters.All(static seen => seen) ? target : null;
        }

        ushort GetConstantMapIndex(BoundConstant constant)
        {
            var key = (constant.Type, constant.Bits);
            if (bodyConstantMapIndexByValue.TryGetValue(key,
                    out var existingIndex))
            {
                return existingIndex;
            }

            var valueReference
                = AddForeignNativeInterfaceConstantMapValue(constant,
                    program, typeSpecRowFor, scalarValues, schema);
            var index = checked((ushort)bodyConstantMapValues.Count);
            bodyConstantMapValues.Add(valueReference);
            bodyConstantMapIndexByValue.Add(key, index);
            return index;
        }

        byte GetConstantRegister(BoundConstant constant)
        {
            var key = (constant.Type, constant.Bits);
            if (constantRegisterByValue.TryGetValue(key,
                    out var existingRegister))
            {
                return existingRegister;
            }

            if (constantLoads.Count == 13)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has too many F+I call sequence constants.");
            }

            var register = checked((byte)(constantLoads.Count + 1));
            var mapIndex = GetConstantMapIndex(constant);
            constantLoads.Add((mapIndex, register));
            constantRegisterByValue.Add(key, register);
            return register;
        }

        byte[] EncodeConstantMap()
        {
            if (bodyConstantMapValues.Count == 0)
            {
                return [];
            }

            var result = new byte[bodyConstantMapValues.Count * 8];
            for (var index = 0; index < bodyConstantMapValues.Count;
                index++)
            {
                var valueReference = bodyConstantMapValues[index];
                var offset = index * 8;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    result.AsSpan(offset), valueReference.TypeSpecRef);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    result.AsSpan(offset + sizeof(uint)),
                    valueReference.ConstantSourceRow);
            }

            return result;
        }
    }

    private static byte[] EncodeForeignInterfaceDirectReturnCallBody(
        BoundFunction function, BoundProgram program, BoundCall call,
        ImmutableArray<BoundLocal> stagedLocals,
        Func<PrimitiveType, int> typeSpecRowFor,
        Func<BoundInterfaceMemberIdentity, uint>
            methodMapTokenForForeignInterface,
        TrRmFrozenScalarValueBuilder scalarValues,
        TrRmArtifactSchema schema)
    {
        var foreignCall = call.ForeignInterfaceCall
            ?? throw new InvalidOperationException(
                "The selected call has no foreign-interface identity.");
        if (!ForeignNativeInterfaceContract.TryCreateCallTarget(
                foreignCall, program, out var target))
        {
            throw Invalid(
                "A Standard F+I caller Body has an invalid InterfaceMethod target.");
        }

        var homeTypeTokens = function.Variables.Select(variable =>
            GetStandardBodyTypeMapToken(variable.Type, typeSpecRowFor))
            .ToArray();
        var typeTokens = GetStandardForeignInterfaceBodyTypeTokens(function,
            call.Arguments.Concat(stagedLocals.Select(
                static local => local.Initializer)), typeSpecRowFor);
        var stagedLocalVariables = stagedLocals.Select(
                static local => local.Variable)
            .ToHashSet();
        var bodyConstantMapValues
            = new List<TrRmFrozenScalarValueReference>();
        var bodyConstantMapIndexByValue
            = new Dictionary<(PrimitiveType Type, ScalarBits Bits), ushort>();
        var constantRegisterByValue
            = new Dictionary<(PrimitiveType Type, ScalarBits Bits), byte>();
        var constantLoads = new List<(ushort MapIndex, byte Register)>();
        var argumentPreparationCode = new List<byte>();
        var nestedRecordPatches = new List<(int Offset, BoundCall Call)>();
        var expressionRegisterByFormal = new Dictionary<int, byte>();
        var methodMapToken
            = methodMapTokenForForeignInterface(foreignCall.Member);

        var nestedCalls = new List<BoundCall>();
        foreach (var argument in call.Arguments)
        {
            if (GetNestedForeignInterfaceCall(argument) is not
                BoundCall candidate)
            {
                continue;
            }

            if (nestedCalls.Count == 13
                || nestedCalls.Any(nested =>
                    ReferenceEquals(nested, candidate)))
            {
                throw Unsupported(
                    $"Function '{function.Name}' has duplicate or excessive nested F+I input calls.");
            }

            nestedCalls.Add(candidate);
        }

        foreach (var argument in call.Arguments)
        {
            if (argument is BoundConstant constant)
            {
                _ = GetConstantRegister(constant);
            }
            else if (GetNestedForeignInterfaceCall(argument) is
                BoundCall nestedCall)
            {
                foreach (var nestedArgument in nestedCall.Arguments)
                {
                    if (nestedArgument is BoundConstant nestedConstant)
                    {
                        _ = GetConstantRegister(nestedConstant);
                    }
                }
            }
            else
            {
                CollectForeignInterfaceScalarInputConstants(argument,
                    constant => _ = GetConstantRegister(constant));
            }
        }

        foreach (var local in stagedLocals)
        {
            CollectForeignInterfaceScalarInputConstants(
                local.Initializer,
                constant => _ = GetConstantRegister(constant));
        }

        if (nestedCalls.Count + constantLoads.Count
            > 13 - (stagedLocals.IsEmpty ? 0 : 1))
        {
            throw Unsupported(
                $"Function '{function.Name}' has too many direct F+I register inputs.");
        }

        var nestedMethodMapTokens = new List<uint>(nestedCalls.Count);
        var nestedInputs = new List<TrRmRegisterBodyCallInput[]>(
            nestedCalls.Count);
        var nestedRegisterByCall
            = new Dictionary<BoundCall, byte>(
                ReferenceEqualityComparer.Instance);
        var methodMapTokens = new List<uint>(nestedCalls.Count + 1);
        for (var nestedIndex = 0;
            nestedIndex < nestedCalls.Count;
            nestedIndex++)
        {
            var nestedCall = nestedCalls[nestedIndex];
            var nestedForeignCall = nestedCall.ForeignInterfaceCall
                ?? throw Invalid(
                    "A nested F+I input has no selected InterfaceMethod identity.");
            if (!ForeignNativeInterfaceContract.TryCreateCallTarget(
                    nestedForeignCall, program, out var nestedTarget)
                || !nestedCall.IsUnsafeAuthorized
                || nestedForeignCall.DoesNotReturn
                || nestedCall.Type != nestedTarget.ReturnType
                || !DirectNativeImportContract.IsScalar(
                    nestedTarget.ReturnType)
                || !nestedTarget.NativeOutputContracts.IsEmpty
                || nestedTarget.ParameterCount != nestedCall.Arguments.Length)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a nested F+I input call outside the scalar no-output subset.");
            }

            var nestedMethodMapToken = methodMapTokenForForeignInterface(
                nestedForeignCall.Member);
            if (nestedMethodMapToken == methodMapToken)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has nested and outer F+I calls that share one MethodMap target.");
            }

            var nestedInputMap = new TrRmRegisterBodyCallInput[
                nestedTarget.ParameterCount + 1];
            nestedInputMap[0] = CreateInput(
                nestedForeignCall.Receiver,
                PrimitiveType.NativeRawPointer);
            var nestedArgumentsByFormal
                = new BoundExpression?[nestedTarget.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < nestedCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = nestedCall.ParameterIndex(argumentIndex);
                if ((uint)formal >= (uint)nestedTarget.ParameterCount
                    || nestedArgumentsByFormal[formal] is not null)
                {
                    throw Invalid(
                        "A nested F+I input does not provide each formal exactly once.");
                }

                nestedArgumentsByFormal[formal]
                    = nestedCall.Arguments[argumentIndex];
            }

            for (var formal = 0;
                formal < nestedArgumentsByFormal.Length; formal++)
            {
                nestedInputMap[formal + 1] = CreateInput(
                    nestedArgumentsByFormal[formal]
                    ?? throw Invalid(
                        "A nested F+I input is missing a formal argument."),
                    nestedTarget.Variables[formal].Type);
            }

            var resultRegister = checked((byte)(nestedIndex
                + (stagedLocals.IsEmpty ? 1 : 2)
                + constantLoads.Count));
            if (!nestedRegisterByCall.TryAdd(nestedCall, resultRegister))
            {
                throw Invalid(
                    "A nested F+I input was assigned twice.");
            }

            nestedMethodMapTokens.Add(nestedMethodMapToken);
            nestedInputs.Add(nestedInputMap);
            methodMapTokens.Add(nestedMethodMapToken);
        }

        methodMapTokens.Add(methodMapToken);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            methodMapTokens);
        var nextExpressionRegister
            = (stagedLocals.IsEmpty ? 1 : 2)
                + constantLoads.Count + nestedCalls.Count;

        int AllocateExpressionRegister()
        {
            if (nextExpressionRegister >= 14)
            {
                throw Unsupported(
                    $"Function '{function.Name}' uses too many F+I input registers.");
            }

            return nextExpressionRegister++;
        }

        var stagedLocalInitializationCode = new List<byte>();
        var stagedInitializerRegisterStart = nextExpressionRegister;
        var nextStagedInitializerRegister = stagedInitializerRegisterStart;

        int AllocateStagedInitializerRegister()
        {
            if (nextStagedInitializerRegister >= 14)
            {
                throw Unsupported(
                    $"Function '{function.Name}' uses too many F+I staged-local expression registers.");
            }

            return nextStagedInitializerRegister++;
        }

        foreach (var local in stagedLocals)
        {
            var homeIndex = checked((ushort)local.Variable);
            byte sourceRegister;
            if (local.Initializer is BoundRead read
                && (uint)read.Variable < (uint)function.ParameterCount
                && read.Variable < 16)
            {
                sourceRegister = 1;
                stagedLocalInitializationCode.Add(0x11);
                stagedLocalInitializationCode.Add(checked((byte)(
                    (read.Variable << 4) | sourceRegister)));
            }
            else if (local.Initializer is BoundConstant constant
                && constantRegisterByValue.TryGetValue(
                    (constant.Type, constant.Bits),
                    out var constantRegister))
            {
                sourceRegister = constantRegister;
            }
            else
            {
                nextStagedInitializerRegister
                    = stagedInitializerRegisterStart;
                sourceRegister = checked((byte)
                    EmitForeignInterfaceScalarInputExpression(
                        function, local.Initializer,
                        local.Initializer.Type,
                        AllocateStagedInitializerRegister,
                        GetConstantRegister,
                        type => maps.GetTypeMapIndex(
                            GetStandardBodyTypeMapToken(type,
                                typeSpecRowFor)),
                        stagedLocalInitializationCode,
                        stagedLocalVariables));
            }

            stagedLocalInitializationCode.Add(0xD6);
            stagedLocalInitializationCode.Add(
                checked((byte)(0x10 | sourceRegister)));
            stagedLocalInitializationCode.Add((byte)homeIndex);
            stagedLocalInitializationCode.Add((byte)(homeIndex >> 8));
        }

        for (var sourceArgument = 0;
            sourceArgument < call.Arguments.Length;
            sourceArgument++)
        {
            var argument = call.Arguments[sourceArgument];
            var formal = call.ParameterIndex(sourceArgument);
            if (GetNestedForeignInterfaceCall(argument) is
                BoundCall nestedInputCall)
            {
                argumentPreparationCode.Add(0x90);
                var operandOffset = argumentPreparationCode.Count;
                argumentPreparationCode.Add(0);
                nestedRecordPatches.Add((operandOffset, nestedInputCall));
            }
            else if (argument is BoundRead read
                && stagedLocalVariables.Contains(read.Variable)
                && DirectNativeImportContract.IsScalar(read.Type))
            {
                var resultRegister
                    = EmitForeignInterfaceScalarInputExpression(
                        function, argument, target.Variables[formal].Type,
                        AllocateExpressionRegister, GetConstantRegister,
                        type => maps.GetTypeMapIndex(
                            GetStandardBodyTypeMapToken(type,
                                typeSpecRowFor)),
                        argumentPreparationCode, stagedLocalVariables);
                if (!expressionRegisterByFormal.TryAdd(formal,
                        checked((byte)resultRegister)))
                {
                    throw Invalid(
                        "A Standard F+I scalar expression maps one formal more than once.");
                }
            }
            else if (argument is BoundUnary or BoundBinary or BoundCast
                or BoundUpdate or BoundUnsafeExpression)
            {
                var resultRegister
                    = EmitForeignInterfaceScalarInputExpression(
                        function, argument, target.Variables[formal].Type,
                        AllocateExpressionRegister, GetConstantRegister,
                        type => maps.GetTypeMapIndex(
                            GetStandardBodyTypeMapToken(type,
                                typeSpecRowFor)),
                        argumentPreparationCode, stagedLocalVariables);
                if (!expressionRegisterByFormal.TryAdd(formal,
                        checked((byte)resultRegister)))
                {
                    throw Invalid(
                        "A Standard F+I scalar expression maps one formal more than once.");
                }
            }
        }

        var homeMap = EncodeStandardInitializationHomeMap(function,
            homeTypeTokens, maps);
        var inputs = new TrRmRegisterBodyCallInput[
            target.ParameterCount + 1];
        inputs[0] = CreateInput(foreignCall.Receiver,
            PrimitiveType.NativeRawPointer);
        var argumentsByFormal = new BoundExpression[target.ParameterCount];
        var outputContractsByFormal = target.NativeOutputContracts
            .ToDictionary(static output => output.FormalOrdinal);
        var outputReservations
            = new List<(ushort HomeIndex, uint FormalOrdinal)>(
                outputContractsByFormal.Count);
        for (var sourceArgument = 0;
            sourceArgument < call.Arguments.Length; sourceArgument++)
        {
            var formal = call.ParameterIndex(sourceArgument);
            var argument = call.Arguments[sourceArgument];
            argumentsByFormal[formal] = argument;
            if (argument is BoundNativeOutputReservation reservation)
            {
                if (!outputContractsByFormal.TryGetValue(formal,
                        out var output)
                    || reservation.PointeeType != output.PointeeType
                    || reservation.Variable < 0
                    || reservation.Variable >= function.Variables.Length
                    || function.Variables[reservation.Variable].Type
                        != output.PointeeType)
                {
                    throw Invalid(
                        "A Standard F+I output reservation does not match its selected formal.");
                }

                outputReservations.Add((
                    checked((ushort)reservation.Variable),
                    checked((uint)(formal + 1))));
            }
        }

        for (var formal = 0; formal < argumentsByFormal.Length; formal++)
        {
            if (argumentsByFormal[formal]
                    is BoundNativeOutputReservation reservation)
            {
                if (!outputContractsByFormal.TryGetValue(formal,
                        out var output)
                    || reservation.PointeeType != output.PointeeType
                    || reservation.Variable < 0
                    || reservation.Variable >= function.Variables.Length
                    || function.Variables[reservation.Variable].Type
                        != output.PointeeType)
                {
                    throw Invalid(
                        "A Standard F+I output reservation does not match its selected formal.");
                }

                inputs[formal + 1] = new TrRmRegisterBodyCallInput(2, 6,
                    checked((ushort)reservation.Variable));
            }
            else if (expressionRegisterByFormal.TryGetValue(formal,
                    out var expressionRegister))
            {
                inputs[formal + 1] = new TrRmRegisterBodyCallInput(1, 1,
                    expressionRegister);
            }
            else if (argumentsByFormal[formal] is BoundConstant constant
                && constant.Type == target.Variables[formal].Type
                && IsSupportedForeignNativeInterfaceConstant(constant,
                    target.Variables[formal].Type, program, schema)
                && constantRegisterByValue.TryGetValue(
                    (constant.Type, constant.Bits), out var constantRegister))
            {
                inputs[formal + 1] = new TrRmRegisterBodyCallInput(1, 1,
                    constantRegister);
            }
            else if (GetNestedForeignInterfaceCall(
                    argumentsByFormal[formal]) is BoundCall nestedInputCall
                && nestedRegisterByCall.TryGetValue(nestedInputCall,
                    out var nestedRegister))
            {
                inputs[formal + 1] = new TrRmRegisterBodyCallInput(1, 1,
                    nestedRegister);
            }
            else
            {
                inputs[formal + 1] = CreateInput(
                    argumentsByFormal[formal],
                    target.Variables[formal].Type);
            }
        }

        var doesNotReturn = foreignCall.DoesNotReturn;
        var hasResult = !doesNotReturn
            && call.Type != PrimitiveType.Void;
        var nestedCallSites = new List<byte[]>(nestedInputs.Count);
        for (var nestedIndex = 0;
            nestedIndex < nestedInputs.Count;
            nestedIndex++)
        {
            var resultRegister = nestedRegisterByCall[
                nestedCalls[nestedIndex]];
            nestedCallSites.Add(
                TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 1,
                    maps.GetMethodMapIndex(
                        nestedMethodMapTokens[nestedIndex]),
                    resultRegister,
                    nestedInputs[nestedIndex], [], schema: schema));
        }

        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1,
            hasResult ? (byte)1 : (byte)0,
            maps.GetMethodMapIndex(methodMapToken), 0, inputs, [],
            schema: schema);
        var records = new List<ReadOnlyMemory<byte>> {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        var nestedRecordIndexByCall = new Dictionary<BoundCall, ushort>(
            ReferenceEqualityComparer.Instance);
        for (var nestedIndex = 0;
            nestedIndex < nestedCallSites.Count;
            nestedIndex++)
        {
            var nestedRecordIndex = checked((ushort)records.Count);
            records.Add(new(nestedCallSites[nestedIndex]));
            nestedRecordIndexByCall.Add(nestedCalls[nestedIndex],
                nestedRecordIndex);
        }

        var callSiteRecordIndex = checked((ushort)records.Count);
        records.Add(new(callSite));
        var code = new List<byte>(checked(
            stagedLocalInitializationCode.Count
            + (constantLoads.Count * 2)
            + argumentPreparationCode.Count
            + (outputReservations.Count * 4) + 4));
        foreach (var (constantMapIndex, register) in constantLoads)
        {
            if (constantMapIndex >= 16)
            {
                throw Unsupported(
                    "The Standard F+I caller Body requires short ConstantMap loads.");
            }

            code.Add(0x16);
            code.Add(checked((byte)((constantMapIndex << 4) | register)));
        }

        code.AddRange(stagedLocalInitializationCode);

        foreach (var (operandOffset, nestedInputCall) in nestedRecordPatches)
        {
            if (!nestedRecordIndexByCall.TryGetValue(nestedInputCall,
                    out var nestedRecordIndex)
                || nestedRecordIndex >= 256)
            {
                throw Unsupported(
                    "The Standard F+I caller Body requires short nested CallSite indexes.");
            }

            argumentPreparationCode[operandOffset]
                = checked((byte)nestedRecordIndex);
        }

        code.AddRange(argumentPreparationCode);

        foreach (var (homeIndex, formalOrdinal) in outputReservations)
        {
            var reservationRecordIndex = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter
                .EncodeNativeOutputReservation(callSiteRecordIndex,
                    homeIndex, formalOrdinal));
            code.Add(0xDF);
            code.Add(0);
            code.Add((byte)reservationRecordIndex);
            code.Add((byte)(reservationRecordIndex >> 8));
        }

        code.Add(0x90);
        code.Add(checked((byte)callSiteRecordIndex));
        if (!doesNotReturn)
        {
            code.Add(hasResult ? (byte)0x17 : (byte)0x18);
            code.Add(0);
        }
        var constantMap = EncodeConstantMap();
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, constantMap,
            [new TrRmRegisterBodyBlock(0, (uint)code.Count, 0)],
            records, [.. code], schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        ushort GetConstantMapIndex(BoundConstant constant)
        {
            var key = (constant.Type, constant.Bits);
            if (bodyConstantMapIndexByValue.TryGetValue(key,
                    out var existingIndex))
            {
                return existingIndex;
            }

            if (bodyConstantMapValues.Count == 65_536)
            {
                throw Unsupported(
                    "The Standard F+I caller Body ConstantMap exceeds its index domain.");
            }

            var valueReference
                = AddForeignNativeInterfaceConstantMapValue(constant,
                    program, typeSpecRowFor, scalarValues, schema);
            var index = checked((ushort)bodyConstantMapValues.Count);
            bodyConstantMapValues.Add(valueReference);
            bodyConstantMapIndexByValue.Add(key, index);
            return index;
        }

        byte GetConstantRegister(BoundConstant constant)
        {
            var key = (constant.Type, constant.Bits);
            if (constantRegisterByValue.TryGetValue(key,
                    out var existingRegister))
            {
                return existingRegister;
            }

            var availableInputRegisters
                = 13 - (stagedLocals.IsEmpty ? 0 : 1);
            if (constantLoads.Count + nestedCalls.Count
                >= availableInputRegisters)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has too many direct F+I register inputs.");
            }

            var firstInputRegister = stagedLocals.IsEmpty ? 1 : 2;
            var register = checked((byte)(
                firstInputRegister + constantLoads.Count));
            var mapIndex = GetConstantMapIndex(constant);
            constantLoads.Add((mapIndex, register));
            constantRegisterByValue.Add(key, register);
            return register;
        }

        byte[] EncodeConstantMap()
        {
            if (bodyConstantMapValues.Count == 0)
            {
                return [];
            }

            var result = new byte[bodyConstantMapValues.Count * 8];
            for (var index = 0; index < bodyConstantMapValues.Count;
                index++)
            {
                var valueReference = bodyConstantMapValues[index];
                var offset = index * 8;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    result.AsSpan(offset), valueReference.TypeSpecRef);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    result.AsSpan(offset + sizeof(uint)),
                    valueReference.ConstantSourceRow);
            }

            return result;
        }

        TrRmRegisterBodyCallInput CreateInput(
            BoundExpression expression, PrimitiveType expectedType,
            bool allowScalarExpression = false)
        {
            if (expression is BoundRead read)
            {
                if (((uint)read.Variable >= (uint)function.ParameterCount
                        && !stagedLocalVariables.Contains(read.Variable))
                    || read.Type != expectedType
                    || function.Variables[read.Variable].Type != read.Type
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a nested F+I call input outside the direct-parameter or staged-local Body subset.");
                }

                return new TrRmRegisterBodyCallInput(2, 1,
                    checked((ushort)read.Variable));
            }

            if (expression is BoundConstant constant
                && constant.Type == expectedType
                && IsSupportedForeignNativeInterfaceConstant(constant,
                    expectedType, program, schema))
            {
                return new TrRmRegisterBodyCallInput(1, 1,
                    GetConstantRegister(constant));
            }

            if (allowScalarExpression)
            {
                var resultRegister
                    = EmitForeignInterfaceScalarInputExpression(function,
                        expression, expectedType,
                        AllocateExpressionRegister,
                        GetConstantRegister,
                        type => maps.GetTypeMapIndex(
                            GetStandardBodyTypeMapToken(type,
                                typeSpecRowFor)),
                        argumentPreparationCode);
                return new TrRmRegisterBodyCallInput(1, 1,
                    checked((ushort)resultRegister));
            }

            throw Unsupported(
                $"Function '{function.Name}' has a nested F+I call input outside the direct Home or scalar ConstantMap subset.");
        }
    }

    private static byte[] EncodeForeignInterfaceConditionalOutputCallBody(
        BoundFunction function, BoundProgram program, BoundCall call,
        BoundCall? successCall, BoundCall? successNestedCall,
        ImmutableArray<BoundCall> preludeCalls,
        ImmutableArray<int> outputVariables,
        int outputVariable, int statusVariable, int fallbackVariable,
        bool successOnTrue,
        Func<PrimitiveType, int> typeSpecRowFor,
        Func<BoundInterfaceMemberIdentity, uint>
            methodMapTokenForForeignInterface,
        TrRmFrozenScalarValueBuilder scalarValues,
        TrRmArtifactSchema schema)
    {
        var foreignCall = call.ForeignInterfaceCall
            ?? throw new InvalidOperationException(
                "The selected call has no foreign-interface identity.");
        if (!ForeignNativeInterfaceContract.TryCreateCallTarget(
                foreignCall, program, out var target))
        {
            throw Invalid(
                "A Standard F+I conditional Body has an invalid InterfaceMethod target.");
        }

        var homeTypeTokens = function.Variables.Select(variable =>
            GetStandardBodyTypeMapToken(variable.Type, typeSpecRowFor))
            .ToArray();
        var typeExpressions = preludeCalls.Cast<BoundExpression>()
            .Concat(call.Arguments);
        if (successCall is not null)
        {
            typeExpressions = typeExpressions.Append(successCall);
        }
        var typeTokens = GetStandardForeignInterfaceBodyTypeTokens(function,
            typeExpressions, typeSpecRowFor);
        var bodyConstantMapValues
            = new List<TrRmFrozenScalarValueReference>();
        var bodyConstantMapIndexByValue
            = new Dictionary<(PrimitiveType Type, ScalarBits Bits), ushort>();
        var constantRegisterByValue
            = new Dictionary<(PrimitiveType Type, ScalarBits Bits), byte>();
        var constantLoads = new List<(ushort MapIndex, byte Register)>();
        var methodMapToken
            = methodMapTokenForForeignInterface(foreignCall.Member);
        var preludeMethodMapTokens
            = new List<uint>(preludeCalls.Length);
        foreach (var preludeCall in preludeCalls)
        {
            var preludeForeignCall = preludeCall.ForeignInterfaceCall
                ?? throw Invalid(
                    "A Standard F+I conditional-output prelude call has no InterfaceMethod identity.");
            if (preludeCall.Function != BoundCall.ForeignInterfaceFunction
                || !preludeCall.IsUnsafeAuthorized
                || preludeCall.Type != PrimitiveType.Void
                || preludeForeignCall.DoesNotReturn
                || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                    preludeForeignCall, program, out var preludeTarget)
                || preludeTarget.ReturnType != PrimitiveType.Void
                || preludeTarget.ParameterCount
                    != preludeCall.Arguments.Length
                || !preludeTarget.NativeOutputContracts.IsEmpty
                || preludeCall.Arguments.IsDefault
                || preludeForeignCall.Receiver is not BoundRead {
                    Variable: 0,
                    Type: PrimitiveType.NativeRawPointer,
                })
            {
                throw Invalid(
                    "A Standard F+I conditional-output prelude call is outside the direct-parameter unit subset.");
            }

            preludeMethodMapTokens.Add(methodMapTokenForForeignInterface(
                preludeForeignCall.Member));
        }

        var nestedCalls = new List<BoundCall>();
        foreach (var argument in call.Arguments)
        {
            if (GetNestedForeignInterfaceCall(argument) is not
                BoundCall candidate)
            {
                continue;
            }

            if (nestedCalls.Count == 13
                || nestedCalls.Any(nested =>
                    ReferenceEquals(nested, candidate)))
            {
                throw Unsupported(
                    $"Function '{function.Name}' has duplicate or excessive nested F+I conditional-output input calls.");
            }

            nestedCalls.Add(candidate);
        }

        if (preludeCalls.Length + nestedCalls.Count > 13)
        {
            throw Unsupported(
                $"Function '{function.Name}' exceeds the conditional F+I CallSite invocation limit.");
        }

        var scalarExpressionRegisterCount = 0;
        var scalarExpressionConstants
            = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
        for (var argumentIndex = 0;
            argumentIndex < call.Arguments.Length;
            argumentIndex++)
        {
            var argument = call.Arguments[argumentIndex];
            if (argument is BoundUnary or BoundBinary or BoundCast
                or BoundUnsafeExpression)
            {
                var formal = call.ParameterIndex(argumentIndex);
                if (!TryAnalyzeForeignInterfaceScalarInputExpression(
                        argument, target.Variables[formal].Type, function,
                        fallbackVariable, scalarExpressionConstants,
                        out var expressionRegisterCount))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a conditional F+I scalar expression outside the supported Home-rooted subset.");
                }

                scalarExpressionRegisterCount = checked(
                    scalarExpressionRegisterCount
                    + expressionRegisterCount);
            }
        }

        foreach (var argument in call.Arguments)
        {
            if (argument is BoundConstant constant)
            {
                _ = GetConstantRegister(constant);
            }
            else if (GetNestedForeignInterfaceCall(argument) is
                BoundCall nestedCall)
            {
                foreach (var nestedArgument in nestedCall.Arguments)
                {
                    if (nestedArgument is BoundConstant nestedConstant)
                    {
                        _ = GetConstantRegister(nestedConstant);
                    }
                }
            }
            else if (argument is BoundUnary or BoundBinary or BoundCast
                or BoundUnsafeExpression)
            {
                CollectForeignInterfaceScalarInputConstants(argument,
                    constant =>
                    {
                        _ = GetConstantRegister(constant);
                    });
            }
        }

        if (nestedCalls.Count + constantLoads.Count
            + scalarExpressionRegisterCount > 13)
        {
            throw Unsupported(
                $"Function '{function.Name}' exceeds the conditional F+I input register limit.");
        }

        var nestedMethodMapTokens = new List<uint>(nestedCalls.Count);
        var nestedInputs = new List<TrRmRegisterBodyCallInput[]>(
            nestedCalls.Count);
        var nestedRegisterByCall
            = new Dictionary<BoundCall, byte>(ReferenceEqualityComparer.Instance);
        var methodMapTokens = new List<uint>(
            preludeMethodMapTokens.Count + nestedCalls.Count + 1);
        methodMapTokens.AddRange(preludeMethodMapTokens);
        for (var nestedIndex = 0;
            nestedIndex < nestedCalls.Count;
            nestedIndex++)
        {
            var nestedCall = nestedCalls[nestedIndex];
            var nestedForeignCall = nestedCall.ForeignInterfaceCall
                ?? throw Invalid(
                    "A nested F+I conditional-output input has no selected InterfaceMethod identity.");
            if (!ForeignNativeInterfaceContract.TryCreateCallTarget(
                    nestedForeignCall, program, out var nestedTarget)
                || !nestedCall.IsUnsafeAuthorized
                || nestedForeignCall.DoesNotReturn
                || nestedCall.Type != nestedTarget.ReturnType
                || !DirectNativeImportContract.IsScalar(
                    nestedTarget.ReturnType)
                || !nestedTarget.NativeOutputContracts.IsEmpty
                || nestedTarget.ParameterCount != nestedCall.Arguments.Length)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a nested F+I conditional-output input outside the scalar no-output subset.");
            }

            var nestedMethodMapToken = methodMapTokenForForeignInterface(
                nestedForeignCall.Member);
            if (nestedMethodMapToken == methodMapToken)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has nested and outer F+I conditional-output calls that share one MethodMap target.");
            }

            var nestedInputMap = new TrRmRegisterBodyCallInput[
                nestedTarget.ParameterCount + 1];
            nestedInputMap[0] = CreateNestedInput(
                nestedForeignCall.Receiver,
                PrimitiveType.NativeRawPointer);
            var nestedArgumentsByFormal
                = new BoundExpression?[nestedTarget.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < nestedCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = nestedCall.ParameterIndex(argumentIndex);
                if ((uint)formal >= (uint)nestedTarget.ParameterCount
                    || nestedArgumentsByFormal[formal] is not null)
                {
                    throw Invalid(
                        "A nested F+I conditional-output input does not provide each formal exactly once.");
                }

                nestedArgumentsByFormal[formal]
                    = nestedCall.Arguments[argumentIndex];
            }

            for (var formal = 0;
                formal < nestedArgumentsByFormal.Length; formal++)
            {
                nestedInputMap[formal + 1] = CreateNestedInput(
                    nestedArgumentsByFormal[formal]
                    ?? throw Invalid(
                        "A nested F+I conditional-output input is missing a formal argument."),
                    nestedTarget.Variables[formal].Type);
            }

            var resultRegister = checked((byte)(nestedIndex + 1));
            if (!nestedRegisterByCall.TryAdd(nestedCall, resultRegister))
            {
                throw Invalid(
                    "A nested F+I conditional-output input was assigned twice.");
            }

            nestedMethodMapTokens.Add(nestedMethodMapToken);
            nestedInputs.Add(nestedInputMap);
            methodMapTokens.Add(nestedMethodMapToken);
        }

        methodMapTokens.Add(methodMapToken);
        uint successCallMethodMapToken = 0;
        uint successNestedMethodMapToken = 0;
        TrRmRegisterBodyCallInput[]? successNestedInputs = null;
        if (successNestedCall is not null)
        {
            var nestedForeignCall = successNestedCall.ForeignInterfaceCall
                ?? throw Invalid(
                    "A Standard F+I conditional-output success argument has no InterfaceMethod identity.");
            if (successCall is null
                || successNestedCall.Function
                    != BoundCall.ForeignInterfaceFunction
                || !successNestedCall.IsUnsafeAuthorized
                || nestedForeignCall.DoesNotReturn
                || !DirectNativeImportContract.IsScalar(
                    successNestedCall.Type)
                || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                    nestedForeignCall, program, out var nestedTarget)
                || nestedTarget.ReturnType != successNestedCall.Type
                || !DirectNativeImportContract.IsScalar(
                    nestedTarget.ReturnType)
                || !nestedTarget.NativeOutputContracts.IsEmpty
                || nestedTarget.ParameterCount
                    != successNestedCall.Arguments.Length
                || nestedForeignCall.Receiver is not BoundRead {
                    Variable: 0,
                    Type: PrimitiveType.NativeRawPointer,
                })
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a side-effecting success input outside the scalar no-output FNI subset.");
            }

            successNestedInputs = new TrRmRegisterBodyCallInput[
                nestedTarget.ParameterCount + 1];
            successNestedInputs[0]
                = new TrRmRegisterBodyCallInput(2, 1, 0);
            var nestedArgumentsByFormal
                = new BoundRead?[nestedTarget.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < successNestedCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = successNestedCall.ParameterIndex(argumentIndex);
                var argument = successNestedCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)nestedTarget.ParameterCount
                    || nestedArgumentsByFormal[formal] is not null
                    || argument is not BoundRead read
                    || read.Variable <= 0
                    || read.Variable >= fallbackVariable
                    || read.Type != nestedTarget.Variables[formal].Type
                    || function.Variables[read.Variable].Type != read.Type
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit
                    || !(IsSupportedForeignNativeInterfaceValueType(
                            read.Type, program, schema)
                        || read.Type == PrimitiveType.NativeRawPointer))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a side-effecting success input outside the direct-Home subset.");
                }

                nestedArgumentsByFormal[formal] = read;
            }

            for (var formal = 0;
                formal < nestedArgumentsByFormal.Length; formal++)
            {
                var argument = nestedArgumentsByFormal[formal]
                    ?? throw Invalid(
                        "A Standard F+I conditional-output nested success call omits a formal input.");
                successNestedInputs[formal + 1]
                    = new TrRmRegisterBodyCallInput(2, 1,
                        checked((ushort)argument.Variable));
            }

            successNestedMethodMapToken
                = methodMapTokenForForeignInterface(
                    nestedForeignCall.Member);
            methodMapTokens.Add(successNestedMethodMapToken);
        }

        if (successCall is not null)
        {
            var successForeignCall = successCall.ForeignInterfaceCall
                ?? throw Invalid(
                    "A Standard F+I conditional-output success call has no InterfaceMethod identity.");
            if (successCall.Function != BoundCall.ForeignInterfaceFunction
                || !successCall.IsUnsafeAuthorized
                || successCall.Type != function.ReturnType
                || successForeignCall.DoesNotReturn
                || !ForeignNativeInterfaceContract.TryCreateCallTarget(
                    successForeignCall, program, out var successTarget)
                || successTarget.ReturnType != function.ReturnType
                || !successTarget.NativeOutputContracts.IsEmpty
                || successTarget.ParameterCount
                    != successCall.Arguments.Length
                || successCall.Arguments.IsDefault
                || successForeignCall.Receiver is not BoundRead
                    successReceiver
                || successReceiver.Type != PrimitiveType.NativeRawPointer
                || (successReceiver.Variable != 0
                    && !outputVariables.Contains(
                        successReceiver.Variable))
                || function.Variables[successReceiver.Variable].Type
                    != PrimitiveType.NativeRawPointer
                || outputVariables.IsEmpty
                || !IsForeignNativeInterfacePointerValueType(
                    function.Variables[outputVariable].Type)
                || preludeCalls.Length + nestedCalls.Count + 2
                    + (successNestedCall is null ? 0 : 1) > 13)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a conditional F+I success call outside the direct-Home no-output subset.");
            }

            var successArgumentsByFormal
                = new BoundExpression?[successTarget.ParameterCount];
            var consumedOutputHomes = new HashSet<int>();
            var hasPointerOutputUse = false;
            var successNestedUseCount = 0;
            if (outputVariables.Contains(successReceiver.Variable))
            {
                _ = consumedOutputHomes.Add(successReceiver.Variable);
                hasPointerOutputUse = true;
            }
            for (var argumentIndex = 0;
                argumentIndex < successCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = successCall.ParameterIndex(argumentIndex);
                var argument = successCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)successTarget.ParameterCount
                    || successArgumentsByFormal[formal] is not null
                    || !(IsSupportedForeignNativeInterfaceValueType(
                            successTarget.Variables[formal].Type,
                            program, schema)
                        || successTarget.Variables[formal].Type
                            == PrimitiveType.NativeRawPointer))
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a conditional F+I success input outside the direct-Home subset.");
                }

                if (argument is BoundRead read)
                {
                    if ((uint)read.Variable
                            >= (uint)function.Variables.Length
                        || read.Type
                            != successTarget.Variables[formal].Type
                        || function.Variables[read.Variable].Type
                            != read.Type
                        || function.Variables[read.Variable].IsOut
                        || function.Variables[read.Variable].IsInit)
                    {
                        throw Invalid(
                            "A Standard F+I conditional-output success input has an invalid direct Home.");
                    }

                    if (outputVariables.Contains(read.Variable))
                    {
                        if (!consumedOutputHomes.Add(read.Variable))
                        {
                            throw Invalid(
                                "A Standard F+I conditional-output success call consumes an output Home more than once.");
                        }

                        hasPointerOutputUse |=
                            IsForeignNativeInterfacePointerValueType(
                                read.Type);
                    }
                    else if (read.Variable <= 0
                        || read.Variable >= fallbackVariable)
                    {
                        throw Invalid(
                            "A Standard F+I conditional-output success call reads a non-parameter Home.");
                    }
                }
                else if (successNestedCall is not null
                    && GetNestedForeignInterfaceCall(argument)
                        is BoundCall nestedArgument
                    && ReferenceEquals(nestedArgument, successNestedCall)
                    && nestedArgument.Type
                        == successTarget.Variables[formal].Type
                    && successNestedUseCount == 0)
                {
                    successNestedUseCount++;
                }
                else
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a conditional F+I success input outside the direct-Home and one nested-call subset.");
                }

                successArgumentsByFormal[formal] = argument;
            }

            if (successArgumentsByFormal.Any(
                    static argument => argument is null)
                || consumedOutputHomes.Count == 0
                || !hasPointerOutputUse
                || successNestedUseCount
                    != (successNestedCall is null ? 0 : 1)
                || !consumedOutputHomes.All(outputHome =>
                    outputVariables.Contains(outputHome))
                || !consumedOutputHomes.Contains(outputVariable))
            {
                throw Invalid(
                    "A Standard F+I conditional-output success call does not consume a pointer-valued output Home.");
            }

            successCallMethodMapToken
                = methodMapTokenForForeignInterface(
                    successForeignCall.Member);
            methodMapTokens.Add(successCallMethodMapToken);
        }

        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            methodMapTokens);
        var homeMap = EncodeStandardInitializationHomeMap(function,
            homeTypeTokens, maps);
        var inputs = new TrRmRegisterBodyCallInput[
            target.ParameterCount + 1];
        inputs[0] = new TrRmRegisterBodyCallInput(2, 1, 0);
        var outputContracts = target.NativeOutputContracts
            .OrderBy(static contract => contract.FormalOrdinal)
            .ToArray();
        if (outputContracts.Length != outputVariables.Length)
        {
            throw Invalid(
                "A Standard F+I conditional-output Body lost one of its NativeOutput locals.");
        }

        var outputContract = outputContracts[0];
        var outputContractsByFormal
            = new Dictionary<int, NativeOutputContract>(
                outputContracts.Length);
        foreach (var contract in outputContracts)
        {
            if ((uint)contract.FormalOrdinal
                    >= (uint)target.ParameterCount
                || !outputContractsByFormal.TryAdd(
                    contract.FormalOrdinal, contract)
                || contract.SuccessSelector
                    != outputContract.SuccessSelector
                || contract.ComplementSelector
                    != outputContract.ComplementSelector
                || contract.SuccessState != NativeOutputState.Live
                || contract.ComplementState != NativeOutputState.Vacant
                || (outputVariables.Length > 1
                    && !DirectNativeImportContract.IsOutputPointee(
                        contract.PointeeType)))
            {
                throw Invalid(
                    "A Standard F+I conditional-output Body has incompatible NativeOutput relations.");
            }
        }

        var outputVariablesSet = outputVariables.ToHashSet();
        var outputVariableByFormal = new Dictionary<int, int>(
            outputContracts.Length);
        var reservedOutputVariables = new HashSet<int>();
        var sourceOperator = successOnTrue
            ? GetForeignNativeOutputSuccessOperator(
                outputContract.SuccessSelector,
                outputContract.ComplementSelector, target.ReturnType)
            : GetForeignNativeOutputComplementOperator(
                outputContract.SuccessSelector,
                outputContract.ComplementSelector, target.ReturnType);
        var comparisonOpcode
            = GetForeignNativeOutputComparisonOpcode(sourceOperator)
                ?? throw Unsupported(
                    "The Standard F+I conditional-output result selector is outside the selected subset.");
        var inputPreparationCode = new List<byte>();
        var nextExpressionRegister
            = nestedCalls.Count + constantLoads.Count + 1;
        for (var sourceArgument = 0;
            sourceArgument < call.Arguments.Length; sourceArgument++)
        {
            var formal = call.ParameterIndex(sourceArgument);
            if ((uint)formal >= (uint)target.ParameterCount
                || formal >= call.Arguments.Length)
            {
                throw Invalid(
                    "A Standard F+I conditional-output argument has an invalid formal ordinal.");
            }

            if (call.Arguments[sourceArgument]
                    is BoundNativeOutputReservation reservation)
            {
                if (!outputContractsByFormal.TryGetValue(
                        formal, out var formalOutputContract)
                    || !outputVariablesSet.Contains(
                        reservation.Variable)
                    || reservation.PointeeType
                        != formalOutputContract.PointeeType
                    || function.Variables[reservation.Variable].Type
                        != formalOutputContract.PointeeType
                    || !outputVariableByFormal.TryAdd(
                        formal, reservation.Variable)
                    || !reservedOutputVariables.Add(reservation.Variable))
                {
                    throw Invalid(
                        "A Standard F+I conditional output reservation does not match its selected formal.");
                }

                inputs[formal + 1] = new TrRmRegisterBodyCallInput(2, 6,
                    checked((ushort)reservation.Variable));
            }
            else if (GetNestedForeignInterfaceCall(
                    call.Arguments[sourceArgument]) is BoundCall nestedInputCall
                && nestedRegisterByCall.TryGetValue(nestedInputCall,
                    out var nestedRegister))
            {
                inputs[formal + 1] = new TrRmRegisterBodyCallInput(1, 1,
                    nestedRegister);
                inputPreparationCode.Add(0x90);
                inputPreparationCode.Add(checked((byte)(
                    preludeCalls.Length + nestedRegister)));
            }
            else if (call.Arguments[sourceArgument] is BoundRead read
                && (uint)read.Variable < (uint)function.ParameterCount)
            {
                inputs[formal + 1] = new TrRmRegisterBodyCallInput(2, 1,
                    checked((ushort)read.Variable));
            }
            else if (call.Arguments[sourceArgument]
                    is BoundConstant constant
                && constant.Type == target.Variables[formal].Type
                && IsSupportedForeignNativeInterfaceConstant(constant,
                    target.Variables[formal].Type, program, schema))
            {
                inputs[formal + 1] = new TrRmRegisterBodyCallInput(1, 1,
                    GetConstantRegister(constant));
            }
            else if (call.Arguments[sourceArgument] is BoundUnary or
                BoundBinary or BoundCast or BoundUnsafeExpression)
            {
                var resultRegister
                    = EmitForeignInterfaceScalarInputExpression(
                        function,
                        call.Arguments[sourceArgument],
                        target.Variables[formal].Type,
                        () => nextExpressionRegister++,
                        GetConstantRegister,
                        type => maps.GetTypeMapIndex(
                            GetStandardBodyTypeMapToken(type,
                                typeSpecRowFor)),
                        inputPreparationCode);
                inputs[formal + 1] = new TrRmRegisterBodyCallInput(1, 1,
                    checked((ushort)resultRegister));
            }
            else
            {
                throw Unsupported(
                    $"Function '{function.Name}' has a conditional F+I input outside the direct-parameter Body subset.");
            }
        }

        if (outputVariableByFormal.Count != outputContracts.Length
            || !reservedOutputVariables.SetEquals(outputVariables)
            || (successCall is null
                ? !outputVariableByFormal.Any(pair =>
                    pair.Value == outputVariable
                    && outputContractsByFormal[pair.Key].PointeeType
                        == function.ReturnType)
                : !outputVariableByFormal.Values.Contains(outputVariable)))
        {
            throw Invalid(
                "A Standard F+I conditional-output Body has incomplete or inconsistent output reservations.");
        }

        if (nextExpressionRegister
            != nestedCalls.Count + constantLoads.Count
                + scalarExpressionRegisterCount + 1)
        {
            throw Invalid(
                "A Standard F+I conditional-output expression changed its Home register count during emission.");
        }

        var nestedCallSites = new List<byte[]>(nestedInputs.Count);
        for (var nestedIndex = 0;
            nestedIndex < nestedInputs.Count;
            nestedIndex++)
        {
            nestedCallSites.Add(
                TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 1,
                    maps.GetMethodMapIndex(
                        nestedMethodMapTokens[nestedIndex]),
                    checked((ushort)(nestedIndex + 1)),
                    nestedInputs[nestedIndex], [], schema: schema));
        }

        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 1,
            maps.GetMethodMapIndex(methodMapToken), 0, inputs, [],
            schema: schema);
        var successNestedCallSite = successNestedCall is null
            ? null
            : TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 1,
                maps.GetMethodMapIndex(successNestedMethodMapToken), 1,
                successNestedInputs
                    ?? throw Invalid(
                        "A Standard F+I conditional-output nested success call has no input map."),
                [], schema: schema);
        var successCallSite = successCall is null
            ? null
            : TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 1,
                maps.GetMethodMapIndex(successCallMethodMapToken), 0,
                CreateSuccessCallInputs(), [], schema: schema);
        var records = new List<ReadOnlyMemory<byte>> {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        for (var preludeIndex = 0;
            preludeIndex < preludeCalls.Length; preludeIndex++)
        {
            var preludeCall = preludeCalls[preludeIndex];
            var preludeForeignCall
                = preludeCall.ForeignInterfaceCall
                    ?? throw Invalid(
                        "A Standard F+I conditional-output prelude call has no InterfaceMethod identity.");
            if (!ForeignNativeInterfaceContract.TryCreateCallTarget(
                    preludeForeignCall, program, out var preludeTarget)
                || preludeTarget.ParameterCount
                    != preludeCall.Arguments.Length)
            {
                throw Invalid(
                    "A Standard F+I conditional-output prelude call has an invalid signature.");
            }

            var receiver = (BoundRead)preludeForeignCall.Receiver;
            var preludeInputs = new TrRmRegisterBodyCallInput[
                preludeTarget.ParameterCount + 1];
            preludeInputs[0] = new TrRmRegisterBodyCallInput(2, 1,
                checked((ushort)receiver.Variable));
            var preludeArgumentsByFormal
                = new BoundRead?[preludeTarget.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < preludeCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = preludeCall.ParameterIndex(argumentIndex);
                var argument = preludeCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)preludeTarget.ParameterCount
                    || preludeArgumentsByFormal[formal] is not null
                    || argument is not BoundRead read
                    || read.Variable <= 0
                    || read.Variable >= function.ParameterCount - 1
                    || read.Type != preludeTarget.Variables[formal].Type
                    || function.Variables[read.Variable].Type != read.Type
                    || function.Variables[read.Variable].IsOut
                    || function.Variables[read.Variable].IsInit
                    || !(DirectNativeImportContract.IsScalar(read.Type)
                        || read.Type == PrimitiveType.NativeRawPointer))
                {
                    throw Invalid(
                        "A Standard F+I conditional-output prelude input is not a direct scalar or pointer Home.");
                }

                preludeArgumentsByFormal[formal] = read;
            }

            for (var formal = 0;
                formal < preludeArgumentsByFormal.Length;
                formal++)
            {
                var argument = preludeArgumentsByFormal[formal]
                    ?? throw Invalid(
                        "A Standard F+I conditional-output prelude omits a formal input.");
                preludeInputs[formal + 1]
                    = new TrRmRegisterBodyCallInput(2, 1,
                        checked((ushort)argument.Variable));
            }

            records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 0,
                maps.GetMethodMapIndex(
                    preludeMethodMapTokens[preludeIndex]), 0,
                preludeInputs, [], schema: schema));
        }

        foreach (var nestedCallSite in nestedCallSites)
        {
            records.Add(new(nestedCallSite));
        }

        var callSiteRecordIndex = checked((ushort)records.Count);
        records.Add(new(callSite));
        var outputReservationRecordIndices
            = new List<ushort>(outputContracts.Length);
        foreach (var contract in outputContracts)
        {
            var reservationRecordIndex = checked((ushort)records.Count);
            records.Add(TrRmRegisterBodyRecordWriter
                .EncodeNativeOutputReservation(callSiteRecordIndex,
                    checked((ushort)outputVariableByFormal[
                        contract.FormalOrdinal]),
                    checked((uint)(contract.FormalOrdinal + 1))));
            outputReservationRecordIndices.Add(reservationRecordIndex);
        }

        var edgeRecordIndex = checked((ushort)records.Count);
        records.Add(TrRmRegisterBodyRecordWriter.EncodeConditionalEdge(
            checked((ushort)(successOnTrue ? 1 : 2)),
            checked((ushort)(successOnTrue ? 2 : 1))));
        ushort? successNestedCallSiteRecordIndex = null;
        if (successNestedCallSite is not null)
        {
            successNestedCallSiteRecordIndex
                = checked((ushort)records.Count);
            records.Add(new(successNestedCallSite));
        }
        ushort? successCallSiteRecordIndex = null;
        if (successCallSite is not null)
        {
            successCallSiteRecordIndex = checked((ushort)records.Count);
            records.Add(new(successCallSite));
        }
        var entryCode = new List<byte>(22
            + (outputContracts.Length * 4)
            + (constantLoads.Count * 2)
            + inputPreparationCode.Count
            + (preludeCalls.Length * 2));
        // Finish input preparation before reserving the output Home.
        for (var preludeIndex = 0;
            preludeIndex < preludeCalls.Length; preludeIndex++)
        {
            entryCode.Add(0x90);
            entryCode.Add(checked((byte)(preludeIndex + 1)));
        }

        foreach (var (constantMapIndex, register) in constantLoads)
        {
            if (constantMapIndex >= 16)
            {
                throw Unsupported(
                    "The Standard F+I conditional-output Body requires short ConstantMap loads.");
            }

            entryCode.Add(0x16);
            entryCode.Add(checked((byte)(
                (constantMapIndex << 4) | register)));
        }
        entryCode.AddRange(inputPreparationCode);
        foreach (var reservationRecordIndex
            in outputReservationRecordIndices)
        {
            entryCode.Add(0xDF);
            entryCode.Add(0);
            entryCode.Add((byte)reservationRecordIndex);
            entryCode.Add((byte)(reservationRecordIndex >> 8));
        }

        entryCode.Add(0x90);
        entryCode.Add(checked((byte)callSiteRecordIndex));
        entryCode.Add(0xD6);
        entryCode.Add(0x10);
        entryCode.Add((byte)statusVariable);
        entryCode.Add((byte)(statusVariable >> 8));
        entryCode.Add(0xD5);
        entryCode.Add(0);
        entryCode.Add((byte)statusVariable);
        entryCode.Add((byte)(statusVariable >> 8));
        entryCode.Add(0xC2);
        entryCode.Add(0);
        var comparisonPayload = (ushort)((15 << 4) | (14 << 12));
        entryCode.Add((byte)comparisonPayload);
        entryCode.Add((byte)(comparisonPayload >> 8));
        entryCode.Add(0xC3);
        entryCode.Add(comparisonOpcode);
        entryCode.Add(0);
        entryCode.Add(0xFF);
        entryCode.Add(0xE4);
        entryCode.Add(0);
        entryCode.Add((byte)edgeRecordIndex);
        entryCode.Add((byte)(edgeRecordIndex >> 8));

        var successCodeBuilder = new List<byte>(6);
        if (successNestedCallSiteRecordIndex is ushort nestedRecordIndex)
        {
            successCodeBuilder.Add(0x90);
            successCodeBuilder.Add(checked((byte)nestedRecordIndex));
        }
        if (successCallSiteRecordIndex is ushort successRecordIndex)
        {
            successCodeBuilder.Add(0x90);
            successCodeBuilder.Add(checked((byte)successRecordIndex));
        }
        else
        {
            successCodeBuilder.Add(0xD5);
            successCodeBuilder.Add(0);
            successCodeBuilder.Add(checked((byte)outputVariable));
            successCodeBuilder.Add(checked((byte)(outputVariable >> 8)));
        }
        successCodeBuilder.Add(0x17);
        successCodeBuilder.Add(0);
        var successCode = successCodeBuilder.ToArray();
        byte[] complementCode = [
            0xD5, 0,
            checked((byte)(fallbackVariable >= 0
                ? fallbackVariable
                : statusVariable)),
            checked((byte)((fallbackVariable >= 0
                ? fallbackVariable
                : statusVariable) >> 8)),
            0x17, 0,
        ];
        var blocks = new TrRmRegisterBodyBlock[] {
            new(0, checked((uint)entryCode.Count), 0),
            new(checked((uint)entryCode.Count),
                checked((uint)successCode.Length), 0),
            new(checked((uint)(entryCode.Count + successCode.Length)),
                checked((uint)complementCode.Length), 0),
        };
        entryCode.AddRange(successCode);
        entryCode.AddRange(complementCode);
        var constantMap = EncodeConstantMap();
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, constantMap, blocks, records,
            entryCode.ToArray(), schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        TrRmRegisterBodyCallInput[] CreateSuccessCallInputs()
        {
            var selectedSuccessCall = successCall
                ?? throw Invalid(
                    "A Standard F+I conditional-output success call is missing.");
            var successForeignCall = selectedSuccessCall.ForeignInterfaceCall
                ?? throw Invalid(
                    "A Standard F+I conditional-output success call has no InterfaceMethod identity.");
            if (!ForeignNativeInterfaceContract.TryCreateCallTarget(
                    successForeignCall, program, out var successTarget))
            {
                throw Invalid(
                    "A Standard F+I conditional-output success call has an invalid signature.");
            }

            var successInputs = new TrRmRegisterBodyCallInput[
                successTarget.ParameterCount + 1];
            if (successForeignCall.Receiver is not BoundRead receiver)
            {
                throw Invalid(
                    "A Standard F+I conditional-output success call has no direct receiver Home.");
            }
            successInputs[0] = new TrRmRegisterBodyCallInput(2, 1,
                checked((ushort)receiver.Variable));
            var argumentsByFormal = new BoundExpression?[
                successTarget.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < selectedSuccessCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = selectedSuccessCall.ParameterIndex(argumentIndex);
                argumentsByFormal[formal]
                    = selectedSuccessCall.Arguments[argumentIndex];
            }

            for (var formal = 0; formal < argumentsByFormal.Length;
                formal++)
            {
                var argument = argumentsByFormal[formal]
                    ?? throw Invalid(
                        "A Standard F+I conditional-output success call omits a formal input.");
                if (argument is BoundRead read)
                {
                    successInputs[formal + 1]
                        = new TrRmRegisterBodyCallInput(2, 1,
                            checked((ushort)read.Variable));
                }
                else if (successNestedCall is not null
                    && GetNestedForeignInterfaceCall(argument)
                        is BoundCall nestedArgument
                    && ReferenceEquals(nestedArgument, successNestedCall))
                {
                    successInputs[formal + 1]
                        = new TrRmRegisterBodyCallInput(1, 1, 1);
                }
                else
                {
                    throw Invalid(
                        "A Standard F+I conditional-output success argument is outside the direct-Home and one nested-call subset.");
                }
            }

            return successInputs;
        }

        TrRmRegisterBodyCallInput CreateNestedInput(
            BoundExpression expression, PrimitiveType expectedType)
        {
            if (expression is BoundConstant constant
                && constant.Type == expectedType
                && IsSupportedForeignNativeInterfaceConstant(constant,
                    expectedType, program, schema))
            {
                return new TrRmRegisterBodyCallInput(1, 1,
                    GetConstantRegister(constant));
            }

            if (expression is not BoundRead read
                || read.Type != expectedType
                || (uint)read.Variable >= (uint)fallbackVariable
                || function.Variables[read.Variable].Type != read.Type
                || function.Variables[read.Variable].IsOut
                || function.Variables[read.Variable].IsInit
                || !(DirectNativeImportContract.IsScalar(read.Type)
                    || read.Type == PrimitiveType.NativeRawPointer))
            {
                throw Invalid(
                    "A nested F+I conditional-output input is not a direct scalar or pointer parameter Home.");
            }

            return new TrRmRegisterBodyCallInput(2, 1,
                checked((ushort)read.Variable));
        }

        ushort GetConstantMapIndex(BoundConstant constant)
        {
            var key = (constant.Type, constant.Bits);
            if (bodyConstantMapIndexByValue.TryGetValue(key,
                    out var existingIndex))
            {
                return existingIndex;
            }

            if (bodyConstantMapValues.Count == 65_536)
            {
                throw Unsupported(
                    "The Standard F+I conditional-output Body ConstantMap exceeds its index domain.");
            }

            var valueReference
                = AddForeignNativeInterfaceConstantMapValue(constant,
                    program, typeSpecRowFor, scalarValues, schema);
            var index = checked((ushort)bodyConstantMapValues.Count);
            bodyConstantMapValues.Add(valueReference);
            bodyConstantMapIndexByValue.Add(key, index);
            return index;
        }

        byte GetConstantRegister(BoundConstant constant)
        {
            var key = (constant.Type, constant.Bits);
            if (constantRegisterByValue.TryGetValue(key,
                    out var existingRegister))
            {
                return existingRegister;
            }

            if (constantLoads.Count + nestedCalls.Count == 13)
            {
                throw Unsupported(
                    "The Standard F+I conditional-output Body has too many distinct register inputs.");
            }

            var register = checked((byte)(
                nestedCalls.Count + constantLoads.Count + 1));
            var mapIndex = GetConstantMapIndex(constant);
            constantLoads.Add((mapIndex, register));
            constantRegisterByValue.Add(key, register);
            return register;
        }

        byte[] EncodeConstantMap()
        {
            if (bodyConstantMapValues.Count == 0)
            {
                return [];
            }

            var result = new byte[bodyConstantMapValues.Count * 8];
            for (var index = 0; index < bodyConstantMapValues.Count;
                index++)
            {
                var valueReference = bodyConstantMapValues[index];
                var offset = index * 8;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    result.AsSpan(offset), valueReference.TypeSpecRef);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    result.AsSpan(offset + sizeof(uint)),
                    valueReference.ConstantSourceRow);
            }

            return result;
        }
    }

    private readonly record struct StandardNativePointerLoadBodyDescription(
        ImmutableArray<BoundStatement> PrefixStatements,
        BoundExpression ReturnValue);

    private static bool TryGetStandardNativePointerLoadRead(
        BoundFunction function, BoundProgram program,
        out StandardNativePointerLoadBodyDescription description,
        bool allowAggregatePointees = false)
    {
        description = default;
        if (function.IsExternal
            || function.Variables.Length > SemanticVerifier.MaximumVariables)
        {
            return false;
        }

        var statements = function.Body.Statements;
        if (statements.Length == 1
            && statements[0] is BoundUnsafe outerUnsafe)
        {
            statements = outerUnsafe.Body.Statements;
        }

        var prefixStatements
            = ImmutableArray.CreateBuilder<BoundStatement>();
        var initializedLocals = new HashSet<int>();
        var pointerReads = new List<BoundNativePointerRead>(1);
        var conversions = 0;
        BoundReturn? returnStatement = null;
        for (var index = 0; index < statements.Length; index++)
        {
            var statement = statements[index];
            if (returnStatement is null && statement is BoundLocal local)
            {
                if (local.Variable < function.ParameterCount
                    || (uint)local.Variable
                        >= (uint)function.Variables.Length
                    || initializedLocals.Contains(local.Variable))
                {
                    return false;
                }

                var variable = function.Variables[local.Variable];
                if (variable.IsOut || variable.IsInit
                    || variable.Type != local.Initializer.Type
                    || !IsStandardNativePointerLoadValueType(variable.Type,
                        program, allowAggregatePointees)
                    || !CanEncodeStandardNativePointerLoadValue(
                        local.Initializer, function, program,
                        initializedLocals, pointerReads, ref conversions,
                        allowAggregatePointees))
                {
                    return false;
                }

                initializedLocals.Add(local.Variable);
                prefixStatements.Add(local);
            }
            else if (returnStatement is null
                && statement is BoundAssignment assignment)
            {
                if (assignment.Variable < function.ParameterCount
                    || (uint)assignment.Variable
                        >= (uint)function.Variables.Length
                    || !initializedLocals.Contains(assignment.Variable))
                {
                    return false;
                }

                var variable = function.Variables[assignment.Variable];
                if (!variable.IsMutable || variable.IsOut || variable.IsInit
                    || variable.Type != assignment.Value.Type
                    || !IsStandardNativePointerLoadValueType(
                        assignment.Value.Type, program,
                        allowAggregatePointees)
                    || !CanEncodeStandardNativePointerLoadValue(
                        assignment.Value, function, program,
                        initializedLocals, pointerReads, ref conversions,
                        allowAggregatePointees))
                {
                    return false;
                }

                prefixStatements.Add(assignment);
            }
            else if (returnStatement is null
                && statement is BoundExpressionStatement {
                    Expression: BoundCall call,
                }
                && CanEncodeStandardNativePointerLoadVoidCall(call,
                    function, program, initializedLocals, pointerReads,
                    ref conversions, allowAggregatePointees))
            {
                prefixStatements.Add(statement);
            }
            else if (index == statements.Length - 1
                && returnStatement is null
                && statement is BoundReturn directReturn)
            {
                returnStatement = directReturn;
            }
            else if (index == statements.Length - 1
                && returnStatement is null
                && statement is BoundUnsafe unsafeBlock
                && unsafeBlock.Body.Statements.Length == 1
                && unsafeBlock.Body.Statements[0] is BoundReturn unsafeReturn)
            {
                returnStatement = unsafeReturn;
            }
            else
            {
                return false;
            }
        }

        if (returnStatement?.Value is not BoundExpression returnValue)
        {
            return false;
        }

        if (returnValue is BoundUnsafeExpression unsafeExpression)
        {
            returnValue = unsafeExpression.Expression;
        }

        if (returnValue.Type != function.ReturnType
            || !IsStandardNativePointerLoadValueType(function.ReturnType,
                program, allowAggregatePointees)
            || !CanEncodeStandardNativePointerLoadValue(returnValue,
                function, program, initializedLocals, pointerReads,
                ref conversions, allowAggregatePointees)
            || pointerReads.Count == 0
            || pointerReads.Count > SemanticVerifier.MaximumAnalysisVisits
            || conversions > 1)
        {
            return false;
        }

        var read = pointerReads[0];
        if (!NativePointerTypes.IsType(read.Address.Type))
        {
            return false;
        }

        var pointerTypeIndex = NativePointerTypes.Index(read.Address.Type);
        if (!read.IsUnsafeAuthorized
            || !IsStandardNativePointerLoadPointeeType(read.Type, program,
                allowAggregatePointees)
            || (uint)pointerTypeIndex
                >= (uint)program.NativePointerTypes.Length)
        {
            return false;
        }

        var pointerType = program.NativePointerTypes[pointerTypeIndex];
        if (pointerType.PointeeType == PrimitiveType.Void
            || pointerType.PointeeType != read.Type
            || !NativePointerTypeContract.IsCopyableSourcePointee(
                read.Type, program.Structures,
                program.CheckedReferenceTypes,
                program.NativeLoweringAgreement,
                program.NativeFixedArrayTypes))
        {
            return false;
        }

        description = new StandardNativePointerLoadBodyDescription(
            prefixStatements.ToImmutable(), returnValue);
        return true;
    }

    private static bool CanEncodeStandardNativePointerLoadVoidCall(
        BoundCall call, BoundFunction function, BoundProgram program,
        HashSet<int> initializedLocals,
        List<BoundNativePointerRead> pointerReads, ref int conversions,
        bool allowAggregatePointees)
    {
        if (call.Type != PrimitiveType.Void
            || (uint)call.Function >= (uint)program.Functions.Length
            || call.Arguments.IsDefault
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || !call.ImportedGenericMethodTypeArguments.IsDefaultOrEmpty
            || call.InterfaceCallIdentity is not null)
        {
            return false;
        }

        var target = program.Functions[call.Function];
        if (target.ReturnType != PrimitiveType.Void
            || target.ParameterCount != call.Arguments.Length
            || target.NativeVTableMethod is not null
            || target.DirectNativeImport is not {
                Parameters.IsEmpty: false,
            }
            || (target.IsExternal && target.DirectNativeImport is null
                && target.ModuleImport is null)
            || (target.RequiresUnsafeCaller && !call.IsUnsafeAuthorized))
        {
            return false;
        }

        for (var sourceArgument = 0;
            sourceArgument < call.Arguments.Length; sourceArgument++)
        {
            var formal = call.ParameterIndex(sourceArgument);
            if ((uint)formal >= (uint)target.ParameterCount
                || target.Variables[formal].IsOut
                || target.Variables[formal].IsInit
                || target.Variables[formal].Type
                    != call.Arguments[sourceArgument].Type
                || !IsStandardNativePointerLoadValueType(
                    target.Variables[formal].Type, program,
                    allowAggregatePointees)
                || !CanEncodeStandardNativePointerLoadValue(
                    call.Arguments[sourceArgument], function, program,
                    initializedLocals, pointerReads, ref conversions,
                    allowAggregatePointees))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsStandardNativePointerLoadValueType(
        PrimitiveType type)
        => IsOutputBodyValueType(type)
            || PrimitiveTypeFacts.IsInteger(type)
            || PrimitiveTypeFacts.IsCodeUnitOrRune(type);

    private static bool IsStandardNativePointerLoadValueType(
        PrimitiveType type, BoundProgram program,
        bool allowAggregatePointees)
        => IsStandardNativePointerLoadValueType(type, program.Structures,
            allowAggregatePointees, program.NativeFixedArrayTypes);

    private static bool IsStandardNativePointerLoadValueType(
        PrimitiveType type, ImmutableArray<BoundStruct> structures,
        bool allowAggregatePointees,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes = default)
    {
        if (IsStandardNativePointerLoadValueType(type))
        {
            return true;
        }

        var structureIndex = (int)type - 100;
        return allowAggregatePointees
            && (uint)structureIndex < (uint)structures.Length
            && NativePointerTypeContract.IsCopyableSourcePointee(type,
                structures, [], nativeLoweringAgreement: null,
                fixedArrayTypes: fixedArrayTypes)
            && ValueLayout.Size(type, structures,
                fixedArrayTypes: fixedArrayTypes) > 0;
    }

    private static bool IsStandardNativePointerLoadPointeeType(
        PrimitiveType type)
        => type != PrimitiveType.NativeRawPointer
            && IsStandardNativePointerLoadValueType(type);

    private static bool IsStandardNativePointerLoadPointeeType(
        PrimitiveType type, BoundProgram program,
        bool allowAggregatePointees)
        => type != PrimitiveType.NativeRawPointer
            && IsStandardNativePointerLoadValueType(type, program,
                allowAggregatePointees);

    private static bool IsStandardNativePointerLoadPointeeType(
        PrimitiveType type, ImmutableArray<BoundStruct> structures,
        bool allowAggregatePointees,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes = default)
        => type != PrimitiveType.NativeRawPointer
            && IsStandardNativePointerLoadValueType(type, structures,
                allowAggregatePointees, fixedArrayTypes);

    private static bool CanEncodeStandardNativePointerLoadValue(
        BoundExpression expression, BoundFunction function,
        BoundProgram program, HashSet<int> initializedLocals,
        List<BoundNativePointerRead> pointerReads, ref int conversions,
        bool allowAggregatePointees)
    {
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            expression = unsafeExpression.Expression;
        }

        if (expression is BoundRead read)
        {
            if ((uint)read.Variable >= (uint)function.Variables.Length)
            {
                return false;
            }

            var variable = function.Variables[read.Variable];
            return variable.Type == read.Type && !variable.IsOut
                && !variable.IsInit
                && IsStandardNativePointerLoadValueType(read.Type,
                    program, allowAggregatePointees)
                && (read.Variable < function.ParameterCount
                    || initializedLocals.Contains(read.Variable));
        }

        if (expression is BoundNativePointerRead pointerRead)
        {
            if (!pointerRead.IsUnsafeAuthorized
                || !IsStandardNativePointerLoadPointeeType(pointerRead.Type,
                    program, allowAggregatePointees)
                || !NativePointerTypes.IsType(pointerRead.Address.Type))
            {
                return false;
            }

            var pointerTypeIndex
                = NativePointerTypes.Index(pointerRead.Address.Type);
            if ((uint)pointerTypeIndex
                    >= (uint)program.NativePointerTypes.Length)
            {
                return false;
            }

            var pointerType = program.NativePointerTypes[pointerTypeIndex];
            if (pointerType.PointeeType == PrimitiveType.Void
                || pointerType.PointeeType != pointerRead.Type
                || !NativePointerTypeContract.IsCopyableSourcePointee(
                    pointerRead.Type, program.Structures,
                    program.CheckedReferenceTypes,
                    program.NativeLoweringAgreement,
                    program.NativeFixedArrayTypes))
            {
                return false;
            }

            pointerReads.Add(pointerRead);
            return CanEncodeStandardNativePointerLoadValue(
                pointerRead.Address, function, program, initializedLocals,
                pointerReads, ref conversions, allowAggregatePointees);
        }

        if (expression is BoundCast cast)
        {
            conversions++;
            return conversions <= 1
                && IsStandardNativePointerLoadValueType(cast.Type, program,
                    allowAggregatePointees)
                && SemanticVerifier.IsTotalConversion(cast.Operand.Type,
                    cast.Type)
                && CanEncodeStandardNativePointerLoadValue(cast.Operand,
                    function, program, initializedLocals, pointerReads,
                    ref conversions, allowAggregatePointees);
        }

        if (expression is not BoundCall call
            || (uint)call.Function >= (uint)program.Functions.Length
            || call.Arguments.IsDefault
            || call.Type == PrimitiveType.Void
            || call.GenericMethodCall is not null
            || call.ImportedGenericMethodCall is not null
            || !call.ImportedGenericMethodTypeArguments.IsDefaultOrEmpty
            || call.InterfaceCallIdentity is not null)
        {
            return false;
        }

        var target = program.Functions[call.Function];
        if (call.Type != target.ReturnType
            || target.ParameterCount != call.Arguments.Length
            || target.NativeVTableMethod is not null
            || (target.IsExternal && target.DirectNativeImport is null
                && target.ModuleImport is null)
            || (target.RequiresUnsafeCaller && !call.IsUnsafeAuthorized)
            || !IsStandardNativePointerLoadValueType(target.ReturnType,
                program, allowAggregatePointees))
        {
            return false;
        }

        for (var sourceArgument = 0;
            sourceArgument < call.Arguments.Length; sourceArgument++)
        {
            var formal = call.ParameterIndex(sourceArgument);
            if ((uint)formal >= (uint)target.ParameterCount
                || target.Variables[formal].IsOut
                || target.Variables[formal].IsInit
                || target.Variables[formal].Type
                    != call.Arguments[sourceArgument].Type
                || !IsStandardNativePointerLoadValueType(
                    target.Variables[formal].Type, program,
                    allowAggregatePointees)
                || !CanEncodeStandardNativePointerLoadValue(
                    call.Arguments[sourceArgument], function, program,
                    initializedLocals, pointerReads, ref conversions,
                    allowAggregatePointees))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountStandardNativePointerLoadCalls(
        BoundExpression expression)
    {
        var pending = new Stack<BoundExpression>();
        pending.Push(expression);
        var count = 0;
        while (pending.TryPop(out var current))
        {
            if (current is BoundCall)
            {
                count++;
            }

            foreach (var nested in current.NestedExpressions)
            {
                pending.Push(nested);
            }
        }

        return count;
    }

    private static int CountStandardNativePointerReads(
        BoundExpression expression)
    {
        var pending = new Stack<BoundExpression>();
        pending.Push(expression);
        var count = 0;
        while (pending.TryPop(out var current))
        {
            if (current is BoundNativePointerRead)
            {
                count++;
            }

            foreach (var nested in current.NestedExpressions)
            {
                pending.Push(nested);
            }
        }

        return count;
    }

    private static byte[] EncodeStandardNativePointerLoadBody(
        BoundFunction function,
        BoundProgram program,
        StandardNativePointerLoadBodyDescription description,
        Func<PrimitiveType, uint> typeSpecRefFor,
        Func<int, uint> methodMapTokenForFunction,
        TrRmArtifactSchema schema)
    {
        var homeTypeTokens = function.Variables
            .Select(variable => GetStandardBodyTypeMapToken(
                variable.Type, typeSpecRefFor))
            .ToArray();
        var typeTokens = homeTypeTokens.ToList();

        var methodTokens = new List<uint>();
        foreach (var statement in description.PrefixStatements)
        {
            if (statement is BoundExpressionStatement {
                    Expression: BoundCall call,
                })
            {
                CollectStandardNativePointerLoadMethodTokens(call,
                    methodMapTokenForFunction, methodTokens);
            }
            else if (statement is BoundLocal local)
            {
                CollectStandardNativePointerLoadMethodTokens(
                    local.Initializer, methodMapTokenForFunction,
                    methodTokens);
            }
            else if (statement is BoundAssignment assignment)
            {
                CollectStandardNativePointerLoadMethodTokens(
                    assignment.Value, methodMapTokenForFunction, methodTokens);
            }
            else
            {
                throw new InvalidOperationException(
                    "A pointer-load Body description has an unsupported prefix statement.");
            }
        }
        CollectStandardNativePointerLoadMethodTokens(
            description.ReturnValue, methodMapTokenForFunction, methodTokens);
        foreach (var statement in description.PrefixStatements)
        {
            var value = statement switch
            {
                BoundLocal local => local.Initializer,
                BoundAssignment assignment => assignment.Value,
                BoundExpressionStatement {
                    Expression: BoundCall call,
                } => call,
                _ => throw new InvalidOperationException(
                    "A pointer-load Body description has an unsupported prefix statement."),
            };
            if (value is not BoundCall { Type: PrimitiveType.Void })
            {
                CollectStandardNativePointerLoadTypeMapTokens(value,
                    typeSpecRefFor, typeTokens);
            }
        }
        CollectStandardNativePointerLoadTypeMapTokens(
            description.ReturnValue, typeSpecRefFor, typeTokens);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            methodTokens);
        var homeMap = EncodeStandardInitializationHomeMap(function,
            homeTypeTokens, maps);
        var records = new List<ReadOnlyMemory<byte>> {
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
        };
        var code = new List<byte>();
        var nextRegister = 0;
        int AllocateRegister()
        {
            if (nextRegister >= 14)
            {
                throw Unsupported(
                    $"Function '{function.Name}' uses too many pointer-load registers.");
            }

            return nextRegister++;
        }

        int EmitValue(BoundExpression expression)
        {
            if (expression is BoundUnsafeExpression unsafeExpression)
            {
                return EmitValue(unsafeExpression.Expression);
            }

            if (expression is BoundRead read)
            {
                if (read.Variable >= function.ParameterCount
                    || read.Variable >= 16)
                {
                    var localRegister = AllocateRegister();
                    code.Add(0xD5);
                    code.Add(checked((byte)localRegister));
                    code.Add(checked((byte)read.Variable));
                    code.Add(checked((byte)(read.Variable >> 8)));
                    return localRegister;
                }

                var register = AllocateRegister();
                code.Add(0x11);
                code.Add(checked((byte)((read.Variable << 4) | register)));
                return register;
            }

            if (expression is BoundNativePointerRead pointerRead)
            {
                var addressRegister = EmitValue(pointerRead.Address);
                var pointerLoadRegister = AllocateRegister();
                code.Add(0x1B);
                code.Add(checked((byte)((addressRegister << 4)
                    | pointerLoadRegister)));
                return pointerLoadRegister;
            }

            if (expression is BoundCast cast)
            {
                var conversionRegister = EmitValue(cast.Operand);
                var conversionTypeMapIndex = maps.GetTypeMapIndex(
                    GetStandardBodyTypeMapToken(cast.Type, typeSpecRefFor));
                if (conversionTypeMapIndex > 15)
                {
                    throw Unsupported(
                        $"Function '{function.Name}' has a non-short pointer-load result conversion type.");
                }

                code.Add(0x50);
                code.Add(checked((byte)((conversionTypeMapIndex << 4)
                    | conversionRegister)));
                return conversionRegister;
            }

            var call = (BoundCall)expression;
            return EmitCall(call);
        }

        int EmitCall(BoundCall call)
        {
            var sourceArgumentRegisters = new int[call.Arguments.Length];
            for (var sourceArgument = 0;
                sourceArgument < call.Arguments.Length; sourceArgument++)
            {
                sourceArgumentRegisters[sourceArgument]
                    = EmitValue(call.Arguments[sourceArgument]);
            }

            var inputs = new TrRmRegisterBodyCallInput[
                program.Functions[call.Function].ParameterCount];
            for (var sourceArgument = 0;
                sourceArgument < call.Arguments.Length; sourceArgument++)
            {
                var formal = call.ParameterIndex(sourceArgument);
                inputs[formal] = new TrRmRegisterBodyCallInput(1, 1,
                    checked((ushort)sourceArgumentRegisters[
                        sourceArgument]));
            }

            var resultRegister = call.Type == PrimitiveType.Void
                ? 0 : AllocateRegister();
            var targetToken = methodMapTokenForFunction(call.Function);
            var resultKind = call.Type == PrimitiveType.Void ? (byte)0 : (byte)1;
            var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1,
                resultKind,
                maps.GetMethodMapIndex(targetToken),
                checked((ushort)resultRegister), inputs, [], schema: schema);
            if (records.Count > byte.MaxValue)
            {
                throw Unsupported(
                    $"Function '{function.Name}' has too many pointer-load call records.");
            }

            code.Add(0x90);
            code.Add(checked((byte)records.Count));
            records.Add(callSite);
            return resultRegister;
        }

        void StoreValueInHome(int variable, int sourceRegister)
        {
            if (variable < 16)
            {
                code.Add(0x14);
                code.Add(checked((byte)((sourceRegister << 4) | variable)));
            }
            else
            {
                code.Add(0xD6);
                code.Add(checked((byte)(0x10 | sourceRegister)));
                code.Add(checked((byte)variable));
                code.Add(checked((byte)(variable >> 8)));
            }
        }

        // Preserve source order across Home stores and calls in their values.
        foreach (var statement in description.PrefixStatements)
        {
            if (statement is BoundExpressionStatement {
                    Expression: BoundCall call,
                })
            {
                _ = EmitCall(call);
                continue;
            }

            var (variable, value) = statement switch
            {
                BoundLocal local => (local.Variable, local.Initializer),
                BoundAssignment assignment
                    => (assignment.Variable, assignment.Value),
                _ => throw new InvalidOperationException(
                    "A pointer-load Body description has an unsupported prefix statement."),
            };
            var sourceRegister = EmitValue(value);
            StoreValueInHome(variable, sourceRegister);
        }

        var resultRegister = EmitValue(description.ReturnValue);
        code.Add(0x17);
        code.Add(checked((byte)resultRegister));
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default,
            [new TrRmRegisterBodyBlock(0, (uint)code.Count, 0)],
            records, code.ToArray(), schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs(),
            usesNativePointerLoads: true);
    }

    private static void CollectStandardNativePointerLoadMethodTokens(
        BoundExpression expression,
        Func<int, uint> methodMapTokenForFunction,
        List<uint> methodTokens)
    {
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            CollectStandardNativePointerLoadMethodTokens(
                unsafeExpression.Expression, methodMapTokenForFunction,
                methodTokens);
        }
        else if (expression is BoundCast cast)
        {
            CollectStandardNativePointerLoadMethodTokens(cast.Operand,
                methodMapTokenForFunction, methodTokens);
        }
        else if (expression is BoundNativePointerRead pointerRead)
        {
            CollectStandardNativePointerLoadMethodTokens(
                pointerRead.Address, methodMapTokenForFunction,
                methodTokens);
        }
        else if (expression is BoundCall call)
        {
            foreach (var argument in call.Arguments)
            {
                CollectStandardNativePointerLoadMethodTokens(argument,
                    methodMapTokenForFunction, methodTokens);
            }

            methodTokens.Add(methodMapTokenForFunction(call.Function));
        }
    }

    private static void CollectStandardNativePointerLoadTypeMapTokens(
        BoundExpression expression, Func<PrimitiveType, uint> typeSpecRefFor,
        List<uint> typeTokens)
    {
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            CollectStandardNativePointerLoadTypeMapTokens(
                unsafeExpression.Expression, typeSpecRefFor, typeTokens);
        }
        else if (expression is BoundCast cast)
        {
            typeTokens.Add(GetStandardBodyTypeMapToken(cast.Type,
                typeSpecRefFor));
            CollectStandardNativePointerLoadTypeMapTokens(cast.Operand,
                typeSpecRefFor, typeTokens);
        }
        else if (expression is BoundNativePointerRead pointerRead)
        {
            CollectStandardNativePointerLoadTypeMapTokens(
                pointerRead.Address, typeSpecRefFor, typeTokens);
        }
        else if (expression is BoundCall call)
        {
            foreach (var argument in call.Arguments)
            {
                CollectStandardNativePointerLoadTypeMapTokens(argument,
                    typeSpecRefFor, typeTokens);
            }
        }
    }

    private static ImmutableArray<int>
        CollectStandardInitializationReferenceFieldStructureIndexes(
            BoundProgram program)
    {
        var structureIndexes = new HashSet<int>();
        var pendingStructureIndexes = new Stack<int>();
        static bool IsSupportedFieldLayout(BoundStruct structure)
            => structure.BlittableTargetRequirement
                    == FixedLayoutStorage.WindowsX64TargetRequirement
                && !structure.IsUnique && structure.CleanupMethod is null
                && (structure.IsSequential || structure.IsTransparent);

        void AddFieldLayout(PrimitiveType type)
        {
            var structureIndex = (int)type - 100;
            if ((uint)structureIndex >= (uint)program.Structures.Length
                || !IsSupportedFieldLayout(
                    program.Structures[structureIndex]))
            {
                throw Unsupported(
                    "A field-backed initialization-reference path requires rootless Copy Sequential or Transparent Blittable layouts.");
            }

            pendingStructureIndexes.Push(structureIndex);
        }

        foreach (var function in program.Functions)
        {
            if (TryGetInitializationReferenceCall(function, program,
                    out _, out var outputPlace, out _, out _)
                && outputPlace is BoundFieldRead field)
            {
                if (!TryGetInitializationFieldPath(field,
                        out var root, out var path))
                {
                    throw Unsupported(
                        "A field-backed initialization-reference target has no local struct root.");
                }

                AddFieldLayout(root.Type);
                foreach (var step in path)
                {
                    AddFieldLayout(step.Receiver.Type);
                }
            }
        }

        while (pendingStructureIndexes.Count != 0)
        {
            var structureIndex = pendingStructureIndexes.Pop();
            if (!structureIndexes.Add(structureIndex))
            {
                continue;
            }

            foreach (var field in program.Structures[structureIndex].Fields)
            {
                var fieldTypeCode = (int)field.Type;
                if (fieldTypeCode is >= 100 and < BoundEnumTypes.First)
                {
                    var fieldStructureIndex = fieldTypeCode - 100;
                    if ((uint)fieldStructureIndex
                        >= (uint)program.Structures.Length
                        || !IsSupportedFieldLayout(
                            program.Structures[fieldStructureIndex]))
                    {
                        throw Unsupported(
                            "A field-backed initialization-reference layout closure requires rootless Copy Sequential or Transparent Blittable field types.");
                    }

                    pendingStructureIndexes.Push(fieldStructureIndex);
                }
            }
        }

        return [.. structureIndexes.Order()];
    }

    private static bool TryGetStandardInitializationReferenceConditionalStore(
        BoundFunction function, BoundProgram program,
        out int conditionVariable,
        out ImmutableArray<StandardInitializationReferenceStore> stores)
    {
        conditionVariable = -1;
        stores = [];
        if (function.IsExternal || function.ParameterCount < 2
            || function.Variables.Length != function.ParameterCount
            || function.ReturnType != PrimitiveType.Bool
            || !function.DeclarationContractClauses.IsEmpty
            || function.Variables[0].Type != PrimitiveType.Bool
            || function.Variables[0].IsOut || function.Variables[0].IsInit
            || function.Body.Statements.Length != 2
            || function.Body.Statements[0] is not BoundIf {
                Condition: BoundRead {
                    Variable: 0,
                    Type: PrimitiveType.Bool,
                },
                Then: BoundBlock thenBlock,
                Else: null,
            }
            || function.Body.Statements[1] is not BoundReturn {
                Value: BoundConstant {
                    Type: PrimitiveType.Bool,
                    Value: 0,
                },
            })
        {
            return false;
        }

        var outputReferents = new Dictionary<int, PrimitiveType>();
        for (var ordinal = 1; ordinal < function.ParameterCount; ordinal++)
        {
            var formal = function.Variables[ordinal];
            if (formal.IsInit)
            {
                if (!IsStandardInitializationReferenceParameter(
                        formal, program)
                    || !TryGetStandardInitializationReferenceReferentType(
                        formal.Type, program.CheckedReferenceTypes,
                        out var referentType))
                {
                    return false;
                }

                outputReferents.Add(ordinal, referentType);
            }
            else if (formal.IsOut || !IsOutputBodyValueType(formal.Type))
            {
                return false;
            }
        }

        if (outputReferents.Count == 0
            || thenBlock.Statements.Length != outputReferents.Count + 1
            || thenBlock.Statements[^1] is not BoundReturn {
                Value: BoundConstant {
                    Type: PrimitiveType.Bool,
                    Value: 1,
                },
            })
        {
            return false;
        }

        var outputStores
            = ImmutableArray.CreateBuilder<
                StandardInitializationReferenceStore>(
                outputReferents.Count);
        var initializedOutputs = new HashSet<int>();
        for (var index = 0; index < outputReferents.Count; index++)
        {
            if (thenBlock.Statements[index]
                    is not BoundExpressionStatement {
                        Expression: BoundReferenceWrite {
                            Descriptor: BoundRead descriptor,
                            Value: BoundRead source,
                        },
                    }
                || !outputReferents.TryGetValue(
                    descriptor.Variable, out var referentType)
                || descriptor.Type
                    != function.Variables[descriptor.Variable].Type
                || source.Type != referentType
                || source.Variable < 0
                || source.Variable >= function.ParameterCount
                || outputReferents.ContainsKey(source.Variable)
                || function.Variables[source.Variable].IsOut
                || function.Variables[source.Variable].IsInit
                || function.Variables[source.Variable].Type != referentType
                || !initializedOutputs.Add(descriptor.Variable))
            {
                return false;
            }

            outputStores.Add(new StandardInitializationReferenceStore(
                descriptor.Variable, source.Variable, 1));
        }

        if (initializedOutputs.Count != outputReferents.Count)
        {
            return false;
        }

        conditionVariable = 0;
        stores = outputStores.MoveToImmutable();
        return true;
    }

    private static bool TryGetInitializationReferenceConditionalCall(
        BoundFunction function, BoundProgram program,
        out BoundCall call, out int outputVariable,
        out int fallbackVariable)
    {
        call = null!;
        outputVariable = -1;
        fallbackVariable = -1;
        if (function.IsExternal || function.ParameterCount != 3
            || function.Variables.Length != 4
            || !IsOutputBodyValueType(function.ReturnType)
            || function.Variables[0].Type != PrimitiveType.Bool
            || function.Variables[0].IsOut || function.Variables[0].IsInit
            || function.Variables[1].IsOut || function.Variables[1].IsInit
            || function.Variables[2].IsOut || function.Variables[2].IsInit
            || function.Variables[2].Type != function.ReturnType
            || !function.Variables[3].IsInitializationLocal
            || function.Variables[3].Type != function.ReturnType)
        {
            return false;
        }

        var statements = function.Body.Statements;
        if (statements.Length != 3
            || statements[0] is not BoundVacantLocal {
                Variable: 3,
            }
            || statements[1] is not BoundIf {
                Condition: BoundCall boundCall,
                Then: BoundBlock { Statements.Length: 1 } thenBlock,
                Else: null,
            }
            || thenBlock.Statements[0] is not BoundReturn {
                Value: BoundRead {
                    Variable: 3,
                },
            }
            || statements[2] is not BoundReturn {
                Value: BoundRead fallback,
            }
            || fallback.Variable != 2
            || (uint)boundCall.Function >= (uint)program.Functions.Length)
        {
            return false;
        }

        var target = program.Functions[boundCall.Function];
        if ((target.IsExternal != (target.ModuleImport is not null))
            || target.DirectNativeImport is not null
            || target.NativeVTableMethod is not null
            || target.ReturnType != PrimitiveType.Bool
            || target.ParameterCount != 3
            || target.Variables[0].Type != function.Variables[0].Type
            || target.Variables[1].Type != function.Variables[1].Type
            || target.Variables[2].IsOut
            || !IsStandardInitializationReferenceParameter(
                target.Variables[2], program)
            || !TryGetStandardInitializationReferenceReferentType(
                target.Variables[2].Type, program.CheckedReferenceTypes,
                out var referentType)
            || referentType != function.ReturnType
            || boundCall.Type != PrimitiveType.Bool
            || boundCall.Arguments.Length != target.ParameterCount
            || Enumerable.Range(0, target.ParameterCount)
                .Any(index => boundCall.ParameterIndex(index) != index)
            || boundCall.Arguments[0] is not BoundRead {
                Variable: 0,
            }
            || boundCall.Arguments[1] is not BoundRead {
                Variable: 1,
            }
            || boundCall.Arguments[2] is not BoundReferenceFormation {
                Place: BoundRead {
                    Variable: 3,
                },
            } output
            || output.Type != target.Variables[2].Type
            || fallback.Type != function.ReturnType)
        {
            return false;
        }

        var (targetOutputSelectors, targetOutputStates)
            = GetStandardInitializationOutputStateVectors(target, program);
        if (!targetOutputSelectors.SequenceEqual([
                new NativeFunctionPointerSelector(3, 0, 1),
                new NativeFunctionPointerSelector(3, 1, 2),
            ])
            || !targetOutputStates.SequenceEqual([
                new NativeFunctionPointerOutputState(2, 0, 1),
                new NativeFunctionPointerOutputState(2, 1, 0),
            ]))
        {
            return false;
        }

        call = boundCall;
        outputVariable = 3;
        fallbackVariable = fallback.Variable;
        return true;
    }

    private static bool TryGetInitializationReferenceStore(
        BoundFunction function, BoundProgram program,
        out ImmutableArray<StandardInitializationReferenceStore> stores)
    {
        stores = [];
        if (function.IsExternal || function.ParameterCount < 2
            || function.Variables.Length != function.ParameterCount
            || function.ReturnType != PrimitiveType.Void
            || !IsStandardInitializationReferenceParameter(
                function.Variables[0], program))
        {
            return false;
        }

        if (!TryGetStandardInitializationReferenceReferentType(
                function.Variables[0].Type, program.CheckedReferenceTypes,
                out var referentType)
            || referentType != function.Variables[1].Type)
        {
            return false;
        }

        for (var index = 1; index < function.ParameterCount; index++)
        {
            var source = function.Variables[index];
            if (source.IsOut || source.IsInit
                || source.Type != referentType
                || !IsOutputScalarType(source.Type))
            {
                return false;
            }
        }

        var writes = new List<BoundReferenceWrite>();
        for (var index = 0; index < function.Body.Statements.Length;
            index++)
        {
            var statement = function.Body.Statements[index];
            if (statement is BoundExpressionStatement {
                    Expression: BoundReferenceWrite write,
                })
            {
                writes.Add(write);
                continue;
            }
            if (statement is BoundReturn {
                    Value: null,
                } && index == function.Body.Statements.Length - 1)
            {
                continue;
            }

            return false;
        }

        if (writes.Count is 0 or > byte.MaxValue)
        {
            return false;
        }

        var reference = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(function.Variables[0].Type)];
        if (writes.Count > 1 && !reference.IsWritable)
        {
            return false;
        }

        var outputStores
            = ImmutableArray.CreateBuilder<
                StandardInitializationReferenceStore>(writes.Count);
        for (var index = 0; index < writes.Count; index++)
        {
            var write = writes[index];
            if (write.Descriptor is not BoundRead {
                    Variable: 0,
                } descriptor
                || descriptor.Type != function.Variables[0].Type
                || write.Value is not BoundRead source
                || source.Variable <= 0
                || source.Variable >= function.ParameterCount
                || source.Type != referentType)
            {
                return false;
            }

            outputStores.Add(new StandardInitializationReferenceStore(
                descriptor.Variable, source.Variable,
                index == 0 ? (byte)1 : (byte)2));
        }

        stores = outputStores.MoveToImmutable();
        return true;
    }

    private static bool TryGetInitializationReferenceCall(
        BoundFunction function, BoundProgram program,
        out BoundCall call, out BoundExpression outputPlace,
        out int outputVariable, out BoundFunction target)
    {
        call = null!;
        outputPlace = null!;
        outputVariable = -1;
        target = null!;
        var localVariable = function.ParameterCount;
        if (function.IsExternal || function.ParameterCount < 1
            || function.Variables.Length != function.ParameterCount + 1
            || (function.ReturnType != PrimitiveType.Void
                && !IsOutputBodyValueType(function.ReturnType))
            || function.Variables.Take(function.ParameterCount).Any(
                static parameter => parameter.IsOut || parameter.IsInit)
            || !function.Variables[localVariable].IsInitializationLocal)
        {
            return false;
        }

        var statements = function.Body.Statements;
        if (statements.Length != 3
            || statements[0] is not BoundVacantLocal vacantLocal
            || vacantLocal.Variable != localVariable
            || statements[1] is not BoundExpressionStatement {
                Expression: BoundCall boundCall,
            }
            || statements[2] is not BoundReturn {
                Value: var returnValue,
            }
            || (uint)boundCall.Function >= (uint)program.Functions.Length)
        {
            return false;
        }

        if (function.ReturnType == PrimitiveType.Void
            ? returnValue is not null : returnValue is null)
        {
            return false;
        }

        var boundTarget = program.Functions[boundCall.Function];
        if ((boundTarget.IsExternal
                && boundTarget.ModuleImport is null)
            || boundTarget.DirectNativeImport is not null
            || boundTarget.NativeVTableMethod is not null
            || boundTarget.ReturnType != PrimitiveType.Void
            || boundTarget.ParameterCount != boundCall.Arguments.Length
            || boundTarget.ParameterCount != function.ParameterCount + 1
            || !IsStandardInitializationReferenceParameter(
                boundTarget.Variables[0], program)
            || !TryGetStandardInitializationReferenceReferentType(
                boundTarget.Variables[0].Type,
                program.CheckedReferenceTypes, out var referentType)
            || !IsOutputBodyValueType(referentType)
            || boundCall.Type != PrimitiveType.Void
            || boundCall.ParameterIndex(0) != 0
            || boundCall.Arguments[0] is not BoundReferenceFormation {
                Place: var place,
            } output
            || output.Type != boundTarget.Variables[0].Type
            || place.Type != referentType
            || place is not (BoundRead or BoundFieldRead))
        {
            return false;
        }

        if (place is BoundRead directPlace)
        {
            if (directPlace.Variable != localVariable)
            {
                return false;
            }
        }
        else if (!TryGetInitializationFieldPath(place,
                out var fieldRoot, out _)
            || fieldRoot.Variable != localVariable)
        {
            return false;
        }

        for (var sourceOrdinal = 0;
            sourceOrdinal < function.ParameterCount; sourceOrdinal++)
        {
            var sourceParameter = function.Variables[sourceOrdinal];
            var targetParameter = boundTarget.Variables[sourceOrdinal + 1];
            var argumentIndex = sourceOrdinal + 1;
            if (!IsOutputScalarType(sourceParameter.Type)
                || sourceParameter.Type != referentType
                || targetParameter.IsOut || targetParameter.IsInit
                || targetParameter.Type != referentType
                || boundCall.ParameterIndex(argumentIndex) != argumentIndex
                || boundCall.Arguments[argumentIndex] is not BoundRead {
                    Variable: var variable,
                } source
                || variable != sourceOrdinal
                || source.Type != sourceParameter.Type)
            {
                return false;
            }
        }

        if (function.ReturnType == PrimitiveType.Void)
        {
            if (place is not BoundFieldRead)
            {
                return false;
            }
        }
        else if (place is BoundRead localPlace)
        {
            if (localPlace.Variable != localVariable
                || returnValue is not BoundRead localReturn
                || localReturn.Variable != localVariable
                || function.Variables[localVariable].Type
                    != function.ReturnType)
            {
                return false;
            }
        }
        else if (place is BoundFieldRead outputField)
        {
            if (returnValue is not BoundFieldRead returnedField
                || !SameInitializationFieldPath(outputField,
                    returnedField, localVariable)
                || outputField.Type != function.ReturnType)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        call = boundCall;
        outputPlace = place;
        outputVariable = localVariable;
        target = boundTarget;
        return true;
    }

    private static bool TryGetInitializationFieldPath(
        BoundExpression expression, out BoundRead root,
        out ImmutableArray<BoundFieldRead> fields)
    {
        var reversed = new List<BoundFieldRead>();
        while (expression is BoundFieldRead field)
        {
            reversed.Add(field);
            expression = field.Receiver;
        }

        if (reversed.Count == 0 || expression is not BoundRead rootRead)
        {
            root = null!;
            fields = [];
            return false;
        }

        reversed.Reverse();
        root = rootRead;
        fields = [.. reversed];
        return true;
    }

    private static bool RequiresStandardInitializationFieldPath(
        BoundFieldRead field, BoundProgram program)
    {
        if (!TryGetInitializationFieldPath(field, out _, out var path))
        {
            return false;
        }
        if (path.Length > 1)
        {
            return true;
        }

        var ownerIndex = (int)path[0].Receiver.Type - 100;
        if (ownerIndex < 0 || ownerIndex >= program.Structures.Length
            || (uint)path[0].Field
                >= (uint)program.Structures[ownerIndex].Fields.Length)
        {
            return true;
        }

        var owner = program.Structures[ownerIndex];
        return owner.StandardTypeOrigin is not null
            || owner.Fields[path[0].Field].StandardFieldOrigin is not null;
    }

    private static bool HasSupportedStandardInitializationFieldPathGeometry(
        PrimitiveType rootType, ImmutableArray<BoundFieldRead> path,
        ImmutableArray<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes,
        IReadOnlyList<BoundEnumDefinition> enumDefinitions,
        string? nativeLoweringAgreement)
    {
        var rootIndex = (int)rootType - 100;
        if (path.IsDefaultOrEmpty
            || rootIndex < 0 || rootIndex >= structures.Length)
        {
            return false;
        }

        var rootStructure = structures[rootIndex];
        var ownerType = rootType;
        var totalOffset = 0UL;
        var leafSize = 0UL;
        for (var ordinal = 0; ordinal < path.Length; ordinal++)
        {
            var step = path[ordinal];
            var ownerIndex = (int)ownerType - 100;
            if (step.Receiver.Type != ownerType
                || ownerIndex < 0 || ownerIndex >= structures.Length)
            {
                return false;
            }

            var owner = structures[ownerIndex];
            if (owner.IsUnique || owner.CleanupMethod is not null
                || owner.SourceGenericTypeDefinitionIndex >= 0
                || owner.BlittableTargetRequirement
                    != FixedLayoutStorage.WindowsX64TargetRequirement
                || (!owner.IsSequential && !owner.IsTransparent)
                || (uint)step.Field >= (uint)owner.Fields.Length)
            {
                return false;
            }

            var field = owner.Fields[step.Field];
            if (field.Type != step.Type || owner.Size == 0
                || field.Size == 0 || field.Offset > owner.Size
                || field.Size > owner.Size - field.Offset)
            {
                return false;
            }

            try
            {
                totalOffset = checked(totalOffset + field.Offset);
            }
            catch (OverflowException)
            {
                return false;
            }

            leafSize = field.Size;
            ownerType = field.Type;
        }

        if (rootStructure.Alignment <= 0
            || totalOffset > rootStructure.Size
            || leafSize > rootStructure.Size - totalOffset)
        {
            return false;
        }

        int leafAlignment;
        try
        {
            leafAlignment = ValueLayout.Alignment(ownerType, structures,
                checkedReferenceTypes, nativeLoweringAgreement,
                enumDefinitions: enumDefinitions);
        }
        catch (NotSupportedException)
        {
            return false;
        }

        return leafAlignment > 0
            && rootStructure.Alignment % leafAlignment == 0
            && totalOffset % checked((ulong)leafAlignment) == 0;
    }

    private static bool SameInitializationFieldPath(
        BoundFieldRead left, BoundFieldRead right, int rootVariable)
    {
        if (!TryGetInitializationFieldPath(left, out var leftRoot,
                out var leftFields)
            || !TryGetInitializationFieldPath(right, out var rightRoot,
                out var rightFields)
            || leftRoot.Variable != rootVariable
            || rightRoot.Variable != rootVariable
            || leftRoot.Type != rightRoot.Type
            || leftFields.Length != rightFields.Length)
        {
            return false;
        }

        for (var index = 0; index < leftFields.Length; index++)
        {
            var leftField = leftFields[index];
            var rightField = rightFields[index];
            if (leftField.Field != rightField.Field
                || leftField.Type != rightField.Type
                || leftField.Receiver.Type != rightField.Receiver.Type)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetInitializationReferenceForwardingCall(
        BoundFunction function, BoundProgram program, out BoundCall call)
    {
        call = null!;
        if (function.IsExternal || function.ParameterCount != 2
            || function.Variables.Length != 2
            || function.ReturnType != PrimitiveType.Void
            || !IsStandardInitializationReferenceParameter(
                function.Variables[0], program)
            || function.Variables[1].IsOut || function.Variables[1].IsInit
            || !IsOutputBodyValueType(function.Variables[1].Type))
        {
            return false;
        }

        var statements = function.Body.Statements;
        if (statements.Length is not (1 or 2)
            || statements[0] is not BoundExpressionStatement {
                Expression: BoundCall boundCall,
            }
            || (statements.Length == 2
                && statements[1] is not BoundReturn { Value: null })
            || (uint)boundCall.Function >= (uint)program.Functions.Length)
        {
            return false;
        }

        var target = program.Functions[boundCall.Function];
        if ((target.IsExternal && target.ModuleImport is null)
            || target.DirectNativeImport is not null
            || target.NativeVTableMethod is not null
            || target.RequiresUnsafeCaller
            || target.ReturnType != PrimitiveType.Void
            || target.ParameterCount != 2
            || !IsStandardInitializationReferenceParameter(
                target.Variables[0], program)
            || target.Variables[0].Type != function.Variables[0].Type
            || target.Variables[1].IsOut || target.Variables[1].IsInit
            || target.Variables[1].Type != function.Variables[1].Type
            || !IsOutputBodyValueType(target.Variables[1].Type)
            || boundCall.Type != PrimitiveType.Void
            || boundCall.Arguments.Length != target.ParameterCount
            || boundCall.ParameterIndex(0) != 0
            || boundCall.ParameterIndex(1) != 1
            || boundCall.Arguments[0] is not BoundReferenceReborrow {
                Descriptor: BoundRead {
                    Variable: 0,
                },
            } output
            || output.Type != target.Variables[0].Type
            || boundCall.Arguments[1] is not BoundRead {
                Variable: 1,
            } value
            || value.Type != target.Variables[1].Type)
        {
            return false;
        }

        call = boundCall;
        return true;
    }

    private static int ResolveStandardInitializationBodyMethodMapFunction(
        uint methodToken, TrRmImportTableReader imports,
        int[] functionIndexByMethodRow,
        ImmutableArray<BoundFunction>.Builder functions)
        => ResolveStandardInitializationBodyMethodMapFunction(methodToken,
            imports, functionIndexByMethodRow, functions, default,
            allowStandardNativeImport: false);

    private static int ResolveStandardInitializationBodyMethodMapFunction(
        uint methodToken, TrRmImportTableReader imports,
        int[] functionIndexByMethodRow,
        ImmutableArray<BoundFunction>.Builder functions,
        TrRmNativeImportTableReader standardNativeImports,
        bool allowStandardNativeImport,
        bool allowNativeImportModuleReference = false)
    {
        var tokenKind = checked((byte)(methodToken >> 24));
        var tokenRow = checked((int)(methodToken & 0x00FF_FFFF));
        var methodRow = tokenRow;
        TrRmImportBinding importBinding = default;
        if (tokenKind == 0x1C)
        {
            if (tokenRow <= 0 || tokenRow > imports.Count
                || imports.GetModuleRefForImportRow(tokenRow) <= 0)
            {
                throw Invalid(
                    "A Standard Body MethodMap Import row is invalid.");
            }

            var expectedDefinition
                = imports.GetExpectedDefinitionForImportRow(tokenRow);
            if ((expectedDefinition >> 24) != 0x16)
            {
                throw Invalid(
                    "A Standard Body MethodMap Import does not expect a MethodDef.");
            }

            methodRow = checked((int)(expectedDefinition & 0x00FF_FFFF));
            importBinding = imports.GetBindingForImportRow(tokenRow);
        }
        else if (tokenKind != 0x16)
        {
            throw Invalid(
                "A Standard Body MethodMap target is neither a local MethodDef nor an Import.");
        }

        if (methodRow <= 0
            || methodRow >= functionIndexByMethodRow.Length)
        {
            throw Invalid(
                "A Standard Body MethodMap target MethodDef row is invalid.");
        }

        var functionIndex = functionIndexByMethodRow[methodRow];
        if ((uint)functionIndex >= (uint)functions.Count)
        {
            throw Invalid(
                "A Standard Body MethodMap MethodDef has no reconstructed function.");
        }

        var function = functions[functionIndex];
        if (tokenKind == 0x1C)
        {
            var moduleImport = function.ModuleImport;
            if (!function.IsExternal || moduleImport is null
                || !StringComparer.Ordinal.Equals(
                    moduleImport.ProducerIdentity,
                    importBinding.ProducerIdentity)
                || !StringComparer.Ordinal.Equals(moduleImport.ExportKey,
                    importBinding.ExportKey)
                || moduleImport.TargetRequirement
                    != importBinding.TargetRequirement
                || moduleImport.RuntimePolicy != importBinding.RuntimePolicy)
            {
                throw Invalid(
                    "A Standard Body MethodMap Import does not bind its exact external MethodDef.");
            }
        }
        else if (function.ModuleImport is not null)
        {
            throw Invalid(
                "A Standard Body imported MethodDef must be named by its Import row.");
        }

        var standardNativeImport
            = allowStandardNativeImport && function.IsExternal
                && function.DirectNativeImport is not null
                && (function.ModuleImport is null
                    || (allowNativeImportModuleReference
                        && function.IsModuleReference
                        && function.ModuleImport is not null))
                && standardNativeImports.HasImportForMethod(methodRow);
        if (((function.IsExternal && function.ModuleImport is null)
                || function.DirectNativeImport is not null
                || function.NativeVTableMethod is not null)
            && !standardNativeImport)
        {
            throw Unsupported(
                "The Standard initialization-reference Body target is not a bound managed function.");
        }

        return functionIndex;
    }

    private static byte[] EncodeInitializationReferenceStoreBody(
        BoundFunction function, Func<PrimitiveType, int> typeSpecRowFor,
        ImmutableArray<StandardInitializationReferenceStore> stores,
        TrRmArtifactSchema schema)
    {
        if (stores.IsDefaultOrEmpty || stores.Length > byte.MaxValue)
        {
            throw Unsupported(
                "A Standard sequential init store Body exceeds its record-index subset.");
        }

        var typeTokens = function.Variables
            .Select(variable => GetStandardBodyTypeMapToken(
                variable.Type, typeSpecRowFor))
            .ToArray();
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        var homeMap = EncodeStandardInitializationHomeMap(function,
            typeTokens, maps);
        var entryState = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        var records = new List<ReadOnlyMemory<byte>>(stores.Length + 1) {
            new(entryState),
        };
        foreach (var store in stores)
        {
            records.Add(TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                2, 2, store.Transition, 1,
                checked((ushort)store.DescriptorVariable),
                checked((ushort)store.SourceVariable), schema));
        }

        var code = new byte[checked((stores.Length * 2) + 2)];
        for (var index = 0; index < stores.Length; index++)
        {
            code[index * 2] = 0x92;
            code[(index * 2) + 1] = checked((byte)(index + 1));
        }
        code[^2] = 0x18;
        code[^1] = 0;
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default,
            [new TrRmRegisterBodyBlock(0, checked((uint)code.Length), 0)],
            records, code, schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeInitializationReferenceConditionalStoreBody(
        BoundFunction function, Func<PrimitiveType, int> typeSpecRowFor,
        int conditionVariable,
        ImmutableArray<StandardInitializationReferenceStore> stores,
        TrRmArtifactSchema schema)
    {
        if (stores.IsDefaultOrEmpty)
        {
            throw Unsupported(
                "A Standard conditional init Body requires at least one output store.");
        }
        if (stores.Length > ushort.MaxValue - 2)
        {
            throw Unsupported(
                "The Standard conditional init Body exceeds its 16-bit store-record references.");
        }

        var typeTokens = function.Variables
            .Select(variable => GetStandardBodyTypeMapToken(
                variable.Type, typeSpecRowFor))
            .ToArray();
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens, []);
        var homeMap = EncodeStandardInitializationHomeMap(function,
            typeTokens, maps);
        var emptyEntry = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        var branchEntry = new byte[13];
        branchEntry[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(branchEntry.AsSpan(4), 1);
        branchEntry[8] = 0;
        branchEntry[9] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(branchEntry.AsSpan(10),
            maps.GetTypeMapIndex(typeTokens[conditionVariable]));
        branchEntry[12] = 0;
        var records = new List<ReadOnlyMemory<byte>>(
            checked(stores.Length + 3)) {
            new(emptyEntry),
            new(branchEntry),
        };
        records.Add(EncodeStore(stores[0], schema));
        records.Add(TrRmRegisterBodyRecordWriter.EncodeConditionalEdge(
            1, 2));
        for (var index = 1; index < stores.Length; index++)
        {
            records.Add(EncodeStore(stores[index], schema));
        }
        var storeInstructionLength = stores.Length == 1 ? 2 : 4;
        var trueBlockLength
            = checked((stores.Length * storeInstructionLength) + 2);
        var falseBlockStart = checked(6 + trueBlockLength);
        var code = new List<byte>(checked(falseBlockStart + 2)) {
            0xD5, 0, checked((byte)conditionVariable), 0,
            0xB1, 0x30,
        };
        for (var index = 0; index < stores.Length; index++)
        {
            var storeRecordIndex = index == 0 ? 2 : index + 3;
            if (stores.Length == 1)
            {
                code.Add(0x92);
                code.Add(checked((byte)storeRecordIndex));
            }
            else
            {
                var storeRecord = checked((ushort)storeRecordIndex);
                code.Add(0xD9);
                code.Add(0);
                code.Add((byte)storeRecord);
                code.Add((byte)(storeRecord >> 8));
            }
        }
        code.Add(0x17);
        code.Add(0);
        code.Add(0x17);
        code.Add(0);
        TrRmRegisterBodyBlock[] blocks = [
            new(0, 6, 0),
            new(6, checked((uint)trueBlockLength), 1),
            new(checked((uint)falseBlockStart), 2, 1),
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            default, default, default, blocks, records, code.ToArray(),
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());

        static byte[] EncodeStore(
            StandardInitializationReferenceStore store,
            TrRmArtifactSchema schema)
            => TrRmRegisterBodyRecordWriter.EncodeSemanticStore(
                2, 2, store.Transition, 1,
                checked((ushort)store.DescriptorVariable),
                checked((ushort)store.SourceVariable),
                schema);
    }

    private static byte[] EncodeInitializationReferenceConditionalCallBody(
        BoundFunction function, BoundCall call, int outputVariable,
        int fallbackVariable, Func<PrimitiveType, int> typeSpecRowFor,
        Func<int, uint> methodMapTokenForFunction,
        TrRmArtifactSchema schema)
    {
        var typeTokens = function.Variables
            .Select(variable => GetStandardBodyTypeMapToken(
                variable.Type, typeSpecRowFor))
            .ToArray();
        var targetMethodMapToken = methodMapTokenForFunction(call.Function);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            [targetMethodMapToken]);
        var homeMap = EncodeStandardInitializationHomeMap(function,
            typeTokens, maps);
        var placeSelection = TrRmRegisterBodyRecordWriter
            .EncodePlaceSelection(1, 3, 1, 0, 0, 0, 0, 0, 0,
                checked((uint)outputVariable),
                maps.GetTypeMapIndex(typeTokens[checked(outputVariable)]),
                schema, flags: 1,
                generalPredicateGraphsInitializationReferenceBodies
                    : schema.UsesGeneralPredicateGraphs());
        var inputs = new TrRmRegisterBodyCallInput[] {
            new(2, 1, 0),
            new(2, 1, 1),
            new(1, 5, 1),
        };
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 1,
            maps.GetMethodMapIndex(targetMethodMapToken), 0, inputs, [],
            schema: schema);
        var edge = TrRmRegisterBodyRecordWriter.EncodeConditionalEdge(1, 2);
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
            new(placeSelection),
            new(callSite),
            new(edge),
        ];
        byte[] code = [
            0xDD, 1, 1, 0,
            0x90, 2,
            0xB1, 0x30,
            0xB8, checked((byte)outputVariable),
            0xB8, checked((byte)fallbackVariable),
        ];
        TrRmRegisterBodyBlock[] blocks = [
            new(0, 8, 0),
            new(8, 2, 0),
            new(10, 2, 0),
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default, blocks, records, code,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeInitializationReferenceCallBody(
        BoundFunction function, BoundProgram program, BoundCall call,
        BoundExpression outputPlace,
        int outputVariable,
        Func<PrimitiveType, int> typeSpecRowFor,
        Func<int, uint> methodMapTokenForFunction,
        Func<BoundFieldRead, uint> fieldDefRowFor,
        Func<BoundFieldRead, uint> fieldPathRowFor,
        TrRmArtifactSchema schema)
    {
        var typeTokens = function.Variables
            .Select(variable => GetStandardBodyTypeMapToken(
                variable.Type, typeSpecRowFor))
            .ToArray();
        var targetMethodMapToken = methodMapTokenForFunction(call.Function);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            [targetMethodMapToken]);
        var homeMap = EncodeStandardInitializationHomeMap(function,
            typeTokens, maps);
        var referentTypeMapIndex = maps.GetTypeMapIndex(
            GetStandardBodyTypeMapToken(outputPlace.Type, typeSpecRowFor));
        var placeSelection = outputPlace switch
        {
            BoundRead { Variable: var variable }
                when variable == outputVariable
                => TrRmRegisterBodyRecordWriter
                .EncodePlaceSelection(1, 3, 1, 0, 0, 0, 0, 0, 0,
                    checked((uint)outputVariable), referentTypeMapIndex,
                    schema, flags: 1,
                    generalPredicateGraphsInitializationReferenceBodies
                        : schema.UsesGeneralPredicateGraphs()),
            BoundFieldRead {
                Receiver: BoundRead { Variable: var variable },
            } field when variable == outputVariable
                && !RequiresStandardInitializationFieldPath(field, program)
                => TrRmRegisterBodyRecordWriter
                .EncodePlaceSelection(2, 3, 1, 2, 0,
                    checked((ushort)outputVariable), 0, 1, 0,
                    fieldDefRowFor(field), referentTypeMapIndex, schema,
                    flags: 1,
                    generalPredicateGraphsInitializationReferenceBodies
                        : schema.UsesGeneralPredicateGraphs()),
            BoundFieldRead field when schema
                    .UsesNestedInitializationReferenceFieldDestinations()
                && TryGetInitializationFieldPath(field, out var root,
                    out var path)
                && root.Variable == outputVariable
                && path.Length > 0
                && RequiresStandardInitializationFieldPath(field, program)
                => TrRmRegisterBodyRecordWriter
                    .EncodePlaceSelection(2, 3, 1, 2, 0,
                        checked((ushort)outputVariable), 0, 4, 0,
                        fieldPathRowFor(field), referentTypeMapIndex, schema,
                        flags: 1,
                        generalPredicateGraphsInitializationReferenceBodies
                            : schema.UsesGeneralPredicateGraphs()),
            _ => throw Unsupported(
                "The Standard init-call Body requires a direct Home or one local field target."),
        };
        var inputs = new TrRmRegisterBodyCallInput[call.Arguments.Length];
        inputs[0] = new(1, 5, 0);
        for (var argumentIndex = 1;
            argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            inputs[argumentIndex] = new(2, 1,
                checked((ushort)(argumentIndex - 1)));
        }
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 0,
            maps.GetMethodMapIndex(targetMethodMapToken), 0, inputs, [],
            schema: schema);
        ReadOnlyMemory<byte>[] records;
        if (function.ReturnType != PrimitiveType.Void
            && outputPlace is BoundFieldRead readField)
        {
            var readFieldPathRequiresFieldPath
                = RequiresStandardInitializationFieldPath(readField,
                    program);
            var readPlaceSelection = TrRmRegisterBodyRecordWriter
                .EncodePlaceSelection(2, 1, 1, 2, 1,
                    checked((ushort)outputVariable), 0,
                    readFieldPathRequiresFieldPath ? (byte)4 : (byte)1, 0,
                    readFieldPathRequiresFieldPath
                        ? fieldPathRowFor(readField)
                        : fieldDefRowFor(readField),
                    referentTypeMapIndex, schema);
            records = [
                new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                new(placeSelection),
                new(callSite),
                new(readPlaceSelection),
            ];
        }
        else
        {
            records = [
                new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                new(placeSelection),
                new(callSite),
            ];
        }
        byte[] code = function.ReturnType == PrimitiveType.Void
            ? [0xDD, 0, 1, 0, 0x90, 2, 0x18, 0]
            : outputPlace is BoundFieldRead
            ? [
                0xDD, 0, 1, 0,
                0x90, 2,
                0xDA, 1, 3, 0,
                0x17, 1,
            ]
            : [
                0xDD, 0, 1, 0,
                0x90, 2,
                0xD5, 1, 0, 0,
                0x17, 1,
            ];
        if (function.ReturnType != PrimitiveType.Void
            && outputPlace is BoundRead)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(8),
                checked((ushort)outputVariable));
        }
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default,
            [new TrRmRegisterBodyBlock(0, (uint)code.Length, 0)],
            records, code, schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeInitializationReferenceForwardingCallBody(
        BoundFunction function, BoundCall call,
        Func<PrimitiveType, int> typeSpecRowFor,
        Func<int, uint> methodMapTokenForFunction,
        TrRmArtifactSchema schema)
    {
        var typeTokens = function.Variables
            .Select(variable => GetStandardBodyTypeMapToken(
                variable.Type, typeSpecRowFor))
            .ToArray();
        var targetMethodMapToken = methodMapTokenForFunction(call.Function);
        var maps = TrRmRegisterBodyMapBuilder.Create(typeTokens,
            [targetMethodMapToken]);
        var homeMap = EncodeStandardInitializationHomeMap(function,
            typeTokens, maps);
        TrRmRegisterBodyCallInput[] inputs = [
            new(2, 5, 0),
            new(2, 1, 1),
        ];
        var callSite = TrRmRegisterBodyRecordWriter.EncodeCallSite(1, 0,
            maps.GetMethodMapIndex(targetMethodMapToken), 0, inputs, [],
            schema: schema);
        ReadOnlyMemory<byte>[] records = [
            new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
            new(callSite),
        ];
        byte[] code = [0x90, 1, 0x18, 0];
        TrRmRegisterBodyBlock[] blocks = [
            new(0, (uint)code.Length, 0),
        ];
        return TrRmRegisterBodyWriter.Encode(homeMap, maps.TypeMap,
            maps.MethodMap, default, default, blocks, records, code,
            schema: schema,
            generalPredicateGraphsInitializationReferenceBodies
                : schema.UsesGeneralPredicateGraphs());
    }

    private static byte[] EncodeStandardInitializationHomeMap(
        BoundFunction function, IReadOnlyList<uint> typeTokens,
        TrRmRegisterBodyMaps maps)
    {
        if (typeTokens.Count != function.Variables.Length
            || function.Variables.Length > ushort.MaxValue)
        {
            throw Unsupported(
                $"Function '{function.Name}' has too many HomeMap entries.");
        }

        var homeMap = new byte[checked(function.Variables.Length * 8)];
        for (var variableIndex = 0;
            variableIndex < function.Variables.Length; variableIndex++)
        {
            var variable = function.Variables[variableIndex];
            var entry = homeMap.AsSpan(variableIndex * 8, 8);
            entry[0] = variableIndex < function.ParameterCount ? (byte)2 : (byte)3;
            entry[1] = variableIndex < function.ParameterCount
                ? (byte)0 : variable.IsMutable ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..],
                maps.GetTypeMapIndex(typeTokens[variableIndex]));
            if (variableIndex < function.ParameterCount)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(entry[4..],
                    checked((ushort)variableIndex));
            }
        }

        return homeMap;
    }

    private static uint GetStandardBodyTypeMapToken(
        PrimitiveType type, Func<PrimitiveType, int> typeSpecRowFor)
    {
        var row = typeSpecRowFor(type);
        if (row <= 0)
        {
            throw Unsupported(
                "A Standard initialization-reference Body type has no TypeSpec row.");
        }

        return CreateStandardBodyTypeMapToken(checked((uint)row));
    }

    private static uint GetStandardBodyTypeMapToken(
        PrimitiveType type, Func<PrimitiveType, uint> typeSpecRefFor)
    {
        var typeSpecRef = typeSpecRefFor(type);
        if (typeSpecRef == 0)
        {
            throw Unsupported(
                "A Standard initialization-reference Body type has no TypeSpec reference.");
        }

        return CreateStandardBodyTypeMapToken(typeSpecRef);
    }

    private static (Dictionary<int, PrimitiveType> TypeDefs,
        Dictionary<int, PrimitiveType> Imports)
        CreateForeignInterfaceBodyEnumTypeMaps(
            ImmutableArray<BoundEnumDefinition> enumDefinitions,
            BoundSourceTypeProviderSnapshot rootSnapshot)
    {
        var typeDefTypes = new Dictionary<int, PrimitiveType>();
        var importTypes = new Dictionary<int, PrimitiveType>();
        if (enumDefinitions.IsDefault)
        {
            throw Invalid(
                "The FNI Body enum table is uninitialized.");
        }

        for (var enumIndex = 0;
            enumIndex < enumDefinitions.Length; enumIndex++)
        {
            var definition = enumDefinitions[enumIndex];
            BoundStandardEnumOccurrence occurrence;
            if (definition.Identity
                is BoundEnumDeclarationIdentity.Standard standard)
            {
                occurrence = standard.Value;
            }
            else if (definition.Identity
                    is BoundEnumDeclarationIdentity.Core
                && definition.CoreStandardProviderBinding is
                    BoundCoreEnumStandardProviderBinding coreBinding)
            {
                if (!BoundCoreEnumStandardProviderBindingContract.IsValid(
                        definition, coreBinding))
                {
                    throw Invalid(
                        "An FNI Body Core enum has an invalid selected Standard provider binding.");
                }

                occurrence = coreBinding.StandardOccurrence;
            }
            else
            {
                continue;
            }

            var enumType = BoundEnumTypes.Type(enumIndex);
            var declaration = occurrence.Declaration;
            if (SameForeignInterfaceBodyProviderSnapshot(
                    declaration.Site.Snapshot, rootSnapshot))
            {
                AddTypeDef(declaration.Site.DefinitionId, enumType);
            }

            foreach (var expectation in occurrence.Expectations)
            {
                var binding = expectation.Binding;
                if (!SameForeignInterfaceBodyProviderSnapshot(
                        binding.UsingModule, rootSnapshot))
                {
                    continue;
                }

                AddTypeDef(binding.ExpectedDefinitionId, enumType);
                AddImport(binding.ImportRow, enumType);
            }
        }

        return (typeDefTypes, importTypes);

        static bool SameForeignInterfaceBodyProviderSnapshot(
            BoundSourceTypeProviderSnapshot left,
            BoundSourceTypeProviderSnapshot right)
            => StringComparer.Ordinal.Equals(left.SchemaIdentity,
                    right.SchemaIdentity)
                && left.ModuleRow == right.ModuleRow
                && StringComparer.Ordinal.Equals(left.ModuleIdentity,
                    right.ModuleIdentity)
                && left.GoverningRuleEdition
                    == right.GoverningRuleEdition
                && left.TargetRequirement == right.TargetRequirement
                && left.RuntimePolicy == right.RuntimePolicy
                && left.ArtifactSnapshot.AsSpan().SequenceEqual(
                    right.ArtifactSnapshot.AsSpan())
                && left.Features.AsSpan().SequenceEqual(
                    right.Features.AsSpan())
                && left.TargetProfiles.AsSpan().SequenceEqual(
                    right.TargetProfiles.AsSpan());

        void AddTypeDef(uint definitionId, PrimitiveType enumType)
        {
            if ((definitionId >> 24) != 0x13
                || (definitionId & 0x00FF_FFFF) == 0)
            {
                throw Invalid(
                    "An FNI Body enum view has an invalid TypeDef identity.");
            }

            var typeDefRow = checked((int)(definitionId & 0x00FF_FFFF));
            if (typeDefTypes.TryGetValue(typeDefRow, out var existing)
                && existing != enumType)
            {
                throw Invalid(
                    "An FNI Body TypeDef maps to multiple nominal enums.");
            }

            typeDefTypes[typeDefRow] = enumType;
        }

        void AddImport(int importRow, PrimitiveType enumType)
        {
            if (importRow <= 0
                || (importTypes.TryGetValue(importRow, out var existing)
                    && existing != enumType))
            {
                throw Invalid(
                    "An FNI Body Import maps to multiple nominal enums.");
            }

            importTypes[importRow] = enumType;
        }
    }

    private static PrimitiveType GetStandardInitializationBodyType(
        TrRmRegisterBodyReader body, ushort index,
        TrRmTypeSpecTableReader typeSpecs, TrRmImportTableReader imports,
        List<NativePointerTypeDefinition> nativePointerTypes,
        int[] structureTypeByTypeDef,
        List<CheckedReferenceTypeDefinition> checkedReferenceTypes,
        Dictionary<CheckedReferenceTypeDefinition, int>
            checkedReferenceTypeIndexes,
        IReadOnlyDictionary<uint, PrimitiveType>?
            nominalClassTypesByTypeSpecReference = null,
        IReadOnlyDictionary<int, PrimitiveType>?
            nominalEnumTypesByTypeDefRow = null,
        IReadOnlyDictionary<int, PrimitiveType>?
            nominalEnumTypesByImportRow = null)
    {
        if (index >= body.TypeCount)
        {
            throw Invalid("A Standard initialization Body TypeMap index is invalid.");
        }

        var typeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
            body.TypeMap[(index * sizeof(uint))..]);
        return FromNativeValueRow(typeSpecs,
            NormalizeStandardRegisteredQueryTypeSpecRef(typeSpecRef),
            nativePointerTypes, structureTypeByTypeDef, imports,
            checkedReferenceTypes, checkedReferenceTypeIndexes,
            nominalClassTypesByTypeSpecReference,
            nominalEnumTypesByTypeDefRow:
                nominalEnumTypesByTypeDefRow,
            nominalEnumTypesByImportRow:
                nominalEnumTypesByImportRow);
    }

    private static BoundFieldRead DecodeStandardInitializationFieldPath(
        int fieldPathRow, int rootVariable, PrimitiveType rootType,
        PrimitiveType expectedLeafType,
        ImmutableArray<BoundStruct> structures,
        TrRmTypeDefTableReader typeDefs,
        TrRmFieldDefTableReader fields,
        TrRmFieldPathTableReader fieldPaths,
        TrRmTypeSpecTableReader typeSpecs,
        TrRmImportTableReader imports,
        List<NativePointerTypeDefinition> nativePointerTypes,
        int[] structureTypeByTypeDef,
        List<CheckedReferenceTypeDefinition> checkedReferenceTypes,
        Dictionary<CheckedReferenceTypeDefinition, int>
            checkedReferenceTypeIndexes,
        IReadOnlyDictionary<uint, PrimitiveType>?
            nominalClassTypesByTypeSpecReference = null,
        IReadOnlyDictionary<int, PrimitiveType>?
            nominalEnumTypesByTypeDefRow = null,
        IReadOnlyDictionary<int, PrimitiveType>?
            nominalEnumTypesByImportRow = null)
    {
        var rootTypeSpecRef = fieldPaths.GetRootTypeSpecRef(fieldPathRow);
        var resolvedRootType = FromNativeValueRow(typeSpecs,
            rootTypeSpecRef, nativePointerTypes, structureTypeByTypeDef,
            imports, checkedReferenceTypes, checkedReferenceTypeIndexes,
            nominalClassTypesByTypeSpecReference,
            nominalEnumTypesByTypeDefRow:
                nominalEnumTypesByTypeDefRow,
            nominalEnumTypesByImportRow:
                nominalEnumTypesByImportRow);
        if (resolvedRootType != rootType)
        {
            throw Invalid(
                "A Standard initialization FieldPath root differs from its Home type.");
        }

        var currentOwnerTypeDef = typeSpecs.GetNominalTypeDefRow(
            rootTypeSpecRef, typeDefs, imports);
        BoundExpression current = new BoundRead(rootVariable, rootType,
            default);
        var fieldCount = fieldPaths.GetFieldCount(fieldPathRow);
        if (fieldCount <= 0)
        {
            throw Invalid(
                "A Standard initialization FieldPath is empty.");
        }

        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            var fieldReference = fieldPaths.GetFieldReference(
                fieldPathRow, ordinal);
            var referenceKind = fieldReference >> 24;
            var referenceRow = checked((int)(
                fieldReference & 0x00FF_FFFF));
            int fieldRow;
            if (referenceKind == 0x14)
            {
                fieldRow = referenceRow;
            }
            else if (referenceKind == 0x1C)
            {
                var expectedDefinition
                    = imports.GetExpectedDefinitionForImportRow(
                        referenceRow);
                if (expectedDefinition >> 24 != 0x14)
                {
                    throw Invalid(
                        "A Standard initialization FieldPath Import is not a FieldDef.");
                }

                fieldRow = checked((int)(
                    expectedDefinition & 0x00FF_FFFF));
            }
            else
            {
                throw Invalid(
                    "A Standard initialization FieldPath step has an invalid reference kind.");
            }

            var fieldOwnerTypeDef = fields.GetOwnerTypeDef(fieldRow);
            if (fieldOwnerTypeDef != currentOwnerTypeDef)
            {
                throw Invalid(
                    "A Standard initialization FieldPath step has a discontinuous owner.");
            }

            var (firstField, endField)
                = typeDefs.GetFieldRange(fieldOwnerTypeDef);
            if (fieldRow < firstField || fieldRow >= endField)
            {
                throw Invalid(
                    "A Standard initialization FieldPath step is outside its owner's FieldDef range.");
            }

            var fieldTypeSpecRef = fields.GetTypeSpecRef(fieldRow);
            var fieldType = FromNativeValueRow(typeSpecs,
                fieldTypeSpecRef, nativePointerTypes,
                structureTypeByTypeDef, imports, checkedReferenceTypes,
                checkedReferenceTypeIndexes,
                nominalClassTypesByTypeSpecReference,
                nominalEnumTypesByTypeDefRow:
                    nominalEnumTypesByTypeDefRow,
                nominalEnumTypesByImportRow:
                    nominalEnumTypesByImportRow);
            if (ordinal + 1 == fieldCount)
            {
                if (fieldType != expectedLeafType)
                {
                    throw Invalid(
                        "A Standard initialization FieldPath leaf differs from its TypeMap.");
                }
            }
            else
            {
                var fieldTypeRef = typeSpecs.GetTypeSpecRef(
                    fieldTypeSpecRef);
                if (!fieldTypeRef.IsRow
                    || fieldTypeRef.Form is not (0x02 or 0x03)
                    || fieldTypeRef.Qualifiers != 0)
                {
                    throw Invalid(
                        "A Standard initialization FieldPath intermediate is not an inline struct type.");
                }

                currentOwnerTypeDef = typeSpecs.GetNominalTypeDefRow(
                    fieldTypeSpecRef, typeDefs, imports);
            }

            current = new BoundFieldRead(current, fieldRow - firstField,
                fieldType, default);
        }

        var result = (BoundFieldRead)current;
        if (!TryGetInitializationFieldPath(result, out var boundRoot,
                out var boundPath)
            || boundRoot.Variable != rootVariable
            || boundRoot.Type != rootType
            || !HasSupportedStandardInitializationFieldPathGeometry(
                rootType, boundPath, structures, checkedReferenceTypes, [],
                nativeLoweringAgreement: null))
        {
            throw Invalid(
                "A Standard initialization FieldPath has an unsupported layout or underaligned leaf.");
        }

        return result;
    }

    private static void DecodeStandardNativePointerLoadHomeStore(
        TrRmRegisterBodyReader body, int sourceRegister, int homeIndex,
        int parameterCount,
        ImmutableArray<BoundVariable>.Builder variables,
        int[] homeVariables, byte[] homeRoles, PrimitiveType[] homeTypes,
        ImmutableArray<BoundStruct> structures,
        bool allowAggregatePointees,
        Dictionary<int, BoundExpression> valueByRegister,
        HashSet<int> initializedValueLocals,
        ImmutableArray<BoundStatement>.Builder statements)
    {
        if (!body.UsesNativePointerLoads || sourceRegister >= 14
            || (uint)homeIndex >= (uint)homeVariables.Length
            || homeRoles[homeIndex] != 3)
        {
            throw Invalid(
                "A pointer-load Body local store has an invalid source or Home.");
        }

        var variable = homeVariables[homeIndex];
        if (variable < parameterCount
            || (uint)variable >= (uint)variables.Count
            || variables[variable].IsOut || variables[variable].IsInit
            || variables[variable].Type != homeTypes[homeIndex]
            || !valueByRegister.TryGetValue(sourceRegister,
                out var initializer)
            || initializer.Type != homeTypes[homeIndex]
            || !IsStandardNativePointerLoadValueType(initializer.Type,
                structures, allowAggregatePointees)
            || (initializedValueLocals.Contains(variable)
                && !variables[variable].IsMutable))
        {
            throw Invalid(
                "A pointer-load Body local initializer does not match its Home.");
        }

        if (initializedValueLocals.Add(variable))
        {
            statements.Add(new BoundLocal(variable, initializer, default));
        }
        else
        {
            statements.Add(new BoundAssignment(variable, initializer,
                default));
        }
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardInitializationReferenceBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeDefTableReader typeDefs,
            TrRmFieldDefTableReader fields,
            TrRmFieldPathTableReader fieldPaths,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmImportTableReader imports,
            TrRmNativeImportTableReader standardNativeImports,
            List<CheckedReferenceTypeDefinition> checkedReferenceTypes,
            Dictionary<CheckedReferenceTypeDefinition, int>
                checkedReferenceTypeIndexes,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] structureTypeByTypeDef,
            ImmutableArray<BoundStruct> structures,
            ImmutableArray<BoundFunction>.Builder functions,
            int[] functionIndexByMethodRow,
            bool construction2MethodLinking = false,
            ImmutableArray<BoundConstant> scalarConstants = default,
            IReadOnlyDictionary<uint, PrimitiveType>?
                nominalClassTypesByTypeSpecReference = null,
            IReadOnlyDictionary<int, PrimitiveType>?
                nominalEnumTypesByTypeDefRow = null,
            IReadOnlyDictionary<int, PrimitiveType>?
                nominalEnumTypesByImportRow = null,
            IReadOnlyDictionary<int, ForeignNativeInterfaceBodyTarget>?
                foreignInterfaceBodyTargets = null)
    {
        var foreignInterfaceConditionalOutputBody
            = body.Schema.UsesForeignNativeInterface()
                && body.BlockCount == 3
                && IsStandardForeignInterfaceConditionalOutputEntry(
                    body.GetBlockCode(0));
        var foreignInterfaceDirectCallBody
            = body.Schema.UsesForeignNativeInterface()
                && !body.UsesNativePointerLoads
                && body.BlockCount == 1;
        if (body.BlockCount is not (1 or 3) || body.HasExceptionDirectory
            || body.FieldCount != 0
            || (body.ConstantCount != 0
                && !construction2MethodLinking
                && !foreignInterfaceConditionalOutputBody
                && !foreignInterfaceDirectCallBody)
            || (body.ConstantCount != 0
                && (scalarConstants.IsDefault
                    || scalarConstants.Length != body.ConstantCount))
            || (construction2MethodLinking && scalarConstants.IsDefault)
            || body.HomeCount < function.ParameterCount
            || (body.UsesNativePointerLoads
                ? body.HomeCount > SemanticVerifier.MaximumVariables
                : body.Schema.UsesForeignNativeInterface()
                    ? body.HomeCount > SemanticVerifier.MaximumVariables
                    : body.HomeCount > function.ParameterCount + 1)
            || body.MethodCount > (body.UsesNativePointerLoads
                ? 14
                : body.Schema.UsesForeignNativeInterface() ? 14 : 1))
        {
            throw Unsupported(
                "The Standard initialization-reference Body consumer requires one scalar Block and direct Homes.");
        }

        var allowAggregatePointerLoads
            = body.UsesNativePointerLoads
                && body.Schema.UsesLocalAggregateLayout();
        var homeVariables = new int[body.HomeCount];
        var homeRoles = new byte[body.HomeCount];
        var homeTypes = new PrimitiveType[body.HomeCount];
        var homeTypeMapIndexes = new ushort[body.HomeCount];
        Array.Fill(homeVariables, -1);
        var parameterHomes = new bool[function.ParameterCount];

        PrimitiveType GetBodyTypeMapType(ushort typeMapIndex)
            => GetStandardInitializationBodyType(body, typeMapIndex,
                typeSpecs, imports, nativePointerTypes,
                structureTypeByTypeDef, checkedReferenceTypes,
                checkedReferenceTypeIndexes,
                nominalClassTypesByTypeSpecReference,
                nominalEnumTypesByTypeDefRow,
                nominalEnumTypesByImportRow);

        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var entry = body.HomeMap.Slice(homeIndex * 8, 8);
            var role = entry[0];
            var flags = entry[1];
            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                entry[2..]);
            var formalOrdinal = BinaryPrimitives.ReadUInt16LittleEndian(
                entry[4..]);
            var homeType = GetBodyTypeMapType(typeMapIndex);
            homeRoles[homeIndex] = role;
            homeTypes[homeIndex] = homeType;
            homeTypeMapIndexes[homeIndex] = typeMapIndex;
            if (role == 2)
            {
                if (formalOrdinal >= function.ParameterCount
                    || parameterHomes[(int)formalOrdinal]
                    || variables[formalOrdinal].Type != homeType)
                {
                    throw Invalid(
                        "A Standard initialization Body formal Home does not match its signature.");
                }

                parameterHomes[(int)formalOrdinal] = true;
                homeVariables[homeIndex] = formalOrdinal;
            }
            else if (role == 3)
            {
                var local = variables.Count;
                variables.Add(new BoundVariable($"home{homeIndex + 1}",
                    homeType, flags != 0, default));
                homeVariables[homeIndex] = local;
            }
            else
            {
                throw Unsupported(
                    "The Standard initialization-reference Body subset does not use receiver Homes.");
            }
        }

        if (parameterHomes.Any(static hasHome => !hasHome))
        {
            throw Invalid(
                "A Standard initialization Body is missing a formal Home.");
        }

        if (body.BlockCount == 3)
        {
            var entryCode = body.GetBlockCode(0);
            if (foreignInterfaceConditionalOutputBody)
            {
                return DecodeStandardForeignInterfaceConditionalOutputBody(
                    body, function, variables, typeSpecs, imports,
                    nativePointerTypes, structureTypeByTypeDef,
                    checkedReferenceTypes, checkedReferenceTypeIndexes,
                    nominalClassTypesByTypeSpecReference,
                    nominalEnumTypesByTypeDefRow,
                    nominalEnumTypesByImportRow,
                    homeVariables, homeRoles, homeTypes,
                    scalarConstants, foreignInterfaceBodyTargets);
            }

            return body.GetBlockCode(0)[0] switch {
                0xD5 =>
                    DecodeStandardInitializationReferenceConditionalBody(
                        body, function, variables, checkedReferenceTypes,
                        homeVariables, homeRoles, homeTypes,
                        homeTypeMapIndexes),
                0xDD =>
                    DecodeStandardInitializationReferenceConditionalCallBody(
                        body, function, variables, typeSpecs, imports,
                        checkedReferenceTypes, checkedReferenceTypeIndexes,
                        nativePointerTypes, structureTypeByTypeDef,
                        functions, functionIndexByMethodRow, homeVariables,
                        homeRoles, homeTypes, homeTypeMapIndexes),
                _ => throw Unsupported(
                    "The Standard init conditional Body entry block is unsupported."),
            };
        }

        var bodyVariableDefinitions = variables.ToImmutable();
        var entryStateIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetBlock(0)[8..]);
        var entryState = body.GetRecord(entryStateIndex);
        if (BinaryPrimitives.ReadUInt32LittleEndian(entryState[4..]) != 0)
        {
            throw Unsupported(
                "The Standard initialization-reference Body subset has no incoming registers.");
        }

        var statements = ImmutableArray.CreateBuilder<BoundStatement>();
        var outputPlaceByRegister = new Dictionary<int, BoundExpression>();
        var valueByRegister = new Dictionary<int, BoundExpression>();
        var valueRegisterWriteOrder = new Dictionary<int, int>();
        var pendingUpdatesByStoreRegister = new Dictionary<int,
            (int Variable, int ResultRegister, TokenKind Operator,
             PrimitiveType Type, BoundIntegerOverflowBehavior OverflowBehavior,
             bool IsPrefix)>();
        (int Destination, int Source)? pendingUpdateMove = null;
        var initializedLocals = new HashSet<int>();
        var initializedValueLocals = new HashSet<int>();
        var stores = 0;
        var storedDescriptor = -1;
        var calls = 0;
        var pointerLoads = 0;
        var conversions = 0;
        var returns = 0;
        var foreignInterfaceCalls = 0;
        var foreignInterfaceNonReturningCalls = 0;
        var foreignInterfaceOutputReservations = 0;
        var callSiteValueResultRegisters = new List<ushort>();
        var forwardedInitializationReference = false;
        var pendingForeignInterfaceComparison
            = ((BoundExpression Left, BoundExpression Right, int Register)?)null;
        var nativeOutputReservationsByRecord
            = new Dictionary<int, (int CallSiteRecordIndex, int HomeIndex,
                uint FormalOrdinal)>();
        var nativeOutputReservationsByCallSiteFormal
            = new Dictionary<(int CallSiteRecordIndex, uint FormalOrdinal),
                int>();
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x0C)
            {
                continue;
            }

            var payload = record[4..];
            var callSiteRecordIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            var homeIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                payload[2..]);
            var formalOrdinal = BinaryPrimitives.ReadUInt32LittleEndian(
                payload[4..]);
            if (!nativeOutputReservationsByRecord.TryAdd(recordIndex,
                    (callSiteRecordIndex, homeIndex, formalOrdinal))
                || !nativeOutputReservationsByCallSiteFormal.TryAdd(
                    (callSiteRecordIndex, formalOrdinal), recordIndex))
            {
                throw Invalid(
                    "A Standard Body has duplicate NativeOutput reservation identities.");
            }
        }

        var activeNativeOutputReservations = new HashSet<int>();
        var code = body.GetBlockCode(0);

        void TrackForeignInterfaceUpdate(BoundBinary binary,
            int destinationRegister, int? immediateHomeStoreIndex)
        {
            if (!TryGetForeignInterfaceScalarUpdateFromBinary(binary,
                    out var update))
            {
                if (pendingUpdateMove is not null)
                {
                    throw Invalid(
                        "A Standard F+I postfix update move is not followed by its arithmetic operation.");
                }

                return;
            }

            var place = (BoundRead)update.Place;
            if (immediateHomeStoreIndex != place.Variable)
            {
                if (pendingUpdateMove is not null)
                {
                    throw Invalid(
                        "A Standard F+I postfix update arithmetic result is not stored back to its source Home.");
                }

                return;
            }

            if ((uint)place.Variable >= (uint)variables.Count
                || !initializedValueLocals.Contains(place.Variable)
                || !variables[place.Variable].IsMutable)
            {
                if (pendingUpdateMove is not null)
                {
                    throw Invalid(
                        "A Standard F+I postfix update does not target an initialized mutable local.");
                }

                return;
            }

            var resultRegister = destinationRegister;
            var isPrefix = true;
            if (pendingUpdateMove is { } move)
            {
                if (move.Destination != destinationRegister
                    || !valueByRegister.TryGetValue(move.Source,
                        out var previousValue)
                    || previousValue is not BoundRead previousRead
                    || previousRead.Variable != place.Variable)
                {
                    throw Invalid(
                        "A Standard F+I postfix update does not preserve its old value.");
                }

                resultRegister = move.Source;
                isPrefix = false;
                pendingUpdateMove = null;
            }

            if (!pendingUpdatesByStoreRegister.TryAdd(
                    destinationRegister,
                    (place.Variable, resultRegister, update.Operator,
                        update.Type, update.OverflowBehavior, isPrefix)))
            {
                throw Invalid(
                    "A Standard F+I update result is stored more than once.");
            }
        }

        static int? GetImmediateHomeStoreIndex(ReadOnlySpan<byte> code,
            int instructionPosition)
        {
            if (instructionPosition > code.Length - 4
                || code[instructionPosition] != 0xD6
                || code[instructionPosition + 1] >> 4 != 1)
            {
                return null;
            }

            return BinaryPrimitives.ReadUInt16LittleEndian(
                code[(instructionPosition + 2)..]);
        }

        for (var position = 0; position < code.Length;)
        {
            var opcode = code[position];
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                opcode, body.Schema, body.UsesNativePointerLoads);
            var instruction = code.Slice(position, instructionLength);
            if (pendingForeignInterfaceComparison is not null
                && opcode != 0xC3)
            {
                throw Invalid(
                    "A Standard F+I relation comparison is not immediately followed by SetCondition.");
            }
            if (pendingUpdateMove is not null
                && opcode is not (0x21 or 0x22 or 0xC0))
            {
                throw Invalid(
                    "A Standard F+I postfix update copy is not followed by arithmetic.");
            }
            if (pendingUpdatesByStoreRegister.Count != 0
                && opcode != 0xD6)
            {
                throw Invalid(
                    "A Standard F+I update is not immediately stored to its mutable Home.");
            }

            switch (opcode)
            {
                case 0x10:
                {
                    if (!foreignInterfaceDirectCallBody)
                    {
                        throw Unsupported(
                            "A Standard initialization-reference Body does not use direct register moves.");
                    }

                    var destinationRegister = instruction[1] & 0x0F;
                    var sourceRegister = instruction[1] >> 4;
                    if (pendingUpdateMove is not null
                        || destinationRegister is 0 or >= 14
                        || sourceRegister is 0 or >= 14
                        || valueByRegister.ContainsKey(
                            destinationRegister)
                        || !valueByRegister.TryGetValue(sourceRegister,
                            out var value))
                    {
                        throw Invalid(
                            "A Standard F+I register move has an invalid source or destination.");
                    }

                    valueByRegister[destinationRegister] = value;
                    valueRegisterWriteOrder[destinationRegister] = position;
                    pendingUpdateMove
                        = (destinationRegister, sourceRegister);
                    break;
                }
                case 0x11:
                {
                    var destinationRegister = instruction[1] & 0x0F;
                    var formalOrdinal = instruction[1] >> 4;
                    if (destinationRegister >= 14
                        || formalOrdinal >= function.ParameterCount
                        || formalOrdinal >= homeVariables.Length
                        || homeRoles[formalOrdinal] != 2
                        || homeVariables[formalOrdinal] != formalOrdinal
                        || variables[formalOrdinal].IsOut
                        || variables[formalOrdinal].IsInit)
                    {
                        throw Invalid(
                            "A Standard initialization Body formal load does not match its Home.");
                    }

                    valueByRegister[destinationRegister] = new BoundRead(
                        formalOrdinal, variables[formalOrdinal].Type, default);
                    valueRegisterWriteOrder[destinationRegister] = position;
                    break;
                }
                case 0x16:
                {
                    if (!foreignInterfaceDirectCallBody)
                    {
                        throw Unsupported(
                            "A Standard initialization-reference Body does not use direct ConstantMap loads.");
                    }

                    var destinationRegister = instruction[1] & 0x0F;
                    var constantIndex = instruction[1] >> 4;
                    if (destinationRegister is 0 or >= 14
                        || constantIndex >= scalarConstants.Length
                        || !DirectNativeImportContract.IsScalar(
                            scalarConstants[constantIndex].Type)
                        || !valueByRegister.TryAdd(destinationRegister,
                            scalarConstants[constantIndex]))
                    {
                        throw Invalid(
                            "A Standard F+I ConstantMap load has an invalid register or typed scalar entry.");
                    }

                    valueRegisterWriteOrder[destinationRegister] = position;
                    break;
                }
                case >= 0x20 and <= 0x2B:
                case >= 0x30 and <= 0x33:
                case >= 0x34 and <= 0x36:
                {
                    if (!foreignInterfaceDirectCallBody)
                    {
                        throw Unsupported(
                            "A Standard initialization-reference Body does not use direct scalar arithmetic operations.");
                    }

                    var destinationRegister = instruction[1] & 0x0F;
                    var sourceRegister = instruction[1] >> 4;
                    if (destinationRegister >= 14
                        || sourceRegister >= 14
                        || callSiteValueResultRegisters.Contains(
                            destinationRegister)
                        || outputPlaceByRegister.ContainsKey(
                            destinationRegister))
                    {
                        throw Invalid(
                            "A Standard F+I scalar operation has an invalid or reserved result register.");
                    }

                    if (opcode is 0x20 or 0x26 or 0x33)
                    {
                        if (!valueByRegister.TryGetValue(sourceRegister,
                                out var operand))
                        {
                            throw Invalid(
                                "A Standard F+I unary operation reads an uninitialized register.");
                        }

                        var unaryOperator = opcode == 0x33
                            ? operand.Type == PrimitiveType.Bool
                                ? TokenKind.Bang : TokenKind.Tilde
                            : TokenKind.Minus;
                        var unary = new BoundUnary(unaryOperator, operand,
                            operand.Type, default) {
                            OverflowBehavior = opcode == 0x20
                                ? BoundIntegerOverflowBehavior.Wrapping
                                : BoundIntegerOverflowBehavior.Checked,
                        };
                        if (!TryGetForeignInterfaceScalarUnaryEncoding(
                                unary, out var expectedOpcode,
                                out var expectedControl)
                            || expectedOpcode != opcode
                            || expectedControl != 0)
                        {
                            throw Invalid(
                                "A Standard F+I unary operation has an unsupported type or overflow policy.");
                        }

                        valueByRegister[destinationRegister] = unary;
                        valueRegisterWriteOrder[destinationRegister] = position;
                        break;
                    }

                    if (!valueByRegister.TryGetValue(destinationRegister,
                            out var left)
                        || !valueByRegister.TryGetValue(sourceRegister,
                            out var right))
                    {
                        throw Invalid(
                            "A Standard F+I binary operation reads an uninitialized register.");
                    }

                    var operation = opcode switch {
                        0x21 or 0x27 => TokenKind.Plus,
                        0x22 or 0x28 => TokenKind.Minus,
                        0x23 or 0x29 => TokenKind.Star,
                        0x2A => TokenKind.Slash,
                        0x2B => TokenKind.Percent,
                        0x30 => TokenKind.Ampersand,
                        0x31 => TokenKind.Pipe,
                        0x32 => TokenKind.Caret,
                        0x34 => TokenKind.ShiftLeft,
                        0x35 => TokenKind.ShiftRight,
                        0x36 => TokenKind.ShiftRightUnsigned,
                        0x34 => TokenKind.ShiftLeft,
                        0x35 => TokenKind.ShiftRight,
                        0x36 => TokenKind.ShiftRightUnsigned,
                        _ => throw Unsupported(
                            "A Standard F+I binary operation uses an unsupported opcode."),
                    };
                    var binary = new BoundBinary(operation, left, right,
                        left.Type, default) {
                        OverflowBehavior = opcode is 0x21 or 0x22 or 0x23
                            ? BoundIntegerOverflowBehavior.Wrapping
                            : BoundIntegerOverflowBehavior.Checked,
                    };
                    if (!TryGetForeignInterfaceScalarBinaryEncoding(binary,
                            out var expectedOpcode,
                            out var expectedControl)
                        || expectedOpcode != opcode
                        || expectedControl != 0)
                    {
                        throw Invalid(
                            "A Standard F+I binary operation has incompatible types or overflow policy.");
                    }

                    valueByRegister[destinationRegister] = binary;
                    valueRegisterWriteOrder[destinationRegister] = position;
                    TrackForeignInterfaceUpdate(binary,
                        destinationRegister,
                        GetImmediateHomeStoreIndex(code,
                            position + instructionLength));
                    break;
                }
                case 0xC0:
                {
                    if (!foreignInterfaceDirectCallBody)
                    {
                        throw Unsupported(
                            "A Standard initialization-reference Body does not use direct scalar arithmetic operations.");
                    }

                    var control = instruction[1];
                    var payload = BinaryPrimitives
                        .ReadUInt16LittleEndian(instruction[2..]);
                    var destinationRegister = payload & 0x0F;
                    var result1 = (payload >> 4) & 0x0F;
                    var source0Register = (payload >> 8) & 0x0F;
                    var source1Register = payload >> 12;
                    if (destinationRegister >= 14 || result1 != 15
                        || source0Register >= 14
                        || callSiteValueResultRegisters.Contains(
                            destinationRegister)
                        || outputPlaceByRegister.ContainsKey(
                            destinationRegister))
                    {
                        throw Invalid(
                            "A Standard F+I checked arithmetic operation has an invalid or reserved result register.");
                    }

                    if (control == 0x04)
                    {
                        if (source1Register != 15
                            || !valueByRegister.TryGetValue(source0Register,
                                out var operand))
                        {
                            throw Invalid(
                                "A Standard F+I checked unary operation has an invalid input.");
                        }

                        var unary = new BoundUnary(TokenKind.Minus, operand,
                            operand.Type, default) {
                            OverflowBehavior
                                = BoundIntegerOverflowBehavior.Checked,
                        };
                        if (!TryGetForeignInterfaceScalarUnaryEncoding(
                                unary, out var expectedOpcode,
                                out var expectedControl)
                            || expectedOpcode != 0xC0
                            || expectedControl != control)
                        {
                            throw Invalid(
                                "A Standard F+I checked unary operation has an unsupported type or overflow policy.");
                        }

                        valueByRegister[destinationRegister] = unary;
                        valueRegisterWriteOrder[destinationRegister] = position;
                        break;
                    }

                    var operation = control switch {
                        0x01 => TokenKind.Plus,
                        0x02 => TokenKind.Minus,
                        0x03 => TokenKind.Star,
                        0x0C => TokenKind.Slash,
                        0x0D => TokenKind.Percent,
                        _ => throw Unsupported(
                            "A Standard F+I checked arithmetic operation uses an unsupported control."),
                    };
                    if (source1Register >= 14
                        || !valueByRegister.TryGetValue(source0Register,
                            out var left)
                        || !valueByRegister.TryGetValue(source1Register,
                            out var right))
                    {
                        throw Invalid(
                            "A Standard F+I checked arithmetic operation reads an uninitialized register.");
                    }

                    var binary = new BoundBinary(operation, left, right,
                        left.Type, default) {
                        OverflowBehavior
                            = BoundIntegerOverflowBehavior.Checked,
                    };
                    if (!TryGetForeignInterfaceScalarBinaryEncoding(binary,
                            out var expectedBinaryOpcode,
                            out var expectedBinaryControl)
                        || expectedBinaryOpcode != 0xC0
                        || expectedBinaryControl != control)
                    {
                        throw Invalid(
                            "A Standard F+I checked arithmetic operation has incompatible types or overflow policy.");
                    }

                    valueByRegister[destinationRegister] = binary;
                    valueRegisterWriteOrder[destinationRegister] = position;
                    TrackForeignInterfaceUpdate(binary,
                        destinationRegister,
                        GetImmediateHomeStoreIndex(code,
                            position + instructionLength));
                    break;
                }
                case 0xC2:
                {
                    if (!foreignInterfaceDirectCallBody)
                    {
                        throw Unsupported(
                            "A Standard initialization-reference Body does not use direct scalar comparison operations.");
                    }

                    var control = instruction[1];
                    var payload = BinaryPrimitives
                        .ReadUInt16LittleEndian(instruction[2..]);
                    var destinationRegister = payload & 0x0F;
                    var result1 = (payload >> 4) & 0x0F;
                    var source0Register = (payload >> 8) & 0x0F;
                    var source1Register = payload >> 12;
                    if (destinationRegister >= 14 || result1 != 15
                        || source0Register >= 14 || source1Register >= 14
                        || callSiteValueResultRegisters.Contains(
                            destinationRegister)
                        || outputPlaceByRegister.ContainsKey(
                            destinationRegister)
                        || !valueByRegister.TryGetValue(source0Register,
                            out var left)
                        || !valueByRegister.TryGetValue(source1Register,
                            out var right))
                    {
                        throw Invalid(
                            "A Standard F+I Boolean comparison has an invalid or unavailable register.");
                    }

                    if (control == 0)
                    {
                        if (destinationRegister != source0Register
                            || left.Type != right.Type
                            || !TryGetForeignInterfaceScalarComparisonPredicateMask(
                                left.Type, TokenKind.EqualsEquals, out _))
                        {
                            throw Invalid(
                                "A Standard F+I relation comparison has incompatible operands or result mapping.");
                        }

                        pendingForeignInterfaceComparison
                            = (left, right, destinationRegister);
                        _ = valueByRegister.Remove(destinationRegister);
                        break;
                    }

                    var operation = control switch {
                        0x01 => TokenKind.EqualsEquals,
                        0x02 => TokenKind.BangEquals,
                        _ => throw Unsupported(
                            "A Standard F+I Boolean comparison uses an unsupported control."),
                    };
                    var binary = new BoundBinary(operation, left, right,
                        PrimitiveType.Bool, default) {
                        OverflowBehavior
                            = BoundIntegerOverflowBehavior.Checked,
                    };
                    if (!TryGetForeignInterfaceScalarBinaryEncoding(binary,
                            out var expectedOpcode,
                            out var expectedControl)
                        || expectedOpcode != 0xC2
                        || expectedControl != control)
                    {
                        throw Invalid(
                            "A Standard F+I Boolean comparison has incompatible operands.");
                    }

                    valueByRegister[destinationRegister] = binary;
                    valueRegisterWriteOrder[destinationRegister] = position;
                    break;
                }
                case 0xC3:
                {
                    if (!foreignInterfaceDirectCallBody
                        || pendingForeignInterfaceComparison is not
                            { } pendingComparison)
                    {
                        throw Invalid(
                            "A Standard F+I SetCondition has no pending relation comparison.");
                    }

                    var predicateMask = instruction[1];
                    var payload = BinaryPrimitives
                        .ReadUInt16LittleEndian(instruction[2..]);
                    var destinationRegister = payload & 0x0F;
                    var conditionRegister = (payload >> 4) & 0x0F;
                    var unused0 = (payload >> 8) & 0x0F;
                    var unused1 = payload >> 12;
                    if (destinationRegister != pendingComparison.Register
                        || conditionRegister != pendingComparison.Register
                        || unused0 != 15 || unused1 != 15
                        || !TryGetForeignInterfaceScalarComparisonOperator(
                            pendingComparison.Left.Type, predicateMask,
                            out var operation))
                    {
                        throw Invalid(
                            "A Standard F+I SetCondition does not match its relation comparison.");
                    }

                    valueByRegister[destinationRegister] = new BoundBinary(
                        operation, pendingComparison.Left,
                        pendingComparison.Right, PrimitiveType.Bool, default) {
                        OverflowBehavior
                            = BoundIntegerOverflowBehavior.Checked,
                    };
                    valueRegisterWriteOrder[destinationRegister] = position;
                    pendingForeignInterfaceComparison = null;
                    break;
                }
                case 0xD4:
                {
                    if (!construction2MethodLinking
                        || instruction[1] >= 14
                        || BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]) >= scalarConstants.Length)
                    {
                        throw Invalid(
                            "A Construction-2 scalar constant load has an invalid register or ConstantMap index.");
                    }

                    var constantIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    var constant = scalarConstants[constantIndex];
                    if (!TryGetC1M1SourceScalarTypeSpecReference(
                            constant.Type, out _)
                        || !constant.Bits.IsCanonicalFor(constant.Type)
                        || !valueByRegister.TryAdd(instruction[1],
                            constant))
                    {
                        throw Invalid(
                            "A Construction-2 scalar constant load does not match its checked ConstantMap entry.");
                    }
                    break;
                }
                case 0x14:
                {
                    if (!body.UsesNativePointerLoads)
                    {
                        throw Unsupported(
                            "A Standard initialization-reference Body does not use pointer-load local stores.");
                    }

                    DecodeStandardNativePointerLoadHomeStore(
                        body, instruction[1] >> 4, instruction[1] & 0x0F,
                        function.ParameterCount, variables, homeVariables,
                        homeRoles, homeTypes, structures,
                        allowAggregatePointerLoads,
                        valueByRegister,
                        initializedValueLocals, statements);
                    break;
                }
                case 0xD6:
                {
                    if (body.Schema.UsesForeignNativeInterface()
                        && !body.UsesNativePointerLoads
                        && instruction[1] >> 4 == 1)
                    {
                        var sourceRegister = instruction[1] & 0x0F;
                        var homeIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                instruction[2..]);
                        if (sourceRegister >= 14
                            || homeIndex >= homeVariables.Length
                            || homeRoles[homeIndex] != 3)
                        {
                            throw Invalid(
                                "A Standard F+I staged input store has an invalid source or Home.");
                        }

                        var variable = homeVariables[homeIndex];
                        if (pendingUpdatesByStoreRegister.TryGetValue(
                                sourceRegister, out var pendingUpdate))
                        {
                            if (variable < function.ParameterCount
                                || variable >= variables.Count
                                || variables[variable].IsOut
                                || variables[variable].IsInit
                                || variables[variable]
                                    .IsInitializationLocal
                                || !initializedValueLocals.Contains(variable)
                                || !variables[variable].IsMutable
                                || variable != pendingUpdate.Variable
                                || homeTypes[homeIndex]
                                    != pendingUpdate.Type
                                || variables[variable].Type
                                    != pendingUpdate.Type
                                || !valueByRegister.TryGetValue(
                                    sourceRegister, out var updateValue)
                                || updateValue is not BoundBinary)
                            {
                                throw Invalid(
                                    "A Standard F+I update store does not match its mutable local and arithmetic result.");
                            }

                            var updateExpression = new BoundUpdate(
                                pendingUpdate.Operator,
                                new BoundRead(variable,
                                    pendingUpdate.Type, default),
                                pendingUpdate.IsPrefix,
                                pendingUpdate.Type, default) {
                                OverflowBehavior
                                    = pendingUpdate.OverflowBehavior,
                            };
                            valueByRegister[pendingUpdate.ResultRegister]
                                = updateExpression;
                            valueRegisterWriteOrder[
                                pendingUpdate.ResultRegister] = position;
                            _ = pendingUpdatesByStoreRegister.Remove(
                                sourceRegister);
                            if (pendingUpdate.ResultRegister
                                != sourceRegister)
                            {
                                _ = valueByRegister.Remove(sourceRegister);
                                _ = valueRegisterWriteOrder.Remove(
                                    sourceRegister);
                            }
                            break;
                        }

                        if (variable < function.ParameterCount
                            || variable >= variables.Count
                            || variables[variable].IsOut
                            || variables[variable].IsInit
                            || variables[variable].IsInitializationLocal
                            || initializedLocals.Contains(variable)
                            || !DirectNativeImportContract.IsScalar(
                                homeTypes[homeIndex]))
                        {
                            throw Invalid(
                                "A Standard F+I staged input Home does not match its parameter or ConstantMap initializer.");
                        }

                        if (!valueByRegister.TryGetValue(sourceRegister,
                                out var initializer)
                            || initializer.Type != homeTypes[homeIndex]
                            || !IsSupportedForeignInterfaceScalarLocalInitializer(
                                initializer))
                        {
                            throw Invalid(
                                "A Standard F+I staged input Home does not match its parameter or ConstantMap initializer.");
                        }

                        if (initializer is BoundConstant constant)
                        {
                            if (constant.Type != homeTypes[homeIndex]
                                || constant.RepresentationType
                                    != constant.Type
                                || !constant.Bits.IsCanonicalFor(
                                    constant.Type))
                            {
                                throw Invalid(
                                    "A Standard F+I staged input ConstantMap value is not canonical for its Home.");
                            }
                        }
                        else
                        {
                            var expressionConstants
                                = new HashSet<(PrimitiveType Type,
                                    ScalarBits Bits)>();
                            if (!TryAnalyzeForeignInterfaceScalarInputExpression(
                                    initializer, homeTypes[homeIndex],
                                    function, function.ParameterCount,
                                    expressionConstants, out _,
                                    initializedValueLocals,
                                    bodyVariableDefinitions))
                            {
                                throw Invalid(
                                    "A Standard F+I staged input expression is outside the checked scalar subset.");
                            }
                        }

                        if (!initializedValueLocals.Add(variable))
                        {
                            throw Invalid(
                                "A Standard F+I staged input Home is initialized more than once.");
                        }

                        statements.Add(new BoundLocal(variable,
                            initializer, default));
                        break;
                    }

                    if (!body.UsesNativePointerLoads
                        || instruction[1] >> 4 != 1)
                    {
                        throw Unsupported(
                            "A Standard pointer-load Body supports only Copy local Home stores.");
                    }

                    DecodeStandardNativePointerLoadHomeStore(
                        body, instruction[1] & 0x0F,
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]),
                        function.ParameterCount, variables, homeVariables,
                        homeRoles, homeTypes, structures,
                        allowAggregatePointerLoads,
                        valueByRegister,
                        initializedValueLocals, statements);
                    break;
                }
                case 0x92:
                {
                    var record = body.GetRecord(instruction[1]);
                    var placeKind = record[4];
                    var sourceKind = record[5];
                    var transition = record[6];
                    var sourceUse = record[7];
                    var placeHome = BinaryPrimitives.ReadUInt16LittleEndian(
                        record[8..]);
                    var sourceHome = BinaryPrimitives.ReadUInt16LittleEndian(
                        record[10..]);
                    if (placeKind != 2 || sourceKind != 2
                        || transition != (stores == 0 ? 1 : 2)
                        || sourceUse != 1
                        || placeHome >= homeVariables.Length
                        || sourceHome >= homeVariables.Length
                        || homeRoles[(int)placeHome] != 2
                        || homeRoles[(int)sourceHome] != 2)
                    {
                        throw Unsupported(
                            "The Standard initialization-reference Body subset supports formal-to-formal init stores.");
                    }

                    var descriptor = homeVariables[placeHome];
                    var source = homeVariables[sourceHome];
                    if (stores != 0 && descriptor != storedDescriptor)
                    {
                        throw Unsupported(
                            "A Standard sequential init replacement Body must use one fixed destination.");
                    }
                    if (!IsStandardInitializationReferenceParameter(
                            variables[descriptor], checkedReferenceTypes)
                        || !TryGetStandardInitializationReferenceReferentType(
                            variables[descriptor].Type,
                            checkedReferenceTypes, out var referentType)
                        || variables[source].Type != referentType
                        || !IsOutputBodyValueType(variables[source].Type))
                    {
                        throw Invalid(
                            "A Standard SemanticStore does not match its initialization-reference formal.");
                    }
                    var reference = checkedReferenceTypes[
                        CheckedReferenceTypes.Index(
                            variables[descriptor].Type)];
                    if (transition == 2
                        && (!reference.IsWritable
                            || !IsOutputScalarType(referentType)))
                    {
                        throw Unsupported(
                            "The Standard init replacement subset requires a writable scalar initialization reference.");
                    }

                    statements.Add(new BoundExpressionStatement(
                        new BoundReferenceWrite(
                            new BoundRead(descriptor,
                                variables[descriptor].Type, default),
                            new BoundRead(source, variables[source].Type,
                                default), default), default));
                    storedDescriptor = descriptor;
                    stores++;
                    break;
                }
                case 0xDD:
                {
                    var destinationRegister = instruction[1] & 0x0F;
                    var record = body.GetRecord(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]));
                    var targetKind = record[4];
                    var accessKind = record[5];
                    var destinationKind = record[6];
                    var destinationIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
                    var baseKind = record[7];
                    var baseIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
                    var indexKind = record[12];
                    var targetRefKind = record[13];
                    var indexIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(record[14..]);
                    var targetRef = BinaryPrimitives.ReadUInt32LittleEndian(
                        record[16..]);
                    var referentTypeMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            record[20..]);
                    var flags = BinaryPrimitives.ReadUInt16LittleEndian(
                        record[22..]);
                    if (accessKind != 3 || destinationKind != 1
                        || destinationIndex != destinationRegister
                        || flags is not (1 or 3)
                        || referentTypeMapIndex >= body.TypeCount)
                    {
                        throw Unsupported(
                            "The Standard initialization-reference Body has an unsupported DD target.");
                    }

                    BoundExpression outputPlace;
                    int variable;
                    PrimitiveType referentType;
                    if (targetKind == 1 && baseKind == 0
                        && baseIndex == 0 && indexKind == 0
                        && targetRefKind == 0 && indexIndex == 0
                        && targetRef < body.HomeCount)
                    {
                        var targetHomeIndex = checked((int)targetRef);
                        if (referentTypeMapIndex
                                != homeTypeMapIndexes[targetHomeIndex]
                            || homeRoles[targetHomeIndex] != 3)
                        {
                            throw Invalid(
                                "A Standard DD Home does not match its initialization descriptor.");
                        }

                        variable = homeVariables[targetHomeIndex];
                        referentType = homeTypes[targetHomeIndex];
                        outputPlace = new BoundRead(variable, referentType,
                            default);
                    }
                    else if (targetKind == 2 && baseKind == 2
                        && baseIndex < homeVariables.Length
                        && indexKind == 0 && targetRefKind == 1
                        && indexIndex == 0 && targetRef is > 0
                            and <= 0x00FF_FFFF
                        && targetRef <= (uint)fields.Count)
                    {
                        var baseHomeIndex = baseIndex;
                        if (homeRoles[baseHomeIndex] != 3)
                        {
                            throw Invalid(
                                "A Standard DD field base is not a local Home.");
                        }

                        var fieldRow = checked((int)targetRef);
                        var fieldOwnerTypeDef = fields.GetOwnerTypeDef(fieldRow);
                        var (firstField, endField)
                            = typeDefs.GetFieldRange(fieldOwnerTypeDef);
                        if (typeDefs.GetKind(fieldOwnerTypeDef) != 1
                            || fieldRow < firstField || fieldRow >= endField)
                        {
                            throw Invalid(
                                "A Standard DD field does not belong to an inline local type.");
                        }

                        variable = homeVariables[baseHomeIndex];
                        var fieldOrdinal = fieldRow - firstField;
                        referentType = GetBodyTypeMapType(
                            referentTypeMapIndex);
                        outputPlace = new BoundFieldRead(
                            new BoundRead(variable, homeTypes[baseHomeIndex],
                                default),
                            fieldOrdinal, referentType, default);
                    }
                    else if (body.Schema
                            .UsesNestedInitializationReferenceFieldDestinations()
                        && targetKind == 2 && baseKind == 2
                        && baseIndex < homeVariables.Length
                        && homeRoles[baseIndex] == 3
                        && indexKind == 0 && targetRefKind == 4
                        && indexIndex == 0 && targetRef is > 0
                            and <= 0x00FF_FFFF
                        && targetRef <= (uint)fieldPaths.Count)
                    {
                        var baseHomeIndex = baseIndex;
                        variable = homeVariables[baseHomeIndex];
                        referentType = GetBodyTypeMapType(
                            referentTypeMapIndex);
                        outputPlace = DecodeStandardInitializationFieldPath(
                            checked((int)targetRef), variable,
                            homeTypes[baseHomeIndex], referentType,
                            structures,
                            typeDefs, fields, fieldPaths, typeSpecs, imports,
                            nativePointerTypes, structureTypeByTypeDef,
                            checkedReferenceTypes,
                            checkedReferenceTypeIndexes,
                            nominalClassTypesByTypeSpecReference,
                            nominalEnumTypesByTypeDefRow,
                            nominalEnumTypesByImportRow);
                    }
                    else
                    {
                        throw Unsupported(
                            "The Standard initialization-reference Body subset supports Home and selected local FieldPath DD formation.");
                    }

                    if (variable < 0 || variable >= variables.Count
                        || !initializedLocals.Add(variable)
                        || !IsOutputBodyValueType(referentType))
                    {
                        throw Invalid(
                            "A Standard DD target is not a unique scalar initialization place.");
                    }

                    variables[variable] = variables[variable] with {
                        IsInitializationLocal = true,
                    };
                    outputPlaceByRegister.Add(destinationRegister, outputPlace);
                    break;
                }
                case 0xDF:
                {
                    if (!body.Schema.UsesForeignNativeInterface()
                        || instruction[1] != 0
                        || !nativeOutputReservationsByRecord.TryGetValue(
                            BinaryPrimitives.ReadUInt16LittleEndian(
                                instruction[2..]),
                            out var reservation)
                        || !activeNativeOutputReservations.Add(
                            BinaryPrimitives.ReadUInt16LittleEndian(
                                instruction[2..]))
                        || (uint)reservation.HomeIndex
                            >= (uint)homeVariables.Length
                        || homeRoles[reservation.HomeIndex] != 3)
                    {
                        throw Unsupported(
                            "The Standard F+I Body supports only activated local NativeOutput reservations.");
                    }

                    var variable = homeVariables[reservation.HomeIndex];
                    if (variable < function.ParameterCount
                        || variable >= variables.Count
                        || initializedValueLocals.Contains(variable)
                        || !initializedLocals.Add(variable)
                        || variables[variable].IsInitializationLocal)
                    {
                        throw Invalid(
                            "A Standard F+I NativeOutput reservation does not select a fresh vacant local.");
                    }

                    variables[variable] = variables[variable] with {
                        IsInitializationLocal = true,
                    };
                    break;
                }
                case 0x90:
                case 0xE0:
                {
                    var recordIndex = opcode == 0xE0
                        ? BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..])
                        : instruction[1];
                    var record = body.GetRecord(recordIndex);
                    var resultKind = record[5];
                    var resultIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
                    var targetMethodMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
                    var inputCount = BinaryPrimitives.ReadUInt32LittleEndian(
                        record[12..]);
                    var preparationCount
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            record[16..]);
                    if (body.Schema.UsesForeignNativeInterface()
                        && record[4] == 1
                        && targetMethodMapIndex < body.MethodCount)
                    {
                        var methodToken
                            = BinaryPrimitives.ReadUInt32LittleEndian(
                                body.MethodMap[(targetMethodMapIndex
                                    * sizeof(uint))..]);
                        var tokenKind
                            = checked((byte)(methodToken >> 24));
                        var tokenRow = checked((int)(methodToken
                            & 0x00FF_FFFF));
                        var targetMethodRow = tokenKind switch
                        {
                            0x16 => tokenRow,
                            0x1C when tokenRow > 0
                                && tokenRow <= imports.Count
                                && (imports
                                    .GetExpectedDefinitionForImportRow(
                                        tokenRow) >> 24) == 0x16
                                => checked((int)(imports
                                    .GetExpectedDefinitionForImportRow(
                                        tokenRow) & 0x00FF_FFFF)),
                            _ => 0,
                        };
                        if (targetMethodRow != 0
                            && foreignInterfaceBodyTargets is not null
                            && foreignInterfaceBodyTargets.TryGetValue(
                                targetMethodRow, out var foreignTarget))
                        {
                            var member = foreignTarget.Member;
                            if (preparationCount != 0
                                || member.ResultType is not
                                    BoundInterfaceType resultBoundType
                                || resultBoundType.Kind
                                    != BoundInterfaceTypeKind.Primitive
                                || (!member.DoesNotReturn
                                    && function.ReturnType
                                        != resultBoundType.PrimitiveType)
                                || foreignTarget.Definition
                                    .ForeignNativeInterface is not
                                    BoundForeignNativeInterfaceLayout layout
                                || member.NativeCallableConvention is not
                                    NativeCallingConvention convention
                                || inputCount
                                    != (uint)(member.Parameters.Length + 1))
                            {
                                throw Unsupported(
                                    "The Standard F+I Body subset requires a normal-returning direct InterfaceMethod CallSite.");
                            }

                            var resultType = resultBoundType.PrimitiveType;
                            var hasValueResult = !member.DoesNotReturn
                                && resultType != PrimitiveType.Void;
                            if ((hasValueResult
                                    && (resultKind != 1 || resultIndex >= 14))
                                || (!hasValueResult
                                    && (resultKind != 0 || resultIndex != 0))
                                || (member.DoesNotReturn
                                    && !function.DoesNotReturn)
                                || (!member.DoesNotReturn
                                    && resultType == PrimitiveType.Void
                                    && !member.NativeOutputContracts.IsEmpty))
                            {
                                throw Unsupported(
                                    "The Standard F+I Body CallSite result does not match its return form.");
                            }

                            var outputContractsByFormal
                                = member.NativeOutputContracts.ToDictionary(
                                    static output => output.FormalOrdinal);
                            var reservationsForCallSite
                                = nativeOutputReservationsByCallSiteFormal
                                    .Where(reservation =>
                                        reservation.Key.CallSiteRecordIndex
                                            == recordIndex)
                                    .ToArray();
                            if (reservationsForCallSite.Length
                                != member.NativeOutputContracts.Length)
                            {
                                throw Invalid(
                                    "A ForeignNativeInterface CallSite does not have one reservation for each NativeOutput formal.");
                            }

                            var declarationKey = member.DeclarationKey;
                            if (tokenKind == 0x1C)
                            {
                                declarationKey = imports
                                    .GetBindingForImportRow(tokenRow)
                                    .ExportKey;
                                if (!StringComparer.Ordinal.Equals(
                                        declarationKey,
                                        member.DeclarationKey)
                                    && !member.AdditionalDeclarationKeys
                                        .Contains(declarationKey,
                                            StringComparer.Ordinal))
                                {
                                    throw Invalid(
                                        "A ForeignNativeInterface CallSite Import does not select one of the exact member ExportKeys.");
                                }
                            }

                            var declaringInterface
                                = new BoundInterfaceType(
                                    BoundInterfaceTypeKind.Nominal,
                                    PrimitiveType.Void,
                                    foreignTarget.Definition.QualifiedName,
                                    -1, [], false, false, false, false) {
                                    ModuleImport =
                                        foreignTarget.Definition.ModuleImport,
                                };
                            var memberIdentity
                                = new BoundInterfaceMemberIdentity(
                                    declaringInterface,
                                    foreignTarget.MemberOrdinal,
                                    declarationKey);
                            var receiverKind = record[20];
                            var receiverUse = record[21];
                            var receiverIndex
                                = BinaryPrimitives.ReadUInt16LittleEndian(
                                    record[22..]);
                            BoundExpression? receiver = null;
                            if (receiverUse == 1 && receiverKind == 1
                                && receiverIndex < 14
                                && valueByRegister.TryGetValue(
                                    receiverIndex, out var registerReceiver)
                                && registerReceiver.Type
                                    == PrimitiveType.NativeRawPointer)
                            {
                                receiver = registerReceiver;
                            }
                            else if (receiverUse == 1
                                && receiverKind == 2
                                && receiverIndex < homeVariables.Length
                                && (homeRoles[receiverIndex] is 2 or 3)
                                && homeVariables[receiverIndex] >= 0
                                && !variables[homeVariables[
                                    receiverIndex]].IsOut
                                && !variables[homeVariables[
                                    receiverIndex]].IsInit
                                && !variables[homeVariables[
                                    receiverIndex]].IsInitializationLocal
                                && (homeRoles[receiverIndex] == 2
                                    || initializedValueLocals.Contains(
                                        homeVariables[receiverIndex]))
                                && variables[homeVariables[
                                    receiverIndex]].Type
                                    == PrimitiveType.NativeRawPointer)
                            {
                                var receiverVariable
                                    = homeVariables[receiverIndex];
                                receiver = new BoundRead(
                                    receiverVariable,
                                    variables[receiverVariable].Type,
                                    default);
                            }

                            if (receiver is null)
                            {
                                throw Invalid(
                                    "A ForeignNativeInterface CallSite has no completed raw-pointer receiver input.");
                            }

                            var argumentsByFormal = new BoundExpression?[
                                member.Parameters.Length];
                            var argumentRegisterByFormal
                                = new int[member.Parameters.Length];
                            Array.Fill(argumentRegisterByFormal, -1);
                            var nestedFormalByRegister
                                = new Dictionary<ushort, int>(
                                    callSiteValueResultRegisters.Count);
                            var nestedFormalIndices = new HashSet<int>();
                            var hasThrowingScalarExpressionInput = false;
                            var inputOffset = 24;
                            for (var formal = 0;
                                formal < member.Parameters.Length; formal++)
                            {
                                var selectorKind = record[inputOffset];
                                var sourceUse = record[inputOffset + 1];
                                var selectorIndex
                                    = BinaryPrimitives
                                        .ReadUInt16LittleEndian(
                                            record[(inputOffset + 2)..]);
                                var formalType
                                    = member.Parameters[formal].Type
                                        .PrimitiveType;
                                if (outputContractsByFormal.TryGetValue(
                                        formal, out var output))
                                {
                                    var callSiteFormalOrdinal
                                        = checked((uint)(formal + 1));
                                    if (selectorKind != 2 || sourceUse != 6
                                        || selectorIndex
                                            >= homeVariables.Length
                                        || !nativeOutputReservationsByCallSiteFormal
                                            .TryGetValue((recordIndex,
                                                    callSiteFormalOrdinal),
                                                out var reservationRecordIndex)
                                        || !activeNativeOutputReservations
                                            .Remove(reservationRecordIndex)
                                        || !nativeOutputReservationsByRecord
                                            .TryGetValue(
                                                reservationRecordIndex,
                                                out var reservation)
                                        || reservation.CallSiteRecordIndex
                                            != recordIndex
                                        || reservation.FormalOrdinal
                                            != callSiteFormalOrdinal
                                        || reservation.HomeIndex
                                            != selectorIndex
                                        || homeRoles[selectorIndex] != 3)
                                    {
                                        throw Invalid(
                                            "A ForeignNativeInterface NativeOutput input does not match an active reservation.");
                                    }

                                    var outputVariable
                                        = homeVariables[selectorIndex];
                                    if (outputVariable
                                            < function.ParameterCount
                                        || !initializedLocals.Contains(
                                            outputVariable)
                                        || !variables[outputVariable]
                                            .IsInitializationLocal
                                        || variables[outputVariable].Type
                                            != output.PointeeType)
                                    {
                                        throw Invalid(
                                            "A ForeignNativeInterface NativeOutput input does not select a matching vacant local.");
                                    }

                                    argumentsByFormal[formal]
                                        = new BoundNativeOutputReservation(
                                            outputVariable,
                                            output.PointeeType, default);
                                }
                                else if (selectorKind == 1 && sourceUse == 1
                                    && selectorIndex < 14
                                    && valueByRegister.TryGetValue(
                                        selectorIndex,
                                        out var registerArgument)
                                    && registerArgument.Type == formalType)
                                {
                                    if (callSiteValueResultRegisters.Contains(
                                            selectorIndex))
                                    {
                                        if (!nestedFormalByRegister.TryAdd(
                                                selectorIndex, formal)
                                            || !nestedFormalIndices.Add(formal)
                                            || GetUnwrappedBoundCall(
                                                registerArgument) is null)
                                        {
                                            throw Invalid(
                                                "A ForeignNativeInterface CallSite reuses or misclassifies a nested result register.");
                                        }
                                    }
                                    else if (registerArgument is BoundRead or
                                        BoundUnary or BoundBinary or BoundCast
                                        or BoundUpdate)
                                    {
                                        var expressionConstants
                                            = new HashSet<(PrimitiveType Type,
                                                ScalarBits Bits)>();
                                        if (!TryAnalyzeForeignInterfaceScalarInputExpression(
                                                registerArgument, formalType,
                                                function,
                                                function.ParameterCount,
                                                expressionConstants, out _,
                                                initializedValueLocals,
                                                bodyVariableDefinitions))
                                        {
                                            throw Invalid(
                                                "A ForeignNativeInterface CallSite contains an unsupported scalar expression input.");
                                        }

                                        hasThrowingScalarExpressionInput |=
                                            ForeignInterfaceScalarInputExpressionMayThrow(
                                                registerArgument);
                                    }
                                    else if (registerArgument
                                        is not BoundConstant)
                                    {
                                        throw Invalid(
                                            "A ForeignNativeInterface register input is neither a ConstantMap value, nested result nor scalar expression.");
                                    }

                                    argumentsByFormal[formal]
                                        = registerArgument;
                                    argumentRegisterByFormal[formal]
                                        = selectorIndex;
                                }
                                else if (selectorKind == 2
                                    && sourceUse == 1
                                    && selectorIndex < homeVariables.Length
                                    && (homeRoles[selectorIndex] is 2 or 3)
                                    && homeVariables[selectorIndex] >= 0
                                    && !variables[homeVariables[
                                        selectorIndex]].IsOut
                                    && !variables[homeVariables[
                                        selectorIndex]].IsInit
                                    && !variables[homeVariables[
                                        selectorIndex]].IsInitializationLocal
                                    && (homeRoles[selectorIndex] == 2
                                        || initializedValueLocals.Contains(
                                            homeVariables[selectorIndex]))
                                    && variables[homeVariables[
                                        selectorIndex]].Type == formalType)
                                {
                                    var argumentVariable
                                        = homeVariables[selectorIndex];
                                    argumentsByFormal[formal] = new BoundRead(
                                        argumentVariable,
                                        variables[argumentVariable].Type,
                                        default);
                                }
                                else
                                {
                                    throw Unsupported(
                                        "The Standard F+I Body subset requires completed register or Home inputs in formal order.");
                                }

                                inputOffset += 4;
                            }

                            if (nestedFormalByRegister.Count != 0
                                && nestedFormalByRegister.Count
                                != callSiteValueResultRegisters.Count)
                            {
                                throw Invalid(
                                    "A ForeignNativeInterface CallSite does not consume each earlier nested result exactly once.");
                            }

                            if (hasThrowingScalarExpressionInput
                                && nestedFormalByRegister.Count != 0
                                && (statements.Any(static statement =>
                                    statement is BoundExpressionStatement
                                        expressionStatement
                                    && GetUnwrappedBoundCall(
                                        expressionStatement.Expression)
                                        is not null)
                                    || Enumerable.Range(0,
                                        member.Parameters.Length)
                                    .Any(formal =>
                                        !outputContractsByFormal
                                            .ContainsKey(formal)
                                        && !nestedFormalIndices.Contains(
                                            formal)
                                        && !DirectNativeImportContract
                                            .IsScalar(member.Parameters[
                                                formal].Type.PrimitiveType)))
                            {
                                throw Invalid(
                                    "A Standard F+I CallSite cannot preserve checked-expression order with nested Calls when a non-nested input is not a scalar value.");
                            }

                            var arguments
                                = ImmutableArray.CreateBuilder<
                                    BoundExpression>(
                                    member.Parameters.Length);
                            var argumentParameterIndices
                                = ImmutableArray.CreateBuilder<int>(
                                    member.Parameters.Length);
                            // Final register definitions retain nested-call and
                            // expression order; Home and constant inputs are pure.
                            var orderedFormals = Enumerable.Range(0,
                                    member.Parameters.Length)
                                .OrderBy(formal =>
                                {
                                    var register
                                        = argumentRegisterByFormal[formal];
                                    return register >= 0
                                        && valueRegisterWriteOrder
                                            .TryGetValue(register,
                                                out var writeOrder)
                                        ? writeOrder : int.MaxValue;
                                })
                                .ThenBy(static formal => formal);
                            foreach (var formal in orderedFormals)
                            {
                                arguments.Add(argumentsByFormal[formal]
                                    ?? throw Invalid(
                                        "A ForeignNativeInterface CallSite omits a formal input."));
                                argumentParameterIndices.Add(formal);
                            }

                            if (reservationsForCallSite.Any(reservation =>
                                    activeNativeOutputReservations.Contains(
                                        reservation.Value)))
                            {
                                throw Invalid(
                                    "A ForeignNativeInterface CallSite leaves an activated NativeOutput reservation unused.");
                            }

                            var call = new BoundCall(
                                BoundCall.ForeignInterfaceFunction,
                                arguments.MoveToImmutable(), resultType,
                                default) {
                                IsUnsafeAuthorized = true,
                                ForeignInterfaceCall =
                                    new BoundForeignInterfaceCall(
                                        receiver, memberIdentity,
                                        layout.VTableByteOffset,
                                        convention,
                                        member.RequiresMutableReceiver,
                                        member.DoesNotReturn,
                                        member.NativeOutputContracts),
                                ArgumentParameterIndices
                                    = argumentParameterIndices
                                        .MoveToImmutable(),
                            };
                            // The caller-unsafe FNI target makes this CallSite the authored unsafe assertion.
                            var authorizedCall
                                = new BoundUnsafeExpression(call, default);
                            foreach (var consumedRegister
                                in nestedFormalByRegister.Keys)
                            {
                                valueByRegister.Remove(consumedRegister);
                            }
                            callSiteValueResultRegisters.RemoveAll(
                                nestedFormalByRegister.ContainsKey);
                            if (hasValueResult
                                && !valueByRegister.TryAdd(resultIndex,
                                    authorizedCall))
                            {
                                throw Invalid(
                                    "A ForeignNativeInterface CallSite overwrites an existing value register.");
                            }
                            if (hasValueResult)
                            {
                                callSiteValueResultRegisters.Add(resultIndex);
                                valueRegisterWriteOrder[resultIndex] = position;
                            }
                            if (!hasValueResult)
                            {
                                statements.Add(new BoundExpressionStatement(
                                    authorizedCall, default));
                            }

                            calls++;
                            foreignInterfaceCalls++;
                            if (member.DoesNotReturn)
                            {
                                foreignInterfaceNonReturningCalls++;
                            }
                            foreignInterfaceOutputReservations = checked(
                                foreignInterfaceOutputReservations
                                + member.NativeOutputContracts.Length);
                            break;
                        }
                    }

                    if (body.UsesNativePointerLoads
                        && body.Schema.UsesNativeParameterContracts()
                        && resultKind == 0)
                    {
                        if (record[4] != 1 || resultIndex != 0
                            || preparationCount != 0
                            || targetMethodMapIndex >= body.MethodCount)
                        {
                            throw Unsupported(
                                "The F16 pointer-load Body requires an unprepared void NativeParameter CallSite.");
                        }

                        var nativeMethodToken
                            = BinaryPrimitives.ReadUInt32LittleEndian(
                                body.MethodMap[(targetMethodMapIndex
                                    * sizeof(uint))..]);
                        var nativeTargetFunction
                            = ResolveStandardInitializationBodyMethodMapFunction(
                                nativeMethodToken, imports,
                                functionIndexByMethodRow, functions,
                                standardNativeImports,
                                allowStandardNativeImport: true,
                                allowNativeImportModuleReference: true);
                        var nativeTarget = functions[nativeTargetFunction];
                        if (!nativeTarget.IsExternal
                            || nativeTarget.DirectNativeImport is not {
                                Parameters.IsEmpty: false,
                            }
                            || nativeTarget.ReturnType != PrimitiveType.Void
                            || inputCount != (uint)nativeTarget.ParameterCount)
                        {
                            throw Invalid(
                                "An F16 void CallSite does not match a NativeParameter target.");
                        }

                        var nativeArguments
                            = ImmutableArray.CreateBuilder<BoundExpression>(
                                nativeTarget.ParameterCount);
                        var nativeInputOffset = 20;
                        for (var formal = 0;
                            formal < nativeTarget.ParameterCount; formal++)
                        {
                            var selectorKind = record[nativeInputOffset];
                            var sourceUse = record[nativeInputOffset + 1];
                            var selectorIndex
                                = BinaryPrimitives.ReadUInt16LittleEndian(
                                    record[(nativeInputOffset + 2)..]);
                            var parameter = nativeTarget.Variables[formal];
                            if (selectorKind != 1 || sourceUse != 1
                                || selectorIndex >= 14
                                || parameter.IsOut || parameter.IsInit
                                || !valueByRegister.TryGetValue(
                                    selectorIndex, out var argument)
                                || argument.Type != parameter.Type
                                || !IsStandardNativePointerLoadValueType(
                                    parameter.Type, structures,
                                    allowAggregatePointerLoads))
                            {
                                throw Invalid(
                                    "An F16 void CallSite has an unsupported staged input.");
                            }

                            nativeArguments.Add(argument);
                            nativeInputOffset += 4;
                        }

                        statements.Add(new BoundExpressionStatement(
                            new BoundCall(nativeTargetFunction,
                                nativeArguments.MoveToImmutable(),
                                PrimitiveType.Void, default) {
                                    IsUnsafeAuthorized =
                                        nativeTarget.RequiresUnsafeCaller,
                                }, default));
                        calls++;
                        break;
                    }

                    if (body.Schema.UsesForeignNativeInterface()
                        && !body.UsesNativePointerLoads
                        && !construction2MethodLinking
                        && resultKind == 1
                        && record[4] == 1
                        && resultIndex < 14
                        && preparationCount == 0
                        && targetMethodMapIndex < body.MethodCount)
                    {
                        var localMethodToken
                            = BinaryPrimitives.ReadUInt32LittleEndian(
                                body.MethodMap[(targetMethodMapIndex
                                    * sizeof(uint))..]);
                        if ((localMethodToken >> 24) == 0x16)
                        {
                            var localTargetFunction
                                = ResolveStandardInitializationBodyMethodMapFunction(
                                    localMethodToken, imports,
                                    functionIndexByMethodRow, functions);
                            var localTarget = functions[
                                localTargetFunction];
                            if (IsValidLocalScalarInputTarget(localTarget)
                                && inputCount
                                    == (uint)localTarget.ParameterCount)
                            {
                                if (resultIndex == 0)
                                {
                                    throw Invalid(
                                        "A Standard F+I local value CallSite overwrites the outer result register.");
                                }

                                var priorResultRegisters
                                    = callSiteValueResultRegisters.ToArray();
                                var localArgumentsByFormal
                                    = new BoundExpression?[
                                        localTarget.ParameterCount];
                                var nestedFormalByRegister
                                    = new Dictionary<ushort, int>(
                                        priorResultRegisters.Length);
                                var nestedFormalIndices = new HashSet<int>();
                                var localInputOffset = 20;
                                for (var formal = 0;
                                    formal < localTarget.ParameterCount;
                                    formal++)
                                {
                                    var selectorKind
                                        = record[localInputOffset];
                                    var sourceUse
                                        = record[localInputOffset + 1];
                                    var selectorIndex
                                        = BinaryPrimitives
                                            .ReadUInt16LittleEndian(
                                                record[(localInputOffset + 2)
                                                    ..]);
                                    var parameter
                                        = localTarget.Variables[formal];
                                    if (selectorKind == 1
                                        && sourceUse == 1
                                        && selectorIndex < 14
                                        && valueByRegister.TryGetValue(
                                            selectorIndex,
                                            out var localRegisterInput)
                                        && localRegisterInput
                                            is BoundConstant constant
                                        && constant.Type == parameter.Type
                                        && DirectNativeImportContract
                                            .IsScalar(constant.Type)
                                        && constant.RepresentationType
                                            == constant.Type
                                        && constant.Bits.IsCanonicalFor(
                                            constant.Type))
                                    {
                                        localArgumentsByFormal[formal]
                                            = constant;
                                    }
                                    else if (selectorKind == 1
                                        && sourceUse == 1
                                        && selectorIndex < 14
                                        && valueByRegister.TryGetValue(
                                            selectorIndex,
                                            out var nestedRegisterInput)
                                        && nestedRegisterInput.Type
                                            == parameter.Type
                                        && callSiteValueResultRegisters
                                            .Contains(selectorIndex)
                                        && GetNestedForeignInterfaceCall(
                                            nestedRegisterInput) is not null)
                                    {
                                        if (!nestedFormalByRegister.TryAdd(
                                                selectorIndex, formal)
                                            || !nestedFormalIndices.Add(
                                                formal))
                                        {
                                            throw Invalid(
                                                "A Standard F+I local value CallSite reuses a nested result register.");
                                        }

                                        localArgumentsByFormal[formal]
                                            = nestedRegisterInput;
                                    }
                                    else if (selectorKind == 2
                                        && sourceUse == 1
                                        && selectorIndex
                                            < homeVariables.Length
                                        && homeRoles[selectorIndex] == 2
                                        && homeVariables[selectorIndex] >= 0
                                        && homeVariables[selectorIndex]
                                            < function.ParameterCount
                                        && !variables[homeVariables[
                                            selectorIndex]].IsOut
                                        && !variables[homeVariables[
                                            selectorIndex]].IsInit
                                        && variables[homeVariables[
                                            selectorIndex]].Type
                                            == parameter.Type)
                                    {
                                        localArgumentsByFormal[formal]
                                            = new BoundRead(
                                            homeVariables[selectorIndex],
                                            parameter.Type, default);
                                    }
                                    else
                                    {
                                        throw Invalid(
                                            "A Standard F+I local value CallSite has an unsupported direct input.");
                                    }

                                    localInputOffset += 4;
                                }

                                var localArguments
                                    = ImmutableArray.CreateBuilder<
                                        BoundExpression>(
                                        localTarget.ParameterCount);
                                var localArgumentParameterIndices
                                    = ImmutableArray.CreateBuilder<int>(
                                        localTarget.ParameterCount);
                                foreach (var nestedResultRegister
                                    in priorResultRegisters)
                                {
                                    if (!nestedFormalByRegister.TryGetValue(
                                            nestedResultRegister,
                                            out var formal))
                                    {
                                        continue;
                                    }

                                    localArguments.Add(
                                        localArgumentsByFormal[formal]
                                        ?? throw Invalid(
                                            "A Standard F+I local value CallSite omits a nested input formal."));
                                    localArgumentParameterIndices.Add(formal);
                                }
                                for (var formal = 0;
                                    formal < localTarget.ParameterCount;
                                    formal++)
                                {
                                    if (nestedFormalIndices.Contains(formal))
                                    {
                                        continue;
                                    }

                                    localArguments.Add(
                                        localArgumentsByFormal[formal]
                                        ?? throw Invalid(
                                            "A Standard F+I local value CallSite omits a formal input."));
                                    localArgumentParameterIndices.Add(formal);
                                }

                                foreach (var consumedRegister
                                    in nestedFormalByRegister.Keys)
                                {
                                    valueByRegister.Remove(consumedRegister);
                                }
                                callSiteValueResultRegisters.RemoveAll(
                                    nestedFormalByRegister.ContainsKey);
                                if (!valueByRegister.TryAdd(resultIndex,
                                        new BoundCall(localTargetFunction,
                                            localArguments.MoveToImmutable(),
                                            localTarget.ReturnType, default) {
                                            IsUnsafeAuthorized =
                                                localTarget
                                                    .RequiresUnsafeCaller,
                                            ArgumentParameterIndices =
                                                localArgumentParameterIndices
                                                    .MoveToImmutable(),
                                        }))
                                {
                                    throw Invalid(
                                        "A Standard F+I local value CallSite overwrites an existing register.");
                                }

                                callSiteValueResultRegisters.Add(resultIndex);
                                valueRegisterWriteOrder[resultIndex] = position;
                                calls++;
                                break;
                            }
                        }
                    }

                    if (resultKind == 1
                        && construction2MethodLinking)
                    {
                        if (record[4] != 1
                            || resultIndex >= 14
                            || preparationCount != 0
                            || targetMethodMapIndex >= body.MethodCount)
                        {
                            throw Unsupported(
                                "A Construction-2 Method CallSite requires an unprepared register result.");
                        }

                        var methodToken
                            = BinaryPrimitives.ReadUInt32LittleEndian(
                                body.MethodMap[(targetMethodMapIndex
                                    * sizeof(uint))..]);
                        var targetFunction
                            = ResolveStandardInitializationBodyMethodMapFunction(
                                methodToken, imports,
                                functionIndexByMethodRow, functions);
                        var target = functions[targetFunction];
                        if (target.ReturnType == PrimitiveType.Void
                            || !TryGetC1M1SourceScalarTypeSpecReference(
                                target.ReturnType, out _)
                            || inputCount
                                != (uint)target.ParameterCount)
                        {
                            throw Invalid(
                                "A Construction-2 Method CallSite result does not match its target signature.");
                        }

                        var arguments
                            = ImmutableArray.CreateBuilder<BoundExpression>(
                                target.ParameterCount);
                        var inputOffset = 20;
                        for (var formal = 0;
                            formal < target.ParameterCount; formal++)
                        {
                            var selectorKind = record[inputOffset];
                            var sourceUse = record[inputOffset + 1];
                            var selectorIndex
                                = BinaryPrimitives.ReadUInt16LittleEndian(
                                    record[(inputOffset + 2)..]);
                            var parameter = target.Variables[formal];
                            if (selectorKind == 1 && sourceUse == 1
                                && selectorIndex < 14
                                && valueByRegister.TryGetValue(
                                    selectorIndex, out var registerValue)
                                && registerValue.Type == parameter.Type)
                            {
                                arguments.Add(registerValue);
                            }
                            else if (selectorKind == 2 && sourceUse == 1
                                && selectorIndex < homeVariables.Length
                                && homeRoles[selectorIndex] == 2
                                && homeVariables[selectorIndex] >= 0
                                && variables[homeVariables[
                                    selectorIndex]].Type == parameter.Type
                                && !parameter.IsOut && !parameter.IsInit
                                && !parameter.IsImplicitRef
                                && !parameter.IsInitializationLocal)
                            {
                                arguments.Add(new BoundRead(
                                    homeVariables[selectorIndex],
                                    parameter.Type, default));
                            }
                            else
                            {
                                throw Unsupported(
                                    "A Construction-2 Method CallSite input must select a fixed-scalar register or formal Home.");
                            }

                            inputOffset += 4;
                        }

                        if (!valueByRegister.TryAdd(resultIndex,
                                new BoundCall(targetFunction,
                                    arguments.MoveToImmutable(),
                                    target.ReturnType, default) {
                                    IsUnsafeAuthorized =
                                        target.RequiresUnsafeCaller,
                                }))
                        {
                            throw Invalid(
                                "A Construction-2 Method CallSite overwrites an existing value register.");
                        }
                        if (body.Schema.UsesForeignNativeInterface()
                            && IsValidLocalScalarInputTarget(target))
                        {
                            callSiteValueResultRegisters.Add(resultIndex);
                            valueRegisterWriteOrder[resultIndex] = position;
                        }

                        calls++;
                        break;
                    }

                    if (resultKind == 1)
                    {
                        if (!body.UsesNativePointerLoads
                            || record[4] != 1
                            || resultIndex >= 14
                            || preparationCount != 0
                            || targetMethodMapIndex >= body.MethodCount)
                        {
                            throw Unsupported(
                                "The Standard native pointer-load Body requires an unprepared register-result CallSite.");
                        }

                        var pointerMethodToken
                            = BinaryPrimitives.ReadUInt32LittleEndian(
                                body.MethodMap[(targetMethodMapIndex
                                    * sizeof(uint))..]);
                        var pointerTargetFunction
                            = ResolveStandardInitializationBodyMethodMapFunction(
                                pointerMethodToken, imports,
                                functionIndexByMethodRow, functions,
                                standardNativeImports,
                                allowStandardNativeImport: true,
                                allowNativeImportModuleReference:
                                    body.Schema.UsesNativeParameterContracts());
                        var pointerTarget = functions[pointerTargetFunction];
                        if (inputCount
                                != (uint)pointerTarget.ParameterCount
                            || pointerTarget.ReturnType == PrimitiveType.Void
                            || !IsStandardNativePointerLoadValueType(
                                pointerTarget.ReturnType, structures,
                                allowAggregatePointerLoads))
                        {
                            throw Invalid(
                                "A pointer-load CallSite result does not match its value target.");
                        }

                        var pointerArguments
                            = ImmutableArray.CreateBuilder<BoundExpression>(
                                pointerTarget.ParameterCount);
                        var pointerInputOffset = 20;
                        for (var formal = 0;
                            formal < pointerTarget.ParameterCount; formal++)
                        {
                            var selectorKind = record[pointerInputOffset];
                            var sourceUse = record[pointerInputOffset + 1];
                            var selectorIndex
                                = BinaryPrimitives.ReadUInt16LittleEndian(
                                    record[(pointerInputOffset + 2)..]);
                            var parameter = pointerTarget.Variables[formal];
                            if (selectorKind != 1 || sourceUse != 1
                                || selectorIndex >= 14
                                || parameter.IsOut || parameter.IsInit
                                || !valueByRegister.TryGetValue(
                                    selectorIndex, out var argument)
                                || argument.Type != parameter.Type
                                || !IsStandardNativePointerLoadValueType(
                                    parameter.Type, structures,
                                    allowAggregatePointerLoads))
                            {
                                throw Invalid(
                                    "A pointer-load CallSite has an unsupported value input.");
                            }

                            pointerArguments.Add(argument);
                            pointerInputOffset += 4;
                        }

                        valueByRegister[resultIndex] = new BoundCall(
                            pointerTargetFunction,
                            pointerArguments.MoveToImmutable(),
                            pointerTarget.ReturnType, default) {
                                IsUnsafeAuthorized =
                                    pointerTarget.RequiresUnsafeCaller,
                            };
                        calls++;
                        break;
                    }

                    if (record[4] != 1 || resultKind != 0
                        || resultIndex != 0
                        || preparationCount != 0
                        || targetMethodMapIndex >= body.MethodCount)
                    {
                        throw Unsupported(
                            "The Standard initialization-reference Body subset requires a direct void CallSite.");
                    }

                    var methodToken
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            body.MethodMap[(targetMethodMapIndex
                                * sizeof(uint))..]);
                    var targetFunction
                        = ResolveStandardInitializationBodyMethodMapFunction(
                            methodToken, imports, functionIndexByMethodRow,
                            functions);
                    var target = functions[targetFunction];
                    if ((target.RequiresUnsafeCaller
                            && !construction2MethodLinking)
                        || target.ReturnType != PrimitiveType.Void
                        || inputCount != (uint)target.ParameterCount)
                    {
                        throw Invalid(
                            "A Standard initialization CallSite does not match its target signature.");
                    }

                    var arguments = ImmutableArray.CreateBuilder<BoundExpression>(
                        target.ParameterCount);
                    var inputOffset = 20;
                    for (var formal = 0; formal < target.ParameterCount;
                        formal++)
                    {
                        var selectorKind = record[inputOffset];
                        var sourceUse = record[inputOffset + 1];
                        var selectorIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                record[(inputOffset + 2)..]);
                        var parameter = target.Variables[formal];
                        if (sourceUse == 5 && selectorKind == 1
                            && outputPlaceByRegister.TryGetValue(
                                selectorIndex, out var outputPlace)
                            && IsStandardInitializationReferenceParameter(
                                parameter, checkedReferenceTypes)
                            && TryGetStandardInitializationReferenceReferentType(
                                parameter.Type, checkedReferenceTypes,
                                out var targetReferent)
                            && outputPlace.Type == targetReferent)
                        {
                            arguments.Add(new BoundReferenceFormation(
                                outputPlace,
                                parameter.Type, default));
                        }
                        else if (sourceUse == 5 && selectorKind == 2
                            && selectorIndex < homeVariables.Length
                            && selectorIndex == formal
                            && selectorIndex == 0
                            && homeRoles[(int)selectorIndex] == 2
                            && homeVariables[(int)selectorIndex]
                                == (int)selectorIndex
                            && homeVariables[(int)selectorIndex]
                                >= 0
                            && homeVariables[(int)selectorIndex]
                                < function.ParameterCount
                            && IsStandardInitializationReferenceParameter(
                                parameter, checkedReferenceTypes)
                            && IsStandardInitializationReferenceParameter(
                                variables[homeVariables[
                                    (int)selectorIndex]],
                                checkedReferenceTypes)
                            && variables[homeVariables[
                                (int)selectorIndex]].Type == parameter.Type)
                        {
                            var descriptorVariable
                                = homeVariables[(int)selectorIndex];
                            arguments.Add(new BoundReferenceReborrow(
                                new BoundRead(descriptorVariable,
                                    variables[descriptorVariable].Type,
                                    default), parameter.Type, default));
                            forwardedInitializationReference = true;
                        }
                        else if (sourceUse == 1 && selectorKind == 2
                            && selectorIndex < homeVariables.Length
                            && homeRoles[(int)selectorIndex] == 2
                            && homeVariables[(int)selectorIndex] >= 0
                            && homeVariables[(int)selectorIndex]
                                < variables.Count
                            && !parameter.IsInit
                            && !variables[homeVariables[
                                (int)selectorIndex]].IsInit
                            && variables[homeVariables[(int)selectorIndex]].Type
                                == parameter.Type)
                        {
                            arguments.Add(new BoundRead(
                                homeVariables[(int)selectorIndex], parameter.Type,
                                default));
                        }
                        else
                        {
                            throw Unsupported(
                                "The Standard initialization-reference Body subset supports refinit DD inputs and scalar Home inputs.");
                        }

                        inputOffset += 4;
                    }

                    statements.Add(new BoundExpressionStatement(new BoundCall(
                        targetFunction, arguments.MoveToImmutable(),
                        PrimitiveType.Void, default) {
                            IsUnsafeAuthorized =
                                construction2MethodLinking
                                && target.RequiresUnsafeCaller,
                        }, default));
                    calls++;
                    break;
                }
                case 0x1B:
                {
                    var destinationRegister = instruction[1] & 0x0F;
                    var sourceRegister = instruction[1] >> 4;
                    if (!body.UsesNativePointerLoads
                        || destinationRegister >= 14
                        || sourceRegister >= 14
                        || !valueByRegister.TryGetValue(sourceRegister,
                            out var address)
                        || !NativePointerTypes.IsType(address.Type))
                    {
                        throw Invalid(
                            "A Standard native pointer load has an invalid source register.");
                    }

                    var pointerType = nativePointerTypes[
                        NativePointerTypes.Index(address.Type)];
                    if (pointerType.PointeeType == PrimitiveType.Void
                        || !IsStandardNativePointerLoadPointeeType(
                            pointerType.PointeeType, structures,
                            allowAggregatePointerLoads))
                    {
                        throw Invalid(
                            "A Standard native pointer load has an unsupported pointee type.");
                    }

                    valueByRegister[destinationRegister]
                        = new BoundNativePointerRead(address,
                            pointerType.PointeeType, default) {
                                IsUnsafeAuthorized = true,
                            };
                    pointerLoads++;
                    break;
                }
                case 0x50:
                {
                    var register = instruction[1] & 0x0F;
                    var typeMapIndex = instruction[1] >> 4;
                    if (register >= 14 || typeMapIndex >= body.TypeCount
                        || !valueByRegister.TryGetValue(register,
                            out var value))
                    {
                        throw Invalid(
                            "A Standard native pointer-load conversion has an invalid source.");
                    }

                    var destinationType = GetBodyTypeMapType(
                        checked((ushort)typeMapIndex));
                    var conversion = new BoundCast(value, destinationType,
                        default);
                    if (!SemanticVerifier.IsTotalConversion(value.Type,
                            destinationType)
                        || (foreignInterfaceDirectCallBody
                            && !TryGetGeneralPredicateConversion(conversion,
                                out _, out _))
                        || (body.UsesNativePointerLoads
                            && !IsStandardNativePointerLoadValueType(
                                destinationType, structures,
                                allowAggregatePointerLoads)))
                    {
                        throw Invalid(
                            "A Standard short conversion is not total for its complete types.");
                    }

                    valueByRegister[register] = conversion;
                    valueRegisterWriteOrder[register] = position;
                    conversions++;
                    break;
                }
                case 0xD0:
                {
                    var control = instruction[1];
                    var conversionSourceRegister = control & 0x0F;
                    var conversionDestinationRegister = control >> 4;
                    var typeMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                    if (conversionSourceRegister >= 14
                        || conversionDestinationRegister >= 14
                        || typeMapIndex >= body.TypeCount
                        || !valueByRegister.TryGetValue(
                            conversionSourceRegister,
                            out var value))
                    {
                        throw Invalid(
                            "A Standard numeric conversion has an invalid source or destination.");
                    }

                    var destinationType = GetBodyTypeMapType(typeMapIndex);
                    if (!body.Schema.UsesForeignNativeInterface()
                        || !SemanticVerifier
                            .IsSupportedCheckedNumericConversion(
                                value.Type, destinationType))
                    {
                        throw Invalid(
                            "A Standard F+I checked numeric conversion is invalid for its complete types.");
                    }

                    valueByRegister[conversionDestinationRegister]
                        = new BoundCast(value, destinationType, default);
                    valueRegisterWriteOrder[
                        conversionDestinationRegister] = position;
                    conversions++;
                    break;
                }
                case 0xDA:
                {
                    var register = instruction[1] & 0x0F;
                    var record = body.GetRecord(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]));
                    var payload = record[4..];
                    var baseHomeIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            payload[6..]);
                    var fieldRowValue
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            payload[12..]);
                    var typeMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            payload[16..]);
                    var targetRefKind = payload[9];
                    if (targetRefKind == 4)
                    {
                        if (record[0] != 0x04
                            || !body.Schema
                                .UsesNestedInitializationReferenceFieldDestinations()
                            || (instruction[1] & 0xF0) != 0
                            || register >= 14
                            || (payload[0] != 2 || payload[1] != 1
                                || payload[2] != 1 || payload[3] != 2)
                            || BinaryPrimitives.ReadUInt16LittleEndian(
                                payload[4..]) != register
                            || payload[8] != 0
                            || BinaryPrimitives.ReadUInt16LittleEndian(
                                payload[10..]) != 0
                            || fieldRowValue is 0 or > 0x00FF_FFFF
                            || fieldRowValue > (uint)fieldPaths.Count
                            || baseHomeIndex >= homeVariables.Length
                            || typeMapIndex >= body.TypeCount
                            || BinaryPrimitives.ReadUInt16LittleEndian(
                                payload[18..]) != 0
                            || homeRoles[baseHomeIndex] != 3)
                        {
                            throw Invalid(
                                "A Standard nested initialization Body field load has an invalid PlaceSelection.");
                        }

                        var variable = homeVariables[baseHomeIndex];
                        var fieldType = GetBodyTypeMapType(typeMapIndex);
                        valueByRegister[register]
                            = DecodeStandardInitializationFieldPath(
                                checked((int)fieldRowValue), variable,
                                homeTypes[baseHomeIndex], fieldType,
                                structures,
                                typeDefs, fields, fieldPaths, typeSpecs,
                                imports, nativePointerTypes,
                                structureTypeByTypeDef,
                                checkedReferenceTypes,
                                checkedReferenceTypeIndexes,
                                nominalClassTypesByTypeSpecReference,
                                nominalEnumTypesByTypeDefRow,
                                nominalEnumTypesByImportRow);
                        break;
                    }
                    if (record[0] != 0x04
                        || (instruction[1] & 0xF0) != 0
                        || payload[0] != 2 || payload[1] != 1
                        || payload[2] != 1 || payload[3] != 2
                        || BinaryPrimitives.ReadUInt16LittleEndian(
                            payload[4..]) != register
                        || baseHomeIndex >= homeVariables.Length
                        || payload[8] != 0 || payload[9] != 1
                        || BinaryPrimitives.ReadUInt16LittleEndian(
                            payload[10..]) != 0
                        || fieldRowValue is 0 or > 0x00FF_FFFF
                        || fieldRowValue > (uint)fields.Count
                        || typeMapIndex >= body.TypeCount
                        || BinaryPrimitives.ReadUInt16LittleEndian(
                            payload[18..]) != 0
                        || homeRoles[baseHomeIndex] != 3)
                    {
                        throw Invalid(
                            "A Standard initialization Body field load has an invalid PlaceSelection.");
                    }

                    var fieldRow = checked((int)fieldRowValue);
                    var fieldOwnerTypeDef = fields.GetOwnerTypeDef(fieldRow);
                    var (firstField, endField)
                        = typeDefs.GetFieldRange(fieldOwnerTypeDef);
                    if (typeDefs.GetKind(fieldOwnerTypeDef) != 1
                        || fieldRow < firstField || fieldRow >= endField)
                    {
                        throw Invalid(
                            "A Standard initialization Body field load does not name a local inline field.");
                    }

                    var variable = homeVariables[baseHomeIndex];
                    var fieldType = GetBodyTypeMapType(typeMapIndex);
                    valueByRegister[register] = new BoundFieldRead(
                        new BoundRead(variable, homeTypes[baseHomeIndex],
                            default),
                        fieldRow - firstField, fieldType, default);
                    break;
                }
                case 0xD5:
                {
                    var register = instruction[1] & 0x0F;
                    var homeIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                        instruction[2..]);
                    if (body.UsesNativePointerLoads)
                    {
                        if ((instruction[1] & 0xF0) != 0
                            || register >= 14
                            || homeIndex >= homeVariables.Length
                            || homeRoles[homeIndex] is not (2 or 3))
                        {
                            throw Invalid(
                                "A pointer-load Body Home read has an invalid destination or Home.");
                        }

                        var pointerLoadHomeVariable
                            = homeVariables[homeIndex];
                        if (pointerLoadHomeVariable < 0
                            || pointerLoadHomeVariable >= variables.Count
                            || !IsStandardNativePointerLoadValueType(
                                homeTypes[homeIndex], structures,
                                allowAggregatePointerLoads))
                        {
                            throw Invalid(
                                "A pointer-load Body reads a vacant or unsupported Home.");
                        }
                        var pointerLoadVariable
                            = variables[pointerLoadHomeVariable];
                        var hasAvailableValue = homeRoles[homeIndex] == 2
                            ? !pointerLoadVariable.IsOut
                                && !pointerLoadVariable.IsInit
                            : pointerLoadVariable.IsInitializationLocal
                                ? initializedLocals.Contains(
                                    pointerLoadHomeVariable)
                                : initializedValueLocals.Contains(
                                    pointerLoadHomeVariable);
                        if (!hasAvailableValue
                            || (homeRoles[homeIndex] == 3
                                && !pointerLoadVariable.IsInitializationLocal
                                && (pointerLoadVariable.IsOut
                                    || pointerLoadVariable.IsInit)))
                        {
                            throw Invalid(
                                "A pointer-load Body reads a vacant or unsupported Home.");
                        }

                        valueByRegister[register] = new BoundRead(
                            pointerLoadHomeVariable,
                            homeTypes[homeIndex], default);
                        valueRegisterWriteOrder[register] = position;
                        break;
                    }

                    if ((instruction[1] & 0xF0) != 0
                        || homeIndex >= homeVariables.Length)
                    {
                        throw Invalid(
                            "A Standard init Body loads a Home before its output call.");
                    }

                    var variable = homeVariables[(int)homeIndex];
                    if (!initializedLocals.Contains(variable)
                        && !(body.Schema.UsesForeignNativeInterface()
                            && homeRoles[homeIndex] == 3
                            && initializedValueLocals.Contains(variable)))
                    {
                        throw Invalid(
                            "A Standard init Body loads a Home before it is initialized.");
                    }

                    valueByRegister[register] = new BoundRead(variable,
                        variables[variable].Type, default);
                    valueRegisterWriteOrder[register] = position;
                    break;
                }
                case 0x17:
                {
                    var register = instruction[1] & 0x0F;
                    if ((instruction[1] & 0xF0) != 0
                        || !valueByRegister.TryGetValue(register,
                            out var value)
                        || value.Type != function.ReturnType)
                    {
                        throw Invalid(
                            "A Standard initialization Body return register is invalid.");
                    }

                    statements.Add(new BoundReturn(value, default));
                    returns++;
                    break;
                }
                case 0x18:
                    if (instruction[1] != 0
                        || function.ReturnType != PrimitiveType.Void)
                    {
                        throw Invalid(
                            "A Standard initialization Body unit return is invalid.");
                    }

                    statements.Add(new BoundReturn(null, default));
                    returns++;
                    break;
                default:
                    throw Unsupported(
                        $"The Standard initialization-reference Body opcode 0x{opcode:X2} is outside this consumer subset.");
            }

            position += instructionLength;
        }

        if (pendingForeignInterfaceComparison is not null)
        {
            throw Invalid(
                "A Standard F+I relation comparison has no SetCondition.");
        }

        if (pendingUpdateMove is not null
            || pendingUpdatesByStoreRegister.Count != 0)
        {
            throw Invalid(
                "A Standard F+I update is missing its arithmetic or Home store.");
        }

        if (activeNativeOutputReservations.Count != 0)
        {
            throw Invalid(
                "A Standard F+I Body ends with an unconsumed NativeOutput reservation.");
        }

        var voidStore = function.ReturnType == PrimitiveType.Void
            && stores > 0 && calls == 0 && pointerLoads == 0
            && initializedLocals.Count == 0;
        var voidForwardingCall = function.ReturnType == PrimitiveType.Void
            && stores == 0 && calls == 1 && pointerLoads == 0
            && forwardedInitializationReference
            && initializedLocals.Count == 0;
        var voidInitializationCall = function.ReturnType
                == PrimitiveType.Void
            && stores == 0 && calls == 1 && pointerLoads == 0
            && initializedLocals.Count == 1;
        var foreignInterfaceVoidCall
            = function.ReturnType == PrimitiveType.Void
                && stores == 0 && calls == 1 && foreignInterfaceCalls == 1
                && pointerLoads == 0 && initializedLocals.Count == 0
                && foreignInterfaceOutputReservations == 0
                && foreignInterfaceNonReturningCalls == 0
                && body.MethodCount == 1
                && variables.Count == function.ParameterCount
                    + initializedValueLocals.Count;
        var foreignInterfaceVoidCallSequence
            = function.ReturnType == PrimitiveType.Void
                && !function.DoesNotReturn
                && stores == 0 && calls > 1
                && foreignInterfaceCalls == calls
                && foreignInterfaceNonReturningCalls == 0
                && pointerLoads == 0
                && initializedLocals.Count == 0
                && foreignInterfaceOutputReservations == 0
                && body.RecordCount == calls + 1
                && body.MethodCount > 0
                && body.MethodCount <= calls
                && statements.Count == calls + 1
                && statements[^1] is BoundReturn { Value: null }
                && statements.Take(calls).All(statement =>
                    statement is BoundExpressionStatement expressionStatement
                    && GetNestedForeignInterfaceCall(
                        expressionStatement.Expression) is BoundCall call
                    && call.Type == PrimitiveType.Void
                    && call.IsUnsafeAuthorized
                    && call.ForeignInterfaceCall is
                        BoundForeignInterfaceCall foreignCall
                    && !foreignCall.DoesNotReturn
                    && foreignCall.NativeOutputContracts.IsEmpty
                    && call.Arguments.All(static argument =>
                        argument is BoundRead
                        || argument is BoundConstant constant
                            && DirectNativeImportContract.IsScalar(
                                constant.Type)
                            && constant.RepresentationType == constant.Type
                            && constant.Bits.IsCanonicalFor(
                                constant.Type)))
                && variables.Count == function.ParameterCount
                    + initializedValueLocals.Count;
        var valueCall = function.ReturnType != PrimitiveType.Void
            && stores == 0 && calls == 1 && pointerLoads == 0
            && initializedLocals.Count == 1;
        var foreignInterfaceValueCall
            = function.ReturnType != PrimitiveType.Void
                && stores == 0 && calls == 1 && foreignInterfaceCalls == 1
                && pointerLoads == 0
                && foreignInterfaceNonReturningCalls == 0
                && initializedLocals.Count
                    == foreignInterfaceOutputReservations
                && body.MethodCount == 1
                && variables.Count == function.ParameterCount
                    + foreignInterfaceOutputReservations
                    + initializedValueLocals.Count;
        var foreignInterfaceTerminalCall = function.DoesNotReturn
            && stores == 0 && calls == 1 && foreignInterfaceCalls == 1
            && foreignInterfaceNonReturningCalls == 1
            && pointerLoads == 0
            && initializedLocals.Count
                == foreignInterfaceOutputReservations
            && body.MethodCount == 1
            && variables.Count == function.ParameterCount
                + foreignInterfaceOutputReservations
                + initializedValueLocals.Count;
        BoundCall? nestedInputOuterCall = null;
        if (statements.Count != 0
            && statements[^1] is BoundReturn {
                Value: BoundExpression returnValue,
            })
        {
            nestedInputOuterCall
                = GetNestedForeignInterfaceCall(returnValue);
        }
        else if (function.ReturnType == PrimitiveType.Void
            && statements.Count >= 2
            && statements[^1] is BoundReturn { Value: null }
            && statements[^2] is BoundExpressionStatement {
                Expression: BoundExpression expression,
            })
        {
            nestedInputOuterCall
                = GetNestedForeignInterfaceCall(expression);
        }
        else if (function.DoesNotReturn
            && statements.Count != 0
            && statements[^1] is BoundExpressionStatement {
                Expression: BoundExpression expression,
            })
        {
            nestedInputOuterCall
                = GetNestedForeignInterfaceCall(expression);
        }

        var nestedInputCallsBuilder = new List<BoundCall>();
        if (nestedInputOuterCall is BoundCall outerCall)
        {
            foreach (var argument in outerCall.Arguments)
            {
                AddNestedSequenceInputCalls(argument);
            }
        }

        var nestedInputCalls = nestedInputCallsBuilder.ToArray();

        BoundCall? GetNestedSequenceInputCall(BoundExpression expression)
        {
            var call = GetUnwrappedBoundCall(expression);
            return call is not null
                && (call.ForeignInterfaceCall is not null
                    || IsValidLocalScalarInputCall(call))
                ? call : null;
        }

        void AddNestedSequenceInputCalls(BoundExpression expression)
        {
            if (GetNestedSequenceInputCall(expression)
                is not BoundCall nestedCall)
            {
                return;
            }

            if (nestedCall.ForeignInterfaceCall is null)
            {
                foreach (var argument in nestedCall.Arguments)
                {
                    AddNestedSequenceInputCalls(argument);
                }
            }

            nestedInputCallsBuilder.Add(nestedCall);
        }

        bool IsValidLocalScalarInputTarget(BoundFunction target)
            => !target.IsExternal
                && target.ModuleImport is null
                && target.DeclaringStruct is null
                && target.DirectNativeImport is null
                && target.NativeVTableMethod is null
                && target.NativeFunctionPointerType is null
                && target.GenericMethodInstantiation is null
                && target.ImportedGenericMethod is null
                && target.InterfaceRequirement is null
                && !target.IsConstEval
                && !target.RequiresUnsafeCaller
                && !target.DoesNotReturn
                && !target.IsConstructor
                && !target.IsCleanup
                && DirectNativeImportContract.IsScalar(
                    target.ReturnType)
                && target.NativeOutputContracts.IsEmpty
                && target.ParameterCount == target.Variables.Length
                && target.Variables.All(static variable =>
                    !variable.IsOut && !variable.IsInit
                    && !variable.IsImplicitRef
                    && !variable.IsImplicitReferenceTemporary
                    && !variable.IsInitializationLocal
                    && (DirectNativeImportContract.IsScalar(
                            variable.Type)
                        || variable.Type
                            == PrimitiveType.NativeRawPointer
                        || NativePointerTypes.IsType(variable.Type)));

        bool IsValidLocalScalarInputCall(BoundCall nestedCall)
        {
            if (nestedCall.ForeignInterfaceCall is not null
                || nestedCall.Function == BoundCall.ForeignInterfaceFunction
                || (uint)nestedCall.Function >= (uint)functions.Count
                || nestedCall.InterfaceCallIdentity is not null
                || nestedCall.GenericMethodCall is not null
                || nestedCall.ImportedGenericMethodCall is not null
                || !nestedCall.ImportedGenericMethodTypeArguments
                    .IsDefaultOrEmpty
                || nestedCall.Arguments.IsDefault
                || !DirectNativeImportContract.IsScalar(nestedCall.Type))
            {
                return false;
            }

            var target = functions[nestedCall.Function];
            if (!IsValidLocalScalarInputTarget(target)
                || nestedCall.Type != target.ReturnType
                || target.ParameterCount != nestedCall.Arguments.Length
                || !target.NativeOutputContracts.IsEmpty)
            {
                return false;
            }

            var seenParameters = new bool[target.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < nestedCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = nestedCall.ParameterIndex(argumentIndex);
                var argument = nestedCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)target.ParameterCount
                    || seenParameters[formal])
                {
                    return false;
                }

                var formalType = target.Variables[formal].Type;
                if (argument is BoundRead read)
                {
                    if ((uint)read.Variable >= (uint)function.ParameterCount
                        || read.Type != formalType
                        || variables[read.Variable].Type != formalType
                        || variables[read.Variable].IsOut
                        || variables[read.Variable].IsInit)
                    {
                        return false;
                    }
                }
                else if (!(argument is BoundConstant constant
                        && DirectNativeImportContract.IsScalar(
                            constant.Type)
                        && constant.Type == formalType
                        && constant.RepresentationType == constant.Type
                        && constant.Bits.IsCanonicalFor(constant.Type))
                    && !(GetNestedForeignInterfaceCall(argument)
                        is BoundCall nestedInputCall
                        && nestedInputCall.Type == formalType
                        && DirectNativeImportContract.IsScalar(
                            nestedInputCall.Type)
                        && nestedInputCall.Function
                            == BoundCall.ForeignInterfaceFunction
                        && nestedInputCall.IsUnsafeAuthorized
                        && nestedInputCall.ForeignInterfaceCall is
                            BoundForeignInterfaceCall nestedForeignCall
                        && !nestedForeignCall.DoesNotReturn
                        && nestedForeignCall.NativeOutputContracts.IsEmpty
                        && nestedForeignCall.Receiver is BoundRead {
                            Type: PrimitiveType.NativeRawPointer,
                        }
                        && nestedInputCall.Arguments.All(
                            static nestedArgument =>
                                nestedArgument is BoundRead
                                || nestedArgument is BoundConstant
                                    nestedConstant
                                    && DirectNativeImportContract.IsScalar(
                                        nestedConstant.Type)
                                    && nestedConstant.RepresentationType
                                        == nestedConstant.Type
                                    && nestedConstant.Bits.IsCanonicalFor(
                                        nestedConstant.Type))))
                {
                    return false;
                }

                seenParameters[formal] = true;
            }

            return seenParameters.All(static seen => seen);
        }

        bool IsValidLocalVoidPrefixCall(BoundCall prefixCall)
        {
            if (prefixCall.ForeignInterfaceCall is not null
                || prefixCall.InterfaceCallIdentity is not null
                || prefixCall.GenericMethodCall is not null
                || prefixCall.ImportedGenericMethodCall is not null
                || !prefixCall.ImportedGenericMethodTypeArguments
                    .IsDefaultOrEmpty
                || (uint)prefixCall.Function >= (uint)functions.Count
                || prefixCall.Type != PrimitiveType.Void
                || prefixCall.Arguments.IsDefault)
            {
                return false;
            }

            var target = functions[prefixCall.Function];
            if (target.IsExternal
                || target.ModuleImport is not null
                || target.DeclaringStruct is not null
                || target.DirectNativeImport is not null
                || target.NativeVTableMethod is not null
                || target.NativeFunctionPointerType is not null
                || target.GenericMethodInstantiation is not null
                || target.ImportedGenericMethod is not null
                || target.InterfaceRequirement is not null
                || target.IsConstEval
                || target.RequiresUnsafeCaller
                || target.DoesNotReturn
                || target.IsConstructor
                || target.IsCleanup
                || target.ReturnType != PrimitiveType.Void
                || !target.NativeOutputContracts.IsEmpty
                || target.ParameterCount != prefixCall.Arguments.Length
                || target.Variables.Length != target.ParameterCount
                || target.Variables.Any(static variable =>
                    variable.IsOut || variable.IsInit
                    || variable.IsImplicitRef
                    || variable.IsImplicitReferenceTemporary
                    || variable.IsInitializationLocal
                    || !(DirectNativeImportContract.IsScalar(
                            variable.Type)
                        || variable.Type
                            == PrimitiveType.NativeRawPointer)))
            {
                return false;
            }

            var seenParameters = new bool[target.ParameterCount];
            for (var argumentIndex = 0;
                argumentIndex < prefixCall.Arguments.Length;
                argumentIndex++)
            {
                var formal = prefixCall.ParameterIndex(argumentIndex);
                var argument = prefixCall.Arguments[argumentIndex];
                if ((uint)formal >= (uint)target.ParameterCount
                    || seenParameters[formal]
                    || !(DirectNativeImportContract.IsScalar(
                            target.Variables[formal].Type)
                        || target.Variables[formal].Type
                            == PrimitiveType.NativeRawPointer)
                    || argument is not BoundRead read
                    || (uint)read.Variable >= (uint)function.ParameterCount
                    || read.Type != target.Variables[formal].Type
                    || variables[read.Variable].Type != read.Type
                    || variables[read.Variable].IsOut
                    || variables[read.Variable].IsInit)
                {
                    return false;
                }

                seenParameters[formal] = true;
            }

            return seenParameters.All(static seen => seen);
        }

        bool IsValidVoidPrefixCall(BoundCall prefixCall)
        {
            if (prefixCall.Type != PrimitiveType.Void)
            {
                return false;
            }

            if (prefixCall.ForeignInterfaceCall is
                BoundForeignInterfaceCall prefixForeignCall)
            {
                return prefixCall.Function
                        == BoundCall.ForeignInterfaceFunction
                    && prefixCall.IsUnsafeAuthorized
                    && !prefixForeignCall.DoesNotReturn
                    && prefixForeignCall.NativeOutputContracts.IsEmpty
                    && prefixForeignCall.Receiver is BoundRead {
                        Type: PrimitiveType.NativeRawPointer,
                    } receiver
                    && (uint)receiver.Variable
                        < (uint)function.ParameterCount
                    && prefixCall.Arguments.All(argument =>
                        argument is BoundRead read
                            && (uint)read.Variable
                                < (uint)function.ParameterCount
                            && read.Type
                                == variables[read.Variable].Type
                            && !variables[read.Variable].IsOut
                            && !variables[read.Variable].IsInit
                        || argument is BoundConstant constant
                            && DirectNativeImportContract.IsScalar(
                                constant.Type)
                            && constant.RepresentationType == constant.Type
                            && constant.Bits.IsCanonicalFor(
                                constant.Type));
            }

            return IsValidLocalVoidPrefixCall(prefixCall);
        }

        var nestedInputOuterTerminal
            = nestedInputOuterCall?.ForeignInterfaceCall?.DoesNotReturn
                == true;
        var nestedInputPrefixCallCount
            = nestedInputOuterCall is BoundCall
                && statements.Count > 1
                && (function.DoesNotReturn
                    ? nestedInputOuterTerminal
                        && statements[^1]
                            is BoundExpressionStatement
                    : function.ReturnType != PrimitiveType.Void
                        && statements[^1] is BoundReturn {
                            Value: not null,
                        })
                && statements.Take(statements.Count - 1).All(statement =>
                    statement is BoundExpressionStatement {
                        Expression: BoundExpression expression,
                    }
                    && GetUnwrappedBoundCall(expression)
                        is BoundCall prefixCall
                    && IsValidVoidPrefixCall(prefixCall))
                    ? statements.Count - 1 : 0;
        var nestedInputLocalPrefixCallCount
            = nestedInputPrefixCallCount == 0 ? 0
                : statements.Take(nestedInputPrefixCallCount).Count(
                    statement =>
                        statement is BoundExpressionStatement {
                            Expression: BoundExpression expression,
                        }
                        && GetUnwrappedBoundCall(expression) is
                            BoundCall prefixCall
                        && prefixCall.ForeignInterfaceCall is null);
        var nestedInputPrefixCallsValid
            = nestedInputPrefixCallCount == 0
                || (initializedLocals.Count == 0
                    && initializedValueLocals.Count == 0
                    && foreignInterfaceOutputReservations == 0
                    && variables.Count == function.ParameterCount
                    && statements.Take(nestedInputPrefixCallCount).All(
                        statement =>
                        statement is BoundExpressionStatement {
                            Expression: BoundExpression expression,
                        }
                        && GetUnwrappedBoundCall(expression) is
                            BoundCall prefixCall
                        && IsValidVoidPrefixCall(prefixCall));
        var foreignInterfaceValueCallSequence
            = function.ReturnType != PrimitiveType.Void
                && !function.DoesNotReturn
                && stores == 0 && calls > 1
                && calls == statements.Count
                && nestedInputPrefixCallCount == calls - 1
                && nestedInputCalls.Length == 0
                && foreignInterfaceCalls
                    == calls - nestedInputLocalPrefixCallCount
                && foreignInterfaceNonReturningCalls == 0
                && pointerLoads == 0
                && initializedLocals.Count == 0
                && foreignInterfaceOutputReservations == 0
                && body.RecordCount == calls + 1
                && body.MethodCount > 0
                && body.MethodCount <= calls
                && statements[^1] is BoundReturn {
                    Value: BoundExpression returnExpression,
                }
                && GetNestedForeignInterfaceCall(returnExpression)
                    is BoundCall returnCall
                && returnCall.Type == function.ReturnType
                && returnCall.IsUnsafeAuthorized
                && returnCall.ForeignInterfaceCall is
                    BoundForeignInterfaceCall returnForeignCall
                && !returnForeignCall.DoesNotReturn
                && returnForeignCall.NativeOutputContracts.IsEmpty
                && returnCall.Arguments.All(static argument =>
                    argument is BoundRead
                    || argument is BoundConstant constant
                        && DirectNativeImportContract.IsScalar(
                            constant.Type)
                        && constant.RepresentationType == constant.Type
                        && constant.Bits.IsCanonicalFor(constant.Type))
                && nestedInputPrefixCallsValid
                && variables.Count == function.ParameterCount;
        var nestedInputLocalCallCount
            = nestedInputCalls.Count(static nested =>
                nested.ForeignInterfaceCall is null);
        var nestedInputCallsValid
            = nestedInputOuterCall?.ForeignInterfaceCall is
                BoundForeignInterfaceCall outerForeignCall
            && nestedInputCalls.Length is > 0 and <= 13
            && nestedInputCalls.Distinct(
                ReferenceEqualityComparer.Instance).Count()
                == nestedInputCalls.Length
            && nestedInputCalls.All(nested =>
                DirectNativeImportContract.IsScalar(nested.Type)
                && (nested.ForeignInterfaceCall is
                    BoundForeignInterfaceCall nestedForeignCall
                    ? nested.Function
                            == BoundCall.ForeignInterfaceFunction
                        && nested.IsUnsafeAuthorized
                        && !nestedForeignCall.DoesNotReturn
                        && nestedForeignCall.NativeOutputContracts.IsEmpty
                        && nestedForeignCall.Member != outerForeignCall.Member
                        && nestedForeignCall.Receiver is BoundRead {
                            Type: PrimitiveType.NativeRawPointer,
                        }
                        && nested.Arguments.All(static argument =>
                            argument is BoundRead
                            || argument is BoundConstant constant
                                && DirectNativeImportContract.IsScalar(
                                    constant.Type)
                                && constant.RepresentationType
                                    == constant.Type
                                && constant.Bits.IsCanonicalFor(
                                    constant.Type))
                    : IsValidLocalScalarInputCall(nested));
        var foreignInterfaceNestedCall
            = stores == 0
                && calls == nestedInputPrefixCallCount
                    + nestedInputCalls.Length + 1
                && foreignInterfaceCalls
                    == calls - nestedInputLocalPrefixCallCount
                        - nestedInputLocalCallCount
                && (nestedInputOuterTerminal
                    ? function.DoesNotReturn
                        && foreignInterfaceNonReturningCalls == 1
                    : !function.DoesNotReturn
                        && foreignInterfaceNonReturningCalls == 0)
                && pointerLoads == 0
                && initializedLocals.Count
                    == foreignInterfaceOutputReservations
                && (nestedInputPrefixCallCount == 0
                    || body.RecordCount == calls + 1)
                && body.MethodCount > 1
                && body.MethodCount <= calls
                && nestedInputCallsValid
                && nestedInputPrefixCallsValid
                && variables.Count == function.ParameterCount
                    + foreignInterfaceOutputReservations
                    + initializedValueLocals.Count;
        var foreignInterfaceTerminalCallSequence
            = function.DoesNotReturn
                && nestedInputOuterTerminal
                && nestedInputPrefixCallCount > 0
                && nestedInputCalls.Length == 0
                && stores == 0
                && foreignInterfaceCalls
                    == calls - nestedInputLocalPrefixCallCount
                && foreignInterfaceNonReturningCalls == 1
                && pointerLoads == 0
                && initializedLocals.Count == 0
                && initializedValueLocals.Count == 0
                && foreignInterfaceOutputReservations == 0
                && statements.Count == nestedInputPrefixCallCount + 1
                && statements[^1] is BoundExpressionStatement {
                    Expression: BoundExpression terminalExpression,
                }
                && GetNestedForeignInterfaceCall(terminalExpression)
                    is BoundCall terminalCall
                && terminalCall.IsUnsafeAuthorized
                && terminalCall.ForeignInterfaceCall is
                    BoundForeignInterfaceCall terminalForeignCall
                && terminalForeignCall.DoesNotReturn
                && terminalForeignCall.NativeOutputContracts.IsEmpty
                && terminalCall.Arguments.All(static argument =>
                    argument is BoundRead
                    || argument is BoundConstant constant
                        && DirectNativeImportContract.IsScalar(
                            constant.Type)
                        && constant.RepresentationType == constant.Type
                        && constant.Bits.IsCanonicalFor(constant.Type))
                && body.RecordCount == calls + 1
                && body.MethodCount > 0
                && body.MethodCount <= calls
                && nestedInputPrefixCallsValid
                && variables.Count == function.ParameterCount;
        var foreignInterfaceNestedTerminalCall
            = foreignInterfaceNestedCall && nestedInputOuterTerminal;
        var pointerLoadCallCount = 0;
        var pointerLoadExpressionCount = 0;
        foreach (var statement in statements)
        {
            pointerLoadCallCount += statement switch {
                BoundLocal local =>
                    CountStandardNativePointerLoadCalls(local.Initializer),
                BoundAssignment assignment =>
                    CountStandardNativePointerLoadCalls(assignment.Value),
                BoundExpressionStatement {
                    Expression: BoundCall call,
                } => CountStandardNativePointerLoadCalls(call),
                BoundReturn { Value: BoundExpression returnValue } =>
                    CountStandardNativePointerLoadCalls(returnValue),
                _ => 0,
            };
            pointerLoadExpressionCount += statement switch {
                BoundLocal local =>
                    CountStandardNativePointerReads(local.Initializer),
                BoundAssignment assignment =>
                    CountStandardNativePointerReads(assignment.Value),
                BoundExpressionStatement expressionStatement =>
                    CountStandardNativePointerReads(
                        expressionStatement.Expression),
                BoundReturn { Value: BoundExpression returnValue } =>
                    CountStandardNativePointerReads(returnValue),
                _ => 0,
            };
        }
        var valuePointerLoad = function.ReturnType != PrimitiveType.Void
            && stores == 0 && pointerLoads > 0 && conversions <= 1
            && initializedLocals.Count == 0 && body.ConstantCount == 0
            && pointerLoads == pointerLoadExpressionCount
            && calls == pointerLoadCallCount
            && (calls == 0
                ? body.MethodCount == 0
                    && decodedPointerRead.Address is BoundRead
                : body.MethodCount > 0
                    );
        var simpleInitializationBody = voidStore || voidForwardingCall
            || voidInitializationCall || foreignInterfaceVoidCall
            || foreignInterfaceVoidCallSequence
            || foreignInterfaceValueCallSequence
            || foreignInterfaceValueCallSequence || valueCall
            || foreignInterfaceValueCall || foreignInterfaceTerminalCall
            || foreignInterfaceNestedCall
            || foreignInterfaceTerminalCallSequence;
        if (returns != (foreignInterfaceTerminalCall
                || foreignInterfaceNestedTerminalCall
                || foreignInterfaceTerminalCallSequence ? 0 : 1)
            || !(simpleInitializationBody || valuePointerLoad))
        {
            throw Unsupported(
                "The Standard initialization-reference Body is outside the supported statement subset.");
        }

        var completeStatements = ImmutableArray.CreateBuilder<BoundStatement>(
            initializedLocals.Count + statements.Count);
        foreach (var variable in initializedLocals.Order())
        {
            completeStatements.Add(new BoundVacantLocal(variable, default));
        }
        completeStatements.AddRange(statements);
        if (valuePointerLoad)
        {
            return [
                new BoundUnsafe(new BoundBlock(
                    completeStatements.MoveToImmutable(), default), default),
            ];
        }

        return completeStatements.MoveToImmutable();
    }

    private static bool IsStandardForeignInterfaceConditionalOutputEntry(
        ReadOnlySpan<byte> code)
    {
        if (code.IsEmpty)
        {
            return false;
        }

        if (code[0] == 0xDF)
        {
            return true;
        }
        return GetStandardForeignInterfaceConditionalOutputPreparationPrefixLength(
            code, out _, out _, out _) > 0;
    }

    private static int
        GetStandardForeignInterfaceConditionalOutputPreparationPrefixLength(
            ReadOnlySpan<byte> code, out int callSiteInvocationCount,
            out bool hasConstantInputs, out bool hasScalarExpressions)
    {
        callSiteInvocationCount = 0;
        hasConstantInputs = false;
        hasScalarExpressions = false;
        var offset = 0;
        while (offset < code.Length && code[offset] != 0xDF)
        {
            if (code[offset] == 0x90 && code.Length - offset >= 2)
            {
                callSiteInvocationCount++;
                offset += 2;
            }
            else if (code[offset] == 0x16
                && code.Length - offset >= 2)
            {
                hasConstantInputs = true;
                offset += 2;
            }
            else if (code[offset] == 0xD4
                && code.Length - offset >= 4)
            {
                hasConstantInputs = true;
                offset += 4;
            }
            else if (code[offset] == 0xD0
                && code.Length - offset >= 4)
            {
                hasScalarExpressions = true;
                offset += 4;
            }
            else if ((code[offset] is 0xC0 or 0xC2 or 0xC3)
                && code.Length - offset >= 4)
            {
                hasScalarExpressions = true;
                offset += 4;
            }
            else if ((code[offset] == 0x11 || code[offset] == 0x50
                    || code[offset] is >= 0x20 and <= 0x2B
                        or >= 0x30 and <= 0x36)
                && code.Length - offset >= 2)
            {
                hasScalarExpressions = true;
                offset += 2;
            }
            else
            {
                return -1;
            }
        }

        return offset > 0 && offset < code.Length && code[offset] == 0xDF
            ? offset : -1;
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardForeignInterfaceConditionalOutputBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmImportTableReader imports,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] structureTypeByTypeDef,
            List<CheckedReferenceTypeDefinition> checkedReferenceTypes,
            Dictionary<CheckedReferenceTypeDefinition, int>
                checkedReferenceTypeIndexes,
            IReadOnlyDictionary<uint, PrimitiveType>?
                nominalClassTypesByTypeSpecReference,
            IReadOnlyDictionary<int, PrimitiveType>?
                nominalEnumTypesByTypeDefRow,
            IReadOnlyDictionary<int, PrimitiveType>?
                nominalEnumTypesByImportRow,
            int[] homeVariables,
            byte[] homeRoles, PrimitiveType[] homeTypes,
            ImmutableArray<BoundConstant> scalarConstants,
            IReadOnlyDictionary<int, ForeignNativeInterfaceBodyTarget>?
                foreignInterfaceBodyTargets)
    {
        PrimitiveType GetBodyTypeMapType(ushort typeMapIndex)
            => GetStandardInitializationBodyType(body, typeMapIndex,
                typeSpecs, imports, nativePointerTypes,
                structureTypeByTypeDef, checkedReferenceTypes,
                checkedReferenceTypeIndexes,
                nominalClassTypesByTypeSpecReference,
                nominalEnumTypesByTypeDefRow,
                nominalEnumTypesByImportRow);

        var hasFallback = function.ParameterCount >= 3;
        var outputVariableStart = function.ParameterCount;
        var outputCount = variables.Count - function.ParameterCount - 1;
        if (outputCount <= 0)
        {
            throw Unsupported(
                "The Standard F+I conditional-output Body requires at least one NativeOutput local.");
        }

        var outputVariables = Enumerable.Range(outputVariableStart,
            outputCount).ToArray();
        var statusVariable = outputVariableStart + outputCount;
        var fallbackVariable = hasFallback
            ? function.ParameterCount - 1 : -1;
        if (body.BlockCount != 3)
        {
            throw Unsupported(
                "The Standard F+I conditional-output Body requires three blocks.");
        }

        var rawEntryCode = body.GetBlockCode(0);
        var entryPrefixLength
            = GetStandardForeignInterfaceConditionalOutputPreparationPrefixLength(
                rawEntryCode, out var callSiteInvocationCount,
                out var hasConstantInputs,
                out var hasScalarExpressions);
        var successCode = body.GetBlockCode(1);
        var hasSuccessCall = successCode.Length >= 4
            && successCode[0] == 0x90
            && successCode[^2] == 0x17
            && successCode[^1] == 0
            && (successCode.Length - 2) % 2 == 0;
        var successCallSiteCount = hasSuccessCall
            ? (successCode.Length - 2) / 2
            : 0;
        var hasCallSiteInputs = callSiteInvocationCount != 0;
        var preparationPrefixLength = Math.Max(0, entryPrefixLength);
        var entryCode = preparationPrefixLength == 0
            ? rawEntryCode : rawEntryCode[entryPrefixLength..];
        if (!body.Schema.UsesForeignNativeInterface()
            || function.IsExternal || function.DoesNotReturn
            || function.ParameterCount < 2
            || body.RecordCount
                != (callSiteInvocationCount + outputCount
                    + 3 + successCallSiteCount)
            || callSiteInvocationCount > 13
            || successCallSiteCount > 2
            || (hasSuccessCall
                && callSiteInvocationCount + 1 + successCallSiteCount > 13)
            || (!hasCallSiteInputs && !hasSuccessCall
                && body.MethodCount != 1)
            || ((hasCallSiteInputs || hasSuccessCall)
                && (body.MethodCount < 2
                    || body.MethodCount > callSiteInvocationCount + 1
                        + successCallSiteCount))
            || ((hasCallSiteInputs || hasConstantInputs) && !hasFallback)
            || (hasSuccessCall && !hasFallback)
            || body.HomeCount != variables.Count
            || !HasEmptyEntryState(body, 0)
            || !HasEmptyEntryState(body, 1)
            || !HasEmptyEntryState(body, 2)
            || (!DirectNativeImportContract.IsOutputPointee(
                    function.ReturnType)
                && !body.Schema
                    .UsesForeignNativeInterfaceAggregateOutputConsumption())
            || (!hasFallback && function.ReturnType is not (
                PrimitiveType.Int32 or PrimitiveType.UInt32))
            || homeTypes[0] != PrimitiveType.NativeRawPointer
            || homeRoles[statusVariable] != 3
            || homeVariables[statusVariable] != statusVariable
            || (hasFallback && (homeRoles[fallbackVariable] != 2
                || homeVariables[fallbackVariable] != fallbackVariable
                || homeTypes[fallbackVariable] != function.ReturnType)))
        {
            throw Unsupported(
                "The Standard F+I conditional-output Body requires a supported result type, direct receiver/parameter Homes, and bounded output/status Homes.");
        }

        foreach (var outputVariable in outputVariables)
        {
            if (homeRoles[outputVariable] != 3
                || homeVariables[outputVariable] != outputVariable)
            {
                throw Unsupported(
                    "The Standard F+I conditional-output Body requires direct output Homes.");
            }
        }

        for (var parameter = 0; parameter < function.ParameterCount;
            parameter++)
        {
            var variable = function.Variables[parameter];
            if (homeRoles[parameter] != 2
                || homeVariables[parameter] != parameter)
            {
                throw Unsupported(
                    "The Standard F+I conditional-output Body requires direct parameter Homes.");
            }

            if (variable.IsOut || variable.IsInit
                || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary
                || variable.IsInitializationLocal)
            {
                throw Invalid(
                    "The Standard F+I conditional-output Body parameter Homes cannot carry output or initialization modes.");
            }
        }

        var complementCode = body.GetBlockCode(2);
        var callOffset = checked(outputCount * 4);
        if (entryCode.Length != callOffset + 22
            || entryCode[callOffset] != 0x90
            || entryCode[callOffset + 2] != 0xD6
            || entryCode[callOffset + 3] != 0x10
            || BinaryPrimitives.ReadUInt16LittleEndian(
                entryCode[(callOffset + 4)..])
                != statusVariable
            || entryCode[callOffset + 6] != 0xD5
            || entryCode[callOffset + 7] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(
                entryCode[(callOffset + 8)..]) != statusVariable
            || entryCode[callOffset + 10] != 0xC2
            || entryCode[callOffset + 11] != 0
            || entryCode[callOffset + 14] != 0xC3
            || entryCode[callOffset + 16] != 0
            || entryCode[callOffset + 17] != 0xFF
            || entryCode[callOffset + 18] != 0xE4
            || entryCode[callOffset + 19] != 0
            || (hasSuccessCall
                ? successCallSiteCount is < 1 or > 2
                    || successCode.Length
                        != (successCallSiteCount * 2) + 2
                    || successCode[^2] != 0x17
                    || successCode[^1] != 0
                : successCode.Length != 6 || successCode[0] != 0xD5
                    || successCode[1] != 0 || successCode[4] != 0x17
                    || successCode[5] != 0)
            || complementCode.Length != 6 || complementCode[0] != 0xD5
            || complementCode[1] != 0
            || complementCode[4] != 0x17 || complementCode[5] != 0)
        {
            throw Unsupported(
                "The Standard F+I conditional-output Body instructions are outside the selected subset.");
        }

        var returnOutputVariable = hasSuccessCall
            ? checked((ushort)outputVariables[0])
            : BinaryPrimitives.ReadUInt16LittleEndian(successCode[2..]);
        if (!outputVariables.Contains(returnOutputVariable)
            || (!hasSuccessCall
                && homeTypes[returnOutputVariable] != function.ReturnType)
            || BinaryPrimitives.ReadUInt16LittleEndian(
                complementCode[2..])
                != (fallbackVariable >= 0
                    ? fallbackVariable : statusVariable))
        {
            throw Invalid(
                "The Standard F+I conditional-output Body reads a different Home.");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(
                entryCode[(callOffset + 12)..])
            != 0xE0F0)
        {
            throw Invalid(
                "The Standard F+I conditional-output comparison does not use the status Home and zero.");
        }

        var callSiteRecordIndex = entryCode[callOffset + 1];
        var edgeRecordIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                entryCode[(callOffset + 20)..]);
        var successCallSiteRecordIndices
            = new ushort[successCallSiteCount];
        var outputReservationRecordIndices
            = new ushort[outputCount];
        for (var outputIndex = 0;
            outputIndex < outputCount;
            outputIndex++)
        {
            var reservationOffset = outputIndex * 4;
            var reservationRecordIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    entryCode[(reservationOffset + 2)..]);
            if (entryCode[reservationOffset] != 0xDF
                || entryCode[reservationOffset + 1] != 0
                || reservationRecordIndex
                    != callSiteInvocationCount + 2 + outputIndex
                || reservationRecordIndex >= body.RecordCount)
            {
                throw Invalid(
                    "The Standard F+I conditional-output reservation sequence is invalid.");
            }

            outputReservationRecordIndices[outputIndex]
                = reservationRecordIndex;
        }

        if (callSiteRecordIndex >= body.RecordCount
            || edgeRecordIndex >= body.RecordCount
            || callSiteRecordIndex != callSiteInvocationCount + 1
            || edgeRecordIndex
                != callSiteInvocationCount + outputCount + 2)
        {
            throw Invalid(
                "The Standard F+I conditional-output Body references an invalid record.");
        }

        for (var successIndex = 0;
            successIndex < successCallSiteCount;
            successIndex++)
        {
            var instructionOffset = successIndex * 2;
            var successRecordIndex
                = ReadByte(body, successCode, instructionOffset + 1);
            if (ReadByte(body, successCode, instructionOffset) != 0x90
                || successRecordIndex
                    != edgeRecordIndex + successIndex + 1
                || successRecordIndex >= body.RecordCount)
            {
                throw Invalid(
                    "The Standard F+I conditional-output success CallSites are not invoked in record order.");
            }

            successCallSiteRecordIndices[successIndex]
                = checked((ushort)successRecordIndex);
        }

        var successCallSiteRecordIndex = successCallSiteCount == 0
            ? -1
            : successCallSiteRecordIndices[^1];
        var successNestedCallSiteRecordIndex
            = successCallSiteCount == 2
                ? successCallSiteRecordIndices[0]
                : -1;
        if ((hasSuccessCall
                && successCallSiteRecordIndex + 1 != body.RecordCount)
            || (!hasSuccessCall
                && BinaryPrimitives.ReadUInt16LittleEndian(
                    successCode[2..]) != returnOutputVariable)
            || BinaryPrimitives.ReadUInt16LittleEndian(
                complementCode[2..]) != (fallbackVariable >= 0
                    ? fallbackVariable : statusVariable))
        {
            throw Invalid(
                "The Standard F+I conditional-output Body reads a different Home.");
        }

        var preludeCallCount = 0;
        var nestedCallCount = 0;
        var sawNestedCall = false;
        for (var recordIndex = 1;
            recordIndex < callSiteRecordIndex;
            recordIndex++)
        {
            var inputCallSite = body.GetRecord(recordIndex);
            if (inputCallSite.Length < 4)
            {
                throw Invalid(
                    "The Standard F+I conditional-output input CallSite is truncated.");
            }

            var inputCallPayload = inputCallSite[4..];
            if (inputCallSite[0] != 0x02
                || inputCallPayload.Length < 16)
            {
                throw Invalid(
                    "The Standard F+I conditional-output input CallSite is malformed.");
            }

            var resultKind = inputCallPayload[1];
            var resultIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                inputCallPayload[6..]);
            if (resultKind == 0 && !sawNestedCall && resultIndex == 0)
            {
                preludeCallCount++;
            }
            else if (resultKind == 1
                && resultIndex == nestedCallCount + 1)
            {
                sawNestedCall = true;
                nestedCallCount++;
            }
            else
            {
                throw Invalid(
                    "The Standard F+I conditional-output input CallSites do not form a prelude followed by ordered scalar results.");
            }
        }

        if (preludeCallCount + nestedCallCount
            != callSiteInvocationCount)
        {
            throw Invalid(
                "The Standard F+I conditional-output CallSite records do not match their invocation prefix.");
        }

        var preparationOffset = 0;
        for (var preludeIndex = 0;
            preludeIndex < preludeCallCount;
            preludeIndex++)
        {
            if (rawEntryCode[preparationOffset] != 0x90
                || rawEntryCode[preparationOffset + 1]
                    != preludeIndex + 1)
            {
                throw Invalid(
                    "The Standard F+I conditional-output prelude CallSites are not invoked first in record order.");
            }

            preparationOffset += 2;
        }

        var nestedCallInvocation = 0;
        while (preparationOffset < preparationPrefixLength)
        {
            if (rawEntryCode[preparationOffset] == 0x90)
            {
                if (rawEntryCode[preparationOffset + 1]
                    != preludeCallCount + nestedCallInvocation + 1)
                {
                    throw Invalid(
                        "The Standard F+I conditional-output nested CallSites are not invoked in record order.");
                }

                nestedCallInvocation++;
                preparationOffset += 2;
            }
            else if (rawEntryCode[preparationOffset] == 0x16)
            {
                preparationOffset += 2;
            }
            else if (rawEntryCode[preparationOffset] == 0xD4)
            {
                preparationOffset += 4;
            }
            else if (rawEntryCode[preparationOffset] == 0xD0)
            {
                preparationOffset += 4;
            }
            else if (rawEntryCode[preparationOffset] is 0xC0 or 0xC2
                or 0xC3)
            {
                preparationOffset += 4;
            }
            else if (rawEntryCode[preparationOffset] is 0x11 or 0x50
                || rawEntryCode[preparationOffset] is >= 0x20 and <= 0x2B
                    or >= 0x30 and <= 0x36)
            {
                preparationOffset += 2;
            }
            else
            {
                throw Invalid(
                    "The Standard F+I conditional-output input preparation prefix is invalid.");
            }
        }

        if (nestedCallInvocation != nestedCallCount)
        {
            throw Invalid(
                "The Standard F+I conditional-output nested CallSites are missing from the preparation prefix.");
        }

        var hasNestedCalls = nestedCallCount != 0;
        var callSite = body.GetRecord(callSiteRecordIndex);
        var callPayload = callSite[4..];
        var targetMethodMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(callPayload[4..]);
        if (callSite[0] != 0x02 || callPayload[0] != 1
            || callPayload[1] != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(callPayload[6..]) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(callPayload[12..]) != 0
            || targetMethodMapIndex >= body.MethodCount)
        {
            throw Invalid(
                "The Standard F+I conditional-output Body has an invalid CallSite.");
        }

        var methodToken = BinaryPrimitives.ReadUInt32LittleEndian(
            body.MethodMap[(targetMethodMapIndex * sizeof(uint))..]);
        var tokenKind = checked((byte)(methodToken >> 24));
        var tokenRow = checked((int)(methodToken & 0x00FF_FFFF));
        var targetMethodRow = tokenKind switch {
            0x16 => tokenRow,
            0x1C when tokenRow > 0 && tokenRow <= imports.Count
                && (imports.GetExpectedDefinitionForImportRow(tokenRow)
                    >> 24) == 0x16
                => checked((int)(imports
                    .GetExpectedDefinitionForImportRow(tokenRow)
                    & 0x00FF_FFFF)),
            _ => 0,
        };
        if (targetMethodRow == 0
            || foreignInterfaceBodyTargets is null
            || !foreignInterfaceBodyTargets.TryGetValue(targetMethodRow,
                out var foreignTarget))
        {
            throw Invalid(
                "The Standard F+I conditional-output CallSite has no selected InterfaceMethod target.");
        }

        var member = foreignTarget.Member;
        var statusType = member.ResultType.PrimitiveType;
        var outputContracts = member.NativeOutputContracts
            .OrderBy(static contract => contract.FormalOrdinal)
            .ToArray();
        if (member.ResultType.Kind != BoundInterfaceTypeKind.Primitive
            || statusType is not (PrimitiveType.Int32
                or PrimitiveType.UInt32)
            || member.DoesNotReturn
            || BinaryPrimitives.ReadUInt32LittleEndian(callPayload[8..])
                != (uint)(member.Parameters.Length + 1)
            || callPayload.Length
                != 16 + ((member.Parameters.Length + 1) * 4)
            || member.NativeCallableConvention is not
                NativeCallingConvention convention
            || foreignTarget.Definition.ForeignNativeInterface is not
                BoundForeignNativeInterfaceLayout layout
            || outputContracts.Length != outputCount)
        {
            throw Invalid(
                "The Standard F+I conditional-output CallSite does not match its selected output contract.");
        }

        var outputContract = outputContracts[0];
        var outputContractsByFormal
            = new Dictionary<int, NativeOutputContract>(outputCount);
        foreach (var contract in outputContracts)
        {
            if ((uint)contract.FormalOrdinal
                    >= (uint)member.Parameters.Length
                || !outputContractsByFormal.TryAdd(
                    contract.FormalOrdinal, contract)
                || contract.SuccessSelector
                    != outputContract.SuccessSelector
                || contract.ComplementSelector
                    != outputContract.ComplementSelector
                || contract.SuccessState != NativeOutputState.Live
                || contract.ComplementState != NativeOutputState.Vacant
                || (outputCount > 1
                    && !DirectNativeImportContract.IsOutputPointee(
                        contract.PointeeType)))
            {
                throw Invalid(
                    "The Standard F+I conditional-output members have incompatible NativeOutput relations.");
            }
        }

        var successOperator = GetForeignNativeOutputSuccessOperator(
            outputContract.SuccessSelector,
            outputContract.ComplementSelector, statusType);
        var complementOperator = GetForeignNativeOutputComplementOperator(
            outputContract.SuccessSelector,
            outputContract.ComplementSelector, statusType);
        var successComparison
            = GetForeignNativeOutputComparisonOpcode(successOperator);
        var complementComparison
            = GetForeignNativeOutputComparisonOpcode(complementOperator);
        var successOnTrue = successComparison is byte successCode
            && entryCode[callOffset + 15] == successCode;
        if ((!successOnTrue
                && (complementComparison is not byte complementCode
                    || entryCode[callOffset + 15] != complementCode))
            || (hasSuccessCall && !successOnTrue)
            || homeTypes[statusVariable] != statusType
            || (!hasFallback && statusType != function.ReturnType))
        {
            throw Invalid(
                "The Standard F+I output read is not guarded by its selected result predicate.");
        }

        var outputVariableByFormal = new Dictionary<int, int>(outputCount);
        var reservedOutputHomes = new HashSet<int>();
        for (var outputIndex = 0;
            outputIndex < outputReservationRecordIndices.Length;
            outputIndex++)
        {
            var reservationRecord = body.GetRecord(
                outputReservationRecordIndices[outputIndex]);
            if (reservationRecord.Length < 12)
            {
                throw Invalid(
                    "The Standard F+I conditional-output reservation is truncated.");
            }

            var reservationPayload = reservationRecord[4..];
            var outputHomeIndex = BinaryPrimitives
                .ReadUInt16LittleEndian(reservationPayload[2..]);
            var oneBasedFormal = BinaryPrimitives
                .ReadUInt32LittleEndian(reservationPayload[4..]);
            if (reservationRecord[0] != 0x0C
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    reservationPayload) != callSiteRecordIndex
                || oneBasedFormal == 0
                || oneBasedFormal > (uint)member.Parameters.Length
                || !outputContractsByFormal.TryGetValue(
                    checked((int)oneBasedFormal - 1),
                    out var reservationContract)
                || !outputVariables.Contains(outputHomeIndex)
                || homeRoles[outputHomeIndex] != 3
                || homeVariables[outputHomeIndex] != outputHomeIndex
                || homeTypes[outputHomeIndex]
                    != reservationContract.PointeeType
                || variables[outputHomeIndex].Type
                    != reservationContract.PointeeType
                || variables[outputHomeIndex].IsOut
                || variables[outputHomeIndex].IsInit
                || variables[outputHomeIndex].IsInitializationLocal
                || !outputVariableByFormal.TryAdd(
                    checked((int)oneBasedFormal - 1),
                    outputHomeIndex)
                || !reservedOutputHomes.Add(outputHomeIndex))
            {
                throw Invalid(
                    "The Standard F+I conditional-output reservation differs from its selected output formal and Home.");
            }
        }

        if (outputVariableByFormal.Count != outputCount
            || !reservedOutputHomes.SetEquals(outputVariables)
            || (hasSuccessCall
                ? !outputVariableByFormal.Values.Contains(
                        returnOutputVariable)
                : !outputVariableByFormal.Any(pair =>
                    pair.Value == returnOutputVariable
                    && outputContractsByFormal[pair.Key].PointeeType
                        == function.ReturnType)))
        {
            throw Invalid(
                "The Standard F+I conditional-output reservations do not cover the selected return and all output Homes.");
        }

        var edgeRecord = body.GetRecord(edgeRecordIndex);
        if (edgeRecord[0] != 0x06
            || BinaryPrimitives.ReadUInt16LittleEndian(edgeRecord[4..])
                != (successOnTrue ? 1 : 2)
            || BinaryPrimitives.ReadUInt16LittleEndian(edgeRecord[6..])
                != (successOnTrue ? 2 : 1))
        {
            throw Invalid(
                "The Standard F+I conditional-output Body has invalid branch targets.");
        }

        var receiverInput = callPayload[16..];
        if (receiverInput[0] != 2 || receiverInput[1] != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(receiverInput[2..])
                != 0)
        {
            throw Invalid(
                "The Standard F+I conditional-output CallSite has no receiver Home.");
        }

        var inputVariableLimit = hasFallback
            ? fallbackVariable : function.ParameterCount;
        var inputHomeVariables = new HashSet<int>();
        var preludeCallsBuilder
            = ImmutableArray.CreateBuilder<BoundCall>(preludeCallCount);
        for (var preludeIndex = 0;
            preludeIndex < preludeCallCount;
            preludeIndex++)
        {
            var preludeRecord = body.GetRecord(preludeIndex + 1);
            if (preludeRecord.Length < 4)
            {
                throw Invalid(
                    "The Standard F+I conditional-output prelude CallSite is truncated.");
            }

            var preludePayload = preludeRecord[4..];
            if (preludePayload.Length < 20)
            {
                throw Invalid(
                    "The Standard F+I conditional-output prelude CallSite is truncated.");
            }

            var preludeMethodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    preludePayload[4..]);
            if (preludeRecord[0] != 0x02
                || preludePayload[0] != 1
                || preludePayload[1] != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    preludePayload[2..]) != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    preludePayload[6..]) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    preludePayload[8..]) < 1
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    preludePayload[12..]) != 0
                || preludeMethodMapIndex >= body.MethodCount)
            {
                throw Invalid(
                    "The Standard F+I conditional-output prelude CallSite is outside the unit-call form.");
            }

            var preludeMethodToken = BinaryPrimitives
                .ReadUInt32LittleEndian(body.MethodMap[
                    (preludeMethodMapIndex * sizeof(uint))..]);
            var preludeTokenKind
                = checked((byte)(preludeMethodToken >> 24));
            var preludeTokenRow = checked((int)(
                preludeMethodToken & 0x00FF_FFFF));
            var preludeMethodRow = preludeTokenKind switch {
                0x16 => preludeTokenRow,
                0x1C when preludeTokenRow > 0
                    && preludeTokenRow <= imports.Count
                    && (imports.GetExpectedDefinitionForImportRow(
                        preludeTokenRow) >> 24) == 0x16
                    => checked((int)(imports
                        .GetExpectedDefinitionForImportRow(
                            preludeTokenRow) & 0x00FF_FFFF)),
                _ => 0,
            };
            if (preludeMethodRow == 0
                || foreignInterfaceBodyTargets is null
                || !foreignInterfaceBodyTargets.TryGetValue(
                    preludeMethodRow, out var preludeForeignTarget))
            {
                throw Invalid(
                    "The Standard F+I conditional-output prelude CallSite has no selected InterfaceMethod target.");
            }

            var preludeMember = preludeForeignTarget.Member;
            if (preludeMember.ResultType.Kind
                    != BoundInterfaceTypeKind.Primitive
                || preludeMember.ResultType.PrimitiveType
                    != PrimitiveType.Void
                || preludeMember.DoesNotReturn
                || !preludeMember.NativeOutputContracts.IsEmpty
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    preludePayload[8..])
                    != (uint)(preludeMember.Parameters.Length + 1)
                || preludePayload.Length
                    != 16 + ((preludeMember.Parameters.Length + 1) * 4)
                || preludeMember.NativeCallableConvention is not
                    NativeCallingConvention preludeConvention
                || preludeForeignTarget.Definition.ForeignNativeInterface
                    is not BoundForeignNativeInterfaceLayout
                        preludeLayout)
            {
                throw Invalid(
                    "The Standard F+I conditional-output prelude target is not a supported unit MethodDef without NativeOutput.");
            }

            var preludeReceiverInput = preludePayload[16..];
            if (preludeReceiverInput[0] != 2
                || preludeReceiverInput[1] != 1
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    preludeReceiverInput[2..]) != 0)
            {
                throw Invalid(
                    "The Standard F+I conditional-output prelude CallSite has no direct receiver Home.");
            }

            var preludeArguments
                = ImmutableArray.CreateBuilder<BoundExpression>(
                    preludeMember.Parameters.Length);
            var preludeParameterIndices
                = ImmutableArray.CreateBuilder<int>(
                    preludeMember.Parameters.Length);
            var preludeInputOffset = 20;
            for (var formal = 0;
                formal < preludeMember.Parameters.Length;
                formal++)
            {
                var parameter = preludeMember.Parameters[formal];
                var formalType = parameter.Type.PrimitiveType;
                var input = preludePayload.Slice(preludeInputOffset, 4);
                var homeIndex = BinaryPrimitives
                    .ReadUInt16LittleEndian(input[2..]);
                if (parameter.Type.Kind
                        != BoundInterfaceTypeKind.Primitive
                    || !(DirectNativeImportContract.IsScalar(formalType)
                        || formalType
                            == PrimitiveType.NativeRawPointer)
                    || input[0] != 2 || input[1] != 1
                    || homeIndex == 0
                    || homeIndex >= inputVariableLimit
                    || homeRoles[homeIndex] != 2
                    || homeVariables[homeIndex] != homeIndex
                    || homeTypes[homeIndex] != formalType
                    || variables[homeIndex].IsOut
                    || variables[homeIndex].IsInit)
                {
                    throw Invalid(
                        "A Standard F+I conditional-output prelude input does not match a direct scalar or pointer formal.");
                }

                _ = inputHomeVariables.Add(homeIndex);
                preludeArguments.Add(new BoundRead(
                    homeVariables[homeIndex], formalType, default));
                preludeParameterIndices.Add(formal);
                preludeInputOffset += 4;
            }

            var preludeDeclarationKey = preludeMember.DeclarationKey;
            if (preludeTokenKind == 0x1C)
            {
                preludeDeclarationKey = imports
                    .GetBindingForImportRow(preludeTokenRow).ExportKey;
                if (!StringComparer.Ordinal.Equals(
                        preludeDeclarationKey,
                        preludeMember.DeclarationKey)
                    && !preludeMember.AdditionalDeclarationKeys.Contains(
                        preludeDeclarationKey, StringComparer.Ordinal))
                {
                    throw Invalid(
                        "A Standard F+I conditional-output prelude Import does not select one of the exact member ExportKeys.");
                }
            }

            var preludeDeclaringInterface = new BoundInterfaceType(
                BoundInterfaceTypeKind.Nominal, PrimitiveType.Void,
                preludeForeignTarget.Definition.QualifiedName, -1, [],
                false, false, false, false) {
                ModuleImport
                    = preludeForeignTarget.Definition.ModuleImport,
            };
            var preludeIdentity = new BoundInterfaceMemberIdentity(
                preludeDeclaringInterface,
                preludeForeignTarget.MemberOrdinal,
                preludeDeclarationKey);
            preludeCallsBuilder.Add(new BoundCall(
                BoundCall.ForeignInterfaceFunction,
                preludeArguments.MoveToImmutable(), PrimitiveType.Void, default) {
                IsUnsafeAuthorized = true,
                ForeignInterfaceCall = new BoundForeignInterfaceCall(
                    new BoundRead(homeVariables[0],
                        PrimitiveType.NativeRawPointer, default),
                    preludeIdentity, preludeLayout.VTableByteOffset,
                    preludeConvention,
                    preludeMember.RequiresMutableReceiver,
                    preludeMember.DoesNotReturn,
                    preludeMember.NativeOutputContracts),
                ArgumentParameterIndices
                    = preludeParameterIndices.MoveToImmutable(),
            });
        }

        var registerInputs = new Dictionary<ushort, BoundExpression>();
        var constantOffset = 0;
        while (constantOffset < preparationPrefixLength)
        {
            var opcode = rawEntryCode[constantOffset];
            if (opcode == 0x90)
            {
                constantOffset += 2;
                continue;
            }
            if (opcode is 0x11 or 0x50
                || opcode is >= 0x20 and <= 0x2B
                    or >= 0x30 and <= 0x36)
            {
                constantOffset += 2;
                continue;
            }
            if (opcode == 0xD0)
            {
                constantOffset += 4;
                continue;
            }
            if (opcode is 0xC0 or 0xC2 or 0xC3)
            {
                constantOffset += 4;
                continue;
            }

            byte destination;
            ushort constantIndex;
            int instructionLength;
            if (opcode == 0x16)
            {
                var operand = rawEntryCode[constantOffset + 1];
                destination = (byte)(operand & 0x0F);
                constantIndex = (ushort)(operand >> 4);
                instructionLength = 2;
            }
            else if (opcode == 0xD4)
            {
                destination = rawEntryCode[constantOffset + 1];
                constantIndex = BinaryPrimitives
                    .ReadUInt16LittleEndian(rawEntryCode[
                        (constantOffset + 2)..]);
                instructionLength = 4;
            }
            else
            {
                throw Invalid(
                    "The Standard F+I conditional-output ConstantMap prefix is invalid.");
            }

            if (destination is 0 or >= 14
                || constantIndex >= scalarConstants.Length
                || !DirectNativeImportContract.IsScalar(
                    scalarConstants[constantIndex].Type))
            {
                throw Invalid(
                    "The Standard F+I conditional-output ConstantMap load is outside the scalar register subset.");
            }

            if (!registerInputs.TryAdd(destination,
                    scalarConstants[constantIndex]))
            {
                throw Invalid(
                    "The Standard F+I conditional-output Body reuses a ConstantMap register.");
            }

            constantOffset += instructionLength;
        }

        for (var nestedIndex = 0;
            nestedIndex < nestedCallCount;
            nestedIndex++)
        {
            var nestedCallSite = body.GetRecord(
                preludeCallCount + nestedIndex + 1);
            var nestedPayload = nestedCallSite[4..];
            var nestedMethodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    nestedPayload[4..]);
            if (nestedCallSite[0] != 0x02 || nestedPayload[0] != 1
                || nestedPayload[1] != 1
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    nestedPayload[6..]) != nestedIndex + 1
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    nestedPayload[12..]) != 0
                || nestedMethodMapIndex >= body.MethodCount)
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested CallSite is invalid.");
            }

            var nestedMethodToken = BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[(nestedMethodMapIndex * sizeof(uint))..]);
            if (nestedMethodToken == methodToken)
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested and outer CallSites share one MethodMap target.");
            }

            var nestedTokenKind
                = checked((byte)(nestedMethodToken >> 24));
            var nestedTokenRow = checked((int)(
                nestedMethodToken & 0x00FF_FFFF));
            var nestedTargetMethodRow = nestedTokenKind switch {
                0x16 => nestedTokenRow,
                0x1C when nestedTokenRow > 0
                    && nestedTokenRow <= imports.Count
                    && (imports.GetExpectedDefinitionForImportRow(
                        nestedTokenRow) >> 24) == 0x16
                    => checked((int)(imports
                        .GetExpectedDefinitionForImportRow(
                            nestedTokenRow) & 0x00FF_FFFF)),
                _ => 0,
            };
            if (nestedTargetMethodRow == 0
                || !foreignInterfaceBodyTargets.TryGetValue(
                    nestedTargetMethodRow, out var nestedForeignTarget))
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested CallSite has no selected InterfaceMethod target.");
            }

            var nestedMember = nestedForeignTarget.Member;
            var nestedResultType = nestedMember.ResultType.PrimitiveType;
            if (nestedMember.ResultType.Kind
                    != BoundInterfaceTypeKind.Primitive
                || !DirectNativeImportContract.IsScalar(nestedResultType)
                || nestedMember.DoesNotReturn
                || !nestedMember.NativeOutputContracts.IsEmpty
                || nestedMember.NativeCallableConvention is not
                    NativeCallingConvention nestedConvention
                || nestedForeignTarget.Definition
                    .ForeignNativeInterface is not
                    BoundForeignNativeInterfaceLayout nestedLayout
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    nestedPayload[8..])
                    != (uint)(nestedMember.Parameters.Length + 1)
                || nestedPayload.Length
                    != 20 + (nestedMember.Parameters.Length * 4))
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested CallSite is outside the scalar no-output subset.");
            }

            var nestedReceiverInput = nestedPayload[16..];
            var nestedReceiverHome
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    nestedReceiverInput[2..]);
            if (nestedReceiverInput[0] != 2
                || nestedReceiverInput[1] != 1
                || nestedReceiverHome >= inputVariableLimit
                || homeRoles[nestedReceiverHome] != 2
                || homeVariables[nestedReceiverHome] != nestedReceiverHome
                || homeTypes[nestedReceiverHome]
                    != PrimitiveType.NativeRawPointer)
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested CallSite has no direct receiver Home.");
            }
            if (nestedReceiverHome != 0)
            {
                _ = inputHomeVariables.Add(nestedReceiverHome);
            }

            var nestedArguments
                = ImmutableArray.CreateBuilder<BoundExpression>(
                    nestedMember.Parameters.Length);
            var nestedInputOffset = 20;
            for (var formal = 0;
                formal < nestedMember.Parameters.Length; formal++)
            {
                var input = nestedPayload.Slice(nestedInputOffset, 4);
                var formalType
                    = nestedMember.Parameters[formal].Type.PrimitiveType;
                var homeIndex = BinaryPrimitives
                    .ReadUInt16LittleEndian(input[2..]);
                BoundExpression? nestedRegisterInput = null;
                var isConstantInput = input[0] == 1 && input[1] == 1
                    && registerInputs.TryGetValue(homeIndex,
                        out nestedRegisterInput)
                    && nestedRegisterInput is BoundConstant nestedConstant
                    && nestedConstant.Type == formalType;
                var isHomeInput = input[0] == 2 && input[1] == 1
                    && homeIndex < inputVariableLimit
                    && homeRoles[homeIndex] == 2
                    && homeVariables[homeIndex] == homeIndex
                    && homeTypes[homeIndex] == formalType
                    && !variables[homeIndex].IsOut
                    && !variables[homeIndex].IsInit;
                if (!(DirectNativeImportContract.IsScalar(formalType)
                        || formalType == PrimitiveType.NativeRawPointer)
                    || !(isConstantInput || isHomeInput))
                {
                    throw Invalid(
                        "The Standard F+I conditional-output nested input does not match its selected formal.");
                }

                if (isConstantInput)
                {
                    nestedArguments.Add(
                        nestedRegisterInput
                        ?? throw Invalid(
                            "A nested F+I conditional-output ConstantMap input is missing."));
                }
                else
                {
                    if (homeIndex != 0)
                    {
                        _ = inputHomeVariables.Add(homeIndex);
                    }
                    nestedArguments.Add(new BoundRead(
                        homeVariables[homeIndex], formalType, default));
                }
                nestedInputOffset += 4;
            }

            var nestedDeclarationKey = nestedMember.DeclarationKey;
            if (nestedTokenKind == 0x1C)
            {
                nestedDeclarationKey = imports
                    .GetBindingForImportRow(nestedTokenRow).ExportKey;
                if (!StringComparer.Ordinal.Equals(nestedDeclarationKey,
                        nestedMember.DeclarationKey)
                    && !nestedMember.AdditionalDeclarationKeys.Contains(
                        nestedDeclarationKey, StringComparer.Ordinal))
                {
                    throw Invalid(
                        "A nested F+I conditional-output Import does not select one of the exact member ExportKeys.");
                }
            }

            var nestedDeclaringInterface = new BoundInterfaceType(
                BoundInterfaceTypeKind.Nominal, PrimitiveType.Void,
                nestedForeignTarget.Definition.QualifiedName, -1, [],
                false, false, false, false) {
                ModuleImport = nestedForeignTarget.Definition.ModuleImport,
            };
            var nestedIdentity = new BoundInterfaceMemberIdentity(
                nestedDeclaringInterface,
                nestedForeignTarget.MemberOrdinal, nestedDeclarationKey);
            var nestedCall = new BoundCall(
                BoundCall.ForeignInterfaceFunction,
                nestedArguments.MoveToImmutable(), nestedResultType, default) {
                IsUnsafeAuthorized = true,
                ForeignInterfaceCall = new BoundForeignInterfaceCall(
                    new BoundRead(
                        homeVariables[nestedReceiverHome],
                        PrimitiveType.NativeRawPointer, default),
                    nestedIdentity, nestedLayout.VTableByteOffset,
                    nestedConvention, nestedMember.RequiresMutableReceiver,
                    nestedMember.DoesNotReturn,
                    nestedMember.NativeOutputContracts),
                ArgumentParameterIndices = [
                    .. Enumerable.Range(0, nestedMember.Parameters.Length),
                ],
            };
            var nestedResultRegister
                = checked((ushort)(nestedIndex + 1));
            if (!registerInputs.TryAdd(nestedResultRegister,
                    new BoundUnsafeExpression(nestedCall, default)))
            {
                throw Invalid(
                    "The Standard F+I conditional-output Body reuses a nested result register.");
            }
        }

        var availableRegisters = new HashSet<ushort>();
        var writableExpressionRegisters = new HashSet<ushort>();
        var scalarExpressionResultRegisters = new HashSet<ushort>();
        var registerWriteOrder = new Dictionary<ushort, int>();
        var scalarExpressionHomeLoadCount = 0;
        var expressionNestedCallIndex = 0;
        var pendingScalarComparison
            = ((BoundExpression Left, BoundExpression Right,
                ushort Register, ushort RightRegister)?)null;
        for (var preparationOffset = preludeCallCount * 2;
            preparationOffset < preparationPrefixLength;)
        {
            var opcode = rawEntryCode[preparationOffset];
            if (pendingScalarComparison is not null && opcode != 0xC3)
            {
                throw Invalid(
                    "The Standard F+I conditional-output relation comparison is not immediately followed by SetCondition.");
            }

            if (opcode == 0x16)
            {
                var destination = (ushort)(
                    rawEntryCode[preparationOffset + 1] & 0x0F);
                if (!registerInputs.TryGetValue(destination,
                        out var constantInput)
                    || constantInput is not BoundConstant
                    || !availableRegisters.Add(destination))
                {
                    throw Invalid(
                        "The Standard F+I conditional-output ConstantMap register is reused or unavailable.");
                }

                preparationOffset += 2;
                continue;
            }

            if (opcode == 0xD4)
            {
                var destination = BinaryPrimitives.ReadUInt16LittleEndian(
                    rawEntryCode[(preparationOffset + 1)..]);
                if (!registerInputs.TryGetValue(destination,
                        out var constantInput)
                    || constantInput is not BoundConstant
                    || !availableRegisters.Add(destination))
                {
                    throw Invalid(
                        "The Standard F+I conditional-output ConstantMap register is reused or unavailable.");
                }

                preparationOffset += 4;
                continue;
            }

            if (opcode == 0x90)
            {
                var resultRegister
                    = checked((ushort)(expressionNestedCallIndex + 1));
                if (!registerInputs.ContainsKey(resultRegister)
                    || !availableRegisters.Add(resultRegister))
                {
                    throw Invalid(
                        "The Standard F+I conditional-output nested result register is reused or unavailable.");
                }

                expressionNestedCallIndex++;
                registerWriteOrder[resultRegister] = preparationOffset;
                preparationOffset += 2;
                continue;
            }

            var destinationRegister = checked((ushort)(
                rawEntryCode[preparationOffset + 1] & 0x0F));
            var sourceRegister = checked((ushort)(
                rawEntryCode[preparationOffset + 1] >> 4));
            if (opcode == 0x11)
            {
                var formalOrdinal = sourceRegister;
                if (destinationRegister is 0 or >= 14
                    || formalOrdinal == 0
                    || formalOrdinal >= inputVariableLimit
                    || homeRoles[formalOrdinal] != 2
                    || homeVariables[formalOrdinal] != formalOrdinal
                    || variables[formalOrdinal].IsOut
                    || variables[formalOrdinal].IsInit
                    || !availableRegisters.Add(destinationRegister)
                    || !registerInputs.TryAdd(destinationRegister,
                        new BoundRead(formalOrdinal,
                            homeTypes[formalOrdinal], default)))
                {
                    throw Invalid(
                        "The Standard F+I conditional-output scalar expression has an invalid Home load.");
                }

                writableExpressionRegisters.Add(destinationRegister);
                scalarExpressionHomeLoadCount++;
                registerWriteOrder[destinationRegister] = preparationOffset;
                preparationOffset += 2;
                continue;
            }

            if (opcode == 0xC2)
            {
                var control = rawEntryCode[preparationOffset + 1];
                var payload = BinaryPrimitives.ReadUInt16LittleEndian(
                    rawEntryCode[(preparationOffset + 2)..]);
                var comparisonDestination
                    = checked((ushort)(payload & 0x0F));
                var result1 = (payload >> 4) & 0x0F;
                var source0Register
                    = checked((ushort)((payload >> 8) & 0x0F));
                var source1Register
                    = checked((ushort)(payload >> 12));
                if (comparisonDestination is 0 or >= 14
                    || result1 != 15
                    || source0Register != comparisonDestination
                    || source1Register is 0 or >= 14
                    || !availableRegisters.Contains(comparisonDestination)
                    || !availableRegisters.Contains(source1Register)
                    || !writableExpressionRegisters.Contains(
                        comparisonDestination))
                {
                    throw Invalid(
                        "The Standard F+I conditional-output Boolean comparison has an invalid register mapping.");
                }

                var leftInput = registerInputs[source0Register];
                var rightInput = registerInputs[source1Register];
                if (control == 0)
                {
                    if (leftInput.Type != rightInput.Type
                        || !TryGetForeignInterfaceScalarComparisonPredicateMask(
                            leftInput.Type, TokenKind.EqualsEquals, out _))
                    {
                        throw Invalid(
                            "The Standard F+I conditional-output relation comparison has incompatible operands.");
                    }

                    pendingScalarComparison
                        = (leftInput, rightInput, comparisonDestination,
                            source1Register);
                    preparationOffset += 4;
                    continue;
                }

                var operationToken = control switch {
                    0x01 => TokenKind.EqualsEquals,
                    0x02 => TokenKind.BangEquals,
                    _ => throw Unsupported(
                        "A Standard F+I conditional-output Boolean comparison uses an unsupported control."),
                };
                var comparison = new BoundBinary(operationToken,
                    leftInput, rightInput, PrimitiveType.Bool, default) {
                    OverflowBehavior
                        = BoundIntegerOverflowBehavior.Checked,
                };
                if (!TryGetForeignInterfaceScalarBinaryEncoding(comparison,
                        out var expectedOpcode,
                        out var expectedControl)
                    || expectedOpcode != 0xC2
                    || expectedControl != control)
                {
                    throw Invalid(
                        "The Standard F+I conditional-output Boolean comparison has incompatible operands.");
                }

                _ = scalarExpressionResultRegisters.Remove(
                    source1Register);
                _ = scalarExpressionResultRegisters.Remove(
                    comparisonDestination);
                registerInputs[comparisonDestination] = comparison;
                _ = scalarExpressionResultRegisters.Add(
                    comparisonDestination);
                registerWriteOrder[comparisonDestination]
                    = preparationOffset;
                preparationOffset += 4;
                continue;
            }

            if (opcode == 0xC3)
            {
                var predicateMask = rawEntryCode[preparationOffset + 1];
                var payload = BinaryPrimitives.ReadUInt16LittleEndian(
                    rawEntryCode[(preparationOffset + 2)..]);
                var destinationRegister
                    = checked((ushort)(payload & 0x0F));
                var conditionRegister
                    = checked((ushort)((payload >> 4) & 0x0F));
                var unused0 = (payload >> 8) & 0x0F;
                var unused1 = payload >> 12;
                if (pendingScalarComparison is not
                        { } pendingComparison
                    || destinationRegister != pendingComparison.Register
                    || conditionRegister != pendingComparison.Register
                    || unused0 != 15 || unused1 != 15
                    || !TryGetForeignInterfaceScalarComparisonOperator(
                        pendingComparison.Left.Type, predicateMask,
                        out var operationToken))
                {
                    throw Invalid(
                        "The Standard F+I conditional-output SetCondition does not match a pending relation comparison.");
                }

                var comparison = new BoundBinary(operationToken,
                    pendingComparison.Left, pendingComparison.Right,
                    PrimitiveType.Bool, default) {
                    OverflowBehavior
                        = BoundIntegerOverflowBehavior.Checked,
                };
                _ = scalarExpressionResultRegisters.Remove(
                    pendingComparison.RightRegister);
                _ = scalarExpressionResultRegisters.Remove(
                    pendingComparison.Register);
                registerInputs[pendingComparison.Register] = comparison;
                _ = scalarExpressionResultRegisters.Add(
                    pendingComparison.Register);
                registerWriteOrder[pendingComparison.Register]
                    = preparationOffset;
                pendingScalarComparison = null;
                preparationOffset += 4;
                continue;
            }

            if (opcode == 0xC0)
            {
                var control = rawEntryCode[preparationOffset + 1];
                var payload = BinaryPrimitives.ReadUInt16LittleEndian(
                    rawEntryCode[(preparationOffset + 2)..]);
                var checkedDestination
                    = checked((ushort)(payload & 0x0F));
                var result1 = (payload >> 4) & 0x0F;
                var checkedSource0
                    = checked((ushort)((payload >> 8) & 0x0F));
                var checkedSource1
                    = checked((ushort)(payload >> 12));
                if (checkedDestination is 0 or >= 14 || result1 != 15
                    || checkedSource0 is 0 or >= 14
                    || checkedDestination != checkedSource0
                    || !availableRegisters.Contains(checkedDestination)
                    || !writableExpressionRegisters.Contains(
                        checkedDestination))
                {
                    throw Invalid(
                        "The Standard F+I conditional-output checked operation has an invalid register mapping.");
                }

                var checkedLeftExpression
                    = registerInputs[checkedSource0];
                BoundExpression checkedOperation;
                if (control == 0x04)
                {
                    if (checkedSource1 != 15)
                    {
                        throw Invalid(
                            "The Standard F+I conditional-output checked negation has an invalid unused input.");
                    }

                    var checkedUnary = new BoundUnary(TokenKind.Minus,
                        checkedLeftExpression, checkedLeftExpression.Type,
                        default) {
                        OverflowBehavior
                            = BoundIntegerOverflowBehavior.Checked,
                    };
                    if (!TryGetForeignInterfaceScalarUnaryEncoding(
                            checkedUnary,
                            out var expectedOpcode,
                            out var expectedControl)
                        || expectedOpcode != 0xC0
                        || expectedControl != control)
                    {
                        throw Invalid(
                            "The Standard F+I conditional-output checked negation has an unsupported type.");
                    }

                    checkedOperation = checkedUnary;
                }
                else
                {
                    var operationToken = control switch {
                        0x01 => TokenKind.Plus,
                        0x02 => TokenKind.Minus,
                        0x03 => TokenKind.Star,
                        0x0C => TokenKind.Slash,
                        0x0D => TokenKind.Percent,
                        _ => throw Unsupported(
                            "A Standard F+I conditional-output checked operation uses an unsupported control."),
                    };
                    if (checkedSource1 is 0 or >= 14
                        || !availableRegisters.Contains(checkedSource1))
                    {
                        throw Invalid(
                            "The Standard F+I conditional-output checked operation reads an unavailable input.");
                    }

                    var checkedRightExpression
                        = registerInputs[checkedSource1];
                    var checkedBinary = new BoundBinary(operationToken,
                        checkedLeftExpression, checkedRightExpression,
                        checkedLeftExpression.Type, default) {
                        OverflowBehavior
                            = BoundIntegerOverflowBehavior.Checked,
                    };
                    if (!TryGetForeignInterfaceScalarBinaryEncoding(
                            checkedBinary, out var expectedOpcode,
                            out var expectedControl)
                        || expectedOpcode != 0xC0
                        || expectedControl != control)
                    {
                        throw Invalid(
                            "The Standard F+I conditional-output checked operation has incompatible types.");
                    }

                    checkedOperation = checkedBinary;
                    _ = scalarExpressionResultRegisters.Remove(
                        checkedSource1);
                }

                _ = scalarExpressionResultRegisters.Remove(
                    checkedDestination);
                registerInputs[checkedDestination] = checkedOperation;
                _ = scalarExpressionResultRegisters.Add(
                    checkedDestination);
                registerWriteOrder[checkedDestination] = preparationOffset;
                preparationOffset += 4;
                continue;
            }

            if (opcode == 0xD0)
            {
                var control = rawEntryCode[preparationOffset + 1];
                var conversionSourceRegister
                    = checked((ushort)(control & 0x0F));
                var conversionDestinationRegister
                    = checked((ushort)(control >> 4));
                var typeMapIndex = BinaryPrimitives
                    .ReadUInt16LittleEndian(rawEntryCode[
                        (preparationOffset + 2)..]);
                if (conversionSourceRegister is 0 or >= 14
                    || conversionDestinationRegister is 0 or >= 14
                    || typeMapIndex >= body.TypeCount
                    || !availableRegisters.Contains(
                        conversionSourceRegister)
                    || !registerInputs.TryGetValue(
                        conversionSourceRegister,
                        out var sourceExpression))
                {
                    throw Invalid(
                        "The Standard F+I checked numeric conversion has an invalid register mapping.");
                }

                var destinationIsAvailable
                    = availableRegisters.Contains(
                        conversionDestinationRegister);
                if (destinationIsAvailable
                    ? !writableExpressionRegisters.Contains(
                        conversionDestinationRegister)
                    : registerInputs.ContainsKey(
                        conversionDestinationRegister))
                {
                    throw Invalid(
                        "The Standard F+I checked numeric conversion overwrites a non-expression register.");
                }

                var destinationType = GetBodyTypeMapType(typeMapIndex);
                var conversion = new BoundCast(sourceExpression,
                    destinationType, default);
                if (!IsForeignInterfaceCheckedNumericConversion(conversion))
                {
                    throw Invalid(
                        "The Standard F+I checked numeric conversion is invalid for its complete types.");
                }

                if (!destinationIsAvailable)
                {
                    _ = availableRegisters.Add(
                        conversionDestinationRegister);
                    _ = writableExpressionRegisters.Add(
                        conversionDestinationRegister);
                }

                _ = scalarExpressionResultRegisters.Remove(
                    conversionSourceRegister);
                _ = scalarExpressionResultRegisters.Remove(
                    conversionDestinationRegister);
                registerInputs[conversionDestinationRegister] = conversion;
                _ = scalarExpressionResultRegisters.Add(
                    conversionDestinationRegister);
                registerWriteOrder[conversionDestinationRegister]
                    = preparationOffset;
                preparationOffset += 4;
                continue;
            }

            if (opcode == 0x50)
            {
                if (destinationRegister is 0 or >= 14
                    || !availableRegisters.Contains(destinationRegister)
                    || !writableExpressionRegisters.Contains(
                        destinationRegister))
                {
                    throw Invalid(
                        "The Standard F+I conditional-output total conversion has an invalid register mapping.");
                }

                var source = registerInputs[destinationRegister];
                var destinationType = GetBodyTypeMapType(sourceRegister);
                var conversion = new BoundCast(source, destinationType,
                    default);
                if (!TryGetGeneralPredicateConversion(conversion, out _, out _))
                {
                    throw Invalid(
                        "The Standard F+I conditional-output conversion is not total for its complete types.");
                }

                _ = scalarExpressionResultRegisters.Remove(
                    destinationRegister);
                registerInputs[destinationRegister] = conversion;
                _ = scalarExpressionResultRegisters.Add(
                    destinationRegister);
                registerWriteOrder[destinationRegister]
                    = preparationOffset;
                preparationOffset += 2;
                continue;
            }

            if (opcode is not (>= 0x20 and <= 0x2B
                    or >= 0x30 and <= 0x36)
                || destinationRegister is 0 or >= 14
                || sourceRegister is 0 or >= 14
                || !availableRegisters.Contains(destinationRegister)
                || !availableRegisters.Contains(sourceRegister)
                || !writableExpressionRegisters.Contains(
                    destinationRegister))
            {
                throw Invalid(
                    "The Standard F+I conditional-output scalar operation reads an unavailable or non-expression register.");
            }

            var leftExpression = registerInputs[destinationRegister];
            var rightExpression = registerInputs[sourceRegister];
            BoundExpression operation;
            if (opcode is 0x20 or 0x26 or 0x33)
            {
                if (sourceRegister != destinationRegister)
                {
                    throw Invalid(
                        "The Standard F+I conditional-output unary operation has an invalid register mapping.");
                }

                var unaryOperator = opcode == 0x33
                    ? leftExpression.Type == PrimitiveType.Bool
                        ? TokenKind.Bang : TokenKind.Tilde
                    : TokenKind.Minus;
                var unary = new BoundUnary(unaryOperator, leftExpression,
                    leftExpression.Type, default) {
                    OverflowBehavior = opcode == 0x20
                        ? BoundIntegerOverflowBehavior.Wrapping
                        : BoundIntegerOverflowBehavior.Checked,
                };
                if (!TryGetForeignInterfaceScalarUnaryEncoding(unary,
                        out var expectedOpcode, out var expectedControl)
                    || expectedOpcode != opcode || expectedControl != 0)
                {
                    throw Invalid(
                        "The Standard F+I conditional-output unary operation has an unsupported type or overflow policy.");
                }

                operation = unary;
            }
            else
            {
                var operationToken = opcode switch {
                    0x21 or 0x27 => TokenKind.Plus,
                    0x22 or 0x28 => TokenKind.Minus,
                    0x23 or 0x29 => TokenKind.Star,
                    0x2A => TokenKind.Slash,
                    0x2B => TokenKind.Percent,
                    0x30 => TokenKind.Ampersand,
                    0x31 => TokenKind.Pipe,
                    0x32 => TokenKind.Caret,
                    _ => throw Unsupported(
                        "A Standard F+I conditional-output binary operation uses an unsupported opcode."),
                };
                var binary = new BoundBinary(operationToken,
                    leftExpression, rightExpression, leftExpression.Type,
                    default) {
                    OverflowBehavior = opcode is 0x21 or 0x22 or 0x23
                        ? BoundIntegerOverflowBehavior.Wrapping
                        : BoundIntegerOverflowBehavior.Checked,
                };
                if (!TryGetForeignInterfaceScalarBinaryEncoding(binary,
                        out var expectedOpcode, out var expectedControl)
                    || expectedOpcode != opcode || expectedControl != 0)
                {
                    throw Invalid(
                        "The Standard F+I conditional-output binary operation has incompatible types or overflow policy.");
                }

                operation = binary;
            }

            _ = scalarExpressionResultRegisters.Remove(destinationRegister);
            _ = scalarExpressionResultRegisters.Remove(sourceRegister);
            registerInputs[destinationRegister] = operation;
            _ = scalarExpressionResultRegisters.Add(destinationRegister);
            registerWriteOrder[destinationRegister] = preparationOffset;
            preparationOffset += 2;
        }

        if (pendingScalarComparison is not null)
        {
            throw Invalid(
                "The Standard F+I conditional-output relation comparison has no SetCondition.");
        }

        if (expressionNestedCallIndex != nestedCallCount)
        {
            throw Invalid(
                "The Standard F+I conditional-output preparation prefix has an incomplete nested-call register sequence.");
        }

        var argumentsByFormal = new BoundExpression?[
            member.Parameters.Length];
        var argumentRegisterByFormal = new int[member.Parameters.Length];
        Array.Fill(argumentRegisterByFormal, -1);
        var nestedFormalByRegister = new Dictionary<ushort, int>(
            nestedCallCount);
        var nestedFormalIndices = new HashSet<int>();
        var inputOffset = 20;
        var nestedInputCount = 0;
        var scalarExpressionInputRegisterCount = 0;
        var hasThrowingScalarExpressionInput = false;
        for (var formal = 0; formal < member.Parameters.Length; formal++)
        {
            var input = callPayload.Slice(inputOffset, 4);
            var selectorKind = input[0];
            var sourceUse = input[1];
            var homeIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                input[2..]);
            var formalType = member.Parameters[formal].Type.PrimitiveType;
            if (outputContractsByFormal.TryGetValue(
                    formal, out var formalOutputContract))
            {
                if (selectorKind != 2 || sourceUse != 6
                    || homeIndex != outputVariableByFormal[formal]
                    || homeRoles[homeIndex] != 3
                    || homeTypes[homeIndex]
                        != formalOutputContract.PointeeType
                    || formalType != formalOutputContract.PointeeType)
                {
                    throw Invalid(
                        "The Standard F+I conditional-output input is not its reserved output Home.");
                }

                argumentsByFormal[formal]
                    = new BoundNativeOutputReservation(
                        homeVariables[homeIndex],
                        formalOutputContract.PointeeType, default);
            }
            else if (selectorKind == 1 && sourceUse == 1
                && availableRegisters.Contains(homeIndex)
                && registerInputs.TryGetValue(homeIndex,
                    out var registerInput)
                && registerInput.Type == formalType)
            {
                if (registerInput is not BoundConstant)
                {
                    if (registerInput is BoundUnary or BoundBinary or BoundCast)
                    {
                        if (!hasScalarExpressions
                            || !scalarExpressionResultRegisters.Remove(
                                homeIndex))
                        {
                            throw Invalid(
                                "The Standard F+I conditional-output input is not a distinct scalar expression result.");
                        }

                        var expressionConstants
                            = new HashSet<(PrimitiveType Type, ScalarBits Bits)>();
                        if (!TryAnalyzeForeignInterfaceScalarInputExpression(
                                registerInput, formalType, function,
                                inputVariableLimit, expressionConstants,
                                out var expressionRegisterCount))
                        {
                            throw Invalid(
                                "The Standard F+I conditional-output scalar expression does not match its selected formal.");
                        }

                        scalarExpressionInputRegisterCount = checked(
                            scalarExpressionInputRegisterCount
                            + expressionRegisterCount);
                        hasThrowingScalarExpressionInput |=
                            ForeignInterfaceScalarInputExpressionMayThrow(
                                registerInput);
                        CollectForeignInterfaceScalarInputHomeVariables(
                            registerInput, inputHomeVariables);
                    }
                    else if (hasNestedCalls)
                    {
                        if (GetNestedForeignInterfaceCall(registerInput)
                                is null
                            || !nestedFormalByRegister.TryAdd(homeIndex,
                                formal)
                            || !nestedFormalIndices.Add(formal))
                        {
                            throw Invalid(
                                "The Standard F+I conditional-output input reuses a nested result register.");
                        }

                        nestedInputCount++;
                    }
                    else
                    {
                        throw Invalid(
                            "The Standard F+I conditional-output register input is not a selected constant.");
                    }
                }

                argumentsByFormal[formal] = registerInput;
                argumentRegisterByFormal[formal] = homeIndex;
            }
            else
            {
                if (homeIndex >= body.HomeCount
                    || selectorKind != 2 || sourceUse != 1
                    || homeIndex == 0
                    || homeIndex >= inputVariableLimit
                    || homeRoles[homeIndex] != 2
                    || homeVariables[homeIndex] != homeIndex
                    || homeTypes[homeIndex] != formalType)
                {
                    throw Invalid(
                        "The Standard F+I conditional-output source input does not match its selected formal.");
                }

                _ = inputHomeVariables.Add(homeIndex);
                argumentsByFormal[formal] = new BoundRead(
                    homeVariables[homeIndex], formalType, default);
            }

            inputOffset += 4;
        }

        if (nestedInputCount != nestedCallCount
            || nestedFormalByRegister.Count != nestedCallCount
            || scalarExpressionInputRegisterCount
                != scalarExpressionHomeLoadCount
            || scalarExpressionResultRegisters.Count != 0
            || (hasThrowingScalarExpressionInput
                && nestedInputCount != 0
                && Enumerable.Range(0, member.Parameters.Length).Any(
                formal => !outputContractsByFormal.ContainsKey(formal)
                        && !nestedFormalIndices.Contains(formal)
                        && !DirectNativeImportContract.IsScalar(
                            member.Parameters[formal].Type.PrimitiveType)))
            || inputHomeVariables.Count != inputVariableLimit - 1)
        {
            throw Invalid(
                "The Standard F+I conditional-output inputs do not match their source parameters and register values.");
        }

        for (var nestedRegister = 1;
            nestedRegister <= nestedCallCount;
            nestedRegister++)
        {
            if (!nestedFormalByRegister.ContainsKey(
                    checked((ushort)nestedRegister)))
            {
                throw Invalid(
                    "The Standard F+I conditional-output Body does not use each nested result register.");
            }
        }

        var arguments = ImmutableArray.CreateBuilder<BoundExpression>(
            member.Parameters.Length);
        var argumentParameterIndices
            = ImmutableArray.CreateBuilder<int>(member.Parameters.Length);
        // Final register definitions retain nested-call and expression order;
        // Home and constant inputs are pure.
        var orderedFormals = Enumerable.Range(0, member.Parameters.Length)
            .OrderBy(formal =>
            {
                var register = argumentRegisterByFormal[formal];
                return register >= 0
                    && registerWriteOrder.TryGetValue(
                        checked((ushort)register), out var writeOrder)
                    ? writeOrder : int.MaxValue;
            })
            .ThenBy(static formal => formal);
        foreach (var formal in orderedFormals)
        {
            arguments.Add(argumentsByFormal[formal]
                ?? throw Invalid(
                    "The Standard F+I conditional-output CallSite omits a formal input."));
            argumentParameterIndices.Add(formal);
        }

        var statusHomeVariable = homeVariables[statusVariable];
        if (statusHomeVariable != statusVariable
            || variables[statusHomeVariable].IsInitializationLocal
            || variables[statusHomeVariable].Type != statusType)
        {
            throw Invalid(
                "The Standard F+I conditional-output Homes do not match their source locals.");
        }

        foreach (var outputVariable in outputVariables)
        {
            if (homeVariables[outputVariable] != outputVariable
                || variables[outputVariable].IsInitializationLocal
                || variables[outputVariable].Type
                    != homeTypes[outputVariable])
            {
                throw Invalid(
                    "A Standard F+I conditional-output Home does not match its output local.");
            }
        }

        var declarationKey = member.DeclarationKey;
        if (tokenKind == 0x1C)
        {
            declarationKey = imports.GetBindingForImportRow(tokenRow)
                .ExportKey;
            if (!StringComparer.Ordinal.Equals(declarationKey,
                    member.DeclarationKey)
                && !member.AdditionalDeclarationKeys.Contains(
                    declarationKey, StringComparer.Ordinal))
            {
                throw Invalid(
                    "A ForeignNativeInterface CallSite Import does not select one of the exact member ExportKeys.");
            }
        }

        var declaringInterface = new BoundInterfaceType(
            BoundInterfaceTypeKind.Nominal, PrimitiveType.Void,
            foreignTarget.Definition.QualifiedName, -1, [],
            false, false, false, false) {
            ModuleImport = foreignTarget.Definition.ModuleImport,
        };
        var memberIdentity = new BoundInterfaceMemberIdentity(
            declaringInterface, foreignTarget.MemberOrdinal, declarationKey);
        var call = new BoundCall(BoundCall.ForeignInterfaceFunction,
            arguments.MoveToImmutable(), statusType, default) {
                IsUnsafeAuthorized = true,
                ForeignInterfaceCall = new BoundForeignInterfaceCall(
                    new BoundRead(homeVariables[0],
                        PrimitiveType.NativeRawPointer, default),
                    memberIdentity, layout.VTableByteOffset, convention,
                    member.RequiresMutableReceiver, member.DoesNotReturn,
                    member.NativeOutputContracts),
                ArgumentParameterIndices
                    = argumentParameterIndices.MoveToImmutable(),
            };
        BoundCall? successNestedCall = null;
        if (successNestedCallSiteRecordIndex >= 0)
        {
            var nestedRecord = body.GetRecord(
                successNestedCallSiteRecordIndex);
            if (nestedRecord.Length < 4)
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested success CallSite is truncated.");
            }

            var nestedPayload = nestedRecord[4..];
            if (nestedRecord[0] != 0x02
                || nestedPayload.Length < 20
                || nestedPayload[0] != 1
                || nestedPayload[1] != 1
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    nestedPayload[2..]) != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    nestedPayload[6..]) != 1
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    nestedPayload[12..]) != 0)
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested success CallSite is malformed.");
            }

            var nestedMethodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    nestedPayload[4..]);
            if (nestedMethodMapIndex >= body.MethodCount)
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested success CallSite has an invalid MethodMap index.");
            }

            var nestedMethodToken = BinaryPrimitives
                .ReadUInt32LittleEndian(body.MethodMap[
                    (nestedMethodMapIndex * sizeof(uint))..]);
            var nestedTokenKind
                = checked((byte)(nestedMethodToken >> 24));
            var nestedTokenRow = checked((int)(
                nestedMethodToken & 0x00FF_FFFF));
            var nestedMethodRow = nestedTokenKind switch {
                0x16 => nestedTokenRow,
                0x1C when nestedTokenRow > 0
                    && nestedTokenRow <= imports.Count
                    && (imports.GetExpectedDefinitionForImportRow(
                        nestedTokenRow) >> 24) == 0x16
                    => checked((int)(imports
                        .GetExpectedDefinitionForImportRow(
                            nestedTokenRow) & 0x00FF_FFFF)),
                _ => 0,
            };
            if (nestedMethodRow == 0
                || foreignInterfaceBodyTargets is null
                || !foreignInterfaceBodyTargets.TryGetValue(
                    nestedMethodRow, out var nestedForeignTarget))
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested success CallSite has no selected InterfaceMethod target.");
            }

            var nestedMember = nestedForeignTarget.Member;
            var nestedResultType = nestedMember.ResultType.PrimitiveType;
            if (nestedMember.ResultType.Kind
                    != BoundInterfaceTypeKind.Primitive
                || !DirectNativeImportContract.IsScalar(nestedResultType)
                || nestedMember.DoesNotReturn
                || !nestedMember.NativeOutputContracts.IsEmpty
                || nestedMember.NativeCallableConvention is not
                    NativeCallingConvention nestedConvention
                || nestedForeignTarget.Definition.ForeignNativeInterface
                    is not BoundForeignNativeInterfaceLayout nestedLayout
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    nestedPayload[8..])
                    != (uint)(nestedMember.Parameters.Length + 1)
                || nestedPayload.Length
                    != 20 + (nestedMember.Parameters.Length * 4))
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested success target is outside the scalar no-output direct-Home subset.");
            }

            var nestedReceiverInput = nestedPayload[16..];
            if (nestedReceiverInput[0] != 2
                || nestedReceiverInput[1] != 1
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    nestedReceiverInput[2..]) != 0)
            {
                throw Invalid(
                    "The Standard F+I conditional-output nested success CallSite has no direct receiver Home.");
            }

            var nestedArguments
                = ImmutableArray.CreateBuilder<BoundExpression>(
                    nestedMember.Parameters.Length);
            var nestedParameterIndices
                = ImmutableArray.CreateBuilder<int>(
                    nestedMember.Parameters.Length);
            var nestedInputOffset = 20;
            for (var nestedFormal = 0;
                nestedFormal < nestedMember.Parameters.Length;
                nestedFormal++)
            {
                var parameter = nestedMember.Parameters[nestedFormal];
                var formalType = parameter.Type.PrimitiveType;
                var input = nestedPayload.Slice(nestedInputOffset, 4);
                var homeIndex = BinaryPrimitives
                    .ReadUInt16LittleEndian(input[2..]);
                if (parameter.Type.Kind
                        != BoundInterfaceTypeKind.Primitive
                    || !(DirectNativeImportContract.IsScalar(formalType)
                        || formalType
                            == PrimitiveType.NativeRawPointer
                        || NativePointerTypes.IsType(formalType)
                        || NativeFunctionPointerTypes.IsType(formalType))
                    || input[0] != 2 || input[1] != 1
                    || homeIndex == 0
                    || homeIndex >= inputVariableLimit
                    || homeRoles[homeIndex] != 2
                    || homeVariables[homeIndex] != homeIndex
                    || homeTypes[homeIndex] != formalType
                    || variables[homeIndex].IsOut
                    || variables[homeIndex].IsInit)
                {
                    throw Invalid(
                        "A Standard F+I conditional-output nested success input does not match a direct scalar or pointer parameter Home.");
                }

                _ = inputHomeVariables.Add(homeIndex);
                nestedArguments.Add(new BoundRead(
                    homeVariables[homeIndex], formalType, default));
                nestedParameterIndices.Add(nestedFormal);
                nestedInputOffset += 4;
            }

            var nestedDeclarationKey = nestedMember.DeclarationKey;
            if (nestedTokenKind == 0x1C)
            {
                nestedDeclarationKey = imports
                    .GetBindingForImportRow(nestedTokenRow).ExportKey;
                if (!StringComparer.Ordinal.Equals(nestedDeclarationKey,
                        nestedMember.DeclarationKey)
                    && !nestedMember.AdditionalDeclarationKeys.Contains(
                        nestedDeclarationKey, StringComparer.Ordinal))
                {
                    throw Invalid(
                        "A Standard F+I conditional-output nested success Import does not select one of the exact member ExportKeys.");
                }
            }

            var nestedDeclaringInterface = new BoundInterfaceType(
                BoundInterfaceTypeKind.Nominal, PrimitiveType.Void,
                nestedForeignTarget.Definition.QualifiedName, -1, [],
                false, false, false, false) {
                ModuleImport
                    = nestedForeignTarget.Definition.ModuleImport,
            };
            var nestedIdentity = new BoundInterfaceMemberIdentity(
                nestedDeclaringInterface,
                nestedForeignTarget.MemberOrdinal,
                nestedDeclarationKey);
            successNestedCall = new BoundCall(
                BoundCall.ForeignInterfaceFunction,
                nestedArguments.MoveToImmutable(), nestedResultType,
                default) {
                IsUnsafeAuthorized = true,
                ForeignInterfaceCall = new BoundForeignNativeInterfaceCall(
                    new BoundRead(homeVariables[0],
                        PrimitiveType.NativeRawPointer, default),
                    nestedIdentity, nestedLayout.VTableByteOffset,
                    nestedConvention, nestedMember.RequiresMutableReceiver,
                    nestedMember.DoesNotReturn,
                    nestedMember.NativeOutputContracts),
                ArgumentParameterIndices
                    = nestedParameterIndices.MoveToImmutable(),
            };
        }

        BoundCall? successCall = null;
        if (hasSuccessCall)
        {
            var successRecord = body.GetRecord(
                successCallSiteRecordIndex);
            if (successRecord.Length < 4)
            {
                throw Invalid(
                    "The Standard F+I conditional-output success CallSite is truncated.");
            }

            var successPayload = successRecord[4..];
            if (successRecord[0] != 0x02
                || successPayload.Length < 20
                || successPayload[0] != 1
                || successPayload[1] != 1
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    successPayload[6..]) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    successPayload[12..]) != 0)
            {
                throw Invalid(
                    "The Standard F+I conditional-output success CallSite has an invalid result shape.");
            }

            var successMethodMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(
                    successPayload[4..]);
            if (successMethodMapIndex >= body.MethodCount)
            {
                throw Invalid(
                    "The Standard F+I conditional-output success CallSite has an invalid MethodMap index.");
            }

            var successMethodToken = BinaryPrimitives
                .ReadUInt32LittleEndian(body.MethodMap[
                    (successMethodMapIndex * sizeof(uint))..]);
            var successTokenKind
                = checked((byte)(successMethodToken >> 24));
            var successTokenRow = checked((int)(
                successMethodToken & 0x00FF_FFFF));
            var successMethodRow = successTokenKind switch {
                0x16 => successTokenRow,
                0x1C when successTokenRow > 0
                    && successTokenRow <= imports.Count
                    && (imports.GetExpectedDefinitionForImportRow(
                        successTokenRow) >> 24) == 0x16
                    => checked((int)(imports
                        .GetExpectedDefinitionForImportRow(
                            successTokenRow) & 0x00FF_FFFF)),
                _ => 0,
            };
            if (successMethodRow == 0
                || foreignInterfaceBodyTargets is null
                || !foreignInterfaceBodyTargets.TryGetValue(
                    successMethodRow, out var successForeignTarget))
            {
                throw Invalid(
                    "The Standard F+I conditional-output success CallSite has no selected InterfaceMethod target.");
            }

            var successMember = successForeignTarget.Member;
            var successResultType = successMember.ResultType.PrimitiveType;
            if (successMember.ResultType.Kind
                    != BoundInterfaceTypeKind.Primitive
                || successResultType != function.ReturnType
                || successMember.DoesNotReturn
                || !successMember.NativeOutputContracts.IsEmpty
                || successMember.NativeCallableConvention is not
                    NativeCallingConvention successConvention
                || successForeignTarget.Definition.ForeignNativeInterface
                    is not BoundForeignNativeInterfaceLayout successLayout
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    successPayload[8..])
                    != (uint)(successMember.Parameters.Length + 1)
                || successPayload.Length
                    != 20 + (successMember.Parameters.Length * 4))
            {
                throw Invalid(
                    "The Standard F+I conditional-output success CallSite is outside the direct no-output method subset.");
            }

            var successReceiverInput = successPayload[16..];
            var successReceiverHome = BinaryPrimitives
                .ReadUInt16LittleEndian(successReceiverInput[2..]);
            if (successReceiverInput[0] != 2
                || successReceiverInput[1] != 1
                || successReceiverHome >= body.HomeCount
                || (successReceiverHome != 0
                    && !reservedOutputHomes.Contains(
                        successReceiverHome))
                || homeRoles[successReceiverHome] is not (2 or 3)
                || homeVariables[successReceiverHome]
                    != successReceiverHome
                || homeTypes[successReceiverHome]
                    != PrimitiveType.NativeRawPointer)
            {
                throw Invalid(
                    "The Standard F+I conditional-output success CallSite has no direct raw-pointer receiver Home.");
            }

            var successArguments
                = ImmutableArray.CreateBuilder<BoundExpression>(
                    successMember.Parameters.Length);
            var successParameterIndices
                = ImmutableArray.CreateBuilder<int>(
                    successMember.Parameters.Length);
            var successInputOffset = 20;
            var consumedOutputHomes = new HashSet<int>();
            var hasPointerOutputUse = false;
            var successNestedUseCount = 0;
            if (reservedOutputHomes.Contains(successReceiverHome))
            {
                _ = consumedOutputHomes.Add(successReceiverHome);
                hasPointerOutputUse = true;
            }
            for (var formal = 0;
                formal < successMember.Parameters.Length;
                formal++)
            {
                var parameter = successMember.Parameters[formal];
                var formalType = parameter.Type.PrimitiveType;
                var input = successPayload.Slice(successInputOffset, 4);
                var selectorKind = input[0];
                var sourceUse = input[1];
                var homeIndex = BinaryPrimitives
                    .ReadUInt16LittleEndian(input[2..]);
                if (parameter.Type.Kind
                        != BoundInterfaceTypeKind.Primitive
                    || !(DirectNativeImportContract.IsScalar(formalType)
                        || formalType
                            == PrimitiveType.NativeRawPointer
                        || NativePointerTypes.IsType(formalType)
                        || NativeFunctionPointerTypes.IsType(formalType)))
                {
                    throw Invalid(
                        "A Standard F+I conditional-output success input does not match a direct pointer or scalar Home.");
                }

                if (selectorKind == 1 && sourceUse == 1
                    && homeIndex == 1 && successNestedCall is not null
                    && successNestedCall.Type == formalType
                    && successNestedUseCount == 0)
                {
                    successArguments.Add(new BoundUnsafeExpression(
                        successNestedCall, default));
                    successNestedUseCount++;
                }
                else if (selectorKind == 2 && sourceUse == 1
                    && homeIndex < body.HomeCount
                    && homeRoles[homeIndex] is 2 or 3
                    && homeVariables[homeIndex] == homeIndex
                    && homeTypes[homeIndex] == formalType
                    && !variables[homeIndex].IsOut
                    && !variables[homeIndex].IsInit)
                {
                    if (reservedOutputHomes.Contains(homeIndex))
                    {
                        if (!consumedOutputHomes.Add(homeIndex))
                        {
                            throw Invalid(
                                "A Standard F+I conditional-output success call consumes an output Home more than once.");
                        }

                        hasPointerOutputUse |=
                            IsForeignNativeInterfacePointerValueType(
                                formalType);
                    }
                    else if (homeIndex != 0
                        && homeIndex < inputVariableLimit
                        && homeRoles[homeIndex] == 2)
                    {
                        _ = inputHomeVariables.Add(homeIndex);
                    }
                    else
                    {
                        throw Invalid(
                            "A Standard F+I conditional-output success input reads a non-parameter Home.");
                    }

                    successArguments.Add(new BoundRead(
                        homeVariables[homeIndex], formalType, default));
                }
                else
                {
                    throw Invalid(
                        "A Standard F+I conditional-output success input does not match its Home or nested-call register.");
                }

                successParameterIndices.Add(formal);
                successInputOffset += 4;
            }

            if (consumedOutputHomes.Count == 0 || !hasPointerOutputUse)
            {
                throw Invalid(
                    "A Standard F+I conditional-output success call does not consume a pointer-valued output Home.");
            }

            if (successNestedUseCount
                != (successNestedCall is null ? 0 : 1))
            {
                throw Invalid(
                    "The Standard F+I conditional-output success call does not consume its ordered nested result.");
            }

            var successDeclarationKey = successMember.DeclarationKey;
            if (successTokenKind == 0x1C)
            {
                successDeclarationKey = imports
                    .GetBindingForImportRow(successTokenRow).ExportKey;
                if (!StringComparer.Ordinal.Equals(successDeclarationKey,
                        successMember.DeclarationKey)
                    && !successMember.AdditionalDeclarationKeys.Contains(
                        successDeclarationKey, StringComparer.Ordinal))
                {
                    throw Invalid(
                        "A Standard F+I conditional-output success Import does not select one of the exact member ExportKeys.");
                }
            }

            var successDeclaringInterface = new BoundInterfaceType(
                BoundInterfaceTypeKind.Nominal, PrimitiveType.Void,
                successForeignTarget.Definition.QualifiedName, -1, [],
                false, false, false, false) {
                ModuleImport
                    = successForeignTarget.Definition.ModuleImport,
            };
            var successIdentity = new BoundInterfaceMemberIdentity(
                successDeclaringInterface,
                successForeignTarget.MemberOrdinal,
                successDeclarationKey);
            successCall = new BoundCall(
                BoundCall.ForeignInterfaceFunction,
                successArguments.MoveToImmutable(), successResultType,
                default) {
                IsUnsafeAuthorized = true,
                ForeignInterfaceCall = new BoundForeignNativeInterfaceCall(
                    new BoundRead(homeVariables[successReceiverHome],
                        PrimitiveType.NativeRawPointer, default),
                    successIdentity, successLayout.VTableByteOffset,
                    successConvention,
                    successMember.RequiresMutableReceiver,
                    successMember.DoesNotReturn,
                    successMember.NativeOutputContracts),
                ArgumentParameterIndices
                    = successParameterIndices.MoveToImmutable(),
            };
        }

        foreach (var outputVariable in outputVariables)
        {
            variables[outputVariable] = variables[outputVariable] with {
                IsInitializationLocal = true,
            };
        }
        var statusRead = new BoundRead(statusHomeVariable, statusType,
            default);
        var conditionOperator = successOnTrue
            ? successOperator
            : complementOperator;
        if (conditionOperator is not TokenKind selectedOperator)
        {
            throw Invalid(
                "The Standard F+I output comparison has no selected result predicate.");
        }

        var condition = new BoundBinary(selectedOperator,
            statusRead, new BoundConstant(0, statusType, default),
            PrimitiveType.Bool, default);
        BoundExpression successfulValue = successCall is null
            ? new BoundRead(returnOutputVariable, function.ReturnType,
                default)
            : new BoundUnsafeExpression(successCall, default);
        var complementValue = fallbackVariable >= 0
            ? new BoundRead(fallbackVariable, function.ReturnType, default)
            : statusRead;
        var thenValue = successOnTrue ? successfulValue : complementValue;
        var finalValue = successOnTrue ? complementValue : successfulValue;
        var decodedStatements
            = ImmutableArray.CreateBuilder<BoundStatement>(
                preludeCallsBuilder.Count + 4);
        foreach (var preludeCall in preludeCallsBuilder)
        {
            decodedStatements.Add(new BoundExpressionStatement(
                new BoundUnsafeExpression(preludeCall, default), default));
        }
        foreach (var outputVariable in outputVariables)
        {
            decodedStatements.Add(new BoundVacantLocal(
                outputVariable, default));
        }
        decodedStatements.Add(new BoundLocal(statusHomeVariable,
            new BoundUnsafeExpression(call, default), default));
        decodedStatements.Add(new BoundIf(condition, new BoundBlock([
            new BoundReturn(thenValue, default),
        ], default), null, default));
        decodedStatements.Add(new BoundReturn(finalValue, default));
        return decodedStatements.MoveToImmutable();

        static bool HasEmptyEntryState(TrRmRegisterBodyReader body,
            int blockIndex)
        {
            var stateIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlock(blockIndex)[8..]);
            var state = body.GetRecord(stateIndex);
            return state.Length == 8 && state[0] == 0x01
                && state[1] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(state[2..]) == 0
                && BinaryPrimitives.ReadUInt32LittleEndian(state[4..]) == 0;
        }
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardInitializationReferenceConditionalBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            IReadOnlyList<CheckedReferenceTypeDefinition>
                checkedReferenceTypes,
            int[] homeVariables, byte[] homeRoles,
            PrimitiveType[] homeTypes, ushort[] homeTypeMapIndexes)
    {
        if (function.IsExternal || function.ReturnType != PrimitiveType.Bool
            || function.ParameterCount < 2
            || function.Variables.Length != function.ParameterCount
            || !function.DeclarationContractClauses.IsEmpty
            || body.HomeCount != function.ParameterCount
            || body.MethodCount != 0
            || homeTypes[0] != PrimitiveType.Bool
            || variables[0].Type != PrimitiveType.Bool
            || !HasEntryState(body, 0, 0)
            || !HasEntryState(body, 1, 1)
            || !HasEntryState(body, 2, 1))
        {
            throw Unsupported(
                "The Standard init conditional Body requires one Boolean, one scalar value and one init-reference formal.");
        }

        var outputReferents = new Dictionary<int, PrimitiveType>();
        for (var ordinal = 0; ordinal < function.ParameterCount; ordinal++)
        {
            if (homeRoles[ordinal] != 2 || homeVariables[ordinal] != ordinal)
            {
                throw Unsupported(
                    "The Standard conditional init Body requires direct parameter Homes.");
            }

            var variable = variables[ordinal];
            if (ordinal == 0)
            {
                if (variable.Type != PrimitiveType.Bool
                    || variable.IsOut || variable.IsInit)
                {
                    throw Invalid(
                        "The Standard conditional init Body has an invalid Boolean condition formal.");
                }
                continue;
            }

            if (variable.IsInit)
            {
                if (!IsStandardInitializationReferenceParameter(
                        variable, checkedReferenceTypes)
                    || !TryGetStandardInitializationReferenceReferentType(
                        variable.Type, checkedReferenceTypes,
                        out var referentType))
                {
                    throw Invalid(
                        "The Standard conditional init Body has an unsupported output formal.");
                }

                outputReferents.Add(ordinal, referentType);
            }
            else if (variable.IsOut || !IsOutputBodyValueType(variable.Type))
            {
                throw Invalid(
                    "The Standard conditional init Body has an unsupported input formal.");
            }
        }

        if (outputReferents.Count == 0
            || body.RecordCount != outputReferents.Count + 3)
        {
            throw Invalid(
                "The Standard conditional init Body has an invalid output-record set.");
        }

        var trueStateIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetBlock(1)[8..]);
        var trueState = body.GetRecord(trueStateIndex);
        if (trueState[8] != 0 || trueState[9] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(trueState[10..])
                != homeTypeMapIndexes[0]
            || trueState[12] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlock(2)[8..]) != trueStateIndex)
        {
            throw Invalid(
                "Standard conditional init Body edges do not carry the same Boolean value.");
        }

        var entryCode = body.GetBlockCode(0);
        var trueCode = body.GetBlockCode(1);
        var falseCode = body.GetBlockCode(2);
        if (entryCode.Length != 6 || entryCode[0] != 0xD5
            || entryCode[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(entryCode[2..]) != 0
            || entryCode[4] != 0xB1 || (entryCode[5] & 0x0F) != 0
            || trueCode.Length < 4
            || trueCode[^2] != 0x17 || trueCode[^1] != 0
            || falseCode.Length != 2 || falseCode[0] != 0x17
            || falseCode[1] != 0)
        {
            throw Unsupported(
                "The Standard init conditional Body has unsupported branch instructions.");
        }

        var edgeRecordIndex = entryCode[5] >> 4;
        var edge = body.GetRecord(edgeRecordIndex);
        if (edge[0] != 0x06 || edge[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]) != 2)
        {
            throw Invalid(
                "The Standard init conditional Body branch targets are invalid.");
        }

        var outputWrites
            = ImmutableArray.CreateBuilder<BoundStatement>(
                outputReferents.Count + 1);
        var initializedOutputs = new HashSet<int>();
        var trueCodePosition = 0;
        for (var index = 0; index < outputReferents.Count; index++)
        {
            var storeRecordIndex = trueCode[trueCodePosition] switch {
                0x92 when trueCodePosition + 2 <= trueCode.Length - 2
                    => ReadStoreRecordReference(trueCode,
                        ref trueCodePosition),
                0xD9 when trueCodePosition + 4 <= trueCode.Length - 2
                    => ReadStoreRecordReference(trueCode,
                        ref trueCodePosition),
                _ => -1,
            };
            if (storeRecordIndex < 0)
            {
                throw Unsupported(
                    "The Standard conditional init Body has unsupported true-branch operations.");
            }

            var store = body.GetRecord(storeRecordIndex);
            if (store.Length != 20 || store[0] != 0x05 || store[1] != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(store[2..]) != 0
                || store[4] != 2 || store[5] != 2 || store[6] != 1
                || store[7] != 1)
            {
                throw Invalid(
                    "The Standard init conditional Body store does not match its formals.");
            }

            var descriptorVariable
                = BinaryPrimitives.ReadUInt16LittleEndian(store[8..]);
            var sourceVariable
                = BinaryPrimitives.ReadUInt16LittleEndian(store[10..]);
            if (!outputReferents.TryGetValue(descriptorVariable,
                    out var referentType)
                || sourceVariable >= function.ParameterCount
                || outputReferents.ContainsKey(sourceVariable)
                || variables[sourceVariable].IsOut
                || variables[sourceVariable].IsInit
                || variables[sourceVariable].Type != referentType
                || !initializedOutputs.Add(descriptorVariable))
            {
                throw Invalid(
                    "The Standard init conditional Body store has an invalid output or source.");
            }

            outputWrites.Add(new BoundExpressionStatement(
                new BoundReferenceWrite(
                    new BoundRead(descriptorVariable,
                        variables[descriptorVariable].Type, default),
                    new BoundRead(sourceVariable,
                        variables[sourceVariable].Type, default), default),
                default));
        }

        if (trueCodePosition != trueCode.Length - 2
            || initializedOutputs.Count != outputReferents.Count)
        {
            throw Invalid(
                "The Standard init conditional Body does not initialize every output exactly once.");
        }

        var condition = new BoundRead(0, PrimitiveType.Bool, default);
        var trueResult = new BoundConstant(1, PrimitiveType.Bool, default);
        var falseResult = new BoundConstant(0, PrimitiveType.Bool, default);
        outputWrites.Add(new BoundReturn(trueResult, default));
        var branch = new BoundIf(condition,
            new BoundBlock(outputWrites.MoveToImmutable(), default),
            null, default);
        return [
            branch,
            new BoundReturn(falseResult, default),
        ];

        static bool HasEntryState(TrRmRegisterBodyReader body, int blockIndex,
            uint registerCount)
        {
            var stateIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlock(blockIndex)[8..]);
            var state = body.GetRecord(stateIndex);
            return state.Length == (registerCount == 0 ? 8 : 13)
                && state[0] == 0x01 && state[1] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(state[2..]) == 0
                && BinaryPrimitives.ReadUInt32LittleEndian(state[4..])
                    == registerCount
                && (registerCount == 0 || state[8] == 0);
        }

        static int ReadStoreRecordReference(ReadOnlySpan<byte> code,
            ref int position)
        {
            if (code[position] == 0x92)
            {
                var record = code[position + 1];
                position += 2;
                return record;
            }

            if (code[position] != 0xD9 || code[position + 1] != 0)
            {
                return -1;
            }

            var wideRecord = BinaryPrimitives.ReadUInt16LittleEndian(
                code[(position + 2)..]);
            position += 4;
            return wideRecord;
        }
    }

    private static ImmutableArray<BoundStatement>
        DecodeStandardInitializationReferenceConditionalCallBody(
            TrRmRegisterBodyReader body, BoundFunction function,
            ImmutableArray<BoundVariable>.Builder variables,
            TrRmTypeSpecTableReader typeSpecs,
            TrRmImportTableReader imports,
            List<CheckedReferenceTypeDefinition> checkedReferenceTypes,
            Dictionary<CheckedReferenceTypeDefinition, int>
                checkedReferenceTypeIndexes,
            List<NativePointerTypeDefinition> nativePointerTypes,
            int[] structureTypeByTypeDef,
            ImmutableArray<BoundFunction>.Builder functions,
            int[] functionIndexByMethodRow, int[] homeVariables,
            byte[] homeRoles, PrimitiveType[] homeTypes,
            ushort[] homeTypeMapIndexes)
    {
        if (function.IsExternal || !IsOutputBodyValueType(function.ReturnType)
            || function.ParameterCount != 3
            || variables.Count != 4 || body.HomeCount != 4
            || body.MethodCount != 1
            || homeRoles[0] != 2 || homeRoles[1] != 2
            || homeRoles[2] != 2 || homeRoles[3] != 3
            || homeVariables[0] != 0 || homeVariables[1] != 1
            || homeVariables[2] != 2 || homeVariables[3] != 3
            || homeTypes[0] != PrimitiveType.Bool
            || homeTypes[1] != function.ReturnType
            || homeTypes[2] != function.ReturnType
            || homeTypes[3] != function.ReturnType
            || !HasEntryState(body, 0)
            || !HasEntryState(body, 1)
            || !HasEntryState(body, 2))
        {
            throw Unsupported(
                "The Standard conditional init-call Body requires three scalar formals and one output Home.");
        }

        var entryCode = body.GetBlockCode(0);
        var trueCode = body.GetBlockCode(1);
        var falseCode = body.GetBlockCode(2);
        if (entryCode.Length != 8 || entryCode[0] != 0xDD
            || entryCode[1] != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(entryCode[2..]) != 1
            || entryCode[4] != 0x90 || entryCode[5] != 2
            || entryCode[6] != 0xB1 || (entryCode[7] & 0x0F) != 0
            || trueCode.Length != 2 || trueCode[0] != 0xB8
            || trueCode[1] != 3
            || falseCode.Length != 2 || falseCode[0] != 0xB8
            || falseCode[1] != 2)
        {
            throw Unsupported(
                "The Standard conditional init-call Body instructions are unsupported.");
        }

        var placeRecord = body.GetRecord(1);
        var outputVariable = homeVariables[3];
        if (placeRecord[0] != 0x04 || placeRecord[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(placeRecord[2..]) != 0
            || placeRecord[4] != 1 || placeRecord[5] != 3
            || placeRecord[6] != 1
            || BinaryPrimitives.ReadUInt32LittleEndian(placeRecord[12..]) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(placeRecord[16..]) != 3
            || BinaryPrimitives.ReadUInt16LittleEndian(placeRecord[20..])
                != homeTypeMapIndexes[3]
            || BinaryPrimitives.ReadUInt16LittleEndian(placeRecord[22..]) != 1
            || !IsOutputBodyValueType(variables[outputVariable].Type))
        {
            throw Invalid(
                "A Standard conditional init-call PlaceSelection does not name its vacant Home.");
        }
        variables[outputVariable] = variables[outputVariable] with {
            IsInitializationLocal = true,
        };

        var call = body.GetRecord(2);
        var targetMethodMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(call[8..]);
        var inputCount = BinaryPrimitives.ReadUInt32LittleEndian(call[12..]);
        if (call[0] != 0x02 || call[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(call[2..]) != 0
            || call[4] != 1 || call[5] != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(call[6..]) != 0
            || inputCount != 3
            || BinaryPrimitives.ReadUInt32LittleEndian(call[16..]) != 0
            || targetMethodMapIndex >= body.MethodCount)
        {
            throw Invalid(
                "A Standard conditional init CallSite has an invalid shape.");
        }

        var methodToken = BinaryPrimitives.ReadUInt32LittleEndian(
            body.MethodMap[(targetMethodMapIndex * sizeof(uint))..]);
        var targetFunction
            = ResolveStandardInitializationBodyMethodMapFunction(
                methodToken, imports, functionIndexByMethodRow, functions);
        var target = functions[targetFunction];
        if (target.RequiresUnsafeCaller
            || target.ReturnType != PrimitiveType.Bool
            || target.ParameterCount != 3
            || target.Variables[0].Type != homeTypes[0]
            || target.Variables[1].Type != homeTypes[1]
            || !IsStandardInitializationReferenceParameter(
                target.Variables[2], checkedReferenceTypes)
            || !TryGetStandardInitializationReferenceReferentType(
                target.Variables[2].Type, checkedReferenceTypes,
                out var referentType)
            || referentType != variables[outputVariable].Type)
        {
            throw Invalid(
                "A Standard conditional init CallSite does not match its provider signature.");
        }

        var arguments = ImmutableArray.CreateBuilder<BoundExpression>(3);
        var inputOffset = 20;
        for (var formal = 0; formal < 3; formal++)
        {
            var selectorKind = call[inputOffset];
            var sourceUse = call[inputOffset + 1];
            var selectorIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                call[(inputOffset + 2)..]);
            if (formal < 2)
            {
                if (selectorKind != 2 || sourceUse != 1
                    || selectorIndex != formal
                    || homeRoles[(int)selectorIndex] != 2
                    || homeTypes[(int)selectorIndex]
                        != target.Variables[formal].Type)
                {
                    throw Invalid(
                        "A Standard conditional init CallSite input does not match its formal Home.");
                }
                arguments.Add(new BoundRead(homeVariables[(int)selectorIndex],
                    target.Variables[formal].Type, default));
            }
            else
            {
                if (selectorKind != 1 || sourceUse != 5
                    || selectorIndex != 1
                    || !IsStandardInitializationReferenceParameter(
                        target.Variables[formal], checkedReferenceTypes))
                {
                    throw Invalid(
                        "A Standard conditional init CallSite has no descriptor input for its output formal.");
                }
                arguments.Add(new BoundReferenceFormation(
                    new BoundRead(outputVariable,
                        variables[outputVariable].Type, default),
                    target.Variables[formal].Type, default));
            }

            inputOffset += 4;
        }

        var edgeRecordIndex = entryCode[7] >> 4;
        var edge = body.GetRecord(edgeRecordIndex);
        if (edge[0] != 0x06 || edge[1] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]) != 2)
        {
            throw Invalid(
                "The Standard conditional init-call branch targets are invalid.");
        }

        var branchCall = new BoundCall(targetFunction,
            arguments.MoveToImmutable(), PrimitiveType.Bool, default);
        var outputRead = new BoundRead(outputVariable,
            variables[outputVariable].Type, default);
        var fallbackRead = new BoundRead(homeVariables[2],
            variables[homeVariables[2]].Type, default);
        return [
            new BoundVacantLocal(outputVariable, default),
            new BoundIf(branchCall,
                new BoundBlock([
                    new BoundReturn(outputRead, default),
                ], default), null, default),
            new BoundReturn(fallbackRead, default),
        ];

        static bool HasEntryState(TrRmRegisterBodyReader body,
            int blockIndex)
        {
            var stateIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                body.GetBlock(blockIndex)[8..]);
            var state = body.GetRecord(stateIndex);
            return state.Length == 8 && state[0] == 0x01
                && state[1] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(state[2..]) == 0
                && BinaryPrimitives.ReadUInt32LittleEndian(state[4..]) == 0;
        }
    }
}
