# Persisted internal `Span<Token>` forwarding

**Status: Unapproved**

## Summary

The hosted compiler now provisionally verifies same-compilation internal
functions that return readonly `Span<Token>` values under a closed
`[return: DependsOn(...)]` source set. Semantic modules still reject every
span-bearing value and do not serialize the span operations or result-source
metadata. Persisting only the function signature or dependency ordinals would
leave the body unverifiable and could erase backing obligations during
linking. The current self-host corpus has not established a concrete persisted
provider/consumer path that needs this extension.

## Recommendation

Defer implementation until a concrete persisted provider, consumer and
invocation path demonstrates value under an existing invocation profile. The
current same-compilation feature does not by itself justify a semantic-module
revision. If that evidence appears, first consider a new exact revision for
private internal top-level functions and persisted semantic-module JIT with
fresh source grants. Keep exported/imported signatures span-free, and do not
imply support for persisted AOT, native packages, graphs or module sets where
their source-grant requirements are not currently admitted.

Any future slice must round-trip the complete admitted private IR, not only a
new metadata row: preserve compiler-known Token/SourceRange/TokenSnapshot
roles through module-local type remapping, serialize the supported span
operations and declared dependency ordinals, then rederive backing sets,
staging obligations and lifetime validity with the mandatory verifier. Do not
persist trusted analysis results. `BoundSpanAdvance` is cursor-only and is
outside a Span-only slice. Reverify every replacement's private graph; private
dependency sets may change when bodies and callers are reverified, and are not
external compatibility contracts.

## Required evidence

- Identify a real private provider/consumer corpus and an already-supported
  invocation profile before assigning implementation priority.
- Round-trip internal forwarding and consuming functions through a new exact
  semantic-module revision using persisted JIT with fresh grants under both
  runtime policies.
- Reorder and rename module-private type tables to prove compiler-known type
  roles survive linking without making private names globally shared.
- Replace a provider while keeping the consumer unchanged; permit changed
  private dependency sets only when the replacement body and complete caller
  graph reverify. Reject undeclared provenance, local-owner escape and
  malformed dependency metadata.
- Cover named/transitive calls, conditional results, copies, owner
  invalidation, descriptor retirement, argument staging, cleanup and checked
  unwind after persistence.
- Reject exported/imported spans, dependency-bearing fields, captures,
  outputs, generic declarations and operations outside the exact profile.
- Confirm revision 70 and unsupported revision 69 or future revisions fail closed; retain
  rejection of persisted AOT/native packages, graphs/module sets when
  invocation profiles do not permit them, and source-free TRRM.

This proposal does not approve a target contract, general reference metadata
or public view ABI. Annotation spelling, profile, use case and any future
bounded support remain provisional. The current recommendation is to defer
this implementation until the evidence above is available.
