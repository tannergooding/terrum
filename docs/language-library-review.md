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
shared-memory publication policy are not awaiting approval. The contract order is
also accepted: prologue, preconditions, method body, postconditions, epilogue;
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
| Output publication and postcondition failure | Specify exact output/result publication and candidate cleanup when a postcondition fails. The body-cleanup endpoint is accepted: local and untransferred owned-parameter cleanup precedes postconditions, and consumed/moved values are unavailable through their former source; see [§3](language-contract-review.md#3-contracts-bracket-semantic-body-completion). Entry snapshots and arbitrary history are deliberately deferred with no promise to add them. |
| Documentation comments | Review the exact delimiter/attachment rules, friendly markup dialect and supported XML/HTML-style tags/normalization, XML schema/member IDs, inheritance, diagnostics, and packaging under the accepted source-markup-to-separate-XML-artifact direction. Common symbol/parameter/type references must resolve into structured references; normal extraction does not execute embedded code or fetch remote includes. See [§5](language-contract-review.md#5-documentation-comments). |
| Remaining ABI policy choices | The layout policy is accepted: opaque private `Auto`; declaration-order `Sequential`; offset-zero, unsafe-overlap `Union`; one-field carrier `Transparent`; and target-specific fixed representation under `Blittable`. Public field access requires `Blittable`; published language-level signatures and layout/access/ownership/effect contracts are binary compatibility commitments even without foreign interoperability. Transparent combines its representation and carrier argument/result contract, but does not freeze arbitrary layout; only `Blittable` freezes per-profile size/alignment/offsets. Preserve the current ABI and explicit migration requirement. The only remaining policy choice here is whether raw `Union` additionally requires `Blittable`; safe tagged/sum types are a separate open feature, not an overlapping-access subset. No frozen managed-machine ABI is promised; runtime validation/adaptation/recompilation or rejection for precompiled code is implementation work, not a stable-ABI-versus-no-composition policy choice. External wire/callback encodings are specified per concrete protocol, not as a global boolean/reference/SIMD format. See [remaining ABI decisions](data-layout-and-abi.md#remaining-decisions-requiring-review). |
| General semantic IL and metadata format | Review the specification structure, normative IL/verifier contract, metadata schema, wire encoding and evolution policy. Current semantic modules remain supported compatibility contracts, not an automatic selection of the final general format; see the [specification roadmap](roadmap.md#language-semantic-il-and-metadata-specification). |

## Specification and implementation work

These items are not requests to reconsider accepted policy. Exact source
grammar, metadata, target profiles and library APIs remain to specify; runtime
support and conformance evidence follow the [roadmap](roadmap.md).

| Area | Work still required | Canonical detail |
|---|---|---|
| Language grammar and compatibility metadata | Specify generic constraints/associated relations, erased interface dispatch, match and closed-case grammar, forwarder attributes/source visibility, construction and annotation spellings, and source-visible compatibility analysis. Keep these separate from the already bounded generic specialization/inference matrix. | [Provisional notation and open decisions](language.md#provisional-source-notation-and-open-decisions); [implementation boundary](implementation.md); [roadmap](roadmap.md) |
| Numeric types, conversions and literals | Specify source/destination witness lookup and user-defined conversion declaration grammar after the remaining policies above are reviewed; separately design custom literal registration and compile-time evaluator APIs. Preserve the implemented bounded integer conversions and distinguish them from the proposed general primitive policies in `conversions.md`. | [Conversion policies](conversions.md#conversion-policies); [literal and constant-evaluation direction](language.md#provisional-source-notation-and-open-decisions); [roadmap](roadmap.md) |
| Data layout and ABI profiles | Specify and implement accepted Auto/private, Sequential, unsafe-overlap Union, Transparent, Blittable, packing and aggregate-alignment contracts. Formalize exact source spellings, target limits, metadata, transitive Blittable eligibility, boundary coupling/invalidation, and safe access to under-aligned fields; add layout, padding, stride, and compatibility conformance cases. Public field access requires Blittable; published language-level signatures and layout/access/ownership/effect contracts are binary compatibility commitments without necessarily being foreign ABI promises. Specify serialization/callback encodings only for concrete protocols, not a global bool/reference/SIMD wire format. Ordinary struct shape/size does not promise foreign interoperability. Transparent specifically combines its checked carrier representation with carrier-equivalent argument/result classification; atomicity, `Copy`, validity, ownership and sharing remain distinct. The existing Windows x64 compact-value and module ABI remains authoritative; future checked opt-in enforcement requires an explicit compatibility migration, not retroactive rejection or reinterpretation of retained contracts. Additional target profiles and generalized layout/call-interface rules are not implemented. Memory guarantees are accepted; target instruction selection is engineering work, not a policy vote. | [Profile specification and implementation](data-layout-and-abi.md#profile-specification-and-implementation-work); [canonical layout direction](language.md#types-and-operations) |
| Libraries and type families | Specify family membership/associated-member syntax, view and tensor shapes, iteration and allocator contracts, collection storage requirements beyond `[Copy]`, collection/text builders, handlers, text representation, equality/key/formatting APIs, and scalar/SIMD/reduction behavior. Do not infer family erasure, capabilities, or vectorization from matching names. | [Open library contract work](libraries.md#open-contract-work); [type families](type-families.md); [roadmap](roadmap.md) |
| Calls, failures and runtime support | Specify exact postcondition guard/pattern grammar and metadata for correlated output initialization facts; complete source/metadata and conformance work for `init ref<T>` invocation, exception/low-memory ABI, general overload binding, cleanup and root handling. Preserve the current bounded bool/out behavior until coordinated migration. | [Language notation](language.md#provisional-source-notation-and-open-decisions); [call/output contract](language-contract-review.md#2-outcome-initialization-and-nullability-are-separate); [runtime roadmap](roadmap.md) |
| Static-proof floor and constant evaluation | Specify the versioned minimum proof rules/complexity and guaranteed resource floor; exact rule catalog, accounting and numeric floor are open. Distinguish disproved from not established, and permit sound beyond-floor extensions without identical acceptance. Specify constant-evaluator operations/accounting/limits separately while preserving exact target-semantic results and no runtime fallback. Document the author-selected ordinary alternative only where runtime values/checks are legal; never silently demote, and never weaken compile-time-only layout/type/metadata or structural safety requirements. Do not infer a wire encoding or certificate format. | [Proof/evaluation contract](language-contract-review.md#4-a-reproducible-proof-and-evaluation-foundation); [roadmap](roadmap.md) |
| Memory specification and `MemoryPrimitives` | Formalize mixed SC/non-SC and fence relations; finalize exact API signatures and valid order pairs; publish target support/alignment/progress matrices and collector-aware reference protocols before implementing those operations. Lock-backed fallbacks remain explicit and globally coordinated across participants. | [Accepted memory model and remaining obligations](memory-model.md#specification-and-implementation-obligations); [implementation sequence](roadmap.md#memory-model-implementation-sequence) |
