# Terrum compiler transition and bootstrap components

This inventory separates the intended compiler architecture from current
Terrum-authored implementation experiments. Component limits and prototype
buffers below are not source-language or portable-format limits.

## Compiler boundary

The C#-authored and Terrum-authored compilers are standalone implementations.
They do not exchange a Terrum-produced AST or partial semantic projection.
While the language is changing, the C# compiler remains maintained alongside
the Terrum compiler. When the Terrum compiler cannot compile a source file,
the transition path is to invoke the C# compiler as a separate process on the
original source. Keep both implementations in sync; the fallback is
transitional and is removed when the Terrum compiler is self-sustaining after
GA. Future Terrum features can be bootstrapped using the preceding stable
compiler.

The process-level fallback is an architectural decision, not evidence that
the current prototype has wired it. The [compiler conformance handoff](conformance-gaps.md)
tracks that work. A profile miss must not make otherwise valid Terrum source
invalid.

## Current implementation experiments

| Component | Current evidence | Boundary |
|---|---|---|
| Direct-source bootstrap emitter | Test components parse a narrow `int32 Main`/helper subset and produce Windows x64 code/PE fixtures. | A development slice of the Terrum compiler, not a language subset or general compiler. Unsupported source belongs on the whole-source C# process path. |
| Scalar frontend projection | Prototype Terrum code writes private v87/v88 buffers that a C# host currently audits, verifies, and emits. | This AST/projection handoff is not the selected compiler architecture. Do not use it as the Terrum-to-C# fallback or as an accepted persistent profile. |
| Scanner, token snapshots, and spans | Terrum-authored scanner/token components run under hosted verification and native JIT/AOT test paths. | Token values and `Span<Token>` views are runtime data. Keep views compilation-local; no TokenCursor wrapper, private persisted Span IR, `Host.Source` grant, or persisted scanner profile is selected. Existing source-grant execution is prototype evidence, not the compiler input/JIT boundary. |

The component inventory distinguishes current code from the intended
standalone compiler path. Prototypes may remain as test fixtures while the
Terrum compiler grows, but their narrower recognizers and buffers do not
establish source, metadata, or compiler-architecture limits.

## Counts, IDs, and resource bounds

For in-memory frontend records, use `int32` counts and IDs with nonnegative
valid values; `-1` may represent an explicitly absent ID. Span lengths and
offsets follow the `nint`-based Span contract. Size storage from validated
counts rather than fixed 32-function/64-node banks. When an identity becomes
a metadata row, use the independent per-table row limit; source-level aliases
that do not emit rows do not inherit that limit.

Use explicit `int32` parser/evaluation stacks sized from the source and
checked resources. A measured default safety budget may be supplied by the
caller and raised for a justified workload; it is not a grammar nesting rule.
The hosted verifier uses cached per-function facts and budgets rather than a
single cumulative visit ceiling. At a call, consume the callee's published
contract, not facts discovered by inspecting its body. JIT/AOT and metadata
writers may maintain private whole-program or whole-module analysis facts in
compiler state, but must not persist them as contract guarantees.

## Control flow and PE publication

Switch-arm transfers are ordinary CFG edges. Valid forward/backward transfers
and cycles remain valid, including inside `try`, `catch`, or `finally`.
An infinite user loop is noncompletion, not a cleanup error; `finally` runs
when control exits its region. The verifier checks targets, effects,
ownership, cleanup, and exception edges without proving user-authored loops
terminate. A distinct failure from a `finally` propagates outward under the
ordinary exception contract and does not re-enter that same handler.

For the direct-source PE experiment, code bytes may be staged internally in
memory or streamed into a temporary image. The composed operation publishes
only a complete PE; failure discards temporary output. It does not expose a
separate standalone code blob or preserve code as a successful result when PE
creation fails.

## Current migration gaps

The existing C# host and test prototypes still contain fixed v87/v88
projection buffers, low count/depth caps, private Span<Token> serialization,
and separate code/image buffers. The C# `IndexedBody.Verify` path also shares
a 100,000-visit counter across functions. These are implementation facts to
migrate or remove, not the selected design. Track replacement of the
projection path, source-level C# subprocess fallback, count-sized Terrum
frontend records, per-function verification summaries, and atomic PE publication in
[CG-015](conformance-gaps.md#cg-015), [CG-016](conformance-gaps.md#cg-016),
and [CG-017](conformance-gaps.md#cg-017). Existing tests that assert the
prototype layouts are not acceptance evidence for the revised architecture.
