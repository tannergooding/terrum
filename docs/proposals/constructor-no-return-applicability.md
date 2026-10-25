# Constructor applicability of DoesNotReturn

**Status: unapproved; explicitly provisional implementation direction.**
The authorized missing-contract workflow permits proceeding with the
recommendation below provisionally. This document does not amend the accepted
specification, allocate a representation, or establish compiler conformance.

## Decision and scope

Recommend allowing checked `[DoesNotReturn]` on **instance constructors and
authored static constructors**, despite their lack of a declared source
result type. The promise concerns the selected constructor's actual normal
completion under its existing initialization contract. It does not introduce
a constructor result declaration or replace construction with a `void` call.

This supplements the accepted result-type-independent rule with one missing
declaration category. It preserves ordinary result typing, source validation,
constructor selection, initialization phases, ownership, cleanup, static
triggers, failure policy and binding compatibility. It proposes no new
attribute spelling, effect bit, opcode, constructor ABI or general no-return
redesign.

Explicit exclusion is the smaller implementation patch, but would create a
declaration-category restriction unrelated to whether an invocation can
complete normally. A constructor can already fail or diverge without
publishing a value. Applying the same checked promise is the simpler semantic
direction. Recasting all constructors as source functions returning `T` or
`void` would be a substantive and unnecessary redesign.

## Why the accepted text does not settle this case

| Governing evidence | What it establishes, and what remains missing |
|---|---|
| [Source no-return contracts](../specs/language/expressions-and-statements.md#no-return-contracts): applicable to “a callable with any declared logical result type” | Settles independence from the declared result type. It does not expressly admit or exclude constructors without such a source declaration. |
| [Results, return and fallthrough](../specs/language/expressions-and-statements.md#results-return-and-fallthrough) preserves constructors' distinct completion/publication contracts | A constructor is not simply an ordinary non-unit function or an implicit unit-returning function for every source rule. |
| [Constructor productions and semantics](../specs/grammar-spec.md#31-source-file-structure-and-declarations) omit a result production; an authored static constructor expressly has “no parameters or result type” | Attribute syntax is available, but syntactic placement alone does not establish a particular semantic attribute's applicability. |
| [Contextual result contracts](../specs/language-contracts.md#22-declared-outcome-relationships) use instance-receiver or static-group initialization facts instead of contextual `return` | A hidden source `T` or `void` result cannot be inferred to resolve the applicability question. |
| [Effects ownership](../specs/representation/contract-clauses.md#3-effects) requires construction and initialization to use their actual selected callable contracts | Prevents a TypeDef-wide or inherited effect, but does not expressly select which constructor declarations may publish the no-return bit. Structural MethodDef ownership is not source applicability or producer truth. |
| [TRRM actual completion](../specs/representation/contract-clauses.md#4-doesnotreturn-and-actual-completion) and [ICNR cases](../specs/conformance/il-contract-clauses.md#no-return-signatures-flow-and-calls) cover arbitrary declared results | Preserve signatures and terminal calls, but contain no explicit constructor applicability case. |
| [Requirement construction](../specs/language/requirement-failures.md#4-semantic-ownership-is-not-physical-execution-location) already recognizes a constructor with no normal exit | Establishes meaningful nonreturning construction, not permission to annotate its declaration with this checked guarantee. |

Thus this is an uncovered applicability decision, not an accepted void-only
restriction and not a contradiction resolved by the current rejection test.
The accepted rule for result-bearing callables must proceed independently.

## Proposed source rule and exact integration owners

Add the following paragraph to the source
[No-return contracts](../specs/language/expressions-and-statements.md#no-return-contracts)
section, retaining its existing result-type rule:

> The same checked contract is applicable to instance constructors and authored
> static constructors, although their source declarations have no result type.
> Such a constructor must have no reachable normal completion of its actual
> invocation under its initialization contract. Instance constructor completion
> is distinct from subsequent caller initialization and final publication;
> failure or divergence only in those later operations does not establish
> the constructor's promise. The source declaration retains its constructor
> form, and an instance construction expression retains its ordinary constructed
> type. No receiver-completion fact, completed group, normal output or result
> is published on an absent normal edge.

These are proposed edits, not changes made to the normative owners:

| Owner | Exact integration change |
|---|---|
| [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations), including [static initialization](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage) | Cross-reference the source applicability rule for both constructor forms. Keep both productions and modifier restrictions unchanged. A partial constructor's primary owns this contract; an implementing secondary cannot add or change it. The static primary still requires its separate exact trigger-policy attribute. |
| [Grammar §5.10 construction](../specs/grammar-spec.md#510-properties-and-accessor-local-effects) and [outcome contracts §2.2](../specs/language-contracts.md#22-declared-outcome-relationships) | Clarify that no-return grants no normal receiver/property/group state and no contextual result reference. Existing completion and publication obligations still govern every actual normal completion. |
| [Contract clauses §§2–4](../specs/representation/contract-clauses.md#2-clause-matrix-and-uniqueness) | Explicitly include admitted instance-constructor and authored-static-constructor MethodDefs as Effects subjects. Apply the same bit `0x02` to their actual constructor completion, without a TypeDef default or a new clause. |
| [Metadata semantic contracts](../specs/il-metadata.md#semantic-contracts-and-stable-binding) and [logical static records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending) | Cross-reference constructor applicability. Retain complete signatures and separately required constructor/initialization identities. On a static constructor, Effects belongs to the exact role-bearing MethodDef, not the group or every member. No candidate row or assignment changes. |
| [Binding §§2–3](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions) | Apply existing directed no-return satisfaction to exact constructor identities, preserving kind, owner, substitution, receiver, initialization, phase and cleanup contracts. State explicitly that adding the promise cannot erase an independently promised successful completion. |
| [Body typed operands](../specs/il-instruction-set.md#typed-operand-records) | State that the existing terminal general CallSite rule also covers an admitted constructor target. No operand, record extent, compact-window eligibility or result-selector encoding changes. |
| [Source execution cases](../specs/conformance/source-values-and-execution.md), [static cases](../specs/conformance/source-static-initialization.md), [ownership cases](../specs/conformance/source-ownership-and-cleanup.md) and [clause cases](../specs/conformance/il-contract-clauses.md#no-return-signatures-flow-and-calls) | Integrate the corresponding cases below after approval; retain existing ordinary-constructor and result-bearing no-return cases. |

The proposed paragraph is the sole source applicability owner. Other chapters
apply or link it rather than defining a second constructor-specific effect.
No accepted no-return rule is superseded; the recommendation supersedes the
implementation's constructor exclusion only when implemented under this
explicit provisional direction or after normative adoption.

## Execution, failure and cleanup

**Instance construction.** Check the constructor's actual invocation for
absence of normal completion using the existing no-return rules. Keep argument
staging, declaration field/property initializers, clauses, body and cleanup in
their [existing order](../specs/language-contracts.md#31-the-normal-sequence).
Caller initializer entries and final validation cannot execute after a
nonreturning selected constructor. They nevertheless retain ordinary source
binding and validation; unreachable source is not exempt from checking.

The construction expression remains typed as the constructed type. There is
no bottom conversion, implicit result-discard conversion, dummy value or
initialized destination. Normal-path completeness obligations do not require
fabricating a complete receiver on a path that cannot complete normally.
Actual reads, transfers, references and captures still require their real
initialization, backing, access and ownership facts.

Failure edges remain even when a normal edge is absent. An enclosing handler
may execute and continue after catching that failure; that is not normal
return from the constructor. On defined unwinding, clean entered scopes,
callee-owned arguments and actual partial acquisitions under the existing
[ownership contract](../specs/language/ownership-and-cleanup.md#construction-and-publication).
Preserve reverse actual acquisition order, complete-child cleanup and
pending-failure replacement/chaining. Never invoke the unpublished parent's
complete-value Cleanup, even if its fields happened to be initialized. Do
not roll back unrelated effects, restore moved sources, replay completed
cleanup, or invent definite vacant/complete exceptional output states.
Fail-fast termination promises no unwind. Divergence promises neither
eventual failure nor cleanup of scopes it never exits.

**Static construction.** The promise belongs to the authored static
constructor's invocation, not a blanket property of its type or a substitute
for its initialization policy. Grouped field initializers still precede its
clauses/body. A truthful no-return static constructor never successfully
completes its group. A divergent invocation leaves initialization running;
independent completion demands may remain waiting, while actual cycles retain
the existing cycle-failure rule. An escaping failure performs required partial
cleanup, leaves the group failed/unpublished and delivers the actual event to
the initializing flow. Waiting/later failed demands create their own fresh
InitializationFailedException under the
[existing state machine](../specs/runtime/standard-failures.md#5-static-and-lazy-initialization-failure).
No retry, forced completion, failure suppression or cancellation is added.

Compose a triggering operation with the completion demand it actually makes.
Do not copy DoesNotReturn onto every static member or initialization helper.
A member/access that does not demand this group, or an independently
authorized in-progress initialization operation, retains its own contract.
No new host static-constructor timing or scheduler behavior is selected.

**Library and safety boundaries.** A constructor remains synchronous and
non-extern; instance caller-unsafe obligations still require authorization,
and a static constructor still cannot acquire a caller-unsafe modifier.
No-return does not establish purity, absence of escaping failure, parameter
separation, cleanup termination or any other effect. For
[runtime requirement construction](../specs/language/requirement-failures.md#5-evaluation-output-state-and-responsibility),
a selected no-return exception constructor produces no outer E event:
propagate its actual failure or diverge, without selecting another constructor
or fabricating the requested exception.

## Representation, binding and lowering

Use the existing MethodDef-owned singleton Effects clause `0x0001` with
`u32 mask` and bit `0x02`; its total extent remains 12 bytes. Preserve the
existing occurrence, absence, mask-validation and caller-unsafe rules.
Construction does not make a TypeDef an Effects owner.

Retain the complete admitted constructor signature, receiver/formals, selected
owner and substitution, initialization role, defaults, dependencies and
cleanup facts across binding, private persistence, standard interchange and
rewriting. This proposal does **not** decide an otherwise missing constructor
MethodSig mapping: a required `resultTypeSpec` is not set to zero or rewritten
to `void` merely because no normal result is delivered. Do not promote a
private value-returning or managed-receiver representation into a standard
constructor ABI. Missing required constructor/initialization codecs still
block affected standard use.

Preserve expected no-return guarantees during import resolution and
replacement. Adding the guarantee is allowed only under the existing complete
directed satisfaction rule; it cannot discharge a retained independent success,
state, ownership, failure or phase promise by vacuity. Retain any stronger
constructor fact used to eliminate a normal edge, including facts obtained
through an initialization dependency.

A generated adapter or class factory remains a separate callable. Preserve
any normal allocation/refusal/status outcome it can produce without invoking
the constructor. The constructor's promise terminates paths that actually
invoke it; copying the bit onto a wrapper requires independently establishing
no normal completion of that wrapper's entire invocation.

For a constructor represented by an admitted CallSite, use result selector
None and terminate the Block with no normal successor. Do not use compact
normal-returning windows, synthesize a result/return or append a Fail fallback.
Preserve actual receiver/input preparation and ownership transfer even though
no normal result is published. Expression flow must propagate this terminal
effect through local initialization, arguments, return expressions, conditions
and nested construction, not only direct statement roots. Earlier evaluated
arguments retain cleanup responsibility; later arguments and the enclosing
invocation do not execute after the terminal evaluation.

AOT/JIT lowering preserves those edges and the selected target's existing
call interface; it needs no new native ABI. Rewriters preserve decoded
identities, typed references, maps and source locations rather than altering
coincident bytes. Producers establish source/body truth, including public
bound-IR emission inputs. Consumers perform mandatory structural, support and
binding checks, but need not re-prove a structurally consumable producer
no-return claim; the existing
[trust boundary](../specs/representation/reader-trust.md) is unchanged.
Exchange still requires
[exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts).
No format pair, Feature, role tag or clause assignment is allocated here.

## Proposed conformance cases

These cases are provisional requirements, not reports of executed tests.
Fixtures supply independently admitted storage, failure and callable
contracts. Unsupported prerequisites remain unsupported rather than being
silently replaced by a scalar/private approximation.

| Case | Scenario | Required outcome under this recommendation |
|---|---|---|
| CNR-01 | Instance constructor's reachable paths only diverge or escape failure, including with still-vacant fields | Applicable and provable without a fabricated receiver; every reached read/transfer still needs actual valid state. Cover structs and managed classes, Copy and unique owners, and zero-data fields. |
| CNR-02 | Annotated constructor has a reachable normal constructor return or fallthrough | Reject the false promise; unreachable textual returns do not by themselves violate it. |
| CNR-03 | Constructor returns normally but a caller initializer or final validation always fails | Does not establish constructor DoesNotReturn. |
| CNR-04 | A no-return construction is a direct expression statement or is nested in an argument or local initializer | A direct statement produces no normally unused value. Preserve constructed type and prior effects/owners; no normal destination initialization, later argument, caller initializer, final validation or enclosing invocation. Invalid unreachable source still diagnoses. |
| CNR-05 | Partial construction fails after acquired children, or partial cleanup itself fails | Retire only actual retained acquisitions in reverse acquisition order, complete-child cleanup intact, no parent Cleanup, replay or rollback; retain ordinary failure chaining. Fail-fast retains its no-unwind outcome. |
| CNR-06 | An enclosing handler catches construction failure | Handler may continue under actual exceptional-state rules; constructor acquired no normal return edge or invented initialized output. |
| CNR-07 | Primary/secondary partial constructors or closed generic owner instances carry the promise | One primary contract author; retain exact selected owner, substitution and contract through implementation/binding. An unrelated constructor or secondary cannot supply it. |
| CNR-08 | Instance constructor is caller-unsafe, or is combined with an independent effect bound | Preserve every obligation; DoesNotReturn supplies no unsafe authorization or no-failure promise. Invalid constructor modifiers remain invalid. |
| CNR-09 | Selected zero-supplied exception constructor is no-return | A successful requirement never invokes it; a failed requirement propagates its actual failure/divergence without an outer fabricated E event or fallback. |
| CNR-10 | Authored static constructor is truthfully no-return with a valid explicit trigger policy | No group publication. Divergence remains running/waiting; escaping failure and later failed demands retain exact existing event/state behavior. Non-triggering members do not inherit no-return. |
| CNR-11 | Producer/public IR claims no-return on a normally completing constructor, or erases its partial-cleanup obligations | Producer validation rejects the invalid implementation. Do not convert this into a mandatory consumer semantic re-proof requirement. |
| CNR-12 | Import/replacement removes an expected constructor no-return guarantee or changes its kind/owner/phase/state | Reject. Adding no-return cannot erase another promised successful outcome; preserve relied-on stronger facts after transformations. |
| CNR-13 | Admitted constructor CallSite selects None versus a result register or compact window | None is terminal with no normal result/successor; reject normal-result/window forms for the verified no-return target. Complete signature and required preparations remain. |
| CNR-14 | Constructor identity, initialization role, ownership or target support is missing, malformed or unsupported | Reject affected use before execution; an Effects row alone establishes neither constructor support nor safe cleanup. |
| CNR-15 | A constructor-backed factory can return a normal refusal/status before invoking the no-return constructor | Keep that factory outcome and its complete result contract. Only paths actually invoking the constructor lose normal continuation; the factory does not inherit the promise. |

## Current gaps and prerequisites

The [native support inventory](../implementation/native.md#checked-normal-continuation-contracts)
now records the accepted arbitrary-result source/private-native slice and its
remaining standard CallSite gate. [CG-013](../implementation/conformance-gaps.md#cg-013)
tracks that same implementation boundary. Neither inventory decides
constructor applicability; this candidate remains unapproved.

Implementation evidence is narrower than this recommendation:

- [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) represents
  instance constructors using containing-type/value and managed receiver paths.
  It now suppresses synthesized normal receiver publication for a truthful
  no-return constructor. Shared verification checks its body, and a source/JIT
  regression covers a failing struct constructor used as a direct construction
  statement. The [BoundFunction](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  retains the constructed type and constructor identity. This provisional
  source slice does not establish class/static construction, constructor AOT,
  standard interchange or every nested construction adaptation.
- [NoReturnTests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NoReturnTests.cs)
  now accepts a truthful struct constructor and rejects a normally completing
  false claim. It also exercises non-unit completion and output nonpublication.
  [NoReturnModuleTests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NoReturnModuleTests.cs)
  exercises ordinary non-unit functions through private modules and native
  replacement/execution, not constructor interchange.
- [OwnershipTests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/OwnershipTests.cs)
  includes partial-constructor failure and forged cleanup-region rejection.
  Extend those mechanisms to no-return constructors rather than deleting
  their obligations. These files are included by the
  [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj);
  source inspection here is not a claim that tests ran or passed.
- The [implementation summary](../implementation.md) reports no current TRRM
  static-initialization execution.
  [CG-008](../implementation/conformance-gaps.md#cg-008) and the
  [logical initialization records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending)
  retain separate schema, runtime and qualification gates. This applicability
  decision does not approve their missing physical assignments or broaden the
  initial implementation gate.

Implement the general expression-level no-return machinery first or together
with supported constructor flow, then preserve it through shared bound-tree
rewriting, producer verification, persistence/binding and native lowering.
Standard constructor execution additionally requires its complete admitted
constructor and initialization representation, actual ownership/failure
support and target call interface. Qualify the cases above through public
compiler APIs and independent producer/consumer inputs; supervise any runtime
failure probes and never execute an unbounded divergent fixture unsupervised.

No further applicability choice is left open by the recommendation. Existing
unassigned constructor/static schemas remain prerequisites for those routes,
not tacitly approved designs. Acceptance of this proposal, implementation of a
bounded source slice and end-to-end standard/native qualification remain
separate gates.
