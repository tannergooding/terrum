# Standalone published-predicate graph closure

**Status: UNAPPROVED; provisionally actionable under the user-authorized
missing-contract workflow, pending review.** All integration choices below
remain unapproved. This document establishes no normative adoption, released
stability, implementation support or qualification result.

## Standalone decision and supersession

Select the existing exact **B+I+G** schema,
`trrm/1-draft-init-reference-bodies-general-predicate-graphs-1`, for complete
standalone MethodRoot graph support. Preserve bounded `10/1` and use general
`17/1` where required; do not broaden Feature 10 or create another graph codec.
The new decision is the standalone selection and origin-preserving
packing/remapping boundary, not shared predicate semantics.

For this full-closure operation, the recommendation supersedes only:

| Standalone B+I owner | Superseded restriction |
|---|---|
| [Finding and smallest decision](standalone-scalar-frontend-method-imports.md#finding-and-smallest-decision), [input agreement](standalone-scalar-frontend-method-imports.md#exact-proposed-input-agreement) | Initial selection of only `trrm/1-draft-init-reference-bodies-1`; select B+I+G instead. |
| [Standard carrier and source-free consumer](standalone-scalar-frontend-method-imports.md#standard-carrier-and-source-free-terrum-consumer) | B+I-only artifact production/consumption; require the same complete B+I+G selection on both routes. |

Preserve the raw-input agreement
`terrum.experimental.standalone-method-imports/1`, owned snapshots, explicit
Module/key roots, using Module, scalar/unit receiver-free nongeneric source
surface, positional inputs, target requirement 1/runtime policy 0, retained
expectations and independent Terrum compiler boundary. Graph-only dependencies
are closure, not additional source lookup roots. No source syntax, runtime API,
CLI transport or adjacent executable profile is selected.

Reusing the existing composition is smaller than amending `10/1` with another
set of graph encodings or replacing Predicate tables with Body/SSA. Either
redesign would change shared consumers without solving an additional
standalone requirement. Neither is selected.

## Shared owners remain authoritative

The [specification index](../specs/README.md) remains the accepted baseline.
This proposal selects the following **unapproved** companions without taking
ownership of their semantics or copying their kind/record tables:

| Owner and exact sections | Responsibility retained there |
|---|---|
| G: [records, references and ownership](general-predicate-graph-schema.md#records-references-and-ownership), [node schema and evaluation](general-predicate-graph-schema.md#node-schema-and-evaluation), [actual observation footprints](general-predicate-graph-schema.md#actual-observation-footprints) | Complete shared codec, typed targets, nodes/operands, operations, captures/type patterns, roots and direct dependencies. |
| G: [binding and normalized comparison](general-predicate-graph-schema.md#binding-and-normalized-comparison), [canonicality and transformations](general-predicate-graph-schema.md#canonicality-and-transformations) | Complete typed comparison/remapping, occurrence identity, canonical writing and ordinary noncanonical acceptance. |
| Checked outputs: [attachment and capability delta](checked-output-outcome-selectors.md#exact-attachment-and-capability-delta), [observation, evaluation and lifetime](checked-output-outcome-selectors.md#observation-evaluation-and-lifetime), [state coverage](checked-output-outcome-selectors.md#state-coverage-and-source-verification) | Phase-3 applicability, same-MethodDef OutputState associations, surviving output-free observations, publication/coverage/invalidation and no predicate replay. |
| Checked outputs: [derivation from checked ensures](checked-output-outcome-selectors.md#candidate-derivation-from-checked-ensures) | Source derivation and logical occurrence order; imported graphs are retained, not rederived from provider Bodies. |
| Initialization composition: [selected contents](standard-trrm-initialization-predicate-composition.md#exact-selected-contents-and-ownership), [Feature closure](standard-trrm-initialization-predicate-composition.md#source-assertions-selector-association-and-feature-closure), [producer/consumer boundary](standard-trrm-initialization-predicate-composition.md#producer-consumer-and-transformation-boundary) | Exact joint B+I+G schema/catalog, initialization Body semantics and cross-layer retention, admission and execution obligations. |

The existing B+I schema itself is unchanged. Physical assignments remain
owned by the accepted registry/metadata; the companions' semantic amendments
and allocations remain unapproved. This document allocates no new numbers,
records or compatibility encodings.

## Exact pairing and Feature closure

Apply the composition's [selection rule](standard-trrm-initialization-predicate-composition.md#selection-rule)
before schema-dependent indexing. **Every supplied artifact**, including
dependency-only and unused inputs, explicitly selects the B+I+G literal;
the written artifact and source-free reader use that same complete selection.
Local-only empty input still selects this fixed operation. The raw-input
agreement is not a schema selector. Equal `(1,0)` bytes, a Feature list or an
internal G enum cannot supply the pairing. Old B+I and standalone G tokens are
not aliases; there is no mixed-input conversion, fallback decoder or downgrade.

Use G's [complete per-Module catalog and detection rules](general-predicate-graph-schema.md#complete-feature-closure-and-per-module-detection):

* Bounded result-only selectors retain **`10/1`, prerequisites `[]`**.
* General phase-3 graphs and retained G form/read-bound uses require
  **`17/1 -> [(10,1)]`**; this does not broaden 10.
* Retained fixed-scalar literals additionally require the selected
  **`11/1`, prerequisites `[]`**, including skipped branches. Pending
  **`12/1 -> [(11,1),(17,1)]`** and conditional layout/type/value requirements
  retain their owning gates; graph support does not complete them.

Requirements follow each original or destination using Module, including
uncalled expectations. Packing cannot replace those lists with an
artifact-wide union or borrow another Module's support. The full selected
catalog applies even to bounded-only content; retain its ordinary/canonical
list rules. No native Feature follows merely from a checked output.

Phase-3 OutputState graphs retain the checked-output owner's applicability:
each reference selects its owner's phase-3 attachment; general graphs serve
checked outputs and cannot classify them by observing those outputs.
Publication relations are not phase-2 assertions or a new runtime evaluation
pass. This scope does not add broader source signatures or enforcement APIs.

## Cross-artifact ownership and packing/remapping boundary

**UNAPPROVED standalone integration obligations:**

1. **Retain original binding contexts.** Discover complete graph dependencies
   from every retained root/attachment, including uncalled roots, across the
   captured artifact set. Resolve each declaration in its original logical
   Module and preserve its original expectation, Imports, selected keys and
   equality obligations. Following a provider does not replace the expected
   contract or rebind that provider's Imports in the source using Module.
2. **Pack unkeyed direct dependencies in their origin Modules.** A graph-only
   provider-local target need not have an independent Export key or be a
   nominated MethodRoot. When no retained expectation represents it, include
   its required semantic closure in its original logical Module and use
   existing direct packed references. Preserve provider-owned expectations/
   Imports there too, together with existing root Import/Export bindings.
   Do not invent keys, expose helpers to source lookup or fabricate local
   source frames. Use existing multi-Module framing, not external row tokens.
   Co-located Modules are not resubmitted as duplicate external providers.
3. **Copy occurrences per retained owner.** Graph-copy identity includes the
   source snapshot, original owner/using context, destination expected owner
   and graph occurrence—not just artifact/graph row or current actual target.
   Clone owner-sensitive graph rows for independent keyed views. Preserve an
   already shared expectation's equality group without introducing new
   equality merely because two views currently resolve to one provider.
4. **Apply the shared complete remapper.** Use G's typed transformation rules
   through all discovered graph/declaration/helper/value dependencies and the
   checked-output owner's association/order rules. Rebuild affected Module
   partitions, owned ranges, Body maps, offsets, source-reference slots and
   manifests together; retain original subjects and binding contexts after
   relocation. Source-free re-emission needs neither source/tokens nor
   provider-body reprojection. The Terrum compiler owns this route, not a C#
   partial-semantic handoff.

Retain Bodies where the selected publication/execution promise requires them;
metadata-only Pure admission does not itself require executing a helper, while
an executable or ConstEval promise cannot be replaced with a stub. Packing
runs no provider code or initialization. Complete structural/support/binding
checks precede publication under the existing
[reader-trust contract](../specs/representation/reader-trust.md).
Malformed input, unsupported required carriers, incompatible binding and
[resource exhaustion](../specs/representation/resource-accounting.md) remain
distinct failures; publish no partial closure or replacement output.

## Compact standalone acceptance matrix

These are **unapproved required gates, not executed results**. Apply the shared
[G cases](general-predicate-graph-schema.md#required-cases) and
[checked-output cases](checked-output-outcome-selectors.md#focused-candidate-conformance)
through the actual standalone writer/source-free consumer; do not duplicate
their codec suite here.

| Area | Positive | Negative | Relocation |
|---|---|---|---|
| Selection/requirements | B+I+G on all inputs/output; bounded and general graphs retain their distinct closures. | Old/mixed tokens, missing per-Module requirements or unsupported carrier cannot select a subset decoder. | Permute Module/Feature rows and retain each Module's closure. |
| Phase-3 associations | General input-only condition and several checked outputs survive write/read. | Wrong owner/phase, circular output observation or lost state guarantee fails under the shared owner rules. | Remap attachment and OutputState graph references together without changing logical order. |
| Graph-only origins | Direct helper without a selected/exported key; provider-owned Import to another supplied Module; uncalled dependency retained. | Invented key/view, wrong using Module, dropped edge or duplicate packed/external Module fails. | Reorder artifacts and origin-Module declarations; retain original bindings and complete target closure. |
| Expected-owner identity | Independent keyed views get independent occurrences; an original shared-expected group stays shared. | Artifact/graph-only cache reuse across views, accidental merge or split equality fails. | Relocate owners and graph rows independently, preserving every view/group. |
| Source-free round trip | Complete graph/helper/capture closure writes and reloads without source/debug. | Root-kind summary, opaque copying, provider-body reprojection or partial-semantic handoff is not acceptance. | Exercise G's full typed remapping and affected Body/source-slot repair; non-reference payloads remain unchanged. |
| Failure/capacity | Complete supported closure succeeds within the declared envelope. | Missing dependency/carrier or exhaustion preserves prior output and publishes no truncated success. | Capacity/width checks cover the combined packed inventory, not separate per-artifact budgets. |

Compare resolved contracts and graph identities, not merely emitted bytes.
Existing [standalone tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/StandaloneMethodImportInputTests.cs)
are the integration test area; shared codec checks and emission alone do not
qualify source-free consumption or native execution.

## Current gaps and remaining gates

The [Terrum reader](../../sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodOutputStates.terrum)
still validates the one-node/result-root subset and declines dependencies/
children. The [writer](../../sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceWriter.terrum)
still records a selector kind under an artifact/graph-row map and regenerates
leaf templates. A source-free metadata stage now selects the input Module and
reconstructs its local/expected MethodDefs and Import groups without
Source/Token buffers; it does not yet decode Body/CallSite records. The stage
and writer regressions remain unverified under the active hold. Complete
standalone G admission, owner-qualified copies, origin-Module packing and
source-free Body/call remapping and re-emission remain required implementation
and acceptance work; shared G codec support does not establish that route.

The [shared independent gates](general-predicate-graph-schema.md#independent-gates-and-implementation-evidence)
remain applicable when retained content requires WitnessProjection, richer
immutable/value transport, generic evidence, ownership/failure or constant
execution carriers. Their unavailable schemas are not guessed or erased.
Broader Body/enforcement/native/CLI work retains its separate owners.

Approval of this integration and its G/checked-output/initialization
prerequisites remains outstanding. No further standalone graph-tag or
export-key policy is needed for the recommendation above. Valid, fully
representable selected graphs must progress beyond the leaf slice rather than
become permanently unsupported because of ordinary graph structure.
