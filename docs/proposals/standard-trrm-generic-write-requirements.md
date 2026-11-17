# Standard TRRM generic Write requirements

> [!IMPORTANT]
> **Unapproved proposal; provisional direction only under the user's
> missing-contract workflow.** This document approves and implements nothing.
> Feature 27, its prerequisites, admission triggers and G1 catalog amendment
> below are candidates, not current assignments. Accepted source `write` semantics
> and existing Unique, Blittable, interface and associated-relation contracts
> remain unchanged.

## Decision and evidence

Recommend **reconciling the already assigned encoding and independently
admitting the generic Write contract**, for both TypeDef-owned and
MethodDef-owned type formals. **Amend the unpublished G1 candidate in place**
with candidate **`27/1=GenericWriteRequirements`**;
do not reinterpret the constant-term Feature 24 or make a type-only contract
depend on generic-method calls.

The owning evidence distinguishes assignments, payload interpretation and
admission:

* **Explicit numeric assignments.** The
  [authoritative registry](../specs/representation/registry.md#1-authority-and-schema-identity)
  makes its numeric domains normative and calls disagreements specification
  defects. The [JSON registry](../specs/representation/trrm-1.registry.json)
  has `metadata.sections`' Constraint entry assigning `001A`, stride 12
  and the four fields below. Distinct fields
  **`metadata.constraint_kinds`** explicitly assign
  `1=InterfaceInstance, 2=IntrinsicProperty, 3=AssociatedRelation`, and
  **`metadata.intrinsic_property_codes`** explicitly assign
  `1=Unique, 2=Write, 3=Blittable`. Code 2 is verified there, not inferred
  from the row stride. The
  [metadata table's GenericParam/Constraint row](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
  independently assigns kinds 1/2/3 and their nonzero kind-specific payload.
* **Payload namespace and residual contradiction.** The JSON enum lists do
  not themselves spell out the per-kind payload-namespace mapping. The
  [logical generic records](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending)
  give the same kinds/property codes, and
  [IBGR-23/24](../specs/conformance/il-binding-and-replacement.md#5-graphs-and-using-contexts)
  require the in-row payload to be an interface TypeSpec, property code or
  associated-relation reference by kind, with immediate-access Write checking.
  Against that evidence, metadata's later source-Write paragraph saying its
  **physical Constraint kind** remains unassigned contradicts both its own
  table and these assignments. The logical chapter's introductory physical
  codec disclaimer and later “Exact typed payload encodings and registry
  assignments remain under review” are likewise overbroad if read to revoke
  the assigned kind/code. Narrow those deferrals to the remaining codecs;
  they do not justify a second Write kind or payload interpretation.
* **Admission remains a real decision.** The accepted catalog has no
  registered generic-constraint capability. Assigned shape does not admit
  generic use. [G1](standard-trrm-generic-method-call-body-encoding.md#exact-schema-and-capability)
  proposes `23/1=GenericMethodCalls`, including method-formal contracts and
  constraints, but does not independently admit a TypeDef-only Write
  declaration. [CG1](standard-trrm-constant-generic-methods.md#exact-selection-and-owner-integration)
  proposes independent `24/1=ConstantGenericTerms` for constant
  formals/actuals/defaults and term carriers. It is unapproved and is not
  generic-constraint admission. Thus metadata's separate claim that generic
  feature admission remains unassigned is consistent with the accepted
  catalog, unlike its physical-kind deferral.

Current code is evidence of interpretation, not approval:
[TrRmConstraintTableReader](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstraintTableReader.cs)
recognizes kinds 1..3 and inline property codes 1..3;
[the G1 writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceWriter.cs)
emits `(kind=2,payload=2)`, and
[its reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceReader.cs)
reconstructs `RequiresWritable`.
[Binder.StaticProtocols](../../sources/Terrum.CodeAnalysis/Semantics/Binder.StaticProtocols.cs)
and [SemanticVerifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs)
check that requirement against actual access and caller-formal guarantees.
The included [source tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Generics/StaticProtocolTests.cs)
assert actual/default and outer-versus-inner Span behavior;
[G1 tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGenericMethodSourceWriterTests.cs)
assert source-free Write forwarding.
[GenericNominalTypeTests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Generics/GenericNominalTypeTests.cs)
still reject a Write-constrained source struct in the closed-struct profile.
The [implementation summary](../implementation.md) and
[CG-006](../implementation/conformance-gaps.md) retain broader generic
support gaps. These active test sources are not a fresh test result or
evidence of the proposed capability.

| Alternative | Disposition |
|---|---|
| Correct the stale encoding sentence; use G1's candidate 23 for method-owned Write contracts only | Sufficient for a deliberately method-only slice. It leaves TypeDef-only publication/admission undecided. Current rejection of such source forms is an implementation limit, not a restriction of the accepted source contract. |
| Broaden 23 to every Write-bearing TypeDef | Possible, but makes a declaration-only type provider require generic-method capability even with no method formals or GenericCallTarget. A consumer's later call must not determine its provider's manifest. Not selected. |
| **Independent Write-contract capability** | **Selected.** One contract codec and admission rule across both formal owners; method calls and constant terms remain independent capabilities. |
| One new capability for all Constraint kinds | Larger than this decision: interface/witness and associated-identity codecs have separate unresolved dependencies. Do not bundle or approve them here. |

The unresolved approval is this independent capability boundary and amendment
of G1's exact candidate catalog, not Write's accepted meaning or property code.

## Candidate encoding and exact selection

Keep required section `001A`, its 12-byte stride and little-endian fields:

| Offset | Field | Write requirement |
|---:|---|---|
| 0 | `u32 ownerGenericParam` | Required one-based GenericParam row, high byte zero, within `1..00FF_FFFF` and the actual table. |
| 4 | `u16 kind` | `2`, the assigned intrinsic-property kind. |
| 6 | `u16 flags` | Zero. |
| 8 | `u32 payload` | Inline unsigned property code `2`, not a reference. |

The owner is a type formal (`formalKind=1`) in its declaration's exact
formal range; its exclusive Constraint range contains this row. Its
`ownerDefId` selects TypeDef kind `13` or MethodDef kind `16`, preserving
owner-qualified ordinal identity. Constant formals cannot own constraints.
Keep GenericParam at 32 bytes and MethodSig at 24 bytes.

For owner row 1, the row alone is
`01 00 00 00 02 00 00 00 02 00 00 00`; this is not a complete artifact.
Payload 2 does not name TypeSpec row 2, GenericParam row 2, a Feature row,
DefId, witness or PropertyWitness. Codes 1 and 3 keep their Unique and
Blittable meanings; neither satisfies Write. No property, qualifier, table,
token, instruction, Body record or source syntax is added or renumbered.

### Amend G1 or add G1W1?

The [draft-versioning owner](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
requires neither compatibility with unpublished drafts nor retention of their
readers, writers or artifacts. A new admission rule therefore does not require
a second schema merely to preserve G1's current candidate contract.

| Exact-selection alternative | Assessment |
|---|---|
| **Amend G1 itself; retain candidate identifier `trrm/1-draft-generic-method-calls-1`** | **Selected.** G1 already represents Write constraints; this changes its candidate catalog/admission checks without changing the Constraint wire grammar. One current exact G1 agreement suffices. Earlier G1 artifacts/readers need not remain compatible, and no parallel contract variant is retained. |
| Introduce distinct unapproved `trrm/1-draft-generic-method-calls-1-write-requirements-1` (G1W1), leaving pre-amendment G1 independently selectable | Coherent if both admission contracts are intentionally needed for an experiment or separately supported exchange. It adds a second exact catalog/selection path and corresponding producer, consumer, dependency and conformance distinctions despite the unchanged row grammar. No such coexistence requirement is established here; keeping both adds unnecessary schema complexity. Not selected. |

The independent Feature boundary and the number of schema candidates are
separate decisions: candidate 27 does not require a separate decoder mode.
Do **not** select G1W1 or retain the pre-amendment G1 catalog as a parallel
current candidate under this recommendation.

Propose G1's **amended** whole-artifact agreement as its complete existing
baseline/SF1/GenericCall composition plus this document's registration and
checks. Keep its candidate identifier above, `(1,0)`, governing edition 1
and register codec `01`; no on-wire selector is added. Preserve G1's bounded
constant-formal, actual, default and forwarding rules unchanged.

Exact out-of-band agreement must identify the amended G1 catalog and governing
text, not merely the reused draft name or version pair. A Write-bearing input
without effective 27 fails the amended rules; do not retry pre-amendment G1,
infer a catalog from Feature presence, or accept G1W1 as an alias. Earlier
bytes unchanged by this amendment are usable only under an independently
established amended-G1 agreement, not automatic compatibility.

CG1, G1L1, C1 and private persistence remain separately selected agreements.
This amendment selects none of them and does not silently rewrite their
catalogs through a link to G1. Integrating it into a composed candidate requires
an explicit update to that candidate's complete agreement; no extra successor
is mandatory for an unpublished composition either. CG1's independent
constant-term extensions are not selected here. The separate
[G1L1 integration proposal](standard-trrm-g1l1-generic-write-requirements.md)
defines its in-place Feature 27 composition; this G1 amendment does not select
that candidate.

## Candidate capability and Module ownership

Propose **Feature `27`, revision `1`, governing editions `[1]`, immediate
Feature prerequisites `[]`**. Its coherent scope is publishing, interpreting,
binding, substituting and preserving the generic Write requirement, including
source-free new instantiations and replacement checks. It adds no runtime
dispatch or shared-generic ABI.

Feature IDs here are decimal; section/DefId kinds are hexadecimal. The checked
owners establish the following allocation status, not approved Features 23–27:

| Exact owner | Verified status |
|---|---|
| [Registry JSON](../specs/representation/trrm-1.registry.json), `semantic_manifest.features`; [manifest owner](../specs/representation/semantic-manifests.md#2-governing-baseline-and-capability-registration) | Registered Features are **1–7 and 13**, at revision 1. The separate `metadata.feature_ids` projection also lists 9–12 as `assigned-registration-pending`; these are not complete registrations. Neither array contains 23–27. |
| [G1 exact schema/capability](standard-trrm-generic-method-call-body-encoding.md#exact-schema-and-capability) | Unapproved candidate `23/1=GenericMethodCalls`, prerequisites `[]`; not an accepted registry assignment. |
| [CG1 exact selection](standard-trrm-constant-generic-methods.md#exact-selection-and-owner-integration) | Unapproved candidate `24/1=ConstantGenericTerms`, prerequisites `[]`; not generic-constraint admission or an accepted assignment. |
| [Construction-2 candidate assignments](standard-trrm-c1-construction-runtime-boundary.md#exact-semantic-successor-and-candidate-assignments) | Unapproved candidate `25/1=ClassConstructionBodies`, no unconditional Feature prerequisites, for that construction agreement; absent from the normative Feature arrays. |
| [Label registration](standard-trrm-g1-interface-parameter-labels.md#record-inputs-and-closure) | Unapproved candidate `26/1=MethodParameterLabels`, prerequisites `[]`; absent from the normative Feature arrays. |
| This proposal | New unapproved candidate `27/1=GenericWriteRequirements`, prerequisites `[]`; no current registry assignment or registration. |

Candidate 27 avoids the separately proposed 23–26 uses without claiming those
proposals reserve or approve numbers. Native 6/7 already have accepted
assignments; candidate 8 concerns native outputs, not generic Write.
Unpublished numbers may be reallocated, but no such redesign is needed here.

Require effective 27/1 in **each using Module** when its retained content:

* Declares a Write-bearing GenericParam, whether owned by a TypeDef or
  MethodDef, including an uncalled, bodyless or expected/import declaration.
* Uses a declaration's Write contract through a retained constructed type,
  nested signature/default, expected/provider contract view, or G1 owner or
  method instantiation. Resolve the declaration in that use's Module context;
  a packed direct reference cannot bypass the requirement.

For local rows, obtain the owning Module through GenericParam.ownerDefId and
the selected TypeDef/MethodDef, not a Constraint row number or artifact-wide
Feature union. An imported expected view belongs to its retaining Module;
the provider independently owns its published declaration's requirement.
Unrelated declarations in a dependency do not become caller-local uses,
but the consumer still admits that selected dependency Module's complete
requirements. Sharing TypeSpecs or Feature lists does not merge manifests.
These candidate triggers govern amended-G1 Modules; independently selected
dependency schemas keep their own catalogs and require separate support. This proposal supplies
no implicit cross-schema support mapping or provider-manifest relabeling.

| Retained use in a Module | Candidate requirements, in addition to independent type/Body/target obligations |
|---|---|
| TypeDef-owned type formal with Write, no generic method use | 27; neither 23 nor 24 follows. |
| MethodDef-owned type formal with Write | 27 and G1's independent 23, including declaration-only/expected views. |
| Generic method or GenericCallTarget without Write anywhere in its used contracts | G1's 23; no 27 merely from generic framing. |
| Ordinary writable TypeSpec, or only Unique/Blittable/interface/associated constraints | No 27 merely from those forms; preserve their existing admission gates. |
| CG1-only constant-term extensions | Not admitted by this G1 amendment; retain G1's bounded constant rules. CG1's proposed 24 remains separate; an explicitly amended CG1 composition would need its independent constant-term and Write requirements. |

No unconditional 23/24, native, LayoutQueries, frozen-value, evaluator or
witness prerequisite follows from testing immediate access. Any such
retained use still requires its own supported closure. Feature 27 does not
complete generic-class value/layout execution, hidden evidence, property
branches, associated relations or missing witness codecs.

A fully discharged specialization needs no 27 solely because its source once
had the constraint. Any surviving expected view or relied-on open obligation
retains its declaration/contract dependency and requirement. If no supported
carrier can preserve that dependency, refuse the output; do not invent a
proof flag or silently drop it.

## Checking and preservation

Preserve [generic write semantics](../specs/language/types-and-normalization.md#generic-write-requirements)
and [WRITE-01..29](../specs/conformance/source-types-and-normalization.md#generic-write-requirements).
After ordinary argument selection, alias expansion and normalization, check
the complete argument's immediate access. Defaults and explicit arguments
must already satisfy it; open forwarding needs a published caller guarantee.
Do not search stored payloads, qualify an inferred readonly actual, infer
presence/initialization, or derive Copy, pinning, unsafe or sharing authority.
Existing ownership/nullability domains still apply independently.

Producers check authored declarations/uses and publish truthful contracts.
Before affected use, consumers validate exact schema/support, checked
section counts/extents, formal/range ownership, zero flags/reserved fields,
closed kind/property domains and every referenced declaration/import context.
Unknown property codes or missing effective 27 reject; optional data and
another Module's feature list cannot substitute.

When forming a **new** binding or specialization, check satisfaction from
supported normalized type facts or published caller constraints. This is
not semantic re-proof of a producer's already represented bodies.
[Consumer trust](../specs/representation/reader-trust.md) remains unchanged:
structural and binding checks are mandatory; deeper truth audits are optional.

Carry the requirement through bound signatures, shared traversal, verifier
reconstruction, dependency views and lowering. A provider cannot newly
require Write where the retained expected contract did not; removing that
restriction is considered under the existing directed satisfaction rules,
without erasing the importer's original expected view or relied-on facts.
Keep owner substitution before ordinal method substitution.

Packing/re-emission maps Constraint.ownerGenericParam through the formal map
and rebuilds exclusive formal/constraint ranges, declaration references,
Module Feature ranges and affected indexes. **Do not remap payload 2**:
it is a code, not a TypeSpec/row reference. Preserve complete inner/outer
qualification and original expected/provider contexts. Packing selects one
destination schema supporting every retained obligation, not a per-Module
decoder.

## Exact owner amendments if adopted

These are proposed edits, not changes made by this document:

| Owner | Amendment |
|---|---|
| [G1 proposal](standard-trrm-generic-method-call-body-encoding.md#exact-schema-and-capability) | Amend its existing exact-selection/capability sections in place with candidate 27 and these retained-use rules, preserving 23's independent triggers and prerequisites. Supersede its pre-amendment candidate catalog; add no G1W1 identity, old-G1 compatibility mode or fallback. This is a proposed edit to an unapproved owner, not a normative allocation. |
| [IL metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Replace the sentence claiming Write's physical kind is unassigned with `Constraint.kind=2, flags=0, payload=2` and the type-formal owner rule. Replace the undifferentiated generic-admission deferral for this contract with the selected 27 scope/triggers; retain all other generic execution/export gates. |
| [Logical generic records](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending) | Make the inline property-code namespace explicit. Qualify the chapter's blanket codec-deferral wording so it does not contradict explicit registry assignments; leave remaining proof/witness/relation codecs gated. Do not change other kinds or property meanings. |
| [Machine registry](../specs/representation/trrm-1.registry.json), [registry chapter](../specs/representation/registry.md) | Preserve the existing `metadata.sections`, `metadata.constraint_kinds` and `metadata.intrinsic_property_codes` assignments; explicitly connect kind 2's payload to its inline property-code domain. If integrated normatively, register 27 consistently in both Feature projections with the proposed scope/editions/prerequisites and explicit amended-G1 catalog. This document does not register 23–27 or select a CG1 composition. |
| [Manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md) | Add the per-Module retained-use rules, independent 23/24 separation and before-use support checks. Selecting a schema or recognizing a row is not support. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md) | Specify owner-qualified Write obligations, new-binding checks, the scalar-code versus row edge and preservation rules above. Add no new constraint ordering, duplicate policy or semantic re-proof duty. |
| [Binding cases](../specs/conformance/il-binding-and-replacement.md), [manifest cases](../specs/conformance/il-semantic-manifests.md) | Add the independent representation/admission cases below. Source grammar, complete-type semantics and existing source Write cases require no amendment. |

The proposed amendment depends on the unapproved G1/SF1 selection, not approval
inferred from prototypes. Approving it would select this in-place amendment,
not two parallel G1 candidates, unrelated capabilities or compiler support.

## Required positive, negative and boundary cases

Use independent complete artifact vectors and public compiler APIs, not only
writer/reader round trips. These are acceptance requirements, not results.

| Class | Case and required outcome |
|---|---|
| Positive: encoding | A valid type formal owns `(2,0,2)`; accept the inline code even without TypeSpec row 2. Repeat for distinct TypeDef/method ordinal-zero formals without conflating their identities. |
| Positive/negative: access | Writable outer ref/pointer/registered view, eligible alias and default satisfy; readonly actual/default and readonly outer view containing writable elements fail. Optional/unique cases retain the independently required domains and prove no presence, initialization or Copy. |
| Negative: source/binding | A constant formal carries Write; explicit readonly `T` is supplied by a writable value; or unconstrained caller `U` forwards to a Write requirement. Reject without qualification repair or witness/private-body inference. |
| Negative: structure | Wrong owner/range, shared or orphan Constraint, constant-formal owner, truncated row, nonzero flags, kind 0/unknown, or kind-2 code 0/4/F2/F3/token lookalike rejects. Codes 1/3 remain recognized but cannot satisfy Write. |
| Positive: admission | A TypeDef-only Write provider requires 27 without 23/24. A method-owned Write declaration requires 23+27 without 24. A type-only generic method without Write and an ordinary writable type do not acquire 27. Test declaration/contract handling separately from value execution. |
| Negative: admission | Omit 27 from an uncalled/bodyless/expected Write declaration, or supply only 23 or 24; reject before affected use. Supply 27 without 23 for a generic MethodDef/GenericCallTarget; still reject the missing method capability. |
| Cross-Module | Pack a Write provider, a Module using its Write contract and an unrelated Module. Require 27 in the first two, not the third. A caller of an unrelated nongeneric provider member need not acquire 27 locally, but provider admission still includes its unused retained Write declaration. Repeat with expected Imports and direct packed references. |
| Cross-feature | Source-free amended-G1 forwarding retains the exact caller requirement and closed actual; a provider that strengthens a non-Write expected formal fails directed binding. Other property/interface/relation gates and backing/unsafe obligations remain effective. |
| Boundary | Test first/last permitted formal and Constraint rows, zero versus absent ranges, count `00FF_FFFF` versus `0100_0000`, checked extent/range overflow, and exact property code 2 versus words with nonzero high bits. No wrapping, masking or wrong-table fallback. |
| Preservation | Independently permute GenericParam, Constraint, TypeSpec and declaration/import rows and shared Feature ranges. Rebuild owners/ranges/contexts while payload 2 remains unchanged; compare source-free obligations after re-emission. |
| Schema/revision | Explicitly select the amended G1 catalog. A reused G1 name, same `(1,0)` bytes or Feature 24 does not establish that agreement; Write-bearing input missing 27 fails with no old-catalog retry. G1W1 is not an alias/alternative selected by this proposal. Unknown/conflicting 27 revisions refuse support; otherwise valid reordered/identically repeated Feature pairs retain ordinary manifest semantics. |
