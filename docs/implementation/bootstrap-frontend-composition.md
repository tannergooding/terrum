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

`frontend-build`, `frontend-build-library`, and `frontend-run` invoke a
separate C# compiler process on the original source for every input. They
preserve the source name and selected runtime/arithmetic profiles; library
build selects public top-level functions. No AST, partial semantic projection,
or `Host.Source` buffer crosses this compiler boundary.
`frontend-component` remains an explicit test-only projection prototype; it
does not select executable build, library, JIT, private-module or standard
metadata behavior. The C# compiler is therefore the selected implementation
until the Terrum-authored compiler can process source independently.
The [compiler conformance handoff](conformance-gaps.md) tracks its isolation
and completion.

## Current implementation experiments

| Component | Current evidence | Boundary |
|---|---|---|
| Direct-source bootstrap emitter | Test components parse a narrow `int32 Main`/helper subset and produce Windows x64 code/PE fixtures. | A development slice of the Terrum compiler, not a language subset or general compiler. Unsupported source belongs on the whole-source C# process path. |
| Scalar frontend projection | Prototype Terrum code writes private v87/v88 buffers; the C# host can audit, verify and emit the projection for component tests. | Only the explicitly named `frontend-component` test command retains this path. Build/library/run commands use original-source process compilation and never consume its projection. |
| Scanner, token snapshots, and spans | Terrum-authored scanner/token components run under hosted and native prototype tests with explicit source grants. | `Span<Token>` remains compilation-local and is rejected by private semantic-module readers/writers. Source-grant and invocation-profile APIs remain test-only prototypes; native packages and standard metadata do not admit them, and they are not compiler input or ordinary JIT routes. |

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

The test-only `frontend-component` prototype still contains fixed v87/v88
projection buffers and low count/depth caps; those limits do not constrain the
selected build/library/run commands. Private semantic modules reject Span IR,
while explicitly tagged source-grant profiles remain test/prototype support.
The C# `IndexedBody.Verify` path also shares a 100,000-visit counter across
functions. These are implementation facts to migrate or remove, not the
selected design. Track replacement of the projection prototype, count-sized
Terrum frontend records, per-function verification summaries, and atomic PE publication in
[CG-015](conformance-gaps.md#cg-015), [CG-016](conformance-gaps.md#cg-016),
and [CG-017](conformance-gaps.md#cg-017). Existing tests that assert the
prototype layouts are not acceptance evidence for the revised architecture.
