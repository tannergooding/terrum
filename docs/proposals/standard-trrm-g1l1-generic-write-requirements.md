# Standard TRRM G1L1 generic Write integration

> [!IMPORTANT]
> **Unapproved proposal; provisional direction only under the user's
> missing-contract workflow.** This document approves no normative contract,
> registry assignment or implementation. It defines only G1L1's integration
> of the existing [generic Write proposal](standard-trrm-generic-write-requirements.md).

## Decision and scope

**Amend unpublished G1L1 in place to include candidate
`27/1=GenericWriteRequirements`.** Reuse the Write proposal's complete
registration, representation, admission, binding and preservation rules;
do not create a second Write design or admit non-Write constraints, new value
forms, or other declaration subsets.

The Write proposal explicitly amends G1 alone and requires a complete-agreement
update for compositions. The [label proposal](standard-trrm-g1-interface-parameter-labels.md#exact-selection-allocations-and-alternatives)
selects G1L1 separately. Its link to G1 therefore does not select the Write
amendment. This document supplies that missing decision.

The active [generic-method test fixture](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGenericMethodSourceWriterTests.cs)
sets `GenericMethodCallsSchemaIdentity` to the G1L1 literal below.
`EmitsAndReconstructsGenericSpanTypeArguments` exercises publication of the
Write-constrained `UseWrite` definition and source-free consumer binding
through that selection. The fixture is an integration case, not an agreement
or approval; this proposal explicitly amends G1L1's candidate catalog.

| Alternative | Disposition |
|---|---|
| **Amend G1L1; retain its existing candidate identifier** | **Selected.** The Constraint and label wire grammars are unchanged. One current agreement suffices; no coexistence requirement for the old catalog has been established. |
| Distinct successor `trrm/1-draft-generic-method-calls-parameter-labels-1-write-requirements-1`, leaving old G1L1 separately selectable | Coherent if both catalogs need independent exchange/support. It adds another selection path and test matrix without a needed representation distinction. Draft compatibility does not require it; not selected. |

## Amended exact G1L1 agreement

Retain **`trrm/1-draft-generic-method-calls-parameter-labels-1`**,
format `(1,0)`, governing edition `1` and register Body codec `01`.
Its **one complete agreement** is:

1. The accepted `trrm/1-draft-layout-queries-1` baseline and catalog.
2. The complete [SF1 scalar delta](scalar-frozen-value-registration.md),
   including all 24 scalar codecs and `11/1 -> []`.
3. The complete [G1 generic-call delta](standard-trrm-generic-method-call-body-encoding.md#exact-schema-and-capability),
   with `23/1 -> []`, plus the Write proposal's
   [candidate 27 registration and retained-use rules](standard-trrm-generic-write-requirements.md#candidate-capability-and-module-ownership).
   Feature 27 has governing editions `[1]`, revision `1` and immediate
   Feature prerequisites `[]`.
4. The label proposal's complete **G1-only L1** delta: section
   `0066=MethodParameterLabel`, its 12-byte rows, bounded owner set,
   source-view preservation and `26/1 -> []`.

This explicitly supersedes the label proposal's **pre-integration G1L1
composition and G1L1 catalog row**, not its other agreements or label rules.
Preserve G1's incomplete Feature 9/10/12 registrations and gates, including
`10 -> 11` and `12 -> {10,11}`; do not substitute C1's reconciled catalog.
Feature 24 and CG1 constant terms are not selected. No numeric allocation,
row width, source syntax, call operand or source-map grammar changes here.
Constraint `(kind=2, flags=0, payload=2)` keeps the Write proposal's assigned
inline-code interpretation.

The [draft-versioning owner](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
permits this in-place replacement without retaining prior artifacts/readers.
Out-of-band agreement must identify **this amended catalog and its governing
text**, not merely the reused literal, `(1,0)`, a Feature list or a section.
There are not two current G1L1 interpretations: an input needing 27 but
omitting it fails; never retry the pre-amendment catalog, infer a schema from
27's presence, or treat the unselected successor as an alias. Earlier bytes
can be used only under independently established amended-G1L1 agreement and
its complete checks, not automatic compatibility.

Keep the label proposal's version-1 and version-3 source-input pairings,
including empty lists, with **this amended G1L1**. Version 2 remains CG1,
not another G1L1 route. Original G1, CG1, C1-family label agreements,
receiver/default successors and private persistence receive no transitive
amendment. Separately supplied dependencies retain their independently
selected supported schemas and catalogs; this proposal creates no cross-schema
support mapping. One packed artifact still selects one destination schema.

## Independent per-Module requirements

Apply the Write proposal's [Module ownership and retained-use rules](standard-trrm-generic-write-requirements.md#candidate-capability-and-module-ownership)
unchanged to amended G1L1. Require effective 27/1 for each Module declaring
a Write-bearing type formal or retaining a use of that contract, including
uncalled/bodyless declarations, expected Imports, constructed/default/nested
terms and owner/method instantiations. Determine declaration ownership through
`GenericParam.ownerDefId` and its TypeDef/MethodDef's Module, not Constraint
row order. Expected and provider views retain their own Module contexts.

Features 23, 26 and 27 each retain prerequisites `[]` and independent triggers:

| Retained content in a Module | Requirements attributable to these three capabilities |
|---|---|
| TypeDef-owned Write formal only | 27; neither 23 nor 26 merely from that formal |
| Private unexported generic helper with Write | 23 + 27; not 26 merely from the helper |
| Exported or expected generic MethodDef with Write | 23 + 26 + 27, including zero ordinary parameters |
| Exported or expected generic MethodDef without a used Write contract | 23 + 26; not 27 merely from generic framing or labels |
| Admitted nongeneric interface requirement without a used Write contract | 26; not 23 or 27 merely from interface publication or owner type formals |
| GenericCallTarget alone | 23, plus 27 if it uses a Write contract; no 26 merely from an ordinal-bound call |
| Ordinary writable TypeSpec, without a retained Write requirement/use | No 27 merely from writability |

These are not complete manifests: other retained contracts keep their own
requirements, including independent SF1, layout, witness, target and runtime
gates. Unsupported TypeDef/value/interface forms still refuse; adding 27
does not supply their missing codecs.

A provider's unrelated Write declaration does not impose caller-local 27,
but its selected dependency Module still requires admission of its complete
manifest. Sharing Feature ranges or packing direct references does not union
manifests or bypass a using Module's requirement. Remove 27 only when no
retained Write contract/use remains after a valid discharge; surviving
expected views keep it even if the current provider relaxes the constraint.

## Preservation, owners and acceptance cases

Use the Write and label proposals' producer checks, mandatory structural and
new-binding checks, original expected/provider views and typed transformations
together. Consumers need not semantically re-prove producer-published bodies;
[reader trust](../specs/representation/reader-trust.md) is unchanged.
Re-emission rebuilds formal/Constraint ranges, label MethodDef/String edges,
TypeSpecs, Body maps/indexes and Module Feature ranges in their proper contexts.
Write payload `2` is not remapped as a reference. Labels neither satisfy
Write nor change its source semantics, and specialization/lowering must
preserve both contracts without adding runtime evidence or a generic ABI.

On adoption, update the label proposal's G1L1 selection/catalog rows and make
the Write proposal's listed [owner amendments](standard-trrm-generic-write-requirements.md#exact-owner-amendments-if-adopted)
explicitly applicable to G1L1. The [registry](../specs/representation/registry.md),
[manifests](../specs/representation/semantic-manifests.md) and
[admission owner](../specs/representation/requirements-and-admission.md)
retain their respective authority; accepted Feature catalogs are not changed
by this proposal. No other candidate is updated by reference.

Inherit both proposals' independent structural/boundary cases. Add these
**G1L1 integration gates, not claimed results**, using public compiler APIs
and independently constructed artifacts, not only paired round trips:

| Case | Required outcome |
|---|---|
| Positive source-free generic publication and forwarding, including the existing Span regression, authored labels and supported actual/default terms | Preserve Write obligations and label vectors together; check each Module's independently triggered 23/26/27 without producer source/debug/configuration at consumption |
| Independent trigger matrix above, including uncalled/bodyless expectations and zero-parameter exports | Derive requirements from retained contents; zero label rows do not remove 26, and schema selection alone does not add 23/26/27 |
| Missing 27 on a Write declaration/use; only 23/26 supplied; conversely missing 23 or 26 on an exported Write-generic method | Reject the missing independent capability before affected use, including unused declarations and empty label vectors |
| Readonly actual/default or unconstrained open forwarding; valid caller Write guarantee; provider strengthens or relaxes an expected contract | Apply the Write proposal's directed satisfaction rules; labels cannot repair access, strengthen caller guarantees or erase the original expectation |
| Provider with retained Write declaration, consumer using that contract, and unrelated Module/caller of an unrelated member; repeat with expected Imports and packed direct references | Require 27 locally only where used, admit the provider's complete requirements, and preserve independent 23/26 ownership even with shared/overlapping Feature ranges |
| Permuted declaration/formal/Constraint/TypeSpec/label/String rows and Body maps, then source-free packing/re-emission and independent import | Rebuild typed edges/ranges/indexes and each Module's manifest; preserve original label/Write views and literal property code 2 |
| Explicit amended agreement versus reused name/version alone, missing or unsupported/conflicting 27 revisions, old-catalog Write bytes without 27, or unselected successor name | Reject unsupported/mismatched selection or requirements without old-catalog fallback, aliases or Feature-driven decoder selection; ordinary reordered/identically repeated Feature pairs retain manifest semantics |
| Schema isolation, including separately supplied CG1/C1-family inputs and source-map versions 1/3 versus 2 | Accept only explicitly agreed schema/input pairs; do not extend other catalogs' 27 support by inheritance. Admit dependencies under their own schemas; pack only into an explicit destination representing all retained obligations |

No G1L1 integration-policy choice remains deferred under this provisional
recommendation. **Normative approval, implementation alignment and final-input
qualification remain outstanding.**
