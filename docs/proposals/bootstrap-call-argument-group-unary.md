# Bounded bootstrap call-argument unary negation on groups

Status: UNAPPROVED recommendation.

The provisional direct-helper-call and forwarding-chain argument parser
supports checked arithmetic, one unary `-` per literal or recognized `Main`
local, and balanced nested grouping under separate still-UNAPPROVED
[call-argument recommendations](bootstrap-call-argument-expressions.md),
[precedence](bootstrap-call-argument-precedence.md),
[scalar unary negation](bootstrap-call-argument-unary.md), and
[parentheses](bootstrap-call-argument-parentheses.md) recommendations.
Implementation evidence does not accept them.

Recommend allowing one unary `-` immediately before a parenthesized expression
in a helper-call argument. Reduce the complete group first, then apply checked
`int32` negation before any surrounding binary operation. Preserve grammar
precedence and left associativity within the group and its containing
expression. Nested groups may each have one unary `-`; repeated prefixes on a
single operand or group remain unsupported. A negation that overflows,
including negating `int32.MinValue`, rejects before code or image publication.

Keep unary `+`, calls, and other operators or expression forms unsupported.
Use the existing source-sized iterative stacks; do not recurse or add a
separate nesting limit. This is compile-time folding only and does not change
canonical grammar, runtime expression lowering, the call ABI, or any language
contract. Implementation evidence does not accept this or its prerequisite
recommendations.
