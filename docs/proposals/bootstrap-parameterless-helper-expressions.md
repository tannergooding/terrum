# Bounded parameterless bootstrap helper expressions

Status: UNAPPROVED recommendation.

This recommendation applies only to the existing direct-source route with one
preceding internal parameterless `int32` helper called by parameterless
`Main`. It does not extend parameterized helper bodies, other bootstrap routes,
or general helper composition.

Recommend reusing the checked call-argument expression parser when the
existing helper-body parser cannot consume the complete expression. Fold
`+`, `-`, `*`, `/`, and `%` with ordinary precedence and left associativity,
one unary minus on a literal or complete grouped expression, and nested
parentheses within the existing source and workspace bounds. Reject any
intermediate overflow, division or remainder by zero, signed minimum divided
by negative one, or unsupported expression before publication.

Newly accepted expressions fold to the existing immediate-return leaf; the
helper call and its ABI/unwind shape remain unchanged. Existing helper-body
encodings remain unchanged. This adds no runtime expression lowering, ABI
change, or canonical grammar change. Implementation evidence does not approve
this recommendation.
