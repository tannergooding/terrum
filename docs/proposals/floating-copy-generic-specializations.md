# Floating Copy generic specializations

**Status: Unapproved**

## Summary

The hosted compiler supports closed specializations of Copy-only templates for
six scalar types, while `float32` and `float64` are currently excluded from
generic argument eligibility. Should the two floating formats be admitted as
concrete Copy-only generic arguments without granting symbolic floating
operations or changing generic proof, evaluator, persistence, or ABI contracts?

## Recommendation

Provisionally admit `float32` and `float64` only as concrete generic
arguments to the existing Copy-only specialization path. Reuse the existing
typed argument, result inference, specialization, and value-transport
mechanisms. Generic bodies may pass, return, assign, and publish a substituted
`T` value, including through nested Copy-only helpers, but do not gain
arithmetic, comparison, conversion, or other primitive operator rights.

Keep this eligibility separate from shared Copy predicates used by getters,
operator results, and protocol conformance. This proposal does not itself
extend symbolic generic scalar types, ConstEval, or concrete floating
dependencies reached during generic proof; the latter remains a separate
[unapproved proposal](floating-copy-proof-dependencies.md). User-defined
structures containing floating fields are outside this slice. Preserve
specialization limits and the existing output publication and exception
behavior.

This is a hosted compiler experiment, not an accepted source-contract change.
It does not make the Terrum-authored scalar frontend independent of hosted
verification/emission, add generic support to source-free TRRM, or change
public generic ABI or persisted metadata.

## Implemented hosted support

The implementation adds floating formats only to concrete generic argument
eligibility and exercises explicit/inferred specializations, nested forwarding,
Copy selection, output publication, bit-preserving native transport, and
private semantic/native module specializations. Proof, operator and evaluator
eligibility remain unchanged. These results are evidence for review, not
approval of the proposed contract.
