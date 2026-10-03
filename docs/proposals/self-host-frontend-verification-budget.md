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
100,000-visit limit. A deliberately extreme source with 20,000 sequential
conditional statements still reports `TRM3016` as a guard that aggregate
analysis remains bounded by that limit. This is not a supported-workload target:
the compiler need not accept pathological source merely because its syntax is
otherwise valid.

Outcome analysis also skips constructing an alternative state when exactly one
result path is feasible. It retains the original traversals and join when both
paths are feasible or neither is feasible. In the generated
`ConstantConditionalKeepsSelectedOutputPublication` regression, six
constant-conditionals each contain an unselected arm with 24 output-effectful
calls. The verifier used 90,164/90,176 visits before pruning and 80,306/80,324
afterward, reducing work by 9,858 visits (about 10.9%) in both generated
source shapes. Both runtime policies and selected arms pass; this is work-count
evidence, not a timing benchmark, and the shared limit remains unchanged.

Nested-output vacating for short-circuit expressions now also skips the
evaluated right operand when exactly one left-result path is feasible, retaining
the original traversal and join when both paths or neither path are feasible.
The `InfeasibleNestedOutputsRemainVacant` regression places 24 output-effectful
calls in a grouped right operand made infeasible by a known-initialized local.
The verifier used 16,838 visits before pruning and 13,792 afterward, a reduction
of 3,046 visits (about 18.1%) under both runtime policies. The temporary counter
was removed; this is work-count evidence, not a timing benchmark, and the
shared limit remains unchanged.

Nested-output vacating for conditionals now skips the infeasible arm when
exactly one condition result is possible, retaining the original traversal and
join when both paths or neither path are feasible. The
`InfeasibleConditionalOutputsRemainVacant` regression places 24
output-effectful calls in the arm made infeasible by a known-initialized local.
The verifier used 16,842 visits before pruning and 13,796 afterward, a reduction
of 3,046 visits (about 18.1%) under both runtime policies. The temporary counter
was removed; this is work-count evidence, not a timing benchmark, and the
shared limit remains unchanged.

State-aware feasibility now propagates through Boolean negation to direct
initialization tests. A grouped-right-operand regression with a known-vacant
local reproduced `TRM3016` before this fix and passes under both runtime
policies now. This is correctness evidence, not a reason to change the visit
budget.

State-aware feasibility also follows initialization-sensitive tests through
short-circuit Boolean operators and conditional expressions, applying each
preceding expression's existing outcome transfer before checking the next
operand or arm. A grouped `(!(ready is init) && true)` condition previously
left an impossible output-effectful operand classified as feasible and failed
with `TRM3016`; it now passes under both runtime policies. A ternary
`(!(ready is init) ? true : false)` form also passes under both policies.
Flow-sensitive analysis is limited to compounds containing initialization
tests, preserving the shared state-independent feasibility cache for ordinary
expressions. The constant-conditional output-publication regression remains
within the unchanged visit limit. This is correctness evidence, not a budget
change or a timing claim.

Optional-pattern feasibility now also recognizes `null` and syntactically
present optional values, including a coalescing expression whose optional
operand is a `BoundOptionalLift`. A short-circuit regression with a
syntactically known-present coalescing result and an output-effectful RHS
reproduced `TRM3016` before the change and passes under both runtime policies
now. The presence fact is structural; unknown optional values retain
conservative feasibility. A complementary `is null` case confirms that the
same known-present fact prunes the impossible `&&` operand without changing
later vacancy. This is correctness evidence only and does not change optional
semantics.

Nested-output vacating now uses the same structural optional-presence fact.
A nested known-present coalesce whose fallback carried an `init ref` output
reproduced `TRM3016` before the change and passes under both runtime policies.
The presence check follows nested coalesce operands and fallback values while
remaining conservative for unknown values. A three-level known-present chain
also reproduced `TRM3016` before the change and passes under both policies.
The fallback remains unevaluated in the verifier's initialization state; the
shared visit budget and optional semantics are unchanged.

Known-presence analysis also accepts conditional optional expressions when
both arms have the same structural presence. An output-effectful `||` arm
after a conditional selecting between two known-present values reproduced
`TRM3016` before this refinement and passes under both policies. These
recursive structural checks charge their work to the existing visit budget.

Nested-output vacating now also checks conditional presence against the
current initialization state. When a constant or `is init` condition makes
one arm infeasible, the verifier analyzes only the selected optional arm and
carries its existing outcome transfer forward. A selected present arm
previously reproduced `TRM3016`; the regression now passes under both runtime
policies. Constant-true and selected-absent cases confirm that the infeasible
arm is skipped and the fallback's initialization effects are retained.
Nested coalesces use the same state-aware presence check. `is some` and
`is null` feasibility also consume that fact through short-circuit operators;
output-effectful impossible Boolean arms pass regression coverage under both
runtime policies. All additional analysis remains within the shared visit
budget and changes no language or optional semantics.

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
