# Source-sized bootstrap leaf locals

Status: UNAPPROVED recommendation.

The standalone direct-source leaf route accepts at most two immutable
`int32` locals in its existing block form. The parameterless-helper,
identity-helper and helper-call routes have separate local contracts. This
recommendation covers only standalone `public int32 Main()` leaf bodies with
three or more locals.

Recommend accepting a source-bounded sequence of immutable `int32` locals
before one explicit `return`. Each initializer is either a literal or an
earlier-local operand, optionally unary-negated, or one checked binary
operation over nonnegative literal or earlier-local operands. The return
expression uses the same bounded form and must reference at least one declared
local. Unary-minus syntax is not combined with the binary form. Preserve the
existing zero-to-two-local path, and continue rejecting mutable locals,
forward/self references, duplicate names, extra statements, unsupported
expressions, overflow and division or remainder by zero.

Store local token indices and folded values in source-sized host buffers and
parse declarations by count; do not add an enumerated local-count limit. The
scanner's existing 16,384-UTF-16-unit input limit remains the outer source
bound, and insufficient host tables reject before code or image publication.
The emitted leaf remains a folded immediate return; this does not add runtime
local storage or general local lowering.

This is a bounded bootstrap-parser recommendation only. It does not change
canonical grammar, accepted contract status, the parameterless-helper or
helper-call routes, or general code-generation support. Implementation
evidence does not approve this recommendation.
