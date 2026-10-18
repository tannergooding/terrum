# Persisted private `Span<Token>` values and operations

**Status: unapproved bounded private-persistence recommendation.**
Provisional implementation is not an accepted span metadata schema.
Returned-view forwarding remains outside this slice.

## Consumer and integration

The complete `ScalarFrontend` producer and `ScalarFrontendProgramReader`
consumer need private readonly descriptors projected from validated snapshots,
passed through private calls, copied, indexed and retired. Their
[invocation profile](scalar-frontend-invocation-profile.md) is a concrete
corpus, not approval of persistence, profile identity or a public span ABI.

Recommend preserving the complete private structured IR needed by that
corpus rather than rewriting span reads into source-shaped host-buffer policy.
The [private-persistence contract](../specs/representation/persistence.md)
continues to distinguish this development format from normative TRRM.
The [dependency](../specs/language/dependencies-and-views.md) and
[lifetime](../specs/lifetimes.md#three-independent-dependencies) contracts
remain authoritative for backing, storage and cleanup.

## Admitted private IR

Retain:

- Snapshot-to-readonly-token-span construction, native-width length and
  unsigned indexed by-value Token reads.
- Readonly descriptor copies and conditional selection, private parameter
  passing, named-argument mappings and lexical descriptor retirement.
- The complete surrounding structured control flow, staged calls, cleanup
  and checked recovery. A list of span opcodes without that context cannot
  establish lifetime correctness.
- Explicit compiler-known Token, SourceRange and TokenSnapshot roles across
  selected type tables, remapping and private renaming. Roles identify a
  layout to check; they do not certify it.

Keep span-bearing callables private to the artifact, regardless of source
accessibility within a private implementation. Imported/exported signatures
remain span-free. No public type identity is inferred from private spelling
or `$producer` renaming.

## Exact private schema

Require an exact selected schema, not “revision 77 or equivalent.” The
prototype's span extension uses roles `none`, `Token`, `SourceRange`,
`TokenSnapshot`; expression tags 32/33/34 for snapshot projection, span length
and span read; and statement tag 28 for descriptor drop. The role byte follows
the struct name, while existing type IDs and IR headers describe fields,
operands, results and source spans. Snapshot projection retains the owner slot
and source-buffer ordinal; length retains the view; read retains view/index
and Token result type; drop retains the descriptor slot.

These are **private implementation encodings**, not proposed TRRM allocations.
The span schema first appeared under development revision 77, but later exact
combined schemas also carry it: active private-span/frontend fixtures use
revision 81, including fixed-layout information. No “77-only” rule follows.

Before adoption, tags/revisions may be replaced, consolidated or reassigned
if the selected producer and reader agree exactly. Do not require revision-76
compatibility or a fresh version for each incremental experiment without a
consumer need. If an older exact reader remains supported, it must retain
its old interpretation and reject new operations; it cannot guess based on
trailing bytes or silently reinterpret a private tag.

## Mandatory reconstruction and linking

After reading and again after linking **each** replacement, independently
validate:

1. Supported exact schema, checked node/type/count/size limits, role values,
   uniqueness, field/result type remapping and actual role layouts.
2. Validated snapshot provenance, source-buffer identity/access and index
   width. A serialized role or owner slot is not trusted source provenance.
3. Initialization, live owners, backing sets, access, scope/drop shape and
   cleanup. Copies/joins/private adapters must preserve all dependencies.
4. Evaluation-order staging through later arguments that could replace/drop
   backing, including output/init arguments. Retire descriptors before owner
   reuse only when all remaining dependencies allow it.
5. Retained invocation requirements and rejection on source-free surfaces.

Persist no trusted liveness, loan, origin or verification certificate.
Producer-generated artifacts do not bypass consumer verification. Replacement
may reorder private types or alter private calls, but cannot turn private
backing facts into a weaker externally replaceable promise.

## Alternatives and exclusions

Keeping spans compilation-local is a valid alternative, but cannot execute
this producer/reader corpus through persisted private artifacts unchanged.
A general public view schema would be substantially broader: it needs
declaration relationships, cross-module compatibility and target layouts.
Prefer the private full-IR slice for the bounded experiment rather than
pretending that generic metadata is already supported.

Do not include returned spans or result-dependency metadata, TokenCursor
captures/advance, span fields, output/vacant/mutable span slots, generic
declaration metadata or imported/exported span signatures. The separately
[deferred result profile](token-span-result-forwarding.md) is not a
prerequisite here. Native DLLs/packages, graphs/module sets, persisted AOT,
source-free TRRM and general references/views remain outside this slice.
It neither changes source semantics nor widens the accepted cursor window.

## Acceptance gates

Require exact schema/profile adoption and:

- Source-provider/consumer round trips for construction, named/transitive
  calls, copies, conditional backing, unsigned reads, retirement and cleanup
  under both policies and fresh grants.
- Replacement with reordered declarations and renamed/remapped private types;
  rederive facts after linking instead of trusting the original provider.
- Forged roles/layouts, mismatched type IDs, wrong source ordinal/access,
  index width, invalid lifetime/drop order and a later argument invalidating
  staged backing. All must reject before emission/execution.
- Checked recovery with descriptor cleanup before owner reuse, normal
  cleanup, missing grants and source-free rejection.
- Complete v88 producer/reader invocation without hosted-parser fallback,
  while the public/exported/result-span exclusions remain enforced.

Current evidence is in the
[private-span reader/writer](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.TokenSpans.cs),
[private-span regressions](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Frontend/ScalarFrontendInvocationProfileTests.PrivateSpans.cs)
and [complete-program regressions](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Frontend/ScalarFrontendInvocationProfileTests.CompleteProgram.cs).
The [module](../implementation/modules.md) and
[composition](../implementation/bootstrap-frontend-composition.md) inventories
own current support, not this list of acceptance requirements.
