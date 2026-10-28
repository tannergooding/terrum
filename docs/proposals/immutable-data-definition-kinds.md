# ImmutableData definition-kind assignments

> [!IMPORTANT]
> **Unapproved proposal.** These numeric candidates are provisional
> implementation direction under the current task authorization, not
> normative allocations. This document changes no accepted schema, assigns
> no section or Feature, and establishes no compiler support or conformance.

## Decision and scope

**Recommend DefId candidates `0x22=InterfaceConstDef`,
`0x23=WitnessAssociatedConst` and `0x24=StaticField`.** Keep the existing
unapproved `0x20=AssociatedTypeDef` candidate and assigned
`0x21=UnionCaseDef` distinct and unchanged.

This supplies only the numeric assignment decision left open by
[ImmutableData declaration references](immutable-data-declaration-reference.md).
Preserve that proposal's direct-only reference, closed target sets, exact
owner substitution, binding and lifetime rules:

| `ImmutableData.kind` | Candidate permitted `declarationDefId` kinds |
|---|---|
| `1=ResolvedConstant` | `0x22` or `0x23` only |
| `2=PublishedImmutableRegion` | `0x24` only, with separately admitted immutable publication |

Do not add `AssociatedConstDef` to these sets. Its first-class declaration
identity and witness-member reference codec remain prerequisites; this
proposal neither assigns that declaration a kind nor substitutes its
requirement identity for a selected binding. No new value, region, witness
or static-storage codec is selected here.

## Namespace audit and alternatives

The [machine registry](../specs/representation/trrm-1.registry.json) and
[metadata reference rules](../specs/il-metadata.md#table-stream-and-token-encoding)
currently give these separate domains:

| Domain | Current allocation or boundary |
|---|---|
| Registered DefId kinds | `0x13=TypeDef`, `0x14=FieldDef`, `0x16=MethodDef`, `0x19=GenericParam`, `0x1E=PropertyDef`, `0x21=UnionCaseDef` |
| Import/Export definition targets | TypeDef, FieldDef, MethodDef and PropertyDef only; being a DefId kind does not itself make a definition importable/exportable |
| Associated-type candidate | [AssociatedTypeDef `0x20`](associated-type-definition-identity.md), targeting section `0x0027`, with its separately proposed AssociatedTypeReference/Import rules; not an accepted allocation |
| Definition word | Four bytes: eight-bit kind and one-based 24-bit row; `0xFF` remains reserved for future required indirection |
| Typed rows and complete types | A typed row has a zero high byte; TypeSpecRef has its own row/F2/F3 forms. Neither is a DefId alternative |
| Section identifiers | Independent `u16` namespace, not definition tags or a conversion rule |

The base definition tags largely coincide with their section identifiers,
but that is not a universal family rule: UnionCaseDef kind `0x21` targets
section `0x0034`, not NativeImport section `0x0021`. The associated-type
candidate likewise does not target Body section `0x0020`. No normative
DefId family band or automatic section-derived assignment is defined.

Candidates `0x22..0x24` collide with no registered DefId or the existing
`0x20` candidate. Sections `0x0022=NativeExport`, `0x0023=LayoutField` and
`0x0024=InterfaceMethod` are already assigned, but in the **section**
namespace. They remain unchanged and are not these candidates' targets.
No Body-map token, opcode, clause or TypeSpec form is allocated by this
decision. All other unassigned definition kinds remain unassigned, without
new family reservations.

| Alternative | Assessment |
|---|---|
| Continue the extended definition list with `0x22/0x23/0x24` | **Recommended.** Three explicit mappings complete the target-kind decision beside `0x20/0x21`, without imposing a new band or changing unrelated identities. The binding definition remains distinct from its associated declaration. |
| Match existing sections: `0x31=InterfaceConstDef`, `0x33=WitnessAssociatedConst`, with an independent candidate such as `0x34=StaticField` | Equally encodable and not a DefId collision; `0x34` would not mean UnionCaseDef merely because that is section `0x0034`. It still needs explicit mapping and a static section decision. Matching two sections gives no width, capacity or reference-rule advantage. |
| Allocate a larger constant group: `0x22=InterfaceConstDef`, `0x23=AssociatedConstDef`, `0x24=WitnessAssociatedConst`, `0x25=StaticField` | Coherent if the associated-declaration identity/Import codec is being selected too. That is an additional decision, not necessary to assign this closed target set; a fourth tag alone would not complete that codec. Keep it a separate gate rather than silently allocating an incomplete adjacent identity. |
| Reallocate `0x21` to AssociatedConstDef, use `0x22/0x23` for fixed/bound constants, move UnionCaseDef to `0x24` and StaticField to `0x25` | A permitted pre-v1 redesign, not forbidden by draft compatibility. It groups declarations more tightly but changes union references and still needs all declaration/static codecs. With fixed-width tags and no band-dependent semantics, that broader regrouping supplies no missing distinction for this slice. Preserve `0x21` for scope/coherence, not because it is immutable. |
| Use a raw table row, Import token, one shared constant tag or FieldDef's static bit | Does not implement the selected direct, kind-restricted DefId contract. It would need a different reviewed union/identity design, not a fallback reader or an alias for these candidates. |

## Candidate words and exact targets

Every rule below is an **unapproved candidate addition**. Encode a word as
`(kind << 24) | row`, little-endian, with
`1 <= row <= targetTable.count <= 0x00FF_FFFF`. The kind selects exactly one
table before row lookup:

| Candidate kind | Exact table target | Row 1 word / bytes | Import / Export |
|---:|---|---|---|
| `0x22` | InterfaceConstDef, existing required section `0x0031`, 16-byte rows | `0x2200_0001` / `01 00 00 22` | Add to both target sets, subject to the rules below |
| `0x23` | WitnessAssociatedConst, existing required section `0x0033`, 16-byte rows, used as selected binding definitions | `0x2300_0001` / `01 00 00 23` | Add to neither target set; standalone binding Imports/Exports are invalid |
| `0x24` | The distinct logical StaticField table required by the [static-record owner](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending); its physical section remains unassigned | `0x2400_0001` / `01 00 00 24` | Add to both target sets only with the complete static expectation/provider representation |

StaticField's target is not FieldDef, InterfaceMethod section `0x0024`, an
initializer MethodDef, EagerGroup or StaticDependency. A definition-kind
candidate does not allocate its missing table/section. Until that complete
schema is selected and supported, a `0x24` word cannot authorize affected
use, even when an existing table has the same row number.
The separate unapproved
[static persistence proposal](immutable-static-field-persistence.md)
recommends section `0x0055` plus the related group/access/publication records;
the accepted registry remains unassigned until that proposal is reviewed and
adopted.

Retain ImmutableData section `0x0041`, its 12-byte row, zero flags/reserved
fields and complete four-byte `ownerTypeSpec` TypeSpecRef. There is no
`0x1C` Import-token branch in `declarationDefId`. A direct candidate DefId
naming an expected definition follows that definition's required binding;
direct spelling cannot bypass an Import. Zero, zero rows, missing/wrong
tables, out-of-bounds rows, unassigned kinds, section-shaped `0x31/0x33`
words, F2/F3 and reserved `0xFF` words reject before affected use.
Recognizing a proposed kind is not support for its required semantics.

These additions do not widen GenericParam's TypeDef/MethodDef owner set,
Body maps, field references, associated-declaration reference fields or any
other polymorphic position. The independent `0x20` candidate remains an
associated-**type declaration** identity, not a constant, witness binding,
TypeSpec row or ImmutableData target.

## Per-kind ownership and binding

### `0x22`: fixed interface constant

Ownership is
`InterfaceConstDef -> InterfaceDef -> TypeDef -> Module`. Require the exact
nominal interface descriptor, a directly declared row in its exclusive
`interfaceConstFirst/interfaceConstCount` range, and agreement with the
row's `ownerInterfaceDef`. Inherited constants retain their original
declaring owner; they are not copied into a derived interface's range.

An imported expectation is a consumer-local InterfaceConstDef inside the
consumer's expected InterfaceDef. Its Import and that Import's ModuleRef
belong to the same consumer Module. Bind the member and owner separately,
requiring the selected member's declaring interface to be exactly the
selected provider owner. Preserve the expected substituted constant domain,
typed source/value and retained dependencies as contract, not a locally
authored initializer or independent runtime storage.

An Export selects the eligible provider-local constant, not an imported
expectation or a member reparented from an imported owner. Eligibility
requires public effective member access and a public provider owner under
the separately representable access contract described below.

### `0x23`: selected associated-constant binding

The row belongs exclusively to its origin InterfaceWitness's
`associatedConstFirst/associatedConstCount` range, with an agreeing typed
`witnessRow`. Its semantic owner/context is the **selected witness origin**,
not automatically the module of the declaring interface. Nominal origin
authority follows the implementing declaration; primitive witnesses need
their exact admitted privileged registration, not a fabricated TypeDef.

The binding identifies the exact complete implementer, witness interface
instance, inherited declaring interface instance and resolved
AssociatedConstDef requirement. Validate the requirement's substituted
domain and typed value source. The independent codec for
`associatedConstDefId` remains required; neither `0x20`, a raw row nor this
binding's `0x23` becomes its meaning through this proposal.

There is no independent binding export key, Import or conformance search.
Expected/provider correspondence follows the retained selected owner/witness
graph, including origin, substitutions and projection dependencies. It must
select that exact requirement binding, not another equal-valued binding.
The witness-origin/expected-binding representation must make that mapping
unambiguous before use; the existing row's lack of a Module cursor is not
permission to infer the declaring interface's Module.

### `0x24`: static-field definition

Ownership is `StaticField.ownerTypeDef -> TypeDef -> Module`, separately
from instance FieldDef ranges and layout. The expected definition belongs
to the consumer's expected owner TypeDef; member Import and ModuleRef share
that consumer Module. Bind it to the exact selected provider field and owner.
The expectation allocates no independent storage, initializer or
initialization state.

The provider Export names an eligible public local static field with a
public local owner, never an imported field/owner expectation or generated
initializer. The separate imported StaticDependency preserves provider
field/group, trigger/effect and compatibility obligations; it supplements,
not replaces, the shared Import. StaticAccess and its completion-demand or
initialization-phase authority remain use-specific.

A kind-2 ImmutableData use additionally requires the admitted immutable
extent/publication/backing contract. Neither a field definition, a
not-writable flag, a scalar payload nor a completed initialization group
establishes that contract by itself.

### Shared owner, access and selection rules

Preserve the [existing candidate owner rules](immutable-data-declaration-reference.md#exact-owner-substitution):
InterfaceConstDef uses its complete declaring interface instance,
WitnessAssociatedConst its complete origin implementing owner, and
StaticField its complete declaring type construction. `ownerTypeSpec` is
zero exactly when no applicable owner formals exist; otherwise require the
exact complete ordered type/constant construction, consistent with witness
terms and expected/provider formal correspondence. No layout-based owner
substitution or erasure of interface arguments is permitted.

For both importable kinds, use the existing exact Module identity/export-key
selection and complete directed binding comparison. Keys remain nonempty
and Module-wide unique; existing aliases to one provider definition do not
create identities or permit re-export. No name/range-position lookup,
alternate-provider retry, getter evaluation or initializer execution selects
the target. ImmutableData has no Module cursor: all retained using contexts
must resolve the same row meaning, or a rewriter must clone/remap the row.

Effective member/source-unit access remains a publication prerequisite.
InterfaceConstDef has no independent visibility word, and StaticField's
approved candidate `flags=0/1` encodes writability, **not visibility**.
Do not apply the existing TypeDef/FieldDef visibility mask to those words,
assume public access from a containing flag alone, or invent a stub sentinel.
The complete access/static-stub encoding must represent the requirements
above; this proposal does not select that missing encoding.

## Integration boundary and remaining gates

If adopted, integrate these amendments together; none are made by this
document:

| Owner | Candidate amendment |
|---|---|
| [Registry](../specs/representation/trrm-1.registry.json) and [allocation policy](../specs/representation/registry.md#4-admission-status-and-namespace-boundaries) | Add `22/23/24` to `metadata.definition_kinds` with the exact table mappings above; add **only `22/24`** to both `metadata.import_export_target_sets` arrays for this proposal; register the two closed ImmutableData target sets and owner/context rules |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) and [reference verification](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces) | Extend the DefId/Import/Export lists and per-kind owner/stub rules without changing the assigned constant/binding/ImmutableData row strides or admitting these kinds in unrelated positions |
| [Witness records](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain), [static records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending) and [immutable-data records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Replace only the relevant numeric-kind deferrals; retain missing associated-declaration, witness-origin, static-section, access and publication codecs as explicit gates |
| [Binding and replacement](../specs/representation/binding-and-replacement.md) | Preserve exact member/owner or selected witness correspondence, value/publication/load obligations and stronger facts retained after folding or internalizing Imports |
| [Declaration-reference proposal](immutable-data-declaration-reference.md#integration-owners-and-remaining-gates) | Supersede its three-kind numeric-assignment deferral only; all other selected rules and remaining gates stay in force |

Coordinate the separate AssociatedTypeDef proposal without merging its
namespace or replacing its rules: if independently adopted, its `0x20`
kind and Import/Export additions coexist with these additions. In
particular, `0x23` stays excluded from Import/Export, and GenericParam
`0x19`/UnionCaseDef `0x21` do not gain Import/Export eligibility by analogy.

The remaining decisions and acceptance gates are:

1. **Associated declarations and witnesses:** a separate unapproved
   [AssociatedConstDef identity proposal](associated-constant-definition-identity.md)
   now recommends its own `0x25` requirement identity/reference/Import
   codec. It does not complete witness origin/expected-binding,
   inheritance/projection and value-domain checks or witness authority/runtime
   carriers; assigning `0x23` still does not complete those obligations.
2. **Declaration publication:** complete effective access, semantic
   source-unit identity and imported expected-definition encodings. Do not
   erase access, value promises or static initialization obligations to fit
   currently available fields.
3. **Values and capability closure:** complete typed ConstantSource/frozen
   value or retained required-evaluation representations and exact target
   dependencies. The [scalar registration candidate](scalar-frozen-value-registration.md)
   does not admit ImmutableData through its scalar-only proposed `11/1`.
   Select the richer coherent registration/revision and predicate,
   execution, view, witness and static prerequisites explicitly.
4. **Static storage and immutable publication:** the separate unapproved
   [static persistence proposal](immutable-static-field-persistence.md)
   recommends the physical sections and a whole-inline publication carrier.
   It does not complete richer source-unit access, StaticPlace/recovery
   execution, indirect backing or runtime support. Preserve trigger/effect
   ordering, failures/recovery, loaded closed-owner/storage generations,
   sharing, cleanup and unload obligations. No DefId is an address, proof of
   successful initialization or lifetime extension.
5. **Exact schema and implementation:** approve/integrate one amended draft
   and its governing catalog, then implement and qualify producer,
   structural reader, binding/replacement, remapping and lowering paths.
   [Format policy](../specs/representation/format-versioning.md) allows draft
   changes without a version-pair change or compatibility reader; equal
   `(1, 0)` bytes do not select these candidates.

No source production, library privilege, runtime initialization trigger,
native ABI, marshalling rule or consumer Body re-proof obligation changes.
Producers remain responsible for truthful values/publication facts;
consumers check selected structure, references, supported requirements and
binding compatibility before affected use. False published semantic facts
remain distinct from malformed or unsupported metadata.

## Candidate acceptance cases

These are future complete-schema obligations, not tests run or support
claims. Assignment-only fixtures cannot establish value/runtime acceptance.

| Case | Candidate outcome |
|---|---|
| Valid row 1 and maximum admitted row for each `0x22/0x23/0x24` target, with complete prerequisites | Resolve only its mapped table; check actual bounds and checked count/extent arithmetic without claiming a portable resource minimum |
| Same row numbers in NativeExport, LayoutField, InterfaceMethod, constant/binding/static tables and AssociatedTypeDef | No namespace collision or fallback; `0x20` remains the separate type-declaration candidate and is never an ImmutableData constant |
| Kind 1 with `0x24`, kind 2 with `0x22/0x23`, zero/raw/section-shaped/Import/F2/F3/reserved words, absent table or bad stride | Reject before affected use, without masking the kind, probing another table or guessing an older schema |
| Consumer fixed/static expectation plus independent member/owner bindings to one eligible provider; direct word still names the expectation | Follow both bindings and compare complete retained contracts; reject wrong owner/provider/key, imported re-export or unrepresented access/stub semantics |
| Standalone `0x23` Import/Export, ambiguous witness origin, unbound requirement or equal value from another witness | Reject that representation or refuse unsupported affected use; do not synthesize or search for a conformance |
| Nongeneric/generic interface, implementing and static owners; two constructed interface bindings for one implementer | Apply the exact per-kind zero/nonzero owner rule and preserve every selected interface/requirement identity, even for equal values |
| Valid static identity without a publication carrier, or with distinct load/extent/generation dependencies | Identity alone does not enable kind 2, read authority, initialization completion or cross-load reuse |
| Pack/reorder targets, owners, witness/member/Import graphs, TypeSpecs, value sources and ImmutableData uses | Remap decoded typed edges, retain expected and stronger relied-on contracts, clone context-sensitive rows, rebuild ranges/order/indexes/extents and affected source slots; never replace matching bytes in payloads |
| Matching version bytes or a recognized candidate tag without exact schema/capability closure | Refuse affected use; inspection is not conformance, immutable publication or runtime support |
