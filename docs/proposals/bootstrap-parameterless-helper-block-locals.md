# Source-sized locals in parameterless bootstrap helper bodies

Status: UNAPPROVED recommendation.

The provisional parameterless helper leaf currently uses an expression body.
Recommend also accepting a block body with one or more immutable `int32` local
declarations followed by one explicit `return` expression. Local initializers
and the return expression use the existing checked call-argument expression
parser; each initializer sees only preceding locals, and the return expression
must reference at least one declared local. Reject mutable or duplicate locals,
self/forward references, a local matching the helper name, empty local lists,
extra statements, unsupported expressions, overflow and division or remainder
by zero.

Store local token indices and folded values in the existing host local tables.
The 16,384-UTF-16-unit scanner bound and supplied workspace capacities remain
the resource limits; do not add an enumerated local-count cap. Fold the return
value into the existing helper leaf encoding, preserving every call frame,
the parameterless forwarding-chain rules, PE shape and per-frame unwind
metadata. Main-body locals remain a separate scope and reuse the tables only
after the helper body has been folded.

This is a bounded bootstrap parser extension, not runtime local lowering,
general block execution, parameterized-helper support, a canonical grammar
change or proposal approval. Tests should cover sequential evaluation,
malformed and overflowing bodies, workspace/source limits, unchanged leaf
bytes, generated PE execution and native unwind under both runtime policies.
