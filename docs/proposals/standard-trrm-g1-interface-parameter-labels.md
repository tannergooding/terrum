# G1 and C1 source-free method and interface parameter labels

> [!IMPORTANT]
> **Unapproved proposal; provisional implementation direction only under the
> user-authorized missing-contract workflow.** This document changes no
> accepted specification, registry, implementation or support claim.

## Missing contract and recommendation

The missing contract is a **source-free parameter-label carrier**, not
permission to erase imported labels:

- [Calls and inference](../specs/language/calls-and-inference.md#argument-mapping-and-call-forms)
  selects statically visible labels before applicability, maps named arguments
  to declaration positions, and evaluates arguments in source order.
  [Name binding](../specs/language/names-and-binding.md) provides no import
  exemption. Labels cannot distinguish otherwise equal overload signatures.
- [Interface witnesses](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity)
  assigns caller-visible names/defaults to the selected requirement, not its
  implementation; [WIT-20](../specs/conformance/source-interface-witnesses.md)
  explicitly preserves that distinction. The
  [callable source-view rule](../specs/language.md#callable-values-params-and-tuples)
  calls names metadata/tooling information **without** making them ABI names
  or nominal identity. It still requires calls to use their visible labels.
- [Standard Param](../specs/il-metadata.md#section-kinds-and-row-layouts) is
  exactly 16 bytes:
  `u32 ownerMethodSig, u32 ordinal, TypeSpecRef typeSpec, u8 mode,
  u8 flags, u16 reserved=0`. It has no name.
  [G1 generic-method export inputs](standard-trrm-source-generic-method-export-inputs.md)
  already publish explicit public/open generic MethodDefs through version 1,
  with their signatures and source-free binding obligations. The
  [G1 interface export-input proposal](standard-trrm-g1-interface-declaration-exports.md)
  adds version 3 and explicit owner/requirement ExportKeys. Neither supplies
  an ordinary parameter-label carrier; GenericParam's existing names belong
  to type/constant formals, not these Param positions.
  The separate [private interface proposal](source-interface-witness-private-persistence.md)
  has a private ParameterSurface; that is not selected Standard G1 metadata.

This is already a generic-method problem, not only a prerequisite for the
unfinished interface reader.
[`CreateGenericMethodImportCore`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceReader.cs)
reconstructs ordinary parameters from nameless Params and invents `argN`
variable names.
[`AddReferenceGenericMethodImports`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.StaticProtocols.cs)
copies those names into generic-template parameters; the selected signature's
names reach
[`Binder.BindArguments`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs).
That code maps named labels against `signature.Parameters[index].Name`.
An ordinally correct signature can therefore lose authored labels and expose
invented alternatives. Positional-call success does not establish preservation.

The plan's C1 provider route is a second required integration, not a transitive
G1L1 use. [C1](standard-trrm-exception-class-witness-binding.md#exact-schema-and-catalog)
includes the original G1 agreement; C1M1 and Construction-1/2 inherit that
composition. The [declaration-failure profile](standard-trrm-declaration-predicate-failure-profile.md#decision)
selects Construction-2 for complete phase-1/2 class/witness/failure closure.
None currently includes this label delta. Moreover, C1 retains common
formatter requirements with method formals, including
`IFormattable.TryFormat<TWriter>` and `ITextWriter.TryWriteFormatted<TValue>`;
G1's bounded interface publication excludes member generics. A G1-only label
agreement cannot carry that complete C1 source view or its construction graph.

**Recommend one required, MethodDef-owned `MethodParameterLabel` table and
one Feature, with explicit G1L1 and C1-family label-bearing agreements:**

- Explicitly exported public/open generic MethodDefs admitted by G1, and
  their retained consumer-local expected declarations, under G1L1.
- Direct InterfaceMethod requirement MethodDefs in the companion's bounded
  public interface/bodyless instance `Callable` subset, including their
  retained expected copies, under G1L1.
- Those generic publication views and the complete retained instance
  InterfaceMethod requirement views admitted by C1, including method-generic
  common members and expected copies, under the C1 label-bearing successors
  selected below. Inherited requirements keep their actual declaring owners;
  the G1-only member restrictions are not a C1 decoder or admission policy.

It preserves each selected declaration's source view without making labels
structural MethodSig/Param identity. The generic-method and interface paths
retain their own admission gates. **G1L1** keeps the companion's exclusions,
including ordinary defaults, bases, associated members, member generics and
unsupported contracts/modes. The **C1 family** instead keeps C1's complete
member/witness/contract obligations and refusal gates; labels supply none of
their missing codecs. Generic formal defaults already selected by G1 are
unchanged; L1 itself supplies no ordinary-parameter default carrier. The
separate [interface-default proposal](standard-trrm-interface-parameter-defaults.md)
proposes a closed-SF1-only field in exact R1D1 successors; it does not change
G1L1 or the original C1-family identities.

This changes no source grammar and does not approve broader interface members,
closed-witness persistence beyond C1's separately required closure, dynamic
dispatch, interface-value representation or runtime policy. Unexported private
generic implementations/helpers, ordinary nongeneric class methods and
constructors, and callable values acquire no new source-view publication
promise. An already ordinal-bound Body call or P→I/`0B` construction plan does
not itself require another label owner. Broader gaps remain explicit below.

### Scope alternatives

| Scope | Assessment |
|---|---|
| Interface requirements only; version 3/G1L1 | Coherent as an explicitly partial interface milestone, with less immediate generic-reader work. It leaves the same loss in G1's already selected version-1 public generic exports and version-3 genericMethodExports. It cannot establish label-preserving G1 source-free binding and would defer a second schema/Feature or a revision of this one for the same owner/ordinal/name fact. |
| Exported generic MethodDefs and bounded InterfaceMethod requirements; versions 1 and 3/G1L1 | Retain this G1 component. One codec, owner/coverage validator and preservation contract serve both existing source views, including expectations and zero-parameter declarations. **Alone it is insufficient for the plan's C1/Construction-2 provider route**, including its method-generic requirements. |
| **Both G1 paths plus C1's retained generic publication and InterfaceMethod requirement views, under explicit label-bearing successors** | **Selected.** Reuse one table and Feature while selecting each complete schema explicitly. Preserve C1's catalog reconciliation, class facts, construction stages and separate failure maps; do not apply the bounded G1 interface gate to required C1 common members. No additional call opcode or label ABI is needed. |
| All MethodDefs/callable signatures in every profile | Not selected. It would add unrelated ordinary-method, callable-value, alias and profile-publication obligations. A common row shape is not approval or support for those source views. |

## Exact selection, allocations and alternatives

All identities and assignments below are **unapproved, provisionally
actionable candidates**, not aliases or automatic component selections.
`L1` is only shorthand for this document's table, Feature and source-view
validation/preservation delta, with the selected schema's owner set below;
it is not an independently selectable schema or decoder.

| Candidate shorthand | Exact candidate identity | Complete composition |
|---|---|---|
| G1L1 | `trrm/1-draft-generic-method-calls-parameter-labels-1` | Exactly [G1](standard-trrm-generic-method-call-body-encoding.md#exact-schema-and-capability) plus L1; retains the two bounded G1 publication paths. |
| C1L1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-parameter-labels-1` | Exactly [C1](standard-trrm-exception-class-witness-binding.md#exact-schema-and-catalog) plus L1, including the C1 requirement-owner extension. Preserve C1's reconciled B+I+G+G1 catalog, not G1L1's unreconciled catalog. |
| C1M1L1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-parameter-labels-1` | Exactly [C1M1](standard-trrm-c1-class-declaration-mutability.md#exact-schema-and-proposed-assignment) plus the same L1; equivalently C1L1 plus class-only TypeDef mutability bit `0x80` and its exact comparison rules. |
| Construction-1L1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1` | Exactly [Construction-1](standard-trrm-c1-constructor-declarations.md#allocation-and-exact-agreement) plus the same L1; equivalently C1M1L1 plus its I/P declarations, receiver rules and successor-only Contract `0003=Construction`, not inactive Transfer. |
| Construction-2L1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1` | Exactly [Construction-2](standard-trrm-c1-construction-runtime-boundary.md#exact-semantic-successor-and-candidate-assignments) plus the same L1; equivalently Construction-1L1 plus Feature `25/1`, HomeMap role `4`, `ED/00` and Body record `11`, with their lifecycle rules. |

Keep `(1,0)`, governing edition `1`, Body codec/encoding `01`, existing row
widths, instructions, tokens and DefId kinds. Only the new section/Feature
and their semantics are added to each stated whole agreement. Equations in
this table describe contracts, not parsing of schema-name components.
G1L1 is not a sub-artifact decoder inside C1L1 or a constructor successor.
No CG1, E1/CheckedExceptions, private, array or RootPin schema joins by link.
Construction-2L1 still needs the construction owner's **independent explicit
host/runtime admission when executed**; its schema selector and labels
do not select N, a native provider ABI or runtime support.

| Profile strategy | Assessment |
|---|---|
| G1L1 alone, with C1 providers | Insufficient. Original C1/C1M1/Construction-2 select label-free G1, and G1L1 does not represent their reconciled graphs, class facts, I/P or backing operations. Decline, rather than decode C1 bytes as G1L1 or silently add L1 to an existing C1 key. |
| **Explicit family above** | **Selected.** Preserves the active C1M1 declaration-binding slice and Construction-1/2 boundaries, with a named label agreement at each stage. More selector literals, but one codec/Feature and no new construction or failure representation. |
| Only G1L1 and one Construction-2L1 whole-schema replacement | Coherent smaller selector set **if** the workflow deliberately replaces all C1-stage label operations and re-emits every supplied provider/plan under Construction-2L1, including declaration-only inputs. It saves no label bytes and removes the separately selected C1M1 binding stage; that work-order change is not selected here. |
| Replace the unpublished original C1-family identities wholesale | Permissible, but their selection/composition owners would all have to retire the old agreements consistently. Keeping the old literals as compatibility aliases or changing their meaning only in a label reader is not that replacement. Not selected. |

### G1 source-map pairing

For the proposed label-preserving publication route, replace the source-input
proposal's version-1/G1 and interface companion's version-3/G1 pairings with:

| Exact artifact schema | Mapping grammar |
|---|---|
| `trrm/1-draft-generic-method-calls-parameter-labels-1` | Version 1: method-only root `schemaVersion`, `sources`, `exports`; no TypeDef assignments |
| `trrm/1-draft-generic-method-calls-parameter-labels-1` | Version 3: root `schemaVersion`, `sources`, `typeExports`, `genericMethodExports`; bounded interface and generic-method assignments |

The existing version-1 and version-3 JSON grammars, source selectors and
snapshot rules are unchanged; **their proposed artifact-schema pairing and
emitter selection change together**. Version 3 may publish either or both
admitted declaration kinds; array contents never select another decoder.
Version 1 cannot carry interfaces even with empty arrays. The existing
six-argument typed emitter selects G1L1 explicitly for these inputs; the
version-1 CLI handoff supplies an empty TypeDef list, and version 3 supplies
both lists against the same checked program. Empty lists do not infer a schema.
These remain **G1-only** pairs: versions 1 and 3 do not select any C1
label-bearing identity, even with empty lists. They are source-producer
configuration, not a C1 provider or phase-map transport.

Do not redefine original G1, C1, C1M1 or Construction-1/2 keys or interpret
their artifacts as label-bearing successors. Historical support is not
required and supplies no label-preservation claim. The old
`InterfaceParameterLabel`/`InterfaceParameterLabels`,
`trrm/1-draft-generic-method-calls-interface-labels-1` and G1-scoped
`G1ParameterLabels` candidate names are superseded here, not compatibility
aliases. CG1/version 2 remains unchanged and is paired with none of these
successors. A schema/map mismatch rejects; equal version bytes, a Feature
list or section 0066 never selects another decoder. No compatibility reader,
alias or schema-key-only conversion is required.

### C1 provider inputs and phase maps

For each C1-family operation, select its **one exact candidate literal** for
the root, every supplied provider including unused inputs, and every captured
Clause, Origin and Plan artifact before schema-dependent indexing or
decoding. Preflight those inputs even when they contribute no label rows.
G1L1, original C1-family and sibling successor artifacts are not mixed-profile
inputs to that operation. No provider bridge or conversion is selected;
schema inclusion does not authorize replacing an immutable provider snapshot.

| Operation scope | Exact label-bearing route and retained inputs |
|---|---|
| C1 declaration/identity stage | C1L1; C1's checked declaration mappings, provider inputs and original role/interface-anchor contexts. Class mutability remains unknown; no complete-class or construction claim follows. |
| C1M1 class/witness binding slice in the work order | C1M1L1; the same original expected/actual contexts plus exact class mutability. Preserve the declaration-binding slice, not Construction-1/2 roles or full failure construction. Unsupported complete member/witness closure still refuses. |
| Constructor/member source publication and I/P retention | Construction-1L1; the [seven-argument source API and class mappings](standard-trrm-c1-class-source-publication.md#candidate-source-input-and-capture), `TrRmSourceClassExport.MemberExports`, and the [explicit projection key](standard-trrm-c1-constructor-declarations.md#complete-construction-projection-and-source-keys). This stage does not acquire ED/record-11 lifecycle Bodies. |
| Complete phase-1/2 class/witness/failure closure required by the plan | **Construction-2L1**, replacing the declaration-failure proposal's Construction-2 selection **for this label-preserving route**. All supplied Core/interface/member providers and real plan-bearing inputs use that same literal, including providers retaining declarations only. Preserve I/P and actual backing lifecycle closure; earlier stages cannot satisfy this gate. |

C1 interface/generic source publication uses C1's exact checked
`(source input ordinal, full declaration span, definition kind, ExportKey)`
associations and complete retained declaration closure, not the G1 version-3
bounded map. Constructor successors additionally retain their class/member
mapping records and explicit I/P keys. Capture each requirement's labels
from its declaring source view before rewriting. Imported publication
preserves its validated vectors, actual keys and original expectations;
no source/span input or implementation-name recovery is required there.
Class/interface keys do not invent MethodDef keys or label identities.
No C1 CLI/JSON grammar for the generic/interface label maps defined here, new
API default or `parameterNames` override is selected. The separate
[class-source CLI transport proposal](standard-trrm-c1-class-source-cli-transport.md)
owns class/member input and does not change the G1-only version-1/version-3
pairings or supply a C1 generic/interface label map.

The full Construction-2L1 handoff preserves these **separate, nonserialized**
contexts with their existing exact shapes:

| Context | Preserved meaning |
|---|---|
| `CoreContractFailureIdentitySelection` | Authorized roles belong to each originating logical Module. Retained Core fallback requires its RequirementException; retained ensures require its PostconditionException. Explicit E is not a Core role. |
| C1 `TrRmExceptionInterfaceBinding` | Independently selected exact Core `IException<TSelf>` interface, original expectation and provider snapshot; no E1 or UnreachableException anchor. |
| [Phase-1 `TrRmPreconditionFailureBinding`](standard-trrm-explicit-precondition-failure-binding.md#exact-candidate-carrier) | Original declaration/formal/ordinal correspondence, ExplicitType/CoreDefault classification and explicit plan location. CoreDefault matches Origin's RequirementException; explicit E remains the plan's complete class. Callable recipes still need their separate admitted carrier, never a false CoreDefault classification. |
| [Phase-2 `TrRmPostconditionConstructionBinding`](standard-trrm-initialization-predicate-postcondition-construction.md#exact-accompanying-map) | Independent original phase-2 occurrence and explicit plan location; the class equals Origin's authorized PostconditionException. A phase-1 entry cannot satisfy this map or vice versa. |

Both maps still select assigned Body `0B=ConstructorBinding` in an explicitly
retained real Plan MethodDef Body, using **that plan's** Type/Method/ConstantMap
context. Contract `000B` phase attachments, Body `0B`, and constructor
Contract `0003` remain distinct namespaces. L1 adds no failure field to them.
Preserve total retained occurrence coverage, bodyless expectations with a
real plan-bearing input, inherited/relocated origins, uncalled declarations
and eliminated failed paths. Neither phase-3 selectors nor static proof
requests receive either map.

Re-emission remaps these contexts alongside declarations, MethodDefs, label
rows, graphs, maps, exact P→I and original provider expectations. Labels
neither choose a failure class/constructor nor rebind the zero-supplied plan,
its actual defaults/empty-params facts or execution route. Bytes plus labels,
a role table or snapshot passthrough do not replace missing maps/closure.
The selected successor is necessary, not evidence of complete codecs,
context-bearing API admission or native execution support.

### Allocation and carrier alternatives

Allocation review: the [registry](../specs/representation/trrm-1.registry.json)
assigns semantic sections through `004B`, plus `7F00`, and Features
`1..7,9..13`. Related candidates occupy `004C..0065`; the
[CG1 review](standard-trrm-constant-generic-methods.md#allocation-review)
includes the static/access and constant-table allocations. Section `0012`
is selected as FixedArrayLayout in the separate
[array proposal](standard-trrm-fixed-array-identity-shape.md).
Feature candidates cover `8,14..24`, with
[class construction](standard-trrm-c1-construction-runtime-boundary.md#exact-semantic-successor-and-candidate-assignments)
selecting `25/1`. Select **section `0066` and decimal Feature `26/1`**.
These are unapproved candidates, not reservations. Repacking unpublished
assignments is permissible; it brings no necessary benefit to this slice.

| Candidate | Assessment |
|---|---|
| Add a name to Param, making its row 20 bytes | Coherent minimal row amendment, not forbidden by draft compatibility. But Param belongs to MethodSig, which can be shared by distinct nongeneric requirements. Declaration-specific labels would require splitting otherwise identical signatures/ranges and revising sharing rules. Generic MethodSigs already have exclusive MethodDef-owned formal ranges; that does not make labels structural identity or eliminate the need for distinct expected/provider views. |
| **MethodDef-owned 12-byte label rows** | **Selected.** One table, existing typed references and explicit ordinal coverage; leaves structural sharing and existing row widths intact. No default/receiver/name-alias field. |
| Method-owned variable name vector | Coherent, potentially smaller at larger arities; requires vector extent/count rules instead of the existing fixed owner/ordinal child-row pattern. No size/performance superiority is claimed for the selection. |
| Labels only in version-1/version-3 configuration, SourceMap, Attr, Blob or a host cache | Insufficient. Configuration may supply producer inputs but cannot replace the required in-artifact source view. Optional/debug data can disappear; opaque reference payloads cannot support typed remapping. |

## Record, inputs and closure

Section **`0066=MethodParameterLabel`**, directory policy **`0000`**,
is an ordinary fixed table with stride **12**, no offset index or extra
header. Multibyte words are little-endian:

| Offset | Field | Meaning |
|---:|---|---|
| 0 | `u32 ownerMethodDef` | Required typed MethodDef row, not a DefId, Import token or provider row number |
| 4 | `u32 ordinal` | Zero-based explicit logical Param ordinal in that method's MethodSig |
| 8 | `u32 name` | Required typed String row containing the decoded parameter name |

Row references have high byte zero and lie in `1..00FF_FFFF` within the
actual table count. Present tables have `1..00FF_FFFF` rows. Ordinals have
Param's full `u32` width, not Body's `u16` index width. Check counts, extents
and `12 * recordCount`; use normal section alignment/framing with no row
trailer or reserved field.

The allowed owner set is the union of the applicable categories below:

1. A public provider-local MethodDef with a nonempty method-formal range and
   at least one explicit MethodDef Export under the selected schema's
   inherited G1 generic-method contract.
2. A bodyless consumer-local expected generic MethodDef with a nonempty
   method-formal range, selected by a retained MethodDef Import under that
   contract. A retained source-view proxy keeps its Import as specified below.
3. **G1L1:** an exact directly declared requirement MethodDef selected by an
   admitted InterfaceMethod in the bounded interface subset, including an
   expected copy. Its InterfaceDef/TypeDef and MethodDef have the same Module.
4. **C1L1, C1M1L1, Construction-1L1 and Construction-2L1:** every retained
   instance requirement MethodDef selected by an InterfaceMethod admitted
   under C1's complete declaration/witness rules, including method-generic
   requirements and expected copies. Direct ownership agrees as above;
   inherited uses retain the actual declaring interface and MethodDef,
   not a synthetic derived-owner copy. A requirement with an admitted default
   Body still owns its requirement vector, not its selected implementation's.

Validate each owner's complete existing declaration/range/identity contract;
labels cannot make an unsupported owner eligible. A MethodDef selected by an
InterfaceMethod must satisfy its schema's requirement category. In G1L1,
an Export or nonempty method-formal range cannot bypass member-generic or
other bounded interface exclusions. In the C1 family, those G1 source-input
exclusions cannot erase required common members; unsupported C1 member,
mode/default, associated, witness or value codecs refuse the complete use
rather than publish a bounded G1 substitute. The label inherits its Module
through MethodDef's existing partition, not its MethodSig, the selected
provider's row number or an artifact-wide Module. Private unexported helpers,
nongeneric ordinary targets and I/P projections, callable-value anchors,
generated proof stubs, generic formals and implicit receivers are not label
owners. A separately selected implementation target gets labels only if it
independently qualifies as an exported generic MethodDef, never merely by
serving a requirement.

Every retained admitted owner has exactly one row for each ordinal
`0..paramCount-1` in its MethodSig, including unused parameters and logical
zero-data values. Export aliases of one MethodDef share its one vector, not
duplicate rows or alias-specific labels. Distinct expected views keep their
own MethodDefs/vectors even if they select the same provider/key.
The pair `(ownerMethodDef, ordinal)` is unique; even identical duplicate rows
reject. Zero-parameter owners have the empty vector and no label rows.
The section is absent exactly when there are no label rows. Missing coverage,
extra ordinals, invalid owners or partial vectors reject, not positional-only
degradation. Names may share String storage; method source views do not merge
because their structural signatures agree. Existing nonempty method-generic
ranges remain exclusive to their MethodDefs; the label table does not relax
that rule.

Names are nonempty, NUL-free, well-formed scalar text satisfying the
[identifier profile](../specs/lexical-grammar.md#source-text-and-identifiers).
Store the decoded name, without `@`; reserved/contextual keyword spellings
are valid names, with escapes used where source vocabulary requires them.
`_` alone, embedded escapes, whitespace and arbitrary-string names reject.
Equality is ordinal scalar equality, with no case folding or Unicode normalization.
Names must be distinct within each declaration view and satisfy ordinary
signature declaration-space rules, including enclosing generic-formal
collisions. Equal decoded names in different String rows/encodings are still
duplicates. Check each expected/provider view against its own formal names;
formal correspondence by kind/ordinal does not rename its value labels.

Retain `TrRmSourceGenericMethodExport(BoundProgram Program,
int SourceInputOrdinal, TextSpan DeclarationSpan, string ExportKey)` and
the source-input proposal's exact-program anchor, complete public-method
coverage and alias/key rules. Version 1 retains exactly:

- Root: `schemaVersion`, `sources`, `exports`.
- `exports[]`: `sourceInputOrdinal`, `declarationStart`,
  `declarationLength`, `exportKey`.

Retain the interface input record
`TrRmSourceInterfaceMethodExport(int SourceInputOrdinal,
TextSpan DeclarationSpan, string ExportKey)` and its enclosing
`TrRmSourceTypeExport.InterfaceMethodExports`. Version 3 keeps exactly:

- Root: `schemaVersion`, `sources`, `typeExports`, `genericMethodExports`.
- `typeExports[]`: `namespace`, `name`, `genericArity`, `exportKey`,
  `interfaceMethodExports`.
- `interfaceMethodExports[]`: `sourceInputOrdinal`, `declarationStart`,
  `declarationLength`, `exportKey`.
- `genericMethodExports[]`: the same method-entry fields as version 1's
  `exports[]`.

The existing source snapshot and generic-method object grammars/checks remain
owned by the [source-input proposal](standard-trrm-source-generic-method-export-inputs.md).
There is **no `parameterNames` override input** in either grammar or API.
Resolve each exact checked source declaration first, then immutably capture
its ordered explicit ordinary-parameter names before rewriting:

- For a generic definition, use its checked declaration/proof signature,
  associated through `BoundGenericMethodDefinition` and the resolved source
  anchor. The current module-level path retains these names in its proof
  function's first `ParameterCount` variables. They are not GenericParam names,
  a generated specialization's names or a function-index identity.
  For C1 member definitions, capture the checked explicit parameter sequence;
  exclude any receiver and proof-only formals when mapping to Param ordinals.
- For an interface requirement, capture `BoundInterfaceParameter.Name`.
  Map explicit parameters to MethodSig Params; do not include the receiver
  even when a runtime/proof input list includes it. For C1's broader required
  members, retain that exact checked declaration/vector association through
  the complete member carrier, including method-formal correspondence;
  an unavailable carrier is a gate, not an empty vector or implementation
  name substitution. Their producer inputs are the C1 route above.

Carry that resolved declaration/vector association through internal rewriting.
Source-free re-emission instead preserves the loaded declaration's validated
vector, not names copied from a provider implementation Body. Preserve source
ordinal/span/snapshot checks, complete generic-method and direct-requirement
mapping, immutable input capture and atomic output preservation. Keys select
identities, never labels or label rows. These additions do not widen the
selected schema's non-label declaration, mode, constraint or Body gates.

**Candidate Feature `26/1=MethodParameterLabels`:** governing editions `[1]`,
unconditional prerequisites `[]`; scope is complete caller-visible labels
for the allowed owner set, source-free declaration binding and typed
preservation under the explicitly selected agreement. One registration serves
both schema families; it adds no executable/runtime layer.
Every Module retaining an allowed owner requires it, even for zero parameters,
declaration-only views or unused publications/expectations. Any label row also
requires it in its owner's Module; invalid owners do not become valid by adding
26. Detection uses retained declarations/Imports/InterfaceMethod links, not
observed named calls or table presence alone.

An interface with no requirements, an unexported private generic helper, an
already ordinal-bound GenericCall alone, or schema/configuration selection
alone does not trigger 26. G1's `23/1` and other Features retain independent
retained-use triggers and complete closure: an exported/expected generic
MethodDef requires both 23 and 26; a private generic helper still requires
23, not 26 by itself.
C1's retained method-generic requirement views also require both 23 and 26,
even when uncalled. A nongeneric requirement-only Module need not have 23
unless its retained contents independently trigger it; owner type formals
alone do not. Thus `26 -> 23` is not an unconditional prerequisite.
26 registers neither missing witness support nor native interface Feature 5.

| Selected catalog | Preserved independent closure |
|---|---|
| G1L1 | `23/1 -> []`, `26/1 -> []`, full SF1 `11/1 -> []`; G1's incomplete `10 -> 11` and `12 -> {10,11}` registrations/gates remain unchanged. |
| All four C1 label-bearing successors | C1's reconciled `10/1 -> []`, `17/1 -> [(10,1)]`, `11/1 -> []`, `12/1 -> [(11,1),(17,1)]`, `23/1 -> []`, plus `26/1 -> []`; `13/1` independently for retained field/query/import/export uses. Missing execution or member codecs remain gates. |
| Construction-2L1 only | Inherited `25/1 -> []` for retained bodyful I/P, role-4 Homes or record-11/ED uses, including uncalled Bodies. Bodyless I/P roles, L1 or schema selection alone do not trigger 25. |

Provider Modules retain their own manifests within the route's exact-schema
agreement; packing never replaces them with an artifact-wide Feature union.

### Interface-only Feature 23 disposition

Follow [Feature 23's owner](standard-trrm-generic-method-call-body-encoding.md#exact-schema-and-capability):
retained MethodDefs with method formals, GenericCallTarget use (including
owner-only instantiation), or forwarded constant-formal arguments trigger 23.
The provisional label design adds **no extra Feature 23 requirement merely
for publishing an interface**, generic owner or nongeneric requirement.
It deliberately corrects interface-only over-reporting rather than broadening
23's semantic scope to container/schema selection.

The current writer **does over-report on that publication path**, but the
exact helper distinction matters:
[`EmitGenericMethodCallsDraftSource`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceWriter.cs)
calls `AddGenericMethodInterfaceMetadata` for nonempty TypeDef mappings,
then calls `AddGenericMethodMetadata` even when `emissionRows` is empty.
The latter unconditionally adds `TrRmFeatureRow.GenericMethodCallsCandidate`;
the interface helper itself writes interface metadata without adding that
Feature. Thus successful requirement-only or empty-interface publication can
include 23 with no owner-defined trigger. This is an inspected implementation
behavior, not the selected candidate closure or qualification evidence.

Candidate writers derive both 23 and 26 from all retained contents before
emission. Remove the blanket interface-path 23 addition only when no real
23 trigger remains; never erase it from an uncalled generic declaration,
expected import, required C1 formatter or GenericCall. Do not add dummy
method formals/calls to justify it. Interface/witness support and all other
requirements remain independently necessary. No writer is changed here.

## Producer, consumer and replacement obligations

The producer publishes the selected schema's inherited complete explicitly
exported generic MethodDef/MethodSig/Param/formal/constraint/default/contract/
Body closure, G1L1 version 3's bounded TypeDef/InterfaceDef/InterfaceMethod
closure, or the C1 family's complete retained requirement/member/witness
closure as applicable, **plus every allowed owner's labels and String rows**,
regardless of observed named calls. The chosen C1 stage also retains its
class facts and construction/phase contexts where required; L1 is not a
partial-closure publication escape. Uncalled public generic definitions retain
the source-input proposal's complete explicit-key coverage; omission is not
a label-loss escape. Labels are truthful facts about each selected source
declaration. Ordinary defaults and broader interface facts remain unsupported
where their selected path lacks a carrier, not represented by empty labels
or new fields. Each publication keeps its explicit ExportKey; labels have no
independent Export/Import identity.

Before exposing an imported declaration, the source-free consumer validates
the selected schema, per-Module support/closure, framing, typed references,
owner relationships, complete parameter coverage and decoded-name validity.
Validate every retained allowed owner, not just labels mentioned by one call.
Resolve generic MethodDef Imports/Exports, and the exact interface
ModuleRef/TypeDef and requirement MethodDef keys, with their expected
declarations and provider snapshots. Missing/duplicate providers, wrong
owners/keys/kinds or unsupported declaration contracts reject. A fresh
label-bearing import requires the provider's complete supported label
contract under the route's exact agreement. On the C1 route, preflight all
providers/plan inputs under its one selected successor as specified above.
Original G1 or C1-family bytes cannot supply missing labels through `argN`,
inferred names, source lookup, a mapping file or a G1L1 sub-decoder.

Reconstruct the generic template/selected signature's ordinary parameter names,
or `BoundInterfaceParameter.Name`, from that view's validated vector and original
declaration/expectation identity. Preserve an immutable explicit-label vector
on the declaration view separately from its selected implementation/proof
Body: binding a provider Body must not replace the signature's names with
provider variable names. Internal synthetic names for unpublished helper
Bodies are not caller-visible labels. Consumer checks require neither
provider source/debug data nor semantic re-proof of producer-published bodies.

A fresh provider declaration view copies its provider's vector into the
consumer-owned expected declaration. A retained expected declaration keeps
**its original vector** through binding and replacement; do not refresh it
from a replacement provider or selected implementation. Corresponding value
parameters still map by ordinal. Labels neither establish cross-artifact
identity nor become witness/binary signature equality tests:

- A differently named implementation can satisfy the requirement; interface
  callers use only the requirement's labels. If that implementation independently
  has an exported generic declaration view, its direct callers use that view,
  not the requirement's vector.
- A label-only provider rename can satisfy existing binary replacement rules
  for either owner kind while changing fresh source binding. Unchanged compiled
  calls retain their argument mapping; existing generic-method and requirement
  expectations retain their source view.
- A source-rebinding-equivalence claim requires equal label vectors by
  ordinal. Discharging a retained source-view proxy into a provider declaration
  likewise requires equal vectors and all ordinary retained contracts;
  otherwise preserve the proxy and Import.

This preserves the
[binary/source compatibility distinction](../specs/representation/binding-and-replacement.md#5-packing-replacement-and-relied-on-facts);
it adds no hot-replacement or runtime policy. A call consults **one selected
view**, never alternate provider/implementation labels. Unknown or duplicate
supplied labels and missing required arguments reject. In-position labels
permit following positionals; out-of-position labels require subsequent
arguments to be named. Evaluate the receiver first and supplied arguments
once in source order, then use the existing ordinal mapping/staging rules.
Owner or method-formal substitution does not rename value labels, retry lookup
or reorder evaluation. Lowered closed helpers need no new label rows unless
an admitted declaration view remains retained; their positional code does not
replace a public/open generic declaration or its label contract.

## Typed transformation and conformance

Remapping decodes typed label rows, remaps **MethodDef and String references**,
and preserves logical ordinals and decoded names. Rebuild generic
MethodDef/MethodSig/Param/formal/constraint/default/Body closure,
InterfaceMethod links, Import/Export expectations and per-Module manifests
in their using contexts under the selected whole-schema rules. The C1 family
also preserves/remaps its class/member/witness graph, mutability where
selected, constructor role/guarantee/I-row references and lifecycle records
where selected, and both original phase maps with role/anchor contexts.
MethodMap, Body-record, complete-type and phase fixups remain separate;
label ordinals are not indexes in any of those maps. Do not rewrite matching
byte patterns.

Preserve original source-view proxies and Imports when resolved provider views
differ or other retained obligations prevent discharge. Do not coalesce two
expected MethodDefs with different vectors merely because their provider,
key or structural signature agrees. Cloning an expected generic MethodDef
also rebuilds its exclusive MethodSig/generic ranges and all affected uses;
sharing a nonempty generic range is still invalid. Clone other structural rows
when their using contexts require it. Never overwrite a provider's own vector
with an expectation's labels to avoid retaining the proxy.

Canonical writers sort label rows by owner row then ordinal; ordinary readers
accept other orders, but never duplicate keys or incomplete vectors. Trim labels
only with genuinely unretained declaration views, not unused named arguments
or a disappeared call. A destination schema unable to represent a retained
label contract rejects re-emission rather than moving it to optional data.
Source-free conversion cannot invent a missing nonempty vector from nameless
G1 or C1-family Params; re-emission is not source rebinding or a byte/schema-key
substitution. Even a zero-parameter subset does not infer agreement on a
different whole schema or supply its other missing class/construction facts.

Required cases, **not claimed passes**:

| Case | Required result |
|---|---|
| Independent version-1 generic provider/consumer with two authored labels supplied in reverse order, explicit/inferred/defaulted generic arguments, no source/debug/configuration files at consumption; repeat with generic exports in version 3 | Imported names, inference and ordinal mapping equal source binding; evaluation remains supplied order; invented `arg0`/`arg1` are unknown unless actually authored |
| Independent version-3 interface provider/consumer and reversed requirement labels under the complete supported interface prerequisites | Requirement labels and ordinal mapping equal source binding; no implementation-label substitution |
| C1-family provider/consumer retaining method-generic formatter and unrelated authored InterfaceMethod requirements, including uncalled/bodyless expectations and differently named implementations | Requirement vectors survive under the exact C1 successor; owning Modules require both 23 and 26. The G1-only interface subset neither substitutes for that closure nor admits these requirements under G1L1 |
| Different implementation labels; unrelated requirements legally share a structural MethodSig but expose different vectors; distinct generic exports have equal structural shapes | Views remain distinct; implementation spellings do not become alternatives; generic formal ranges remain exclusive |
| Zero/one/multiple parameters, uncalled exported generic method, unused requirement/expected import, logical unit parameter; ordinal 65536 when counts permit | Complete exact vectors and Feature 26 for every retaining owner Module, even with no label rows; no receiver row, missing-label fallback or `u16` narrowing |
| Keyword escape, distinct case/normalization spellings, equivalent ASCII/UTF-8/UTF-16/UTF-32 name rows | Decoded ordinal identifier rules; equal decoded duplicate names reject |
| Absent table for nonzero arity, partial vector, duplicate pair/name, empty or malformed String, invalid identifier, bad reference/kind/owner, extra ordinal, extent/capacity overflow | Reject before declaration exposure or publication; preserve the output destination |
| Unknown/duplicate call label, required omission, positional after an out-of-position label | Ordinary named-argument diagnostics; no implementation-label or overload retry |
| Label-only generic or requirement provider replacement; provider Body variables differ from a retained expected vector | Preserve original expectation/call mapping for binary-compatible replacement; fresh views see new labels and source-equivalence fails; attaching the Body does not refresh expected labels |
| Changed owner/signature/key, enclosing-formal collision, source-proxy discharge with unequal labels | Identity/contract/validity mismatches reject; unequal labels prevent source-view discharge, not otherwise valid binary satisfaction |
| Multiple Export aliases for one generic MethodDef; separate expected views for the same selected provider/key with different valid labels | Aliases share one publication vector; expected vectors remain distinct through replacement and re-emission; no duplicate label rows or first-view-wins merge |
| Packed Modules with reordered rows/String encodings and shared nongeneric MethodSigs, source-free re-emission, then another independent import; repeat within one selected C1 successor with its class/witness/construction and phase contexts | Typed references remap, original generic/requirement views, keys and complete selected closure survive; clone exclusive generic ranges when needed and remap both maps with MethodDefs/Bodies; unrelated Modules do not acquire Feature 26 |
| Generic-publication Module, nongeneric requirement-only Module with owner type formals but no GenericCall, private-helper-only Module and empty-interface Module packed together | 26 is required for the first two; 23 for the generic-publication and private-helper Modules, not the nongeneric requirement-only or empty-interface Module absent another real trigger. No artifact-wide union or unconditional `26 -> 23` is inferred; correct the current interface-path over-reporting |
| Complete phase-1/2 handoff under Construction-2L1 with non-first and multiple providers, aliases, Core generic common members, bodyless expectations and real retained P→I/backing plans | One exact selection for all supplied inputs, complete original labels plus class/witness/failure closure, separate role/anchor/phase contexts and per-Module 17/10/11/13/23/25/26 as applicable. Schema/label success alone establishes no native enforcement |
| Phase-1 ExplicitType/CoreDefault and phase-2 maps share ordinal/index values; remap labels, clause/plan/origin MethodDefs and records together | Maps remain distinct and total; Plan maps decode in the Plan Body context. Original failure selection, P→I/defaults and expected source views survive; labels never reselect a class/constructor |
| C1L1, C1M1L1 and Construction-1L1 declaration-stage use versus attempted complete construction/failure closure | Retain each stage's label contract and only its defined class/role facts; refuse complete phase-1/2 uses needing Construction-2L1 rather than erase them or upgrade the selector |
| Original C1/C1M1/Construction-1/2, G1L1 or sibling successors supplied to a selected C1-family operation, including unused/zero-parameter providers | Reject exact-selection mismatch before schema-dependent decoding; no G1L1 sub-decoder, component parsing, zero-bit/empty-vector inference or compatibility alias |
| Versions 1 and 3 paired with G1L1, including empty lists; version-1 type fields, mixed root shapes, version 2/G1L1, any C1 successor paired with either G1 map, old G1 selected for the proposed label-preserving route | Accept only the two exact G1L1 pairs with their unchanged grammars; C1 uses its separate typed/context-bearing route. Reject mismatches without decoder retry or source reads out of order |
| Missing 26/1, unsupported exact schema, missing nonempty vectors on attempted source-free upgrade, downgrade, unsupported ordinary defaults or broader interface members | Reject affected label-preserving use; no alias, erased obligation or inferred schema composition |

## Integration owners and current support

On adoption, the metadata chapter and machine registry would own section
0066's framing, fields/references, allowed owner set, coverage and Module
ownership; manifests would own Feature 26's registration/detection; binding,
reader-trust and canonicality chapters would own the preservation/checking
rules. The source-input and interface-export proposals would select G1L1
for versions 1 and 3 respectively and extend both serialized closures. This explicitly
supersedes their old G1 pairings and missing label persistence, not their
mapping grammars or GenericCall records. Feature 23's retained-use rule is
preserved, not amended to match the current writer's blanket addition.

The C1, C1M1, Construction-1/2 and declaration-failure owners would add the
four exact label-bearing selections and extend their input/context
applicability for this route. The full failure handoff selects
Construction-2L1, and the C1M1 declaration-binding slice selects C1M1L1;
neither original key is redefined or aliased. Preserve C1's catalog,
C1M1's bit, Construction-1's `0003` role semantics, Construction-2's
`25`/role-4/ED/record-11 lifecycle, and both phase maps' existing shapes.
The C1 requirement-owner extension supplies labels for retained generic
common members without widening the G1-only interface map or claiming
missing C1 member/witness/default/runtime codecs.

Source names/calls/witness chapters already own label semantics and need no
replacement rule. No owning document is edited here.

Current support is separate; these are **source observations, not newly
executed qualification**:

| Evidence | Boundary and implementation consequence |
|---|---|
| [`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`Binder.Interfaces`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs) and [`Binder.BindArguments`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | Checked generic proof variables and BoundInterfaceParameter retain names; requirement stubs copy names, and selected signature names drive named mapping. Capture the original explicit vectors, not generated stub identities. |
| [`G1 writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceWriter.cs), `AddGenericMethodMetadata` / `AddGenericMethodInterfaceMetadata` | Both publish MethodDef/MethodSig/Param closures with nameless 16-byte Params. The interface path subsequently invokes the shared metadata helper, which adds 23 unconditionally even without generic emissions. Add labels under G1L1 and content-derived 23/26 closure, preserving source anchors, aliases and helper gates; do not describe the current blanket 23 as the selected interface contract. |
| [`Param reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmParamTableReader.cs) / [`writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmParamTableWriter.cs) | Current rows contain owner/ordinal/type/mode/flags only. Keep their stride and structural meaning; add a separate required owner/coverage/name validator and per-Module Feature detection. |
| [`G1 reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceReader.cs), [`generic import binding`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.StaticProtocols.cs) | The generic reader invents `argN`; template adapters consume proof-variable names. Reconstruct and carry each view's validated vector into selected signature parameters. The interface path still rejects interface constraints and role-2 calls; the table does not complete it. |
| [`G1 provider binding`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodProviderBinding.cs), `CompleteExpectedGenericMethodBody` | The current adapter attaches a provider ProofFunction while restoring the expected name, caller-unsafety and no-return facts, not a separate value-label vector. Preserve the expectation's immutable source view independently when attaching/substituting a provider Body; labels are not an added binary equality test. |

The six-argument emitter, CLI exact-schema/version dispatch, artifact/section
selection, declaration import adapters and source-free transformations need
the coordinated G1L1 integration described above. The C1-family routes also
need explicit successor/index/context admission, complete requirement
vectors, provider/class source inputs and both phase-map transformations;
the G1 writer/map is not their implicit adapter. Shared traversal, verification
and lowering must preserve declaration views and existing ordinal argument
mapping; they need no new call opcode, runtime lookup or native label ABI. The
[support inventory](../implementation.md#bounded-generic-functions) leaves
source-free interface binding, role-2 production, complete replacement closure
and generic-import re-emission open. Existing positional generic-import support
does not establish label preservation.

Remaining cross-profile gaps include ordinary nongeneric exports (even when
co-resident in a label-bearing artifact), named class-constructor/direct
method source views outside these publication paths, callable-value/fnptr
signature views and aliases, and generic-method labels under baseline or
other exact schemas such as CG1.
CG1 is still its explicitly selected old G1-plus-enum delta, not a transitive
G1L1 composition; its version-2 route needs a separate reviewed label selection
before claiming preservation. Private ParameterSurface support likewise supplies
no Standard carrier. None of those gaps expands this allowed owner set.
The already bound zero-supplied `0B` plan does not require named constructor
arguments, so that separate label gap is not a reason to alias the plan's
required C1 interface/generic views into G1. A named use requiring an
unrepresented source view must still decline, not expose synthetic names.

This proposal does not claim implementation or qualification; the current
G1L1 slice and remaining routes are tracked in the
[conformance-gap index](../implementation/conformance-gaps.md).
**Normative adoption of the shared table/Feature, five exact agreements and
G1-only map pairings remains unapproved.**
Under the authorized provisional workflow, composition, owner coverage,
Feature 23 disposition, provider selection and phase-map applicability are
specified here; no further policy choice is deferred to implementation.
