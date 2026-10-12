# Source-sized bootstrap helper-call locals

Status: UNAPPROVED recommendation.

The direct parameterized-helper, parameterless-helper, and forwarding-chain
routes now have provisional source-sized local-table support as implementation
evidence; this does not accept the recommendation. The standalone
leaf/identity-helper routes have separate local subsets; source-sized leaf
locals are tracked independently in the
[direct leaf recommendation](bootstrap-source-sized-leaf-locals.md). This
recommendation covers helper-call routes only and does not generalize those
other routes.

Recommend applying one source-sized local-table policy to both the direct
helper-call/forwarding-chain route and the parameterless-helper route. Allow a
source-bounded sequence of immutable `int32` local declarations before the
existing single helper-call return. Local declarations remain sequential and
immutable; only preceding locals are visible, and forward references,
self-reference, duplicate/shadowing names, mutable locals and additional
statements remain unsupported. Initializer expression forms are tracked
separately by the
[helper-local expression recommendation](bootstrap-helper-local-expressions.md).
Resolve call arguments against the complete preceding local table.

Store token indices and folded values in source-sized host buffers and parse
declarations by count; do not add an enumerated local-count limit. The existing
scanner limit of 16,384 UTF-16 source units and the supplied table capacities
remain the resource bounds. Overflow, insufficient capacity or invalid local
binding rejects before code or image publication. This is a bounded bootstrap
parser extension only; it does not change canonical grammar, runtime local
lowering, the call ABI, or any language contract. Implementation evidence does
not accept this or its prerequisite call-argument recommendations.

## Provisional implementation evidence

These helper-call routes store `Main` local names and folded values in the
supplied source-sized tables and resolve each initializer and call argument
against only preceding locals. Release and Debug regressions derive the
maximum local count whose complete source fits the scanner bound, execute the
generated image under both runtime policies, and confirm one additional local
exceeds that bound. Separate regressions reject insufficient name or value
table capacity. This remains implementation evidence; the recommendation and
its related call-argument rules remain UNAPPROVED.
