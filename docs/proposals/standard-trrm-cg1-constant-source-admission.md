# CG1 ConstantSource schema admission

> [!IMPORTANT]
> **Unapproved proposal; provisional direction under the authorized
> missing-contract workflow.** This document narrows an ambiguous admission
> rule in the [CG1 proposal](standard-trrm-constant-generic-methods.md).
> It does not amend normative chapters, compose another draft, complete
> Feature 12, or claim implementation or execution support.

## Decision and scope

For exact **`trrm/1-draft-constant-generic-methods-1`**
(`GenericConstantMethodsDraft`), admit ConstantSource **kinds 1, 4 and 5
only**, subject to their existing structural, contextual and support checks.
Reject kinds **2 and 3 in every serialized row**, including unreferenced
rows, and reject their unsupported backing sections as specified below.
Keep their assigned meanings in schemas that separately admit them; neither
kind becomes an alias for a frozen leaf or template.

This is a clarification of CG1's intentional exclusion of general-predicate
and concrete-request successors, not a new composition or optional mode.
Keep `(1,0)`, edition 1, register codec 01, all row layouts/assignments and
**`24/1` prerequisites `[]`**. Do not add an edge to 19 or 23.
The complete revised unpublished candidate must be agreed out of band;
equal bytes or the same candidate name from an older snapshot do not select
it. No compatibility reader or automatic schema conversion is required by
[format versioning](../specs/representation/format-versioning.md).

This proposal takes precedence only over CG1 wording that could admit
tags 2/3 or their backing sections. Its source semantics, nominal domains,
template layout, request-envelope semantics and independent Feature-23
triggers are otherwise unchanged.

## Verified mismatch and separate dependencies

These are source observations, not results from an executed artifact probe.

| Evidence | Consequence |
|---|---|
| [CG1 exact selection, Feature rationale and ConstantSource table](standard-trrm-constant-generic-methods.md#exact-selection-and-owner-integration) exclude general predicates and ConstantExecution successors, but retain tags 2/3 and refer to separately admitted execution/target obligations. | Exclusion is intentional. What is missing is an exact row/section admission rule, not permission to import the excluded schema. |
| [G1 selection](standard-trrm-generic-method-call-body-encoding.md#exact-schema-and-capability) leaves 10/12 incomplete and says tags 2/3 obtain no execution support from G1. [Logical records](../specs/representation/logical-records.md#assigned-pre-v1-identities) require Feature 12 for **both** generic source kinds. | CG1's independent 24 cannot complete either source kind by recognizing its tag. |
| [ConstantSource reader/writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceTable.cs) select execution/binding counts for kinds 2/3 without a schema condition; only 4/5 are CG1-gated. | An in-range payload is accepted by the table codec without establishing that its target table is admitted or validated. |
| [Structural validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs) constructs request/substitution/argument/dependency/view readers and runs execution-use validation only under `UsesGeneralPredicateGraphs()`. [Constructed-value validation](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceValueValidator.cs) can append a kind-2 execution use without that schema check. | Explicit rejection in default/value paths is not a complete guard: a constructed-TypeSpec use can be collected without the corresponding execution validator. TargetValueBinding has no corresponding typed reader in this structural path. |
| [Feature reader/catalog](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs) admit 19 only under GeneralPredicateGraphs; CG1 has 24 with no prerequisites and retains `10 -> 11`, `12 -> {10,11}`. | Adding 19 or silently changing the 12 catalog would change the selected schema, not repair a bound check. |
| [Artifact index](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) recognizes fixed strides for `0041..004A` broadly, while general-predicate extension sections have schema gates. | Framing recognition does not establish semantic admission of those baseline-assigned tables. |

**Kind 2** names a required request, not its completed value. Its support
includes the selected checked program/intrinsic, substitutions, transformed
inputs/views, executed dependencies, exact target and actual envelope.
An in-range 80-byte `0042` row cannot supply the excluded read-bound,
declaration, binding or evaluation support.

**Kind 3** names an already completed frozen result and exact target binding.
It does **not inherently require** a request, general predicate graph,
ImmutableData identity or Feature 19, and does not replay a constructor.
Its separate obligations are the exact typed frozen root, concrete target,
exclusively owned layout-dependency range, per-use target compatibility and
binding preservation. An empty dependency range is valid, not target
independence. Nevertheless, its current generic admission also requires
Feature 12; CG1 has neither completed that admission nor supplied the binding
validator. Allowing 3 through scalar decoding would silently waive both.
See the [request/value owners](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms).

## Alternatives and recommendation

| Direction | Assessment |
|---|---|
| **Explicit CG1 exclusion of 2/3 and unsupported backing sections** | **Selected.** Completes the schema boundary without widening its catalog or importing another evaluator. The discriminator set is not a claim that every tag-5 program is executable. |
| Admit kind 3 alone with a dedicated binding validator | Structurally independent of general graphs/19. It would also need an explicit amendment of its generic Feature-12 admission, or a completed applicable 12 registration, plus root/target/range/binding and transformation support. This is an additional capability decision, not a necessary consequence of admitting frozen leaves; not selected here. |
| New exact CG1 + general-predicate/evaluation successor | Coherent, but a substantive composition with its own whole-schema identity, catalog and support gates; not a switch inside CG1. It is not needed to make CG1 fail closed. |

For comparison, a concrete broader candidate could be
`trrm/1-draft-constant-generic-methods-general-predicate-graphs-1`, explicitly
composing CG1 with the complete [general-predicate graph delta](general-predicate-graph-schema.md),
[ImmutableData declaration/admission delta](immutable-data-declaration-reference.md),
[static declaration/publication delta](immutable-static-field-persistence.md),
[intrinsic identity delta](intrinsic-registration-identity.md), and
[mixed checked-Body substitution delta](constant-execution-mixed-substitutions.md).
That recipe would select `004D..0052`, `0055..005E` and the typed `0041`
identity rules, preserve CG1's `0053/0054` and `0064/0065`, and select one
eight-byte `005E` layout, never both substitution layouts.

Its graph catalog would replace the relevant lists with `10 -> []`,
`12 -> {11,17}`, `17 -> {10}` and `19 -> []`, while retaining independent
23/24 admission. Actual immutable identities, execution dependency kind 6
or view backing kind 2 would require 19 in each using Module; a request with
none of those uses would not acquire 19 merely from its source tag.
Full Feature-12 program/value/intrinsic-recipe support would still need
completion; those proposals explicitly leave gates open. This comparison
does **not** select that candidate, complete its integration, or authorize
its implementation under the CG1 identity.

## Exact text and integration owners

The following replacements are proposed for integration, not applied by
this draft.

### CG1 ConstantSource and section rules

In [CG1's ConstantSource table](standard-trrm-constant-generic-methods.md#constantsource-and-constant-positions),
replace the kind-2 and kind-3 rows with:

| Kind | Exact CG1 disposition |
|---:|---|
| 2 | Assigned elsewhere to R(ConstantExecution), `0042`; **not admitted in CG1**. |
| 3 | Assigned elsewhere to R(TargetValueBinding), `0049`; **not admitted in CG1**. |

Add immediately after that table:

> In exact CG1, every row of section 0030 must have kind 1, 4 or 5 and a
> valid reference in that kind's namespace. Kinds 2 and 3 reject even if
> their payloads are in range, their result types would match, or no
> retained declaration or Body references them. Feature rows cannot enable
> them. Validate the entire source table's discriminators and reference
> bounds before returning validated CG1 metadata; contextual scope, domain
> and support checks still apply at every use.

Add this closed exclusion list to CG1's exact-selection section:

| Unsupported section in CG1 | Reason |
|---|---|
| `0041` ImmutableData | Its selected declaration/identity and Feature-19 admission are absent. |
| `0042`, `0043`, `0044`, `0046`, `0047` | Concrete requests, request inputs/dependencies and logical views are not selected. |
| `0049` TargetValueBinding | Completed target-dependent value admission is not selected. |
| `004D..0052` | General-predicate operation/application/read-bound extensions are not selected. |
| `0055..005E` | Static publication, intrinsic identity and request-substitution extensions are not selected. |

> Reject any directory entry for a listed section, independently of
> references, reachability, otherwise valid stride, or Module Feature lists.
> Absent sections represent empty tables; zero-count/zero-length directory
> entries remain invalid under common framing. These required semantic
> sections cannot be marked optional/debug or silently skipped.

Do **not** blanket-ban the interval `0041..004B`: shared
`0045/0048/004A/004B` retain their existing query/profile/path rules and
ownership/support checks, including CG1's template target-profile field.
Their presence cannot authorize 2/3. CG1 enum sections `0053/0054` remain
selected; this is not a blanket ban on sections above `004B`.
Other unselected extensions, including `005F..0063`, remain unselected.

Well-formed unreferenced **1/4/5** sources are not forbidden by this
decision. Their row checks still run; contextual obligations are checked
where used. Do not invent a using Module for a global orphan row or a new
Feature trigger from its presence. Retained definitions/helpers keep their
own independent requirements. This distinction preserves CG1's existing
retained-use rules while closing the unsupported-discriminator hole.

### Features, requests and transformations

In CG1's additional-requirement table, replace the combined
“ConstantTemplate ... or an existing tag-2/3 source” row with:

> A ConstantTemplate's ConstEval declaration/program retains all independently
> required evaluation-form, program, target and execution support, including
> the existing Feature-12 closure. Tags 2/3 are unavailable in exact CG1,
> regardless of asserted Features. Feature 24 registers neither incomplete
> 10/12 capabilities nor general graphs/ImmutableData.

Keep all Feature-24 triggers and prerequisites `[]`, G1's independent
Feature-23 triggers, and CG1's existing 10/11/12/18 catalog unchanged.
Requiring 12 for an affected program remains a support gate, not permission
to serialize `0042`. An unreferenced row cannot repair a missing manifest.

Replace CG1's concrete-request persistence paragraph with:

> Exact CG1 cannot persist a concrete request or completed target-dependent
> binding through tags 2/3 or their excluded sections. Persisting such a
> graph requires a separately agreed exact schema that admits every surviving
> obligation; CG1 does not select one automatically. A genuinely completed,
> target-independent frozen value may use tag 1 only when no required
> request, formation, validity, provider or target obligation is erased.
> Otherwise refuse that output operation. Do not freeze one instance to
> replace an open declaration's required default/template behavior.

The existing actual-envelope selection/accounting rules remain requirements
for an independently admitted new evaluation: four u64 ceilings at
`48/56/64/72` when a request is persisted in its applicable schema, including
selected zero limits. A budget-free template remains neither an execution
occurrence nor permission for host evaluation or runtime fallback.

Replace the CG1 transformation-table row “ConstantSource 1/2/3/4/5” with
“CG1 ConstantSource 1/4/5”, mapping FrozenValueNode/GenericParam/
ConstantTemplate respectively. A separately selected source artifact may
use another supported schema, but packing into CG1 must reject a surviving
excluded graph. It must not reinterpret a tag, strip dependencies, suppress
a default, or change only version bytes.

On approval, the [logical records](../specs/representation/logical-records.md),
[metadata](../specs/il-metadata.md),
[registry](../specs/representation/trrm-1.registry.json),
[manifests](../specs/representation/semantic-manifests.md) and
[admission](../specs/representation/requirements-and-admission.md) owners
should record these **CG1-specific** discriminator/section restrictions,
without deleting the assigned meanings in other schemas. The
[reader-trust](../specs/representation/reader-trust.md) and
[canonicality](../specs/representation/encoding-canonicality.md) integration
must preserve all-row schema checking versus per-use contextual/support
checking. No normative owner is edited or adopted by this document.

## Implementation consequences and remaining gates

* **Producer:** gate both ConstantSource encoding and artifact section
  emission by the exact selection. Refuse unsupported output before
  replacing an existing destination; do not omit a retained required graph.
* **Consumer/verification:** enforce the section exclusions at schema-aware
  indexing and the discriminator exclusions when constructing the complete
  source table. Defaults, nested TypeSpecs, ConstantMap, GenericCall actuals
  and templates must not bypass that common check. Merely declining to
  collect a kind-2 use would silently lose an obligation, not fix the gap.
  Framing inspection may report unsupported input, not return it as
  validated/admitted CG1. No arbitrary Body-truth re-proof is required.
* **Binding/rewriting:** preserve allowed sources' exact domains, scope,
  original expectations and dependencies. Rebuild every affected source,
  type, formal, template, map, offset/index and optional SourceMap reference;
  numeric row equality is not a mapping. A different dependency schema does
  not automatically change the destination schema.
* **Lowering:** already checked admitted values/forwarded formals need no
  constructor replay. New tag-5 formation still needs complete admitted
  evaluation support; unavailable support refuses formation. Never use host
  execution, invented carrier bits, a runtime fallback or another source
  tag to conceal the missing request/value contract. No source, library,
  runtime or target ABI changes are selected.
* **Scope:** make the restrictions CG1-specific. Do not incidentally change
  Standard, G1 or GeneralPredicateGraphs selectors/catalogs, or infer a
  universal rule that kind 3 intrinsically requires general graphs/19.

Prerequisites for this bounded integration are exact agreement on CG1 plus
this clarification and its existing G1/SF1/Standard-enum composition.
There is **no additional proposal prerequisite for rejecting 2/3**.
No discriminator, section or Feature choice remains open for this direction;
formal approval remains pending. Future standalone kind-3 admission,
concrete-request composition and complete template evaluation are separate
decisions/gates, not completion claims for this slice.

## Required positive, negative and boundary cases

Extend the existing ConstantSource table/value-binding, artifact-index,
constructed-TypeSpec and generic-constant Feature tests through supported
public compiler APIs. These are acceptance requirements, **not claimed
passes**:

| Case | Required result |
|---|---|
| Allowed 1/4 sources in defaults, nested constant TypeSpec slots and Body/call positions | Preserve exact domain/value/formal identity and ordinary contextual checks. TypeDef-only uses require 24 without a new 23/19 edge; method uses retain independent 23. |
| Kind 5 with the exact existing template layout | Structural decoding remains distinct from admission/execution. Missing ConstEval program/read-bound/Feature-12 support still refuses affected use; no synthetic request is emitted. |
| Kinds 2 and 3, independently, in every value position and in entirely unreferenced rows | Reject under CG1 with otherwise in-range payloads, matching types and plausible backing rows. Adding 12, 17, 19, 23 or 24 cannot change the decoder or repair the forbidden form. |
| Constructed-TypeSpec kind-2 use with no default/ConstantMap use, including an unused declaration or expected Import view | Reject before validated metadata/lowering; a collected execution-use list cannot go unchecked. |
| Each excluded section, with no ConstantSource reference and otherwise correct framing | Reject; cover request-only, view-only, ImmutableData-only and TargetValueBinding-only artifacts, plus general-predicate/static/intrinsic/substitution extensions. |
| Present empty excluded section, optional-policy relabeling, or valid excluded rows after permitted rows | Reject; absence is the only empty representation. Check the entire source table, not a reached prefix. |
| References 0, 1, last row, `00FF_FFFF`, high-byte-set and out-of-count values | Retain permitted-kind namespace/count checks with bounded fixtures. Kinds 2/3 reject even at valid boundary references; no out-of-range accident is needed to trigger the gate. |
| Well-formed duplicated/reordered/unreferenced allowed sources; shared profile/path records and CG1 enum sections | Preserve ordinary noncanonical acceptance and existing contextual/ownership rules. Do not invent orphan-Module requirements or over-broad section bans. |
| Same `(1,0)` bytes under explicitly different schemas; multiple Modules and separately selected providers | No schema inference or borrowed manifest capability. Other supported exact selections retain their own rules; an unsupported selection fails, never retries as CG1 or GeneralPredicateGraphs. |
| Source-free remapping/packing, target-dependent result, unavailable formation, or failed output | Preserve all allowed obligations and existing output contents on failure. Reject surviving excluded graphs rather than flattening 3 to 1, replaying 2 on the host, dropping request envelopes/defaults, or switching schemas. |

Documentation-only drafting establishes neither these regression results
nor full CG1 acceptance. Integration is complete only when final selected
inputs exercise these gates without weakening existing value, target,
Feature or producer-truth obligations.
