# Checked-output normal-outcome selectors

**Status: unapproved; provisional implementation direction only under the
user's authorized missing-contract workflow.** This recommendation does not
approve a schema, change a normative owner or establish compiler support.

## Decision and scope

Extend the candidate **`17/1=GeneralPredicateGraphs`** to general
`phaseTag=3=NormalOutcomeSelector` graphs used by ordinary checked
`OutputState` relations. Reuse the
[general graph schema](general-predicate-graph-schema.md); allocate no new
Feature, revision, section, clause, node, operand or Body instruction.

This closes only its explicit restriction of general graphs to phases 1/2.
The accepted [conditional-output authoring contract](../specs/grammar-spec.md#58-postcondition-syntax-for-conditional-output-state)
already permits compound conditions and relations to unchanged inputs.
Preserve general phase-1/2 assertions and the
[bounded Feature composition](predicate-feature-composition.md#exact-predicate-admission-boundary).
The [six result-only selectors](aggregate-native-outputs.md#exact-shared-predicate-codec)
remain the entire `10/1` registration. This is normal-invocation publication,
not a retargeting to task/cursor or deferred completion.

**Choose publication relations, not another runtime predicate pass.** A
phase-3 graph identifies the condition of a normal-publication guarantee;
it is neither an assertion that the condition is true nor an instruction
to replay an authored postcondition. An additional general evaluation pass
would add calls, failures and lifetime demands absent from the source model.

## Exact attachment and capability delta

* Retain the 20-byte `000B` attachment, its unique MethodDef owner, typed
  `0037` graph reference, and contiguous zero-based phase-local ordinals.
  Retain `0002`'s 20-byte shape, formal ordinal, typed graph reference and
  states `0=vacant`, `1=complete`. Every OutputState reference selects a
  phase-3 attachment of that **same** MethodDef.
* Under `17/1`, a general phase-3 graph has a built-in-bool value root and
  uses the existing general node/operand/type/footprint schemas. It cannot
  embed node kinds `1..6`; those still require the complete one-node
  `10/1` selector shape.
* Each general phase-3 graph must serve at least one checked OutputState
  row. Several outputs of its owner may reference it. The destination domain
  remains the existing admitted `ref<init T>` and whole-range `Span<init T>`
  formals, including their existing complete-type rules and Param mode `00`.
  This does not admit new parameter, region or callable roles.
* NativeOutput raw-cell rows and TypeSpec `0x0D` signature anchors may use
  **only** the bounded selectors, even when their Module requires 17.
  Address formation/adaptation cannot erase a general checked-output
  contract to make an anchor admissible.
* Consumers classify each phase-3 graph by its exact shape: the six
  result-only selectors require `10/1`; any general occurrence-tree selector
  requires `17/1`. Feature `10/1` supplied through `17/1` does not broaden
  the selector-only registration.
* Canonical phase-3 ordinals place reused bounded selectors first, in their
  existing unsigned-nodeKind order, then general selectors in the origin
  order defined by the [derivation rule](#candidate-derivation-from-checked-ensures).
  Source-free writers retain that logical order. Physical row order is not
  identity or a new runtime execution order.

| Retained use | Effective requirement |
|---|---|
| Closed result-only selector | `10/1`, unchanged, immediate prerequisites `[]` |
| General checked-output selector | `17/1`, unchanged immediate prerequisites `[(10,1)]` |
| Scalar literal in that graph | Additionally the selected scalar `11/1` registration, even on an unreached branch |
| Other type, call, witness, immutable-data, layout or constant-execution use | Its existing complete prerequisites; 17 does not supply them |

No Feature 8/native requirement follows merely from a checked output.
Requirements remain per using Module, including retained expectations and
uncalled declarations. Select the general graph bundle **as amended here**
as one exact schema/catalog out of band under
[format versioning](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts):
recognizing `17/1` or unchanged `(1,0)` bytes does not establish support for
this expanded applicability.

## Observation, evaluation and lifetime

The relation applies only to normal publication, after the saved return, body
`finally`/cleanup, ordered phase-2 checks and their phase cleanup. It describes
the then-valid subjects, not earlier values reconstructed from retired callee
storage. The graph is metadata for call-site reasoning, not a provider
instruction or second assertion; the provider verifies the relation against
source and does not run a phase-3 pass after returning. A `void` invocation
still has its ordinary unit result; input-only selectors need not observe it.

Reuse the general schema's
[observation footprints](general-predicate-graph-schema.md#actual-observation-footprints)
and pure-expression semantics: runtime true/false mode, required constant
evaluation true/true mode under its separate admission gates. Receiver/input
value and reachable state, saved result, mode and independently admitted
immutable data require surviving identities, access and backing.
An altered by-value formal cannot stand for its staged caller argument.
Consumed inputs, released guards, dead backing and implicit entry snapshots
cannot supply publication observations.

Root `4=logical output` is **not** admitted in a selector. Directly or through
input aliases/helper read bounds, it cannot observe the owner's initialization
destinations, including their state. This prevents circular classification;
output-dependent selectors remain outside this candidate.
An authored phase-2 ensure may still inspect an output under its existing
checked rules. Root `8=PhaseCapture` is also excluded: no earlier-selector or
phase-2 capture is a surviving public subject. Same-graph captures retain
their ordinary scoped rules; their names, holders and evidence do not escape.

Phase-3 metadata schedules no provider-side evaluation. A caller evaluates
only its own ordinary discriminator occurrence, once, or uses an equivalent
surviving fact; it does not replay a provider's ensure or repeat getters,
helpers, construction or cleanup to recover a value. Total tests over retained
scalars require existing source equivalence. A caller test discharges the
relation only with matching semantics and unchanged observations; ordinary-
form lookalikes do not acquire purity. Unknown truth supplies no refinement.
Runtime state bookkeeping/cleanup retains actual availability or valid
observations, not vacant T reads or a new public success flag.

Later clauses, cleanup, mutation, aliases, moves and replacement participate
in the existing [correlation invalidation rules](../specs/language/initialization-and-flow.md#outcome-correlations-and-invalidation).
A guarantee that does not survive diagnoses; replay or lifetime extension
cannot repair it. Normally false selectors merely leave their rows inactive.
False ensure and actual predicate/cleanup failures keep their existing
channels. Escaping failure, divergence and DoesNotReturn provide no normal
publication relation, exceptional-state guarantee, rollback or repeated cleanup.

## State coverage and source verification

Each row means: **on normal publication, if P holds, this exact output
generation has state S**. False P implies neither vacancy nor completeness.
For one output, overlapping predicates may promise the same state;
opposite-state predicates must be disjoint on admitted normal outcomes.
Duplicate occurrence keys still reject. Exhaustiveness is required when the
effective contract promises a complete partition, not merely because rows exist.

An explicitly conditional output with no applicable row has no state
guarantee from these rows: retain maybe-live/unknown availability and its
ordinary observation/cleanup obligations. Do not fall back to unconditional
completion or infer a vacant complement. Sparse coverage cannot authorize
publication of an undiscriminated maybe-live owner. No normal outcome may
leak partial initialization or unsettled acquisitions. State 1 includes value
validity, ownership and dependencies; Span state 1 covers every element.

Normalize [source defaults](../specs/language-contracts.md#22-declared-outcome-relationships)
once into the effective per-output contract. Boolean-success defaults retain
true/complete and false/vacant rows using the narrow codec. Non-Boolean
unconditional completion needs no conditional row. Authored replacements
retain their checked meaning, not an overlay of contradictory defaults.

Source/public-bound-program verification must establish legal predicate
reads, purity/accessibility, stable subjects, every promised state and any
required coverage/disjointness. Unestablished safety or unsupported required
relations reject; private body behavior is not a vacant-arm contract.
Preserve these facts through shared traversal, verification and lowering.

## Candidate derivation from checked ensures

The accepted contracts define truth, flow and publication obligations, but
do **not** select a unique P: equivalent sufficient conditions can have
different expression occurrences. Recommend the following deterministic
**declaration-local Boolean projection**, not a prover-selected predicate.
This is an additional unapproved normalization decision, with no new syntax.

1. **Take declaration inputs, not implementation paths.** Use the effective
   per-output contract after source-default/replacement checking and the
   checked, unoptimized bound `ensure` trees. Analyze each clause separately
   in declaration order. Earlier declaration facts may establish access and
   availability; a later false clause or a private body that never returns
   cannot erase an earlier relation or an adopted default. Keep the original
   phase-2 trees, including their checks, captures and cleanup. Projection
   does not decide that a clause replaces a default merely because it
   mentions a destination.
2. **Identify public guard occurrences.** A guard atom is a maximal
   output-free Boolean subtree with a complete, self-contained recipe under
   this proposal's subject rules. Assign origin
   `(ensure ordinal, root-to-node child-ordinal path)` before optimization;
   children follow the general schema's evaluation order. Preserve selected
   operations, policies, calls, forms, arguments and read dependencies.
   Equal-looking calls remain different atoms; only checked copies of the
   same surviving Boolean observation may share an origin. Same-graph
   bindings are cloned with their definitions; a retired/cross-clause capture
   is not substituted by its name or guessed producing expression.
3. **Compute guarded publication facts.** For each assignment of the public
   atoms, traverse the clause's finite Boolean/pattern truth cases, treating
   other tests as unknown observations. Apply `!`, conditional, short-circuit
   and eager truth rules with their actual evaluation and binding order;
   do not inline a helper's private body or prune using its current returns.
   Start authored state reasoning at unknown, not a private body state.
   Keep successful true cases and their declaration-derived output facts
   that survive phase cleanup.
   Legal whole-T reads imply completeness on every reached successful read,
   not just a true comparison; reading one live field is not a whole-T read.
   A false `Initialized` test means
   **not complete**, not vacant: state 0 additionally needs checked absence
   or settlement of all partial acquisitions at publication. A descriptor
   test is not a destination test. Body proofs validate these obligations
   but do not replace unknown declaration facts with today's private writes.
   Failed/divergent evaluation is not Boolean false or a normal case.

For a formal and state S, the resulting Boolean function is exactly:

```text
P_S(assignment) is true iff
    at least one successful declaration case is compatible, and
    every compatible successful case establishes published state S.
Otherwise P_S(assignment) is false.
```

Unknown state counts against both states. Thus eliminating an output/private
test is a meet across its possible successful cases, not deletion of an
inconvenient guard. This finite Boolean abstraction need not solve arbitrary
numeric, alias or helper implications. Exhausted/unsupported required work
declines emission; an incomplete case enumeration is not a weaker success.

4. **Emit an occurrence tree, not a trace flag.** Order atoms by their source
   origins and construct an ordered conditional tree for P_S by Boolean
   cofactors. On each tree path an atom occurs at most once. Mutually
   exclusive copies still get distinct node/operand/capture rows; no shared
   expression nodes or synthetic public observation IDs are introduced.
   Equal residual branches may be removed only with capture/domain validity
   preserved. For `C(G,T,F)`, the conditional selecting T on G true, apply
   the following reductions in listed order after that equal-branch rule;
   `&&`/`||` denote existing AndThen/OrElse nodes, not source rewrites:

```text
C(G,true,false) -> G             C(G,false,true) -> !G
C(G,T,false)    -> G && T        C(G,true,F)     -> G || F
C(G,false,F)    -> !G && F        C(G,T,true)     -> !G || T
Otherwise retain Conditional(G,T,F).
```

   Do not rewrite operations inside an atom. Remaining constants use the
   existing typed scalar literal codec and its Feature 11 requirement.
   Check the **derived tree's** guards and backing:
   an eliminated output guard cannot authorize a newly exposed division,
   dereference or helper domain. Neither provider nor caller runs this tree
   merely because metadata contains it.
5. **Publish exact retained consequences.** False functions contribute no
   row, not a vacant arm. Unconditional completeness uses its existing
   no-conditional-row encoding; unconditional vacancy needs a true
   predicate/state-0 row. Reuse `10/1` only if the entire normalized tree is
   exactly one of its six templates; otherwise retain `17/1`. Share graphs
   only for the same derived recipe, occurrence origins and complete owner
   context, never merely equal helper names. Order general relations by
   their earliest contributing state-establishing source occurrence, then
   successful trace order (true edge before false), formal ordinal and
   state. Coalesce repeated identical guard/output/state consequences and
   assign contiguous phase-3 ordinals after the bounded selector group.

The producer checks the resulting relations against every effective source
obligation, including default retention, coverage, disjointness and survival.
Projection is not permission to drop an unrepresentable authored relation:
if no eligible tree preserves a required conditional contract, decline the
affected Standard path rather than emit an empty range meaning unconditional
completion, fabricate a false marker, or narrow the caller's domain.
These are representation/support failures, not new source restrictions.

Here I denotes a whole destination `Initialized` observation and P an
eligible source guard; the notation describes bound expressions, not new
source forms. Negative-state entries assume the required vacancy proof:

| Authored Boolean relation | Derived additional state consequences |
|---|---|
| `P == I` | P/complete, !P/vacant |
| `P != I` | P/vacant, !P/complete |
| `P ? I : true`, or `!P || I` | P/complete only |
| `P || I` | !P/complete only |
| `I || P` | !P/complete only **if** P remains independently publishable; no output-guard hoisting |
| `I || true` | No state consequence; mentioning a destination does not cancel an adopted default |
| Legal `*destination > 0` | Complete on every successful normal clause case; keep the value guarantee in phase 2 |

The narrower alternative is a syntax-pattern catalog recognizing only the
first few forms; it misses ordinary composition and is not selected.
Arbitrary semantic guard synthesis could establish more, but needs a
separate canonical-choice/equivalence contract. Optional stronger proofs may
validate this projection; they cannot silently choose another P from private
code or change the selected baseline's occurrence identities.

For example, this is accepted authoring syntax with a **proposed
GeneralPredicateGraphsDraft mapping**, not an executable-support claim:

```terrum
void MaybeFill(bool enabled, bool ready, ref<init int32> destination)
    ensure (enabled && ready) == (*destination is init)
{
    if (enabled && ready) { *destination = 42; }
}
```

Publish general selector P over input ordinals 0/1 and its complement, with
OutputState `(formal 2, P, 1)` and `(formal 2, !P, 0)`. Keep the authored
ensure as a distinct phase-2 assertion. With only
`ensure !(enabled && ready) || *destination is init`, publish P/complete,
not an invented !P/vacant promise.

## Consumers, binding and remapping

Consumers validate exact schema/support, owner/range/ordinal/key rules,
graph and footprint ownership, bool roots, reference namespaces, selected
types/forms/read bounds, eligible destinations and complete requirement
closure before use. They do not re-prove body truth or general
coverage/disjointness; structural contradictions cannot be discarded and
unsupported predicates are not false.

Binding preserves each expectation's conditions, state, subject/generation,
publication phase, dependencies and ownership. Exact normalized graph
identity under rebasing suffices for equality; otherwise establish directed
satisfaction or decline. Narrowing the provider's condition cannot drop a
guarantee; changing promised vacancy to completion is not strengthening.

Reuse the general schema's
[typed transformation rules](general-predicate-graph-schema.md#canonicality-and-transformations),
including every OutputState predicate reference, owner/formal map and mixed
phase-3 ordinal sequence. Clone owner-sensitive graphs rather than sharing
another MethodDef's subjects. Preserve originating observations and actual
Body value/place/generation associations without serializing transient proof
flags and rebuild ranges, offsets and per-Module requirements. Version
relabeling or Effects-only fallback is not conversion.

## Focused candidate conformance

These are required future cases, not qualification results.

| Case | Required outcome |
|---|---|
| Boolean default and non-Boolean unconditional output | Preserve narrow true/false rows or absence of conditional rows respectively; neither requires 17 merely for that default. |
| Example's P/!P partition; one-sided variant | First publishes complete/vacant on the selected arms; second gives no false-arm state guarantee. Both use 17/10 without a selector literal or Feature 8. |
| Derivation truth cases | Exercise Boolean equality/inequality, both disjunction orders, conditional and eager operators; derive the table above without changing source evaluation order. |
| Partial/descriptor/field observations | False Initialized on a partial aggregate supplies no vacant fact; descriptor or one-field initialization supplies no whole referent completeness. Require settlement before any state-0 row. |
| Projection removes an output guard | A total scalar P may remain a classifier; reject an exposed unproved dereference/division/helper domain, not a stronger caller precondition or new evaluation. |
| Origins and private proofs | Keep two independent identical helper occurrences distinct, permit a checked saved-Boolean copy, and retain declared relations/defaults despite private body reachability or a later false ensure. |
| Unrepresentable conditional consequence | A required output-dependent/cross-clause-capture relation cannot become an empty unconditional contract, a false marker or an Effects-only import. |
| General scalar status set with literals | Require 17 plus the selected 11; missing literal support rejects even on a guarded, unexecuted branch. |
| Same-state overlap; possible complete/vacant overlap | Permit the former; source/producer verification rejects the latter or unestablished required disjointness/coverage. |
| Ordered and skipped predicate occurrences | Preserve actual short-circuit calls, once-only observation and cleanup; an extra publication-time helper/getter invocation fails conformance. |
| False selector, false ensure, body/phase cleanup failure | Distinguish inactive relation from actual failure; publish no exceptional guarantee and replay no cleanup. |
| Output alias read, phase capture, moved input, released guard; stale caller copy | Reject unavailable/circular subjects; output writes invalidate every related saved copy, while reassignment invalidates only that discriminator's version. |
| Wrong phase/owner/formal/type, duplicate key, malformed graph or missing 17 | Reject before affected use, including uncalled expectations; phase 2 is not a selector. |
| General selectors beside narrow outputs/anchors, including packed Modules | Retain each closure independently; 17 cannot admit general raw NativeOutput or anchor predicates or repair another Module's missing requirement. |
| Source-free rebinding, row permutation and provider replacement | Preserve complete normalized conditions, types, states and dependencies; reject weakened conditions or vacancy, accept consistent remapping under ordinary/canonical distinctions. |

## Intended owner integration

On later adoption, amend the general proposal's decision, retained-graph,
node-phase, Feature-detection and phase-execution sections by this checked
applicability only. Integrate the candidate source-derivation and occurrence
rules with the producer obligations, not as an inferred existing rule.
Reconcile the stale `000B/0037` assignment projection in the clause owner;
integrate the phase-3 relation and coverage rules into clauses/logical records,
metadata, binding and conformance. Keep the bounded composition and
NativeOutput codec owners unchanged in meaning.
Independent Body, reference, ownership, failure and target support remains
required; this decision supplies none of those missing implementations.
