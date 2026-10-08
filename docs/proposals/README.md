# Terrum specification proposals

The [source grammar](../specs/grammar-spec.md), [metadata design](../specs/il-metadata.md)
and bounded [coded instruction set](../specs/il-instruction-set.md) are approved
target designs, not claims of implementation or released formats. The
proposal documents in this directory contain unapproved choices. The source
grammar records its own open choices and scope boundaries. An **accepted
direction** settles a semantic
boundary only; adjacent grammar and APIs remain open unless explicitly
approved.

- [Core-library proposal](core-library-reference.md) records candidate
  declarations, dependency layers and unresolved API contracts.
- [Built-in numeric selection members](numeric-selection-members.md) records
  accepted universal selection semantics and primitive-member placement while
  leaving signatures and exact generic-interface contracts under review.
- [Static-proof and constant-evaluation proposal](static-proof-and-constant-evaluation.md)
  records the accepted portable-floor policy and initial proof scope while
  exact rules, effects, evaluator budgets and result representation remain
  under review.
- [Semantic IL extensions](il-extensions.md) collect the unapproved inline
  built-in type word and compact-profile follow-up, separate from the
  approved `(1, 0)` designs. Their size-oriented compact-encoding direction
  is accepted, while exact writer selection and additional forms remain open;
  allocation of kind `0xFE` is deferred pending compatibility and performance
  evidence.
- [Executable TRRM operation-encoding families](trrm-operation-encoding-families.md)
  is deferred until a concrete new operation/type set needs the decision;
  accepted draft opcode meanings remain unchanged and no future bytes are
  assigned.
- [Self-host frontend verification budget](self-host-frontend-verification-budget.md)
  records the deferred, unapproved split/phase-handoff option; reconsider it
  only if representative growth again reaches `TRM3016` after profiling and
  behavior-preserving caching/hot-path reductions.
- [Reference-call metadata](reference-call-metadata.md) has an accepted,
  bounded indexed-Body unsafe-region contract; its dependency-relation
  profile is deferred until a concrete persisted use case, while general
  output-call migration is deferred pending place, alias and publication
  rules; other extensions remain unapproved.
- [Floating results from generic Copy output templates](generic-floating-output-results.md)
  has accepted fixed floating results with single-mode outputs and the bounded
  mixed-mode/fixed-side combination; broader generic and floating capabilities
  remain excluded.
- [Span-backed token cursor](span-backed-token-cursor.md) accepts the bounded
  compilation-local `TokenCursor` and indexed scalar-parser contract; general
  `Span<T>` support, source-level `nuint`, writable/ref-returning access and
  persisted capture metadata remain unapproved.
- [Forwarded `Span<Token>` results](token-span-result-forwarding.md)
  is deferred until a concrete same-compilation provider/consumer path needs
  returned views; its proposed `DependsOn` contract remains unapproved.
- [Persisted internal `Span<Token>` forwarding](token-span-semantic-module.md)
  is deferred until a concrete private provider/consumer corpus and supported
  invocation profile justify its full IR/linking work.
- [Invocation-scoped source grant](invocation-scoped-source-grant.md)
  is deferred; accepted semantic-module execution remains source-independent
  unless a concrete source-processing tooling API requires this boundary.
- [Native source-grant library](native-source-grant-library.md) is deferred
  until a concrete native source-processing caller exists; no ABI or
  16,384-unit cap is accepted.
- [Sample-driven Windows native FFI](windows-native-ffi.md) accepts only the
  staged Windows x64 direction using the DX11 sketch as a target. Its
  import/catalog, storage/pointer, callback and closed-COM contracts remain
  unapproved; this does not establish general interop.
- [Persisted scanner invocation profile](scanner-invocation-profile.md) is
  deferred until an integrated compiler path needs it and its full-file
  size/resource policy is defined; revision-76 support remains provisional.
- [Registered UTF-8 string-literal target](registered-string-target-binding.md)
  is deferred; its source-reading semantic-module-JIT route is not the
  intended compile-time literal model and does not define general handlers.
- [Checked-exception TRRM](checked-exception-trrm.md) records the accepted
  requirement that TRRM 1.0 support core checked-exception semantics by GA;
  exceptional-edge encoding and conformance details remain open.
- [Output parameters in executable TRRM](trrm-output-parameters.md) records
  the accepted requirement that core output-mode semantics be executable in
  TRRM 1.0 by GA; its profile, encoding, place, publication and
  exceptional-state details remain unapproved.
- [Optional scalar TRRM execution](optional-scalar-trrm-execution.md) records
  an implementation-only boundary under the approved Body contract; optional
  source types, call signatures and native ABI support remain out of scope.
- [Static initialization metadata and attribute schema](static-initialization-metadata.md)
  records the accepted typed-enum `StaticInitializer` argument and
  zero-argument `LazyInitialization` marker, plus the explicit typed
  owning-group access obligation and restricted implementation-only first
  slice. Static fields have a distinct logical identity; multiplicity,
  record/operation encoding and runtime ABI remain open.
- [First public fixed-layout profile](public-fixed-layout-profile.md)
  recommends a bounded natural-alignment Windows x64 opt-in without changing
  existing private Sequential or semantic-module layout contracts.
- [Compare-exchange order pairs](atomic-cas-order-pairs.md) recommends a
  finite compile-time-valid table for CAS success/failure memory orders;
  it does not add atomic support or change the accepted bitwise-comparison rule.
Use these proposals for candidate APIs, source choices and format extensions,
not as a second policy or implementation checklist. Settled call, output,
cleanup and proof contracts are in [language contracts](../specs/language-contracts.md);
other accepted direction lives in the relevant [language](../language.md),
[lifetime](../specs/lifetimes.md), [conversion](../specs/conversions.md),
[layout](../specs/data-layout-and-abi.md), [type-family](../specs/type-families.md),
[memory](../specs/memory-model.md) and [library](../specs/libraries.md) specifications.
Current support and sequencing are in the [implementation reference](../implementation.md)
and [roadmap](../roadmap.md); candidate compiler components are in
[self-host](../../self-host/README.md).
