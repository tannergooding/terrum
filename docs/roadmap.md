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
| Full runtime and structured contracts (current) | Checked NoThrow and DoesNotReturn declarations; normal/exceptional `try`/`finally` funclets, unsafe bounded catch; saved-RBP source frames and shared PE/JIT scope/unwind records; exact lexical/temporary roots and checked root unwind in the managed-node proof; general exception objects and broader runtime contracts remain open | Effect/normal-continuation source, IR and module checks; cleanup ordering and rooted return snapshots; independent C frame/unwind and escaping-native-catch proofs; bounded checked failure injection; root lifetime and malformed-IR checks across calls and cleanup; general runtime integration remains outstanding |
| Resources and graphs (bounded ownership and tracing implemented) | Compact unique POD/composite owners, whole-owner transfers, receiver loans, recursive cleanup, reusable native `int32` storage and lexical slices; source-defined managed classes with complete-publication construction, optional edges, rooted presence snapshots and descriptor-based cycle tracing; partial field moves, escaping views, arbitrary owning graphs and broader resource adapters remain open | AOT/JIT ownership and exact roots; independent native allocation failure, construction/unwind and release accounting; source-class cycles, heterogeneous edges, shared aliases, pointer-shaped scalar nonroots, and 4,096-object collection/reuse fixtures; broader managed layouts and GC stress remain outstanding |
| General separate compilation (bounded native graphs and catalog resolution implemented) | Versioned semantic contracts, persisted EXE/DLL graphs with up to sixteen producers, unique-compatible selection from explicit local candidates, and private managed-node leaf implementations behind scalar exports; rich LMOD contracts, version-ordering policy and resilient layouts remain open | Compatible replacement preserves root/intermediate images, including frozen older wire formats and a replacement that introduces a private traced heap; distinct leaf identities coexist; ambiguous, incompatible, malformed or incomplete catalogs fail before execution; larger layout evolution remains outstanding |
| Rich libraries and self-hosting | Executed range, scanner, integer, string and source-line components, including an owned line-start index, reusable signed/unsigned decimal formatter and bounded managed token chain; compilation-local static operator protocols and bounded closed generic expressions; general generic algorithms, views/builders and public witnesses remain open | Shared host/self-host fixtures, binary-searched UTF-16 positions, invariant 64-bit formatting boundaries and linked-storage count/version/collection checks; two nominal wrappers execute one constrained quadratic through AOT/JIT; false conformance and undeclared operations reject; no parser-only bootstrap or full standard-library claims |
| Later capabilities | Production JIT and additional native targets, async, generation/reflection, interop, scalable SIMD | Workload-specific correctness, ABI, lifetime, servicing and measured cost evidence |

Do not implement all later stages simultaneously. The immediate next coherent
work extends the verified native corpus and its semantic checks, not a plugin
loader or wholesale translation of every design sample. Stabilize syntax accessors while extending the
grammar; keep diagnostics/source mappings usable on incomplete input.

Open design decisions are centralized in [language](language.md),
[lifetimes](lifetimes.md), [type families](type-families.md),
[conversions](conversions.md) and [libraries](libraries.md). In particular, generic
member-contract refinement, stored-view ABI, inline class eligibility,
exception/low-memory ABI, family/conditional-conformance grammar and custom
participation, text representation, explicit async affinity/scheduler syntax,
async start policy, floating-destination conversion policies, scalable
sharing ABI and final construction/annotation spellings remain open. None should
be silently fixed by a convenient host or machine instruction.
