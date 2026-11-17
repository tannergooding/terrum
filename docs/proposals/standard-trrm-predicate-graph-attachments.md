# Standard TRRM predicate-graph attachments

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This proposal does not amend normative owners,
allocate numbers, approve its companion proposals or establish compiler
conformance. Provisional implementation direction is not normative approval.

## Decision and exact gap

Recommend completing the existing MethodDef-owned **Contract `0x000B`**
attachment, retaining its **12-byte payload / 20-byte total extent** and
typed reference to **PredicateGraph section `0x0037`**. Add
`phaseTag=3=NormalOutcomeSelector` under the selector-only **Feature
`10/1=PublishedPredicates`** composition. Do not introduce a second
attachment, an OutputState-specific predicate language or another Feature.

The current owners disagree; this is a genuinely missing integrated
admission contract, not simply missing compiler support:

| Evidence | Current boundary |
|---|---|
| [Registry authority](../specs/representation/registry.md#1-authority-and-schema-identity) and [machine registry](../specs/representation/trrm-1.registry.json), `metadata.clauses` / `metadata.sections` | Assign `000B`, its 20-byte shape and MethodDef occurrence key, and sections `0037..003E`. The attachment is shape-assigned, capability-registration-pending; only phase tags 1/2 are listed. |
| [Clause owner §2](../specs/representation/contract-clauses.md#2-clause-matrix-and-uniqueness) | Describes the same candidate payload, but omits PredicateGraph from the assigned matrix and explicitly leaves its clause kind and section/Feature assignment open. |
| [Metadata contracts and example](../specs/il-metadata.md#semantic-contracts-and-stable-binding) | OutputState `0002` is assigned, but its `predicateRef` still depends on the shared codec. The displayed clause table does not admit `000B`; recognizable predicate rows do not complete that codec. |
| [Logical predicate records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Preserve single-attachment graph ownership, phase-local ordinals and occurrence trees, while leaving complete Feature registration and general node semantics gated. |
| Registry Feature catalogs | `metadata.feature_ids` gives pending `10/1 -> 11/1 -> 9/1`; the completed `semantic_manifest.features` catalog does not register 10. The companion bounded `10/1` with no prerequisites is an amendment, not that accepted registration. |

The [source producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs),
in `AddStandardInitializationOutputStateContracts`, already constructs
12-byte `000B` payloads and separate 12-byte OutputState payloads.
The [contract reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs)
consumes 20-byte attachment rows. These sizes agree: both exclude/include
the same eight-byte Contract header, respectively. Neither implementation nor
the registry's structural assignment resolves the admission disagreement.

### Relation to existing proposals

* [Standard initialization-destination CallSites](standard-trrm-initialization-destination-callsite.md)
  remains the owner of source-use 5, descriptor formation/preparation and
  result/destination publication. This proposal supplies only its bounded
  predicate-attachment prerequisite; it changes no Body operand or ABI.
* [Predicate Feature composition](predicate-feature-composition.md) remains
  the owner of the six-selector `10/1` scope, prerequisite removal and
  cross-feature closure. This proposal supersedes its instruction to leave
  the `000B/0037` owner discrepancy unresolved, selecting the existing
  attachment as the candidate integration. Its other decisions are preserved.
* [NativeOutput cells](aggregate-native-outputs.md#exact-shared-predicate-codec)
  remains the owner of the six-node selector codec. Reuse it for checked
  outputs without adopting NativeOutput destinations, Feature 8 or reservation
  records. Supersede its “20-byte payload” wording with **12-byte payload /
  20-byte total row**; this candidate has no alternate 20-byte data payload.
* [General predicate graphs](general-predicate-graph-schema.md) and
  [general checked-output selectors](checked-output-outcome-selectors.md)
  retain their independent `17/1` choices and prerequisites. This resolves
  the common attachment shape/allocation question, not admission of their
  nodes, phases or schema compositions.

## Candidate attachment and ownership

All multibyte fields are little-endian. The offset-indexed Contract row has
exactly these fields; there are no trailing attributes or padding:

| Byte offset | Field | Candidate meaning |
|---:|---|---|
| 0 | `u32 ownerRow` | Required typed MethodDef row |
| 4 | `u16 clauseKind=0x000B` | One attachment kind for all predicate phases |
| 6 | `u16 flags=0` | Contract-header flags |
| 8 | `u8 phaseTag` | `1=Precondition`, `2=NormalPostcondition`, candidate `3=NormalOutcomeSelector` |
| 9 | `u8 flags=0` | Payload flags, distinct from header flags |
| 10 | `u16 reserved=0` | Reserved |
| 12 | `u32 phaseLocalOrdinal` | Zero-based ordinal within this owner and phase |
| 16 | `u32 predicateGraphRef` | Required typed PredicateGraph row in section `0x0037` |

Both row references use the standard one-based `u32` encoding
`1..0x00FF_FFFF`, reserved-zero high byte. Neither is a DefId, a Contract
row, a Body-map token or a phase-local ordinal. OutputState's
`predicateRef` uses this same typed **PredicateGraph** namespace, not the
attachment's Contract-row namespace.

Every attachment belongs to exactly one contiguous MethodDef Contract range
and names that exact MethodDef. Each graph has exactly one attachment;
OutputState may additionally reference it for several formals of that
**same MethodDef**. Imported expected declarations have their own attachments
and graphs, even when several expectations select one provider. TypeDef,
MethodSig, Body, Param and TypeSpec do not own these attachments. An admitted
function-pointer anchor refers to its MethodDef; it does not become another
Contract owner.

The graph retains its assigned eight-byte `rootNodeRef, readFootprintRef`
row. Those fields target PredicateNode `003B` and PredicateReadFootprint
`0038`, respectively. The attachment supplies callable/phase/order identity;
do not duplicate it in the graph. Every graph owns its actual footprint,
nodes and operand occurrences. Footprint root/dependency ranges and node
operand ranges are exclusive; equal occurrences in different graphs require
distinct rows. Only immutable complete type/other admitted identity targets
may be shared. Orphan graphs, shared attachments, cross-owner OutputState
references and shared occurrence/footprint rows are invalid.

### Occurrence and canonicality

The attachment occurrence key is
`(owner, 000B, phaseTag, phaseLocalOrdinal)`. Each owner's ordinals in each
present phase are exactly `0..n-1`, with no gaps or duplicate keys, including
identical duplicates. Physical Contract or graph-row order is not declaration
order. A missing attachment supplies no predicate and cannot erase a
required source default or retained contract.

For phase 3, canonical writers reuse an equal selector template within the
same complete owner context, sort selectors by unsigned nodeKind and assign
contiguous ordinals. Thus Boolean defaults canonically use true at ordinal
0 and false at ordinal 1. Graph-owned rows follow their owner's logical
identity, phase and ordinal, then the codec's occurrence order. Re-sort and
rebuild references after remapping; row numbers are not stable identities.

Ordinary readers accept consistently permuted tables, a different contiguous
phase-3 selector order, and redundant equal selector templates under distinct
valid attachment keys. They do not accept duplicate attachment keys, two
attachments to one graph or duplicate OutputState
`(owner, parameterOrdinal, predicateRef)` keys. Distinct predicate rows
selecting overlapping outcomes for one formal must agree on state.
Canonical graph reuse remaps those references and coalesces resulting equal
OutputState keys; a state conflict rejects rather than choosing a row.
These equivalences do not authorize reordering phase-1/2 assertions.
Canonicality audits remain separate from ordinary structural admission.

## Feature, schema and bounded profile

Select the whole candidate out of band as
**`trrm/1-draft-init-reference-bodies-1`**, jointly with the CallSite
proposal and the bounded Feature-composition decision. Preserve `(1,0)`;
add no serialized selector or header field. A profile string or matching
version bytes without agreement on the entire schema/catalog is insufficient.
Do not infer the schema from the presence of `000B`, Feature 10 or plausible
row extents. Other draft profiles require explicit composition or conversion.

For governing edition 1, the candidate registers exact `10/1` with immediate
prerequisites `[]`, completing only the companion's six phase-3 result tests.
There is no unconditional Feature 11/9, native Feature 1/2/8 or general
Feature 17 requirement merely for these attachments. Preserve all independent
requirements of retained values, calls, types and target/runtime uses.
Actual frozen values cannot be admitted merely by omitting 11; content beyond
this exact profile still needs its own selected complete composition.

Infer requirements in each attachment owner's Module and retain them through
imports, packing and replacement. Explicit prerequisite rows are optional;
catalog-derived closure is mandatory. Ordinary reordered/repeated identical
Feature pairs retain their meaning; canonical lists are unsigned-ID sorted
without repeated IDs. Conflicting revisions of one ID reject. Do not select
different meanings of `10/1` per Module or borrow another Module's requirement.
The composition proposal's Feature 8 closure remains unchanged; that is not
permission to admit NativeOutput cells in this initialization-body profile.

The initialization-body subset admitted by this decision is narrower than
all six selectors:

| Surface | Admitted subset |
|---|---|
| Owner/signature | Existing bounded Standard scalar `ref<init T>` declarations/expectations, ordinary Param mode `00`, with exact built-in Boolean ordinary result for conditional defaults |
| Publication | For every such conditional output, true means complete (`state=1`) and false means vacant (`state=0`); both relations are retained. The captured call result selects the state of that call's exact destination generation. |
| Attachment | Phase 3 only; Boolean selector nodeKinds 1/2. Canonical output uses one shared pair per MethodDef, not a pair per output formal. |
| Graph tables | Nonempty `0037` graph (8 bytes), `0038` footprint (16), `0039` root (8), `003B` node (16), `003C` operand (8) as specified below |
| Other predicates | No nonempty `003A` dependencies, `003D` edges or `003E` captures; no literals, selected calls, extra read roots, phase-1/2 predicates or general phase-3 programs |

Each graph has one bool-result node, one operand and one actual root.
The footprint is `rootCount=1, dependencyFirst=dependencyCount=0`.
The root is `rootKind=5=OrdinaryResult, rootIdentity=0`; flags/reserved
fields are zero. The node has `operandCount=1` and a complete
`TypeSpecRef` resolving exactly to built-in bool, including equivalent
well-known/type-row forms. The operand is `operandKind=1=ReadRoot` and
references this graph's sole root. A true/false node tests the attached
owner's saved Boolean result, not a formal, output storage or integer status.

This is observation at normal Publish, not an asserted postcondition:
false is an ordinary outcome, not a contract failure. No user evaluation,
output read or implicit conversion is performed. Exceptional completion,
failed checks/preparation/postconditions and nonreturn supply no normal
OutputState guarantee. Unconditional normal-complete outputs continue to
need no OutputState row or selector attachment.

Span/range production, hidden-output preparation, explicit alternative output
maps, general CFG/loan support, general predicates, NativeOutput, deferred
completion, ownership/failure cleanup and general physical reference ABI
admission are not supplied by this attachment decision. They retain their
independent contracts and support gates. A known phase 1/2 attachment is
unsupported here, not an unknown numeric tag; zero or unassigned phase tags
are malformed.

## Validation, binding and transformation obligations

**Consumers, before affected use:** select the exact schema and supported
per-Module closure; check directory/table framing, strides and Contract
offsets, exact 20-byte extents, both flags, reserved fields, owner ranges,
typed-reference domains/bounds and phase ordinal uniqueness/contiguity.
Validate single graph attachment and graph-owned row/range closure, the
closed node/operand/root shapes, exact result/formal types, same-owner
OutputState references, state domain `0/1`, duplicate keys and structurally
contradictory/incomplete default maps. Decline other required forms before
binding, reconstruction, code generation or execution; do not drop them to
fit this subset.

**Bounds:** table record counts remain at most `0xFF_FFFF`; required
references reject zero, a nonzero high byte and past-end rows. Zero is valid
for phase/formal ordinals and the OrdinaryResult identity, not for required
references. Nonempty ranges have nonzero typed first rows; empty ranges are
exactly `(0,0)`. Check `first+count-1`, stride products, offset-index size
and section/row ends without wrapping or truncation. Phase ordinals remain
full `u32` numbers, not row references; the finite attachment range bounds
their possible contiguous values. Resource-envelope exhaustion explicitly
declines the operation and is distinct from malformed input.

**Producer truth:** source verification establishes authored output states,
actual writes and normal-return correlation; emission/rewriting publishes
truthful facts. Structural admission does not require the consumer to
re-prove a provider Body, arbitrary predicate coverage or a foreign promise.
False semantic metadata remains a producer defect and can cause undefined
behavior. Optional semantic audits retain their separate scope under
[reader trust](../specs/representation/reader-trust.md).

**Binding/replacement:** resolve the selected provider/key and retain each
original expected signature, output map, attachment and owning Module
context. Compare normalized selector identity by corresponding owner,
complete result type and test, mapping formals by declaration ordinal;
graph rows, phase-3 ordinals and names are not cross-artifact identity.
Apply [directed satisfaction](../specs/representation/binding-and-replacement.md):
true/complete plus false/vacant is not satisfied by true/complete plus
false/complete. Extra initialization cannot replace required vacancy.
Neither stronger provider facts nor resolution to one provider merges
independent expectations or erases their retained conditions/dependencies.

**Re-encoding:** decode typed records and rebuild MethodDef Contract ranges,
attachment owners/ordinals, both attachment and OutputState graph references,
graph node/footprint references, footprint ranges, node operand ranges/root
references and complete TypeSpecRefs in the destination context. Clone
owner-sensitive graphs when contexts diverge; do not share occurrence rows
because their bytes match. Recompute table counts, Contract offset indexes,
section offsets/extents, Module partitions and Feature ranges/closure.
Remap retained Import/Export/Method/Type identities and any affected source
reference slots. If Bodies are rewritten, also rebuild their maps, typed
records, Block/record indexes, instruction offsets/extents and source/debug
positions while preserving saved-result/destination provenance. Re-encoding
is not octet replacement, profile relabeling or serialization of a private
success flag. A destination schema unable to express retained obligations
must refuse the transformation.

## Alternatives and intended integration

| Alternative | Assessment |
|---|---|
| **Complete `000B/0037` and bounded `10/1` — recommended** | One attachment and one shared predicate identity serve OutputState and other independently admitted users. Preserves explicit phase/order and exclusive graph ownership with no extra bytes or decoder. The reason is coherent reuse, not protection of unpublished IDs. |
| Inline a Boolean selector tag in OutputState, or introduce a dedicated output-predicate table | Could eliminate graph rows for this tiny subset, but changes OutputState's assigned payload or adds another predicate identity/remapping language. It duplicates the shared codec and does not resolve declaration graph attachment for other users. A deliberate full redesign could reassign draft IDs without aliases, but is unnecessary for this gap. |
| Move owner/phase/order into PredicateGraph, removing `000B`, or make graphs a new callable program/Body | Makes the graph self-identifying but replaces the eight-byte graph shape, Contract-range ownership and existing occurrence/remapping contracts. Unifying executable predicates with Bodies additionally needs evaluation/effect/failure contracts. No demonstrated benefit here warrants that larger change. |

All owner changes below are **proposed, not performed**:

| Owner | Exact integration |
|---|---|
| [Clause chapter §§1–2 and §5](../specs/representation/contract-clauses.md) | Add MethodDef-only `000B`, 20 bytes, to the matrix with the key/absence/ownership above; replace the open-assignment statement. Add phase 3 with bounded support and fix OutputState's typed graph-reference target. Preserve phases 1/2 without admitting their programs through 10/1. |
| [Machine registry](../specs/representation/trrm-1.registry.json) and [registry prose](../specs/representation/registry.md) | Preserve `metadata.clauses[000B]` fields and `metadata.sections[0037..003E]`; add phase 3 and bounded applicability/reference constraints. Reconcile both Feature catalogs with the composition proposal's exact 10/1 registration and closure, under one explicitly selected integrated draft schema. |
| [Metadata](../specs/il-metadata.md#semantic-contracts-and-stable-binding) and [logical records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Project the attachment and completed bounded gate consistently, including complete TypeSpecRefs, occurrence/canonicality rules and the nonasserting phase. Replace codec-pending language only for this subset; retain general codec/support gates. |
| [Clause conformance](../specs/conformance/il-contract-clauses.md#owner-ranges-and-occurrence-keys) | Align the existing attachment assignment cases with these owners and add the cases below; do not claim general predicate execution. |

## Required cases and remaining gates

These are candidate acceptance cases, not test results:

| Family | Required cases/outcomes |
|---|---|
| Positive/default | One output and several scalar outputs share an owner's Boolean selector pair; separately authored provider and bodyless expectation retain identical normalized relations. Unconditional complete output needs neither graphs nor Feature 10 solely for output state. |
| Ownership/references | Reject TypeDef/MethodSig ownership, orphan/shared graph/footprint/node/operand rows, cross-MethodDef output references and a Contract-row/DefId substituted for a typed graph reference, even if its low bits name an existing row. |
| Ordinary/canonical | Permute all five tables and Contract rows consistently; reverse contiguous phase-3 selector ordinals; include redundant equal templates with distinct owned occurrences. Preserve meaning, then canonicalize/reload. Reject duplicate occurrence/output keys and conflicting overlapping states independently of canonicality. |
| Bounds/malformed | Required row 0, nonzero high byte, last valid versus past-end row; ordinal 0, duplicate/gapped ordinals; short/long Contract extent, nonzero flags/reserved bytes, overlapping ranges and overflowing offset/count arithmetic. Valid capacity exhaustion is reported separately. |
| Feature/schema | Missing effective 10/1 in one owning Module is not repaired by another Module. Derive only the selected catalog's closure; reject conflicting revisions. Decline phase 1/2, general nodes, captures or Feature 17 content under this profile; equal version bytes do not select another decoder. |
| Binding/rewrites | Renumber provider and expected rows independently; preserve each original expectation through replacement and re-emission/reload. Reject changed result/formal types, lost selectors, false/complete replacing false/vacant and conversion to a schema lacking retained contracts. Exercise graph and source-slot remapping, not identical-byte round trips only. |
| Trust/publication | Source/producer checks catch lying output implementations. Structural consumer checks catch malformed maps without claiming Body re-proof. Runtime qualification separately exercises both normal outcomes and exact destination generations; structural decoding or image emission is not that evidence. |

Current source is evidence of a provisional slice only. The
[bounded state reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.InitializationReferenceBodies.cs)
currently requires the normalized true/false ordinal pair, while the
[shared selector validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmPublishedPredicateSelectorValidator.cs)
recognizes the closed six-selector graph shapes.
The active [initialization-reference fixture](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardInitializationReferenceBodyTests.cs)
contains default-state, provider-binding and re-emission regressions; it is
not approval or evidence for every ordinary noncanonical case above.
Implementing this candidate must not turn a current canonical-only support
limit into a general validity restriction.

The remaining in-scope decision is approval of this attachment integration
jointly with the bounded 10/1 composition, versus a deliberate redesign.
No field, namespace, owner or default-state policy remains unspecified within
the recommendation. General predicate/phase registration, broader init-body
forms and independent ABI/runtime qualification remain separate gates, not
implicitly approved choices.
