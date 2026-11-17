using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;

namespace Terrum.CodeAnalysis.Semantics;

internal readonly record struct BoundGenericUniquePropertyFact(
    int FormalOrdinal, PrimitiveType FormalType, bool IsUnique);

internal readonly record struct BoundGenericUniquePropertyFlow(
    bool CanBeTrue,
    ImmutableArray<BoundGenericUniquePropertyFact> WhenTrue,
    bool CanBeFalse,
    ImmutableArray<BoundGenericUniquePropertyFact> WhenFalse)
{
    internal bool? KnownResult => CanBeTrue == CanBeFalse
        ? null : CanBeTrue;

    internal bool HasFacts => !WhenTrue.IsDefaultOrEmpty
        || !WhenFalse.IsDefaultOrEmpty;
}

internal static class BoundGenericUniquePropertyFlowAnalysis
{
    private readonly record struct Outcome(
        bool IsPossible,
        ImmutableArray<BoundGenericUniquePropertyFact> Facts);

    private readonly record struct Flow(Outcome WhenTrue, Outcome WhenFalse);

    internal static BoundGenericUniquePropertyFlow Analyze(
        BoundExpression expression, IReadOnlyList<BoundVariable> variables)
    {
        var known = AnalyzeCore(expression, variables, respectKnownResults: true);
        var syntactic = known.WhenTrue.IsPossible && known.WhenFalse.IsPossible
            ? known
            : AnalyzeCore(expression, variables, respectKnownResults: false);
        var whenTrue = known.WhenTrue.IsPossible
            ? known.WhenTrue : syntactic.WhenTrue;
        var whenFalse = known.WhenFalse.IsPossible
            ? known.WhenFalse : syntactic.WhenFalse;
        return new BoundGenericUniquePropertyFlow(
            known.WhenTrue.IsPossible, whenTrue.Facts,
            known.WhenFalse.IsPossible, whenFalse.Facts);
    }

    private static Flow AnalyzeCore(BoundExpression expression,
        IReadOnlyList<BoundVariable> variables, bool respectKnownResults)
    {
        if (expression is BoundGenericTypePropertyTest propertyTest)
        {
            var unique = new BoundGenericUniquePropertyFact(
                propertyTest.FormalOrdinal, propertyTest.FormalType, true);
            var copy = unique with { IsUnique = false };
            return new Flow(
                new Outcome(!respectKnownResults
                        || propertyTest.KnownResult != false, [unique]),
                new Outcome(!respectKnownResults
                        || propertyTest.KnownResult != true, [copy]));
        }

        if (expression is BoundConstant {
                Type: PrimitiveType.Bool,
            } propertyConstant
            && (!propertyConstant.GenericUniquePropertyWhenTrue
                    .IsDefaultOrEmpty
                || !propertyConstant.GenericUniquePropertyWhenFalse
                    .IsDefaultOrEmpty))
        {
            var result = propertyConstant.Value != 0;
            return new Flow(
                new Outcome(!respectKnownResults || result,
                    Normalize(propertyConstant
                        .GenericUniquePropertyWhenTrue)),
                new Outcome(!respectKnownResults || !result,
                    Normalize(propertyConstant
                        .GenericUniquePropertyWhenFalse)));
        }

        if (expression is BoundConstant { Type: PrimitiveType.Bool } constant)
        {
            var result = constant.Value != 0;
            return new Flow(
                new Outcome(!respectKnownResults || result, []),
                new Outcome(!respectKnownResults || !result, []));
        }

        if (expression is BoundRead { Type: PrimitiveType.Bool } read
            && (uint)read.Variable < (uint)variables.Count)
        {
            var variable = variables[read.Variable];
            if (variable.Type == PrimitiveType.Bool
                && (!variable.GenericUniquePropertyWhenTrue.IsDefaultOrEmpty
                    || !variable.GenericUniquePropertyWhenFalse.IsDefaultOrEmpty
                    || variable.GenericUniquePropertyKnownResult is not null))
            {
                var knownResult
                    = variable.GenericUniquePropertyKnownResult;
                return new Flow(
                    new Outcome(!respectKnownResults
                            || knownResult != false,
                        Normalize(variable
                            .GenericUniquePropertyWhenTrue)),
                    new Outcome(!respectKnownResults
                            || knownResult != true,
                        Normalize(variable
                            .GenericUniquePropertyWhenFalse)));
            }
        }

        if (expression is BoundUnary { Operator: TokenKind.Bang } unary)
        {
            var operand = AnalyzeCore(unary.Operand, variables,
                respectKnownResults);
            return new Flow(operand.WhenFalse, operand.WhenTrue);
        }

        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            return AnalyzeCore(unsafeExpression.Expression, variables,
                respectKnownResults);
        }

        if (expression is BoundBinary {
            Operator: TokenKind.AmpersandAmpersand or TokenKind.PipePipe,
        } logical)
        {
            var operands = new List<BoundExpression>();
            BoundExpression current = logical;
            while (current is BoundBinary binary
                && binary.Operator == logical.Operator)
            {
                operands.Add(binary.Right);
                current = binary.Left;
            }
            operands.Add(current);
            operands.Reverse();

            var flow = AnalyzeCore(operands[0], variables,
                respectKnownResults);
            for (var index = 1; index < operands.Count; index++)
            {
                var right = AnalyzeCore(operands[index], variables,
                    respectKnownResults);
                flow = logical.Operator == TokenKind.AmpersandAmpersand
                    ? And(flow, right) : Or(flow, right);
            }
            return flow;
        }

        if (expression is BoundConditional { Type: PrimitiveType.Bool } conditional)
        {
            var condition = AnalyzeCore(conditional.Condition, variables,
                respectKnownResults);
            var whenTrue = AnalyzeCore(conditional.WhenTrue, variables,
                respectKnownResults);
            var whenFalse = AnalyzeCore(conditional.WhenFalse, variables,
                respectKnownResults);
            return Conditional(condition, whenTrue, whenFalse);
        }

        return new Flow(new Outcome(true, []), new Outcome(true, []));
    }

    private static Flow And(Flow left, Flow right) =>
        new(
            Sequence(left.WhenTrue, right.WhenTrue),
            Alternative(left.WhenFalse,
                Sequence(left.WhenTrue, right.WhenFalse)));

    private static Flow Or(Flow left, Flow right) =>
        new(
            Alternative(left.WhenTrue,
                Sequence(left.WhenFalse, right.WhenTrue)),
            Sequence(left.WhenFalse, right.WhenFalse));

    private static Flow Conditional(Flow condition, Flow whenTrue,
        Flow whenFalse) =>
        new(
            Alternative(
                Sequence(condition.WhenTrue, whenTrue.WhenTrue),
                Sequence(condition.WhenFalse, whenFalse.WhenTrue)),
            Alternative(
                Sequence(condition.WhenTrue, whenTrue.WhenFalse),
                Sequence(condition.WhenFalse, whenFalse.WhenFalse)));

    private static Outcome Sequence(Outcome first, Outcome second)
    {
        if (!first.IsPossible || !second.IsPossible)
        {
            return new Outcome(false, []);
        }

        var facts = new Dictionary<int, BoundGenericUniquePropertyFact>();
        foreach (var fact in first.Facts)
        {
            if (facts.TryGetValue(fact.FormalOrdinal, out var existing))
            {
                if (existing != fact)
                {
                    return new Outcome(false, []);
                }
            }
            else
            {
                facts.Add(fact.FormalOrdinal, fact);
            }
        }
        foreach (var fact in second.Facts)
        {
            if (facts.TryGetValue(fact.FormalOrdinal, out var existing))
            {
                if (existing != fact)
                {
                    return new Outcome(false, []);
                }
            }
            else
            {
                facts.Add(fact.FormalOrdinal, fact);
            }
        }

        return new Outcome(true,
            [.. facts.Values.OrderBy(static fact => fact.FormalOrdinal)]);
    }

    private static ImmutableArray<BoundGenericUniquePropertyFact> Normalize(
        ImmutableArray<BoundGenericUniquePropertyFact> facts) =>
        facts.IsDefault ? [] : facts;

    private static Outcome Alternative(Outcome first, Outcome second)
    {
        if (!first.IsPossible)
        {
            return second;
        }
        if (!second.IsPossible)
        {
            return first;
        }

        return new Outcome(true,
            [.. first.Facts.Where(second.Facts.Contains)
                .OrderBy(static fact => fact.FormalOrdinal)]);
    }
}
