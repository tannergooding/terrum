# Live-field replacement and throwing retirement

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This candidate records implementation direction,
not normative adoption, compiler support or runtime qualification.

## Decision and boundary

Recommend admitting ordinary replacement of a live owning field only when
its old value's complete retirement is proven nonthrowing. Unknown retirement
effects reject that replacement. This is a per-operation requirement, not a
ban on throwing Cleanup or on parents with authored Cleanup.

The accepted [replacement contract](../specs/language/ownership-and-cleanup.md#replacement-and-checked-takereplace)
already stages the new value before retirement and forbids an escaping
failure or observation of an incomplete receiver. Its
[conformance case OREP-11](../specs/conformance/source-ownership-and-cleanup.md#replacement-and-destination-identity)
requires rejection of unsupported post-staging failure. Separately,
[complete-value Cleanup](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement)
may throw: retirement then remains incomplete, and remaining generated
cleanup does not resume during that unwind.

Those rules already forbid lowering that strands a receiver. They do not
choose whether ordinary assignment must exclude throwing retirement or
acquire a new recoverable state and parent-Cleanup contract. This candidate
selects the former; it does not redefine the aftermath of a throwing
standalone retirement.

### Alternatives

| Candidate | Consequence |
|---|---|
| **Require nonthrowing old-value retirement; recommended** | Uses existing live/vacant/partial states and complete-receiver checks. Some replacements reject; an authored ordinary method must establish any different complete output/state contract. No new failure state, API or carrier is needed. |
| Admit throwing retirement with recoverable/quarantined fields | Must define ownership of partly retired resources, readable/repairable subplaces, catch and alias permissions, staged-value responsibility, whether Cleanup can be retried, and how an authored parent Cleanup handles that receiver. Marking the slot vacant loses obligations; marking it live permits invalid reads or repeated destruction. Coherent only with a broader state/retirement design. |
| Publish the new field first and retire the old value in another holder | Can keep the parent complete during old retirement, but intentionally changes accepted retirement-before-installation order and retained dependencies. It also needs an explicit exceptional old-holder contract. This is not an implementation of the current assignment contract. |

The recommendation is the smallest choice because it adds one admission
condition rather than a new source-visible state or replacement protocol.
An outside catch cannot undo resource release or make an incomplete
retirement complete.

## Exact proposed rule

If approved, add the following condition to the before-commit checks in
[Replacement and checked take/replace](../specs/language/ownership-and-cleanup.md#replacement-and-checked-takereplace):

> An ordinary assignment that will retire a live owning field may commit only
> when the checker establishes that the complete old-value retirement has no
> escaping language-failure edge. Include the selected Cleanup body and every
> transitive field-held retirement obligation. Potentially throwing or unknown
> retirement rejects this ordinary replacement, including when its exception
> could be caught outside the assignment. A maybe-live destination requires
> this proof on every reachable live path.
>
> Proven nonthrowing retirement does not waive access, dependencies,
> nonsuspension, reentrancy, complete validity or protocol settlement.
> Installation and restoration of affected enclosing validity must also have
> no escaping failure or exposed incomplete-value interval after retirement
> starts. A destination proven vacant uses ordinary initialization/repair,
> not this old-value retirement condition.

This specializes ordinary owning-field assignment; it supersedes no Cleanup
propagation rule. It applies equally during construction and after
publication when an old complete field value must be retired. A child already
holding partial acquired state is neither a live complete old value nor a
vacant repair destination; existing partial-state rules still govern it.
Readonly/fixed fields, unpublished initialization authority, supported place
forms and enclosing ownership restrictions remain unchanged.

An authored ordinary method is not an exemption for the same forbidden
assignment. It must actually establish its permitted complete receiver,
outputs and exceptional state using admitted operations. The existing
checked take/replace contract can transfer a still-live old owner while
installing a staged replacement, rather than retiring that old owner inside
the field operation. No method name, signature, intrinsic, attribute or
public API is selected here.

## Evaluation, states and failure edges

1. Evaluate the destination place once, then the right-hand side once in
   ordinary order. Preserve the destination's required backing and access.
   The staged value retains its actual owning holder, dependencies and
   explicit-move effects.
2. Complete and admit the staged value before retiring anything from the
   destination. Recheck the destination's actual state and all affected
   validity/dependency facts after permitted right-hand-side mutations.
   Do not rely on a stale pre-evaluation live/vacant assumption.
3. Perform potentially failing admission/validation before retirement, or
   prove it nonfailing if it must execute later. If supported checking cannot
   establish the new child and every affected enclosing invariant without a
   post-retirement escaping failure, reject before use. This selects no
   validation helper or hidden reconstruction.
4. On a live path, retire the old complete value once, then transfer/install
   the staged complete value and establish affected validity within the
   checked confined interval. On a vacant path, install without old cleanup.

| Point or outcome | Field and containing-owner state |
|---|---|
| Destination/source evaluation or pre-commit admission fails | Assignment retirement has not begun. Keep the destination's actual current state, including permitted source-side mutations. New acquired/staged ownership follows existing construction and exceptional-holder rules; consumed sources are not restored. |
| Live-path retirement and installation | The old value ceases to be live as retirement proceeds; any intermediate retiring/partial/vacant storage is implementation-local, not a catchable or observable source state. No rollback owner or default value is created. |
| Successful installation | Field contains the new complete value. A previously complete containing owner remains complete with its affected invariants established. |
| Containing derived-cleanup owner was already partial in other fields | Preserve those vacancies and remaining live obligations. Replacing one live child does not establish whole-owner completeness or whole-place vacancy. |
| Claimed nonthrowing retirement actually throws | Outside the admitted contract; no source-level recovery state or runtime repair is promised. A checker cannot erase a possible throw by marking retirement successful, installing the staged value in a catch thunk, or resuming generated cleanup. |

There is no new catchable failure edge between retirement starting and
admitted publication. Earlier failures and failures of later statements
retain ordinary propagation and authored `finally` behavior. This rule
introduces no generated exceptional-unwind Cleanup, retry or suppression.
It proves neither termination nor rollback of external effects.

## Cleanup order, dependencies and observation

Replacing a child does not invoke its parent's Cleanup. Retire the old child
using its own selected body once, then its owning fields in reverse
declaration order, recursively. Retire descriptors, roots and other
obligations under their actual contracts. Only afterward install the new
child. Later complete-parent retirement runs the parent's body on the complete
receiver, then its current children in reverse declaration order. An eligible
partial structural parent instead retires only its remaining live fields.
Replacement does not change a field's position or revive the retired child.
Failed unpublished construction keeps its separate reverse-acquisition rule.

Check surviving sibling, descendant, external and later-cleanup dependencies,
including the staged value's backing. A new value needing the old resource
cannot justify retiring it; a hidden retained old owner cannot repair that
conflict. Precise independent siblings remain usable under their actual
contracts, while broad whole-source dependencies are not privately narrowed.
Task, guard, affinity and settlement obligations remain independent; a
nonthrowing destructor alone cannot make a pending task disposable.

Nonthrowing does not imply non-reentrant. Any callback or observation during
retirement must preserve the existing complete-value boundary and access
rules. From the first invalidation of an affected complete value until
installation/validity restoration, no callback, suspension or escaping
operation may expose that incomplete field or ancestor. In particular,
nonthrowing child Cleanup that can reenter a partially retired parent still
rejects without the required confinement proof. No blanket sibling exclusion,
lock, concurrent atomicity guarantee or runtime quarantine is introduced.

A parent or enclosing ancestor with authored Cleanup therefore stays
complete on every admitted observable exit. Its own Cleanup may still throw
when that parent is later retired normally; remaining generated cleanup then
does not resume, exactly as accepted. The nonthrowing requirement concerns
the old field's retirement now, not every future cleanup of the parent.

## Effects, proofs and independent consumption

Use the actual selected retirement program and its supported contracts or a
sound scoped closed proof, including transitive cleanup, invoked operations,
arithmetic/check policies, initialization, backing and binding dependencies.
Unknown calls/cleanup are potentially failing. Merely catching around the
assignment, selecting fail-fast execution, or observing a successful test
does not establish the fact. Internally handled failures are usable only
when existing checked contracts establish complete retirement and its other
obligations, not just absence of an escaping exception.

No general source `NoThrow` annotation is required or revived. This composes
with the separately unapproved
[NoThrow retirement direction](no-throw-retirement-and-proof-boundaries.md#proposed-source-and-proof-rules).
The accepted [Effects mask](../specs/representation/contract-clauses.md#3-effects)
contains no no-failure promise: neither its absence nor zero proves this
condition. Pure, DoesNotReturn, readonly, unique and caller-unsafe are not
substitutes for the exact retirement proof.

An independently replaceable provider's current body cannot silently grant
an unrepresented guarantee. Closed proofs must retain their exact
dependencies and be invalidated/rechecked with affected code regenerated
when those dependencies change. Where the selected modular contract cannot
establish the fact, reject affected ordinary live-field replacement rather
than invent a published promise. Producer truth and mandatory consumer
structure/reference/support/binding checks remain distinct; consumers need
not semantically re-prove producer-published body facts.

## Implementation and integration consequences

The existing [`CleanupMayFail`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs)
conservatively treats an authored Cleanup or a transitive owning child with
one as potentially failing. Its current uses model lifetime-end, drop and
constructed-field cleanup failures, not ordinary live owning-field
replacement. It is not evidence that an authored body is nonthrowing.
The [bounded owner inventory](../implementation/view-and-owner-profiles.md#compact-unique-owner-loans)
also does not establish general owning-field replacement support.

Implementation prerequisites are exact destination state/authority, staged
ownership, full retirement effects, ancestor validity and dependency checks,
and supported confinement through lowering. Binding and mandatory bound-tree
verification must agree on admission; constructed IR is not permission to
bypass it. A conservative checker may reject unknown cleanup rather than
pretend this proposal supplies a proof.

Native lowering must retain evaluation and cleanup order, own the staged new
value until transfer, and preserve real live/retired generations without
replay. A temporary for the new value is not a hidden old owner. A nonthrowing
commit interval does not make source evaluation nonthrowing or remove target
frame/unwind, root, address-stability or code-lifetime requirements. No
catch/repair thunk, ABI change, stored-reference representation, runtime
bitmap, standard instruction, Feature or private persistence revision is
selected. Unsupported crossings remain independently gated, not silently
enabled by source admission.

If adopted, integrate the exact condition into the ownership chapter's
replacement section and add cases under its existing replacement conformance
section. The structural-flow owner keeps its current states; the Cleanup
section and lifetime summary keep their current throwing-retirement rule.
No other normative text is superseded. Current support inventories should
change only with implementation evidence. Broader recoverable retirement and
any public library surface remain outside this decision, not prerequisites
for implementing the recommended admitted subset.

## Required acceptance cases

These are proposed conformance/implementation tests, not reports of executed
coverage. Use public compiler APIs and the existing headless supervised native
runner; verify admission separately from emission and checked-runtime results.

| Case | Required outcome |
|---|---|
| Nonthrowing old child, parent with derived cleanup; repeat with authored parent Cleanup | Admit when other proofs hold. Observe destination evaluation, source construction, old body, old fields in reverse declaration order, installation, and later parent/current-field retirement. Old and new retire once each at their actual times. |
| Old child has a proved nonthrowing authored body and nested owning fields | Include all transitive retirement in the proof and preserve body-before-fields order; checking only the outer body is insufficient. |
| Old child body can throw, or only a nested child retirement can throw; repeat for both parent kinds and an authored ancestor | Reject ordinary live-field replacement, including inside an outside catch. Do not execute rejected programs to infer a state. |
| Old retirement has unknown external effects, absent/zero Effects, retired NoThrow bits or merely a currently nonthrowing replaceable body | No inferred guarantee; reject or apply existing schema rejection as appropriate. Closed proof dependencies must survive supported rebinding, or invalidate/regenerate affected code. |
| Definitely vacant eligible field whose complete type has potentially throwing Cleanup | This restriction adds no old-retirement rejection: repair has no old value. Preserve ordinary construction, validity and later-retirement checks. |
| Maybe-live destination, or permitted source-side mutation changes its state | Retire only on actual live paths and require the proof there; a proven vacant branch initializes without double cleanup. Keep unrelated partial-owner state. |
| Source construction/admission fails before commitment, including after an explicit move or permitted mutation | Preserve actual old-field/receiver state and source effects. Check staged/acquired responsibility through the existing failure rules; no old retirement, source restoration or fabricated vacancy. |
| Nonthrowing retirement can expose incomplete state through a callback, or installation/ancestor validity can escape failure | Reject missing confinement/failure proof. Include nested ancestors and a zero-data owning field; no payload bytes is not absence of an obligation. |
| New backing or a surviving sibling/alias needs the old resource; repeat with a precise independent sibling | Reject the conflicting replacement without hidden retention; do not reject the independent case merely because it shares a root. |
| Readonly/fixed field, partial child, initialization-only destination, pending task or unsettled guard | Preserve existing rejection/settlement rules; no new authority or conversion from partial to vacant. |
| Authored ordinary method transfers old ownership while restoring every complete receiver | Check actual receiver/output/failure truth under the existing take/replace contract, without recognizing a special name or excusing a forbidden assignment. |
| Parent Cleanup throws at later normal retirement after a valid replacement | Preserve the separate accepted failure behavior: no replay of the replaced old value and no resumption of remaining generated cleanup during unwind. |

Approval of the admission rule remains pending. No new recoverable-state,
reference, ABI or serialized ownership contract is implicitly approved.
