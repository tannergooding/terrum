# Standard TRRM Construction-2 contextual TypeDef binding views

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow, pending later review.** This supplies provisional
direction for continued authorized implementation; it approves no
specification, allocation or API and claims no qualification. This document
was prepared by static inspection only. The active build/test hold remains.

## Finding and scope

**Contextual cloning is already required; persistence of a common identity
across distinct, nonmergeable expectations is the missing decision.**
Keep separate expected declarations and add an explicit, consumer-owned
same-actual-TypeDef obligation when those views jointly represent one
retained source type. Do not turn a contextual clone into another nominal
type or merge contracts to obtain a shared Import expectation.

This supplements the unapproved
[method/type selection](standard-trrm-c1-method-type-dependency-selection.md)
and [alias-publication B](standard-trrm-c1-source-reference-alias-publication.md)
directions. B still selects the first explicit type/field key for new uses
and retains each callable-view key. Omitted source-only aliases acquire no
replacement obligation. This proposal selects the representation decision
that B expressly leaves open for distinct nonmergeable expectations; it
does **not** select B's alternative A or restore blanket alias equality.
It supersedes the parents' no-new-wire conclusions **only for the
cross-view identity obligation defined here**, not their API, key-selection,
original-context or complete-contract rules.

Scope is the existing six-argument combined `CreateStandardReference`
operation: one classless method-root Module, ordinary nongeneric functions
with admitted scalar/unit results and by-value parameters, and nongeneric,
positive-Copy, rootless Blittable Sequential/Transparent Windows x64 structs.
The exact successors below preserve the three non-EN1 Copy-evidence parents.
Multiple expected TypeDefs in the method root and a closure-only child
required by several parent-provider Modules are included. No additional
method-root Module, callable reference-set composition, class/enum/generic
domain, foreign ABI, private format or live replacement protocol is selected.
Preserve [complete source types](../specs/language/types-and-normalization.md),
[lookup](../specs/language/names-and-binding.md) and the Windows x64
[layout](../specs/targets/windows-x64-data-layout.md) and
[call](../specs/targets/windows-x64-call-interface.md) contracts. No source
alias syntax, library API, value ownership or runtime representation changes.

### Why this is not just a cloning bug

The following are inspected contracts and code, not test results:

| Evidence | Consequence |
|---|---|
| [Binding §§1, 4–5](../specs/representation/binding-and-replacement.md) | Proxies are not new nominal types. Each expectation retains its own contract/context; contextual structural cloning and preservation of relied-on equality are already required. Distinct expected DefIds do not automatically assert equality. |
| The parents' [transformation rules](standard-trrm-c1-method-type-dependency-selection.md#binding-substitution-and-transformations) and [alias alternatives](standard-trrm-c1-source-reference-alias-publication.md#alternatives-and-allocation-decision) | Neither permits merging different authored expectations/using contexts. The latter explicitly defers a relation between distinct nonmergeable expectations. |
| [`StandardMethodTypeReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardMethodTypeReference.cs) | Interns checked actual struct origins and projects one source struct per actual. Its closure-only projection chooses one `ReferenceRoot`/expected declaration and throws for multiple required using Modules or expected TypeDefs. Removing those throws does not decide what equality emitted views retain. |
| [`Import reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableReader.cs), [`writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableWriter.cs) | C2 admits repeated expected MethodDefs, not repeated expected TypeDefs/FieldDefs. The writer validates through the exact reader. Row-scoped binding storage is not an equality record. |
| [`Provider validator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs) | `TypeSpecsMatch` already requires identical actual nominal origins for each compared signature/layout edge. Many splits therefore already reject. It does not compare every pair of independent expected TypeDefs just because they once resolved to the same actual. Field-owner lookup currently requires one TypeDef Import per expected owner. |
| [`Class/type writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs) | Fresh struct planning requires one root-owned expected TypeDef context. `CreateC1M1ClassImports` remaps existing distinct expectations; it adds no cross-view relation. |
| [`Native merger`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.LocalAggregateLayout.cs), [`origin carriers`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | Joint lowering interns actual types while retaining provider edges. A canonical bound type and in-memory expectation list cannot, alone, preserve a required relationship in separately published metadata. |

Consider two retained consumer-local stubs `E1` and `E2`, each with its own
Import and complete expectation, both initially resolving to `D:T`.
Expected callable signatures retain `E1` and `E2` respectively. A source
consumer can pass a value from the first role to the second because both
roles denote T. After publication, a replacement could bind the first
method/type view to T and the second to a distinct, individually satisfying U.
Each expected/provider comparison can succeed without preserving that
cross-role fact. The same issue affects a folded identity observation.
Where an existing signature/layout correspondence already relates the
views, its normal comparison is sufficient; that is not a general carrier.
Requiring consumers to recover omitted equality by re-proving Bodies or
reconstructing source would contradict
[consumer trust](../specs/representation/reader-trust.md).

The contract gap is therefore the **publication trigger and carrier for
retained cross-view equality**, not ordinary substitution, graph checking,
field-owner comparison or native type interning.

## Recommended semantics

1. Keep two identities separate:
   * A **view** is the original artifact/schema snapshot, using Module,
     expected TypeDef and Import/ModuleRef selection, with its complete
     declaration, requirements and applicable owner/substitution context.
   * Its **actual** is the checked provider Module and provider-local TypeDef
     in the unambiguously selected snapshot. Snapshots identify revisions,
     not another source nominal constructor.
   Equal names, keys, layout, field positions or context-local `PrimitiveType`
   numbers establish neither identity.
2. Bind and validate every view independently before interning its actual.
   Several valid views of one actual have **one source-visible nominal
   identity**, with unchanged complete-type normalization and original
   declaration names. Explicit type selections remain lookup roots;
   closure-only views gain no lookup name. Distinct actuals remain distinct,
   including equal-named or zero-data structs.
3. Keep every original expectation and its use context. Method signatures,
   nested field types and field-owner paths select their own contextual
   views; common nominal identity does not select a first contract, combine
   contracts into a weaker facade or grant operations from another view.
   Any stronger actual fact used by new source operations must also survive.
   This includes a fresh explicitly selected lookup view alongside retained
   provider-owned views of that actual.
4. When publication retains two or more views **as representatives of that
   one source type**, retain a same-actual obligation connecting those views.
   This includes the required method-root and nested-parent contexts of a
   closure-only type, even without an explicit type key. The consuming
   Module owns the obligation. It is consumer-relative, not a promise that
   all keys in the provider remain aliases.
5. Do not infer such a group for arbitrary independent Imports in an existing
   artifact merely because their current actuals coincide. Preserve groups
   already encoded and create groups for the combined source-type
   association just described. An omitted source-selection alias is not a
   member. A retained member remains obligatory even if no Body calls its
   associated method; pruning may remove only genuinely unretained meaning.
6. Equality never resolves a key, repairs an incompatible expectation or
   redirects a provider-owned Import. Retain its exact selected Module/key.
   A newly needed local dependency still needs the parent's explicit
   selection; an equality edge is not a missing-child-key fallback.

Build source-view associations from selected methods' exposed complete
signature/contract types and selected type roots, recursively traversing
their view-specific field/layout/declaration closure. Include each original
expectation required by those occurrences and any fresh lookup expectation;
deduplicate only the same complete view. Implementation-only or unrelated
old views do not join a group solely from coincident actual resolution.
Their original dependencies and any pre-existing equality still survive.

Method implementations need not be equal: independent callable views may
split to different satisfying methods while their retained type obligations
hold. Existing shared-expected-MethodDef equality remains unchanged.
Field stubs stay distinct, bound separately to their exact owner and field.
Owner equality plus checked ordered field correspondence supplies the
necessary corresponding-field identity; this proposes no general FieldDef
alias group or permission for field access.

## Alternatives

| Alternative | Assessment |
|---|---|
| **Contextual clones plus required equality records — recommended** | Preserves each contract/using context and records only the additional common identity relied on by the consumer. Independent binding and replacement remain explicit. |
| Contextual clones only, with binding-time actual interning | Sufficient for independent old expectations and for cases where existing retained signature/layout edges already enforce every required correspondence. Otherwise coherent only with a weaker publication policy that allows those views to split and forbids unrepresented cross-view uses/folds. That policy does not preserve the combined operation's retained single source identity. Cloning alone is still required but not sufficient for the general case. |
| Contextual clones, one new-use anchor and retained signature/layout witnesses | Credible for a gated case when every original view's equality to the anchor is obligatorily checked by a surviving normal contract edge. Keep every old expectation/key and prove the witness graph remains total through pruning/re-emission; never redirect an old signature or weaken its view merely to share an anchor. Closure-only anchors cannot select a previously unselected key. This can avoid a new record in fully witnessed cases, but requires an explicit anchor/publication policy and refusal of unwitnessed identity-dependent uses. The uniform record is recommended rather than delegating those choices or relying on incidental surrounding graph shape. |
| Admit C2 TypeDef Import aliases sharing one expected DefId | A small amendment for genuinely shared expectations in one using context. It cannot represent distinct original expected DefIds/Modules without the forbidden merge, and would additionally require all-kind alias/field-owner consumption work. It does not solve this bounded problem. |
| Add an equality anchor to Import or TypeDef rows | Can be designed, but changes every row's stride and mixes binding relations with unrelated declarations. An Import anchor also needs a multi-member representation and careful obligation ownership across using Modules. No byte-size advantage is assumed. |
| Pack the entire actual provider closure and use one direct TypeDef | A snapshot-closed product can represent common identity that way if all original contracts survive. It is not a separately replaceable reference solution; a direct reference to another Module's expected proxy or a fabricated proxy Export is not that alternative. |

The recommendation does **not** additionally admit repeated expected
TypeDef/FieldDef Imports. Each cloned expected TypeDef keeps its one exact
Import; each field stub retains the unique separately bound expected owner.
Representative-only new alias selection therefore remains sufficient.

## Exact C2 representation candidate

Add one required semantic section, not a new nominal definition or Body form:

| Item | Candidate |
|---|---|
| Section | **`0x0067 = TypeDefBindingEquality`**, directory policy `0x0000` |
| Fixed stride | **12 bytes**, little-endian |
| Row | `u32 ownerModule, u32 leftTypeDef, u32 rightTypeDef` |
| References | All three are required artifact-wide table rows `1..0xFF_FFFF`, high byte zero. They are Module/TypeDef rows, not DefIds, Import tokens, TypeSpecRefs or Body-map indexes. |
| Meaning | The owning Module retains an obligation that independently resolved left/right declarations select the identical actual TypeDef. Each endpoint resolves in **its own TypeDef's owning Module context**. |

Endpoints may be admitted imported expected stubs or direct provider-local
kind-1 TypeDefs in this artifact. Allowing direct endpoints preserves the
relation under packing; it does not let distinct locally authored TypeDefs
become aliases. Every endpoint must retain the scoped nongeneric,
positive-Copy, non-unique, complete Blittable struct contract.
Endpoint Modules may differ from the obligation owner and each other.
Their complete declaration/binding requirements enter the owner's retained
dependency closure, without making their Import rows usable in the owner's
ordinary TypeSpec/Body context or granting lookup/access.

Equality is symmetric and transitive. Connected endpoints must resolve to
one actual; no endpoint is the provider-selection winner. Reflexive edges,
reversed pairs, duplicates and redundant cycles are valid noncanonical
encodings after their references are checked. They authorize no structural
type/storage cycles. Canonical writers, separately for each obligation owner,
emit a star from the lowest destination TypeDef row to every other member of
each nontrivial connected component, sort by owner/left/right and omit
reflexive/redundant edges. Do not combine ownership across Modules.
Absent section means no additional equality obligations, **not** inequality.
Present counts/extents obey existing checked positive 24-bit record limits,
`12 * count` framing and `u32` artifact lengths; allocate no new depth cap.
For n views one owning Module needs n−1 rows, plus one directory entry
if the section is present. There is no claim of universally smaller output
than a row amendment.

`0067` is unassigned in the accepted
[registry](../specs/representation/registry.md). The
[CM1 alternatives](standard-trrm-c1-class-parameter-defaults.md#allocation-alternatives-requirements-and-integration)
mention it only for an **unselected**, explicitly nonreserved sparse default
table; CM1's selected parameter surface is `0066`. This candidate chooses
`0067` for binding equality, not both meanings. Adopting the sparse default
alternative later would require an integrated allocation decision. No
unpublished-number or byte-compatibility obligation dictates this choice.

Keep Import/Export strides 12/8, TypeDef stride 44, Module stride 48,
DefId kinds `13/14/16`, all TypeSpec/FieldPath/Body shapes and conditional
parent Features. **No new Feature, Contract clause, opcode, Body record,
trust flag or runtime capability is allocated.** This is mandatory binding
metadata of the selected schema, like Import, representing the already
required retained-identity obligation. Unsupported required section meaning
must refuse affected use; a Feature row or optional/debug payload cannot
substitute for it.

Select these **three unapproved whole-schema successors**, respectively the
parents named in [Copy evidence](standard-trrm-c1-initializer-copy-evidence.md#exact-schema-scope-and-reused-declaration-facts)
plus exactly this section and its rules:

| Parent | Exact successor selection literal |
|---|---|
| Non-EN1 Construction-2 + Copy | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1-type-binding-views-1` |
| Non-EN1 CM1 + Copy | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1-type-binding-views-1` |
| Non-EN1 CM1AR1 + Copy | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1-type-binding-views-1` |

Retain `(1,0)` and edition 1. The bounded operation requires the identical
full literal, ordinally and case-sensitively, for the root, every supplied
provider including unused inputs, retained snapshots and output selection.
No suffix parser, overlay, old-schema relabeling, fallback or compatibility
reader is selected. Supporting previous unpublished drafts is not required;
their bytes cannot be decoded as these successors by guessing. Other
operations/schema domains and the separately proposed
[CLI transport](standard-trrm-c1-method-type-source-provider-cli-transport.md)
gain no automatic applicability or new pairing.

## Consumer checking and replacement

Safely decode the exact schema, section framing, all reference namespaces,
ownership, complete requirements and retained closure before affected use.
Resolve each endpoint's original Import through its own ModuleRef/key and
check its full directed expected/provider relation. Resolve direct endpoints
as their actual owned declaration. Validate method and field correspondences
normally; equality cannot substitute for any comparison.

Then require identical actual `(selected provider Module, TypeDef in its
selected snapshot)` throughout each retained equality component. Resolve
first; do not assume the group is true to make an earlier type comparison
pass. Comparison/worklist keys retain expected and actual declarations,
using/owner/substitution contexts and applicable rules, not just the actual.
Check all retained members, including uncalled edges and equality-only
dependencies. An earlier successful member cannot hide a later incompatible
view, wrong owner, missing key, lost Copy fact or layout mismatch.

On replacement, re-resolve all retained keys against the new selected
snapshots and repeat those checks. Accept compatible row relocation,
including a common actual moving to another TypeDef row. Do not compare its
old numeric row across revisions. Reject a retained group's split into
different actuals even if every individual contract still passes and T/U
have equal layout. A removed/split omitted source-only alias remains
irrelevant unless another retained edge needs it; a fresh reference request
still checks every explicit selection. Independent ungrouped views retain
their normal contract and graph obligations, not an invented no-split rule.

Producers establish the truth of source interning and any identity-dependent
Body/fold they publish. Consumers check the declared equality as binding
metadata, not arbitrary Body truth. Malformed or false/incompatible equality
uses `InvalidDataException`; unsupported schema/representation uses
`NotSupportedException`, preserving existing capacity/resource distinctions.
Existing selector argument failures and source diagnostics remain unchanged.
Fail atomically without dropping a view, selecting another key or retrying
another schema.

## Remapping, publication and lowering

Maintain a canonical-actual-to-source-type map **and** a view-to-complete-
expectation map. Preserve view provenance through signatures, variables,
calls, nested field types, field-owner paths, shared traversal/rewriting,
verification and the immutable reference handoff. One anonymous
`ExpectedDeclaration`/`ReferenceRoot` per actual cannot replace that map.

For each published using context, clone the original expected TypeDef,
owned FieldDefs/Layout/LayoutFields and required contract/type closure.
Either preserve its original Module partition or translate the complete
context into distinct destination-owned expectations, retaining every
original selected Module/key, requirement and edge. Clone nested TypeSpecs
and MethodSigs/Params where import resolution differs, preserving their
ordering/owners and acyclic structural references. Method expectations use
their own contextual signature views, not whichever canonical struct was
projected first. New source uses keep B's representative choice.
Emit equality rows owned by the consumer for the corresponding surviving
view endpoints. Neither copied private layout nor equality exposes fields.

Re-emission/packing rebuilds Module partitions/ModuleRefs, expected and actual
definitions, Imports/Exports, field/layout ranges, TypeSpecs/signatures/Params,
applicable parameter surfaces/contracts/constants, equality owner/endpoints,
Body Home/Type/Method/Field/Constant maps, FieldPath roots/steps, instruction
and source locations, indexes and section extents. Remap decoded fields in
their owning contexts, never matching octets or snapshot-local type numbers.
No added row changes a provider's Module ownership or implicitly re-exports it.

Import-to-direct rewriting must retain each original complete expectation
and equality obligation. Keep equality endpoints on expected stubs while
their independently replaceable Imports survive, even if Body uses become
direct. Only a supported transformation that preserves those expectations
and makes the common identity unavoidable may discharge the equality as
redundant; one successful snapshot comparison is not that proof.
Pruning must retain every endpoint/requirement needed by surviving equality
and every stronger relied-on fact.

Source-free reconstruction recovers both the view graph and checked common
nominal identity without source/debug/selection-list data. Joint lowering
may then intern one actual storage type and recompute its target layout/call
plans; it must not emit one nominal implementation per contextual clone or
discard incoming contracts. Code/load lifetimes remain intact. This is
neither an aggregate foreign-call agreement nor permission to patch or reuse
machine code after provider dependencies change.

## Integration and required public acceptance

On adoption, the owning deltas are:

| Owner | Delta |
|---|---|
| [Metadata](../specs/il-metadata.md), [registry](../specs/representation/registry.md), [reference namespaces](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces) | Register the exact required section/stride, typed rows, endpoint ownership and support boundary. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [admission](../specs/representation/requirements-and-admission.md), [canonicality](../specs/representation/encoding-canonicality.md) | Specify this carrier for retained cross-view equality, its dependency closure and canonical writer form; preserve independent complete comparisons and producer/consumer responsibilities. |
| Combined-selection and alias-publication proposals | Replace only the applicable no-new-wire conclusion and deferred cross-view decision; retain B, explicit keys, distinct expectations and the API. |
| Exact C2 readers/writers, source-reference/context carriers, provider validator, source-free re-emitter and native merger linked above | Carry and check view-specific facts and equality end to end. Cloning, remapping and actual interning are implementation, not further selection policy. |

The existing `TrRmStandardClassSourceEmissionTests` includes nested-provider,
representative-alias and remapping regressions; inspecting them establishes
neither coverage nor a pass for this amendment. Required future cases through
public compiler/metadata APIs, without a new friend assembly:

| Family | Required outcome |
|---|---|
| Method-root views | Two or more original expected TypeDefs in one method Module resolve to one actual. Keep distinct expectations/signature mappings, one source nominal identity and consumer-owned equality through emit/load/re-emit/reload. Include an identity-dependent cross-view use, not just duplicate key selection. |
| Nested-provider contexts | Two outer types in different using Modules import one closure-only leaf; combine them with a method-root use of that leaf and omit a fresh leaf-key selection where each required context already has its exact Import. Cover chains/diamonds, co-located/non-first Modules and reversed selection order. Retain every original incoming view, owner and key. |
| Replacement | Accept common actual/consumer/provider row relocation. Reject a retained group split to two individually satisfying equal-layout types, including an uncalled retained member and a changed leaf under unchanged outer bytes. Reject a later incompatible expectation after an earlier view passes. |
| Alias isolation | Omitted nonrepresentative type/field aliases may split/disappear as B allows. Independent callable implementations may split with unchanged nominal types; common-expected-MethodDef groups may not. Do not invent equality for independent old stubs merely from coincident resolution. |
| Structural/negative | Wrong owner/endpoints/kinds, zero/high-byte/out-of-range rows, invalid framing/counts, unsupported equality section/schema, missing dependencies, unsatisfied Copy/layout/field-owner or method contracts refuse before use. Unresolved equality never supplies a key or bypasses context checks. |
| Re-encoding/packing | Permute Module/TypeDef/FieldDef/Import/Export/TypeSpec/signature and Body-map rows, source-free pack/reload, and exercise imported/direct endpoints. Preserve distinct original contracts, source locations and equality ownership; never drop a group while independently replaceable member Imports survive. |
| Boundary/cross-feature | One view needs no equality row; n views form a complete group. Exercise reversed/duplicate/reflexive/cyclic edge encodings and canonical stars, checked row/map capacities, unit/empty/nested structs, all three exact successors, CM1 parameter surfaces and CM1AR1 inherited requirements. Mixed/parent/unlisted schema inputs refuse under the scoped operation. |
| Native evidence | After the hold ends, separately qualify headless joint lowering/JIT and supervised AOT for a cross-view struct call and nested field path. Emission/loading alone is not execution evidence. |

Approval must choose the **consumer-owned common-source-type retention rule,
the `0067` carrier and the three exact successors**, or deliberately choose
the weaker clone-only alternative with its publication restrictions.
No additional in-scope selector, equivalence inference or merge policy is
left for implementation to invent. Broader schema/CLI applicability and
class/enum/generic adoption remain separate decisions. Implementation,
qualification and formal approval are outstanding; this document changes
no normative owner, implementation inventory, plan or work queue.
