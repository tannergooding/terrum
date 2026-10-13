# Self-host frontend verification-budget proposal

**Status: deferred by decision; unapproved architecture option.**
The scalar frontend composes the current single-function and complete-program
projections under the unchanged shared 100,000-visit verification limit. This
proposal does not change language semantics, the accepted source grammar,
native ABI, or verification budget.

**Decision recorded:** keep a frontend split or phase handoff deferred.
Prefer further caching and hot-spot analysis on real workloads; reconsider a
verification boundary only if representative growth again reaches `TRM3016`
after behavior-preserving repeated-work reductions.

## Current mechanism and evidence

The [shared verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs)
caches state-independent feasibility, optional-presence and flow-effect facts
by expression identity within a function. Flow-state clones share those facts;
state-dependent results are not cached across different initialization states.
Cache hits and recursive analysis remain charged to the existing visit budget.

Outcome computation skips provably infeasible paths and avoids constructing
states with no publication/cleanup consumer. Unknown expressions and unknown
presence retain conservative analysis. Initialization-sensitive short-circuit
and conditional expressions apply each preceding expression's outcome
transfer before testing later operands; pruning cannot erase a possible
publication, cleanup or vacancy effect.

[Initialization regressions](../../tests/Terrum.CodeAnalysis.Tests/InitReferenceTests.cs)
cover selected output publication and infeasible nested/conditional outputs
under both runtime policies. [Value regressions](../../tests/Terrum.CodeAnalysis.Tests/ValueTests.cs)
cover bounded deep Boolean analysis and explicit `TRM3016` exhaustion.
These are correctness and resource-bound checks, not throughput benchmarks
or a guarantee that every syntactically valid program fits the budget.

Use the [verifier history](https://github.com/tannergooding/terrum/commits/main/sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs)
for implementation provenance. No persistent visit-count telemetry or aligned
before/after timing benchmark is retained, so this proposal makes no measured
speedup claim.

For a future budget failure, profile the same representative source and record
whether each run completes. Compare repeated-proof work and hot methods before
changing the limit or considering a component boundary. An early budget
failure is not a like-for-like throughput baseline.

## Deferred option

Do not split the frontend or add an internal phase handoff merely to bypass
the verification budget. If representative frontend growth reaches `TRM3016` after
hot-path identification and behavior-preserving repeated-work reductions,
consider partitioning independently verified bounded components while
retaining the existing limit per verification. Moving methods between files
alone would not split a compilation/verification unit.

Any future partition must preserve existing source diagnostics, projection
facts, AOT/JIT output and runtime-policy behavior. Its handoff would remain an
implementation detail of the hosted-verified components, not a language-level
separate-compilation or module-ABI contract.
