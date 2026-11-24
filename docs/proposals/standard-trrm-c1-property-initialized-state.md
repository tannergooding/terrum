# Standard TRRM C1 property initialized-state guarantees

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow, pending later review.** This is a proposed
Construction-1/2 successor, not normative approval, an implementation or
runtime-support claim, or a new ordinary property/reference contract.

## Finding and selected boundary

The missing decision is genuine. The [specification index](../specs/README.md)
leads to the following distinct owners:

| Evidence | Settled contract; missing representation |
|---|---|
| [Property grammar and construction](../specs/grammar-spec.md#510-properties-and-accessor-local-effects) | An auto accessor or property-local `field` creates one implicit backing field; authored bodies without either create none. Backing starts vacant. Constructors publish declared minimum property guarantees; caller initialization and final validation precede publication. Additional accessor/physical mappings remain separate work. |
| [References](../specs/language/references.md#formation), [call materialization](../specs/language-contracts.md#11-places-values-and-materialization) | A by-value property evaluation is an accessor call, not a loan of hidden storage. Ref-returning/lending operations need their complete result-loan contract. |
| [Flow](../specs/language/initialization-and-flow.md#states-reads-and-initialization), [ownership](../specs/language/ownership-and-cleanup.md#construction-and-publication), [lifetimes](../specs/lifetimes.md#definitions-and-notation) | State belongs to an exact place/generation. Complete backing, a live handle, complete object validity and publication are different facts. Partial state is not vacancy. |
| [PropertyDef](../specs/il-metadata.md#section-kinds-and-row-layouts), [binding](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions) | The 20-byte row identifies a getter and its value type. It identifies neither backing nor a setter/init role; binding currently follows the getter only. |
| [Constructor declarations](standard-trrm-c1-constructor-declarations.md#exact-construction-clause) | Role-1 guarantees admit PropertyDef in principle, but expressly require its complete initialized-place/backing/accessor contract. A FieldDef substitution is not that contract. |
| [Construction-2](standard-trrm-c1-construction-runtime-boundary.md#checked-construction-program) | Fresh backing, I completion and final Publish/Abort are separate. It does not define property initialization calls or property-state subjects. |
| [Ordinary class receivers](standard-trrm-c1-instance-call-receivers.md#proposed-exact-mapping), [R1](standard-trrm-method-receiver-access.md#initialization-is-not-mutable-receiving) | Ordinary get/set access is a live receiver borrow. Initialization authority is phase-owned, not readonly access, `write C`, or R1's writable-receiver bit. |
| [IO1](standard-trrm-c1-indexer-parameter-default-ownership.md#proposed-propertydef-successor) | Proposes explicit set/init links for **indexers**, but selects no named-property backing or initialization-completion contract. Its init link explicitly supplies no backing guarantee. |

The exact PI1 implementation is now partial. These are source observations,
not validation results:

* [`TrRmContractTableReader.ReadConstructionClause`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs)
  accepts role-1 PropertyDef guarantees only under PI1 and requires their
  checked backing relation; it also validates role-3 PropertyDef linkage.
  Other schemas still reject PropertyDef construction guarantees.
* [`TrRmPropertyDefTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmPropertyDefTableReader.cs)
  selects the 20-byte baseline or 32-byte PI1 row by exact schema and checks
  setter/init/backing references. This is not complete provider binding,
  replacement or source-free property closure.
* [`Binder.ManagedClasses`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs)
  creates compiler-owned backing fields for auto and `field`-backed
  properties. `Binder` no longer rejects instance auto getters.
* [`TrRmExecutableCompiler.StandardClassTypeReferenceWriter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs)
  emits selected public scalar properties with field-reading explicit or
  auto getters, direct mutable setters to the exact mutable backing field,
  and auto or simple authored init accessors with one direct scalar FieldWrite
  to the exact backing field. Other setter/init Body shapes, init contracts
  and broader class closure remain unsupported.
* [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) binds
  phase-1/2 `Q is init` and `this.Q is init` predicates in class constructors
  and matching init accessors to a `BoundPropertyInitialized` carrying the
  exact getter, backing FieldDef and receiver slot. The mandatory verifier
  checks those identities and rejects the node in function Bodies or when an
  init accessor tests another property. Graph production, failure-map
  composition, provider remapping and lowering remain open.
* [`TrRmStandardClassSourceEmissionTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardClassSourceEmissionTests.cs)
  covers auto-getter, setter, init-accessor and constructor CallSite producer
  cases, including repeated calls, multiple properties and source-order
  interleaving with direct scalar FieldWrites. The focused
  [`GetterPropertyTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/GetterPropertyTests.cs)
  also exercise property-state binding, verifier identity rejection and
  cross-property init-accessor rejection. These tests remain unverified under
  the active execution hold.

**Recommend PI1:** one explicit PropertyDef-to-implicit-FieldDef relation,
explicit accessor roles, and a bounded initialization-accessor state contract.
Only a named, instance, by-value property with that checked backing relation
can be a property initialized-state subject. Preserve the PropertyDef identity;
do not replace its guarantee with a field guarantee.

PI1 covers automatic backing and mixed/authored accessors using `field`,
not only auto-getter bodies. It adds no computed-property, arbitrary
getter-selected place, multi-backing, indexer, static-group or result-loan
state contract. The declaring class must already be admissible under the
selected construction parent. Construction-2 keeps its nongeneric, nonunique
normal-class/runtime restrictions. Independently unsupported value,
ownership, dependency, validation and accessor requirements still refuse
**complete class publication**, including when uncalled; nothing may be
silently omitted.

## Exact proposed agreement and allocations

Use two new, explicitly **unapproved whole-schema identities**:

| Parent, exactly as defined by [R1](standard-trrm-method-receiver-access.md#exact-profile-consequence) | Exact PI1 candidate |
|---|---|
| Construction-1L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1` |
| Construction-2L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1` |

Each is precisely its listed parent plus this document's delta. PI1 is
shorthand, not an independently selectable Feature. Keep `(1,0)`, edition 1,
Body kind/encoding `01` and the parent's complete catalog. Original
Construction-1/2, L1/R1 parents and IO1/D1/GD1 agreements keep their meanings.
This selects no IO1 index/default composition, generic-property codec,
private persistence revision or native ABI.

Select one exact literal, ordinally and case-sensitively, for the root and
every supplied provider/captured semantic input, including unused inputs,
before schema-dependent decoding. No version/length/Feature sniffing,
component parsing, old-token reinterpretation, compatibility alias or retry
is defined. A future composition requires an explicit whole agreement.
Changing the selector or appending zero words cannot recover erased facts;
[draft conversion](../specs/representation/format-versioning.md) must preserve
decoded semantics.

All assignments below are **candidates in these successors only**:

| Namespace | Proposed delta |
|---|---|
| Section `001E` PropertyDef / DefId kind `1E` | Replace stride 20 with stride **32**, below. Reuse IO1's explicit role-link design, not its indexer/default semantics. |
| FieldDef flags | Candidate bit **`0x10=PropertyBacking`**, below. Existing row stride is unchanged. |
| Construction Contract `0003` | Add distinct role **`3=BackedPropertyInitializer`**, below. Roles 1/2 and their bytes remain unchanged. |
| General PredicateNode kind | **`37=PropertyInitialized`** hexadecimal, below. Existing node/operand/edge row shapes remain unchanged. |

No section, DefId, Contract kind, Feature, PropertyDef/MethodDef flag, FieldDef
row stride, Body record, opcode, prefix/escape or map is added. Reference/index
widths remain unchanged; only the listed PropertyDef stride changes. In the inspected
[registry](../specs/representation/registry.md) and
[general-graph candidate](general-predicate-graph-schema.md#node-schema-and-evaluation),
`37` is not a selected node kind: expressions end at `36`, patterns occupy
`40..4F`. This node number is not section `0037` PredicateGraph or a Body
instruction. Construction's role byte is a different namespace again.
Reusing these families avoids a second property identity/table and a
general place/state-transformer language.

### PropertyDef and backing ownership

Every PropertyDef row in PI1 has this exact little-endian layout:

```text
u32 ownerTypeDef, u32 name, u32 typeSpec, u32 getterMethodDef, u32 flags
u32 setterMethodDef, u32 initMethodDef, u32 backingFieldDef
```

The first five words retain their existing namespaces and visibility rules.
The last three are nullable **local typed rows**: MethodDef, MethodDef and
FieldDef respectively, never DefIds, Imports, dispatch slots or Body indexes.
Nonzero rows are `1..00FF_FFFF`, high byte zero, and within their tables.
Check the full `32 * recordCount` extent without narrowing/overflow.

For a backed declaration Q, require:

1. Q is a zero-index instance property of the exact admitted class C.
   Its getter is required, returns Q's complete T, has zero explicit Params
   and no method formals. Retain its own receiver/access/effects/contracts.
2. At most one of set/init is present. Every linked accessor is a distinct
   MethodDef in C's logical Module, immediately owned by C, and belongs to
   this one property/role. Aliases of Q do not duplicate ownership.
   Set/init returns unit and has exactly one ordinary by-value Param, the
   assigned `value`, of exactly T, ordinal zero, without a default or method
   formals. Receiver presence does not shift that ordinal. Admitted zero-data
   types retain their logical positions and state obligations.
   Get/set requires the exact nonzero ordinary C receiver and independent
   R1 access flag/type agreement; init requires the phase receiver below.
   A receiverless type-owned static method is not an instance accessor.
3. Nonzero `backingFieldDef` selects exactly Q's one implicit field F.
   F is a private, nonstatic instance FieldDef in C's complete contiguous
   field range, flagged `PropertyBacking`, and with complete type exactly T.
   Each F belongs to at most one property's backing relation and occurs once
   in each applicable complete instance Layout. Its declaration position is
   Q's position in the established partial-type input order. Do not duplicate
   storage for Q.
4. Source capture obtains Q/F from the checked auto-accessor/`field`
   declaration and primary/implementation association, not a getter-body
   recognizer, generated name, offset or same-shaped authored field.
   Retain F even if private, zero-data or unused by the getter.
5. F's `Mutable` flag is the checked ordinary write permission of its
   property-local `field` binding. It is set iff an ordinary accessor is
   authorized to write F under the accessor-local receiver/access rules.
   An auto-setter writes F and therefore requires the source-required `mut set`
   authority; an ordinary `mut get` contributes only when its `field` binding
   has write access. An `init` accessor never contributes ordinary mutability:
   its write authority is Construction role 3 below.

### Implicit backing FieldDef identity

The current FieldDef requires a name; a synthetic name would create a new
name-based identity and replacement obligation for storage that has no source
identifier. PI1 instead assigns the successor-only `FieldDef.flags` bit
`0x10=PropertyBacking` and permits `name=0` **only** for that flag. Such a row
must be private, nonstatic and inside its owning TypeDef's contiguous field
range. Its complete type and layout membership remain required. Every flagged
row is referenced by exactly one same-owner PropertyDef's
`backingFieldDef`; every nonzero `backingFieldDef` selects exactly one flagged
row. An unflagged FieldDef still requires its ordinary nonzero name, and a
flagged row cannot also be a source-named field.

The `PropertyBacking` flag does not make F source-nameable or independently
exportable/importable. Body FieldMaps may select its local FieldDef row for the
declaring accessor, and Layout rows include it as storage; source selectors,
expected-provider binding and replacement identify it only through the
retained PropertyDef relation and its position in the complete TypeDef field
map. A consumer validates the PropertyDef/FieldDef cross-reference before
using a zero-name row. This avoids inferring F from a generated name, row
number, getter Body or matching type.

`FieldDef.Mutable` remains ordinary field write authority, not a record of
whether a particular Body happened to write. PI1 derives it from the checked
`field` access permitted to ordinary property accessors; `init`-only authority
does not set it. Each accessor Body still retains and is checked against its
own receiver/access contract. This flag grants no authority outside those
accessors or beyond the ordinary C1 field rules.

For declarations outside that backed shape, added set/init words remain
zero and the parent getter-only contract remains usable where admitted.
`backingFieldDef=0` supplies **no initialized-place contract**: it means
neither live nor vacant. A required named setter/init or backing outside
PI1's shape refuses rather than being erased to such a row. IO1 indexer
links cannot be slipped into this zero-index route.

### Initialization accessor: authority and completion are explicit

A nonzero Q.initMethodDef M requires exactly one MethodDef-owned Construction
clause of role 3. Conversely every role-3 M must be that one Q's init accessor.
After the ordinary eight-byte Contract header, its entire payload is:

```text
u8 role=3, u8 flags=0, u16 reserved=0, u32 propertyDefRow
```

Total extent **16 bytes**. `propertyDefRow` is a required typed PropertyDef
row in M's Module, not kind-`1E` DefId bits. The clause's `ownerRow` is M.
Role 3 is mutually exclusive with roles 1/2; tag 3 is never decoded as an
initializer's count or a projection's I reference. All existing exclusive
range/cardinality rules apply.

M has the same checked initialization receiver type/nominal C backing as
C's role-1 I, with R1's writable-receiver bit **clear**. Classify role 3
before interpreting ordinary receiver access. A local actual owns a real
Body; a property-selected expected M is bodyless and retains this complete
contract. No native-function-pointer anchor, ordinary method, setter,
Cleanup operation or constructor projection can stand in for M.

Role 3 explicitly publishes this **bounded state-transformer contract**:

* Entry designates the same active unpublished C generation. F may be
  wholly vacant or complete/live; a join of those possibilities is allowed.
  Partial F or a conflicting reservation/loan is not an eligible whole
  initialization/replacement destination.
* The added initialization/replacement authority is **only for Q's F**, not
  I's whole-object initialization authority. Sibling writes require
  independently admitted authority; neither this role nor its state frame
  supplies it. On normal completion, **F is a complete live T after body
  and postcondition-phase cleanup**. Other receiver fields retain their
  entry structural availability/reservation states, including nested partial state,
  not merely a whole-field live bit. It concerns state, not unchanged values,
  Pure, absence of writes, or preserved value predicates.
* Actual reads, replacements, source captures, dependencies and failures
  retain their ordinary contracts. The callee cannot assume sibling fields
  live merely because it has a live receiver handle. Guarded field-state
  projection may establish a read; ordinary whole-receiver access still
  needs its independently applicable completeness/publication gate.
* This role never enters/completes I, publishes C, reopens a published
  generation, copies construction authority, or transfers the provisional
  backing hold. It may be invoked repeatedly during that construction.

This is **not inferred from an init link or the word `init`**. For an
automatic init, normalize its specified generated assignment into the
explicit role-3 completion contract and retain the real generated operation
and accessor checks. An authored init must publish and establish the
unconditional source guarantee `ensure Q is init`; retain the authored
clause as well as this normalized summary. Prove the frame against its
actual implementation. An empty authored init admitting vacant F cannot
acquire completion by assertion. A no-op with a genuine entry condition
establishing live F can preserve it; retain that condition and its failure
behavior. An incidental private write without the published guarantee or a
multi-place state transformer cannot acquire this summary either.

PI1 deliberately does not encode every legal authored init. An accessor
that requires a different completion relation or changes sibling
availability is outside this bounded codec, even if valid source under a
broader contract. Refuse its required class closure; do not change source
semantics, insert an ensure, ignore its effects or claim getter-only success.
Ordinary authored clauses may impose additional supported conditions;
their safety, order and caller domain still check independently.

## Exact meaning of a property guarantee

For an active object generation G, Q's initialized-state subject is
**Q's explicitly related F in G**. It is not the getter result, a hidden
temporary, the receiver handle cell, an arbitrary field read by the getter
or the object as a whole.
Ordinary replacement does not change G; ending/reusing backing does.
Publish/Abort consumes construction authority, and reusing a context Home
starts a new generation without its old state facts.

| Operation/fact | PI1 meaning |
|---|---|
| Role-1 member DefId `1E:Q` | On every normal I completion, Q/F in that exact construction generation is a complete T, with actual validity, ownership and dependencies. Keep Q in the set. |
| State test `Q is init` in a construction clause | Inspect that state without invoking any accessor or reading T. True establishes complete F. False may mean vacant **or partial**, not permission to overwrite live children. Reserved storage is not observable. |
| Ordinary `receiver.Q` | Invoke its exact getter, with ordinary receiver/result/effects/dependencies. Even an initialized Q does not promise that this getter reads F, returns F's contents, is Pure, or cannot fail. |
| Ordinary assignment through set | Invoke the exact setter under its ordinary complete-receiver authority. Its existence grants no partial-receiver initialization entry. |
| Assignment through init | Invoke the exact role-3 M, preserving all its checks/effects; do not replace it with a direct store. Normal completion establishes its explicit summary. |
| Declaration initializer / declaring-constructor getter-only backing privilege | Perform the source-specified backing initialization directly, not a fabricated get/set/init call. It grants no public setter or caller privilege for getter-only Q. |
| Descriptor/handle stored in T | Complete initialization is of that stored value, with its own validity. It does not initialize a pointer's pointee, turn a descriptor-slot fact into a referent fact or prove an unrelated object complete. |
| Ref-returning/lending property | Requires its separate full result-loan/access/backing contract. PI1 supplies no loan of F or getter-selected referent and cannot discharge a missing result codec. |

Only direct, unconditional role-1 minimum guarantees use the existing
`initializedCount/memberDefId` vector. Retain the checked declaration's
minimum, not additional init-property promises inferred from its private
Body. Conditional/nested/richer guarantees require their own exact codecs.
A PropertyDef entry with zero/mismatched backing is invalid in PI1.
Unlisted properties are not thereby vacant.
Backing that cannot be left to a declared init operation remains the
constructor's obligation on every successful I path. In particular,
getter-only backing cannot be delegated to a caller initializer.

Q and F are distinct declaration identities even when both occur in a
retained guarantee set. Reject repeated DefIds, not their explicit
relationship; establish one storage state/acquisition, never two. Matching
a promised Q requires the corresponding PropertyDef guarantee, not an actual
set containing only F. A producer may use the checked relation to establish
truth, but cannot serialize away Q or its accessor obligations.

### State predicates, without an addressable property place

Add the candidate general-graph node `37 PropertyInitialized`:

| Field | Exact shape/meaning |
|---|---|
| `nodeKind`, result | `0000_0037`; built-in bool result |
| Operands | Exactly one existing kind `08 Property`, a typed Q row |
| Children | Exactly one `10 SubjectPlace` node over this graph's receiver ReadRoot (`rootKind=1`, identity zero), with that declaration's exact initialization receiver TypeSpec |
| Context | Phase 1 or 2 of role-1 I or role-3 M for the same C; an active unpublished receiver, not an ordinary class-handle argument |
| Evaluation | Resolve Q/F and the captured receiver generation; test complete backing state without reading T or calling the getter |

Its node/operand/child extents, occurrence ownership and reserved fields
retain the general-graph schema. The operand-08 rule requiring a matching
getter Call applies to `2D PropertyGet`, **not** to this state-only node.
The graph's footprint retains the receiver-state observation and Q/F
declaration/binding closure; no getter call/read dependency is invented.
Allowed-read bounds, when applicable, must cover that receiver observation.
Changing Q/F state invalidates its fact under the ordinary flow rules.

This produces bool, not `P(T)` or a new reference type. It cannot supply
Load, reference/address formation, a writable place or an init-output formal.
An ordinary by-value property remains nonborrowable even with nonzero
backing. Do not encode the source predicate as `PropertyGet + Initialized`,
or as FieldPlace alone after dropping Q's retained identity.

Retained node 37 requires the parent's `17/1` general-graph closure
including `10/1`. It is not a phase-3 output selector. Backing metadata alone
does not add Feature 13; retained FieldMaps/FieldPaths/imports/queries keep
their independent triggers. In Construction-2, extend `25/1`'s existing
bodyful initialization trigger to role-3 Bodies and their phase-bound calls.
Bodyless roles alone add no Feature. L1/R1 and other inherited requirements
remain independent.

## Binding, calls and publication

Capture source property/primary, actual accessor implementation, F, constructor
I and construction-site identities once. Partial primaries provide contracts,
not extra auto backing; their implementing secondaries must establish the
same relation and summaries. Preserve all facts through shared bound-tree
visitors, rewriting, mandatory producer verification and source-free
reconstruction. Never recreate them from display names or source lookup.
Under PI1, extend the existing
[class-member source selector](standard-trrm-c1-class-source-publication.md#candidate-source-input-and-capture)
from getter-only properties to the backed shapes above. The existing
`TrRmSourceClassMemberExport` on the exact PropertyDeclaration selects Q and
its PropertyDef ExportKey; the constructor member selector selects I and its
optional existing `ConstructionProjectionExportKey` selects P. The original
Program/input/primary-span association selects Q/F and all accessor identities
from checked declarations, never by display name. Keep the parent's
key/access/partial rules. No extra accessor/backing key, caller-supplied
semantic fact or new CLI/JSON grammar is selected.

The [role-3 invocation companion](standard-trrm-c1-property-initializer-invocation.md)
completes the exact CallSite/Home encoding and role-directed I/M/context
entry-state and lifecycle rules for these two PI1 agreements. Its unapproved
amendment owns that invocation mapping; original R1/Construction-1/2
agreements remain unchanged.

For a role-3 call, use a **general MethodMap CallSite**, receiver logical input
zero with `sourceUse=5`, followed by assigned `value` at explicit formal zero.
Its receiver selects either an active Construction-2 role-4 context Home,
or the role-1 receiver Home of I/M borrowing that same unpublished generation.
For Construction-1, retain the phase-bound I/M receiver view; this selects
no fresh-backing runtime. The receiver is neither a Param nor `ref<init C>`.
No ordinary Register/Home value, raw pointer, live published C, different
generation or source-use 3/4 satisfies it. Compact calls cannot encode M.
Role-aware validation must distinguish I, M and ordinary get/set; receiver
presence alone is insufficient.

For Construction-2 caller-entry construction, retain the actual call-site
program rather than a role-2 projection with invented entries. A role-4
context in an otherwise admitted caller Body may Begin, invoke its explicitly
selected role-1 I once, execute the entries, then validate/Publish. The
general CallSite's bound MethodMap target selects I; retain that identity in
the context state. No receiver-bearing method or name supplies it. This is
the PI1 completion of the parent's caller-entry route, superseding P-only
caller restrictions for this context use; a genuine role-2 projection still
requires its exact associated I and has no caller entries.
An M call from the context requires I **Completed**. An I receiver may
forward to M while I is **Entered**, including its initializer prefix; an
M receiver preserves its received Entered/Completed state. I/context
authority may select any eligible Q of C; an M receiver may forward only
its same Q/F authority, not amplify it to another property's init role.
A broader helper needs another complete codec. M never changes I's lifecycle
state. CFG joins preserve the possible selected I identities and only their common guaranteed state,
with actual acquisitions retained. No M-before-I or second-I path is admitted.

Capture receiver before RHS, stage the RHS once, perform required checks and
ordinary destination/argument preparation, then invoke M. A live F takes
ordinary replacement checks, not a second vacancy-only init-reference
formation. Its old value is not retired before RHS success. `init` may replace
F repeatedly in the unpublished phase even where ordinary field mutation
would be forbidden; that authority ends at Publish/Abort. Outstanding
dependencies and partial children still block invalid replacement.

I's guarantee describes **I's normal-completion point**. Caller entries run
after it, in source order; later effects can invalidate value/validation
facts. Do not rerun I's ensures after caller initialization. M's summary does
not complete I again, nor establish every other field or final invariant.
A role-2 projection is still available only when its real no-caller-entry
recipe establishes the complete result. Final publication checks actual
required storage once per field, including private/getter-only backing, all
remaining init obligations, dependencies and registered validity checks.
Do not invoke getters to perform that check.

### Provider correspondence and replacement

Resolve Module/revision/key, definition kind and owner before comparison
under [directed binding](../specs/representation/binding-and-replacement.md).
An expected Q retains bodyless expected accessor MethodDefs and its backing
FieldDef stub in the expected C's field range. Bind those **subordinate
edges** to the selected provider Q's get/set/init/F, with matching owner/
Module/type/role. Its `backingFieldDef` selects the expected stub, which is
private, has `name=0` and `PropertyBacking=1`, and owns no consumer storage or
initializer. This explicit backing correspondence is a PI1 amendment, not an
ordinary FieldDef Import to a private key or a field-name search.
It must agree with the parent's full C field correspondence by declaration
position, names for named fields, PropertyDef relation for unnamed backing,
access, roles and complete types; it cannot bypass that comparison or replace
an incompatible field map.

No separate getter/setter/init/backing Export key is required. Private
definitions remain provider-owned closure or are packed as actual
definitions; do not export private access or invent key suffixes. Any
independently retained member/field Import must agree with the subordinate
selection and satisfy its own access/layout obligations.

Check all of these dimensions:

* Each expected I PropertyDef guarantee maps to the same actual property
  through the bound C/Q correspondence. Removing it fails; adding one may
  satisfy an older consumer, which may still explicitly initialize Q.
* Every required accessor role, complete signature and directed R1 access,
  unsafe/effect, predicate, dependency, capture and phase contract survives.
  An ordinary setter cannot replace init. Q's init role-3 completion/frame
  promise cannot weaken, even with identical getter or machine-code shape.
* Required backing cannot disappear, change T, become static/foreign/shared
  between properties, or be selected by a getter's implementation. A zero
  backing expectation promises no initialized-place capability. Additional
  provider facts/roles are permitted only when all other retained contracts
  allow them.
* Physical row/String-row permutation is not a new backing identity. The
  checked Q/F edge and the full owner field map must agree; renaming or moving
  private F does not evade the parent's exact retained field obligations.
  Preserve every direct field, layout, offset, reference, cleanup or stronger
  provider fact too. This grants no resilient layout, object migration, hot
  replacement or binary compatibility behind an unchanged native caller.

Keep every original expected view and alias, including after previous
resolution or Import-to-direct packing. Do not replace expectations with the
provider's stronger facts; separately retain any stronger facts actually used.
Binding executes no getter, init, equality, validation or cleanup.

## Failure, lowering and transformation obligations

Source production proves the backing association, initializer summary/frame,
every promised constructor guarantee, legal partial reads and the real
phase/acquisition program. A consumer performs required exact-schema,
framing, count, namespace, reference, owner, role, signature, support and
binding checks before affected use. It need not semantically re-prove an
arbitrary provider Body; false published summaries remain producer violations
under [reader trust](../specs/representation/reader-trust.md).

Malformed rows/links/roles/graphs reject as invalid data; unsupported required
semantics reject as unsupported, not absent backing or an empty guarantee
set. Current public API conventions are `InvalidDataException` and
`NotSupportedException` respectively; invalid host selectors use argument
errors. Source obligations diagnose. Resource exhaustion explicitly declines.
None returns a partial artifact/reference or launches execution to discover
support.

An invocation that fails has no normal edge and publishes no normal
accessor/constructor summary; an independently admitted DoesNotReturn
invocation likewise supplies no normal state/result. A later construction
failure publishes no completed C but does not erase earlier actual effects
or acquisitions. Preserve F/child state and surviving captures, including
failure in clauses or final validation. No forced vacant state, default
value, repeated check or transactional rollback follows. Under independently
admitted unwinding, retire remaining actual acquisitions in reverse
acquisition order, with
complete children distinguished from partial storage; never invoke the
incomplete parent's Cleanup. Construction-2's selected policy zero still
promises **no unwind cleanup**. This proposal admits no new handler or
failure transport. Abort remains legal only after actual acquisitions and
dependents have ended.

Lower fields using validated layouts and the exact construction generation.
M borrows that context's normal backing through the parent's admitted
receiver projection; it does not allocate another object, retain/copy an
unpublished handle or introduce an independently callable native entry.
Construction-2 native preparation additionally needs a real private
construction-state/acquisition plan shared by I/M views of that backing.
Static facts may eliminate bookkeeping, but a dynamically needed state test
or replacement must observe the actual same state, not a fresh callee-local
false flag. Keep that plan compiler-private within the jointly regenerated
[normal-backing agreement](reference-normal-backing-support.md#exact-carrier-and-allocation-identity).
No public header, serialized bitmap, hidden semantic formal, GC fallback or
new runtime-policy selection is assigned. Missing native state support refuses.

Ordinary get/set continues to use R1's selected caller view and borrow mode,
with actual effects/dependencies. A fixed backing relation justifies neither
getter elision nor replacing an authored accessor with load/store. State
observations may lower to equivalent private Boolean/CFG/Home bookkeeping;
no new Body state-test instruction is allocated.

Packing/re-emission must remap C/Q/F, all accessor/I/projection MethodDefs,
typed role-3 Q rows, role-1 kind-directed member DefIds, signatures/Params,
property operands and graph roots, FieldMaps/FieldPaths, Imports/Exports,
contracts and original provider/Clause/Origin/Plan contexts together.
Preserve private-field declaration/acquisition distinctions and expected
versus actual views. Clone context-dependent rows, rebuild ranges/indexes,
manifests, directories and affected Body/source locations. Canonical
role-1 sets sort unsigned DefIds **after** remapping; row permutation never
changes the source field order. Recompute target plans and transient state
witnesses, never remap live runtime pointers or substitute matching octets.

## Alternatives and intended integration

| Alternative | Disposition |
|---|---|
| Keep current refusal | Sound until implementation, but leaves the genuine missing contract and no interoperable PropertyDef completion guarantee. |
| **PI1 backing edge, bounded init summary and state-only predicate** | **Selected minimal candidate.** Reuses existing identities/calls and carries exactly the storage/phase distinction required for ordinary backed properties. Its fixed init frame is an explicit codec boundary, not a universal source restriction. |
| Dedicated PropertyBacking table and LayoutField target union | More explicit about unnamed storage, but adds a second storage identity and a new layout-reference namespace. Not selected: the existing FieldDef/LayoutField rows can represent the storage when a successor-only PropertyBacking flag and PropertyDef edge make its unnamed status unambiguous. |
| Getter reference/body, Pure, setter presence or FieldDef-only guarantee | Insufficient. None establishes this property's declared storage state, init authority or full accessor contract; no fallback is permitted. |
| General property-place descriptors and arbitrary receiver state transformers | A coherent broader redesign could name sets/paths of backing, accessor entry requirements, conditional/nested outputs, sibling state changes, lending and computed-place protocols. It needs explicit place identity, frames, result loans and phase/binding codecs. Those capabilities are not necessary for the selected single-implicit-backing decision and are not silently supplied here. |

On later integration, amend these owners together:

* [Grammar §5.10](../specs/grammar-spec.md#510-properties-and-accessor-local-effects),
  [flow](../specs/language/initialization-and-flow.md) and
  [call contracts](../specs/language-contracts.md#22-declared-outcome-relationships):
  link the explicit construction-only property-state interpretation and
  published accessor summary. Preserve ordinary property/result-loan rules.
* [Metadata](../specs/il-metadata.md#section-kinds-and-row-layouts),
  [registry](../specs/representation/trrm-1.registry.json),
  [clauses](../specs/representation/contract-clauses.md) and
  [logical records](../specs/representation/logical-records.md):
  register the successor-only row, role and state node with their complete
  ownership/support rules. Construction's original role-1 PropertyDef gate
  is discharged **only** for that complete PI1 evidence.
* [Calls/Body](../specs/il-instruction-set.md#calls), binding/replacement and
  the class/source-publication, constructor, receiver and general-graph
  proposals: add the exact role-aware invocation, state-only operand and
  subordinate-backing correspondence, including the actual caller-entry
  context program above. Do not reinterpret original selectors or IO1's
  index/default contract.
* [Native boundaries](../specs/native.md), the construction runtime candidate
  and normal-backing preparation: preserve the same generation/state and
  actual retirement/code origins without expanding their ABI/runtime scope.

This proposal and its status-index entries do not change normative
specifications, registry assignments or implementation. No PI1 policy choice
is deferred to an implementer. Formal approval and implementation/
qualification remain open; the stated broader property, ownership, failure
and target codecs remain independent gates.

## Focused acceptance cases

Required future outcomes, **not results from this documentation change**:

| Area | Positive and rejecting cases |
|---|---|
| Real declarations | Two unrelated classes with automatic and authored/mixed `field` backing, getter-only and get/init forms; get/set with independently initialized backing. Include admitted zero-data values without inventing addresses/defaults. Capture one F at Q's declaration position, all real accessor Bodies and explicit public keys. Computed get-only controls remain ordinary getters without state subjects. |
| Constructor minimum | I guarantees Q while another init property remains vacant; caller completes the remainder before publication. Removing Q's declared guarantee rejects an old expectation; adding it permits old explicit reassignment. An incidental private write does not add a published minimum. Refuse a complete-result projection whose no-caller-entry recipe remains incomplete. |
| Value versus backing | A getter returns a constant without reading F, returns a transformed value or has an observable counter/failure/unsafe requirement. `Q is init` neither invokes it nor predicts its value; ordinary Q access still applies every getter obligation. Reject getter-result initialization or Pure as backing evidence, and reject leaving getter-only backing to caller initialization. |
| Init authority/summary | Auto init and authored init with declared/proved Q completion; repeated assignment, including after I's guarantee, yields the last value with real accessor phase order. A no-op requires its genuine live-F entry condition, not an invented write. Reject missing/false completion on admitted vacant entry, sibling state-changing code outside the frame, amplification of M's Q/F authority, partial/reserved F entry, M before I, a second I, ordinary live receiver, stale generation after context reuse, setter-as-init and R1 write-as-init. Treat valid but unrepresentable source as unsupported closure, not dropped members. |
| Partial state and references | A partial nested F makes Q incomplete without proving vacancy; a complete sibling stays usable under its own checked projection. Reject overwriting remaining children, premature whole-object borrowing/publication and property address/reference formation. Test initialized null/zero separately from vacancy, pointer-cell versus pointee state and descriptor versus referent state. Missing ref-return/result-loan codecs still refuse. |
| Ordinary effects | Independent readonly/mutable get/set modes, safe/unsafe accessors, dependencies/captures and value replacements survive. An init-state frame does not preserve old value predicates, make code Pure or erase failure-side captures. A partial receiver cannot invoke an ordinary setter merely because its property has F. |
| Malformed consumer input | Wrong stride/count/high byte/namespace, name-zero FieldDef without `PropertyBacking`, flagged field with a name, missing/duplicate PropertyDef cross-link, direct backing-field Export/Import, missing getter/backing/role-3 clause, both set/init links, duplicate role or shared F/accessor, static/foreign-owner/type-mismatched F, wrong M signature/owner/result/value ordinal, bodyful expectation/bodyless actual, unsupported flags, role-3-as-I/P, and node-37 wrong phase/root/arity/result/Property operand all reject before use. A structurally valid but false Body summary remains the producer's truth violation, not a mandatory consumer semantic audit. |
| Binding/replacement | Bodyless expected Q/M/F, a non-first provider, several using Modules and export aliases. Keep all original expectations. Reject retargeted Q/F, weakened init frame/completion, removed required role, changed access/effects/dependencies or actual FieldDef-only completion. Check independent field/layout expectations too; matching names/widths/ABIs do not repair a mismatch. |
| Remapping | Independently encode, strip source/debug, legally permute Q/F/MethodDef/Contract/graph/Body maps and provider order while preserving owned ranges/declaration order, pack Import-to-direct, re-emit and reconsume through public Standard APIs. Recover the same explicit property/backing/role relation and all phase origins without private export keys or old row-number inference. |
| Schema/support | Exact PI1 root and every provider agree; reject original Construction-1/2, L1R1 parents, IO1 siblings and private data, including empty/unused inputs. Missing graph/field/Feature-25/native state support refuses; declaration inspection or a getter test is not execution evidence. |
| Runtime | With complete independently admitted prerequisites, supervised headless source-free JIT **and** AOT execute declaration initialization, I, multiple M calls, final validation and publication in order. Observe one backing/I, once-staged receiver/RHS, correct dynamic live-state replacement and no getter invocation for state checks. Later RHS failure, accessor/body/postcondition failure and failed final validation publish no C. Policy zero must not claim unwind cleanup; only separately admitted unwind tests assert reverse actual-acquisition cleanup and no incomplete-parent Cleanup. Unsupported ownership/failure paths refuse before execution. |

Validation must identify the final compiler/configuration/semantic inputs
and matched active cases at the appropriate source, metadata, binding and
runtime layers. No build, test, native execution or broader C1 completion
claim is made here.
