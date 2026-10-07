# Copy outputs for non-Boolean calls

**Status: Accepted bounded contract.** The accepted slice is the existing
unconditional non-Boolean/void output contract described below; custom
conditional postcondition-driven `Copy` outputs remain outside this proposal.

## Summary

The accepted output contract makes Boolean-result `out` publication
conditional on `true`; non-Boolean and `void` outputs are initialized on every
normal return unless a declaration postcondition explicitly states a
conditional relationship. The accepted bounded profile adds ordinary
non-unique, handle-free `Copy`-struct `out` values to non-Boolean and `void`
functions, including public semantic-module, native-module and native-graph
exports.

## Accepted contract

Admit only ordinary non-unique, handle-free `Copy`-struct outputs with
non-Boolean or `void` results. Every normal return must fully initialize the
output; the function result is not a success discriminator. Exceptional
departure publishes no output, and the caller cannot read that destination on
the exceptional path. `out var` infers the declared parameter type. Reuse the
existing `Copy` layout and output ABI without changing `out` into `init ref<T>`.
Compatible public semantic-module and native-module exports, including native
graph imports, preserve the contract.

Keep unique, managed-handle, buffer-view and cursor outputs outside this
non-Boolean slice. Preserve Boolean conditional publication, existing scalar
support, generic static-protocol restrictions and the scalar frontend's
hosted-verifier/emitter boundary. Compilation-local generic outputs are a
separate question in the
[generic Copy-output proposal](generic-copy-out-publication.md). This
acceptance does not approve broader output metadata, custom conditional
postcondition-driven `Copy` outputs, or general `out`/`init ref<T>` migration.

## Implemented hosted support

Regression coverage now exercises zero and nonzero return values, void
functions, inferred `out var` destinations, checked exceptional departure,
and unchanged consumers linked against compatible semantic and native
producer replacements. The checked-exception case also verifies unwind
cleanup and rejects reading either Copy output in the catch path. CLI JIT,
semantic linking and native graph execution are covered.

This implementation supports the accepted bounded contract. It does not add
executable `out`/`init ref<T>` support to compact-indexed TRRM or make
Terrum-authored scalar frontend components independent of the hosted
verifier/emitter.

## Scope and validation

The hosted regression suite covers non-Boolean and void results, inferred
`out var` destinations, publication on every normal return, and public
producer/consumer boundaries under fail-fast and checked-exception policies.
It verifies semantic-module linking, native AOT emission, native consumer
execution and managed native graph imports. Existing export-shape tests reject
unique or managed-containing values. The implementation and its tests remain
evidence for this accepted slice only; they do not imply support for excluded
output forms.
