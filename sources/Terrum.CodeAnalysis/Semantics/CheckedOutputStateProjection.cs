using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal static class CheckedOutputStateProjection
{
    private abstract record Formula;

    private sealed record ConstantFormula(bool Value) : Formula;

    private sealed record GuardAtomFormula(int Ordinal) : Formula;

    private sealed record OutputValueAtomFormula(int Ordinal) : Formula;

    private sealed record OutputStateFormula(int OutputIndex) : Formula;

    private sealed record NotFormula(Formula Operand) : Formula;

    private sealed record BinaryFormula(TokenKind Operator, Formula Left,
        Formula Right) : Formula;

    private sealed record ConditionalFormula(Formula Condition,
        Formula WhenTrue, Formula WhenFalse) : Formula;

    private sealed record OutputFormal(int Ordinal, int OutputIndex);

    private readonly record struct PredicateKey(BoundExpression Expression);

    private readonly record struct TruthTableKey(uint ClauseOrdinal,
        string TruthKey);

    private sealed record DerivedRelation(uint ClauseOrdinal,
        ulong FirstTrueAssignment, int FormalOrdinal, byte State,
        PredicateKey PredicateKey, BoundExpression Predicate,
        byte? BoundedSelectorKind);

    private readonly record struct OutputReferences(
        ImmutableArray<int> StateTests,
        ImmutableArray<int> ValueReads,
        ImmutableArray<int> ReferencedFormals,
        bool IsInvalid)
    {
        internal bool HasOutputReferences
            => !ReferencedFormals.IsEmpty;
    }

    private enum EvaluationKind : byte
    {
        Invalid,
        False,
        True,
    }

    private sealed class AnalysisBudget
    {
        private int _remaining = SemanticVerifier.MaximumAnalysisVisits;

        internal void Consume()
        {
            if (--_remaining < 0)
            {
                throw new ProjectionDeclinedException();
            }
        }

        internal int Remaining => _remaining;
    }

    private sealed class ProjectionDeclinedException : Exception
    {
    }

    private sealed class ClauseProjection
    {
        private readonly BoundFunction _function;
        private readonly IReadOnlyDictionary<int, int> _outputIndexes;
        private readonly IReadOnlyList<CheckedReferenceTypeDefinition>
            _checkedReferenceTypes;
        private readonly AnalysisBudget _budget;
        private readonly Func<BoundExpression, PredicateKey> _getPredicateKey;
        private readonly List<BoundExpression> _guardExpressions = [];
        private readonly Dictionary<PredicateKey, int> _guardOrdinals = [];
        private readonly List<ImmutableArray<int>> _valueReadOutputs = [];
        private readonly Dictionary<PredicateKey, int> _valueReadOrdinals = [];

        internal ClauseProjection(BoundFunction function,
            IReadOnlyDictionary<int, int> outputIndexes,
            IReadOnlyList<CheckedReferenceTypeDefinition>
                checkedReferenceTypes,
            AnalysisBudget budget,
            Func<BoundExpression, PredicateKey> getPredicateKey)
        {
            _function = function;
            _outputIndexes = outputIndexes;
            _checkedReferenceTypes = checkedReferenceTypes;
            _budget = budget;
            _getPredicateKey = getPredicateKey;
        }

        internal bool TryBuild(BoundExpression expression,
            out Formula formula)
            => TryBuild(expression, out formula, out _);

        internal bool TryBuild(BoundExpression expression,
            out Formula formula, out ImmutableArray<int> referencedFormals)
        {
            _budget.Consume();
            formula = null!;
            referencedFormals = [];
            if (expression.Type != PrimitiveType.Bool)
            {
                return false;
            }

            var references = InspectOutputReferences(expression);
            referencedFormals = references.ReferencedFormals;
            if (references.IsInvalid)
            {
                return false;
            }

            if (expression is BoundConstant {
                    Type: PrimitiveType.Bool,
                } constant)
            {
                formula = new ConstantFormula(constant.Value != 0);
                return true;
            }

            if (expression is BoundReferenceInitializationTest stateTest)
            {
                if (!TryGetOutputFormal(stateTest.Descriptor,
                        out var formalOrdinal)
                    || !_outputIndexes.TryGetValue(formalOrdinal,
                        out var outputIndex))
                {
                    if (!references.HasOutputReferences)
                    {
                        return TryBuildIndependentPredicate(expression,
                            out formula);
                    }

                    return false;
                }

                formula = new OutputStateFormula(outputIndex);
                return true;
            }

            if (!references.HasOutputReferences)
            {
                return TryBuildIndependentPredicate(expression,
                    out formula);
            }

            if (expression is BoundUnary {
                    Operator: TokenKind.Bang,
                } unary)
            {
                if (!TryBuild(unary.Operand, out var operand))
                {
                    return false;
                }

                formula = new NotFormula(operand);
                return true;
            }

            if (expression is BoundBinary binary
                && IsBooleanConnective(binary))
            {
                if (!TryBuild(binary.Left, out var left)
                    || !TryBuild(binary.Right, out var right))
                {
                    return false;
                }

                formula = new BinaryFormula(binary.Operator, left, right);
                return true;
            }

            if (expression is BoundConditional conditional)
            {
                if (!TryBuild(conditional.Condition, out var condition)
                    || !TryBuild(conditional.WhenTrue, out var whenTrue)
                    || !TryBuild(conditional.WhenFalse, out var whenFalse))
                {
                    return false;
                }

                formula = new ConditionalFormula(condition, whenTrue,
                    whenFalse);
                return true;
            }

            if (!references.StateTests.IsEmpty
                || references.ValueReads.IsEmpty)
            {
                return false;
            }

            var requiredOutputIndexes = ImmutableArray.CreateBuilder<int>(
                references.ValueReads.Length);
            foreach (var formal in references.ValueReads)
            {
                if (!_outputIndexes.TryGetValue(formal,
                        out var outputIndex))
                {
                    return false;
                }

                requiredOutputIndexes.Add(outputIndex);
            }
            requiredOutputIndexes.Sort();
            var requiredOutputs = requiredOutputIndexes
                .Distinct().ToImmutableArray();
            var valueReadKey = _getPredicateKey(expression);
            if (!_valueReadOrdinals.TryGetValue(valueReadKey,
                    out var valueOrdinal))
            {
                valueOrdinal = _valueReadOutputs.Count;
                _valueReadOrdinals.Add(valueReadKey, valueOrdinal);
                _valueReadOutputs.Add(requiredOutputs);
            }

            formula = new OutputValueAtomFormula(valueOrdinal);
            return true;
        }

        internal bool TryDerive(Formula formula, uint clauseOrdinal,
            ImmutableArray<OutputFormal> outputFormals,
            Func<BoundExpression, PredicateKey> getPredicateKey,
            out ImmutableArray<DerivedRelation> relations,
            out ImmutableArray<int> relatedOutputFormals)
        {
            relations = [];
            relatedOutputFormals = [];
            var bitCount = checked(_guardExpressions.Count
                + _valueReadOutputs.Count + outputFormals.Length);
            if (bitCount >= 63)
            {
                return false;
            }

            var combinationCount = 1UL << bitCount;
            if (combinationCount > (ulong)_budget.Remaining)
            {
                return false;
            }

            var guardCount = _guardExpressions.Count;
            var valueCount = _valueReadOutputs.Count;
            var stateCount = outputFormals.Length;
            var guardCases = 1UL << guardCount;
            var valueCases = 1UL << valueCount;
            var stateCases = 1UL << stateCount;
            var guardValues = new bool[guardCount];
            var valueValues = new bool[valueCount];
            var outputStates = new byte[stateCount];
            var hasSuccessfulCase = new bool[stateCount];
            var allComplete = new bool[stateCount];
            var allVacant = new bool[stateCount];
            var completeFunctions = new bool[stateCount][];
            var vacantFunctions = new bool[stateCount][];
            for (var output = 0; output < stateCount; output++)
            {
                completeFunctions[output] = new bool[checked((int)guardCases)];
                vacantFunctions[output] = new bool[checked((int)guardCases)];
            }

            for (var guardMask = guardCases; guardMask > 0;)
            {
                guardMask--;
                _budget.Consume();
                for (var index = 0; index < guardCount; index++)
                {
                    guardValues[index]
                        = (guardMask & (1UL << (guardCount - index - 1)))
                            != 0;
                }

                Array.Clear(hasSuccessfulCase);
                Array.Fill(allComplete, true);
                Array.Fill(allVacant, true);
                for (var stateMask = 0UL; stateMask < stateCases;
                    stateMask++)
                {
                    _budget.Consume();
                    for (var index = 0; index < stateCount; index++)
                    {
                        outputStates[index]
                            = (stateMask & (1UL << index)) == 0 ? (byte)0
                                : (byte)1;
                    }

                    for (var valueMask = 0UL; valueMask < valueCases;
                        valueMask++)
                    {
                        _budget.Consume();
                        for (var index = 0; index < valueCount; index++)
                        {
                            valueValues[index]
                                = (valueMask & (1UL << index)) != 0;
                        }

                        if (Evaluate(formula, guardValues, valueValues,
                                outputStates, _valueReadOutputs, _budget)
                            != EvaluationKind.True)
                        {
                            continue;
                        }

                        for (var index = 0; index < stateCount; index++)
                        {
                            hasSuccessfulCase[index] = true;
                            if (outputStates[index] != 1)
                            {
                                allComplete[index] = false;
                            }
                            if (outputStates[index] != 0)
                            {
                                allVacant[index] = false;
                            }
                        }
                    }
                }

                for (var index = 0; index < stateCount; index++)
                {
                    completeFunctions[index][(int)guardMask]
                        = hasSuccessfulCase[index]
                            && allComplete[index];
                    vacantFunctions[index][(int)guardMask]
                        = hasSuccessfulCase[index] && allVacant[index];
                }
            }

            var predicateCache = new Dictionary<TruthTableKey,
                BoundExpression>();
            var builder = ImmutableArray.CreateBuilder<DerivedRelation>();
            var outputsWithConsequences = new HashSet<int>();
            for (var index = 0; index < stateCount; index++)
            {
                if (!AddRelation(index, state: 1, completeFunctions[index])
                    || !AddRelation(index, state: 0, vacantFunctions[index]))
                {
                    return false;
                }
            }

            relations = builder
                .OrderBy(static relation => relation.FirstTrueAssignment,
                    Comparer<ulong>.Create(static (left, right) =>
                        right.CompareTo(left)))
                .ThenBy(static relation => relation.FormalOrdinal)
                .ThenBy(static relation => relation.State)
                .Select(relation => relation with { ClauseOrdinal = clauseOrdinal })
                .ToImmutableArray();
            relatedOutputFormals = [.. outputsWithConsequences];
            return true;

            bool AddRelation(int outputIndex, byte state, bool[] truthTable)
            {
                _budget.Consume();
                if (!truthTable.Contains(true))
                {
                    return true;
                }

                // Track unconditional completion before omitting its redundant row.
                _ = outputsWithConsequences.Add(
                    outputFormals[outputIndex].Ordinal);
                var allTrue = truthTable.All(static value => value);
                if (allTrue && state == 1)
                {
                    return true;
                }

                var firstTrueAssignment = 0UL;
                for (var index = truthTable.Length - 1; index >= 0; index--)
                {
                    _budget.Consume();
                    if (truthTable[index])
                    {
                        firstTrueAssignment = (ulong)index;
                        break;
                    }
                }

                var truthKey = new string(truthTable.Select(
                    static value => value ? '1' : '0').ToArray());
                var truthTableKey = new TruthTableKey(clauseOrdinal,
                    truthKey);
                if (!predicateCache.TryGetValue(truthTableKey,
                        out var predicate))
                {
                    predicate = allTrue
                        ? new BoundConstant(1, PrimitiveType.Bool,
                            _function.Span)
                        : BuildPredicate(truthTable, _guardExpressions,
                            _function.Span, _budget);
                    predicateCache.Add(truthTableKey, predicate);
                }

                var boundedSelectorKind
                    = TryGetBoundedResultSelectorKind(predicate,
                        _function.ReturnType, out var nodeKind)
                        ? nodeKind : (byte?)null;

                var formalOrdinal
                    = outputFormals[outputIndex].Ordinal;
                builder.Add(new DerivedRelation(clauseOrdinal,
                    firstTrueAssignment, formalOrdinal, state,
                    getPredicateKey(predicate), predicate,
                    boundedSelectorKind));
                return true;
            }
        }

        private bool TryGetGuardAtom(BoundExpression expression,
            out Formula formula)
        {
            if (!IsSafeGuardExpression(expression))
            {
                formula = null!;
                return false;
            }

            var key = _getPredicateKey(expression);
            if (!_guardOrdinals.TryGetValue(key,
                    out var ordinal))
            {
                ordinal = _guardExpressions.Count;
                _guardOrdinals.Add(key, ordinal);
                _guardExpressions.Add(expression);
            }

            formula = new GuardAtomFormula(ordinal);
            return true;
        }

        private bool TryBuildIndependentPredicate(
            BoundExpression expression, out Formula formula)
        {
            _budget.Consume();
            if (expression is BoundConstant {
                    Type: PrimitiveType.Bool,
                } constant)
            {
                formula = new ConstantFormula(constant.Value != 0);
                return true;
            }

            if (expression is BoundUnary {
                    Operator: TokenKind.Bang,
                    Type: PrimitiveType.Bool,
                    Operand.Type: PrimitiveType.Bool,
                } unary)
            {
                if (!TryBuildIndependentPredicate(unary.Operand,
                        out var operand))
                {
                    formula = null!;
                    return false;
                }

                formula = new NotFormula(operand);
                return true;
            }

            if (expression is BoundBinary binary
                && IsBooleanConnective(binary))
            {
                if (!TryBuildIndependentPredicate(binary.Left,
                        out var left)
                    || !TryBuildIndependentPredicate(binary.Right,
                        out var right))
                {
                    formula = null!;
                    return false;
                }

                formula = new BinaryFormula(binary.Operator, left, right);
                return true;
            }

            if (expression is BoundConditional conditional
                && conditional.Type == PrimitiveType.Bool
                && conditional.Condition.Type == PrimitiveType.Bool
                && conditional.WhenTrue.Type == PrimitiveType.Bool
                && conditional.WhenFalse.Type == PrimitiveType.Bool)
            {
                if (!TryBuildIndependentPredicate(conditional.Condition,
                        out var condition)
                    || !TryBuildIndependentPredicate(conditional.WhenTrue,
                        out var whenTrue)
                    || !TryBuildIndependentPredicate(conditional.WhenFalse,
                        out var whenFalse))
                {
                    formula = null!;
                    return false;
                }

                formula = new ConditionalFormula(condition, whenTrue,
                    whenFalse);
                return true;
            }

            return TryGetGuardAtom(expression, out formula);
        }

        private bool IsSafeGuardExpression(BoundExpression expression)
        {
            _budget.Consume();
            return expression switch {
                BoundRead read => (uint)read.Variable
                        < (uint)_function.ParameterCount
                    && !_function.Variables[read.Variable].IsOut
                    && !_function.Variables[read.Variable].IsInit
                    && !_function.Variables[read.Variable].IsMutable
                    && _function.Variables[read.Variable].Type == read.Type
                    && PrimitiveTypeFacts.IsBuiltInScalar(read.Type),
                BoundConstant constant
                    => PrimitiveTypeFacts.IsBuiltInScalar(constant.Type),
                BoundContractReturnValue result
                    => PrimitiveTypeFacts.IsBuiltInScalar(result.Type)
                        && result.Type == _function.ReturnType,
                BoundCast cast
                    => PrimitiveTypeFacts.IsBuiltInScalar(
                            cast.Operand.Type)
                        && PrimitiveTypeFacts.IsBuiltInScalar(cast.Type)
                        && SemanticVerifier.IsTotalConversion(
                            cast.Operand.Type, cast.Type)
                        && IsSafeGuardExpression(cast.Operand),
                BoundUnary unary
                    => unary.Operator == TokenKind.Bang
                        ? unary.Type == PrimitiveType.Bool
                            && unary.Operand.Type == PrimitiveType.Bool
                            && IsSafeGuardExpression(unary.Operand)
                        : IsSafeScalarUnary(unary),
                BoundBinary binary
                    => binary.Type == PrimitiveType.Bool
                        ? IsSafeBooleanBinary(binary)
                        : IsSafeScalarBinary(binary),
                BoundConditional conditional
                    => PrimitiveTypeFacts.IsBuiltInScalar(
                            conditional.Type)
                        && conditional.Condition.Type
                            == PrimitiveType.Bool
                        && conditional.WhenTrue.Type
                            == conditional.Type
                        && conditional.WhenFalse.Type
                            == conditional.Type
                        && IsSafeGuardExpression(conditional.Condition)
                        && IsSafeGuardExpression(conditional.WhenTrue)
                        && IsSafeGuardExpression(conditional.WhenFalse),
                _ => false,
            };
        }

        private OutputReferences InspectOutputReferences(
            BoundExpression expression)
        {
            var stateTests = new HashSet<int>();
            var valueReads = new HashSet<int>();
            var referencedFormals = new HashSet<int>();
            var pending = new Stack<BoundExpression>();
            pending.Push(expression);
            var invalid = false;
            while (pending.TryPop(out var current))
            {
                _budget.Consume();
                if (current is BoundReferenceInitializationTest
                    initializationTest)
                {
                    if (TryGetOutputFormal(initializationTest.Descriptor,
                            out var outputFormal))
                    {
                        _ = referencedFormals.Add(outputFormal);
                        _ = stateTests.Add(outputFormal);
                    }
                    else
                    {
                        pending.Push(initializationTest.Descriptor);
                    }

                    continue;
                }

                if (current is BoundReferenceRead referenceRead)
                {
                    if (TryGetOutputFormal(referenceRead.Descriptor,
                            out var outputFormal))
                    {
                        _ = referencedFormals.Add(outputFormal);
                        if (!TryGetReferentType(outputFormal,
                                out var referentType)
                            || referenceRead.Type != referentType)
                        {
                            invalid = true;
                        }
                        else
                        {
                            _ = valueReads.Add(outputFormal);
                        }
                    }
                    else
                    {
                        pending.Push(referenceRead.Descriptor);
                    }

                    continue;
                }

                if (current is BoundIsInitialized initializedTest
                    && _outputIndexes.ContainsKey(
                        initializedTest.Variable))
                {
                    _ = referencedFormals.Add(initializedTest.Variable);
                    invalid = true;
                    continue;
                }

                if (current is BoundRead read
                    && _outputIndexes.ContainsKey(read.Variable))
                {
                    _ = referencedFormals.Add(read.Variable);
                    invalid = true;
                    continue;
                }

                foreach (var nested in current.NestedExpressions)
                {
                    pending.Push(nested);
                }
            }

            return new([.. stateTests.Order()], [.. valueReads.Order()],
                [.. referencedFormals.Order()], invalid);
        }

        private bool TryGetOutputFormal(BoundExpression descriptor,
            out int formalOrdinal)
        {
            formalOrdinal = -1;
            if (descriptor is not BoundRead read
                || !_outputIndexes.ContainsKey(read.Variable)
                || read.Variable >= _function.ParameterCount
                || read.Type != _function.Variables[read.Variable].Type)
            {
                return false;
            }

            formalOrdinal = read.Variable;
            return true;
        }

        private bool TryGetReferentType(int formalOrdinal,
            out PrimitiveType referentType)
        {
            referentType = PrimitiveType.Void;
            var formalType = _function.Variables[formalOrdinal].Type;
            if (!CheckedReferenceTypeContract.IsValidType(formalType,
                    _checkedReferenceTypes))
            {
                return false;
            }

            var reference = _checkedReferenceTypes[
                CheckedReferenceTypes.Index(formalType)];
            if (!reference.IsInitialization || reference.IsOptional
                || reference.IsGarbageCollected)
            {
                return false;
            }

            referentType = reference.ReferentType;
            return true;
        }

        private static bool IsBooleanConnective(BoundBinary binary)
            => binary.Type == PrimitiveType.Bool
                && binary.Left.Type == PrimitiveType.Bool
                && binary.Right.Type == PrimitiveType.Bool
                && binary.Operator is TokenKind.AmpersandAmpersand
                    or TokenKind.PipePipe
                    or TokenKind.Ampersand
                    or TokenKind.Pipe
                    or TokenKind.Caret
                    or TokenKind.EqualsEquals
                    or TokenKind.BangEquals;

        private bool IsSafeBooleanBinary(BoundBinary binary)
        {
            if (binary.Type != PrimitiveType.Bool)
            {
                return false;
            }

            var bothBoolean = binary.Left.Type == PrimitiveType.Bool
                && binary.Right.Type == PrimitiveType.Bool;
            var sameScalar = binary.Left.Type == binary.Right.Type
                && PrimitiveTypeFacts.IsBuiltInScalar(binary.Left.Type);
            var orderedScalar = sameScalar
                && (PrimitiveTypeFacts.IsIntegerOrWide(binary.Left.Type)
                    || PrimitiveTypeFacts.IsCodeUnitOrRune(binary.Left.Type)
                    || PrimitiveTypeFacts.IsFloating(binary.Left.Type));
            var supported = binary.Operator switch {
                TokenKind.AmpersandAmpersand or TokenKind.PipePipe
                    or TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret
                    => bothBoolean,
                TokenKind.EqualsEquals or TokenKind.BangEquals
                    => sameScalar,
                TokenKind.Less or TokenKind.LessEquals
                    or TokenKind.Greater or TokenKind.GreaterEquals
                    => orderedScalar,
                _ => false,
            };
            return supported
                && IsSafeGuardExpression(binary.Left)
                && IsSafeGuardExpression(binary.Right);
        }

        private bool IsSafeScalarUnary(BoundUnary unary)
        {
            if (unary.Type != unary.Operand.Type
                || !PrimitiveTypeFacts.IsBuiltInScalar(unary.Type)
                || unary.OverflowBehavior
                    is not (BoundIntegerOverflowBehavior.Checked
                        or BoundIntegerOverflowBehavior.Wrapping))
            {
                return false;
            }

            var integer = PrimitiveTypeFacts.IsIntegerOrWide(unary.Type);
            var floating = PrimitiveTypeFacts.IsFloating(unary.Type);
            var supported = unary.Operator switch {
                TokenKind.Plus or TokenKind.Minus => integer || floating,
                TokenKind.Tilde => integer,
                _ => false,
            };
            if (!supported
                || (unary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Wrapping
                    && !(unary.Operator == TokenKind.Minus && integer)))
            {
                return false;
            }

            return IsSafeGuardExpression(unary.Operand);
        }

        private bool IsSafeScalarBinary(BoundBinary binary)
        {
            var leftType = binary.Left.Type;
            var rightType = binary.Right.Type;
            var sameOperands = leftType == rightType;
            var integer = PrimitiveTypeFacts.IsIntegerOrWide(leftType);
            var floating = PrimitiveTypeFacts.IsFloating(leftType);
            if (!PrimitiveTypeFacts.IsBuiltInScalar(leftType)
                || !PrimitiveTypeFacts.IsBuiltInScalar(rightType)
                || binary.OverflowBehavior
                    is not (BoundIntegerOverflowBehavior.Checked
                        or BoundIntegerOverflowBehavior.Wrapping))
            {
                return false;
            }

            var integerOverflowOperation = integer
                && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star);
            var supported = binary.Operator switch {
                TokenKind.Plus or TokenKind.Minus or TokenKind.Star =>
                    sameOperands && binary.Type == leftType
                    && (integer || floating)
                    && (integer || binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked),
                TokenKind.Slash or TokenKind.Percent =>
                    sameOperands && binary.Type == leftType
                    && (integer || floating)
                    && binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked,
                TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret =>
                    sameOperands && binary.Type == leftType && integer
                    && binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked,
                TokenKind.ShiftLeft or TokenKind.ShiftRight
                    or TokenKind.ShiftRightUnsigned =>
                    integer
                    && PrimitiveTypeFacts.IsIntegerOrWide(rightType)
                    && binary.Type == leftType
                    && binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked,
                _ => false,
            };
            return supported
                && (integerOverflowOperation
                    || binary.OverflowBehavior
                        == BoundIntegerOverflowBehavior.Checked)
                && IsSafeGuardExpression(binary.Left)
                && IsSafeGuardExpression(binary.Right);
        }
    }

    internal static BoundFunction Apply(BoundFunction function,
        IReadOnlyList<CheckedReferenceTypeDefinition> checkedReferenceTypes)
    {
        if (function.IsExternal || function.DeclaringStruct is not null
            || function.IsConstructor || function.IsClassConstructor
            || function.CheckedOutputSelectors.IsDefault
            || function.CheckedOutputStates.IsDefault
            || function.CheckedOutputStatePredicates.IsDefault
            || !function.CheckedOutputSelectors.IsEmpty
            || !function.CheckedOutputStates.IsEmpty
            || !function.CheckedOutputStatePredicates.IsEmpty
            || function.HasPrivateCheckedOutputStateMetadata
            || function.HasStandardCheckedOutputStateMetadata
            || function.DeclarationContractClauses.IsDefaultOrEmpty)
        {
            return function;
        }

        var outputs = ImmutableArray.CreateBuilder<OutputFormal>();
        var outputIndexes = new Dictionary<int, int>();
        for (var ordinal = 0;
            ordinal < function.ParameterCount
                && ordinal < function.Variables.Length;
            ordinal++)
        {
            var formal = function.Variables[ordinal];
            if (!formal.IsInit)
            {
                continue;
            }
            if (formal.IsOut
                || !CheckedReferenceTypeContract.IsValidType(formal.Type,
                    checkedReferenceTypes))
            {
                return function;
            }

            var reference = checkedReferenceTypes[
                CheckedReferenceTypes.Index(formal.Type)];
            if (!reference.IsInitialization || reference.IsOptional
                || reference.IsGarbageCollected)
            {
                return function;
            }

            var outputIndex = outputs.Count;
            outputIndexes.Add(ordinal, outputIndex);
            outputs.Add(new OutputFormal(ordinal, outputIndex));
        }

        if (outputs.Count == 0)
        {
            return function;
        }

        var budget = new AnalysisBudget();
        var derivedRelations = new List<DerivedRelation>();
        var explicitlyReplacedOutputFormals = new HashSet<int>();
        var predicateKeys = new Dictionary<BoundExpression, PredicateKey>(
            ReferenceEqualityComparer.Instance);
        var supportedClauses = new List<(
            uint Ordinal, BoundExpression Predicate,
            bool HasOutputReferences)>();
        var hasOutputContract = false;
        var canCombineClauses = true;
        PredicateKey GetPredicateKey(BoundExpression predicate)
        {
            if (predicateKeys.TryGetValue(predicate, out var existingKey))
            {
                return existingKey;
            }

            var normalizedPredicate
                = BoundTreeTraversal.RewriteExpressions(predicate,
                    expression => {
                        budget.Consume();
                        return expression with { Span = default };
                    });
            var key = new PredicateKey(normalizedPredicate);
            predicateKeys.Add(predicate, key);
            return key;
        }

        try
        {
            foreach (var clause in function.DeclarationContractClauses)
            {
                if (clause.Phase
                        != BoundDeclarationContractPhase.NormalPostcondition)
                {
                    continue;
                }

                var projection = new ClauseProjection(function, outputIndexes,
                    checkedReferenceTypes, budget, GetPredicateKey);
                if (!projection.TryBuild(clause.Predicate,
                        out var formula, out var referencedFormals))
                {
                    if (!referencedFormals.IsEmpty)
                    {
                        return function;
                    }

                    canCombineClauses = false;
                    continue;
                }

                var hasOutputReferences = !referencedFormals.IsEmpty;
                supportedClauses.Add((clause.PhaseLocalOrdinal,
                    clause.Predicate, hasOutputReferences));
                if (!hasOutputReferences)
                {
                    continue;
                }

                hasOutputContract = true;
                if (!projection.TryDerive(formula,
                        clause.PhaseLocalOrdinal, outputs.ToImmutable(),
                        GetPredicateKey,
                        out var clauseRelations,
                        out var clauseOutputFormals))
                {
                    return function;
                }

                derivedRelations.AddRange(clauseRelations);
                explicitlyReplacedOutputFormals.UnionWith(
                    clauseOutputFormals);
            }

            if (canCombineClauses && hasOutputContract
                && supportedClauses.Count > 1)
            {
                try
                {
                    var combinedProjection = new ClauseProjection(function,
                        outputIndexes, checkedReferenceTypes, budget,
                        GetPredicateKey);
                    var formulas = new List<Formula>(
                        supportedClauses.Count);
                    uint? combinedClauseOrdinal = null;
                    var canDeriveConjunction = true;
                    foreach (var clause in supportedClauses)
                    {
                        if (!combinedProjection.TryBuild(clause.Predicate,
                                out var formula,
                                out var referencedFormals)
                            || referencedFormals.IsEmpty
                                == clause.HasOutputReferences)
                        {
                            canDeriveConjunction = false;
                            break;
                        }

                        formulas.Add(formula);
                        if (clause.HasOutputReferences
                            && (combinedClauseOrdinal is null
                                || clause.Ordinal
                                    < combinedClauseOrdinal.Value))
                        {
                            combinedClauseOrdinal = clause.Ordinal;
                        }
                    }

                    if (canDeriveConjunction
                        && combinedClauseOrdinal is uint clauseOrdinal)
                    {
                        var combinedFormula
                            = CombineClauseConjunction(formulas, budget);
                        if (combinedProjection.TryDerive(combinedFormula,
                                clauseOrdinal, outputs.ToImmutable(),
                                GetPredicateKey, out var relations,
                                out var outputFormals))
                        {
                            derivedRelations.AddRange(relations);
                            explicitlyReplacedOutputFormals.UnionWith(
                                outputFormals);
                        }
                    }
                }
                catch (ProjectionDeclinedException)
                {
                    // The conjunction supplements valid per-clause relations.
                }
            }
        }
        catch (ProjectionDeclinedException)
        {
            return function;
        }

        if (!hasOutputContract)
        {
            return function;
        }

        var defaultOutputFormals = outputs
            .Where(output => !explicitlyReplacedOutputFormals.Contains(
                output.Ordinal))
            .ToImmutableArray();
        var defaultBooleanOutputFormals
            = function.ReturnType == PrimitiveType.Bool
                ? defaultOutputFormals
                : ImmutableArray<OutputFormal>.Empty;
        if (derivedRelations.Count == 0
            && defaultBooleanOutputFormals.IsEmpty
            && (function.ReturnType != PrimitiveType.Bool
                || explicitlyReplacedOutputFormals.IsEmpty))
        {
            return function;
        }

        var boundedKinds = new HashSet<byte>();
        if (!defaultBooleanOutputFormals.IsEmpty)
        {
            _ = boundedKinds.Add(1);
            _ = boundedKinds.Add(2);
        }
        foreach (var relation in derivedRelations)
        {
            if (relation.BoundedSelectorKind is byte nodeKind)
            {
                _ = boundedKinds.Add(nodeKind);
            }
        }
        var orderedBoundedKinds = boundedKinds.Order().ToArray();
        var selectorOrdinalByKind = new Dictionary<byte, uint>(
            orderedBoundedKinds.Length);
        var boundedSelectors
            = ImmutableArray.CreateBuilder<NativeFunctionPointerSelector>(
                orderedBoundedKinds.Length);
        for (var index = 0; index < orderedBoundedKinds.Length; index++)
        {
            var nodeKind = orderedBoundedKinds[index];
            selectorOrdinalByKind.Add(nodeKind, checked((uint)index));
            boundedSelectors.Add(new NativeFunctionPointerSelector(3,
                checked((uint)index), nodeKind));
        }

        var boundedOutputStates
            = new Dictionary<(int FormalOrdinal, uint SelectorOrdinal), byte>();
        foreach (var output in defaultBooleanOutputFormals)
        {
            if (!TryAddBoundedState(output.Ordinal, 1,
                    selectorOrdinalByKind[1])
                || !TryAddBoundedState(output.Ordinal, 0,
                    selectorOrdinalByKind[2]))
            {
                return function;
            }
        }
        foreach (var relation in derivedRelations)
        {
            if (relation.BoundedSelectorKind is byte nodeKind
                && !TryAddBoundedState(relation.FormalOrdinal,
                    relation.State, selectorOrdinalByKind[nodeKind]))
            {
                return function;
            }
        }

        var phaseLocalOrdinal = checked((uint)boundedSelectors.Count);
        var predicateOrdinals = new Dictionary<PredicateKey, uint>();
        var emittedOutputRelations = new HashSet<(
            int FormalOrdinal, byte State, PredicateKey Predicate)>();
        var predicates = ImmutableArray.CreateBuilder<
            BoundOutputStatePredicate>();
        foreach (var relation in derivedRelations
            .OrderBy(static relation => relation.ClauseOrdinal)
            .ThenBy(static relation =>
                relation.FirstTrueAssignment,
                Comparer<ulong>.Create(static (left, right) =>
                    right.CompareTo(left)))
            .ThenBy(static relation => relation.FormalOrdinal)
            .ThenBy(static relation => relation.State))
        {
            if (relation.BoundedSelectorKind is not null)
            {
                continue;
            }

            if (!predicateOrdinals.TryGetValue(relation.PredicateKey,
                    out var predicateOrdinal))
            {
                predicateOrdinal = phaseLocalOrdinal++;
                predicateOrdinals.Add(relation.PredicateKey,
                    predicateOrdinal);
            }

            if (!emittedOutputRelations.Add((relation.FormalOrdinal,
                    relation.State, relation.PredicateKey)))
            {
                continue;
            }

            predicates.Add(new BoundOutputStatePredicate(
                relation.FormalOrdinal, predicateOrdinal, relation.State,
                relation.Predicate));
        }

        return function with {
            CheckedOutputSelectors = boundedSelectors.ToImmutable(),
            CheckedOutputStates = boundedOutputStates
                .OrderBy(static pair => pair.Key.FormalOrdinal)
                .ThenBy(static pair => pair.Key.SelectorOrdinal)
                .Select(static pair =>
                    new NativeFunctionPointerOutputState(
                        checked((uint)pair.Key.FormalOrdinal),
                        pair.Key.SelectorOrdinal, pair.Value))
                .ToImmutableArray(),
            CheckedOutputStatePredicates = predicates.ToImmutable(),
            HasStandardCheckedOutputStateMetadata = true,
        };

        bool TryAddBoundedState(int formalOrdinal, byte state,
            uint selectorOrdinal)
        {
            var key = (formalOrdinal, selectorOrdinal);
            if (boundedOutputStates.TryGetValue(key, out var previous))
            {
                return previous == state;
            }

            boundedOutputStates.Add(key, state);
            return true;
        }
    }

    private static Formula CombineClauseConjunction(
        IReadOnlyList<Formula> formulas, AnalysisBudget budget)
    {
        if (formulas.Count == 0)
        {
            throw new ArgumentException(
                "A clause conjunction requires at least one formula.",
                nameof(formulas));
        }

        return Combine(0, formulas.Count);

        Formula Combine(int start, int count)
        {
            if (count == 1)
            {
                return formulas[start];
            }

            budget.Consume();
            var leftCount = count / 2;
            return new BinaryFormula(TokenKind.AmpersandAmpersand,
                Combine(start, leftCount),
                Combine(start + leftCount, count - leftCount));
        }
    }

    internal static HashSet<int> GetExplicitOutputFormals(
        BoundFunction function,
        IReadOnlyList<CheckedReferenceTypeDefinition>
            checkedReferenceTypes)
    {
        var explicitOutputFormals = new HashSet<int>();
        var outputs = ImmutableArray.CreateBuilder<OutputFormal>();
        var outputIndexes = new Dictionary<int, int>();
        for (var ordinal = 0;
            ordinal < function.ParameterCount
                && ordinal < function.Variables.Length;
            ordinal++)
        {
            if (function.Variables[ordinal].IsInit
                && !function.Variables[ordinal].IsOut)
            {
                var formal = function.Variables[ordinal];
                if (!CheckedReferenceTypeContract.IsValidType(formal.Type,
                        checkedReferenceTypes))
                {
                    throw new NotSupportedException(
                        "A checked output-state relation lost its initialization-reference type during revalidation.");
                }

                var reference = checkedReferenceTypes[
                    CheckedReferenceTypes.Index(formal.Type)];
                if (!reference.IsInitialization || reference.IsOptional
                    || reference.IsGarbageCollected)
                {
                    throw new NotSupportedException(
                        "A checked output-state relation has an unsupported initialization-reference type during revalidation.");
                }

                outputIndexes.Add(ordinal, outputs.Count);
                outputs.Add(new OutputFormal(ordinal, outputs.Count));
            }
        }

        if (outputs.Count == 0
            || function.DeclarationContractClauses.IsDefaultOrEmpty)
        {
            return explicitOutputFormals;
        }

        var immutableOutputs = outputs.ToImmutable();
        var budget = new AnalysisBudget();
        PredicateKey GetPredicateKey(BoundExpression predicate)
            => new(BoundTreeTraversal.RewriteExpressions(predicate,
                expression => {
                    budget.Consume();
                    return expression with { Span = default };
                }));

        var supportedClauses = new List<(
            uint Ordinal, BoundExpression Predicate,
            bool HasOutputReferences)>();
        var hasOutputContract = false;
        var canCombineClauses = true;
        try
        {
            foreach (var clause in function.DeclarationContractClauses)
            {
                if (clause.Phase
                    != BoundDeclarationContractPhase.NormalPostcondition)
                {
                    continue;
                }

                var projection = new ClauseProjection(function,
                    outputIndexes, checkedReferenceTypes, budget,
                    GetPredicateKey);
                if (!projection.TryBuild(clause.Predicate,
                        out var formula, out var referencedFormals))
                {
                    if (!referencedFormals.IsEmpty)
                    {
                        throw new NotSupportedException(
                            "A checked output-state relation could not be rederived from its retained postcondition.");
                    }

                    canCombineClauses = false;
                    continue;
                }

                var hasOutputReferences = !referencedFormals.IsEmpty;
                supportedClauses.Add((clause.PhaseLocalOrdinal,
                    clause.Predicate, hasOutputReferences));
                if (!hasOutputReferences)
                {
                    continue;
                }

                hasOutputContract = true;
                if (!projection.TryDerive(formula,
                        clause.PhaseLocalOrdinal, immutableOutputs,
                        GetPredicateKey,
                        out _, out var clauseOutputFormals))
                {
                    throw new NotSupportedException(
                        "A checked output-state relation exceeded its revalidation limits.");
                }

                explicitOutputFormals.UnionWith(clauseOutputFormals);
            }

            if (canCombineClauses && hasOutputContract
                && supportedClauses.Count > 1)
            {
                try
                {
                    var combinedProjection = new ClauseProjection(function,
                        outputIndexes, checkedReferenceTypes, budget,
                        GetPredicateKey);
                    var formulas = new List<Formula>(
                        supportedClauses.Count);
                    uint? combinedClauseOrdinal = null;
                    var canDeriveConjunction = true;
                    foreach (var clause in supportedClauses)
                    {
                        if (!combinedProjection.TryBuild(clause.Predicate,
                                out var formula,
                                out var referencedFormals)
                            || referencedFormals.IsEmpty
                                == clause.HasOutputReferences)
                        {
                            canDeriveConjunction = false;
                            break;
                        }

                        formulas.Add(formula);
                        if (clause.HasOutputReferences
                            && (combinedClauseOrdinal is null
                                || clause.Ordinal
                                    < combinedClauseOrdinal.Value))
                        {
                            combinedClauseOrdinal = clause.Ordinal;
                        }
                    }

                    if (canDeriveConjunction
                        && combinedClauseOrdinal is uint clauseOrdinal)
                    {
                        var combinedFormula
                            = CombineClauseConjunction(formulas, budget);
                        if (combinedProjection.TryDerive(combinedFormula,
                                clauseOrdinal, immutableOutputs,
                                GetPredicateKey, out _,
                                out var combinedOutputFormals))
                        {
                            explicitOutputFormals.UnionWith(
                                combinedOutputFormals);
                        }
                    }
                }
                catch (ProjectionDeclinedException)
                {
                    // The conjunction supplements per-clause consequences.
                }
            }
        }
        catch (ProjectionDeclinedException exception)
        {
            throw new NotSupportedException(
                "A checked output-state relation exceeded its revalidation limits.",
                exception);
        }

        return explicitOutputFormals;
    }

    private static bool TryGetBoundedResultSelectorKind(
        BoundExpression predicate, PrimitiveType returnType,
        out byte nodeKind)
    {
        if (predicate is BoundUnary {
                Operator: TokenKind.Bang,
            } unary
            && TryGetBoundedResultSelectorKind(unary.Operand, returnType,
                out var operandKind))
        {
            nodeKind = operandKind switch {
                1 => 2,
                2 => 1,
                3 => 4,
                4 => 3,
                5 => 6,
                6 => 5,
                _ => 0,
            };
            return nodeKind != 0;
        }

        if (returnType == PrimitiveType.Bool
            && predicate is BoundContractReturnValue {
                Type: PrimitiveType.Bool,
            })
        {
            nodeKind = 1;
            return true;
        }

        if (returnType == PrimitiveType.Bool
            && predicate is BoundBinary {
                Type: PrimitiveType.Bool,
                Operator: TokenKind.EqualsEquals or TokenKind.BangEquals,
            } booleanComparison)
        {
            if (booleanComparison.Left is BoundContractReturnValue {
                    Type: PrimitiveType.Bool,
                }
                && booleanComparison.Right is BoundConstant {
                    Type: PrimitiveType.Bool,
                } rightBoolean)
            {
                var constantValue = rightBoolean.Value != 0;
                var matchesTrue
                    = booleanComparison.Operator
                        == TokenKind.EqualsEquals
                        ? constantValue : !constantValue;
                nodeKind = matchesTrue ? (byte)1 : (byte)2;
                return true;
            }
            if (booleanComparison.Right is BoundContractReturnValue {
                    Type: PrimitiveType.Bool,
                }
                && booleanComparison.Left is BoundConstant {
                    Type: PrimitiveType.Bool,
                } leftBoolean)
            {
                var constantValue = leftBoolean.Value != 0;
                var matchesTrue
                    = booleanComparison.Operator
                        == TokenKind.EqualsEquals
                        ? constantValue : !constantValue;
                nodeKind = matchesTrue ? (byte)1 : (byte)2;
                return true;
            }
        }

        if (returnType is not (PrimitiveType.Int32 or PrimitiveType.UInt32))
        {
            nodeKind = 0;
            return false;
        }

        if (predicate is not BoundBinary binary
            || binary.Type != PrimitiveType.Bool)
        {
            nodeKind = 0;
            return false;
        }

        var resultOnLeft = binary.Left is BoundContractReturnValue result
            && result.Type == returnType
            && binary.Right is BoundConstant constant
            && constant.Type == returnType && constant.Value == 0;
        var resultOnRight = binary.Right is BoundContractReturnValue rightResult
            && rightResult.Type == returnType
            && binary.Left is BoundConstant leftConstant
            && leftConstant.Type == returnType
            && leftConstant.Value == 0;
        if (resultOnLeft)
        {
            nodeKind = binary.Operator switch {
                TokenKind.BangEquals => 3,
                TokenKind.EqualsEquals => 4,
                TokenKind.GreaterEquals when returnType
                    == PrimitiveType.Int32 => 5,
                TokenKind.Less when returnType
                    == PrimitiveType.Int32 => 6,
                _ => 0,
            };
            return nodeKind != 0;
        }

        if (resultOnRight)
        {
            nodeKind = binary.Operator switch {
                TokenKind.BangEquals => 3,
                TokenKind.EqualsEquals => 4,
                TokenKind.LessEquals when returnType
                    == PrimitiveType.Int32 => 5,
                TokenKind.Greater when returnType
                    == PrimitiveType.Int32 => 6,
                _ => 0,
            };
            return nodeKind != 0;
        }

        nodeKind = 0;
        return false;
    }

    private static BoundExpression BuildPredicate(bool[] truthTable,
        List<BoundExpression> guardExpressions, TextSpan span,
        AnalysisBudget budget)
    {
        var falseConstant = new BoundConstant(0, PrimitiveType.Bool, span);
        var trueConstant = new BoundConstant(1, PrimitiveType.Bool, span);
        var guardCount = guardExpressions.Count;

        return Build(0, 0);

        BoundExpression Build(int atomIndex, int prefix)
        {
            budget.Consume();
            if (atomIndex == guardCount)
            {
                return truthTable[prefix] ? trueConstant : falseConstant;
            }

            var remainingBits = guardCount - atomIndex;
            var start = prefix << remainingBits;
            var count = 1 << remainingBits;
            var first = truthTable[start];
            var isConstant = true;
            for (var offset = 1; offset < count; offset++)
            {
                if (truthTable[start + offset] != first)
                {
                    isConstant = false;
                    break;
                }
            }
            if (isConstant)
            {
                return first ? trueConstant : falseConstant;
            }

            var lowPrefix = prefix << 1;
            var highPrefix = lowPrefix | 1;
            var suffixCount = 1 << (remainingBits - 1);
            var lowStart = lowPrefix << (remainingBits - 1);
            var highStart = highPrefix << (remainingBits - 1);
            var equivalentBranches = true;
            for (var offset = 0; offset < suffixCount; offset++)
            {
                budget.Consume();
                if (truthTable[lowStart + offset]
                    != truthTable[highStart + offset])
                {
                    equivalentBranches = false;
                    break;
                }
            }
            if (equivalentBranches)
            {
                return Build(atomIndex + 1, lowPrefix);
            }

            var low = Build(atomIndex + 1, lowPrefix);
            var high = Build(atomIndex + 1, highPrefix);
            return Simplify(guardExpressions[atomIndex], high, low);
        }

        static BoundExpression Simplify(BoundExpression condition,
            BoundExpression whenTrue, BoundExpression whenFalse)
        {
            if (ReferenceEquals(whenTrue, whenFalse))
            {
                return whenTrue;
            }

            if (whenTrue is BoundConstant {
                    Type: PrimitiveType.Bool,
                    Value: 1,
                } && whenFalse is BoundConstant {
                    Type: PrimitiveType.Bool,
                    Value: 0,
                })
            {
                return condition;
            }
            if (whenTrue is BoundConstant {
                    Type: PrimitiveType.Bool,
                    Value: 0,
                } && whenFalse is BoundConstant {
                    Type: PrimitiveType.Bool,
                    Value: 1,
                })
            {
                return new BoundUnary(TokenKind.Bang, condition,
                    PrimitiveType.Bool, condition.Span);
            }
            if (whenFalse is BoundConstant {
                    Type: PrimitiveType.Bool,
                    Value: 0,
                })
            {
                return new BoundBinary(TokenKind.AmpersandAmpersand,
                    condition, whenTrue, PrimitiveType.Bool, condition.Span);
            }
            if (whenTrue is BoundConstant {
                    Type: PrimitiveType.Bool,
                    Value: 1,
                })
            {
                return new BoundBinary(TokenKind.PipePipe, condition,
                    whenFalse, PrimitiveType.Bool, condition.Span);
            }
            if (whenTrue is BoundConstant {
                    Type: PrimitiveType.Bool,
                    Value: 0,
                })
            {
                var notCondition = new BoundUnary(TokenKind.Bang, condition,
                    PrimitiveType.Bool, condition.Span);
                return new BoundBinary(TokenKind.AmpersandAmpersand,
                    notCondition, whenFalse, PrimitiveType.Bool,
                    condition.Span);
            }
            if (whenFalse is BoundConstant {
                    Type: PrimitiveType.Bool,
                    Value: 1,
                })
            {
                var notCondition = new BoundUnary(TokenKind.Bang, condition,
                    PrimitiveType.Bool, condition.Span);
                return new BoundBinary(TokenKind.PipePipe, notCondition,
                    whenTrue, PrimitiveType.Bool, condition.Span);
            }

            return new BoundConditional(condition, whenTrue, whenFalse,
                PrimitiveType.Bool, condition.Span);
        }
    }

    private static EvaluationKind Evaluate(Formula formula,
        bool[] guardValues, bool[] valueValues, byte[] outputStates,
        IReadOnlyList<ImmutableArray<int>> valueReadOutputs,
        AnalysisBudget budget)
    {
        budget.Consume();
        return formula switch {
            ConstantFormula constant => constant.Value
                ? EvaluationKind.True : EvaluationKind.False,
            GuardAtomFormula guard => guardValues[guard.Ordinal]
                ? EvaluationKind.True : EvaluationKind.False,
            OutputValueAtomFormula value
                => EvaluateOutputValue(value, valueValues, outputStates,
                    valueReadOutputs),
            OutputStateFormula state => outputStates[state.OutputIndex] == 1
                ? EvaluationKind.True : EvaluationKind.False,
            NotFormula not => EvaluateNot(not, guardValues, valueValues,
                outputStates, valueReadOutputs, budget),
            BinaryFormula binary => EvaluateBinary(binary, guardValues,
                valueValues, outputStates, valueReadOutputs, budget),
            ConditionalFormula conditional => EvaluateConditional(conditional,
                guardValues, valueValues, outputStates, valueReadOutputs,
                budget),
            _ => EvaluationKind.Invalid,
        };
    }

    private static EvaluationKind EvaluateOutputValue(
        OutputValueAtomFormula value, bool[] valueValues,
        byte[] outputStates,
        IReadOnlyList<ImmutableArray<int>> valueReadOutputs)
    {
        foreach (var outputIndex in valueReadOutputs[value.Ordinal])
        {
            if (outputStates[outputIndex] != 1)
            {
                return EvaluationKind.Invalid;
            }
        }

        return valueValues[value.Ordinal]
            ? EvaluationKind.True : EvaluationKind.False;
    }

    private static EvaluationKind EvaluateNot(NotFormula formula,
        bool[] guardValues, bool[] valueValues, byte[] outputStates,
        IReadOnlyList<ImmutableArray<int>> valueReadOutputs,
        AnalysisBudget budget)
    {
        var value = Evaluate(formula.Operand, guardValues, valueValues,
            outputStates, valueReadOutputs, budget);
        return value switch {
            EvaluationKind.True => EvaluationKind.False,
            EvaluationKind.False => EvaluationKind.True,
            _ => EvaluationKind.Invalid,
        };
    }

    private static EvaluationKind EvaluateBinary(BinaryFormula formula,
        bool[] guardValues, bool[] valueValues, byte[] outputStates,
        IReadOnlyList<ImmutableArray<int>> valueReadOutputs,
        AnalysisBudget budget)
    {
        var left = Evaluate(formula.Left, guardValues, valueValues,
            outputStates, valueReadOutputs, budget);
        if (left == EvaluationKind.Invalid)
        {
            return EvaluationKind.Invalid;
        }

        if (formula.Operator == TokenKind.AmpersandAmpersand
            && left == EvaluationKind.False)
        {
            return EvaluationKind.False;
        }
        if (formula.Operator == TokenKind.PipePipe
            && left == EvaluationKind.True)
        {
            return EvaluationKind.True;
        }

        var right = Evaluate(formula.Right, guardValues, valueValues,
            outputStates, valueReadOutputs, budget);
        if (right == EvaluationKind.Invalid)
        {
            return EvaluationKind.Invalid;
        }

        var leftValue = left == EvaluationKind.True;
        var rightValue = right == EvaluationKind.True;
        var result = formula.Operator switch {
            TokenKind.AmpersandAmpersand or TokenKind.Ampersand
                => leftValue && rightValue,
            TokenKind.PipePipe or TokenKind.Pipe
                => leftValue || rightValue,
            TokenKind.Caret => leftValue ^ rightValue,
            TokenKind.EqualsEquals => leftValue == rightValue,
            TokenKind.BangEquals => leftValue != rightValue,
            _ => false,
        };
        return result ? EvaluationKind.True : EvaluationKind.False;
    }

    private static EvaluationKind EvaluateConditional(
        ConditionalFormula formula, bool[] guardValues,
        bool[] valueValues, byte[] outputStates,
        IReadOnlyList<ImmutableArray<int>> valueReadOutputs,
        AnalysisBudget budget)
    {
        var condition = Evaluate(formula.Condition, guardValues, valueValues,
            outputStates, valueReadOutputs, budget);
        if (condition == EvaluationKind.Invalid)
        {
            return EvaluationKind.Invalid;
        }

        return Evaluate(condition == EvaluationKind.True
                ? formula.WhenTrue : formula.WhenFalse,
            guardValues, valueValues, outputStates, valueReadOutputs,
            budget);
    }
}
