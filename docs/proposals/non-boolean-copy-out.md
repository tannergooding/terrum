# Copy outputs for non-Boolean calls

**Status: Unapproved**

## Summary

The accepted output contract makes Boolean-result `out` publication conditional
on `true`; outputs of other return types must be initialized on every normal
return. The hosted compiler now also supports ordinary non-unique Copy-struct
`out` declarations on non-Boolean and void functions. Whether this hosted
support should be adopted as an accepted target contract remains unapproved.

## Question

Should the existing hosted support for handle-free Copy-struct `out`
parameters on non-Boolean and void functions be adopted as an accepted target
contract, including public semantic-module, native-module and native-graph
exports?

## Recommendation

The original bounded implementation recommendation has been carried out:
retain the hosted support while keeping this proposal unapproved until the
target-contract decision is reviewed. The implementation admits non-unique
struct values through the existing checked publication rule: every normal
return must initialize the output, and exceptional departure does not publish
the output. `out var` continues to infer the declared parameter type. It
reuses the existing Copy layout and output ABI without changing `out` into
`init ref<T>`.

Keep unique, managed-handle, buffer-view and cursor outputs outside this
non-Boolean slice. Preserve Boolean conditional publication, existing scalar
support, generic static-protocol restrictions and the scalar frontend's
hosted-verifier/emitter boundary. This experiment does not approve broader
output metadata or resolve general `out`/`init ref<T>` migration.

## Implemented hosted support

Regression coverage now exercises zero and nonzero return values, void
functions, inferred `out var` destinations, checked exceptional departure,
and unchanged consumers linked against compatible semantic and native
producer replacements. The checked-exception case also verifies unwind
cleanup and rejects reading either Copy output in the catch path. CLI JIT,
semantic linking and native graph execution are covered.

This is implementation evidence, not approval of a language contract. It
does not add executable `out`/`init ref<T>` support to compact-indexed TRRM
or make Terrum-authored scalar frontend components independent of the hosted
verifier/emitter.

## Scope and validation

The hosted regression suite covers non-Boolean and void results, inferred
`out var` destinations, publication on every normal return, and public
producer/consumer boundaries under fail-fast and checked-exception policies.
It verifies semantic-module linking, native AOT emission, native consumer
execution and managed native graph imports. Existing export-shape tests reject
unique or managed-containing values. The implementation and its tests remain
provisional pending approval of the target contract.
