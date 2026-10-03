# Self-host frontend verification-budget proposal

**Status: unapproved, deferred architecture option.** The scalar frontend
composes the current single-function and complete-program projections under
the unchanged shared 100,000-visit verification limit. This proposal does not
change language semantics, the accepted source grammar, native ABI, or
verification budget.

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
ending port under both runtime policies. Replacing only the verifier with its
pre-cache implementation reproduces `TRM3016` against the current frontend
sources; the cached build succeeds. The no-cache run stops early, so its CPU
profile is not a like-for-like throughput baseline. No persistent visit-count
telemetry or before/after timing benchmark is retained.

One short post-cache CPU-sampling trace of `terrumc frontend-component` showed
`VerifyCore` at 18.8% inclusive sampled CPU and both `CanBe` overloads among
the top 100 at 4.43% each. This confirms feasibility checks remain active,
but does not measure cache speedup or establish them as the dominant cost.

Boolean-return outcome analysis is limited to states with consumers. With no
cleanup and no output parameters, no return outcomes are needed; with output
parameters but no cleanup, only the successful outcome is needed for
publication checks. For the bundled frontend and the same
`int32 Main() { return 42; }` input, shared verifier visits fell from 79,243 to
74,554 under fail-fast and from 79,379 to 74,690 under checked-exceptions.
Each reduction is 4,689 visits (about 5.9%). The temporary counter is
removed; these counts measure verifier work, not wall-time.

Outcome-state computation now classifies and caches whether an expression can
change initialization facts. Pure expressions reuse a clone of the input state
instead of recursively reconstructing the same state for each Boolean outcome.
Initialization tests, pattern bindings, output calls, moves and unknown
expression forms retain full traversal. The 40-level alternating `&&`/`||`
analysis fixture now completes successfully instead of exceeding the unchanged
100,000-visit limit. A separate source with 20,000 sequential conditional
statements still reports `TRM3016`, confirming aggregate analysis remains
bounded by that same limit.

During the v78 budget investigation, temporary visit counters measured 100,001
semantic-analysis visits across 210 functions before the limit failure. In the
largest function, `ParseExpressionCore`, output-vacating traversal accounted
for 9,271 visits; caching whether an expression can affect initialization
state reduced that count to 3,150 and the function's total from 19,477 to
14,268. `IsSourceWhitespace` visits fell from 4,627 to 2,959 in the same
profile. The counters were removed after measurement. These historical counts
are work counts, not wall-time or throughput measurements; the pre-cache run
stopped at the budget while the cached run progressed. For a future budget
failure, capture profiles on the same representative source, account for
whether each run completes, and compare hot methods and repeated-proof work
before changing the limit or considering a component boundary.

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
