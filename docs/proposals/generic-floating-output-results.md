# Floating results from generic Copy output templates

**Status: Unapproved**

## Summary

Compilation-local `[Copy]` generic output templates provisionally support
fixed `float32` and `float64` results only when a generic-typed output anchors
the specialization. Consider retaining that bounded hosted extension,
including fixed scalar side outputs, without changing generic output modes or
symbolic floating capabilities.

## Recommendation

Provisionally allow `float32` and `float64` as fixed results for
compilation-local `[Copy]` templates that have at least one `out` or
`init ref<T>` output whose formal type is a generic parameter. Fixed scalar
outputs may appear as side outputs but cannot be the template's only outputs;
output-free templates remain outside this extension. Keep the existing
every-normal-return publication proof: floating results are non-Boolean and
do not correlate with whether an output is initialized. Checked exceptional
departure publishes no output; `init` destinations remain vacant, while
ordinary `out` destinations retain their existing lack of
rollback/readability guarantees.

Use a result-specific eligibility gate rather than widening shared generic
scalar/type parsing. This does not admit fixed floating by-value parameters,
floating generic operator/protocol results, output-free floating-result
templates, or formal-valued results. It does not grant symbolic floating
operators or widen concrete generic argument eligibility; existing floating
specialization and proof-dependency support remain separate provisional
features. Preserve exact `out` and `init ref<T>` destination matching,
scalar-only fixed side outputs, per-mode eligibility when a formal is used in
both modes, private managed-handle support for `init` only, and rejection of
managed ordinary `out`, unique/owning outputs, and public generic
declarations/witnesses. Boolean-result conditional publication is unchanged.
Reuse existing fixed floating result transport and private semantic/native
module formats and ABI.

This is a hosted implementation experiment, not an accepted contract. It
does not extend source-free TRRM or the Terrum-authored scalar frontend beyond
hosted verification/emission.

## Required evidence

Cover both fixed floating result formats with generic `out`, `init ref<T>`,
and mixed-mode outputs, plus fixed scalar side outputs and explicit/inferred
generic substitutions. Uncalled-definition proof must independently reject a
normal return that omits each generic or fixed output. Add negative cases for
output-free floating-result templates, fixed floating by-value parameters,
floating protocol results, and formal-valued results; fixed result/side-output
types must not infer generic substitutions, and `init var` alone remains
insufficient. Verify unconditional publication for both signed zeros and
subnormal/infinite results without introducing a new NaN-payload or arithmetic
contract. Under checked exceptions, cover a write followed by failure; verify
init vacancy, reject catch-path reads independently for each output, and retry
the same init destination. Retain the existing generic `finally` no-throw
restriction; do not add throwing-cleanup support in this slice. Exercise direct
native execution and private semantic/native producer replacement behind
unchanged consumers under both runtime policies, retaining exact result width
and per-parameter mode metadata. Floating result checks must use the existing
hosted floating semantics and avoid equality as a proxy for signed-zero bit
identity.

**Progress:** The hosted implementation provisionally supports fixed
`float32`/`float64` results for compilation-local `[Copy]` templates with
generic outputs. Direct execution and module replacement already cover generic
`out T` and `init ref<T>` results, signed zero, subnormals and checked-failure
publication. An isolated replacement test now combines both floating result
widths with mixed generic `out T`/`init ref<T>` modes and fixed `out int32` and
`init ref<float32/float64>` side outputs, explicit and inferred generic
substitutions, exact mode/type metadata, unchanged consumers, semantic-module
JIT/AOT and native graph AOT/JIT under both runtime policies; the checked paths
verify `init` vacancy and retry. A separate native raw-bit oracle checks both
zero signs, minimum subnormals, both infinities and width-specific rounding
midpoints for each result width without adding a NaN-payload guarantee.
Boundary tests reject fixed-floating by-value parameters with an otherwise
valid generic payload, templates with fixed outputs only, and substitution
inference from a typed floating result context. These remain provisional
implementation evidence and do not approve this proposal.

This proposal does not approve the target contract. Any implementation is
provisional evidence pending review.
