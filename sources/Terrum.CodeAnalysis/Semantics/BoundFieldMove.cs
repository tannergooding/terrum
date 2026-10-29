using System;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Transfers a live unique field from a structurally tracked owner.</summary>
public sealed record BoundFieldMove(BoundFieldRead Place, PrimitiveType Type, TextSpan Span)
    : BoundExpression(Type, Span)
{
    internal override ImmutableArray<BoundExpression> NestedExpressions => [Place];

    internal override BoundExpression RewriteNestedExpressions(
        Func<BoundExpression, BoundExpression> rewrite) =>
        this with { Place = (BoundFieldRead)rewrite(Place) };
}
