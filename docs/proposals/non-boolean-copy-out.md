# Copy outputs for non-Boolean calls

**Status: Unapproved**

## Summary

The accepted output contract makes Boolean-result `out` publication conditional
on `true`; outputs of other return types must be initialized on every normal
return. Hosted binding currently limits non-Boolean/void `out` declarations to
scalar types, although ordinary non-unique Copy structs already have a
by-value representation and Boolean `out` supports them.

## Question

Should hosted source compilation provisionally admit handle-free Copy-struct
`out` parameters on non-Boolean and void functions, including public semantic
module, native module and native graph exports?

## Recommendation

Yes, as a bounded hosted-only support step. Admit non-unique struct values
through the existing checked publication rule: every normal return must
initialize the output, and exceptional departure publishes nothing. `out var`
continues to infer the declared parameter type. Reuse the existing Copy
layout and output ABI without changing `out` into `init ref<T>`.

Keep unique, managed-handle, buffer-view and cursor outputs outside this
non-Boolean slice. Preserve Boolean conditional publication, existing scalar
support, generic static-protocol restrictions and the scalar frontend's
hosted-verifier/emitter boundary. This experiment does not approve broader
output metadata or resolve general `out`/`init ref<T>` migration.

## Scope and validation

Cover both non-Boolean and void results, inferred `out var` destinations,
publication on every normal return, and public producer/consumer boundaries
under fail-fast and checked-exception policies. Verify semantic-module linking,
native AOT emission, native consumer execution and managed native graph
imports. Reject non-unique Copy eligibility for unique or managed-containing
values through the existing export-shape checks.
