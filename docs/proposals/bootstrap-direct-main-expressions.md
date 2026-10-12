# Bounded bootstrap direct-Main expressions

Status: UNAPPROVED recommendation.

This recommendation applies only to expression-bodied `Main` and a block
containing only one `return` in the direct-source `BootstrapInt32MainSource`
route. It does not extend helper bodies, helper arguments, or the one-/two-
local route.

Recommend reusing the existing checked call-argument expression evaluator
when the direct `Main` expression is not fully consumed by its existing
single-operation parser. The bounded form supports `int32` literals, one
unary minus before a literal or grouped expression, nested parentheses, and
count-driven `+`, `-`, `*`, `/`, and `%` with the evaluator's existing
precedence and left-associativity. Names and calls remain unsupported in this
no-local form.

Each intermediate signed-`int32` operation is checked; negative operands are
allowed by those arithmetic rules, while overflow and division or remainder
by zero reject before code publication. Newly supported expressions fold to
the existing immediate-return leaf. Existing single-operation forms retain
their current operation-specific encoding. This adds no runtime expression
lowering, local storage, ABI, or canonical grammar change.

Implementation evidence does not approve this recommendation.
