# Source interfaces and witnesses in private persistence

> [!IMPORTANT]
> **Unapproved proposal; provisionally actionable under the authorized
> missing-contract workflow.** The governing user direction is:
>
> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.
>
> This document changes no implementation, normative chapter, registry or
> support claim. Provisional direction is not formal approval or format
> stabilization. No build, execution or independent-provider result is claimed.

## Decision and scope

**Reuse the assigned Standard metadata row payloads as a typed component of
one new exact private schema, candidate structured-module revision 98.**
Do not invent a parallel name-based interface serializer, embed an allegedly
complete Standard `.trrm` artifact, or add interfaces to an unspecified
interpretation of revision 97.

The out-of-band whole-schema identity is
**`terrum/private-semantic-98-interface-witness-1`**, abbreviated **P98-I1**.
It comprises the complete checked-reference revision-97 composition below
and one mandatory private interface graph. The graph reuses Standard
declaration, complete-type, formal, contract and witness row payloads, with
the exact private additions specified here. It has its own framing and
admission; it is **not** a Standard TRRM profile or an independently
interpreted `.trrm` payload.

This closes the private transport decision for:

* Source interface declarations with explicit nominal owners, ordered
  type/constant formals, bases, requirement identities and associated members.
* Complete **closed declared witnesses**, including inherited requirements,
  selected authored implementations/defaults and associated bindings.
* Original declaration/witness expectations, closed callable instantiations
  and their private implementation links, without source or body inspection.

The initial component is deliberately metadata-first. It does not select
open conformance templates, qualified-self projection records, existential
value evidence, a dynamic interface object/dictionary ABI or new native
cross-image value modes. An admitted declaration is not an executable
default body. Unrepresented required source contracts reject transport or
affected use; they are never normalized to an empty contract.

## Owners and inspected boundary

| Owner | Contract retained |
|---|---|
| [Interface witnesses](../specs/language/interface-witnesses.md), [complete types](../specs/language/types-and-normalization.md), [constant generics](../specs/language/constant-generics.md) | Nominal conformance authority; exact declaring-member instances; forward type/constant substitution; associated selection; inherited/default resolution; complete qualification and formal domains. |
| [Names and binding](../specs/language/names-and-binding.md), [calls](../specs/language/calls-and-inference.md), [grammar](../specs/grammar-spec.md) | Source declaration and lookup rules, caller-facing labels/defaults, argument mapping and generic method correspondence. No source production changes. |
| [Dependencies](../specs/language/dependencies-and-views.md), [references](../specs/language/references.md), [ownership](../specs/language/ownership-and-cleanup.md), [lifetimes](../specs/lifetimes.md) | Whole-source versus backing-only permissions, exact init destinations, transfers, holders, captures, roots, retirement and invalidation endpoints. |
| [Logical witness records](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain), [metadata](../specs/il-metadata.md#section-kinds-and-row-layouts), [registry](../specs/representation/registry.md) | Assigned row families and reference spaces; missing Standard capability, projection, access and execution carriers remain separate. |
| [Contract clauses](../specs/representation/contract-clauses.md), [binding/replacement](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md) | Complete directed satisfaction, original expectations and stronger relied-on facts; structural safe consumption versus producer truth. |
| [Persistence](../specs/representation/persistence.md), [format versioning](../specs/representation/format-versioning.md) | Private/Standard separation; exact whole-schema agreement, not revision ordering, extensions, magic or plausible payloads. |
| [Modules](../specs/modules.md), [sets](../specs/module-sets.md), [graphs](../specs/native-graph.md), [Windows deployment](../specs/targets/windows-x64-deployment.md) | Semantic versus machine dependencies, selected origins, unchanged-image compatibility and code/cleanup lifetime. |

These are observations of the inspected worktree, not conclusions from
older proposals' implementation paragraphs:

| Source | Observed boundary |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `BoundInterfaceDefinition`, `BoundInterfaceMemberDefinition`, `Interfaces` | An in-memory source declaration model exists: self/type/constant formals, defaults, bases, constraints, member/accessor roles, access, unsafe/no-return facts, dependencies and contract tokens. This is not a complete checked witness model. `HasBody` is not a retained default implementation program. |
| [`Binder.Interfaces.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs), [`BoundInterfaceContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundInterfaceContract.cs), [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) | Source interfaces are composed/bound and their declaration table is validated. Existing compilation-local static-protocol witnesses in `Binder.StaticProtocols.cs` do not supply general nominal witnesses. |
| [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs), [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | The in-memory reference has an `Interfaces` collection and the binder imports it. It has no complete witness collection. This must not be described as an already persisted reference codec. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Module`, `Write`, `ReadModuleHeader`, `CreateReference(Module)` | Private `Module` has structures/classes, functions and pointer/signature tables, but no source interface or witness graph. The writer finally selects **92**; the header reader admits only **92**. The private byte-to-reference projection passes layouts/functions/pointer tables, not interfaces. Thus serializing a program or erasing an imported Function cannot currently preserve the source interface model. |
| [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs) | Merging, restoration and graph-library reads have separate paths with name dictionaries and private type maps. A new collection on `BoundProgram` alone would miss these boundaries. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [`ModuleSets`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [`GraphPackages`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs), [`GraphJit`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs) | TNPK/TNSP/TNGP select outer **3**, embed separately parsed semantic Contracts and distinguish declaration names from actual machine imports. Their existing lists are not interface/witness identity carriers. Root JIT does not regenerate provider images. |
| [`TrRmInterfaceDefTable.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceDefTable.cs), [`TrRmInterfaceBaseTable.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceBaseTable.cs), [`TrRmInterfaceMethodTable.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceMethodTable.cs), [`TrRmInterfaceWitnessTables.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceWitnessTables.cs), associated-table readers | Typed Standard table readers/writers exist, including witness and associated-constant structural work. Their existence does not connect source declarations, private persistence, selected provider binding or executable witness dispatch. |
| [`InterfaceDeclarationBindingTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/InterfaceDeclarationBindingTests.cs) | Tests cover declaration binding, partial composition, generic contexts, dependencies, members and malformed bases. They are not source-free interface/witness persistence or replacement evidence. |

## Why reuse rows, but select a new private schema

| Alternative | Assessment |
|---|---|
| **P98-I1: reuse typed Standard row payloads plus private bridges/origin records** | **Selected.** One nominal/formal/requirement graph, shared structural helpers and explicit private code links. Additional records identify facts the assigned rows do not encode. No second interpretation is selected by a missing body or a familiar filename. |
| Append `BoundInterfaceDefinition` strings/booleans to 92 or 97 | Smaller text, incomplete contract. Current declaration keys and `HasBody` cannot identify imported requirements, complete generic contexts, selected defaults, private implementation origins or original witness expectations. Contract tokens are not a normalized semantic schema. |
| Embed an ordinary Standard `(1,0)` artifact and declare the problem solved | A legitimate future design only with an exact complete Standard selection and all required records/capability registrations. Assigned families alone do not supply witness authority, default/required distinctions, open constant-formal sources, private code links or all access/projection carriers. A private wrapper cannot waive those independent Standard obligations. |
| Separate private interface/base/member/type/formal/witness row codecs | Expressible, but duplicates the graph and its substitution/remapping rules. The private additions below are needed either way; no private-specific observation justifies a second encoding of the assigned core row payloads. |
| Replace the entire private module with Standard declarations and register Bodies | Substantive coherent redesign, not prohibited by draft compatibility. It migrates every private type, function, operation and native expectation and depends on broader Body/runtime codecs. That is unnecessary for this metadata transport boundary. |
| Amend unpublished 97 with this same graph | Permissible; no compatibility rule freezes 97. Choose **98** because this slice introduces an explicit open declaration/closed witness graph alongside the closed private IR, rather than another bounded native/reference field amendment. The number distinguishes this integration contract; it does not establish support or compatibility. |

No separate permanent codec, historical reader, compatibility alias or
“reserve a witness slot” decision is left for implementation.

## Exact revision composition

P98-I1's base **P97-R** is
[`terrum/private-semantic-97-checked-references-1`](checked-reference-private-persistence.md#exact-schema-composition),
including its complete P+E base, **with the explicit composition amendments
in this table and the operation allocation below**. It is not whichever
earlier proposal first used the number 97:

| Ingredient | Meaning retained |
|---|---|
| 92 | Resolved arithmetic from 89; NoThrow retirement composed with the complete candidate-90 NativeOutput collection/removals and tag 41; scalar/pointer output-cell eligibility. Include the provisional [owning-field tag 42 amendment](owning-field-move-private-persistence.md). |
| 93 | Mandatory general declaration Dependency collection, disjoint from NativeOutput's phase-1 rows. |
| 94 | [Wide scalar catalog and type-derived exact payloads](scalar-wide-value-carriers.md#exact-private-schema-and-composition), not unconditional i64 constants. |
| 95 | Mandatory [copy-origin summary](reference-field-value-provenance.md#necessary-private-transport-candidate-95), including the [unique-constructor amendment](reference-unique-constructor-field-provenance.md) where applicable. |
| 96 | [Nominal origins, foreign directory/member variant and every-tag-5 target discriminator](native-output-foreign-interface-private-persistence.md#exact-revision-composition). |
| 97 native composition | [Convention](native-calling-convention-private-persistence.md#exact-composition-and-selection) and [resolution](native-import-resolution-private-persistence.md) tokens; foreign convention-byte removal; [complete fnptr contracts](native-function-pointer-contract-private-persistence.md) and original address-contract roots; the [foreign-invocation amendment](standard-native-import-function-pointer-contracts.md#private-persistence-interaction), including mandatory NativeParameter vectors and raw-output applicability. |
| Direct expectations | [Import-only original foreign descriptor and full unsigned input extents](native-import-semantic-provider-expectations.md#candidate-record-and-validity), including its mandatory presence byte. |
| E | [Nominal enum directory/origins, `356+i`, `enum#i` and exact four-byte enum values](nominal-enum-persistence.md#complete-composition-not-a-revision-byte-shortcut). |
| R | Mandatory recursive checked-reference/optional table, `ctype#i`, declaration publication vectors, variable flags and typed checked operations from the checked-reference companion. |
| **I1** | Resolve the version-local expression-42 collision by the allocation below; add the mandatory component, private records, exact witness call target and preservation rules. Other P97-R fields retain their order, widths and meanings. |

The outer module header is `TRRM` followed by little-endian **i32 98**.
It is not the Standard u16 major/minor pair. All structured producers,
ordinary/graph-library readers, nested Contracts, references, remappers,
snapshots and validators select this whole draft, including empty interface
graphs. Explicitly selected older or partial schemas reject before their
records are interpreted. No `>=98` layout dispatch or earlier-97 retry.

Keep the selected foreign/convention-aware **outer-3 TNPK/TNSP/TNGP field
layouts**, extending their semantic-edge admission as specified below.
No new outer field/version or `native-N` label is needed: the inner graph
already carries the required typed dependencies. This is a new exact
outer-3/P98-I1 agreement, not a claim that arbitrary outer-3 artifacts have
it. A proposed outer 4 with a duplicate witness dependency vector adds no
otherwise missing information and is not selected.

Existing proposals remain owners of their deltas. This candidate supersedes
their exclusion of source interface/open declaration/closed witness
transport and the checked-reference expression assignments **within
P98-I1**. It does not retroactively amend P97-R or the field-move revision-92
draft, approve their Standard counterparts or make the foreign-interface
directory a source interface/witness codec.

### Expression-42 composition, not two interpretations

The inspected proposals deliberately have **distinct** meanings for
expression 42: implemented revision-92 FieldMove versus candidate-97
ReferenceForm. Their union is not an exact schema. P98-I1 selects:

| Private tag | Sole P98-I1 meaning | Payload/extent after the existing envelope |
|---|---|---|
| Expression **42 (`2A`)** | FieldMove | Exactly the field-move proposal's typed FieldPlace chain; total `30 + 17*k` bytes for `k >= 1`. |
| Expression **43 (`2B`)** | ReferenceForm | R's `u8 formationKind; PrivatePlace place`; total `14 + placeExtent`. |
| Expression **44 (`2C`)** | PlaceRead | R's `u8 useKind; PrivatePlace place`; total `14 + placeExtent`. |
| Expression **45 (`2D`)** | PlaceIsInitialized | R's `PrivatePlace place`; total `13 + placeExtent`. |
| Expression **46 (`2E`)** | InitializationAttenuation | R's child descriptor expression; total `13 + childExtent`. |
| Statement **28 (`1C`)** | CheckedStore, unchanged | R's `u8 transition; PrivatePlace destination; Expression value`; total `10 + placeExtent + valueExtent`. |

Expression and statement namespaces remain distinct. Preserve all operand,
type, span, ownership, reborrow, initialization, traversal and budget rules
of the respective owners; only the four R expression discriminators move.
Other expression tags 1–41 and statements 1–27 retain the composed base,
including tag 9's mandatory nested field-assignment count. Tags 47–255 gain
no meaning here.

The minimal alternate allocation would retain R's 43–45 and move only
ReferenceForm to 46. Select the contiguous **43–46 checked-reference
family** instead: there is no exchanged/implemented R reader to preserve,
all tags have the same one-byte cost, and this removes rather than creates
a family split. Neither allocation needs a compatibility alias. Do not
decode 42 as one operation and retry another based on its operand shape.

## Private interface component

### Placement, framing and reference spaces

Immediately after P97-R's complete `foreignInterfaceCount` directory and
before `Module.Functions.Length`, insert:

```text
u32 interfaceGraphByteLength
u8 interfaceGraph[interfaceGraphByteLength]
```

The graph is mandatory, including for a module with no interfaces. Its
private header and directory are:

```text
u8 magic[4] = "TIG1"
u16 componentRevision = 1
u16 tableCount
repeat tableCount:
    u16 tableKind
    u16 flags = 0
    u32 recordCount
    u32 byteOffset
    u32 byteLength
// table payloads follow
```

Offsets are relative to the graph start. Header size is **8**, each directory
entry **16** bytes. Payload starts are four-byte aligned; intervening minimum
alignment padding is zero. Payloads do not overlap the directory or each
other. The graph ends at the last payload end, with no trailing extension.
Kinds are unique; known empty tables are absent. At least Module, String and
ModuleUse are present, identifying the using logical Module even when the
graph otherwise has no declarations. There is no Standard header, section
policy namespace, Body, optional semantic trailer or nested schema selector.

Fixed tables concatenate their exact rows. Variable tables use the Standard
`u32 offsets[count+1]` index and exact nonempty row extents. String/Blob
payloads retain their existing tagged/indexed rules. Ordinary readers may
accept consistently remapped table/payload order; canonical writers order
directory entries and payloads by unsigned kind.

All multibyte fields are little-endian. `R(T)` below is a nonzero, one-based
u32 row in table T with a zero high byte; `?` alone permits zero.
Complete types use the existing **TypeSpecRef** row/F2/F3 union, not private
i32 codes or Body tokens. DefIds retain an eight-bit kind and 24-bit row;
they are artifact-local references, not stable row numbers across artifacts.
Each graph reference names this graph's tables. Private array indexes appear
only in explicitly named bridge fields.

### Selected core table payloads

Reuse these exact current metadata payload families, subject to the
explicit I1 amendments and support boundary below:

| Kinds | Payloads reused |
|---|---|
| `0001..0004` | Module, String, Blob, Feature |
| `0010`, `0011`, `0013..001D` | ModuleRef, TypeSpec, TypeDef, FieldDef, Layout, MethodDef, MethodSig, Param, GenericParam, Constraint, Contract, Import, Export |
| `0023..002A` | LayoutField, InterfaceMethod, InterfaceDef, InterfaceBase, AssociatedTypeDef, InterfaceWitness, WitnessMember, WitnessAssociatedType |
| `0030..0033`, `0036` | ConstantSource, InterfaceConstDef, AssociatedConstDef, WitnessAssociatedConst, AssociatedRelation |
| `0037..0039`, `003B`, `003C`, `003F`, `0048`, `004B` | Bounded selector graph/footprint/root/node/operand, FrozenValueNode, TargetProfile, FieldPath |

Other kinds are not selected, including Body `0020`, SourceFile/SourceMap,
general predicate dependencies/edges/captures, construction/union/static
families and ConstantExecution. A source location remains optional private
diagnostic data, never a graph identity or access principal.

In particular, the central reused rows are:

| Table / bytes | Ordered payload |
|---|---|
| InterfaceMethod / 12 | `ownerInterfaceDef, ordinal, methodDef` |
| InterfaceDef / 48 | `ownerTypeDef, selfGenericParam?, baseFirst, baseCount, methodFirst, methodCount, associatedTypeFirst, associatedTypeCount, associatedConstFirst, associatedConstCount, interfaceConstFirst, interfaceConstCount` |
| InterfaceBase / 12 | `ownerInterfaceDef, ordinal, baseTypeSpec` |
| AssociatedTypeDef / 12 | `ownerInterfaceDef, name, defaultTypeSpec?` |
| InterfaceWitness / 32 | `implementingTypeSpec, interfaceTypeSpec, memberFirst, memberCount, associatedTypeFirst, associatedTypeCount, associatedConstFirst, associatedConstCount` |
| WitnessMember / 20 | `witnessRow, declaringInterfaceTypeSpec, requirementMethodDef, selectedOwnerTypeSpec?, selectedMethodDef` |
| WitnessAssociatedType / 20 | `witnessRow, declaringInterfaceTypeSpec, associatedTypeDefId, valueTypeSpec, bindingOrigin` |
| InterfaceConstDef / 16 | `ownerInterfaceDef, name, constantTypeSpec, constantSourceRef` |
| AssociatedConstDef / 12 | `ownerInterfaceDef, name, constantTypeSpec` |
| WitnessAssociatedConst / 16 | `witnessRow, declaringInterfaceTypeSpec, associatedConstDefId, constantSourceRef` |
| AssociatedRelation / 12 | `declaringInterfaceTypeSpec, associatedTypeDefId, requiredTypeSpec` |

All columns are four-byte words; names are String references, owners/ranges
are table-specific, complete-type columns are TypeSpecRefs and the two
associated declaration columns use their selected reference unions.
InterfaceMethod order remains declaration order, **not a native slot**.

P98-I1 selects these additional meanings explicitly, not by inspecting
Feature rows to choose a decoder:

* [Associated-type identity](associated-type-definition-identity.md):
  kind **20**, its direct/Import reference union, owner binding and exact
  absent/present/default comparison.
* [Associated-constant identity](associated-constant-definition-identity.md):
  kind **25**, its direct/Import union and separate requirement/binding
  identities; no associated-constant declaration default.
* [Associated-type origin](witness-associated-type-binding-origin.md):
  exactly **1=ExplicitBinding**, **2=DeclarationDefault**, materialized values
  and exact retained-origin comparison.
* [Constraint relation payload](constraint-associated-relation-payload.md):
  kind 3 targets one AssociatedRelation row, not a type or declaration row.
* For initialized InterfaceConstDef identity, private DefId **22** targets
  section `0031`, consistently with the
  [definition-kind candidate](immutable-data-definition-kinds.md).
  It is eligible for explicit owner-checked Import/Export here. This does
  not select ImmutableData or Feature 19.
* The [six phase-3 selectors](aggregate-native-outputs.md#exact-shared-predicate-codec)
  and [composition](predicate-feature-composition.md), with `10/1 -> []`;
  [fixed scalar frozen values](scalar-frozen-value-registration.md), with
  `11/1 -> []`. Select the 24 fixed-scalar leaves, exact widths/validity and
  no union prerequisite. General predicates/constant programs are not
  silently included.
* [CopyOrigin `000D`/15/1](reference-copy-origin-trrm.md), including exact
  type/field identity closure, where an admitted ordinary callable publishes
  that fact. Unrepresented summary variants cannot be erased.

One **private-only TypeSpec form `0x10`** completes the bridge to P97-R
without claiming a Standard anchor or enum schema. Its exact row is
**six bytes**:

```text
u8 form = 0x10
u8 qualifiers = 0
u32 closedTypeLink                  // required ClosedTypeLink row
```

Core forms `0x0D` fnptr anchors and `0x0F` existentials are not selected in
I1. **Reuse existing `0x0E` for initialization references**, including its
write-only qualifier domain and in-scope generic inner terms; ordinary
references and optionals also retain their assigned forms. No second init
type encoding is needed. This preserves R's complete constructor-2 identity,
descriptor/referent distinction and scoped attenuation.

Private form `0x10` is a leaf into a separately validated closed type closure,
not a Body pointer, opaque Blob or escape from contract validation. It can
carry, for example, P97-R's complete closed fnptr contract or enum identity
without selecting their different Standard codecs. It cannot hide an
interface, unbound formal, unrepresented open constructor or required
nominal owner declaration. Require the complete original P97-R table,
origin and expectation closure even after Function erasure. Closed structs/
classes still need their graph TypeDef/NominalOriginLink. A context-only
prototype carrier without a standalone complete private identity is not
eligible.

Use the core representation whenever that complete type has an admitted
I1 core form, including `0x0E`; redundant form-`0x10` spellings
of those types reject. Thus the leaf is not a compatibility alias.
Scalar identity mapping uses the exact selected catalogs, not equal private
and Standard code numbers. Structural helpers must receive the private
type resolver explicitly; a Standard reader does not accept private form
`0x10`.

This component's only additional ConstantSource alternative is
**private tag 4 = GenericConstantFormal**, with `payloadRef=R(GenericParam)`.
The formal must be a constant formal in the exact using declaration's
lexical owner/method scope. It denotes that formal's typed symbolic value,
not its default or one observed instantiation. It may appear as a constant
argument/default in open declarations; after closing a witness or callable
instance it must substitute to a closed admitted value. Tag 1 retains the
selected frozen scalar codec; tags 2/3 are unsupported here. No implicit
evaluation, literal guess or new Standard tag 4 is assigned. Symbolic
arithmetic recipes, aggregate/enum frozen values and unsupported domains
remain gated, not decoded as scalar or formal aliases.

Module ownership/cursors, complete TypeSpec and formal scope rules, exact
Import/Export keys, contract ownership/cardinality, range exclusivity and
structural sharing retain their metadata meanings. Body rows are absent
and every MethodDef `bodyRow=0`; private CallableDeclaration/CodeBinding,
not this zero, identifies required/default/implementation roles and code.
No interface is given a private PrimitiveType code or instance payload.

Two value/admission details are explicit private selections:

* Module and ModuleRef `runtimePolicy` use **u32 0=FailFast** and
  **u32 1=CheckedExceptions**, corresponding exactly to P97-R. The latter
  is not a new Standard runtime-policy assignment. Target requirements
  remain 0=portable semantic and 1=Windows x64; native use separately checks
  its complete target/runtime profile.
* Param mode remains exactly **00**. Complete init references use `0x0E`,
  not a resurrected out/ref mode. Param flag 01 retains only its assigned
  implicit readonly-reference adaptation; local binding `mut` is not a
  signature flag. Existing private legacy output prototypes are not aliases
  for checked-reference declarations.

The admitted Contract kinds are `0001` Effects (only mask bits 02/04),
`0002` OutputState, `0004` Dependency, `0005` Cleanup, `000B` phase-3
selectors, `000C` effective NativeCallable where its ordinary definition
role is independently admitted, and `000D` CopyOrigin. Clause ownership,
applicability and exact payloads follow the selected owners. Unsupported
phase/state/role variants still reject; the table whitelist does not
activate every logical use of a clause. Inactive `0003` Transfer and general
predicate/evaluation attachments are not selected.

### Required private records

The following kinds are scoped **only to TIG1/P98-I1**. They are not
Standard section, Feature, DefId, opcode or Body-record assignments.
All listed references require their exact table/kind.

| Kind / row bytes | Candidate record |
|---|---|
| `0100` / 8 | **ModuleUse:** `R(Module) module, u32 requiredKinds` |
| `0101` / variable | **DefinitionBinding:** described below |
| `0102` / 16 | **NominalOriginLink:** `R(Module) usingModule, u32 nominalTypeOriginIndex, DefId(TypeDef) definition, u32 reserved=0` |
| `0103` / 8 | **ClosedTypeLink:** `R(Module) usingModule, i32 privateType` |
| `0104` / 16 | **CallableDeclaration:** `R(MethodDef) method, u8 role, u8 receiverMode, u16 reserved=0, u32 surfaceFirst, u32 surfaceCount` |
| `0105` / 16 | **ParameterSurface:** `R(MethodDef) method, u32 explicitOrdinal, R(String) label, R(ConstantSource)? defaultValue` |
| `0106` / 24 | **MethodInstance:** `R(Module) usingModule, R(MethodDef) method, TypeSpecRef ownerType?, TypeSpecRef implementingType?, u32 argumentFirst, u32 argumentCount` |
| `0107` / 8 | **SubstitutionArgument:** `u8 kind, u8 flags=0, u16 reserved=0, u32 argumentRef` |
| `0108` / 8 | **CodeBinding:** `R(MethodInstance) instance, u32 moduleFunctionIndex` |
| `0109` / 20 | **WitnessOrigin:** `R(InterfaceWitness) witness, R(Module) usingModule, u8 originKind, u8 authorityKind, u16 reserved=0, DefId(TypeDef)? implementerDefinition, R(ModuleRef)? authorityModule` |
| `010A` / 8 | **WitnessSelection:** `R(WitnessMember) member, u8 selectionKind, u8 flags=0, u16 reserved=0` |
| `010B` / 12 | **DefaultReplacement:** `R(MethodDef) replacement, TypeSpecRef declaringInterface, R(MethodDef) requirement` |

`nominalTypeOriginIndex` and `moduleFunctionIndex` are **zero-based** indexes
into P97-R's containing private arrays. They never accept row tokens or an
absent sentinel. All other indexes follow the stated graph namespace.
Empty ranges are exactly `(0,0)`; nonempty ranges have a nonzero first row,
checked bounds and exclusive ownership. Unknown tags/bits reject.

#### Module and definition ownership

There is exactly one ModuleUse per Module. `requiredKinds` permits only
bit **01=DeclarationGraph** and **02=ClosedDeclaredWitnesses**;
02 requires 01. DeclarationGraph includes nominal/support declarations
published by an independent type-only provider, not only authored
InterfaceDef rows. Zero is valid for an otherwise empty interface component.
The producer sets every applicable bit, including declaration-only retained
uses. Readers require support for the exact indicated private rules.
These are not Standard Feature numbers or a host authorization grant.
Other selected Feature requirements remain Module-owned; scalar, selector
and copy-origin uses require their exact catalog closure in every using
Module. There is no invented Standard generic/witness Feature registration.

Each TypeDef, FieldDef, MethodDef, AssociatedTypeDef,
InterfaceConstDef and AssociatedConstDef in the graph has exactly one
DefinitionBinding:

```text
u32 localDefId
u8 originKind
u8 flags = 0
u16 reserved = 0
u32 localDeclarationKey             // required String row
if originKind == 2:
    u32 importRow                   // required Import row
if originKind == 3:
    u32 selectedModuleRef           // required ModuleRef row
    u32 selectedDeclarationKey      // required String row
    u32 authorizingWitnessMember    // required WitnessMember row
if originKind == 4:
    u32 expectedOwnerTypeDef        // required TypeDef row
    u32 selectedDeclarationKey      // required String row
```

Extents are **12**, **16**, **24**, **20** bytes for kinds 1, 2, 3, 4.
The DefId target set is exactly **13/14/16/20/22/25**. Generic formals
retain their owner/kind/ordinal identity; kind 19 is not independently
exported. Witnesses and selected associated bindings have no new DefId
or standalone Export.

* **1=OwnedDefinition:** the declaration belongs to its actual logical
  Module. Its producer-assigned local declaration key is unique across
  these kinds in that Module, whether public or private. Exports separately
  publish eligible definitions; their keys do not confer ownership.
* **2=PublicImportExpectation:** the Import expects exactly this local
  DefId and belongs to the same using Module. Resolve its selected
  Module/export key before comparison. The stub is not a new nominal
  definition, implementation or semantic re-export.
* **3=WitnessTargetExpectation:** MethodDef only, for a selected target
  not independently public/importable. Select the Module and explicit
  producer declaration key **only through the already bound authorizing
  witness requirement**. That selected witness must identify this exact
  target origin. This is a private origin reference, not a new ordinary
  Import/Export eligibility or permission to look up/call a private method.
  The expected MethodDef is exactly the named WitnessMember's
  `selectedMethodDef`. It retains the target's complete expected contract.
* **4=RetainedFieldExpectation:** FieldDef only, for a field retained as
  part of a complete expected nominal type/layout, including private fields.
  Bind `expectedOwnerTypeDef` first, then select that provider owner's exact
  explicit field declaration key. Require the selected FieldDef to belong
  to that owner; its ordinal, type, access and layout correspondence must
  still satisfy every retained commitment. This is not a public field
  Import/Export, a name/offset search or field-access authority. The stub
  belongs to that expected TypeDef's complete checked field range. A field
  independently imported by public key instead uses kind 2.

Module identities and declaration keys retain candidate 96's explicit
private grammar/capacities and ordinal, case-sensitive equality. Keys are
inputs to publication, not derived from source names, signatures, slots,
paths, body hashes or array positions. Kind 3 is necessary for an explicit
private implementation without making it public merely to serialize a
witness. A replacement preserves the retained target's Module/declaration
key and selected owner; physical DefId renumbering is immaterial.
This bounded private component permits one canonical export key per
exported definition, consistently with candidate 96; no new export aliases
or inferred member keys are introduced.
I1 Import/Export has that same target-kind set, subject to ordinary public
owner eligibility. Private witness targets and retained private fields use
their explicit sidecars, not widened public Import/Export permissions.
Module identities retain the 1–64-character ASCII grammar; declaration and
export keys are nonempty, NUL-free and at most 256 strict-UTF-8 bytes.
Other selected names/labels are at most 1,024 strict-UTF-8 bytes. These
logical-text limits apply after decoding any admitted String row encoding;
its actual encoded extent separately consumes the graph/artifact budget.

NominalOriginLink is required for every retained closed nominal struct/class
declaration needed by the graph, **including bodyless contracts**, not only
executed signatures. Its graph TypeDef and P97-R Binding must agree on owned
versus expected origin, using Module, local declaration key, selected
owner/provider and export binding. Retain the complete P97-R nominal facts,
including ownership/cleanup/layout facts not supplied by the reused
TypeDef's visibility/kind flags. An explicit link is not permission to infer
correspondence by shape. No link duplicates the separate foreign-interface
directory as an ordinary source interface. Unrelated P97-R declarations
need not enter this graph. Wider generic value-type declarations lacking
complete retained declaration facts remain unsupported, not opaque success.

ClosedTypeLink supplies form `0x10`'s explicit link to an already admitted
**closed** P97-R type. Require at most one row per
`(usingModule, privateType)` and validate the entire referred private
closure, including nominal origins, every constructor/qualification,
fields/layout and all reachable contracts. The leaf has no reverse
TypeSpec reference or structural cycle. Reordering/cloning requires
remapping the leaf and its private type, not comparing integers.
Core graph types instead resolve through their typed constructors,
nominal links and exact scalar catalog. A graph type without an admitted
private executable carrier may still be inspected/bound; it cannot be
lowered by guessing one.

#### Requirements, defaults and callable instances

Each graph MethodDef has exactly one CallableDeclaration. Roles are
**1=RequiredSlot**, **2=InterfaceDefault**, **3=AuthoredImplementation**,
**4=PrivateInterfaceHelper**, **5=InterfaceReplacementDefault**.
Role 1 has no implementation/default body; role 2 is a directly declared
requirement with a default; roles 4/5 create no requirement.
Roles do not grant static/instance applicability or access.
InterfaceMethod contains precisely the directly declared instance
requirements for this bounded component, including role 2 but not roles 4/5.
An imported default retains role 2 despite having no local body.
Roles 1/2/5 require an interface TypeDef owner and the matching complete
instance receiver; role 4 requires an interface owner and private effective
access. Role 3 is an actual authored implementation, not a bodyless abstract
declaration disguised by its role byte. A role on an expected declaration
records the selected provider's required role without claiming local code.

`receiverMode` is exactly **0=None**, **1=ReadonlyReceiver** or
**2=MutablePlaceReceiver**. Zero agrees exactly with absent MethodSig
receiver; roles 1/2/5 require 1 or 2. Mode 2 retains the normalized mutable
receiver/place-loan obligation independently of the receiver's complete type.
It is not a write qualifier, an initialization reference or caller-unsafe
authorization. A witness target cannot require mode 2 where the requirement
admits only readonly receiving. Check all remaining access, loan, capture
and output obligations normally; no source receiver mode is inferred from
the target body or erased into a same-shaped MethodSig.

For interface-owned instance methods, MethodSig's receiver identifies the
exact **declaring interface instance in declaration scope**, including
instance helpers. In I1 that distinguished
receiver position denotes the implementing receiver supplied by the
witness, not a stored interface-object value. Closing an invocation binds
that receiver position to MethodInstance's implementing type while keeping
receiverMode. Do not replace ordinary occurrences of that interface/type
elsewhere in the signature. This private abstract-receiver rule does not
assign a Standard dispatch representation or confer interface-value ABI
support.

ParameterSurface has exactly one row per explicit logical Param, in formal
order; unit parameters count. Its exclusive range belongs to that method's
CallableDeclaration. Labels are nonempty and unique in the signature.
Default absence is zero; a present default is a typed admitted
ConstantSource in the declaration's scope, not the implementation's or
current provider's default. More general default recipes require their
complete codec and reject in I1. Already compiled calls retain their
selected default operands and mapping.

A DefaultReplacement names an explicitly published replacement default
and the exact inherited requirement instance it replaces. The replacement
is role 5, owned by the derived interface; the declaring interface is a
base instance in that owner's formal context and directly owns the named
requirement. Every role-5 method has at least one such row; keys
`(replacement owner, complete declaring interface, requirement)` are unique.
Its effective replacement graph is finite and acyclic.
It does not identify a target by name, manufacture a new requirement or
use base/import order as precedence. Distinct surviving defaults still
require explicit resolution.

MethodInstance records are **closed instantiations**, not open templates.
`ownerType` supplies the exact declaring owner's ordered arguments, or is
zero only for a module-level method. `implementingType` is required exactly
when that owner is an interface; otherwise it is zero. It records the
complete witness implementer independently of the interface arguments,
including interfaces without a self formal. It is not a fabricated formal,
new interface argument or source inference input.

The instance key includes using context, selected method, complete owner,
implementer when present and ordered method arguments. Thus the same
default of a self-independent interface can have separate closed code for
two different implementing types without conflating them. Its method
argument range has exactly one SubstitutionArgument per method formal,
in ordinal order:
**1=TypeSpecRef**, **2=ConstantSource row**. Compare kind/domain before
substitution; reject missing, extra, unbound, wrong-scope or unsupported
arguments. ConstantSource tag 4 cannot remain after closure. Owner arguments
are not repeated in this method-only range.

First substitute the declaring owner's arguments, then correspond method
formals by kind/ordinal and matching arity. Formal names are labels.
Preserve constraints/defaults and the distinction between self and an
ordinary argument coincidentally equal to self. A witness's generic method
mapping is this fixed correspondence, not an arbitrary permutation.

CodeBinding associates one closed MethodInstance with an actual private
ModuleFunction definition or representable retained ordinary code expectation.
It has one binding per closed instance and never targets a required slot,
helper mistaken for a requirement, or candidate-96 foreign dispatch stub.
The Function's complete closed signature and declaration collections must
agree with the graph under the links, including output states, general
dependencies, summary facts, access/unsafe/ownership and original native
dependencies. A matching fingerprint alone is insufficient.
This is consistency between two encodings of the **same originating
declaration or expectation**; provider-versus-expectation satisfaction
remains the separate directed comparison.

A private WitnessTargetExpectation need not have an ordinary code-import
stub or a consumer CodeBinding. A semantic linker obtains admitted code
from the selected provider's owned MethodInstance/CodeBinding after binding
that witness origin. It cannot change an abstract/private origin into a
public legacy export to satisfy the old name-based linker. Cross-image
execution additionally needs a real admitted entry/ABI; this metadata does
not invent a private-target export or thunk.

Closed default code uses the recorded implementer as its physical receiver,
but retains the default MethodDef's original Module/type access principal
and semantic body origin. Code hosting/specialization is not reparenting
the default into the implementing type or granting its private access.
Reconstruction must keep semantic body owner separate from physical
receiver/entry; the CodeBinding's MethodInstance supplies that origin.

Graph declaration facts are not reconstructed from a Function body.
Conversely, no empty BoundFunction counts as an implementation. A
declaration-only reference may retain a default origin without a
CodeBinding; source-free execution must obtain an admitted complete program
or actual selected provider entry and its origin/lifetime, or decline.
Required constant execution instead needs its complete checked program and
evaluation/value contract, not an arbitrary native entry; I1 selects no
general constant-execution codec. This proposal supplies no open generic
Body codec and does not freeze one closed specialization as a generic definition.

#### Witness authority, keys and selected origins

Every InterfaceWitness has exactly one WitnessOrigin; it supplies the
otherwise ownerless row's using Module. `originKind` is exactly
**1=OwnedWitness**, **2=RetainedExpectation**. `authorityKind` is exactly
**1=NominalPrimary**, **2=PrivilegedPrimitive**.

For NominalPrimary, `implementerDefinition` is required and identifies the
exact primary nominal definition of `implementingTypeSpec`, not the
interface owner. An owned witness's using Module owns that non-imported
definition and `authorityModule=0`. An expectation retains its own expected
implementer declaration and a required using-Module-owned ModuleRef to
that primary authority. Bind that owner independently before comparing
the witness. Extensions, importing interfaces and adapters of another type
cannot publish the witness.

For PrivilegedPrimitive, `implementerDefinition=0`; never synthesize a
TypeDef for a primitive. Owned publication requires the selected environment's
explicit privileged Core authority for this exact primitive/role. An
expectation retains its required authority ModuleRef. The tag is not proof
of privilege: host/provider selection must establish the authority under
the relevant Core registration contract, and is not serialized as trust.
An owned primitive witness also has `authorityModule=0` and its using Module
must be the selected privileged publisher, not a consumer impersonating it.

The bound witness key is
`(selected authority, complete implementing type, complete root interface)`.
Each requirement key additionally contains its **complete declaring
interface instance and resolved declaration identity**. Bind owner/member
Imports before comparing these keys. Do not select a witness by name, slot,
layout or a method's shape.

Close ordered bases by forward substitution. The declaration inheritance
graph is finite and acyclic even when cycling arguments change. Deduplicate
diamond paths to the same complete declaring-member identity; different
closed instances remain distinct. Require exactly one callable, associated
type and associated constant binding for every applicable requirement in
the full inherited closure, with no extras or duplicate keys.

Each WitnessMember has one WitnessSelection:
**1=AuthoredImplementation**, **2=InterfaceDefault**. Its existing
`selectedOwnerTypeSpec` and `selectedMethodDef`, together with that target's
DefinitionBinding, are the exact origin. A selected default is role 2 with
the declaration relationship or role 5 with its explicit replacement
relationship; an authored target is role 3. Preserve that selection rather
than rerunning overload/default lookup on load. A private target is reachable
only through its authorizing witness. Selected-owner substitution and
method-formal correspondence
retain the original target even when lowering emits a direct call.

Associated bindings retain the actual base declaring instance, declaration
identity, selected complete type/value and origin. Type defaults and
explicit selections compare under the exact origin/default rules selected
above. Constants require a closed typed value in the declaration's exact
substituted domain, not a callable or runtime static field. A changed
explicit/default origin fails retained-origin comparison even if its value
is unchanged. Transparent/default expansion must terminate; a finite
nominal data anchor is not transparent recursive expansion.

## Contracts and the support boundary

The following are preservation requirements, not additional source syntax
or permission to claim all accepted features:

| Dimension | Required handling |
|---|---|
| Complete signatures | Retain receiver, result, all logical formals/modes, full type/constant substitutions and constraints. No unit erasure, init covariance, qualification upgrade or carrier-based equality. |
| Dependency/output contracts | Retain exact receiver/input/output identities, destination and phase, broad source permissions, backing-only refinements, captures, selector/result correlation, vacant/live/partial/pending obligations and invalidation endpoints. Keep raw NativeOutput separate from checked references. |
| Effects/unsafe | Use the selected declared facts and directed caller-domain relation; `DoesNotReturn` is not arbitrary failure freedom. Preserve caller-unsafety from both source declaration and native role. Unsafe operations still require the proper body scope. Never restore general NoThrow. |
| Ownership/lifetime/access | Preserve transfer/consumption, actual responsible holders, cleanup order/origins, source access principals, backing/root/pin/code lifetimes and all remaining obligations. A witnessed private target grants the selected operation, not private-member lookup. |
| Defaults and published guarantees | Interface labels/defaults and broad contracts stay distinct from target labels/defaults and stronger target facts. Retain every stronger fact actually relied on; body inspection cannot supply an unpublished guarantee. |
| Independent use | Reference erasure removes implementation bodies only where permitted. Layouts, constant programs when required, declarations, default origins, witnesses and cleanup/code/provider closure remain. No host cache or debug/source file carries a required fact. |

I1 can transport the assigned type/formal/instance/associated records and
the selected Effects, Dependency, checked-output selectors, Cleanup and
CopyOrigin forms where all their contracts are representable. It does
**not** turn inactive Transfer `0003`, an attribute token or `HasBody` into
a complete ownership, evaluation or failure schema.

In particular, additional file/friend/nested/conformance access principals,
property/indexer or static/operator requirement surfaces, fnptr contract
anchors not composed into I1,
existential/projection evidence, general require/ensure/read/evaluation/
NoAlias programs, unassigned generic-write constraint carriers,
unrepresented async/lending completion phases and unsupported type/value
forms remain explicit gates. A producer declines an affected declaration
even if that member is uncalled; it cannot publish a smaller interface.
This is a support limit of the initial component, not a narrowing of the
accepted source language. A private closed-type leaf retains its P97-R
contracts but does not select the Standard enum TypeDef/EnumDef or complete
fnptr anchor/invocation compositions. Enum-as-implementer graph declarations,
enum/aggregate frozen values, open fnptr signatures and unsupported generic
constant recipes remain gated. Native/foreign interface roles remain in
candidate 96's separate directory until their complete graph role/layout
schema is explicitly selected; they cannot be encoded as ordinary I1
requirements to bypass that boundary.

## Private call and API consequences

Add exactly one target variant to candidate 96's existing **expression
tag 5**, leaving its common source-ordered argument/mapping payload intact:

```text
u8 targetKind = 2
u32 witnessMemberRow                // required graph WitnessMember row
u32 requirementMethodInstanceRow    // required graph MethodInstance row
// existing argumentCount, mapping and ordered expression operands follow
```

Kinds 0 ordinary symbolic and 1 foreign dispatch retain P97-R's meanings;
other kinds reject. Kind 2's MethodInstance names that exact requirement,
its complete declaring interface, that witness's complete implementer and
closed method arguments. Derive the
selected target through the recorded witness and ordinal correspondence,
not a provider overload search. The existing expression type resolves to
the same closed requirement result through the selected type/nominal links
and exact catalogs.

Retain receiver-once/source-order staging and the existing logical parameter
map. For this instance-call route receiver occupies logical argument
position 0; explicit formal n maps to position n+1, including dependencies,
selectors and init reservations. The graph MethodSig keeps its receiver
separate. No tag 41 raw-output reservation becomes a checked reference.
If the selected call cannot be lowered with the existing admitted ABI,
reject affected emission; tag 2 is not a runtime witness table.

Candidate internal API responsibilities are exact; their names are
implementation-facing, not new Terrum or public Standard APIs:

| API/input | Required result |
|---|---|
| `PrivateModuleIdentityContext` | Explicit logical Module identity, definition-key/export assignments and selected provider identities; external Core-authority context is separate. APIs lacking this input cannot invent identity from filenames or declaration names. |
| `WritePrivateModule(program, identityContext, P98-I1)` | Normalize/check the complete interface graph and all admitted contracts before writing. Nonempty source interfaces without normalized records fail; do not silently omit them. |
| `ReadPrivateModule(bytes, P98-I1)` | Immutable decoded snapshot retaining graph, private bridges, original expectations and exact schema. Inspection may leave bindings unresolved; execution/reference admission cannot. |
| `CreateReference(snapshot, usingIdentityContext)` | Consumer-owned expected declarations and complete witness expectations, including private target expectations. Retain all reachable formal/type/default/contract/provider facts before erasing bodies. Extend CompilationReference with the same typed graph/witness facts rather than a second serialization of strings. |
| `BindReference(reference, selectedProviders)` | Bind exact identities, then compare the entire retained graph using the shared satisfaction rules. Do not overwrite expected facts with provider declarations. |
| Shared traversal/rewriting | Visit and remap types, substitutions, identities, witness calls, selected targets, dependency/ownership facts and code origins on every transformation. An unvisited side object is not a complete integration. |

Source declaration binding, new witness construction, private serialization,
Standard TRRM validation, native deployment and lowering remain separate
operations. Reading an existing checked witness does not authorize creating
a new conformance, and generic declarations do not manufacture a closed one.

## Providers, replacement, packing and native boundaries

Before affected use:

1. Select the exact schema and unambiguous provider Module/revision/export
   for each public expectation. Resolve nominal owners independently from
   requirements and private selected targets. Bind every retained witness
   through its implementing authority and exact complete pair.
2. Compare the full finite context-sensitive graph: owner/member identities,
   type/constant domains and arguments, bases, caller domain, output/access/
   phase/ownership/unsafe/effect/dependency guarantees, associated values and
   default/implementation origins. Compare open declaration terms under
   corresponding owner/method formals, not one convenient closed instance.
3. Keep each consumer's original expectations. Directed strengthening is
   allowed only where the defining contract permits it. Retained selected
   implementation/default origins and associated origin/value dimensions
   use the exact rules here; a new private declaration key is not a body-only
   change. Check stronger relied-on facts as additional obligations.
4. Publish success only after every required comparison/dependency succeeds.
   An in-progress recursive pair, resource cutoff, unchanged hash, cached
   acceptance or compatible machine carrier is not success.

Packing/re-emission selects one destination whole schema. Rebuild Module
partitions, DefIds, Import/Export maps, complete type/formal/constant
references, interface/base/member ordinals and exclusive ranges, witness
origin/selection/default-replacement links, sidecar indexes, closed
instances, private type/function bridges, call targets and source spans.
Recompute variable indexes, lengths, alignment, outer offsets and checksums.
Never substitute matching bytes in strings, constant payloads or source data.

Do not coalesce nominal definitions or independent expectations by name,
layout or equal rows. Structural rows may share only when their resolved
meaning agrees in every using Module/substitution context; otherwise clone
and remap or reject an unrepresentable merge. The initial writer retains
all original expected declarations/witnesses and their contract closure,
even after direct-call lowering. Devirtualization does not discharge those
roots. A later trimming transformation must establish that an obligation
is genuinely no longer retained, not merely that its machine call vanished.
Embedding body-derived code/facts would additionally retain the actual
code/value-origin obligations through a separately admitted mechanism;
I1 selects no body-inspection specialization or way to replace them with
a broader declaration fingerprint.

The unchanged outer-3 fields have these P98-I1 admission rules:

| Boundary | Required behavior |
|---|---|
| Semantic versus PE exports | Interface/type/associated declarations and witnesses are inner semantic facts, not dummy PE exports, vtable slots or code-name aliases. Existing real code export/image requirements remain. A metadata-only provider need not be packaged as a fake native DLL. |
| Dependency classification | Derive semantic edges from graph ModuleRefs, witness authority/selected target origins and retained type/default/cleanup/code dependencies. Keep existing code declaration and actual machine import lists as separate views. |
| Empty code lists | Extend candidate 96's semantic-only edge rule: TNPK's selected provider and TNSP/TNGP dependency entries may have empty code-name lists when an actual retained I1 semantic obligation justifies the edge. Reject unjustified extra providers; do not insert non-PE keys into machine-import lists. |
| Identity scope | In a native package this bounded route still requires owned published logical Module identity to match its explicit provider identity. Multi-Module semantic packing does not implicitly admit a new multi-Module native hosting map. |
| Closure/catalogs | Include semantic-only providers and all independent diamond expectations in closure/uniqueness checks. Keep policy, target, topology, size, cycle and filename-collision gates. Argument order is no selector. |
| Snapshots/replacement | Consumer/Provider/GraphContractSnapshot retain P98-I1, the complete graph, original private target/default origins, all bridges and selected dependencies. Recheck replacement against these snapshots, not reconstructed expectations from the replacement. |
| AOT and root JIT | Validate semantic closure plus the actual ABI/image/import map. AOT uses unchanged images; root JIT re-lowers only admitted root semantics. Provider DLLs remain precompiled. Missing generic/default code, cross-image carriers or cleanup/code lifetime support rejects; no provider regeneration or JIT fallback. |

No new dynamic witness ABI, heap sharing, implicit boxing/marshalling,
allocation, pin/root acquisition, live replacement or hot unload is selected.
Native resource retention comes from actual selected contracts, not keeping
a metadata row or successfully loading a DLL.

## Producer and consumer obligations

Producers establish truthful source declarations, conformance authority,
complete witness selection, scoped substitution and published body facts.
New source/witness construction performs the full language checks. Rewriters
are producers for changed facts.

Consumers must structurally validate the selected component before affected
use: framing, exact extents/counts, assigned tags and reference namespaces,
reserved values, ownership/ranges, formal scopes/kinds/arity, closed-instance
formation, identity and origin relationships, required support and complete
provider compatibility. They need not replay source binding or re-prove
producer-published implementation bodies. A self-declared authority bit,
signature, valid checksum or optional audit does not prove producer truth.

Keep the existing private 4 MiB semantic-artifact limit, 100,000-node and
depth-128 budgets and the existing string/function/slot/argument limits.
All graph/sidecar rows, arguments and traversed references consume those
shared budgets, not a fresh budget per table/Module/witness. A graph table's
row count additionally fits 24 bits; all lengths/ranges/products use checked
wide arithmetic before allocation. Resource exhaustion explicitly declines
the operation; it never completes an unfinished closure.

An honestly selected unsupported older schema rejects; equal 98/TIG1/outer-3
bytes still do not identify the history of every relabelled stream whose
bytes happen to be valid. Regeneration or an explicitly selected semantic
conversion needs complete decoded source-schema facts and rebuilt indexes.
No source/debug recovery, provider-derived expectations, header patch,
schema sniffing, alternate decoder or success-shaped fallback is selected.

## Acceptance cases and implementation gates

These are required future cases, not tests run by this documentation change.

| Case family | Required positive and negative observations |
|---|---|
| Declaration-only round trip | Persist/reload generic interfaces with explicit self, ordinary type/constant arguments, defaults, ordered bases, overload identities, associated types and initialized versus required constants. Remove source/debug and Function payloads. Retain complete declarations and necessary private field/layout facts; do not infer them from names or bodies. |
| Generic substitution | Distinguish self from an equal ordinary argument; preserve type/constant formal kinds, domains, owner scope, defaults and method-ordinal mapping. A base forwarding its constant formal uses private source tag 4 and closes to a typed value. Existing form 0E preserves generic init-reference inner types; private form-10 leaves preserve complete P97-R pointer/enum contracts. Reject wrong arity/kind/domain/scope, redundant leaves, unbound closed instances and growing expansions. |
| Requirements/defaults | Cover required-only and defaulted slots, explicit implementations, private explicit targets, inherited defaults/replacements and diamonds. Two implementers of a self-independent interface retain distinct default MethodInstances and original default access principals. Same-named unrelated requirements stay distinct. Ambiguous authored candidates or remaining defaults reject rather than fall back or use import order. |
| Associated bindings | Explicit/default equal values still retain different origins. Preserve exact associated declaration and closed declaring instance, including generic bases. Reject missing/extra/duplicate bindings, wrong owners/domains, associated-constant declaration defaults and transparent cycles. |
| Complete contracts | Exercise receiver access, caller-unsafety, no-return, logical unit positions, reference/checked-output modes, source/backing/capture relations, selector conditions and applicable ownership/cleanup facts. Reject authority upgrades, init covariance, extra caller obligations, lost dependencies and unsupported required contracts even in unused members. |
| Independent providers | Encode the provider and consumer independently, with unrelated row/key/string ordering and different source/parameter names. Exact explicit identities and contracts bind source-free. Equal names/layouts from another Module, owner, requirement or private target key do not. Core primitive publication requires real selected privilege, never a fabricated nominal owner. |
| Original expectations/replacement | Build the consumer once, discard source and imported bodies, then replace providers. Physical reindexing and compatible body changes succeed. Changed nominal owner, base arguments, default/required role, selected implementation/default origin, associated domain/value/origin, output/access/unsafe/dependency contract or retained stronger fact rejects. The replacement cannot redefine the expectation. |
| Malformed/private namespace | Reject truncated/trailing data, overlaps, bad alignment/indexes/high bytes, unknown kinds/tags, nonzero reserved values, duplicate keys/ranges, orphan rows, foreign/private index confusion, illegal kind-3 target authority, wrong kind-4 field owner/key, forged bridge identities and out-of-scope constant-formal sources. Recomputed checksums do not repair semantic bindings. |
| Re-emission/remapping | Reorder Modules, types, formals, methods, constants, origins, private arrays and all witness/binding ranges. Rebuild every typed map and expression target; preserve payload bytes resembling tokens. Divergent shared contexts clone or reject, not merge by shape. |
| Private/Standard isolation | Reject unsupported 92–97, partial earlier 98 selections, wrong nested selections and raw Standard input passed as TIG1. Independently assert FieldMove 42 and checked-reference 43–46, including remapped nested operands; never retry the other revision-local 42 meaning. Standard readers do not accept private TypeSpec 10, runtime-policy 1, source tag 4, sidecars or private code links as current Standard additions. |
| Packages/sets/graphs | Retain semantic-only provider edges, private target/default origins and distinct actual imports at root/intermediate/leaf boundaries. Test diamond expectations and incompatible replacement before image load. AOT and root-only JIT preserve provider images; missing ABI/code/lifetime support declines affected execution. |

Implementation proceeds only through these explicit gates:

1. **Typed semantic model and source admission:** materialize nominal
   declaration/member/formal identities and normalized contracts, not token
   strings; implement checked closed witness construction separately.
   Reject currently unrepresented source member/access/contract forms.
2. **P97-R prerequisite composition:** implement and qualify its actual
   complete deltas. Current 92 is not evidence that these prerequisites
   exist; no reduced 98 is permitted.
3. **Private component and reference lifecycle:** implement exact P98-I1
   readers/writers, all required private records, bodyless expectations and
   source-free provider binding through public compiler APIs.
4. **Shared graph preservation:** integrate traversal, verification,
   substitution, set/graph merging, replacement, remapping and re-emission;
   include independent-provider and malformed fixtures, not only a
   same-writer round trip.
5. **Actual default/call execution:** supply each required closed program/
   entry, then qualify supported witness calls and default origins through
   ordinary lowering. Serialization or declaration binding is not execution.
6. **Native deployment:** extend semantic-edge/snapshot classification and
   qualify packages, sets, graphs, unchanged-image replacement and
   root-only JIT only for separately admitted ABI/lifetime profiles.
7. **Standard integration, separately:** adopt exact missing Standard
   identity/origin/admission/constant-formal/code/evidence contracts in their
   owners and qualify independent `.trrm` producers/readers before making
   any Standard interface/witness interchange claim.

No remaining wire choice inside this bounded candidate is delegated to an
implementer. Wider source contracts, open witness/projection/existential
carriers, general predicate/constant programs and runtime dispatch/ABI are
explicit non-goals or separate contract gates, not silently approved
extensions of P98-I1.
