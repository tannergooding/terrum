# Static proof and constant-evaluation follow-up

**Status: the portable proof floor, deterministic constant execution, effect
boundaries and default resource envelopes are accepted. Remaining physical
records, intrinsic registrations and beyond-floor extensions are not
admission or implementation promises.**

## Accepted contracts

The canonical
[proof and constant-execution chapter](../specs/language/proof-and-constant-evaluation.md)
defines scalar facts, predicate transfer, joins, conservative loop handling,
invalidation, evaluator eligibility, deterministic accounting and the portable
resource envelopes. The
[published-predicate and pure-evaluation chapter](../specs/language/published-predicates-and-effects.md)
defines input-state reads/effects, Pure/ConstEval forms, mode queries, NoAlias
and declared guarantees. Those decisions are no longer proposals.

The [specified conformance cases](../specs/conformance/source-proof-and-constant-evaluation.md)
are requirements, not evidence that the current compiler passes them.
[Hosted support](../implementation.md#hosted-compiler) remains a bounded
implementation: exact local branch predicates and short-circuit facts, selected
floating predicates, and a scalar evaluator with private 4,096-step/64-frame
limits. These limits and exclusions do not redefine the accepted floor.

## Remaining integration and extension work

Bulk intrinsic and literal-parser registrations still need their concrete
identities, admitted input transformations and exact resource recipes.
A native method name or one cheap helper step cannot supply that contract.
Persistent constant programs, target constant-value codecs and independently
consumed predicate/effect/dependency records require complete physical
representations under the
[logical record requirements](../specs/representation/logical-records.md).
No wire identifier or codec is assigned here.

General relational solving, induction, private-call inference and
alias-sensitive numeric analysis are sound optional extensions, not minimum
floor guarantees. Managed graphs, native resources and identity-bearing
constant values require separately admitted execution and representation
features. Wall-clock cancellation remains a tooling failure, never an
alternative proof or constant result.
