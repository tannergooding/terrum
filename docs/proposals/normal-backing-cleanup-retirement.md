# Normal-backing class Cleanup retirement

**Status: unapproved; provisional runtime-integration proposal under the
authorized missing-contract workflow.** This document does not amend accepted
semantics, establish implementation support or approve another proposal.

## Decision and scope

Recommend extending the [normal-backing candidate](reference-normal-backing-support.md#hold-acquisition-copy-publication-and-retirement)
with **cause-aware retirement and before-use exceptional-exit admission**:
keep hold bookkeeping nonfailing, but treat complete normal retirement as a
potentially throwing operation. A generated checked-unwind release must not
start authored Cleanup. Reject an affected native use before execution unless
its release is proven nonfinal or its entire possible retirement cascade is
runtime-only.

This is the smallest extension that preserves the accepted normal-only
Cleanup decision. It deliberately does **not** support arbitrary checked
exceptional exits from Cleanup-bearing class lifetimes. Supporting those exits
without the proof requires a different reviewed policy, not an emitter default.

The scope remains one synchronous, single-threaded Windows x64 regenerated
closure and its explicitly selected normal runtime instance. Normal class
handles and supported references into those classes share the same retirement
driver. Existing class ownership, field/layout and call-support gates remain;
this adds no box construction, owning/resource/task/guard fields, GC/pin graph,
foreign crossing or independent precompiled managed provider.

## Accepted rules and the missing integration choice

| Owner | Already decided |
|---|---|
| [Complete-value Cleanup](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement), OREG-01/06 and OCLN-01–16 in [cleanup conformance](../specs/conformance/source-ownership-and-cleanup.md) | One exact object-level operation, complete readonly receiver, nonsuspending body once, then recursive reverse-declaration field retirement. Normal Cleanup may throw; remaining generated owner/field cleanup does not resume during that unwind. No ordinary destruction alias is exposed. |
| [Backing cases SGC-28–30, 43 and 46](../specs/conformance/source-storage-and-gc.md) and [lifetimes](../specs/lifetimes.md#definitions-and-notation) | Final normal strong holds and bounded dependencies govern retirement, including an interior reference surviving its source handle local. Strong cycles are not collected. Optimizations cannot advance observable Cleanup. |
| [Normal and exceptional exits](../specs/language-contracts.md#33-normal-and-exceptional-exits) | Unwind is not a normal epilogue and introduces no implicit owner cleanup. Crossed authored `finally` blocks have their ordinary effects and failure rules. |
| [Construction](../specs/language/ownership-and-cleanup.md#construction-and-publication), OCON-04–07/17/18 | Failed unpublished construction retires actual remaining acquisitions in reverse acquisition order. Never invoke the incomplete parent's Cleanup; a complete acquired child retains its own complete cleanup. |
| [Normal-backing candidate](reference-normal-backing-support.md#hold-acquisition-copy-publication-and-retirement) | Nonallocating, nonfailing retain/drop currently relies on a recursively runtime-only payload graph. Authored Cleanup is explicitly gated on a complete driver and every-exit schedule; the candidate selects no deferred-finalizer solution. |

SGC-36 still includes **throwing** among reasons to reject Cleanup. That
summary contradicts the canonical ownership chapter and current OREG-06.
Under the [authority rule](../specs/overview/conformance.md#normative-status-and-authority)
this is an erratum, not an undecided choice or permission to require
nonthrowing Cleanup. Its eventual correction must retain the other
eligibility/direct-call restrictions. This proposal changes no specification.

There is no missing decision about field order, throwing eligibility or
automatic Cleanup on ordinary unwind. The missing candidate agreement is
how to enlarge the runtime-only subset without keeping an impossible
unconditional promise that final release both executes arbitrary Cleanup and
cannot fail. This proposal selects a bounded support boundary rather than
new exceptional destruction semantics.

## Exact proposed amendment to the normal-backing candidate

Replace its blanket authored-Cleanup exclusion only for class uses satisfying
the following rules. Preserve its runtime-only definition for exceptional
drains, all other exclusions, carrier layouts, host key and closure identity.
No new selector, public helper ABI, numeric profile, Feature, opcode or
persistence revision is proposed.

### Release cause and admission

Distinguish a residence's nonallocating count/transfer bookkeeping from
starting the complete retirement recipe. Bookkeeping remains nonsuspending
and nonfailing on admitted inputs; **normal final release as a whole does
not acquire that guarantee**.

| Cause | Required behavior |
|---|---|
| Normal source retirement: normal scope exit (including nonexceptional control transfers), admitted replacement, explicit discard or local lifetime end | Release that actual residence once. A nonfinal release invokes no object Cleanup; the final release starts the complete normal driver synchronously at that retirement point. A value transfer alone does not retire the transferred value. |
| Generated release on a checked exceptional edge | Permit only a proven nonfinal release or a proven runtime-only complete cascade. A possible final release that would start authored Cleanup is unsupported and rejects before affected execution. |
| Already active or incomplete retirement | Retire temporary support under its actual holder; never start the selected body again. The retirement record retains its separate storage responsibility. |
| Unpublished construction abort | Use the distinct acquisition-order contract below, not the ordinary exceptional-drain rule. |
| Fail-fast/process termination | Preserve the separate no-unwind/no-cleanup promise; it is not evidence that an otherwise possible language-failure edge is absent. |

Cause belongs to the source operation or generated exit, not a process-wide
“an exception is pending” Boolean. An explicitly authored, admitted retirement
operation inside a crossed `finally` can invoke normal Cleanup; it does not
authorize the surrounding generated unwind drain to do so. A failure from
that operation follows ordinary causal replacement/propagation rules.

A nonfinal proof needs an actual independently retained hold that survives
the release on that path, including intervening calls and reentrancy. Do not
invent an outer anchor, prolong a local or use a stale count observation.
“Runtime-only” includes the entire possible zero-hold cascade through actual
field-held handles and reference support, not just the absence of a body on
the outer type. Unknown retirement is not runtime-only. Proven nonthrowing
**authored** Cleanup is still source execution and does not qualify.

Check all reachable exceptional drains: later-argument failure, entered
parameters, saved/unpublished results, output checks, constructor phases and
failure of normal Cleanup itself. A catch outside the operation does not
repair missing admission. A runtime count branch may implement an already
valid proof, but cannot turn an unsupported final branch into a new catchable
failure, fail-fast fallback, leak or deferred cleanup queue.

Consequently, a final hold on a still-live Cleanup-bearing allocation is
**not released by an admitted ordinary generated checked-unwind drain**.
The author must establish an admitted explicit retirement/transfer schedule,
or the use remains unsupported. This is a runtime-support limitation, not a
new source ban on Cleanup-bearing classes or throwing Cleanup.

### Complete driver, lifetime and failure

At normal final release, transfer responsibility out of the releasing
residence exactly once and establish a private active-retirement record
before invoking source code. Zero strong holds does not authorize freeing
the payload. The record preserves the actual allocation generation, complete
receiver, live field obligations and leaf allocator/type/helper/code origins;
it is not another source handle, explicit owner or lifetime extension for
unrelated backing.

Invoke the exact selected readonly instance body once, with every field live
and complete. On its normal completion, retire fields in reverse declaration
order, completing each child's applicable transitive retirement before
proceeding to the next field. Implicit property backing keeps its declaration
position; replacement/acquisition order does not reorder complete retirement.
Dropping a descriptor retires its own dependency, not an arbitrarily
referenced value; if its actual normal support becomes final, apply that
allocation's recipe at that point. Release storage only after all remaining
obligations permit it.

Internal borrows/copies must be bounded by the active record and end without
restarting its body. A new surviving dependency on the retiring receiver
cannot be used to invalidate later field retirement. Where confinement,
capture or reentrant observation cannot be checked, reject this native use.
This does not import GC's specific resurrection ban as a new universal
normal-source rule or select a normal resurrection protocol.

If the body or a child retirement throws, propagate that actual failure
under [ordinary exception rules](../specs/language/local-failures.md#5-selection-recovery-and-propagation).
Do not wrap/suppress it, fabricate normal completion or resume remaining
generated owner/field cleanup during the unwind. Crossed authored `finally`
blocks still execute. Completed releases and moved residences are not
replayed, restored or counted twice.

The bounded runtime retains an **incomplete-retirement record** for the
active child and every still-undischarged containing obligation. It records
completed, active-incomplete and unstarted work separately; incomplete is
neither live complete nor discharged/vacant. Retain the necessary payloads,
field-held support and origins until process termination, without retry,
later automatic draining or a user-visible recovery/destruction API. Do not
release untouched fields in a helper's unwind handler. This conservative
physical retention implements already-incomplete retirement; it is **not**
permission to admit a final exceptional release of a still-live object.

Before admitting potentially failing retirement, establish that every
possible retained incomplete suffix can preserve its required lifetime and
access bounds. Otherwise decline the native use. In particular, a record
cannot prolong inline caller storage or an explicit owner/guard endpoint.

Prepare bookkeeping/traversal capacity before publication. No new allocation
or unbounded native recursion is required by the driver machinery; authored
body operations retain their actual allocation, failure and effect contracts.
Supported calls and failures must preserve typed live residences and active
records across reentrancy and unwind. These records are normal-backing
support, not tracing-GC roots or an excuse to admit mixed GC/pin fields.
The active failure event keeps its independently selected propagation
support; retirement bookkeeping does not capture another event owner.

### Construction, binding and native boundaries

Unpublished construction retains its provisional hold and actual acquisition
state through declaration/body/caller initialization and final validation.
Failure never publishes a handle or invokes the parent's complete Cleanup,
even when all its fields are live. Retire outstanding acquisitions in reverse
actual acquisition order, preserving interleaved partial children and each
complete child's separate cleanup boundary. This specific accepted rollback
contract can require a complete child's body during failure; it is not a
general license for frame-unwind Cleanup.

Admit that rollback only with its full acquired-state, rooting and failure
schedule. If a child's cleanup throws, preserve ordinary causal propagation,
stop remaining generated cleanup and retain incomplete obligations as above;
do not free the parent as successfully rolled back. After successful parent
publication, later failure is not failed construction.

Binding, shared rewriting and mandatory verification must retain exact
Cleanup selection, declaration/field order, ownership, dependencies and the
exit-specific support facts. No method spelling, sample name, empty Effects
mask or retired general NoThrow promise supplies them. A closed proof retains
its real binding dependencies and must be invalidated/rechecked on change.
Consumers still perform required structural/reference/support/binding checks,
not semantic re-proof of producer body facts.

Keep the candidate's eight-byte normal handles, sixteen-byte ordinary
references and direct-call handoff/publication unchanged. The driver invokes
the selected member through a compiler-private checked entry with a bounded
readonly receiver; it exposes no user-callable destructor. Native frames,
unwind state, saved values and actual leaf origins must remain valid until
their obligations end; an incomplete record prevents unload/quiescent disposal.
Normal allocations never become reclaimable by the tracing prototype.
Terrum failures do not acquire an outward foreign-unwind route.

[CC110](private-class-cleanup-persistence.md) selects a separate unapproved
declaration carrier, not this runtime behavior. Standard/private artifacts
and bodyless provider expectations need their own supported exact schemas,
selection/origin closure and complete relowering; missing facts refuse use.
No live retirement record, control address or inferred execution permission
is serialized by this proposal.

## Alternatives and intended integration

| Direction | Assessment |
|---|---|
| **Recommended: normal driver plus cause-aware admission** | Minimal amendment to the existing normal candidate. Preserves accepted cleanup observations and needs no new source state or public API. Its cost is rejecting checked-exit uses without a sound schedule. |
| Run Cleanup whenever any decrement reaches zero, including ordinary unwind | Wider support, but intentionally replaces accepted normal-only generation. Even a nonthrowing body adds observable source execution on unwind. Throwing bodies additionally need a complete secondary-failure and remaining-cleanup policy. Not an implementation of the accepted rule. |
| Park zero-hold live objects for later cleanup, or abandon them permanently | Avoids source execution in the drain, but chooses new retirement timing or abandonment semantics, retained roots/origins, shutdown and failure responsibility. A deferred queue additionally needs scheduling/order/reentrancy rules. Neither follows from strong-cycle leakage or from retaining an already-incomplete retirement. Not selected. |

If adopted, integrate the release/driver/admission agreement into the
normal-backing candidate's **Hold acquisition, Copy, publication and
retirement** section and its call/failure acceptance gates. The canonical
ownership, lifetime and exception owners keep their accepted semantics;
SGC-36's throwing entry needs only the independent erratum identified above.
Target-native integration belongs with the eventually adopted normal
layout/call/deployment agreement, not a silent extension of `(1,1)`.

## Required tests and later review

These are future acceptance cases, **not executed or passing evidence**.
Separate source/mandatory-IR admission, emission and supervised execution.

| Family | Required outcome |
|---|---|
| Positive: aliases and delayed final support | Exercise aliases of two unrelated normal class types: one obligation per allocation. Ending the last handle while a valid interior reference survives does not run Cleanup; normal retirement of the final reference does, exactly once. Repeat through Copy wrappers and moves. |
| Positive: complete field order | Parent body observes all fields; then child field holds retire in reverse declaration order, with each final child's body before its fields. Out-of-order acquisition, later replacement and implicit backing fields do not change that order. External child aliases delay only that child's object retirement. |
| Positive: nonfinal checked release | A genuinely surviving outer hold permits an inner alias's exceptional drain without source Cleanup. Its later normal final release invokes the driver. No invisible caller copy or synthetic anchor supplies the proof. |
| Negative: final exceptional release | A potentially final generated drain of an authored-Cleanup object rejects before execution, including a nonthrowing body, interior-reference final hold, staged argument, entered parameter and unpublished result. Do not test a runtime fallback as successful admission. |
| Negative: transitive and unknown recipes | A derived outer type or Copy aggregate can reach a Cleanup-bearing child through final support release. Reject an unsafe exceptional cascade; absence of an outer body, empty Effects or opaque provider facts does not prove runtime-only. |
| Boundary: operation cause | An admitted explicit retirement in a crossed authored `finally` runs its normal recipe despite a pending event. An adjacent generated exceptional drain gains no such permission. Preserve ordinary replacement/parent causality if the explicit operation fails. |
| Failure: normal parent or child Cleanup | Throw before fields, or after a later-declared child retired. Propagate the actual event; no remaining generated cleanup or replay. Preserve active-incomplete and untouched obligations/storage/origins. Include failure of a crossed authored `finally`. |
| Construction | Fail before any acquisition, after complete children acquired out of declaration order, within interleaved partial children, and after all fields but before validation/publication. No parent body; reverse actual acquisitions and complete-child cleanup. A cleanup failure stops remaining generated work. Repeat a later failure after publication to exclude rollback replay. |
| Negative: authority and protocol | Direct Cleanup calls, incomplete receivers, escaping receiver dependencies, incompatible sibling cleanup dependencies and unsettled owner/task/guard obligations remain rejected or unsupported under their existing contracts. Reject potentially incomplete retention that would outlive required inline backing or an explicit owner/guard. No roots/counts manufacture authority. |
| Boundary: empty state, cycles and capacity | Never-initialized, moved and already-ended residences do not release again. Zero-data fields retain logical obligations. Strong normal cycles remain uncollected. Prepared capacity and exact allocator pairing cover success and incomplete retirement without count overflow or late bookkeeping allocation. |
| Native and retained-input conformance | Independently qualify final-input JIT and AOT, nested calls/staging, active-record failure transport and origin lifetime. Source-free supported binding/remapping preserves selection/order and invalidates affected closed proofs; reject missing schema/support, cross-instance exchange and foreign outward unwind. |

The user must later review **the bounded exceptional-exit admission choice
and conservative retention of already-incomplete retirement**. If general
final-hold release on ordinary checked unwind is required, review an explicit
alternative above before extending the support claim. Acceptance of existing
Cleanup semantics, a declaration codec or a passing normal-path test does not
approve that broader behavior.
