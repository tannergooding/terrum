# Complete checked-reference types in private persistence

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** The governing authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This is a private encoding decision, not source approval, compiler support,
runtime qualification, a Standard TRRM allocation or a stable public ABI.
It persists a program, not a suspended process or live reference bits.

## Decision, scope and owners

Amend the **unimplemented private i32 revision 97** with one shared recursive
reference/optional type table, typed place operands and checked operations.
Use it everywhere a complete type occurs, not just in function pointers.
Retain checked normal-publication contracts on ordinary declarations even
when their Function payload is absent. Extend the existing local declaration
record with its initialization/adaptation facts.

This tightly scoped companion owns those deltas. The
[complete pointer-contract proposal](native-function-pointer-contract-private-persistence.md)
continues to own signature rows, conventions, invocation identity,
`fnptr#i` and original address-contract references. This document replaces
only its unselected checked-reference type/operation boundary and the
corresponding boundary in
[checked function-pointer outputs](function-pointer-checked-output-parameters.md#private-persistence-owner-and-explicit-next-decision).
It does not duplicate or replace that signature contract.

| Owner | Contract preserved |
|---|---|
| [Grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments), [complete types](../specs/language/types-and-normalization.md) | Complete constructors, qualification at each node, optional normalization and exact substitution; `mut` is a binding permission, not a type. No source production changes. |
| [References](../specs/language/references.md) | Explicit formation/indirection, ordinary versus initialization reference, fixed destination, scoped reborrow, one-shot attenuation and descriptor/referent separation. |
| [Dependencies](../specs/language/dependencies-and-views.md), [lifetimes](../specs/lifetimes.md) | Actual source/storage generations, source-storage versus forwarded backing, every supporting obligation and independent endpoint. |
| [Flow](../specs/language/initialization-and-flow.md), [ownership](../specs/language/ownership-and-cleanup.md) | Vacancy, complete/partial/maybe-live/reserved states, outcome correlations, actual holders, replacement and distinct normal-retirement/failed-construction orders. |
| [Provenance carrier](reference-provenance-carriers.md#private-exchange-candidate-revision-93), [field origins](reference-field-value-provenance.md#necessary-private-transport-candidate-95) | One operation-derived ledger; declaration-owned general Dependency rows and separate copy-origin guarantees. No serialized verifier-instance graph. |
| [Stored descriptor candidate](reference-stored-descriptor-layout.md) | Only its externally bounded Windows x64 8/8 ordinary-reference route and separate ordinary direct-value agreement; no stored init-capability layout. |
| [RootPin](reference-root-pin-runtime.md#value-layouts-and-identity) | Separate `(2,1)` profile: stable references remain uniformly 16/8 and rooted locators remain distinct. Root/pin operations, provider/domain and support holds are not supplied here. |
| [Persistence](../specs/representation/persistence.md), [versioning](../specs/representation/format-versioning.md), [binding](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md) | Exact selected private schema, original expectations, safe structural consumption and truthful producers; no required consumer semantic re-proof. |

### Why existing carriers do not suffice

These are inspected source facts, not newly executed tests:

* [`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  has struct/class, native-pointer and function-pointer collections, but no
  recursive checked-reference/optional type collection. `BoundVariable.Type`
  plus `IsOut`/`IsInit`/`IsImplicitRef` describes narrower prototypes, not a
  descriptor-valued field, result or nested substitution.
* [`ModuleCompiler`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs)
  selects exact revision 92. `WritePrivateType`/`GetType` transport i32
  identities; signature rows remain shape-only, and writing nonempty pointer
  invocation facts refuses. Variable records lose initialization-local and
  implicit-adaptation facts. Tags 8/21 are `BoundOutArgument`, tag 41 is the
  raw NativeOutput reservation, and raw pointer tags 38–40 are not checked
  reference formation/access.
* Its `Contract`/`AppendTypeName`, declaration projection, type-table merging,
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
  uses outer version 3 and separately stores direct-import expectations.
  Its native parameter type fields belong to the importing inner Contract.
  Those fields cannot be resolved in the replacement provider's type table.

Active private-92 output/container/remapping work does not implement pending
93 general dependencies or the pending complete-97 pointer codec. The shared
reference/provenance work likewise retains type, source verification,
capability, binding and lowering gaps. The active
[implicit-reference](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ImplicitReferenceParameterTests.cs)
and [initialization-output](../../tests/Terrum.CodeAnalysis.Tests/Semantics/PublicInitReferenceTests.cs)
tests exercise narrower/legacy forms; neither establishes this codec.
The [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
has no Compile exclusions. No execution result is claimed here.

### Minimal extension versus redesign

| Candidate | Assessment |
|---|---|
| **Supplement existing i32 identities with recursive reference/optional rows — selected** | Keeps existing complete scalar/nominal/pointer/signature carriers and adds only the missing constructors and checked operations. Requires one shared resolver and closure traversal across all type positions, not another fnptr-only flag. |
| Replace every private type with a universal TypeDef/TypeSpec/generic-context graph | Materially useful for open generic definitions, constructed nominal identities, witnesses and wider owner families. It would migrate every declaration, contract string and type-bearing operation, or adopt an explicitly selected Standard graph as private IR. That broader ownership/context redesign is not needed to preserve references over already representable complete inner types. It is not prohibited by draft compatibility. |
| Inline recursive variable-length types at every i32 occurrence | Viable only after changing every type-field extent, including outer expectation joins. Repeats large nested signatures and needs a second textual/bodyless strategy. Not selected. |
| Reuse pointer codes, Standard TypeSpec bytes, legacy init flags or only serialize provenance | Loses constructor, authority, place or protocol identity. Rejected; equal width and a familiar revision byte supply no codec. |

No candidate 98, historical reader, optional trailer or second spelling of
an earlier-97 layout is selected.

## Exact schema composition

Select out of band the whole draft
**`terrum/private-semantic-97-checked-references-1`**. Its base **P+E** is the
complete private composition in
[nominal enums](nominal-enum-persistence.md#complete-composition-not-a-revision-byte-shortcut),
not merely the first proposal that used 97:

| Component | Required meaning |
|---|---|
| 92 | Resolved arithmetic modes from 89; NoThrow retirement composed with the complete output delta from 90, scalar/pointer-cell eligibility, sole declaration-owned NativeOutput collection and tag 41. No legacy raw-output vector/suffix revival. |
| 93 | Mandatory general declaration Dependency collection, disjoint from NativeOutput's phase-1 dependency rows. |
| 94 | Exact wide-scalar catalog and type-derived constant payloads, not an unconditional i64. |
| 95 | Mandatory copy-origin summary byte and its conditional payload. |
| 96 | Nominal origins, foreign interfaces/member variant and the target discriminator on every tag-5 call. |
| Existing 97 | Exact convention and resolution tokens; foreign-member convention-byte removal; complete pointer invocation rows and `fnptr#i`/address-contract roots. Include the [foreign-invocation amendment](standard-native-import-function-pointer-contracts.md#private-persistence-interaction): mandatory NativeParameter vectors, raw OutputState applicability and tag-41 indirect-call context supersede the earlier pointer owner's raw-output exclusion. The resulting signature row remains **`29 + 4*n + 12*(s+o+d+p)` bytes**. |
| Direct expectations | The [import-only descriptor](native-import-semantic-provider-expectations.md#candidate-record-and-validity), with its mandatory presence byte, original foreign target, complete input vector and full u32 extents. |
| E | The mandatory enum directory, origins, `356+i` identities, `enum#i` atoms and four-byte enum constants. |
| This companion R | The mandatory type table, reference-closure origin rule, declaration publication vectors, variable flag byte and checked Body forms below. All other P+E fields retain their widths, order and meanings. |

The header stays `TRRM` plus little-endian **i32 97**, not Standard u16
major/minor. Select R even with zero type rows/outputs and no new Body forms.
All writers, ordinary/graph-library readers, nested Contracts, projections,
snapshots and producer validators must select this identical composition.
Referenced documents identify the ingredients; later edits do not silently
change the exact selected draft.

Reject other inner revisions and unsupported schema selections before
record decoding. Earlier-97 drafts are not aliases. Do not dispatch on
`>=97`, infer a layout from counts, try an earlier reader, patch a header or
fill omitted fields from defaults. Equal bytes cannot reveal the provenance
of every dishonestly relabelled stream that happens to be structurally valid.

Keep the selected foreign/convention-aware **TNPK/TNSP/TNGP outer version 3
field layouts unchanged**, selecting this inner draft throughout the retained
closure. Outer 2 and old outer-3 compositions are not aliases. No new native
profile string, checksum or outer version identifies the inner schema.

## Shared recursive complete types

### Table, codes and namespaces

Immediately after the complete existing `NativePointerTypes` table and
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
native-pointer row or complete pointer signature. Forward references are
allowed; complete closure validation precedes use. No Standard TypeSpecRef,
token or DefId is accepted in this field.

Nonnegative codes retain P+E's exact identities and restrictions: inline
structs `100..355`, enums `356..1023`, existing managed-qualified ranges
`1024..2047`, function pointers beginning at `2048` below `1<<30`, and
native-pointer identities beginning at `1<<30`. Reserved/retired scalars and
out-of-range table references do not become valid leaves. The negative
domain cannot collide with those families or depend on a future host enum.

Use the existing code when it already denotes a normalized optional identity
in P+E's bounded managed catalog. Constructor 3 is not a second encoding of
that identity; reject such redundant rows. Also reject an Optional whose
inner type is already Optional, including those pre-existing optional codes.
Normalize optional construction before writing. Presence layers at distinct
positions remain distinct.

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
their declaring field owners and recursively required field/type contracts.
Struct/enum origins retain P+E's domains. For a class record needed by this
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

P+E has **no open generic-formal inventory or general constructed-nominal
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
`01=InitializationCapableLocal`, `02=ImplicitReferenceAdaptation`;
all other bits reject. Bit 01 is valid only on a local declaration, not a
formal/receiver; it distinguishes `init T` storage from a fixed binding even
when an initializer is present. `isMutable` independently permits rebinding/
replacement. State is reconstructed from operations, not this permission.
Bit 02 is valid only on an ordinary readonly, nongc outer reference formal.
An init-reference formal is a live fixed descriptor, not a vacant variable,
and must have `isMutable=false`.

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
statement tags remain separate. Existing expression tags 1–41 and statement
tags 1–27 retain P+E's meanings. No new record directory is introduced.

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
```

Only kinds 1–4 are valid. Local is exactly 5 bytes; InlineField/ObjectField
are `9 + childExtent`; Referent is `1 + expressionExtent`. There is no
length prefix, place ID, cached access, initialized bit or address payload.
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
access through a gc locator. RootPin exact-place/relocation/pin operations
retain their separate missing private Body gate.

Receivers/descriptors evaluate once in recursive source order. Copying a
descriptor field's value and referencing that field's slot remain distinct.
A property/indexer, range, arbitrary element or zero-extent anchor lacking
a selected place carrier is unsupported; do not disguise it as a Local,
invent an addressable byte or broaden a raw storage operation. This bounds
the place codec, not the source language or recursive type constructors.

### New operation schemas

ExpressionEnvelope remains exactly
`u8 kind, i32 completeType, i32 spanStart, i32 spanLength` (13 bytes).
StatementEnvelope is `u8 kind, i32 spanStart, i32 spanLength` (9 bytes).
All new fields below are mandatory, without flags or padding:

| Private namespace/tag | Payload after envelope | Exact extent |
|---|---|---|
| Expression **42 (`2A`) ReferenceForm** | `u8 formationKind; PrivatePlace place` | `14 + placeExtent` |
| Expression **43 (`2B`) PlaceRead** | `u8 useKind; PrivatePlace place` | `14 + placeExtent` |
| Expression **44 (`2C`) PlaceIsInitialized** | `PrivatePlace place` | `13 + placeExtent` |
| Expression **45 (`2D`) InitializationAttenuation** | `Expression descriptor` | `13 + childExtent` |
| Statement **28 (`1C`) CheckedStore** | `u8 transition; PrivatePlace destination; Expression value` | `10 + placeExtent + valueExtent` |

**ReferenceForm 42:** formationKind is `1=FreshPlace` or
`2=ScopedReborrow`; other values reject. The envelope must be a nonoptional
stable constructor-1 type with qualifiers 00/01 or a constructor-2 init type,
with exact inner type equal to the selected place. The complete result type
selects ordinary versus initialization authority; there is no IsInit flag.
FreshPlace checks the actual eligible place and its authority/state/backing.
ScopedReborrow must explicitly select an existing checked referent in its
place path, preserving its parent loan/reservation; it cannot hide a new
reservation as fresh. A place path containing Referent requires
ScopedReborrow, not FreshPlace. Ordinary formation requires complete live T; init
formation requires eligible whole vacancy, including no retained partial state.
Outer write, actual extent/alignment and stable backing must be independently
available. Copy from an admitted packed descriptor slot does not make that
underaligned slot eligible for an aligned reference to the descriptor.

**PlaceRead 43:** useKind is `1=Copy`, `2=Transfer` or
`3=BorrowDescriptor`. The envelope type equals the place's complete stored
type. Copy requires its Copy contract and propagates value dependencies
without borrowing the descriptor slot. Transfer uses existing permitted
move/take rules, vacates the source only at their actual transfer point and
preserves its responsible holder/obligations. It grants no new arbitrary
field move or through-reference take authority.

BorrowDescriptor requires a live reference descriptor slot and is a
non-Copy operand use, not a separately storable reference value. Its result
can occur only as the descriptor operand of Referent or operation 45;
nesting those in operation 42 supplies the explicit child reborrow for a
call/local capability. It cannot itself be returned, stored, lifted to
optional, cast or passed as an ordinary value. In particular, reading a live
init descriptor does not read T or duplicate init authority.

**PlaceIsInitialized 44:** the envelope type is built-in bool. It tests the
selected place's state, not its value. Testing Local(r) tests r's binding;
testing Referent(BorrowDescriptor(r)) tests T. A false whole-partial test
does not prove vacancy; pending reserved caller storage is not observable.

**InitializationAttenuation 45:** source is a constructor-2 descriptor
operand, normally operation 43 BorrowDescriptor. Result is constructor 1
with exactly the same inner type and outer write, and no gc. Complete T
state must already be established. Consume only the one-time init
authority; do not rebind the source slot, retire T or acquire support.
The slot's declared type remains fixed; this is its explicit flow view.
Reusing a completed view is not another initialization permission.
Separate readonly projection may then attenuate outer write; it cannot
replace this state transition.

**CheckedStore statement 28:** transition is `1=Initialize`,
`2=Replace` or `3=InitializeOrReplace`; other values reject.
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

For widths, ReferenceForm of Local(0) is 19 bytes; a PlaceRead of that Local
is 19; its state test is 18. An init reborrow whose Referent operand is a
19-byte BorrowDescriptor is 34 bytes. A CheckedStore through that 20-byte
Referent with a 17-byte int32 constant is 47 bytes.

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

Source implicit-reference adaptation is elaborated into explicit storage,
initialization/replacement and formation under the existing enclosing
supporting scope, preserving capture/staging and short-circuit order.
The variable flag and `in:implicit:` token preserve its source-call contract;
they do not authorize a runtime temporary or omitted Body operation.
Use one reusable hidden local per materialization site/scope activation.
If the producer cannot express that expansion completely with existing
structured control flow and these operations, refuse persistence instead of
dropping its `ArgumentImplicitReferenceTemporaries` information.

Reuse the existing block lexical-scope marker (`IsManagedLexicalScope` in
the host) and its byte placement. Binder already marks source lexical blocks
and the for-header wrapper independently of whether a managed local exists;
synthetic cleanup/grouping blocks do not create new supporting scopes.
Preserve that distinction, statement 24 vacancy declarations, lifetime-end/
drop operations and exact loop/branch/finally edges. A local's storage site
is its declaration occurrence in the actual supporting activation, not its
span or index alone. Re-entry/reuse creates the appropriate new generation;
an ordinary allowed Copy assignment need not end that storage lifetime.

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

## Target, RootPin and ABI separation

Type-table bytes are not a physical descriptor. Apply the stored-layout
candidate only to ordinary stable descriptors in its explicitly selected
externally bounded Windows x64 rootless extension: 8/8, no descriptor-owned
runtime hold, and optional absence under that physical agreement.
It selects neither Blittable nor foreign eligibility by size.

Retain the separate RootPin profile's 16/8 stable references even when their
support word is zero; `gc ref<T>` is its independently rooted locator,
not an ordinary address. A constructor-1 gc qualifier preserves that semantic
distinction but supplies no private root acquisition, relocation, pin,
runtime-provider or support-frame program. Required private RootPin use,
including declaration-only/runtime requirements, remains refused until
its complete separately selected private program/requirement carrier exists.
No 42/Referent fallback implements it.

Likewise, preserving `ref<T>` identity does not supply the still-unselected
normal C/box backing-hold kind, its Copy/last-dependent release or handoff.
Do not silently treat an escape needing that support as externally bounded,
reject it as a language restriction, or repair it with a hidden owner.
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

Apply directed source-bound/no-return/caller-domain satisfaction only under
their existing owners. Preserve exact checked state relations: changing
false/vacant to false/complete is not strengthening, because the old caller
may initialize presumed-vacant storage. Required copy-origin precision stays
separate from broad lifetime bounds. Retain any stronger provider fact
actually used instead of silently strengthening an imported view.

Before Function erasure, capture original `ctype#i`/`fnptr#i` roots,
publication vectors, general/output/copy-origin contracts, nominal origins
and address/direct-import expectations. Reference projection, restoration,
sets/diamonds and consumer/provider/graph snapshots preserve/check each
expectation even when no emitted call remains. A provider-derived type table
or overlay of flags is not restoration.

Implementation must update the shared bound representation, `BoundProgram`,
CompilationReference/private Module tables and declaration collections;
type classification, equality/substitution/interning; all expression/place
children and shared `NestedExpressions`/rewriters; mandatory producer
verification; projection, comparison, restoration and lowering together.
A place walker must expose each embedded receiver/descriptor expression
exactly once and visit its owner type/field identity as well. A new node
must not fall through a visitor as a leaf.

For re-emission:

1. Decode/validate the source selection, collect the full retained type and
   declaration closure, and build context-sensitive maps for nominal origins,
   structs/classes/enums, native pointers, signatures and negative type rows.
   Resolve identities before deduplication. Never use an in-progress
   structural pair as successful equality.
2. Rewrite every binary type code and grammatical `ctype#i`, `fnptr#i`,
   `enum#i` atom, including annotation-only and bodyless roots. Clone rows
   when one physical row cannot preserve distinct using contexts.
3. Map fields by exact owner/substitution/declaration correspondence; rebuild
   PrivatePlace owner/ordinal paths and 95 summary paths. Map variables,
   actual call targets, source-order formal maps and publication/dependency
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
reference execution or select the separate RootPin/NativeAddress Body-0D
schema conflict. Debug/source data cannot carry missing semantics.

## Bounds, cycles, canonical writing and trust

Retain P+E's 4 MiB artifact, 1,024-function, 256-slot, 64-call-argument,
1,024-byte String, 100,000-node/work and depth-128 private limits. Table rows,
place nodes, declaration vectors and their traversal share those budgets;
do not reset a budget per signature, child or imported use. These are private
limits, not source arity limits, Standard row capacities or public ABI minima.

Before allocation/consumption, use checked wide arithmetic for `4+8*c`,
vector minima and `12*count`, `s*n`, path extents, String lengths, cumulative
bytes and remaining input. Checked output count is at most `n*s` as well as
the shared budget, with actual applicability more restrictive. All type/
formal/field/slot references must resolve in their specified namespace.
Reject truncation, unknown tags, nonzero reserved bits, invalid strict
Booleans/UTF-8, dangling references and trailing bytes.

Validate the finite structural graph across reference/optional, native-pointer
and signature edges, including pointer-mediated cycles. A shared acyclic DAG
is valid; compute/check its longest depth without letting memoization hide
an excessive path. Nominal declarations are anchors, not field expansion
inside type equality. Separately validate retained fields/layouts; nominal
recursion through references is not automatically a structural type cycle,
while an impossible by-value layout remains invalid.

Canonical writers normalize complete terms, then enumerate retained type
roots in declaration/field/formal/body order and emit missing rows in
child-before-parent dependency order, preserving signature-local logical
ordinals. Intern only exactly equal complete types in the same resolved
context; rebase all uses. Ordinary readers may accept valid forward references,
physical row permutations and duplicate equal structural rows, but never
duplicate declaration occurrence keys or forbidden redundant Optional forms.
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
| Recursive identity | Preserve every example table entry, nested ptr/fnptr/ref/optional combinations and legal inner/outer write. Distinguish ordinary/init/gc/raw forms and descriptor-slot versus referent types. Reject illegal qualifiers, mode-only substitutes and missing inner carriers. |
| Canonicality/closure | Read reordered/forward/shared-DAG rows; remap equal duplicated structural rows. Reject direct/mutual/pointer/signature cycles, wrong table indexes, redundant Optional and unknown negative codes. Permit supported nominal reference recursion without infinite field expansion. |
| Generic substitution | Instantiate the same helper with scalar, pointer and ordinary/nested/optional reference T; preserve exact T and qualification at every position. Reject open owner/formal/witness or generic nominal identity without its complete carrier, rather than inventing a specialization/export or losing the original expectation. |
| Declarations | Round-trip ordinary and checked-output formals/results/fields, init versus init-mut locals, live descriptor versus vacant T, explicit versus implicit adaptation and unused/bodyless imports. Reject conflicting variable flags/tokens, mutable init formals and payload-T plus legacy IsInit as a substitute. |
| Formation/place | Form live ordinary and vacant init references; reborrow exact same reservation and admitted field subplaces; evaluate nested receivers once. Reject value/property-as-place, owner/field mismatch, dead generation, live/partial/maybe-live init actual, GC locator stable dereference and raw-pointer substitution. |
| Read/store/attenuation | Copy an ordinary descriptor without copying non-Copy T, initialize a descriptor slot through nested init ref, replace under retained outer write, and distinguish slot/referent state tests. Init-mut maybe-live assignment stages once and retires only live alternatives. Reject init Copy, escaped BorrowDescriptor, pre-init reads/attenuation, partial/pending conditional stores, double initialization and write upgrades. |
| Slots versus backing | Copy a descriptor field, separately borrow that field slot, rebind it and observe old copies retaining old referents. Reject return into callee parameter-copy or ABI-copy storage. Preserve data/shape, owner/guard/lease, root/pin, publication, code/load and affinity dependencies. |
| Publication/selectors | Cover Boolean defaults, explicit Boolean unconditional completion, int32 negative/zero/positive and uint32 zero/positive, shared selectors and multiple outputs. Reject wrong result tests/owners, gaps/duplicates, holes/conflicts and checked rows on raw/value formals. |
| Dependencies/captures | Preserve explicit empty, broad, borrow-only and backing-only sources and destination/phase ordinals. Exercise permitted inter-output publication and failure-surviving holder capture. Reject vacant sibling sources, cycles, slot-address escape, lost backing and empty/unavailable conflation. |
| Staging/flow | Named direct-argument maps, positional indirect calls, later argument rebinding/mutation/failure, explicit reusable materialization, loops/joins and saved discriminator copies. Reject stale-result revival, overlapping reservations, parent use during child reborrow and premature backing retirement. |
| Ownership/failure | Observe failed RHS before old retirement, actual partial acquisitions, normal complete versus failed-construction cleanup, explicit finally, unentered cancellation and indeterminate entered failure. No false/vacant rollback, complete-T cleanup of vacancy, repeated cleanup or abandoned child loan. |
| No-return/unused | Retain and validate full signatures, publication/dependency/type closure and required carriers for uncalled and DoesNotReturn declarations. No output/result fabrication, no omission because a normal edge is absent. |
| Origin/replacement | Distinct same-name/layout nominal providers; wrong field owner/order; source-free body erasure and diamonds. Compatible source narrowing succeeds under its owner; changed reference type, conditional state, phase, original provider or retained layout fails. Equal physical codes and eight-byte carriers are insufficient. |
| Complete 92–97 | One fixture combines raw outputs/tag 41, nonempty 93 general rows, wide scalar/enum constants, nonzero 95 summary, 96 foreign targets, amended pointer input/output vectors, resolution/convention tokens, import-only direct expectations and R's types/operations. Empty new collections still require their exact prefixes. |
| Bounds | Test zero/100,000 rows, index 99,999/code -100,000, one-past, int.MinValue, negative/overflowing/truncated counts and exact work/depth boundaries. Exercise slot 255/count 256 and argument 63/count 64 under valid declaration roles; keep full u32 logical ordinals/extents and reject nonrepresentable private uses rather than truncate. |
| Re-emission | Permute/prune/intern all type families, declarations, fields and providers, including annotation-only and address-only roots; rebuild atoms, paths, argument/subject maps, Strings, extents and checksums. Reconstruct lexical/loan ledgers without key/span/byte substitution. |
| Profile/native isolation | 8/8 only for externally bounded rootless backing; RootPin stable 16/8 unchanged. Refuse missing normal shared holds, zero-extent anchors, stored init-capability layout, foreign Blittable inference, implicit root/pin/retain and cross-profile/precompiled ABI guessing. |
| Exact selection | Current 92 refuses these facts; wrong inner/outer, earlier-97, Standard/Development rows, equal-header unsupported drafts and stale schema/binding caches cannot select a fallback. Independently malformed R artifacts reject even with valid outer hashes. |
| Evidence levels | After implementation distinguish syntax/binding, producer verification, structural round trip, selected-provider admission, image emission and supervised headless JIT/AOT execution. Record final compiler/input/configuration/schema/profile identities; successful emission is not runtime evidence. |

This chooses the private reference/optional constructors, all numeric/record
placements, ordinary declaration publication carrier and checked place/
formation/reborrow/read/state/attenuation/store forms. No choice inside that
bounded encoding is delegated to an implementer.

Remaining gates are formal approval; complete P+E+R implementation across
shared types/verification/projection/binding/traversal/lowering and native
containers; final-patch qualification; and each independently required
representation/runtime/ABI. In particular, open generic/constructed-nominal
interchange, additional place families, normal shared backing holds,
zero-extent anchors, private RootPin programs and broader fnptr/public native
ABIs retain their separate gates. Missing support refuses affected transport
or use without weakening accepted source contracts.

No normative specification, source code, runtime provider, Standard registry
or implementation inventory is changed by this proposal. Selecting it
provisionally is neither milestone acceptance nor released-schema stability.
Provisional implementation may proceed under the stated authorization;
formal adoption remains separate.
