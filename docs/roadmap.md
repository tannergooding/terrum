# Development roadmap

The first milestone is a working syntax front end, not the entire language.
The [language](language.md) defines accepted semantic direction, while
[implementation](implementation.md) records actual support and tool contracts.

| Stage | Deliverable | Exit evidence |
|---|---|---|
| Syntax foundation (current) | Source, Unicode lexer, lossless CST, recovery, CLI, content-bound edits | Public API/CLI tests for round trips, positions, malformed input, numeric boundaries and stale edits |
| Binding kernel | Symbol/type identity, target-typed literals, calls, places, initialized/consumed state, conditional outputs | Positive/negative semantic fixtures, including unresolved numerics and invalid output reads |
| Structured operations | Explicit control/exception/cleanup structure and checked contracts | Mandatory verifier, deterministic dump/round trip, invalid-IR rejection |
| CLI execution | Minimal CLI IL emission, entry host, checked arithmetic | Same corpus under JIT and supported Native AOT; no hidden allocation or semantic fallback |
| Resources and graphs | Checked loans/cleanup, managed handles, list/node identity and guards | Invalidating-operation rejection; retained-node/clear/reinsertion and GC stress fixtures |
| Separate compilation | LMOD contract metadata, baseline comparison, binding and resilient layouts | Unchanged consumer against compatible producer, incompatible replacement rejected, side-by-side identities |
| Rich libraries and self-hosting | Static protocols/generic math, views/builders, candidate compiler pieces | Executed shared host/self-host fixtures, not parser-only bootstrap claims |
| Later capabilities | Async, generation/reflection, interop, scalable SIMD, independent runtime/backend | Workload-specific correctness, ABI, lifetime and measured cost evidence |

Do not implement all later stages simultaneously. The immediate next coherent
slice is binding a small integer/function corpus, not designing a plugin loader
or translating every sample. Stabilize syntax accessors while extending the
grammar; keep diagnostics/source mappings usable on incomplete input.

Open design decisions are centralized in [language](language.md),
[lifetimes](lifetimes.md) and [libraries](libraries.md). In particular, generic
member-contract refinement, stored-view ABI, inline class eligibility,
exception/low-memory ABI, text representation, async start policy, scalable
sharing ABI and final construction/annotation spellings remain open. None should
be silently fixed by a convenient CLR lowering.
