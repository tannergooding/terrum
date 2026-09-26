# Development roadmap

The current milestone is a small checked native compiler, not the entire language.
The [language](language.md) defines accepted semantic direction, while
[implementation](implementation.md) records actual support and tool contracts.

| Stage | Deliverable | Exit evidence |
|---|---|---|
| Syntax foundation (implemented) | Source, Unicode lexer, lossless CST, recovery, CLI, content-bound edits | Public API/CLI tests for round trips, positions, malformed input, numeric boundaries and stale edits |
| Integer binding and native AOT (implemented) | Typed functions/locals, contextual literals, structured control flow, mandatory verifier, direct Windows x64 PE emission | Positive/negative semantic tests, real native execution, checked failures, stack arguments, imports/unwind inspection and safe artifact replacement |
| Bounded values and compiler components (current) | Checked signed/unsigned/code-unit numerics, nested aggregates up to 16 bytes, explicit places, conditional outputs, checked host buffers | Executed range/character/token/scanner candidates, exact token kinds and lossless trivia, shared pinned Unicode fixtures, boundary and invalid-state tests, independent C direct/indirect aggregate/out caller |
| Early JIT and module proofs (current) | Shared x64 encoder, W^X/unwind lifetime, independent library/reference binding, versioned semantic contracts and native packages | AOT/JIT agreement; unchanged semantic/native consumer against compatible producer replacement; incompatible public contracts rejected before execution |
| Full runtime and structured contracts (current) | Checked NoThrow and DoesNotReturn declarations; normal/exceptional `try`/`finally` funclets, unsafe bounded catch; saved-RBP source frames and shared PE/JIT scope/unwind records; remaining general exception, roots and runtime contracts | Effect/normal-continuation source, IR and module checks; normal cleanup ordering and return snapshots; independent C frame/unwind proof; bounded checked failure injection, exceptional unwind and catch in AOT/JIT; root verification still outstanding |
| Resources and graphs (bounded ownership underway) | Executable compact unique POD owners, named moves, readonly synchronous receiver loans and lexical NoThrow cleanup; owning graphs, native resource adapters and managed handles remain open | AOT/JIT and module ownership contracts, checked unwind and deterministic cleanup traces; retained-node/clear/reinsertion and GC stress fixtures remain outstanding |
| General separate compilation | Rich LMOD contracts, native DLL imports, resilient layouts and independent deployment | Larger contract/layout evolution, side-by-side identities and servicing without whole-image relinking |
| Rich libraries and self-hosting | Static protocols/generic math, views/builders, candidate compiler pieces | Executed shared host/self-host fixtures, not parser-only bootstrap claims |
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
