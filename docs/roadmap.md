# Development roadmap

The current milestone is a small checked native compiler, not the entire language.
The [language](language.md) defines accepted semantic direction, while
[implementation](implementation.md) records actual support and tool contracts.

| Stage | Deliverable | Exit evidence |
|---|---|---|
| Syntax foundation (implemented) | Source, Unicode lexer, lossless CST, recovery, CLI, content-bound edits | Public API/CLI tests for round trips, positions, malformed input, numeric boundaries and stale edits |
| Integer binding and native AOT (implemented) | Typed functions/locals, contextual literals, structured control flow, mandatory verifier, direct Windows x64 PE emission | Positive/negative semantic tests, real native execution, checked failures, stack arguments, imports/unwind inspection and safe artifact replacement |
| Bounded values and compiler components (current) | Checked signed/unsigned/code-unit numerics, nested aggregates up to 16 bytes, explicit places, conditional outputs, checked host buffers | Executed range/character/token/scanner candidates, exact token kinds and lossless trivia, shared pinned Unicode fixtures, boundary and invalid-state tests, independent C direct/indirect aggregate/out caller |
| Early JIT and module proofs (current) | Shared x64 encoder, W^X/unwind lifetime, protected exception continuations, independent library/reference binding, versioned semantic contracts and native packages | AOT/JIT agreement; root-only JIT against unchanged persisted dependency DLLs; unchanged semantic/native consumer against compatible producer replacement; incompatible public contracts rejected before execution |
| Full runtime and structured contracts (current) | Checked NoThrow and DoesNotReturn declarations; normal/exceptional `try`/`finally` funclets, unsafe bounded catch; saved-RBP source frames and shared PE/JIT scope/unwind records; remaining general exception, roots and runtime contracts | Effect/normal-continuation source, IR and module checks; normal cleanup ordering and return snapshots; independent C frame/unwind proof; bounded checked failure injection, exceptional unwind and catch in AOT/JIT; root verification still outstanding |
| Resources and graphs (bounded ownership underway) | Executable compact unique POD and composite owners, whole-owner transfers, synchronous receiver loans, recursive cleanup, an opaque fixed-capacity native `int32` buffer and lexical readonly slices; partial field moves, escaping views, arbitrary owning graphs, broader native resource adapters and managed handles remain open | AOT/JIT and module ownership contracts, checked unwind, reverse actual-acquisition cleanup, initialized-prefix and slice access, independent native allocation-failure and exactly-once-release checks; retained-node/clear/reinsertion and GC stress fixtures remain outstanding |
| General separate compilation (bounded native graphs implemented) | Versioned semantic contracts and persisted EXE/DLL dependency graphs with up to sixteen producers; rich LMOD contracts, automatic version resolution and resilient layouts remain open | Compatible leaf replacement leaves root and intermediate images unchanged; distinct leaf identities can coexist behind separate intermediates; incompatible contracts, dependency closures and PE imports rejected before execution; larger layout evolution remains outstanding |
| Rich libraries and self-hosting | Executed range, scanner, integer, string and source-line components, including an owned line-start index; static protocols/generic math and general views/builders remain open | Shared host/self-host fixtures and binary-searched UTF-16 positions; no parser-only bootstrap or full standard-library claims |
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
