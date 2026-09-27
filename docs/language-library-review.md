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
qualified `mut`/`write` rules, ordinary-reference semantics for
`implicit ref<T>`, and memory ordering/publication policy are not awaiting
approval. Their exact syntax, ABI, formalization or target support may still
be work.

## Open policy choices

| Topic | Remaining choice | Established boundary |
|---|---|---|
| Root type and array projections | Whether Terrum has a universal root type at all, and which explicit readonly projections, if any, arrays expose. Writable storage remains invariant; any projection must preserve representation, validity, backing lifetime and cost. | [Language types](language.md#types-and-operations); [view and tensor APIs](libraries.md#views-shapes-and-tensor-primitives) |
| External conformance ownership | Who may declare a witness for an imported type, and how ownership, conflict detection and versioning work across separately compiled packages. Nominal checked conformance remains the rule; only ownership is open. | [Interfaces and generic properties](language.md#interfaces-access-and-generic-properties); [roadmap](roadmap.md) |
| Type-family participation and projection | Which generic families may opt into erased/projection members such as a family-specific `void` form, who declares the mapping, and what participation guarantees are required. There is no universal top/bottom `void` projection and no automatic `Type<T>`-to-`Type<void>` conversion. | [Type families](type-families.md); [SIMD and view APIs](libraries.md#simd-as-a-family-of-shapes) |
| Collection `params` binding | Passing an existing compatible collection and expanding elements are both intended; binding priority for the ambiguous cases remains open. Expansion uses ordinary collection construction, new slots, and normal per-element copy/move. | [Callable and `params` direction](language.md#callable-values-params-and-tuples); [library contract](libraries.md#callable-kernels-and-collection-parameters) |
| Async start and execution context | Whether task-like operations start hot or cold, how captures and responsibility transfer are represented, and what scheduler, affinity or ambient-context policy applies. Completion ownership remains required; cancellation is not completion. | [Async language direction](language.md#storage-concurrency-entry-and-boundaries); [library callable work](libraries.md#callable-kernels-and-collection-parameters); [roadmap](roadmap.md) |
| Numeric conversion policies and lookup | Review the remaining rounding, range, exactness and NaN/infinity rules for proposed floating/general primitive conversions, including saturation and truncation; floating-destination saturation/truncation is explicitly open. The table is a proposed general policy, not an accepted primitive conversion matrix. Preserve the implemented checked range/representation semantics of bounded integer type-call conversions (`int32`, `uint32`, `int64`, `uint64`, `char16`); accepted checked-by-default and use-site variant-selection direction is not reopened. Source/destination witness lookup and user-defined conversion declaration rules also remain open. | [Conversion policies](conversions.md#conversion-policies) |

## Proposals awaiting review

The accepted policies are not reopened here. These entries identify proposals
in the [call/output/contract follow-up](language-contract-review.md) that still
need review; that document is not accepted wholesale.

| Proposal | Review boundary |
|---|---|
| Materialized temporary scope for `implicit ref<T>` | Ordinary readonly-reference equivalence and dependencies are accepted. Decide the ordinary temporary endpoint; the full-expression rule in [§1](language-contract-review.md#1-one-call-and-reference-model) is a recommendation, not a selected implicit-only noescape rule. |
| Conditional postconditions and owning-output state | Review the guarded-postcondition representation, initialized/null state facts, and the policy for possibly initialized owning outputs. These are proposals, not canonical rules; see [§2](language-contract-review.md#2-outcome-initialization-and-nullability-are-separate). |
| Normal-return ownership endpoint and entry snapshots | Review owned-parameter cleanup and explicit entry-state snapshots within the accepted broad body/postcondition/epilogue order. The detailed ownership endpoint still needs approval; see [§3](language-contract-review.md#3-contracts-bracket-semantic-body-completion). |
| Mandatory proof/evaluation profile and budget | Review whether required proofs use a mandatory versioned conservative profile and how deterministic evaluator/proof resources are accounted and bounded. Candidate ceilings are not accepted defaults; see [§4](language-contract-review.md#4-a-reproducible-proof-and-evaluation-foundation). |
| Documentation comments | Decide whether to add a language/tooling contract and, if so, its syntax, structured fields, symbol links and packaging in [§5](language-contract-review.md#5-documentation-comments). |
| ABI declarations and external boundaries | Review exact trivial-wrapper eligibility/declarations, whether any checked overlapping-union subset is allowed, which externally promised representations need an explicit encoding beyond the selected profile, and whether a future public managed boundary uses a stable ABI or versioned profiles/adapters. This does not reopen accepted memory guarantees or imply a current public managed-handle ABI; see [ABI decisions](data-layout-and-abi.md#remaining-decisions-requiring-review). |
| General semantic IL and metadata format | Review the specification structure, normative IL/verifier contract, metadata schema, wire encoding and evolution policy. Current semantic modules remain supported compatibility contracts, not an automatic selection of the final general format; see the [specification roadmap](roadmap.md#language-semantic-il-and-metadata-specification). |

## Specification and implementation work

These items are not requests to reconsider accepted policy. Exact source
grammar, metadata, target profiles and library APIs remain to specify; runtime
support and conformance evidence follow the [roadmap](roadmap.md).

| Area | Work still required | Canonical detail |
|---|---|---|
| Language grammar and compatibility metadata | Specify generic constraints/associated relations, erased interface dispatch, match and closed-case grammar, forwarder attributes/source visibility, construction and annotation spellings, and source-visible compatibility analysis. Keep these separate from the already bounded generic specialization/inference matrix. | [Provisional notation and open decisions](language.md#provisional-source-notation-and-open-decisions); [implementation boundary](implementation.md); [roadmap](roadmap.md) |
| Numeric types, conversions and literals | Specify source/destination witness lookup and user-defined conversion declaration grammar after the remaining policies above are reviewed; separately design custom literal registration and compile-time evaluator APIs. Preserve the implemented bounded integer conversions and distinguish them from the proposed general primitive policies in `conversions.md`. | [Conversion policies](conversions.md#conversion-policies); [literal and constant-evaluation direction](language.md#provisional-source-notation-and-open-decisions); [roadmap](roadmap.md) |
| Data layout and ABI profiles | Fill per-target size/alignment/representation and call-classification tables and implement checks for the accepted opaque-default and opt-in Sequential/Union/Transparent directions. The existing Windows x64 compact-value and module ABI is implemented and remains authoritative; additional target profiles and generalized layout/call-interface rules are not frozen or implemented. Memory guarantees are accepted; target instruction selection is engineering work, not a policy vote. | [Profile specification and implementation](data-layout-and-abi.md#profile-specification-and-implementation-work); [canonical layout direction](language.md#types-and-operations) |
| Libraries and type families | Specify family membership/associated-member syntax, view and tensor shapes, iteration and allocator contracts, collection storage requirements beyond `[Copy]`, collection/text builders, handlers, text representation, equality/key/formatting APIs, and scalar/SIMD/reduction behavior. Do not infer family erasure, capabilities, or vectorization from matching names. | [Open library contract work](libraries.md#open-contract-work); [type families](type-families.md); [roadmap](roadmap.md) |
| Calls, failures and runtime support | Complete the source/metadata and conformance work for `init ref<T>` invocation, exception/low-memory ABI, general overload binding, cleanup and root handling. The existing supported forms remain bounded by [implementation](implementation.md). | [Language notation](language.md#provisional-source-notation-and-open-decisions); [runtime roadmap](roadmap.md) |
| Memory specification and `MemoryPrimitives` | Formalize mixed SC/non-SC and fence relations; finalize exact API signatures and valid order pairs; publish target support/alignment/progress matrices and collector-aware reference protocols before implementing those operations. Lock-backed fallbacks remain explicit and globally coordinated across participants. | [Accepted memory model and remaining obligations](memory-model.md#specification-and-implementation-obligations); [implementation sequence](roadmap.md#memory-model-implementation-sequence) |
