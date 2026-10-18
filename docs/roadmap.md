# Development roadmap

The project is a bounded checked native compiler, not the whole language.
[Language and grammar specifications](specs/grammar-spec.md) define target
contracts; the [implementation reference](implementation.md) records current
support; [proposals](proposals/README.md) collect unapproved choices.
Approval, implementation, and pre-v1 stability are separate.
Keep the grammar normative: source productions, semantic constraints and
explicit scope boundaries, not approval history or implementation snapshots.
Record support and migration details in the implementation reference; use
this roadmap for adoption sequencing and remaining work.

## Roadmap status

This roadmap records pending contracts and dependencies only. Current support
and compatibility belong in the [implementation reference](implementation.md)
and feature-specific documents; unapproved choices remain in proposals.
Commit-level history is tracked by Git rather than duplicated here.

## Planned work (priority order)

The roadmap has four current areas, in area priority order. The sequence below
is priority order across pending work; numbering does not imply that a
prerequisite has already been met.

| Priority | Area | Next contracts and dependencies |
|---|---|---|
| 1 | Full runtime/structured contracts | Priorities 1–3, 9–10 below |
| 2 | Resources/graphs | Priority 4 |
| 3 | General separate compilation | Priorities 5–6 and 11 |
| 4 | Rich libraries/self-hosting | Priorities 7–8 and 12 |

1. **Full runtime/structured contracts — references and safety.** Complete
   callable access, caller-unsafe obligations, reference/loan parameter
   metadata, and producer-owned transfer/share capabilities before widening
   their use across calls and imports. Define how these contracts persist and
   are checked across separately compiled modules, including canonical target
   identity and complete argument mapping. Preserve existing access,
   initialization, place, ownership and reservation checks; do not infer
   lifetime or memory-safety guarantees from `unsafe`. The
   [reference-call metadata proposal](proposals/reference-call-metadata.md)
   records one unapproved schema recommendation. Implemented bounded slices
   are tracked in the [implementation reference](implementation.md).
2. **Full runtime/structured contracts — initialization and outputs.** Complete
   general `out`/`init ref<T>` declaration and call migration with definite
   initialization, cleanup and publication rules. Preserve their distinct
   contracts; this is not a parser-only rename. Carry the
   [static initialization contracts](specs/grammar-spec.md#static-initialization-and-load-scoped-storage)
   through separate metadata/schema work before compiler adoption: loaded
   closed-type groups and lazy units, explicit constructor triggers, checked
   sharing, failure/recovery, initialization effects and load-scope cleanup.
   The accepted first implementation gate is the restricted fail-fast,
   single-module profile in the [implementation reference](implementation.md);
   complete its schemas, metadata and runtime dependencies before adoption.
   Do not infer sharing permission from `static` or adopt source forms without
   their state, lifetime and publication machinery.
   Preserve uniform generic/concrete reference matching and supply checked
   runtime type evidence/generic execution before adopting runtime captures.
   General source catches/filters require incremental post-inner-cleanup
   selection, not reuse of the bounded Windows first-pass discriminator as an
   authored predicate.
3. **Full runtime/structured contracts — conditions and constants.** Implement
   the selected published/static conditions, literal/constant domains and
   portable proof/evaluation floor. Complete their physical effect/program/
   value records and independent-consumer support before claiming general
   execution; additional domains and beyond-floor rules remain separate work.
4. **Resources/graphs.** Extend beyond bounded owners, views and managed graphs
   only after representation, lifetime, cleanup, failure and publication
   rules are defined. Implement the accepted partial-field move/repair and
   retirement contracts through their complete state/cleanup representation.
   General stored-view profiles, arbitrary owning graphs, allocators and
   relocation retain their remaining design and implementation gates.
5. **General separate compilation — layouts.** Complete standard persistence
   and native ABI integration for the accepted public Windows x64 Blittable
   contracts. Natural Sequential and bounded Transparent source support
   already exist; Packing/Alignment controls, Union and broader ABI cases
   remain implementation work. The
   [integration proposal](proposals/public-fixed-layout-profile.md) separates
   the accepted target rules, pending physical carriers and current support.
6. **General separate compilation — binding contracts.** Implement the
   selected source call/inference algorithm, witness/default resolution and
   qualified interface forms; complete their physical identity, dispatch and
   replacement records. Adapter, family and additional declaration/lookup
   choices remain pending rather than adopting convenient host behavior. The staged,
   Windows x64 FFI direction crosses this area and runtime safety: raw
   caller-unsafe imports, general stable storage/pointers, explicit native
   entries/function pointers, generic vtable calls and authored applications.
   The settled [authored-library boundary](proposals/unsafe-ffi-vtable-cleanup.md)
   supersedes compiler catalogs, COM ownership inference and sample-specific
   admission. Review the [standard metadata](proposals/trrm-native-interop-metadata.md)
   as a coherent route through declarations, dependencies, mandatory
   verification and lowering. Completed sample generalization does not
   approve its experimental source or wire schemas.
7. **Rich libraries — builders and handlers.** Implement the selected
   registered builders and handlers, including eager capture before draining,
   ownership, refusal, cleanup, failure and publication. Complete their
   independent physical contracts; additional profiles/APIs require review.
8. **Rich libraries — callables.** Progress in order from fixed-signature
   callables and tuples, to generic scalar/SIMD functors, to contextual generic
   lambdas. Each stage depends on its declared type, effect, access, loan,
   ownership and ABI contracts; it does not imply type packs or polymorphic
   callable erasure.
9. **Full runtime/structured contracts — conversions.** Complete accepted
   numeric-conversion policies with their target contracts and boundary
   behavior. Existing bounded floating implementation does not establish
   support for every accepted format, source form or interchange operation.
10. **Full runtime/structured contracts — asynchronous producers.** Integrate
    accepted completion, producer, capture and suspension contracts after
    their loan, effect, ownership and cleanup prerequisites. Complete physical
    frame/phase records and runtime support; broader awaiter, supervision and
    scheduling APIs remain design work.
11. **General separate compilation — evolution.** Integrate selected
    closed-case/exhaustiveness rules and their physical identities. Complete
    exact-contract forwarder schemas and compatibility analysis without
    silently widening retained contracts.
12. **Rich libraries/self-hosting — bootstrap components.** Grow the current
    bounded components toward a compiler bootstrap only as their language and
    library prerequisites are met and through the shared
    [self-host gate](#compiler-and-self-hosting-gate). Keep the deferred
    [invocation-scoped source grant](proposals/invocation-scoped-source-grant.md),
    [native source-grant ABI](proposals/native-source-grant-library.md) and
    [scanner invocation profile](proposals/scanner-invocation-profile.md)
    separate from accepted execution contracts. Promotion requires concrete
    consumers and complete invocation, lifetime, size/resource and admission
    rules; provisional implementation does not establish those contracts.
    Current component support and the hosted verifier/emitter boundary are
    recorded in the [Terrum-authored component inventory](implementation/terrum-code-analysis.md).

These are pending contracts, not support claims. Exact grammar, metadata and
implementation remain subject to their canonical specifications and proposals.

### Later capabilities excluded from this sequence

Production JIT, additional backends, wholesale source generation/reflection/
interop and scalable SIMD remain outside these four current areas. This does
not exclude the existing bounded JIT proof or the targeted asynchronous
contracts listed above.

Future tooling prerequisites also remain outside the four current areas and
are not implementation claims. Semantic facts must distinguish unknown,
invalid and proven; an empty diagnostic set is not safety proof. Semantic
queries need snapshot, target/options and dependency identity with fact
provenance, and semantic edits must recheck affected contracts and diagnostics
against the resulting compilation before publication. Edits should be
candidate batches with checked preconditions. Generated source needs a stable
document identity, producer/version/input digest and authorization, with
separate spelling and expansion maps that can associate a generated range
with zero, one or several original ranges; diagnostics may carry related
locations, rule arguments and fixes scoped to the same snapshot.

Analyzer and generator APIs should be versioned snapshot consumers, not
callbacks that mutate compiler internals. Pin dependencies and input manifests;
require explicit capabilities, cancellation and resource bounds. Generators
approved by the author may access only declared implementation scope; consumer
analyzers do not inherit that grant. Comments and generated text grant no
execution, filesystem, network or private-member authority. Generation must
use declared deterministic inputs and not depend on undeclared clock,
environment or network state. An in-process interface alone is not a security
boundary. Plugin discovery, query scheduling, remote services and persistent
query caches remain deferred until usage establishes requirements; cache keys
must include compiler/schema versions, target, references, contract-rule
version and authorization context, not just source content.

## Language, semantic IL and metadata specification

Maintain one evolving language specification organized around syntax, types,
static semantics and execution. Distinguish intended rules, implementation
coverage and unresolved proposals; do not version chapters independently.
Breaking changes are expected before v1. Prior alpha formats are not
compatibility targets and should not be preserved merely because they were
implemented; retain only selected versions as comparison/validation fixtures,
and remove or reshape the rest as the current contracts advance. Migration
semantics apply only where the accepted language contract explicitly requires
them. Roadmap progress is toward the approved metadata design's v1.0; do not
version each internal increment. If a preview is shipped, its versioning is an
explicit part of that release decision. No pre-v1 stability promise is made.
Language major/minor versions define language evolution and its compatibility
story. Internal projection `v#` tags identify the current implementation
layout; they are not language versions or implicit compatibility promises.
Compatibility is an explicit design choice with a deliberately scoped version
boundary, never an automatic consequence of retaining an old reader.
Work may proceed against approved grammar while remaining decisions stay in
the proposal queue; using a provisional design for an increment does not
approve other proposals.

TRRM is a compact binary representation of the language and ecosystem: an
input for metadata binding, reflection, tooling, linking and AOT/JIT code
generation without source recompilation. It is not merely private-IR
serialization or a verifier certificate. The approved source grammar and
current `(1, 0)` metadata and coded-instruction designs specify target
contracts, not a complete executable profile, current compiler support or a
released schema. Complete implementation of the selected Body/verifier and
feature-admission rules remains work; higher structured physical profiles
and numerical portable representation-reader minima retain their explicit design gates.
Source compilation checks authored bodies;
binary consumers validate structure, references, binding, compatibility and
caller use, then may rely on declared contracts for external dependencies
without re-proving their bodies. Malformed data is never trusted.

Pre-v1 changes, including breaking format changes, remain possible; no frozen
versioning scheme, published managed-machine ABI, or pre-v1 stability level is
promised. Do not preserve prior formats or require migrations solely for
backward compatibility; keep at most a small selected set for independent
comparison and validation, then retire obsolete shapes. Validate independently
authored readers, writers and verifier cases, not only same-version round
trips. Dynamically composed code must be checked against dependency, compiler
and runtime assumptions before execution; an implementation may adapt,
rebuild, discard or reject it. No JIT fallback or IR retention is required.
Declared FFI exports remain fixed to their target profile.

Keep minimal language conformance separate from the application-library and
runtime surface. Core requirements stay small; richer features retain their
checked dependencies without silent fallback. The library proposal is not an
approved inventory, and no universal runtime, backend or API set is implied.
The source specification, verifier, metadata/module interchange and target
profiles must state their boundaries explicitly. Do not infer implementation
from an approved syntax form or infer approval from an implementation choice.

## Compiler and self-hosting gate

Maintain the hosted C# compiler and Terrum-authored compiler in parallel
through at least 1.0 GA; the first self-build does not replace the host.
Compare both on the same source, dependencies, options, runtime policy and
target profile: lexical/diagnostic facts, canonical semantic artifacts, then
native output for their shared subset. The gate is a hosted build, a rebuild
with that compiler, and a further rebuild plus native execution against shared
fixtures. Require byte equality only for output whose ordering, layout and
tie-breaking are specified; explain other differences rather than normalizing
them away. Current scanner/library components are precursors, not evidence that
the gate passes. Compilation-local cursor support does not establish general
stored views or an independently verified/emitted frontend; those boundaries
remain in the [implementation reference](implementation.md).

Do not translate every design sample at once or build a plugin loader first.
Extend the verified executable corpus and semantic checks in coherent slices;
keep diagnostics and source mappings useful on incomplete input. Preserve
checked managed access, unique-owner mutation, field-view dependencies and
natural buffer-element alignment when widening the implemented subset.
General `out` to `init ref<T>` migration remains scheduled. The existing
conditional-init subset is not general reference support. Bounded static
exports do not imply `func`, general `fnptr`, tuples, callable witnesses or
contextual lambdas. Fixed-signature callables precede generic scalar/SIMD
functors, which precede contextual generic lambdas; type packs, polymorphic
erasure and arbitrary callback vectorization are excluded from these stages.

## Memory-model implementation sequence

The [memory model](specs/memory-model.md) is accepted design, not implemented
concurrency support. Integrate it through the four roadmap areas above, while
preserving existing single-threaded behavior.

| Order | Increment and prerequisite | Required evidence |
|---|---|---|
| 1 | Preserve observable evaluation, checked arithmetic, cleanup, ordinary access widths and current unsafe-recovery behavior. | Source/IR/AOT/JIT regressions show optimization adds no invalid access or observable fault/effect. Unsafe discovery must not invent repair or trap behavior; caller-unsafe declarations and explicit scopes express call-site obligations, not memory-safety proofs or complete reference/capability metadata. |
| 2 | Implement caller obligations for `unsafe` callable declarations, explicit unsafe-block requirements, and producer-owned transfer/share capabilities with reference/loan/parameter metadata and separate compilation. Ordinary safe declarations may contain checked internal unsafe blocks; no trusted-implementation marker is required. | Wrappers/imports cannot erase obligations; unsafe operations remain scoped; generic bodies use published constraints; private layout/JIT facts do not change source binding; incompatible capability/safety changes reject without freezing opaque layouts. |
| 3 | Add bounded `MemoryPrimitives` value access and scalar atomics after typed access paths, representation eligibility and compile-time order binding; begin with supported Windows x64 profiles. | Check atomicity, ordering and progress separately; define bitwise CAS comparison; checked overflow leaves storage unchanged; unaligned access is not a snapshot; prove target capability and AOT/JIT/module parity; no hidden lock fallback. |
| 4 | Build checked handoff, immutable publication and scoped synchronization on those primitives within resources/graphs and rich libraries; allow an explicitly selected coordinated fallback where feasible. | Test publication and mixed-order litmus cases; enforce guards, lifetimes and aliases; justify lock-free and wait-free claims separately; share fallback protocols across modules; preserve effects while allowing valid read elision. |
| 5 | Extend reference atomics only after collector-aware roots, barriers, safepoints and reclamation are defined. Add explicit SIMD, non-temporal and cache operations only with layout, feature and memory-domain contracts. | Establish concurrent root safety before reference APIs; preserve ranges and per-location guarantees through SIMD; complete non-temporal stores before publication; do not confuse cache maintenance with DMA coherence or persistence. |

Before claiming concurrent support, qualify the accepted mixed-order/fence
rules and concurrent-execution cases. Preserve the explicit acquire-release
relay rule, not implicit relaxed-RMW release sequences, and implement the
selected CAS success/failure order matrix. Exact primitive API spellings,
physical conditional-capability/operation records and target instruction
recipes remain separate work. This sequence does not authorize new backends,
public managed-handle ABI, or wholesale adoption of design-only libraries.

Represent access, ordering and lifetime effects in checked IR before lowering.
Persist and validate those contracts across semantic imports, native producers,
module sets and graphs, with feature-based version selection and rejection
under incompatible headers. Existing ordinary IR and lexical unsafe recovery
do not already encode those guarantees.
