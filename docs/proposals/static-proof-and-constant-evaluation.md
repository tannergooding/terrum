# Static proof and constant-evaluation specification proposal

> [!IMPORTANT]
> The v1 core is accepted and specified in
> [proof and constant execution](../specs/language/proof-and-constant-evaluation.md).
> Remaining extensions below are not additional admission or implementation promises.
> Cross-cutting call/phase boundaries remain in
> [language contracts](../specs/language-contracts.md#4-a-reproducible-proof-and-evaluation-foundation);
> current hosted support is tracked separately in the
> [implementation reference](../implementation.md#hosted-compiler).

## Portable proof floor

The accepted [fact and transfer catalog](../specs/language/proof-and-constant-evaluation.md#scalar-proof-facts)
and [accounting](../specs/language/proof-and-constant-evaluation.md#proof-outcomes-and-accounting)
cover local constants/Boolean relationships, integer bounds/exclusions,
branches/joins, conservative loop reset and invalidation. Mandatory
initialization/pattern flow is separate from optional numeric theorems.
General relational solving, induction, private-call inference and
alias-sensitive numerical extensions are not guaranteed. Any such extension
needs sound rules and must not weaken source semantics.
No additional tree-depth value, proof certificate or wire encoding is adopted.

## Effect footprints and observations

The accepted logical rules are in
[published predicates and pure evaluation](../specs/language/published-predicates-and-effects.md).
They define finite input-state read/effect records, selected Pure/ConstEval
forms and mode queries, initialization results, published predicate admission,
alias invalidation and immutable-data obligations. No `Stable` or read-scope
modifier is required. Pure-form support is not evidence that two input-state
observations are unchanged, and valid-input no-escaping-failure guarantees do
not eliminate precondition or enclosing requirement failures.
The accepted [local scalar invalidation boundary](../specs/language/proof-and-constant-evaluation.md#joins-loops-and-effects)
resets exposed/captured mutable locals that an opaque call may affect, but
preserves independent non-exposed scalar copies.
The accepted no-argument [NoAlias parameter schema](../specs/language/published-predicates-and-effects.md#noalias-parameters)
supplies invocation-wide checked separation without exclusive loans.
General numerical summaries, finer region proofs and physical
predicate/effect/constant records remain separate extension/admission work.

## Deterministic evaluator and proposed budgets

The accepted [constant domain](../specs/language/proof-and-constant-evaluation.md#constant-execution-domain),
[semantic actions](../specs/language/proof-and-constant-evaluation.md#semantic-actions-and-normalization)
and [portable envelope](../specs/language/proof-and-constant-evaluation.md#portable-resource-envelope)
now define the initial evaluator. Eligibility is on the executed path;
arithmetic NaNs have a constant-only deterministic policy. Existing private
implementation limits do not satisfy or redefine that source guarantee.

Bulk intrinsic/registration records still need their concrete identities,
input transformations and exact resource recipes; a native method name
does not supply them. Persistent constant programs, admitted target codecs
and independently consumed dependency records remain separate metadata/ABI
work, not implicit allocations from this proposal.
Managed graphs, native resources and identity-bearing values need separate
constant features. Sound proof extensions do not make those execution domains
eligible. Wall-clock cancellation remains a tool failure, never an alternative
proof/value result.
