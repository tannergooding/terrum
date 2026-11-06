# Authored NativeOutput adoption and settlement

**Status: unapproved; provisionally actionable as direction under the
authorized missing-contract workflow.** This selects an authored handoff
recipe, not approval of the resource extension, source/API, ABI, persistence
or implementation. It changes no accepted owner.

## Missing decision and recommendation

The [cell owner's resource section](aggregate-native-outputs.md#resource-ownership-adoption-and-typed-interfaces)
already specifies acquisition responsibility and failure obligations; the
[resource record](native-pointer-output-ownership.md) preserves independent
approval. Neither selects an adoption operation or release disposition.
Accepted [unique ownership and Cleanup](../specs/language/ownership-and-cleanup.md)
supplies single-holder transfer and normal retirement, not implicit
exceptional resource settlement. The missing choice is the authored
handoff/failure protocol, not a new ownership family.

Recommend **retain through validation, commit by ordinary unique transfer,
and admit only total one-shot retirement** for the first resource slice.
Use an ordinary library-private inline unique custodian, not Copy
pointer/handle aliases standing for checked responsibility. Its custody
invariant is authored, not a new Core type,
ownership flag, recognized constructor or serialized authorization.

This owns only the narrowed recipe; it does not supersede either NativeOutput
owner. Release that can leave responsibility outstanding needs a separate
complete protocol.

## Separate facts

| Fact | Required evidence; not inferred from another row |
|---|---|
| Cell publication | The exact NativeOutput T and result/publication contract, owned by the cell proposal. A pointer cell is not its pointee. |
| Pointee validity | The authored provider's actual extent, initialization, access, alignment, backing and retention contract before any pointee use. |
| Resource custody | One acquired right/responsibility, its actual acquisition instance, and the exact operation that settles it. Releasing one reference need not destroy the foreign object. |
| Provider/code lifetime | Actual allocator/provider origin, release context, code/helpers and any guard/lease remain valid through every use and final retirement. Roots or a copied address do not establish this. |
| Typed-interface identity | Fix or independently validate the actual interface identity before typed publication. Caller-selected IID plus `void**`, pointer equality, casts or a common release prefix are insufficient. |

## Candidate authored protocol

1. **Prepare before acquisition.** Establish release context, backing/provider
   dependencies, required affinity and explicit unsafe `try`/`finally`
   structure. The success-only provider contract must already permit release
   of its untyped result if later identity validation fails; never guess a
   vtable release slot.
2. **Establish custody without a failure gap.** Preserve the resource owner's
   responsibility at successful machine return and nonfallible interval
   through cell publication. After publication and its result test, establish
   one complete unique custodian before fallible validation/adaptation.
   Raw-to-custodian transfer and publication require a sound, supported
   nonfallible local region, including all constructor/helper phases. An
   ordinary call remains potentially failing; neither a name, constant
   assignments nor NoThrow supplies permission. Missing proof/support
   declines the recipe before acquisition.
3. **Validate while retaining.** Fallible validation borrows the complete
   custodian; it neither consumes nor releases responsibility. Custody of an
   opaque resource claims no valid pointee or typed interface. An operation
   acquiring another right establishes another custodian through this
   protocol, even when pointer bits coincide.
4. **Commit once.** After all fallible checks and other argument preparation,
   transfer the complete custodian into the checked result/owner through
   ordinary move and initialization/publication. Preserve all dependencies;
   do not reconstruct ownership from Copy fields. No fallible constructor,
   postcondition, phase cleanup or exposing reentry may intervene in this
   final region. Validation failure retains custody; move vacates its source,
   never restores it. Later failure belongs to the actual new holder.
   Moving custody does not preserve its old inline storage or extend the
   output cell's lifetime; outstanding loans still constrain transfer.
5. **Settle through normal retirement.** The custodian's selected authored
   Cleanup performs the exact foreign release once while its context and
   backing remain live. It retains the existing complete readonly receiver,
   nonsuspension and body-then-reverse-declaration field order. An ordinary
   consuming helper can finish normal retirement of its input from an
   explicit `finally`, returning normally before the enclosing transfer
   continues. This neither directly calls Cleanup nor generates it on unwind.
   Never manually release and then leave the same live custodian for generated
   retirement. Raw calls require explicit unsafe authorization in the body.

The **total retirement restriction** requires the complete program, including
helper pre/post phases and transitive field retirement, to settle the right
nonthrowingly and nonsuspendingly on valid inputs. This role-specific
restriction preserves throwing normal Cleanup and the
[NoThrow retirement direction](no-throw-retirement-and-proof-boundaries.md#proposed-source-and-proof-rules).
External release remains an unsafe provider truth obligation; void or ABI
agreement proves no settlement.

| Outcome | Required holder and cleanup |
|---|---|
| Failure before invocation | No new acquisition; earlier custodians retain their actual scope obligations. |
| Complementary normal result or admitted acquisition exception | Apply the existing provider-settlement rules; no adoption/release from failure bytes. Exception does not establish vacancy. |
| Validation failure or crossed return/break/continue | Authored cleanup settles held custodians once in the required order, preserving the ordinary pending transfer/failure. |
| Successful handoff | Only the destination owns responsibility; source cleanup cannot replay it. |
| Release leaves a right live, throws or needs async settlement | Outside this slice: retain actual unfinished responsibility/outcome, never claim settlement or blindly retry. No safe recovery/remaining-owner transport is selected. |
| Fatal interruption or provider-contract breach | No settlement/finally guarantee or new recovery policy; preserve the existing failure/termination boundary. |

Multiple-resource schedules must respect actual acquisition order, ordinary
field retirement order and every surviving dependency. Failed unpublished
construction keeps reverse acquisition without incomplete-parent Cleanup.
An exceptional cell cannot be inspected to reconstruct custody.

## Integration and independent gates

These are follow-up integration points **if separately adopted**, not edits
or approvals made here.

| Owner | Exact integration |
|---|---|
| [Cell owner, resource section](aggregate-native-outputs.md#resource-ownership-adoption-and-typed-interfaces) and [resource record](native-pointer-output-ownership.md) | Link this bounded recipe as a separate candidate; preserve cell eligibility, publication, provider failure obligations and independent resource approval. Add no ownership option or automatic adoption. |
| [Construction/publication](../specs/language/ownership-and-cleanup.md#construction-and-publication), [complete Cleanup](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement), [exceptional exits](../specs/language-contracts.md#33-normal-and-exceptional-exits) | Integrate the total custodian subset using ordinary unique transfer, actual holders and explicit finally; preserve general Cleanup and construction rules. |
| [Dependencies](../specs/language/dependencies-and-views.md#stored-values-and-flow) and [owner lifetimes](../specs/lifetimes.md#owner-transfer-and-cleanup-contracts) | Preserve whole-source captures/backing, acquisition generations and cleanup endpoints through wrapping; annotations acquire no right or hidden retain. |
| [Native owners](../specs/targets/windows-x64-call-interface.md#6-complete-owners-and-retirement-entries), [native roles](../specs/native.md#native-declaration-roles), [deployment](../specs/targets/windows-x64-deployment.md#4-selected-closure-origin-and-replacement) | Require exact provider types/modes, convention, receiver/layout, failures, allocating/releasing domain and retained code lifetime through forwarding; select no universal release ABI. |
| [Logical records](../specs/representation/logical-records.md), [binding/replacement](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions), [consumer checking](../specs/representation/reader-trust.md) | Preserve complete Unique/Cleanup/dependency/failure facts through binding, traversal/rewriting, mandatory verification and lowering. Consumers check structure/support/binding, not provider semantic truth. Missing carriers reject; no trust certificate. |

Prerequisites: independently selected cell/resource directions and exact
registrations, supported unique construction/retirement, scoped proofs and
explicit finally, and complete backing/target/provider binding. Safe exported
wrappers expose ordinary checked ownership/dependencies, not raw-bit
construction privileges.

Remaining external decisions are:

* [Foreign method identity](native-output-foreign-interface-method-identity.md)
  owns nominal method/dispatch binding, not a checked interface-value
  representation. That representation remains independently unselected;
  neither proposal creates an I value or blesses a raw receiver.
* [Direct-import provider binding](native-output-direct-import-provider-binding.md),
  [cell private persistence](aggregate-native-output-private-persistence.md)
  and [foreign identity persistence](native-output-foreign-interface-private-persistence.md)
  retain separate exact schemas and approval. No revision/row/flag or
  compatibility reader is selected; equal version bytes select no alias.
* [Checked-reference outputs](function-pointer-checked-output-parameters.md)
  and [indirect foreign-call contracts](standard-native-import-function-pointer-contracts.md)
  remain separate; a release pointer bypasses neither gate.
* Release that can fail while retaining responsibility, asynchronous release,
  unsupported affinity and failure-live acquisition outputs require their own
  disposition/transport and cleanup decisions.
* No GC architecture changes: the established [integrated runtime direction](integrated-gc-runtime-boundary.md)
  remains one runtime/domain interface with interchangeable collectors;
  card tables remain collector-internal. Roots/pins do not adopt or settle
  resources.

**Alternative:** keep raw responsibility entirely in unsafe wrapper control
flow until public-owner publication. It requires an authored unsafe
single-holder invariant that Copy bits cannot enforce. A consuming fallible
adopter needs explicit remaining-responsibility outcomes, not source revival.

## Acceptance cases

These are proposed tests, not support or execution claims. Known source/body
violations diagnose or decline before affected use; false opaque provider
promises are provider breaches, not facts proved by metadata validation.

| Case | Required evidence/outcome |
|---|---|
| Unrelated synthetic pointer-reference and integer-handle providers | Record acquisition generations and exact provider releases once after successful adoption/retirement; no symbol/sample recognizer. |
| Dirty normal-failure bytes; admitted acquisition exception after private allocation | Provider settles unpublished acquisitions; no caller adoption/release. Preserve vacant versus indeterminate state. |
| Validation failure or crossed return/break/continue before commit | Explicit finally settles held custodians once and preserves ordinary transfer/failure; no generated unwind Cleanup. |
| Later acquisition/argument fails with earlier custody live | No unentered call; settle actual earlier acquisitions in required dependency/acquisition order. |
| Fallible custody/commit constructor, postcondition or consumed-parameter cleanup | Decline the recipe; no name/body-derived NoThrow default or early publication. |
| Copy/use-after-move/repeated settlement; two acquisitions with equal bits | Reject duplicating/replaying one unique responsibility; independent acquisitions require two releases, supporting ABI copies do not. |
| Null/non-null success; borrowed copies; expired/underaligned backing or loan into custodian storage | No inferred sentinel/pointee/lease or new right; enforce actual validity/dependencies and move exclusion. |
| Fixed/validated identity versus caller-selected IID, wrong identity or equal slot | Typed publication needs admitted identity/binding; failed validation releases only through the established untyped contract. |
| Release leaves a right live, throws or needs suspension/unsupported affinity | Decline total retirement; injected breaches must not count as settled success or trigger blind retry. |
| Complete owner, failed construction, nested release contexts | Check body/field versus acquisition order; no incomplete-parent Cleanup, double release or early context retirement. |
| Leaf origin forwarded through another Module; replacement/unload | Reject changed release domains or premature unload. Selected persistence/re-emission and JIT/AOT retain facts or decline unsupported use. |
| Unique custodian used as NativeOutput T | Reject: rootless/pointer-sized fields do not make a checked owner eligible Copy cell data. |

Use public compiler APIs and supervised headless native execution under the
[qualification workflow](../building.md#native-qualification), retaining final
source/schema/configuration and native-input identity. Emission alone proves
no resource lifetime behavior or milestone completion.
