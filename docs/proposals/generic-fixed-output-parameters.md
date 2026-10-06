# Fixed scalar outputs in generic Copy templates

**Status: Unapproved**

## Summary

Compilation-local `[Copy]` generic templates with fixed non-Boolean or `void`
results currently require every output parameter to use a generic type
parameter. Consider allowing fixed scalar `out` and `init ref<T>` side outputs
alongside at least one generic output, while retaining each output's existing
mode and publication contract.

## Recommendation

Provisionally allow fixed scalar output parameters in compilation-local
`[Copy]` templates with a fixed `int32`, `uint32`, `int64`, `uint64`, `char16`,
or `void` result, provided the template also has at least one output whose
type is a generic parameter. Limit fixed output types to `bool`, `char16`,
`int32`, `uint32`, `int64`, `uint64`, `float32`, and `float64`. Fixed outputs
do not participate in generic type inference.

Apply the existing output rules independently: every output must be fully
initialized on every normal return; `out` requires its existing writable
destination and supported non-Boolean output shape; `init ref<T>` requires a
vacant destination and retains its private managed-handle behavior only when
its type is generic. Fixed outputs are scalar-only, so they do not add
managed-handle or unique-value support. Checked exceptional departure
publishes neither fixed nor generic output. Preserve Boolean-result behavior,
including its existing fixed-output eligibility and conditional publication.

This is a hosted implementation experiment. It does not approve a target
contract, add fixed-valued outputs to the preceding mixed-mode proposal,
change private module formats or ABI, admit formal-valued results, or extend
source-free TRRM or scalar-frontend verification/emission.

## Required evidence

Cover fixed `out` and fixed `init ref<T>` side outputs alongside generic
outputs, with explicit and inferred substitutions. Verify definition-site
proof independently requires each fixed output, destinations retain their
exact modes, and checked exceptions publish neither fixed nor generic values.
Exercise direct native execution and semantic/native private-specialization
replacement behind unchanged consumers under both runtime policies. Retain
the existing Boolean mixed-mode behavior.

This proposal does not approve the target contract. Any implementation is
provisional evidence pending review.
