# Bootstrap leaf local expressions

Status: UNAPPROVED recommendation.

This recommendation is a follow-on to
[source-sized bootstrap leaf locals](bootstrap-source-sized-leaf-locals.md)
and applies only to the standalone direct-source leaf route with three or more
immutable `int32` locals. It does not widen the parameterless-helper or
helper-call routes.

Recommend evaluating each initializer and the final return expression with
the same bounded expression parser already used for helper-call arguments:
checked, left-associative `+`, `-`, `*`, `/`, and `%` with ordinary
multiplicative-before-additive precedence; one unary minus on a literal/local
or a complete parenthesized expression; and nested grouping evaluated with
the existing iterative source-sized stacks. Only preceding locals are visible
in an initializer. The return expression must reference at least one declared
local. Overflow and division or remainder by zero reject before publication.

Reusing this parser avoids a second expression evaluator and does not add
runtime local storage or general expression lowering. The underlying
call-argument precedence, unary, parentheses and grouped-unary recommendations
remain **UNAPPROVED**; this recommendation neither accepts nor changes them or
canonical grammar. Local count remains bounded by source and host-table
capacity, not an enumerated count.

Implementation evidence does not approve this recommendation or any
prerequisite expression recommendation.
