# Bounded bootstrap call-argument unary negation

Status: UNAPPROVED recommendation.

The provisional direct-helper-call and forwarding-chain argument parser
supports grammar-precedence arithmetic chains and one optional unary `-`
before each literal or recognized `Main` local operand, but currently rejects
parenthesized groups.
This recommendation is a separate bounded extension of the still-UNAPPROVED
[call-argument expression](bootstrap-call-argument-expressions.md) and
[precedence recommendations](bootstrap-call-argument-precedence.md).

Recommend allowing one optional unary `-` before each literal or recognized
`Main` local in a call-argument chain. The unary operation binds before the
existing binary operators. Repeated prefix operators, unary `+`, other prefix
operators, and all other currently excluded syntax remain outside this
recommendation. Parenthesized groups are provisionally implemented under the
separate still-UNAPPROVED
[call-argument parentheses recommendation](bootstrap-call-argument-parentheses.md).
Preserve existing standalone unary-negation support.

Check negation and every binary operation as `int32` before folding it.
`-2147483648` remains representable as the minimum literal; negating an
existing `int32.MinValue` local or overflowing any intermediate operation
rejects before code or image publication. Division or remainder by zero and
the signed `int32.MinValue / -1` and `int32.MinValue % -1` cases also reject.

This is compile-time folding only. It does not change canonical grammar,
runtime expression lowering, the call ABI, or any language contract, and
implementation evidence does not accept this or either prerequisite
recommendation.
