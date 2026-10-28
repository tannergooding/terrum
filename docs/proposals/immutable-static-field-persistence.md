# Immutable static-field persistence

> [!IMPORTANT]
> **Unapproved; provisional direction only under the user's authorized
> missing-contract workflow.** This is a concrete physical-contract
> recommendation, not normative adoption, a registry allocation, compiler
> support or qualification evidence. It changes no source syntax or ABI.

## Decision and scope

**Recommend the eight required tables `0055..005C` and MethodDef Contract
clauses `0013/0014` below.** Preserve StaticField's 20-byte row, StaticAccess's
12-byte row, ImmutableData's 12-byte row, candidate DefId `24=StaticField`
and candidate Feature `19/1` with prerequisites `[]`.

The missing fact is not just where to put StaticField. A field definition,
readonly slot, initialized group or native address does not identify a
published immutable extent. Select a **StaticImmutablePublication sidecar**
with an explicit backing kind, exact typed extent, completion unit,
load-lifetime rule and optional independently materialized constant value.
ImmutableData kind 2 follows the field to that sidecar; it acquires no new
target kind, region identity or payload field.

For the smallest complete backing decision, select **one whole inline
field value**, not an arbitrary reachable object graph. The extent is one
fully initialized, self-contained inline Copy value of the exact field type;
publication additionally promises no mutation or invalidation during its
live publication. This includes admitted scalar, enum, tuple, optional,
union and nominal inline values, not just scalars. A field containing a
handle, reference, view, pointer, function pointer or resource owner does
not qualify for this backing kind. Publishing an indirectly backed sequence
or graph needs a different, explicitly reviewed backing carrier; it is not
silently interpreted as this one.

The surrounding tables encode ordinary and lazy identity, initialization
order, trigger contracts, use-specific authority and imported expectations.
They do not assign StaticPlace's Body encoding, a recovery callable,
runtime state cells, addresses or a native calling convention. This is a
complete persistence recommendation for the selected backing, not a claim
that all static initialization or every possible kind-2 backing is encoded
or executable.

## Owners, exact supersession and preserved guarantees

The [specification index](../specs/README.md) leads to the current owners.
The source static owner is now in
[grammar §3.1](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage),
not a separate source-static-initialization specification. Its
[conformance chapter](../specs/conformance/source-static-initialization.md)
does not independently allocate a codec.

If adopted, this proposal would supersede **only** the following text:

| Existing text and owner | Proposed replacement |
|---|---|
| [Logical static records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending): “Its physical table or section mapping remains open” and StaticDependency's “physical fields, reference encodings and section assignment remain for the coordinated static-initialization schema review” | Use `0055` and `005B`, respectively, with the exact reference and binding rules below. Preserve the distinct instance/static model and the shared Import resolver. |
| Same owner: “Clause-kind IDs and typed-reference encodings remain unassigned” for InitializationRole/InitializationDemand; the combined deferral of “remaining static-row schemas” | Select `0013/0014` and `0056..005C` as specified here. Do **not** close the separate Body-opcode, recovery-call, state-layout or runtime-handle deferrals. |
| [Contract clauses §1](../specs/representation/contract-clauses.md#1-scope-and-row-admission): static clause IDs and typed references “remain unassigned” | Add the two MethodDef-owned clauses below without changing their approved extents, role tags, cardinality or existing Contract ownership rules. |
| [Definition-kind candidate](immutable-data-definition-kinds.md#candidate-words-and-exact-targets): StaticField's “physical section remains unassigned”; its static expectation/publication codec gate | Map the already selected DefId `24` to `0055`. Supply the basic-access expectation and whole-inline publication representations here; retain independent richer-access and indirect-backing gates. Do not reconsider the selected DefId or Import/Export sets. |
| [Declaration-reference candidate](immutable-data-declaration-reference.md#published-immutable-regions): “This proposes no StaticField section ... or new region codec” and “Until the static/publication representation is complete, kind 2 remains gated” | The physical deferral is closed for this selected backing by the complete sidecar below, once separately adopted and supported. Other backings remain unsupported, not inferred. Preserve its direct DefId, single-extent, substitution and binding decisions. |
| [Feature-admission candidate](immutable-data-feature-admission.md#owner-integration-and-remaining-gates): the missing StaticField section/static-access/dependency and “precise immutable extent/publication/backing carrier” decisions | Select these physical records for the stated scope. Keep `19/1`, its empty unconditional prerequisites, per-Module detection and every unrelated value/witness/access gate unchanged. |
| [Metadata's static-storage gap](../specs/il-metadata.md#unspecified-schema-details), [registry](../specs/representation/registry.md) and [machine registry](../specs/representation/trrm-1.registry.json) | Add these required tables, clause shapes and schema-owned references on adoption. Do not reinterpret existing FieldDef flags, NativeImport data fields or instance LayoutField lists as StaticField. |

Preserve the accepted source/runtime guarantees: one group per loaded closed
owner; distinct generic/load identities; no implicit initialized default;
compiler-input/source initializer order; explicit authored-constructor
trigger selection; separate lazy units; validated phase authority; exact
completion, waiting, cycle and failure behavior; no implicit retry; and
quiescent cleanup/unload with required code retained.

Preserve [producer truth versus consumer checking](../specs/representation/reader-trust.md),
[directed binding and retained observations](../specs/representation/binding-and-replacement.md),
and [pure/constant read restrictions](../specs/language/published-predicates-and-effects.md#read-roots-and-effect-records).
The [trigger-identity proposal](static-initializer-trigger-identities.md)
continues to own the unapproved source registration. The normalized wire
tags here are not its enum values, declaration identities or a substitute
for source registration.

## Allocation audit and alternatives

Section numbers and clause/DefId codes in this document are hexadecimal;
Feature IDs are decimal. They occupy independent namespaces.

The current machine registry assigns required sections through `004B` and
`7F00=SourceMap`; `8000..FFFF` is nonsemantic extension space. The
[foreign-interface candidate](native-output-foreign-interface-method-identity.md#proposed-standard-metadata-delta)
uses `004C`; the
[general-predicate candidate](general-predicate-graph-schema.md#new-candidate-sections)
uses `004D..0052`, including TypePattern `0052`; the
[nominal-enum candidate](nominal-enum-persistence.md#allocation-and-representation)
uses **`0053/0054`**. Inspection of the current specifications, proposals and
JSON registries found no competing section assignment for `0055..005C`.
Do not allocate StaticField at `0052` or `0053` merely because an earlier
allocation summary stopped before those candidates.

| Namespace | Exact candidate mapping |
|---|---|
| Required sections, policy `0000` | `0055=StaticField`, `0056=StaticFieldContract`, `0057=EagerGroup`, `0058=EagerInitializer`, `0059=LazyUnit`, `005A=StaticAccess`, `005B=StaticDependency`, `005C=StaticImmutablePublication` |
| MethodDef Contract clauses | `0013=InitializationRole`, `0014=InitializationDemand` |
| DefId | Preserve **`24 -> StaticField section 0055`**, not section `0024` or a tag derived from `0055` |
| Feature | Preserve **`19/1=ImmutableDataIdentities`, prerequisites `[]`**; no new Feature or prerequisite edge is allocated here |
| Other namespaces | No Body opcode/map token, TypeSpec form, native symbol, attribute ID, runtime handle or escape is allocated |

The standard registry's clauses run through `000C`; the copy-origin and
foreign-invocation proposals use `000D/000E`. Development/private native
clauses use `0010..0012` under their separate schemas. Select `0013/0014`
as one adjacent pair without reinterpreting those private records.
`000F` stays unassigned, not reserved. Predicate operation codes `0013/0014`
are a different namespace. No private clause becomes standard-admitted
merely because its number is left undisturbed.

| Alternative | Assessment |
|---|---|
| **Minimal amendment: existing candidate rows plus typed static tables and one whole-inline publication carrier** | Recommended. Closes the actual physical and publication choices, preserves logical separation, and makes initialization contracts inspectable without executing them. Exact group snapshots are conservative for replacement, as specified below. |
| Reuse FieldDef with its static bit; put publication in ImmutableData flags or an RVA | Smaller only by dropping necessary distinctions. A coherent version needs to redesign common field identity, layout participation, access, imports, groups and publication. Neither the existing bit nor image placement supplies an immutable extent or lifetime. |
| Unified storage/publication descriptor for instance, static, indirect and external regions, with one universal initialization/effect program | A substantive, permissible redesign. It could share access/lifetime machinery and summarize private initialization more flexibly, but needs a common owner/projection/retention/effect and operation schema. It is not a section reassignment and is larger than this missing contract. |
| Insert static tables at `004C` and move foreign-interface, graph and enum candidates | Permitted pre-v1; the current numbers are not frozen. It groups older static logical requirements earlier but changes several independent projections and fixtures with no width/capacity benefit. Not selected. |
| Use `0012` or a sparse high section family; use clauses `000F/0010` | Numerically possible after intentional reassignment. The former gives no eight-table adjacency without relocating unrelated sections; the latter overlaps the private NativeAbi spelling. Neither makes decoding smaller. No compatibility alias is justified. |

Eight `u16` section identifiers and two `u16` clause identifiers consume
eight and two assignments, respectively; no surrounding band is reserved.
All new row references retain 24-bit capacity in four-byte words:
`1..00FF_FFFF`, subject to actual table counts, `u32` section/artifact
extents and supported resource limits. Counts and ordinals are `u32`, not
Body-local `u16` indexes. A larger table does not acquire an escape or wider
reference by setting the high byte.

Select one complete combined draft/catalog **out of band** under
[format versioning](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts).
A descriptive label such as `trrm/1-draft-immutable-static-fields-1` is
not an on-wire selector. Equal `(1,0)` bytes, Feature 19, plausible strides
or a PE container do not select this schema; no compatibility sniffing or
historical alias is proposed.

## Physical conventions and exact records

All multibyte fields are little-endian. `R(Table)` below means a required
high-byte-zero, one-based `u32` typed row reference to that table;
`R(Table)?` allows zero only where explicitly stated. `str` is an `R(String)`.
A complete type uses the existing four-byte **TypeSpecRef**, including its
admitted equivalent row/F2/F3 forms, not a Body TypeMap token. Complete-type
fields are required/nonzero except for the explicit OwnerRef absence rule.
Declaration names are nonempty; existing String identity/encoding rules
apply without new name-based identity or binding.

`OwnerRef` is a TypeSpecRef: zero **exactly** for an owner with no applicable
formals; otherwise the exact complete declaring-owner construction with all
ordered type/constant arguments. Symbolic terms require their admitted
formal context; storage use requires the actual closed owner. This is the
[companion substitution rule](immutable-data-declaration-reference.md#exact-owner-substitution),
not a new optionality convention.

Ranges are `(0,0)` iff empty; otherwise they have a nonzero first row and
a checked inclusive last row within the table and 24-bit capacity.
Validate products, additions, offsets and allocation bounds without
wrapping. Fixed sections concatenate exactly `count * stride` bytes;
Contract retains its existing `u32 offsets[count+1]` framing and exact
row extents. All reserved bytes/bits are zero. Unknown tags reject.

Unchanged `0041` ImmutableData remains exactly
`u8 kind, u8 flags=0, u16 reserved=0, u32 declarationDefId,
OwnerRef ownerTypeSpec`: 12 bytes. Kind 2 permits only direct DefId
`24`; its word is `(0x24 << 24) | row`, for example `0x2400_0001`
for StaticField row 1. Zero/bad rows, wrong kinds, `1C` Import tokens and
reserved `FF` indirection reject. Kind 1's separate `22/23` target decision
and all other polymorphic reference sets remain unchanged.

| Section | Stride | Exact field sequence |
|---|---:|---|
| `0055` StaticField | 20 | `R(TypeDef) ownerTypeDef, str name, TypeSpecRef typeSpec, u32 flags, R(EagerGroup) eagerGroupRef` |
| `0056` StaticFieldContract | 20 | `R(StaticField) fieldRef, u8 declarationRole, u8 visibility, u8 callerUnsafe, u8 reserved=0, u32 declarationOrdinal, R(LazyUnit)? lazyUnitRef, R(StaticImmutablePublication)? publicationRef` |
| `0057` EagerGroup | 20 | `R(TypeDef) ownerTypeDef, u8 triggerTag, u8 executionPolicy=1, u16 reserved=0, u32 initializerFirst, u32 initializerCount, R(MethodDef)? constructorMethodDefRef` |
| `0058` EagerInitializer | 16 | `R(EagerGroup) groupRef, u32 ordinal, R(StaticField) fieldRef, R(MethodDef) initializerMethodDefRef` |
| `0059` LazyUnit | 8 | `R(StaticField) fieldRef, R(MethodDef) initializerMethodDefRef` |
| `005A` StaticAccess | 12 | `u8 targetKind, u8 authorityKind, u16 reserved=0, u32 targetRow, OwnerRef ownerTypeSpec` |
| `005B` StaticDependency | 16 | `R(Import) fieldImportRef, OwnerRef ownerTypeSpec, R(EagerGroup) expectedGroupRef, R(StaticImmutablePublication)? expectedPublicationRef` |
| `005C` StaticImmutablePublication | 24 | `R(StaticField) fieldRef, u8 backingKind=1, u8 completionKind, u8 lifetimeKind=1, u8 sharingKind=1, TypeSpecRef extentTypeSpec, u64 valueCount=1, R(ConstantSource)? constantSourceRef` |

The eight-byte Contract header remains
`R(MethodDef) ownerRow, u16 clauseKind, u16 flags=0`:

| Clause | Total bytes | Exact payload |
|---|---:|---|
| `0013` InitializationRole | 20 | `u8 roleTag, u8 flags=0, u16 reserved=0, R(EagerGroup) eagerGroupRow, OwnerRef ownerTypeSpec` |
| `0014` InitializationDemand | 16 | `R(EagerGroup) eagerGroupRow, OwnerRef ownerTypeSpec` |

These are typed references even when their target also has a DefId.
For example, `StaticAccess(targetKind=1).targetRow` names `0055` with a
zero high byte; `ImmutableData.declarationDefId` names that same table
through **DefId kind `24`**. `StaticDependency.fieldImportRef` is an Import
**row**, not an `1C` token. No field accepts a cross-artifact row number,
native address, initializer-as-value or untagged polymorphic reference.

### Static identity, access and ownership

Every StaticField has exactly one StaticFieldContract. Its owner determines
its Module through TypeDef's existing Module partition; add no StaticField
cursor or instance field range. Every owner with static storage, including
lazy-only storage, has exactly one EagerGroup; constructor-only groups are
also allowed. Every field names that exact owner's group, including lazy
fields. StaticField never occurs in TypeDef's instance `fieldFirst/count`,
LayoutField, FieldPath or Body FieldMap.

StaticField `flags` is exactly `0=not writable after initialization` or
`1=writable after initialization`. It encodes neither visibility nor
immutability, caller safety, foreign storage, initialization state or a
publication capability.

StaticFieldContract's closed fields are:

* `declarationRole`: `1=OwnedDefinition`, `2=ImportedFieldExpectation`,
  `3=GroupMemberExpectation`. Role 1 has a local, non-imported owner.
  Roles 2/3 belong to an expected owner TypeDef with its separate Import.
  Role 2 has exactly one StaticField Import. Role 3 has no independent
  Import: it is a subordinate member of a retained expected group snapshot,
  bound only through that group, not a new export/lookup route.
* `visibility`: the existing meanings `0=private`, `1=internal`, `2=public`;
  other values reject in this carrier. `callerUnsafe` is exactly 0 or 1
  and preserves the field-access obligation, not an unsafe body context.
  Effective containing-type access still applies.
* `declarationOrdinal`: unique and contiguous from zero among all static
  fields of this owner, including lazy and generated property backing.
  It preserves merged compiler-input/source declaration order independently
  of table placement, names and primary-part position.
* `lazyUnitRef=0` means ordinary group storage; otherwise it names the sole
  LazyUnit with agreeing `fieldRef`. `publicationRef=0` means **no published
  immutable-region promise**, even when `flags=0`. A nonzero publication
  names the sole sidecar for this exact field.

This basic-access carrier does not erase the separate
[semantic source-unit/friend obligations](../specs/representation/logical-records.md#source-unit-access-and-definition-identity-encoding-pending).
It cannot represent `file` or member-scoped friend restrictions, grants or
file-scoped owner identities. A retained definition requiring those records
remains unsupported until their complete independent representation is
selected; neither public-owner inference, private-name mangling nor optional
SourceFile paths substitutes. This is an explicit remaining access decision,
not an implementation permission to weaken those contracts.

Role-3 fields, private initialization declarations and their contract types
may be retained for initialization comparison without becoming accessible
members. A role-3 field cannot be an Import/Export target, StaticAccess
target or ImmutableData declaration. Binding its sibling's public Import
does not grant access to it.

### Group, trigger, initializer and lazy contracts

EagerGroup's normalized trigger tags are exactly
`1=GroupedStaticFieldAccess` and `2=RuntimeStaticMemberAccess`.
With no authored constructor, require tag 1. With one, preserve its
source-selected tag; the presence of a MethodDef does not choose tag 2.
Source attribute/enum lookup remains governed by the trigger-identity
proposal, not these numeric bytes.

`executionPolicy=1` is the accepted single-initializing-flow protocol:
competing permitted callers wait, unfinished-unit demand cycles use the
cycle failure contract, and only successful completion publishes. No
alternative policy tag, duplicate execution or unchecked-race mode is
selected. Module's admitted failure/runtime contract supplies the existing
fail-fast versus defined-unwinding behavior; the group does not replace it.

An EagerGroup exclusively owns its EagerInitializer range. Every entry
belongs to exactly one range and agrees on `groupRef`. Its explicit
ordinals are unique/contiguous from zero, and execution follows **ordinal**,
not physical row order. Field targets are distinct non-lazy fields of the
group; their declaration ordinals increase in this execution order.
Fields initialized by the constructor rather than a declaration expression
need no invented initializer entry. Every retained expression initializer
has one entry. Constructor preconditions/body/postconditions follow all
entries. No computed property acquires a hidden storage entry.

`constructorMethodDefRef=0` means no authored constructor. Otherwise it
names the one exact private, unspeakable static-constructor MethodDef.
Generated initializer MethodDefs are likewise private/unspeakable. Each
has the exact owning TypeDef, no receiver or method formals, no parameters,
unit result and its required role. Their bodies are present when execution
requires them; a reference-only expected contract may be bodyless but is
not executable or an ordinary imported callable.

Each LazyUnit belongs to exactly one field, outside the eager sequence,
and owns one generated initializer MethodDef with role 2. External lazy
access first demands the containing group, then that exact lazy unit.
The lazy initializer's field is identified by its LazyUnit, not by guessing
from its method name or the group. One initializer MethodDef cannot serve
multiple initializer/constructor entries or lazy units.

Role tags remain `1=eager initializer`, `2=lazy initializer`,
`3=authored static constructor`, `4=eager-phase helper`,
`5=lazy-phase helper`. Each method has at most one role, naming one exact
group and owner substitution. Tags 1/2/3 must agree with the corresponding
initializer/constructor/LazyUnit edge. Helper roles grant only their
validated group/phase authority; a lazy helper's group does not authorize
access to every unfinished lazy unit. Role-bearing helpers are
initialization-only, not ordinary calls with authority inferred from their
caller. Actual invocation must carry the valid phase context.

InitializationDemand permits one clause per resolved closed eager group;
duplicates reject, including equivalent owner spellings. Absence means no
published demand. Static calls still use ordinary Call. All clauses remain
exclusively in their MethodDef's checked Contract range.

**Trigger/effect carrier:** the EagerGroup reference identifies the normalized
trigger, fixed gate/failure/publication protocol, ordered initializer
MethodDef contracts and optional constructor contract. A LazyUnit adds its
own initializer contract after the eager requirement. These are the exact
typed identities consumers follow for effects, completion and dependencies;
there is no undefined `effectId`, scalar purity bit or duplicate contract
snapshot inside StaticDependency. The composition retains all initialization
failures, allocation, blocking, synchronization, reads, writes and cleanup.
An absent/unknown callable effect remains conservative unknown, not empty.
Complete independently required callable/effect/cleanup codecs are still
mandatory before their affected use.

### StaticAccess and imported dependency

StaticAccess keeps target tags `1=local StaticField`, `2=StaticDependency`
and authority tags `1=ordinary completion demand`,
`2=validated initialization-phase authority`. The target tag fixes
`targetRow`'s table. Resolve owner substitution before access.

For tag 1, use a role-1 field in the using Module. For tag 2, the dependency
selects the role-2 expectation and shared provider binding below. An
ordinary demand completes the eager group and, when applicable, the lazy
unit before access. Phase authority must match the actual active loaded
group/phase and initialized/vacant field state; its metadata tag is not a
runtime authority token. It never lets an ordinary reentrant caller observe
partial state or replace a fixed live field.

Read/write/reference-formation semantics come from the consuming ordinary
place operation and complete field type. StaticAccess does not grant writes,
waive caller-unsafety or encode read authority in reserved bits. The eventual
StaticPlace operation must carry this typed access reference; this document
does not choose its Body carrier or opcode.

StaticDependency is unique per exact imported field expectation and normalized
complete owner substitution in its Import's using Module. Share it across
uses, not across distinct expected declarations merely because they select
equal values or one provider. Its fields mean:

1. `fieldImportRef` selects the shared Import whose `expectedLocalDefId`
   is DefId `24` for that role-2 field. The Import's ModuleRef belongs to the
   same consumer Module as that field and its expected owner.
2. `expectedGroupRef` names that field's exact expected EagerGroup. The
   selected provider field supplies its exact provider group; the owner
   binding establishes their correspondence. There is no independent group
   export key, foreign-artifact row or alternate provider resolver.
3. `expectedPublicationRef` is zero exactly when this dependency retains no
   immutable-publication promise. Otherwise it names the field's expected
   publication and retains all its extent/backing/completion/lifetime/value
   obligations. It must equal that expected field's
   `StaticFieldContract.publicationRef`, including zero. Kind-2 use requires
   this nonzero contract; a dependency cannot suppress its field's promise.

Follow the expected field's lazy sidecar and group/initializer contracts for
the remaining unit/trigger/effect edges. Source compilation emits these same
normalized identities after ordinary checked declaration binding; no name
recognizer, native symbol, getter evaluation or initializer execution supplies
them during dependency admission.

### Resolving kind 2 without a StaticAccess occurrence

The identity/read-root, dependency-only and input-view routes need no dummy
StaticPlace or executed access. For every retained using context:

1. Decode ImmutableData kind 2's direct DefId `24`; validate its field,
   unique sidecar and exact `ownerTypeSpec` presence/substitution.
2. For role 1, follow the field's nonzero publication directly. For role 2,
   require exactly one StaticDependency for that expected field and
   normalized owner construction, even when no StaticAccess refers to it.
   Follow its actual Import/ModuleRef binding and complete expected group
   and publication. A missing/ambiguous dependency rejects. Role 3 rejects.
3. Resolve the selected provider field's unique publication and perform
   the complete expected/provider comparison before relying on its facts.
   Any direct packed-provider reference also retains its original expected
   binding graph; co-location supplies neither a missing dependency nor
   access authority.
4. Validate the selected extent and required representation/Feature closure
   in every using context. Runtime use separately requires actual loaded
   publication and backing; metadata resolution performs no initialization.

The dependency key is an index over decoded typed identities, not another
serialized resolver, an artifact-global provider choice or a name search.

## The immutable publication carrier

Every StaticImmutablePublication has exactly one owning StaticFieldContract
with agreeing `fieldRef`; shared/orphan/duplicate sidecars reject. It is a
contract on that field template under the field owner's exact substitution,
not a second definition or independently importable region. One field has
one unambiguous publication extent per loaded closed owner, as required by
the declaration-reference candidate.

| Field | Exact selected meaning and checks |
|---|---|
| `fieldRef` | The same field as the owning sidecar; require StaticField `flags=0`. A mutable static cannot publish through this carrier. |
| `backingKind=1` | **WholeInlineFieldStorage.** The backing is the actual declared field's own storage, not a referenced object, constant Blob, RVA, initializer MethodDef, PE section or separately allocated hidden field. No other backing tag is assigned. |
| `completionKind` | `1=EagerGroupComplete` exactly for a non-lazy field; `2=LazyUnitComplete` exactly for a lazy field. Tag 2 includes prior eager-group completion. Follow field/group/LazyUnit references; no numeric unit/address is serialized. |
| `lifetimeKind=1` | **DeclaringLoadPublication.** Storage and the publication remain valid only within the actual provider's loaded closed-owner/storage generation, with every outstanding observation/loan and required cleanup/code dependency retained. No process-immortal, temporary or foreign-address alternative is assigned. |
| `sharingKind=1` | **NoAdditionalSharingAuthority.** Publication supplies required visibility under the already admitted sharing contract, but grants no transfer, thread sharing, guard acquisition, pin, writable access or synchronization for later mutation. Actual type/protocol sharing obligations remain separate retained prerequisites. |
| `extentTypeSpec` | Required complete type, equal to the exact substituted StaticField type. Require a fully initialized, storable, self-contained inline Copy value with no transitive handle/reference/view/pointer/function-pointer/resource-ownership component. No private-layout omission or type erasure can satisfy this check. |
| `valueCount=1` | Exactly one complete logical value, including all its inline subvalues. This is neither a byte count nor a dynamic sequence length. Zero and every other `u64` value reject; a zero-storage inline value is still one logical value. Size/alignment/padding derive from the admitted target representation, not this integer. |
| `constantSourceRef` | Zero means no promised constant image. Nonzero is a separately admitted ConstantSource selecting tag 1 FrozenValueNode or tag 3 TargetValueBinding only, with the exact extent type and promised value. Tag 2 ConstantExecution is invalid here. This is data already materialized independently, never an initializer to run. |

The extent covers typed value state, not padding as an independently readable
byte buffer. A byte reinterpretation or foreign pointer cannot enlarge it or
turn unspecified padding into a promised constant image.

The type/shape requirement is necessary, **not sufficient**, for immutable
publication. The sidecar additionally promises that successful completion
establishes the entire extent, including every relevant inline subvalue,
and that no permitted write, replacement, move-out, explicit invalidation or
unsafe alias changes it until publication retirement. A Copy/Blittable
property, readonly access path or lack of a discovered write is not this
promise. Structurally false qualifiers/counts/links reject; a structurally
valid but false no-mutation/value promise violates producer truth and is not
a mandatory consumer whole-Body proof.

A generic field template can carry this publication only if the promise
holds for all constructions admitted by its retained formation contract.
Apply its actual substitution before concrete type/extent use. Do not
fabricate an “inline immutable” source constraint, infer it from an
unconstrained Copy formal, or omit a reference-bearing instantiation.
Missing required type/formation support refuses affected use.

Completion does not itself execute a second publish initializer. The runtime
publishes the whole extent as part of the selected unit's successful
completion; initialization failure leaves it unpublished. A failed lazy
field is not published by its completed eager group. Recovery retries only
an exact failed, cleaned-up unit under the existing unsafe/exclusive
preconditions; it never resets a live publication. Serialized metadata
contains no `Complete` bit or reusable original failure event.

### Constant observation and views

A nonzero constant source is an exact promise about the field's value after
successful publication, preserving all observations required by its admitted
typed codec, including signed zero/NaN/nominal facts where applicable.
TargetValueBinding retains its exact target/profile/layout dependencies.
Consumers compare those facts; they do not initialize storage to verify them.

Runtime-only publications may be retained by immutable read roots without
a constant source. Required constant evaluation or an ImmutableData-backed
LogicalInputView that actually reads the value requires the admitted
materialized source, target and value/Feature closure. Absence is an
unsupported required observation, not an empty value, host-address read or
runtime fallback.

LogicalInputView backing kind 2 still references **ImmutableData section
0041**, not StaticField or StaticImmutablePublication. Its existing typed
path selects a subextent of this one whole-value root. Preserve effective
access and substituted types at every step; validate active-case/element
availability and checked ordinal/range endpoints. No path can cross a handle,
reference or other indirect backing through this selected carrier.
Retaining a view is not proof its runtime group has completed.

An evaluator observes logical immutable input/value storage, never a host or
target address. A runtime observation additionally needs successful actual
publication and live load backing. Neither form runs a runtime initializer,
marks a group complete or treats the generated MethodDef as a frozen value.
Embedding/folding a constant cannot erase surviving initialization timing,
effects, failure or lifetime obligations without an independent
semantics-preserving discharge.

## Binding, source-free use and replacement

Use the [shared exact identity resolver](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities):
one selected Module identity/revision, nonempty Module-wide-unique export
key and required declaration kind. Bind the expected owner TypeDef
separately. A StaticField Export is role 1, public, with a public local
owner; imported/subordinate fields and initializer methods cannot masquerade
as exports. Export aliases preserve one declaration identity.

The member Import must select exactly the provider field belonging to the
selected provider owner. Compare complete field type, writability, access,
caller-unsafety, owner formal correspondence and all retained initialization
and publication facts. Neither an equal name/layout nor failed comparison
permits searching another key/provider. Direct DefId `24` naming an expected
field still follows its Import; it allocates no storage, initializer or
consumer-local completion state.

**Expected group representation is deliberately complete and conservative.**
The expected group retains its full ordered static declaration/initializer
snapshot, lazy-unit contracts and constructor contract when present.
The selected public field is role 2; siblings needed for the group contract
are role 3 unless independently imported. Initializer/constructor stubs
correspond through their typed group entry, field/lazy identity and role,
not through public method Imports or private-name lookup.
Completeness here concerns the declaration/initialization sequence and
retained contracts; it does not copy every extra provider value guarantee
or private Body into the expectation. An expected sidecar may omit an
unrelied-on optional publication or constant image. Its absence does not
require the provider to lack that extra guarantee.

Map the unique groups through the already bound owners. Their static member
lists correspond by explicit declaration ordinal, requiring exact member
name/type/role-of-storage/access/writability correspondence; initializer
lists correspond by explicit execution ordinal and selected field. Compare
the referenced callable contracts under the existing directed relation;
body bytes are not a compatibility key. Require exact trigger,
execution-policy, lazy/eager and publication-unit choices. Every retained
expected entry must pass. This selects an exact snapshot relation rather
than leaving an aggregate trigger-effect comparison undefined.

Consequently, a provider change to this retained declaration/initialization
sequence can fail compatibility even if a future aggregate-effect proof
could show the same externally visible effects. That conservative cost is
the minimal amendment's tradeoff. An independently specified aggregate
initialization contract could replace this relation later; implementation
must not silently invent one, omit private steps or claim an unvisited step
succeeded. Private helper implementation changes with unchanged complete
retained contracts remain distinct from changes to the retained sequence.

For publication compare exactly: whole-field backing, complete extent
type/count, eager/lazy completion unit, declaring-load lifetime and
sharing conditions. Preserve every expected constant image and stronger
provider value/publication fact actually relied on. A provider with an
extra constant-image promise may satisfy a runtime-only expectation, but
using that promise for folding retains it as an additional obligation.
Immutability alone does not promise equal values in different loads or
provider revisions.

Replacement checks the actual retained graph, including every independent
expectation for one provider/key. Trigger changes are not strengthening;
same extent bytes do not replace a different field/owner/load identity.
No live hot replacement, owner migration or patching of old machine code is
implied. Caches additionally key actual load/publication/storage generation,
selected provider revision, complete owner, admitted rules/target and every
relied-on value fact; row numbers, names and native addresses are insufficient.

### Loaded lifetime, relocation and teardown

Declaration identity resolves a contract; it is not a process-immortal
address. Runtime observation identifies the selected provider load, closed
owner, group/lazy unit, storage generation and successful publication.
Different instantiations and reloads have distinct state even with identical
TRRM bytes and constant values.

The field/load retains ownership of its storage and applicable retirement;
an ImmutableData root or view does not transfer it to a caller. Observation
and checked-reference dependencies keep the backing and any required
initialization/cleanup code available through their actual endpoints.
Publication grants neither an exclusive owner nor permission to dispose,
move or reuse backing early. Other group fields can still have roots,
resources and cleanup obligations even though this published extent is
self-contained.

Address selection and load relocation derive from the selected backend's
validated storage mapping. Moving an image before activation does not
change the logical publication. Moving live storage is permitted only when
all active reference/address-stability and dependency contracts remain true;
otherwise require quiescence. Never persist an RVA or PE/native address as
the backing proof or carry cached addresses into another load. No relocation
fixup is needed in this metadata because no address is serialized.

Checked unload first discharges active calls, observations, loans, roots,
pins and ownership dependencies. It then performs applicable retirement
while required code is still available, and only afterward invalidates
storage/code. Image removal is not cleanup. Partial initialization failure
retires actual acquired state without publishing, rolling back external
effects or sharing the original escaping event. Waiters/later demands use
fresh failed-unit events under the accepted failure contract.

## Per-Module admission and exact Feature closure

The new tables are required physical schemas interpreted under the selected
edition-1 static contracts; this proposal allocates no separate static
Feature or a reduced StaticInitProfile. Required schema, callable, type,
sharing, failure, cleanup, runtime and target support remain real admission
gates even when they have no numeric Feature. Recognition of these rows
does not assert execution support.

Keep the
[Feature 19 candidate's detection](immutable-data-feature-admission.md#per-module-detection-and-closure)
unchanged. Reach ImmutableData from actual **or allowed-only** root 6,
ConstantExecutionDependency kind 6, LogicalInputView backing kind 2, or
another already admitted typed retention edge. Traverse every retained
using Module/formal context, including uncalled/bodyless declarations,
unselected branches and stored requests not yet executed. A StaticField or
publication sidecar alone does not add Feature 19; an unreferenced 0041
row has no invented using Module. Physical rows still obey their schema.

| Retained use in a using Module | Effective Feature pairs in the combined candidate |
|---|---|
| Existing typed retention of the immutable identity without a graph/value | `{19/1}`; no new standalone retention hook is introduced |
| Phase-1/2 graph or Pure allowed bound with this runtime-only region root | `{10/1,17/1,19/1}` plus actual independently retained prerequisites |
| Same use retaining an admitted fixed-scalar constant image | `{10/1,11/1,17/1,19/1}` |
| Constant-execution dependency kind 6 or input view backed by this identity | `{10/1,11/1,12/1,17/1,19/1}` plus actual program/view/value/target prerequisites; this does not complete Feature 12 |
| A preceding use retains actual layout-query/FieldPath/FieldDef-Import facts in Feature 13's scope | Add `13/1`; ordinary owner types, Layout rows, field storage and typed extents alone do not add it |
| A constant image retains nominal-enum value metadata | Add `18/1` and its `11/1` prerequisite without making enum members ImmutableData targets |
| Phase-3 selector or function-pointer anchor attempts root 6 | Reject applicability, even with every listed Feature present |

These sets list Feature pairs, not all representation/runtime admission
conditions. No unconditional edge from 19 to 11, 13, 17 or a static-execution
feature is added. Only actual retained forms add other capabilities.
Provider Modules retain their own requirements; they cannot supply a missing
using-Module 19. Conversely, a provider's private frozen image or graph does
not add 11/17/19 to a consumer that retains no corresponding identity/value/
graph obligation. Admit that provider's manifest separately.

ImmutableData, TypeSpecs and structural access rows have no Module cursor.
Share them only when their meaning agrees in **every** retained using
context; otherwise clone/remap. Two consumer Modules selecting different
providers or owner constructions cannot use one expected-field row as an
artifact-global binding cache. Multiple Modules retaining the same selected
provider identity each retain their own expectations and Feature closure.

Admission follows metadata, never initialization. Runtime ordinary access
has its declared completion gate/effects. A pure read requires those entry
publication/lifetime obligations already satisfied or separately discharged;
it cannot acquire a blocking initialization gate as an invisible pure read.
Constant evaluation does not discharge the runtime gate.

## Validation and meaning-preserving transformation

Mandatory checks before affected use include exact schema/catalog/support,
section policy/stride/extent, closed tags, required/null references, owner
and range exclusivity, unique sidecars/ordinals, initializer/role
correspondence, reference namespaces, complete substitutions, publication
eligibility, per-Module closure and the complete selected binding relation.
Retained unexecuted metadata is not exempt. False producer no-mutation,
successful-initialization or exact-value claims remain distinct from
structural defects; optional audits may examine their truth against Bodies.

Rewriters must decode and remap:

1. StaticField DefIds by kind `24`, but static typed rows by their exact
   tables; owner TypeDefs/TypeSpecRefs and nested formal/constant arguments;
   Import/Export/ModuleRef and subordinate expected-group correspondences.
2. Every field/group/initializer/lazy/access/dependency/publication edge and
   MethodDef Contract owner/range, preserving declaration and execution
   ordinals. Rebuild exclusive ranges, physical extents and all indexes.
3. ConstantSource/value/profile/layout graphs and every 0041 use in roots,
   actual/allowed footprints, executed dependencies and views. Preserve
   existing value-tree and path ownership rather than sharing occurrence rows.
4. Each Module's exact manifest closure and all original expectations plus
   stronger relied-on facts, even after folding or direct binding.

Keep imported accesses on their typed dependency path when packing providers
into the same artifact; co-location is not a reason to change target tag 2
to a local-access escape. Internalization can eliminate a binding edge only
after a separately justified transformation preserves or discharges all its
retained meaning. Merely changing an expected field to role 1 is invalid.
Rebuild Body maps/code and affected SourceMap slots if a later supported
lowering changes them; do not substitute matching octets in arbitrary payloads.

Canonical writers order initializer rows by explicit ordinal and sidecars
by their owning declaration, use one required sidecar per owner and reuse
dependencies only under the exact key above. Ordinary readers may accept
consistently remapped table permutations and initializer-range row
permutations with valid explicit ordinals. Duplicate owned sidecars, ranges
or semantic demand keys are invalid, not alternative canonical forms.
Equivalent duplicate ImmutableData identities remain governed by their
existing semantic-identity/footprint rules.

## Candidate acceptance cases

These are future specification/implementation obligations, **not tests run
or current support claims**. Assume independently needed codecs are selected
and supported unless their absence is the case.

| Case | Required outcome |
|---|---|
| Non-generic public fixed inline field, no constructor, valid publication and runtime access | Resolve DefId `24` only to `0055`; use synthesized trigger 1, complete the group, then observe its one declared extent under load lifetime. |
| An admitted inline tuple/nominal/optional/union field, or closed generic owner with representation-irrelevant arguments | Apply exact full substitution and inline/value validity; retain all arguments and active-state obligations, not a scalar-only or layout-erased policy. |
| Writable static `flags=1` with no publication | Valid ordinary static identity if its other contracts are valid; cannot be kind-2 ImmutableData. |
| Writable static with publication; `flags=0` without publication; fixed slot holding a mutable class/Span/reference/native pointer | Reject the attempted whole-inline publication/kind-2 use. Readonly access, Copy, Blittable or a native address is insufficient. |
| Missing publication/backing fields, bad stride/reserved bytes, unknown backing/lifetime/sharing tag | Reject malformed/unsupported affected input; do not invent defaults or probe an older codec. |
| Extent type differs from substituted field type; `valueCount=0`, 2 or `u64` maximum | Reject the incorrect extent without multiplication overflow or reinterpretation as byte size. |
| Zero-storage storable inline value, first/last permitted table rows, maximum representable checked extents | One logical value remains valid; require actual bounds and nonwrapping section/artifact arithmetic. Capacity exhaustion is not success or a portable-minimum claim. |
| Eager field with completion tag 2, lazy field with tag 1, dangling/mismatched group/LazyUnit | Reject before use; a complete group does not publish an unfinished lazy field. |
| Failed group/lazy unit, caught internal cycle, competing permitted callers, unsafe retry | Preserve accepted waiting/cycle/escaping-failure distinctions, publish only on actual successful completion, never share the original event or reset a live unit. |
| Static initializer MethodDef, EagerGroup row, Import token, raw row or section-shaped word used as ImmutableData declaration | Reject wrong namespaces/target kinds; no initializer value, table probing or address interpretation. |
| Duplicate field sidecars/publications, orphan initializer, overlapping ranges, duplicate/gapped declaration or execution ordinals | Reject malformed ownership/order; equal payloads do not excuse duplicate owned rows. |
| Source partial-input order changes during packing or filename/primary-part sorting | Preserve explicit recorded source execution order; table renumbering alone cannot reorder initializers. |
| Ordinary access without completion, phase tag without actual authority, wrong-group helper, attempted fixed-live write | Refuse that use; tags/roles do not manufacture authority or writable storage. |
| Valid consumer member/owner Imports plus StaticDependency and complete expected group | Bind the exact public provider field/owner/group, retain trigger/effects and publication, allocate no consumer storage or initialization state. |
| Kind-2 identity/dependency/view without a StaticAccess row; matching dependency absent, duplicated or publication-mismatched | Require the unique typed dependency and matching nonzero publication through the identity-resolution rule; reject defects without synthesizing an access or initializer execution. |
| Private role-3 sibling/initializer exported, independently imported, accessed or used as kind-2 target | Reject the identity/access route; subordinate comparison is not public lookup or re-export. |
| Wrong provider/key/owner, changed trigger or initializer sequence, changed extent/lifetime or added caller-unsafety | Fail the complete retained comparison; no alternate-provider search, trigger ordering or “same scalar” exception. |
| Retained `file`/friend access with only this basic visibility byte or SourceFile paths | Refuse affected support; do not erase semantic unit/grant requirements. |
| Runtime-only publication with no constant source | Permit supported runtime identity/read use after publication; refuse required constant observation, not replace it with an empty value or runtime initialization. |
| Exact materialized scalar source; tag-3 source for the selected profile; tag-2 request or wrong typed source | Accept only admitted exact-value images and profile closure; reject tag 2 here and never replay an initializer to obtain data. |
| LogicalInputView backing 2 selects a valid inline subvalue, then tests inactive case, wrong type, one-past index or overflowing range | Resolve through 0041 and validate the typed path; reject invalid subextents and indirect escapes. |
| Root 6 appears only in an uncalled/bodyless Pure allowed footprint | Require using-Module `{10/1,17/1,19/1}` plus actual dependencies, even without an actual-footprint read or execution. |
| Only ConstantExecutionDependency kind 6 is retained; stored request has not executed in this consumer | Require 19 and the exact constant-execution closure; retain the identity without executing initialization for admission. |
| Only LogicalInputView backing kind 2 retains the identity | Require 19 and the actual view/request closure; no direct StaticField/Blob substitution. |
| Known but unselected graph branch retains a view/request/immutable identity; unselected runtime-only call is not an executed dependency | Inspect retained structure/requirements without executing the branch or fabricating an executed-dependency entry. |
| Bare 19; runtime-only region graph; same graph plus scalar image or FieldPath facts | Derive exactly `{19}`, `{10,17,19}`, then add 11 or 13 only for the actual retained form, all at revision 1. Representation/runtime gates remain separate. |
| Provider or another packed Module has 19, using Module does not; only 11/13/17 supplied | Refuse affected identity use; no artifact-wide Feature union or implicit prerequisite repair. |
| Two using Modules retain the same provider; two others bind structurally shared rows to different providers/owners | Admit each Module independently; preserve same-identity dependencies, and clone/remap context-sensitive rows rather than overwrite a shared binding. |
| Phase-3 selector or function-pointer anchor names root 6 with 10/17/19 all present | Reject applicability; this proposal does not widen selectors or anchor contracts. |
| Provider privately retains a publication/value; consumer retains no ImmutableData/value promise; unused 0041 row is co-located | Do not invent consumer 19/11; still validate selected physical structures and admit the provider's own closure. |
| Replacement changes a relied-on constant value, load generation or owner while preserving key/name/address bits | Recheck every retained fact and invalidate/rebind affected observations; equal strings/addresses are insufficient. |
| Image rebased before activation, live loan during relocation/unload, unload/reload of identical artifact | Remap storage under the backend; preserve active stability or wait for quiescence, retire before invalidation, and create fresh load/state identities on reload. |
| Folding, trimming or packing removes an Import or machine read but leaves a contract/view/publication dependency | Preserve that dependency, original expectation and stronger facts; only a complete justified discharge can remove them. |
| Matching version bytes or recognized 19/DefId 24 without the complete selected codec/runtime support | Refuse affected use; decoding recognition is not publication, execution support or conformance. |

## Remaining decisions and implementation boundary

No implementer is left to choose a section, static row stride, reference
namespace, trigger/effect identity path, expectation correspondence,
publication point, typed whole-value extent or lifetime for **this**
carrier. These are concrete provisional recommendations, still unapproved.

Independent decisions remain explicit:

* **Indirect immutable backing:** a static reference/view/owner publishing a
  sequence or graph needs exact projection/extent, responsible backing,
  retention/root/pin and invalidation records. This proposal deliberately
  chooses whole inline storage instead; it does not assign an empty
  “provider-defined backing” payload or claim those other cases work.
* **Richer access:** semantic source-unit identities and member-scoped
  friend/grant codecs still need their own complete decision. Affected
  declarations remain gated; this basic-access carrier is not their codec.
* **Execution carriers:** StaticPlace's Body form, recovery callable/exact
  unit operand, general ownership/sharing/failure/cleanup codecs and runtime
  integration remain independently required where retained. Their missing
  choices must not be hidden behind successful structural decoding.
* **Broader values/evaluation:** the richer frozen/target/value programs and
  Feature 12 gates remain with their current owners. A whole-inline region
  does not approve their implementations or source catalogs.

Future adoption would update the linked physical, registry, reference,
binding/admission and logical owners together, followed by producer,
structural reader, provider comparison, remapping and runtime/lowering
work with independent cases. No normative file, implementation inventory,
source production, ABI, runtime scheduling policy or approval status is
changed by this document.
