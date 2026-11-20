# Standard TRRM C1 class-bearing contextual TypeDef binding views

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow, pending later review.** Recommend extending the
existing candidate `0067 TypeDefBindingEquality` semantics to class-bearing
declaration graphs through three distinct exact successors. This selects
neither normative adoption nor compiler conformance or qualification.
Implementation is in progress: class/struct equality carriers, provider
binding, class/struct reference composition, and source-class publication of
distinct class/inline-struct views are implemented with focused regressions.
The source publisher preserves view-owned fields/layouts/TypeSpecs and
bodyless MethodDef Imports, signatures, contracts, defaults, dependencies,
and CM1/CM1AR1 interface/associated-relation constraints. Validation remains
held; multi-owner source-free remapping, broader closure/acceptance coverage,
and final qualification are outstanding.

## Finding, owners and scope

**Contextual cloning and preservation of relied-on identity are already
required. Their class-bearing publication trigger, endpoint domain and
source-free carrier applicability are the missing decision.** Do not merge
expected contracts, select one original using Module for all occurrences, or
silently extend the classless proposal's explicitly excluded domain.

The [specification index](../specs/README.md) reaches these accepted owners:

| Owner | Preserved obligation |
|---|---|
| [Complete types](../specs/language/types-and-normalization.md#complete-type-terms), [names and binding](../specs/language/names-and-binding.md) | A proxy or alias is not another nominal declaration. Complete representations and qualifications remain distinct; equal names or storage do not establish identity or access. |
| [Binding/replacement §§1–2, 4–5](../specs/representation/binding-and-replacement.md) | Resolve exact Module/key identity first; check every original complete expectation in its own context. Clone context-dependent structural rows and preserve equality, selected owners and stronger relied-on facts across transformations and replacement. |
| [Metadata ownership and references](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [Module linking](../specs/modules.md) | TypeDef/FieldDef ownership, structural TypeSpec/MethodSig contexts, provider-local Exports and exact Import selection survive co-location and remapping. Private fields do not become accessible or exported. |
| [Requirements/admission](../specs/representation/requirements-and-admission.md), [Module manifests](../specs/representation/semantic-manifests.md) | Retain each Module's edition, exact Features, target/runtime and complete dependency closure, including non-call dependencies. |
| [Format versioning](../specs/representation/format-versioning.md), [registry](../specs/representation/registry.md) | One artifact selects one exact whole schema. Unpublished drafts need no compatibility readers, but equal `(1,0)` bytes cannot select another interpretation. |
| [Consumer trust](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md) | Required structural/support/binding checks precede affected use. Producer truth is not mandatory Body re-proof; decoded contextual references, not matching octets, drive remapping. |
| [Data layout and ABI](../specs/data-layout-and-abi.md) | Declared layout, complete nominal identity and executable storage/call/runtime support are separate contracts. Metadata equality assigns no class object ABI. |

The unapproved
[classless contextual-view proposal](standard-trrm-c1-contextual-type-binding-views.md)
selects `0067` only for the three non-EN1 Construction-2/CM1/CM1AR1
Copy-evidence successors and its combined classless method/type operation.
It explicitly excludes classes, enums and generics. The
[class declaration carrier](standard-trrm-c1-class-declaration-carrier.md)
already selects separate typed actual/expected class payloads, and
[class publication](standard-trrm-c1-class-source-publication.md) requires
complete class/member closure. Neither selects this new class-bearing
equality-carrier applicability.

This proposal supplements those owners. In the **new successors only**, it
replaces the classless carrier's struct-only endpoint restrictions and
operation-only applicability with the bounded rules below. The original
three literals, operation scope and endpoint restrictions remain unchanged.
It supersedes class publication's no-additional-wire conclusion only where
distinct retained views need the cross-view relation defined here.
It does not replace accepted nominal identity or complete binding rules.

Included:

- Ordinary nongeneric kind-2 class declaration views and their admitted
  nongeneric kind-1 struct closure, including class-bearing inline structs
  and closure-only dependencies. Equality itself is a nominal binding fact,
  not a Copy, rootlessness or Blittable promise.
- Existing explicit type/type-and-field reference operations,
  `CreateStandardReferenceSet` for two or more independently captured type
  references, source-class publication using its existing checked mappings,
  and source-free load/re-emission/packing of this closure.
- Multiple expected TypeDefs in one using Module; several parent Modules
  requiring one leaf; and an explicitly selected lookup view alongside
  retained imported views of that same actual.

No new public selector or overload is proposed. The inherited six-argument
combined method/type operation remains **classless**; this is not permission
to compose class Method roots or multiple callable-root Modules.
Under the new selections, the four-/five-argument type factories admit exact
public provider-local nongeneric kind-1/kind-2 roots in the selected Module;
field selections stay within those roots' checked closure. A class-bearing
reference set may include independently captured struct-only references of
the identical successor, including a fresh leaf lookup selection. Require at
least one retained class declaration in that set, two or more inputs and no
independently selected callable roots. Preserve inherited member closure
without exposing it as a new callable-root selection. This deliberately
extends the existing kind-2-only projection and class/non-class mixing gates
only in the new schemas; it adds no Module-qualified selector to these APIs.
The classless alias-publication
[decision B](standard-trrm-c1-source-reference-alias-publication.md) remains
scoped to that operation. It is not transplanted into other class factories:
their existing key/alias retention promises remain in force.

Excluded are new class Method selection/equality/dispatch semantics, generic
TypeDef views or substitutions, interface/witness equality, enums/EN1,
numeric class-instance layout, PI1/property completion, new cleanup/transfer
codecs, foreign/native class ABI, object allocation/rooting, and live
replacement/unload protocols. Inherited required MethodDefs, constructor
roles, signatures, contracts, parameter surfaces and actual Bodies are
**not excluded from retention**. Type-only means no additional callable
selection, not permission to strip required members. A missing inherited
codec refuses complete publication; it does not create a field-only facade.

## Pre-implementation source boundary

The following source findings establish the gap this proposal addresses; they
are not claims about the current in-progress implementation or executed
outcomes.

| Evidence | Consequence |
|---|---|
| [`StandardClassTypeReferenceWriter:681–726`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs#L681-L726), [`8811–8926`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs#L8811-L8926) | The only caller of `GetC1M1ImportedStructImportPlan` is the fresh source-class/type-context writer's noncontextual struct branch. It rejects lookup-root/Import mixing and admits only one expected TypeDef/provider pair from `ReferenceRoot`'s using snapshot. Other incoming contexts are filtered, not represented as equality. This is not the source-free class re-emitter's Import-remapping helper. |
| [`writer:528–559`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs#L528-L559), [`630–654`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs#L630-L654), [`2852–2895`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs#L2852-L2895) | A different branch already has per-struct contextual emission rows, exact per-view keys and consumer-owned `0067` stars. Its presence does not establish class applicability. |
| [`compiler:1294–1354`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L1294-L1354), [`1570–1593`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L1570-L1593), [`index:528–544`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs#L528-L544) | The three existing TypeBindingViews literals route to their corresponding Copy-evidence decoding schema while retaining the exact string separately. `EmitC1M1Construction2` explicitly refuses contextual views with local/imported classes or source-class mappings. A shared enum value or writer file is not another schema selection. |
| [`type reference:73–80`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs#L73-L80), [`210–217`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs#L210-L217), [`411–421`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs#L411-L421) | Both existing type/type-and-field public factories reject TypeBindingViews before their later class-schema branches; the class projection additionally requires kind-2 roots. The downstream switch entries are not reachable class support through these operations or permission to select a fresh struct lookup root. |
| [`equality codec:47–101`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeDefBindingEqualityTable.cs#L47-L101), [`binding validator:1003–1083`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs#L1003-L1083) | The row codec requires kind 1. Binding additionally requires nongeneric positive-Copy, non-unique, Blittable structs. Kind 2 and class-bearing Auto structs do not acquire this carrier from their existing nominal origins. |
| [`class projection:479–679`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs#L479-L679), [`typed class carrier:64–137`](../../sources/Terrum.CodeAnalysis/Semantics/BoundStandardClassDeclarations.cs#L64-L137) | Class projection already groups checked actual origins while retaining typed expected declarations and separate FieldDef/MethodDef owner-Import edges. This is useful machinery, not serialized cross-view equality. |
| [`composition:337–513`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs#L337-L513), [`691–701`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs#L691-L701), [`2007–2167`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs#L2007-L2167), [`2319–2333`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs#L2319-L2333) | Struct composition retains several expected declarations but chooses one `ReferenceRoot`; class composition merges actual identities and original expectations. Inline-struct class composition is currently gated to EN1 class-instance-layout selections, and the supported class-schema list does not include TypeBindingViews. |
| [`metadata class re-emitter:9378–9430`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs#L9378-L9430), [`9679–9718`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs#L9679-L9718), [`12920–13133`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs#L12920-L13133), [`16713–16791`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs#L16713-L16791) | Source-free re-emission rejects `StandardQuerySourceAssociation`, rebuilds root-owned expected rows/Imports and uses `CreateC1M1ClassImports` to remap existing type/field/method bindings. It emits no `0067` section. Removing the fresh-writer restriction alone cannot complete source-free retention. |
| [`Import reader:251–305`](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableReader.cs#L251-L305), [`field binding:623–674`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs#L623-L674), [`nominal comparison:3484–3504`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs#L3484-L3504) | Repeated expected TypeDef/FieldDef Imports are not admitted by the scoped C2 schemas, and each expected field owner needs one TypeDef Import. Existing signature/field comparisons require the same checked actual nominal origin on their compared edges. Neither is a general relation between distinct independently satisfying views. |

Consequently, the single-root restriction and missing class/context remapping
are **implementation gaps**, but removing them requires the distinct
applicability decision here. Existing comparison edges already enforce
equality where they explicitly relate two occurrences. They do not encode
every common-source-type relationship after separate publication.

For example, two retained views can select different explicit keys that
initially alias one provider-local leaf TypeDef. Their independently retained
parent-class fields select those respective views. After a consumer exposes
one source leaf type, it can rely on that common identity without an
additional parent-to-parent signature comparison. If a later provider set
splits the keys to distinct actual TypeDefs while still satisfying each
complete individual expectation, independent success does not preserve that
consumer's common identity. A name/kind/contract mismatch already rejects
under ordinary binding; equality is additional only where those checks
otherwise succeed. Conversely, arbitrary old Imports that currently coincide
do not automatically promise never to split. Consumers cannot recover an
omitted publication obligation by reconstructing source or re-proving Bodies.

### Active tests and their limits

`Terrum.CodeAnalysis.Tests.csproj` and its imported test props/targets have
no exclusion for the fixtures below. They are NUnit `[Test]` sources, not
excluded prototypes. No run or pass is claimed.

| Active source | What its assertions exercise; what they do not establish |
|---|---|
| [`ComposesClassLayoutReferencesFromDistinctProviders`, lines 2570–2717](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmC1EnumClosureSourceTests.cs#L2570-L2717) | Two distinct class actuals, exact provider snapshots, separate query dependencies and loaded combined metadata. Its schema is EN1 plus class-instance-layout; it does not test two contextual views of one actual or `0067`. |
| [`ComposesClassQueryRootsWithImportedStructFieldProviders`, lines 5299–5514](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmC1EnumClosureSourceTests.cs#L5299-L5514) | Two class roots with Wrapper/Leaf provider Imports, exact field dependencies and loading. The leaves are distinct LeafA/LeafB, each asserting one expectation; its selection is EN1 class-layout plus Copy evidence, not a non-EN1 contextual successor. |
| [`ComposesCrossProviderClassHandleDependencies`, lines 5034–5296](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmC1EnumClosureSourceTests.cs#L5034-L5296) | Parent/Beta/Gamma closure and selected-versus-imported class views, source-free load/re-emission/reload and an incompatible provider. Still EN1 class-layout, with no equality section or equal-contract split case. Its one-reference call at 5163–5165 conflicts with the public composition API's `Count < 2` guard at [50–56](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs#L50-L56); test presence cannot establish successful execution. This proposal preserves the two-or-more API contract. |
| [`BindsImportedClassDeclarationsWithoutExecutableClassLayout` and `ReemitsImportedStructFieldDependencies`, lines 27–139](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardClassTypeProviderBindingTests.cs#L27-L139) | Non-EN1 C1M1 typed expected/actual retention, no executable `Classes`, closure-only lookup refusal and one imported struct dependency. They do not select this carrier or exercise multiple nonmergeable contexts. |
| [`EmitsClassStubsForImportedStructProviderExpectations`, lines 142–258](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardClassTypeProviderBindingTests.cs#L142-L258) | Non-EN1 CM1 fresh source-class emission using one retained root-owned imported struct expectation, exact ModuleRef/TypeSpec links and changed-field rejection. It does not exercise lookup-root/Import mixing or a multi-parent common actual. |
| [`PreservesContextualTypeDefEqualityViewsAcrossBindingAndReemission`, at line 7189](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardClassSourceEmissionTests.cs#L7189) | The existing classless Copy/Sequential struct case has three expected views, an uncalled member, remapping, canonical stars and split-provider rejection. It supplies a regression pattern, not class-bearing coverage. |

The native EN1 query case beginning at
[5518](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmC1EnumClosureSourceTests.cs#L5518-L5526)
also has a Windows/x64 assumption. EN1 numeric lowering, a private managed
class path, or an emission/loading assertion cannot establish this proposal's
schema or runtime support.

## Alternatives and recommendation

| Candidate | Complete policy and tradeoff |
|---|---|
| **Reuse `0067` with explicit exact successors — recommended** | Keep independent complete views and extend the one consumer-owned nominal binding relation to the admitted class-bearing graph. Retain the 12-byte row, canonicalization and replacement rules; no second relation, runtime identity or Import alias codec is needed. |
| Separate required `ClassTypeDefBindingEquality` carrier | A coherent alternative is a separate required 12-byte owner/left/right table with the same class-bearing trigger, endpoint contexts, closure and no-split checks. It needs a new explicitly unapproved allocation and exact schema agreement. Separating tables adds an applicability distinction, not missing class payload or a smaller encoding; it duplicates the relation for shared struct leaves and cross-Module groups. No numeric allocation for this unselected alternative is made here. |
| Reject until contexts are kept separately | Preserve every view, but refuse composition/publication that would expose one source identity without a surviving normal contract-edge witness connecting every retained view. Independent references remain usable separately; no invented nominal clones, stronger merge or source-only equality may cross publication. This is the required interim policy for unrepresented obligations, not a claim that current implementation satisfies it; it deliberately leaves the authorized common-identity operation unavailable. |

Packing a genuinely snapshot-closed provider closure into one actual direct
TypeDef can make equality unavoidable, provided every original expectation
and requirement survives. It is a useful transformation, not a separately
replaceable reference policy. Redirecting an old Import to a selected lookup
key or exporting another Module's expected stub is not that transformation.

## Exact whole-schema candidates

Select exactly these **three new, unapproved whole-schema successors**.
Each parent is the full literal from the classless contextual-view proposal,
itself extending the corresponding non-EN1 Copy-evidence parent. Each new
schema is exactly that parent plus this document's applicability,
class-bearing endpoint, publication and preservation rules.

| Exact parent schema | Exact proposed successor schema |
|---|---|
| `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1-type-binding-views-1` | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1-type-binding-views-1-class-type-binding-views-1` |
| `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1-type-binding-views-1` | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1-type-binding-views-1-class-type-binding-views-1` |
| `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1-type-binding-views-1` | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1-type-binding-views-1-class-type-binding-views-1` |

Require identical full ordinal, case-sensitive selections for the root,
every supplied provider including unused inputs, captured reference inputs,
retained snapshots and output. Independently supplied artifacts can generally
use different supported schemas under the accepted contract; **this bounded
operation selects the stricter same-schema rule**. It proposes no mixed-input
conversion. Unlisted C1/C1M1, standalone C2L1R1, EN1/class-layout, PI1, generic
and private siblings acquire no new pairing.

Keep `(1,0)`, edition 1, the parent's complete catalog, all row/Body forms
and conditional Feature requirements. CM1's `0066` parameter surfaces and
CM1AR1's associated-relation/owner contracts survive unchanged. No new
Feature, Contract, DefId, opcode, Body record, header field, source grammar,
CLI/JSON transport or runtime capability is selected. A suffix parser,
overlay, relabeling, zero-bit inference, parent-reader fallback or plausible
stride is not a decoder. The existing CLI pairings do not automatically
select these successors.

### Reused required section and endpoint extension

`0x0067 = TypeDefBindingEquality` remains an **unapproved candidate
allocation**, not an accepted registry entry. The accepted machine
[section catalog](../specs/representation/trrm-1.registry.json) has no such
entry. CM1's
[sparse-default alternative](standard-trrm-c1-class-parameter-defaults.md#allocation-alternatives-requirements-and-integration)
mentions `0067` only as an unselected, nonreserved alternative; selected CM1
uses `0066`. This proposal shares the classless binding allocation, not
both meanings. Later selecting sparse defaults requires an integrated
allocation decision; draft-number compatibility is not a reason to retain
either number.

| Item | Candidate rule in the new schemas |
|---|---|
| Framing | Directory kind `0x0067`, required policy `0x0000`, fixed stride 12; no offset index or trailer. |
| Entire row | Little-endian `u32 ownerModule, u32 leftTypeDef, u32 rightTypeDef`, at offsets 0/4/8. |
| References | Required artifact-wide Module/TypeDef rows `1..0xFF_FFFF`, high byte zero, within the corresponding tables. Not DefIds, Import tokens, TypeSpecRefs, class-instance indexes or Body-map indexes. |
| Endpoints | Independently admitted nongeneric kind-1 or kind-2 TypeDefs, imported expected stubs or actual direct definitions. Each edge/component has one nominal kind; class/struct mixing is invalid. Kind 3/4, generic definitions and qualified/constructed type terms are not endpoints. |
| Meaning | The owner retains an obligation that endpoints, independently resolved in **each endpoint TypeDef's owning Module context**, select the same actual TypeDef in the unambiguously selected provider snapshot. |

Class endpoints retain exact declaration mutability and known uniqueness,
complete fields/declared Layouts and every inherited required contract.
Struct endpoints in class-bearing closure retain their own complete
representation and ownership facts: do not impose the classless rootless
Blittable/Sequential domain merely to compare metadata identity.
A Copy struct containing a class handle is not thereby rootless or Blittable.
Conversely, an endpoint without positive Copy evidence does not become
copyable by joining a group. Naming a unique class or a non-Copy declaration
in binding metadata adds no transfer, cleanup or executable value support.
Required but unrepresented closure still refuses.

Endpoint Modules may differ from the obligation owner and each other.
Their declarations, exact Imports and complete requirements enter the
owner's retained dependency closure. This does not make an endpoint Module's
Imports valid in another Module's ordinary TypeSpec/Body context.

Equality is symmetric/transitive, never provider selection. Connected
endpoints must resolve to one actual; distinct locally authored TypeDefs
cannot become aliases by a group. Reflexive/reversed/duplicate edges and
redundant cycles are valid noncanonical encodings after all endpoints are
checked. They authorize no structural type or inline-storage cycles.

For each owner separately, canonical writers compute components after
destination row assignment, emit a star from the lowest destination TypeDef
row to the other members, sort by owner/left/right and omit reflexive and
redundant edges. Never merge ownership across Modules. A component of n
distinct views needs n−1 rows; a singleton needs none. Absence means no
additional equality obligation, not inequality. Present counts are positive
and at most `0xFF_FFFF`; checked `12 * count`, section/artifact `u32`
extents and existing table/map capacities still apply. No new depth cap or
claim of a unique canonical whole artifact is made.

## Source association, independent views and publication

Keep separate maps for **actual nominal identity**, **complete contextual
view**, and **consumer-owned equality obligations**. Reuse the existing
nominal/class declaration identity machinery, not another `PrimitiveType`
range, one executable `BoundClass` per clone, or one anonymous expected
declaration per actual.

A complete view includes its original exact schema/artifact snapshot,
TypeDef-owning using Module, expected TypeDef and Import/ModuleRef/key
selection (or direct declaration), its typed declaration/field/layout closure
and the applicable owner context. Actual identity is the checked provider
Module plus provider-local TypeDef in that selected snapshot. Snapshot
identity distinguishes revisions; source names, row numbers, layout and keys
alone cannot replace either identity.

1. Capture each existing operation's inputs and perform its exact text,
   key/kind/access, snapshot and complete-closure checks before returning a
   reference. Existing source anchors remain compiler-owned checked mappings;
   loaded snapshots cannot acquire fresh source-publication authority from
   names or spans.
2. Bind **every view independently**, retaining expected facts separately
   from actual facts. Intern equal checked actual origins as one source
   nominal declaration, but keep all distinct incoming views. Same names
   on different actuals remain distinct and undergo ordinary lookup
   ambiguity rules; closure-only children gain no source lookup name.
   Deduplicate only the same complete view: different owning/using Modules,
   expected declarations or exact Import/key selections remain distinct even
   when their contract bytes and checked actuals coincide.
3. For a class-bearing source-reference association, start with selected
   class/type roots and their complete view-specific declaration closure.
   Follow class/struct fields, owner paths, wrapped nominal occurrences and
   inherited required signature/contract dependencies in their own contexts.
   Preserve all views needed by these occurrences, including a fresh
   explicitly selected lookup view alongside old imported ones.
4. When two or more retained views jointly represent **that one source
   nominal type**, the publishing consumer owns a same-actual group connecting
   them. This applies to closure-only common leaves and mixed
   lookup-root/import views, even if no surviving Body instruction directly
   compares them. Build the association before canonicalization/pruning;
   do not choose a first incoming Module or contract.
5. Preserve groups already encoded in retained input Modules with their
   owners. Do not manufacture groups for arbitrary independent old Imports
   just because their current actuals coincide. A later explicit source
   composition can establish a new consumer association; ordinary
   source-free loading cannot reconstruct an omitted one.
6. Retain each operation's existing selected-key/alias promises. This
   amendment neither adds omitted source-only aliases nor applies B globally.
   If several retained selections need distinct expectations, clone complete
   expected views with one exact Import each; do not admit new repeated
   expected TypeDef/FieldDef Imports or drop a required key to fit the codec.
   Already retained shared-expected-MethodDef obligations remain unchanged.
7. New external uses need an exact authorized selected key or an existing
   independently checked Import in the relevant view. An equality edge
   cannot select an unexported child, redirect a provider-owned Import,
   supply private access or repair an unsatisfied expectation.

For every emitted context, retain the original Module partition or translate
its **entire** meaning into distinct destination-owned expected declarations
and structural rows, preserving exact selected Module/key, requirements,
owner relationships and outgoing edges. Translation is not flattening away
the original obligations. If it cannot represent that meaning in the selected
schema, refuse. Private direct children can remain provider-owned definitions
in a checked packed closure; they need no fabricated key or proxy Export.

### FieldDefs, owners and inherited members

Clone each expected TypeDef's complete owned FieldDef and Layout/LayoutField
ranges. Fields cannot be shared across distinct owners. A field's complete
type chooses that occurrence's contextual view, not whichever canonical
source type was projected first. Compare the entire declaration and retained
Layout variant set, including default/exact-profile selection, alignment,
packing and ordered membership; an equal prefix or offset is insufficient.

Each FieldDef Import keeps its exact field key and separately bound owner
TypeDef view/owner Import. The selected actual field must belong to that
exact actual owner and correspond at the retained declaration/layout
position. Owner equality plus independently checked field correspondence
preserves corresponding field identity; this chooses no general FieldDef
alias table. Wrong-owner or same-shaped different fields still reject.
Cloned field-alias expectations require their own complete owner views,
not duplicate fields inserted into one owner's range.

Keep TypeSpec representations and all inner/outer qualifications intact:
nominal equality does not equate normal and GC handles, optional and present
types, reference cells and pointees, or readonly and writable authority.
FieldPath roots/ordered steps retain immediate containing-owner context
through every transition. Retaining private fields/layout grants no access,
address formation, initialization, marshalling or backing lifetime.

Inherited constructors/member declarations retain their exact owning class
view, signature/parameter/contract context and expected-versus-actual role.
Clone MethodSig/Param/type rows when their interpretation differs, and
preserve required class-self, construction-completion and parameter-default
edges. Actual implementations retain their real Bodies; expectations remain
bodyless. This is preservation of the parent contract, not new callable
composition or equality of independent method implementations.
CM1/CM1AR1 inherited generic-method/associated-relation closure is not erased
by a nongeneric TypeDef endpoint. Where it exceeds the selected operation's
independently supported inherited domain, refuse that use.

## Consumer binding and replacement

Before affected use, safely decode the selected exact schema, framing,
typed references, ownership/ranges, required semantics and retained Module
closure. Resolve each endpoint's original Import via its own ModuleRef/key
to the required provider-local declaration. A direct reference to an expected
stub still honors that stub's Import; a true direct actual selects its owning
definition. Reject missing/ambiguous revisions, proxy re-exports, wrong kinds,
unsupported closure and target/runtime mismatches without another-key search.

Check every original complete expected/provider relation independently.
Keep contextual comparison/worklist keys containing expected and actual
declarations, using/owner context, supported substitution where inherited,
relation and rules. Do not let common actual interning hide an incompatible
second expectation, an uncalled retained member, wrong owner, changed
mutability/uniqueness, lost Copy promise or layout mismatch.
Finite nominal handle recursion can close a finite graph; in-progress pairs
do not establish success or authorize inline/structural cycles.

Only then check that all endpoints in each retained component resolve to
one identical actual `(provider Module, provider-local TypeDef in its
selected snapshot)`. Equality cannot make an earlier contract comparison
pass. Require all equality-only dependencies even after calls, queries or
source lookup roots have disappeared.

On replacement, re-resolve retained keys and all affected obligations against
the new unambiguous snapshots. A common actual may relocate to a different
row; compare the new resolution, not old numeric rows across revisions.
Reject a retained group's split to two distinct individually satisfying
actuals, including equal-layout classes/structs and a changed common leaf
behind unchanged parent bytes. Independent ungrouped views retain ordinary
checks without an invented no-split promise. A formerly omitted alias matters
only if another retained obligation needs it.

Producers establish the truth of common-source associations and
identity-dependent source uses/folds. Consumers check the encoded equality
as binding metadata; they need not semantically re-prove Bodies or recover
source/debug data. Preserve existing API failure categories: malformed
references or incompatible bindings use `InvalidDataException`, unsupported
schema/required forms use `NotSupportedException`, caller configuration uses
its argument exceptions, and scoped resource refusal remains distinct.
Failure is atomic, not partial output, a dropped endpoint or subset retry.

## Source-free transformation, direct endpoints and lowering

Source-free load reconstructs both the independent view graph and its
encoded owner-relative groups from required metadata. An artifact snapshot,
an in-memory actual-type cache, optional SourceMap or old selection list is
not a substitute. Source-free re-emission supports class-bearing contexts
under the exact new selection without asking for fresh source mappings.

Rebuild all affected decoded relationships together:

- Module partitions/cursors, ModuleRefs, manifest ranges and retained provider
  closure; TypeDef/FieldDef/Import/Export rows and complete layout/contract
  ranges; every equality owner and endpoint.
- Context-dependent TypeSpecs, inherited MethodDefs/MethodSigs/Params,
  construction/member/owner/completion references and required parameter
  surfaces, constraints and constant graphs.
- Body Home/Type/Method/Field/Constant maps, FieldPath roots/ordered steps,
  typed records, instruction indexes, offsets and containing extents.
  Optional source locations/step roles are correctly remapped, regenerated
  or dropped; they never carry required equality.

Canonicalize `0067` only after context-aware endpoint remapping. Preserve
every retained owner separately, and do not deduplicate TypeSpecs, fields or
expected declarations by actual origin alone. Distinct expected declarations
and their original key/contract interpretations remain distinct.

Packing permits imported/imported, imported/direct and direct/direct
endpoints across co-located Modules. Import-to-direct Body rewriting does
not discharge an independently replaceable expected stub or its group.
Keep that expectation and endpoint while its Import remains required.
Only a meaning-preserving closed transformation that retains all relied-on
contracts and makes equality unavoidable may remove a redundant relation;
one previously successful snapshot comparison is not that proof.
Two direct endpoints that remain distinct actual definitions cannot be made
equal by relabeling them. Pruning retains equality-only declarations,
requirements and stronger facts needed by surviving meaning.

After these checks, joint lowering may intern one actual nominal/storage
implementation while keeping all incoming view obligations. It must not
allocate one object/type identity per contextual clone, merge expected
contracts or reuse native plans under changed schema/binding/target facts.
The current
[native struct interning path](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.LocalAggregateLayout.cs#L754-L766)
and [class-provider correspondence](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.LocalAggregateLayout.cs#L698-L745)
are related mechanisms, not evidence of this extension's support.
No class size, header, field base, GC/root, hold/drop, allocation, native
parameter/result classification or ABI is selected here.
Unsupported executable class use still refuses; independently selected
native/runtime contracts and code/load lifetimes remain mandatory.

## Implementation prerequisites and acceptance

This is a proposal-local dependency index, not an edit to the maintained
implementation inventory or roadmap:

| Prerequisite | Current gap and dependency |
|---|---|
| Exact selection and section admission | Add explicit new whole-schema selections through producer/index/structural reader/binding/load/re-emission paths. Reuse `0067` framing with schema-aware endpoint validation; do not globally relax the old struct-only reader or strip the exact identity. Required before any new input is admitted. |
| Complete contextual carriers | Reuse existing class actual/expected payloads and struct view machinery, adding owner-relative groups and per-occurrence class/struct/field-owner mappings. The existing [`BoundStandardTypeView`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs#L1242-L1254) is struct-shaped; one `ReferenceRoot` and an expectation list cannot represent the whole association. Required before composition/publication. |
| Reference composition and source publication | Replace the new-schema-only fresh single-root/lookup-mixing restrictions and EN1-only inline-struct composition gate with complete independent views and this trigger. Preserve existing key promises and required members; the [current no-Methods composition guard](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs#L1982-L2004) cannot justify dropping inherited constructor/member closure. Depends on selection and carriers. |
| Complete binding and shared rewriting | Extend equality checking to the new endpoint domain after independent expected/provider and exact field-owner checks. Carry all contexts/groups through [`class traversal/rewriting`](../../sources/Terrum.CodeAnalysis/Semantics/BoundTreeTraversal.cs#L139-L165), immutable reference handoff and verification. Depends on carriers; any unsupported inherited closure remains an explicit refusal. |
| Source-free load/re-emission/packing | Remove only new-schema admission barriers once full contextual Module/type/member/maps and equality remapping exist. The present root-only `CreateC1M1ClassImports` path needs contextual row maps and `0067` ownership preservation, not a byte-copy workaround. Depends on complete selection, binding and rewriting. |
| Lowering and evidence | Preserve canonical actuals plus incoming contracts in supported lowering; refuse missing class/runtime ABI. After the hold, focused public-API tests must cover the final implementation and exact inputs, with native evidence only for independently supported runtime profiles. Depends on the completed metadata path; existing tests supply no qualification. |

Future public compiler/metadata acceptance must include these cases, not just
successful fresh emission:

| Family | Required positive/negative/cross-feature cases |
|---|---|
| Same actual, distinct views | Two class roots in distinct using Modules require one closure-only struct, and separately one class-handle leaf, with distinct complete expected TypeDefs. Retain one source nominal identity and all views/groups through composition, checked source publication and source-free emit/load/re-emit/reload. Add an explicit lookup selection of that leaf without redirecting either original Import. |
| Trigger and isolation | Multiple expectations in one Module, multi-parent chains/diamonds and a mixed direct/imported view produce total groups only when associated as one retained source type. Include an identity-dependent source use/fold where independently admitted, not just duplicate selection. Independent coincident old Imports remain ungrouped; omitted aliases do not gain obligations. Reverse input/selection order and include unrelated equal-named/equal-layout actuals. |
| Exact binding/replacement | Accept common actual row relocation and valid consumer/provider row remaps. Reject two individually satisfying actuals splitting a group, an equality-only/uncalled retained endpoint, incompatible second expectation after first success, changed leaf under unchanged parent bytes, wrong field key/owner and missing/conflicting provider revisions. Equality never supplies a key or bypasses comparison. |
| Complete class/struct closure | Preserve genuinely empty metadata declarations, class mutability and both known uniqueness values, positive versus unavailable struct Copy evidence, handle-bearing Auto structs and complete declared Layout variants without inventing object geometry. Preserve inherited constructor/member/signature/completion/default facts where retained. Refuse missing required member/cleanup/PI1/generic/witness codecs rather than emit a partial facade. |
| Structural/canonical | Reject zero/high-byte/out-of-range/wrong-namespace references, wrong kinds, mixed-kind components, bad count/framing/policy and unsupported required schemas. Accept checked reversed/reflexive/duplicate/cyclic noncanonical edges; emit per-owner canonical stars after row remapping. Test one view, n views, distinct obligation owners and checked table/map capacities; no unvisited obligation counts as success. |
| Packing and contexts | Permute Module, TypeDef, FieldDef, Import/Export, TypeSpec/signature and Body-map rows while preserving partitions/ranges. Exercise non-first/co-located Modules, equality-only provider closure and imported/direct endpoints. Preserve FieldPath owners and selected keys through source-free packing and Import-to-direct rewriting; retaining independent Imports forbids dropping their groups. |
| Inherited cross-features | Exercise all three exact successors, CM1 parameter surfaces/defaults and retained CM1AR1 owner/associated-relation contexts without inventing generic TypeDef equality. Preserve pointer/reference/optional qualifications and supported function-pointer contract anchors when already admitted; equal descriptor width cannot erase pointee/owner/code context. |
| Exclusions and lowering | Parent/unlisted/mixed/EN1/private schemas refuse the new route, even with unused supplied inputs or equal version bytes. Old classless scopes stay classless. Metadata class identity alone cannot authorize numeric class layout, construction/dispatch, GC/root/marshalling or native ABI. For independently supported lowering, verify one actual implementation with all obligations; later native/JIT/AOT evidence must be separately scoped and supervised. |

On formal adoption, integrate only this chosen carrier/applicability delta
into the metadata/registry/reference-namespace owners, binding/admission,
canonicality and the affected proposals. The accepted source/runtime/target
contracts are preserved, not reopened. No in-scope choice of first using
context, weaker expected facade, hidden alias policy or inferred decoder is
left to implementation.

The recommendation, `0067` allocation and three successor literals remain
unapproved. Broader callable composition, enums/class-layout pairings,
generic/witness endpoints, CLI transport and runtime ABI remain separate
decisions, not prerequisites to invent here. Implementation and all relevant
qualification are open. This document changes no normative owner, registry,
support inventory, roadmap, compiler or test.
