# Self-host frontend verification-budget proposal

**Status: unapproved, deferred architecture option.** The scalar frontend
currently compiles through projection v64 under the unchanged shared
100,000-visit verification limit. This proposal does not change language
semantics, the accepted source grammar, native ABI, or verification budget.

## Evidence

An initial mechanical attempt to add hosted `name = _;` lifetime ending to
the authored scalar frontend failed with `TRM3016` while verifying the
frontend's own Terrum source. The hot path included repeated recursive
`CanBe` feasibility proofs over Boolean expressions while flow analysis
cloned initialization states.

The verifier now memoizes both feasibility outcomes by bound-expression
identity within one function's verification. The cache is shared by flow
state clones; recursive cache hits still consume a visit, so the limit and
budget accounting remain unchanged. With this change, projection v60 and its
source-bound reader compile, and the frontend builds and executes the lifetime
ending port under both runtime policies. No visit-count telemetry or
before/after timing benchmark has been collected, so the evidence establishes
budget feasibility for this workload, not a general speedup magnitude.

## Deferred option

Do not split the frontend or add an internal phase handoff for the current
budget issue; the cache removes the observed blocker without a new component
boundary. If future aligned frontend growth again reaches `TRM3016` after
hot-path identification and behavior-preserving repeated-work reductions,
consider partitioning independently verified bounded components while
retaining the existing limit per verification. Moving methods between files
alone would not split a compilation/verification unit.

Any future partition must preserve existing source diagnostics, projection
facts, AOT/JIT output and runtime-policy behavior. Its handoff would remain an
implementation detail of the hosted-verified components, not a language-level
separate-compilation or module-ABI contract.
