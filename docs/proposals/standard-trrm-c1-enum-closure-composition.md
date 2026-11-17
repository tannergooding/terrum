# Standard TRRM C1 enum closure composition

**Status: unapproved; provisionally actionable under the user-authorized
missing-contract workflow, pending later review.** This selects an exact
composition and its inputs now, not compiler support, validation or normative
adoption. Existing NominalEnumsDraft and C1 parent agreements are unchanged.

## Required decision and evidence

**Complete class closure needs an enum-bearing C1 agreement.** Removing an
invalid enum manifest entry cannot justify rejecting enum-bearing classes
from the planned scope, dropping their private dependencies or substituting
the enum's integer base.

These are inspected source/contract facts, not executed tests:

| Evidence | Consequence |
|---|---|
| [Class source publication](standard-trrm-c1-class-source-publication.md#required-constructor-and-member-closure) retains complete field, constructor, method, property and witness types, including private and uncalled closure. | A smaller mapping list cannot remove a retained enum obligation. |
| [`Binder.ManagedClasses`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs) binds field types and obtains their layouts through the enum-aware value-layout path; [`SemanticVerifier.IsClassArgumentType`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) admits valid enum types. | Enum fields and constructor/result types are not a separate forbidden class kind. |
| [`BoundEnumTypes`](../../sources/Terrum.CodeAnalysis/Semantics/BoundEnumTypes.cs), [`PrimitiveTypeFacts`](../../sources/Terrum.CodeAnalysis/Semantics/PrimitiveTypeFacts.cs) and [source enum identity](source-nominal-enum-identity-inputs.md) retain source identities, nonpublic/file-local declarations and exact mathematical values over integer bases, including wide/native integers. [Existing wide-source test source](../../tests/Terrum.CodeAnalysis.Tests/Modules/PrivateNominalEnumTests.cs) includes a valid uint64 value above int32 range. | Public-int32-only persistence is not the whole source class closure. Checked projection must not truncate values or export private types. |
| [Nominal enum persistence](nominal-enum-persistence.md#exact-schema-selection-and-feature-composition) selects explicit B/C/F/G enum compositions, not C1's complete I+G+G1/class/construction agreements. | Neither its Feature 18 nor a plausible enum row adds enums to a C1 selector. |
| [CG1](standard-trrm-constant-generic-methods.md#domains-and-frozen-values) supplies a ten-fixed-integer enum delta with typed member-value references, but selects a different whole agreement. | Reuse that enum delta explicitly; do not import CG1's constant terms, Feature 24 or changed generic-call argument tags into C1. |
| [Standard enum source inputs](standard-trrm-source-enum-identity-inputs.md#exact-candidate-api) and [`EmitStandard`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) scope the name-based `EnumMemberDeclarationKeys` input to exact NominalEnumsDraft. | C1 needs a source-anchored enum input, not a broadened legacy property or CLI pairing. |

### Alternatives and recommendation

| Candidate | Disposition |
|---|---|
| C1 plus the original public/int32 enum delta unchanged | Insufficient: it selects no current C1 agreement and cannot retain wider or nonpublic enum dependencies. Not the completion gate. |
| C1 plus all of CG1 | Adds unrelated constant-template/domain records and changes G1 argument semantics. Not selected. |
| **Explicit C1 enum-closure successors, reusing enum rows and the typed member-value delta** | **Selected.** Add only the visibility/closed-private and native-base rules needed below, one source-anchored input and one complete class-owned JSON version. No new enum operation or constant-term language. |
| Change old selectors' meanings or use NominalEnumsDraft providers inside C1 | Not selected. Exact agreement and original provider expectations must survive; no alias, permissive reader or selector-only conversion. |

No accepted text is edited here. The intended integration owners are
[metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics),
[registry](../specs/representation/registry.md),
[manifests](../specs/representation/semantic-manifests.md),
[binding/replacement](../specs/representation/binding-and-replacement.md),
[reader trust](../specs/representation/reader-trust.md) and
[canonicality](../specs/representation/encoding-canonicality.md).
The enum and CG1 proposals remain the owners of the reused rows; only the
explicit delta below applies to these new agreements.

## Exact whole-schema selection

EN1 below is shorthand, never a selector component or independently enabled
Feature. Define exactly these **four new unapproved agreements**:

| Parent, exactly as defined by [R1](standard-trrm-method-receiver-access.md#exact-profile-consequence) / [PI1](standard-trrm-c1-property-initialized-state.md#exact-proposed-agreement-and-allocations) | Exact EN1 identity |
|---|---|
| Construction-1L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1` |
| Construction-2L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1` |
| Construction-1L1R1PI1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1` |
| Construction-2L1R1PI1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1` |

Each is precisely its listed parent plus this document's enum delta and
input applicability. Preserve `(1,0)`, edition 1, Body codec/encoding `01`,
the parent's entire C1-reconciled catalog, constructor I/P/Body closure,
L1 labels, R1 receiver facts and PI1 facts where selected. Construction-1
still does not acquire Construction-2 lifecycle Bodies.

Select one full literal ordinally and case-sensitively for root, **every
supplied provider including unused/declaration-only inputs**, retained origins
and captured Clause/Origin/Plan artifacts before schema-dependent indexing or
decoding. Equal version bytes, a Feature list or a four-byte last member word
cannot distinguish this schema from an older enum encoding. No sniffing,
component parsing, subset retry or schema alias is defined.

NominalEnumsDraft remains exactly `trrm/1-draft-nominal-enums-1`, with its
original public/int32 rows, five-argument/name-based source input and
enum-only CLI option. CG1, the four C1 parents, other enum compositions and
private persistence retain their own meanings. None is an EN1 provider.
A conversion must explicitly decode its source agreement, preserve all
retained facts and rebuild references under EN1; changing a selector or
copying the old member word is not conversion. No such conversion is required
for this source-publication completion.

## Enum records, catalog and values

Reuse the Standard enum delta's TypeDef kind 4, EnumDef `0053`, EnumMemberDef
`0054`, nominal TypeSpec forms `02/03` and typed frozen-leaf family. Apply
[CG1's enum-only replacement](standard-trrm-constant-generic-methods.md#domains-and-frozen-values)
for member values. The exact EN1 rows are little-endian:

```text
EnumDef, 20 bytes:
    u32 ownerTypeDef, TypeSpecRef underlyingType, u32 flags,
    u32 memberFirst, u32 memberCount

EnumMemberDef, 20 bytes:
    u32 ownerTypeDef, u32 ordinal, str declarationKey, str name,
    u32 valueSourceRef
```

For EN1 only, the final `0x0054` word deliberately replaces the
NominalEnumsDraft `i32 value` with a typed `ConstantSource` row reference.
This is not a compatible interpretation or fallback: the exact EN1 identity
selects it, while NominalEnumsDraft retains its existing row meaning.

`valueSourceRef` is a nonzero typed ConstantSource row, tag **1 only**,
selecting a kind-1 FrozenValueNode of **that exact unqualified enum T**.
It is not inline i32, a Body-map index, member token or base-integer value.
The node's flags/reserved/child/case fields are zero; its required Blob has
exactly the declared base's width. Use existing source/node/Blob framing and
checked one-based `1..00FF_FFFF` references with zero high bytes.

| Base catalog in EN1 | Exact Blob interpretation |
|---|---|
| Atomic `06..0A`: int8/16/32/64/128 | 1/2/4/8/16-byte two's-complement little-endian |
| Atomic `0B..0F`: uint8/16/32/64/128 | 1/2/4/8/16-byte unsigned little-endian |
| Atomic `10/11`: nint/nuint | Exactly eight bytes under the explicit Windows x64 binding below; signed/unsigned respectively |

`underlyingType` resolves to one unqualified built-in in this table, never
bool, char, float, another enum, wrapper or formal. Range-check the source
mathematical integer before encoding; no truncation, wrapping or host-width
conversion. Member values and ordinary unnamed values have the same complete
`(T, mathematical integer)` identity. Duplicate-valued members remain distinct
declarations. Share typed roots only for equal complete T and bits; identical
Blob bytes may be shared across different typed roots.

Field/argument storage and the selected existing internal call carrier
derive from that exact base/target while the logical type remains T.
An empty enum field range is not a zero-size Auto struct. Generalize retained
field/call/value/predicate paths for these complete types; do not erase a
declaration's identity to obtain scalar lowering or infer foreign-boundary
permission from equal storage.

Require exactly one EnumDef per retained kind-4 TypeDef, including expected
proxies, and exclusive complete member ranges with ordinals `0..count-1`.
Keep the enum owner's exact name/order/Flags facts, empty field/formal/
Contract ranges, empty Auto default Layout and actual target-specific
Blittable commitments. Members have keys unique **within T**, not Module
exports or DefIds. They may equal another enum's member key or a Module
ExportKey. No FieldMap/Import/Export target set gains an enum-member token.

### Allocation and catalog reconciliation

TypeDef stays 44 bytes. Kind is now `(flags >> 2) & 7`, with struct/class/
interface/enum values `1/2/3/4`; zero and `5..7` are invalid. Combined permitted
mask is **`0x9F`**, with `0x80` still **class-only** and enum visibility using
bits 0..1. Bits 5, 6 and 8..31 stay zero. Public enum flags are `0x12`,
internal `0x11`, closed provider-private `0x10`; visibility 3 rejects.
Do not confuse enum kind bit `0x10` with PI1's FieldDef backing flag.

Reuse candidate **18/1 NominalEnums**, governing edition 1, with immediate
prerequisite **11/1**. Its EN1 scope is the complete enum contract here,
not the older int32-only registration. That scoped draft amendment applies
only in these exact new agreements. Preserve C1's `11 -> []`,
`17 -> [(10,1)]`, `12 -> [(11,1),(17,1)]`, `23 -> []` and all conditional
1/13/25/26 requirements, including 1/1 for an actual Blittable Layout
commitment. Every using Module retaining an enum declaration,
nested type, member or value requires 18/11, including uncalled/declaration-only
uses. Enum literals in G graphs require their own graph closure as well.

This adds no section, DefId, opcode, Body record, prefix/escape or width.
Existing `0053/0054` are reinterpreted only through the selected exact EN1
agreement; the candidate allocation family and 20-byte strides are reused.
Feature rows do not select the `0x0054` value word's decoding; the exact
schema does, and EN1 requires `11/1` for the typed frozen-value references.
No Feature 24, ConstantSource tags 4/5, sections `0064/0065`, CG1 argument-tag
change, new enum operator or general ConstantExecution support is composed.
The ordinary SF1 scalar-only registration remains unchanged.

### Native-width enum binding

For T based on nint/nuint, require its declaring Module's target requirement
**1**, exact `windows-x64-native/1` profile **(1,1)**, and a complete Auto
Layout variant for that profile with no fields/alignment/packing override.
Its Blittable bit records the actual commitment, not an automatic consequence
of the base. Every using Module and relevant ModuleRef retaining T must
select the same target requirement/profile. Target-neutral or differently
bound native-width enum input refuses before use.

This is the explicit binding for this **nominal enum** value codec: width
comes from the checked enum owner and exact target, never the host or Blob
length. It does not register standalone SF1 nint/nuint roots, replace them
with int64/uint64, add a per-value target override or waive independently
retained query/evaluation dependencies. Fixed-width enum declarations retain
their parent's target flexibility.

## Nonpublic closure and nominal origins

EN1 deliberately extends the original public-only enum carrier to complete
namespace/file-level source enum closure, without changing source access:

* Public enums may have zero or more explicit TypeDef Export aliases.
  Internal and file-local enums are closure-only, with **no** Export keys.
  A public-but-unmapped enum can likewise remain unpublished.
* Preserve internal visibility. A file-local enum is retained as a closed,
  provider-private TypeDef, never a source lookup root or a grant to open the
  original source unit. Original source access is producer-checked; consuming
  its typed value does not grant source-name access. Optional debug paths do
  not supply permission.
* Distinct file-local enums with equal qualified names remain distinct owned
  definitions. For these closed private enum rows, equal namespace/name is
  not a uniqueness or interning key; direct typed references and immutable
  provider origins distinguish them. This is an EN1-only allowance, not a
  duplicate-name rule for exported/public lookup roots.
* Preserve every actual source/member handle during capture. On loading,
  retain a typed Standard enum origin: exact schema, immutable artifact
  snapshot, logical Module, typed definition, selected aliases or closed
  private origin, original expected declaration and provider context.
  Use the [Standard bound enum carrier](#standard-bound-enum-carrier) below,
  not an int32, fabricated struct, Source handle or fabricated Core designation.

Across artifacts, use checked TypeDef Imports/Exports where selected.
Closed private definitions stay in their retained/packed provider closure;
never invent a private export or write an external row as a local row.
Replacement needs checked nominal correspondence for a private origin or
must preserve that origin/refuse the replacement, as C1 already requires.
Source-free consumers need no enum source file, hash or name-based rebinding.

## Standard bound enum carrier

**Unapproved EN1-local completion; provisionally actionable under the same
authorized missing-contract workflow.** A minimal amendment here is sufficient:
the missing choice is one internal enum carrier, not another schema or a
general nominal-identity redesign. This section owns that carrier; the
[source identity](source-nominal-enum-identity-inputs.md),
[private identity](private-105-enum-bound-identity.md) and
[Core provider](core-provider-module-origin-carrier.md) proposals keep their
separate ownership, publication and designation rules.

Inspected code, not execution evidence:

| Owner | Consequence |
|---|---|
| [`BoundEnumTypes`](../../sources/Terrum.CodeAnalysis/Semantics/BoundEnumTypes.cs) | Declaration identities are Core/Private/Source; member `OwnerKey` and exact `BigInteger` values already exist. Table validation still requires non-Source declarations/members to be public and Private bases to be int32. Adding an identity case alone is insufficient. |
| [`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`generic nominal types`](../../sources/Terrum.CodeAnalysis/Semantics/BoundGenericNominalTypes.cs), [`Standard class declarations`](../../sources/Terrum.CodeAnalysis/Semantics/BoundStandardClassDeclarations.cs) | Reuse `BoundSourceTypeProviderSnapshot` and `BoundSourceTypeExpectation`. `BoundStandardTypeOrigin.ExpectedDeclaration` is a `BoundStruct`; generic definition validation admits kinds 1..3. Neither is an enum payload. The separate class view/expectation pattern supplies a suitable precedent without requiring either carrier to accept fabricated enum data. |
| [`Enum loader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NominalEnumsReader.cs), [`provider validator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmEnumProviderBindingValidator.cs), [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | The existing loader constructs Core-shaped Module/key enum identities and requires an Export or Import; the validator selects NominalEnumsDraft. `AddBoundEnumDefinition` adds all imported enum names to lookup. These paths cannot implement closed-private EN1 origins unchanged. |
| [`Fixed-array provider binding`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs) | `ResolveImport` returns the immediate exported provider TypeDef and `ValidateC1M1TypeDefImports` retains those direct bindings across Modules. A provider-local row is not by itself proof that the row is an owned declaration rather than an imported expected proxy. |

### Selected internal shape and owner

In `Semantics/BoundEnumTypes.cs`, add a disjoint
`BoundEnumDeclarationIdentity.Standard(BoundStandardEnumOccurrence Value)`.
Use an enum-specific immutable occurrence, not an optional enum field on the
struct origin or a kind-4 `BoundGenericNominalTypeDefinition`. The concrete
logical shapes are:

```text
BoundStandardEnumTypeSite(Snapshot, DefinitionId)
BoundStandardEnumView(Site, Namespace, Name, TypeDefFlags,
                      UnderlyingType, EnumFlags, Layouts, Members)
BoundStandardEnumExpectation(Binding, ExpectedDeclaration)
BoundStandardEnumOccurrence(Declaration, OriginalDeclarations, Expectations,
                            ExportKeys, SelectedExportKeys)

enum identity   = Standard(occurrence)
member identity = OwnerKey(Standard(occurrence), memberDeclarationKey)
```

`Snapshot` is the existing `BoundSourceTypeProviderSnapshot`; `Binding` is
the existing `BoundSourceTypeExpectation`. All collections are nondefault
immutable arrays. `OriginalDeclarations` is nonempty; the others may be
empty subject to the rules below. A view contains:

* A checked snapshot-local TypeDef DefId (`0x13`, nonzero 24-bit row) and its
  actual owning logical Module, not an enum-table index or bare provider
  string. The snapshot owns copied immutable artifact bytes, exact schema,
  Module row/identity, governing edition/Features, target/runtime requirements
  and decoded target profiles. No source tree, debug path, source hash or
  Core registration is required to construct it.
* Separate namespace/name, complete TypeDef visibility/kind flags, exact
  built-in underlying type and EnumDef flags. `Layouts` retains every
  default/exact-profile variant's decoded profile identity/revision, kind,
  flags, alignment and packing; its field range is empty. Do not reduce this
  to the current single `BlittableTargetRequirement` field.
* `Members` is the complete ordered array of
  `(DeclarationKey: string, Name: string, Value: BigInteger)`; position is the
  checked zero-based ordinal. Owner identity comes from the enclosing site.
  Keys/names obey EN1's per-owner uniqueness and exact scalar-text rules;
  keys are never Module member exports. Range-check values against the exact
  base and native-target binding without narrowing.

`Declaration` is the currently selected **owned actual** view.
`OriginalDeclarations` retains every original expected view, including the
projection of the original checked source declaration at first publication,
the initially selected direct reference view, and each imported expected
stub in its own using snapshot. These are source-free enum snapshots, not
reconstructed Source handles. Views may share immutable storage; an actual
provider or replacement never overwrites an original view. The ordinary
`BoundEnumDefinition` remains the typed enum payload in the program/reference
enum table, with this Standard identity and facts agreeing with `Declaration`.
Its members use the existing `OwnerKey`, agree with `DeclaringEnum`, and
inherit the owner's access. Complete composed declarations have `IsPrimary`
true; this does not deserialize original partial-source units.
For Standard declarations the legacy single-edge `ModuleImport` must be null:
all import provenance belongs to `Expectations`. Its absence never establishes
local ownership.

The occurrence is a compiler-created sealed immutable class with reference
equality. Construct it only after validating its views/edges, then construct
the bound declaration and member identities. Direct carrier equality is not
cross-artifact nominal equality. Resolve/check the binding graph before
interning one bound enum type for aliases or repeated paths; a merged
occurrence retains the union of original views and edges. A second read or a
different context-local `PrimitiveType` number does not create another T.
Snapshots identify provenance/revisions, not a new nominal type constructor
or provider authentication.

### Publication, lookup and invalid states

`ExportKeys` captures the selected actual declaration's complete retained
publication aliases. `SelectedExportKeys` is its distinct explicitly selected
source-lookup-root subset; derive `IsLookupRoot` from that subset being
nonempty. A closure Import's key lives in its expectation edge and does not
itself select a lookup root.

For EN1 only, the existing four-argument `CreateStandardReference` TypeDef-key
selection accepts public owned enum keys alongside the parent's class roots.
An enum-only selection requires its complete declaration closure, not a class
constructor or function export. Distinct requested aliases selecting one enum
produce one lookup type and retain every selection. This changes applicability,
not the overload, and leaves each parent/legacy reference route unchanged.

Only public owned enums can publish aliases or become lookup roots. Public
unmapped, internal and closed-private/file-local enums can have no aliases
and remain closure-only; internal/private enums must have both key arrays
empty. Their positive origin is their owned site in the retained provider
closure. Equal qualified names, members and bits do not merge two such
definitions. File-local source access is projected to closed-private
visibility, not reconstructed lexical access. Do not add any closure-only
enum to ordinary source-name lookup, including an unselected public enum.
Selected aliases expose the declaration's original name, not new source names.

Reject a missing/default snapshot or collection, wrong schema, out-of-range
or wrong-kind site, mismatched owning Module, actual view naming an imported
proxy, inconsistent bound payload/member owner, illegal visibility/flags,
incomplete layouts/member ranges, invalid value width or native binding,
duplicate per-owner keys/names, missing original view, or conflicting actual
selections. Also reject a selected alias absent from the actual public Export
map, aliases resolving to different actual definitions, and any nonpublic
export/lookup root. Preserve the existing Core/Private/Source validity rules;
relax their public/int32 restrictions **only for this Standard EN1 case**.
No default Core token, private occurrence or source handle repairs an invalid
Standard occurrence.
Every view's facts and the snapshot's manifest/context fields must agree with
their own selected immutable bytes, not just with each other. A copied record
with changed facts is not a newly checked origin.

### Expected/provider relation and replacement

For each retained Import, `BoundStandardEnumExpectation.ExpectedDeclaration`
is the original view at `Binding.UsingModule/ExpectedDefinitionId`, and must
be present in `OriginalDeclarations`. Validate its Import row and ModuleRef
ownership in that snapshot. Resolve `ProducerIdentity/ExportKey` to the exact
public enum at `Binding.Provider/ProviderDefinitionId`, preserving this
**immediate** provider site in `Binding`. Check the original ModuleRef
target/runtime pair against that provider and independently apply the parent's
using-Module compatibility rules.

**EN1 disallows enum proxy re-exports**, preserving
[binding's owned-export rule](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities)
and the C1 publication parent's rejection of proxy re-exports. For every enum
Export in the admitted closure, including unselected/declaration-only exports,
require a public TypeDef owned by its exporting Module that is **not the
expected definition of any Import in that Module**. Apply the same ownership
check to explicitly selected enum lookup roots. Checking the TypeDef's Module
range alone is insufficient. Reject an exported proxy even if its own Import
resolves, all enum snapshots agree, or it is co-located with the owner.
Do not follow it as a forwarding alias or flatten it into a new binding.

Consequently, an admitted enum Import's immediate provider site is also its
**terminal owned** site and must equal the occurrence's `Declaration.Site`.
No extra terminal-site field is needed under this selected rule. Keep every
direct Import/ModuleRef edge and its original expected/provider snapshots and
row spaces; never retarget `Binding` or replace its original expectation with
a downstream view. A direct binding returned by the existing validator is
not sufficient ownership evidence without the check above.

Expected and actual sites normally belong to **different Modules/artifacts
and row spaces**. Do not require using/provider Module or DefId equality,
read the expected view in the provider snapshot, require all outgoing edges
to use the root's provider, or copy a provider's requirements over an
expectation. Reuse snapshot/edge shapes, not a generic-definition validator's
additional restrictions. Provider-owned imports have their own occurrences
and using contexts; retain every chain/diamond edge, alias equality and
stronger relied-on fact, including declaration-only/uncalled uses.

Here, chain/diamond acceptance means **owned-declaration dependency graphs**,
not re-export chains of one enum. For example, A imports B's owned class,
whose closure imports C's owned enum; a diamond imports B/C-owned classes
which each retain their own Import to D's owned enum. The class edges remain
in the parent closure; both enum edges retain their distinct using snapshots
in D's enum occurrence. Selecting B's exported expected proxy for C's enum
instead rejects. This distinction preserves the stated chain/diamond gates
without permitting proxy exports.

Within one admitted binding context, intern aliases and repeated dependency
paths by their checked terminal owned site (exact snapshot/schema, logical
Module and TypeDef), never by the Import's expected site, key text or path.
All paths to D's one owned enum yield one nominal T only after **every** direct
expectation passes; retain their union of original views and edges. Different
owned definitions stay distinct despite equal shape or spelling. A changed
provider snapshot requires the replacement checks below, not row-based reuse.

After nominal resolution, compare each complete original enum contract
exactly under EN1: namespace/name, visibility, base/native binding, Flags,
complete decoded Layout variants, and every member's key/name/ordinal/exact
value. Raw rows or equal scalar carriers are not correspondence. Check all
originals before interning and again for a changed binding context.

Public replacement resolves all retained aliases in the new selected
snapshot and preserves their same-definition relationships and every original
expectation; it need not preserve physical row numbers or byte identity.
Closed-private correspondence is narrower: retain the original owned site,
or use the exact old-site-to-new-site map produced by a checked
meaning-preserving rewrite of that retained provider closure. Validate
ownership, one-to-one nominal correspondence and complete contracts before
rebasing uses. An arbitrary host map, matching name/shape, unchanged row in
different bytes or a new private export is not proof. A separately authored
replacement without that correspondence must preserve the original private
closure or refuse; this amendment adds no stable private key/selector API.

### Propagation and persistence boundary

Fresh source capture keeps the original checked declaration/member handles
and exact program association until the EN1 projection succeeds; it does not
mutate Source identities into Standard identities. Preserve the resulting
typed occurrence through `CompilationReference`, `BoundProgram`, Binder
lookup/type-table composition, shared traversal/rewriting, mandatory enum
verification, class/member/type/value/graph closure and lowering. A new
composition/remap creates checked occurrences and rebases all owner/member
and contextual type associations together; it cannot reuse a stale local
type number or silently drop originals.

Admit this Standard case only under one of the **four complete EN1 literals
above**, with every supplied/captured snapshot selecting that same literal
before schema-dependent access. Feature 18, typed member values or equal
version bytes do not select it. This adds no schema, wire field, allocation,
host publication API or CLI grammar. NominalEnumsDraft, CG1, their source
inputs and the four C1 parents keep their meanings; this is not a retrofit
of the older loader's Core-shaped reconstruction.

Standard persistence uses EN1's existing owned enum rows, ordinary expected
TypeDef/EnumDef/member snapshots and checked Import/Export/ModuleRef edges.
Re-emission rebuilds these and every affected TypeSpec, typed value reference
and Body/graph map under their owning contexts; retain separate expectation
stubs where their original views differ. Private direct edges require the
retained/packed provider closure, never a consumer-owned copy. Reconstruct
occurrences from the newly admitted bytes on source-free loading; the
in-memory occurrence and its backing snapshots are not serialized as opaque
host authorization or a new identity directory. If existing rows cannot
preserve a retained obligation, refuse publication rather than erase it.

**Private semantic/native-container persistence of this Standard alternative
is excluded.** It does not become a private-105 enum or acquire that codec's
int32/public domain. Existing private/development writers must refuse retained
Standard occurrences through their existing unsupported-contract channel;
no token fabrication, narrowing or implicit Standard-to-private conversion.
Native lowering consumes verified typed facts under the separately selected
parent target/runtime route; an emitted image is not carrier round-trip
evidence. Argument, invalid-data and unsupported failures keep this proposal's
existing categories; failure publishes no partial reference or artifact.

Affected implementation surfaces are the enum identity/table verifier,
source capture, enum loading/writing/provider comparison, Standard
type/class/reference-set composition and re-emission, contextual traversal
and type/value remapping, and enum-aware layout/lowering. Reuse the existing
snapshot and member-key machinery; no whole generic/Core carrier migration
is a prerequisite.

Additional carrier acceptance cases, **required tests, not results**; exercise
public compiler APIs and independent artifact fixtures:

| Family | Required gate |
|---|---|
| Positive/source-free | Publish a source class using public aliased, internal and file-local enums; load/re-emit/reconsume after discarding source/debug/Core inputs. Preserve the original declaration, all member keys, uint64-above-int32/128-bit values and target-bound nint/nuint through actual class signatures/Bodies and typed constants. |
| Lookup/identity | Two equal-named file-local enums with reused member keys and equal values remain distinct. Explicit enum-only and mixed class/enum TypeDef-root selections succeed; both alias selections of one public enum denote one T. Unselected public/internal/private closure never becomes a source lookup root; a private value's type does not authorize its source name. |
| Invalid carriers | Mutate independent fixtures' site kind/owner, expected/actual context, member owner/key/value, layout/native binding or alias targets; reject. Mutating caller-owned input bytes after capture must not change retained origins. Exercise empty enums and default-versus-empty arrays through their public input boundaries. |
| Cross-provider | Use non-first providers and multiple logical Modules per artifact. Accept A importing B's owned class with a C-owned enum dependency, and a diamond through B/C-owned classes each importing D's one owned enum: intern D once while retaining both direct edges and their expected/provider row spaces. Different physical expected/actual rows with equal complete contracts pass; coincident rows in unrelated Modules do not. Reject an exported expected proxy, including an unselected export, selected lookup root or otherwise matching A-to-B-proxy-to-C chain; also reject a conflicting later diamond expectation or unused wrong-schema provider after an earlier edge passed. |
| Replacement/remapping | Reorder Module/TypeDef/enum/member/value rows and rebuild all references, then source-free reconsume and replace. Public aliases must stay unified; a later incompatible replacement still fails against the original view. A checked retained-private remap passes; same-shaped fresh private definitions without correspondence, alias splits, changed base/member/order/Flags/layout/target or lost originals fail. |
| Exact/persistence boundary | Cover all four EN1 selectors; reject each parent, NominalEnumsDraft, CG1 and private selection as an EN1 origin/provider, including enum-free inputs. An old/private writer cannot persist the Standard case by casting, rekeying or narrowing. |

No carrier choice is deferred within this provisional direction. Formal
approval/normative adoption and the implementation/qualification gates remain
open; this documentation amendment claims none of them.

## Source-anchored API and joint capture

Add one candidate input record and a required-eight-argument overload in
`Terrum.CodeAnalysis.Modules`; existing signatures remain unchanged:

```csharp
public sealed record TrRmSourceEnumDeclarationExport(
    BoundProgram Program,
    int SourceInputOrdinal,
    TextSpan DeclarationSpan,
    ImmutableArray<string> ExportKeys,
    ImmutableArray<string> MemberDeclarationKeys);

public static byte[] EmitStandard(
    BoundProgram program,
    string moduleIdentity,
    string schemaIdentity,
    uint moduleTargetRequirement,
    IReadOnlyList<TrRmSourceTypeExport> typeExports,
    IReadOnlyList<TrRmSourceGenericMethodExport> genericMethodExports,
    IReadOnlyList<TrRmSourceClassExport> classExports,
    IReadOnlyList<TrRmSourceEnumDeclarationExport> enumExports);
```

This source route requires one of the four exact EN1 identities even for
four empty lists. `typeExports` still selects **structs/interfaces only**;
its `EnumMemberDeclarationKeys` is always default. Do not extend the legacy
name/arity enum input or the seven-argument parent route.

Require the exact checked program/source association, zero-based original
input ordinal and exact full primary EnumDeclaration `Span`, with checked
nonnegative start, positive length and end within its immutable UTF-16
snapshot. Resolve its complete checked partial composition and original
compiler-file/source member order, not primary-first order. No secondary,
imported/Core/private-artifact declaration, copy/rebind, same-name or
same-value substitute acquires fresh-source eligibility.

Require **one record per fresh retained source enum**, including unused
source declarations; no duplicate selectors. Both arrays are nondefault,
nonnull-entry immutable captures. `ExportKeys: []` means no publication;
nonempty aliases require public source access. `MemberDeclarationKeys`
has exactly one explicit key per actual composed member, including `[]`
for an empty enum. Positions associate actual handles; they generate no
identity. Capture base, visibility/source-unit restrictions, Flags, target
commitments and exact member values from checked facts, not caller overrides.

Snapshot all four lists before processing. Check Module-wide ExportKey
uniqueness across type/field/requirement, generic, class/member/P and enum
**ExportKeys**, seeded by existing publications. Check enum member keys
separately per owner, never in that Module-wide set. In particular,
`SourceClassExports.Capture` iterating the **legacy** enum property does not
define EN1 member identity: that property remains default on this route.

Bind once and make one eight-argument emission, carrying the resolved checked
associations through helpers/rewrites. An empty enum list supplies no fresh
enum keys; it does not prune required enum closure or select the parent
seven-argument writer. Imported/loaded publications retain their original
aliases/member associations and expectations; new source records cannot
rekey them. Source-free re-emission consumes retained typed facts through its
admitted source-free route, not a fresh-source map or a retry after refusal.

## Complete class-owned version-2 CLI transport

Use the **same distinct** `compile-trrm --trrm-source-class-exports <path>`
option, with a second fully specified grammar **now**, not a deferred
follow-up. Version 1 remains exactly the
[non-enum class transport](standard-trrm-c1-class-source-cli-transport.md)
and pairs only with its four parent identities. Version **2** pairs only
with the four exact EN1 identities above.

The complete root is exactly `schemaVersion`, `sources`, `typeExports`,
`genericMethodExports`, `classExports`, **`enumExports`**, all required and
nonnull. Existing three mapping arrays have precisely version 1's non-enum
shapes. An enum entry has exactly `sourceInputOrdinal`, `declarationStart`,
`declarationLength`, `exportKeys`, `memberDeclarationKeys`; there are no
namespace/name/arity/base/value/access/Flags/target overrides. For example,
these are placeholder selectors/digests, not a validated source fixture:

```json
{
  "schemaVersion": 2,
  "sources": [
    {
      "path": "provider.terrum",
      "snapshotSha256": "0000000000000000000000000000000000000000000000000000000000000000"
    }
  ],
  "typeExports": [],
  "genericMethodExports": [],
  "classExports": [],
  "enumExports": [
    {
      "sourceInputOrdinal": 0,
      "declarationStart": 0,
      "declarationLength": 80,
      "exportKeys": ["type.mode", "type.mode.alias"],
      "memberDeclarationKeys": ["member.first", "member.also-first"]
    }
  ]
}
```

Apply the version-1 strict UTF-8/JSON rules, exact integer tokens, text/key
capacities, depth 128, alias preflight, entire **one ordered source vector**
and UTF-16LE snapshot hashes to all four lists. `exportKeys`/member-key arrays
have the same scalar-text and per-key UTF-8 capacity rules. Count module
exports and each enum's member range against their actual table capacities
with checked aggregate arithmetic; member keys do not consume Export rows.

All arrays may be empty only subject to retained coverage. Reject
`definitionKind: "enum"` or `enumMemberDeclarationKeys` in `typeExports`
under **both** versions. Version 1 rejects `enumExports`; version 2 requires
it, even empty. Recognize the eight exact option/schema candidates before
content reads, then enforce the exact grammar/schema pair after JSON version
decoding. No empty-list inference or grammar retry.

Keep required identity/output/runtime/arithmetic/library behavior, target 1
on this CLI route, configuration order, output safety and source/Core gates.
Reject combining this option with existing enum/type/generic mapping
options. In particular, `--trrm-source-enum-exports` stays exact
NominalEnumsDraft-only, and G1 version-1/version-3 maps remain G1-only.
The integer `2` here does not select CG1's unrelated grammar or emitter.
No source-provider, artifact-command whitelist or machine-protocol extension
is added.

After one successful binding, pass the four captured lists, actual Module
identity, exact EN1 literal and target 1 to the new eight-argument source
overload. Validate all required class/member/Body/witness and phase contexts,
not just enum rows. No separately produced enum fragment is merged by
guessed rows, and no output is replaced before the whole artifact validates.

## Binding, rewriting, failures and acceptance

Resolve Module/revision/key/kind and actual nominal T before enum/member
comparison. Retain each original expectation separately from its selected
provider. Compare complete base and target binding, access/Flags/Layout and
ordered member key/name/ordinal/exact-value snapshots, including aliases and
all independent diamond expectations. Same width/bits, row number or source
name is not correspondence. Check nested enum uses in every field, signature,
property, graph, dependency and witness term; class metadata still supplies
no object geometry.

Rebuild TypeDef/EnumDef/member ranges, Imports/Exports, Layouts/profiles,
TypeSpecs, MethodSigs, contracts/graphs, value sources/nodes/Blobs and every
Body map with their actual reference namespaces. Remap the member's
`valueSourceRef`, not its Blob bytes. The nominal back-edge from a member's
typed leaf to its owning enum is legal; it is not permission for cyclic
frozen child graphs. Preserve expected/actual origins, I/P relationships,
PI1 backing, label vectors and separate phase-1/phase-2 plan contexts.
Never restamp NominalEnumsDraft, CG1 or a C1 parent snapshot.

Use existing argument/invalid-data/unsupported families. Null/default arrays,
bad fresh selectors, malformed/duplicate keys, wrong member counts, duplicate
enum records, nonpublic export attempts and bad program anchors are mapping
argument errors attributed to `enumExports` (or the affected existing list).
Missing required enum captures or unsupported valid retained use refuses
complete publication; malformed serialized values/owners/bindings use
`InvalidDataException`. Resource refusal is explicit, never partial success.

CLI option/unknown-schema errors remain `TRM2000`/2 before content reads;
JSON/version-pair/hash/selector/key errors are `TRM2001`/2; path aliases are
`TRM2002`/2; source diagnostics and unsupported/incomplete publication retain
their existing exit-1 boundary. Preserve existing `TRM5001` for the latter.
Do not relabel unrelated program exceptions. Recheck all input/configuration
versus output aliases and use the existing complete-image staged publication:
prepublication failures preserve existing output bytes and leave absent
output absent. No worker discovers support and no false success artifact is
returned.

Required focused cases, **not results**:

| Family | Required gate |
|---|---|
| Complete source closure | A class has an enum field, constructor formal, result/property and real uncalled member Body; combine nonempty type/interface, generic, class and enum lists. Exercise all ten fixed bases, public/internal/file-local dependencies, empty and duplicate-valued enums, uint64 above int32, 128-bit extrema and ordinary unnamed values. Retain actual Bodies and nominal identity, not only declaration counts. |
| Target and privacy | nint/nuint enums succeed with exact target-1/profile-(1,1) binding and eight-byte values; target-neutral/mismatched owners/using Modules or Blob widths reject. Standalone SF1 native-width roots remain unsupported. Distinct equal-named file-local enums stay distinct and unexported; source-free lookup never gains file authority. |
| Inputs and keys | Exact primary/partial/member order, stale/copied programs, wrong owner/spans, all-input hashes, aliases in one ExportKeys array, default versus empty arrays, missing/extra member keys and nonpublic export attempts. Same member keys across enum owners or equal to Module exports pass; duplicate keys within one enum or duplicate Module exports fail. |
| Exact selection | Test every EN1 literal, including unused providers and enum-free declaration-only providers; reject every parent, NominalEnumsDraft, CG1, old enum composition/private input and wrong version/schema pair. Version-1 type-kind/vector rejection remains. No guessed decoder, promotion or selector-only conversion. |
| Structural/value preservation | Check flag/kind legality, owner/range/source/node/type references, every width/sign endpoint and reserved field. Reorder physical rows, legally share typed roots/Blobs, pack providers and reconsume without source/debug/maps; wrong enum T with equal bits fails. |
| Replacement/output | Preserve separate expected snapshots and alias equalities through non-first providers, chains/diamonds and class/private-enum closure. Changed base, binding, member key/order/value, Flags/access or lost private origin fails. Exercise existing/absent output preservation for every failure family. |

Implementation must generalize checked enum carriers and all class/value/
provider paths together; old public/int32 helpers do not satisfy these gates.
Native execution additionally needs the parent's independently selected
runtime/target support and headless qualification; storage width is not ABI
or execution evidence. No input/composition decision in this recommendation
is deferred, and no narrower enum-free/public-int32 slice is called complete.
