# Development roadmap

The project is a bounded checked native compiler, not the whole language.
[Language and grammar specifications](grammar-spec.md) define target
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

1. **Full runtime/structured contracts — references and safety.** General
   reference/access/loan metadata and caller-unsafe member contracts still
   need definition before wider use across calls or imports. Structured
   semantic-module revision 70 provisionally carries top-level caller-unsafe
   obligations, narrow readonly-instance and static `Copy` method slices,
   recursively exportable by-value Copy signatures, and readonly instance and
   storage-free static getters with recursively exportable `Copy` results,
   plus caller-unsafe public instance fields and existing mutable receiver
   loans through eligible public `Copy` struct paths, all through explicit
   unsafe-block scopes. Revision 67 added top-level and eligible static
   scalar/`Copy` `unsafe(expr)` call scopes; revision 68 adds eligible
   caller-unsafe field-value reads to those expression extents while
   excluding updates and loans. Revision 69 also provisionally carries
   caller-unsafe readonly instance and storage-free static getter calls in
   `unsafe(expr)` for eligible public `Copy` owners, with recursively
   exportable `Copy` results. It preserves canonical getter identity and
   permits producer qualification only during linking; mutation, loans, TRRM
   and scalar-frontend support remain excluded. Revision 70 also provisionally
   persists eligible caller-unsafe readonly instance-method calls in
   `unsafe(expr)`, validating the owner-relative language identifier under
   Unicode-XID plus the underscore exception and
   allowing the exact shared-owner producer qualification only during
   linking. Revision 70 did not support mutable or loan receivers,
   constructors, operators or reference/output modes. Revision 71 provisionally
   carries a mutable field-receiver loan only as source/formal argument zero
   for an ordinary synchronous mutable instance-method call wholly inside
   `unsafe(expr)`. At least one caller-unsafe field edge, recursively public
   non-unique `Copy` layouts and all existing access, initialization, place, ownership
   and reservation checks remain mandatory; wrapped/reordered loans,
   caller-unsafe mutable methods, escaping references, TRRM and scalar
   frontend support remain excluded from revision 71. This uses existing
   call/loan representations and does not approve the broader reference
   metadata proposal. Revision 72 provisionally extends the expression scope
   to caller-unsafe mutable receiver methods, using the same direct field-loan
   shape and requiring the complete call to remain in `unsafe(expr)`. The
   writer and reader validate the complete argument mapping and `MutReceiver`
   body contract; mutable receiver imports and direct exports remain
   unsupported. This bounded implementation adds no ABI or authorization bit
   and does not approve the broader reference-call metadata proposal.
   Revision 73 provisionally extends `unsafe(expr)` persistence to eligible
   caller-unsafe static binary nominal operators on recursively exportable
   public `Copy` owners. The full call must be enclosed, use two exact
   by-value owner operands and canonical positional mapping, and satisfy the
   existing owner-result or comparison-`bool` signature contract. Revision 74
   provisionally adds recognized unary plus, negation, logical-not and
   ones-complement calls with an exact by-value owner operand and an existing
   unary-contract result; caller-unsafe nominal updates remain outside
   expression-scope support. Existing unsafe-block behavior is unchanged.
   These slices add no ABI, TRRM or scalar-frontend support and do not approve
   the broader proposal.
   Revision 64 also provisionally
   persists identity for supported static operators on public `Copy` structs,
   preserving unsafe effects and nominal-update target checks. These bounded
   implementations do not settle the general schema or approve the
   [reference-call metadata proposal](proposals/reference-call-metadata.md),
   which recommends a narrow first dependency registry and reuse of the
   assigned caller-unsafe effect bit. The proposal recommends a structurally
   validated indexed-Body region for explicit caller-unsafe scopes. A bounded
   kind-`0x06` implementation now admits indexed calls provisionally, while
   compact-linear calls and calls without validated unsafe scope remain
   unsupported; the broader proposal remains unapproved. The `unsafe(expr)`
   getter slice is implemented provisionally but remains unapproved along
   with the broader metadata proposal.
2. **Full runtime/structured contracts — initialization and outputs.** Complete
   general `out`/`init ref<T>` declaration and call migration with definite
   initialization, cleanup and publication rules. Preserve their distinct
   contracts; this is not a parser-only rename. Carry the
   [static initialization contracts](grammar-spec.md#static-initialization-and-load-scoped-storage)
   through separate metadata/schema work before compiler adoption: loaded
   closed-type groups and lazy units, explicit constructor triggers, checked
   sharing, failure/recovery, initialization effects and load-scope cleanup.
   Do not infer sharing permission from `static` or adopt source forms without
   their state, lifetime and publication machinery.
   Preserve uniform generic/concrete reference matching and supply checked
   runtime type evidence/generic execution before adopting runtime captures.
   General source catches/filters require incremental post-inner-cleanup
   selection, not reuse of the bounded Windows first-pass discriminator as an
   authored predicate.
3. **Full runtime/structured contracts — conditions and constants.** Specify
   published/static conditions and required literal/constant domains, including
   proof/effect limits, before claiming general proof execution.
4. **Resources/graphs.** Extend beyond bounded owners, views and managed graphs
   only after representation, lifetime, cleanup, failure and publication
   rules are defined. General stored views, partial field moves, arbitrary
   owning graphs, allocators and relocation remain pending.
5. **General separate compilation — layouts.** Specify fixed public
   `Blittable` layouts, packing and over-alignment with target limits,
   metadata, access lowering and compatibility checks. Exact source spellings
   and implementation are not settled by this plan.
6. **General separate compilation — binding contracts.** Complete overload
   and witness selection plus adapter, family and explicit-interface
   contracts. Keep unresolved declaration, lookup and compatibility choices
   pending rather than adopting convenient host behavior.
7. **Rich libraries — builders and handlers.** Specify registered builders and
   handlers with checked eager capture before draining, ownership, refusal,
   cleanup, failure and publication behavior.
8. **Rich libraries — callables.** Progress in order from fixed-signature
   callables and tuples, to generic scalar/SIMD functors, to contextual generic
   lambdas. Each stage depends on its declared type, effect, access, loan,
   ownership and ABI contracts; it does not imply type packs or polymorphic
   callable erasure.
9. **Full runtime/structured contracts — conversions.** Implement accepted
   numeric-conversion policies, including floating support, only with their
   target contracts and boundary behavior. This is not current floating-point
   compiler support.
10. **Full runtime/structured contracts — asynchronous producers.** Specify
    completion, producer, capture and suspension contracts after their loan,
    effect, ownership and cleanup prerequisites. This targeted work is not an
    async-runtime support claim.
11. **General separate compilation — evolution.** Add evolvable-case
    contracts, exact-contract forwarders and compatibility analysis without
    silently widening existing contracts.
12. **Rich libraries/self-hosting — bootstrap components.** Grow the current
    bounded components toward a compiler bootstrap only as their language and
    library prerequisites are met and through the shared
    [self-host gate](#compiler-and-self-hosting-gate). A bounded semantic-module
    JIT path is provisionally implemented under the still-unapproved
    [invocation-scoped source-grant proposal](proposals/invocation-scoped-source-grant.md);
    the maintained Windows x64 build now also has an explicit opt-in,
    provisional source-grant companion for the checked UTF-16 scalar adapter,
    scalar source-text-length, source-code-unit, source-offset and
    source-position queries, scalar source-line-count and source-line
    content/including-break end queries, a scalar source-range validity
    predicate, packed scalar string- and character-literal queries, a scalar
    character-literal value query, scalar string-literal
    validation/count/indexed-value/encoded-length queries and paired scalar
    integer-parsing queries and target-typed hexadecimal-float raw-bit
    and based-integer floating-bit queries, with selected-export
    descriptors bound to the image hash and end-relative
    reads based on invocation length. The source-free
    compiler-primitives DLL remains unchanged; this does not promote the
    unapproved ABI or establish general source-dependent DLL support. The
    scanner and scalar frontend still rely on hosted verification/emission. A separate
    [native source-grant library proposal](proposals/native-source-grant-library.md)
    records the candidate per-call ABI and remaining promotion decisions; it
    remains unapproved.
    A scanner-specific `scanner-10` persisted semantic-module JIT profile is
    implemented provisionally under the still-unapproved
    [scanner invocation-profile proposal](proposals/scanner-invocation-profile.md).
    It does not expand the source-only grant contract or establish support for
    graphs, module sets, native DLLs or persisted AOT.

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
released schema. The complete Body/verifier contract, feature negotiation and
resource budgets remain work. Source compilation checks authored bodies;
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
the gate passes. The hosted compiler supports a compilation-local readonly
`TokenCursor` over a validated `TokenSnapshot`, and the Terrum-authored scalar
frontend now consumes that snapshot through bounded indexed parser windows.
This does not add general stored views or move the frontend's independently
audited result beyond the hosted verifier/emitter boundary.

Do not translate every design sample at once or build a plugin loader first.
Extend the verified executable corpus and semantic checks in coherent slices;
keep diagnostics and source mappings useful on incomplete input. Bounded
managed access, unique-owner mutation and field-view contracts are implemented;
the bounded owned-`int32` buffer/view path preserves natural element alignment.
General `out` to `init ref<T>` migration remains scheduled. The existing
conditional-init subset is not general reference support. Bounded static
exports do not imply `func`, general `fnptr`, tuples, callable witnesses or
contextual lambdas. Fixed-signature callables precede generic scalar/SIMD
functors, which precede contextual generic lambdas; type packs, polymorphic
erasure and arbitrary callback vectorization are excluded from these stages.

## Memory-model implementation sequence

The [memory model](memory-model.md) is accepted design, not implemented
concurrency support. Integrate it through the four roadmap areas above, while
preserving existing single-threaded behavior.

| Order | Increment and prerequisite | Required evidence |
|---|---|---|
| 1 | Preserve observable evaluation, checked arithmetic, cleanup, ordinary access widths and current unsafe-recovery behavior. | Source/IR/AOT/JIT regressions show optimization adds no invalid access or observable fault/effect. Unsafe discovery must not invent repair or trap behavior; caller-unsafe declarations and explicit scopes express call-site obligations, not memory-safety proofs or persisted metadata. |
| 2 | Implement caller obligations for `unsafe` callable declarations, explicit unsafe-block requirements, and producer-owned transfer/share capabilities with reference/loan/parameter metadata and separate compilation. Ordinary safe declarations may contain checked internal unsafe blocks; no trusted-implementation marker is required. | Wrappers/imports cannot erase obligations; unsafe operations remain scoped; generic bodies use published constraints; private layout/JIT facts do not change source binding; incompatible capability/safety changes reject without freezing opaque layouts. |
| 3 | Add bounded `MemoryPrimitives` value access and scalar atomics after typed access paths, representation eligibility and compile-time order binding; begin with supported Windows x64 profiles. | Check atomicity, ordering and progress separately; define bitwise CAS comparison; checked overflow leaves storage unchanged; unaligned access is not a snapshot; prove target capability and AOT/JIT/module parity; no hidden lock fallback. |
| 4 | Build checked handoff, immutable publication and scoped synchronization on those primitives within resources/graphs and rich libraries; allow an explicitly selected coordinated fallback where feasible. | Test publication and mixed-order litmus cases; enforce guards, lifetimes and aliases; justify lock-free and wait-free claims separately; share fallback protocols across modules; preserve effects while allowing valid read elision. |
| 5 | Extend reference atomics only after collector-aware roots, barriers, safepoints and reclamation are defined. Add explicit SIMD, non-temporal and cache operations only with layout, feature and memory-domain contracts. | Establish concurrent root safety before reference APIs; preserve ranges and per-location guarantees through SIMD; complete non-temporal stores before publication; do not confuse cache maintenance with DMA coherence or persistence. |

Before concurrent operations, specify mixed-order/fence rules and executable
litmus cases. Preserve the accepted explicit acquire-release relay rule, not
implicit relaxed-RMW release sequences. Exact declaration spellings,
conditional-capability metadata, CAS order-pair validation and target tables
remain specification work. This sequence does not authorize new backends,
public managed-handle ABI, or wholesale adoption of design-only libraries.

Represent access, ordering and lifetime effects in checked IR before lowering.
Persist and validate those contracts across semantic imports, native producers,
module sets and graphs, with feature-based version selection and rejection
under incompatible headers. Existing ordinary IR and lexical unsafe recovery
do not already encode those guarantees.
