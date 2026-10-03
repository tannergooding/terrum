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

## Active work (priority order)

1. Carry accepted runtime and initialization contracts through compiler
   binding, verification, persistence and lowering. Keep their required
   metadata, proof, API and ABI work explicit in the planned areas below; an
   accepted source form is not a support claim. The
   [grammar](grammar-spec.md) records source contracts and their boundaries.
2. Refine concrete library proposals. Candidate signatures and contracts do
   not imply parser, compiler, runtime or library support.

The source-free TRRM scalar path remains fail-fast; checked-exception Bodies
and richer contracts are not implemented. See
[implementation](implementation.md) for the current support boundary.

The bounded Terrum-authored scalar source frontend supports v74 complete
global-function snapshots and source-order scalar call targets, including
forward and recursive calls with up to three by-value scalar arguments.
Projection v75 additionally permits calls to void functions only as direct
expression-statement roots. Projection v76 admits the existing void-only
`[DoesNotReturn]` contract in complete snapshots and carries its verified
continuation effect across those calls. Independent source auditing precedes
the existing hosted verifier/emitter. General binding/imports, broader
output/reference-call migration beyond scalar `out` and `out var`, and
bootstrap compilation remain pending; this does not extend source-free TRRM
checked exceptions.

Projection v77 extends complete snapshots with the existing scalar `out`
parameter modes, explicit destinations and `out var` call destinations.
Projection v78 adds the existing scalar `init ref<T>` mode and matching
`init`/`init var` destinations to complete snapshots. Hosted
definite-publication verification remains authoritative; general
`init ref<T>` migration and bootstrap compilation remain pending. Single-
function projection v79 adds the existing scalar `init ref<T>` mode and
non-Boolean scalar `out` mode to the bounded self-call frontend, including
matching `out var` destinations in recursive self-calls.
Complete-program projection v80 adds existing-destination calls to scalar `out` parameters on
non-Boolean/void-returning functions; the hosted verifier requires publication
on every normal return, and the hosted emitter publishes them without using
the result as a success flag. It also supports matching `out var` destinations
for these calls, with the destination available after normal return. This
remains a hosted-verifier/emitter-backed projection; it does not broaden
non-scalar non-Boolean outputs. Projection tags are alpha implementation/validation
details, not roadmap or product versions: older layouts may be retired or
reshaped, and only a small selected set should remain for comparison or
validation. Single-function projection v80 and complete-program projection
v81 now also project the accepted `is init` test for named scalar locals and
parameters; hosted definite-initialization verification and native emission
remain authoritative.

The hosted compiler now accepts non-Boolean and void `init ref<T>` completion
for supported output types, requiring full publication on every normal return
and withholding publication on exceptions. Unique outputs transfer on normal
completion independently of the non-Boolean result bits. Managed graph exports
remain limited to handle-free Copy outputs; the bounded scalar projections
project scalar non-Boolean/void init-ref declarations to the hosted verifier
and emitter through separately audited v81/v82 shapes.

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

1. **Full runtime/structured contracts — references and safety.** Define
   general reference/access/loan metadata and caller-unsafe declaration
   contracts before relying on them across calls or imports. Exact syntax and
   implementation remain pending. The unapproved
   [reference-call metadata proposal](proposals/reference-call-metadata.md)
   recommends a narrow first dependency registry and reuse of the assigned
   caller-unsafe effect bit.
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
    [self-host gate](#compiler-and-self-hosting-gate).

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

## Completed work (newest first)

Hashes identify local repository commits. Detailed support and compatibility
boundaries remain in the [implementation reference](implementation.md).

| Commit | Completed milestone |
|---|---|
| `e6277c7` | Project caller-unsafe callable obligations through bounded complete-program v83 while keeping verification hosted |
| `2562118` | Propagate caller-unsafe static operator requirements through generic proof bodies |
| `03ef008` | Enforce same-compilation unsafe field access scopes while persisted writers fail closed |
| `f714926` | Add same-compilation caller-unsafe checks while persisted writers fail closed |
| `84d2564` | Project scalar non-Boolean/void `init ref<T>` completion through v81/v82 source-audited projections |
| `63699aa` | Support hosted non-Boolean/void `init ref<T>` completion without widening scalar projections |
| `63016a8` | Project named-storage `is init` tests in bounded single-function v80 and complete-program v81 scalar frontend |
| `7d8f117` | Support named-storage `is init` tests in hosted native-20 and semantic v49 |
| `8681e1f` | Support vacant-safe optional-handle `is var` and expression-capable `is var?` patterns in hosted native-20 and semantic v48 |
| `a519f22` | Accept the existing initialized/non-null optional-handle presence pattern as `is var item` |
| `db573eb` | Support non-Boolean scalar `out var` recursive self-calls in single-function projection 79 |
| `a74593b` | Support non-Boolean/void scalar `out var` calls in complete-program projection 80 |
| `15c4f0e` | Flatten semantic-module revision 47 serialization and remove obsolete reader branches |
| `2717561` | Retire obsolete semantic-module revisions; revision 47 only |
| `d919c6a` | Native scalar `init ref<T>` parameters and recursive self-call destinations, single-function projection 79 |
| `40664fb` | Native scalar `init ref<T>` parameter modes and call destinations in multi-function snapshots, projection 78 |
| `e0cf111` | Native scalar `out` parameter modes and call destinations in multi-function snapshots, projection 77 |
| `4c17397` | Native scalar multi-function snapshots with `[DoesNotReturn]` contracts and call continuation, projection 76 |
| `5e30392` | Native scalar calls to other void functions as expression statements, projection 75 |
| `3e7fcf8` | Native scalar multi-function snapshots and source-order scalar call targets, projection 74 |
| `296658a` | Native scalar void direct self-call expression statements, projection 73 |
| `324c2d4` | Native scalar direct self-call expression statements, projection 72 |
| `7346396` | Native scalar direct self-calls with inferred `out var` destinations, projection 71 |
| `a1f5cb6` | Native scalar direct self-calls with existing `out` destinations, projection 70 |
| `3c7e65d` | Native scalar `out` parameter declarations with source-audited projection 69 and hosted definite-publication verification |
| `b4e477a` | Native scalar conditional const initializers with lazy branch evaluation, projection 64 |
| `daadff9` | Native scalar short-circuit static proof folding, projection 68 |
| `1f4635b` | Native scalar constant-conditional static proof folding, projection 68 |
| `cdc392c` | Native scalar foldable-true static proofs, projection 68 |
| `f55f904` | Native scalar checked bare catch with optional `finally`, projection 67 |
| `158999e` | Native scalar explicit unsafe scopes and checked bare catch, projection 66 |
| `264e7ee` | Native scalar nested `try`/`finally`, projection 65 |
| `e8f0465` | Native scalar foldable const initializers, projection 64 |
| `8bcd3ff` | Native scalar literal-initialized const locals, projection 63 |
| `7d162b6` | Native scalar inferred vacant locals, projection 62 |
| `088b732` | Native scalar explicit vacant locals, projection 61 |
| `4d7999c` | Native scalar local lifetime ending, projection 60; verifier feasibility memoization within the fixed work limit |
| `df788b8` | Hosted explicit local lifetime ending, checked cleanup/root and loan retirement, and semantic v47 graph persistence |
| `1c80cc9` | Hosted assignment initialization of definitely vacant locals, checked publication and semantic v46 |
| `e40d0b4` | Single same-line loop-label attachment, syntax-44 |
| `586aa4b` | Native parameterless NoThrow/DoesNotReturn function contracts, projection 59 |
| `27aaf17` | Native braced scalar/void `else if` chains, projection 58 |
| `e43ea1c` | Native void arrow requirements and scalar discards, projection 57 |
| `6952d2d` | Native braced void functions and null returns, projection 56 |
| `e1fe270` | Native scalar standalone runtime requirements, projection 55 |
| `3b10d51` | Native scalar labeled loops and named transfers, projection 54 |
| `cee8c31` | Native scalar non-void discard statements, projection 53 |
| `9e20137` | Non-nearest named loop transfers through indexed region exits |
| `1fc9e7e` | Native scalar standalone lexical blocks, projection 52 |
| `e06aee6` | Syntax 43: maximal ASCII punctuation runs and expected-linear indexed diagnostic audits |
| `d6e65d3` | Syntax 42: contextual punctuation boundaries |
| `2f29b3e` | Full-width scalar, indexed and String TRRM serialization |
| `6f0d088` | Syntax 41: symbolic operator and binding spacing |
| `fb49af6` | Syntax 40: role-specific dot attachment |
| `af21c1f` | Syntax 39: attached constructs |
| `4e8fd9b` | Syntax 38: keyword escapes |
| `f2dc5c1` | Syntax 37: generalized comment fences |
| `8113012` | Indexed TRRM `do`/`for` loops |
| `85e1743` | Native scalar `do`/`for` projection 51 |
| `7c3c857` | Indexed lexical locals |
| `9051689` | Native scoped-local projection 50 |
| `3115e5a` | Module-local TRRM scalar calls |
| `e2267db` | Indexed shifts and mutations |
| `37576ad` | Native shifts and mutations, projection 49 |
| `a2083b5` | Indexed branches and loops |
| `0a5200e` | Initial persisted scalar TRRM execution |

Earlier compiler, runtime, module and component milestones are summarized in
the implementation reference rather than repeated here as a second feature
inventory.

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
the gate passes.

Do not translate every design sample at once or build a plugin loader first.
Extend the verified executable corpus and semantic checks in coherent slices;
keep diagnostics and source mappings useful on incomplete input. Bounded
managed access, unique-owner mutation and field-view contracts are implemented;
primitive buffer/view alignment and general `out` to `init ref<T>` migration
remain scheduled. The existing conditional-init subset is not general
reference support. Bounded static exports do not imply `func`, general
`fnptr`, tuples, callable witnesses or contextual lambdas. Fixed-signature
callables precede generic scalar/SIMD functors, which precede contextual
generic lambdas; type packs, polymorphic erasure and arbitrary callback
vectorization are excluded from these stages.

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
