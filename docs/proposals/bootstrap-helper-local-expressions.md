# Bounded bootstrap helper-local expressions

Status: UNAPPROVED recommendation.

This recommendation applies only to local initializers in the source-bounded
direct helper-call/forwarding-chain and parameterless-helper routes. Standalone
direct-leaf local expressions are tracked separately by the
[leaf local expression recommendation](bootstrap-leaf-local-expressions.md).

Recommend folding each immutable `int32` local initializer with the same
bounded expression parser used for helper-call arguments. The supported
grammar is checked, left-associative `+`, `-`, `*`, `/`, and `%` with ordinary
multiplicative-before-additive precedence; one unary minus on a literal, a
preceding local, or a complete parenthesized expression; and nested grouping
evaluated with the existing iterative source-sized stacks. Only preceding
locals are visible. Overflow and division or remainder by zero reject before
code or image publication.

Reusing the parser avoids a second expression evaluator and adds no runtime
local storage, general expression lowering, ABI change, or canonical grammar
change. It does not extend the grammar of other bootstrap routes or accept the
underlying call-argument expression recommendations.

## Provisional implementation evidence

The parameterless-helper, forwarding-chain, and direct parameterized-helper
routes reuse the bounded checked call-argument parser for source-local
initializers. A direct parameterized-helper regression covers grouping,
precedence, unary negation and remainder across three sequential locals;
overflow and division by zero reject without publishing output. The related
source-bound and table-capacity cases also pass in Release and Debug. This
evidence does not approve the recommendation, its expression grammar, or the
underlying call-argument proposals.
