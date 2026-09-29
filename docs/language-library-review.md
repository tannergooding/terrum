# Pending language and library decisions

This document tracks only unresolved policy choices and design deliverables.
Accepted language contracts live in [language](language.md),
[lifetimes](lifetimes.md), [conversions](conversions.md),
[libraries](libraries.md), [data layout and ABI](data-layout-and-abi.md), and
the [memory model](memory-model.md). Executable support is recorded in
[implementation](implementation.md); sequencing belongs in the
[roadmap](roadmap.md). Nothing here authorizes implementation or reopens an
accepted contract. Settled memory, access, and qualified-type/capability
policies are intentionally absent below even where implementation remains
scheduled.
In particular, the accepted `[ThreadTransferable]`/`[ThreadShareable]` names,
qualified `mut`/`write` rules, ordinary-reference semantics and enclosing
lexical-scope materialization for `implicit ref<T>`, conditional output state
semantics and postcondition authoring direction, and memory ordering and
shared-memory publication policy are not awaiting approval. The async execution
direction is also accepted: eager inline start through actual suspension,
completed-await inline continuation, no implicit caller-executor capture or
mandatory worker hop for incomplete awaits, declaration-scoped affinity, and
explicit scheduling of initial invocation separately from continuation
placement. `Run` does not bind helper continuations; `ResumeOn` controls the
awaiting continuation. The contract order is also
accepted: prologue, preconditions, method body, postconditions, epilogue;
the body includes initialization of local and output values, authored
execution/`finally`, local cleanup and untransferred owned-parameter cleanup
before postconditions.
Preconditions and postconditions are declaration contracts outside the body.
Language-versioned output defaults are also accepted: boolean-success outputs
initialize on `true` and remain uninitialized on `false`, while outputs with
other result types initialize on every normal return unless an explicit
declaration postcondition says otherwise. Exact syntax, ABI, formalization or
target support may still be work. Entry snapshots and arbitrary state history
are deliberately deferred from the initial contract model, with no promise to
add them later; narrow explicit capture may be reconsidered only if concrete
APIs justify its costs. Static-proof policy is also accepted: a versioned
minimum rule/complexity and resource floor is mandatory, while sound
beyond-floor analysis and larger budgets may vary by compiler and accept
additional programs.

## Proposals awaiting review

The accepted policies are not reopened here. These entries identify proposals
in the [call/output/contract follow-up](language-contract-review.md) that still
need review; that document is not accepted wholesale. Exact source grammar,
semantic IL/metadata design and core-library/runtime inventories are separate
technical reviews; accepting their high-level direction does not adopt any
detailed proposal wholesale.

| Proposal | Review boundary |
|---|---|
| Coded instructions and remaining `TRRM` contracts | The [metadata design](il-metadata.md) is approved as current intent, not implemented or released. Review the separate [coded-instruction proposal](proposals/terrum-il-instruction-set.md), including its [compact-profile decisions](proposals/terrum-il-instruction-set.md#compact-profile-review-decisions), generic/witness and callable contracts, numerical budgets, feature negotiation and evolution rules before claiming complete interchange support. The current semantic-module revisions and historical-reader tests are implementation facts scheduled for consolidation before v1, not external production compatibility; see the [specification roadmap](roadmap.md#language-semantic-il-and-metadata-specification). |

## Specification and implementation work

These items are not requests to reconsider accepted policy. Exact source
grammar, remaining metadata contracts, target profiles and library APIs remain
to specify; runtime
support and conformance evidence follow the [roadmap](roadmap.md).

| Area | Work still required | Canonical detail |
|---|---|---|
| Calls and `params` grammar | Formalize syntax, diagnostics and generic collection-expression inference for the accepted overload rules: applicability first; then per-argument identity versus permitted qualification weakening; candidates compare by dominance, with crossed matches ambiguous. Apply declared specificity, by-value/`implicit ref<T>` and `params` priorities; no summed scores, return-type/declaration-order/literal-fit tie-breakers, or conversion-chain search. Preserve ordinary collection binding and the `List<T>`-over-`T` result for `List<int32>`. | [Callable and `params` direction](language.md#callable-values-params-and-tuples); [library contract](libraries.md#callable-kernels-and-collection-parameters) |
| Language grammar and compatibility metadata | Specify generic constraints/associated relations and their exact named-member/equality grammar, erased interface dispatch, match and closed-case grammar, forwarder attributes/source visibility, construction and annotation spellings, accepted caller-unsafe declaration/call rules, getter-local effects, unary operator protocol mapping, remaining context-specific discard forms, and source-visible compatibility analysis. The standalone `_ = expression;` form is implemented in syntax-20/scanner-5/schema-5; wildcard patterns, lambda placeholders and generic type holes remain unsupported. Keep these separate from the bounded generic specialization/inference matrix. | [Language notation](language.md#provisional-source-notation-and-open-decisions); [implementation boundary](implementation.md); [roadmap](roadmap.md) |
| Numeric types, conversions and literals | Preserve the accepted integer-to-integer checked/wrapping direction and IEEE floating conversion rules, including unchecked NaN-to-integer conversion to zero. Specify source/destination witness lookup, user-defined conversion declarations, and exact source/API spellings. Separately design custom literal registration and compile-time evaluator APIs. Preserve the implemented bounded checked integer conversions; floating support remains target-only. | [Conversion policies](conversions.md#conversion-policies); [literal and constant-evaluation direction](language.md#provisional-source-notation-and-open-decisions); [roadmap](roadmap.md) |
| Data layout and ABI profiles | Specify and implement accepted Auto/private, Sequential, unsafe-overlap `Blittable` Union, Transparent, Blittable, packing and aggregate-alignment contracts. Formalize exact source spellings, target limits, metadata, transitive Blittable eligibility, boundary coupling/invalidation, and safe access to under-aligned fields; add layout, padding, stride, and compatibility conformance cases. Public field access requires Blittable; published language-level signatures and layout/access/ownership/effect contracts are binary compatibility commitments without necessarily being foreign ABI promises. Specify serialization/callback encodings only for concrete protocols, not a global bool/reference/SIMD wire format. Ordinary struct shape/size does not promise foreign interoperability. Transparent specifically combines its checked carrier representation with carrier-equivalent argument/result classification; atomicity, `Copy`, validity, ownership and sharing remain distinct. The existing Windows x64 compact-value and module ABI remains authoritative; future checked opt-in enforcement requires an explicit compatibility migration, not retroactive rejection or reinterpretation of retained contracts. Additional target profiles and generalized layout/call-interface rules are not implemented. Memory guarantees are accepted; target instruction selection is engineering work, not a policy vote. | [Profile specification and implementation](data-layout-and-abi.md#profile-specification-and-implementation-work); [canonical layout direction](language.md#types-and-operations) |
| Adapters, families and readonly projections | Specify adapter intent/forwarding, witness identity/conflicts and dispatch metadata for explicit extension-interface types. Formalize explicit family opt-in with declared associated projections/constraints; preserve already declared examples such as `TextSpan<void>` without inferring universal `Type<T>`-to-`Type<void>` erasure. Specify eligible one-way adapter projections and explicit readonly array views only where validity, representation, access, ownership, backing/lifetime and cost are preserved. Storage remains invariant; do not infer generic writable covariance or retroactive global conformance. No universal root is introduced merely for equality/hash/formatting; use operation-specific witnesses/interfaces and explicit adapters. | [Interfaces and generic properties](language.md#interfaces-access-and-generic-properties); [type families](type-families.md); [views and shapes](libraries.md#views-shapes-and-tensor-primitives) |
| Libraries and type families | Specify associated-member syntax, view and tensor shapes, iteration and allocator contracts, collection storage requirements beyond `[Copy]`, collection/text builders, handlers, text representation, equality/key/formatting APIs, and scalar/SIMD/reduction behavior. No universal root is introduced merely to provide equality/hash/formatting; use operation-specific witnesses/interfaces and explicit adapters. Do not infer family erasure, capabilities, or vectorization from matching names. | [Open library contract work](libraries.md#open-contract-work); [type families](type-families.md); [roadmap](roadmap.md) |
| Async APIs and runtime | Specify exact `await` integration, task/future and ownership APIs, executor/affinity annotations, `Run`, continuation-placement and scheduling-boundary forms, operation/capture lifetimes, and error/cancellation representation; implement the accepted eager-inline/no-ambient-capture execution model without imposing a thread pool or weakening transfer/share/lifetime/affinity checks. | [Async execution contract](language.md#storage-concurrency-entry-and-boundaries); [library designs](libraries.md#open-contract-work); [roadmap](roadmap.md) |
| Core/application library boundary | Specify the exact language-minimum/API mapping, standard application-library/runtime surface and feature-specific dependencies; do not make the full library inventory or a universal runtime service mandatory for language conformance. | [Library dependency layers](libraries.md#dependency-layers-and-package-boundaries); [language direction](language.md#accepted-direction); [roadmap](roadmap.md#language-semantic-il-and-metadata-specification) |
| Documentation comments | Formalize accepted `///`/`/** ... */` attachment, GFM prose, implicit summary and structured top-level heading direction, declaration-scoped `@` mentions, Markdown navigation and explicit custom `xref:` links. Specify tokenization/escaping, complex generic/overload reference grammar, XML schema/member IDs, inheritance, diagnostics, packaging and deterministic output; ordinary extraction does not execute examples or fetch remote includes. | [Documentation-comment contract](language-contract-review.md#5-documentation-comments) |
| Calls, failures and runtime support | Specify exact `ensure <expr>` clause placement/grammar and metadata for correlated output initialization facts; implement the accepted `Create(init value)` call-site form for `init ref<T>` with coordinated source/metadata migration; complete exception/low-memory ABI, general overload binding, cleanup and root handling. Preserve current bounded bool/out behavior until migration. Postconditions execute at their declared phase unless legally proven redundant without losing observable effects; failure follows ordinary unwinding, with no transactional rollback or rerun of completed `finally` blocks. | [Language notation](language.md#provisional-source-notation-and-open-decisions); [call/output contract](language-contract-review.md#2-outcome-initialization-and-nullability-are-separate); [runtime roadmap](roadmap.md) |
| Static-proof floor and constant evaluation | Specify the versioned minimum proof rules/complexity and guaranteed resource floor; exact rule catalog, accounting and numeric floor are open. Distinguish disproved from not established, and permit sound beyond-floor extensions without identical acceptance. Specify constant-evaluator operations/accounting/limits separately while preserving exact target-semantic results and no runtime fallback. Document the author-selected ordinary alternative only where runtime values/checks are legal; never silently demote, and never weaken compile-time-only layout/type/metadata or structural safety requirements. Do not infer a wire encoding or certificate format. | [Proof/evaluation contract](language-contract-review.md#4-a-reproducible-proof-and-evaluation-foundation); [roadmap](roadmap.md) |
| Memory specification and `MemoryPrimitives` | Formalize mixed SC/non-SC and fence relations; finalize exact API signatures and valid order pairs; publish target support/alignment/progress matrices and collector-aware reference protocols before implementing those operations. Lock-backed fallbacks remain explicit and globally coordinated across participants. | [Accepted memory model and remaining obligations](memory-model.md#specification-and-implementation-obligations); [implementation sequence](roadmap.md#memory-model-implementation-sequence) |
