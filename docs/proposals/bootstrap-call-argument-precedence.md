# Bounded bootstrap call-argument precedence

Status: UNAPPROVED recommendation.

The direct-source bootstrap route initially folded at most one checked
arithmetic operation per helper-call argument under the separate still-
UNAPPROVED [call-argument expression recommendation]
(bootstrap-call-argument-expressions.md). The provisional implementation now
also supports the multi-operation expression subset recommended here, with
one optional unary minus per operand under the separate still-UNAPPROVED
[call-argument unary-negation recommendation]
(bootstrap-call-argument-unary.md). Implementation evidence does not accept
those recommendations. Parenthesized groups are provisionally implemented
under the separate still-UNAPPROVED
[call-argument parentheses recommendation]
(bootstrap-call-argument-parentheses.md); this evidence does not accept any of
the recommendations.

Recommend allowing count-driven chains of `+`, `-`, `*`, `/`, and `%` in
direct helper-call and forwarding-chain entry arguments. Use the precedence
already specified by the source grammar: multiplication, division, and
remainder bind more tightly than addition and subtraction, with each level
left-associative. Keep chained-expression operands limited to nonnegative
integer literals and recognized `Main` locals; an existing standalone unary
negation remains unchanged. Do not add parentheses or other unary/binary
operators in this increment.

Fold each operation to checked `int32` as it is encountered. Overflow at any
intermediate operation, division or remainder by zero, or an out-of-subset
operand must reject the whole source before publishing code or an image. The
scanner's existing bounded token/source resources remain the only expression
length limit; do not add an enumerated operation-count limit. This is a
compile-time extension only and does not change runtime expression lowering,
the call ABI, canonical grammar, or any language contract.
