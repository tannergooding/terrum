# Concrete floating dependencies in hosted generic proofs

**Status: Unapproved**

## Summary

Hosted generic definition proofs can hydrate reached concrete Copy layouts and
callable signatures, but floating leaves are currently excluded. Should
`float32` and `float64` be allowed inside reached non-unique Copy-struct
dependencies and non-class concrete signatures, without changing symbolic
generic capabilities or managed-class proof dependencies?

## Recommendation

Provisionally hydrate floating leaves only within reached, non-unique Copy
struct descriptors and within reached top-level or struct-owned concrete
function signatures. This enables generic bodies to use existing concrete
constructors, fields, factories, and methods involving those structs, while
keeping the type known and concrete at the definition site. It does not confer
field access, floating operations, or other capabilities on symbolic `T`.

Preserve descriptor identity, fields, layout, visibility, mutability,
effects, caller-safety, output mode, specialization limits, and existing
persistence rules. Hydration must preserve `init ref<T>` metadata as well as
`out`; a proof-only parameter cannot silently change its publication mode.

Keep floating fields and scalar signatures in managed-class dependency
closures outside this slice. Leave shared Copy predicates, primitive
operator-witness eligibility and results, generic scalar parameter tables,
ConstEval, scalar floating getters, public generic ABI, and source-free TRRM
unchanged. This is hosted implementation evidence, not an accepted target
contract or a scalar-frontend generic-support claim.

## Implemented hosted support

The implementation provisionally hydrates floating leaves in concrete
non-unique Copy-struct dependencies reached by generic proofs, and floating
scalar types in reached non-class concrete signatures. Regression coverage
checks nested layouts, initialization modes, execution under both runtime
policies, and private semantic/native module use. Managed-class dependency
closures and symbolic generic operations remain excluded. These results are
evidence for review, not approval of the proposed contract.
