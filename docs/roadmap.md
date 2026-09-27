# Development roadmap

The current milestone is a small checked native compiler, not the entire language.
The [language](language.md) defines accepted semantic direction, while
[implementation](implementation.md) records actual support and tool contracts.

| Stage | Deliverable | Exit evidence |
|---|---|---|
| Syntax foundation (implemented) | Source, Unicode lexer, lossless CST, recovery, CLI, content-bound edits | Public API/CLI tests for round trips, positions, malformed input, numeric boundaries and stale edits |
| Integer binding and native AOT (implemented) | Typed functions/locals, contextual literals, structured control flow, mandatory verifier, direct Windows x64 PE emission | Positive/negative semantic tests, real native execution, checked failures, stack arguments, imports/unwind inspection and safe artifact replacement |
| Bounded values and compiler components (bounded milestone implemented) | Checked signed/unsigned/code-unit numerics, nested aggregates up to 16 bytes, explicit places, conditional outputs, checked host buffers | Executed range/character/token/scanner candidates, exact token kinds and lossless trivia, shared pinned Unicode fixtures, boundary and invalid-state tests, independent C direct/indirect aggregate/out caller |
| Early JIT and module proofs (bounded milestone implemented) | Shared x64 encoder, W^X/unwind lifetime, protected exception continuations, independent library/reference binding, versioned semantic contracts and native packages | AOT/JIT agreement; root-only JIT against unchanged persisted dependency DLLs; unchanged semantic/native consumer against compatible producer replacement; incompatible public contracts rejected before execution |
| Accepted access migration (class/ManagedNode batch validated) | `mut class`, writable `mut` class receivers, readonly bare handles and `write` authority for source classes and `ManagedNode`, including optionals, fields, parameters, results and inference; exact output qualification, writable-storage invariance and legacy-artifact normalization; primitive buffer/view alignment, struct receiver writeback and coherent `out`-to-`init ref<T>` migration remain scheduled | Normal-analysis Debug/Release builds and full suites; native ABI proof under both runtime policies; positive/negative declaration, receiver, handle, field and intrinsic checks; frozen v18/v19/v23 producer execution and unchanged-consumer module servicing; private managed state and scalar-only public exports preserved |
| Full runtime and structured contracts (current) | Checked NoThrow and DoesNotReturn declarations; normal/exceptional `try`/`finally` funclets, unsafe bounded catch; nearest-loop break/continue with scoped for iterators; saved-RBP source frames and shared PE/JIT scope/unwind records; exact lexical/temporary roots and checked root unwind in the managed-node proof; general exception objects and broader runtime contracts remain open | Effect/normal-continuation source, IR and module checks; cleanup ordering and rooted return snapshots; independent C frame/unwind and escaping-native-catch proofs; bounded checked failure injection; root lifetime and malformed-IR checks across calls and cleanup; loop-transfer allocation/release accounting and persisted v21 servicing; general runtime integration remains outstanding |
| Resources and graphs (bounded ownership and tracing implemented) | Compact unique POD/composite owners, whole-owner transfers, receiver loans, recursive cleanup, reusable native `int32` storage and lexical slices; source-defined managed classes with complete-publication construction, optional edges, compact handle-free Copy fields and constructor arguments, rooted presence snapshots and descriptor-based cycle tracing; partial field moves, escaping views, arbitrary owning graphs and broader resource adapters remain open | AOT/JIT ownership and exact roots; independent native allocation failure, direct/indirect aggregate construction/unwind and release accounting; copied token/range values surviving source-link collection, source-class cycles, heterogeneous edges, shared aliases, pointer-shaped scalar nonroots, and 4,096-object collection/reuse fixtures; broader managed layouts and GC stress remain outstanding |
| General separate compilation (bounded native graphs and catalog resolution implemented) | Versioned semantic contracts, persisted EXE/DLL graphs with up to sixteen producers, unique-compatible selection from explicit local candidates, and private managed-node or source-class leaf implementations behind scalar exports; rich LMOD contracts, version-ordering policy and resilient layouts remain open | Compatible replacement preserves root/intermediate images, including frozen older wire formats and a replacement that introduces private class cycles; distinct leaf identities coexist; malformed class layouts/construction/roots and ambiguous, incompatible or incomplete catalogs fail before execution; public managed ABI and larger layout evolution remain outstanding |
| Rich libraries and self-hosting | Executed range, scanner, integer, string and source-line components, including scanner-backed integer binding, an owned line-start index, reusable signed/unsigned decimal formatter and bounded managed token chain; compilation-local static operator protocols, composed structured generic algorithms, scalar results and conditional Copy outputs; schedule operation-specific equality, key/hash, formatting and display/debug witnesses here; general generic algorithms, views/builders and public witnesses remain open | Shared host/self-host fixtures, byte-identical host/component images and numeric diagnostic parity, binary-searched UTF-16 positions, invariant 64-bit formatting boundaries and linked-storage count/version/collection checks; nominal wrappers execute constrained quadratic and composed 32-/64-bit integer-power algorithms through AOT/JIT, including private helper specialization replacement behind unchanged scalar exports; recursive specialization reuse, mixed indirect Copy arguments/outputs, conditional token/range selection, and definition-site witness/effect/publication checks; equality/hash compatibility and explicit-formatting witness checks without universal root methods; no parser-only bootstrap or full standard-library claims |
| Fixed-signature callables and tuples (scheduled) | After metadata, access, loan, effect, ABI and ownership contracts are established: transparent aliases; closed `func`/`fnptr` signatures; tuple construction; named static callable views; fixed-signature contextual lambdas and checked nominal/callable witnesses, without boxing, user-declared delegate types or multicast | AOT/JIT and module-contract parity; signature/effect/access/lifetime/ABI witness checks; static/open/bound invocation, receiver access and capture cleanup; source-label compatibility and named-argument order; tuple copy/uniqueness/borrow checks; no implicit tuple ABI or callable allocation |
| Generic scalar/SIMD functors (scheduled) | Named generic functors checked against definition-site symbolic constraints and specialized for supported scalar and registered SIMD members; a distinct elementwise kernel contract | Scalar and supported-width results against explicit contracts; call/effect counts, cross-lane behavior, inactive lanes, tails and floating-point ordering/contraction; captured scalar values require explicit broadcast; a closed scalar callback never implies vectorization |
| Contextually generic lambdas (scheduled) | Generic lambdas as shorthand for the same checked generic-functor contract, not a new type-pack facility | Constrained symbolic operations such as `x => x * x` validate `T` operations and Copy requirements; contextual scalar/SIMD behavior matches the named functor contract; capture and diagnostics are checked without arbitrary pack expansion or polymorphic callable erasure |
| Later capabilities | Production JIT and additional native targets, async, generation/reflection, interop, scalable SIMD | Workload-specific correctness, ABI, lifetime, servicing and measured cost evidence |

Do not implement all stages simultaneously. The source-class/`ManagedNode`
readonly/writable and mutable-class-receiver batch is validated. It preserves
historical authority when normalizing private managed artifacts; primitive
buffer/view access, mutable struct receiver writeback, and the `out`
declaration/call migration to `init ref<T>` are scheduled separately.
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

Semantic version 24 now carries the managed access capability contract for new
managed programs. Readers normalize private managed artifacts from versions
18 through 23 into qualified internal IR without changing their justified
capabilities or widening the private managed-state/scalar-only export
boundary. Nonmanaged programs retain their historical versions and bytes.

General `ref<T>`/`ptr<T>`/`box<T>`/`val<T>`, `write` view, `own<T>`, generic
access-constraint and `init ref<T>` invocation grammar each require their
respective representation, lifetime, cleanup, ABI and regression work. They
are scheduled implementation work, not part of the active class batch and not
items to park under Later. Invocation syntax for `init ref<T>` remains open.
The immediate next coherent work extends the verified native corpus and its
semantic checks, not a plugin loader or wholesale translation of every design
sample. Stabilize syntax accessors while extending the grammar; keep
diagnostics/source mappings usable on incomplete input.

Within Rich libraries, define operation-specific checked witnesses for value
equality, identity where supported, key equality paired with a compatible hash,
invariant machine-readable formatting, and display/debug rendering. These are
opt-in operations alongside primitive built-ins, not universal root methods or
an obligation to make every value stringifiable. This library/witness work does
not decide whether Terrum has a root type.

Open design decisions are centralized in [language](language.md),
[lifetimes](lifetimes.md), [type families](type-families.md),
[conversions](conversions.md) and [libraries](libraries.md). In particular, generic
member-contract refinement, stored-view ABI, inline class eligibility,
exception/low-memory ABI, family/conditional-conformance grammar and custom
participation, text representation, explicit async affinity/scheduler syntax,
async start policy, floating-destination conversion policies, scalable
sharing ABI and final construction/annotation spellings remain open. None should
be silently fixed by a convenient host or machine instruction.
