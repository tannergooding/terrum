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
  only remaining integration/API extensions; exact scalar signatures,
  primitive-member placement, selection and bounds-failure semantics are
  specified canonically.
- [Static-proof and constant-evaluation proposal](static-proof-and-constant-evaluation.md)
  links the selected rule catalog, effects, deterministic evaluator and
  portable resource envelopes; physical records, intrinsic registrations and
  beyond-floor extensions remain separate work.
- [Semantic IL extensions](il-extensions.md) collect the unapproved inline
  built-in type word and compact-profile follow-up, separate from the
  approved `(1, 0)` designs. Their size-oriented compact-encoding direction
  is accepted, while exact writer selection and additional forms remain open;
  allocation of kind `0xFE` is deferred pending compatibility and performance
  evidence.
- [Executable TRRM operation-encoding families](trrm-operation-encoding-families.md)
  records alternatives beyond the adopted unified registry and bounded
  semantic-family capacity. Short forms, generic numeric witnesses, new
  ternary operations and extended envelopes remain unapproved.
- [Self-host frontend verification budget](self-host-frontend-verification-budget.md)
  records the deferred, unapproved split/phase-handoff option; reconsider it
  only if representative growth again reaches `TRM3016` after profiling and
  behavior-preserving caching/hot-path reductions.
- [Capacity-scaled bootstrap helper calls](bootstrap-variable-arity-helper-calls.md)
  recommends count-driven helper lowering and PE image sizing from actual
  buffers, with checked stack probing and unwind support before large frames;
  the current fixed PE section is a resource limit, not an ABI arity boundary.
- [Bounded bootstrap helper call chains](bootstrap-helper-call-chains.md)
  recommends one same-arity, declaration-order forwarding chain to test
  nonleaf argument passing and multiple unwind records in the provisional
  source-to-PE route; general function composition and ABI contracts remain
  unapproved.
- [Count-driven bootstrap helper call chains](bootstrap-count-driven-call-chains.md)
  recommends deriving linear same-arity forwarding-chain depth from source and
  available PE/unwind resources; it does not authorize general function
  composition or a language/ABI contract.
- [Bounded bootstrap call-argument expressions](bootstrap-call-argument-expressions.md)
  recommends checked-folding one existing arithmetic operation per direct
  helper-call argument without adding runtime expression lowering or changing
  the call ABI.
- [Bounded bootstrap call-argument precedence](bootstrap-call-argument-precedence.md)
  recommends count-driven, checked folding of arithmetic chains with existing
  grammar precedence, without parentheses or runtime expression lowering.
- [Bounded bootstrap call-argument unary negation](bootstrap-call-argument-unary.md)
  recommends one optional unary minus per literal/local operand in those
  chains, without general prefix-operator support.
- [Bounded bootstrap call-argument parentheses](bootstrap-call-argument-parentheses.md)
  recommends nested grouping with source-sized iterative evaluation stacks,
  without recursion or an enumerated nesting limit.
- [Bounded bootstrap unary negation on grouped arguments](bootstrap-call-argument-group-unary.md)
  recommends one checked unary minus on a complete grouped argument expression,
  without general prefix-operator support.
- [Source-sized bootstrap helper-call locals](bootstrap-call-chain-source-sized-locals.md)
  recommends source-bounded sequential local tables in helper-call and
  parameterless-helper routes, without general local lowering.
- [Bounded bootstrap helper-local expressions](bootstrap-helper-local-expressions.md)
  recommends reusing the checked call-argument expression parser for helper
  local initializers, without general expression lowering.
- [Bounded bootstrap parameterless-helper expressions](bootstrap-parameterless-helper-expressions.md)
  recommends checked-folding complete expressions in the existing
  parameterless-helper route without changing its call/ABI shape or adding
  runtime expression lowering.
- [Source-bounded parameterless bootstrap helper call chains](bootstrap-parameterless-helper-call-chains.md)
  recommends callee-first parameterless forwarders ending in an existing helper
  leaf, preserving runtime calls and per-frame unwind records; it remains
  unapproved and does not extend general calls or parameterized helpers.
- [Source-sized locals in parameterless bootstrap helper bodies](bootstrap-parameterless-helper-block-locals.md)
  recommends folding a bounded local block into the existing leaf encoding,
  without runtime local lowering or general block execution; it remains
  unapproved.
- [Source-sized bootstrap leaf locals](bootstrap-source-sized-leaf-locals.md)
  recommends source-bounded local parsing in standalone direct leaf bodies
  with three or more locals, without general local lowering.
- [Bootstrap leaf local expressions](bootstrap-leaf-local-expressions.md)
  recommends reusing the bounded call-argument expression evaluator for
  source-sized standalone leaf locals, without runtime expression lowering.
- [Bounded bootstrap direct-Main expressions](bootstrap-direct-main-expressions.md)
  recommends reusing checked expression folding for no-local direct `Main`
  returns, without extending helper bodies, local forms, or runtime lowering.
- [Bounded bootstrap direct-Main local expressions](bootstrap-direct-main-local-expressions.md)
  recommends reusing checked expression folding for the existing one- and
  two-local direct `Main` route, without general local lowering.
- [Bootstrap source file-scoped namespace](bootstrap-source-file-scoped-namespace.md)
  recommends one leading file-scoped namespace for the existing direct-source
  bootstrap subset, without general namespace lookup, linking or metadata.
- [Scalar frontend function token windows](scalar-frontend-function-token-window.md)
  recommends removing the provisional per-function token-count gate while
  preserving bounded parser resources; the accepted 64-token cursor/window
  contract remains unchanged pending approval.
- [Scalar frontend expression-node capacity](scalar-frontend-expression-node-budget.md)
  recommends increasing the provisional per-function expression-tree capacity
  from 32 to 64 nodes; it is a separate unapproved decision from token-window
  support. Provisional `v87`/`v88` layouts implement the larger capacity but do
  not accept the recommendation.
- [Scalar frontend four-parameter projection](scalar-frontend-four-parameter-projection.md)
  records an unapproved bounded complete-program projection recommendation;
  provisional `v88` implementation coverage does not accept its encoding.
- [Scalar frontend aliases in one nested namespace leaf](scalar-frontend-nested-namespace-aliases.md)
  recommends the provisional same-leaf alias scope without parent inheritance;
  general selected lookup is not implemented by this private frontend profile.
- [Scalar frontend namespace block chains](scalar-frontend-namespace-chains.md)
  recommends a bounded single chain of nested blocks with declarations only
  in the terminal leaf; sibling and repeated nested blocks remain unsupported.
- [Scalar frontend cleanup-contained switch transfers](scalar-frontend-cleanup-switch-transfer.md)
  records the unapproved arm-graph termination-proof recommendation and its
  provisional acyclic implementation; cycles and unproved intra-arm iteration
  still reject, without changing accepted switch semantics or relaxing cleanup
  verification.
- [Reference-call metadata](reference-call-metadata.md) links the accepted
  reference, dependency-source, call/output and bounded indexed-Body
  unsafe-region contracts. Its physical dependency profile remains deferred;
  general output migration needs complete implementation and codecs, not new
  source policy approval.
- [Span-backed token cursor](span-backed-token-cursor.md) links the accepted
  bounded compilation-local `TokenCursor` and indexed scalar-parser
  contract; implementation coverage is tracked separately. General
  `Span<T>` support, source-level `nuint`, writable/ref-returning access and
  persisted capture metadata remain unapproved.
- [Forwarded `Span<Token>` results](token-span-result-forwarding.md)
  is deferred until a concrete same-compilation provider/consumer path needs
  returned views; its private execution profile is not admission of the
  selected general dependency-source contract.
- [Persisted private `Span<Token>` values and operations](token-span-semantic-module.md)
  recommends the concrete scalar-frontend private IR slice, provisionally
  implemented in exact revision 77 with explicit type roles and mandatory
  backing/lifetime revalidation. It remains UNAPPROVED; returned views,
  captures/advance and exported/imported span signatures remain excluded.
- [Invocation-scoped source grant](invocation-scoped-source-grant.md)
  is deferred; accepted semantic-module execution remains source-independent
  unless a concrete source-processing tooling API requires this boundary.
- [Native source-grant library](native-source-grant-library.md) is deferred
  until a concrete native source-processing caller exists; no ABI or
  16,384-unit cap is accepted.
- [Sample-driven Windows native FFI](windows-native-ffi.md) accepts only the
  sample-specific staged direction. General fixed/rootless Windows x64
  storage/calls are now selected canonically; the proposed import catalog,
  callback and closed-COM APIs remain unapproved. Exact sample gates do not
  establish general interop support.
- [Persisted scanner invocation profile](scanner-invocation-profile.md) is
  deferred until an integrated compiler path needs it and its full-file
  size/resource policy is defined; revision-76 support remains provisional.
- [Persisted scalar frontend v88 summary invocation profile](scalar-frontend-invocation-profile.md)
  is a distinct UNAPPROVED recommendation for a 12-buffer, complete-v88-only
  fresh-source module path bounded to 64 scanner tokens including EOF and a
  recommended 32-function projection capacity.
  Revision 77 provisionally executes complete publication and summary decoding
  against fresh grants using the separate unapproved private-span slice. A
  separately fingerprinted provisional implementation profile supports up to
  16,385 source-bounded tokens including EOF; neither profile accepts the
  recommendation or changes the accepted cursor/window contract.
  `scanner-10` remains unchanged.
- [Registered UTF-8 string-literal target](registered-string-target-binding.md)
  is deferred; its source-reading semantic-module-JIT route is not the
  intended compile-time literal model and does not define general handlers.
- [Checked-exception TRRM](checked-exception-trrm.md) records the accepted
  TRRM 1.0 GA requirement for core checked-exception semantics;
  exceptional-edge encoding and conformance details remain open.
- [Output parameters in executable TRRM](trrm-output-parameters.md) records
  the accepted TRRM 1.0 GA requirement for executable core output-mode
  semantics; its profile, encoding, place, publication and exceptional-state
  details remain unapproved.
- [Optional scalar TRRM execution](optional-scalar-trrm-execution.md) records
  an implementation-only boundary under the approved Body contract; optional
  source types, call signatures and native ABI support remain out of scope.
- [Static initialization metadata and attribute schema](static-initialization-metadata.md)
  links the accepted trigger and typed owning-group access requirements and
  restricted implementation-only first gate. Static fields have a distinct
  logical identity; attribute registration/multiplicity, record/operation
  encoding and runtime ABI remain open.
- [First public fixed-layout implementation slice](public-fixed-layout-profile.md)
  links the accepted bounded Windows x64 implementation direction for the
  existing target-specific `Blittable` plus `Sequential` contract, with
  native ABI passing and no separate profile identity or 16-byte cap. Core
  primitives and transitively eligible `Copy` fields are accepted;
  source schemas are selected; their compiler adoption, physical metadata and
  source-version/tooling selector remain pending.
- [Compare-exchange order pairs](atomic-cas-order-pairs.md) records the
  accepted finite compile-time-valid table for CAS success/failure memory
  orders in the memory model; source spelling, diagnostics and support remain
  open. It does not add atomic support or change the accepted
  bitwise-comparison rule.
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
