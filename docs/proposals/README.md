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
  output-call migration and other extensions remain unapproved.
- [Floating results from generic Copy output templates](generic-floating-output-results.md)
  has accepted fixed floating results for single-mode generic outputs without
  fixed side parameters; mixed-mode and fixed-side combinations remain
  unapproved.
- [Span-backed token cursor](span-backed-token-cursor.md) records the open
  backing, capture, indexing and construction contracts needed for the
  existing zero-copy self-host cursor candidate.
- [Forwarded `Span<Token>` results](token-span-result-forwarding.md)
  recommends a same-compilation hosted experiment for forwarding declared
  backing dependencies through bounded top-level functions.
- [Persisted internal `Span<Token>` forwarding](token-span-semantic-module.md)
  recommends deferring persistence until a concrete private provider/consumer
  corpus and supported invocation profile justify its full IR/linking work.
- [Invocation-scoped source grant](invocation-scoped-source-grant.md)
  recommends a narrow persisted-module dependency for source-aware compiler
  components without enabling arbitrary host-buffer capture.
- [Native source-grant library](native-source-grant-library.md) proposes a
  bounded per-call source ABI for internal native-library exports; it does not
  extend the semantic-module source-grant profile or establish DLL support.
- [Sample-driven Windows native FFI](windows-native-ffi.md) recommends checked
  Windows x64 scalar imports through a trusted semantic-identity catalog
  first, followed by only the storage, pointer, callback and closed-COM slices
  needed by the Win32/DX11 design sample; it does not establish general
  interop or change accepted contracts.
- [Persisted scanner invocation profile](scanner-invocation-profile.md)
  considers a named, scanner-specific invocation profile and copied result
  publication without generalizing source grants to arbitrary buffers.
- [Registered UTF-8 string-literal target](registered-string-target-binding.md)
  recommends one bounded `StringLiteral` to `StringNameRowUtf8` registration
  without defining general builder/handler semantics.
- [Checked-exception TRRM](checked-exception-trrm.md) records the unapproved
  exceptional-control-flow contract needed before source-free checked-exception
  Bodies can be considered.
- [Output parameters in executable TRRM](trrm-output-parameters.md) records
  the unapproved place, publication and exceptional-state contract required
  before `out` or `init ref<T>` modes can execute from source-free artifacts.
- [Optional scalar TRRM execution](optional-scalar-trrm-execution.md) records
  a proposed internal execution boundary that does not imply source-language
  or public native ABI support.
- [Static initialization metadata and attribute schema](static-initialization-metadata.md)
  recommends how to persist the accepted static-storage contract without
  changing its initialization or failure semantics.
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
