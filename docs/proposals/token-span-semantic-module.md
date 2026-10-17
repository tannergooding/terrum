# Persisted private `Span<Token>` values and operations

**Status: UNAPPROVED bounded recommendation, with provisional implementation
under semantic-module revision 77. No persisted span contract or metadata is
accepted. Returned-view forwarding remains outside this slice.**

## Summary

The complete `ScalarFrontend` publication and `ScalarFrontendProgramReader`
consumer supply the previously missing concrete use case. The frontend needs
private readonly token descriptors projected from validated snapshots, passed
through private calls, copied, indexed and retired. It does not need
`TokenCursor` capture storage or a returned-view ABI to publish v88 and summarize
it against a fresh source grant.

## Bounded recommendation

Proceed provisionally with a new exact semantic-module revision for the private
operations this corpus actually uses. Keep revision 76 byte-compatible and
span-free; reject private span IR under that revision rather than reinterpreting
it. Emit revision 77 only for the admitted source-grant span path or the separate
[v88 frontend invocation profile](scalar-frontend-invocation-profile.md).
Existing non-span scanner modules retain revision 76 and the unchanged
`scanner-10` identity and ten-buffer shape.

Retain the full admitted structured IR: snapshot projection, native-width
length, unsigned indexing with by-value Token results, readonly descriptor
copies/conditional selection, private parameter passing, and lexical descriptor
retirement. Retain ordinary call argument mappings, structured cleanup and
checked recovery. Keep span-bearing implementation callables private to the
artifact even when a source method is accessible within its private managed
implementation. Imported/exported signatures must remain span-free.

Preserve explicit compiler-known Token, SourceRange and TokenSnapshot roles
through type-table selection, remapping and private renaming. Roles identify
which layout to validate; they do not attest that a layout or lifetime is
valid. Mandatory verification must rederive backing sets, staged-argument
obligations, initialized/live owners and descriptor retirement after reading
and again after linking every replacement. Do not serialize trusted analysis
results or turn private backing dependencies into external compatibility
contracts.

## Exact provisional encoding

Revision 77 adds one role byte after each struct name: none, Token, SourceRange
or TokenSnapshot. Existing declared type identifiers and remapping still
identify each field and result type. Expression tags 32, 33 and 34 retain
`BoundSnapshotSpan` (owner slot/source-buffer ordinal), `BoundSpanLength`
(view expression), and `BoundSpanRead` (view/index expressions and Token result
type). Statement tag 28 retains `BoundDropSpan`'s descriptor slot. The existing
type/span headers, recursive node/size limits and function/variable metadata
remain checked.

Role layouts, uniqueness, snapshot provenance, source-buffer access, unsigned
index carrier, scope shape and backing liveness are independently verified.
The roles survive `$producer` renaming; private spelling is not a public type
identity. New tags reject in revision 76. Unsupported revisions and malformed
roles/tags reject without falling back to another interpretation.

## Evidence and remaining boundary

The focused source-provider/consumer corpus round-trips private span creation,
named/transitive reads, copies, conditional backing, retirement and cleanup
under both policies through fresh scanner grants. An unchanged revision-76
scalar consumer also accepts a replacement revision-77 provider with reordered
type declarations and changed private calls; linking renames its private types
and rechecks their roles. Forgery regressions reject altered roles/layouts,
source provenance, index width, descriptor retirement and a later output
argument that invalidates already-staged backing. Checked recovery exercises
descriptor cleanup before owners are reused.

The separate frontend profile additionally executes complete v88 publication
and summary decoding with fresh grants. Actual evidence and precise limits are
recorded in the [implementation reference](../implementation.md) and
[self-host inventory](../implementation/self-host.md), not as acceptance here.

Returned `Span<Token>` values, `[return: DependsOn(...)]` result metadata,
`TokenCursor` captures/advance, span fields, output/vacant/mutable span slots,
exported/imported span signatures and generic declaration metadata remain
excluded. General views/references, source-dependent native DLLs/packages,
graphs/module sets, persisted AOT and source-free TRRM are not established.
This recommendation changes neither canonical lifetime contracts nor the
accepted 64-token cursor/window bound.
