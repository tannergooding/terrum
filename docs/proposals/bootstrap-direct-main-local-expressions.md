# Bounded bootstrap direct-Main local expressions

Status: UNAPPROVED recommendation.

This recommendation applies only to the existing one- and two-local forms of
the direct-source `BootstrapInt32MainSource` route. It does not add local
declarations, extend the three-or-more-local leaf route, or change helper
routes.

Recommend reusing the checked call-argument expression evaluator when an
initializer or return expression is not fully consumed by the existing
single-operation parser. Initializers may reference only preceding immutable
locals. A return expression must reference at least one declared local.
Supported operations, grouping and unary negation match the evaluator's
bounded `int32` rules; each intermediate operation is checked, and overflow
or division or remainder by zero rejects before publication.

Existing fully parsed single-operation forms retain their operation-specific
leaf encoding. When the older parser rejects an expression because an operand
is negative, the shared evaluator's checked signed-`int32` behavior applies;
this intentionally relaxes that parser's provisional nonnegative-operand
restriction for expressions it newly accepts. Newly supported forms fold to
the immediate-return leaf; locals remain compile-time values and add no
runtime storage. This adds no general local lowering, ABI, or canonical
grammar change.

Implementation evidence does not approve this recommendation.
