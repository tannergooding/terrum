# Standard TRRM PI1 property-initializer invocation

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow, pending later review.** This completes PI1's
invocation contract, not implementation, qualification or normative approval.

## Decision and exact scope

Reuse **general CallSite `02`, receiver input `(Home, sourceUse=5, h)`**
for a role-3 `BackedPropertyInitializer` M. Classify the selected callable's
Construction role before ordinary R1 receiver checking. The selected M→Q→F
relation determines the property authority; the selected Home determines the
construction generation and lifecycle. Neither alone supplies both.

[PI1](standard-trrm-c1-property-initialized-state.md#binding-calls-and-publication)
already sketches source-use 5, I/M forwarding and caller entries. This
companion makes their exact encoding and role-directed validation explicit.
It completes that section and supersedes
[R1's role-1-only receiver restriction](standard-trrm-method-receiver-access.md#initialization-is-not-mutable-receiving)
and [Construction-2's P-only I-call restriction](standard-trrm-c1-construction-runtime-boundary.md#checked-construction-program)
**only inside PI1's two exact candidate agreements**.

Amend the two unpublished whole agreements in
[PI1's allocation section](standard-trrm-c1-property-initialized-state.md#exact-proposed-agreement-and-allocations)
in place: Construction-1L1R1 plus PI1, and Construction-2L1R1 plus PI1.
This document is part of their complete exact agreement, not a separately
selectable component, Feature, alias or schema suffix. Root, every supplied
provider and retained semantic context must agree before dependent decoding.
No compatibility with an earlier incomplete draft is required; changing a
selector cannot recover erased state or authorize a different decoder.
Original PI1 parents, IO1 and other agreements retain their meanings.

No source syntax, MethodDef flag, TypeSpec, Home role, record, opcode,
operand width or native ABI is added. Construction-1 PI1 can retain the
phase-bound I/M receiver route below, but gains no Begin/Publish/Abort or
fresh-backing execution route. Construction-2 PI1 retains its admitted
nongeneric, nonunique normal-class and policy-zero restrictions.

### Why reuse 5

Construction-1 already specializes initialization-destination use 5 for I's
receiver. Extending that role-directed meaning to M is unambiguous: receiver
position, target role and Home role distinguish it from an explicit
`ref<init T>`/Span formal. A separate source-use or receiver record could
label the category, but would still need the same Q/F, generation, phase and
frame checks and duplicate existing identities. It supplies no missing fact.
Reuse is selected for that reason, not to protect unpublished numeric IDs.
It does not reinterpret any other source-use, including the separate
NativeOutput candidate.

### Source observations

These are inspected-source facts, not validation results:

* [`TrRmRegisterBodyReader.ValidateCallSite`](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs)
  checks record framing and selector domains; those checks alone do not
  establish a receiver's construction authority.
* [`TrRmRegisterBodyMapValidator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs)
  currently refuses role-3 targets in `ValidateCallSiteEffects` and
  `ValidateCallSiteHomeTypes`. `ValidateConstructionReceiverInput` admits
  only P→its I through role-4 Home/5; `ValidateConstructionContextHome`
  likewise restricts context Homes to P.
* Its lifecycle worklist treats a role-4 Home/5 call as constructor
  completion without distinguishing I from M. Simply removing the role-3
  refusals would therefore be incorrect. Ordinary class receiver checking
  instead uses borrow 3/4, class qualifications and R1 flag bit 2.
* [`TrRmContractTableReader.ReadConstructionClause`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs)
  has provisional role-3 Q linkage. It does not supply these CallSite,
  caller-entry or lifecycle rules.

## Exact CallSite and receiver source

Use the existing record header `u8 kind=02, u8 flags=0, u16 reserved=0`
and [CallSite payload](../specs/il-instruction-set.md#typed-operand-records):

| Field | Role-3 requirement |
|---|---|
| `targetKind`, `targetIndex` | `1=MethodMap`; index selects M's statically selected declaration view, directly or through a checked MethodDef Import. No callable-value, GenericCall, getter or setter target substitutes. |
| `resultKind`, `resultIndex` | `0=None`, `0`; M's result is exact unit. DoesNotReturn still has no normal edge and must terminate its Block. |
| Payload `flags` | Zero. |
| `inputCount` | Exactly **2**: receiver, then Q's assigned `value`. Count the receiver once in both PI1 agreements, independently of whether ObjectConstruction operations are admitted. |
| Input 0 | `u8 selectorKind=2, u8 sourceUse=5, u16 selectorIndex=h`, with h admitted below. |
| Input 1 | The already staged ordinary by-value T input at explicit formal ordinal **0**, checked against M's exact Param and ownership/type contract. For exact unit T, retain this logical row as `(None,0,0)`; it is not omitted. |
| `preparationCount`, list | Existing ordered DestinationPreparation records only. None may retire, initialize, rebind or publish the receiver Home/context, transfer its hold, or prepare F as an omitted output. M's real Body owns its backing writes and retirement. |

With n preparations the payload is exactly `24 + 2*n` bytes and the whole
record `28 + 2*n`, checked without overflow. Inherit the parent's counts,
record references and zero-based full-u16 map/Home indexes, including FFFF;
no sentinel or new index space is introduced. Use general `90`/`E0` under
the existing canonical width rules. Compact calls cannot encode I or M.

Before receiver checking, validate M's role-3 clause, Q's unique init link,
Q/F ownership and complete types under PI1. M has the exact initialization
receiver of the bound class C, with `RequiresWritableReceiver` **clear**.
Match h's complete TypeMap type after checked owner/provider correspondence,
not by equal row numbers across Modules. Do not apply ordinary R1
flag-versus-`write C` inference to this initialization receiver.

Receiver input zero is not a Param, initialization-output formal or
descriptor cell. It cannot consume formal ordinal zero, acquire an
OutputState relation, or use ordinary source-use-5 descriptor preparation.
Other explicit initialization formals retain their existing contract.
Ordinary get/set calls retain R1's selected caller view and borrow 3/4;
neither an init link nor a clear receiver flag makes them phase operations.

## Home authority and entry-state matrix

The following are the complete receiver-source cases for this amendment:

| Selected h | Required retained entry facts | Permitted target/authority |
|---|---|---|
| Construction-2 PI1 Home role **4**, in an otherwise admitted caller Body | Exact C context, active generation G, owning provisional hold. For I: **Uncalled**. For M: selected I **Completed**. | I once, or M for any eligible Q of this C, subject to source access and every independent contract. |
| Home role **1** of role-1 I, in either PI1 agreement | Borrowed active G; selected initializer is this I and is **Entered**, including its declaration-initializer prefix. | M for any eligible Q of the same C. Never another I over G. |
| Home role **1** of role-3 M₀, in either PI1 agreement | Borrowed same G and selected-I identity; preserve the received **Entered/Completed** alternative and M₀'s Q₀/F₀ authority. | M only for that same bound Q₀/F₀. No sibling-property authority or I invocation. |
| Anything else | No such phase receiver. | Refuse this receiver form. |

Role-1 and role-4 Home flags, formalOrdinal and reserved remain zero; their
TypeMap is the exact admitted initialization receiver/eventual C type.
Role-1 storage is the live nominal receiver cell, not a complete object.
Only role 4 owns the context; I/M borrow it and cannot Publish/Abort it.
No ordinary Register, formal/local Home, ordinary receiver Home, raw pointer,
place handle, init-reference cell or published C can manufacture this view.
Source-use 1/2/3/4 is not an alternative encoding.

For a role-4 Home, derive C from that Home's checked type, **not the caller
MethodDef's owner type or Module**. A module function, unrelated method,
I or M may own a separately admitted fresh construction. Its context has
independent authority; M₀'s restriction applies to forwarding its borrowed
receiver, not to a different construction it genuinely owns.
This grants neither private member access nor a general helper/escape codec.

For caller-entry construction, the bound MethodMap target at I Invoke
selects the exact I for G. Retain the selected declaration view and provider
correspondence in context state. No new Home word or source-supplied
initializer identity is needed. A genuine role-2 P still requires its
clause-associated I, the same bound C, and its real no-caller-entry recipe.
Other callers need no synthesized role-2 clause or membership in C.

**I entry is not I completion.** Fresh backing begins vacant; mark I
Entered before its declaration-initializer prefix. Do not seed the receiver
with I's declared normal-completion guarantees. Preserve the
[source construction phases](../specs/grammar-spec.md#510-properties-and-accessor-local-effects),
including each phase's required cleanup; invocation Check must not hoist
constructor clauses before the prefix.

**M entry is not whole-object validity.** At Invoke, F must be wholly vacant
or complete/live, including a supported join of those cases, without
conflicting loans/reservations. Partial F is not vacancy. Retain every
sibling's actual structural availability/reservation state, including nested
partial state, without assuming it live. M's added authority concerns only
Q/F; ordinary reads, additional clauses, effects, dependencies and independent
authority still check. Same-Q forwarding is a scoped child borrow, not a copy
of authority; conflicting parent access is suspended for its required extent.

## Capture, lifecycle and completion edges

Capture the receiver's exact generation/authority before RHS evaluation,
then stage the RHS once in source order. The selected Home cannot be consumed
or reused while that captured view is needed. Neither its nominal type nor
re-reading a reused Home can recover the old generation. Capture is not an
early read/retirement of F or an invented vacancy reservation.
After Stage, perform the required Check/Prepare/Invoke/normal Publish protocol.
Check actual destination eligibility then, not a stale pre-RHS state snapshot.
Keep earlier source-position checks and actual RHS effects; do not replay
either expression. In particular:

| Edge/operation | Required state transfer |
|---|---|
| Begin | New G, I Uncalled, fresh vacant required backing; retain actual acquisitions as they occur. |
| I Invoke | Uncalled→Entered before its real prefix. No second I, even after a failed first entry. |
| I normal completion | Entered→Completed only after its complete normal phases; apply the selected I's minimum guarantees, preserving PropertyDef identities. Unlisted subjects are not thereby vacant. |
| M normal completion | I lifecycle/identity unchanged; F complete/live after body and postcondition-phase cleanup. Preserve the PI1 frame of all other receiver fields. |
| Failure before M Invoke | No M summary or entry. No call-protocol retirement of F before RHS success; retain actual RHS/preparation effects and staged responsibilities. |
| Failure/no-return after Invoke | No normal completion summary or fabricated unit result/normal edge. Keep actual field/child, acquisition and capture state; M does not complete I. An already Completed I is not undone by a later M failure. |
| Publish/Abort | Consume the owning context under Construction-2's existing gates. Reusing the Home starts another G with no old authority/state facts. |

The state frame preserves availability, not values, Pure or old predicates.
Repeated M calls may replace live F even without ordinary field-write
permission, but must execute the real accessor and ordinary replacement/
cleanup checks. The CallSite never lowers directly to a backing store merely
because it knows Q/F. No normal frame applies to a failing accessor, including
failure after writing F or during postcondition-phase cleanup.

Track state per context/generation and selected target role, not “any Home/5
call completes I.” At CFG joins keep possible selected I identities and
their correspondence, only common guaranteed state, and actual correlated
acquisitions/loans. Every continuation must be legal for every incoming
alternative; retain distinctions or refuse an unsupported merge. A nominal
carrier-0 BlockEntryState row cannot assert publication, manufacture phase
authority or collapse Entered/Completed or fresh/stale generations.

Final publication still needs I Completed once, all actual required storage,
dependencies and registered final validation. Do not rerun I's ensures after
caller entries or invoke getters for state checks. Failure publishes no C
and promises no rollback. Policy zero adds no unwinding/handler support;
Abort requires ended acquisitions/dependents, and the incomplete parent's
Cleanup never runs. Any independently admitted unwinding retains PI1's
reverse actual-acquisition obligations.

## Integration obligations and refusal boundary

On later integration, amend the
[Body call/record owner](../specs/il-instruction-set.md#calls) and
[registry constraints](../specs/representation/trrm-1.registry.json) with this
receiver matrix, plus the PI1/R1/Construction-1/2 cross-references above.
Do not globally broaden use 5 or alter parent selectors.

* **Production and verification:** retain capture, G, I entry/completion,
  Q/F authority/frame and real accessor phase facts through shared bound
  traversal, rewriting and producer verification. Extend receiver counting,
  role-directed CallSite checking, Home/context admission, escape/loan checks
  and lifecycle transfer together, including Construction-1 I/M Bodies.
* **Binding and consumers:** preserve the statically selected expected M/Q/F
  view and PI1's subordinate accessor/backing correspondence through provider
  replacement. Check schema, framing, counts, maps, complete types, roles,
  owner correspondence, required support and binding before affected use.
  Necessary supported reconstruction/state handling must not be bypassed.
  Under [reader trust](../specs/representation/reader-trust.md), producers
  prove semantic authorization and frames; consumers need not re-prove an
  arbitrary provider Body's truth.
* **Persistence and lowering:** remap C/I/M/Q/F, expected/actual views,
  Contracts, Method/Type/Home maps, CallSite/preparation/Block/record indexes
  and original clause/phase contexts together; rebuild affected offsets and
  source maps. Preserve same-generation state/acquisitions across I/M views
  in the selected private construction plan. Add no serialized runtime state,
  hidden semantic formal, public receiver ABI or new runtime policy.

Construction-2 PI1 retains Feature 25/1 for bodyful M and phase-bound calls;
other field/graph/label requirements remain independent. Missing class/member
closure, ownership/dependency, replacement/cleanup, phase/failure, dynamic
state or target support refuses the required use before execution.
Broader helper authority, sibling-state-changing frames, computed/multi-backed,
indexer/static/ref-result properties and other receiver modes remain outside
this amendment. Malformed structures/bindings are invalid data; unsupported
required semantics are unsupported, never ordinary access or missing backing.

## Focused acceptance obligations

Future cases, **not results from this documentation change**:

| Cases | Required outcome |
|---|---|
| I prefix/body→M; M→same-Q M; caller Begin→I→several M→validate→Publish | Preserve one G, one I, once-only RHS, real accessor order and repeated replacement. Include two unrelated classes, automatic/authored init and logical unit T. |
| I entry and authority | No I completion guarantees at entry. Reject context M before I, second I, M→other-Q forwarding, partial/reserved F, receiver-as-formal, ordinary borrow/handle/descriptor substitution and early escape. An independently owned nested context keeps its own authority. |
| Roles and wire | Check exact two inputs, value ordinal zero, None unit row/result, flags, full-u16 indexes, wrong Home/target roles and owner/type mismatches. Reject compact/GenericCall/callable-value M and role-3-as-ordinary-readonly, without weakening explicit init-formal controls. |
| Flow and generation | Branch-selected I identities retain only common guarantees; reject an invalid incoming phase or stale authority after Home reuse. M normal completion never advances I; unsupported correlated state/loan merges refuse. |
| Failure | RHS, accessor/pre/postcondition/cleanup and final-validation failure publish no normal summary/C; DoesNotReturn has no normal successor. Preserve actual prior effects/children without transactional restoration or policy-zero unwind claims. |
| Source-free binding/remapping | Bodyless expected M/Q/F, non-first/multi-Module providers, row/map permutations, Import-to-direct packing and replacement retain original views and same Q/F authority. Refuse retargeting or weakened frames even with equal signatures/ABI. |

Formal approval, implementation across these paths and later focused
source/Standard/source-free qualification remain open. No build, test,
profiling, process inspection or execution evidence is claimed here.
