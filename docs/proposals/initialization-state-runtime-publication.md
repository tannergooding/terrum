# Initialization-state runtime publication

**Status: unapproved; provisional implementation recommendation.** This
document selects a lowering protocol, not normative adoption, compiler
support, a released ABI or qualification evidence. Scope: synchronous checked
initialization references and observable normal-return state in an explicitly
admitted, jointly lowered one-address runtime closure. Support-bearing, GC,
deferred and foreign crossings keep their independent admission/ABI gates.

## Decision and accepted baseline

Recommend **declaration-normalized output relations plus caller-local state
witnesses**, retaining the candidate **one-address checked-output ABI**.
Do not add an outcome-less initialization mode or a hidden state argument.

| Accepted owner | Rule preserved |
|---|---|
| [Structural flow](../specs/language/initialization-and-flow.md#states-reads-and-initialization) | `place is init` observes actual liveness without reading T; maybe-live outgoing edges refine live/vacant. Losing a correlation does not vacate a live value or cancel cleanup. Partial and pending-reserved states are not vacancy. |
| [References](../specs/language/references.md#initialization-references) | `ref<init T>` is a live non-Copy descriptor to one fixed vacant T place. Formation, scoped reborrow, generic T, attenuation and backing obligations remain distinct from descriptor-slot state. Uncompleted capabilities cannot escape into stored containers/results/captures. |
| [Call contracts §§2.2–2.3](../specs/language-contracts.md#22-declared-outcome-relationships) | Boolean-success defaults are true/complete and false/vacant; other results default to complete on every normal return. Authored conditional relations must be checked. The complete init-reference formal identifies the output contract; absence of an authored `ensure` does not mean unspecified output state. |
| [OutputState](../specs/representation/contract-clauses.md#5-outputstate) | State is wholly vacant or complete/publishable, never partial or exceptional. Missing rows mean unconditional completion, not a reader-inferred Boolean default. Producers must preserve normalized source defaults. |
| [Windows x64 §5](../specs/targets/windows-x64-call-interface.md#5-stable-input-borrows-and-initialization) | The admitted call-extent projection passes one actual T address; its declaration publishes normal-outcome states. This assigns neither stored init-capability layout nor general reference ABI. |

**Classification:** the reported wrong result is a source-contract/
implementation defect: marking an output maybe-live permits a legal state
test but cannot implement it. There is also a genuine lowering-protocol
decision—reconstruct state from published outcomes or transport a dynamic
witness. Select reconstruction. This is both a conformance repair and a
runtime-contract completion, **not evidence that another ABI word is necessary**.

### Local evidence, not release qualification

The in-worktree
[`GenericInitReferenceCallRequiresAnExplicitStateCheck`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ReferenceInitializationCapabilityTests.cs)
contains generic `MaybeWrite<T>`, no authored ensure/output relation, and
the caller's saved `completed` followed by `selected is init`. The reported
local `NativeJit.Execute` experiment takes the fallback despite the helper's
write. No fresh run, final compiler/configuration identity or release claim
is established by this document.

Source inspection explains the disconnected paths:
[`ApplyInitializationPostconditionEffects`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs)
first marks checked destinations maybe-initialized, then consumes supported
authored postconditions.
[`NativeEmitter`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs)
lowers `BoundIsInitialized` and `BoundReferenceInitializationTest` through
separate local flags; its legacy `BoundOutArgument` publication updates caller
flags. The
[checked-reference store](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.NativeStorage.cs)
updates the descriptor-local referent flag, not a cross-call caller witness.
[`NativeJit`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs) executes
that emitter's code; it supplies no independent state-test implementation.
The active legacy
[`IsInitTracksScalarPublicationAndLifetime`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/InitReferenceTests.cs)
asserts result 59 under both policies using parameter-only `init ref<T>`.
Its output publication path does not qualify canonical `ref<init T>`.

## Alternatives

| Candidate | Assessment |
|---|---|
| **Normalize declarations; reconstruct caller state from normal results** | Smallest sound route for the existing total selector contracts. Preserves maybe-live testing and generic helpers without changing entry shape. Selected below. |
| Pass `(destinationAddress, stateCellAddress)`, or point at an address/state record | Can support a genuinely unspecified normal state, but requires fixed caller/callee ownership, publication and failure rules, and an intentional replacement of the checked-output candidate's one-address ABI. An ordinary T address alone cannot locate a caller flag. Accepted defaults make this redesign unnecessary for the reproduction. Not selected, including as a body-dependent hidden fallback. |
| Forbid maybe-live tests, require explicit ensures at every call, or ban generic init formals | Would restrict accepted source instead of implementing its contract. Requiring faithful normalization/verification of existing defaults is not such a restriction. Reject these alternatives. |

## Exact candidate protocol

1. **One declaration contract.** Bind defaults before generic-body checking,
   call analysis or address formation. For each canonical init formal,
   produce either unconditional completion or a total, consistent normal
   state map. Normalize the Boolean default to explicit true/live and
   false/vacant selectors; metadata absence retains its unconditional meaning.
   Verify authored bodies on every applicable normal exit, including
   postcondition-phase cleanup. True/vacant, false/live under the default,
   or a no-write `void` helper are defects, not an implicit unknown-output
   mode. The current `RefInitFormationPreservesVacantDestination` test's
   no-write `void` helper needs correction against that accepted default.
2. **Private representation.** Associate observable liveness with the exact
   storage root, structural field path and activation/generation, not the
   descriptor variable or T's bytes. Use a compiler-private 4-byte,
   4-aligned `u32` flag, `0=vacant`, `1=complete`, for an observable two-state
   place unless equivalent state is proved statically. Descriptor-binding
   availability is separate. Partial state needs its actual structural
   state; pending reservations remain unobservable. Neither is encoded as
   ordinary vacant. No flag address, generation token or live mask is
   serialized or exposed as source data.
3. **Formation and local transitions.** `ref init place` evaluates once,
   checks exact type, vacancy, authority, actual alignment/extent and stable
   backing, and associates the capability with that place's state target.
   Exclude conflicting caller/reentrant access and later-argument invalidation
   for the reservation's extent.
   A runtime-selected legal place retains the matching private state target
   through the same CFG selection; never recover it with pointer lookup.
   Complete initialization sets live only after validity is established;
   take/end establishes vacancy. Failed RHS evaluation does not prematurely
   clear state. All permitted aliases observe the same place state.
   Attenuation consumes only init authority; outer write permits subsequent
   legal replacement, not second initialization.
4. **Call and normal publication.** Capture arguments once in source order;
   retain exact argument-to-formal/destination correspondence through later
   arguments. Pass only the actual T address under the selected ABI.
   The callee starts with a live descriptor and vacant referent and maintains
   its private state during execution. Save its return expression before body
   cleanup and postconditions. On normal checked return, capture that ordinary
   result once in the caller, evaluate its normalized selectors, and update
   every captured caller state target before exposing the continuation.
   Unconditional outputs set live.
   This is bookkeeping, not a payload copy-back or multi-output transaction.
   Ignoring the result still requires publication bookkeeping.
5. **Reborrow.** Forwarding `Helper(ref init *destination)` suspends the
   parent reservation and passes the same T address. On normal child return,
   apply the child's map to the parent's destination witness before resuming
   it. No independent reservation, descriptor-slot destination, state-cell
   argument or Copy of init authority is created. Ending the capability
   releases its loan, not T, and never resets an initialized place.
6. **Observation and cleanup.** Lower `place is init`, projected
   `*destination is init`, and already-required guarded retirement from that
   same current place state. A missing witness for a dynamically maybe-live
   observation is a lowering error, never constant false/true. Preserve state
   independently of saved-result correlations when those correlations are
   invalidated. Guard only cleanup required by the selected exit contract,
   in its actual order; add no exceptional Cleanup pass.
7. **Failure.** An unentered call releases only its staged child loan and
   preserves actual prior argument effects. After entry, body/postcondition
   failure has no normal result or state-map publication; caller state is
   indeterminate under existing recovery rules. Do not assign false/vacant,
   roll back writes, infer safe recovery from a flag, or replay cleanup.
   Actual private/partial acquisitions and surviving captures retain their
   selected settlement obligations. DoesNotReturn, divergence and failfast
   produce no normal publication or additional cleanup guarantee.

The first executable relation domain is the
[checked-output candidate's](function-pointer-checked-output-parameters.md#output-selectors-and-the-complete-state-relation)
six bool/int32/uint32 result selectors plus unconditional completion. Check
coverage and agreement over their complete result domains. An accepted
broader conditional contract lacking a supported reconstruction cannot be
silently treated as this subset: preserve its source meaning and refuse the
affected lowering/transport. Do not evaluate an `ensure` again in the caller,
use an output-state test circularly to compute itself, or reread mutable inputs.
No new source rule or general-predicate evaluator is selected here.

## Integration and representation consequences

| Owner/surface | Required integration; unchanged gates |
|---|---|
| Source, shared bound trees and verifier | Use complete reference types and the normalized declaration relation, not legacy passing flags or private-body inference. `ApplyInitializationPostconditionEffects` and saved-result refinement consume that relation; maybe-live remains a sound conservative state only with executable observation. Preserve place/state-target mappings through visitors, rewrites, joins and adapters. |
| Source-generic methods | Check the relation over generic T, then substitute complete types/qualifications and remap formals in the defining context. Recompute closed layout/call plans. `MaybeWrite<T>` needs no authored ensure or type-specific recognizer, and its true result already establishes the default live fact. Direct source specialization gains no new dictionary/state argument; open generic fnptr entries retain their separate exclusion. |
| NativeEmitter / NativeJit / AOT | Share the same publication/state plan. Update canonical place flags after checked stores and calls, not only descriptor-local or legacy output flags. Keep recursive/reentrant activations independent; regenerate all dependent code in the admitted closure. Matching sizes or an old precompiled entry do not establish ABI agreement. |
| [Checked-output fnptr proposal](function-pointer-checked-output-parameters.md#same-runtime-abi-addition) | Replace its vague runtime-bookkeeping paragraph with this protocol; preserve one eight-byte address carrier, ordinary result registers/hidden-result ordering and source-use 5. Direct and indirect calls use the same normalized map. Keep its positive-extent rootless Copy payload restriction; stored-descriptor-valued direct outputs do not become fnptr-admitted. |
| [Stored descriptors](reference-stored-descriptor-layout.md#initialization-is-not-descriptor-copy) | Preserve the exclusion of stored uncompleted init capabilities. Local witness bookkeeping assigns no stored capability layout, root/hold, Blittable property or foreign pointer equivalence. Ordinary descriptor-slot outputs still initialize that slot, not its referent. |
| [Standard clauses](../specs/representation/contract-clauses.md), [registry](../specs/representation/registry.md), [logical records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Reuse TypeSpec `0E`, Param `00`, OutputState `0002` and its shared selector/dependency owners. Add the normal-publication protocol to call/flow owners, not another runtime-state table. Standard producers can lower state observations to existing Boolean/CFG/Home operations with producer-established equivalence; this is not an allocation of a dedicated state-test opcode. Retain all independent type, Body and capability gates. |
| [General graphs](general-predicate-graph-schema.md#patterns-and-typed-captures) / [pure composite](pure-callable-contract-composition.md#complete-logical-callable-record) | Candidate `41 Initialized` observes the same logical place state, distinguishing descriptor and output roots. Phase-3 selectors are not asserted ensures. Preserve the composite's output/dependency/entry obligations and unknown ordinary effects; Pure, rootless Copy or bookkeeping supplies no ordinary NoThrow. General graph execution retains its separate support gates. |
| [Core failure selection](core-contract-failure-cli-selection.md#integration-gates-and-focused-cases) | Default normalization does not synthesize an executable ensure or require a failure-role manifest merely to update flags. Authored clauses retain their exact originating failure/default/constructor bindings and enforcement gates. A graph or manifest neither repairs runtime publication nor grants a new ABI/failure transport. |
| [Private complete-reference companion](checked-reference-private-persistence.md#one-checked-publication-authority-per-declaration) | Reuse its candidate-105 checked selector/state vectors and `PlaceIsInitialized` tag 51; introduce no revision, field or tag here. Preserve complete types, bodyless expectations and original contexts. Reconstruct runtime witnesses after reading; refuse routes lacking the exact selected codec/agreement. |
| Binding and transformation | Check each original expected signature/state/dependency contract against the actual provider; false/live is not a strengthening of false/vacant. Validate schema, extents, references, selector ownership/domain/coverage and support before use; producer truth remains the producer's responsibility, not mandatory consumer body re-proof. Remap typed subjects/formals/maps/source data and regenerate witnesses. No decoder guessing, dropped rows or compatibility aliases. |

**Allocation boundary:** no new field, section, clause, Feature, opcode,
private tag or ABI parameter is required by this candidate. All numeric
candidates above remain owned by their linked proposals, unapproved where
those owners say so. Exact combined draft/schema and runtime agreement remain
required; equal version bytes select neither.

## Required acceptance cases

These are proposed tests, not results. Use public compiler APIs, unrelated
synthetic helpers and bodyless/source-free provider expectations.

| Family | Positive, negative and boundary obligations |
|---|---|
| Reproduction/defaults | Canonical generic helper, true and false, with/without explicit state guard and with discarded result. Observe written value versus vacancy through both local and projected tests. Reject true/vacant, default false/live and non-Boolean incomplete normal returns. Check explicit both-live Boolean and non-Boolean conditional maps separately. |
| Place identity | Local capability, forwarding through two helpers, nested fields and eligible descriptor-slot output; descriptor test stays true while referent starts false. Rebinding/CFG selection updates only the selected destination. Whole-partial tests are false without vacating live children. Reject Copy/escape, double initialization, overlapping reservations, wrong slot, live/partial/reserved formation and write upgrades. |
| Correlation versus state | Saved/copied status, reassignment, relevant alias effects, take/end and loop reuse. A lost correlation cannot clear live state; an old result cannot revive a new storage generation. Initialized zero/null is live, not vacant. |
| ABI and generics | Several generic substitutions and independent outputs; named/source order, unit ordinals, register/stack boundary, hidden ordinary result and recursive calls. Inspect unchanged one-address entry shape and actual destination writes. Reject unsupported payloads, alignment/zero-extent cases and open-generic fnptr context rather than inventing carriers. |
| Failure/cleanup | Later argument fails before entry; body fails after a write; postcondition/phase cleanup fails. No false result, normal publication, rollback or duplicate cleanup. Exercise only already-admitted ownership/failure paths and preserve their independent refusal gates. |
| Interchange/replacement | Normalize source Boolean pairs before Standard/private publication; round-trip retained bodyless expectations and remapped ordinals. Missing normalized pair must not silently bind as an equivalent unconditional contract. Reject conflicting/holey selectors, wrong subjects, changed provider states and unsupported exact schemas. |
| Execution evidence | Keep the legacy result-59 case as separate coverage. Qualify canonical direct/generic and admitted indirect paths with final-input identity, both implemented policies, matched test counts and supervised headless JIT/AOT execution. Emission or a legacy pass alone is insufficient. |

Approval remains open for this reconstruction protocol. Implementation and
qualification of the selected contract/type/persistence/runtime prerequisites
are separate gates; this proposal closes none by documentation alone.
