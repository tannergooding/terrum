# Self-host frontend verification budget

**Status: split/phase handoff deferred by decision.** Prefer profiling and
behavior-preserving reductions of repeated proof work. Reconsider a component
boundary only if representative growth still reaches `TRM3016` after that
work. This proposal does not raise a limit or waive mandatory verification.

## Current mechanism and constraints

The [hosted verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs)
has a 100,000-visit analysis limit. It is an implementation ceiling, not an
accepted portable source/program minimum.

Per-function expression-identity caches share state-independent feasibility,
optional-presence and flow-effect facts across flow-state clones. Cache hits
and recursive work remain charged. Do not cache initialization-dependent
results across states, functions, source revisions or rewritten trees.

Prune only proved infeasible paths. Initialization-sensitive short-circuit
and conditional analysis must apply preceding outcome transfers before
testing later operands. Unknown feasibility/presence stays conservative;
skipping work cannot erase a possible publication, vacancy, cleanup or
exception effect. Avoid constructing unused states only when no consumer
needs their facts.

The [initialization regressions](../../tests/Terrum.CodeAnalysis.Tests/InitReferenceTests.cs)
and [value regressions](../../tests/Terrum.CodeAnalysis.Tests/ValueTests.cs)
exercise correctness and explicit exhaustion. They are not throughput
benchmarks or a guarantee that every syntactically valid program fits.
The [composition inventory](../implementation/bootstrap-frontend-composition.md)
owns current frontend support; historical suite totals and temporary limit
experiments are not continuing qualification evidence.

## Alternatives

1. **Recommended now:** profile the same representative source and policy,
   reduce redundant state-independent proof work, and preserve conservative
   state-dependent transfer. This keeps one verification boundary.
2. **Deferred:** partition independently meaningful, independently verified
   components with an explicit handoff. Moving methods between files alone
   does not split a verification unit, and splitting solely to reset a budget
   is not an architecture justification.
3. **Not selected:** raise the limit. A larger ceiling needs workload/resource
   evidence; passing a larger fixture does not itself justify it.

## Reconsideration and integration

Before changing architecture, record representative inputs, configuration,
runtime policy, final revision, completion/failure and repeated-work hot spots.
An early budget failure is not a like-for-like throughput baseline. No aligned
before/after timing evidence is claimed here.

A future split must preserve source association and diagnostics, projection
facts, initialization/effects, AOT/JIT behavior and runtime policy. Reconstruct
and verify handoff inputs rather than accepting producer facts as trusted.
Retain code/backing/owner obligations across the boundary.

Keep a private handoff separate from a language-level module ABI or standard
TRRM schema. If it becomes a standardized artifact, apply the
[persistence](../specs/representation/persistence.md) and
[resource-accounting](../specs/representation/resource-accounting.md)
contracts, including aggregate work; partitioning must not manufacture
unaccounted root budgets.

Reconsider only with a failing representative workload after repeated-work
reductions, a diagnosed bottleneck, a useful component boundary and evidence
covering final inputs. Acceptance would require equivalent observable
diagnostics/results and negative initialization/cleanup cases under both
policies, explicit resource exhaustion and measured work/latency where a
performance claim is made. None of those requirements accepts a split now.
