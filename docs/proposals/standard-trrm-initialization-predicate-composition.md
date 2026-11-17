# Standard TRRM initialization-reference predicate composition

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** This selects a candidate composition, not
normative adoption, implementation support or qualification evidence.

## Decision and missing contract

Recommend the distinct exact out-of-band identity
**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-1`**.
It selects the assigned Standard interchange baseline plus the
initialization-reference register-Body contract and `GeneralPredicateGraphsDraft`
metadata/catalog, including its provisional checked-output phase-3 extension.
It does **not** compose the full provisional LayoutQueries source/provider
profile or claim its query-Body support. Keep
`trrm/1-draft-init-reference-bodies-1` unchanged.

The [Body-profile owner](standard-trrm-initialization-destination-callsite.md)
explicitly gates general graphs on a composition decision. The
[checked-output selector owner](checked-output-outcome-selectors.md) defines
their applicability and source projection, but does not select that Body
composition. Understanding `17/1` graphs is not permission to emit or load
them as the existing Standard initialization Body profile. This is the missing
selection contract; source syntax, graph semantics and missing implementations
are separate matters.

| Alternative | Assessment |
|---|---|
| Minimal Body-profile amendment | An explicitly named Body-profile successor could replace bounded predicate admission with G and retain source ensures. It still needs G's whole schema/catalog: `Standard` decoding plus Feature 17 or reuse of the existing identity is not sufficient. With exact G selection it has the same wire composition, not a smaller encoding. An explicit retirement could remove the old draft without compatibility readers, but is not selected here. |
| **Distinct exact GeneralPredicateGraphsDraft + initialization-Body composition** | Recommended. Reuses both owners without redefining their semantics, preserves the existing bounded selection, and makes producer, loader and rewriter opt-in unambiguous. Adds one selection, no wire allocation or compatibility path. |

## Exact selected contents and ownership

The candidate selects **B + I + G**, with the named companion amendments
below, as one fixed whole-artifact schema/catalog, not flags or a powerset
construction. **B** means the assigned Standard interchange schema, catalog
and governing chapters, not a source-producer profile. **LQ** means the
additional provisional/unapproved source/provider/query-Body selection
`trrm/1-draft-layout-queries-1`, owned by the
[LQ source/provider boundary](standard-trrm-layout-queries-source-provider-boundary.md)
and [reference composition](standard-trrm-layout-queries-reference-composition.md).
That exact source-profile identity is not selected as a component here.

| Scope alternative | Assessment |
|---|---|
| Full LQ + I + G | Would also select explicit TypeDef/FieldDef source keys, query-only source/reference projection, reference-set composition, LQ provider closure and query-Body emission/loading. Their joint admission and retention with I/G would need a separately defined exact composition. This is broader than the initialization/selector gap and is not selected. |
| **Standard baseline B + I + G — recommended** | Select the assigned interchange baseline and only the initialization/graph amendments below. Keep assigned LayoutQueries metadata and conditional Feature 13 obligations without inheriting LQ producer/provider policy or query-Body support. This matches the title and the new exact identity. |

| Owner | Selected contents; responsibility retained there |
|---|---|
| B: [Assigned Standard baseline](../specs/representation/registry.md#1-authority-and-schema-identity) | Existing framing, row/reference namespaces, Module manifests and register-Body codec/semantics before the amendments below, including assigned Layout/FieldPath/query forms and Feature 13. Assignment is not a source-profile selection or implementation-support claim. |
| I: [Initialization-destination Body profile](standard-trrm-initialization-destination-callsite.md) | Source-use-5 descriptor inputs, DD formation, complete `ref<init T>`/whole-range `Span<init T>` types, formal-bound hidden preparation, destination/result lineage and normal publication. No new range producer or physical ABI. |
| [Predicate-graph attachments](standard-trrm-predicate-graph-attachments.md) and [bounded Feature composition](predicate-feature-composition.md) | MethodDef-owned `000B`, **12-byte payload / 20-byte total row**, typed `0037` reference, phase-local keys and bounded `10/1` with no prerequisites. The attachment proposal's Boolean-default-only admission is replaced **only in this new composition** by the following two owners. |
| G: [General graph schema](general-predicate-graph-schema.md) | Complete occurrence-tree, operations, calls, types, captures, read bounds, phase-1/2 assertions, candidate `004D..0052`, scoped TypeSpec `10`, read-root kind `8`, canonicality and its single combined catalog. Select its fixed-scalar `11/1` and pending `12/1` prerequisite amendment; independent missing codecs remain gates. |
| [Checked-output normal-outcome selectors](checked-output-outcome-selectors.md) | General phase-3 applicability, checked destination association, surviving output-free observations, relation/coverage rules, declaration-local projection from checked ensures, mixed-selector order, comparison and invalidation. Its derivation algorithm remains unapproved provisional direction, not a newly accepted source rule. |
| [Initialization-state runtime publication](initialization-state-runtime-publication.md) | Caller-local availability witness, actual destination state, saved-result correlation and bounded one-address lowering. No graph-selected witness field or additional ABI word. |

**This proposal owns only the closed joint selection and its cross-layer
retention/admission boundary.** It satisfies the Body owner's composition
prerequisite through the new identity; it does not supersede either existing
identity, the projection algorithm, selector language or Body carrier.
References to other proposals do not compose their LQ source/provider/query-Body,
private, foreign-invocation, CopyOrigin, root/pin, generic-call, aggregate or
exception-region profiles.

B's assigned LayoutQueries records and query shapes are not removed or
reinterpreted. G may retain layout/field observations and their complete
Feature 13, type, field and provider obligations. That metadata use does not
select LQ source publication, reference projection/composition or query-Body
producer/consumer routes. This proposal's source and executable retention
obligations apply to admitted I/G routes; unsupported LQ-required operations
decline rather than dispatching to LQ because the wire baseline is shared.

There is no new clause, section, Feature, node, operand, opcode, prefix,
escape or width assignment. `000B`/`0037..003E`, G's helper sections/type/root
values and I's DD/records retain their owners' candidate meanings in their
distinct namespaces. General selectors still do not become TypeSpec `0x0D`
anchor or NativeOutput contracts. No normative registry assignment is edited.

Intended integration adds the explicit composition to the source-selection
owner and cross-links combined predicate/Body applicability in
[Contract clauses](../specs/representation/contract-clauses.md#5-outputstate),
[logical predicates](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms),
[metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [IL calls](../specs/il-instruction-set.md#general-call-site-record).
The underlying record/catalog amendments remain owned by I, G and their
companions; accepted source grammar and existing exact selections are unchanged.

### Selection rule

Resolve the exact, case-sensitive identity ordinally, without normalization
or component parsing, **before schema-dependent indexing, emission or
loading**. It selects a distinct closed composition whose metadata decoder
and catalog use `GeneralPredicateGraphsDraft` and whose Body admission uses
the initialization-reference contract. Codec reuse must retain this complete
identity; a bare `GeneralPredicateGraphsDraft` or `Standard` enum value cannot
select the composition or stand in for its identity.

The existing bounded Body profile remains Standard plus its exact identity;
the standalone `trrm/1-draft-general-predicate-graphs-1` selection retains
its provisional graph/checked-selector direction without implicitly acquiring
the Body composition. Development and private module formats remain distinct.
Add the token as an explicit producer entry under the
[source-selection owner](standard-trrm-source-schema-selection.md); index,
load and writer routes independently require this same complete selection.
Change no API/CLI default. Unsupported selections or retained forms decline
the affected operation, never fall back to a common subset.

Retain format `(1,0)` and add no serialized discriminator. Features, version
bytes, filename, container or plausible operands cannot choose the profile.
Revision 1 denotes this complete agreement, not future moving proposal text.
A different composition requires another explicit selection, without an
obligation to retain converters. This preserves
[format versioning](../specs/representation/format-versioning.md),
[admission](../specs/representation/requirements-and-admission.md) and
[Module manifests](../specs/representation/semantic-manifests.md).

## Source assertions, selector association and Feature closure

Preserve the accepted [ordinary ensure authoring contract](../specs/grammar-spec.md#58-postcondition-syntax-for-conditional-output-state).
Retain each original checked ensure as its own **phase-2 assertion**, in
declaration order, with its operations, captures, cleanup, selected failure
identity and publication observations. An ensure that legally observes an
output remains a phase-2 assertion; it is not itself a phase-3 selector.
Retained phase-1 clauses likewise use G's existing rules and cannot be dropped.

Project effective output relations using only the
[companion derivation](checked-output-outcome-selectors.md#candidate-derivation-from-checked-ensures).
Keep defaults/replacements normalized once; do not infer a relation from
private writes, turn a false Initialized test into vacancy without settlement,
or erase an authored conditional contract because projection is unsupported.
Absent conditional rows keep their existing meaning, not a fallback encoding
for lost guarantees.

Each OutputState `0002` retains its **12-byte payload / 20-byte total row**,
formal ordinal, typed graph reference and `0=vacant`/`1=complete` state.
Its formal ordinal selects the exact checked init destination, Param mode
`00`; its predicate reference selects a phase-3 `000B`-attached graph of that
same MethodDef, not a graph read of the output. Several outputs
may share one graph within that owner; every general phase-3 graph must serve
at least one such row. Unattached graphs and general selectors without a
checked OutputState use reject.

General selectors have G's built-in-bool value root and occurrence-tree
schema, not embedded bounded node kinds `1..6`. Root `4=logical output`,
output observations through aliases/helper bounds and root `8=PhaseCapture`
are excluded from phase 3; they do not become legal by appearing in phase 2.
Same-graph captures keep G's scoped rules. NativeOutput cells and TypeSpec
`0x0D` anchors remain bounded-selector-only, including adaptation/address
formation; this composition does not admit those separate invocation routes.

False selectors activate no row and imply no complementary state. Sparse
relations retain unknown/maybe-live availability and settlement obligations.
Producers establish promised state, survival, required coverage and opposite-
state disjointness under the companion rules. Source-free writers retain
bounded-first unsigned-nodeKind order followed by the emitted general logical
order; contiguous phase-local ordinals are not physical row order or another
runtime schedule.

The **whole composition** uses G's catalog, even in Modules containing only
bounded defaults. Compute least exact closure separately for every using
Module, including bodyless expectations, uncalled declarations and retained
branches. Another Module's Feature list supplies nothing:

| Retained use | Requirement in this candidate |
|---|---|
| Unconditional checked completion / descriptor Body operations | No new Feature merely for initialization; retain independent type/call/target requirements. |
| Entire bounded result-only selector | `10/1`, prerequisites `[]`, unchanged six-template domain. |
| Phase-1/2 assertion, general checked phase-3 selector, or G form/read-bound use | `17/1`, prerequisites `[(10,1)]`. A retained ensure requires 17 even when its projected selector is bounded or no conditional row is needed. |
| Retained fixed-scalar literal | Additionally `11/1`, prerequisites `[]`, including an unexecuted branch or projected constant. |
| Retained G layout/field observation or binding | `13/1` where its existing assigned rules require it; retain complete layout/provider dependencies. This does not select LQ source or query-Body support. |
| Required constant execution | Pending `12/1 -> [(11,1),(17,1)]`; closure does not complete its independent program/value/resource codecs. |

Neither 17 nor a checked output implies NativeOutput Feature 8, foreign
Feature 16, richer immutable data or any missing witness/failure/ABI carrier.
Other catalog registrations and conditional prerequisites remain their owners'
rules, not per-Module decoder switches.

## Producer, consumer and transformation boundary

| Route | Required integration |
|---|---|
| Source/public-bound producer | Check authored source and effective output obligations. Carry original phase-1/2 clauses through `BoundFunction.DeclarationContractClauses` (`phase`, ordinal, `BoundExpression` and failure context), and carry general selectors plus OutputState associations as separate semantic facts through shared traversal, rewriting, substitution and verification. Emit every retained required clause/graph; unsupported projection, node, subject or enforcement representation declines emission rather than stripping clauses or reducing to the default selector enum. |
| Structural consumer | Before affected use, validate exact schema/support and closure, framing/extents, reserved fields, typed namespaces/bounds, MethodDef Contract ranges, phase keys/ordinals, graph/footprint/node/operand/edge/capture ownership and topology, bool roots, operations/call/type scopes, eligible formals and same-owner phase-3 associations. Enforce selector restrictions and codec-defined structural conflicts; unsupported required graphs are not false or empty facts. |
| Source-free load and re-emission | Within admitted I/G routes, reconstruct or retain the complete typed phase-1/2 trees, general phase-3 relations, logical occurrence order and every required binding/observation dependency without source text. Do **not** re-run projection from a reconstructed provider Body, require debug source to recover semantics, or map a general graph into `CheckedOutputSelectors`' closed bounded node kinds. A snapshot-only pass-through does not establish remapping support or LQ query-Body admission. |
| Typed remapping | Rebuild MethodDef/Import/Contract ranges, formal/owner correspondence, every OutputState graph reference, graph/footprint/root/dependency/node/operand/edge/capture reference, G operation/call/type/control/read-bound helpers, TypeSpecRefs/binders, selected declarations/witnesses, values and their independent dependencies. Clone owner-sensitive occurrences; preserve emitted origin order without source re-projection. Separately rebuild Body Type/Home/Method/Field/Constant maps, preparation/formal/result links, Block/record indexes, instruction widths/offsets/extents, variable-table offsets, directory ranges and per-Module closure; update or discard affected optional source/debug data under its own rules. Numeric policies/ordinals are not row references. |
| Provider binding and replacement | Retain every original expectation and actual provider/key, exact schema/catalog/Module/target context, substituted types, phase-2 clauses, phase-3 conditions/states, observation subjects and dependencies. Alpha-rebased normalized graph equality suffices; otherwise establish the supported directed relation or decline. A narrower provider condition cannot discard an expected guarantee; complete is not strengthening of promised vacancy. Resolution/remapping/optimization cannot replace the original expectation with today's provider facts. |

Separate artifacts select and admit their own exact schemas before binding;
packing selects one destination capable of representing all retained
obligations. Same-profile provider loading/re-emission is a required case,
not an implicit cross-profile converter. Replacement invalidates affected
cached checks and observation/code dependencies. These rules preserve
[binding](../specs/representation/binding-and-replacement.md),
[schema-owned references and producer truth](../specs/representation/reader-trust.md)
and [canonicality](../specs/representation/encoding-canonicality.md).

Source/producer verification establishes legal reads, purity, guard safety,
backing, output truth and every-exit settlement. Mandatory consumers perform
safe structural/support/binding checks; they do not re-prove arbitrary
provider body truth or general semantic coverage. Optional truth audits stay
separate. A rewriter remains responsible for facts it changes or emits.

**Lowering preserves two different roles.** Phase-2 assertions execute once
in order after saving the result and completing body cleanup, followed by
phase cleanup before normal Publish. Preserve exact PostconditionException,
predicate/cleanup failure channels and independently admitted enforcement
bindings/runtime policy; graph metadata cannot supply a missing failure
codec or justify omitted/double enforcement. Phase 3 schedules **no provider
predicate pass**. A caller uses its own once-evaluated discriminator or an
equivalent surviving fact, with ordinary correlation invalidation; it does
not replay ensures, getters or helpers. Writes and cleanup establish actual
availability under the runtime-publication owner, not graph evaluation.
JIT/AOT retain destination generation, loans, backing and failure/code
dependencies. Structural graph decoding or emission alone is not execution
support, nor a new Span/reference ABI.

## Required conformance cases and remaining gates

These are future acceptance cases, not results from this documentation pass.

| Family | Required cases |
|---|---|
| Exact selection | Preserve bounded profile/default behavior. Explicitly select the new composite for combined Bodies/graphs; reject unsupported/missing selection without version-, Feature- or enum-based fallback. Standalone G structural support does not enable I Bodies. |
| LQ scope isolation | Retain assigned layout/FieldPath metadata and require 13 for G uses under its existing rules. The new identity must not inherit LQ type/field key publication, reference-set/provider projection or query-Body production/loading from the shared baseline; refuse unsupported requests. Schema-valid metadata does not establish those source/Body routes. |
| Phase separation / projection | Compound input-only guard plus whole-output state ensure retains its phase-2 output observation and distinct phase-3 complete/vacant relations. A one-sided ensure supplies no invented false-arm vacancy. Non-Boolean/unit results, default and authored replacements, several outputs and mixed bounded/general selectors retain their separate meanings and order. |
| Feature closure | Bounded-only use needs 10, not 17; the retained phase-2 assertion needs 17/10 even for a bounded projection. Literals additionally need 11 on unreached branches. Missing per-Module closure, uncalled expectations or incomplete ConstEval support refuse affected use; no Feature 8 follows from checked outputs. |
| Observations and failures | Reject circular output/alias/helper reads, PhaseCapture selectors, moved/changed inputs, expired guards and newly exposed unsafe guard operations. Distinguish false selector, false ensure, predicate/body/phase-cleanup failure and nonreturn. Count ordinary discriminator/helper occurrences and cleanup; no publication replay or exceptional-state promise. |
| State and ownership | Require settlement before state 0; distinguish descriptor state, partial fields, whole T and whole Span. Preserve hidden preparation and forwarded destination/result generations; stale copies and cross-paired CFG merges cannot revive availability. Unsupported Span/ownership routes remain gates, not scalar fallbacks. |
| Source-free transformations | Load bodyful providers and bodyless expectations without source, permute physical rows, remap/clone owners and rebuild mixed phase ordinals, references, Body maps and source offsets; re-emit/re-consume with complete contracts. Do not depend on a byte-identical snapshot or debug text. |
| Binding / replacement | Accept supported normalized equality under formal/capture rebasing and every retained expectation. Reject lost phase-2 clauses, narrower guarantee conditions, vacancy-to-completion substitution, wrong provider identity and dropped observation/dependency context; exercise separately selected unsupported schemas and packing refusal. |
| Structural / capacity | Reject wrong phase/owner/formal/type, duplicate keys, orphan general selectors, shared occurrence rows, malformed trees and general NativeOutput/anchor use. Exercise 24-bit global row bounds, u32 counts/ordinals and u16 Body indexes through 65,535, with nonwrapping extents and no truncation or graph-row/Body-index substitution. |
| Verification / execution | Source producers reject unproved promised states/guard safety; ordinary consumers reject structural/support/binding defects without claiming universal truth audits. Qualify admitted JIT/AOT paths for once-only phase-2 enforcement and actual normal destination publication, with exact final schema/compiler/target/input identity. Missing enforcement/failure representations block execution. |

Current [source emission](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
rejects declaration clauses; its initialization writer/loader retain only
bounded default selector vectors. The separate G consumer's provisional
phase-3 structural admission does not supply source projection/emission,
general graph source-free reconstruction/remapping or executable enforcement.
The new selection is not implemented by this document. These and the existing
Span, hidden-preparation, CFG/loan, provider-linked execution and failure
gates remain in the [conformance index](../implementation/conformance-gaps.md).

No further user decision is required **within this recommended composition**
for provisional continuation. Normative adoption remains unapproved;
independently unselected codecs, ABI and failure-profile compositions are
not decided or bypassed here.
