# Predicate Feature composition for outputs and anchors

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** The user's provisional-design authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This recommendation changes no normative owner, registry, implementation or
support claim. Provisional implementation direction is not normative approval.

## Decision and scope

**Recommend option (c): retain the bounded `10/1` registration and restrict
anchor predicates to that exact subset; keep general predicates gated.**
`10/1=PublishedPredicates` has no unconditional Feature prerequisites and
covers only the six phase-3 normal-result selectors defined by
[aggregate native outputs](aggregate-native-outputs.md#exact-shared-predicate-codec).
`8/1=NativeAggregateOutputs` continues to require exactly
`[(1,1),(2,1),(10,1)]`. No new Feature ID or revision is selected.

This document supersedes the
[function-pointer anchor proposal's](standard-function-pointer-anchor-contracts.md)
former `10 -> 11 -> 9` requirement and broad phase-1/2 predicate applicability
for provisional integration. Its applicability/normalization rules now apply
only to admitted selector graphs. It confirms the aggregate proposal's
`10/1` scope and prerequisites, and owns their cross-proposal composition.
That proposal still owns the six-node codec; the anchor proposal still owns
TypeSpec identity and rebasing. Neither owner may independently broaden
`10/1` or restore its removed prerequisite.

This is not a redesign of TypeSpec anchors, output states, source syntax,
ABI, evaluation or runtime behavior. Required contracts outside the subset
must remain retained and unsupported, not be erased to fit it.

## Evidence and preserved owner discrepancy

| Direct source | Boundary |
|---|---|
| [Registry](../specs/representation/trrm-1.registry.json), `metadata.feature_ids` | `9/1`, `10/1`, `11/1`, `12/1` are assigned-registration-pending; current edges are `10/1 -> 11/1 -> 9/1` and `12/1 -> {10/1,11/1}`. The completed `semantic_manifest.features` catalog does not yet register them. |
| [Module manifests §§3–5](../specs/representation/semantic-manifests.md#3-rows-ranges-and-ordinary-encodings), [admission](../specs/representation/requirements-and-admission.md#2-complete-requirement-closure) | Requirements are exact pairs with catalog-derived closure. One Module's effective closure cannot require two revisions of one ID. Physical prerequisite rows may be omitted; identical pairs may repeat ordinarily. |
| [Canonicality](../specs/representation/encoding-canonicality.md#1-ordinary-reading-writing-and-auditing), [format versioning](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts) | Noncanonical does not mean unsupported or malformed. One artifact has one exact schema/catalog; equal `(1,0)` bytes do not select different draft meanings. |
| [Logical predicate records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Row shapes and occurrence-tree rules exist, but general node/operand values, target schemas and per-kind roles/arities remain open. |
| [Feature reader and manifest validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs) | The implementation hard-codes `10 -> 11` and `12 -> {10,11}`. In this snapshot its prerequisite switches do **not** implement the registry's `11 -> 9` edge. That is a catalog/implementation discrepancy, not an alternative normative closure. Recognizing Feature rows establishes no graph execution support. |

Preserve Contract **`0x000B`**, PredicateGraph section **`0x0037`**, the
20-byte attachment and related `0x0038..0x003E` assignments stated by
[ICOW-16/18](../specs/conformance/il-contract-clauses.md#owner-ranges-and-occurrence-keys),
the registry and logical records. The
[clause chapter §2](../specs/representation/contract-clauses.md#2-clause-matrix-and-uniqueness)
still says clause/section/Feature assignment remains open. This normative
owner discrepancy remains unresolved by this documentation change.
Provisionally follow the explicit assignments; do not allocate another
attachment, alias them, or infer general semantics from them. Adding
`phaseTag=3=NormalOutcomeSelector` remains an unapproved amendment, not an
already assigned phase.

## Alternatives

| Option | Consequence and disposition |
|---|---|
| (a) Broaden `10/1` to general published predicates, preserving `[(11,1)]` | Feature 8 would inherit `{9/1,11/1}` and require completion of general graphs and their value dependencies even for a fixed zero/sign test with no literal Blob. Changing Feature 8 to avoid that closure would need a different selector capability. This is a larger contract, not completion of the bounded candidate. Do not select it. |
| (b) Keep `10/1` narrow; add a general-graph capability | A distinct candidate `16/1=GeneralPredicateGraphs` could require `[(10,1),(11,1)]`, retaining `9/1` transitively under the current value catalog. It would compose with Feature 8 without a revision conflict, but still needs complete general node/operand/edge, evaluation and dependency semantics before registration. IDs 14/15 have separate root/pin and copy-origin candidates; using or reallocating them solves no predicate problem. Reallocating narrow selectors to 16 and leaving 10 general would instead change Feature 8's prerequisite without completing those missing semantics. No such ID is allocated here. |
| Revision variant of (b): general `10/2` | Feature 8 still requires exact `10/1`; combining it with `10/2` in one Module conflicts. A larger revision is not a compatibility mapping. Revising Feature 8 too, or adding a distinct general ID, would be necessary; no duplicate-ID, maximum-revision or alias workaround is valid. |
| **(c) Bound anchor applicability to existing candidate selectors** | Delivers one complete predicate subset shared by outputs and anchor identity, changes no Feature 8 requirement, and adds no incomplete general capability. General pre/post predicates remain unavailable. This is the smallest coherent integration and is recommended. |

Unused capacity is not the reason for this choice; a later fully specified
general capability may justify a distinct ID or a deliberate wider revision.
No automatic draft compatibility obligation constrains that later decision.

## Recommended registration and closure

All pairs below use governing edition **1** and exact rule revision **1**.
The edition's empty baseline Feature set is unchanged.

| Exact pair | Immediate prerequisite pairs | Candidate disposition |
|---|---|---|
| `(1,1)` NativeBoundaryContracts | `[]` | Preserve anchor identity gate. |
| `(2,1)` RootlessNativeValues | `[(1,1)]` | Preserve function-pointer value-use gate. |
| `(3,1)` DirectNativeCalls | `[(1,1),(2,1)]` | Preserve direct-call gate. |
| `(4,1)` NativeExportEntries | `[(3,1)]` | Preserve address/indirect-call gate; transitive closure includes 1 and 2. |
| `(5,1)` NativeInterfaceCalls | `[(1,1),(2,1)]` | Preserve interface-call gate. |
| `(8,1)` NativeAggregateOutputs | `[(1,1),(2,1),(10,1)]` | Confirm aggregate-output candidate; closure is `{1/1,2/1,8/1,10/1}`. |
| `(9,1)` NominalUnionMetadata | `[]` | Keep assigned-registration-pending; selectors do not require it. |
| `(10,1)` PublishedPredicates | `[]` | Register only the six phase-3 selectors and their complete admission/binding/rewrite obligations. Remove only the unconditional `10 -> 11` edge. |
| `(11,1)` TypedFrozenValues | `[(9,1)]` | Keep current assigned-registration-pending definition unchanged by this decision. No selector literal/value dependency is fabricated. |
| `(12,1)` ConstantExecution | `[(10,1),(11,1)]` | Keep assigned-registration-pending. Its derived closure is `{9/1,10/1,11/1,12/1}`; narrowing 10 does not complete or authorize general predicate/constant execution. |

Unmentioned registrations and conditional target/runtime/type requirements
remain unchanged. In particular, native unwind and layout-query uses retain
their independent gates; neither becomes an unconditional selector dependency.
The separate [scalar frozen-value proposal](scalar-frozen-value-registration.md)
recommends narrowing `11/1` and removing `11 -> 9`; that value-registration
decision is not adopted here. Its instruction to preserve `10 -> 11` is
superseded **for this predicate composition**. Any later joint adoption must
select one complete catalog, retaining this decision's absence of that edge,
not choose different Feature 11 meanings per Module.

These are logical requirement sets, not mandatory explicit generating lists.
Canonical writers sort explicit pairs by unsigned ID and emit one row per ID;
they need not find a uniquely minimal list. Ordinary reordered lists and
identical duplicate pairs remain equivalent. Requiring both `(10,1)` and
`(10,2)`, whether explicitly or through prerequisites, remains a conflict.
No revision 2 or compatibility mapping is selected.

### Coexistence within one Module

Each cell lists exact `ID/revision` pairs; explicit prerequisite rows are
optional when derived from the selected catalog.

| Retained profile/content | Sufficient explicit list | Effective Feature closure |
|---|---|---|
| Predicate-free anchor metadata | `{1/1}` | `{1/1}` |
| Anchor with admitted selector contracts, metadata only | `{1/1,10/1}` | `{1/1,10/1}` |
| Function-pointer value use with selector contracts | `{2/1,10/1}` | `{1/1,2/1,10/1}` |
| Address formation/indirect use with selector contracts | `{4/1,10/1}` | `{1/1,2/1,3/1,4/1,10/1}` |
| Raw aggregate-output declarations without calls | `{8/1}` | `{1/1,2/1,8/1,10/1}` |
| Direct native aggregate-output call | `{3/1,8/1}` | `{1/1,2/1,3/1,8/1,10/1}` |
| Native-interface aggregate-output call | `{5/1,8/1}` | `{1/1,2/1,5/1,8/1,10/1}` |
| Selector anchors/indirect use plus both output-call forms | `{4/1,5/1,8/1}` | `{1/1,2/1,3/1,4/1,5/1,8/1,10/1}` |
| Selectors plus separately retained frozen values | `{10/1,11/1}` | `{9/1,10/1,11/1}`; value/union registrations remain pending, so this is not yet an admitted complete profile. |

The last row does not make Feature 11 a prerequisite of 10 or enable general
graphs. Every path to 10 resolves to the same `(10,1)` meaning. “Profiles”
here are explanatory content groupings, not wire selectors or alternate
decoders. Packed Modules keep independent manifests; one Module's Feature 10
cannot repair another's missing effective requirement. One artifact cannot
interpret 10 narrowly in one Module and broadly in another.

## Exact predicate admission boundary

Reuse the aggregate proposal's complete selector codec without a second
condition language:

| `nodeKind` | Required ordinary result type and test |
|---|---|
| `1`, `2` | `bool`: true, false respectively |
| `3`, `4` | `int32` or `uint32`: nonzero, zero respectively |
| `5`, `6` | `int32`: nonnegative, negative respectively |

Each graph has one bool-result node, one `operandKind=1=ReadRoot` operand
selecting its own footprint's sole `rootKind=5=OrdinaryResult` row, and no
child edges, captures, literals or selected-call dependencies. It has one
owning MethodDef `000B` attachment with `phaseTag=3`, valid contiguous
phase-local ordinals and the exact existing row extents/reference namespaces.
Canonical selector reuse/order and ordinary noncanonical acceptance remain
owned by the aggregate proposal. Observation at normal Publish is not an
assertion, user-code evaluation or false-postcondition failure.

For TypeSpec `0x0D`, an anchor may carry `000B` **only** for those phase-3
graphs. Its eligible ordinary checked OutputState clauses may refer to them;
identity/rebasing compares the complete admitted contracts under the anchor
proposal, not opaque graph bytes or MethodDef names. Feature 1 covers anchor
identity, Feature 10 the selectors, and Features 2/4 their existing uses.
Feature 8 is not required merely for a checked-output anchor. Combining
Features 8 and 10 does not admit raw native-output formals on arbitrary
function-pointer types or enable the aggregate proposal's reservation
protocol on a raw callable-value target.

Phase `1=Precondition` and `2=NormalPostcondition` retain their existing
assigned meanings but are **not admitted by this bounded registration**,
even if their bytes contain one of the six node kinds. General node/operand
kinds, arities, selected operation/call identities, typed literal references,
capture/edge evaluation and complete read/effect/dependency semantics remain
gated. Neither Feature 1, all of `{9/1,10/1,11/1}`, nor a known row codec
completes them. Preserve required broad contracts and decline affected use
before binding/code generation/execution; never downgrade to Effects-only,
drop pre/post clauses, or reinterpret phase 2 as phase 3. No new general
predicate Feature or revision is registered by this recommendation.

## Proposed manifest and cross-feature cases

These are exact candidate requirements, **not executed implementation
results**. All fixtures select the combined provisional schema/catalog,
edition 1, valid framing/types/binding and required target support unless
the case says otherwise. “Accept” concerns the specified composition, not
proof of producer truth or completion of unrelated carriers.

| Case | Explicit pairs and retained content | Required outcome |
|---|---|---|
| PFC-01 Positive | `{1/1}`; predicate-free anchor | No Feature 10/11/9 requirement follows merely from TypeSpec `0x0D`. |
| PFC-02 Positive | `{1/1,10/1}`; eligible checked-output anchor with exact bool true/false phase-3 selectors | Admit predicate composition with no Feature 8/11/9. Retain all independent checked-output prerequisites. |
| PFC-03 Derived prerequisites | `{3/1,8/1}`; direct aggregate-output call | Derive `{1/1,2/1,3/1,8/1,10/1}`. Accept absence of explicit 10/1; do not derive 11/1 or 9/1. |
| PFC-04 Cross-feature | `{4/1,5/1,8/1}`; selector anchors, indirect use, direct/interface output calls | Derive exactly `{1/1,2/1,3/1,4/1,5/1,8/1,10/1}` once per pair. Shared 10 needs no second revision or physical row. |
| PFC-05 Ordinary/canonical | Ordered sequence `[10/1,8/1,10/1]`, or sorted `[8/1,10/1]`; valid outputs | Same closure as `{8/1}`. Ordinary reader accepts both; canonical writer/audit requires sorted, nonrepeated IDs, but does not require omission of explicit derived 10. |
| PFC-06 Missing effective requirement | `{1/1}` with a retained phase-3 anchor graph; `{3/1,10/1}` with retained raw aggregate-output declarations | Decline for missing effective 10/1 or 8/1 respectively; recognizing rows cannot supply it. |
| PFC-07 Revision conflict | `{8/1,10/2}`, or `{10/1,10/2}` | Reject conflicting exact revisions (and unregistered 10/2); no maximum, roll-forward, alias or profile branch. |
| PFC-08 General assertion | `{1/1,10/1}`; anchor phase-1/2 graph using even a single selector-shaped node | Decline unsupported predicate applicability. Assigned phase values are not malformed merely for being known, but their required semantics are not supplied by this registration. |
| PFC-09 Broader graphs | `{9/1,10/1,11/1}`; literal/call/capture/composition graph | Incomplete registrations and general graph semantics still prevent admission. Adding 11 or 9 cannot widen 10. |
| PFC-10 Selector violation | `{8/1}`; phase-3 graph with an edge/capture/extra root, wrong operand namespace, output read, or signed test on `uint32` | Reject the invalid bounded selector; no general-node interpretation or provider-body inference repairs it. |
| PFC-11 Independent value closure | `{11/1}` alone, then `{10/1,11/1}` with a retained selector | Derive `{9/1,11/1}`, then `{9/1,10/1,11/1}`. Neither profile is currently complete; 11 does not imply 10 and 10 does not imply 11. |
| PFC-12 Constant execution | `{12/1}` | Derive `{9/1,10/1,11/1,12/1}` but decline pending required registration/semantics. A six-selector registration is not general constant/predicate execution. |
| PFC-13 Per-Module ownership | Packed A has `{1/1,10/1}` and selector anchors; B has `{3/1,8/1}` and output calls | Preserve both closures independently. Removing A's 10/1 fails A even though B still derives it; no artifact-wide union repairs A. |
| PFC-14 Support boundary | Valid selector manifest, but consumer knows only old broad 10/1 semantics, a greater revision, or the row shapes | No support claim without exact selected schema/semantics. Do not infer a compatibility mapping or execute to discover contracts. |
| PFC-15 Draft conversion | Old `10 -> 11 -> 9` artifact is relabeled or packed unchanged into the bounded candidate | Not a conversion. Select source/destination schemas, preserve decoded contracts and rebuild requirements/references; unsupported general predicates cannot be dropped. |
| PFC-16 Identity/framing boundary | Zero Feature/revision, unknown high-bit Feature identity, conflicting derived revision, or out-of-range Module Feature range | Reject invalid/unsupported requirements before affected use. IDs/revisions are full `u32` identities, not masked 24-bit table references; range fields retain their separate row rules. |

## Minimal integration handoff and remaining gates

1. Select the combined draft explicitly. In a later implementation/adoption
   change, align both registry Feature catalogs, manifest/logical-record
   projections and the manifest validator with the table above: add 8/1,
   complete bounded 10/1 and remove `10 -> 11`. Reconcile the observed missing
   implementation `11 -> 9` edge with the **selected** value catalog; do not
   silently adopt the scalar-value alternative or treat the omission as policy.
2. Consume the aggregate proposal's selector codec and this exact gate in
   producers, structural/support checks and retained requirement inference;
   apply the anchor proposal's identity/rebasing only to admitted inputs.
   Preserve those facts through binding, rewriting and lowering. A row
   reader, private codec or emitted image is not evidence of those semantics.
3. Exercise the manifest cases above with the referenced proposals' selector,
   ownership/reference-remapping and before-use cases. Keep ordinary duplicate
   acceptance separate from canonical writer/audit checks. Preserve each
   Module's requirements, ranges and resolved context when packing.

Normative reconciliation of the `000B/0037` owner discrepancy and approval
remain outstanding. General predicate registration, node/operand semantics
and any future Feature/revision are deliberately outside this bounded
decision; Feature 11/9 completion remains independent. These are explicit
gates, not policy choices left to an implementer of the recommended subset.
No code, tests, normative text or implementation inventory is changed here.
