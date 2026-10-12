# Bounded bootstrap call-argument parentheses

Status: UNAPPROVED recommendation.

The provisional direct-helper-call and forwarding-chain argument parser
supports checked arithmetic chains with grammar precedence and one unary `-`
per literal or recognized `Main` local operand. It now provisionally accepts
parenthesized expressions. This is a separate bounded extension of the still-
UNAPPROVED [call-argument expression](bootstrap-call-argument-expressions.md),
[precedence](bootstrap-call-argument-precedence.md), and
[unary-negation recommendations](bootstrap-call-argument-unary.md).

Recommend allowing balanced, nested parentheses to group expressions made
from the already-supported `int32` literals, recognized `Main` locals, one
optional unary `-` per scalar operand, and `+`, `-`, `*`, `/`, and `%`.
Preserve source-grammar precedence and associativity inside and outside each
group. Keep calls, unary operators on grouped expressions, repeated prefixes,
and other operators or expression forms unsupported. The distinct
[grouped-operand unary-negation recommendation](bootstrap-call-argument-group-unary.md)
proposes one optional unary minus on a complete grouped expression.

Evaluate each unary and binary operation with checked `int32` semantics as it
is reduced. Overflow, division or remainder by zero, invalid grouping, or an
out-of-subset operand rejects before code or image publication. Use explicit
value and operator stacks sized from the bounded source/token resources; do
not recurse on grouping depth or add a separate enumerated nesting limit.

This is compile-time folding only. It does not change canonical grammar,
runtime expression lowering, the call ABI, or any language contract, and
implementation evidence does not accept this or its prerequisite
recommendations.
