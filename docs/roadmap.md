# Development roadmap

The current milestone is a small checked native compiler, not the entire language.
The [language](language.md) defines accepted semantic direction, while
[implementation](implementation.md) records actual support and tool contracts.

| Stage | Deliverable | Exit evidence |
|---|---|---|
| Syntax foundation (implemented) | Source, Unicode lexer, lossless CST, recovery, CLI, content-bound edits | Public API/CLI tests for round trips, positions, malformed input, numeric boundaries and stale edits |
| Decimal type-call and suffix migration (implemented; syntax-14) | Context-typed decimal integer literals without a fallback type; checked numeric `T(expression)` conversions for `int32`, `uint32`, `int64`, `uint64` and `char16`; attached width suffixes diagnose as `TRM0006`, and prefix casts are no longer parsed. Source-function/local shadowing retains existing call precedence. The scanner-4 five-word integer record is unchanged: valid literals are untagged, while rejected suffix tokens retain tag metadata for diagnostics | Parser/binder/native and scanner-component agree on direct decimal literal binding, supported conversions, signed minima, no fallback type and call precedence; diagnostics include `TRM0006` without changing the scanner record shape or schema version |
| Contextual hexadecimal integer support (implemented; syntax-15) | Unsuffixed `0x`/`0X` integer literals bind directly in their expected type without a fallback; at least one ASCII hex digit is required (`TRM0007` otherwise), and `a`–`f` are digits, so `0x1f32` is not a width suffix. At syntax-15, based values used nonnegative magnitude/range interpretation; syntax-16 supersedes it with target-width bit patterns. Attached width suffixes remain rejected with `TRM0006`; the scanner-4 record layout/schema and semantic/native artifact versions are unchanged | Host and compiled scanner preserve exact token spans, agree on decimal/hex magnitudes and overflow, distinguish missing digits from width-suffix diagnostics, and parser/binder/native execution agree for contextual literals and checked type-call conversions |
| Based integer bit patterns and separators (implemented; syntax-16) | Adds `0b`/`0B` integers and `_` separators in decimal, hexadecimal and binary digit runs, including runs after based prefixes. Based literals construct the expected target width as a bit pattern; leading zeroes/separators do not add significant bits, nonzero excess bits reject. Decimal range and signed-minimum behavior is unchanged. Missing binary digits diagnose as `TRM0008`; malformed separators as `TRM0009`; the scanner-4 record layout and semantic/native artifact versions remain unchanged | Host and compiled scanner agree on radix/separator token boundaries and diagnostics; scanner magnitudes ignore separators; binder and AOT/JIT agree on signed/unsigned target-width patterns, decimal ranges, checked typed conversions and checked unary-minus behavior |
| General numeric conversion policies (accepted direction; implementation scheduled) | Checked integer-to-integer conversions preserve value or throw on range overflow; unchecked conversions wrap to destination-width bits; saturating integer conversions are explicit APIs. Floating-to-floating conversions follow IEEE-754 with nearest/ties-even default rounding, no language failures for inexactness, overflow to infinity, underflow, NaN or infinity, and no enabled FP traps. Floating-to-integer truncates toward zero; checked out-of-range throws, unchecked or explicitly saturating conversion saturates rather than modulo-narrows, with NaN converting to zero. Preserve bounded checked integer conversions; floating support is not implemented | Pair-specific conversion/witness conformance, explicit alternate-rounding APIs, checked/unchecked range edges and floating exceptional values; do not change existing integer conversions or imply cross-format NaN payload preservation/exactness |
| Integer binding and native AOT (implemented) | Typed functions/locals, contextual literals, structured control flow, mandatory verifier, direct Windows x64 PE emission | Positive/negative semantic tests, real native execution, checked failures, stack arguments, imports/unwind inspection and safe artifact replacement |
| Bounded values and compiler components (bounded milestone implemented) | Checked signed/unsigned/code-unit numerics, nested aggregates up to 16 bytes, explicit places, conditional outputs, checked host buffers | Executed range/character/token/scanner candidates, exact token kinds and lossless trivia, shared pinned Unicode fixtures, boundary and invalid-state tests, independent C direct/indirect aggregate/out caller |
| Early JIT and module proofs (bounded milestone implemented) | Shared x64 encoder, W^X/unwind lifetime, protected exception continuations, independent library/reference binding, versioned semantic contracts and native packages | AOT/JIT agreement; root-only JIT against unchanged persisted dependency DLLs; unchanged semantic/native consumer against compatible producer replacement; incompatible public contracts rejected before execution |
| Accepted access migration (class/ManagedNode/compact-struct receiver batches validated) | Semantic v24: `mut class`, writable `mut` class receivers, readonly bare handles and `write` authority for source classes and `ManagedNode`, including optionals, fields, parameters, results and inference; exact output qualification, writable-storage invariance and legacy-artifact normalization. Semantic v25: persisted, non-exported `MutReceiver` methods on non-owning compact structs. Semantic v26: nested mutable-field loans rooted in named mutable locals or an existing writable `this`; every containing field must permit mutation, and calls address original subobjects. Source methods may be public or private without a public receiver ABI. Primitive buffer/view alignment and coherent `out`-to-`init ref<T>` migration remain scheduled | Normal-analysis Debug/Release builds and full suites; native ABI proof under both runtime policies; positive/negative declaration, receiver, handle, field and intrinsic checks; frozen v18/v19/v23 producer execution and unchanged-consumer module servicing; nested receiver writes survive checked exceptions, readonly/by-value behavior is preserved, forged field paths and immutable-place loans reject, and argument evaluation cannot invalidate reserved roots; v26 nested loans and v25 whole-local receivers reject under older headers and are selected by contract use regardless of managedness; private managed state and the absence of a public managed-handle ABI remain preserved |
| Unique mutable receivers and field views (implemented; semantic v27-v30) | Ordinary `mut` methods on initialized named mutable unique owners or forwarded writable `this` update mutable scalar/Copy fields in place without ownership transfer, owning-field replacement, or cleanup-contract changes. v28 extends writable nested field paths through fully initialized unique roots and children; v29 admits owning-buffer field loans for `Set`, `TryAppend` and `Clear`; v30 admits lexical field `SliceMutable` with whole-root exclusion; readonly unique methods retain readonly loans | AOT/JIT updates and checked-failure persistence; recursive cleanup and native heap release exactly once; readonly, moved, temporary, partially initialized, view-conflicting and root-invalidating calls reject, including sibling-field access during an exclusive view; scope/unwind ends only the loan; malformed paths and forged readonly loans reject; unchanged-consumer private producer replacement, feature-dependent writer selection and older-header rejection; partial-constructor mutation, escaping views and public mutating-receiver ABI remain unsupported |
| Full runtime and structured contracts (current) | Checked NoThrow and DoesNotReturn declarations; normal/exceptional `try`/`finally` funclets, unsafe bounded catch; nearest-loop break/continue with scoped for iterators; saved-RBP source frames and shared PE/JIT scope/unwind records; exact lexical/temporary roots and checked root unwind in the managed-node proof; scheduled context-specific discard evaluation/cleanup and explicit separation of by-value returns from source loans; general exception objects and broader runtime contracts remain open | Effect/normal-continuation source, IR and module checks; cleanup ordering and rooted return snapshots; independent C frame/unwind and escaping-native-catch proofs; bounded checked failure injection; root lifetime and malformed-IR checks across calls and cleanup; loop-transfer allocation/release accounting and persisted v21 servicing; discard expressions evaluate once and clean owners, wildcard patterns preserve the scrutinee, lambda `_` creates no binding, and unresolved type holes diagnose; general runtime integration remains outstanding |
| Existing requirement/recovery regression gate (preserve first) | Preserve the implemented safe statement-form `require`, boolean/`out` conditional publication, false-only requirement-failure dispatch under the current policy, predicate-exception propagation, safe cleanup `finally`, unsafe checked recovery, and fail-fast's no-cleanup guarantee; this is regression coverage, not a new API implementation | Retain existing tests for predicate evaluation/publication, predicate exceptions versus false requirements, cleanup, recovery-policy gates, and conditional initialization; no behavioral change is implied by later declaration or lifting work |
| Registered literals and constant evaluation (scheduled within full runtime) | Target-registered literal parsers over borrowed source text plus bounded `[ConstEval]` declarations and use-site requirements; no runtime literal fallback, ambient I/O or separate consteval-only function category | Valid literals produce direct target constants; malformed/out-of-range literals diagnose; successful parser calls fully initialize and false leaves output vacant; runtime parser calls remain callable; evaluator is deterministic/resource-bounded and preserves target semantics, ownership, representation and producer/rule/value-ABI dependencies without changing binding |
| Published conditions and static proofs (scheduled after literal migration) | Declaration preconditions/postconditions as versioned header contracts; body-local `static require`/`static ensure` as compile-time-only proofs; ordinary body `require` remains the existing executable point check, not inferred public metadata. Where runtime behavior is legal, authors may explicitly choose an ordinary expression/check instead of a compile-time requirement; compilers never silently demote it. Accepted call boundary: prologue, preconditions, method body, postconditions, epilogue; body includes initialization of local/output values, authored execution/`finally`, local cleanup and cleanup of untransferred owned parameters before postconditions. Static proofs use a versioned minimum rule/complexity and resource floor; sound beyond-floor proof support and larger budgets may vary | Stable diagnostics and compatibility checks for stronger preconditions/weaker postconditions; runtime values can satisfy symbolic proofs; required static obligations fail compilation if disproved or not established, with an explicit distinction; floor support is independent of optimization level and compilers may accept additional programs through sound extensions. Exact floor rules, accounting and numeric limits remain to specify; constant evaluation is separate and retains exact target semantics/no runtime fallback. Runtime alternatives are unavailable for compile-time-only layout/type/metadata requirements and do not weaken structural lifetime/initialization safety. Entry snapshots/history remain deferred without promise; ordinary postcondition checks execute at their declared phase unless soundly proven redundant, preserving effects/failures; no transactional rollback or rerun of completed `finally` is implied |
| Requirement failure diagnostics and construction (scheduled after contract shape) | General exception construction/ABI, default failure annotations, explicit call-site override, and recognized null/range diagnostics without method-name inference or predicate re-evaluation | Predicate exceptions propagate unchanged; only false selects the declared/selected failure; supported exception shapes and diagnostics are stable across AOT/JIT and modules; arbitrary parameterless exception construction is not assumed |
| Conditional-output lifting and matching (scheduled after output-contract migration) | Requirement-expression lifting for one declared success-conditioned output plus explicit status matching; preserves the existing boolean/`out` implementation until the coordinated `init ref<T>` migration | Evaluate once; publish only complete output on declared success; false/exception cleanup leaves no readable vacant output; multiple outputs and enum success predicates remain explicit; no `Try`-name or zero-enum guessing |
| Bounded Copy generic specialization and inference (implemented; syntax-18, managed producer contract semantic v24) | Top-level function templates support multiple independently constrained type parameters over eligible `Copy` structs, scalar value types and existing source-class/`ManagedNode` handles with their readonly/write and supported optional forms. Typed arguments establish handle types before expected-result fallback; ordinary permitted result weakening does not retarget them. Existing conditional-output type and qualification rules, GC/rooting, semantic class remapping and native per-image heaps apply. Qualified generic-call type arguments do not change the scanner record/schema, wire format or native profile. See the [bounded generic contract](implementation.md#bounded-generic-functions) for inference and constraint details | No unique/owning arguments, views, generic classes/structs, managed fields inside user structs, partial holes, ordinary overloads, public generic ABI or new managed-handle ABI; handle substitutions do not grant member, null-test or operator capabilities |
| Homogeneous binary operator protocols (implemented; syntax-17) | Extends explicit nominal Copy-struct protocols with subtraction, division, remainder and bitwise AND/OR/XOR alongside addition/multiplication; retains one static abstract binary declaration, closed homogeneous conformance, definition-site constraints and declared effects; at this milestone, no primitive, unary, comparison, shift or heterogeneous witnesses | AOT/JIT arithmetic and bit-selection samples, checked overflow/zero-divisor failures and signed-min remainder, NoThrow checks, inferred helper composition and rejection of missing/conflicting nominal witnesses; compiled-scanner parity and private specialization replacement behind unchanged semantic/native consumers; no scanner layout, semantic version or physical ABI change |
| Homogeneous equality/comparison protocols (implemented; syntax-19) | Six distinct source-declared nominal `Copy`-struct protocols for `==`, `!=`, `<`, `<=`, `>` and `>=`, each with homogeneous operands and `bool` result using the existing three-parameter static-operator shape; each conformance and generic constraint is explicit; no derived laws, inferred `NoThrow`, universal equality/hash/identity policy, primitive or heterogeneous witnesses, or public generic ABI | Generic `Clamp`/`Within`/`Different` and conditional equality selection preserve declared effect and publication rules; missing/conflicting protocol witnesses reject; AOT/direct-JIT, scanner parity and private semantic/native servicing validated under both policies, with no wire-format or native-profile bump |
| Async completion responsibility (scheduled after syntax/literal migration within the full runtime) | Task-like completion, including `Task<void>`, with every started operation joined by a structured owner, explicitly transferred to another responsible owner, or supervised under an error/lifetime/shutdown policy; no `async void`; cancellation is not completion or early borrow release | Ownership paths cannot silently discard completion/error obligations; cancellation and failure retain captures through actual completion and cleanup; synchronous `void` remains valid |
| Resources and graphs (bounded ownership and tracing implemented) | Compact unique POD/composite owners, whole-owner transfers, receiver loans, recursive cleanup, reusable native `int32` storage and lexical slices; source-defined managed classes with complete-publication construction, optional edges, compact handle-free Copy fields and constructor arguments, rooted presence snapshots and descriptor-based cycle tracing; schedule value/lending/consuming iterator categories and allocator/relocation contracts; partial field moves, escaping views, arbitrary owning graphs and broader resource adapters remain open | AOT/JIT ownership and exact roots; independent native allocation failure, direct/indirect aggregate construction/unwind and release accounting; copied token/range values surviving source-link collection, source-class cycles, heterogeneous edges, shared aliases, pointer-shaped scalar nonroots, and 4,096-object collection/reuse fixtures; iterator loans retain declared backing dependencies, cursor-state loans cannot overlap invalidating advancement, and allocation failure preserves cleanup/publication invariants; broader managed layouts and GC stress remain outstanding |
| General references and implicit readonly parameter loans (scheduled with loan/parameter metadata) | Parameter-only `implicit ref<T>` borrows an eligible place or initializes a hidden local at the original argument-evaluation point in the current enclosing lexical scope, with ordinary readonly-reference semantics, no Copy/ownership transfer solely to borrow and no extra implicit-only noescape rule; skipped conditional/short-circuit evaluation creates no initialized local; declared result/backing dependencies and full `T` qualification are preserved; applies to concrete/generic parameters and explicit extension receivers, not ordinary receiver defaults; existing APIs change only by declaration; no standalone `implicit T`/`in T`, `view<T>` or representation attribute | Implement ordinary reference/loan metadata and lexical-local initialization/cleanup; do not hoist evaluation or create a lifetime-only region; apply ordinary reference lifetime, return, storage, capture and suspension checks without widening existing place/backing dependencies; preserve nested write capability, backing, roots and ownership when copying a Copy `T`; reject stacking with writable/init/pointer contracts or arbitrary conversions; explicit and implicit readonly-ref forms share a signature; ordinary `F(value)` chooses by-value `F(T)` when both apply; preserve source semantics under by-value/indirect ABI lowering, with no size threshold or adaptive type attribute |
| General separate compilation (bounded native graphs and catalog resolution implemented) | Versioned semantic contracts, persisted EXE/DLL graphs with up to sixteen producers, unique-compatible selection from explicit local candidates, private managed-state replacement and semantic-v31 compact `Copy` exports; semantic v32 supports private managed consumers through ordinary single-provider semantic consumers and native single-provider/module-set entry points using scalar and handle-free `Copy` contracts. Semantic modules share one heap/root domain and remap private class identities; persisted native images retain separate domains. Semantic v32 persisted native graphs additionally permit managed intermediate and root producers. Implement accepted layout policy: private default `Auto`; declaration-order `Sequential`; offset-zero `Blittable` `Union`; one-field `Transparent` with carrier representation and argument/result ABI; and fixed per-target `Blittable: true` with size/alignment/offsets and transitive field compatibility. Layout kind alone does not freeze; unfrozen fields remain private, while frozen fields may be exposed without changing readonly/`mut` rules. Unfrozen layout-dependent values cannot cross independently replaceable dynamic boundaries, but may be used in layout-coupled compilation/rebuild/invalidation scopes. Add both power-of-two member packing and aggregate-over-alignment controls, including combined use and tail padding/array stride; exact source spellings, target limits, metadata and under-aligned access lowering remain specification work. Atomicity, `Copy`, sharing, arbitrary validity and universal wire encoding do not follow from layout. Preserve existing ABI through an explicit compatibility migration | Compatible replacement preserves semantic consumers and native graph root/intermediate images, including frozen older wire formats and managed implementations at leaf, middle and root; distinct leaf identities coexist; caller roots survive imported calls and collection is image-local; replacing a semantic provider requires native relinking for a new PE, while compatible native package replacement retains the persisted consumer image; transparent wrappers match carrier classification across direct/indirect calls; Blittable layout changes reject under the same contract unless separately versioned; unfrozen layout-dependent values either remain layout-coupled or are invalidated/rebuilt before use across a dynamic boundary; padding, initialization, raw-copy, malformed-layout and array-stride conformance cases hold; preserve compact-value/module contracts and feature migration; public managed-handle ABI and cross-image shared collection remain outstanding |
| Managed-backed compact `Copy` exports (implemented; semantic v31) | Leaf producers may expose existing public, handle-free `Copy` values up to 16 bytes by value as parameters/results and through existing `bool`/`out` conditional outputs, including scalar outputs; private managed storage remains producer-local, and native ABI/profile versions do not change | Preserve nominal type provenance and public-field visibility for `Copy` types crossing the argument/result ABI, not automatically for the declaring owner; existing scalar-only static exports on public owners with private fields remain valid, and a static declaration on a unique owner does not transfer it; preserve `NoThrow` and existing value-ABI contracts; older headers reject expanded export shapes; no exported managed handles or unique values, nested managed-containing values, views, reference modes, mutating-receiver ABI or managed-leaf imports |
| Managed graph participants and consumers (implemented; semantic v32) | Semantic v32 supports private managed consumers through ordinary single-provider semantic consumers and native single-provider/module-set entry points. Persisted acyclic native DLL graphs also permit managed roots and intermediate producers through scalar and handle-free public `Copy` by-value/result and `bool`/`out` contracts; each persisted image owns its heap and root chain, imported calls preserve caller roots, and collection is image-local. Semantic modules statically link into one heap/root domain and remap private class identities | AOT/root-only JIT leaf replacement with unchanged managed middle and root images under both runtime policies; semantic provider servicing and fresh static links; no cross-image handles, unique values, views, reference modes, cross-image shared collection, graph cycles, DLL-unload collection or native ABI/profile/package-format changes |
| Nominal witnesses, adapters, families and operation protocols (scheduled) | Coherent checked behavioral witnesses; each type declares ordinary implementations, with no import-driven retroactive global conformance. Explicit adapter types may add extension-interface behavior without changing the original type. A one-way Original-to-Adapter projection is possible only under checked validity, representation, access, ownership, lifetime and container rules; no blanket generic covariance. Families require explicit opt-in and declared checked projections/constraints. No universal root is introduced merely for equality, hashing or formatting; use operation-specific witnesses/interfaces and explicit adapters. Specify exact syntax, forwarding, conflicts, dispatch metadata, projection eligibility and conformance evidence | Conflicting ordinary witness claims reject independent of import order; adapter selection preserves the original type's conformance set; invalid/narrowing representations do not gain total projections from equal bits; no projection weakens writable invariance; one selected witness binds each associated result; non-injective relations are not inverted; associated integer and SIMD relationships validate across AOT/JIT and module boundaries |
| Explicit interface representations (scheduled) | Checked borrowed `ref<I>`/`write ref<I>` and managed `box<I>`/`write box<I>` forms, with slot access distinct from referent authority; no `dyn` keyword or implicit interface handle | Borrow/root/dispatch and module ABI checks; explicit inline-struct boxing and unique transfer; class-object projection preserves identity only under a checked representation; heterogeneous `List<box<I>>` retains normal storage/lifetime rules |
| General overload binding and generic inference (scheduled) | General source-time overload binding recorded as a selected declaration plus closed generic arguments; ordered inference from explicit arguments, typed arguments, expected result, then contextual literals/lambdas; exact syntax and extension qualification remain open; ordinary by-value `T` wins over `implicit ref<T>` for `F(value)`, while explicit reference arguments may select a reference overload | Link/JIT never repeats source overload resolution; `Set(1)` ambiguity after an added `uint32` overload is diagnosed on recompilation while existing bindings remain stable; no numeric fallback, constraint-only overloads or unresolved exported holes; implicit versus explicit readonly-ref alone never distinguishes overloads; bounded Copy inference does not imply general overload or argument/result inference |
| Evolvable types and compatibility forwarding (scheduled) | Open-by-default API/case evolution, explicit closed-case/layout/frozen contracts, exhaustiveness-checked `[Exhaustive]` fallbacks, exact metadata forwarders preserving full old contracts, and first-class build-tool compatibility analysis | Closed-case additions break compatibility; exhaustive dispatch retains checked unknown-case failure; fallback preserves valid payload cleanup/rooting and warns about missing known cases on recompilation; tooling reports binary and source-binding risks plus witness/default and layout/access/effect/lifetime changes without claiming behavioral proof |
| Rich libraries and self-hosting | Executed range, scanner, integer, string and source-line components, including scanner-backed integer binding, an owned line-start index, reusable signed/unsigned decimal formatter and bounded managed token chain; compilation-local static operator protocols, composed structured generic algorithms, scalar results and conditional Copy outputs; schedule operation-specific equality, key/hash, formatting and display/debug witnesses, SIMD ABI families distinct from logical TensorSpan shape/stride views, and tensor primitive contracts; general generic algorithms, views/builders and public witness implementation remain scheduled | Shared host/self-host fixtures, byte-identical host/component images and numeric diagnostic parity, binary-searched UTF-16 positions, invariant 64-bit formatting boundaries and linked-storage count/version/collection checks; nominal wrappers execute constrained quadratic and composed 32-/64-bit integer-power algorithms through AOT/JIT, including private helper specialization replacement behind unchanged scalar exports; tensor empty/rank-zero/stride/overlap cases preserve shape and backing dependencies; recursive specialization reuse, mixed indirect Copy arguments/outputs, conditional token/range selection, and definition-site witness/effect/publication checks; equality/hash compatibility and explicit-formatting witness checks without universal root methods; functional interfaces and broadly composable kernels remain separately scheduled; no parser-only bootstrap or full standard-library claims |
| Registered builders and parameter-directed handlers (scheduled within rich libraries) | Collection/text target registration, eager typed capture, and streaming handlers with explicit ownership, lifetime, refusal and publication contracts; expression handlers consume explicitly supported typed operation graphs, not arbitrary callback bodies | Holes evaluate once left-to-right before draining; move/copy/refusal/cleanup and failure publication are checked; alias/backing and capture dependencies are preserved; unsupported expression graphs diagnose or use separately declared fallback; no FMA, reassociation, reordering or changed effects/failure behavior is introduced silently |
| Fixed-signature callables and tuples (scheduled) | After metadata, access, loan, effect, ABI and ownership contracts are established: transparent aliases; closed single-target `func`/`fnptr` signatures; tuple construction; named static callable views; fixed-signature contextual lambdas and checked nominal/callable witnesses, without boxing, user-declared delegate types or language-level multicast events; any event/subscription aggregation remains an explicit library contract | AOT/JIT and module-contract parity; signature/effect/access/lifetime/ABI witness checks; static/open/bound invocation, receiver access and capture cleanup; source-label compatibility and named-argument order; tuple copy/uniqueness/borrow checks; explicit aggregation defines order, exceptions/refusal, reentrancy, mutation/disposal and all-result handling; async aggregators retain responsibility for every returned task; no implicit tuple ABI or callable allocation |
| Generic scalar/SIMD functors and explicit specialization (scheduled) | Named generic functors checked against definition-site constraints; exact closed-generic specialization references with declared CPU/OS requirements and priority; portable AOT baseline/variants with same-contract effects, access, lifetime, ownership and cleanup | Scalar and supported-width results against explicit contracts; exact target selection and deterministic tie rejection; CPU/OS dispatch reaches eligible variants and preserves fallback policy; differential tests/proof obligations compare handwritten algorithms with generic operation graphs; call/effect counts, cross-lane behavior, inactive lanes, tails and floating-point ordering/contraction; captured scalar values require explicit broadcast; a closed scalar callback never implies vectorization |
| Contextually generic lambdas (scheduled) | Generic lambdas as shorthand for the same checked generic-functor contract, not a new type-pack facility | Constrained symbolic operations such as `x => x * x` validate `T` operations and Copy requirements; contextual scalar/SIMD behavior matches the named functor contract; capture and diagnostics are checked without arbitrary pack expansion or polymorphic callable erasure |
| Later capabilities | Production JIT and additional native targets, async runtime/start/scheduler mechanics, generation/reflection, interop, scalable SIMD | Workload-specific correctness, ABI, lifetime, servicing and measured cost evidence |

Do not implement all stages simultaneously. The source-class/`ManagedNode`
readonly/writable, mutable-class-receiver, and named/nested mutable compact-struct
receiver-writeback batches are validated. They preserve historical authority
when normalizing private managed artifacts; primitive buffer/view access and
the `out` declaration/call migration to `init ref<T>` are scheduled separately.
Preserve conditional publication, cleanup and module servicing. `init ref<T>`
is not an alias for arbitrary writable references or a parser-only rename of
`out`.

The callable stages above are target work, not current compiler support. The
existing bounded static native-export path and its physical ABI do not imply
that `func`, general `fnptr`, tuples, callable witnesses or contextual lambdas
are parsed or implemented. The fixed-signature stage precedes generic
scalar/SIMD functors, which precede contextual generic lambdas; user-defined
type packs, pack operations, polymorphic callable erasure and automatic
vectorization of arbitrary callbacks are outside these scheduled stages.

Semantic version 24 carries the managed access capability contract. Semantic
version 25 adds a distinct `MutReceiver` contract for persisted, non-exported
mutating methods on non-owning compact structs; source visibility may be public
or private. Semantic version 26 adds nested mutable-field loans rooted in
named mutable locals or an existing writable `this`, requiring writable
fields throughout the path. Older headers reject the respective contracts;
whole-local non-owning receiver loans remain v25. Semantic v27 adds ordinary
mutating unique-owner methods and rejects them under older headers.
Semantic v28 adds nested mutable-field loans through fully initialized unique
roots and children; earlier headers reject the broadened loan contract.
Semantic v29 adds writable buffer-field loans for `Set`, `TryAppend` and
`Clear`, without a new public or physical native ABI.
Semantic v30 adds lexical mutable buffer-field views with the existing
no-escape rules and conservative whole-root exclusion.
Programs requiring none of these contracts retain
their existing versions and bytes regardless of managedness.
Readers normalize private managed artifacts
from versions 18 through 23 into qualified internal IR without changing their
justified capabilities. Semantic v31 permits compact handle-free `Copy`
exports while keeping managed state and handles private.

General `ref<T>`/`ptr<T>`/`box<T>`/`val<T>`, explicit erased-interface
representations, `write` views, `own<T>`, generic access constraints and
`init ref<T>` invocation grammar each require their respective representation,
lifetime, cleanup, ABI and regression work. They are scheduled implementation
work, not part of the active class batch and not items to park under Later.
The accepted call-site form is `Create(init value)`; parser/binder support and
coordinated `out` migration remain scheduled.
The immediate next coherent work extends the verified native corpus and its
semantic checks, not a plugin loader or wholesale translation of every design
sample. Stabilize syntax accessors while extending the grammar; keep
diagnostics/source mappings usable on incomplete input.

Concrete `Copy` helper composition is implemented within the bounded generic
stage: templates can combine concrete local/imported value types and symbolic
formals using normal declaration lookup and inference. Isolated proof contexts
load only reached declarations and nested layouts, preserve accessibility and
effects, and keep concrete witnesses distinct from formal constraints. This
adds no source syntax, semantic wire version, native profile or public generic
ABI; closed specializations remain ordinary private verified functions.

The same stage admits concrete source-class factories and members inside
templates, retaining existing handle qualifications, cyclic descriptor layouts,
conditional outputs and managed rooting. Only isolated definition proofs may
use declaration-only managed dependencies; executable and persisted checks
remain unchanged. This uses existing `TryCreate`, not a new construction form
or public managed boundary.

Within Rich libraries, define operation-specific checked witnesses for value
equality, identity where supported, key equality paired with a compatible hash,
invariant machine-readable formatting, and display/debug rendering. These are
opt-in operations alongside primitive built-ins, not universal root methods or
an obligation to make every value stringifiable. No universal root is
introduced merely to provide these operations.

The [pending-decision index](language-library-review.md) distinguishes review
items from accepted contracts and specification work. Detailed boundaries live
in [language](language.md), [lifetimes](lifetimes.md), [type families](type-families.md),
[conversions](conversions.md) and [libraries](libraries.md). They include
generic member-contract refinement, stored-view ABI, inline class eligibility,
exception/low-memory ABI, associated/conditional-conformance grammar and
explicit adapter witnesses, erased-interface dispatch ABI, match grammar and
closed-case markers, forwarder attributes/source visibility and compatibility
tooling policy, text representation, explicit async affinity/scheduler syntax,
async start policy, floating-destination conversion policies, scalable
sharing ABI and final construction/annotation spellings. Accepted semantic
policies are not reopened by these syntax/ABI questions; none should be
silently fixed by a convenient host or machine instruction.

## Language, semantic IL and metadata specification

The long-term specification must cover both the source language and its
execution/interchange contracts: comparable in scope to a language standard
plus an infrastructure standard, without adopting CIL, CLR or their historical
format choices. The semantic IL and metadata format are first-class work
within full runtime/structured contracts and general separate compilation,
not merely serialization details of the current compiler's private IR.

Maintain one evolving language specification, organized into syntax, types,
static-semantics and execution chapters. Distinguish intended rules, actual
implementation coverage and unresolved proposals. The specification is
authoritative for current intent, not frozen forever; intentional pre-v1
changes, including breaking changes, remain allowed and should become
increasingly infrequent as the design solidifies. This does not require
independently versioned chapters or create a perpetual compatibility promise.
Existing implemented compatibility behavior and explicit migration
requirements remain unchanged.

The intended IL is a compact binary representation of the language and
ecosystem, carrying type information, metadata/contracts and operations. It is
usable as compilation input for metadata binding, reflection and tooling, and
for linking and AOT/JIT machine-code generation without source recompilation.
It is not merely serialization of a private compiler object graph or a
verifier certificate. Its ecosystem role is comparable to CIL without adopting
its format. Compactness does not select a compression codec or container;
pre-v1 IL breaks remain allowed and no frozen versioning scheme is selected.
Source compilation checks authored bodies against their contracts. The verifier
is one consumer of metadata; ordinary binary consumers perform safe structural,
reference, binding, compatibility and caller-use checks, then trust declared
contracts for external dependencies without mandatory dependency-body
re-proof. Callers still satisfy imported preconditions, access, ownership and
effect contracts. This does not trust malformed bytes or change the current
bounded readers/verifier. Optional deeper audit tooling is not a v1 promise;
signing/provenance does not establish semantic correctness; no mandatory
signature or trust store is selected.

Separate minimal language conformance from the standard application-library/
runtime surface and feature-specific dependencies. Core requirements remain
genuinely small; richer facilities retain their checked dependencies when
used, with no silent weakening or fallback. This does not define an exhaustive
type/API list, named runtime profiles, universal support for every feature or
an implemented embedded/kernel backend.

Specify the source language, semantic operations and verifier, metadata/module
interchange, and runtime/target profiles with explicit boundaries. The exact
normative IL and wire encoding remain design work, not a selected new format.
Cover declaration identities and references, types, generic
constraints/witnesses, access/ownership/lifetime and output contracts, effects,
capabilities, layouts and runtime dependencies. Accepted documentation
direction: attach `///` comments to declarations, with `/** ... */` equivalent;
use GFM-compatible prose, implicit leading summaries, and structured
top-level Markdown headings. Declaration-scoped mentions such as `@value`,
`@T`, and `@Type.Member` resolve to structured references and may be
post-processed. Ordinary Markdown links remain for document navigation;
explicit `xref:` links support custom or qualified references. Documentation
emits a separate XML artifact and does not define executable contracts.
Exact tokenization/escaping, complex reference grammar, schema/member IDs,
and packaging remain specification work.

Format work must define version and feature negotiation, unknown-data handling,
compatibility, deterministic/canonical encoding where promised, malformed-input
rejection, validation limits and producer/consumer trust responsibilities.
Keep source-language versions, semantic artifact formats and physical native
profiles distinct. Establish conformance with independently exercised readers,
writers and verifier cases rather than relying only on same-version round trips.

The existing versioned semantic modules are implemented compatibility contracts
and evidence for this work, not automatically the final general IL standard.
Preserve supported historical artifacts and feature-dependent version selection;
do not replace or freeze the current format merely to make the specification
look complete. This work does not require a new backend or public managed ABI.
Terrum makes no published frozen managed-machine ABI promise. Dynamically
composed precompiled code must be validated against dependency/compiler/runtime
assumptions before execution; implementation may adapt, recompile from an
available form, discard, or reject incompatible code. No JIT fallback or IR
retention is mandatory, so strict AOT may require rebuilding or fail. This
does not weaken published language contracts or required static/safety checks;
declared FFI exports remain fixed to their target-profile contract.

## Memory-model implementation sequence

The [memory model](memory-model.md) is accepted design, not implemented
concurrency support. Integrate it through the four current areas above rather
than introducing a parallel runtime or moving the work under Later capabilities.
Each increment must preserve existing single-threaded behavior and remain
honest about unsupported source and artifact contracts.

| Order | Increment and dependencies | Required evidence |
|---|---|---|
| 1 | Preserve the existing baseline within full runtime/structured contracts: observable evaluation, checked arithmetic, cleanup, ordinary access widths and current unsafe-recovery behavior | Source/IR/AOT/JIT regressions establish that optimization introduces no invalid accesses or extra observable faults/effects; unsafe discovery does not create new repair/trap behavior; no claim that current unsafe blocks implement general caller-unsafe contracts |
| 2 | Implement the accepted `unsafe` callable-declaration contract for caller obligations, explicit unsafe-block requirements in bodies and at caller use sites, and producer-owned transfer/share capability rules alongside reference/loan/parameter metadata; integrate them with general separate compilation. No trusted-implementation source marker is required for ordinary safe declarations containing checked internal unsafe blocks | Safe callers cannot erase obligations through wrappers or imports; ordinary declarations do not propagate caller-unsafe obligations; unsafe operations remain explicitly scoped even in caller-unsafe methods; generic bodies use published constraints; private-layout or JIT facts do not alter source binding; incompatible capability/safety changes reject without freezing otherwise opaque layouts |
| 3 | Implement the bounded `MemoryPrimitives` value-access and scalar-atomic surface after typed access paths, representation eligibility and compile-time order binding are available; start with supported Windows x64 profiles | Separate atomicity, ordering and progress checks; defined bitwise CAS comparison; checked overflow leaves storage unchanged; unaligned access does not imply a snapshot; exact target capabilities and AOT/JIT/module parity; no hidden lock fallback |
| 4 | Build checked handoff, immutable publication and scoped synchronization protocols on those primitives within resources/graphs and rich libraries; provide explicitly selected coordinated fallback where feasible | Publication and mixed-order litmus tests; guard/lifetime/alias enforcement; lock-free versus wait-free claims justified independently; fallback users across modules share one protocol; no unconditional read-elision prohibition when required effects are preserved |
| 5 | Extend reference atomics only after collector-aware roots, barriers, safepoints and reclamation are defined; add explicit SIMD/non-temporal/cache operations as their layout, feature and memory-domain contracts become available | Concurrent collection/root safety before safe reference APIs; source ranges and per-location guarantees survive SIMD lowering; non-temporal completion precedes publication; cache maintenance does not masquerade as DMA coherence or persistence |

Before implementing explicit concurrent operations, complete the formal
mixed-order/fence rules and executable litmus cases. Preserve the accepted
explicit acquire-release relay rule, not implicit relaxed-RMW release
sequences. Exact declaration spellings, conditional-capability metadata,
CAS order-pair validation and target tables are specification deliverables
within these increments. This sequence does not authorize additional backend
architectures, a public managed-handle ABI, or wholesale implementation of the
design-only library inventories.

Represent access, ordering and lifetime effects in checked IR before lowering
the concurrent operations. Persist and validate the required contracts across
semantic imports, native producers, module sets and graphs, with feature-based
version selection and rejection under incompatible headers. Existing ordinary
IR and lexical unsafe recovery do not already encode those new guarantees.
