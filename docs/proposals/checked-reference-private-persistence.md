# Complete checked references and implicit adaptation in private persistence

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** The governing authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This is a private encoding decision, not source approval, compiler support,
runtime qualification, a Standard TRRM allocation or a stable public ABI.
It persists a program, not a suspended process or live reference bits.

## Decision, scope and owners

Replace this owner's earlier **unselected revision-97 reference candidate**
with one exact **private i32 revision-105 candidate**, based on the inspected
complete revision-104 codec plus the explicitly reconciled deltas below.
Revision 104 remains the implemented selection; this document implements or
approves neither revision.

Use one shared recursive reference/optional type table everywhere a complete
type occurs, not just in function pointers. Retain declaration-owned checked
normal-publication and implicit-parameter facts after Function erasure.
Preserve reusable implicit-argument backing with an explicit materialization
expression at the original argument point, rather than dropping call metadata
or hoisting evaluation. Typed places and checked operations preserve descriptor
storage, referent storage and initialization authority separately.

This companion owns the complete **105 composition and reference/adaptation
deltas**. The
[complete pointer-contract proposal](native-function-pointer-contract-private-persistence.md)
continues to own signature rows, conventions, invocation identity,
`fnptr#i` and original address-contract references. Its row semantics are
explicitly composed here, not inherited from its proposed header number.
This document supersedes the earlier reference candidate's header, directory
placement, tag assignments and materialization expansion, and the
corresponding boundary in
[checked function-pointer outputs](function-pointer-checked-output-parameters.md#private-persistence-owner-and-explicit-next-decision).
It does not duplicate the Standard/register proposal or redesign Core APIs.

| Owner | Contract preserved |
|---|---|
| [Grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments), [complete types](../specs/language/types-and-normalization.md) | Complete constructors, qualification at each node, optional normalization and exact substitution; `mut` is a binding permission, not a type. No source production changes. |
| [References](../specs/language/references.md) | Explicit formation/indirection, ordinary versus initialization reference, fixed destination, scoped reborrow, one-shot attenuation and descriptor/referent separation. |
| [Dependencies](../specs/language/dependencies-and-views.md), [lifetimes](../specs/lifetimes.md) | Actual source/storage generations, source-storage versus forwarded backing, every supporting obligation and independent endpoint. |
| [Flow](../specs/language/initialization-and-flow.md), [ownership](../specs/language/ownership-and-cleanup.md) | Vacancy, complete/partial/maybe-live/reserved states, outcome correlations, actual holders, replacement and distinct normal-retirement/failed-construction orders. |
| [Provenance carrier](reference-provenance-carriers.md#private-exchange-candidate-revision-93), [field origins](reference-field-value-provenance.md#necessary-private-transport-candidate-95) | One operation-derived ledger; declaration-owned general Dependency rows and separate copy-origin guarantees. No serialized verifier-instance graph. |
| [Stored descriptor candidate](reference-stored-descriptor-layout.md) | Only its externally bounded Windows x64 8/8 ordinary-reference route and separate ordinary direct-value agreement; no stored init-capability layout. |
| [Normal backing](reference-normal-backing-support.md) | Separate normal-only 16/8 agreement, explicit host selection, actual normal retention control and one regenerated runtime closure. This private codec selects neither its propagation nor its support protocol. |
| [Integrated GC boundary](integrated-gc-runtime-boundary.md), [common ABI](integrated-gc-runtime-abi.md#values-cell-identity-and-support) | One integrated runtime/domain interface with interchangeable collectors. Its GC locators, stable 16/8 envelopes, PinCell support, bootstrap and JIT/AOT admission are separate. The superseded RootPin DLL/path/table architecture is not a prerequisite or fallback. |
| [Persistence](../specs/representation/persistence.md), [versioning](../specs/representation/format-versioning.md), [binding](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md) | Exact selected private schema, original expectations, safe structural consumption and truthful producers; no required consumer semantic re-proof. |

### Why existing carriers do not suffice

These are inspected source facts, not newly executed tests:

* [`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  and [`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs)
  now have `CheckedReferenceTypes`; the
  [host type carrier](../../sources/Terrum.CodeAnalysis/Semantics/CheckedReferenceTypes.cs)
  uses negative identities and referent/write/gc/init/optional facts.
  Formation, reborrow, read and write nodes already exist. This is not a
  private table: `Module` and `ModuleReader` lack that collection, and the
  writer's expression switch has no checked-reference cases.
* [`ModuleCompiler`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs)
  writes exact **104**, and `ReadModuleHeader` rejects every other revision.
  `WritePrivateType`/`GetType` still reject scalar identities 39..46;
  signature rows are shape-only, nonempty invocation contracts and general
  dependencies refuse, and no copy-origin/nominal-origin/enum directory is
  written. Variables store name/type/mut/out/init/span, losing
  initialization-local and adaptation facts. The writer explicitly refuses
  `IsImplicitRef` or `IsImplicitReferenceTemporary` near line 5298.
  Expression tags **42–48 are already allocated**, not available for the
  earlier reference candidate; raw pointer tags 38–40 and NativeOutput tag
  41 are not checked-reference operations.
* The current `Contract` already writes `DirectNativeImport`,
  `DirectNativeParameter` and `DirectNativeOutput` fingerprint tokens,
  including numeric convention/resolution and full uint input extents.
  `RestoreDirectNativeImportSettings` parses the settings for a stored
  Function, and compatibility compares the retained fingerprint.
  The earlier proposal's assertion that the descriptor simply disappears is
  not the current 104 source fact. These strings still need exact
  grammar-aware reconstruction/type remapping; they are not the proposed
  dedicated import-only record. The selected replacement below prevents
  dual descriptor authorities in 105.
* [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) still
  represents its narrower implicit-reference prototype with an underlying-T
  formal flag and `BoundCall.ArgumentImplicitReferenceTemporaries`.
  `AddImplicitReferenceTemporary` does not publish the accepted reusable
  `init mut T` permission. A producer must complete checked elaboration into
  the canonical descriptor/signature/body carriers below; retyping those
  prototypes or clearing their flags is not preservation.
* `ModuleCompiler.Contract`/`AppendTypeName`, declaration projection, type-table merging,
  remappers and the separate
  [set](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs)/
  [graph](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs)
  paths need the same closure. `RecordedImportPromises` overlays selected
  flags on a replacement; it cannot recreate an erased expected type.
* [`BoundTreeTraversal`](../../sources/Terrum.CodeAnalysis/Semantics/BoundTreeTraversal.cs)
  walks expression children and rewrites fields/variables/native annotations.
  Merely adding an unvisited place object or table would lose its children,
  inner types and provenance on those paths.
* [`NativeModuleCompiler`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs)
  and its set/graph counterparts use outer version **3** and separately store
  direct-import expectations.
  Its native parameter type fields belong to the importing inner Contract.
  Those fields cannot be resolved in the replacement provider's type table.

The [module inventory](../implementation/modules.md) records the implemented
104 chain: 92 output/NoThrow retirement, 99 storage-address widths, 100 arrays,
101 ancestor paths, 102 Repeat/field paths, 103 layout controls and 104 wide
inline geometry. It expressly does **not** compose proposed 93–98.
The active
[implicit-reference](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ImplicitReferenceParameterTests.cs)
tests exercise direct loans, staging and persistence refusals;
[checked-reference tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/CheckedReferenceTypeTests.cs)
exercise source/type behavior. Neither establishes this codec.
[`TrRmExecutableCompiler`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
also refuses implicit parameters/temporaries near line 414; that separate
producer is not fixed by a private schema decision. Standard Param mode
`00`/flag `01` already constrains implicit adaptation to ordinary readonly
outer `ref<T>`; its remaining use-site elaboration is separate.
The [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
has no Compile exclusions. No execution result is claimed here.

### Minimal extension versus redesign

| Candidate | Assessment |
|---|---|
| **Supplement existing i32 identities with recursive reference/optional rows — selected** | Keeps existing complete scalar/nominal/pointer/signature carriers and adds only the missing constructors and checked operations. Requires one shared resolver and closure traversal across all type positions, not another fnptr-only flag. |
| Replace every private type with a universal TypeDef/TypeSpec/generic-context graph | Materially useful for open generic definitions, constructed nominal identities, witnesses and wider owner families. It would migrate every declaration, contract string and type-bearing operation, or adopt an explicitly selected Standard graph as private IR. That broader ownership/context redesign is not needed to preserve references over already representable complete inner types. It is not prohibited by draft compatibility. |
| Inline recursive variable-length types at every i32 occurrence | Viable only after changing every type-field extent, including outer expectation joins. Repeats large nested signatures and needs a second textual/bodyless strategy. Not selected. |
| Reuse pointer codes, Standard TypeSpec bytes, legacy init flags or only serialize provenance | Loses constructor, authority, place or protocol identity. Rejected; equal width and a familiar revision byte supply no codec. |

Selecting 105 avoids conflating this integration with implemented 104 or
the independently unselected 97/98 designs. The number is a concrete private
candidate, not a stability claim or an additive-only compatibility rule.
No historical reader, optional trailer or second spelling is selected.

## Exact schema composition

Select out of band the whole draft
**`terrum/private-semantic-105-checked-references-1`**, abbreviated **B+C+R**:
the inspected **B=104**, the explicitly selected prior field/semantic deltas
**C**, and this reference/adaptation delta **R**. This is neither 104 with
optional additions nor the old 97 stream with a new header. The following
table is the exhaustive composition; referenced owners supply only their
named deltas, not automatic later amendments or numerical inheritance.

| Component | Required meaning |
|---|---|
| **B: complete implemented 104** | [104 owner](wide-fixed-layout-private-persistence.md#exact-private-104-delta): retain header/profile/runtime/kind/grant/buffers/entry, struct/class/import tables, all current Function fields, statements and admitted Body forms. Preserve resolved arithmetic, NoThrow retirement, sole NativeOutput collection/tag 41, tag-35 three u64 extents plus mandatory ancestor count/list, and tag 42 FieldMove. Preserve tags 43–46 flat arrays, 47 Repeat, 48 PathWrite, the mandatory **44-byte** fixed-array table and 103 packing/alignment controls. Inline struct size and field offset/size remain **u64**; class geometry remains **i32**. |
| **C: declaration dependencies** | [93 delta](reference-provenance-carriers.md#private-exchange-candidate-revision-93): mandatory general Dependency count and 12-byte rows, disjoint from NativeOutput's phase-1 rows. B does not already contain this field. |
| **C: scalar payloads** | [94 delta](scalar-wide-value-carriers.md#exact-private-schema-and-composition): exact 24-scalar identities and type-derived tag-1 payloads of 0/1/2/4/8/16 bytes, replacing B's unconditional i64 even for narrow scalars. Preserve that owner's separately admitted non-scalar cases and exclusions; no nint/nuint codec is selected. |
| **C: copy origins** | [95 delta](reference-field-value-provenance.md#necessary-private-transport-candidate-95): mandatory summary byte and conditional path/binding payload, with its existing result/fresh-inline-construction applicability. |
| **C: nominal/foreign identities** | [96 delta](native-output-foreign-interface-private-persistence.md#candidate-binary-directory): Binding, nominal origins, foreign interfaces/member variant and mandatory target discriminator on every tag-5 call. Directory placement is reconciled below, not taken from its pre-array layout. |
| **C: complete pointer/invocation contracts** | [Convention](native-calling-convention-private-persistence.md#exact-composition-and-selection), [resolution](native-import-resolution-private-persistence.md#exact-revision-selection) and [pointer row](native-function-pointer-contract-private-persistence.md#exact-candidate-row-and-reference-layout) deltas: exact tokens, foreign-member convention-byte removal, complete invocation vectors and `fnptr#i`/address-contract roots. Include the [foreign-invocation amendment](standard-native-import-function-pointer-contracts.md#private-persistence-interaction): mandatory NativeParameter vectors, raw OutputState applicability and tag-41 indirect-call context replace the pointer owner's raw-output exclusion. Each signature row is exactly **`29 + 4*n + 12*(s+o+d+p)` bytes**. No B shape-only row survives. |
| **C: direct expectations** | [Import-only descriptor](native-import-semantic-provider-expectations.md#candidate-record-and-validity): mandatory presence byte, original target/input vector and full **u32** direct-input extents in owned, expected and corresponding outer fields. It replaces B's direct-descriptor fingerprint tokens under the rule below; it is not composed alongside them. |
| **C: enums** | [Enum delta](nominal-enum-persistence.md#exact-directory-and-typevalue-references): mandatory enum directory/origins, `356+i` identities, `enum#i` atoms and four-byte enum constants. This adds no source-interface/witness codec. |
| **R: this companion** | Mandatory complete type table and reference-origin closure, declaration checked publication, variable flags, typed places and expression tags **49–54**, including explicit reusable materialization. All other B fields retain their widths/order/meaning unless the table above explicitly replaces them. |

The header is `TRRM` plus little-endian **i32 105** (`69 00 00 00` at
offset 4), not Standard u16 major/minor. All C/R prefixes occur even with
zero rows, no references, no constants or no new Body forms.
All writers, ordinary/graph-library readers, nested Contracts, projections,
snapshots and producer validators must select this identical composition.
Referenced documents identify the ingredients; later edits do not silently
change the exact selected draft.

Reject other inner revisions and unsupported schema selections before
record decoding, including implemented 104 and every earlier 97 candidate.
Do not dispatch on `>=104`, infer a layout from counts, try an earlier reader,
patch a header or fill omitted fields from defaults. Equal bytes cannot reveal the provenance
of every dishonestly relabelled stream that happens to be structurally valid.

Keep the existing **TNPK/TNSP/TNGP outer version-3 fields**, including their
mandatory direct-expectation tables and hashes. Apply C's exact convention/
resolution interpretation and full u32 direct-input extent semantics; R adds
no outer field. Every embedded structured Contract in this selected closure
must select 105. The joint selection is **outer 3 / inner 105 / B+C+R**;
outer 2, outer 3/104 and earlier outer-3/97 compositions are not aliases.
A native-profile string, checksum or outer version does not identify the
inner schema or a runtime agreement.

### Reconciled directory and operation allocation

The top-level order is exactly:

```text
105 header/profile/kind/runtime/grant/invocation buffers/entry
104 Structures (u64 inline geometry and existing layout controls)
104 Classes (i32 geometry)
104 ImportedStructures
C complete NativeFunctionPointerSignatures
104 NativePointerTypes
104 NativeFixedArrayTypes (i32 count, 44-byte rows)
R completeReferenceTypes (i32 count, 8-byte rows)
C nominalTypeOrigins
C EnumDefinitions
C foreignInterfaces
ModuleFunctions (composed declaration order below)
```

All counts are mandatory. Earlier binary type uses may forward-reference
these directories; safe closure validation completes before use. Do not
eagerly validate a pointer/signature against an incomplete type universe.
Current 104 structural/array geometry checks still apply after resolution.
Origin keys and eligibility are not recovered from names or layouts.

In the private expression namespace, **1–48 keep B's allocations** except
the explicitly selected scalar/tag-5/tag-41 interpretation deltas.
In particular, 42 is FieldMove, 43 Initialization, 44 Element, 45
PointerAddress, 46 Write, 47 Repeat and 48 PathWrite. R uses **49–54**;
statement tags **1–27 remain unchanged**. The earlier reference tags 42–45
and statement 28 are retired candidate choices, not decoder aliases.
Scalar identity 42, expression tag 42, place kind and Standard opcode
namespaces remain independent.

Existing `native-10`..`native-20` labels retain their non-R meanings.
Type-closure/profile discovery must visit the new inner types and retained
contracts; the label alone never admits R, normal backing or integrated GC.
Source interfaces/witnesses (candidate 98), general open contexts, additional
runtime programs and other unlisted candidates are **not** composed.

### Direct-descriptor authority reconciled with 104

For 105, remove B's `|DirectNativeImport:...`,
`|DirectNativeParameter:...` and `|DirectNativeOutput:...` tokens completely.
They are not aliases. Use C's exact `NativeImportResolution` then
`NativeImportCallConv` tokens for normalized resolution/convention, the
unchanged owned Function descriptor for an owned direct import, and C's
mandatory expected-direct descriptor for a declaration-only consumer import.
NativeOutput's declaration collection remains the sole output-state authority.
Every original old output condition/failure fact must normalize truthfully to
that collection or make transport unsupported; removing a fingerprint is not
permission to discard an obligation.

Keeping the full 104 descriptor grammar instead is a viable smaller byte
change, but requires complete bodyless parsing, typed nested-atom discovery/
remapping and restoration inside the 1,024-byte Contract bound. Select the
already-proposed typed import-only record instead: it gives the same typed
vector/closure discipline as owned inputs and outer expectations without a
second general type/name parser. This is a chosen replacement, not a claim
that current 104 carries no descriptor facts. Normalize each original
expectation before erasure; never obtain its missing fields from a provider.

The selected import-only record is exactly that owner's presence byte,
two i32-length Strings, i32 count and 14-byte input rows
`i32 formalOrdinal, i32 pointeeType, u8 access, u8 retention, u32 extent`.
Absence is one byte; presence is
`13 + libraryUtf8Length + exportUtf8Length + 14*n` bytes. Its role/presence
checks, original spelling, bounded Windows name comparison, ordinal/export
identity and typed owner rules remain unchanged. There is no optional
104-token variant, reconstructed numeric default or outer-only expectation.

### Owner amendments and preserved contracts

This document is the composition owner to amend now. The pointer companion
link, checked-output boundary, direct-expectation source note and proposal
index identify 105 instead of the superseded reference-97 candidate.
The named 93–97/enum/direct-expectation
owners continue to define their selected field semantics; their historical
header/placement instructions do not govern 105. The 104 owner still defines
the implemented baseline, not reference support.

After implementation, reconcile `docs\implementation\modules.md`,
`docs\implementation\view-and-owner-profiles.md` and
`docs\implementation\conformance-gaps.md` against actual evidence. Do not
update them to claim support during this proposal-only task.
Grammar, references, types/normalization, dependencies, lifetimes, ownership,
reader trust, binding/replacement and persistence/versioning need **no
normative change** for this private encoding. The stored, normal-backed and
integrated-GC proposals remain the owners of any later physical/runtime
amendment; this decision selects none of their missing private crossings.

## Shared recursive complete types

### Table, codes and namespaces

Immediately after the complete **104 `NativeFixedArrayTypes` table** and
**before `nominalTypeOriginCount`**, insert:

```text
i32 completeReferenceTypeCount
repeat completeReferenceTypeCount:
    u8 constructorKind
    u8 qualifiers
    u16 reserved = 0
    i32 innerType
```

Every integer is little-endian. Each row is exactly **8 bytes**; the table
is exactly **`4 + 8*c` bytes**. There is no row length, owner index, runtime
address, layout, state bitmap or padding.

Row index `i` has private i32 type code **`-1-i`**, for
`0 <= i < c <= 100,000`. Codes `-1..-100,000` are possible only when their
row exists; other negative codes, including `int.MinValue`, reject. Decode
with checked wide arithmetic, not `Abs(i32)` or unchecked negation.

| constructorKind | Complete constructor | qualifiers |
|---:|---|---|
| `1` | Ordinary checked `ref<innerType>` | `00` readonly stable; `01` outer write; `02` gc locator; `03` outer write plus gc locator |
| `2` | Checked `ref<init innerType>` | Exactly `00` or `01` outer write; gc and other bits reject |
| `3` | Optional of the complete inner type | Exactly zero |

Zero/unknown constructors and other qualifier bits reject. `init` belongs
to constructor 2, not to an arbitrary inner value or binding mode.
Qualification inside `innerType` remains at that node. Pin support is a
dependency, not another qualifier bit.

`innerType` uses the **same containing Module's complete i32 type universe**:
it can recursively name these rows, an existing scalar/nominal type,
native-pointer row, complete pointer signature or fixed-array recipe.
Forward references are
allowed; complete closure validation precedes use. No Standard TypeSpecRef,
token or DefId is accepted in this field.

Nonnegative codes retain B+C's exact identities and restrictions: inline
structs `100..355`, enums `356..1023`, existing managed-qualified ranges
`1024..2047`, function pointers `2048..(1<<29)-1`, fixed arrays
`(1<<29)..(1<<30)-1`, and native pointers beginning at `1<<30`.
Each family requires its actual in-range row; its whole numeric band is
not automatically admitted. Fixed-array rows keep exact u64 N/S/B/A/R and
their metadata-only versus runtime-use restrictions. A reference constructor
does not turn a recipe-only nested array, ptr-to-array or unassigned layout
into a borrowable place. Reserved/retired scalars and out-of-range table
references do not become valid leaves. The negative domain is disjoint
from every B+C family and does not depend on a future host enum.

Use the existing code when it already denotes a normalized optional identity
in B's bounded managed catalog. Constructor 3 is not a second encoding of
that identity; reject such redundant rows. Also reject an Optional whose
inner type is already Optional, including those pre-existing optional codes.
Normalize optional construction before writing. Presence layers at distinct
positions remain distinct.

The host's current `IsOptional` bit on a reference definition is **not** a
wire qualifier. Normalize it into constructor 3 around the exact nonoptional
reference row, then map every use into this table. An internal negative
index is not necessarily the serialized negative index. This mapping also
applies to imported references, fields, optional projections and signatures.
No `write` bit is invented for inline scalar/ordinary struct values; legal
inner access stays in its actual admitted constructor or nominal contract.
Outer gc-init is invalid, while an inner `gc C` would require its own
complete class/backing codec, not a gc bit on constructor 2.

The following table illustrates **candidate wire identities over accepted
type forms**, not runnable source or a representation claim:

| Index/code | kind, qualifiers, innerType | Complete identity |
|---|---|---|
| `0 / -1` | `1, 00, 2` | `ref<int32>` |
| `1 / -2` | `1, 01, 2` | `write ref<int32>` |
| `2 / -3` | `2, 00, 2` | `ref<init int32>` |
| `3 / -4` | `3, 00, -1` | `ref<int32>?` |
| `4 / -5` | `1, 01, -2` | `write ref<write ref<int32>>` |
| `5 / -6` | `2, 01, -4` | `write ref<init ref<int32>?>` |
| `6 / -7` | `3, 00, 2` | `int32?` |
| `7 / -8` | `1, 00, -7` | `ref<int32?>` |

The first row bytes are `01 00 00 00 02 00 00 00`; code -1 is
`FF FF FF FF`. A pointer to int32, a reference to its pointer slot and a
reference to int32 have different constructor trees regardless of layout.
Naming an optional scalar does not assign that scalar's optional layout.

### Every type occurrence and textual expectations

Use these codes in all binary complete-type fields: fields, Function result,
variables/formals, expression envelopes, pointer pointees, signature
parameters/results, publication subjects derived from those types, native
input/output annotations and reservations, summaries and restored references.
Validate each use's separate eligibility; a raw output does not become
eligible merely because its pointee code resolves.

At every grammatical textual type position use exactly **`ctype#i`** for
a row in this table. It occupies `6 + decimalDigitCount(i)` ASCII bytes in
the existing i32-length strict-UTF-8 String. Decimal is unsigned, in range,
without whitespace, sign or leading zero except `0`. It is not source syntax.
Old textual `ref<...>`/mode-only descriptions are not aliases. `fnptr#i`
and `enum#i` retain their separate table namespaces; nested existing pointer
productions can contain `ctype#i`.

Parse atoms to typed references before comparison/remapping. Equal indices
or strings across Modules do not mean equal types; reordered indices can
denote equal types. Never replace matching text in names, entry symbols,
ordinary strings or source data. Retain the complete reachable table before
erasing an imported Function, even for an unused formal/field/result.

Expand candidate 96's **nominal-origin closure requirement**, not its Binding
codec, to every nominal type reached by these retained reference types,
typed-place owner paths (even when the final referent is scalar), and
recursively required field/type contracts.
Struct/enum origins retain C's domains. For a class record needed by this
closure, additionally admit its canonical readonly nonoptional private
code `1024 + classIndex` as `nominalTypeOrigin.privateType`; other qualified
class codes resolve through that one origin using the existing class index.
Require at most 256 class records and one origin per canonical class in each
using context. This extends semantic identity, not class layout/runtime
support or the meaning of image-private managed prototypes.

Binding supplies the actual using/selected Module, declaration/export key
and original expectation. Names, field offsets or identical class payloads
cannot manufacture them. A required unavailable nominal export/origin makes
transport unsupported. Preserve separate expectations selecting one provider;
clone a shared physical closure if its using contexts differ.

### Substitution boundary

Substitute complete terms, not payload codes or passing flags. For a generic
helper instantiated with an admitted T, retain T's full constructor tree;
T=`ref<U>` makes an init destination's inner type that descriptor, not U.
Normalize after substitution, retaining inner/outer write, optional position,
nominal origin and every dependency/formal correspondence. Revalidate actual
formation, constraints, state, representation and access.

B+C has **no open generic-formal inventory or general constructed-nominal
definition/argument codec**. This companion does not invent one. It supports
closed substitution when every resulting leaf and the selected closed
declaration already have complete admitted carriers. An unresolved formal
must remain identified by its original declaration/kind/ordinal in the
compiler; it cannot be written as its name, `void`, one observed
specialization or a provider-chosen type.

Refuse private transport of an open expected declaration, unresolved
owner/witness context or constructed nominal whose original definition and
ordered arguments cannot be retained. Do not replace an imported generic
expectation with a newly invented nongeneric export or discard an original
generic dependency after specialization. Open generic interchange remains a
separate general codec gate, not a restriction on accepted reference syntax.

## Declarations, formals and normal publication

### One checked publication authority per declaration

On **every ModuleFunction**, immediately after the complete revision-93
general-dependency vector and **before the revision-95 summary byte**, insert:

```text
i32 checkedSelectorCount
SelectorIdentity checkedSelector[checkedSelectorCount]
i32 checkedOutputStateCount
CheckedOutputState checkedOutputState[checkedOutputStateCount]
```

Reuse exactly the pointer owner's **12-byte** `SelectorIdentity` and
**12-byte** `CheckedOutputState` records and bounded test meanings, not
Standard graph rows:

```text
SelectorIdentity:
    u8 phaseTag = 3
    u8 flags = 0
    u16 reserved = 0
    u32 phaseLocalOrdinal
    u32 nodeKind                    // 1 true, 2 false, 3 nonzero, 4 zero,
                                   // 5 nonnegative, 6 negative
CheckedOutputState:
    u32 formalOrdinal
    u32 selectorOrdinal
    u8 state                       // 0 wholly vacant, 1 complete
    u8 reserved[3] = 0
```

The new declaration payload is exactly **`8 + 12*(s+o)` bytes**, including
empty vectors. Its implicit owner is that exact ModuleFunction. Selectors
have contiguous unique logical ordinals; references select that owner's
attachments, not the signature table's, another declaration's or Standard
PredicateGraph rows. Preserve retained unreferenced attachments.

Require the exact bool/int32/uint32 result/test pairs, unique output/selector
keys, valid init-reference formal types and the checked-output proposal's
total, consistent normal-state relation. Overlaps must agree and nonempty
conditional rows must cover the result domain. No rows means the published
unconditional-complete contract, not an inferred Boolean default.
Producers normalize bare Boolean defaults to their explicit true/complete
and false/vacant pair. Readers never infer rows from a name, body or edition.

These vectors carry **checked outputs only**. NativeOutput remains solely
in 92's role/output collection, and its raw selector projection into pointer
types remains governed by the foreign-invocation amendment. Foreign/direct
native declarations without an admitted checked-reference ABI have zero new
vectors; adding rows cannot make their signature executable.
All checked output dependencies belong in 93's general vector. Do not
duplicate raw phase-1 rows there or duplicate general rows in this payload.

If a declaration also has an admitted address contract, cross-check its
types, checked publication and applicable dependencies against the original
`NativeAddressContract:fnptr#i` row under the pointer owner's rules. That
reference still denotes the declaration's original exact contract, not a
projected use. Independent pointer types need no target. Restoring a bodyless
expectation retains its own vectors/types rather than copying the provider's.

Thus the composed declaration order is:

```text
existing Name / Contract / Import / hasFunction / HasOwnershipTransfer
92 OutputContracts
93 general Dependency collection
R checked selectors and checked OutputStates
95 copy-origin summary
96 foreign-member discriminator and conditional variant, as amended by 97
existing expected-vtable Boolean and conditional record
97 expected-direct-import Boolean and conditional descriptor
conditional stored Function
```

### Variable records and source-call adaptation

In every stored Function variable record, add one mandatory byte immediately
before its existing span:

```text
String name
i32 completeType
Boolean isMutable
Boolean legacyIsOut
Boolean legacyIsInit
u8 declarationFlags
i32 spanStart
i32 spanLength
```

The extent is **`20 + nameUtf8ByteLength` bytes**. Flags are
`01=InitializationCapableLocal`, `02=ImplicitReferenceAdaptation`,
`04=ReusableImplicitReferenceBacking`; all other bits reject.
Bit 01 is valid only on a local declaration, not a
formal/receiver; it distinguishes `init T` storage from a fixed binding even
when an initializer is present. `isMutable` independently permits rebinding/
replacement. State is reconstructed from operations, not this permission.
Bit 02 is valid only on an ordinary readonly, nongc outer reference formal.
An init-reference formal is a live fixed descriptor, not a vacant variable,
and must have `isMutable=false`.

Bit 04 is valid only on a compiler-hidden ordinary local of exact stored T,
with bit 01 and `isMutable=true`, never bit 02 or either legacy mode.
Its declaration is the reusable `init mut T` backing site in the already
existing supporting scope. Statement 24 establishes vacancy once per
activation without evaluating a value; expression 54 performs each reached
assignment/borrow. This is a declaration/operation relationship, not a
serialized live bit or permission to keep every old value. Independent
sites require independent locals even when T or spans agree.
Require one owning source-scope block with its vacancy declaration in that
activation's declaration prefix, before every corresponding materialization.
Do not declare/reset it inside a per-call conditional or synthetic block.

For canonical checked-reference parameters the existing Contract parameter
production is **`in:Type`**, or exactly **`in:implicit:Type`** when bit 02
applies. Both have ordinary logical mode `00`; the extra token publishes
adaptation, not another overload/type identity. Retain it before Function
erasure and cross-check stored variables. Type is the complete type atom or
existing production. Local `mut` does not enter signature identity.

For complete checked-reference values, legacyIsOut/legacyIsInit are both
zero, and `out:`/`init:` are not alternate parameter encodings. Unchanged
legacy fields/tags retain only their separately admitted prototype roles;
they never decode to a canonical ref type. Required unsupported prototype
or source-call facts still refuse rather than being silently normalized.

The old underlying-T implicit prototype may be transformed only from its
complete checked declaration and use facts: create the actual `ref<T>`
formal, rewrite descriptor versus referent uses, replace each recorded
adaptation at its original argument point, and establish the hidden local's
accepted permissions/scope/cleanup. Reverify that transformed program.
The reader never performs this normalization or infers it from an IsImplicitRef
flag, an `in:T` token, a local name or a replacement provider.
This is checked internal elaboration, not approval of prototype transparent
indirection or a compatibility source form. If canonical source binding
cannot establish the actual complete descriptor/use contract, retain refusal.

The implicit marker is a declaration-owned **source-call contract**, not
structural function-pointer type identity. It survives bodyless direct
imports even though it does not distinguish overloads. The composed pointer
row keeps complete ordinary parameter types/modes and its invocation vectors;
an indirect call must not inspect the current target to infer implicit
adaptation. Any wider callable/lambda source view needing a separate
unrepresented adaptation/default/context contract remains gated.

Local/formal slot identity is the containing Function plus zero-based i32
variable index. Preserve the existing constructor receiver offset when mapping
explicit formal ordinals. Result subject ordinal is zero; it is not an
invented local or hidden ABI buffer. Fields are selected by their exact
nominal owner/substitution and declaration ordinal, not name or byte offset.
Formal ordinals include zero-data unit formals and exclude the receiver and
hidden result carriers; physical placement never renumbers them.
Labels/defaults do not establish formal identity or a missing carrier.

## Private Body: typed places and checked operations

These are **private structured-tree namespaces**, not Standard instructions,
Body records, map indexes or native pointer operations. Expression and
statement tags remain separate. Existing expression tags 1–48 and statement
tags 1–27 retain B+C's meanings. No new record directory is introduced.

### Place operands

Introduce this recursive operand grammar only at the new positions below:

```text
PrivatePlace:
    u8 placeKind
    case 1 Local:
        i32 variableIndex
    case 2 InlineField:
        i32 ownerType
        u32 instanceFieldOrdinal
        PrivatePlace receiverPlace
    case 3 ObjectField:
        i32 ownerType
        u32 instanceFieldOrdinal
        Expression receiverValue
    case 4 Referent:
        Expression descriptor
    case 5 FixedArrayElement:
        i32 arrayType
        PrivatePlace arrayPlace
        Expression index
    case 6 InvocationBufferElement:
        i32 bufferIndex
        Expression index
    case 7 LexicalViewElement:
        i32 variableIndex
        Expression index
```

Only kinds 1–7 are valid. Local is exactly 5 bytes; InlineField/ObjectField
are `9 + childExtent`; Referent is `1 + expressionExtent`;
FixedArrayElement is `5 + placeExtent + indexExpressionExtent`;
InvocationBufferElement/LexicalViewElement are `5 + indexExpressionExtent`.
There is no length prefix, place ID, cached access, initialized bit or
address payload.
Each place node consumes the shared depth/work budget.

Local selects that exact declared slot, including an explicit formal's
descriptor slot. InlineField requires the exact complete inline containing
type; ObjectField requires an independently admitted nonoptional class
handle/object mapping. `ownerType` selects the canonical nominal declaration
instance (nonoptional readonly class code for a class owner). Check receiver
type, complete substitution and immediate field owner, then derive the field
type and access through the path. Field order is the retained structural
instance-field order; static/computed members and offset-based guessing reject.
Caller-unsafe fields still require an actual enclosing unsafe block/expression;
no place field serializes authorization or waives that declaration contract.

Referent requires a live nonoptional ordinary stable reference or init
capability and derives its exact inner T. It selects T without reading it.
It is **not** dereference of ptr<T>, a Home containing T, or direct stable
access through a gc locator. Integrated-GC exact-place/relocation/pin
operations retain their separate missing private program/admission gate.

FixedArrayElement derives E, N and geometry from the exact retained array
type, requires its containing place to have that complete type, and evaluates
the array place before the index once. It uses existing index/range,
initialization, actual-alignment and backing obligations, not pointer
arithmetic. Nested steps preserve each complete ancestor. Only an already
admitted runtime backing/access route is usable; 104's metadata-only or
raw-address-only nested recipes gain no checked-reference authority.
Zero extent or unit elements supply no borrowed payload address.

InvocationBufferElement selects the containing Module's already retained,
explicitly admitted invocation-buffer binding and derives its element type
and access there. It adds neither buffer contents nor general host-buffer
persistence. LexicalViewElement selects an admitted local view and derives
the element/access/owner relation from its actual existing view contract;
it adds no general Span<T> codec. Missing buffer/view identity, source-grant,
backing or lifetime carriers remain refusals. These two forms preserve the
existing implicit-place alternatives without treating a value read as a
property-backed place or reusing raw address bits.

Receivers/descriptors/indices evaluate once in recursive source order.
Copying a descriptor field's value and referencing that field's slot remain
distinct.
A property/value-returning indexer, range, other element family or zero-extent
anchor lacking a selected place carrier is unsupported; do not disguise it as a Local,
invent an addressable byte or broaden a raw storage operation. This bounds
the place codec, not the source language or recursive type constructors.

### New operation schemas

ExpressionEnvelope remains exactly
`u8 kind, i32 completeType, i32 spanStart, i32 spanLength` (13 bytes).
StatementEnvelope is `u8 kind, i32 spanStart, i32 spanLength` (9 bytes).
All new fields below are mandatory, without flags or padding:

| Private namespace/tag | Payload after envelope | Exact extent |
|---|---|---|
| Expression **49 (`31`) ReferenceForm** | `u8 formationKind; PrivatePlace place` | `14 + placeExtent` |
| Expression **50 (`32`) PlaceRead** | `u8 useKind; PrivatePlace place` | `14 + placeExtent` |
| Expression **51 (`33`) PlaceIsInitialized** | `PrivatePlace place` | `13 + placeExtent` |
| Expression **52 (`34`) InitializationAttenuation** | `Expression descriptor` | `13 + childExtent` |
| Expression **53 (`35`) CheckedStore** | `u8 transition; PrivatePlace destination; Expression value` | `14 + placeExtent + valueExtent` |
| Expression **54 (`36`) ImplicitReferenceMaterialize** | `i32 backingVariableIndex; Expression value` | `17 + valueExtent` |

**ReferenceForm 49:** formationKind is `1=FreshPlace` or
`2=ScopedReborrow`; other values reject. The envelope must be a nonoptional
stable constructor-1 type with qualifiers 00/01 or a constructor-2 init type,
with exact inner type equal to the selected place. The complete result type
selects ordinary versus initialization authority; there is no IsInit flag.
FreshPlace checks the actual eligible place and its authority/state/backing.
ScopedReborrow must explicitly select an existing checked referent or an
admitted loan-bearing lexical view in its place path, preserving its actual
parent loan/reservation; it cannot hide a new reservation as fresh.
A place path containing Referent or a loan-bearing LexicalViewElement requires
ScopedReborrow, not FreshPlace. Ordinary formation requires complete live T; init
formation requires eligible whole vacancy, including no retained partial state.
The producer establishes explicit `ref init` formation or an existing init
reborrow; an expected type or implicit-parameter flag never creates it.
Outer write, actual extent/alignment and stable backing must be independently
available. Copy from an admitted packed descriptor slot does not make that
underaligned slot eligible for an aligned reference to the descriptor.

**PlaceRead 50:** useKind is `1=Copy`, `2=Transfer` or
`3=BorrowDescriptor`. The envelope type equals the place's complete stored
type. Copy requires its Copy contract and propagates value dependencies
without borrowing the descriptor slot. Transfer uses existing permitted
move/take rules, vacates the source only at their actual transfer point and
preserves its responsible holder/obligations. It grants no new arbitrary
field move or through-reference take authority.

BorrowDescriptor requires a live reference descriptor slot and is a
non-Copy operand use, not a separately storable reference value. Its result
can occur only as the descriptor operand of Referent or operation 52;
nesting those in operation 49 supplies the explicit child reborrow for a
call/local capability. It cannot itself be returned, stored, lifted to
optional, cast or passed as an ordinary value. In particular, reading a live
init descriptor does not read T or duplicate init authority.

**PlaceIsInitialized 51:** the envelope type is built-in bool. It tests the
selected place's state, not its value. Testing Local(r) tests r's binding;
testing Referent(BorrowDescriptor(r)) tests T. A false whole-partial test
does not prove vacancy; pending reserved caller storage is not observable.

**InitializationAttenuation 52:** source is a constructor-2 descriptor
operand, normally operation 50 BorrowDescriptor. Result is constructor 1
with exactly the same inner type and outer write, and no gc. Complete T
state must already be established. Consume only the one-time init
authority; do not rebind the source slot, retire T or acquire support.
The slot's declared type remains fixed; this is its explicit flow view.
Reusing a completed view is not another initialization permission.
Persist repeated ordinary uses through the checked completed view/result;
do not retag the original descriptor slot or create another reservation.
Separate readonly projection may then attenuate outer write; it cannot
replace this state transition.

**CheckedStore 53:** the envelope type is built-in void. Transition is
`1=Initialize`, `2=Replace` or `3=InitializeOrReplace`; other values reject.
Evaluate/capture the destination once,
then evaluate the value once. Its complete type must match the selected T
under existing explicit conversions, not by retagging. Initialize requires
eligible vacancy and init authority. Replace requires actual live state and
ordinary write authority, including preserved outer write after completion.
InitializeOrReplace is the existing init-mut assignment: require both
vacant-initialization and live-replacement authority, with only live/vacant
alternatives and supported conditional retirement. Inspect actual current state
at commitment, retire the old value only on live paths, and install the
once-staged value. Partial, pending or indeterminate state is not an eligible
vacant alternative. None writes the descriptor binding when the destination
is Referent.
Store propagates the new value's sources/access/backing into the actual
destination and preserves every surviving old/sibling/capture obligation.
No cached offset, pointer-size Copy, copy-back or default construction
substitutes for this operation.
It remains an expression so a checked write preserves admitted unit-valued
expression/call positions. Existing statement 4 wraps it at statement use;
there is no parallel statement-28 store encoding.

**ImplicitReferenceMaterialize 54:** backingVariableIndex selects that
Function's bit-04 local in its actual supporting scope. The envelope is a
nonoptional ordinary readonly stable `ref<T>` with exact T equal to the
local's stored type and the value expression's type. Evaluate the value once
at this operand's original source-order point, stage its complete value under
its actual Copy/transfer contract, and perform ordinary InitializeOrReplace
on the reusable local. Do not retire an old value until the new value is
complete; preserve permitted RHS mutations and actual partial-acquisition
cleanup on failure. Then form the readonly reference to that exact local.
This is the fused encoding of assignment followed by borrowing, not an
allocation, new output reservation or new assignment semantics.

A skipped operation changes no state. Later evaluation reuses the same
storage site, with value-generation changes and all surviving loans/backing
checked normally. A live old dependency can forbid replacement; no hidden
owner history or per-iteration allocation repairs it. A later argument/call
failure does not make the already-installed value a consumed by-value
argument: it remains under that local's ordinary scope/lifetime cleanup.
The operation adds no noescape, deep readonly, Copy-of-T, pin/root or
normal-backing acquisition policy. Independently unsupported T, scope,
ownership or runtime support refuses before persistence/use.

For widths, ReferenceForm of Local(0) is 19 bytes; a PlaceRead of that Local
is 19; its state test is 18. An init reborrow whose Referent operand is a
19-byte BorrowDescriptor is 34 bytes. A CheckedStore through that 20-byte
Referent with a 17-byte int32 constant is 51 bytes. Materializing that
constant is 34 bytes. These use C's four-byte int32 constant payload,
not B's earlier eight-byte payload.

### Existing operations, calls and supporting scopes

Existing typed local initialization/assignment, fields, optional construction,
returns and calls may carry these complete values under their own rules.
Tag 15 Null gains the constructor-3 optional type domain with **no payload**;
tag 16 lift and existing presence tests/pattern bindings preserve its exact
inner type. No tag-1 scalar/address constant is admitted for a checked
reference; typed absence is not zero initialization of a nonoptional descriptor.
Tag 19 readonly projection preserves inner type, optional position and
backing; it cannot remove init or gc.

Direct tag 5 keeps the complete 96 target discriminator and its source-order
argument/formal mapping. Indirect tag 37 keeps its target-then-count/ordered-
arguments framing and positional correspondence; it gains no named-argument
or generic-context encoding. Capture the target first and stage operands in
source order. Pass an init descriptor using a fresh formation or explicit
scoped reborrow, never tag 8/21 scratch output or tag 41 raw reservation.
Any required correspondence not expressible by those admitted call forms
refuses; do not reorder expressions to make types line up.

For a direct implicit parameter, elaborate the **checked chosen adaptation**:

| Actual at the source argument point | Persisted operand |
|---|---|
| Already compatible complete descriptor | Pass that descriptor with the admitted Copy/transfer/projection and all original dependencies; no new reference or temporary |
| Eligible exact T place | ReferenceForm 49 over the corresponding typed place, with readonly outer authority; a conflicting borrow rejects rather than becoming materialization |
| Exact T value | ImplicitReferenceMaterialize 54 over that once-evaluated value and its bit-04 reusable local |

The formal's bit 02/`in:implicit:` token preserves future independently
compiled source calls; it does not select an omitted adaptation at runtime.
Validate the current source-order argument/formal and backing-temporary vectors
against complete types and sites, emit the explicit operand at that same
argument index, then remove the now-represented temporary vector from the
canonical stored call. Any residual `ArgumentImplicitReferenceTemporaries`
fact is a producer error/refusal, not silently ignored metadata.

An implicit `ref<ref<U>>` may borrow the descriptor slot of a `ref<U>`;
it never flattens U or supplies inner write authority. Compatible stable
descriptors retain existing pin/backing dependencies without new acquisition.
Property/operator/conversion results use materialization only as values.
Neither site interning by T/span nor a pre-call statement prelude is selected:
the expression carrier preserves nesting, named-argument source order,
short-circuit/conditional non-evaluation and earlier-argument capture before
later rebinding, mutation or failure.

Reuse the existing block lexical-scope marker (`IsManagedLexicalScope` in
the host) and its byte placement. Binder already marks source lexical blocks
and the for-header wrapper independently of whether a managed local exists;
synthetic cleanup/grouping blocks do not create new supporting scopes.
Preserve that distinction, statement 24 vacancy declarations, lifetime-end/
drop operations and exact loop/branch/finally edges. A local's storage site
is its declaration occurrence in the actual supporting activation, not its
span or index alone. Re-entry/reuse creates the appropriate new generation;
an ordinary allowed Copy assignment need not end that storage lifetime.

The scope marker does not limit this obligation to managed locals: hidden
scalar, aggregate, descriptor and owner-bearing backing follow their actual
ordinary retirement contracts. While/do conditions use the enclosing block;
for-header sites use the existing header scope; original braced bodies use
each body activation. Declaration clause sites require their actual isolated
phase scope, not the body/call scope. An unrepresented phase, iteration-close,
suspension or other scope contract remains a refusal, not a new cleanup policy.

Reconstruct reservations, value generations, child loans, guarded output
states and correlation versions from these operations and declaration
contracts. No runtime source, StorageKey, CapabilityKey, verified bit,
root/pin token or acquisition journal is serialized. Uncompleted init
capabilities remain non-Copy, fixed-destination and non-escaping: fields,
results, captures, async state and opaque containers cannot retain them.
Ending a local descriptor releases its actual loan after child obligations
end; it does not destroy or vacate T.

## State, dependencies, failure and ownership

Instantiate declaration relationships against captured source values and
exact destination generations. General dependencies keep 93's twelve-byte
payload, source kinds/ordinals, explicit-empty versus absent distinction and
normal/every-applicable-exit/deferred phases. Selector phase 3 is not
deferred Dependency phase 3. No phase is activated by a matching number.

For a checked output, destination 2 denotes the published **inner T**,
not the descriptor slot or reserved output address. A reference source's
selected T storage is distinct from backing forwarded through its value.
ForwardsBacking cannot authorize returning the original ref under a promise
excluding that selected storage. Captures permits neither a write nor an
implicit owner retain, and mutation into an existing holder can survive
failure. Explicit inter-output sources require that exact source's live
publication on the relevant outcome; cyclic claims supply no backing.

The 95 CopyOrigin summary remains separate from those broad lifetime bounds.
It carries its exact normal-result/fresh-inline-construction entry-value or
entry-referent-value alternatives and ordered field paths; descriptor Copies
preserve those alternatives while a borrow of their slot preserves the slot
source. Unavailable is not empty or known independent provenance.
This composition does not invent copy-origin summaries for arbitrary checked
outputs, async/witness contexts or unsupported views. If a retained use needs
such an unrepresented guarantee, refuse transport/replacement until its
summary owner supplies the carrier; do not weaken it to broad DependsOn,
copy the provider's current summary or claim semantic re-proof repaired it.

| Boundary | Required outcome |
|---|---|
| Decode/write failure | Work in unpublished temporary compiler structures/streams; reject/discard the incomplete result. Do not publish partially resolved types, references, snapshots or images. Decoding performs no represented root/pin/loan acquisition or T cleanup. |
| Formation/staging failure before invocation | Settle only acquisitions/loans actually created; release an unentered child reborrow and resume its parent with the actual state. Earlier argument effects and consumed inputs are not rolled back. |
| Store RHS failure | The assignment has not retired the old destination. Preserve its actual current state, permitted RHS mutations and still-owned staged acquisitions; do not restore a snapshot. |
| Successful initialization/publication | Establish complete validity/backing and the declared normal state after required clauses, cleanup and epilogue. Init attenuation consumes only init authority. Publishing a descriptor initializes its slot, not its referent. |
| Conditional normal output | State 1 is complete T; state 0 is wholly vacant with no retained partial acquisition. Preserve the exact saved discriminator/place generation. Lost/invalidated correlation is not a vacant-state transition. |
| Exceptional completion after entry/postcondition failure | No normal result/output-state pair. Caller output state is indeterminate, not the false/vacant arm. Keep actual holders and surviving captures; no automatic rollback or invented cleanup of complete T. |
| Partial construction/retirement | Follow actual reverse acquisition for unpublished failed construction, without parent complete-value Cleanup. Complete normal retirement uses authored Cleanup then reverse declaration order. Preserve explicit finally behavior; no new universal exceptional Cleanup pass. |
| DoesNotReturn/divergence/failfast | Preserve all required types, operands, source obligations and contracts. No normal publication; divergence is not completion, and failfast adds no unwind guarantee. |

An ordinary descriptor Copy duplicates its dependent extent under the stored
layout candidate, not T, an owner, guard, pin or cleanup responsibility.
Descriptor rebinding does not retarget old copies; referencing its slot does.
Known backing, access, actual alignment, publication, affinity and code/load
obligations survive unsafe operations and every admitted adapter.
Readonly/write access asserts neither exclusivity nor NoAlias. Independently
required separation, guard or invalidation contracts need their own complete
admitted carrier; this encoding does not turn their absence into permission.

## Target, normal backing and integrated GC separation

Type-table bytes are not a physical descriptor. Apply the stored-layout
candidate only to ordinary stable descriptors in its explicitly selected
externally bounded Windows x64 rootless extension: 8/8, no descriptor-owned
runtime hold, and optional absence under that physical agreement.
It selects neither Blittable nor foreign eligibility by size.

Keep the three agreements distinct:

| Route | Physical/runtime owner; private boundary |
|---|---|
| Externally bounded ordinary stable references | [Stored descriptor](reference-stored-descriptor-layout.md): 8/8, one address and no descriptor-owned runtime hold. Its bounded ordinary direct-value call agreement is separate from foreign/init projections. |
| Normal-backed ordinary stable references | [Normal backing](reference-normal-backing-support.md): uniform 16/8, actual normal allocation control, nonallocating hold Copy/retirement and one explicitly selected regenerated normal-runtime closure. Its host key is not a private schema selector or serialized authorization. |
| Integrated GC | [Integrated boundary](integrated-gc-runtime-boundary.md) and [ABI](integrated-gc-runtime-abi.md#values-cell-identity-and-support): one runtime/domain and interchangeable collectors; exact-place RootCell locator 8/8, stable address/SupportCell envelope 16/8 and explicit PinCell support. No independently selected RootPin DLL/path/table service. |

One selected physical agreement is uniform for the complete type throughout
its closure, including zero-support values, fields and substitutions. Equal
source spelling, size or zero support does not merge these layouts or support
namespaces. There is no implicit 8-to-16 widening/truncation, owner lookup,
normal-hold-to-PinCell adapter or common-layout inference.

R's gc qualifier retains semantic identity but supplies no private GC
allocation/root/relocation/pin/barrier/frame program, runtime/domain identity,
bootstrap or admission carrier. Required GC use, including an unused
declaration's retained requirements, remains refused until that complete
private route is selected and supported. ReferenceForm 49/Referent cannot
implement `ref *root` by treating the locator as an address. The older
RootPinRuntimeDraft selector and F0/record-0D program remain separate prior
schema snapshots; they are not aliases for the common GC ABI or 105.

Likewise, the normal-backing direction is already concrete, but 105 adds
no `NativeLoweringAgreement` field, normal-provider selection or propagation
through persisted/native routes. A route needing that agreement remains gated
until the normal-backing owner extends its host-selection, reader/verifier,
layout/query, relowering and runtime-lifetime propagation consistently.
Changing a stamp or comparing sixteen-byte carriers cannot preserve retained
eight-byte layouts, and root-only graph JIT cannot jointly regenerate providers.
Refuse private transport where the selected agreement/retained facts cannot
be preserved. Do not misclassify required normal support as externally bounded,
declare the accepted source escape illegal, or repair it with a hidden owner.
Zero-extent nominal referents retain their separate anchor decision;
void has no borrowable payload. No new storage or general ABI is assigned
to `ref<init T>` or optional/container-wrapped uncompleted capabilities.

Ordinary direct-value calls and initialization of an ordinary descriptor
slot use only the stored-layout candidate's own bounded agreement.
The first same-runtime fnptr ABI and its checked-output addition retain their
separate payload/positive-extent restrictions; this type codec does **not**
widen them to general descriptor inputs/results or descriptor-valued outputs.
Foreign/native boundaries retain actual Blittable and invocation contracts.
No implicit marshalling, root, pin, owner retention, address-stability
extension, ptr/ref conversion or cross-profile adapter is selected.

## Binding, rewriting and native containers

Resolve each selected provider/export/owner first, then compare every
**original** expected declaration in its using Module/substitution context.
Exact dimensions include complete recursive type, access, optionality,
formal correspondence, field owner/order, output state/phase and retained
layout/ownership contract. Mutable descriptor cells remain invariant.
An admitted readonly value projection is not a replacement rule.

Compare the retained implicit declaration marker exactly in this bounded
private contract. It is not overload/type identity, but erasing or silently
adding source-call adaptation is not an assigned strengthening rule here.
Already-bound named calls keep their formal map independently of labels.
If an independently used facade needs original labels/defaults or a broader
source-call view absent from its carriers, refuse that use rather than
inventing names, positional calls or provider-derived defaults.

Apply directed source-bound/no-return/caller-domain satisfaction only under
their existing owners. Preserve exact checked state relations: changing
false/vacant to false/complete is not strengthening, because the old caller
may initialize presumed-vacant storage. Required copy-origin precision stays
separate from broad lifetime bounds. Retain any stronger provider fact
actually used instead of silently strengthening an imported view.

Before Function erasure, capture original `ctype#i`/`fnptr#i` roots,
publication vectors, implicit markers, general/output/copy-origin contracts, nominal origins
and address/direct-import expectations. Reference projection, restoration,
sets/diamonds and consumer/provider/graph snapshots preserve/check each
expectation even when no emitted call remains. A provider-derived type table
or overlay of flags is not restoration.

Implementation must update the shared bound representation, `BoundProgram`,
CompilationReference/private Module tables and declaration collections;
type classification, equality/substitution/interning; all expression/place
children and shared `NestedExpressions`/rewriters; mandatory producer
verification; projection, comparison, restoration and lowering together.
A place walker must expose each embedded receiver/descriptor/index expression
exactly once and visit its owner/array type, field identity, buffer binding
and view/backing variable as well. Materialization visits its value and
backing local, and rewriting preserves the local's owning scope. A new node
must not fall through a visitor as a leaf.

For re-emission:

1. Decode/validate the source selection, collect the full retained type and
   declaration closure, and build context-sensitive maps for nominal origins,
   structs/classes/enums, native pointers, signatures, fixed arrays and
   negative type rows.
   Resolve identities before deduplication. Never use an in-progress
   structural pair as successful equality.
2. Rewrite every binary type code and grammatical `ctype#i`, `fnptr#i`,
   `enum#i` atom, including annotation-only and bodyless roots. Clone rows
   when one physical row cannot preserve distinct using contexts.
3. Map fields by exact owner/substitution/declaration correspondence; rebuild
   PrivatePlace owner/ordinal/element paths, buffer/view bindings and 95
   summary paths. Map variables/materialization sites, actual call targets,
   source-order formal maps and publication/dependency
   subjects independently. Published logical selector ordinals survive
   physical movement; an intentional renumber updates every referring row.
4. Preserve source/lexical scope ownership and actual evaluation order.
   Reconstruct local ledgers for the transformed occurrences; never serialize
   or reuse stale activation/capability/correlation keys. Preserve actual
   cleanup, code/provider and supporting resource origins.
5. Re-encode inner Strings/counts and outer Contract lengths, offsets and
   SHA-256. Outer direct expectations join the **original importing**
   declaration using existing provider/key/bijection rules; resolve their
   type fields in that importing Contract's final universe and cross-check
   its import-only descriptor. No new outer type table or contract vector.
6. Regenerate affected images from final checked inputs under the selected
   layout/call/runtime agreement. A repaired checksum cannot repair an ABI
   mismatch. Stored-image AOT and root-only graph JIT remain distinct;
   root-only JIT does not rebuild providers or authorize joint regeneration
   of an independently precompiled boundary.

Cross-encoding to Standard separately rebuilds actual TypeSpecs, signature/
Param/field/Import owners, predicate graphs and attachments, Body maps,
Homes/places/records, code/block offsets and affected SourceMap references.
Private ordinals/codes are not Standard rows or DD flag bytes. This proposal
does not adopt those encodings, allocate IDs, enable general Standard
reference execution or resolve the prior RootPin/NativeAddress Body-0D
schema conflict. Standard Param mode 00/flag 01 remains its existing
structural contract; source use-site elaboration and successor integrated-GC
schema selection remain separate work. Debug/source data cannot carry
missing semantics.

## Bounds, cycles, canonical writing and trust

Retain B+C's 4 MiB artifact, 1,024-function, 256-slot, 64-call-argument,
1,024-byte String, 100,000-node/work and depth-128 private limits. Table rows,
place/materialization nodes, declaration vectors and their traversal share those budgets;
do not reset a budget per signature, child or imported use. These are private
limits, not source arity limits, Standard row capacities or public ABI minima.

Before allocation/consumption, use checked wide arithmetic for `4+8*c`,
the retained array table's `4+44*a`,
vector minima and `12*count`, `s*n`, path extents, String lengths, cumulative
bytes and remaining input. Checked output count is at most `n*s` as well as
the shared budget, with actual applicability more restrictive. All type/
formal/field/slot references must resolve in their specified namespace.
Reject truncation, unknown tags, nonzero reserved bits, invalid strict
Booleans/UTF-8, dangling references and trailing bytes.

Validate the finite structural graph across reference/optional, native-pointer,
signature and fixed-array edges, including pointer-mediated cycles and N=0
recipes. A shared acyclic DAG
is valid; compute/check its longest depth without letting memoization hide
an excessive path. Nominal declarations are anchors, not field expansion
inside type equality. Separately validate retained fields/layouts; nominal
recursion through references is not automatically a structural type cycle,
while an impossible by-value layout remains invalid.

Canonical writers normalize complete terms, enumerate retained roots in
declaration/field/formal/body order, and assign each family's destination
indexes from child-before-parent first-discovery order. Then emit the fixed
directory order above; cross-family forward references are intentional, not
permission to move tables. Preserve signature-local logical ordinals.
Intern only exactly equal complete types in the same resolved context and
rebase all uses. Ordinary readers may accept valid forward references,
physical permutations and duplicate equal reference/pointer/signature rows,
but never duplicate declaration occurrence keys, forbidden redundant Optional
forms or duplicate fully resolved fixed-array `(E,N)` identities.
There is no requirement to globally deduplicate equal types. Preserve distinct
original expectations regardless of type interning.

Build decoded immutable structures privately, validate references/closure/
roles/support and bindings, then publish the admitted result. Producers must
establish truthful formation, vacancy, state, Copy/transfer, source and cleanup
facts and matching emitted code. Source and transformed bound programs still
undergo their actual verification. Consumers check safe decoding, complete
type/operation agreement, required support and retained bindings before
affected use; they need not semantically re-prove arbitrary provider bodies
or foreign promises. Reconstructing facts needed by lowering is not such a
general proof. False but structurally consumable metadata remains a producer
violation; checksums, native loads and serialized authorization bits do not
establish its truth. Optional deeper audits remain separate.

## Conformance gates and remaining work

These are **required future cases**, not tests run or support claimed by
this documentation change. Use public compiler APIs and independent
source-free byte fixtures, not writer/reader agreement alone.

| Family | Positive, negative and boundary requirements |
|---|---|
| Recursive identity | Preserve every example table entry and nested ptr/fnptr/ref/optional/admitted-array types, including inner/outer write. Distinguish ordinary/init/gc/raw and slot/referent identities. Reject illegal qualifiers, missing leaves and mode-only substitutes; preserve metadata-only array gates. |
| Canonicality/closure | Read reordered/forward/shared-DAG rows; remap equal duplicated reference rows. Reject direct/mutual/pointer/signature/array cycles (also N=0), duplicate normalized array identities, wrong indexes, redundant Optional and unknown negative codes. Permit admitted nominal reference recursion. |
| Generic substitution | Instantiate the same helper with scalar, pointer and ordinary/nested/optional reference T; preserve exact T and qualification at every position. Reject open owner/formal/witness or generic nominal identity without its complete carrier, rather than inventing a specialization/export or losing the original expectation. |
| Declarations | Round-trip formals/results/fields, init versus init-mut locals, live descriptor versus vacant T, explicit/implicit markers and unused/bodyless imports. Reject flags 01/02/04 on wrong roles, nonmutable/unprepared hidden backing, mutable init formals and legacy modes in place of complete types. |
| Formation/place | Form live ordinary/vacant init references; reborrow the exact parent and admitted field/array/buffer/view places; evaluate receivers/indices once. Reject property-as-place, wrong owners/bindings, unit/zero backing, live/partial init actuals, metadata-only nested backing promotion, gc stable dereference and raw-pointer substitution. |
| Read/store/attenuation | Copy an ordinary descriptor without copying non-Copy T, initialize a descriptor slot through nested init ref, replace under retained outer write, and distinguish slot/referent state tests. Init-mut maybe-live assignment stages once and retires only live alternatives. Reject init Copy, escaped BorrowDescriptor, pre-init reads/attenuation, partial/pending conditional stores, double initialization and write upgrades. |
| Slots versus backing | Copy a descriptor field, separately borrow that field slot, rebind it and observe old copies retaining old referents. Reject return into callee parameter-copy or ABI-copy storage. Preserve data/shape, owner/guard/lease, root/pin, publication, code/load and affinity dependencies. |
| Publication/selectors | Cover Boolean defaults, explicit Boolean unconditional completion, int32 negative/zero/positive and uint32 zero/positive, shared selectors and multiple outputs. Reject wrong result tests/owners, gaps/duplicates, holes/conflicts and checked rows on raw/value formals. |
| Dependencies/captures | Preserve explicit empty, broad, borrow-only and backing-only sources and destination/phase ordinals. Exercise permitted inter-output publication and failure-surviving holder capture. Reject vacant sibling sources, cycles, slot-address escape, lost backing and empty/unavailable conflation. |
| Staging/flow | Combine compatible descriptor, eligible-place and value adaptations in one source-ordered named call; retain 54 at nested/short-circuited sites. Cover skipped/first/repeated/final-false evaluations, while/do/for/body/phase scopes and later rebinding/mutation/failure. Reject residual temporary vectors, indirect target-derived adaptation, stale correlations and overlapping/reused reservations. |
| Materialization cleanup | Trace new-value construction before old retirement, one live value per site, unchanged state on skip/RHS failure, later-call failure, transfer/finally and final cleanup. Reject T/span site merging, per-call scopes, fresh per-iteration retained owners, cleanup of vacancy and reuse that invalidates an old dependency. |
| Ownership/failure | Observe failed RHS before old retirement, actual partial acquisitions, normal complete versus failed-construction cleanup, explicit finally, unentered cancellation and indeterminate entered failure. No false/vacant rollback, complete-T cleanup of vacancy, repeated cleanup or abandoned child loan. |
| No-return/unused | Retain and validate full signatures, publication/dependency/type closure and required carriers for uncalled and DoesNotReturn declarations. No output/result fabrication, no omission because a normal edge is absent. |
| Origin/replacement | Source-free projection/body erasure, independent same-name/layout providers, diamonds and multiple original expectations. Compatible source narrowing succeeds; changed type, implicit marker, state, phase, retained CopyOrigin/provider/layout fails. Missing required output/witness summary never becomes Unavailable success. |
| Complete **104+C+R / 105** | One fixture combines B's tag-35 ancestor/u64 paths, FieldMove 42, arrays/Repeat 43–47, PathWrite 48 and wide inline geometry with nonempty general rows, scalar/enum constants, CopyOrigin, foreign targets, complete pointer vectors, direct expectations and R 49–54. Empty C/R collections still require their prefixes. No B direct-descriptor fingerprint token survives. |
| Bounds | Test row-count zero/100,000/one-past, code -100,000/int.MinValue, count/extent truncation/overflow and exact work/depth limits; distinguish count-domain acceptance from exhausted aggregate budgets. Exercise slot 255/count 256, argument 63/count 64 and complete u32 extents. Preserve high-word u64 layout facts without allocating their payloads. |
| Re-emission | Permute/prune/intern every family, including array/reference/signature cross-edges and annotation-only roots; remap 35/42–48 and 49–54, fields, sites/scopes, formal/subject maps, atoms, imports, Strings/extents/hashes. Scalar/index-looking payload bits remain unchanged. Run ordinary links, sets, graph roots/intermediates and compatible/incompatible replacements. |
| Profile/native isolation | Keep external 8/8, normal-backed 16/8 and integrated-GC locator/support agreements separate. Refuse missing private propagation/programs, zero-extent anchors, stored init layout, Blittable inference, implicit holds/pins and independently precompiled/runtime-instance crossings. |
| Exact selection | 104 continues to refuse missing facts; 105 rejects 104/97/98, stale 42–45 reference meanings/statement 28, unsupported outer pairs and Standard/Development rows. Equal headers, hashes and stale admission caches cannot select another schema or revive removed descriptor tokens. |
| Evidence levels | After implementation distinguish syntax/binding, producer verification, structural round trip, selected-provider admission, image emission and supervised headless JIT/AOT execution. Record final compiler/input/configuration/schema/profile identities; successful emission is not runtime evidence. |

This chooses the private reference/optional constructors, all numeric/record
placements, ordinary declaration publication carrier and checked place/
formation/reborrow/read/state/attenuation/store/materialization forms. No choice inside that
bounded encoding is delegated to an implementer.

### Implementation gaps and dependency gates

| Area and exact code/document owners | Required next work; not established by this proposal |
|---|---|
| `CheckedReferenceTypes.cs`, `BoundProgram.cs`, `CompilationReference.cs`, `Binder.cs`, `BoundTreeTraversal.cs` | Generalize complete optional/reference terms and every use; establish canonical descriptor versus referent binding, 01/02/04 permissions, explicit 49–54 nodes/places and source-scope ownership. Preserve full-width geometry and loan/owner provenance through adapters. |
| `ModuleCompiler.cs`, `.Graph.cs`, `.ModuleSets.cs` | Implement **one** 105 writer/reader/header/producer selection, all mandatory C/R directories/vectors, canonical type atoms and removed-token rules. Current 104's rejection is not permission to omit fields or emit guessed defaults. |
| `SemanticVerifier*.cs`, dependency/CopyOrigin proposal owners | Establish truthful state/reservation/attenuation, source versions, backing/captures, normal publication and cleanup for source/transformed programs. Retain exact bounded 93/95 contracts; required richer output/witness/async summaries need their own carrier before transport. |
| Reference projection, restoration, all type/field/body remappers and contract snapshots | Preserve original bodyless types/implicit/publication/identity facts and full closure, rebase all families/places/sites/subjects, check every expectation and selected provider before deduplication or replacement. |
| `NativeModuleCompiler*.cs`, `NativeEmitter*.cs`, `NativeJit.cs` | Join importing inner expectations to existing outer 3, regenerate affected admitted images, and enforce the separately selected layout/call/runtime/code-lifetime agreement. AOT, semantic relowering and root-only graph JIT are distinct acceptance paths. |
| Stored/normal-backing/integrated-GC owners; 98 and wider type/context owners | External 8/8 requires its complete bounded support; normal 16/8 needs explicit private agreement propagation; integrated GC needs its private program/requirements plus common runtime admission. Open generic/constructed-nominal/witness, additional places, zero-extent anchors, general stored init and broader fnptr/native crossings remain independent gates. |
| Active public-API tests and independent bytes/native probes | Implement the matrix above, including the combined B+C+R fixture, source-free remapping and supervised headless execution only for admitted agreements. Record final-patch compiler/input/configuration/schema/target identities; syntax, decoding and emission do not establish runtime support. |

Formal review remains outstanding. Provisional implementation may proceed
under the quoted authorization, but no absent prerequisite is treated as
implemented or automatically approved. Complete 105 support and every
independently required representation/runtime/ABI must be verified before
claiming the affected transport or execution path. Missing support refuses
that path without weakening accepted source contracts or accepting the wider
milestone.

No normative specification, source code, runtime provider, Standard registry
or implementation inventory is changed by this proposal. Selecting it
provisionally is neither milestone acceptance nor released-schema stability.
Formal adoption remains separate.
